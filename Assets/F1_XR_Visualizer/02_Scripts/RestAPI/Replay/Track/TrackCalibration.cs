using System;
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

        public int circuitKey;
        public string circuitName;
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
        public Point[] points;

        bool hasRuntimeSourceHeightOrigin;
        float runtimeSourceHeightOrigin;
        [NonSerialized] bool hasRouteOffsetScale;
        [NonSerialized] float routeOffsetScale;

        public float OutputScale =>
            outputScale > 0f ? outputScale : 1f;

        [Serializable]
        public struct Point
        {
            public string name;
            public Vector2 sourcePosition;
            public Vector3 targetLocalPosition;
        }

        public bool TryMap(LocationSample sample, out Vector3 localPosition)
        {
            if (!TryMap(new Vector2(sample.x, sample.y), out localPosition))
                return false;

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

        void OnEnable()
        {
            hasRouteOffsetScale = false;
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

            var mappedSourcePosition = MapSourceAxes(sourcePosition);
            if (mappingMode == MappingMode.Route && TryMapRoute(
                mappedSourcePosition,
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
                limitSourceDistance: true,
                out targetPosition,
                out _);
        }

        bool TryMapRoute(
            Vector2 sourcePosition,
            out Vector2 targetPosition,
            out float targetHeight)
        {
            return TryMapPolyline(
                sourcePosition,
                limitSourceDistance: false,
                out targetPosition,
                out targetHeight);
        }

        bool TryMapPolyline(
            Vector2 sourcePosition,
            bool limitSourceDistance,
            out Vector2 targetPosition,
            out float targetHeight)
        {
            targetPosition = default;
            targetHeight = localY;

            if (points == null || GetConfiguredPointCount() < 2)
                return false;

            var bestDistance = float.MaxValue;
            var found = false;
            var lastConfiguredIndex = loopMappingSegments ? GetLastConfiguredPointIndex() : -1;
            var firstConfiguredPoint = loopMappingSegments ? GetFirstConfiguredPoint() : null;

            for (int i = 0; i < points.Length; i++)
            {
                var a = points[i];
                if (!IsConfigured(a))
                    continue;

                if (TryGetNextConfiguredPoint(i, out var b) && TryUseMappingSegment(
                    sourcePosition,
                    a,
                    b,
                    limitSourceDistance,
                    ref bestDistance,
                    ref targetPosition,
                    ref targetHeight))
                    found = true;

                if (i != lastConfiguredIndex || !firstConfiguredPoint.HasValue)
                    continue;

                if (TryUseMappingSegment(
                    sourcePosition,
                    a,
                    firstConfiguredPoint.Value,
                    limitSourceDistance,
                    ref bestDistance,
                    ref targetPosition,
                    ref targetHeight))
                    found = true;
            }

            return found;
        }

        bool TryUseMappingSegment(
            Vector2 sourcePosition,
            Point a,
            Point b,
            bool limitSourceDistance,
            ref float bestDistance,
            ref Vector2 targetPosition,
            ref float targetHeight
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
