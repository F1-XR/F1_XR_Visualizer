using System;
using System.Collections.Generic;
using F1XR.RestAPI.Api;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    [CreateAssetMenu(menuName = "F1 XR/Track Calibration")]
    public sealed class TrackCalibration : ScriptableObject
    {
        public enum SourceAxisMode
        {
            XY,
            XNegativeY,
            YX,
            NegativeYX
        }

        public enum HeightMode
        {
            CalibrationPoints,
            SourceSample,
            Flat
        }

        public enum MappingMode
        {
            Global,
            Segment,
            Route
        }

        private const float SegmentEndpointPadding = 0.08f;
        private const float MinSegmentSourceDistance = 120f;
        private const float MaxSegmentSourceDistance = 900f;
        private const float SegmentSourceDistanceRatio = 0.12f;
        private const int RuntimeAlignmentSampleCount = 128;
        private const int RuntimeAlignmentMinimumSamples = 64;
        private const float RuntimeAlignmentMaximumStepRatio = 0.025f;
        private const float RuntimeAlignmentMaximumRmsRatio = 0.01f;
        private const int RouteSegmentLookBehind = 2;
        private const int RouteSegmentLookAhead = 8;

        public int circuitKey;
        public string circuitName;
        public int referenceSessionKey;
        public int sourceTranslationCorrectionSessionKey;
        public Vector2 sourceTranslationCorrection;
        public bool active;
        public MappingMode mappingMode;
        [Range(0f, 1f)] public float segmentBlend = 1f;
        public bool loopMappingSegments = true;
        public SourceAxisMode sourceAxisMode;
        public HeightMode heightMode;
        public float localY;
        public float heightOffset;
        [Min(0.01f)] public float outputScale = 1f;
        public bool useFirstSourceHeightAsOrigin = true;
        public float sourceHeightOrigin;
        public float sourceHeightScale = 0.0001f;
        public bool useNearestPointHeight = true;
        [Range(0f, 1f)] public float pointHeightBlend = 0.25f;
        public bool loopPointHeightSegments = true;
        public bool useSourceHeightForRouteSegments;
        [Min(0f)] public float routeSourceHeightWeight = 1f;
        [Min(0f)] public float maxRouteLateralOffset;
        public Point[] points;

        bool hasRuntimeSourceHeightOrigin;
        float runtimeSourceHeightOrigin;
        [NonSerialized] bool hasRouteOffsetScale;
        [NonSerialized] float routeOffsetScale;
        [NonSerialized] Vector2 runtimeSourceTranslation;

        public float OutputScale =>
            outputScale > 0f ? outputScale : 1f;

        public bool SupportsRuntimeSourceAlignment =>
            active &&
            mappingMode == MappingMode.Route &&
            referenceSessionKey > 0 &&
            GetConfiguredPointCount() >= 8;

        public Vector2 RuntimeSourceTranslation =>
            runtimeSourceTranslation;

        [Serializable]
        public struct Point
        {
            public string name;
            public Vector2 sourcePosition;
            public float sourceHeight;
            public Vector3 targetLocalPosition;
        }

        public bool TryMap(LocationSample sample, out Vector3 localPosition)
        {
            return TryMapContinuous(
                sample,
                -1,
                out localPosition,
                out _);
        }

        internal bool TryMapContinuous(
            LocationSample sample,
            int previousRouteSegmentIndex,
            out Vector3 localPosition,
            out int routeSegmentIndex)
        {
            routeSegmentIndex = -1;

            if (active &&
                mappingMode == MappingMode.Route &&
                TryMapRoute(
                    MapRuntimeSourceAxes(sample.x, sample.y),
                    sample.z,
                    previousRouteSegmentIndex,
                    out var routePosition,
                    out var routeHeight,
                    out routeSegmentIndex))
            {
                float scale = OutputScale;
                localPosition = new Vector3(
                    routePosition.x * scale,
                    routeHeight * scale + heightOffset,
                    routePosition.y * scale);
            }
            else if (!TryMap(new Vector2(sample.x, sample.y), out localPosition))
            {
                return false;
            }

            if (heightMode == HeightMode.SourceSample)
                localPosition.y =
                    (localY + GetSourceHeight(sample.z)) * OutputScale +
                    heightOffset;

            return true;
        }

        public void ResetRuntimeHeightOrigin()
        {
            hasRuntimeSourceHeightOrigin = false;
            runtimeSourceHeightOrigin = 0f;
        }

        public bool RequiresRuntimeSourceAlignment(int sessionKey)
        {
            return SupportsRuntimeSourceAlignment &&
                sessionKey != referenceSessionKey;
        }

        public void ResetRuntimeSourceTranslation()
        {
            runtimeSourceTranslation = Vector2.zero;
        }

        public void SetRuntimeSourceTranslation(Vector2 translation)
        {
            runtimeSourceTranslation = translation;
        }

        public Vector2 GetSourceTranslationCorrection(int sessionKey)
        {
            return sessionKey == sourceTranslationCorrectionSessionKey
                ? sourceTranslationCorrection
                : Vector2.zero;
        }

        public bool TryEstimateRuntimeSourceTranslation(
            Dictionary<int, List<LocationSample>> samplesByDriver,
            float startTime,
            out Vector2 translation,
            out float rmsError,
            out int driverNumber)
        {
            translation = Vector2.zero;
            rmsError = float.MaxValue;
            driverNumber = 0;

            if (!SupportsRuntimeSourceAlignment ||
                samplesByDriver == null ||
                !TryBuildRuntimeAlignmentRoute(
                    out List<Vector2> route,
                    out float routeLength))
            {
                return false;
            }

            Vector2[] routeSamples =
                ResampleClosedRoute(
                    route,
                    routeLength,
                    RuntimeAlignmentSampleCount);
            float maximumStep =
                routeLength *
                RuntimeAlignmentMaximumStepRatio;

            foreach (KeyValuePair<int, List<LocationSample>> pair in samplesByDriver)
            {
                if (!TryBuildRuntimeAlignmentPath(
                    pair.Value,
                    startTime,
                    routeLength,
                    maximumStep,
                    out List<Vector2> path,
                    out float pathLength))
                {
                    continue;
                }

                Vector2[] pathSamples =
                    ResampleOpenPath(
                        path,
                        routeLength,
                        RuntimeAlignmentSampleCount);

                FindBestTranslation(
                    routeSamples,
                    pathSamples,
                    out Vector2 candidateTranslation,
                    out float candidateError);

                if (candidateError >= rmsError)
                    continue;

                translation = candidateTranslation;
                rmsError = candidateError;
                driverNumber = pair.Key;
            }

            return driverNumber != 0 &&
                rmsError <=
                    routeLength *
                    RuntimeAlignmentMaximumRmsRatio;
        }

        void OnEnable()
        {
            hasRouteOffsetScale = false;
            runtimeSourceTranslation = Vector2.zero;
        }

        void OnValidate()
        {
            hasRouteOffsetScale = false;
        }

        public bool TryMap(Vector2 sourcePosition, out Vector3 localPosition)
        {
            localPosition = default;

            if (!active)
                return false;

            var mappedSourcePosition =
                MapRuntimeSourceAxes(sourcePosition);
            if (mappingMode == MappingMode.Route && TryMapRoute(
                mappedSourcePosition,
                null,
                out var routePosition,
                out var routeHeight))
            {
                float scale = OutputScale;
                localPosition = new Vector3(
                    routePosition.x * scale,
                    routeHeight * scale + heightOffset,
                    routePosition.y * scale);
                return true;
            }

            if (!TrySolve(out var a, out var b, out var translation))
                return false;

            var globalPosition = MapGlobal(mappedSourcePosition, a, b, translation);
            var mappedPosition = globalPosition;
            var y = GetHeight(mappedSourcePosition);

            if (mappingMode == MappingMode.Segment && TryMapSegment(mappedSourcePosition, out var segmentPosition))
                mappedPosition = Vector2.Lerp(globalPosition, segmentPosition, Mathf.Clamp01(segmentBlend));

            float output = OutputScale;
            localPosition = new Vector3(
                mappedPosition.x * output,
                y * output + heightOffset,
                mappedPosition.y * output);
            return true;
        }

        public bool TryMapGlobalPreview(Vector2 sourcePosition, out Vector3 localPosition)
        {
            localPosition = default;

            if (!active)
                return false;

            if (!TrySolve(out var a, out var b, out var translation))
                return false;

            var mappedSourcePosition = MapSourceAxes(sourcePosition);
            var mappedPosition = MapGlobal(mappedSourcePosition, a, b, translation);
            var y = GetHeight(mappedSourcePosition);

            localPosition = new Vector3(
                mappedPosition.x,
                y + heightOffset,
                mappedPosition.y);
            return true;
        }

        static Vector2 MapGlobal(Vector2 sourcePosition, float a, float b, Vector2 translation)
        {
            return new Vector2(
                a * sourcePosition.x - b * sourcePosition.y + translation.x,
                b * sourcePosition.x + a * sourcePosition.y + translation.y
            );
        }

        bool TryMapSegment(Vector2 sourcePosition, out Vector2 targetPosition)
        {
            return TryMapPolyline(
                sourcePosition,
                null,
                limitSourceDistance: true,
                preferredSegmentIndex: -1,
                out targetPosition,
                out _,
                out _);
        }

        bool TryMapRoute(
            Vector2 sourcePosition,
            float? sourceHeight,
            out Vector2 targetPosition,
            out float targetHeight)
        {
            return TryMapRoute(
                sourcePosition,
                sourceHeight,
                -1,
                out targetPosition,
                out targetHeight,
                out _);
        }

        bool TryMapRoute(
            Vector2 sourcePosition,
            float? sourceHeight,
            int preferredSegmentIndex,
            out Vector2 targetPosition,
            out float targetHeight,
            out int routeSegmentIndex)
        {
            return TryMapPolyline(
                sourcePosition,
                sourceHeight,
                limitSourceDistance: false,
                preferredSegmentIndex,
                out targetPosition,
                out targetHeight,
                out routeSegmentIndex);
        }

        bool TryMapPolyline(
            Vector2 sourcePosition,
            float? sourceHeight,
            bool limitSourceDistance,
            int preferredSegmentIndex,
            out Vector2 targetPosition,
            out float targetHeight,
            out int segmentIndex)
        {
            targetPosition = default;
            targetHeight = localY;
            segmentIndex = -1;

            if (points == null || GetConfiguredPointCount() < 2)
                return false;

            var bestDistance = float.MaxValue;
            var found = false;

            if (!limitSourceDistance &&
                preferredSegmentIndex >= 0 &&
                preferredSegmentIndex < points.Length)
            {
                for (int delta = -RouteSegmentLookBehind;
                    delta <= RouteSegmentLookAhead;
                    delta++)
                {
                    int candidateIndex = preferredSegmentIndex + delta;
                    if (loopMappingSegments)
                    {
                        candidateIndex =
                            (candidateIndex % points.Length + points.Length) %
                            points.Length;
                    }
                    else if (candidateIndex < 0 || candidateIndex >= points.Length)
                    {
                        continue;
                    }

                    if (TryUsePointSegment(
                        candidateIndex,
                        sourcePosition,
                        sourceHeight,
                        limitSourceDistance,
                        ref bestDistance,
                        ref targetPosition,
                        ref targetHeight,
                        ref segmentIndex))
                    {
                        found = true;
                    }
                }

                if (found)
                    return true;
            }

            for (int i = 0; i < points.Length; i++)
            {
                if (TryUsePointSegment(
                    i,
                    sourcePosition,
                    sourceHeight,
                    limitSourceDistance,
                    ref bestDistance,
                    ref targetPosition,
                    ref targetHeight,
                    ref segmentIndex))
                {
                    found = true;
                }
            }

            return found;
        }

        bool TryUsePointSegment(
            int pointIndex,
            Vector2 sourcePosition,
            float? sourceHeight,
            bool limitSourceDistance,
            ref float bestDistance,
            ref Vector2 targetPosition,
            ref float targetHeight,
            ref int segmentIndex)
        {
            Point a = points[pointIndex];
            if (!IsConfigured(a))
                return false;

            Point b;
            if (!TryGetNextConfiguredPoint(pointIndex, out b))
            {
                if (!loopMappingSegments ||
                    pointIndex != GetLastConfiguredPointIndex())
                {
                    return false;
                }

                Point? first = GetFirstConfiguredPoint();
                if (!first.HasValue)
                    return false;

                b = first.Value;
            }

            return TryUseMappingSegment(
                sourcePosition,
                sourceHeight,
                a,
                b,
                pointIndex,
                limitSourceDistance,
                ref bestDistance,
                ref targetPosition,
                ref targetHeight,
                ref segmentIndex);
        }

        bool TryUseMappingSegment(
            Vector2 sourcePosition,
            float? sourceHeight,
            Point a,
            Point b,
            int candidateSegmentIndex,
            bool limitSourceDistance,
            ref float bestDistance,
            ref Vector2 targetPosition,
            ref float targetHeight,
            ref int segmentIndex
        )
        {
            var sourceA = MapSourceAxes(a.sourcePosition);
            var sourceB = MapSourceAxes(b.sourcePosition);
            var sourceSegment = sourceB - sourceA;
            var sourceLengthSquared = sourceSegment.sqrMagnitude;

            if (sourceLengthSquared <= 0.000001f)
                return false;

            var sourceLength = Mathf.Sqrt(sourceLengthSquared);
            var rawT = Vector2.Dot(sourcePosition - sourceA, sourceSegment) / sourceLengthSquared;
            if (limitSourceDistance &&
                (rawT < -SegmentEndpointPadding || rawT > 1f + SegmentEndpointPadding))
                return false;

            var t = Mathf.Clamp01(rawT);
            var projectedSource = sourceA + sourceSegment * t;
            var distance = (sourcePosition - projectedSource).sqrMagnitude;

            if (useSourceHeightForRouteSegments && sourceHeight.HasValue)
            {
                float projectedSourceHeight = Mathf.Lerp(
                    a.sourceHeight,
                    b.sourceHeight,
                    t);
                float heightDistance =
                    sourceHeight.Value - projectedSourceHeight;
                distance +=
                    heightDistance *
                    heightDistance *
                    Mathf.Max(0f, routeSourceHeightWeight);
            }
            var maxDistance = Mathf.Min(
                MaxSegmentSourceDistance,
                Mathf.Max(MinSegmentSourceDistance, sourceLength * SegmentSourceDistanceRatio)
            );

            if (limitSourceDistance && distance > maxDistance * maxDistance)
                return false;

            if (distance >= bestDistance)
                return false;

            var targetA = new Vector2(a.targetLocalPosition.x, a.targetLocalPosition.z);
            var targetB = new Vector2(b.targetLocalPosition.x, b.targetLocalPosition.z);
            var targetSegment = targetB - targetA;
            var targetLength = targetSegment.magnitude;

            if (targetLength <= 0.000001f || sourceLength <= 0.000001f)
                return false;

            var sourceDirection = sourceSegment / sourceLength;
            var targetDirection = targetSegment / targetLength;
            var sourceNormal = new Vector2(-sourceDirection.y, sourceDirection.x);
            var targetNormal = new Vector2(-targetDirection.y, targetDirection.x);
            var signedOffset = Vector2.Dot(sourcePosition - projectedSource, sourceNormal);
            if (mappingMode == MappingMode.Route &&
                runtimeSourceTranslation.sqrMagnitude > 0.000001f &&
                maxRouteLateralOffset > 0f)
            {
                signedOffset = Mathf.Clamp(
                    signedOffset,
                    -maxRouteLateralOffset,
                    maxRouteLateralOffset);
            }
            var segmentOffsetScale = targetLength / sourceLength;
            var offsetScale = mappingMode == MappingMode.Route
                ? GetRouteOffsetScale(segmentOffsetScale)
                : segmentOffsetScale;

            bestDistance = distance;
            targetPosition = Vector2.Lerp(targetA, targetB, t) + targetNormal * signedOffset * offsetScale;
            targetHeight = Mathf.Lerp(
                a.targetLocalPosition.y,
                b.targetLocalPosition.y,
                t);
            segmentIndex = candidateSegmentIndex;
            return true;
        }

        float GetRouteOffsetScale(float fallback)
        {
            if (hasRouteOffsetScale)
                return routeOffsetScale;

            var sourceLength = 0f;
            var targetLength = 0f;
            Point? first = null;
            Point? previous = null;

            if (points != null)
            {
                foreach (var point in points)
                {
                    if (!IsConfigured(point))
                        continue;

                    if (!first.HasValue)
                        first = point;

                    if (previous.HasValue)
                        AddRouteSegmentLength(previous.Value, point, ref sourceLength, ref targetLength);

                    previous = point;
                }
            }

            if (loopMappingSegments && first.HasValue && previous.HasValue)
                AddRouteSegmentLength(previous.Value, first.Value, ref sourceLength, ref targetLength);

            routeOffsetScale = sourceLength > 0.000001f && targetLength > 0.000001f
                ? targetLength / sourceLength
                : fallback;
            hasRouteOffsetScale = true;
            return routeOffsetScale;
        }

        bool TryBuildRuntimeAlignmentRoute(
            out List<Vector2> route,
            out float routeLength)
        {
            route = new List<Vector2>();
            routeLength = 0f;

            if (points == null)
                return false;

            foreach (Point point in points)
            {
                if (IsConfigured(point))
                    route.Add(MapSourceAxes(point.sourcePosition));
            }

            if (route.Count < 8)
                return false;

            for (int i = 0; i < route.Count; i++)
                routeLength +=
                    Vector2.Distance(
                        route[i],
                        route[(i + 1) % route.Count]);

            return routeLength > 0.0001f;
        }

        bool TryBuildRuntimeAlignmentPath(
            List<LocationSample> samples,
            float startTime,
            float routeLength,
            float maximumStep,
            out List<Vector2> path,
            out float pathLength)
        {
            path = new List<Vector2>();
            pathLength = 0f;

            if (samples == null)
                return false;

            foreach (LocationSample sample in samples)
            {
                if (sample == null || sample.t < startTime)
                    continue;

                Vector2 position =
                    MapSourceAxes(sample.x, sample.y);

                if (path.Count == 0)
                {
                    path.Add(position);
                    continue;
                }

                float step =
                    Vector2.Distance(
                        path[path.Count - 1],
                        position);

                if (step <= 0.0001f)
                    continue;

                if (step > maximumStep)
                    return false;

                path.Add(position);
                pathLength += step;

                if (pathLength >= routeLength)
                    break;
            }

            return path.Count >= RuntimeAlignmentMinimumSamples &&
                pathLength >= routeLength &&
                Vector2.Distance(path[0], path[path.Count - 1]) <=
                    routeLength *
                    RuntimeAlignmentMaximumStepRatio;
        }

        static Vector2[] ResampleClosedRoute(
            List<Vector2> path,
            float pathLength,
            int sampleCount)
        {
            Vector2[] result =
                new Vector2[sampleCount];
            int segment = 0;
            float segmentStartDistance = 0f;

            for (int i = 0; i < sampleCount; i++)
            {
                float targetDistance =
                    pathLength *
                    i /
                    sampleCount;

                while (segment < path.Count - 1)
                {
                    float segmentLength =
                        Vector2.Distance(
                            path[segment],
                            path[(segment + 1) % path.Count]);

                    if (segmentStartDistance + segmentLength >= targetDistance)
                        break;

                    segmentStartDistance += segmentLength;
                    segment++;
                }

                Vector2 from = path[segment];
                Vector2 to =
                    path[(segment + 1) % path.Count];
                float length =
                    Vector2.Distance(from, to);
                float interpolation =
                    length > 0.0001f
                        ? (targetDistance - segmentStartDistance) / length
                        : 0f;
                result[i] =
                    Vector2.Lerp(
                        from,
                        to,
                        Mathf.Clamp01(interpolation));
            }

            return result;
        }

        static Vector2[] ResampleOpenPath(
            List<Vector2> path,
            float sampleLength,
            int sampleCount)
        {
            Vector2[] result =
                new Vector2[sampleCount];
            int segment = 0;
            float segmentStartDistance = 0f;

            for (int i = 0; i < sampleCount; i++)
            {
                float targetDistance =
                    sampleLength *
                    i /
                    sampleCount;

                while (segment < path.Count - 2)
                {
                    float segmentLength =
                        Vector2.Distance(
                            path[segment],
                            path[segment + 1]);

                    if (segmentStartDistance + segmentLength >= targetDistance)
                        break;

                    segmentStartDistance += segmentLength;
                    segment++;
                }

                Vector2 from = path[segment];
                Vector2 to = path[segment + 1];
                float length =
                    Vector2.Distance(from, to);
                float interpolation =
                    length > 0.0001f
                        ? (targetDistance - segmentStartDistance) / length
                        : 0f;
                result[i] =
                    Vector2.Lerp(
                        from,
                        to,
                        Mathf.Clamp01(interpolation));
            }

            return result;
        }

        static void FindBestTranslation(
            Vector2[] route,
            Vector2[] path,
            out Vector2 translation,
            out float rmsError)
        {
            translation = Vector2.zero;
            rmsError = float.MaxValue;

            for (int shift = 0; shift < route.Length; shift++)
            {
                Vector2 candidateTranslation =
                    Vector2.zero;

                for (int i = 0; i < route.Length; i++)
                {
                    int pathIndex =
                        (i + shift) %
                        path.Length;
                    candidateTranslation +=
                        route[i] -
                        path[pathIndex];
                }

                candidateTranslation /= route.Length;
                float squaredError = 0f;

                for (int i = 0; i < route.Length; i++)
                {
                    int pathIndex =
                        (i + shift) %
                        path.Length;
                    Vector2 error =
                        path[pathIndex] +
                        candidateTranslation -
                        route[i];
                    squaredError += error.sqrMagnitude;
                }

                float candidateError =
                    Mathf.Sqrt(
                        squaredError /
                        route.Length);

                if (candidateError >= rmsError)
                    continue;

                translation = candidateTranslation;
                rmsError = candidateError;
            }
        }

        void AddRouteSegmentLength(
            Point a,
            Point b,
            ref float sourceLength,
            ref float targetLength)
        {
            sourceLength += Vector2.Distance(
                MapSourceAxes(a.sourcePosition),
                MapSourceAxes(b.sourcePosition));

            targetLength += Vector2.Distance(
                new Vector2(a.targetLocalPosition.x, a.targetLocalPosition.z),
                new Vector2(b.targetLocalPosition.x, b.targetLocalPosition.z));
        }

        float GetHeight(Vector2 mappedSourcePosition)
        {
            if (heightMode == HeightMode.Flat)
                return localY;

            return useNearestPointHeight
                ? GetBlendedPointHeight(mappedSourcePosition)
                : localY;
        }

        float GetSourceHeight(float sourceHeight)
        {
            float origin = sourceHeightOrigin;

            if (useFirstSourceHeightAsOrigin)
            {
                if (!hasRuntimeSourceHeightOrigin)
                {
                    runtimeSourceHeightOrigin = sourceHeight;
                    hasRuntimeSourceHeightOrigin = true;
                }

                origin = runtimeSourceHeightOrigin;
            }

            return (sourceHeight - origin) * sourceHeightScale;
        }

        [ContextMenu("Log Calibration Report")]
        void LogCalibrationReport()
        {
            LogCalibrationReport(sourceAxisMode, true);
        }

        [ContextMenu("Log All Axis Calibration Reports")]
        void LogAllAxisCalibrationReports()
        {
            foreach (SourceAxisMode axisMode in Enum.GetValues(typeof(SourceAxisMode)))
                LogCalibrationReport(axisMode, axisMode == sourceAxisMode);
        }

        void LogCalibrationReport(SourceAxisMode axisMode, bool logPoints)
        {
            if (!TrySolve(axisMode, out var a, out var b, out var translation))
            {
                Debug.LogWarning($"{name}: axis={axisMode}, calibration solve failed.");
                return;
            }

            GetErrorStats(axisMode, a, b, translation, out var meanError, out var rmsError, out var maxError, out var worstPointName);

            var scale = Mathf.Sqrt(a * a + b * b);
            var rotation = Mathf.Atan2(b, a) * Mathf.Rad2Deg;
            Debug.Log(
                $"{name}: axis={axisMode}, scale={scale}, rotation={rotation}, offset={translation}, " +
                $"points={GetConfiguredPointCount()}, meanError={meanError}, rmsError={rmsError}, maxError={maxError}, worst={worstPointName}"
            );

            if (!logPoints)
                return;

            foreach (var point in points)
            {
                if (!IsConfigured(point))
                    continue;

                var source = MapSourceAxes(axisMode, point.sourcePosition);
                var mapped = new Vector2(
                    a * source.x - b * source.y + translation.x,
                    b * source.x + a * source.y + translation.y
                );
                var target = new Vector2(point.targetLocalPosition.x, point.targetLocalPosition.z);
                var error = Vector2.Distance(mapped, target);
                Debug.Log($"{name}: {point.name} mapped={mapped}, target={target}, error={error}");
            }
        }

        bool TrySolve(out float a, out float b, out Vector2 translation)
        {
            return TrySolve(sourceAxisMode, out a, out b, out translation);
        }

        bool TrySolve(SourceAxisMode axisMode, out float a, out float b, out Vector2 translation)
        {
            a = 0f;
            b = 0f;
            translation = default;

            if (points == null || GetConfiguredPointCount() < 2)
                return false;

            var sourceMean = Vector2.zero;
            var targetMean = Vector2.zero;
            var count = 0;

            foreach (var point in points)
            {
                if (!IsConfigured(point))
                    continue;

                sourceMean += MapSourceAxes(axisMode, point.sourcePosition);
                targetMean += new Vector2(point.targetLocalPosition.x, point.targetLocalPosition.z);
                count++;
            }

            sourceMean /= count;
            targetMean /= count;

            var denominator = 0f;
            var sumA = 0f;
            var sumB = 0f;

            foreach (var point in points)
            {
                if (!IsConfigured(point))
                    continue;

                var source = MapSourceAxes(axisMode, point.sourcePosition) - sourceMean;
                var target = new Vector2(point.targetLocalPosition.x, point.targetLocalPosition.z) - targetMean;

                denominator += source.x * source.x + source.y * source.y;
                sumA += source.x * target.x + source.y * target.y;
                sumB += source.x * target.y - source.y * target.x;
            }

            if (denominator <= Mathf.Epsilon)
                return false;

            a = sumA / denominator;
            b = sumB / denominator;
            translation = targetMean - new Vector2(
                a * sourceMean.x - b * sourceMean.y,
                b * sourceMean.x + a * sourceMean.y
            );

            return true;
        }

        void GetErrorStats(
            SourceAxisMode axisMode,
            float a,
            float b,
            Vector2 translation,
            out float meanError,
            out float rmsError,
            out float maxError,
            out string worstPointName
        )
        {
            meanError = 0f;
            rmsError = 0f;
            maxError = 0f;
            worstPointName = string.Empty;

            if (points == null)
                return;

            var count = 0;
            var squaredErrorSum = 0f;

            foreach (var point in points)
            {
                if (!IsConfigured(point))
                    continue;

                var source = MapSourceAxes(axisMode, point.sourcePosition);
                var mapped = new Vector2(
                    a * source.x - b * source.y + translation.x,
                    b * source.x + a * source.y + translation.y
                );
                var target = new Vector2(point.targetLocalPosition.x, point.targetLocalPosition.z);
                var error = Vector2.Distance(mapped, target);

                meanError += error;
                squaredErrorSum += error * error;
                count++;

                if (error <= maxError)
                    continue;

                maxError = error;
                worstPointName = point.name;
            }

            if (count == 0)
                return;

            meanError /= count;
            rmsError = Mathf.Sqrt(squaredErrorSum / count);
        }

        float GetBlendedPointHeight(Vector2 sourcePosition)
        {
            if (points == null || points.Length == 0)
                return localY;

            if (TryGetSegmentHeight(sourcePosition, out var segmentHeight))
                return BlendPointHeight(segmentHeight);

            foreach (var point in points)
            {
                if (!IsConfigured(point))
                    continue;

                var distance = (MapSourceAxes(point.sourcePosition) - sourcePosition).sqrMagnitude;
                if (distance <= 0.000001f)
                    return BlendPointHeight(point.targetLocalPosition.y);
            }

            return localY;
        }

        bool TryGetSegmentHeight(Vector2 sourcePosition, out float height)
        {
            height = localY;

            if (points == null)
                return false;

            var bestDistance = float.MaxValue;
            var found = false;
            var lastConfiguredIndex = loopPointHeightSegments ? GetLastConfiguredPointIndex() : -1;
            var firstConfiguredPoint = loopPointHeightSegments ? GetFirstConfiguredPoint() : null;

            for (int i = 0; i < points.Length; i++)
            {
                var a = points[i];
                if (!IsConfigured(a))
                    continue;

                for (int j = i + 1; j < points.Length; j++)
                {
                    var b = points[j];
                    if (!IsConfigured(b))
                        continue;

                    if (TryUseHeightSegment(sourcePosition, a, b, ref bestDistance, ref height))
                        found = true;

                    break;
                }

                if (i == lastConfiguredIndex && firstConfiguredPoint.HasValue && TryUseHeightSegment(
                    sourcePosition,
                    a,
                    firstConfiguredPoint.Value,
                    ref bestDistance,
                    ref height))
                    found = true;
            }

            return found;
        }

        bool TryUseHeightSegment(
            Vector2 sourcePosition,
            Point a,
            Point b,
            ref float bestDistance,
            ref float height
        )
        {
            var sourceA = MapSourceAxes(a.sourcePosition);
            var sourceB = MapSourceAxes(b.sourcePosition);
            var segment = sourceB - sourceA;
            var segmentLength = segment.sqrMagnitude;

            if (segmentLength <= 0.000001f)
                return false;

            var t = Mathf.Clamp01(Vector2.Dot(sourcePosition - sourceA, segment) / segmentLength);
            var projected = sourceA + segment * t;
            var distance = (sourcePosition - projected).sqrMagnitude;

            if (distance >= bestDistance)
                return false;

            bestDistance = distance;
            height = Mathf.Lerp(a.targetLocalPosition.y, b.targetLocalPosition.y, t);
            return true;
        }

        Point? GetFirstConfiguredPoint()
        {
            if (points == null)
                return null;

            foreach (var point in points)
            {
                if (IsConfigured(point))
                    return point;
            }

            return null;
        }

        bool TryGetNextConfiguredPoint(int startIndex, out Point nextPoint)
        {
            nextPoint = default;

            if (points == null)
                return false;

            for (int i = startIndex + 1; i < points.Length; i++)
            {
                if (!IsConfigured(points[i]))
                    continue;

                nextPoint = points[i];
                return true;
            }

            return false;
        }

        int GetLastConfiguredPointIndex()
        {
            if (points == null)
                return -1;

            for (int i = points.Length - 1; i >= 0; i--)
            {
                if (IsConfigured(points[i]))
                    return i;
            }

            return -1;
        }

        float BlendPointHeight(float height)
        {
            return Mathf.Lerp(localY, height, Mathf.Clamp01(pointHeightBlend));
        }

        int GetConfiguredPointCount()
        {
            if (points == null)
                return 0;

            var count = 0;
            foreach (var point in points)
            {
                if (IsConfigured(point))
                    count++;
            }

            return count;
        }

        internal static bool IsConfigured(Point point)
        {
            return !string.IsNullOrWhiteSpace(point.name)
                && point.sourcePosition.sqrMagnitude > 0.000001f;
        }

        Vector2 MapSourceAxes(Vector2 sourcePosition)
        {
            return MapSourceAxes(sourcePosition.x, sourcePosition.y);
        }

        Vector2 MapRuntimeSourceAxes(Vector2 sourcePosition)
        {
            return MapSourceAxes(sourcePosition) +
                runtimeSourceTranslation;
        }

        Vector2 MapRuntimeSourceAxes(float x, float y)
        {
            return MapSourceAxes(x, y) +
                runtimeSourceTranslation;
        }

        Vector2 MapSourceAxes(float x, float y)
        {
            return MapSourceAxes(sourceAxisMode, x, y);
        }

        internal static Vector2 MapSourceAxes(SourceAxisMode axisMode, Vector2 sourcePosition)
        {
            return MapSourceAxes(axisMode, sourcePosition.x, sourcePosition.y);
        }

        static Vector2 MapSourceAxes(SourceAxisMode axisMode, float x, float y)
        {
            return axisMode switch
            {
                SourceAxisMode.XNegativeY => new Vector2(x, -y),
                SourceAxisMode.YX => new Vector2(y, x),
                SourceAxisMode.NegativeYX => new Vector2(-y, x),
                _ => new Vector2(x, y)
            };
        }

    }
}
