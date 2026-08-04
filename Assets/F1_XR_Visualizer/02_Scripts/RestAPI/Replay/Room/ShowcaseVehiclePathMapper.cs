using System.Collections.Generic;
using F1XR.RestAPI.Api;
using UnityEngine;

namespace F1XR.RestAPI.Replay.Room
{
    internal sealed class ShowcaseRoute
    {
        private const int CurveSamplesPerSegment = 24;
        private const float MinimumSourceSpan = 0.0001f;
        private const float MinimumHandleLength = 0.02f;
        private const float PlaneTolerance = 0.005f;
        private readonly CubicSegment entryToFocus;
        private readonly CubicSegment focusToExit;
        private readonly List<Vector3> centerline = new();

        private ShowcaseRoute(
            float sourceStart,
            float sourceEntry,
            float sourceFocus,
            float sourceExit,
            float sourceEnd,
            Vector3 entryPosition,
            Vector3 focusPosition,
            Vector3 exitPosition,
            Vector3 entryDirection,
            Vector3 focusDirection,
            Vector3 exitDirection,
            float entryContinuationLength,
            float exitContinuationLength,
            CubicSegment entryToFocus,
            CubicSegment focusToExit)
        {
            SourceStart = sourceStart;
            SourceEntry = sourceEntry;
            SourceFocus = sourceFocus;
            SourceExit = sourceExit;
            SourceEnd = sourceEnd;
            EntryPosition = entryPosition;
            FocusPosition = focusPosition;
            ExitPosition = exitPosition;
            EntryDirection = entryDirection;
            FocusDirection = focusDirection;
            ExitDirection = exitDirection;
            EntryContinuationLength = entryContinuationLength;
            ExitContinuationLength = exitContinuationLength;
            this.entryToFocus = entryToFocus;
            this.focusToExit = focusToExit;
            BuildCenterline();
        }

        public float SourceStart { get; }
        public float SourceEntry { get; }
        public float SourceFocus { get; }
        public float SourceExit { get; }
        public float SourceEnd { get; }
        public Vector3 EntryPosition { get; }
        public Vector3 FocusPosition { get; }
        public Vector3 ExitPosition { get; }
        public Vector3 EntryDirection { get; }
        public Vector3 FocusDirection { get; }
        public Vector3 ExitDirection { get; }
        public float EntryContinuationLength { get; }
        public float ExitContinuationLength { get; }
        public IReadOnlyList<Vector3> Centerline => centerline;
        public bool IsValid =>
            SourceStart + MinimumSourceSpan < SourceEntry &&
            SourceEntry + MinimumSourceSpan < SourceFocus &&
            SourceFocus + MinimumSourceSpan < SourceExit &&
            SourceExit + MinimumSourceSpan < SourceEnd &&
            centerline.Count >= 5;

        public static bool TryCreate(
            ShowcasePlaybackWindow timing,
            Pose entryPose,
            Pose focusPose,
            Pose exitPose,
            Vector3 entryTravelDirection,
            Vector3 exitTravelDirection,
            float floorHeight,
            float roadFloorOffset,
            float focusForwardOffset,
            float minimumEntryContinuation,
            float minimumExitContinuation,
            float sourceStart,
            float sourceEntry,
            float sourceFocus,
            float sourceExit,
            float sourceEnd,
            out ShowcaseRoute route,
            out string failure)
        {
            route = null;
            failure = "";
            if (!timing.IsValid ||
                !AreOrdered(
                    sourceStart,
                    sourceEntry,
                    sourceFocus,
                    sourceExit,
                    sourceEnd))
            {
                failure =
                    "The source route does not provide ordered Start, Entry, Focus, Exit, and End landmarks.";
                return false;
            }

            Vector3 entryDirection = Flat(entryTravelDirection);
            Vector3 exitDirection = Flat(exitTravelDirection);
            if (entryDirection.sqrMagnitude <= 0.000001f ||
                exitDirection.sqrMagnitude <= 0.000001f)
            {
                failure =
                    "The Entry or Exit wall has no stable horizontal travel direction.";
                return false;
            }

            entryDirection.Normalize();
            exitDirection.Normalize();
            float roadHeight = floorHeight + roadFloorOffset;
            Vector3 entry = AtHeight(entryPose.position, roadHeight);
            Vector3 exit = AtHeight(exitPose.position, roadHeight);
            Vector3 focusForward = Flat(focusPose.forward);
            if (focusForward.sqrMagnitude <= 0.000001f)
                focusForward = Flat(exit - entry);
            if (focusForward.sqrMagnitude <= 0.000001f)
            {
                failure =
                    "The automatic Focus and selected walls do not define a stable route direction.";
                return false;
            }

            focusForward.Normalize();
            Vector3 focus = AtHeight(
                focusPose.position +
                focusForward * Mathf.Max(0f, focusForwardOffset),
                roadHeight);
            Vector3 overallDirection = Flat(exit - entry);
            if (overallDirection.sqrMagnitude <= 0.0001f)
            {
                failure =
                    "The Entry and Exit anchors are too close to build a showcase route.";
                return false;
            }

            overallDirection.Normalize();
            if (Vector3.Dot(focusForward, overallDirection) < 0f)
                focusForward = -focusForward;
            Vector3 incoming = Flat(focus - entry).normalized;
            Vector3 outgoing = Flat(exit - focus).normalized;
            Vector3 focusDirection =
                incoming + outgoing + focusForward;
            if (focusDirection.sqrMagnitude <= 0.000001f)
                focusDirection = overallDirection;
            else
                focusDirection.Normalize();

            Vector3 exitInsideDirection = -exitDirection;
            if (!IsInside(entry, exit, exitInsideDirection) ||
                !IsInside(exit, entry, entryDirection) ||
                !IsStrictlyInside(focus, entry, entryDirection) ||
                !IsStrictlyInside(focus, exit, exitInsideDirection) ||
                Vector3.Dot(entryDirection, focus - entry) <= 0f ||
                Vector3.Dot(exitDirection, exit - focus) <= 0f)
            {
                failure =
                    "The automatic Focus is not inside a single Entry-to-Exit wall corridor.";
                return false;
            }

            if (!TryBuildSegment(
                    entry,
                    focus,
                    entryDirection,
                    focusDirection,
                    entry,
                    entryDirection,
                    exit,
                    exitInsideDirection,
                    out CubicSegment first) ||
                !TryBuildSegment(
                    focus,
                    exit,
                    focusDirection,
                    exitDirection,
                    entry,
                    entryDirection,
                    exit,
                    exitInsideDirection,
                    out CubicSegment second))
            {
                failure =
                    "The wall corridor does not leave enough room for stable route tangents.";
                return false;
            }

            float sourceCoreLength = sourceExit - sourceEntry;
            float physicalCoreLength =
                EstimateLength(first) + EstimateLength(second);
            float coreScale = physicalCoreLength /
                Mathf.Max(MinimumSourceSpan, sourceCoreLength);
            float entryContinuation = Mathf.Max(
                minimumEntryContinuation,
                (sourceEntry - sourceStart) * coreScale);
            float exitContinuation = Mathf.Max(
                minimumExitContinuation,
                (sourceEnd - sourceExit) * coreScale);

            ShowcaseRoute candidate = new(
                sourceStart,
                sourceEntry,
                sourceFocus,
                sourceExit,
                sourceEnd,
                entry,
                focus,
                exit,
                entryDirection,
                focusDirection,
                exitDirection,
                entryContinuation,
                exitContinuation,
                first,
                second);
            if (!candidate.IsValid ||
                !candidate.HasSinglePortalCrossings() ||
                candidate.HasSelfIntersection())
            {
                failure =
                    "The generated route would cross a portal more than once or intersect itself.";
                return false;
            }

            route = candidate;
            return true;
        }

