using System.Collections.Generic;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    internal sealed class OvertakeFallbackCorridor
    {
        private readonly List<Vector3> centerline = new();
        private float roadWidth;
        private bool loop;

        public bool IsAvailable =>
            roadWidth > 0f &&
            centerline.Count >= 2;

        public void Set(
            IReadOnlyList<Vector3> source,
            float width,
            bool isLoop)
        {
            centerline.Clear();
            roadWidth = Mathf.Max(0f, width);
            loop = isLoop;

            if (source == null)
                return;

            for (int i = 0; i < source.Count; i++)
            {
                Vector3 point = source[i];
                if (!float.IsFinite(point.x) ||
                    !float.IsFinite(point.y) ||
                    !float.IsFinite(point.z))
                {
                    continue;
                }

                centerline.Add(point);
            }
        }

        public float ClampOffset(
            Vector3 position,
            Vector3 lateralDirection,
            float vehicleWidth,
            float safetyMargin,
            float offset)
        {
            if (!TryGetOffsetRange(
                    position,
                    lateralDirection,
                    vehicleWidth,
                    safetyMargin,
                    out float minimum,
                    out float maximum))
            {
                return offset;
            }

            return Mathf.Clamp(offset, minimum, maximum);
        }

        public float AvailableOffset(
            Vector3 position,
            Vector3 lateralDirection,
            float vehicleWidth,
            float safetyMargin,
            int direction)
        {
            if (!TryGetOffsetRange(
                    position,
                    lateralDirection,
                    vehicleWidth,
                    safetyMargin,
                    out float minimum,
                    out float maximum))
            {
                return float.PositiveInfinity;
            }

            return Mathf.Max(
                0f,
                direction >= 0 ? maximum : -minimum);
        }

        private bool TryGetOffsetRange(
            Vector3 position,
            Vector3 lateralDirection,
            float vehicleWidth,
            float safetyMargin,
            out float minimum,
            out float maximum)
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
                    out _))
            {
                return false;
            }

            float directionProjection =
                Vector3.Dot(
                    lateralDirection,
                    corridorRight);
            if (Mathf.Abs(directionProjection) <= 0.000001f)
                return false;

            float usableHalfWidth = Mathf.Max(
                0f,
                roadWidth * 0.5f -
                Mathf.Max(0f, vehicleWidth) * 0.5f -
                Mathf.Max(0f, safetyMargin));
            float baseLateral = Vector3.Dot(
                position - center,
                corridorRight);
            minimum =
                (-usableHalfWidth - baseLateral) /
                directionProjection;
            maximum =
                (usableHalfWidth - baseLateral) /
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
            out int segmentIndex)
        {
            center = Vector3.zero;
            right = Vector3.right;
            segmentIndex = -1;
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
                float alignment = Mathf.Abs(
                    Vector3.Dot(
                        expectedForward,
                        horizontalDirection.normalized));
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
            }

            return segmentIndex >= 0;
        }

    }
}
