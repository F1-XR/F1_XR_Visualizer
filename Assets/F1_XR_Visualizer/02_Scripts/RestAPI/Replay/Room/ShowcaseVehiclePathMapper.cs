using System.Collections.Generic;
using F1XR.RestAPI.Api;
using UnityEngine;

namespace F1XR.RestAPI.Replay.Room
{
    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public sealed class ShowcaseVehiclePathMapper : MonoBehaviour
    {
        private const int BindRetryIntervalFrames = 10;
        private const float MaximumCompatibleWallAngle = 55f;
        private const float MinimumRevealMotion = 0.02f;
        private const float MinimumRevealDelay = 0.05f;
        private const float MaximumRevealDelay = 0.25f;

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

        [Header("Presentation")]
        [SerializeField, Min(0f)] private float wallContinuationTarget = 10f;
        [SerializeField, Range(1f, 2f)] private float entryContinuationMultiplier = 1.5f;
        [SerializeField, Min(0f)] private float heroForwardOffset = 0.25f;
        [SerializeField] private float roadFloorOffset = 0.02f;

        [Header("Control")]
        [SerializeField] private bool mappingEnabled = true;

        private readonly List<Vector3> eventLocalPath = new();
        private EventPopoutReplay eventReplay;
        private ShowcasePortalPresentation portalPresentation;
        private Transform boundStage;
        private VehicleBinding firstBinding;
        private VehicleBinding secondBinding;
        private int bindRetryFrames;
        private string bindingState = "WaitingForEvent";
        private string lastFailureReason = "";
        private float sourceReplayProgress;
        private float sourceWindowStart;
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
        private float appliedRoomVehicleLength;
        private float appliedPresentationScale;
        private float eventCoordinateScale;
        private float vehicleLengthBefore;
        private float vehicleLengthAfter;
        private float entryContinuation;
        private float exitContinuation;
        private float heroMissDistance;
        private float entryWallAngle;
        private float exitWallAngle;
        private float portalCrossingMiss;
        private int boundSourceRevision = -1;
        private bool wallPairCompatible;
        private bool stageRevealPending;
        private float stageRevealStartTime;
        private float stageRevealFirstLongitudinal;
        private float stageRevealSecondLongitudinal;

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
        public float AppliedRoomVehicleLength =>
            appliedRoomVehicleLength;
        public float AppliedPresentationScale =>
            appliedPresentationScale;
        public float EventCoordinateScale => eventCoordinateScale;
        public float VehicleLengthBefore => vehicleLengthBefore;
        public float VehicleLengthAfter => vehicleLengthAfter;
        public float EntryContinuation => entryContinuation;
        public float ExitContinuation => exitContinuation;
        public float HeroMissDistance => heroMissDistance;
        public float EntryWallAngle => entryWallAngle;
        public float ExitWallAngle => exitWallAngle;
        public float PortalCrossingMiss => portalCrossingMiss;
        public bool WallPairCompatible => wallPairCompatible;
        public bool PortalsConfigured =>
            portalPresentation != null &&
            portalPresentation.IsConfigured;
        public int AuthoritativePortalVehicleCount =>
            portalPresentation != null
                ? portalPresentation.AuthoritativeVehicleCount
                : 0;
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
            wallContinuationTarget = Mathf.Max(0f, wallContinuationTarget);
            entryContinuationMultiplier = Mathf.Clamp(
                entryContinuationMultiplier,
                1f,
                2f);
            heroForwardOffset = Mathf.Max(0f, heroForwardOffset);

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
                ReleaseBinding(true);
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

            Transform stage = eventReplay.PresentationRoot;
            if (stage == null)
            {
                ReleaseBinding(false);
                SetInactive(
                    "WaitingForStage",
                    "EventReplayStage is unavailable.");
                return;
            }

            if (boundStage != stage ||
                firstBinding == null ||
                !firstBinding.IsValid ||
                secondBinding == null ||
                !secondBinding.IsValid ||
                boundSourceRevision != eventReplay.SourceGeometryRevision)
            {
                ReleaseBinding(false);

                if (showcaseLayout == null)
                {
                    SetInactive(
                        "MissingReference",
                        "Showcase layout reference is unavailable.");
                    return;
                }

                if (!showcaseLayout.IsLayoutValid)
                {
                    SetInactive(
                        "LayoutInvalid",
                        "Showcase layout is invalid.");
                    return;
                }

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
                ReleaseBinding(true);
                SetInactive(
                    "SourceUnavailable",
                    "Current per-vehicle source longitudinal state is unavailable.");
                return;
            }

            anchorMappedProgress = referenceSourceLongitudinal;
            firstMappedProgress = firstSourceLongitudinal;
            secondMappedProgress = secondSourceLongitudinal;

            RevealStageAfterReplayMotion();
            UpdateOrderDiagnostics();
            bindingState = "GlobalEventPlacement";
            lastFailureReason = "";
        }

