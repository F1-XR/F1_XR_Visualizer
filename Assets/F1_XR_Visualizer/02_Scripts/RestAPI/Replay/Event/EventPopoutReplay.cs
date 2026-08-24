using System;
using System.Collections;
using System.Collections.Generic;
using F1XR.Interaction.World;
using F1XR.RestAPI.Api;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace F1XR.RestAPI.Replay
{
    internal readonly struct ShowcasePlaybackWindow
    {
        public ShowcasePlaybackWindow(
            float portalVisualStartTime,
            float startTime,
            float entryTime,
            float focusTime,
            float exitTime,
            float endTime,
            float portalVisualEndTime)
        {
            PortalVisualStartTime = portalVisualStartTime;
            StartTime = startTime;
            EntryTime = entryTime;
            FocusTime = focusTime;
            ExitTime = exitTime;
            EndTime = endTime;
            PortalVisualEndTime = portalVisualEndTime;
        }

        public float PortalVisualStartTime { get; }
        public float StartTime { get; }
        public float EntryTime { get; }
        public float FocusTime { get; }
        public float ExitTime { get; }
        public float EndTime { get; }
        public float PortalVisualEndTime { get; }
        public bool IsValid =>
            PortalVisualStartTime <= StartTime &&
            EndTime > StartTime &&
            EntryTime >= StartTime &&
            FocusTime > EntryTime &&
            ExitTime > FocusTime &&
            ExitTime <= EndTime &&
            PortalVisualEndTime >= EndTime;
    }

    internal readonly struct ShowcaseActionBeat
    {
        public ShowcaseActionBeat(
            float time,
            float startTime,
            float endTime,
            float confirmedTime,
            int overtaker,
            int defender,
            OvertakeBattleExchangeKind kind,
            Vector3 eventLocalPosition)
        {
            Time = time;
            StartTime = startTime;
            EndTime = endTime;
            ConfirmedTime = confirmedTime;
            Overtaker = overtaker;
            Defender = defender;
            Kind = kind;
            EventLocalPosition = eventLocalPosition;
        }

        public float Time { get; }
        public float StartTime { get; }
        public float EndTime { get; }
        public float ConfirmedTime { get; }
        public int Overtaker { get; }
        public int Defender { get; }
        public OvertakeBattleExchangeKind Kind { get; }
        public Vector3 EventLocalPosition { get; }
    }

    public sealed partial class EventPopoutReplay : MonoBehaviour
    {
        private const int MaxEventDrivers = 4;
        private const float RelativeTrackRegionPadding = 0.12f;
        private const float MinimumTrackRegionPadding = 0.00005f;
        private const float MaximumConventionalPitLaneSeconds = 120f;
        private const float MinimumRedFlagPitOverlapSeconds = 5f;
        private const string PitShowcaseAssetResourcePath =
            "PitShowcase/PitShowcaseAssets";

        [Header("Development")]
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public bool allowDevelopmentFallbackEvent;
#else
        public bool allowDevelopmentFallbackEvent;
#endif

        [Header("Stage")]
        public Transform stageAnchor;
        [Min(0.1f)] public float stageScale = 3f;
        [Min(0.1f)] public float stageSideOffset = 0.9f;
        [Min(0.1f)] public float stageForwardDistance = 1.1f;
        public float stageHeightOffset = 0.25f;
        [Min(0.001f)] public float trackRegionPadding = 0.04f;
        [Min(0.001f)] public float roadWidth = 0.035f;
        public Color safetyApronColor =
            new(0.16f, 0.18f, 0.22f, 1f);
        [Min(0f)] public float safetyApronSurfaceOffset = 0.0002f;
        [Min(0f)] public float roadEndPadding;
        [Min(0f)] public float trackPaddingSeconds = 5f;
        [Min(3)] public int maxTrackPoints = 192;
        [Min(1f)] public float maxEventDuration = 30f;
        [Min(0.01f)] public float eventPlaybackSpeed = 1f;
        [Min(0f)] public float eventLeadSeconds = 6f;
        [Min(0f)] public float eventTailSeconds = 3f;
        [Min(0f)] public float showcaseEntryApproachSeconds = 0.75f;
        [Min(0f)] public float showcaseExitDepartureSeconds = 0.75f;
        [Min(0f)] public float showcasePortalVisualPaddingSeconds = 5f;
        [Min(0f)] public float overtakeMotionLeadSeconds = 3f;
        [Min(0f)] public float raceStartMotionGraceSeconds = 1f;
        [Min(0f)] public float minimumMovingLeadSeconds = 4f;
        [Min(1f)] public float eventMinimumSeparationInVehicleWidths = 1.05f;

        [Header("Showcase Battle")]
        [Min(1f)] public float battleScanSeconds = 14f;
        [Min(0f)] public float battleContinuationSeconds = 12f;
        [Min(0f)] public float battleConfirmationInVehicleLengths = 0.15f;
        [Min(0f)] public float battleConfirmationSeconds = 0.45f;
        [Range(0.02f, 0.25f)] public float battleSampleSeconds = 0.05f;
        [Min(0f)] public float battleVictoryDelaySeconds = 0.65f;
        [Min(1f)] public float battleCruiseSpeedMultiplier = 1.5f;
        [Min(0f)] public float battleExchangeNormalSpeedSeconds = 1.25f;
        [Min(0f)] public float battleCruiseBlendSeconds = 0.75f;

        [Header("Pit Stop Showcase")]
        [Min(10f)] public float pitMaximumEventDuration = 45f;
        [Min(0.5f)] public float pitVisibleApproachSeconds = 2f;
        [Min(0f)] public float pitVisibleExitSeconds = 2.5f;
        public GameObject pitWheelGunPrefab;
        public AudioClip pitWheelGunClip;
        public PitEnvironmentProfile[] pitEnvironmentProfiles;

        private readonly ReplayTimeline timeline = new();
        private readonly Dictionary<int, List<LocationSample>> eventSamples = new();
        private readonly Dictionary<int, int> eventIndices = new();
        private readonly HashSet<int> eventDrivers = new();
        private readonly List<Vector3> mappedPath = new();
        private readonly List<float> mappedPathDistances = new();
        private readonly List<Vector3> presentationPath = new();
        private readonly List<float> presentationPathDistances = new();
        private readonly List<Vector3> fallbackCorridorPath = new();
        private readonly List<Vector3> safetyApronPath = new();
        private readonly List<Vector3> safetyApronLeftEdge = new();
        private readonly List<Vector3> safetyApronRightEdge = new();
        private readonly List<Vector3> drivableLeftEdge = new();
        private readonly List<Vector3> drivableRightEdge = new();
        private readonly Dictionary<int, List<float>> eventLongitudinals = new();
        private readonly List<TableTrackRendererState> tableTrackRendererStates = new();
        private readonly OvertakeCompletionDetector
            completionDetector = new();
        private readonly ShowcaseOvertakeBattleBuilder
            battleBuilder = new();
        private readonly PitStopSequenceBuilder
            pitStopBuilder = new();

        private ReplayPlayer player;
        private ReplayCarSet eventCars;
        private ReplayAudio eventAudio;
        private int showcaseAudioFocusDriver;
        private ReplayEventDto currentEvent;
        private ReplayEventDto motionEvent;
        private OvertakeBattleSequence battleSequence;
        private PitStopSequence pitStopSequence;
        private PitStopShowcasePresentation pitStopPresentation;
        private PitShowcaseAssetProfile pitShowcaseAssets;
        private OvertakeMotionSettings eventOvertakeSettings;
        private GameObject stageRoot;
        private BoxCollider stageInteractionCollider;
        private Vector3 stageInteractionDefaultCenter;
        private Vector3 stageInteractionDefaultSize;
        private bool stageInteractionDefaultsCaptured;
        private EventTrackSegment trackSegment;
        private Mesh roadMesh;
        private LineRenderer leftRoadEdge;
        private LineRenderer rightRoadEdge;
        private Material roadMaterial;
        private Material edgeMaterial;
        private Mesh safetyApronMesh;
        private Material safetyApronMaterial;
        private Coroutine openRoutine;
        private ReplaySnapshot snapshot;
        private int referenceDriverNumber;
        private int sourceGeometryRevision;
        private Vector3 eventSpaceCenter;
        private Quaternion sourceToEventRotation = Quaternion.identity;
        private bool hasSnapshot;
        private bool isLoading;
        private bool isActive;
        private bool showcaseTransitionHeld;
        private float showcasePlaybackSpeedMultiplier = 1f;
        private float overtakeVehicleSizeScale = 1f;
        private int nextBattleExchangeIndex;
        private float lastBattleVfxReplayTime = float.NaN;
        private bool battleCompletionConfirmed;
        private bool battleVictoryTriggered;
        private ShowcasePlaybackWindow showcasePlaybackWindow;
        private float showcasePresentationEndTime = float.NaN;
        private int showcaseTimelineRevision;

        public bool IsLoading => isLoading;
        public bool IsActive => isActive;
        public bool IsPlaying => isActive && timeline.IsPlaying;
        public bool HasOvertake =>
            player != null &&
            FindClosestOvertake(
                player.Events,
                player.CurrentTime,
                ResolveEarliestAutomaticOvertakeAnchor()) != null;
        public bool HasNextOvertake =>
            TryFindNextOvertake(out _);
        public bool HasPitStop =>
            FindClosestEvent(
                player != null ? player.Events : null,
                player != null ? player.CurrentTime : 0f,
                "PitStop",
                player != null
                    ? player.TimelineStartTime
                    : float.NegativeInfinity,
                player != null
                    ? player.ReadyUntilTime
                    : float.PositiveInfinity,
                IsUsablePitStop) != null;
        public bool HasNextPitStop =>
            TryFindNextPitStop(out _);
        public bool IsPitStopActive =>
            isActive && IsPitStopDefinition(currentEvent);
        public bool PitStopReconstructed =>
            IsPitStopActive &&
            pitStopSequence != null &&
            pitStopSequence.IsReconstructed;
        public bool PitStopDriveThrough =>
            IsPitStopActive &&
            pitStopSequence != null &&
            pitStopSequence.IsDriveThrough;
        public PitShowcaseAssetProfile PitShowcaseAssets =>
            pitShowcaseAssets ??=
                Resources.Load<PitShowcaseAssetProfile>(
                    PitShowcaseAssetResourcePath);
        public PitStopPhase CurrentPitStopPhase =>
            pitStopSequence != null
                ? pitStopSequence.GetPhase(CurrentTime)
                : PitStopPhase.Approach;
        public float CurrentTime => isActive ? timeline.CurrentTime : 0f;

        public bool TryGetPitStopPresentationState(
            out PitStopPresentationState state)
        {
            state = default;
            if (!IsPitStopActive || pitStopSequence == null)
                return false;

            state = pitStopSequence.GetPresentationState(CurrentTime);
            return true;
        }
        public bool OvertakeCompletionConfirmed =>
            isActive &&
            (battleSequence != null && battleSequence.IsValid
                ? battleCompletionConfirmed
                : completionDetector.HasTriggered);
        public int OvertakeFinalLeader =>
            battleSequence != null && battleSequence.IsValid
                ? battleSequence.FinalLeader
                : currentEvent != null &&
                  currentEvent.driverNumbers != null &&
                  currentEvent.driverNumbers.Length > 0
                    ? currentEvent.driverNumbers[0]
                    : 0;
        public float StartTime => isActive ? timeline.StartTime : 0f;
        public float EndTime => isActive ? timeline.RaceEndTime : 0f;
        public float NormalizedTime => isActive
            ? timeline.ToNormalized(timeline.CurrentTime)
            : 0f;
        public ReplayEventDto CurrentEvent => currentEvent;
        public Transform PresentationRoot => stageRoot != null
            ? stageRoot.transform
            : null;
        public int SourceGeometryRevision => sourceGeometryRevision;
        internal int ShowcaseTimelineRevision =>
            showcaseTimelineRevision;
        public int SourceGeometryPointCount => presentationPath.Count;
        public float OrderingTransitionTime => isActive
            ? ResolveOrderingTransitionTime()
            : 0f;

        internal bool HasMultipleShowcaseExchanges =>
            battleSequence != null &&
            battleSequence.IsValid &&
            battleSequence.Exchanges.Count > 1;

        internal bool TryGetShowcaseExchangeSpan(
            out Vector3 firstPosition,
            out Vector3 lastPosition)
        {
            firstPosition = Vector3.zero;
            lastPosition = Vector3.zero;
            if (!HasMultipleShowcaseExchanges)
                return false;

            OvertakeBattleExchange first =
                battleSequence.Exchanges[0];
            OvertakeBattleExchange last =
                battleSequence.Exchanges[
                    battleSequence.Exchanges.Count - 1];
            return TryGetEventLocalPathPosition(
                    first.anchorTime,
                    out firstPosition) &&
                TryGetEventLocalPathPosition(
                    last.anchorTime,
                    out lastPosition);
        }

        internal bool TryCopyShowcaseActionBeats(
            List<ShowcaseActionBeat> destination)
        {
            destination?.Clear();
            if (destination == null ||
                battleSequence == null ||
                !battleSequence.IsValid)
            {
                return false;
            }

            IReadOnlyList<OvertakeBattleExchange> exchanges =
                battleSequence.Exchanges;
            for (int i = 0; i < exchanges.Count; i++)
            {
                OvertakeBattleExchange exchange = exchanges[i];
                if (!TryGetEventLocalPathPosition(
                        exchange.anchorTime,
                        out Vector3 position))
                {
                    destination.Clear();
                    return false;
                }

                float padding = Mathf.Max(
                    battleSampleSeconds,
                    battleExchangeNormalSpeedSeconds);
                float beatStart = Mathf.Max(
                    showcasePlaybackWindow.StartTime,
                    exchange.anchorTime - padding);
                float beatEnd = Mathf.Min(
                    showcasePlaybackWindow.EndTime,
                    Mathf.Max(
                        exchange.confirmedTime,
                        exchange.anchorTime + padding));
                if (i > 0)
                {
                    beatStart = Mathf.Max(
                        beatStart,
                        (exchanges[i - 1].anchorTime +
                         exchange.anchorTime) * 0.5f);
                }
                if (i < exchanges.Count - 1)
                {
                    beatEnd = Mathf.Min(
                        beatEnd,
                        (exchange.anchorTime +
                         exchanges[i + 1].anchorTime) * 0.5f);
                }

                destination.Add(
                    new ShowcaseActionBeat(
                        exchange.anchorTime,
                        beatStart,
                        beatEnd,
                        exchange.confirmedTime,
                        exchange.overtaker,
                        exchange.defender,
                        exchange.kind,
                        position));
            }

            return destination.Count > 0;
        }

        internal bool TryGetShowcasePlaybackWindow(
            out ShowcasePlaybackWindow window)
        {
            window = showcasePlaybackWindow;
            return isActive && window.IsValid;
        }

        internal bool TryGetEventLocalPathPosition(
            float replayTime,
            out Vector3 position)
        {
            position = Vector3.zero;
            if (!isActive ||
                referenceDriverNumber <= 0 ||
                !TryGetSourceLongitudinalAtTime(
                    referenceDriverNumber,
                    replayTime,
                    out float distance))
            {
                return false;
            }

            position = sourceToEventRotation *
                (EvaluateSourcePathDistance(distance) -
                 eventSpaceCenter);
            return true;
        }

        internal bool TryGetEventLocalVehiclePosition(
            int driverNumber,
            float replayTime,
            out Vector3 position)
        {
            position = Vector3.zero;
            if (!isActive ||
                !eventSamples.TryGetValue(
                    driverNumber,
                    out List<LocationSample> samples) ||
                !TryGetMappedPosition(
                    samples,
                    replayTime,
                    out Vector3 mappedPosition))
            {
                return false;
            }

            position = sourceToEventRotation *
                (mappedPosition - eventSpaceCenter);
            return true;
        }

        internal bool TryGetPitStopVehicle(
            out Transform vehicle,
            out int driverNumber)
        {
            vehicle = null;
            driverNumber = 0;
            if (!IsPitStopActive ||
                currentEvent.driverNumbers == null ||
                currentEvent.driverNumbers.Length == 0 ||
                eventCars == null)
            {
                return false;
            }

            driverNumber = currentEvent.driverNumbers[0];
            return eventCars.TryGetCarTransform(
                    driverNumber,
                    out vehicle) &&
                vehicle != null;
        }

        internal bool TryGetPitStopFocusLocalPosition(
            out Vector3 position)
        {
            position = Vector3.zero;
            return IsPitStopActive &&
                pitStopSequence != null &&
                TryGetEventLocalPathPosition(
                    pitStopSequence.FocusTime,
                    out position);
        }

        internal bool TryGetPitStopVehicleLength(
            out float length)
        {
            length = 0f;
            return IsPitStopActive &&
                currentEvent.driverNumbers != null &&
                currentEvent.driverNumbers.Length > 0 &&
                eventCars != null &&
                eventCars.TryGetVisualLength(
                    currentEvent.driverNumbers[0],
                    out length);
        }

        internal bool TryGetShowcaseTerrainOcclusion(
            Vector3 worldOrigin,
            Vector3 worldTarget,
            float worldTargetClearance,
            out bool occluded)
        {
            occluded = false;
            return isActive &&
                trackSegment != null &&
                trackSegment.TryIsTerrainOccluded(
                    worldOrigin,
                    worldTarget,
                    worldTargetClearance,
                    out occluded);
        }

        internal bool TryGetShowcaseOcclusion(
            Vector3 worldOrigin,
            Vector3 worldTarget,
            float worldTargetClearance,
            out ShowcaseOcclusionHit hit)
        {
            hit = default;
            return isActive &&
                trackSegment != null &&
                trackSegment.TryGetOcclusion(
                    worldOrigin,
                    worldTarget,
                    worldTargetClearance,
                    out hit);
        }

        internal bool TryCollectShowcaseRemovableOccluders(
            Vector3 worldOrigin,
            Vector3 worldTarget,
            float worldTargetClearance,
            HashSet<Material> destination,
            out bool occluded,
            out bool hasNonRemovableOccluder)
        {
            occluded = false;
            hasNonRemovableOccluder = false;
            return isActive &&
                trackSegment != null &&
                trackSegment.TryCollectRemovableOccluders(
                    worldOrigin,
                    worldTarget,
                    worldTargetClearance,
                    destination,
                    out occluded,
                    out hasNonRemovableOccluder);
        }

        internal void SetShowcaseIgnoredOcclusionMaterials(
            IReadOnlyCollection<Material> materials)
        {
            trackSegment?.SetIgnoredOcclusionMaterials(materials);
        }

        public bool TryCopyEventLocalCenterPath(
            List<Vector3> destination,
            out Vector3 overtakePosition,
            out float transitionReplayProgress)
        {
            overtakePosition = Vector3.zero;
            if (!TryCopySourceCenterPath(
                    destination,
                    out _,
                    out transitionReplayProgress))
            {
                return false;
            }

            for (int i = 0; i < destination.Count; i++)
            {
                destination[i] =
                    sourceToEventRotation *
                    (destination[i] - eventSpaceCenter);
            }

            float transitionTime = ResolveOrderingTransitionTime();
            if (referenceDriverNumber > 0 &&
                TryGetSourceLongitudinalAtTime(
                    referenceDriverNumber,
                    transitionTime,
                    out float transitionDistance))
            {
                overtakePosition =
                    sourceToEventRotation *
                    (EvaluateSourcePathDistance(transitionDistance) -
                     eventSpaceCenter);
            }
            else
            {
                overtakePosition =
                    destination[destination.Count / 2];
            }

            return true;
        }

        public bool TryCopySourceCenterPath(
            List<Vector3> destination,
            out float transitionPathProgress,
            out float transitionReplayProgress)
        {
            transitionPathProgress = 0.5f;
            transitionReplayProgress = 0.5f;
            destination?.Clear();
            if (!isActive ||
                destination == null ||
                mappedPath.Count < 3 ||
                mappedPathDistances.Count != mappedPath.Count)
            {
                return false;
            }

            if (!TryGetSourceLongitudinalAtTime(
                    referenceDriverNumber,
                    timeline.StartTime,
                    out float windowStart) ||
                !TryGetSourceLongitudinalAtTime(
                    referenceDriverNumber,
                    timeline.RaceEndTime,
                    out float windowEnd) ||
                windowEnd - windowStart <= 0.0001f)
            {
                return false;
            }

            CopySourcePathWindow(
                destination,
                windowStart,
                windowEnd);
            if (destination.Count < 3)
                return false;

            float transitionTime = ResolveOrderingTransitionTime();
            transitionReplayProgress =
                timeline.ToNormalized(transitionTime);

            if (referenceDriverNumber > 0 &&
                TryGetSourceLongitudinalAtTime(
                    referenceDriverNumber,
                    transitionTime,
                    out float transitionDistance))
            {
                transitionPathProgress = Mathf.InverseLerp(
                    windowStart,
                    windowEnd,
                    transitionDistance);
            }

            return true;
        }

        private void CopySourcePathWindow(
            List<Vector3> destination,
            float startDistance,
            float endDistance)
        {
            destination.Add(EvaluateSourcePathDistance(startDistance));
            for (int i = 1; i < mappedPath.Count - 1; i++)
            {
                float distance = mappedPathDistances[i];
                if (distance <= startDistance ||
                    distance >= endDistance)
                {
                    continue;
                }

                Vector3 point = mappedPath[i];
                if (Vector3.SqrMagnitude(
                        point - destination[destination.Count - 1]) >
                    0.000001f)
                {
                    destination.Add(point);
                }
            }

            Vector3 end = EvaluateSourcePathDistance(endDistance);
            if (Vector3.SqrMagnitude(
                    end - destination[destination.Count - 1]) >
                0.000001f)
            {
                destination.Add(end);
            }
        }

        private Vector3 EvaluateSourcePathDistance(float distance)
        {
            float clamped = Mathf.Clamp(
                distance,
                mappedPathDistances[0],
                mappedPathDistances[mappedPathDistances.Count - 1]);
            int upper = 1;
            while (upper < mappedPathDistances.Count - 1 &&
                   mappedPathDistances[upper] < clamped)
            {
                upper++;
            }

            int lower = upper - 1;
            float blend = Mathf.InverseLerp(
                mappedPathDistances[lower],
                mappedPathDistances[upper],
                clamped);
            return Vector3.Lerp(
                mappedPath[lower],
                mappedPath[upper],
                blend);
        }

        public bool TryGetSourceLongitudinal(
            int driverNumber,
            out float longitudinal)
        {
            longitudinal = 0f;
            return isActive &&
                TryGetSourceLongitudinalAtTime(
                    driverNumber,
                    timeline.CurrentTime,
                    out longitudinal);
        }

        public bool TryGetReferenceSourceLongitudinal(
            float normalizedTime,
            out float longitudinal)
        {
            longitudinal = 0f;
            if (!isActive || referenceDriverNumber <= 0)
                return false;

            float time = Mathf.Lerp(
                timeline.StartTime,
                timeline.RaceEndTime,
                Mathf.Clamp01(normalizedTime));
            return TryGetSourceLongitudinalAtTime(
                referenceDriverNumber,
                time,
                out longitudinal);
        }

        internal bool TryGetReferenceSourceLongitudinalAtTime(
            float replayTime,
            out float longitudinal)
        {
            longitudinal = 0f;
            return isActive &&
                referenceDriverNumber > 0 &&
                TryGetSourceLongitudinalAtTime(
                    referenceDriverNumber,
                    replayTime,
                    out longitudinal);
        }

        public void Configure(ReplayPlayer replayPlayer)
        {
            player = replayPlayer;
        }

        public bool TrySetPresentationPose(
            Vector3 position,
            Quaternion rotation,
            float uniformScale)
        {
            if (PresentationRoot == null)
                return false;

            PresentationRoot.SetPositionAndRotation(position, rotation);
            PresentationRoot.localScale =
                Vector3.one * Mathf.Max(0.1f, uniformScale);
            return true;
        }

        internal bool TryApplyRoomStagePlacement(
            Vector3 position,
            Quaternion rotation,
            float uniformScale,
            Vector3 eventLocalFocus,
            float physicalInteractionWidth = 0.6f)
        {
            if (PresentationRoot == null ||
                stageInteractionCollider == null ||
                !IsFinite(position) ||
                !IsFinite(rotation) ||
                !IsFinite(eventLocalFocus) ||
                !float.IsFinite(uniformScale) ||
                !float.IsFinite(physicalInteractionWidth))
            {
                return false;
            }

            float resolvedScale = Mathf.Max(0.1f, uniformScale);
            Vector3 parentScale =
                PresentationRoot.parent != null
                    ? PresentationRoot.parent.lossyScale
                    : Vector3.one;
            float parentWorldScale = Mathf.Max(
                Mathf.Abs(parentScale.x),
                Mathf.Abs(parentScale.y),
                Mathf.Abs(parentScale.z));
            float uniformWorldScale =
                parentWorldScale * resolvedScale;
            if (!float.IsFinite(uniformWorldScale) ||
                uniformWorldScale <= 0.000001f)
            {
                return false;
            }

            float localWidth =
                Mathf.Max(0.1f, physicalInteractionWidth) /
                uniformWorldScale;
            float localHeight = 0.12f / uniformWorldScale;
            Vector3 interactionCenter =
                eventLocalFocus +
                Vector3.up * localHeight * 0.5f;
            Vector3 interactionSize =
                new(localWidth, localHeight, localWidth);
            if (!IsFinite(interactionCenter) ||
                !IsFinite(interactionSize))
            {
                return false;
            }

            PresentationRoot.SetPositionAndRotation(
                position,
                rotation);
            PresentationRoot.localScale =
                Vector3.one * resolvedScale;
            stageInteractionCollider.center = interactionCenter;
            stageInteractionCollider.size = interactionSize;
            SetStageInteractionEnabled(false);
            return true;
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) &&
                float.IsFinite(value.y) &&
                float.IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return float.IsFinite(value.x) &&
                float.IsFinite(value.y) &&
                float.IsFinite(value.z) &&
                float.IsFinite(value.w);
        }

        public bool TryRestoreTableRelativePose()
        {
            if (PresentationRoot == null)
                return false;

            ResolveStagePose(out Vector3 position, out Quaternion rotation);
            rotation = Quaternion.Euler(0f, rotation.eulerAngles.y, 0f);
            if (!TrySetPresentationPose(position, rotation, stageScale))
                return false;

            if (stageInteractionCollider != null &&
                stageInteractionDefaultsCaptured)
            {
                stageInteractionCollider.center =
                    stageInteractionDefaultCenter;
                stageInteractionCollider.size =
                    stageInteractionDefaultSize;
            }

            SetStageInteractionEnabled(true);

            return true;
        }

        private void SetStageInteractionEnabled(bool enabled)
        {
            if (PresentationRoot == null)
                return;

            var policy =
                PresentationRoot.GetComponent<WorldGrabPolicy>();
            if (policy != null)
                policy.enabled = enabled;

            var scale =
                PresentationRoot.GetComponent<ScaleController>();
            if (scale != null)
                scale.enabled = enabled;

            var grab =
                PresentationRoot.GetComponent<XRGrabInteractable>();
            if (grab != null)
                grab.enabled = enabled;

            if (stageInteractionCollider != null)
                stageInteractionCollider.enabled = enabled;
        }

        private void Awake()
        {
            if (player == null)
                player = GetComponent<ReplayPlayer>();
        }

        public void OpenTestOvertake()
        {
            if (player == null || !player.HasDataset)
            {
                Debug.LogWarning("[EventReplay] Cannot open an event before the replay dataset is ready.", this);
                return;
            }

            float earliestUsableAnchor =
                ResolveEarliestAutomaticOvertakeAnchor();
            ReplayEventDto definition = FindClosestOvertake(
                player.Events,
                player.CurrentTime,
                earliestUsableAnchor);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (definition == null)
                definition = FindClosestOvertake(
                    ReplayEventFixtures.Load(player.Manifest),
                    player.CurrentTime,
                    earliestUsableAnchor);

            if (definition == null && allowDevelopmentFallbackEvent)
                definition = CreateDevelopmentEvent();
#endif

            if (definition == null)
            {
                Debug.LogWarning("[EventReplay] No overtake event is available in the manifest.", this);
                return;
            }

            Open(definition);
        }

        public void OpenNextOvertake()
        {
            if (isLoading ||
                player == null ||
                !player.HasDataset)
            {
                return;
            }

            if (!TryFindNextOvertake(out ReplayEventDto definition))
                return;

            Open(definition);
        }

        public void OpenTestPitStop()
        {
            if (player == null || !player.HasDataset || isLoading)
                return;

            ReplayEventDto definition = FindClosestEvent(
                player.Events,
                player.CurrentTime,
                "PitStop",
                player.TimelineStartTime,
                player.ReadyUntilTime,
                IsUsablePitStop);
            if (definition == null)
            {
                Debug.LogWarning(
                    "[EventReplay] No pit stop is available in the loaded range.",
                    this);
                return;
            }

            Open(definition);
        }

        public void OpenNextPitStop()
        {
            if (isLoading || player == null || !player.HasDataset)
                return;

            if (TryFindNextPitStop(
                    out ReplayEventDto definition))
            {
                Open(definition);
            }
        }

        internal bool TryOpenFirstPitStop()
        {
            if (isLoading ||
                isActive ||
                player == null ||
                !player.HasDataset ||
                player.Events == null)
            {
                return false;
            }

            ReplayEventDto first = null;
            ReplayEventDto[] events = player.Events;
            for (int i = 0; i < events.Length; i++)
            {
                ReplayEventDto candidate = events[i];
                if (!IsUsablePitStop(candidate) ||
                    candidate.endTime > player.ReadyUntilTime)
                {
                    continue;
                }

                if (first == null ||
                    candidate.anchorTime < first.anchorTime ||
                    Mathf.Approximately(
                        candidate.anchorTime,
                        first.anchorTime) &&
                    string.CompareOrdinal(
                        candidate.eventId,
                        first.eventId) < 0)
                {
                    first = candidate;
                }
            }

            if (first == null)
                return false;

            Open(first);
            return true;
        }

        public void Open(ReplayEventDto definition)
        {
            if (IsPitStopDefinition(definition) &&
                !IsUsablePitStop(definition))
            {
                Debug.LogWarning(
                    "[EventReplay] The selected pit event is not a conventional pit stop showcase.",
                    this);
                return;
            }

            ReplayEventDto presentation = CreatePresentationEvent(definition);
            if (!TryValidate(presentation, out string error))
            {
                Debug.LogWarning($"[EventReplay] Invalid event: {error}", this);
                return;
            }

            if (IsCollisionEvent(presentation))
            {
                if (IsCollisionPrepared)
                    OpenPreparedCollision();
                else
                    PrepareTestCollision();
                return;
            }

            if (!hasSnapshot)
            {
                snapshot = new ReplaySnapshot(player);
                hasSnapshot = true;
            }

            player.Pause();

            if (openRoutine != null)
                StopCoroutine(openRoutine);

            DestroyStage(false);
            if (IsCollisionEvent(presentation))
                SuspendTableTrackRendering();
            else
                RestoreTableTrackRendering();
            currentEvent = presentation;
            openRoutine = StartCoroutine(OpenRoutine(presentation));
        }

        public void Play()
        {
            if (!isActive)
                return;

#if UNITY_EDITOR
            pitStopPresentation?.ClearFirstMilestoneCalibrationTime();
#endif
            timeline.Play();
            eventAudio?.SetPlaying(!showcaseTransitionHeld);
        }

        public void Pause()
        {
            timeline.Pause();
            eventAudio?.SetPlaying(false);
        }

        public void SetShowcaseAudioFocus(int driverNumber)
        {
            driverNumber = Mathf.Max(0, driverNumber);
            if (showcaseAudioFocusDriver == driverNumber)
                return;

            showcaseAudioFocusDriver = driverNumber;
            eventCars?.SetAudioFocusDriver(driverNumber);
        }

        public void SetShowcaseDrivingPresentation(
            int firstDriver,
            int secondDriver,
            bool enabled)
        {
            eventCars?.SetShowcaseDrivingPresentation(
                firstDriver,
                secondDriver,
                enabled);
        }

        internal bool TrySetCarWorldPoseOverride(
            ReplayCarWorldPoseOverride resolver)
        {
            if (!isActive || eventCars == null || resolver == null)
                return false;

            eventCars.SetWorldPoseOverride(resolver);
            return true;
        }

        internal void ClearCarWorldPoseOverride(
            ReplayCarWorldPoseOverride resolver)
        {
            eventCars?.ClearWorldPoseOverride(resolver);
        }

        public void SetShowcasePlaybackSpeedMultiplier(
            float multiplier)
        {
            showcasePlaybackSpeedMultiplier =
                Mathf.Max(1f, multiplier);
        }

        internal void SetShowcaseTransitionHold(bool held)
        {
            if (showcaseTransitionHeld == held)
                return;

            showcaseTransitionHeld = held;
            eventAudio?.SetPlaying(
                timeline.IsPlaying &&
                !showcaseTransitionHeld);
        }

        internal bool TrySetShowcasePresentationEndTime(
            float replayTime)
        {
            if (!isActive ||
                !showcasePlaybackWindow.IsValid ||
                !float.IsFinite(replayTime) ||
                replayTime <= showcasePlaybackWindow.StartTime ||
                replayTime >= showcasePlaybackWindow.EndTime)
            {
                return false;
            }

            showcasePresentationEndTime = replayTime;
            return true;
        }

        internal void ClearShowcasePresentationEndTime()
        {
            showcasePresentationEndTime = float.NaN;
        }

        internal bool TryGetShowcaseResultPresentationEndTime(
            out float replayTime)
        {
            replayTime = 0f;
            if (!isActive ||
                !showcasePlaybackWindow.IsValid ||
                battleSequence == null ||
                !battleSequence.IsValid)
            {
                return false;
            }

            OvertakeBattleExchange finalExchange =
                battleSequence.Exchanges[
                    battleSequence.Exchanges.Count - 1];
            float finalConfirmationTime = Mathf.Max(
                finalExchange.anchorTime,
                finalExchange.confirmedTime);
            float resultDuration = 0f;
            OvertakeCompletionVfxSettings settings =
                player != null
                    ? player.overtakeCompletionVfx
                    : null;
            if (settings != null && settings.enabled)
            {
                resultDuration = Mathf.Max(
                    settings.pulseDurationReplaySeconds,
                    settings.sweepDurationReplaySeconds,
                    settings.streakDurationReplaySeconds,
                    settings.hudDisplayDurationReplaySeconds);
            }

            replayTime = Mathf.Min(
                showcasePlaybackWindow.EndTime,
                finalConfirmationTime +
                Mathf.Max(0f, battleVictoryDelaySeconds) +
                resultDuration);
            return float.IsFinite(replayTime) &&
                replayTime > showcasePlaybackWindow.StartTime;
        }

        public void SetOvertakeVehicleSizeScale(float scale)
        {
            overtakeVehicleSizeScale = Mathf.Max(0.01f, scale);
            eventCars?.SetOvertakeVehicleSizeScale(
                overtakeVehicleSizeScale);
        }

        public void TogglePlay()
        {
            if (IsPlaying)
                Pause();
            else
                Play();
        }

        public void Restart()
        {
            if (!isActive)
                return;

#if UNITY_EDITOR
            pitStopPresentation?.ClearFirstMilestoneCalibrationTime();
#endif
            timeline.SetTime(timeline.StartTime);
            showcaseTimelineRevision++;
            ResetIndices();
            Play();
            ApplyCars();
        }

        public void SeekNormalized(float normalized)
        {
            if (!isActive)
                return;

#if UNITY_EDITOR
            pitStopPresentation?.ClearFirstMilestoneCalibrationTime();
#endif
            timeline.SetTime(timeline.FromNormalized(normalized));
            showcaseTimelineRevision++;
            ResetIndices();
            ApplyCars();
        }

#if UNITY_EDITOR
        public bool TryPauseAtPitStopCalibrationTime(
            float choreographyTime)
        {
            if (!isActive ||
                pitStopSequence == null ||
                pitStopPresentation == null ||
                !IsPitStopDefinition(currentEvent))
            {
                return false;
            }

            if (!pitStopPresentation.SetFirstMilestoneCalibrationTime(
                    choreographyTime))
            {
                return false;
            }

            timeline.Pause();
            eventAudio?.SetPlaying(false);
            timeline.SetTime(pitStopSequence.FocusTime);
            showcaseTimelineRevision++;
            ResetIndices();
            ApplyCars();
            return true;
        }

        public void ClearPitStopCalibrationTime()
        {
            pitStopPresentation?.ClearFirstMilestoneCalibrationTime();
            if (isActive)
                ApplyCars();
        }

        public bool TryGetPitStopCalibrationRange(
            out float serviceStartTime,
            out float focusTime,
            out float serviceEndTime,
            out float pitStopDuration,
            out float pitLaneDuration)
        {
            serviceStartTime = 0f;
            focusTime = 0f;
            serviceEndTime = 0f;
            pitStopDuration = -1f;
            pitLaneDuration = -1f;
            if (!isActive ||
                pitStopSequence == null ||
                !IsPitStopDefinition(currentEvent))
            {
                return false;
            }

            serviceStartTime = pitStopSequence.ServiceStartTime;
            focusTime = pitStopSequence.FocusTime;
            serviceEndTime = pitStopSequence.ServiceEndTime;
            pitStopDuration = currentEvent != null
                ? currentEvent.pitStopDuration
                : -1f;
            pitLaneDuration = currentEvent != null
                ? currentEvent.pitLaneDuration
                : -1f;
            return true;
        }

        public bool TryPauseAtPitStopReplayTime(float replayTime)
        {
            if (!isActive ||
                pitStopSequence == null ||
                pitStopPresentation == null ||
                !IsPitStopDefinition(currentEvent))
            {
                return false;
            }

            pitStopPresentation.ClearFirstMilestoneCalibrationTime();
            timeline.Pause();
            eventAudio?.SetPlaying(false);
            timeline.SetTime(Mathf.Clamp(
                replayTime,
                timeline.StartTime,
                timeline.EndTime));
            showcaseTimelineRevision++;
            ResetIndices();
            ApplyCars();
            return true;
        }
#endif

        public void Close()
        {
            if (TryClosePreparedCollision())
                return;

            if (openRoutine != null)
            {
                StopCoroutine(openRoutine);
                openRoutine = null;
            }

            DestroyStage();
            RestoreReplay();
        }

        internal void SuspendTableTrackRendering()
        {
            if (player == null ||
                tableTrackRendererStates.Count > 0)
                return;

            Transform tableTrack = player.GetTrackPlacementTransform();
            if (tableTrack == null)
                return;

            Renderer[] renderers =
                tableTrack.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                tableTrackRendererStates.Add(
                    new TableTrackRendererState(renderer));
                renderer.forceRenderingOff = true;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        internal void RestoreTableTrackRendering()
        {
            for (int i = 0; i < tableTrackRendererStates.Count; i++)
                tableTrackRendererStates[i].Restore();

            tableTrackRendererStates.Clear();
        }

        private IEnumerator OpenRoutine(ReplayEventDto definition)
        {
            isLoading = true;

            // Let deferred Unity destruction finish before a replacement
            // stage allocates another map and portal presentation.
            yield return null;

            bool pitStop = IsPitStopDefinition(definition);
            bool collision = IsCollisionEvent(definition);
            float scanSeconds = collision
                ? Mathf.Max(
                    CollisionLeadSeconds,
                    CollisionTailSeconds)
                : Mathf.Max(
                    Mathf.Max(
                        eventLeadSeconds,
                        eventTailSeconds),
                    battleScanSeconds);
            float loadStart = Mathf.Max(
                player.TimelineStartTime,
                (pitStop
                    ? definition.startTime
                    : definition.anchorTime - scanSeconds) -
                trackPaddingSeconds);
            float loadEnd = Mathf.Min(
                player.ReadyUntilTime,
                (pitStop
                    ? definition.endTime
                    : definition.anchorTime + scanSeconds) +
                trackPaddingSeconds);
            bool loaded = false;

            yield return player.LoadEventRange(loadStart, loadEnd, value => loaded = value);

            if (loaded && IsDevelopmentEvent(definition))
                SelectClosestDevelopmentDrivers(
                    definition,
                    definition.startTime,
                    definition.endTime);

            if (!loaded || !BuildEventSamples(definition, loadStart, loadEnd))
            {
                Debug.LogWarning(
                    $"[EventReplay] Event '{definition.eventId}' has no usable vehicle samples in " +
                    $"{definition.startTime:0.00}-{definition.endTime:0.00}.",
                    this);
                isLoading = false;
                openRoutine = null;
                Close();
                yield break;
            }

            bool stageBuilt;
            try
            {
                stageBuilt = BuildStage(
                    definition,
                    loadStart,
                    loadEnd);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[EventReplay] Event '{definition.eventId}' stage build failed: " +
                    exception.Message,
                    this);
                Debug.LogException(exception, this);
                isLoading = false;
                openRoutine = null;
                Close();
                yield break;
            }

            if (!stageBuilt)
            {
                Debug.LogWarning(
                    $"[EventReplay] Event '{definition.eventId}' did not produce enough mapped track points.",
                    this);
                isLoading = false;
                openRoutine = null;
                Close();
                yield break;
            }

            timeline.Reset(
                showcasePlaybackWindow.StartTime,
                showcasePlaybackWindow.EndTime);
            isLoading = false;
            isActive = true;
            openRoutine = null;
            if (collision)
                ActivateCollisionPresentationStage();
            ApplyCars();
            if (!collision)
                CreateSafetyApron();
            EnsureCollisionShowcase();
            UpdateCollisionShowcase(timeline.CurrentTime);
            Play();
        }

        private void Update()
        {
            if (!isActive)
                return;

            if (IsCollisionEvent(currentEvent) &&
                collisionIncidentPresentation != null)
            {
                UpdateCollisionIncidentPresentation();
                return;
            }

            bool collisionHitStop =
                UpdateCollisionHitStop(
                    Time.unscaledDeltaTime);
            if (timeline.IsPlaying &&
                !showcaseTransitionHeld &&
                !collisionHitStop)
            {
                timeline.Advance(
                    Time.deltaTime,
                    eventPlaybackSpeed *
                    showcasePlaybackSpeedMultiplier *
                    ResolveBattleCruiseSpeedMultiplier(
                        timeline.CurrentTime) *
                    ResolveCollisionPlaybackSpeedMultiplier(
                        timeline.CurrentTime));
                if (!float.IsNaN(showcasePresentationEndTime) &&
                    timeline.CurrentTime >=
                        showcasePresentationEndTime)
                {
                    timeline.SetTime(showcasePresentationEndTime);
                    timeline.Pause();
                    eventAudio?.SetPlaying(false);
                }
                else if (timeline.StopAtEnd())
                    eventAudio?.SetPlaying(false);
            }

            eventAudio?.Update(
                player != null ? player.engineSound : null,
                true,
                timeline.IsPlaying &&
                !showcaseTransitionHeld &&
                !collisionHitStop,
                null);
            ApplyCars();
        }

        private float ResolveBattleCruiseSpeedMultiplier(
            float replayTime)
        {
            if (!HasMultipleShowcaseExchanges ||
                battleCruiseSpeedMultiplier <= 1f)
            {
                return 1f;
            }

            IReadOnlyList<OvertakeBattleExchange> exchanges =
                battleSequence.Exchanges;
            for (int i = 0; i < exchanges.Count - 1; i++)
            {
                float previousTime = exchanges[i].anchorTime;
                float nextTime = exchanges[i + 1].anchorTime;
                if (replayTime <= previousTime ||
                    replayTime >= nextTime)
                {
                    continue;
                }

                float nearestExchange = Mathf.Min(
                    replayTime - previousTime,
                    nextTime - replayTime);
                float blendStart = Mathf.Max(
                    0f,
                    battleExchangeNormalSpeedSeconds);
                float blendEnd = blendStart + Mathf.Max(
                    0.0001f,
                    battleCruiseBlendSeconds);
                float cruiseBlend = Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(
                        blendStart,
                        blendEnd,
                        nearestExchange));
                return Mathf.Lerp(
                    1f,
                    Mathf.Max(1f, battleCruiseSpeedMultiplier),
                    cruiseBlend);
            }

            return 1f;
        }

        private bool BuildEventSamples(
            ReplayEventDto definition,
            float loadStart,
            float loadEnd)
        {
            eventSamples.Clear();
            eventIndices.Clear();
            eventDrivers.Clear();
            referenceDriverNumber = 0;

            Dictionary<int, List<LocationSample>> source = player.LocationsByDriver;
            if (source == null)
                return false;

            int[] requestedDrivers = definition.driverNumbers;
            if (requestedDrivers == null || requestedDrivers.Length == 0)
                return false;

            for (int i = 0; i < requestedDrivers.Length && eventDrivers.Count < MaxEventDrivers; i++)
            {
                int driver = requestedDrivers[i];
                if (driver <= 0 || eventDrivers.Contains(driver))
                    continue;

                if (!source.TryGetValue(driver, out List<LocationSample> samples))
                {
                    Debug.LogWarning(
                        $"[EventReplay] Driver {driver} is unavailable for event '{definition.eventId}'.",
                        this);
                    continue;
                }

                List<LocationSample> clip = CopyRange(samples, loadStart, loadEnd);
                if (clip.Count < 2)
                {
                    Debug.LogWarning(
                        $"[EventReplay] Driver {driver} has fewer than two samples for event '{definition.eventId}'.",
                        this);
                    continue;
                }

                eventDrivers.Add(driver);
                eventSamples.Add(driver, clip);
                eventIndices.Add(driver, 0);
                if (referenceDriverNumber == 0)
                    referenceDriverNumber = driver;
            }

            return eventDrivers.Count > 0;
        }

        private bool BuildStage(
            ReplayEventDto definition,
            float trackStartTime,
            float trackEndTime)
        {
            bool collisionShowcase =
                IsCollisionEvent(definition);
            bool pitStop = IsPitStopDefinition(definition);
            if (pitStop && pitShowcaseAssets == null)
            {
                pitShowcaseAssets =
                    Resources.Load<PitShowcaseAssetProfile>(
                        PitShowcaseAssetResourcePath);
            }
            eventCars = new ReplayCarSet(
                player.carPrefab,
                player,
                false);
            eventCars.SetRenderLodEnabled(
                !collisionShowcase);
            eventCars.SetOvertakePresentationMode(
                OvertakePresentationMode.Showcase);
            eventCars.SetOvertakeVehicleSizeScale(
                collisionShowcase
                    ? 1f
                    : overtakeVehicleSizeScale);
            eventCars.SetMapScaleRatio(
                player.GetTrackMapScaleRatio());
            eventCars.SetTeamPrefabs(player.teamCarPrefabs);
            eventCars.SetCalibration(player.trackCalibration, false);
            eventCars.SetLabelsVisible(!collisionShowcase);
            eventCars.SetLeaderHighlightVisible(false);
            eventCars.SetDrivers(player.Manifest != null ? player.Manifest.drivers : null);
            eventOvertakeSettings =
                CreateEventOvertakeSettings(
                    player.overtakeMotion);
            eventCars.SetOvertakeSettings(
                eventOvertakeSettings);
            if (collisionShowcase)
                eventCars.PrepareMappedPositions(eventSamples);

            List<LocationSample> referenceSamples = FindReferenceSamples();
            if (!BuildMappedPath(
                    referenceSamples,
                    trackStartTime,
                    trackEndTime))
                return false;
            if (!BuildEventLongitudinals())
                return false;
            float referenceVehicleLength =
                ResolveBattleReferenceVehicleLength(definition);
            float transitionTime;
            if (pitStop)
            {
                battleSequence = null;
                if (!eventLongitudinals.TryGetValue(
                        referenceDriverNumber,
                        out List<float> pitDistances))
                {
                    return false;
                }

                pitStopSequence = pitStopBuilder.Build(
                    definition,
                    referenceSamples,
                    pitDistances,
                    referenceVehicleLength);
                transitionTime = pitStopSequence.FocusTime;
            }
            else if (collisionShowcase)
            {
                pitStopSequence = null;
                battleSequence = null;
                transitionTime = CollisionPresentationContactTime > 0f
                    ? CollisionPresentationContactTime
                    : definition.anchorTime;
            }
            else
            {
                pitStopSequence = null;
                battleSequence = battleBuilder.Build(
                    definition,
                    definition.anchorTime -
                    Mathf.Max(1f, battleScanSeconds),
                    definition.anchorTime +
                    Mathf.Max(1f, battleScanSeconds),
                    player.TimelineStartTime,
                    player.ReadyUntilTime,
                    eventLeadSeconds,
                    eventTailSeconds,
                    overtakeMotionLeadSeconds,
                    battleContinuationSeconds,
                    maxEventDuration,
                    referenceVehicleLength *
                    Mathf.Max(
                        0f,
                        battleConfirmationInVehicleLengths),
                    battleConfirmationSeconds,
                    battleSampleSeconds,
                    TryGetSourceGap);
                transitionTime = battleSequence != null &&
                                 battleSequence.IsValid
                    ? battleSequence.FocusTime
                    : ResolveOrderingTransitionTime(
                        definition,
                        definition.startTime,
                        definition.endTime);
                if (battleSequence != null &&
                    battleSequence.IsValid)
                {
                    definition.startTime =
                        battleSequence.StartTime;
                    definition.endTime =
                        battleSequence.EndTime;
                    LogBattleSequence(
                        definition,
                        battleSequence);
                }
            }
            float playbackStartTime = pitStop
                ? ResolvePitPlaybackStart(
                    definition.startTime,
                    pitStopSequence)
                : definition.startTime;
            float playbackEndTime = pitStop
                ? ResolvePitPlaybackEnd(
                    definition.endTime,
                    pitStopSequence)
                : definition.endTime;
            showcasePlaybackWindow =
                CreateShowcasePlaybackWindow(
                    trackStartTime,
                    playbackStartTime,
                    transitionTime,
                    playbackEndTime,
                    trackEndTime);
            if (!showcasePlaybackWindow.IsValid)
                return false;
            float presentationStart = collisionShowcase
                ? Mathf.Max(
                    showcasePlaybackWindow.StartTime,
                    transitionTime - 1.4f)
                : showcasePlaybackWindow.StartTime;
            float presentationEnd = collisionShowcase
                ? Mathf.Min(
                    showcasePlaybackWindow.EndTime,
                    transitionTime + 0.85f)
                : showcasePlaybackWindow.EndTime;
            if (!BuildPresentationPath(
                    presentationStart,
                    presentationEnd))
            {
                return false;
            }
            motionEvent = pitStop
                ? null
                : collisionShowcase
                    ? CreateMotionEvent(
                        definition,
                        transitionTime)
                : CreateBattleMotionEvent(
                    definition,
                    transitionTime,
                    battleSequence);
            bool overtakeShowcase =
                !pitStop && !collisionShowcase;
            if (!overtakeShowcase)
            {
                eventCars.SetReplayEvents(null);
                eventCars.SetShowcaseBattle(null);
                eventCars.SetOvertakeApproachRibbon(
                    null,
                    player.overtakeApproachRibbon);
                eventCars.SetOvertakeSideBySideVfx(
                    null,
                    player.overtakeSideBySideVfx);
            }
            else
            {
                eventCars.SetReplayEvents(
                    new[] { motionEvent });
                eventCars.SetShowcaseBattle(
                    battleSequence);
                eventCars.SetOvertakeApproachRibbon(
                    motionEvent,
                    player.overtakeApproachRibbon);
                eventCars.SetOvertakeSideBySideVfx(
                    motionEvent,
                    player.overtakeSideBySideVfx);
                completionDetector.Configure(
                    player.overtakeCompletionVfx);
                eventCars.SetOvertakeCompletionVfx(
                    player.overtakeCompletionVfx);
            }
            ResetBattleVfxPlayback(
                showcasePlaybackWindow.StartTime);
            sourceGeometryRevision++;

            GetPathFrame(out Vector3 center, out Quaternion sourceToLocalRotation);
            eventSpaceCenter = center;
            sourceToEventRotation = sourceToLocalRotation;
            bool fallbackCorridorLoops =
                BuildFallbackCorridorPath(
                    center,
                    sourceToLocalRotation);
            BuildSafetyApronPath(
                center,
                sourceToLocalRotation);
            eventCars.SetFallbackOvertakeCorridor(
                fallbackCorridorPath,
                roadWidth,
                fallbackCorridorLoops);
            CreateStageRoot();

            TryGetMappedPosition(
                referenceSamples,
                definition.startTime,
                out Vector3 eventStartPosition);
            TryGetMappedPosition(
                referenceSamples,
                definition.anchorTime,
                out Vector3 eventAnchorPosition);
            Debug.Log(
                $"[EventReplayFrame] event={definition.eventId}, " +
                $"center={center:F4}, " +
                $"startLocal={(sourceToLocalRotation * (eventStartPosition - center)):F4}, " +
                $"anchorLocal={(sourceToLocalRotation * (eventAnchorPosition - center)):F4}, " +
                $"stageScale={stageRoot.transform.localScale.x:F4}",
                this);

            Transform carsRoot = new GameObject("Cars").transform;
            carsRoot.SetParent(stageRoot.transform, false);
            eventCars.SetCustomSpace(carsRoot, center, sourceToLocalRotation);

            Bounds stageBounds;
            if (collisionShowcase)
            {
                CreateCollisionIncidentIsland(
                    center,
                    sourceToLocalRotation,
                    referenceVehicleLength,
                    out stageBounds);
            }
            else if (pitStop)
            {
                CreateRoad(
                    center,
                    sourceToLocalRotation,
                    createEdges: false);
                stageBounds = roadMesh.bounds;
            }
            else if (!CreateActualTrackRegion(
                         center,
                         sourceToLocalRotation,
                         out stageBounds))
            {
                CreateRoad(
                    center,
                    sourceToLocalRotation,
                    createEdges: true);
                stageBounds = roadMesh.bounds;
            }

            if (pitStop &&
                TryGetMappedPosition(
                    referenceSamples,
                    pitStopSequence.FocusTime,
                    out Vector3 pitFocusPosition))
            {
                Vector3 pitLocalFocus = sourceToLocalRotation *
                    (pitFocusPosition - center);
                ReplayCarView pitVehicle = null;
                if (eventCars.TryGetCarTransform(
                        referenceDriverNumber,
                        out Transform pitVehicleRoot))
                {
                    pitVehicle =
                        pitVehicleRoot.GetComponent<ReplayCarView>();
                }
                pitStopPresentation =
                    new PitStopShowcasePresentation();
                pitStopPresentation.Build(
                    stageRoot.transform,
                    pitVehicle,
                    pitLocalFocus,
                    referenceVehicleLength,
                    player.GetDriverInfo(referenceDriverNumber),
                    definition,
                    pitStopSequence,
                    pitWheelGunPrefab,
                    pitWheelGunClip,
                    ResolvePitEnvironmentProfile(),
                    pitShowcaseAssets);
                stageBounds.Encapsulate(
                    pitStopPresentation.LocalBounds);
            }

            ConfigureStageInteraction(stageBounds);

            eventAudio = new ReplayAudio(eventCars);
            eventAudio.Reset(player.engineSound, true, null);
            if (pitStop)
                SetShowcaseAudioFocus(referenceDriverNumber);
            stageRoot.SetActive(false);

            Debug.Log(
                $"[EventReplay] Opened '{definition.eventId}' with {eventDrivers.Count} vehicle(s) " +
                $"and {mappedPath.Count} track points.",
                this);
            return true;
        }

        private bool BuildMappedPath(
            List<LocationSample> samples,
            float startTime,
            float endTime)
        {
            mappedPath.Clear();
            if (samples == null)
                return false;

            Vector3 last = default;
            bool hasLast = false;
            AddMappedPathPoint(samples, startTime, ref last, ref hasLast);

            int stride = Mathf.Max(
                1,
                Mathf.CeilToInt(samples.Count / (float)Mathf.Max(3, maxTrackPoints)));

            for (int i = 0; i < samples.Count; i += stride)
            {
                LocationSample sample = samples[i];
                if (sample.t <= startTime || sample.t >= endTime ||
                    !eventCars.TryGetMappedPosition(sample, out Vector3 position))
                    continue;

                if (hasLast && Vector3.SqrMagnitude(position - last) < 0.000004f)
                    continue;

                mappedPath.Add(position);
                last = position;
                hasLast = true;
            }

            AddMappedPathPoint(samples, endTime, ref last, ref hasLast);

            return mappedPath.Count >= 3;
        }

        private bool BuildEventLongitudinals()
        {
            mappedPathDistances.Clear();
            eventLongitudinals.Clear();
            if (mappedPath.Count < 2)
                return false;

            float distance = 0f;
            mappedPathDistances.Add(distance);
            for (int i = 1; i < mappedPath.Count; i++)
            {
                distance += Vector3.Distance(
                    mappedPath[i - 1],
                    mappedPath[i]);
                mappedPathDistances.Add(distance);
            }

            if (distance <= 0.0001f)
                return false;

            foreach (KeyValuePair<int, List<LocationSample>> pair in eventSamples)
            {
                List<LocationSample> samples = pair.Value;
                List<float> longitudinals = new(samples.Count);
                float previousLongitudinal = 0f;
                int previousSegment = 0;
                for (int i = 0; i < samples.Count; i++)
                {
                    if (!eventCars.TryGetMappedPosition(
                            samples[i],
                            out Vector3 position))
                    {
                        eventLongitudinals.Clear();
                        return false;
                    }

                    float longitudinal = ProjectSourcePathDistance(
                        position,
                        previousLongitudinal,
                        ref previousSegment);
                    previousLongitudinal = Mathf.Max(
                        previousLongitudinal,
                        longitudinal);
                    longitudinals.Add(previousLongitudinal);
                }

                eventLongitudinals.Add(pair.Key, longitudinals);
            }

            return eventLongitudinals.Count > 0;
        }

        private bool BuildPresentationPath(
            float startTime,
            float endTime)
        {
            presentationPath.Clear();
            presentationPathDistances.Clear();
            if (referenceDriverNumber <= 0 ||
                !TryGetSourceLongitudinalAtTime(
                    referenceDriverNumber,
                    startTime,
                    out float startDistance) ||
                !TryGetSourceLongitudinalAtTime(
                    referenceDriverNumber,
                    endTime,
                    out float endDistance) ||
                endDistance - startDistance <= 0.0001f)
            {
                return false;
            }

            presentationPath.Add(
                EvaluateSourcePathDistance(startDistance));
            presentationPathDistances.Add(startDistance);
            for (int i = 1; i < mappedPath.Count - 1; i++)
            {
                float distance = mappedPathDistances[i];
                if (distance <= startDistance ||
                    distance >= endDistance)
                {
                    continue;
                }

                presentationPath.Add(mappedPath[i]);
                presentationPathDistances.Add(distance);
            }

            presentationPath.Add(
                EvaluateSourcePathDistance(endDistance));
            presentationPathDistances.Add(endDistance);
            return presentationPath.Count >= 3;
        }

        private ShowcasePlaybackWindow
            CreateShowcasePlaybackWindow(
                float loadedStartTime,
                float startTime,
                float focusTime,
                float endTime,
                float loadedEndTime)
        {
            if (endTime <= startTime ||
                focusTime <= startTime ||
                focusTime >= endTime)
            {
                return default;
            }

            float entryTime = startTime +
                Mathf.Min(
                    Mathf.Max(
                        0f,
                        showcaseEntryApproachSeconds),
                    (focusTime - startTime) * 0.5f);
            float exitTime = endTime -
                Mathf.Min(
                    Mathf.Max(
                        0f,
                        showcaseExitDepartureSeconds),
                    (endTime - focusTime) * 0.5f);
            float visualPadding = Mathf.Max(
                0f,
                showcasePortalVisualPaddingSeconds);
            float portalVisualStartTime = Mathf.Max(
                loadedStartTime,
                startTime - visualPadding);
            float portalVisualEndTime = Mathf.Min(
                loadedEndTime,
                endTime + visualPadding);
            return new ShowcasePlaybackWindow(
                portalVisualStartTime,
                startTime,
                entryTime,
                focusTime,
                exitTime,
                endTime,
                portalVisualEndTime);
        }

        private float ResolvePitPlaybackStart(
            float eventStartTime,
            PitStopSequence sequence)
        {
            if (sequence == null)
                return eventStartTime;

            float focusTime = sequence.FocusTime;
            float approachTarget = sequence.IsDriveThrough
                ? focusTime - pitVisibleApproachSeconds
                : sequence.ServiceStartTime -
                  pitVisibleApproachSeconds;
            return Mathf.Clamp(
                approachTarget,
                eventStartTime,
                focusTime - 0.05f);
        }

        private float ResolvePitPlaybackEnd(
            float eventEndTime,
            PitStopSequence sequence)
        {
            if (sequence == null)
                return eventEndTime;

            float exitStart = sequence.IsDriveThrough
                ? sequence.FocusTime
                : sequence.ReleaseEndTime;
            return Mathf.Clamp(
                exitStart + Mathf.Max(0f, pitVisibleExitSeconds),
                sequence.FocusTime + 0.05f,
                eventEndTime);
        }

        private float ProjectSourcePathDistance(
            Vector3 position,
            float minimumPathDistance,
            ref int segmentHint)
        {
            float closestSqrDistance = float.PositiveInfinity;
            float closestPathDistance = minimumPathDistance;
            int closestSegment = segmentHint;

            int firstSegment = Mathf.Max(0, segmentHint - 1);
            for (int i = firstSegment; i < mappedPath.Count - 1; i++)
            {
                Vector3 start = mappedPath[i];
                Vector3 segment = mappedPath[i + 1] - start;
                float segmentSqrLength = segment.sqrMagnitude;
                if (segmentSqrLength <= 0.000001f)
                    continue;

                float interpolation = Mathf.Clamp01(
                    Vector3.Dot(position - start, segment) /
                    segmentSqrLength);
                float pathDistance =
                    mappedPathDistances[i] +
                    Mathf.Sqrt(segmentSqrLength) * interpolation;
                if (pathDistance + 0.00001f < minimumPathDistance)
                    continue;

                Vector3 projected = start + segment * interpolation;
                float sqrDistance =
                    Vector3.SqrMagnitude(position - projected);
                if (sqrDistance >= closestSqrDistance)
                    continue;

                closestSqrDistance = sqrDistance;
                closestPathDistance = pathDistance;
                closestSegment = i;
            }

            segmentHint = closestSegment;
            return closestPathDistance;
        }

        internal bool TryGetSourceLongitudinalAtTime(
            int driverNumber,
            float time,
            out float longitudinal)
        {
            longitudinal = 0f;
            if (!eventSamples.TryGetValue(
                    driverNumber,
                    out List<LocationSample> samples) ||
                !eventLongitudinals.TryGetValue(
                    driverNumber,
                    out List<float> longitudinals) ||
                samples.Count < 2 ||
                longitudinals.Count != samples.Count)
            {
                return false;
            }

            if (time <= samples[0].t)
            {
                longitudinal = longitudinals[0];
                return true;
            }

            int last = samples.Count - 1;
            if (time >= samples[last].t)
            {
                longitudinal = longitudinals[last];
                return true;
            }

            int low = 0;
            int high = last;
            while (low + 1 < high)
            {
                int middle = (low + high) / 2;
                if (samples[middle].t <= time)
                    low = middle;
                else
                    high = middle;
            }

            float duration = Mathf.Max(
                0.001f,
                samples[high].t - samples[low].t);
            float interpolation = Mathf.Clamp01(
                (time - samples[low].t) / duration);
            longitudinal = Mathf.Lerp(
                longitudinals[low],
                longitudinals[high],
                interpolation);
            return true;
        }

        public bool TryConfigureRoomStageInteraction(
            Vector3 eventLocalFocus,
            float physicalWidth = 0.6f)
        {
            if (stageInteractionCollider == null ||
                PresentationRoot == null)
            {
                return false;
            }

            Vector3 worldScale = PresentationRoot.lossyScale;
            float uniformWorldScale = Mathf.Max(
                Mathf.Abs(worldScale.x),
                Mathf.Abs(worldScale.y),
                Mathf.Abs(worldScale.z));
            if (uniformWorldScale <= 0.000001f)
                return false;

            float localWidth =
                Mathf.Max(0.1f, physicalWidth) /
                uniformWorldScale;
            float localHeight = 0.12f / uniformWorldScale;
            stageInteractionCollider.center =
                eventLocalFocus +
                Vector3.up * localHeight * 0.5f;
            stageInteractionCollider.size =
                new Vector3(
                    localWidth,
                    localHeight,
                    localWidth);
            return true;
        }

        private float ResolveOrderingTransitionTime()
        {
            if (battleSequence != null && battleSequence.IsValid)
                return battleSequence.FocusTime;

            return ResolveOrderingTransitionTime(
                currentEvent,
                timeline.StartTime,
                timeline.RaceEndTime);
        }

        private float ResolveBattleReferenceVehicleLength(
            ReplayEventDto definition)
        {
            int[] drivers = definition != null
                ? definition.driverNumbers
                : null;
            if (drivers == null || drivers.Length == 0)
                return 0.001f;

            float length = 0f;
            for (int i = 0;
                 i < Mathf.Min(2, drivers.Length);
                 i++)
            {
                if (eventCars.TryEnsureVisualSize(
                        drivers[i],
                        out _,
                        out float visualLength))
                {
                    length = Mathf.Max(length, visualLength);
                }
            }

            return Mathf.Max(0.001f, length);
        }

        private static void LogBattleSequence(
            ReplayEventDto definition,
            OvertakeBattleSequence sequence)
        {
            if (definition == null || sequence == null)
                return;

            string exchanges = string.Empty;
            for (int i = 0; i < sequence.Exchanges.Count; i++)
            {
                OvertakeBattleExchange exchange =
                    sequence.Exchanges[i];
                if (i > 0)
                    exchanges += ", ";
                exchanges +=
                    $"{exchange.overtaker}>{exchange.defender}@{exchange.anchorTime:0.000}";
            }

            Debug.Log(
                $"[ShowcaseBattle] event={definition.eventId}, " +
                $"reconstructed={sequence.Reconstructed}, " +
                $"window={sequence.StartTime:0.000}-{sequence.EndTime:0.000}, " +
                $"exchanges=[{exchanges}]");
        }

        private float ResolveOrderingTransitionTime(
            ReplayEventDto definition,
            float startTime,
            float endTime)
        {
            float fallback = definition != null
                ? definition.anchorTime
                : Mathf.Lerp(startTime, endTime, 0.5f);
            int[] drivers = definition != null
                ? definition.driverNumbers
                : null;
            if (drivers == null || drivers.Length < 2)
                return fallback;

            const int sampleCount = 96;
            float previousTime = startTime;
            if (!TryGetSourceGap(
                    drivers[0],
                    drivers[1],
                    previousTime,
                    out float previousGap))
            {
                return fallback;
            }

            for (int i = 1; i <= sampleCount; i++)
            {
                float time = Mathf.Lerp(
                    startTime,
                    endTime,
                    i / (float)sampleCount);
                if (!TryGetSourceGap(
                        drivers[0],
                        drivers[1],
                        time,
                        out float gap))
                {
                    continue;
                }

                if (GapOrder(previousGap) != 0 &&
                    GapOrder(gap) != 0 &&
                    GapOrder(previousGap) != GapOrder(gap))
                {
                    float blend =
                        Mathf.Abs(previousGap) /
                        (Mathf.Abs(previousGap) + Mathf.Abs(gap));
                    return Mathf.Lerp(previousTime, time, blend);
                }

                previousTime = time;
                previousGap = gap;
            }

            return fallback;
        }

        private bool TryGetSourceGap(
            int firstDriver,
            int secondDriver,
            float time,
            out float gap)
        {
            gap = 0f;
            if (!TryGetSourceLongitudinalAtTime(
                    firstDriver,
                    time,
                    out float first) ||
                !TryGetSourceLongitudinalAtTime(
                    secondDriver,
                    time,
                    out float second))
            {
                return false;
            }

            gap = first - second;
            return true;
        }

        private static int GapOrder(float gap)
        {
            if (gap > 0.0001f)
                return 1;
            if (gap < -0.0001f)
                return -1;
            return 0;
        }

        private void AddMappedPathPoint(
            List<LocationSample> samples,
            float time,
            ref Vector3 last,
            ref bool hasLast)
        {
            if (!TryGetMappedPosition(samples, time, out Vector3 position) ||
                hasLast && Vector3.SqrMagnitude(position - last) < 0.000004f)
                return;

            mappedPath.Add(position);
            last = position;
            hasLast = true;
        }

        private bool TryGetMappedPosition(
            List<LocationSample> samples,
            float time,
            out Vector3 position)
        {
            position = default;
            if (!TryGetSamplePair(samples, time, out LocationSample a, out LocationSample b))
                return false;

            if (!eventCars.TryGetMappedPosition(a, out Vector3 positionA) ||
                !eventCars.TryGetMappedPosition(b, out Vector3 positionB))
                return false;

            float duration = Mathf.Max(0.001f, b.t - a.t);
            float interpolation = Mathf.Clamp01((time - a.t) / duration);
            position = Vector3.Lerp(positionA, positionB, interpolation);
            return true;
        }

        private void GetPathFrame(
            out Vector3 center,
            out Quaternion sourceToLocalRotation)
        {
            Bounds bounds = new Bounds(
                presentationPath[0],
                Vector3.zero);
            for (int i = 1; i < presentationPath.Count; i++)
                bounds.Encapsulate(presentationPath[i]);

            center = bounds.center;
            Vector3 forward =
                presentationPath[presentationPath.Count - 1] -
                presentationPath[0];
            forward.y = 0f;

            if (forward.sqrMagnitude < 0.000001f)
            {
                for (int i = 1; i < presentationPath.Count; i++)
                {
                    Vector3 candidate =
                        presentationPath[i] -
                        presentationPath[i - 1];
                    candidate.y = 0f;
                    if (candidate.sqrMagnitude > forward.sqrMagnitude)
                        forward = candidate;
                }
            }

            sourceToLocalRotation = forward.sqrMagnitude > 0.000001f
                ? Quaternion.Inverse(Quaternion.LookRotation(forward.normalized, Vector3.up))
                : Quaternion.identity;
        }

        private void CreateStageRoot()
        {
            stageRoot = new GameObject("EventReplayStage");
            TryRestoreTableRelativePose();
        }

        private void ResolveStagePose(out Vector3 position, out Quaternion rotation)
        {
            if (stageAnchor != null)
            {
                position = stageAnchor.position;
                rotation = stageAnchor.rotation;
                return;
            }

            Transform cameraTransform = Camera.main != null ? Camera.main.transform : null;
            Transform trackTransform = player.GetTrackPlacementTransform();
            Vector3 flatForward = cameraTransform != null
                ? Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized
                : Vector3.forward;
            Vector3 flatRight = cameraTransform != null
                ? Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized
                : Vector3.right;

            if (flatForward.sqrMagnitude < 0.001f)
                flatForward = Vector3.forward;
            if (flatRight.sqrMagnitude < 0.001f)
                flatRight = Vector3.right;

            if (trackTransform != null)
            {
                position = trackTransform.position + flatRight * stageSideOffset + Vector3.up * stageHeightOffset;
            }
            else if (cameraTransform != null)
            {
                position = cameraTransform.position + flatForward * stageForwardDistance;
                position.y += stageHeightOffset - 0.5f;
            }
            else
            {
                position = transform.position + transform.forward * stageForwardDistance;
                position.y += stageHeightOffset;
            }

            rotation = Quaternion.LookRotation(flatRight, Vector3.up);
        }

        private void CreateRoad(
            Vector3 center,
            Quaternion sourceToLocalRotation,
            bool createEdges)
        {
            int count = presentationPath.Count;
            Vector3[] vertices = new Vector3[count * 2];
            Vector2[] uv = new Vector2[count * 2];
            int[] triangles = new int[(count - 1) * 6];
            Vector3[] localPath = new Vector3[count];

            for (int i = 0; i < count; i++)
                localPath[i] =
                    sourceToLocalRotation *
                    (presentationPath[i] - center);

            ExtendRoadEnd(localPath);

            for (int i = 0; i < count; i++)
            {
                Vector3 before = localPath[Mathf.Max(0, i - 1)];
                Vector3 after = localPath[Mathf.Min(count - 1, i + 1)];
                Vector3 tangent = after - before;
                tangent.y = 0f;
                if (tangent.sqrMagnitude < 0.000001f)
                    tangent = Vector3.forward;

                Vector3 side = Vector3.Cross(Vector3.up, tangent.normalized);
                vertices[i * 2] = localPath[i] - side * roadWidth * 0.5f;
                vertices[i * 2 + 1] = localPath[i] + side * roadWidth * 0.5f;
                float v = i / (float)(count - 1);
                uv[i * 2] = new Vector2(0f, v);
                uv[i * 2 + 1] = new Vector2(1f, v);

                if (i >= count - 1)
                    continue;

                int triangle = i * 6;
                int vertex = i * 2;
                triangles[triangle] = vertex;
                triangles[triangle + 1] = vertex + 2;
                triangles[triangle + 2] = vertex + 1;
                triangles[triangle + 3] = vertex + 1;
                triangles[triangle + 4] = vertex + 2;
                triangles[triangle + 5] = vertex + 3;
            }

            roadMesh = new Mesh { name = "EventRoadMesh" };
            roadMesh.vertices = vertices;
            roadMesh.uv = uv;
            roadMesh.triangles = triangles;
            roadMesh.RecalculateNormals();
            roadMesh.RecalculateBounds();

            GameObject road = new GameObject("EventRoad", typeof(MeshFilter), typeof(MeshRenderer));
            road.transform.SetParent(stageRoot.transform, false);
            road.GetComponent<MeshFilter>().sharedMesh = roadMesh;
            roadMaterial = CreateMaterial(new Color(0.12f, 0.13f, 0.15f, 1f));
            MeshRenderer renderer = road.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = roadMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            if (createEdges)
            {
                edgeMaterial = CreateMaterial(
                    new Color(0.8f, 0.08f, 0.04f, 1f));
                leftRoadEdge =
                    CreateEdge(
                        "LeftEdge",
                        vertices,
                        0,
                        edgeMaterial);
                rightRoadEdge =
                    CreateEdge(
                        "RightEdge",
                        vertices,
                        1,
                        edgeMaterial);
            }

        }

        private bool CreateActualTrackRegion(
            Vector3 center,
            Quaternion sourceToLocalRotation,
            out Bounds stageBounds)
        {
            float effectivePadding =
                ResolveTrackRegionPadding();
            trackSegment = new EventTrackSegment();
            bool created = trackSegment.Build(
                stageRoot.transform,
                player.GetTrackPlacementTransform(),
                presentationPath,
                center,
                sourceToLocalRotation,
                effectivePadding,
                out stageBounds);
            if (created)
                return true;

            trackSegment.Clear();
            trackSegment = null;
            Debug.LogWarning(
                "[EventReplay] Actual track geometry was unavailable; using the generated road fallback.",
                this);
            return false;
        }

        private bool BuildFallbackCorridorPath(
            Vector3 center,
            Quaternion sourceToLocalRotation)
        {
            fallbackCorridorPath.Clear();
            TrackCalibration calibration =
                player != null
                    ? player.trackCalibration
                    : null;
            if (calibration != null &&
                calibration.active &&
                calibration.mappingMode ==
                TrackCalibration.MappingMode.Route &&
                calibration.points != null &&
                calibration.points.Length >= 2)
            {
                float scale = calibration.OutputScale;
                for (int i = 0;
                     i < calibration.points.Length;
                     i++)
                {
                    Vector3 target =
                        calibration.points[i]
                            .targetLocalPosition;
                    target = new Vector3(
                        target.x * scale,
                        target.y * scale +
                        calibration.heightOffset,
                        target.z * scale);
                    fallbackCorridorPath.Add(
                        sourceToLocalRotation *
                        (target - center));
                }

                return calibration.loopMappingSegments;
            }

            for (int i = 0; i < mappedPath.Count; i++)
            {
                fallbackCorridorPath.Add(
                    sourceToLocalRotation *
                    (mappedPath[i] - center));
            }

            return false;
        }

        private void BuildSafetyApronPath(
            Vector3 center,
            Quaternion sourceToLocalRotation)
        {
            safetyApronPath.Clear();
            for (int i = 0; i < presentationPath.Count; i++)
            {
                safetyApronPath.Add(
                    sourceToLocalRotation *
                    (presentationPath[i] - center));
            }
        }

        private void CreateSafetyApron()
        {
            if (stageRoot == null ||
                IsPitStopDefinition(currentEvent) ||
                trackSegment == null ||
                safetyApronMesh != null ||
                safetyApronPath.Count < 2 ||
                eventCars == null)
            {
                return;
            }

            float vehicleWidth = 0f;
            foreach (int driver in eventDrivers)
            {
                if (!eventCars.TryGetVisualTransform(
                        driver,
                        out Transform visual) ||
                    visual == null ||
                    !visual.TryGetComponent(
                        out ReplayCarView car))
                {
                    continue;
                }

                vehicleWidth = Mathf.Max(
                    vehicleWidth,
                    car.GetVisualWidth());
            }

            if (vehicleWidth <= 0f)
                return;

            bool hasDrivableEdges =
                trackSegment.TryBuildDrivableEdges(
                    safetyApronPath,
                    vehicleWidth,
                    drivableLeftEdge,
                    drivableRightEdge);
            bool hasReliableRoadEdges =
                !hasDrivableEdges &&
                trackSegment.TryBuildReliableRoadEdges(
                    safetyApronPath,
                    vehicleWidth,
                    drivableLeftEdge,
                    drivableRightEdge);
            if (hasDrivableEdges ||
                hasReliableRoadEdges)
            {
                eventCars.SetActualOvertakeCorridor(
                    safetyApronPath,
                    drivableLeftEdge,
                    drivableRightEdge,
                    false);
                eventCars.ResetResolvedOvertakeSides();
                ApplyCars();
                if (hasReliableRoadEdges)
                {
                    Debug.LogWarning(
                        "[EventReplay] Drivable-only boundaries were incomplete; overtake motion is using detected road-surface boundaries.",
                        this);
                }
            }
            else
            {
                Debug.LogWarning(
                    "[EventReplay] Exact drivable boundaries were unavailable; overtake motion is using the event roadWidth fallback.",
                    this);
            }

            bool hasRoadEdges =
                trackSegment.TryBuildSafetyApronEdges(
                    safetyApronPath,
                    vehicleWidth,
                    safetyApronLeftEdge,
                    safetyApronRightEdge);
            if (!hasRoadEdges)
            {
                Debug.LogWarning(
                    "[EventReplay] Safety apron was skipped because actual road edges were unavailable.",
                    this);
                return;
            }

            if (motionEvent == null ||
                !eventCars.TryGetResolvedOvertakeSide(
                    motionEvent,
                    out int passingSide) ||
                !TryGetSafetyApronDistances(
                    out float motionStartDistance,
                    out float approachEndDistance,
                    out float returnStartDistance,
                    out float motionEndDistance))
            {
                Debug.LogWarning(
                    "[EventReplay] Safety apron was skipped because the resolved overtake path was unavailable.",
                    this);
                return;
            }

            int count = safetyApronPath.Count;
            Vector3[] vertices = new Vector3[count * 2];
            Vector2[] uv = new Vector2[count * 2];
            int[] triangles = new int[(count - 1) * 6];
            Vector3 surfaceOffset =
                Vector3.down *
                Mathf.Max(
                    0f,
                    safetyApronSurfaceOffset);

            for (int i = 0; i < count; i++)
            {
                Vector3 left =
                    safetyApronLeftEdge[i];
                Vector3 right =
                    safetyApronRightEdge[i];
                float extensionWeight =
                    SafetyApronExtensionWeight(
                        presentationPathDistances[i],
                        motionStartDistance,
                        approachEndDistance,
                        returnStartDistance,
                        motionEndDistance);
                if (extensionWeight > 0f)
                {
                    Vector3 side = right - left;
                    side.y = 0f;
                    if (side.sqrMagnitude >
                        0.000001f)
                    {
                        side.Normalize();
                        if (passingSide > 0)
                        {
                            right +=
                                side *
                                vehicleWidth *
                                extensionWeight;
                        }
                        else
                        {
                            left -=
                                side *
                                vehicleWidth *
                                extensionWeight;
                        }
                    }
                }

                vertices[i * 2] =
                    left + surfaceOffset;
                vertices[i * 2 + 1] =
                    right + surfaceOffset;
                float v = i / (float)(count - 1);
                uv[i * 2] = new Vector2(0f, v);
                uv[i * 2 + 1] = new Vector2(1f, v);

                if (i >= count - 1)
                    continue;

                int triangle = i * 6;
                int vertex = i * 2;
                triangles[triangle] = vertex;
                triangles[triangle + 1] = vertex + 2;
                triangles[triangle + 2] = vertex + 1;
                triangles[triangle + 3] = vertex + 1;
                triangles[triangle + 4] = vertex + 2;
                triangles[triangle + 5] = vertex + 3;
            }

            safetyApronMesh = new Mesh
            {
                name = "EventSafetyApronMesh"
            };
            safetyApronMesh.vertices = vertices;
            safetyApronMesh.uv = uv;
            safetyApronMesh.triangles = triangles;
            safetyApronMesh.RecalculateNormals();
            safetyApronMesh.RecalculateBounds();

            GameObject apron = new GameObject(
                "EventRoadSafetyApron",
                typeof(MeshFilter),
                typeof(MeshRenderer));
            apron.transform.SetParent(
                stageRoot.transform,
                false);
            apron.GetComponent<MeshFilter>()
                .sharedMesh = safetyApronMesh;
            safetyApronMaterial =
                CreateMaterial(safetyApronColor);
            MeshRenderer renderer =
                apron.GetComponent<MeshRenderer>();
            renderer.sharedMaterial =
                safetyApronMaterial;
            renderer.shadowCastingMode =
                ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private bool TryGetSafetyApronDistances(
            out float motionStart,
            out float approachEnd,
            out float returnStart,
            out float motionEnd)
        {
            motionStart = 0f;
            approachEnd = 0f;
            returnStart = 0f;
            motionEnd = 0f;
            if (motionEvent == null ||
                eventOvertakeSettings == null ||
                referenceDriverNumber <= 0)
            {
                return false;
            }

            if (battleSequence != null &&
                battleSequence.IsValid &&
                battleSequence.Exchanges.Count > 0)
            {
                OvertakeBattleExchange firstExchange =
                    battleSequence.Exchanges[0];
                OvertakeBattleExchange finalExchange =
                    battleSequence.Exchanges[
                        battleSequence.Exchanges.Count - 1];
                float battleReturnStart = Mathf.Max(
                    finalExchange.anchorTime,
                    finalExchange.confirmedTime);
                return TryGetSourceLongitudinalAtTime(
                        referenceDriverNumber,
                        battleSequence.MotionStartTime,
                        out motionStart) &&
                    TryGetSourceLongitudinalAtTime(
                        referenceDriverNumber,
                        firstExchange.anchorTime,
                        out approachEnd) &&
                    TryGetSourceLongitudinalAtTime(
                        referenceDriverNumber,
                        battleReturnStart,
                        out returnStart) &&
                    TryGetSourceLongitudinalAtTime(
                        referenceDriverNumber,
                        battleSequence.EndTime,
                        out motionEnd) &&
                    motionEnd - motionStart > 0.0001f;
            }

            float duration =
                motionEvent.endTime -
                motionEvent.startTime;
            if (duration <= 0f)
                return false;

            float totalPortion = Mathf.Max(
                0.0001f,
                eventOvertakeSettings.approachPortion +
                eventOvertakeSettings.parallelPortion +
                eventOvertakeSettings.returnPortion);
            float approachProgress =
                eventOvertakeSettings.approachPortion /
                totalPortion;
            float returnProgress =
                (eventOvertakeSettings.approachPortion +
                 eventOvertakeSettings.parallelPortion) /
                totalPortion;
            float anchorProgress = Mathf.Clamp01(
                (motionEvent.anchorTime -
                 motionEvent.startTime) /
                duration);
            approachProgress = Mathf.Clamp(
                Mathf.Min(
                    approachProgress,
                    anchorProgress),
                0.0001f,
                0.9998f);
            returnProgress = Mathf.Clamp(
                Mathf.Max(
                    returnProgress,
                    anchorProgress),
                approachProgress + 0.0001f,
                0.9999f);

            return TryGetSourceLongitudinalAtTime(
                    referenceDriverNumber,
                    motionEvent.startTime,
                    out motionStart) &&
                TryGetSourceLongitudinalAtTime(
                    referenceDriverNumber,
                    Mathf.Lerp(
                        motionEvent.startTime,
                        motionEvent.endTime,
                        approachProgress),
                    out approachEnd) &&
                TryGetSourceLongitudinalAtTime(
                    referenceDriverNumber,
                    Mathf.Lerp(
                        motionEvent.startTime,
                        motionEvent.endTime,
                        returnProgress),
                    out returnStart) &&
                TryGetSourceLongitudinalAtTime(
                    referenceDriverNumber,
                    motionEvent.endTime,
                    out motionEnd) &&
                motionEnd - motionStart >
                0.0001f;
        }

        private static float SafetyApronExtensionWeight(
            float distance,
            float motionStart,
            float approachEnd,
            float returnStart,
            float motionEnd)
        {
            if (distance <= motionStart ||
                distance >= motionEnd)
            {
                return 0f;
            }

            if (distance < approachEnd)
            {
                return Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(
                        motionStart,
                        approachEnd,
                        distance));
            }

            if (distance <= returnStart)
                return 1f;

            return Mathf.SmoothStep(
                1f,
                0f,
                Mathf.InverseLerp(
                    returnStart,
                    motionEnd,
                    distance));
        }

        private float ResolveTrackRegionPadding()
        {
            float pathLength = 0f;
            for (int i = 1; i < presentationPath.Count; i++)
            {
                Vector3 segment =
                    presentationPath[i] -
                    presentationPath[i - 1];
                segment.y = 0f;
                pathLength += segment.magnitude;
            }

            float relativePadding = Mathf.Max(
                MinimumTrackRegionPadding,
                pathLength * RelativeTrackRegionPadding);
            return Mathf.Min(
                trackRegionPadding,
                relativePadding);
        }

        private void ExtendRoadEnd(Vector3[] localPath)
        {
            if (localPath == null || localPath.Length < 2 || roadEndPadding <= 0f)
                return;

            int last = localPath.Length - 1;
            Vector3 direction = localPath[last] - localPath[last - 1];
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.000001f)
                return;

            localPath[last] += direction.normalized * roadEndPadding;
        }

        private LineRenderer CreateEdge(
            string name,
            Vector3[] roadVertices,
            int side,
            Material material)
        {
            GameObject edge = new GameObject(name, typeof(LineRenderer));
            edge.transform.SetParent(stageRoot.transform, false);
            LineRenderer line = edge.GetComponent<LineRenderer>();
            line.useWorldSpace = false;
            int edgePointCount = roadVertices.Length / 2;
            line.positionCount = edgePointCount;
            line.widthMultiplier = roadWidth * 0.06f;
            line.numCapVertices = 2;
            line.sharedMaterial = material;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;

            for (int i = 0; i < edgePointCount; i++)
                line.SetPosition(i, roadVertices[i * 2 + side] + Vector3.up * 0.001f);

            return line;
        }

        private void ConfigureStageInteraction(Bounds bounds)
        {
            BoxCollider collider = stageRoot.AddComponent<BoxCollider>();
            stageInteractionCollider = collider;
            bounds.Expand(new Vector3(0f, 0.04f, 0f));
            collider.center = bounds.center;
            collider.size = bounds.size;
            stageInteractionDefaultCenter = collider.center;
            stageInteractionDefaultSize = collider.size;
            stageInteractionDefaultsCaptured = true;

            Rigidbody body = stageRoot.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            XRGrabInteractable grab = stageRoot.AddComponent<XRGrabInteractable>();
            if (!grab.colliders.Contains(collider))
                grab.colliders.Add(collider);

            grab.useDynamicAttach = true;
            grab.matchAttachPosition = true;
            grab.matchAttachRotation = false;
            grab.trackRotation = false;
            grab.snapToColliderVolume = false;
            grab.attachEaseInTime = 0f;
            grab.throwOnDetach = false;

            stageRoot.AddComponent<WorldGrabTarget>();
            WorldGrabPolicy policy = stageRoot.AddComponent<WorldGrabPolicy>();
            policy.UseGrabPoint(grab, stageRoot.transform);
            stageRoot.AddComponent<ScaleController>();
        }

        private void ApplyCars()
        {
            if (!isActive || eventCars == null)
                return;

            float replayTime = timeline.CurrentTime;
            eventCars.Show(
                eventSamples,
                eventIndices,
                replayTime,
                null,
                eventDrivers);
            if (IsPitStopDefinition(currentEvent))
            {
                pitStopPresentation?.Apply(
                    replayTime,
                    timeline.IsPlaying &&
                    !showcaseTransitionHeld,
                    showcaseTimelineRevision);
            }
            else if (IsCollisionEvent(currentEvent))
            {
                FitCollisionPresentationStage();
                if (collisionIncidentPresentation == null)
                    UpdateCollisionShowcase(replayTime);
            }
            else
            {
                UpdateOvertakeCompletion(replayTime);
                eventCars.UpdateOvertakeCompletionVfx(
                    replayTime);
            }
        }

        private void UpdateOvertakeCompletion(
            float replayTime)
        {
            if (IsCollisionEvent(currentEvent))
                return;

            if (battleSequence != null && battleSequence.IsValid)
            {
                UpdateBattleVfxPlayback(replayTime);
                return;
            }

            int[] drivers = motionEvent != null
                ? motionEvent.driverNumbers
                : null;
            if (drivers == null ||
                drivers.Length < 2 ||
                !TryGetSourceLongitudinalAtTime(
                    drivers[0],
                    replayTime,
                    out float overtakerProgress) ||
                !TryGetSourceLongitudinalAtTime(
                    drivers[1],
                    replayTime,
                    out float defenderProgress) ||
                !eventCars.TryGetVisualLength(
                    drivers[0],
                    out float overtakerLength) ||
                !eventCars.TryGetVisualLength(
                    drivers[1],
                    out float defenderLength))
            {
                return;
            }

            float clearanceDistance =
                overtakerProgress -
                defenderProgress -
                (overtakerLength + defenderLength) *
                0.5f;
            float centerLeadDistance =
                overtakerProgress -
                defenderProgress;
            float referenceVehicleLength =
                Mathf.Max(
                    overtakerLength,
                    defenderLength);
            bool orderingConfirmed =
                currentEvent != null &&
                replayTime >= currentEvent.anchorTime;
            OvertakeCompletionResult result =
                completionDetector.Update(
                    replayTime,
                    clearanceDistance,
                    centerLeadDistance,
                    referenceVehicleLength,
                    orderingConfirmed);
            if (result == OvertakeCompletionResult.Reset ||
                result == OvertakeCompletionResult.Suppressed)
            {
                eventCars.ResetOvertakeCompletionVfx();
            }
            else if (
                result ==
                OvertakeCompletionResult.Triggered)
            {
                eventCars.TriggerOvertakeCompletionVfx(
                    drivers[0],
                    replayTime);
            }
        }

        private void UpdateBattleVfxPlayback(float replayTime)
        {
            if (battleSequence == null ||
                !battleSequence.IsValid ||
                eventCars == null)
            {
                return;
            }

            bool hasPrevious =
                !float.IsNaN(lastBattleVfxReplayTime);
            float seekThreshold =
                player != null &&
                player.overtakeCompletionVfx != null
                    ? Mathf.Max(
                        0.05f,
                        player.overtakeCompletionVfx
                            .seekResetThresholdSeconds)
                    : 0.5f;
            bool discontinuity = hasPrevious &&
                (replayTime < lastBattleVfxReplayTime ||
                 replayTime - lastBattleVfxReplayTime >
                 seekThreshold);
            if (!hasPrevious || discontinuity)
            {
                ResetBattleVfxPlayback(replayTime);
                return;
            }

            while (nextBattleExchangeIndex <
                       battleSequence.Exchanges.Count &&
                   battleSequence.Exchanges[
                           nextBattleExchangeIndex]
                       .anchorTime <= replayTime)
            {
                OvertakeBattleExchange exchange =
                    battleSequence.Exchanges[
                        nextBattleExchangeIndex];
                if (exchange.anchorTime >
                    lastBattleVfxReplayTime + 0.0001f)
                {
                    TriggerBattleExchangeVfx(
                        exchange,
                        nextBattleExchangeIndex,
                        replayTime);
                }

                nextBattleExchangeIndex++;
            }

            OvertakeBattleExchange finalExchange =
                battleSequence.Exchanges[
                    battleSequence.Exchanges.Count - 1];
            float finalConfirmationTime = Mathf.Max(
                finalExchange.anchorTime,
                finalExchange.confirmedTime);
            if (replayTime >= finalConfirmationTime)
                battleCompletionConfirmed = true;

            float victoryTime =
                finalConfirmationTime +
                Mathf.Max(0f, battleVictoryDelaySeconds);
            if (!battleVictoryTriggered &&
                replayTime >= victoryTime &&
                lastBattleVfxReplayTime < victoryTime)
            {
                battleVictoryTriggered = true;
                string winner = eventCars.GetDriverLabel(
                    battleSequence.FinalLeader);
                eventCars.TriggerOvertakeCompletionVfx(
                    battleSequence.FinalLeader,
                    replayTime,
                    $"BATTLE WON\n{winner}",
                    1.4f,
                    OvertakeCompletionVfxProfile.Victory);
            }

            lastBattleVfxReplayTime = replayTime;
        }

        private void TriggerBattleExchangeVfx(
            OvertakeBattleExchange exchange,
            int exchangeIndex,
            float replayTime)
        {
            string driver = eventCars.GetDriverLabel(
                exchange.overtaker);
            string text;
            float intensity;
            OvertakeCompletionVfxProfile profile;
            switch (exchange.kind)
            {
                case OvertakeBattleExchangeKind.Counter:
                    text = $"{driver}\nCOUNTER";
                    intensity = 1.12f;
                    profile =
                        OvertakeCompletionVfxProfile.Counter;
                    break;
                case OvertakeBattleExchangeKind.Repass:
                    text =
                        $"{driver}\nREPASS ×{exchangeIndex}";
                    intensity = Mathf.Min(
                        1.35f,
                        1.18f + exchangeIndex * 0.05f);
                    profile =
                        OvertakeCompletionVfxProfile.Repass;
                    break;
                default:
                    text = $"{driver}\nPASS";
                    intensity = 1f;
                    profile =
                        OvertakeCompletionVfxProfile.Standard;
                    break;
            }

            eventCars.TriggerOvertakeCompletionVfx(
                exchange.overtaker,
                replayTime,
                text,
                intensity,
                profile);
        }

        private void ResetBattleVfxPlayback(float replayTime)
        {
            nextBattleExchangeIndex = 0;
            battleCompletionConfirmed = false;
            battleVictoryTriggered = false;
            lastBattleVfxReplayTime = replayTime;
            eventCars?.ResetOvertakeCompletionVfx();
            if (battleSequence == null ||
                !battleSequence.IsValid)
            {
                return;
            }

            while (nextBattleExchangeIndex <
                       battleSequence.Exchanges.Count &&
                   battleSequence.Exchanges[
                           nextBattleExchangeIndex]
                       .anchorTime <= replayTime)
            {
                nextBattleExchangeIndex++;
            }

            OvertakeBattleExchange finalExchange =
                battleSequence.Exchanges[
                    battleSequence.Exchanges.Count - 1];
            float finalConfirmationTime = Mathf.Max(
                finalExchange.anchorTime,
                finalExchange.confirmedTime);
            battleCompletionConfirmed =
                replayTime >= finalConfirmationTime;
            battleVictoryTriggered =
                replayTime >= finalConfirmationTime +
                Mathf.Max(0f, battleVictoryDelaySeconds);
        }

        private void ResetIndices()
        {
            if (eventDrivers.Count == 0)
                return;

            foreach (int driver in eventDrivers)
                eventIndices[driver] = 0;
        }

        private List<LocationSample> FindReferenceSamples()
        {
            if (referenceDriverNumber > 0 &&
                eventSamples.TryGetValue(referenceDriverNumber, out List<LocationSample> reference))
                return reference;

            foreach (int driver in eventDrivers)
            {
                if (eventSamples.TryGetValue(driver, out List<LocationSample> samples))
                    return samples;
            }

            return null;
        }

        private void DestroyStage(
            bool restoreTableTrack = true)
        {
            if (restoreTableTrack)
                RestoreTableTrackRendering();

            if (stageRoot != null)
                stageRoot.SetActive(false);

            isLoading = false;
            isActive = false;
            showcaseTransitionHeld = false;
            timeline.Pause();
            ClearCollisionIncidentPresentation();
            DestroyCollisionShowcase();
            eventAudio?.Clear();
            pitStopPresentation?.Clear();
            completionDetector.Reset();
            nextBattleExchangeIndex = 0;
            lastBattleVfxReplayTime = float.NaN;
            battleCompletionConfirmed = false;
            battleVictoryTriggered = false;
            showcasePlaybackWindow = default;
            showcasePresentationEndTime = float.NaN;
            eventCars?.Clear();
            eventAudio = null;
            eventCars = null;
            pitStopPresentation = null;
            showcaseAudioFocusDriver = 0;
            showcasePlaybackSpeedMultiplier = 1f;

            if (stageRoot != null)
                Destroy(stageRoot);
            trackSegment?.Clear();
            if (roadMesh != null)
                Destroy(roadMesh);
            if (roadMaterial != null)
                Destroy(roadMaterial);
            if (edgeMaterial != null)
                Destroy(edgeMaterial);
            if (safetyApronMesh != null)
                Destroy(safetyApronMesh);
            if (safetyApronMaterial != null)
                Destroy(safetyApronMaterial);

            stageRoot = null;
            stageInteractionCollider = null;
            stageInteractionDefaultCenter = Vector3.zero;
            stageInteractionDefaultSize = Vector3.zero;
            stageInteractionDefaultsCaptured = false;
            trackSegment = null;
            roadMesh = null;
            leftRoadEdge = null;
            rightRoadEdge = null;
            roadMaterial = null;
            edgeMaterial = null;
            safetyApronMesh = null;
            safetyApronMaterial = null;
            currentEvent = null;
            motionEvent = null;
            battleSequence = null;
            pitStopSequence = null;
            eventOvertakeSettings = null;
            eventSamples.Clear();
            eventIndices.Clear();
            eventDrivers.Clear();
            referenceDriverNumber = 0;
            mappedPath.Clear();
            mappedPathDistances.Clear();
            presentationPath.Clear();
            presentationPathDistances.Clear();
            fallbackCorridorPath.Clear();
            safetyApronPath.Clear();
            safetyApronLeftEdge.Clear();
            safetyApronRightEdge.Clear();
            drivableLeftEdge.Clear();
            drivableRightEdge.Clear();
            eventLongitudinals.Clear();
            eventSpaceCenter = Vector3.zero;
            sourceToEventRotation = Quaternion.identity;
            sourceGeometryRevision++;
        }

        private void RestoreReplay()
        {
            if (!hasSnapshot || player == null)
                return;

            if (!player.isActiveAndEnabled ||
                !player.gameObject.activeInHierarchy)
            {
                hasSnapshot = false;
                return;
            }

            player.SetSpeed(snapshot.Speed);
            player.Seek(snapshot.Time);
            player.SetSelectedDriver(snapshot.SelectedDriver);
            if (snapshot.WasPlaying)
                player.Play();
            else
                player.Pause();

            hasSnapshot = false;
        }

        private ReplayEventDto CreatePresentationEvent(ReplayEventDto source)
        {
            if (source == null || player == null)
                return source;

            bool pitStop = IsPitStopDefinition(source);
            float anchor = Mathf.Clamp(
                source.anchorTime,
                player.TimelineStartTime,
                player.ReadyUntilTime);
            float start;
            float end;
            if (pitStop)
            {
                start = Mathf.Clamp(
                    source.startTime,
                    player.TimelineStartTime,
                    anchor);
                end = Mathf.Clamp(
                    source.endTime,
                    anchor,
                    player.ReadyUntilTime);
                float maximumDuration = Mathf.Max(
                    10f,
                    pitMaximumEventDuration);
                if (end - start > maximumDuration)
                {
                    float before = Mathf.Min(
                        anchor - start,
                        maximumDuration * 0.5f);
                    start = anchor - before;
                    end = Mathf.Min(
                        player.ReadyUntilTime,
                        start + maximumDuration);
                    start = Mathf.Max(
                        player.TimelineStartTime,
                        end - maximumDuration);
                }
            }
            else if (IsCollisionEvent(source))
            {
                start = Mathf.Clamp(
                    source.startTime,
                    player.TimelineStartTime,
                    anchor);
                end = Mathf.Clamp(
                    source.endTime,
                    anchor,
                    player.ReadyUntilTime);
                if (end <= start)
                {
                    start = Mathf.Max(
                        player.TimelineStartTime,
                        anchor - Mathf.Max(0f, CollisionLeadSeconds));
                    end = Mathf.Min(
                        player.ReadyUntilTime,
                        anchor + Mathf.Max(0f, CollisionTailSeconds));
                }
            }
            else
            {
                float leadSeconds = eventLeadSeconds;
                float tailSeconds = eventTailSeconds;
                start = Mathf.Max(
                    player.TimelineStartTime,
                    anchor - Mathf.Max(0f, leadSeconds));
                float movingStart =
                    player.RaceStartTime +
                    Mathf.Max(0f, raceStartMotionGraceSeconds);
                if (movingStart > player.TimelineStartTime &&
                    anchor > player.RaceStartTime)
                {
                    start = Mathf.Min(
                        anchor,
                        Mathf.Max(start, movingStart));
                }
                end = Mathf.Min(
                    player.ReadyUntilTime,
                    anchor + Mathf.Max(0f, tailSeconds));
            }

            return new ReplayEventDto
            {
                eventId = source.eventId,
                eventType = source.eventType,
                anchorTime = anchor,
                startTime = start,
                endTime = end,
                driverNumbers = source.driverNumbers != null
                    ? (int[])source.driverNumbers.Clone()
                    : null,
                progressStart = source.progressStart,
                progressEnd = source.progressEnd,
                confidence = source.confidence,
                displayTitle = source.displayTitle,
                displayDescription = source.displayDescription,
                passingSide = source.passingSide,
                sideSource = source.sideSource,
                sideConfidence = source.sideConfidence,
                motionProfile = source.motionProfile,
                overtakerShare = source.overtakerShare,
                defenderShare = source.defenderShare,
                lapNumber = source.lapNumber,
                pitLaneDuration = source.pitLaneDuration,
                pitStopDuration = source.pitStopDuration,
                timingSource = source.timingSource
            };
        }

        private ReplayEventDto CreateMotionEvent(
            ReplayEventDto source,
            float transitionTime)
        {
            if (source == null)
                return null;

            float motionAnchor = Mathf.Clamp(
                transitionTime,
                source.startTime,
                source.endTime);
            float motionStart = Mathf.Clamp(
                motionAnchor -
                Mathf.Max(0f, overtakeMotionLeadSeconds),
                source.startTime,
                motionAnchor);
            return new ReplayEventDto
            {
                eventId = source.eventId,
                eventType = source.eventType,
                anchorTime = motionAnchor,
                startTime = motionStart,
                endTime = source.endTime,
                driverNumbers = source.driverNumbers != null
                    ? (int[])source.driverNumbers.Clone()
                    : null,
                progressStart = source.progressStart,
                progressEnd = source.progressEnd,
                confidence = source.confidence,
                displayTitle = source.displayTitle,
                displayDescription = source.displayDescription,
                passingSide = source.passingSide,
                sideSource = source.sideSource,
                sideConfidence = source.sideConfidence,
                motionProfile = source.motionProfile,
                overtakerShare = source.overtakerShare,
                defenderShare = source.defenderShare
            };
        }

        private ReplayEventDto CreateBattleMotionEvent(
            ReplayEventDto source,
            float transitionTime,
            OvertakeBattleSequence sequence)
        {
            if (sequence == null ||
                !sequence.IsValid ||
                sequence.Exchanges.Count == 0)
            {
                return CreateMotionEvent(
                    source,
                    transitionTime);
            }

            OvertakeBattleExchange firstExchange =
                sequence.Exchanges[0];
            bool sourceDirectionMatches =
                source.driverNumbers != null &&
                source.driverNumbers.Length >= 2 &&
                source.driverNumbers[0] ==
                    firstExchange.overtaker &&
                source.driverNumbers[1] ==
                    firstExchange.defender;
            return new ReplayEventDto
            {
                eventId = source.eventId + "_battle",
                eventType = source.eventType,
                anchorTime = firstExchange.anchorTime,
                startTime = sequence.MotionStartTime,
                endTime = sequence.EndTime,
                driverNumbers = new[]
                {
                    firstExchange.overtaker,
                    firstExchange.defender
                },
                progressStart = source.progressStart,
                progressEnd = source.progressEnd,
                confidence = source.confidence,
                displayTitle = source.displayTitle,
                displayDescription = source.displayDescription,
                passingSide = sourceDirectionMatches
                    ? source.passingSide
                    : null,
                sideSource = sourceDirectionMatches
                    ? source.sideSource
                    : "BattleContinuity",
                sideConfidence = sourceDirectionMatches
                    ? source.sideConfidence
                    : 0f,
                motionProfile = "ShowcaseBattle",
                overtakerShare = source.overtakerShare,
                defenderShare = source.defenderShare
            };
        }

        private OvertakeMotionSettings CreateEventOvertakeSettings(
            OvertakeMotionSettings source)
        {
            source ??= new OvertakeMotionSettings();
            return new OvertakeMotionSettings
            {
                enableOvertakeVisuals =
                    source.enableOvertakeVisuals,
                targetSeparationInVehicleWidths =
                    Mathf.Max(
                        source.targetSeparationInVehicleWidths,
                        eventMinimumSeparationInVehicleWidths),
                maximumCorrectionInVehicleWidths =
                    source.maximumCorrectionInVehicleWidths,
                maximumOffsetInVehicleWidths =
                    source.maximumOffsetInVehicleWidths,
                overtakerShare = source.overtakerShare,
                defenderShare = source.defenderShare,
                approachPortion = source.approachPortion,
                parallelPortion = source.parallelPortion,
                returnPortion = source.returnPortion,
                maximumVisualYawDegrees =
                    source.maximumVisualYawDegrees,
                overlapBlendMode = source.overlapBlendMode,
                debugOvertakeVisuals =
                    source.debugOvertakeVisuals
            };
        }

        private float ResolveEarliestAutomaticOvertakeAnchor()
        {
            if (player == null ||
                player.RaceStartTime <=
                player.TimelineStartTime + 0.001f)
            {
                return float.NegativeInfinity;
            }

            return player.RaceStartTime +
                Mathf.Max(0f, raceStartMotionGraceSeconds) +
                Mathf.Max(0f, minimumMovingLeadSeconds);
        }

        private bool TryValidate(ReplayEventDto definition, out string error)
        {
            if (player == null || !player.HasDataset)
            {
                error = "dataset is not ready";
                return false;
            }

            if (definition == null)
            {
                error = "definition is null";
                return false;
            }

            if (string.IsNullOrWhiteSpace(definition.eventId))
            {
                error = "eventId is empty";
                return false;
            }

            if (definition.endTime <= definition.startTime)
            {
                error = $"empty time range {definition.startTime:0.00}-{definition.endTime:0.00}";
                return false;
            }

            float maximumDuration = IsPitStopDefinition(definition)
                ? Mathf.Max(10f, pitMaximumEventDuration)
                : maxEventDuration;
            if (definition.endTime - definition.startTime > maximumDuration)
            {
                error = $"event duration exceeds {maximumDuration:0.#} seconds";
                return false;
            }

            if (definition.driverNumbers == null || definition.driverNumbers.Length == 0)
            {
                error = "driverNumbers is empty";
                return false;
            }

            if (IsCollisionEvent(definition) &&
                (definition.driverNumbers.Length < 2 ||
                 definition.driverNumbers[0] ==
                 definition.driverNumbers[1]))
            {
                error =
                    "collision event needs two different drivers";
                return false;
            }

            if (definition.startTime < player.TimelineStartTime ||
                definition.endTime > player.ReadyUntilTime)
            {
                error = $"time range is outside ready data {player.TimelineStartTime:0.00}-{player.ReadyUntilTime:0.00}";
                return false;
            }

            error = null;
            return true;
        }

        private ReplayEventDto CreateDevelopmentEvent()
        {
            DatasetManifestDto manifest = player.Manifest;
            if (manifest == null || manifest.drivers == null || manifest.drivers.Length < 2)
                return null;

            float earliest = player.TimelineStartTime;
            float latest = player.ReadyUntilTime;
            if (latest - earliest < 2f)
                return null;

            float anchor = Mathf.Clamp(player.CurrentTime, earliest + 1f, latest - 1f);
            float start = Mathf.Max(earliest, anchor - 6f);
            float end = Mathf.Min(latest, anchor + 4f);

            return new ReplayEventDto
            {
                eventId = $"development_overtake_{manifest.datasetId}",
                eventType = "Overtake",
                anchorTime = anchor,
                startTime = start,
                endTime = end,
                driverNumbers = new[]
                {
                    manifest.drivers[0].driverNumber,
                    manifest.drivers[1].driverNumber
                },
                progressStart = -1f,
                progressEnd = -1f,
                confidence = -1f,
                passingSide = "Unknown",
                sideSource = "DeterministicFallback",
                sideConfidence = 0f,
                motionProfile = "Default",
                displayTitle = "Development Close Battle Test",
                displayDescription = "Development fixture using nearby cars in the current replay window; not an automatically detected overtake."
            };
        }

        private void SelectClosestDevelopmentDrivers(
            ReplayEventDto definition,
            float startTime,
            float endTime)
        {
            Dictionary<int, List<LocationSample>> source = player.LocationsByDriver;
            if (source == null || source.Count < 2)
                return;

            List<DriverPoint> points = new(source.Count);
            float bestDistance = float.MaxValue;
            float bestTime = definition.anchorTime;
            int bestDriverA = 0;
            int bestDriverB = 0;

            for (float time = startTime; time <= endTime + 0.001f; time += 0.5f)
            {
                points.Clear();
                foreach (KeyValuePair<int, List<LocationSample>> pair in source)
                {
                    if (TryGetSourcePosition(pair.Value, time, out Vector2 position))
                        points.Add(new DriverPoint(pair.Key, position));
                }

                for (int i = 0; i < points.Count - 1; i++)
                {
                    for (int j = i + 1; j < points.Count; j++)
                    {
                        float distance = Vector2.SqrMagnitude(
                            points[i].Position - points[j].Position);
                        if (distance >= bestDistance)
                            continue;

                        bestDistance = distance;
                        bestTime = time;
                        bestDriverA = points[i].Driver;
                        bestDriverB = points[j].Driver;
                    }
                }
            }

            if (bestDriverA <= 0 || bestDriverB <= 0)
                return;

            definition.driverNumbers = new[] { bestDriverA, bestDriverB };
            definition.anchorTime = bestTime;
            definition.displayDescription =
                $"Development fixture showing nearby drivers {bestDriverA} and {bestDriverB}; " +
                "not an automatically detected overtake.";

            Debug.Log(
                $"[EventReplay] Development fixture selected closest drivers " +
                $"{bestDriverA} and {bestDriverB} at {bestTime:0.00}.",
                this);
        }

        private static bool TryGetSourcePosition(
            List<LocationSample> samples,
            float time,
            out Vector2 position)
        {
            position = default;
            if (!TryGetSamplePair(samples, time, out LocationSample a, out LocationSample b))
                return false;

            float duration = Mathf.Max(0.001f, b.t - a.t);
            float interpolation = Mathf.Clamp01((time - a.t) / duration);
            position = Vector2.Lerp(
                new Vector2(a.x, a.y),
                new Vector2(b.x, b.y),
                interpolation);
            return true;
        }

        private static bool TryGetSamplePair(
            List<LocationSample> samples,
            float time,
            out LocationSample a,
            out LocationSample b)
        {
            a = null;
            b = null;
            if (samples == null || samples.Count < 2 ||
                time < samples[0].t || time > samples[samples.Count - 1].t)
                return false;

            int low = 0;
            int high = samples.Count - 1;
            while (low + 1 < high)
            {
                int middle = (low + high) / 2;
                if (samples[middle].t <= time)
                    low = middle;
                else
                    high = middle;
            }

            a = samples[low];
            b = samples[high];
            return b.t > a.t;
        }

        private static bool IsDevelopmentEvent(ReplayEventDto definition)
        {
            return definition != null &&
                !string.IsNullOrEmpty(definition.eventId) &&
                definition.eventId.StartsWith(
                    "development_",
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPitStopDefinition(
            ReplayEventDto definition)
        {
            return definition != null &&
                string.Equals(
                    definition.eventType,
                    "PitStop",
                    StringComparison.OrdinalIgnoreCase);
        }

        private PitEnvironmentProfile ResolvePitEnvironmentProfile()
        {
            if (pitEnvironmentProfiles == null ||
                player == null ||
                player.Manifest == null)
            {
                return null;
            }

            for (int i = 0; i < pitEnvironmentProfiles.Length; i++)
            {
                PitEnvironmentProfile profile =
                    pitEnvironmentProfiles[i];
                if (profile != null &&
                    profile.Matches(player.Manifest.circuit))
                {
                    return profile;
                }
            }

            return null;
        }

        private static ReplayEventDto FindClosestOvertake(
            ReplayEventDto[] events,
            float time,
            float minimumAnchorTime =
                float.NegativeInfinity)
        {
            return FindClosestEvent(
                events,
                time,
                "Overtake",
                minimumAnchorTime,
                float.PositiveInfinity);
        }

        private static ReplayEventDto FindClosestEvent(
            ReplayEventDto[] events,
            float time,
            string eventType,
            float minimumAnchorTime,
            float maximumAnchorTime,
            Predicate<ReplayEventDto> predicate = null)
        {
            if (events == null)
                return null;

            ReplayEventDto closest = null;
            float closestDistance = float.PositiveInfinity;
            foreach (ReplayEventDto item in events)
            {
                if (item == null ||
                    !string.Equals(
                        item.eventType,
                        eventType,
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                if (item.anchorTime < minimumAnchorTime ||
                    item.anchorTime > maximumAnchorTime)
                    continue;
                if (predicate != null && !predicate(item))
                    continue;

                float distance = Mathf.Abs(item.anchorTime - time);
                if (distance < closestDistance ||
                    Mathf.Approximately(distance, closestDistance) &&
                    string.CompareOrdinal(item.eventId, closest?.eventId) < 0)
                {
                    closest = item;
                    closestDistance = distance;
                }
            }

            return closest;
        }

        private bool TryFindNextOvertake(
            out ReplayEventDto next)
        {
            return TryFindNextEvent(
                "Overtake",
                out next,
                ResolveEarliestAutomaticOvertakeAnchor());
        }

        private bool TryFindNextPitStop(out ReplayEventDto next)
        {
            return TryFindNextEvent(
                "PitStop",
                out next,
                float.NegativeInfinity,
                IsUsablePitStop);
        }

        private bool TryFindNextEvent(
            string eventType,
            out ReplayEventDto next,
            float minimumAnchorTime =
                float.NegativeInfinity,
            Predicate<ReplayEventDto> predicate = null)
        {
            next = null;
            ReplayEventDto[] events =
                player != null ? player.Events : null;
            if (events == null || events.Length == 0)
                return false;

            bool currentMatchesType =
                currentEvent != null &&
                string.Equals(
                    currentEvent.eventType,
                    eventType,
                    StringComparison.OrdinalIgnoreCase);
            float currentAnchor = currentMatchesType
                ? currentEvent.anchorTime
                : player.CurrentTime;
            string currentId = currentMatchesType
                ? currentEvent.eventId
                : string.Empty;

            for (int i = 0; i < events.Length; i++)
            {
                ReplayEventDto candidate = events[i];
                if (candidate == null ||
                    !string.Equals(
                        candidate.eventType,
                        eventType,
                        StringComparison.OrdinalIgnoreCase) ||
                    candidate.anchorTime < minimumAnchorTime ||
                    candidate.anchorTime > player.ReadyUntilTime ||
                    predicate != null && !predicate(candidate))
                {
                    continue;
                }

                bool followsCurrent =
                    candidate.anchorTime > currentAnchor + 0.0001f ||
                    Mathf.Approximately(
                        candidate.anchorTime,
                        currentAnchor) &&
                    string.CompareOrdinal(
                        candidate.eventId,
                        currentId) > 0;
                if (!followsCurrent)
                    continue;

                if (next == null ||
                    candidate.anchorTime < next.anchorTime ||
                    Mathf.Approximately(
                        candidate.anchorTime,
                        next.anchorTime) &&
                    string.CompareOrdinal(
                        candidate.eventId,
                        next.eventId) < 0)
                {
                    next = candidate;
                }
            }

            return next != null;
        }

        private bool IsUsablePitStop(ReplayEventDto definition)
        {
            if (!IsPitStopDefinition(definition) ||
                definition.driverNumbers == null ||
                definition.driverNumbers.Length == 0 ||
                definition.endTime <= definition.startTime)
            {
                return false;
            }

            float laneDuration = definition.pitLaneDuration;
            if (laneDuration > MaximumConventionalPitLaneSeconds)
                return false;

            DatasetManifestDto manifest = player != null
                ? player.Manifest
                : null;
            RaceControlEventDto[] redFlags = manifest != null
                ? manifest.redFlags
                : null;
            if (redFlags == null || redFlags.Length == 0)
                return true;

            float pitStart = laneDuration > 0f
                ? definition.anchorTime - laneDuration * 0.5f
                : definition.startTime;
            float pitEnd = laneDuration > 0f
                ? definition.anchorTime + laneDuration * 0.5f
                : definition.endTime;
            for (int i = 0; i < redFlags.Length; i++)
            {
                RaceControlEventDto redFlag = redFlags[i];
                if (redFlag == null)
                    continue;

                float redStart = redFlag.startT > 0f
                    ? redFlag.startT
                    : redFlag.t;
                float redEnd = redFlag.endT;
                if (redEnd <= redStart)
                    continue;

                float overlap = Mathf.Min(pitEnd, redEnd) -
                    Mathf.Max(pitStart, redStart);
                if (overlap >= MinimumRedFlagPitOverlapSeconds)
                    return false;
            }

            return true;
        }

        private static List<LocationSample> CopyRange(
            List<LocationSample> source,
            float startTime,
            float endTime)
        {
            List<LocationSample> result = new();
            LocationSample before = null;

            foreach (LocationSample sample in source)
            {
                if (sample.t < startTime)
                {
                    before = sample;
                    continue;
                }

                if (before != null)
                {
                    result.Add(before);
                    before = null;
                }

                result.Add(sample);
                if (sample.t > endTime)
                    break;
            }

            return result;
        }

        private static Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            Material material = new Material(shader)
            {
                color = color
            };
            return material;
        }

        private void OnDestroy()
        {
            if (openRoutine != null)
                StopCoroutine(openRoutine);
            CancelCollisionPreparation();

            DestroyStage();
            RestoreReplay();
        }

        private readonly struct ReplaySnapshot
        {
            public readonly float Time;
            public readonly float Speed;
            public readonly int SelectedDriver;
            public readonly bool WasPlaying;

            public ReplaySnapshot(ReplayPlayer source)
            {
                Time = source.CurrentTime;
                Speed = source.playbackSpeed;
                SelectedDriver = source.SelectedDriverNumber;
                WasPlaying = source.IsPlaying;
            }
        }

        private readonly struct TableTrackRendererState
        {
            private readonly Renderer renderer;
            private readonly bool forceRenderingOff;
            private readonly ShadowCastingMode shadowCastingMode;
            private readonly bool receiveShadows;

            public TableTrackRendererState(Renderer renderer)
            {
                this.renderer = renderer;
                forceRenderingOff =
                    renderer.forceRenderingOff;
                shadowCastingMode = renderer.shadowCastingMode;
                receiveShadows = renderer.receiveShadows;
            }

            public void Restore()
            {
                if (renderer == null)
                    return;

                renderer.shadowCastingMode = shadowCastingMode;
                renderer.receiveShadows = receiveShadows;
                renderer.forceRenderingOff =
                    forceRenderingOff;
            }
        }

        private readonly struct DriverPoint
        {
            public readonly int Driver;
            public readonly Vector2 Position;

            public DriverPoint(int driver, Vector2 position)
            {
                Driver = driver;
                Position = position;
            }
        }
    }

    public static class ReplayEventFixtures
    {
        public static ReplayEventDto[] Merge(
            DatasetManifestDto manifest)
        {
            if (manifest == null)
                return null;

            ReplayEventDto[] fixtureEvents = Load(manifest);
            ReplayEventDto[] manifestEvents = manifest.events;
            if ((manifestEvents == null ||
                 manifestEvents.Length == 0) &&
                (fixtureEvents == null ||
                 fixtureEvents.Length == 0))
            {
                return null;
            }

            var merged = new List<ReplayEventDto>();
            var eventIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            AddUniqueEvents(
                merged,
                eventIds,
                manifestEvents);
            AddUniqueEvents(
                merged,
                eventIds,
                fixtureEvents);
            merged.Sort((first, second) =>
            {
                int timeOrder = first.anchorTime.CompareTo(
                    second.anchorTime);
                return timeOrder != 0
                    ? timeOrder
                    : string.CompareOrdinal(
                        first.eventId,
                        second.eventId);
            });
            return merged.ToArray();
        }

        public static ReplayEventDto[] Load(DatasetManifestDto manifest)
        {
            if (manifest == null || manifest.sessionKey <= 0)
                return null;

            TextAsset asset = Resources.Load<TextAsset>(
                $"ReplayEvents/{manifest.sessionKey}");
            if (asset == null)
                return null;

            ReplayEventFixtureDto fixture;
            try
            {
                fixture = JsonUtility.FromJson<ReplayEventFixtureDto>(asset.text);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[EventReplay] Failed to parse fixture '{asset.name}': {exception.Message}");
                return null;
            }

            if (fixture == null || fixture.sessionKey != manifest.sessionKey)
            {
                Debug.LogWarning(
                    $"[EventReplay] Fixture session mismatch for '{asset.name}'.");
                return null;
            }

            return fixture.events;
        }

        private static void AddUniqueEvents(
            List<ReplayEventDto> destination,
            HashSet<string> eventIds,
            ReplayEventDto[] source)
        {
            if (source == null)
                return;

            for (int i = 0; i < source.Length; i++)
            {
                ReplayEventDto replayEvent = source[i];
                if (replayEvent == null ||
                    string.IsNullOrWhiteSpace(
                        replayEvent.eventId) ||
                    !eventIds.Add(replayEvent.eventId))
                {
                    continue;
                }

                destination.Add(replayEvent);
            }
        }

        [Serializable]
        private sealed class ReplayEventFixtureDto
        {
            public int sessionKey;
            public ReplayEventDto[] events;
        }
    }
}