        public bool TryEvaluate(
            float sourceLongitudinal,
            out Vector3 position,
            out Vector3 tangent)
        {
            position = Vector3.zero;
            tangent = Vector3.forward;
            if (!IsValid)
                return false;

            float source = Mathf.Clamp(
                sourceLongitudinal,
                SourceStart,
                SourceEnd);
            if (source <= SourceEntry)
            {
                float progress = Mathf.InverseLerp(
                    SourceStart,
                    SourceEntry,
                    source);
                position = Vector3.Lerp(
                    EntryPosition -
                    EntryDirection * EntryContinuationLength,
                    EntryPosition,
                    progress);
                tangent = EntryDirection;
                return true;
            }

            if (source <= SourceFocus)
            {
                float progress = Mathf.InverseLerp(
                    SourceEntry,
                    SourceFocus,
                    source);
                entryToFocus.Evaluate(
                    progress,
                    out position,
                    out tangent);
                return true;
            }

            if (source <= SourceExit)
            {
                float progress = Mathf.InverseLerp(
                    SourceFocus,
                    SourceExit,
                    source);
                focusToExit.Evaluate(
                    progress,
                    out position,
                    out tangent);
                return true;
            }

            float exitProgress = Mathf.InverseLerp(
                SourceExit,
                SourceEnd,
                source);
            position = Vector3.Lerp(
                ExitPosition,
                ExitPosition +
                ExitDirection * ExitContinuationLength,
                exitProgress);
            tangent = ExitDirection;
            return true;
        }

        private void BuildCenterline()
        {
            centerline.Clear();
            centerline.Add(
                EntryPosition -
                EntryDirection * EntryContinuationLength);
            centerline.Add(EntryPosition);
            AppendSegment(entryToFocus);
            AppendSegment(focusToExit);
            centerline.Add(
                ExitPosition +
                ExitDirection * ExitContinuationLength);
        }

        private void AppendSegment(CubicSegment segment)
        {
            for (int i = 1; i <= CurveSamplesPerSegment; i++)
            {
                segment.Evaluate(
                    i / (float)CurveSamplesPerSegment,
                    out Vector3 position,
                    out _);
                centerline.Add(position);
            }
        }

        private bool HasSinglePortalCrossings()
        {
            Vector3 exitInside = -ExitDirection;
            for (int i = 0; i < centerline.Count; i++)
            {
                Vector3 point = centerline[i];
                bool beforeEntry = i == 0;
                bool afterExit = i == centerline.Count - 1;
                float entrySide = Vector3.Dot(
                    point - EntryPosition,
                    EntryDirection);
                float exitSide = Vector3.Dot(
                    point - ExitPosition,
                    exitInside);
                if (beforeEntry)
                {
                    if (entrySide >= -PlaneTolerance)
                        return false;
                }
                else if (entrySide < -PlaneTolerance)
                {
                    return false;
                }

                if (afterExit)
                {
                    if (exitSide >= -PlaneTolerance)
                        return false;
                }
                else if (exitSide < -PlaneTolerance)
                {
                    return false;
                }
            }

            return true;
        }