        private void OnDisable()
        {
            ReleaseBinding(true);
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

            if (portalPresentation == null)
                portalPresentation =
                    GetComponent<ShowcasePortalPresentation>() ??
                    gameObject.AddComponent<ShowcasePortalPresentation>();
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

            if (!eventReplay.TryCopyEventLocalCenterPath(
                    eventLocalPath,
                    out Vector3 overtakePosition,
                    out _))
            {
                SetInactive(
                    "SourceUnavailable",
                    "The event-local source path is unavailable. No room-path fallback was applied.");
                return false;
            }

            if (!TryPlaceEventStage(
                    stage,
                    eventLocalPath,
                    overtakePosition,
                    out string placementFailure))
            {
                SetInactive(
                    "PlacementInvalid",
                    placementFailure);
                return false;
            }

            CaptureVehicleScale(first, second);
            if (!portalPresentation.Configure(
                    stage,
                    showcaseLayout,
                    first.VehicleRoot,
                    second.VehicleRoot,
                    out string portalFailure))
            {
                SetInactive(
                    "PortalInvalid",
                    portalFailure);
                return false;
            }

            boundStage = stage;
            firstBinding = first;
            secondBinding = second;
            boundSourceRevision = eventReplay.SourceGeometryRevision;
            stageRevealPending = true;
            stageRevealStartTime = eventReplay.CurrentTime;
            eventReplay.TryGetSourceLongitudinal(
                first.DriverNumber,
                out stageRevealFirstLongitudinal);
            eventReplay.TryGetSourceLongitudinal(
                second.DriverNumber,
                out stageRevealSecondLongitudinal);
            ResetOrderDiagnostics();
            bindingState = "GlobalEventPlacement";
            lastFailureReason = "";
            Debug.Log(
                $"[RoomEventPlacement] sourcePoints={eventLocalPath.Count}, " +
                $"eventScale={eventCoordinateScale:0.###}, " +
                $"entryContinuation={entryContinuation:0.##}m, " +
                $"exitContinuation={exitContinuation:0.##}m, " +
                $"wallPairCompatible={wallPairCompatible}, " +
                $"wallAngles={entryWallAngle:0.#}/{exitWallAngle:0.#}deg, " +
                $"portalMiss={portalCrossingMiss:0.###}m, " +
                $"heroMiss={heroMissDistance:0.###}m, " +
                $"vehicleLength={vehicleLengthBefore:0.###}m->{vehicleLengthAfter:0.###}m, " +
                $"vehicleScale={appliedPresentationScale:0.#####}, " +
                $"portals={portalPresentation.IsConfigured}",
                this);
            return true;
        }

        private void RevealStageAfterReplayMotion()
        {
            if (!stageRevealPending ||
                boundStage == null ||
                eventReplay == null)
            {
                return;
            }

            float elapsed =
                eventReplay.CurrentTime - stageRevealStartTime;
            bool replayMoved =
                Mathf.Abs(
                    firstSourceLongitudinal -
                    stageRevealFirstLongitudinal) >=
                MinimumRevealMotion ||
                Mathf.Abs(
                    secondSourceLongitudinal -
                    stageRevealSecondLongitudinal) >=
                MinimumRevealMotion;
            if (elapsed < MinimumRevealDelay ||
                !replayMoved && elapsed < MaximumRevealDelay)
            {
                return;
            }

            boundStage.gameObject.SetActive(true);
            stageRevealPending = false;
        }

