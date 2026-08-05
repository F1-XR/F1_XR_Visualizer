using UnityEngine;

namespace F1XR.RestAPI.Replay.Room
{
    [DisallowMultipleComponent]
    public sealed class LifeSizeDriveByVehiclePresentation :
        MonoBehaviour
    {
        private EventPopoutReplay eventReplay;
        private LifeSizeDriveByPlan preparedPlan;
        private int firstDriver;
        private int secondDriver;
        private ReplayCarView firstCar;
        private ReplayCarView secondCar;
        private Transform firstOriginalParent;
        private Transform secondOriginalParent;
        private float firstPresentationScale = 1f;
        private float secondPresentationScale = 1f;
        private bool committed;

        internal bool IsPrepared =>
            eventReplay != null &&
            preparedPlan != null &&
            preparedPlan.IsValid &&
            firstDriver > 0 &&
            secondDriver > 0 &&
            firstDriver != secondDriver &&
            firstCar != null &&
            secondCar != null;
        internal bool IsCommitted => IsPrepared && committed;

        internal bool TryPrepare(
            EventPopoutReplay replay,
            LifeSizeDriveByPlan plan,
            int firstDriverNumber,
            int secondDriverNumber,
            ReplayCarView firstVehicle,
            ReplayCarView secondVehicle,
            out string failure)
        {
            Clear();
            failure = "";
            if (replay == null ||
                !replay.IsActive ||
                plan == null ||
                !plan.IsValid ||
                firstDriverNumber <= 0 ||
                secondDriverNumber <= 0 ||
                firstDriverNumber == secondDriverNumber ||
                firstVehicle == null ||
                secondVehicle == null)
            {
                failure =
                    "The LifeSize vehicle presentation contract is unavailable.";
                return false;
            }

            eventReplay = replay;
            preparedPlan = plan;
            firstDriver = firstDriverNumber;
            secondDriver = secondDriverNumber;
            firstCar = firstVehicle;
            secondCar = secondVehicle;
            firstOriginalParent = firstCar.LogicalRoot.parent;
            secondOriginalParent = secondCar.LogicalRoot.parent;
            if (!TryResolvePresentationScale(
                    firstCar,
                    out firstPresentationScale) ||
                !TryResolvePresentationScale(
                    secondCar,
                    out secondPresentationScale))
            {
                failure =
                    "The LifeSize vehicle visual length could not be resolved.";
                Clear();
                return false;
            }

            if (!TryValidateDriver(firstDriver, out failure) ||
                !TryValidateDriver(secondDriver, out failure))
            {
                Clear();
                return false;
            }

            return true;
        }

        internal bool TryCommit(
            LifeSizeDriveByPlan plan,
            out string failure)
        {
            failure = "";
            if (!IsPrepared ||
                !ReferenceEquals(plan, preparedPlan) ||
                !eventReplay.TrySetCarWorldPoseOverride(
                    TryResolveWorldPose))
            {
                failure =
                    "Only the validated LifeSize vehicle plan can be committed.";
                return false;
            }

            committed = true;
            ApplyPresentationScale();
            return true;
        }

        internal void ApplyPresentationScale()
        {
            if (!committed)
                return;

            firstCar?.ApplyRoomPresentation(
                Vector3.zero,
                firstPresentationScale);
            secondCar?.ApplyRoomPresentation(
                Vector3.zero,
                secondPresentationScale);
        }

        internal void Clear()
        {
            if (committed && eventReplay != null)
            {
                eventReplay.ClearCarWorldPoseOverride(
                    TryResolveWorldPose);
            }

            RestoreVehicle(firstCar, firstOriginalParent);
            RestoreVehicle(secondCar, secondOriginalParent);

            committed = false;
            eventReplay = null;
            preparedPlan = null;
            firstDriver = 0;
            secondDriver = 0;
            firstCar = null;
            secondCar = null;
            firstOriginalParent = null;
            secondOriginalParent = null;
            firstPresentationScale = 1f;
            secondPresentationScale = 1f;
        }

        private bool TryValidateDriver(
            int driverNumber,
            out string failure)
        {
            failure = "";
            ShowcasePlaybackWindow timing = preparedPlan.Timing;
            if (!TryResolvePose(
                    driverNumber,
                    timing.StartTime,
                    out _) ||
                !TryResolvePose(
                    driverNumber,
                    timing.FocusTime,
                    out _) ||
                !TryResolvePose(
                    driverNumber,
                    timing.EndTime,
                    out _))
            {
                failure =
                    $"Driver {driverNumber} cannot be evaluated across the LifeSize replay window.";
                return false;
            }

            return true;
        }

        private bool TryResolveWorldPose(
            int driverNumber,
            float replayTime,
            out Pose pose)
        {
            pose = default;
            if (!committed ||
                driverNumber != firstDriver &&
                driverNumber != secondDriver)
            {
                return false;
            }

            return TryResolvePose(
                driverNumber,
                replayTime,
                out pose);
        }

        private bool TryResolvePose(
            int driverNumber,
            float replayTime,
            out Pose pose)
        {
            pose = default;
            return eventReplay != null &&
                preparedPlan != null &&
                eventReplay.TryGetSourceLongitudinalAtTime(
                    driverNumber,
                    replayTime,
                    out float sourceLongitudinal) &&
                preparedPlan.TryEvaluateVehiclePose(
                    sourceLongitudinal,
                    out pose);
        }

        private bool TryResolvePresentationScale(
            ReplayCarView car,
            out float scale)
        {
            scale = 1f;
            car.ClearRoomPresentation();
            float visualLength = car.GetVisualLength();
            if (!float.IsFinite(visualLength) ||
                visualLength <= 0.001f)
            {
                return false;
            }

            scale = preparedPlan.VehicleLength / visualLength;
            return float.IsFinite(scale) && scale > 0f;
        }

        private static void RestoreVehicle(
            ReplayCarView car,
            Transform originalParent)
        {
            if (car == null)
                return;

            car.ClearRoomPresentation();
            Transform root = car.LogicalRoot;
            if (root != null && root.parent != originalParent)
                root.SetParent(originalParent, false);
        }

        private void OnDisable()
        {
            Clear();
        }

        private void OnDestroy()
        {
            Clear();
        }
    }
}
