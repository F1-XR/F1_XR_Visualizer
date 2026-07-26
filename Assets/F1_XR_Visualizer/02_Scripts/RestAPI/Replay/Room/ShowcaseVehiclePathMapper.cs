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
        [SerializeField, Min(0)] private int secondTargetDriverNumber;

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
        private VehicleBinding firstBinding;
        private VehicleBinding secondBinding;
        private int bindRetryFrames;
        private string bindingState = "WaitingForEvent";
        private string lastFailureReason = "";
        private float sourceReplayProgress;
        private float sourceWindowLength;
        private float referenceSourceLongitudinal;
        private float anchorMappedProgress;
        private float firstSourceLongitudinal;
        private float secondSourceLongitudinal;
        private float firstMappedProgress;
        private float secondMappedProgress;
        private float sourceLongitudinalGap;
        private float mappedLongitudinalGap;
        private int sourceOrder;
        private int mappedOrder;
        private int previousSourceOrder;
        private int previousMappedOrder;
        private int sourceOrderTransitionCount;
        private int mappedOrderTransitionCount;
        private bool overtakeTransitionDetected;
        private bool isApplyingRoomPoses;

        public bool TargetVehicleResolved =>
            firstBinding != null &&
            firstBinding.IsValid;
        public int TargetDriverId => FirstTargetDriverId;
        public string BoundVehicleName =>
            firstBinding != null && firstBinding.VehicleRoot != null
                ? firstBinding.VehicleRoot.name
                : "";
        public string BindingState => bindingState;
        public float SourceReplayProgress => sourceReplayProgress;
        public float MappedPathProgress => firstMappedProgress;
        public bool IsApplyingRoomPose => isApplyingRoomPoses;
        public string LastFailureReason => lastFailureReason;
        public int BoundVehicleCount =>
            firstBinding != null && firstBinding.IsValid &&
            secondBinding != null && secondBinding.IsValid
                ? 2
                : 0;
        public int FirstTargetDriverId =>
            firstBinding != null ? firstBinding.DriverNumber : 0;
        public int SecondTargetDriverId =>
            secondBinding != null ? secondBinding.DriverNumber : 0;
        public float FirstSourceLongitudinal => firstSourceLongitudinal;
        public float SecondSourceLongitudinal => secondSourceLongitudinal;
        public float ReferenceSourceLongitudinal =>
            referenceSourceLongitudinal;
        public float AnchorMappedProgress => anchorMappedProgress;
        public float FirstMappedProgress => firstMappedProgress;
        public float SecondMappedProgress => secondMappedProgress;
        public float SourceLongitudinalGap => sourceLongitudinalGap;
        public float MappedLongitudinalGap => mappedLongitudinalGap;
        public int SourceOrder => sourceOrder;
        public int MappedOrder => mappedOrder;
        public bool OvertakeTransitionDetected =>
            overtakeTransitionDetected;
        public int SourceOrderTransitionCount =>
            sourceOrderTransitionCount;
        public int MappedOrderTransitionCount =>
            mappedOrderTransitionCount;
        public bool IsApplyingRoomPoses => isApplyingRoomPoses;
        public Vector3 FirstVisualLocalPosition =>
            firstBinding != null && firstBinding.VisualMotionRoot != null
                ? firstBinding.VisualMotionRoot.localPosition
                : Vector3.zero;
        public Vector3 SecondVisualLocalPosition =>
            secondBinding != null && secondBinding.VisualMotionRoot != null
                ? secondBinding.VisualMotionRoot.localPosition
                : Vector3.zero;
        public Quaternion FirstVisualLocalRotation =>
            firstBinding != null && firstBinding.VisualMotionRoot != null
                ? firstBinding.VisualMotionRoot.localRotation
                : Quaternion.identity;
        public Quaternion SecondVisualLocalRotation =>
            secondBinding != null && secondBinding.VisualMotionRoot != null
                ? secondBinding.VisualMotionRoot.localRotation
                : Quaternion.identity;

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

            if (mappedPathEnd <= mappedPathStart)
            {
                mappedPathEnd = Mathf.Min(
                    1f,
                    mappedPathStart + 0.01f);
                mappedPathStart = Mathf.Min(
                    mappedPathStart,
                    mappedPathEnd - 0.01f);
            }
        }

        private void LateUpdate()
        {
            isApplyingRoomPoses = false;
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
                firstBinding == null ||
                !firstBinding.IsValid ||
                secondBinding == null ||
                !secondBinding.IsValid)
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

            if (!eventReplay.TryGetSourceLongitudinal(
                    firstBinding.DriverNumber,
                    out firstSourceLongitudinal) ||
                !eventReplay.TryGetSourceLongitudinal(
                    secondBinding.DriverNumber,
                    out secondSourceLongitudinal) ||
                !eventReplay.TryGetReferenceSourceLongitudinal(
                    sourceReplayProgress,
                    out referenceSourceLongitudinal))
            {
                ReleaseBinding();
                SetInactive(
                    "SourceUnavailable",
                    "Current per-vehicle source longitudinal state is unavailable.");
                return;
            }

            float mappedRange = mappedPathEnd - mappedPathStart;
            float sourceRangeProgress = Mathf.InverseLerp(
                sourceProgressStart,
                sourceProgressEnd,
                sourceReplayProgress);
            anchorMappedProgress = Mathf.Lerp(
                mappedPathStart,
                mappedPathEnd,
                sourceRangeProgress);
            firstMappedProgress =
                anchorMappedProgress +
                (firstSourceLongitudinal - referenceSourceLongitudinal) /
                sourceWindowLength *
                mappedRange;
            secondMappedProgress =
                anchorMappedProgress +
                (secondSourceLongitudinal - referenceSourceLongitudinal) /
                sourceWindowLength *
                mappedRange;

            UpdateOrderDiagnostics();

            if (!TryApplyRoomPose(
                    firstBinding,
                    firstMappedProgress) ||
                !TryApplyRoomPose(
                    secondBinding,
                    secondMappedProgress))
            {
                ReleaseBinding();
                SetInactive(
                    "PathInvalid",
                    "Showcase path evaluation failed.");
                return;
            }

            bindingState = "Applying";
            lastFailureReason = "";
            isApplyingRoomPoses =
                firstBinding.PresentationRoot.gameObject.activeSelf ||
                secondBinding.PresentationRoot.gameObject.activeSelf;
        }

        private void OnDisable()
        {
            ReleaseBinding();
            isApplyingRoomPoses = false;
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
            if (!TryResolveTargetDrivers(
                    out int firstDriver,
                    out int secondDriver))
            {
                SetInactive(
                    "WaitingForVehicle",
                    "The active replay event has fewer than two valid target drivers.");
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

            if (!TryResolveVehicle(
                    carsRoot,
                    firstDriver,
                    out VehicleBinding first) ||
                !TryResolveVehicle(
                    carsRoot,
                    secondDriver,
                    out VehicleBinding second))
            {
                return false;
            }

            if (!eventReplay.TryGetReferenceSourceLongitudinal(
                    sourceProgressStart,
                    out float windowStart) ||
                !eventReplay.TryGetReferenceSourceLongitudinal(
                    sourceProgressEnd,
                    out float windowEnd) ||
                windowEnd - windowStart <= 0.0001f)
            {
                SetInactive(
                    "SourceUnavailable",
                    "The shared source longitudinal window is unavailable.");
                return false;
            }

            CreatePresentationRoot(first);
            CreatePresentationRoot(second);

            boundStage = stage;
            firstBinding = first;
            secondBinding = second;
            sourceWindowLength = windowEnd - windowStart;
            ResetOrderDiagnostics();
            bindingState = "Bound";
            lastFailureReason = "";
            return true;
        }

        private bool TryResolveTargetDrivers(
            out int firstDriver,
            out int secondDriver)
        {
            firstDriver = 0;
            secondDriver = 0;
            ReplayEventDto replayEvent = eventReplay.CurrentEvent;
            int[] drivers = replayEvent != null
                ? replayEvent.driverNumbers
                : null;
            if (drivers == null || drivers.Length < 2)
                return false;

            firstDriver = ResolveConfiguredDriver(
                drivers,
                targetDriverNumber,
                0);
            if (firstDriver <= 0)
                return false;

            secondDriver = ResolveConfiguredDriver(
                drivers,
                secondTargetDriverNumber,
                firstDriver);
            return secondDriver > 0 && secondDriver != firstDriver;
        }

        private static int ResolveConfiguredDriver(
            int[] drivers,
            int configuredDriver,
            int excludedDriver)
        {
            if (configuredDriver > 0)
            {
                for (int i = 0; i < drivers.Length; i++)
                {
                    if (drivers[i] == configuredDriver &&
                        drivers[i] != excludedDriver)
                    {
                        return configuredDriver;
                    }
                }

                return 0;
            }

            for (int i = 0; i < drivers.Length; i++)
            {
                if (drivers[i] > 0 && drivers[i] != excludedDriver)
                    return drivers[i];
            }

            return 0;
        }

        private bool TryResolveVehicle(
            Transform carsRoot,
            int driverNumber,
            out VehicleBinding binding)
        {
            binding = null;
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

            if (logicalRoot.Find("RoomPathPresentationRoot") != null)
            {
                SetInactive(
                    "WaitingForVehicle",
                    $"Car_{driverNumber} already has a room presentation root.");
                return false;
            }

            binding = new VehicleBinding(
                driverNumber,
                logicalRoot,
                visualRoot);
            return true;
        }

        private static void CreatePresentationRoot(
            VehicleBinding binding)
        {
            GameObject presentationObject =
                new("RoomPathPresentationRoot");
            Transform presentation = presentationObject.transform;
            presentation.SetParent(binding.VehicleRoot, false);

            Transform visualRoot = binding.VisualMotionRoot;
            Vector3 localPosition = visualRoot.localPosition;
            Quaternion localRotation = visualRoot.localRotation;
            Vector3 localScale = visualRoot.localScale;
            visualRoot.SetParent(presentation, false);
            visualRoot.localPosition = localPosition;
            visualRoot.localRotation = localRotation;
            visualRoot.localScale = localScale;
            binding.PresentationRoot = presentation;
        }

        private bool TryApplyRoomPose(
            VehicleBinding binding,
            float progress)
        {
            bool visible =
                progress >= mappedPathStart &&
                progress <= mappedPathEnd;
            GameObject presentationObject =
                binding.PresentationRoot.gameObject;
            if (!visible)
            {
                if (presentationObject.activeSelf)
                    presentationObject.SetActive(false);
                return true;
            }

            if (!presentationObject.activeSelf)
                presentationObject.SetActive(true);

            if (!showcasePath.TryEvaluate(
                    progress,
                    out Pose pathPose))
            {
                return false;
            }

            Quaternion rotation =
                pathPose.rotation *
                Quaternion.Euler(0f, modelHeadingCorrection, 0f);
            binding.PresentationRoot.SetPositionAndRotation(
                pathPose.position,
                rotation);
            return true;
        }

        private void UpdateOrderDiagnostics()
        {
            sourceLongitudinalGap =
                firstSourceLongitudinal -
                secondSourceLongitudinal;
            mappedLongitudinalGap =
                firstMappedProgress -
                secondMappedProgress;
            sourceOrder = GapOrder(sourceLongitudinalGap);
            mappedOrder = GapOrder(mappedLongitudinalGap);

            if (sourceOrder != 0)
            {
                if (previousSourceOrder != 0 &&
                    sourceOrder != previousSourceOrder)
                {
                    sourceOrderTransitionCount++;
                    overtakeTransitionDetected = true;
                }

                previousSourceOrder = sourceOrder;
            }

            if (mappedOrder != 0)
            {
                if (previousMappedOrder != 0 &&
                    mappedOrder != previousMappedOrder)
                {
                    mappedOrderTransitionCount++;
                }

                previousMappedOrder = mappedOrder;
            }
        }

        private static int GapOrder(float gap)
        {
            if (gap > 0.0001f)
                return 1;
            if (gap < -0.0001f)
                return -1;
            return 0;
        }

        private void ResetOrderDiagnostics()
        {
            firstSourceLongitudinal = 0f;
            secondSourceLongitudinal = 0f;
            referenceSourceLongitudinal = 0f;
            anchorMappedProgress = 0f;
            firstMappedProgress = 0f;
            secondMappedProgress = 0f;
            sourceLongitudinalGap = 0f;
            mappedLongitudinalGap = 0f;
            sourceOrder = 0;
            mappedOrder = 0;
            previousSourceOrder = 0;
            previousMappedOrder = 0;
            sourceOrderTransitionCount = 0;
            mappedOrderTransitionCount = 0;
            overtakeTransitionDetected = false;
        }

        private void ReleaseBinding()
        {
            ReleaseBinding(firstBinding);
            ReleaseBinding(secondBinding);

            boundStage = null;
            firstBinding = null;
            secondBinding = null;
            sourceWindowLength = 0f;
            isApplyingRoomPoses = false;
            ResetOrderDiagnostics();
        }

        private static void ReleaseBinding(
            VehicleBinding binding)
        {
            if (binding == null)
                return;

            Transform visualRoot = binding.VisualMotionRoot;
            Transform presentationRoot = binding.PresentationRoot;
            if (visualRoot != null &&
                binding.OriginalVisualParent != null &&
                visualRoot.parent == presentationRoot)
            {
                Vector3 localPosition = visualRoot.localPosition;
                Quaternion localRotation = visualRoot.localRotation;
                Vector3 localScale = visualRoot.localScale;
                visualRoot.SetParent(
                    binding.OriginalVisualParent,
                    false);
                visualRoot.localPosition = localPosition;
                visualRoot.localRotation = localRotation;
                visualRoot.localScale = localScale;
            }

            if (presentationRoot != null)
                Destroy(presentationRoot.gameObject);
        }

        private void SetInactive(string state, string failure)
        {
            bindingState = state;
            lastFailureReason = failure;
        }

        private sealed class VehicleBinding
        {
            public readonly int DriverNumber;
            public readonly Transform VehicleRoot;
            public readonly Transform VisualMotionRoot;
            public readonly Transform OriginalVisualParent;
            public readonly Vector3 OriginalVisualLocalPosition;
            public readonly Quaternion OriginalVisualLocalRotation;
            public readonly Vector3 OriginalVisualLocalScale;
            public Transform PresentationRoot;

            public bool IsValid =>
                VehicleRoot != null &&
                VisualMotionRoot != null &&
                OriginalVisualParent != null &&
                PresentationRoot != null &&
                VisualMotionRoot.parent == PresentationRoot;

            public VehicleBinding(
                int driverNumber,
                Transform vehicleRoot,
                Transform visualMotionRoot)
            {
                DriverNumber = driverNumber;
                VehicleRoot = vehicleRoot;
                VisualMotionRoot = visualMotionRoot;
                OriginalVisualParent = visualMotionRoot.parent;
                OriginalVisualLocalPosition =
                    visualMotionRoot.localPosition;
                OriginalVisualLocalRotation =
                    visualMotionRoot.localRotation;
                OriginalVisualLocalScale =
                    visualMotionRoot.localScale;
                PresentationRoot = null;
            }
        }
    }
}