        private bool TryPlaceEventStage(
            Transform stage,
            IReadOnlyList<Vector3> sourcePath,
            Vector3 overtakePosition,
            out string failure)
        {
            failure = "";
            if (stage == null ||
                sourcePath == null ||
                sourcePath.Count < 2)
            {
                failure = "The event stage or source geometry is unavailable.";
                return false;
            }

            Vector3 roomTravel = Flat(
                showcaseLayout.ExitPose.position -
                showcaseLayout.EntryPose.position);
            if (roomTravel.sqrMagnitude <= 0.000001f)
            {
                failure =
                    "The selected room walls have no stable horizontal travel direction.";
                return false;
            }

            Vector3 heroForward = Flat(
                showcaseLayout.HeroPose.forward);
            if (heroForward.sqrMagnitude <= 0.000001f)
                heroForward = roomTravel.normalized;
            else
                heroForward.Normalize();

            Vector3 overtakeTarget =
                showcaseLayout.HeroPose.position +
                heroForward * heroForwardOffset;
            if (!showcaseLayout.TryGetRoomFloorHeight(
                    out float roomFloorHeight))
            {
                failure =
                    "The selected walls do not provide a stable floor height.";
                return false;
            }

            Vector3 entryInward = Flat(
                showcaseLayout.EntryTravelDirection);
            Vector3 exitInward = Flat(
                -showcaseLayout.ExitTravelDirection);
            if (entryInward.sqrMagnitude <= 0.000001f ||
                exitInward.sqrMagnitude <= 0.000001f)
            {
                failure =
                    "The selected Entry or Exit wall plane has no stable horizontal normal.";
                return false;
            }

            entryInward.Normalize();
            exitInward.Normalize();

            int transitionIndex =
                FindClosestPointIndex(
                    sourcePath,
                    overtakePosition);
            if (transitionIndex <= 0 ||
                transitionIndex >= sourcePath.Count - 1)
            {
                failure =
                    "The ordering-transition point does not leave both an Entry approach and Exit departure.";
                return false;
            }

            float[] cumulativeDistances =
                BuildCumulativeDistances(sourcePath);
            float totalDistance =
                cumulativeDistances[cumulativeDistances.Length - 1];
            float roomSpan = roomTravel.magnitude;
            float entryContinuationTarget =
                wallContinuationTarget *
                Mathf.Max(1f, entryContinuationMultiplier);
            float targetPresentationLength =
                roomSpan + wallContinuationTarget * 2f;
            float resolvedScale =
                targetPresentationLength /
                Mathf.Max(0.0001f, totalDistance);
            if (!float.IsFinite(resolvedScale) ||
                resolvedScale <= 0f ||
                resolvedScale > 10000f)
            {
                failure =
                    "A finite source-relative event scale could not be resolved.";
                return false;
            }

            float bestScore = float.PositiveInfinity;
            float bestScale = 0f;
            int bestEntryIndex = -1;
            int bestExitIndex = -1;
            Vector3 bestPosition = Vector3.zero;
            Quaternion bestRotation = Quaternion.identity;
            float bestEntryAngle = 180f;
            float bestExitAngle = 180f;
            float bestHeroMiss = float.PositiveInfinity;
            float bestPortalMiss = float.PositiveInfinity;

            for (int entryIndex = 0;
                 entryIndex < transitionIndex;
                 entryIndex++)
            {
                for (int exitIndex = transitionIndex + 1;
                     exitIndex < sourcePath.Count;
                     exitIndex++)
                {
                    Vector3 sourceCrossing = Flat(
                        sourcePath[exitIndex] -
                        sourcePath[entryIndex]);
                    float sourceSpan =
                        sourceCrossing.magnitude;
                    if (sourceSpan <= 0.0001f)
                        continue;

                    float scale = resolvedScale;

                    float yaw = Vector3.SignedAngle(
                        sourceCrossing,
                        roomTravel,
                        Vector3.up);
                    Quaternion rotation =
                        Quaternion.Euler(0f, yaw, 0f);
                    Vector3 transformedEntry =
                        rotation *
                        sourcePath[entryIndex] *
                        scale;
                    Vector3 transformedExit =
                        rotation *
                        sourcePath[exitIndex] *
                        scale;
                    Vector3 position =
                        ((showcaseLayout.EntryPose.position -
                          transformedEntry) +
                         (showcaseLayout.ExitPose.position -
                          transformedExit)) *
                        0.5f;

                    position.y =
                        roomFloorHeight +
                        roadFloorOffset -
                        (rotation *
                         overtakePosition *
                         scale).y;

                    Vector3 mappedExit =
                        position +
                        transformedExit;
                    Vector3 mappedEntry =
                        position +
                        transformedEntry;
                    Vector3 mappedOvertake =
                        position +
                        rotation *
                        overtakePosition *
                        scale;
                    float exitMiss = Flat(
                        mappedExit -
                        showcaseLayout.ExitPose.position).magnitude;
                    float entryMiss = Flat(
                        mappedEntry -
                        showcaseLayout.EntryPose.position).magnitude;
                    float candidatePortalMiss =
                        Mathf.Max(entryMiss, exitMiss);
                    float heroMiss = Flat(
                        mappedOvertake -
                        overtakeTarget).magnitude;

                    Vector3 sourceEntryDirection =
                        FindDirectionAt(
                            sourcePath,
                            entryIndex);
                    Vector3 sourceExitDirection =
                        FindDirectionAt(
                            sourcePath,
                            exitIndex);
                    float candidateEntryAngle =
                        Vector3.Angle(
                            Flat(rotation *
                                 sourceEntryDirection),
                            Flat(showcaseLayout.EntryTravelDirection));
                    float candidateExitAngle =
                        Vector3.Angle(
                            Flat(rotation *
                                 sourceExitDirection),
                            Flat(showcaseLayout.ExitTravelDirection));
                    float candidateEntryContinuation =
                        cumulativeDistances[entryIndex] *
                        scale;
                    float candidateExitContinuation =
                        (totalDistance -
                         cumulativeDistances[exitIndex]) *
                        scale;
                    float continuationShortfall =
                        Mathf.Max(
                            0f,
                            entryContinuationTarget -
                            candidateEntryContinuation) +
                        Mathf.Max(
                            0f,
                            wallContinuationTarget -
                            candidateExitContinuation);
                    float directionPenalty =
                        (candidateEntryAngle +
                         candidateExitAngle) /
                        180f *
                        roomSpan;
                    float score =
                        heroMiss * 6f +
                        (entryMiss + exitMiss) * 6f +
                        directionPenalty +
                        continuationShortfall;
                    if (score >= bestScore)
                        continue;

                    bestScore = score;
                    bestScale = scale;
                    bestEntryIndex = entryIndex;
                    bestExitIndex = exitIndex;
                    bestPosition = position;
                    bestRotation = rotation;
                    bestEntryAngle =
                        candidateEntryAngle;
                    bestExitAngle =
                        candidateExitAngle;
                    bestHeroMiss = heroMiss;
                    bestPortalMiss =
                        candidatePortalMiss;
                }
            }

            if (bestEntryIndex < 0 ||
                bestExitIndex < 0 ||
                bestScale <= 0f)
            {
                failure =
                    "A finite rigid wall-room-wall placement could not be resolved.";
                return false;
            }

            eventCoordinateScale = bestScale;
            if (!eventReplay.TrySetPresentationPose(
                    bestPosition,
                    bestRotation,
                    eventCoordinateScale))
            {
                failure = "The EventReplayStage rejected the global placement.";
                return false;
            }
            if (!eventReplay.TryConfigureRoomStageInteraction(
                    overtakePosition))
            {
                failure = "The EventReplayStage rejected the interaction focus.";
                return false;
            }

            entryContinuation =
                cumulativeDistances[bestEntryIndex] *
                eventCoordinateScale;
            exitContinuation =
                (totalDistance -
                 cumulativeDistances[bestExitIndex]) *
                eventCoordinateScale;
            heroMissDistance = bestHeroMiss;
            entryWallAngle = bestEntryAngle;
            exitWallAngle = bestExitAngle;
            portalCrossingMiss = bestPortalMiss;
            wallPairCompatible =
                entryContinuation + 0.01f >=
                entryContinuationTarget &&
                exitContinuation + 0.01f >= wallContinuationTarget &&
                entryWallAngle <= MaximumCompatibleWallAngle &&
                exitWallAngle <= MaximumCompatibleWallAngle &&
                portalCrossingMiss <= 0.75f &&
                heroMissDistance <= Mathf.Max(0.75f, roomSpan * 0.35f);
            return true;
        }