        private bool HasSelfIntersection()
        {
            for (int first = 0;
                 first < centerline.Count - 1;
                 first++)
            {
                for (int second = first + 2;
                     second < centerline.Count - 1;
                     second++)
                {
                    if (SegmentsIntersect(
                            centerline[first],
                            centerline[first + 1],
                            centerline[second],
                            centerline[second + 1]))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryBuildSegment(
            Vector3 start,
            Vector3 end,
            Vector3 startDirection,
            Vector3 endDirection,
            Vector3 entryPlanePoint,
            Vector3 entryInsideDirection,
            Vector3 exitPlanePoint,
            Vector3 exitInsideDirection,
            out CubicSegment segment)
        {
            segment = default;
            float chordLength = Flat(end - start).magnitude;
            if (chordLength <= MinimumHandleLength)
                return false;

            float targetHandle = chordLength / 3f;
            float startHandle = LimitHandleLength(
                start,
                startDirection,
                targetHandle,
                entryPlanePoint,
                entryInsideDirection,
                exitPlanePoint,
                exitInsideDirection);
            float endHandle = LimitHandleLength(
                end,
                -endDirection,
                targetHandle,
                entryPlanePoint,
                entryInsideDirection,
                exitPlanePoint,
                exitInsideDirection);
            if (startHandle < MinimumHandleLength ||
                endHandle < MinimumHandleLength)
            {
                return false;
            }

            segment = new CubicSegment(
                start,
                start + startDirection * startHandle,
                end - endDirection * endHandle,
                end);
            return true;
        }

        private static float LimitHandleLength(
            Vector3 origin,
            Vector3 handleDirection,
            float targetLength,
            Vector3 entryPlanePoint,
            Vector3 entryInsideDirection,
            Vector3 exitPlanePoint,
            Vector3 exitInsideDirection)
        {
            float length = Mathf.Max(0f, targetLength);
            length = LimitHandleAgainstPlane(
                origin,
                handleDirection,
                length,
                entryPlanePoint,
                entryInsideDirection);
            return LimitHandleAgainstPlane(
                origin,
                handleDirection,
                length,
                exitPlanePoint,
                exitInsideDirection);
        }

        private static float LimitHandleAgainstPlane(
            Vector3 origin,
            Vector3 handleDirection,
            float targetLength,
            Vector3 planePoint,
            Vector3 insideDirection)
        {
            float rate = Vector3.Dot(
                handleDirection,
                insideDirection);
            if (rate >= -0.000001f)
                return targetLength;

            float clearance = Vector3.Dot(
                origin - planePoint,
                insideDirection);
            return Mathf.Min(
                targetLength,
                Mathf.Max(0f, clearance * 0.9f / -rate));
        }

        private static float EstimateLength(CubicSegment segment)
        {
            float length = 0f;
            Vector3 previous = segment.Start;
            for (int i = 1; i <= CurveSamplesPerSegment; i++)
            {
                segment.Evaluate(
                    i / (float)CurveSamplesPerSegment,
                    out Vector3 position,
                    out _);
                length += Vector3.Distance(previous, position);
                previous = position;
            }

            return length;
        }

        private static bool AreOrdered(
            float start,
            float entry,
            float focus,
            float exit,
            float end)
        {
            return float.IsFinite(start) &&
                float.IsFinite(entry) &&
                float.IsFinite(focus) &&
                float.IsFinite(exit) &&
                float.IsFinite(end) &&
                start + MinimumSourceSpan < entry &&
                entry + MinimumSourceSpan < focus &&
                focus + MinimumSourceSpan < exit &&
                exit + MinimumSourceSpan < end;
        }

        private static bool IsInside(
            Vector3 point,
            Vector3 planePoint,
            Vector3 insideDirection)
        {
            return Vector3.Dot(
                point - planePoint,
                insideDirection) >= -PlaneTolerance;
        }

        private static bool IsStrictlyInside(
            Vector3 point,
            Vector3 planePoint,
            Vector3 insideDirection)
        {
            return Vector3.Dot(
                point - planePoint,
                insideDirection) > PlaneTolerance;
        }

        private static Vector3 AtHeight(Vector3 point, float height)
        {
            point.y = height;
            return point;
        }

        private static Vector3 Flat(Vector3 value)
        {
            value.y = 0f;
            return value;
        }

        private static bool SegmentsIntersect(
            Vector3 firstStart,
            Vector3 firstEnd,
            Vector3 secondStart,
            Vector3 secondEnd)
        {
            Vector2 a = new(firstStart.x, firstStart.z);
            Vector2 b = new(firstEnd.x, firstEnd.z);
            Vector2 c = new(secondStart.x, secondStart.z);
            Vector2 d = new(secondEnd.x, secondEnd.z);
            float abC = Cross(b - a, c - a);
            float abD = Cross(b - a, d - a);
            float cdA = Cross(d - c, a - c);
            float cdB = Cross(d - c, b - c);
            return abC * abD < -0.000001f &&
                cdA * cdB < -0.000001f;
        }

        private static float Cross(Vector2 first, Vector2 second)
        {
            return first.x * second.y - first.y * second.x;
        }

        private readonly struct CubicSegment
        {
            public CubicSegment(
                Vector3 start,
                Vector3 firstControl,
                Vector3 secondControl,
                Vector3 end)
            {
                Start = start;
                FirstControl = firstControl;
                SecondControl = secondControl;
                End = end;
            }

            public Vector3 Start { get; }
            public Vector3 FirstControl { get; }
            public Vector3 SecondControl { get; }
            public Vector3 End { get; }

            public void Evaluate(
                float progress,
                out Vector3 position,
                out Vector3 tangent)
            {
                float t = Mathf.Clamp01(progress);
                float inverse = 1f - t;
                position =
                    inverse * inverse * inverse * Start +
                    3f * inverse * inverse * t * FirstControl +
                    3f * inverse * t * t * SecondControl +
                    t * t * t * End;
                tangent =
                    3f * inverse * inverse *
                    (FirstControl - Start) +
                    6f * inverse * t *
                    (SecondControl - FirstControl) +
                    3f * t * t *
                    (End - SecondControl);
                tangent = Flat(tangent);
                if (tangent.sqrMagnitude <= 0.000001f)
                    tangent = Flat(End - Start);
                if (tangent.sqrMagnitude > 0.000001f)
                    tangent.Normalize();
            }
        }
    }

    internal readonly struct ShowcaseRun
    {
        public ShowcaseRun(
            ShowcasePlaybackWindow timing,
            ShowcaseRoute route,
            Pose entryPose,
            Pose focusPose,
            Pose exitPose,
            Vector3 entryTravelDirection,
            Vector3 exitTravelDirection,
            float floorHeight,
            int layoutRevision,
            int sourceRevision)
        {
            Timing = timing;
            Route = route;
            EntryPose = entryPose;
            FocusPose = focusPose;
            ExitPose = exitPose;
            EntryTravelDirection = entryTravelDirection;
            ExitTravelDirection = exitTravelDirection;
            FloorHeight = floorHeight;
            LayoutRevision = layoutRevision;
            SourceRevision = sourceRevision;
        }

        public ShowcasePlaybackWindow Timing { get; }
        public ShowcaseRoute Route { get; }
        public Pose EntryPose { get; }
        public Pose FocusPose { get; }
        public Pose ExitPose { get; }
        public Vector3 EntryTravelDirection { get; }
        public Vector3 ExitTravelDirection { get; }
        public float FloorHeight { get; }
        public int LayoutRevision { get; }
        public int SourceRevision { get; }
        public bool IsValid =>
            Timing.IsValid &&
            Route != null &&
            Route.IsValid &&
            EntryTravelDirection.sqrMagnitude > 0.000001f &&
            ExitTravelDirection.sqrMagnitude > 0.000001f &&
            float.IsFinite(FloorHeight);
    }

    internal enum ShowcaseStagePlacementMode
    {
        None,
        PortalAlignedRigid,
        RoomDioramaRigid,
        LifeSizeDriveBy
    }

    internal enum ShowcasePresentationMode
    {
        RoomDiorama,
        LifeSizeDriveByExperimental
    }

    internal readonly struct ShowcaseStagePlacement
    {
        public ShowcaseStagePlacement(
            ShowcaseStagePlacementMode mode,
            Vector3 position,
            Quaternion rotation,
            float uniformScale,
            Vector3 interactionFocus,
            float entryContinuation,
            float exitContinuation,
            float entryContinuationTarget,
            float exitContinuationTarget,
            float heroMissDistance,
            float heroMissLimit,
            float entryWallAngle,
            float exitWallAngle,
            float portalCrossingMiss,
            bool wallPairCompatible)
        {
            Mode = mode;
            Position = position;
            Rotation = rotation;
            UniformScale = uniformScale;
            InteractionFocus = interactionFocus;
            EntryContinuation = entryContinuation;
            ExitContinuation = exitContinuation;
            EntryContinuationTarget = entryContinuationTarget;
            ExitContinuationTarget = exitContinuationTarget;
            HeroMissDistance = heroMissDistance;
            HeroMissLimit = heroMissLimit;
            EntryWallAngle = entryWallAngle;
            ExitWallAngle = exitWallAngle;
            PortalCrossingMiss = portalCrossingMiss;
            WallPairCompatible = wallPairCompatible;
        }

        public ShowcaseStagePlacementMode Mode { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public float UniformScale { get; }
        public Vector3 InteractionFocus { get; }
        public float EntryContinuation { get; }
        public float ExitContinuation { get; }
        public float EntryContinuationTarget { get; }
        public float ExitContinuationTarget { get; }
        public float HeroMissDistance { get; }
        public float HeroMissLimit { get; }
        public float EntryWallAngle { get; }
        public float ExitWallAngle { get; }
        public float PortalCrossingMiss { get; }
        public bool WallPairCompatible { get; }
        public bool IsValid =>
            Mode != ShowcaseStagePlacementMode.None &&
            IsFinite(Position) &&
            IsFinite(Rotation) &&
            IsFinite(InteractionFocus) &&
            float.IsFinite(UniformScale) &&
            UniformScale > 0f &&
            float.IsFinite(EntryContinuation) &&
            float.IsFinite(ExitContinuation) &&
            float.IsFinite(EntryContinuationTarget) &&
            float.IsFinite(ExitContinuationTarget) &&
            float.IsFinite(HeroMissDistance) &&
            float.IsFinite(HeroMissLimit) &&
            float.IsFinite(EntryWallAngle) &&
            float.IsFinite(ExitWallAngle) &&
            float.IsFinite(PortalCrossingMiss);

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
    }

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
        [SerializeField]
        private ShowcasePresentationMode presentationMode =
            ShowcasePresentationMode.RoomDiorama;
        [SerializeField, Min(0f)] private float wallContinuationTarget = 10f;
        [SerializeField, Range(1f, 2f)] private float entryContinuationMultiplier = 1.5f;
        [SerializeField, Min(0f)] private float heroForwardOffset = 0.25f;
        [SerializeField] private float roadFloorOffset = 0.02f;
        [SerializeField, Range(1f, 1.5f)] private float showcaseTrackScaleMultiplier = 1.15f;
        [SerializeField, Range(0.5f, 1.5f)] private float showcaseVehicleScale = 0.7f;
        [SerializeField, Min(1f)] private float showcasePlaybackSpeedMultiplier = 1.5f;
        [SerializeField] private bool immersiveScaleEnabled;

        [Header("Life Size Drive-By Contract")]
        [SerializeField]
        private LifeSizeDriveBySettings lifeSizeDriveBy = new();

        [Header("Overtake Exit Portal VFX")]
        [SerializeField]
        private OvertakePortalTransitionVfxSettings
            overtakePortalTransitionVfx = new();

        [Header("Control")]
        [SerializeField] private bool mappingEnabled = true;

        private readonly List<Vector3> eventLocalPath = new();
        private EventPopoutReplay eventReplay;
        private ShowcasePortalPresentation portalPresentation;
        private LifeSizeDriveByRoadPresentation lifeSizeRoad;
        private LifeSizeDriveByVehiclePresentation lifeSizeVehicles;
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
        private ShowcaseRun activeRun;
        private LifeSizeDriveByPlan preparedLifeSizePlan;
        private string lifeSizePlanFailure = "";
        private ShowcaseStagePlacementMode activePlacementMode;
        private int boundLayoutRevision = -1;

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
        internal bool TryGetActiveRun(out ShowcaseRun run)
        {
            run = activeRun;
            return run.IsValid;
        }
        internal bool TryGetActiveRoute(out ShowcaseRoute route)
        {
            route = activeRun.Route;
            return route != null && route.IsValid;
        }
        internal bool TryGetPreparedLifeSizePlan(
            out LifeSizeDriveByPlan plan,
            out string failure)
        {
            plan = preparedLifeSizePlan;
            failure = lifeSizePlanFailure;
            return plan != null && plan.IsValid;
        }
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
            showcasePlaybackSpeedMultiplier =
                Mathf.Max(
                    1f,
                    showcasePlaybackSpeedMultiplier);
            overtakePortalTransitionVfx ??=
                new OvertakePortalTransitionVfxSettings();
            overtakePortalTransitionVfx.ClampValues();
            lifeSizeDriveBy ??= new LifeSizeDriveBySettings();
            lifeSizeDriveBy.ClampValues();

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
            eventReplay?.SetOvertakeVehicleSizeScale(
                lifeSizeVehicles != null &&
                lifeSizeVehicles.IsCommitted
                    ? 1f
                    : showcaseVehicleScale);

            if (!mappingEnabled)
            {
                ReleaseBinding(true);
                SetInactive("Disabled", "");
                return;
            }

            if (eventReplay == null)
            {
                ReleaseBinding();
                SetInactive(
                    "WaitingForEvent",
                    "Replay event controller is unavailable.");
                return;
            }

            if (eventReplay.IsLoading)
            {
                ReleaseBinding(false, false);
                eventReplay.SuspendTableTrackRendering();
                SetInactive("LoadingEvent", "");
                return;
            }

            if (!eventReplay.IsActive)
            {
                ReleaseBinding();
                SetInactive("WaitingForEvent", "");
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
                boundSourceRevision != eventReplay.SourceGeometryRevision ||
                showcaseLayout == null ||
                boundLayoutRevision != showcaseLayout.LayoutRevision)
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

            ApplyActiveVehiclePresentation(
                firstBinding,
                secondBinding);
            RevealStageAfterReplayMotion();
            UpdateOrderDiagnostics();
            portalPresentation
                .UpdateOvertakePortalTransition(
                    eventReplay.CurrentTime,
                    eventReplay.IsPlaying,
                    eventReplay
                        .OvertakeCompletionConfirmed);
            bindingState = activePlacementMode.ToString();
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

            if (lifeSizeRoad == null)
                lifeSizeRoad =
                    GetComponent<LifeSizeDriveByRoadPresentation>() ??
                    gameObject.AddComponent<LifeSizeDriveByRoadPresentation>();

            if (lifeSizeVehicles == null)
                lifeSizeVehicles =
                    GetComponent<LifeSizeDriveByVehiclePresentation>() ??
                    gameObject.AddComponent<LifeSizeDriveByVehiclePresentation>();
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
                    out _,
                    out _))
            {
                SetInactive(
                    "SourceUnavailable",
                    "The event-local source path is unavailable. No room-path fallback was applied.");
                return false;
            }

            if (!TryCreateShowcaseRun(
                    out ShowcaseRun run,
                    out string runFailure))
            {
                SetInactive("LayoutInvalid", runFailure);
                return false;
            }

            ShowcasePlaybackWindow playbackWindow = run.Timing;
            if (
                !eventReplay.TryGetEventLocalPathPosition(
                    playbackWindow.EntryTime,
                    out Vector3 entryPosition) ||
                !eventReplay.TryGetEventLocalPathPosition(
                    playbackWindow.FocusTime,
                    out Vector3 focusPosition) ||
                !eventReplay.TryGetEventLocalPathPosition(
                    playbackWindow.ExitTime,
                    out Vector3 exitPosition))
            {
                SetInactive(
                    "SourceUnavailable",
                    "The showcase playback window could not resolve its Entry, focus, and Exit landmarks.");
                return false;
            }

            if (!TryCreateEventStagePlacement(
                    eventLocalPath,
                    entryPosition,
                    focusPosition,
                    exitPosition,
                    run,
                    out ShowcaseStagePlacement placement,
                    out string placementFailure))
            {
                SetInactive(
                    "PlacementInvalid",
                    placementFailure);
                return false;
            }

            PrepareLifeSizeDriveByPlan(
                run,
                stage,
                first,
                second);

            ShowcaseStagePlacement portalAlignedPlacement =
                placement;
            if (!TryCreateRoomDioramaPlacement(
                    eventLocalPath,
                    entryPosition,
                    focusPosition,
                    exitPosition,
                    run,
                    portalAlignedPlacement,
                    out placement,
                    out placementFailure))
            {
                SetInactive(
                    "PlacementInvalid",
                    placementFailure);
                return false;
            }

            CapturePlacementDiagnostics(placement);
            if (!TryValidateEventStagePlacement(
                    placement,
                    out placementFailure))
            {
                SetInactive(
                    "PlacementInvalid",
                    placementFailure);
                return false;
            }

            if (!TryCommitEventStagePlacement(
                    placement,
                    out placementFailure))
            {
                SetInactive(
                    "PlacementInvalid",
                    placementFailure);
                return false;
            }

            CaptureVehicleScale(first, second);
            bool usesLifeSize = TryCommitLifeSizeDriveBy(
                out string lifeSizeCommitFailure);
            if (!usesLifeSize &&
                !string.IsNullOrEmpty(lifeSizeCommitFailure))
            {
                lifeSizePlanFailure = lifeSizeCommitFailure;
            }

            eventReplay.SetShowcaseDrivingPresentation(
                first.DriverNumber,
                second.DriverNumber,
                true);
            eventReplay.SetShowcasePlaybackSpeedMultiplier(
                showcasePlaybackSpeedMultiplier);
            bool usesPortals = !usesLifeSize &&
                placement.Mode ==
                ShowcaseStagePlacementMode.PortalAlignedRigid;
            if (usesPortals)
            {
                portalPresentation.ImmersiveScaleEnabled =
                    immersiveScaleEnabled;
                if (!portalPresentation.Configure(
                        stage,
                        showcaseLayout,
                        first.VehicleRoot,
                        second.VehicleRoot,
                        out string portalFailure))
                {
                    eventReplay.TryRestoreTableRelativePose();
                    eventReplay.SetShowcaseDrivingPresentation(
                        first.DriverNumber,
                        second.DriverNumber,
                        false);
                    eventReplay.SetShowcasePlaybackSpeedMultiplier(1f);
                    SetInactive(
                        "PortalInvalid",
                        portalFailure);
                    return false;
                }
            }
            else
                portalPresentation.Clear();

            if (usesPortals)
            {
                ResolvePortalTransitionVehicles(
                    first,
                    second,
                    out Transform overtakingVehicle,
                    out Transform defendingVehicle);
                overtakePortalTransitionVfx ??=
                    new OvertakePortalTransitionVfxSettings();
                overtakePortalTransitionVfx.ClampValues();
                portalPresentation.ConfigureOvertakePortalTransition(
                    overtakePortalTransitionVfx,
                    overtakingVehicle,
                    defendingVehicle,
                    eventReplay.CurrentTime);
            }

            ApplyActiveVehiclePresentation(first, second);

            eventReplay.SuspendTableTrackRendering();
            boundStage = stage;
            firstBinding = first;
            secondBinding = second;
            eventReplay.SetShowcaseAudioFocus(
                first.DriverNumber);
            boundSourceRevision = run.SourceRevision;
            boundLayoutRevision = run.LayoutRevision;
            activeRun = run;
            activePlacementMode = usesLifeSize
                ? ShowcaseStagePlacementMode.LifeSizeDriveBy
                : placement.Mode;
            stageRevealPending = true;
            stageRevealStartTime = eventReplay.CurrentTime;
            eventReplay.TryGetSourceLongitudinal(
                first.DriverNumber,
                out stageRevealFirstLongitudinal);
            eventReplay.TryGetSourceLongitudinal(
                second.DriverNumber,
                out stageRevealSecondLongitudinal);
            ResetOrderDiagnostics();
            bindingState = activePlacementMode.ToString();
            lastFailureReason = "";
            Debug.Log(
                $"[RoomEventPlacement] sourcePoints={eventLocalPath.Count}, " +
                $"routePoints={run.Route.Centerline.Count}, " +
                $"mode={activePlacementMode}, " +
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

        private bool TryCreateShowcaseRun(
            out ShowcaseRun run,
            out string failure)
        {
            run = default;
            failure = "";
            if (eventReplay == null ||
                showcaseLayout == null ||
                !eventReplay.TryGetShowcasePlaybackWindow(
                    out ShowcasePlaybackWindow timing) ||
                !showcaseLayout.TryGetEntryPose(
                    out Pose entryPose) ||
                !showcaseLayout.TryGetHeroPose(
                    out Pose focusPose) ||
                !showcaseLayout.TryGetExitPose(
                    out Pose exitPose) ||
                !showcaseLayout.TryGetRoomFloorHeight(
                    out float floorHeight))
            {
                failure =
                    "The showcase timing, automatic Focus, or wall layout is unavailable.";
                return false;
            }

            if (!eventReplay.TryGetReferenceSourceLongitudinalAtTime(
                    timing.StartTime,
                    out float sourceStart) ||
                !eventReplay.TryGetReferenceSourceLongitudinalAtTime(
                    timing.EntryTime,
                    out float sourceEntry) ||
                !eventReplay.TryGetReferenceSourceLongitudinalAtTime(
                    timing.FocusTime,
                    out float sourceFocus) ||
                !eventReplay.TryGetReferenceSourceLongitudinalAtTime(
                    timing.ExitTime,
                    out float sourceExit) ||
                !eventReplay.TryGetReferenceSourceLongitudinalAtTime(
                    timing.EndTime,
                    out float sourceEnd))
            {
                failure =
                    "The reference vehicle could not resolve the authoritative route landmarks.";
                return false;
            }

            float continuationScale =
                Mathf.Max(1f, showcaseTrackScaleMultiplier);
            if (!ShowcaseRoute.TryCreate(
                    timing,
                    entryPose,
                    focusPose,
                    exitPose,
                    showcaseLayout.EntryTravelDirection,
                    showcaseLayout.ExitTravelDirection,
                    floorHeight,
                    roadFloorOffset,
                    heroForwardOffset,
                    wallContinuationTarget *
                    Mathf.Max(1f, entryContinuationMultiplier) *
                    continuationScale,
                    wallContinuationTarget * continuationScale,
                    sourceStart,
                    sourceEntry,
                    sourceFocus,
                    sourceExit,
                    sourceEnd,
                    out ShowcaseRoute route,
                    out string routeFailure))
            {
                failure = routeFailure;
                return false;
            }

            run = new ShowcaseRun(
                timing,
                route,
                entryPose,
                focusPose,
                exitPose,
                showcaseLayout.EntryTravelDirection,
                showcaseLayout.ExitTravelDirection,
                floorHeight,
                showcaseLayout.LayoutRevision,
                eventReplay.SourceGeometryRevision);
            if (run.IsValid)
                return true;

            run = default;
            failure =
                "The showcase timing or captured wall travel direction is invalid.";
            return false;
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

        private bool TryCreateEventStagePlacement(
            IReadOnlyList<Vector3> sourcePath,
            Vector3 sourceEntryPosition,
            Vector3 sourceFocusPosition,
            Vector3 sourceExitPosition,
            ShowcaseRun run,
            out ShowcaseStagePlacement placement,
            out string failure)
        {
            placement = default;
            failure = "";
            if (sourcePath == null ||
                sourcePath.Count < 2)
            {
                failure = "The event source geometry is unavailable.";
                return false;
            }

            Vector3 roomTravel = Flat(
                run.ExitPose.position -
                run.EntryPose.position);
            if (roomTravel.sqrMagnitude <= 0.000001f)
            {
                failure =
                    "The selected room walls have no stable horizontal travel direction.";
                return false;
            }

            Vector3 heroForward = Flat(
                run.FocusPose.forward);
            if (heroForward.sqrMagnitude <= 0.000001f)
                heroForward = roomTravel.normalized;
            else
                heroForward.Normalize();

            Vector3 overtakeTarget =
                run.FocusPose.position +
                heroForward * heroForwardOffset;
            float roomFloorHeight = run.FloorHeight;

            Vector3 entryInward = Flat(
                run.EntryTravelDirection);
            Vector3 exitInward = Flat(
                -run.ExitTravelDirection);
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
                    sourceFocusPosition);
            int entryIndex =
                FindClosestPointIndex(
                    sourcePath,
                    sourceEntryPosition);
            int exitIndex =
                FindClosestPointIndex(
                    sourcePath,
                    sourceExitPosition);
            if (entryIndex < 0 ||
                transitionIndex <= entryIndex ||
                exitIndex <= transitionIndex ||
                exitIndex >= sourcePath.Count)
            {
                failure =
                    "The authoritative showcase window does not leave an ordered Entry, focus, and Exit path.";
                return false;
            }

            float[] cumulativeDistances =
                BuildCumulativeDistances(sourcePath);
            float totalDistance =
                cumulativeDistances[cumulativeDistances.Length - 1];
            float roomSpan = roomTravel.magnitude;
            float continuationScale =
                Mathf.Max(1f, showcaseTrackScaleMultiplier);
            float entryContinuationTarget =
                wallContinuationTarget *
                Mathf.Max(1f, entryContinuationMultiplier) *
                continuationScale;
            float exitContinuationTarget =
                wallContinuationTarget *
                continuationScale;
            Vector3 sourceCrossing = Flat(
                sourceExitPosition -
                sourceEntryPosition);
            float sourceSpan = sourceCrossing.magnitude;
            if (sourceSpan <= 0.0001f)
            {
                failure =
                    "The authoritative Entry and Exit landmarks have no usable horizontal separation.";
                return false;
            }

            float resolvedScale =
                roomSpan / sourceSpan;
            if (!float.IsFinite(resolvedScale) ||
                resolvedScale <= 0f ||
                resolvedScale > 10000f)
            {
                failure =
                    "A finite source-relative event scale could not be resolved.";
                return false;
            }

            float yaw = Vector3.SignedAngle(
                sourceCrossing,
                roomTravel,
                Vector3.up);
            Quaternion rotation =
                Quaternion.Euler(0f, yaw, 0f);
            Vector3 transformedEntry =
                rotation *
                sourceEntryPosition *
                resolvedScale;
            Vector3 transformedExit =
                rotation *
                sourceExitPosition *
                resolvedScale;
            Vector3 position =
                run.EntryPose.position -
                transformedEntry;
            position.y =
                roomFloorHeight +
                roadFloorOffset -
                (rotation *
                 sourceFocusPosition *
                 resolvedScale).y;

            Vector3 mappedEntry =
                position + transformedEntry;
            Vector3 mappedExit =
                position + transformedExit;
            Vector3 mappedFocus =
                position +
                rotation *
                sourceFocusPosition *
                resolvedScale;
            float entryMiss = Flat(
                mappedEntry -
                run.EntryPose.position).magnitude;
            float exitMiss = Flat(
                mappedExit -
                run.ExitPose.position).magnitude;
            Vector3 sourceEntryDirection =
                FindDirectionAt(sourcePath, entryIndex);
            Vector3 sourceExitDirection =
                FindDirectionAt(sourcePath, exitIndex);

            float resolvedEntryContinuation =
                cumulativeDistances[entryIndex] *
                resolvedScale;
            float resolvedExitContinuation =
                (totalDistance -
                 cumulativeDistances[exitIndex]) *
                resolvedScale;
            float resolvedHeroMissDistance = Flat(
                mappedFocus - overtakeTarget).magnitude;
            float resolvedEntryWallAngle = Vector3.Angle(
                Flat(rotation * sourceEntryDirection),
                Flat(run.EntryTravelDirection));
            float resolvedExitWallAngle = Vector3.Angle(
                Flat(rotation * sourceExitDirection),
                Flat(run.ExitTravelDirection));
            float resolvedPortalCrossingMiss =
                Mathf.Max(entryMiss, exitMiss);
            float heroMissLimit =
                Mathf.Max(0.75f, roomSpan * 0.35f);
            bool compatible =
                resolvedEntryContinuation + 0.01f >=
                entryContinuationTarget &&
                resolvedExitContinuation + 0.01f >=
                exitContinuationTarget &&
                resolvedEntryWallAngle <= MaximumCompatibleWallAngle &&
                resolvedExitWallAngle <= MaximumCompatibleWallAngle &&
                resolvedPortalCrossingMiss <= 0.75f &&
                resolvedHeroMissDistance <= heroMissLimit;

            placement = new ShowcaseStagePlacement(
                ShowcaseStagePlacementMode.PortalAlignedRigid,
                position,
                rotation,
                resolvedScale,
                sourceFocusPosition,
                resolvedEntryContinuation,
                resolvedExitContinuation,
                entryContinuationTarget,
                exitContinuationTarget,
                resolvedHeroMissDistance,
                heroMissLimit,
                resolvedEntryWallAngle,
                resolvedExitWallAngle,
                resolvedPortalCrossingMiss,
                compatible);
            if (placement.IsValid)
                return true;

            placement = default;
            failure =
                "The event stage placement candidate contains invalid values.";
            return false;
        }

        private void PrepareLifeSizeDriveByPlan(
            ShowcaseRun run,
            Transform stage,
            VehicleBinding first,
            VehicleBinding second)
        {
            lifeSizeRoad?.Clear();
            lifeSizeVehicles?.Clear();
            preparedLifeSizePlan = null;
            lifeSizePlanFailure = "";
            if (presentationMode !=
                ShowcasePresentationMode.LifeSizeDriveByExperimental)
            {
                return;
            }

            lifeSizeDriveBy ??= new LifeSizeDriveBySettings();
            if (!LifeSizeDriveByPlanner.TryPrepare(
                    run,
                    showcaseLayout,
                    lifeSizeDriveBy,
                    out preparedLifeSizePlan,
                    out lifeSizePlanFailure))
            {
                preparedLifeSizePlan = null;
                return;
            }

            if (lifeSizeRoad == null ||
                !lifeSizeRoad.TryPrepare(
                    preparedLifeSizePlan,
                    stage,
                    out lifeSizePlanFailure) ||
                lifeSizeVehicles == null ||
                !lifeSizeVehicles.TryPrepare(
                    eventReplay,
                    preparedLifeSizePlan,
                    first.DriverNumber,
                    second.DriverNumber,
                    ResolveCar(first),
                    ResolveCar(second),
                    out lifeSizePlanFailure))
            {
                lifeSizeRoad?.Clear();
                lifeSizeVehicles?.Clear();
                preparedLifeSizePlan = null;
            }
        }

        private bool TryCommitLifeSizeDriveBy(out string failure)
        {
            failure = "";
            if (presentationMode !=
                    ShowcasePresentationMode.LifeSizeDriveByExperimental ||
                preparedLifeSizePlan == null)
            {
                return false;
            }

            if (lifeSizeVehicles == null ||
                lifeSizeRoad == null ||
                !lifeSizeVehicles.TryCommit(
                    preparedLifeSizePlan,
                    out failure))
            {
                lifeSizeVehicles?.Clear();
                lifeSizeRoad?.Clear();
                preparedLifeSizePlan = null;
                return false;
            }

            if (lifeSizeRoad.TryCommit(
                    preparedLifeSizePlan,
                    out failure))
            {
                appliedRoomVehicleLength =
                    preparedLifeSizePlan.VehicleLength;
                vehicleLengthAfter = appliedRoomVehicleLength;
                appliedPresentationScale = vehicleLengthBefore > 0f
                    ? vehicleLengthAfter / vehicleLengthBefore
                    : 1f;
                return true;
            }

            lifeSizeVehicles.Clear();
            lifeSizeRoad.Clear();
            preparedLifeSizePlan = null;
            return false;
        }

        private bool TryCreateRoomDioramaPlacement(
            IReadOnlyList<Vector3> sourcePath,
            Vector3 sourceEntryPosition,
            Vector3 sourceFocusPosition,
            Vector3 sourceExitPosition,
            ShowcaseRun run,
            ShowcaseStagePlacement portalAlignedPlacement,
            out ShowcaseStagePlacement placement,
            out string failure)
        {
            placement = default;
            failure = "";
            if (sourcePath == null ||
                sourcePath.Count < 2 ||
                !run.IsValid ||
                !portalAlignedPlacement.IsValid)
            {
                failure =
                    "The source track is unavailable for Hero-anchored placement.";
                return false;
            }

            int focusIndex = FindClosestPointIndex(
                sourcePath,
                sourceFocusPosition);
            int entryIndex = FindClosestPointIndex(
                sourcePath,
                sourceEntryPosition);
            int exitIndex = FindClosestPointIndex(
                sourcePath,
                sourceExitPosition);
            if (focusIndex < 0 ||
                entryIndex < 0 ||
                exitIndex < 0)
            {
                failure =
                    "The source track landmarks are unavailable for Hero-anchored placement.";
                return false;
            }

            Vector3 sourceFocusDirection =
                FindDirectionAt(sourcePath, focusIndex);
            Vector3 heroForward = Flat(run.FocusPose.forward);
            if (heroForward.sqrMagnitude <= 0.000001f)
            {
                heroForward = Flat(
                    run.ExitPose.position -
                    run.EntryPose.position);
            }

            sourceFocusDirection = Flat(sourceFocusDirection);
            if (sourceFocusDirection.sqrMagnitude <= 0.000001f ||
                heroForward.sqrMagnitude <= 0.000001f)
            {
                failure =
                    "The source track or Hero has no stable travel direction.";
                return false;
            }

            sourceFocusDirection.Normalize();
            heroForward.Normalize();
            float yaw = Vector3.SignedAngle(
                sourceFocusDirection,
                heroForward,
                Vector3.up);
            Quaternion rotation =
                Quaternion.Euler(0f, yaw, 0f);
            float scale = portalAlignedPlacement.UniformScale;
            Vector3 overtakeTarget =
                run.FocusPose.position +
                heroForward * heroForwardOffset;
            Vector3 position =
                overtakeTarget -
                rotation * sourceFocusPosition * scale;
            position.y =
                run.FloorHeight +
                roadFloorOffset -
                (rotation * sourceFocusPosition * scale).y;

            Vector3 mappedEntry =
                position +
                rotation * sourceEntryPosition * scale;
            Vector3 mappedExit =
                position +
                rotation * sourceExitPosition * scale;
            Vector3 mappedFocus =
                position +
                rotation * sourceFocusPosition * scale;
            Vector3 sourceEntryDirection =
                FindDirectionAt(sourcePath, entryIndex);
            Vector3 sourceExitDirection =
                FindDirectionAt(sourcePath, exitIndex);
            float resolvedEntryWallAngle = Vector3.Angle(
                Flat(rotation * sourceEntryDirection),
                Flat(run.EntryTravelDirection));
            float resolvedExitWallAngle = Vector3.Angle(
                Flat(rotation * sourceExitDirection),
                Flat(run.ExitTravelDirection));
            float entryMiss = Flat(
                mappedEntry - run.EntryPose.position).magnitude;
            float exitMiss = Flat(
                mappedExit - run.ExitPose.position).magnitude;
            float resolvedPortalCrossingMiss =
                Mathf.Max(entryMiss, exitMiss);
            float resolvedHeroMissDistance = Flat(
                mappedFocus - overtakeTarget).magnitude;

            placement = new ShowcaseStagePlacement(
                ShowcaseStagePlacementMode.RoomDioramaRigid,
                position,
                rotation,
                scale,
                sourceFocusPosition,
                portalAlignedPlacement.EntryContinuation,
                portalAlignedPlacement.ExitContinuation,
                portalAlignedPlacement.EntryContinuationTarget,
                portalAlignedPlacement.ExitContinuationTarget,
                resolvedHeroMissDistance,
                portalAlignedPlacement.HeroMissLimit,
                resolvedEntryWallAngle,
                resolvedExitWallAngle,
                resolvedPortalCrossingMiss,
                false);
            if (placement.IsValid)
                return true;

            placement = default;
            failure =
                "The RoomDiorama source track placement contains invalid values.";
            return false;
        }

        private static bool TryValidateEventStagePlacement(
            ShowcaseStagePlacement placement,
            out string failure)
        {
            failure = "";
            if (!placement.IsValid)
            {
                failure =
                    "The event stage placement candidate is invalid.";
                return false;
            }

            if (placement.Mode ==
                ShowcaseStagePlacementMode.RoomDioramaRigid)
            {
                return true;
            }

            if (placement.Mode !=
                ShowcaseStagePlacementMode.PortalAlignedRigid)
            {
                failure =
                    "The event stage placement mode is unsupported.";
                return false;
            }

            if (placement.WallPairCompatible)
                return true;

            failure =
                "The rigid source track is incompatible with the selected walls. " +
                $"continuation={placement.EntryContinuation:0.##}/" +
                $"{placement.EntryContinuationTarget:0.##}m entry, " +
                $"{placement.ExitContinuation:0.##}/" +
                $"{placement.ExitContinuationTarget:0.##}m exit; " +
                $"wallAngles={placement.EntryWallAngle:0.#}/" +
                $"{placement.ExitWallAngle:0.#}deg; " +
                $"portalMiss={placement.PortalCrossingMiss:0.###}m; " +
                $"heroMiss={placement.HeroMissDistance:0.###}/" +
                $"{placement.HeroMissLimit:0.###}m.";
            return false;
        }

        private bool TryCommitEventStagePlacement(
            ShowcaseStagePlacement placement,
            out string failure)
        {
            failure = "";
            if (eventReplay == null ||
                !placement.IsValid ||
                (placement.Mode ==
                     ShowcaseStagePlacementMode.PortalAlignedRigid &&
                 !placement.WallPairCompatible))
            {
                failure =
                    "The event stage placement was not ready to commit.";
                return false;
            }

            if (eventReplay.TryApplyRoomStagePlacement(
                    placement.Position,
                    placement.Rotation,
                    placement.UniformScale,
                    placement.InteractionFocus))
            {
                return true;
            }

            failure =
                "The EventReplayStage rejected the validated placement commit.";
            return false;
        }

        private void CapturePlacementDiagnostics(
            ShowcaseStagePlacement placement)
        {
            eventCoordinateScale = placement.UniformScale;
            entryContinuation = placement.EntryContinuation;
            exitContinuation = placement.ExitContinuation;
            heroMissDistance = placement.HeroMissDistance;
            entryWallAngle = placement.EntryWallAngle;
            exitWallAngle = placement.ExitWallAngle;
            portalCrossingMiss = placement.PortalCrossingMiss;
            wallPairCompatible = placement.WallPairCompatible;
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

        private void ResolvePortalTransitionVehicles(
            VehicleBinding first,
            VehicleBinding second,
            out Transform overtakingVehicle,
            out Transform defendingVehicle)
        {
            overtakingVehicle = null;
            defendingVehicle = null;
            int overtakingDriver =
                eventReplay.OvertakeFinalLeader;
            if (first != null &&
                first.DriverNumber == overtakingDriver)
            {
                overtakingVehicle =
                    first.VisualMotionRoot;
                defendingVehicle =
                    second != null
                        ? second.VisualMotionRoot
                        : null;
            }
            else if (
                second != null &&
                second.DriverNumber == overtakingDriver)
            {
                overtakingVehicle =
                    second.VisualMotionRoot;
                defendingVehicle =
                    first != null
                        ? first.VisualMotionRoot
                        : null;
            }
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

        private void ApplyRoomVehiclePresentation(
            VehicleBinding first,
            VehicleBinding second)
        {
            ReplayCarView firstCar = ResolveCar(first);
            ReplayCarView secondCar = ResolveCar(second);
            if (firstCar == null || secondCar == null)
                return;

            firstCar.ClearRoomPresentation();
            secondCar.ClearRoomPresentation();

            Vector3 presentationAnchor =
                (first.VisualMotionRoot.position +
                 second.VisualMotionRoot.position) *
                0.5f;
            float scale =
                immersiveScaleEnabled &&
                portalPresentation != null
                    ? showcaseVehicleScale *
                      Mathf.Max(
                          1f,
                          portalPresentation.EvaluateImmersiveScale(
                              presentationAnchor))
                    : showcaseVehicleScale;
            firstCar.ApplyRoomPresentation(
                presentationAnchor,
                scale);
            secondCar.ApplyRoomPresentation(
                presentationAnchor,
                scale);
            appliedPresentationScale = scale;
            vehicleLengthAfter =
                vehicleLengthBefore *
                appliedPresentationScale;
            appliedRoomVehicleLength =
                vehicleLengthAfter;
        }

        private void ApplyActiveVehiclePresentation(
            VehicleBinding first,
            VehicleBinding second)
        {
            if (lifeSizeVehicles != null &&
                lifeSizeVehicles.IsCommitted)
            {
                lifeSizeVehicles.ApplyPresentationScale();
                return;
            }

            ApplyRoomVehiclePresentation(first, second);
        }

        private static ReplayCarView ResolveCar(
            VehicleBinding binding)
        {
            if (binding == null ||
                binding.VisualMotionRoot == null)
            {
                return null;
            }

            return binding.VisualMotionRoot
                .GetComponent<ReplayCarView>();
        }

        private static void RestoreVehiclePresentation(
            VehicleBinding binding)
        {
            ReplayCarView car = ResolveCar(binding);
            if (car != null)
                car.ClearRoomPresentation();
            else if (binding != null &&
                     binding.VisualMotionRoot != null)
                binding.VisualMotionRoot.localScale =
                    binding.OriginalVisualLocalScale;
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

        private void ReleaseBinding(
            bool restoreStage = false,
            bool restoreTableTrack = true)
        {
            Transform stageToRestore = boundStage;
            portalPresentation?.Clear();
            lifeSizeVehicles?.Clear();
            lifeSizeRoad?.Clear();
            RestoreVehiclePresentation(firstBinding);
            RestoreVehiclePresentation(secondBinding);
            eventReplay?.SetShowcaseDrivingPresentation(
                firstBinding != null
                    ? firstBinding.DriverNumber
                    : 0,
                secondBinding != null
                    ? secondBinding.DriverNumber
                    : 0,
                false);
            eventReplay?.SetShowcasePlaybackSpeedMultiplier(1f);
            eventReplay?.SetShowcaseAudioFocus(0);
            if (restoreTableTrack)
                eventReplay?.RestoreTableTrackRendering();

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
            boundLayoutRevision = -1;
            activeRun = default;
            preparedLifeSizePlan = null;
            lifeSizePlanFailure = "";
            activePlacementMode =
                ShowcaseStagePlacementMode.None;
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
