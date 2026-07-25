using F1XR.RestAPI.Api;
using UnityEngine;

namespace F1XR.RestAPI.Replay.Room
{
    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public sealed class ShowcaseVehiclePathMapper : MonoBehaviour
    {
        private const int BindRetryIntervalFrames = 10;

        [Header("Sources")]
        [SerializeField] private ShowcasePathPreview showcasePath;
        [SerializeField] private ShowcaseLayout showcaseLayout;
        [SerializeField] private ReplayPlayer replayPlayer;

        [Header("Target")]
        [SerializeField, Min(0)] private int targetDriverNumber;

        [Header("Progress Mapping")]
        [SerializeField, Range(0f, 1f)] private float sourceProgressStart;
        [SerializeField, Range(0f, 1f)] private float sourceProgressEnd = 1f;
        [SerializeField, Range(0f, 1f)] private float mappedPathStart;
        [SerializeField, Range(0f, 1f)] private float mappedPathEnd = 1f;

        [Header("Orientation")]
        [SerializeField] private float modelHeadingCorrection;

        [Header("Control")]
        [SerializeField] private bool mappingEnabled = true;

        private EventPopoutReplay eventReplay;
        private Transform boundStage;
        private Transform vehicleRoot;
        private Transform visualMotionRoot;
        private Transform originalVisualParent;
        private Transform roomPathPresentationRoot;
        private int bindRetryFrames;
        private int resolvedDriverNumber;
        private string bindingState = "WaitingForEvent";
        private string lastFailureReason = "";
        private float sourceReplayProgress;
        private float mappedPathProgress;
        private bool isApplyingRoomPose;

        public bool TargetVehicleResolved =>
            vehicleRoot != null &&
            visualMotionRoot != null &&
            roomPathPresentationRoot != null;
        public int TargetDriverId => resolvedDriverNumber;
        public string BoundVehicleName =>
            vehicleRoot != null ? vehicleRoot.name : "";
        public string BindingState => bindingState;
        public float SourceReplayProgress => sourceReplayProgress;
        public float MappedPathProgress => mappedPathProgress;
        public bool IsApplyingRoomPose => isApplyingRoomPose;
        public string LastFailureReason => lastFailureReason;

        private void Reset()
        {
            ResolveLocalReferences();
        }

        private void Awake()
        {
            ResolveLocalReferences();
        }

        private void OnValidate()
        {
            sourceProgressStart = Mathf.Clamp01(sourceProgressStart);
            sourceProgressEnd = Mathf.Clamp01(sourceProgressEnd);
            mappedPathStart = Mathf.Clamp01(mappedPathStart);
            mappedPathEnd = Mathf.Clamp01(mappedPathEnd);

            if (sourceProgressEnd <= sourceProgressStart)
            {
                sourceProgressEnd = Mathf.Min(
                    1f,
                    sourceProgressStart + 0.01f);
                sourceProgressStart = Mathf.Min(
                    sourceProgressStart,
                    sourceProgressEnd - 0.01f);
            }

            if (mappedPathEnd < mappedPathStart)
                mappedPathEnd = mappedPathStart;
        }

        private void LateUpdate()
        {
            isApplyingRoomPose = false;
            ResolveEventReplay();

            if (!mappingEnabled)
            {
                ReleaseBinding();
                SetInactive("Disabled", "");
                return;
            }

            if (eventReplay == null || !eventReplay.IsActive)
            {
                ReleaseBinding();
                SetInactive(
                    "WaitingForEvent",
                    eventReplay == null
                        ? "Replay event controller is unavailable."
                        : "");
                return;
            }

            sourceReplayProgress = Mathf.Clamp01(
                eventReplay.NormalizedTime);

            if (showcasePath == null || showcaseLayout == null)
            {
                ReleaseBinding();
                SetInactive(
                    "MissingReference",
                    "Showcase path or layout reference is unavailable.");
                return;
            }

            if (!showcaseLayout.IsLayoutValid ||
                !showcasePath.IsPathValid)
            {
                ReleaseBinding();
                SetInactive(
                    "PathInvalid",
                    "Showcase layout or path is invalid.");
                return;
            }

            Transform stage = eventReplay.PresentationRoot;
            if (stage == null)
            {
                ReleaseBinding();
                SetInactive(
                    "WaitingForStage",
                    "EventReplayStage is unavailable.");
                return;
            }

            if (boundStage != stage ||
                vehicleRoot == null ||
                visualMotionRoot == null ||
                roomPathPresentationRoot == null)
            {
                ReleaseBinding();

                if (bindRetryFrames > 0)
                {
                    bindRetryFrames--;
                    SetInactive("WaitingForVehicle", lastFailureReason);
                    return;
                }

                bindRetryFrames = BindRetryIntervalFrames;
                if (!TryBind(stage))
                    return;
            }

            float sourceRangeProgress = Mathf.InverseLerp(
                sourceProgressStart,
                sourceProgressEnd,
                sourceReplayProgress);
            mappedPathProgress = Mathf.Lerp(
                mappedPathStart,
                mappedPathEnd,
                sourceRangeProgress);

            if (!showcasePath.TryEvaluate(
                    mappedPathProgress,
                    out Pose pathPose))
            {
                ReleaseBinding();
                SetInactive(
                    "PathInvalid",
                    "Showcase path evaluation failed.");
                return;
            }

            Quaternion rotation =
                pathPose.rotation *
                Quaternion.Euler(0f, modelHeadingCorrection, 0f);
            roomPathPresentationRoot.SetPositionAndRotation(
                pathPose.position,
                rotation);

            bindingState = "Applying";
            lastFailureReason = "";
            isApplyingRoomPose = true;
        }

        private void OnDisable()
        {
            ReleaseBinding();
            isApplyingRoomPose = false;
            bindingState = "Disabled";
        }

        private void OnDestroy()
        {
            ReleaseBinding();
        }

        private void ResolveLocalReferences()
        {
            if (showcasePath == null)
                showcasePath = GetComponent<ShowcasePathPreview>();

            if (showcaseLayout == null)
                showcaseLayout = GetComponent<ShowcaseLayout>();
        }

        private void ResolveEventReplay()
        {
            EventPopoutReplay current = replayPlayer != null
                ? replayPlayer.EventReplay
                : null;

            if (eventReplay == current)
                return;

            ReleaseBinding();
            eventReplay = current;
            bindRetryFrames = 0;
        }

        private bool TryBind(Transform stage)
        {
            int driverNumber = ResolveTargetDriver();
            if (driverNumber <= 0)
            {
                SetInactive(
                    "WaitingForVehicle",
                    "The active replay event has no valid target driver.");
                return false;
            }

            Transform carsRoot = stage.Find("Cars");
            if (carsRoot == null)
            {
                SetInactive(
                    "WaitingForVehicle",
                    "EventReplayStage/Cars is unavailable.");
                return false;
            }

            Transform logicalRoot =
                carsRoot.Find($"Car_{driverNumber}");
            if (logicalRoot == null)
            {
                SetInactive(
                    "WaitingForVehicle",
                    $"Event vehicle Car_{driverNumber} is unavailable.");
                return false;
            }

            Transform visualRoot = logicalRoot.Find("VisualMotionRoot");
            if (visualRoot == null)
            {
                SetInactive(
                    "WaitingForVehicle",
                    $"Car_{driverNumber}/VisualMotionRoot is unavailable.");
                return false;
            }

            GameObject presentationObject =
                new($"RoomPathPresentationRoot_{driverNumber}");
            Transform presentation = presentationObject.transform;
            presentation.SetParent(logicalRoot, false);

            Vector3 visualLocalPosition = visualRoot.localPosition;
            Quaternion visualLocalRotation = visualRoot.localRotation;
            Vector3 visualLocalScale = visualRoot.localScale;
            visualRoot.SetParent(presentation, false);
            visualRoot.localPosition = visualLocalPosition;
            visualRoot.localRotation = visualLocalRotation;
            visualRoot.localScale = visualLocalScale;

            boundStage = stage;
            vehicleRoot = logicalRoot;
            visualMotionRoot = visualRoot;
            originalVisualParent = logicalRoot;
            roomPathPresentationRoot = presentation;
            resolvedDriverNumber = driverNumber;
            bindingState = "Bound";
            lastFailureReason = "";
            return true;
        }

        private int ResolveTargetDriver()
        {
            ReplayEventDto replayEvent = eventReplay.CurrentEvent;
            int[] drivers = replayEvent != null
                ? replayEvent.driverNumbers
                : null;
            if (drivers == null || drivers.Length == 0)
                return 0;

            if (targetDriverNumber <= 0)
            {
                for (int i = 0; i < drivers.Length; i++)
                {
                    if (drivers[i] > 0)
                        return drivers[i];
                }

                return 0;
            }

            for (int i = 0; i < drivers.Length; i++)
            {
                if (drivers[i] == targetDriverNumber)
                    return targetDriverNumber;
            }

            return 0;
        }

        private void ReleaseBinding()
        {
            if (visualMotionRoot != null &&
                originalVisualParent != null &&
                visualMotionRoot.parent == roomPathPresentationRoot)
            {
                Vector3 localPosition = visualMotionRoot.localPosition;
                Quaternion localRotation = visualMotionRoot.localRotation;
                Vector3 localScale = visualMotionRoot.localScale;
                visualMotionRoot.SetParent(originalVisualParent, false);
                visualMotionRoot.localPosition = localPosition;
                visualMotionRoot.localRotation = localRotation;
                visualMotionRoot.localScale = localScale;
            }

            if (roomPathPresentationRoot != null)
                Destroy(roomPathPresentationRoot.gameObject);

            boundStage = null;
            vehicleRoot = null;
            visualMotionRoot = null;
            originalVisualParent = null;
            roomPathPresentationRoot = null;
            resolvedDriverNumber = 0;
            isApplyingRoomPose = false;
        }

        private void SetInactive(string state, string failure)
        {
            bindingState = state;
            lastFailureReason = failure;
        }
    }
}