        private static Quaternion ResolvePlacementRotation(
            Vector3 sourceTravel,
            Vector3 roomTravel,
            Vector3 sourceEntryDirection,
            Vector3 entryDirection,
            Vector3 sourceExitDirection,
            Vector3 exitDirection)
        {
            float sine = 0f;
            float cosine = 0f;
            AddYawCandidate(
                sourceTravel,
                roomTravel,
                2f,
                ref sine,
                ref cosine);
            AddYawCandidate(
                sourceEntryDirection,
                entryDirection,
                1f,
                ref sine,
                ref cosine);
            AddYawCandidate(
                sourceExitDirection,
                exitDirection,
                1f,
                ref sine,
                ref cosine);

            float yaw = Mathf.Atan2(sine, cosine) * Mathf.Rad2Deg;
            return Quaternion.Euler(0f, yaw, 0f);
        }

        private static void AddYawCandidate(
            Vector3 source,
            Vector3 target,
            float weight,
            ref float sine,
            ref float cosine)
        {
            source = Flat(source);
            target = Flat(target);
            if (source.sqrMagnitude <= 0.000001f ||
                target.sqrMagnitude <= 0.000001f)
            {
                return;
            }

            float angle =
                Vector3.SignedAngle(source, target, Vector3.up) *
                Mathf.Deg2Rad;
            sine += Mathf.Sin(angle) * weight;
            cosine += Mathf.Cos(angle) * weight;
        }

