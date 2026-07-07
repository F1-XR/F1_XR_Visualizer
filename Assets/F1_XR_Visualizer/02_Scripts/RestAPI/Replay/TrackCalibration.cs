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

        public int circuitKey;
        public string circuitName;
        public bool active;
        public SourceAxisMode sourceAxisMode;
        public float localY;
        public float heightOffset;
        public bool useNearestPointHeight = true;
        [Range(0f, 1f)] public float pointHeightBlend = 0.25f;
        public bool loopPointHeightSegments = true;
        public Point[] points;

        [Serializable]
        public struct Point
        {
            public string name;
            public Vector2 sourcePosition;
            public Vector3 targetLocalPosition;
        }

        public bool TryMap(LocationSample sample, out Vector3 localPosition)
        {
            return TryMap(new Vector2(sample.x, sample.y), out localPosition);
        }

        public bool TryMap(Vector2 sourcePosition, out Vector3 localPosition)
        {
            localPosition = default;

            if (!active)
                return false;

            if (!TrySolve(out var a, out var b, out var translation))
                return false;

            var mappedSourcePosition = MapSourceAxes(sourcePosition);
            var x = a * mappedSourcePosition.x - b * mappedSourcePosition.y + translation.x;
            var z = b * mappedSourcePosition.x + a * mappedSourcePosition.y + translation.y;
            var y = useNearestPointHeight
                ? GetBlendedPointHeight(mappedSourcePosition)
                : localY;

            localPosition = new Vector3(x, y + heightOffset, z);
            return true;
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

                if (!loopPointHeightSegments)
                    continue;

                var first = GetFirstConfiguredPoint();
                if (first.HasValue && i == GetLastConfiguredPointIndex() && TryUseHeightSegment(sourcePosition, a, first.Value, ref bestDistance, ref height))
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

        static bool IsConfigured(Point point)
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

        static Vector2 MapSourceAxes(SourceAxisMode axisMode, Vector2 sourcePosition)
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
