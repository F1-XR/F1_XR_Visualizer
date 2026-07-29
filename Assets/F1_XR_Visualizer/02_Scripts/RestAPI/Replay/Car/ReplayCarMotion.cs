using System.Collections.Generic;
using F1XR.RestAPI.Replay.Track.Placement;
using F1XR.RestAPI.Api;
using F1XR.RestAPI.Replay.Track.Build;
using F1XR.RestAPI.Utility;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public readonly struct ReplayCarPose
    {
        public readonly Transform parent;
        public readonly Vector3 rawPosition;
        public readonly Vector3 localForward;
        public readonly Vector3 worldPosition;
        public readonly Quaternion worldRotation;
        public readonly float localSpeed;
        public readonly bool hasDirection;

        public ReplayCarPose(
            Transform parent,
            Vector3 rawPosition,
            Vector3 localForward,
            Vector3 worldPosition,
            Quaternion worldRotation,
            float localSpeed,
            bool hasDirection)
        {
            this.parent = parent;
            this.rawPosition = rawPosition;
            this.localForward = localForward;
            this.worldPosition = worldPosition;
            this.worldRotation = worldRotation;
            this.localSpeed = localSpeed;
            this.hasDirection = hasDirection;
        }
    }

    public class ReplayCarMotion
    {
        private static readonly bool SnapCarsToTrackSurface = false;
        private const float RouteContinuityMaximumGap = 2f;

        private readonly CarGroundSnap groundSnap = new();
        private readonly Dictionary<LocationSample, Vector3> mappedPositions = new();
        private readonly Dictionary<int, int> preparedSampleCounts = new();
        private readonly Dictionary<int, LocationSample> preparedLastSamples = new();

        private bool hasOrigin;
        private Vector3 origin;
        private Vector2 preparedRuntimeSourceTranslation;
        private ARPlanePlacementController placement;
        private TrackCalibration calibration;
        private TrackRevealPlacer buildPlacer;
        private Transform customParent;
        private Vector3 customOrigin;
        private Quaternion customRotation = Quaternion.identity;

        public ReplayCarMotion(CarInstances _)
        {
        }

        public void SetPlacement(ARPlanePlacementController source)
        {
            if (placement != source)
                groundSnap.ClearSurfaceCache();

            placement = source;
        }

        public void SetBuildPlacer(TrackRevealPlacer source)
        {
            if (buildPlacer != source)
                groundSnap.ClearSurfaceCache();

            buildPlacer = source;
        }

        public void SetCalibration(
            TrackCalibration source,
            bool resetRuntimeState = true)
        {
            calibration = source;
            mappedPositions.Clear();
            ClearPreparedSamples();

            if (calibration != null && resetRuntimeState)
            {
                calibration.ResetRuntimeHeightOrigin();
                calibration.ResetRuntimeSourceTranslation();
            }

            hasOrigin = false;
            origin = Vector3.zero;
            groundSnap.ResetForCalibration();
        }

        public void SetCustomSpace(
            Transform parent,
            Vector3 sourceOrigin,
            Quaternion sourceToLocalRotation)
        {
            customParent = parent;
            customOrigin = sourceOrigin;
            customRotation = sourceToLocalRotation;
            groundSnap.ClearSurfaceCache();
        }

        internal void PrepareMappedPositions(
            Dictionary<int, List<LocationSample>> samplesByDriver)
        {
            if (calibration == null ||
                !calibration.active ||
                calibration.mappingMode != TrackCalibration.MappingMode.Route ||
                samplesByDriver == null ||
                IsPrepared(samplesByDriver))
            {
                return;
            }

            mappedPositions.Clear();
            preparedSampleCounts.Clear();
            preparedLastSamples.Clear();
            hasOrigin = false;
            origin = Vector3.zero;

            foreach (KeyValuePair<int, List<LocationSample>> pair in samplesByDriver)
            {
                List<LocationSample> samples = pair.Value;
                int previousSegmentIndex = -1;
                LocationSample previousSample = null;

                for (int i = 0; i < samples.Count; i++)
                {
                    LocationSample sample = samples[i];
                    if (sample == null)
                        continue;

                    if (previousSample == null ||
                        sample.t <= previousSample.t ||
                        sample.t - previousSample.t > RouteContinuityMaximumGap)
                    {
                        previousSegmentIndex = -1;
                    }

                    if (calibration.TryMapContinuous(
                        sample,
                        previousSegmentIndex,
                        out Vector3 position,
                        out int mappedSegmentIndex))
                    {
                        mappedPositions[sample] = position;
                        previousSegmentIndex = mappedSegmentIndex;
                    }
                    else
                    {
                        previousSegmentIndex = -1;
                    }

                    previousSample = sample;
                }

                preparedSampleCounts[pair.Key] = samples.Count;
                preparedLastSamples[pair.Key] =
                    samples.Count > 0 ? samples[samples.Count - 1] : null;
            }

            preparedRuntimeSourceTranslation =
                calibration.RuntimeSourceTranslation;
        }

        public bool TryGetMappedPosition(
            LocationSample sample,
            out Vector3 position)
        {
            if (sample == null)
            {
                position = default;
                return false;
            }

            if (mappedPositions.TryGetValue(sample, out position))
                return true;

            if (calibration != null)
            {
                if (calibration.TryMap(sample, out position))
                {
                    mappedPositions.Add(sample, position);
                    return true;
                }
            }

            position = ReplayCoordinate.ToUnity(sample);
            if (!hasOrigin)
            {
                origin = position;
                hasOrigin = true;
            }

            position -= origin;
            mappedPositions.Add(sample, position);
            return true;
        }

        public void Move(
            ReplayCarView car,
            LocationSample a,
            LocationSample b,
            float time,
            out float interpolation,
            out float duration)
        {
            ResolvePose(
                car,
                a,
                b,
                time,
                out ReplayCarPose pose,
                out interpolation,
                out duration);
            ApplyLogicalPose(car, pose);
            car.ResetVisualMotion();
        }

        public void ResolvePose(
            ReplayCarView car,
            LocationSample a,
            LocationSample b,
            float time,
            out ReplayCarPose pose,
            out float interpolation,
            out float duration)
        {
            ResolvePose(
                car,
                a,
                a,
                b,
                b,
                time,
                out pose,
                out interpolation,
                out duration);
        }

        public void ResolvePose(
            ReplayCarView car,
            LocationSample previous,
            LocationSample a,
            LocationSample b,
            LocationSample next,
            float time,
            out ReplayCarPose pose,
            out float interpolation,
            out float duration)
        {
            duration = Mathf.Max(0.001f, b.t - a.t);
            interpolation = Mathf.Clamp01((time - a.t) / duration);

            TryGetMappedPosition(a, out Vector3 positionA);
            TryGetMappedPosition(b, out Vector3 positionB);
            TryGetMappedPosition(previous, out Vector3 previousPosition);
            TryGetMappedPosition(next, out Vector3 nextPosition);

            if (customParent != null)
            {
                positionA = customRotation * (positionA - customOrigin);
                positionB = customRotation * (positionB - customOrigin);
                previousPosition = customRotation * (previousPosition - customOrigin);
                nextPosition = customRotation * (nextPosition - customOrigin);
            }

            Vector3 rawPosition = Vector3.Lerp(
                positionA,
                positionB,
                interpolation);
            Transform placementTransform = ResolvePlacementTransform();
            Transform carParent = ResolveCarParent(placementTransform);

            Vector3 segmentDirection = FlatDirection(positionA, positionB);
            Vector3 entryDirection = FlatDirection(previousPosition, positionB);
            Vector3 exitDirection = FlatDirection(positionA, nextPosition);
            Vector3 direction = SmoothDirection(
                entryDirection,
                exitDirection,
                segmentDirection,
                interpolation);
            bool hasDirection = direction.sqrMagnitude > 0.000001f;
            Quaternion trackRotation = hasDirection
                ? Quaternion.LookRotation(direction.normalized, Vector3.up)
                : Quaternion.identity;
            Quaternion worldTrackRotation = placementTransform != null
                ? placementTransform.rotation * trackRotation
                : trackRotation;
            Quaternion worldRotation = worldTrackRotation;
            Vector3 worldPosition = placementTransform != null
                ? placementTransform.TransformPoint(rawPosition)
                : rawPosition;

            if (SnapCarsToTrackSurface)
            {
                groundSnap.ResolvePose(
                    car,
                    placementTransform,
                    hasDirection,
                    worldTrackRotation,
                    Quaternion.identity,
                    ref worldPosition,
                    ref worldRotation);
            }
            else
            {
                groundSnap.ClearPose(car.driverNumber);
            }

            pose = new ReplayCarPose(
                carParent,
                rawPosition,
                hasDirection ? direction.normalized : Vector3.forward,
                worldPosition,
                worldRotation,
                segmentDirection.magnitude / duration,
                hasDirection);
        }

        private static Vector3 FlatDirection(Vector3 from, Vector3 to)
        {
            Vector3 direction = to - from;
            direction.y = 0f;
            return direction;
        }

        private static Vector3 SmoothDirection(
            Vector3 entry,
            Vector3 exit,
            Vector3 fallback,
            float interpolation)
        {
            if (entry.sqrMagnitude <= 0.000001f)
                entry = fallback;
            if (exit.sqrMagnitude <= 0.000001f)
                exit = fallback;
            if (entry.sqrMagnitude <= 0.000001f || exit.sqrMagnitude <= 0.000001f)
                return fallback;

            return Vector3.Slerp(
                entry.normalized,
                exit.normalized,
                interpolation).normalized;
        }

        public void ApplyLogicalPose(
            ReplayCarView car,
            ReplayCarPose pose)
        {
            SetCarParent(car, pose.parent);
            ApplyPose(car, pose);
        }

        public void ApplyVisualPose(
            ReplayCarView car,
            ReplayCarPose basePose,
            VisualMotionPose visualPose)
        {
            if (!visualPose.active || Mathf.Abs(visualPose.lateralOffset) <= Mathf.Epsilon)
            {
                car.ResetVisualMotion();
                return;
            }

            Vector3 localRight =
                visualPose.localLateralDirection;
            if (localRight.sqrMagnitude <= 0.000001f)
            {
                localRight = Vector3.Cross(
                    Vector3.up,
                    basePose.localForward);
            }
            localRight.Normalize();
            Vector3 localOffset = localRight * visualPose.lateralOffset;
            Vector3 worldOffset = basePose.parent != null
                ? basePose.parent.TransformVector(localOffset)
                : localOffset;

            car.ApplyVisualMotion(worldOffset, visualPose.localYaw);
        }

        public void RemoveCar(int driver)
        {
            groundSnap.RemoveCar(driver);
        }

        public void Clear()
        {
            groundSnap.Clear();
            mappedPositions.Clear();
            ClearPreparedSamples();
            hasOrigin = false;
            origin = Vector3.zero;
        }

        private bool IsPrepared(
            Dictionary<int, List<LocationSample>> samplesByDriver)
        {
            if (preparedSampleCounts.Count != samplesByDriver.Count ||
                preparedRuntimeSourceTranslation !=
                    calibration.RuntimeSourceTranslation)
            {
                return false;
            }

            foreach (KeyValuePair<int, List<LocationSample>> pair in samplesByDriver)
            {
                List<LocationSample> samples = pair.Value;
                LocationSample lastSample =
                    samples.Count > 0 ? samples[samples.Count - 1] : null;

                if (!preparedSampleCounts.TryGetValue(
                        pair.Key,
                        out int preparedCount) ||
                    preparedCount != samples.Count ||
                    !preparedLastSamples.TryGetValue(
                        pair.Key,
                        out LocationSample preparedLastSample) ||
                    !ReferenceEquals(preparedLastSample, lastSample))
                {
                    return false;
                }
            }

            return true;
        }

        private void ClearPreparedSamples()
        {
            preparedSampleCounts.Clear();
            preparedLastSamples.Clear();
            preparedRuntimeSourceTranslation = Vector2.zero;
        }

        private Transform ResolvePlacementTransform()
        {
            if (customParent != null)
                return customParent;

            if (HasBuildPlacement())
                return buildPlacer.PlacementTransform;

            return placement != null && placement.HasPlacement
                ? placement.PlacementTransform
                : null;
        }

        private Transform ResolveCarParent(Transform placementTransform)
        {
            if (customParent != null)
                return customParent;

            return HasBuildPlacement()
                ? buildPlacer.CarsTransform
                : placementTransform;
        }

        private bool HasBuildPlacement()
        {
            return buildPlacer != null && buildPlacer.HasPlacement;
        }

        private static void ApplyPose(
            ReplayCarView car,
            ReplayCarPose pose)
        {
            car.rawPosition = pose.rawPosition;
            car.LogicalRoot.position = pose.worldPosition;

            if (pose.hasDirection)
                car.LogicalRoot.rotation = pose.worldRotation;
        }

        private static void SetCarParent(
            ReplayCarView car,
            Transform parent)
        {
            Transform root = car.LogicalRoot;
            if (root.parent == parent)
                return;

            root.SetParent(parent, worldPositionStays: false);
        }
    }
}
