using F1XR.AR;
using F1XR.RestAPI.Api;
using F1XR.RestAPI.AR;
using F1XR.RestAPI.Utility;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public class ReplayCarMotion
    {
        private static readonly bool SnapCarsToTrackSurface = false;

        private readonly CarInstances carInstances;
        private readonly CarGroundSnap groundSnap = new();

        private bool hasOrigin;
        private Vector3 origin;
        private ARPlanePlacementController placement;
        private TrackCalibration calibration;
        private TrackRevealPlacer buildPlacer;

        public ReplayCarMotion(CarInstances carInstances)
        {
            this.carInstances = carInstances;
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

        public void SetCalibration(TrackCalibration source)
        {
            calibration = source;

            if (calibration != null)
                calibration.ResetRuntimeHeightOrigin();

            hasOrigin = false;
            origin = Vector3.zero;
            groundSnap.ResetForCalibration();
        }

        public void Move(
            ReplayCarView car,
            LocationSample a,
            LocationSample b,
            float time,
            out float interpolation,
            out float duration)
        {
            duration = Mathf.Max(0.001f, b.t - a.t);
            interpolation = Mathf.Clamp01((time - a.t) / duration);

            Vector3 positionA = default;
            Vector3 positionB = default;
            bool useCalibration = false;

            if (calibration != null)
            {
                bool mappedA = calibration.TryMap(a, out positionA);
                bool mappedB = calibration.TryMap(b, out positionB);
                useCalibration = mappedA && mappedB;
            }

            if (!useCalibration)
            {
                positionA = ReplayCoordinate.ToUnity(a);
                positionB = ReplayCoordinate.ToUnity(b);

                if (!hasOrigin)
                {
                    origin = positionA;
                    hasOrigin = true;
                }

                positionA -= origin;
                positionB -= origin;
            }

            Vector3 rawPosition = Vector3.Lerp(
                positionA,
                positionB,
                interpolation);
            Transform placementTransform = ResolvePlacementTransform();
            Transform carParent = ResolveCarParent(placementTransform);

            SetCarParent(car, carParent);

            Vector3 direction = positionB - positionA;
            direction.y = 0f;
            bool hasDirection = direction.sqrMagnitude > 0.000001f;
            Quaternion baseRotation =
                carInstances.GetBaseRotation(car.driverNumber);
            Quaternion trackRotation = hasDirection
                ? Quaternion.LookRotation(direction.normalized, Vector3.up)
                : Quaternion.identity;
            Quaternion worldTrackRotation = placementTransform != null
                ? placementTransform.rotation * trackRotation
                : trackRotation;
            Quaternion worldRotation = placementTransform != null
                ? worldTrackRotation * baseRotation
                : trackRotation * baseRotation;
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
                    baseRotation,
                    ref worldPosition,
                    ref worldRotation);
            }
            else
            {
                groundSnap.ClearPose(car.driverNumber);
            }

            ApplyPose(
                car,
                rawPosition,
                worldPosition,
                worldRotation,
                hasDirection);
        }

        public void RemoveCar(int driver)
        {
            groundSnap.RemoveCar(driver);
        }

        public void Clear()
        {
            groundSnap.Clear();
            hasOrigin = false;
            origin = Vector3.zero;
        }

        private Transform ResolvePlacementTransform()
        {
            if (HasBuildPlacement())
                return buildPlacer.PlacementTransform;

            return placement != null && placement.HasPlacement
                ? placement.PlacementTransform
                : null;
        }

        private Transform ResolveCarParent(Transform placementTransform)
        {
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
            Vector3 rawPosition,
            Vector3 worldPosition,
            Quaternion worldRotation,
            bool hasDirection)
        {
            car.rawPosition = rawPosition;
            car.transform.position = worldPosition;

            if (hasDirection)
                car.transform.rotation = worldRotation;
        }

        private static void SetCarParent(
            ReplayCarView car,
            Transform parent)
        {
            if (car.transform.parent == parent)
                return;

            car.transform.SetParent(parent, worldPositionStays: false);
        }
    }
}