        private static Vector3 FindEndDirection(
            IReadOnlyList<Vector3> path,
            bool fromStart)
        {
            int start = fromStart ? 0 : path.Count - 1;
            int step = fromStart ? 1 : -1;
            Vector3 origin = path[start];
            for (int offset = 1; offset < path.Count; offset++)
            {
                Vector3 direction =
                    path[start + offset * step] - origin;
                if (!fromStart)
                    direction = -direction;
                direction = Flat(direction);
                if (direction.sqrMagnitude > 0.000001f)
                    return direction.normalized;
            }

            return Vector3.zero;
        }

        private static Vector3 Flat(Vector3 value)
        {
            value.y = 0f;
            return value;
        }

        private static int FindClosestPointIndex(
            IReadOnlyList<Vector3> path,
            Vector3 target)
        {
            int closest = -1;
            float closestDistance = float.PositiveInfinity;
            for (int i = 0; i < path.Count; i++)
            {
                float distance =
                    (path[i] - target).sqrMagnitude;
                if (distance >= closestDistance)
                    continue;

                closestDistance = distance;
                closest = i;
            }

            return closest;
        }

        private static float[] BuildCumulativeDistances(
            IReadOnlyList<Vector3> path)
        {
            float[] distances = new float[path.Count];
            for (int i = 1; i < path.Count; i++)
            {
                distances[i] =
                    distances[i - 1] +
                    Vector3.Distance(
                        path[i - 1],
                        path[i]);
            }

            return distances;
        }

        private static Vector3 FindDirectionAt(
            IReadOnlyList<Vector3> path,
            int index)
        {
            int before = Mathf.Max(0, index - 1);
            int after = Mathf.Min(
                path.Count - 1,
                index + 1);
            Vector3 direction =
                Flat(path[after] - path[before]);
            return direction.sqrMagnitude > 0.000001f
                ? direction.normalized
                : Vector3.forward;
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

            binding = new VehicleBinding(
                driverNumber,
                logicalRoot,
                visualRoot);
            return true;
        }

        private void CaptureVehicleScale(
            VehicleBinding first,
            VehicleBinding second)
        {
            float firstLength = MeasureWorldVisualLength(first);
            float secondLength = MeasureWorldVisualLength(second);
            float sourceLength = Mathf.Max(firstLength, secondLength);
            vehicleLengthBefore = sourceLength;
            vehicleLengthAfter = sourceLength;
            appliedRoomVehicleLength = sourceLength;
            appliedPresentationScale = 1f;
        }

        private static float MeasureWorldVisualLength(
            VehicleBinding binding)
        {
            ReplayCarView car =
                binding.VisualMotionRoot.GetComponent<ReplayCarView>();
            float visualLength =
                car != null ? car.GetVisualLength() : 0f;
            float parentScale = MaxAxis(
                binding.VehicleRoot.lossyScale);
            return visualLength * parentScale;
        }

        private static float MaxAxis(Vector3 value)
        {
            return Mathf.Max(
                Mathf.Abs(value.x),
                Mathf.Abs(value.y),
                Mathf.Abs(value.z));
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

        private void ReleaseBinding(bool restoreStage = false)
        {
            Transform stageToRestore = boundStage;
            portalPresentation?.Clear();

            boundStage = null;
            firstBinding = null;
            secondBinding = null;
            sourceWindowLength = 0f;
            sourceWindowStart = 0f;
            appliedRoomVehicleLength = 0f;
            appliedPresentationScale = 0f;
            eventCoordinateScale = 0f;
            vehicleLengthBefore = 0f;
            vehicleLengthAfter = 0f;
            entryContinuation = 0f;
            exitContinuation = 0f;
            heroMissDistance = 0f;
            entryWallAngle = 0f;
            exitWallAngle = 0f;
            portalCrossingMiss = 0f;
            boundSourceRevision = -1;
            wallPairCompatible = false;
            stageRevealPending = false;
            stageRevealStartTime = 0f;
            stageRevealFirstLongitudinal = 0f;
            stageRevealSecondLongitudinal = 0f;
            eventLocalPath.Clear();
            isApplyingRoomPoses = false;
            ResetOrderDiagnostics();

            if (restoreStage &&
                stageToRestore != null &&
                eventReplay != null &&
                eventReplay.PresentationRoot == stageToRestore)
            {
                eventReplay.TryRestoreTableRelativePose();
                stageToRestore.gameObject.SetActive(true);
            }
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
            public bool IsValid =>
                VehicleRoot != null &&
                VisualMotionRoot != null &&
                OriginalVisualParent != null &&
                VisualMotionRoot.parent == OriginalVisualParent;

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
            }
        }
    }
}
