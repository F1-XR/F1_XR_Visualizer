using System.Collections.Generic;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public partial class CarGroundSnap
    {
        private static readonly bool DebugGroundSnap = false;
        private const float GroundProbeHeight = 100f;
        private const float GroundProbeDistance = 220f;
        private const float MinGroundOffset = 0.005f;
        private const float MaxTiltDegrees = 35f;
        private const float MinTrackSurfaceNormalDot = 0.35f;
        private const float SurfaceHeightChangeSeconds = 0.3f;
        private const float SurfaceHeightChangeBodyRatio = 1f;
        private const float SurfaceHeightSpreadBodyRatio = 1f;
        private const float SurfaceProbeBodyRatio = 1.5f;
        private const float GroundOffsetBodyRatio = 0.75f;
        private const float PositionSnapLerp = 0.45f;
        private const float RotationSnapLerp = 0.35f;
        private const float GroundSnapDebugLogInterval = 0.5f;

        private readonly Dictionary<int, Vector3> snappedPositions = new();
        private readonly Dictionary<int, Quaternion> snappedRotations = new();
        private readonly HashSet<Transform> colliderReadyRoots = new();
        private readonly HashSet<Collider> trackSurfaceColliders = new();
        private readonly Dictionary<int, Collider> lastGroundSnapColliders = new();
        private readonly Dictionary<int, float> nextGroundSnapDebugLogTimes = new();
        private readonly Dictionary<int, int> groundSnapMissCounts = new();
        private Transform trackSurfaceRoot;

        private struct GroundHit
        {
            public Vector3 point;
            public Vector3 normal;
            public Collider collider;
            public float verticalOffset;

            public GroundHit(Vector3 point, Vector3 normal, Collider collider, float verticalOffset)
            {
                this.point = point;
                this.normal = normal;
                this.collider = collider;
                this.verticalOffset = verticalOffset;
            }
        }

        public void ResolvePose(
            ReplayCarView car,
            Transform surfaceRoot,
            bool hasDirection,
            Quaternion trackRotation,
            Quaternion baseRotation,
            ref Vector3 position,
            ref Quaternion rotation)
        {
            EnsureTrackSurfaceColliders(surfaceRoot);

            if (!hasDirection)
            {
                ClearPose(car.driverNumber);
                return;
            }

            Vector3 up = trackRotation * Vector3.up;

            if (TrySnapToTrackSurface(
                car,
                position,
                trackRotation,
                baseRotation,
                out Vector3 snappedPosition,
                out Quaternion snappedRotation,
                out float bodyHeight))
            {
                groundSnapMissCounts.Remove(car.driverNumber);
                SmoothSnap(
                    car.driverNumber,
                    snappedPosition,
                    snappedRotation,
                    up,
                    bodyHeight,
                    out position,
                    out rotation);
            }
            else if (TryHoldPreviousSnap(
                car.driverNumber,
                position,
                rotation,
                up,
                out Vector3 heldPosition,
                out Quaternion heldRotation))
            {
                position = heldPosition;
                rotation = heldRotation;
            }
            else
            {
                ClearPose(car.driverNumber);
            }
        }

        public void ClearPose(int driverNumber)
        {
            snappedPositions.Remove(driverNumber);
            snappedRotations.Remove(driverNumber);
            groundSnapMissCounts.Remove(driverNumber);
        }

        public void RemoveCar(int driverNumber)
        {
            ClearPose(driverNumber);
            lastGroundSnapColliders.Remove(driverNumber);
            nextGroundSnapDebugLogTimes.Remove(driverNumber);
        }

        public void ClearSurfaceCache()
        {
            colliderReadyRoots.Clear();
            trackSurfaceColliders.Clear();
            trackSurfaceRoot = null;
        }

        public void ResetForCalibration()
        {
            ClearSurfaceCache();
            lastGroundSnapColliders.Clear();
            nextGroundSnapDebugLogTimes.Clear();
            groundSnapMissCounts.Clear();
        }

        public void Clear()
        {
            snappedPositions.Clear();
            snappedRotations.Clear();
            ResetForCalibration();
        }

        private void SmoothSnap(
            int driverNumber,
            Vector3 snappedPosition,
            Quaternion snappedRotation,
            Vector3 up,
            float bodyHeight,
            out Vector3 position,
            out Quaternion rotation)
        {
            if (snappedPositions.TryGetValue(driverNumber, out Vector3 previousPosition))
            {
                position = Vector3.Lerp(previousPosition, snappedPosition, PositionSnapLerp);
                position = ClampSurfaceHeightChange(previousPosition, position, up, bodyHeight);
                rotation = snappedRotations.TryGetValue(driverNumber, out Quaternion oldRotation)
                    ? Quaternion.Slerp(oldRotation, snappedRotation, RotationSnapLerp)
                    : snappedRotation;
            }
            else
            {
                position = snappedPosition;
                rotation = snappedRotation;
            }

            snappedPositions[driverNumber] = position;
            snappedRotations[driverNumber] = rotation;
        }

        private static Vector3 ClampSurfaceHeightChange(Vector3 previousPosition, Vector3 targetPosition, Vector3 up, float bodyHeight)
        {
            float heightDelta = Vector3.Dot(targetPosition - previousPosition, up);
            float maxStep = GetMaxSurfaceHeightStep(bodyHeight);
            float clampedDelta = Mathf.Clamp(heightDelta, -maxStep, maxStep);
            return targetPosition + up * (clampedDelta - heightDelta);
        }

        private static float GetMaxSurfaceHeightStep(float bodyHeight)
        {
            float deltaTime = Time.deltaTime > 0f ? Time.deltaTime : Time.unscaledDeltaTime;
            float height = Mathf.Max(MinGroundOffset, bodyHeight * SurfaceHeightChangeBodyRatio);
            return height * Mathf.Clamp01(deltaTime / SurfaceHeightChangeSeconds);
        }

        private static float GetMaxSurfaceHeightSpread(float bodyHeight)
        {
            return Mathf.Max(MinGroundOffset, bodyHeight * SurfaceHeightSpreadBodyRatio);
        }

        private static float GetMaxSurfaceProbeOffset(float bodyHeight)
        {
            return Mathf.Max(MinGroundOffset * 2f, bodyHeight * SurfaceProbeBodyRatio);
        }

        private bool TryHoldPreviousSnap(
            int driverNumber,
            Vector3 fallbackPosition,
            Quaternion fallbackRotation,
            Vector3 up,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = fallbackPosition;
            rotation = fallbackRotation;

            if (!snappedPositions.TryGetValue(driverNumber, out Vector3 previousPosition))
                return false;

            int misses = groundSnapMissCounts.TryGetValue(driverNumber, out int oldMisses)
                ? oldMisses + 1
                : 1;
            groundSnapMissCounts[driverNumber] = misses;

            float heightDelta = Vector3.Dot(previousPosition - fallbackPosition, up);
            position = fallbackPosition + up * heightDelta;
            rotation = snappedRotations.TryGetValue(driverNumber, out Quaternion previousRotation)
                ? Quaternion.Slerp(previousRotation, fallbackRotation, RotationSnapLerp)
                : fallbackRotation;

            snappedPositions[driverNumber] = position;
            snappedRotations[driverNumber] = rotation;
            return true;
        }
    }
}
