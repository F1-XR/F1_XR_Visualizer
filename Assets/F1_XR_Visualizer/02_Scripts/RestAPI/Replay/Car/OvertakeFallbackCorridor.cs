using System.Collections.Generic;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    internal sealed class OvertakeFallbackCorridor
    {
        private const float MinimumTrackOverlapRatio = 0.7f;

        private readonly List<Vector3> centerline = new();
        private readonly List<Vector3> leftBoundary = new();
        private readonly List<Vector3> rightBoundary = new();
        private float roadWidth;
        private bool loop;

        public bool IsAvailable =>
            centerline.Count >= 2 &&
            (roadWidth > 0f ||
             HasAuthoritativeBoundaries);

        private bool HasAuthoritativeBoundaries =>
            leftBoundary.Count == centerline.Count &&
            rightBoundary.Count == centerline.Count;

        public void Set(
            IReadOnlyList<Vector3> source,
            float width,
            bool isLoop)
        {
            centerline.Clear();
            leftBoundary.Clear();
            rightBoundary.Clear();
            roadWidth = Mathf.Max(0f, width);
            loop = isLoop;

            CopyFinitePoints(source, centerline);
        }

        public void SetBoundaries(
            IReadOnlyList<Vector3> sourceCenterline,
            IReadOnlyList<Vector3> sourceLeftBoundary,
            IReadOnlyList<Vector3> sourceRightBoundary,
            bool isLoop)
        {
            centerline.Clear();
            leftBoundary.Clear();
            rightBoundary.Clear();
            roadWidth = 0f;
            loop = isLoop;

            if (sourceCenterline == null ||
                sourceLeftBoundary == null ||
                sourceRightBoundary == null ||
                sourceCenterline.Count != sourceLeftBoundary.Count ||
                sourceCenterline.Count != sourceRightBoundary.Count)
            {
                return;
            }

            for (int i = 0; i < sourceCenterline.Count; i++)
            {
                Vector3 center = sourceCenterline[i];
                Vector3 left = sourceLeftBoundary[i];
                Vector3 right = sourceRightBoundary[i];
                if (!IsFinite(center) ||
                    !IsFinite(left) ||
                    !IsFinite(right))
                {
                    centerline.Clear();
                    leftBoundary.Clear();
                    rightBoundary.Clear();
                    return;
                }

                centerline.Add(center);
                leftBoundary.Add(left);
                rightBoundary.Add(right);
            }
        }

        public float ClampOffset(
            Vector3 position,
            Vector3 lateralDirection,
            Vector3 localForward,
            float vehicleWidth,
            float vehicleLength,
            float safetyMargin,
            float offset,
            bool includeFutureLimits = true)
        {
            if (!TryGetOffsetRange(
                    position,
                    lateralDirection,
                    localForward,
                    vehicleWidth,
                    vehicleLength,
                    safetyMargin,
                    out float minimum,
                    out float maximum,
                    includeFutureLimits))
            {
                return offset;
            }

            float clamped = Mathf.Clamp(offset, minimum, maximum);
            if (offset > 0f)
                return Mathf.Max(0f, clamped);
            if (offset < 0f)
                return Mathf.Min(0f, clamped);

            return 0f;
        }

        public bool TryGetOffsetRange(
            Vector3 position,
            Vector3 lateralDirection,
            Vector3 localForward,
            float vehicleWidth,
            float vehicleLength,
            float safetyMargin,
            out float minimum,
            out float maximum,
            bool includeFutureLimits = true)
        {
            minimum = 0f;
            maximum = 0f;
            lateralDirection.y = 0f;
            if (lateralDirection.sqrMagnitude <= 0.000001f)
                return false;

            lateralDirection.Normalize();
            if (!IsAvailable ||
                !TryGetFrame(
                    position,
                    lateralDirection,
                    out Vector3 center,
                    out Vector3 corridorRight,
                    out int segmentIndex,
                    out float interpolation))
            {
                return false;
            }

            float directionProjection =
                Vector3.Dot(
                    lateralDirection,
                    corridorRight);
            if (Mathf.Abs(directionProjection) <= 0.000001f)
                return false;

            float bodyHalfExtent = ProjectedBodyHalfExtent(
                localForward,
                corridorRight,
                vehicleWidth,
                vehicleLength);
            float boundaryMinimum;
            float boundaryMaximum;
            if (HasAuthoritativeBoundaries)
            {
                float minimumTrackOverlap =
                    Mathf.Max(0f, vehicleWidth) *
                    MinimumTrackOverlapRatio;
                float boundaryInset =
                    minimumTrackOverlap -
                    bodyHalfExtent;
                int next =
                    (segmentIndex + 1) %
                    centerline.Count;
                Vector3 left = Vector3.Lerp(
                    leftBoundary[segmentIndex],
                    leftBoundary[next],
                    interpolation);
                Vector3 right = Vector3.Lerp(
                    rightBoundary[segmentIndex],
                    rightBoundary[next],
                    interpolation);
                boundaryMinimum =
                    Vector3.Dot(
                        left - center,
                        corridorRight) +
                    boundaryInset;
                boundaryMaximum =
                    Vector3.Dot(
                        right - center,
                        corridorRight) -
                    boundaryInset;
                if (includeFutureLimits)
                {
                    ApplyPredictiveBoundaryLimits(
                        segmentIndex,
                        interpolation,
                        vehicleWidth,
                        vehicleLength,
                        safetyMargin,
                        ref boundaryMinimum,
                        ref boundaryMaximum);
                }
            }
            else
            {
                float minimumTrackOverlap =
                    Mathf.Max(0f, vehicleWidth) *
                    MinimumTrackOverlapRatio;
                float usableHalfWidth = Mathf.Max(
                    0f,
                    roadWidth * 0.5f +
                    bodyHalfExtent -
                    minimumTrackOverlap);
                boundaryMinimum = -usableHalfWidth;
                boundaryMaximum = usableHalfWidth;
            }

            if (boundaryMinimum > boundaryMaximum)
            {
                float collapsed = (
                    boundaryMinimum +
                    boundaryMaximum) * 0.5f;
                boundaryMinimum = collapsed;
                boundaryMaximum = collapsed;
            }

            float baseLateral = Vector3.Dot(
                position - center,
                corridorRight);
            minimum =
                (boundaryMinimum - baseLateral) /
                directionProjection;
            maximum =
                (boundaryMaximum - baseLateral) /
                directionProjection;
            if (minimum > maximum)
                (minimum, maximum) = (maximum, minimum);

            return true;
        }

        private bool TryGetFrame(
            Vector3 position,
            Vector3 lateralDirection,
            out Vector3 center,
            out Vector3 right,
            out int segmentIndex,
            out float segmentInterpolation)
        {
            center = Vector3.zero;
            right = Vector3.right;
            segmentIndex = -1;
            segmentInterpolation = 0f;
            if (!IsAvailable)
                return false;

            float closestDistance = float.PositiveInfinity;
            float closestAlignment = -1f;
            Vector3 expectedForward =
                Vector3.Cross(
                    lateralDirection,
                    Vector3.up);
            int segmentCount = loop
                ? centerline.Count
                : centerline.Count - 1;
            for (int i = 0; i < segmentCount; i++)
            {
                Vector3 from = centerline[i];
                Vector3 to =
                    centerline[(i + 1) % centerline.Count];
                Vector3 direction = to - from;
                float lengthSquared =
                    direction.sqrMagnitude;
                if (lengthSquared <= 0.000001f)
                    continue;

                Vector3 horizontalDirection = direction;
                horizontalDirection.y = 0f;
                if (horizontalDirection.sqrMagnitude <=
                    0.000001f)
                {
                    continue;
                }

                float interpolation = Mathf.Clamp01(
                    Vector3.Dot(
                        position - from,
                        direction) /
                    lengthSquared);
                Vector3 candidate =
                    from + direction * interpolation;
                Vector3 difference =
                    position - candidate;
                float distance =
                    difference.sqrMagnitude;
                float alignment = Vector3.Dot(
                    expectedForward,
                    horizontalDirection.normalized);
                if (alignment <= 0f)
                    continue;
                if (distance > closestDistance ||
                    Mathf.Approximately(
                        distance,
                        closestDistance) &&
                    alignment <= closestAlignment)
                {
                    continue;
                }

                closestDistance = distance;
                closestAlignment = alignment;
                center = candidate;
                right = Vector3.Cross(
                    Vector3.up,
                    horizontalDirection.normalized);
                segmentIndex = i;
                segmentInterpolation = interpolation;
            }

            return segmentIndex >= 0;
        }

        private void ApplyPredictiveBoundaryLimits(
            int segmentIndex,
            float segmentInterpolation,
            float vehicleWidth,
            float vehicleLength,
            float safetyMargin,
            ref float minimum,
            ref float maximum)
        {
            if (!HasAuthoritativeBoundaries ||
                loop ||
                segmentIndex < 0 ||
                segmentIndex >= centerline.Count - 1)
            {
                return;
            }

            float safeWidth =
                Mathf.Max(0f, vehicleWidth);
            float transitionLength =
                Mathf.Max(
                    safeWidth,
                    Mathf.Max(0f, vehicleLength));
            if (safeWidth <= 0f ||
                transitionLength <= 0f)
            {
                return;
            }

            float lateralPerLongitudinal =
                safeWidth / transitionLength;
            float distance = Vector3.Distance(
                Vector3.Lerp(
                    centerline[segmentIndex],
                    centerline[segmentIndex + 1],
                    segmentInterpolation),
                centerline[segmentIndex + 1]);
            float minimumTrackOverlap =
                Mathf.Max(0f, vehicleWidth) *
                MinimumTrackOverlapRatio;
            for (int i = segmentIndex + 1;
                 i < centerline.Count;
                 i++)
            {
                Vector3 forward =
                    GetSampleForward(i);
                Vector3 right =
                    Vector3.Cross(
                        Vector3.up,
                        forward);
                float bodyHalfExtent =
                    ProjectedBodyHalfExtent(
                        forward,
                        right,
                        vehicleWidth,
                        vehicleLength);
                float transitionAllowance =
                    distance *
                    lateralPerLongitudinal;
                float futureMinimum =
                    Vector3.Dot(
                        leftBoundary[i] -
                        centerline[i],
                        right) +
                    minimumTrackOverlap -
                    bodyHalfExtent;
                float futureMaximum =
                    Vector3.Dot(
                        rightBoundary[i] -
                        centerline[i],
                        right) -
                    minimumTrackOverlap +
                    bodyHalfExtent;
                minimum = Mathf.Max(
                    minimum,
                    futureMinimum -
                    transitionAllowance);
                maximum = Mathf.Min(
                    maximum,
                    futureMaximum +
                    transitionAllowance);

                if (i < centerline.Count - 1)
                {
                    distance += Vector3.Distance(
                        centerline[i],
                        centerline[i + 1]);
                }
            }
        }

        private Vector3 GetSampleForward(int index)
        {
            int previous = loop
                ? (index - 1 + centerline.Count) %
                  centerline.Count
                : Mathf.Max(0, index - 1);
            int next = loop
                ? (index + 1) % centerline.Count
                : Mathf.Min(
                    centerline.Count - 1,
                    index + 1);
            Vector3 forward =
                centerline[next] -
                centerline[previous];
            forward.y = 0f;
            return forward.sqrMagnitude > 0.000001f
                ? forward.normalized
                : Vector3.forward;
        }

        private static float ProjectedBodyHalfExtent(
            Vector3 localForward,
            Vector3 corridorRight,
            float vehicleWidth,
            float vehicleLength)
        {
            localForward.y = 0f;
            corridorRight.y = 0f;
            if (localForward.sqrMagnitude <= 0.000001f ||
                corridorRight.sqrMagnitude <= 0.000001f)
            {
                return Mathf.Max(0f, vehicleWidth) * 0.5f;
            }

            localForward.Normalize();
            corridorRight.Normalize();
            Vector3 vehicleRight =
                Vector3.Cross(
                    Vector3.up,
                    localForward);
            return
                Mathf.Abs(
                    Vector3.Dot(
                        vehicleRight,
                        corridorRight)) *
                Mathf.Max(0f, vehicleWidth) * 0.5f +
                Mathf.Abs(
                    Vector3.Dot(
                        localForward,
                        corridorRight)) *
                Mathf.Max(0f, vehicleLength) * 0.5f;
        }

        private static void CopyFinitePoints(
            IReadOnlyList<Vector3> source,
            List<Vector3> destination)
        {
            if (source == null)
                return;

            for (int i = 0; i < source.Count; i++)
            {
                Vector3 point = source[i];
                if (IsFinite(point))
                    destination.Add(point);
            }
        }

        private static bool IsFinite(Vector3 point)
        {
            return float.IsFinite(point.x) &&
                float.IsFinite(point.y) &&
                float.IsFinite(point.z);
        }

    }
}
