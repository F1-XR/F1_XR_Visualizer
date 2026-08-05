using System.Collections.Generic;
using F1XR.RestAPI.Api;
using UnityEngine;
using UnityEngine.Rendering;

namespace F1XR.RestAPI.Replay.Room
{
    internal readonly struct ShowcaseVisibleInterval
    {
        public ShowcaseVisibleInterval(float startTime, float endTime)
        {
            StartTime = startTime;
            EndTime = endTime;
        }

        public float StartTime { get; }
        public float EndTime { get; }
        public bool IsValid => EndTime >= StartTime;
    }

    internal readonly struct ShowcaseAdditionalShotRequest
    {
        public ShowcaseAdditionalShotRequest(
            int actionBeatIndex,
            ShowcaseActionBeat actionBeat,
            float transitionStartTime,
            float transitionEndTime,
            ShowcaseShotPlacementCandidate placement)
        {
            ActionBeatIndex = actionBeatIndex;
            ActionBeat = actionBeat;
            TransitionStartTime = transitionStartTime;
            TransitionEndTime = transitionEndTime;
            Placement = placement;
        }

        public int ActionBeatIndex { get; }
        public ShowcaseActionBeat ActionBeat { get; }
        public float TransitionStartTime { get; }
        public float TransitionEndTime { get; }
        public ShowcaseShotPlacementCandidate Placement { get; }
        public bool HasTransitionWindow =>
            TransitionEndTime > TransitionStartTime;
        public bool HasPlacement => Placement.IsValid;
    }

    internal readonly struct ShowcaseShotPlacementCandidate
    {
        public ShowcaseShotPlacementCandidate(
            Vector3 position,
            Quaternion rotation,
            float uniformScale,
            Vector3 eventLocalFocus)
        {
            Position = position;
            Rotation = rotation;
            UniformScale = uniformScale;
            EventLocalFocus = eventLocalFocus;
        }

        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public float UniformScale { get; }
        public Vector3 EventLocalFocus { get; }
        public bool IsValid =>
            IsFinite(Position) &&
            IsFinite(Rotation) &&
            IsFinite(EventLocalFocus) &&
            float.IsFinite(UniformScale) &&
            UniformScale > 0f;

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

    internal enum RoomDioramaCompositionMode
    {
        Balanced,
        TracksideImmersiveExperimental
    }

    internal enum WallPortalRecommendationMode
    {
        FreeTrackExit,
        ExitWall,
        WallPair
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

        [Header("Adaptive Room Diorama Presentation")]
        [SerializeField]
        private bool normalizeRoomPresentation = true;
        [SerializeField, Min(0.05f)]
        private float minimumRoomVehicleLength = 0.5f;
        [SerializeField, Min(0.05f)]
        private float maximumRoomVehicleLength = 0.7f;
        [SerializeField, Range(0.25f, 2f)]
        private float minimumVehiclePresentationScale = 0.5f;
        [SerializeField, Range(0.25f, 2f)]
        private float maximumVehiclePresentationScale = 1.1f;
        [SerializeField, Min(0.1f)]
        private float minimumBoostedBattleTravelInVehicleLengthsPerSecond = 10f;
        [SerializeField, Min(1f)]
        private float maximumBoostedPlaybackSpeed = 2.4f;

        [Header("Room Diorama Composition")]
        [SerializeField]
        private RoomDioramaCompositionMode compositionMode =
            RoomDioramaCompositionMode.Balanced;
        [SerializeField, Range(0f, 1f)]
        private float criticalSegmentCenterBias = 0.65f;
        [SerializeField, Min(0f)]
        private float balancedMaximumShift = 2f;
        [SerializeField, Min(0f)]
        private float multiExchangeMaximumShift = 3.5f;

        [Header("Reference Pass Profile")]
        [SerializeField]
        private bool referencePassProfileEnabled = true;
        [SerializeField, Range(-60f, 60f)]
        private float referencePassCrossingYaw = 28f;

        [Header("Portals")]
        [SerializeField]
        private bool trackExitPortalEnabled = true;
        [SerializeField]
        private bool connectedWallPortalsEnabled = true;

        [Header("Wall Portal Candidate Evaluation")]
        [SerializeField]
        private bool evaluateWallPortalCandidates = true;
        [SerializeField, Min(0.1f)]
        private float minimumEvaluatedPortalWallWidth = 2f;
        [SerializeField, Min(0.1f)]
        private float minimumEvaluatedPortalWallHeight = 1.7f;
        [SerializeField, Range(0f, 100f)]
        private float wallPairRecommendationScore = 60f;
        [SerializeField, Range(0f, 100f)]
        private float wallExitRecommendationScore = 52f;

        [Header("Wall Portal Evaluation Runtime")]
        [SerializeField] private int evaluatedWallCount;
        [SerializeField] private int evaluatedWallPairCount;
        [SerializeField] private int qualifiedWallPairCount;
        [SerializeField] private float bestWallPairScore;
        [SerializeField] private int evaluatedWallExitCount;
        [SerializeField] private int qualifiedWallExitCount;
        [SerializeField] private float bestWallExitScore;
        [SerializeField]
        private WallPortalRecommendationMode wallPortalRecommendation;

        [Header("Life Size Drive-By Contract")]
        [SerializeField]
        private LifeSizeDriveBySettings lifeSizeDriveBy = new();

        [Header("Overtake Exit Portal VFX")]
        [SerializeField]
        private OvertakePortalTransitionVfxSettings
            overtakePortalTransitionVfx = new();

        [Header("Multi-Shot Visibility Analysis")]
        [SerializeField] private bool analyzeShotVisibility = true;
        [SerializeField, Min(0.05f)]
        private float visibilitySampleSeconds = 0.2f;
        [SerializeField, Min(0.1f)]
        private float visibilityMaximumDistance = 8f;
        [SerializeField, Range(0f, 0.45f)]
        private float visibilityViewportMargin = 0.05f;
        [SerializeField, Range(0f, 1f)]
        private float visibilityProbeHeightInVehicleLengths = 0.25f;
        [SerializeField, Range(0f, 1f)]
        private float visibilityProbeCenterInVehicleHeight = 0.5f;
        [SerializeField, Range(0f, 0.5f)]
        private float visibilityProbeSpanInVehicleHeight = 0.35f;
        [SerializeField, Min(0f)]
        private float visibilityTerrainClearanceInVehicleLengths = 0.35f;

        [Header("Multi-Shot Battle Reframe")]
        [SerializeField, Range(0.2f, 1.5f)]
        private float battleReframeDuration = 0.7f;
        [SerializeField]
        private Color battleReframeStartColor =
            new(0.05f, 0.78f, 1f, 0.82f);
        [SerializeField]
        private Color battleReframeEndColor =
            new(1f, 0.12f, 0.62f, 0.82f);

        [Header("Multi-Shot Runtime Analysis")]
        [SerializeField] private int actionBeatCount;
        [SerializeField] private int visibleActionBeatCount;
        [SerializeField] private int continuousVisibleIntervalCount;
        [SerializeField] private bool terrainOcclusionAvailable;
        [SerializeField] private bool allActionBeatsVisible;
        [SerializeField] private bool requiresAdditionalShot;
        [SerializeField] private int actionBeatsOutsideDistance;
        [SerializeField] private int actionBeatsOutsideView;
        [SerializeField] private int actionBeatsTerrainOccluded;
        [SerializeField] private int actionBeatsWithMissingPosition;

        [Header("Reference Pass Runtime Metrics")]
        [SerializeField] private Vector3 perceptionEyeRelativeTrackFocus;
        [SerializeField] private float perceptionClosestVehicleDistance;
        [SerializeField] private float perceptionVehicleAngularSize;
        [SerializeField] private float perceptionScreenTravelPerSecond;
        [SerializeField, Range(0f, 1f)]
        private float perceptionBattleVisibleFraction;

        [Header("Reference Pass Live Runtime Metrics")]
        [SerializeField] private int runtimePerceptionSampleCount;
        [SerializeField, Range(0f, 1f)]
        private float runtimePerceptionVisibleFraction;
        [SerializeField] private float runtimePerceptionScreenSpeedP90;
        [SerializeField] private float runtimePerceptionScreenSpeedPeak;
        [SerializeField] private float runtimePerceptionLateralSpeedP90;
        [SerializeField] private float runtimePerceptionLateralSpeedPeak;
        [SerializeField] private float runtimePerceptionLoomingRateP90;
        [SerializeField] private float runtimePerceptionLoomingRatePeak;
        [SerializeField] private float runtimePerceptionAngularSizePeak;
        [SerializeField] private float runtimePerceptionClosestDistance;
        [SerializeField] private Vector3 runtimePerceptionClosestEyePosition;

        [Header("Control")]
        [SerializeField] private bool mappingEnabled = true;

        private readonly List<Vector3> eventLocalPath = new();
        private readonly List<ShowcaseWallFrame>
            wallPortalEvaluationFrames = new();
        private readonly List<ShowcaseActionBeat> actionBeats = new();
        private readonly List<ShowcaseActionBeat>
            presentationCalibrationBeats = new();
        private readonly List<bool> actionBeatVisibility = new();
        private readonly List<ShowcaseVisibleInterval>
            visibleIntervals = new();
        private readonly List<ShowcaseAdditionalShotRequest>
            additionalShotRequests = new();
        private readonly List<float> runtimeScreenSpeeds = new(2048);
        private readonly List<float> runtimeLateralSpeeds = new(2048);
        private readonly List<float> runtimeLoomingRates = new(2048);
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
        private float resolvedShowcaseVehicleScale;
        private float resolvedShowcasePlaybackSpeedMultiplier;
        private float measuredBattleTravelInVehicleLengthsPerSecond;
        private float entryContinuation;
        private float exitContinuation;
        private float heroMissDistance;
        private float entryWallAngle;
        private float exitWallAngle;
        private float portalCrossingMiss;
        private float compositionShiftDistance;
        private float appliedReferencePassYaw;
        private Vector3 referencePassTravelDirection;
        private bool connectedPortalCandidate;
        private bool hasRecommendedWallExitPlacement;
        private bool appliedRecommendedWallExitPlacement;
        private ShowcaseStagePlacement recommendedWallExitPlacement;
        private ShowcaseWallFrame recommendedWallExitFrame;
        private string activePortalMode = "None";
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
        private float firstVisibilityVehicleHeight;
        private float secondVisibilityVehicleHeight;
        private float visibilityAnalysisSampleStep;
        private ShowcaseShotPlacementCandidate initialShotPlacement;
        private int nextAdditionalShotRequestIndex;
        private int activeAdditionalShotRequestIndex = -1;
        private int observedShowcaseTimelineRevision = -1;
        private float previousShotReplayTime;
        private bool shotPlacementRuntimeInitialized;
        private bool battleReframeActive;
        private float battleReframeElapsed;
        private int battleReframeRequestIndex = -1;
        private ShowcaseShotPlacementCandidate battleReframeFrom;
        private ShowcaseShotPlacementCandidate battleReframeTo;
        private Transform battleReframeCueRoot;
        private LineRenderer battleReframeCue;
        private Material battleReframeCueMaterial;
        private bool deferExitPortalUntilFinalShot;
        private int finalAdditionalShotRequestIndex = -1;
        private int lastProcessedAdditionalShotRequestIndex = -1;
        private bool usesNaturalVisibilityEnd;
        private float naturalVisibilityEndTime;
        private int runtimePerceptionTimelineRevision = -1;
        private int runtimePerceptionAttemptedSamples;
        private int runtimePerceptionVisibleSamples;
        private bool runtimePerceptionPublished;
        private bool runtimeFirstPreviousValid;
        private bool runtimeSecondPreviousValid;
        private Vector2 runtimeFirstPreviousViewport;
        private Vector2 runtimeSecondPreviousViewport;
        private float runtimeFirstPreviousAngularSize;
        private float runtimeSecondPreviousAngularSize;

        private enum VisibilityFailure
        {
            None = 0,
            MissingPosition = 1,
            Distance = 2,
            View = 4,
            Terrain = 8
        }

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
        public float ResolvedShowcasePlaybackSpeedMultiplier =>
            resolvedShowcasePlaybackSpeedMultiplier;
        public float MeasuredBattleTravelInVehicleLengthsPerSecond =>
            measuredBattleTravelInVehicleLengthsPerSecond;
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
        internal IReadOnlyList<ShowcaseVisibleInterval>
            VisibleIntervals => visibleIntervals;
        internal IReadOnlyList<bool> ActionBeatVisibility =>
            actionBeatVisibility;
        internal IReadOnlyList<ShowcaseAdditionalShotRequest>
            AdditionalShotRequests => additionalShotRequests;
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
            minimumRoomVehicleLength = Mathf.Max(
                0.05f,
                minimumRoomVehicleLength);
            maximumRoomVehicleLength = Mathf.Max(
                minimumRoomVehicleLength,
                maximumRoomVehicleLength);
            minimumVehiclePresentationScale = Mathf.Clamp(
                minimumVehiclePresentationScale,
                0.25f,
                2f);
            maximumVehiclePresentationScale = Mathf.Clamp(
                maximumVehiclePresentationScale,
                minimumVehiclePresentationScale,
                2f);
            minimumBoostedBattleTravelInVehicleLengthsPerSecond =
                Mathf.Max(
                    0.1f,
                    minimumBoostedBattleTravelInVehicleLengthsPerSecond);
            maximumBoostedPlaybackSpeed = Mathf.Max(
                showcasePlaybackSpeedMultiplier,
                maximumBoostedPlaybackSpeed);
            criticalSegmentCenterBias = Mathf.Clamp01(
                criticalSegmentCenterBias);
            balancedMaximumShift = Mathf.Max(
                0f,
                balancedMaximumShift);
            multiExchangeMaximumShift = Mathf.Max(
                0f,
                multiExchangeMaximumShift);
            referencePassCrossingYaw = Mathf.Clamp(
                referencePassCrossingYaw,
                -60f,
                60f);
            minimumEvaluatedPortalWallWidth = Mathf.Max(
                0.1f,
                minimumEvaluatedPortalWallWidth);
            minimumEvaluatedPortalWallHeight = Mathf.Max(
                0.1f,
                minimumEvaluatedPortalWallHeight);
            wallPairRecommendationScore = Mathf.Clamp(
                wallPairRecommendationScore,
                0f,
                100f);
            wallExitRecommendationScore = Mathf.Clamp(
                wallExitRecommendationScore,
                0f,
                100f);
            visibilitySampleSeconds = Mathf.Max(
                0.05f,
                visibilitySampleSeconds);
            visibilityMaximumDistance = Mathf.Max(
                0.1f,
                visibilityMaximumDistance);
            visibilityViewportMargin = Mathf.Clamp(
                visibilityViewportMargin,
                0f,
                0.45f);
            visibilityProbeHeightInVehicleLengths = Mathf.Clamp01(
                visibilityProbeHeightInVehicleLengths);
            visibilityProbeCenterInVehicleHeight = Mathf.Clamp01(
                visibilityProbeCenterInVehicleHeight);
            visibilityProbeSpanInVehicleHeight = Mathf.Clamp(
                visibilityProbeSpanInVehicleHeight,
                0f,
                0.5f);
            visibilityTerrainClearanceInVehicleLengths = Mathf.Max(
                0f,
                visibilityTerrainClearanceInVehicleLengths);
            battleReframeDuration = Mathf.Clamp(
                battleReframeDuration,
                0.2f,
                1.5f);
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
                    : ResolveActiveShowcaseVehicleScale());

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

            UpdateAdditionalShotPlacement();
            ApplyActiveVehiclePresentation(
                firstBinding,
                secondBinding);
            RevealStageAfterReplayMotion();
            UpdateOrderDiagnostics();
            UpdateRuntimePerceptionMetrics();
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
            DisposeBattleReframeCue();
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
            appliedReferencePassYaw = 0f;
            referencePassTravelDirection = Vector3.zero;

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
            EvaluateWallPortalCandidates(
                eventLocalPath,
                entryPosition,
                focusPosition,
                exitPosition,
                run,
                portalAlignedPlacement);
            appliedRecommendedWallExitPlacement =
                trackExitPortalEnabled &&
                wallPortalRecommendation ==
                    WallPortalRecommendationMode.ExitWall &&
                hasRecommendedWallExitPlacement;
            connectedPortalCandidate =
                connectedWallPortalsEnabled &&
                compositionMode ==
                    RoomDioramaCompositionMode.Balanced &&
                portalAlignedPlacement.WallPairCompatible;
            if (appliedRecommendedWallExitPlacement)
            {
                placement = recommendedWallExitPlacement;
            }
            else if (!connectedPortalCandidate &&
                !TryCreateRoomDioramaPlacement(
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

            ResolveAdaptiveRoomPresentation(
                stage,
                first,
                second,
                usesLifeSize);
            eventReplay.SetOvertakeVehicleSizeScale(
                usesLifeSize
                    ? 1f
                    : ResolveActiveShowcaseVehicleScale());

            eventReplay.SetShowcasePlaybackSpeedMultiplier(
                ResolveActiveShowcasePlaybackSpeed());
            bool usesWallPortals = !usesLifeSize &&
                placement.Mode ==
                ShowcaseStagePlacementMode.PortalAlignedRigid;
            bool usesTrackExitPortal =
                !usesLifeSize &&
                trackExitPortalEnabled &&
                placement.Mode ==
                    ShowcaseStagePlacementMode.RoomDioramaRigid;
            bool usesPortals =
                usesWallPortals || usesTrackExitPortal;
            activePortalMode = usesWallPortals
                ? "WallPair"
                : usesTrackExitPortal
                    ? appliedRecommendedWallExitPlacement
                        ? "ExitWall"
                        : "TrackExit"
                    : "None";
            if (usesWallPortals)
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
            else if (usesTrackExitPortal)
            {
                bool hasExitPose =
                    appliedRecommendedWallExitPlacement
                        ? TryCreateWallExitPortalPose(
                            stage,
                            exitPosition,
                            recommendedWallExitFrame,
                            out Pose trackExitPose)
                        : TryCreateTrackExitPortalPose(
                            stage,
                            eventLocalPath,
                            exitPosition,
                            out trackExitPose);
                if (!hasExitPose ||
                    !portalPresentation.ConfigureTrackExit(
                        stage,
                        trackExitPose,
                        first.VehicleRoot,
                        second.VehicleRoot,
                        out _))
                {
                    portalPresentation.Clear();
                    usesPortals = false;
                    activePortalMode = "None";
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
            eventReplay.SetShowcaseDrivingPresentation(
                first.DriverNumber,
                second.DriverNumber,
                true);

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
            AnalyzeCurrentShotVisibility();
            InitializeShotPlacementRuntime(placement);
            InitializeFinalShotExitPortalVisibility();
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
                $"composition={compositionMode}, " +
                $"portalCandidate={connectedPortalCandidate}, " +
                $"portalMode={activePortalMode}, " +
                $"eventScale={eventCoordinateScale:0.###}, " +
                $"entryContinuation={entryContinuation:0.##}m, " +
                $"exitContinuation={exitContinuation:0.##}m, " +
                $"wallPairCompatible={wallPairCompatible}, " +
                $"wallAngles={entryWallAngle:0.#}/{exitWallAngle:0.#}deg, " +
                $"portalMiss={portalCrossingMiss:0.###}m, " +
                $"heroMiss={heroMissDistance:0.###}m, " +
                $"compositionShift={compositionShiftDistance:0.###}m, " +
                $"referencePassYaw={appliedReferencePassYaw:0.#}deg, " +
                $"vehicleLength={vehicleLengthBefore:0.###}m->{vehicleLengthAfter:0.###}m, " +
                $"vehicleScale={appliedPresentationScale:0.#####}, " +
                $"showcaseSpeed={ResolveActiveShowcasePlaybackSpeed():0.###}x, " +
                $"speedFloor={minimumBoostedBattleTravelInVehicleLengthsPerSecond:0.##} car/s, " +
                $"speedCeiling={maximumBoostedPlaybackSpeed:0.###}x, " +
                $"battleTravel={measuredBattleTravelInVehicleLengthsPerSecond:0.##} car/s, " +
                $"portals={portalPresentation.IsConfigured}",
                this);
            return true;
        }

        private void AnalyzeCurrentShotVisibility()
        {
            eventReplay?.ClearShowcasePresentationEndTime();
            ResetShotVisibilityAnalysis();
            if (!analyzeShotVisibility ||
                eventReplay == null ||
                boundStage == null ||
                firstBinding == null ||
                secondBinding == null ||
                activePlacementMode ==
                    ShowcaseStagePlacementMode.LifeSizeDriveBy ||
                Camera.main == null ||
                !eventReplay.TryGetShowcasePlaybackWindow(
                    out ShowcasePlaybackWindow window) ||
                !eventReplay.TryCopyShowcaseActionBeats(actionBeats))
            {
                return;
            }

            actionBeatCount = actionBeats.Count;
            Camera viewer = Camera.main;
            firstVisibilityVehicleHeight =
                MeasureWorldVisualHeight(firstBinding, boundStage.up);
            secondVisibilityVehicleHeight =
                MeasureWorldVisualHeight(secondBinding, boundStage.up);
            float sampleStep = Mathf.Max(
                0.05f,
                visibilitySampleSeconds);
            int sampleCount = Mathf.Max(
                1,
                Mathf.CeilToInt(
                    (window.EndTime - window.StartTime) /
                    sampleStep));
            visibilityAnalysisSampleStep =
                (window.EndTime - window.StartTime) /
                sampleCount;
            bool intervalOpen = false;
            float intervalStart = 0f;
            float lastVisibleTime = 0f;
            for (int i = 0; i <= sampleCount; i++)
            {
                float replayTime = Mathf.Lerp(
                    window.StartTime,
                    window.EndTime,
                    i / (float)sampleCount);
                bool visible = TryEvaluateBattleVisibility(
                    viewer,
                    replayTime,
                    out _);
                if (visible && !intervalOpen)
                {
                    intervalOpen = true;
                    intervalStart = replayTime;
                }
                if (visible)
                {
                    lastVisibleTime = replayTime;
                }
                else if (intervalOpen)
                {
                    intervalOpen = false;
                    visibleIntervals.Add(
                        new ShowcaseVisibleInterval(
                            intervalStart,
                            lastVisibleTime));
                }
            }
            if (intervalOpen)
            {
                visibleIntervals.Add(
                    new ShowcaseVisibleInterval(
                        intervalStart,
                        lastVisibleTime));
            }
            continuousVisibleIntervalCount = visibleIntervals.Count;

            for (int i = 0; i < actionBeats.Count; i++)
            {
                bool visible = TryEvaluateActionBeatVisibility(
                    viewer,
                    actionBeats[i],
                    out VisibilityFailure failure);
                actionBeatVisibility.Add(visible);
                if (visible)
                {
                    visibleActionBeatCount++;
                }
                else
                {
                    AccumulateActionBeatFailure(failure);
                }
            }

            allActionBeatsVisible =
                actionBeatCount > 0 &&
                visibleActionBeatCount == actionBeatCount;
            requiresAdditionalShot =
                actionBeatCount > 0 &&
                !allActionBeatsVisible;
            BuildAdditionalShotRequests();
            TryConfigureNaturalVisibilityEnd(window);
            MeasureShowcasePerception(viewer);
            int transitionCount = 0;
            int placementCount = 0;
            for (int i = 0; i < additionalShotRequests.Count; i++)
            {
                if (additionalShotRequests[i].HasTransitionWindow)
                    transitionCount++;
                if (additionalShotRequests[i].HasPlacement)
                    placementCount++;
            }
            Debug.Log(
                $"[ShowcaseVisibility] beats=" +
                $"{visibleActionBeatCount}/{actionBeatCount}, " +
                $"intervals={continuousVisibleIntervalCount}, " +
                $"terrainAvailable={terrainOcclusionAvailable}, " +
                $"requiresAdditionalShot={requiresAdditionalShot}, " +
                $"additionalShotRequests=" +
                $"{additionalShotRequests.Count}, " +
                $"transitionWindows={transitionCount}, " +
                $"placementCandidates={placementCount}, " +
                $"naturalEnd=" +
                $"{(usesNaturalVisibilityEnd ? naturalVisibilityEndTime.ToString("0.00") : "none")}, " +
                $"failures(distance/view/terrain/missing)=" +
                $"{actionBeatsOutsideDistance}/" +
                $"{actionBeatsOutsideView}/" +
                $"{actionBeatsTerrainOccluded}/" +
                $"{actionBeatsWithMissingPosition}.",
                this);
        }

        private void MeasureShowcasePerception(Camera viewer)
        {
            perceptionEyeRelativeTrackFocus = Vector3.zero;
            perceptionClosestVehicleDistance = 0f;
            perceptionVehicleAngularSize = 0f;
            perceptionScreenTravelPerSecond = 0f;
            perceptionBattleVisibleFraction = 0f;
            if (viewer == null ||
                eventReplay == null ||
                boundStage == null ||
                firstBinding == null ||
                secondBinding == null ||
                actionBeats.Count == 0)
            {
                return;
            }

            Vector3 eye = viewer.transform.position;
            float closestDistance = float.PositiveInfinity;
            float screenDistance = 0f;
            float screenDuration = 0f;
            int validSamples = 0;
            int visibleSamples = 0;
            float sampleStep = Mathf.Max(
                0.05f,
                visibilitySampleSeconds);
            for (int beatIndex = 0;
                beatIndex < actionBeats.Count;
                beatIndex++)
            {
                ShowcaseActionBeat beat = actionBeats[beatIndex];
                float duration = Mathf.Max(
                    0f,
                    beat.EndTime - beat.StartTime);
                int sampleCount = Mathf.Max(
                    1,
                    Mathf.CeilToInt(duration / sampleStep));
                bool hasPreviousScreenPosition = false;
                float previousTime = 0f;
                Vector2 previousScreenPosition = Vector2.zero;
                for (int sampleIndex = 0;
                    sampleIndex <= sampleCount;
                    sampleIndex++)
                {
                    float replayTime = Mathf.Lerp(
                        beat.StartTime,
                        beat.EndTime,
                        sampleIndex / (float)sampleCount);
                    if (!eventReplay.TryGetEventLocalVehiclePosition(
                            firstBinding.DriverNumber,
                            replayTime,
                            out Vector3 firstLocal) ||
                        !eventReplay.TryGetEventLocalVehiclePosition(
                            secondBinding.DriverNumber,
                            replayTime,
                            out Vector3 secondLocal))
                    {
                        hasPreviousScreenPosition = false;
                        continue;
                    }

                    Vector3 firstTrackPoint =
                        boundStage.TransformPoint(firstLocal);
                    Vector3 secondTrackPoint =
                        boundStage.TransformPoint(secondLocal);
                    Vector3 up = boundStage.up;
                    Vector3 firstCenter =
                        firstTrackPoint +
                        up * firstVisibilityVehicleHeight * 0.5f;
                    Vector3 secondCenter =
                        secondTrackPoint +
                        up * secondVisibilityVehicleHeight * 0.5f;
                    float firstDistance = Vector3.Distance(
                        eye,
                        firstCenter);
                    float secondDistance = Vector3.Distance(
                        eye,
                        secondCenter);
                    float sampleClosestDistance = Mathf.Min(
                        firstDistance,
                        secondDistance);
                    if (sampleClosestDistance < closestDistance)
                    {
                        closestDistance = sampleClosestDistance;
                        Vector3 trackFocus =
                            (firstTrackPoint + secondTrackPoint) * 0.5f;
                        perceptionEyeRelativeTrackFocus =
                            viewer.transform.InverseTransformPoint(
                                trackFocus);
                    }

                    Vector3 firstViewport =
                        viewer.WorldToViewportPoint(firstCenter);
                    Vector3 secondViewport =
                        viewer.WorldToViewportPoint(secondCenter);
                    if (firstViewport.z > viewer.nearClipPlane &&
                        secondViewport.z > viewer.nearClipPlane)
                    {
                        Vector2 screenPosition =
                            (new Vector2(
                                 firstViewport.x,
                                 firstViewport.y) +
                             new Vector2(
                                 secondViewport.x,
                                 secondViewport.y)) * 0.5f;
                        if (hasPreviousScreenPosition)
                        {
                            float segmentDuration =
                                replayTime - previousTime;
                            if (segmentDuration > 0f)
                            {
                                screenDistance += Vector2.Distance(
                                    previousScreenPosition,
                                    screenPosition);
                                screenDuration += segmentDuration;
                            }
                        }

                        hasPreviousScreenPosition = true;
                        previousTime = replayTime;
                        previousScreenPosition = screenPosition;
                    }
                    else
                    {
                        hasPreviousScreenPosition = false;
                    }

                    validSamples++;
                    if (TryEvaluateBattleVisibility(
                            viewer,
                            replayTime,
                            out _))
                    {
                        visibleSamples++;
                    }
                }
            }

            if (float.IsFinite(closestDistance))
            {
                perceptionClosestVehicleDistance = closestDistance;
                float safeDistance = Mathf.Max(
                    0.001f,
                    closestDistance);
                perceptionVehicleAngularSize =
                    2f * Mathf.Atan(
                        appliedRoomVehicleLength /
                        (2f * safeDistance)) *
                    Mathf.Rad2Deg;
            }
            if (screenDuration > 0.0001f)
            {
                perceptionScreenTravelPerSecond =
                    screenDistance /
                    screenDuration *
                    ResolveActiveShowcasePlaybackSpeed();
            }
            if (validSamples > 0)
            {
                perceptionBattleVisibleFraction =
                    visibleSamples /
                    (float)validSamples;
            }

            Debug.Log(
                $"[ShowcasePerception] drivers=" +
                $"{firstBinding.DriverNumber}/" +
                $"{secondBinding.DriverNumber}, " +
                $"eyeTrack={perceptionEyeRelativeTrackFocus:F3}m, " +
                $"closestPass={perceptionClosestVehicleDistance:0.###}m, " +
                $"angularVehicle={perceptionVehicleAngularSize:0.##}deg, " +
                $"screenTravel={perceptionScreenTravelPerSecond:0.###} viewport/s, " +
                $"battleVisible={perceptionBattleVisibleFraction:P0}.",
                this);
        }

        private void UpdateRuntimePerceptionMetrics()
        {
            if (eventReplay == null ||
                firstBinding == null ||
                secondBinding == null ||
                actionBeats.Count == 0)
            {
                ClearRuntimePerceptionPreviousSamples();
                return;
            }

            int timelineRevision =
                eventReplay.ShowcaseTimelineRevision;
            if (runtimePerceptionTimelineRevision != timelineRevision)
            {
                ResetRuntimePerceptionMetrics();
                runtimePerceptionTimelineRevision = timelineRevision;
            }

            float replayTime = eventReplay.CurrentTime;
            float finalActionTime = GetFinalRuntimeActionTime();
            if (runtimePerceptionPublished)
                return;

            if (!eventReplay.IsPlaying || battleReframeActive)
            {
                ClearRuntimePerceptionPreviousSamples();
                if (runtimePerceptionSampleCount > 0 &&
                    replayTime >= finalActionTime - 0.001f)
                {
                    PublishRuntimePerceptionMetrics(true);
                }
                return;
            }

            if (!IsRuntimeActionTime(replayTime))
            {
                ClearRuntimePerceptionPreviousSamples();
                if (runtimePerceptionSampleCount > 0 &&
                    replayTime > finalActionTime)
                {
                    PublishRuntimePerceptionMetrics(true);
                }
                return;
            }

            Camera viewer = Camera.main;
            float deltaTime = Time.unscaledDeltaTime;
            if (viewer == null || deltaTime <= 0.0001f)
            {
                ClearRuntimePerceptionPreviousSamples();
                return;
            }

            float firstHeight = MeasureWorldVisualHeight(
                firstBinding,
                boundStage.up);
            float secondHeight = MeasureWorldVisualHeight(
                secondBinding,
                boundStage.up);
            SampleRuntimeVehiclePerception(
                viewer,
                firstBinding,
                firstHeight,
                deltaTime,
                ref runtimeFirstPreviousValid,
                ref runtimeFirstPreviousViewport,
                ref runtimeFirstPreviousAngularSize);
            SampleRuntimeVehiclePerception(
                viewer,
                secondBinding,
                secondHeight,
                deltaTime,
                ref runtimeSecondPreviousValid,
                ref runtimeSecondPreviousViewport,
                ref runtimeSecondPreviousAngularSize);

            runtimePerceptionSampleCount =
                runtimePerceptionAttemptedSamples;
            runtimePerceptionVisibleFraction =
                runtimePerceptionAttemptedSamples > 0
                    ? runtimePerceptionVisibleSamples /
                      (float)runtimePerceptionAttemptedSamples
                    : 0f;
            if (replayTime >= finalActionTime - 0.001f)
                PublishRuntimePerceptionMetrics(true);
        }

        private void SampleRuntimeVehiclePerception(
            Camera viewer,
            VehicleBinding binding,
            float vehicleHeight,
            float deltaTime,
            ref bool previousValid,
            ref Vector2 previousViewport,
            ref float previousAngularSize)
        {
            runtimePerceptionAttemptedSamples++;
            if (binding == null ||
                binding.VisualMotionRoot == null ||
                !binding.VisualMotionRoot.gameObject.activeInHierarchy)
            {
                previousValid = false;
                return;
            }

            float vehicleLength = Mathf.Max(
                0.001f,
                appliedRoomVehicleLength);
            float safeHeight = Mathf.Max(0f, vehicleHeight);
            Vector3 center =
                binding.VisualMotionRoot.position +
                boundStage.up * safeHeight * 0.5f;
            if (!VehicleBoundsIntersectViewer(
                    viewer,
                    center,
                    vehicleLength * 0.5f,
                    safeHeight * 0.5f))
            {
                previousValid = false;
                return;
            }

            float terrainClearance = vehicleLength *
                visibilityTerrainClearanceInVehicleLengths;
            if (eventReplay.TryGetShowcaseTerrainOcclusion(
                    viewer.transform.position,
                    center,
                    terrainClearance,
                    out bool terrainOccluded) &&
                terrainOccluded)
            {
                previousValid = false;
                return;
            }

            Vector3 viewport3 = viewer.WorldToViewportPoint(center);
            Vector2 viewport = new(viewport3.x, viewport3.y);
            float distance = Vector3.Distance(
                viewer.transform.position,
                center);
            float angularSize = 2f * Mathf.Atan(
                vehicleLength /
                (2f * Mathf.Max(0.001f, distance))) *
                Mathf.Rad2Deg;

            runtimePerceptionVisibleSamples++;
            runtimePerceptionAngularSizePeak = Mathf.Max(
                runtimePerceptionAngularSizePeak,
                angularSize);
            if (runtimePerceptionClosestDistance <= 0f ||
                distance < runtimePerceptionClosestDistance)
            {
                runtimePerceptionClosestDistance = distance;
                runtimePerceptionClosestEyePosition =
                    viewer.transform.InverseTransformPoint(center);
            }

            if (previousValid)
            {
                float screenSpeed = Vector2.Distance(
                    previousViewport,
                    viewport) / deltaTime;
                float lateralSpeed = Mathf.Abs(
                    viewport.x - previousViewport.x) / deltaTime;
                float loomingRate = Mathf.Max(
                    0f,
                    (angularSize - previousAngularSize) / deltaTime);
                if (float.IsFinite(screenSpeed) &&
                    float.IsFinite(lateralSpeed) &&
                    float.IsFinite(loomingRate))
                {
                    runtimeScreenSpeeds.Add(screenSpeed);
                    runtimeLateralSpeeds.Add(lateralSpeed);
                    runtimeLoomingRates.Add(loomingRate);
                    runtimePerceptionScreenSpeedPeak = Mathf.Max(
                        runtimePerceptionScreenSpeedPeak,
                        screenSpeed);
                    runtimePerceptionLateralSpeedPeak = Mathf.Max(
                        runtimePerceptionLateralSpeedPeak,
                        lateralSpeed);
                    runtimePerceptionLoomingRatePeak = Mathf.Max(
                        runtimePerceptionLoomingRatePeak,
                        loomingRate);
                }
            }

            previousValid = true;
            previousViewport = viewport;
            previousAngularSize = angularSize;
        }

        private bool IsRuntimeActionTime(float replayTime)
        {
            for (int i = 0; i < actionBeats.Count; i++)
            {
                ShowcaseActionBeat beat = actionBeats[i];
                if (replayTime >= beat.StartTime &&
                    replayTime <= Mathf.Max(
                        beat.EndTime,
                        beat.ConfirmedTime))
                {
                    return true;
                }
            }

            return false;
        }

        private float GetFinalRuntimeActionTime()
        {
            float finalTime = 0f;
            for (int i = 0; i < actionBeats.Count; i++)
            {
                finalTime = Mathf.Max(
                    finalTime,
                    actionBeats[i].EndTime,
                    actionBeats[i].ConfirmedTime);
            }
            return finalTime;
        }

        private void PublishRuntimePerceptionMetrics(bool complete)
        {
            if (runtimePerceptionPublished ||
                runtimePerceptionSampleCount <= 0 ||
                firstBinding == null ||
                secondBinding == null)
            {
                return;
            }

            runtimePerceptionScreenSpeedP90 =
                ResolvePercentile(runtimeScreenSpeeds, 0.9f);
            runtimePerceptionLateralSpeedP90 =
                ResolvePercentile(runtimeLateralSpeeds, 0.9f);
            runtimePerceptionLoomingRateP90 =
                ResolvePercentile(runtimeLoomingRates, 0.9f);
            runtimePerceptionVisibleFraction =
                runtimePerceptionAttemptedSamples > 0
                    ? runtimePerceptionVisibleSamples /
                      (float)runtimePerceptionAttemptedSamples
                    : 0f;
            runtimePerceptionPublished = true;

            Debug.Log(
                $"[ShowcasePerceptionRuntime] drivers=" +
                $"{firstBinding.DriverNumber}/" +
                $"{secondBinding.DriverNumber}, " +
                $"complete={complete}, " +
                $"samples={runtimePerceptionSampleCount}, " +
                $"visibleSamples={runtimePerceptionVisibleSamples}, " +
                $"visible={runtimePerceptionVisibleFraction:P0}, " +
                $"screenP90={runtimePerceptionScreenSpeedP90:0.###} viewport/s, " +
                $"screenPeak={runtimePerceptionScreenSpeedPeak:0.###} viewport/s, " +
                $"lateralP90={runtimePerceptionLateralSpeedP90:0.###} viewport/s, " +
                $"lateralPeak={runtimePerceptionLateralSpeedPeak:0.###} viewport/s, " +
                $"loomP90={runtimePerceptionLoomingRateP90:0.##} deg/s, " +
                $"loomPeak={runtimePerceptionLoomingRatePeak:0.##} deg/s, " +
                $"angularPeak={runtimePerceptionAngularSizePeak:0.##}deg, " +
                $"closest={runtimePerceptionClosestDistance:0.###}m, " +
                $"closestEye={runtimePerceptionClosestEyePosition:F3}m.",
                this);
        }

        private static float ResolvePercentile(
            List<float> values,
            float percentile)
        {
            if (values == null || values.Count == 0)
                return 0f;

            values.Sort();
            int index = Mathf.Clamp(
                Mathf.CeilToInt(
                    Mathf.Clamp01(percentile) * values.Count) - 1,
                0,
                values.Count - 1);
            return values[index];
        }

        private void ClearRuntimePerceptionPreviousSamples()
        {
            runtimeFirstPreviousValid = false;
            runtimeSecondPreviousValid = false;
        }

        private void ResetRuntimePerceptionMetrics()
        {
            runtimeScreenSpeeds.Clear();
            runtimeLateralSpeeds.Clear();
            runtimeLoomingRates.Clear();
            runtimePerceptionTimelineRevision = -1;
            runtimePerceptionAttemptedSamples = 0;
            runtimePerceptionVisibleSamples = 0;
            runtimePerceptionPublished = false;
            runtimePerceptionSampleCount = 0;
            runtimePerceptionVisibleFraction = 0f;
            runtimePerceptionScreenSpeedP90 = 0f;
            runtimePerceptionScreenSpeedPeak = 0f;
            runtimePerceptionLateralSpeedP90 = 0f;
            runtimePerceptionLateralSpeedPeak = 0f;
            runtimePerceptionLoomingRateP90 = 0f;
            runtimePerceptionLoomingRatePeak = 0f;
            runtimePerceptionAngularSizePeak = 0f;
            runtimePerceptionClosestDistance = 0f;
            runtimePerceptionClosestEyePosition = Vector3.zero;
            runtimeFirstPreviousViewport = Vector2.zero;
            runtimeSecondPreviousViewport = Vector2.zero;
            runtimeFirstPreviousAngularSize = 0f;
            runtimeSecondPreviousAngularSize = 0f;
            ClearRuntimePerceptionPreviousSamples();
        }

        private void TryConfigureNaturalVisibilityEnd(
            ShowcasePlaybackWindow window)
        {
            usesNaturalVisibilityEnd = false;
            naturalVisibilityEndTime = 0f;
            if (eventReplay == null ||
                requiresAdditionalShot ||
                actionBeats.Count == 0 ||
                visibleIntervals.Count == 0)
            {
                return;
            }

            ShowcaseActionBeat finalBeat =
                actionBeats[actionBeats.Count - 1];
            ShowcaseVisibleInterval finalInterval =
                visibleIntervals[visibleIntervals.Count - 1];
            float mandatoryBeatTime = Mathf.Max(
                finalBeat.Time,
                finalBeat.ConfirmedTime);
            if (eventReplay.TryGetShowcaseResultPresentationEndTime(
                    out float resultPresentationEndTime))
            {
                mandatoryBeatTime = Mathf.Max(
                    mandatoryBeatTime,
                    resultPresentationEndTime);
            }
            float sampleTolerance = Mathf.Max(
                0.05f,
                visibilityAnalysisSampleStep);
            if (!finalInterval.IsValid ||
                mandatoryBeatTime <
                    finalInterval.StartTime - sampleTolerance ||
                mandatoryBeatTime >
                    finalInterval.EndTime + sampleTolerance ||
                finalInterval.EndTime >=
                    window.EndTime - sampleTolerance)
            {
                return;
            }

            if (!eventReplay.TrySetShowcasePresentationEndTime(
                    finalInterval.EndTime))
            {
                return;
            }

            usesNaturalVisibilityEnd = true;
            naturalVisibilityEndTime = finalInterval.EndTime;
        }

        private void BuildAdditionalShotRequests()
        {
            additionalShotRequests.Clear();
            if (!requiresAdditionalShot)
                return;

            for (int beatIndex = 0;
                beatIndex < actionBeats.Count;
                beatIndex++)
            {
                if (actionBeatVisibility[beatIndex])
                    continue;

                ShowcaseActionBeat beat = actionBeats[beatIndex];
                float transitionStart = beat.StartTime;
                float transitionEnd = beat.StartTime;
                if (TryFindPrecedingVisibleInterval(
                        beat.StartTime,
                        out ShowcaseVisibleInterval interval))
                {
                    float firstHiddenSample =
                        interval.EndTime +
                        visibilityAnalysisSampleStep;
                    if (beatIndex > 0)
                    {
                        firstHiddenSample = Mathf.Max(
                            firstHiddenSample,
                            actionBeats[beatIndex - 1].EndTime);
                    }
                    if (firstHiddenSample < beat.StartTime)
                    {
                        transitionStart = firstHiddenSample;
                        transitionEnd = beat.StartTime;
                    }
                }

                additionalShotRequests.Add(
                    new ShowcaseAdditionalShotRequest(
                        beatIndex,
                        beat,
                        transitionStart,
                        transitionEnd,
                        TryCreateAdditionalShotPlacement(
                            beat,
                            out ShowcaseShotPlacementCandidate placement)
                                ? placement
                                : default));
            }
        }

        private bool TryCreateAdditionalShotPlacement(
            ShowcaseActionBeat beat,
            out ShowcaseShotPlacementCandidate placement)
        {
            placement = default;
            if (!activeRun.IsValid ||
                eventLocalPath.Count < 2 ||
                eventCoordinateScale <= 0f)
            {
                return false;
            }

            int focusIndex = FindClosestPointIndex(
                eventLocalPath,
                beat.EventLocalPosition);
            if (focusIndex < 0)
                return false;

            Vector3 sourceDirection = Flat(
                FindDirectionAt(eventLocalPath, focusIndex));
            Vector3 heroForward = Flat(
                activeRun.FocusPose.forward);
            if (heroForward.sqrMagnitude <= 0.000001f)
            {
                heroForward = Flat(
                    activeRun.ExitPose.position -
                    activeRun.EntryPose.position);
            }
            if (sourceDirection.sqrMagnitude <= 0.000001f ||
                heroForward.sqrMagnitude <= 0.000001f)
            {
                return false;
            }

            sourceDirection.Normalize();
            heroForward = referencePassTravelDirection.sqrMagnitude >
                0.000001f
                    ? referencePassTravelDirection.normalized
                    : heroForward.normalized;
            Quaternion rotation = Quaternion.Euler(
                0f,
                Vector3.SignedAngle(
                    sourceDirection,
                    heroForward,
                    Vector3.up),
                0f);
            Vector3 heroTarget =
                activeRun.FocusPose.position +
                heroForward * heroForwardOffset;
            Vector3 position =
                heroTarget -
                rotation *
                beat.EventLocalPosition *
                eventCoordinateScale;
            position.y =
                activeRun.FloorHeight +
                roadFloorOffset -
                (rotation *
                 beat.EventLocalPosition *
                 eventCoordinateScale).y;

            placement = new ShowcaseShotPlacementCandidate(
                position,
                rotation,
                eventCoordinateScale,
                beat.EventLocalPosition);
            return placement.IsValid;
        }

        private void InitializeShotPlacementRuntime(
            ShowcaseStagePlacement placement)
        {
            initialShotPlacement = placement.IsValid
                ? new ShowcaseShotPlacementCandidate(
                    placement.Position,
                    placement.Rotation,
                    placement.UniformScale,
                    placement.InteractionFocus)
                : default;
            nextAdditionalShotRequestIndex = 0;
            activeAdditionalShotRequestIndex = -1;
            observedShowcaseTimelineRevision =
                eventReplay != null
                    ? eventReplay.ShowcaseTimelineRevision
                    : -1;
            previousShotReplayTime = eventReplay != null
                ? eventReplay.CurrentTime
                : 0f;
            shotPlacementRuntimeInitialized =
                initialShotPlacement.IsValid;
        }

        private void UpdateAdditionalShotPlacement()
        {
            if (!shotPlacementRuntimeInitialized ||
                eventReplay == null ||
                activePlacementMode !=
                    ShowcaseStagePlacementMode.RoomDioramaRigid)
            {
                return;
            }

            float replayTime = eventReplay.CurrentTime;
            if (observedShowcaseTimelineRevision !=
                eventReplay.ShowcaseTimelineRevision)
            {
                CancelBattleReframe();
                RestoreShotPlacementForTime(replayTime);
                observedShowcaseTimelineRevision =
                    eventReplay.ShowcaseTimelineRevision;
                previousShotReplayTime = replayTime;
                return;
            }
            if (replayTime + 0.0001f < previousShotReplayTime)
            {
                CancelBattleReframe();
                RestoreShotPlacementForTime(replayTime);
                previousShotReplayTime = replayTime;
                return;
            }

            previousShotReplayTime = replayTime;
            if (battleReframeActive)
            {
                UpdateBattleReframe();
                return;
            }
            if (!eventReplay.IsPlaying)
                return;

            while (nextAdditionalShotRequestIndex <
                additionalShotRequests.Count)
            {
                ShowcaseAdditionalShotRequest request =
                    additionalShotRequests[
                        nextAdditionalShotRequestIndex];
                if (!request.HasTransitionWindow ||
                    !request.HasPlacement)
                {
                    lastProcessedAdditionalShotRequestIndex =
                        nextAdditionalShotRequestIndex;
                    nextAdditionalShotRequestIndex++;
                    UpdateFinalShotExitPortalVisibility();
                    continue;
                }

                if (replayTime < request.TransitionStartTime)
                    return;
                if (replayTime > request.TransitionEndTime)
                {
                    lastProcessedAdditionalShotRequestIndex =
                        nextAdditionalShotRequestIndex;
                    nextAdditionalShotRequestIndex++;
                    UpdateFinalShotExitPortalVisibility();
                    continue;
                }

                Camera viewer = Camera.main;
                if (viewer == null ||
                    TryEvaluateBattleVisibility(
                        viewer,
                        replayTime,
                        out _))
                {
                    return;
                }

                ShowcaseShotPlacementCandidate previousPlacement =
                    activeAdditionalShotRequestIndex >= 0
                        ? additionalShotRequests[
                            activeAdditionalShotRequestIndex].Placement
                        : initialShotPlacement;
                BeginBattleReframe(
                    previousPlacement,
                    request.Placement,
                    nextAdditionalShotRequestIndex,
                    viewer);
                return;
            }
        }

        private void BeginBattleReframe(
            ShowcaseShotPlacementCandidate from,
            ShowcaseShotPlacementCandidate to,
            int requestIndex,
            Camera viewer)
        {
            if (!from.IsValid ||
                !to.IsValid ||
                requestIndex < 0 ||
                requestIndex >= additionalShotRequests.Count ||
                viewer == null)
            {
                return;
            }

            battleReframeFrom = from;
            battleReframeTo = to;
            battleReframeRequestIndex = requestIndex;
            battleReframeElapsed = 0f;
            battleReframeActive = true;
            SetBattleReframeVehiclesHidden(true);
            ShowBattleReframeCue(viewer);
        }

        private void UpdateBattleReframe()
        {
            SetBattleReframeVehiclesHidden(true);
            if (!eventReplay.IsPlaying)
                return;

            battleReframeElapsed += Time.unscaledDeltaTime;
            float duration = Mathf.Max(
                0.2f,
                battleReframeDuration);
            float progress = Mathf.Clamp01(
                battleReframeElapsed / duration);
            if (battleReframeRequestIndex >= 0 &&
                battleReframeRequestIndex <
                    additionalShotRequests.Count)
            {
                ShowcaseAdditionalShotRequest request =
                    additionalShotRequests[
                        battleReframeRequestIndex];
                progress = Mathf.Max(
                    progress,
                    Mathf.InverseLerp(
                        request.TransitionStartTime,
                        request.TransitionEndTime,
                        eventReplay.CurrentTime));
            }
            float eased = progress * progress *
                (3f - 2f * progress);
            ShowcaseShotPlacementCandidate placement =
                new(
                    Vector3.Lerp(
                        battleReframeFrom.Position,
                        battleReframeTo.Position,
                        eased),
                    Quaternion.Slerp(
                        battleReframeFrom.Rotation,
                        battleReframeTo.Rotation,
                        eased),
                    Mathf.Lerp(
                        battleReframeFrom.UniformScale,
                        battleReframeTo.UniformScale,
                        eased),
                    Vector3.Lerp(
                        battleReframeFrom.EventLocalFocus,
                        battleReframeTo.EventLocalFocus,
                        eased));
            if (!TryApplyShotPlacement(placement))
            {
                CancelBattleReframe();
                TryApplyShotPlacement(battleReframeFrom);
                return;
            }

            UpdateBattleReframeCue(progress);
            if (progress < 1f)
                return;

            activeAdditionalShotRequestIndex =
                battleReframeRequestIndex;
            lastProcessedAdditionalShotRequestIndex =
                battleReframeRequestIndex;
            nextAdditionalShotRequestIndex =
                battleReframeRequestIndex + 1;
            battleReframeActive = false;
            battleReframeRequestIndex = -1;
            SetBattleReframeVehiclesHidden(false);
            HideBattleReframeCue();
            UpdateFinalShotExitPortalVisibility();
        }

        private void CancelBattleReframe()
        {
            battleReframeActive = false;
            battleReframeElapsed = 0f;
            battleReframeRequestIndex = -1;
            battleReframeFrom = default;
            battleReframeTo = default;
            SetBattleReframeVehiclesHidden(false);
            HideBattleReframeCue();
        }

        private void SetBattleReframeVehiclesHidden(bool hidden)
        {
            ResolveCar(firstBinding)?
                .SetShowcaseTransitionHidden(hidden);
            ResolveCar(secondBinding)?
                .SetShowcaseTransitionHidden(hidden);
        }

        private void ShowBattleReframeCue(Camera viewer)
        {
            EnsureBattleReframeCue(viewer);
            if (battleReframeCueRoot != null)
                battleReframeCueRoot.gameObject.SetActive(true);
            UpdateBattleReframeCue(0f);
        }

        private void EnsureBattleReframeCue(Camera viewer)
        {
            if (battleReframeCueRoot != null &&
                battleReframeCue != null)
            {
                battleReframeCueRoot.SetParent(
                    viewer.transform,
                    false);
                return;
            }

            Shader shader = Shader.Find(
                "Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null)
                return;

            GameObject cueObject = new("BattleReframeCue")
            {
                hideFlags = HideFlags.DontSave
            };
            battleReframeCueRoot = cueObject.transform;
            battleReframeCueRoot.SetParent(
                viewer.transform,
                false);
            battleReframeCueRoot.localPosition =
                new Vector3(0f, 0f, 0.75f);
            battleReframeCueRoot.localRotation =
                Quaternion.identity;
            battleReframeCueMaterial = new Material(shader)
            {
                name = "Battle Reframe Cue Material",
                hideFlags = HideFlags.HideAndDontSave
            };
            battleReframeCue =
                cueObject.AddComponent<LineRenderer>();
            battleReframeCue.useWorldSpace = false;
            battleReframeCue.loop = true;
            battleReframeCue.positionCount = 48;
            battleReframeCue.sharedMaterial =
                battleReframeCueMaterial;
            battleReframeCue.alignment = LineAlignment.View;
            battleReframeCue.numCornerVertices = 3;
            battleReframeCue.numCapVertices = 3;
            battleReframeCue.shadowCastingMode =
                ShadowCastingMode.Off;
            battleReframeCue.receiveShadows = false;
        }

        private void UpdateBattleReframeCue(float progress)
        {
            if (battleReframeCue == null)
                return;

            float envelope = Mathf.Sin(
                Mathf.Clamp01(progress) * Mathf.PI);
            float radius = Mathf.Lerp(
                0.12f,
                0.42f,
                Mathf.SmoothStep(0f, 1f, progress));
            float alpha = envelope * 0.82f;
            Color start = battleReframeStartColor;
            Color end = battleReframeEndColor;
            start.a *= alpha;
            end.a *= alpha;
            battleReframeCue.startColor = start;
            battleReframeCue.endColor = end;
            battleReframeCue.widthMultiplier =
                Mathf.Lerp(0.012f, 0.003f, progress);
            for (int i = 0;
                i < battleReframeCue.positionCount;
                i++)
            {
                float angle =
                    i /
                    (float)battleReframeCue.positionCount *
                    Mathf.PI * 2f;
                battleReframeCue.SetPosition(
                    i,
                    new Vector3(
                        Mathf.Cos(angle) * radius,
                        Mathf.Sin(angle) * radius,
                        0f));
            }
        }

        private void HideBattleReframeCue()
        {
            if (battleReframeCueRoot != null)
                battleReframeCueRoot.gameObject.SetActive(false);
        }

        private void DisposeBattleReframeCue()
        {
            if (battleReframeCueRoot != null)
                Destroy(battleReframeCueRoot.gameObject);
            if (battleReframeCueMaterial != null)
                Destroy(battleReframeCueMaterial);

            battleReframeCueRoot = null;
            battleReframeCue = null;
            battleReframeCueMaterial = null;
        }

        private void RestoreShotPlacementForTime(float replayTime)
        {
            ShowcaseShotPlacementCandidate placement =
                initialShotPlacement;
            int resolvedRequestIndex = -1;
            int resolvedNextIndex = 0;
            for (int i = 0;
                i < additionalShotRequests.Count;
                i++)
            {
                ShowcaseAdditionalShotRequest request =
                    additionalShotRequests[i];
                if (!request.HasTransitionWindow ||
                    !request.HasPlacement)
                {
                    resolvedNextIndex = i + 1;
                    continue;
                }
                if (replayTime < request.TransitionStartTime)
                    break;

                placement = request.Placement;
                resolvedRequestIndex = i;
                resolvedNextIndex = i + 1;
            }

            if (resolvedRequestIndex !=
                    activeAdditionalShotRequestIndex &&
                TryApplyShotPlacement(placement))
            {
                activeAdditionalShotRequestIndex =
                    resolvedRequestIndex;
            }
            nextAdditionalShotRequestIndex = resolvedNextIndex;
            lastProcessedAdditionalShotRequestIndex =
                resolvedNextIndex - 1;
            UpdateFinalShotExitPortalVisibility();
        }

        private void InitializeFinalShotExitPortalVisibility()
        {
            finalAdditionalShotRequestIndex = -1;
            for (int i = 0;
                i < additionalShotRequests.Count;
                i++)
            {
                ShowcaseAdditionalShotRequest request =
                    additionalShotRequests[i];
                if (request.HasTransitionWindow &&
                    request.HasPlacement)
                {
                    finalAdditionalShotRequestIndex = i;
                }
            }
            lastProcessedAdditionalShotRequestIndex = -1;
            deferExitPortalUntilFinalShot =
                portalPresentation != null &&
                portalPresentation.IsConfigured &&
                activePlacementMode ==
                    ShowcaseStagePlacementMode.RoomDioramaRigid &&
                finalAdditionalShotRequestIndex >= 0;
            UpdateFinalShotExitPortalVisibility();
        }

        private void UpdateFinalShotExitPortalVisibility()
        {
            if (portalPresentation == null ||
                !portalPresentation.IsConfigured)
            {
                return;
            }

            bool finalShot =
                !deferExitPortalUntilFinalShot ||
                !battleReframeActive &&
                lastProcessedAdditionalShotRequestIndex >=
                    finalAdditionalShotRequestIndex;
            portalPresentation.SetExitPortalVisible(
                finalShot && !usesNaturalVisibilityEnd);
        }

        private bool TryApplyShotPlacement(
            ShowcaseShotPlacementCandidate placement)
        {
            return eventReplay != null &&
                placement.IsValid &&
                eventReplay.TryApplyRoomStagePlacement(
                    placement.Position,
                    placement.Rotation,
                    placement.UniformScale,
                    placement.EventLocalFocus);
        }

        private bool TryFindPrecedingVisibleInterval(
            float replayTime,
            out ShowcaseVisibleInterval result)
        {
            result = default;
            bool found = false;
            for (int i = 0; i < visibleIntervals.Count; i++)
            {
                ShowcaseVisibleInterval interval =
                    visibleIntervals[i];
                if (!interval.IsValid ||
                    interval.EndTime >= replayTime)
                {
                    continue;
                }

                if (!found ||
                    interval.EndTime > result.EndTime)
                {
                    result = interval;
                    found = true;
                }
            }

            return found;
        }

        private bool TryEvaluateActionBeatVisibility(
            Camera viewer,
            ShowcaseActionBeat beat,
            out VisibilityFailure failure)
        {
            failure = VisibilityFailure.None;
            float duration = Mathf.Max(
                0f,
                beat.EndTime - beat.StartTime);
            int sampleCount = Mathf.Max(
                1,
                Mathf.CeilToInt(
                    duration /
                    Mathf.Max(0.05f, visibilitySampleSeconds)));
            for (int i = 0; i <= sampleCount; i++)
            {
                float replayTime = Mathf.Lerp(
                    beat.StartTime,
                    beat.EndTime,
                    i / (float)sampleCount);
                if (TryEvaluateBattleVisibility(
                        viewer,
                        replayTime,
                        out VisibilityFailure sampleFailure))
                {
                    failure = VisibilityFailure.None;
                    return true;
                }

                failure |= sampleFailure;
            }

            return false;
        }

        private bool TryEvaluateBattleVisibility(
            Camera viewer,
            float replayTime,
            out VisibilityFailure failure)
        {
            failure = VisibilityFailure.None;
            if (!eventReplay.TryGetEventLocalVehiclePosition(
                    firstBinding.DriverNumber,
                    replayTime,
                    out Vector3 firstLocal) ||
                !eventReplay.TryGetEventLocalVehiclePosition(
                    secondBinding.DriverNumber,
                    replayTime,
                    out Vector3 secondLocal))
            {
                failure = VisibilityFailure.MissingPosition;
                return false;
            }

            float targetClearance =
                appliedRoomVehicleLength *
                visibilityTerrainClearanceInVehicleLengths;
            Vector3 up = boundStage.up;
            Vector3 firstWorld =
                boundStage.TransformPoint(firstLocal);
            Vector3 secondWorld =
                boundStage.TransformPoint(secondLocal);
            VisibilityFailure firstFailure =
                EvaluateVehicleVisibility(
                    viewer,
                    firstWorld,
                    up,
                    firstVisibilityVehicleHeight,
                    targetClearance);
            VisibilityFailure secondFailure =
                EvaluateVehicleVisibility(
                    viewer,
                    secondWorld,
                    up,
                    secondVisibilityVehicleHeight,
                    targetClearance);
            failure = firstFailure | secondFailure;
            return failure == VisibilityFailure.None;
        }

        private VisibilityFailure EvaluateVehicleVisibility(
            Camera viewer,
            Vector3 basePosition,
            Vector3 up,
            float vehicleHeight,
            float targetClearance)
        {
            Vector3 eye = viewer.transform.position;
            float fallbackProbeHeight =
                appliedRoomVehicleLength *
                visibilityProbeHeightInVehicleLengths;
            float centerHeight = vehicleHeight > 0.0001f
                ? vehicleHeight *
                  visibilityProbeCenterInVehicleHeight
                : fallbackProbeHeight;
            float probeSpan = vehicleHeight > 0.0001f
                ? vehicleHeight *
                  visibilityProbeSpanInVehicleHeight
                : 0f;
            Vector3 vehicleCenter =
                basePosition + up * centerHeight;
            float horizontalRadius =
                appliedRoomVehicleLength * 0.5f;
            float verticalRadius = vehicleHeight > 0.0001f
                ? vehicleHeight * 0.5f
                : probeSpan;
            float boundsRadius = Mathf.Sqrt(
                horizontalRadius * horizontalRadius +
                verticalRadius * verticalRadius);
            if (Vector3.Distance(eye, vehicleCenter) - boundsRadius >
                visibilityMaximumDistance)
            {
                return VisibilityFailure.Distance;
            }

            if (!VehicleBoundsIntersectViewer(
                    viewer,
                    vehicleCenter,
                    horizontalRadius,
                    verticalRadius))
            {
                return VisibilityFailure.View;
            }

            float minimumHeight = Mathf.Max(
                0f,
                centerHeight - probeSpan);
            float maximumHeight = vehicleHeight > 0.0001f
                ? Mathf.Min(
                    vehicleHeight,
                    centerHeight + probeSpan)
                : centerHeight;
            for (int i = 0; i < 3; i++)
            {
                float probeHeight = i == 0
                    ? minimumHeight
                    : i == 1
                        ? centerHeight
                        : maximumHeight;
                Vector3 target =
                    basePosition + up * probeHeight;
                bool hasTerrain =
                    eventReplay.TryGetShowcaseTerrainOcclusion(
                        eye,
                        target,
                        targetClearance,
                        out bool occluded);
                terrainOcclusionAvailable |= hasTerrain;
                if (!hasTerrain || !occluded)
                    return VisibilityFailure.None;
            }

            return VisibilityFailure.Terrain;
        }

        private bool VehicleBoundsIntersectViewer(
            Camera viewer,
            Vector3 center,
            float horizontalRadius,
            float verticalRadius)
        {
            Vector3 viewport = viewer.WorldToViewportPoint(center);
            if (viewport.z <= viewer.nearClipPlane)
                return false;

            Vector3 horizontalEdge = viewer.WorldToViewportPoint(
                center +
                viewer.transform.right * horizontalRadius);
            Vector3 verticalEdge = viewer.WorldToViewportPoint(
                center +
                viewer.transform.up * verticalRadius);
            float viewportRadiusX = Mathf.Abs(
                horizontalEdge.x - viewport.x);
            float viewportRadiusY = Mathf.Abs(
                verticalEdge.y - viewport.y);
            float margin = visibilityViewportMargin;
            return viewport.x + viewportRadiusX >= margin &&
                viewport.x - viewportRadiusX <= 1f - margin &&
                viewport.y + viewportRadiusY >= margin &&
                viewport.y - viewportRadiusY <= 1f - margin;
        }

        private void AccumulateActionBeatFailure(
            VisibilityFailure failure)
        {
            if ((failure & VisibilityFailure.MissingPosition) != 0)
                actionBeatsWithMissingPosition++;
            if ((failure & VisibilityFailure.Distance) != 0)
                actionBeatsOutsideDistance++;
            if ((failure & VisibilityFailure.View) != 0)
                actionBeatsOutsideView++;
            if ((failure & VisibilityFailure.Terrain) != 0)
                actionBeatsTerrainOccluded++;
        }

        private void ResetShotVisibilityAnalysis()
        {
            actionBeats.Clear();
            actionBeatVisibility.Clear();
            visibleIntervals.Clear();
            additionalShotRequests.Clear();
            actionBeatCount = 0;
            visibleActionBeatCount = 0;
            continuousVisibleIntervalCount = 0;
            terrainOcclusionAvailable = false;
            allActionBeatsVisible = false;
            requiresAdditionalShot = false;
            actionBeatsOutsideDistance = 0;
            actionBeatsOutsideView = 0;
            actionBeatsTerrainOccluded = 0;
            actionBeatsWithMissingPosition = 0;
            perceptionEyeRelativeTrackFocus = Vector3.zero;
            perceptionClosestVehicleDistance = 0f;
            perceptionVehicleAngularSize = 0f;
            perceptionScreenTravelPerSecond = 0f;
            perceptionBattleVisibleFraction = 0f;
            ResetRuntimePerceptionMetrics();
            firstVisibilityVehicleHeight = 0f;
            secondVisibilityVehicleHeight = 0f;
            visibilityAnalysisSampleStep = 0f;
            initialShotPlacement = default;
            nextAdditionalShotRequestIndex = 0;
            activeAdditionalShotRequestIndex = -1;
            observedShowcaseTimelineRevision = -1;
            previousShotReplayTime = 0f;
            shotPlacementRuntimeInitialized = false;
            battleReframeActive = false;
            battleReframeElapsed = 0f;
            battleReframeRequestIndex = -1;
            battleReframeFrom = default;
            battleReframeTo = default;
            HideBattleReframeCue();
            deferExitPortalUntilFinalShot = false;
            finalAdditionalShotRequestIndex = -1;
            lastProcessedAdditionalShotRequestIndex = -1;
            usesNaturalVisibilityEnd = false;
            naturalVisibilityEndTime = 0f;
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

        private void EvaluateWallPortalCandidates(
            IReadOnlyList<Vector3> sourcePath,
            Vector3 sourceEntryPosition,
            Vector3 sourceFocusPosition,
            Vector3 sourceExitPosition,
            ShowcaseRun run,
            ShowcaseStagePlacement baselinePlacement)
        {
            ResetWallPortalEvaluation();
            if (!evaluateWallPortalCandidates ||
                showcaseLayout == null ||
                sourcePath == null ||
                sourcePath.Count < 2 ||
                !run.IsValid ||
                !baselinePlacement.IsValid)
            {
                return;
            }

            evaluatedWallCount =
                showcaseLayout.CopyAvailableWallFrames(
                    wallPortalEvaluationFrames);
            if (evaluatedWallCount == 0)
            {
                Debug.Log(
                    "[WallPortalEvaluation] walls=0, " +
                    "recommendation=FreeTrackExit, reason=NoAvailableWalls",
                    this);
                return;
            }

            Camera viewer = Camera.main;
            float baselineScale = baselinePlacement.UniformScale;
            int sizeRejectedWalls = 0;
            int pairPlacementRejected = 0;
            int pairCompatibilityRejected = 0;
            int exitPlacementRejected = 0;
            int exitCompatibilityRejected = 0;
            float bestQualifiedPairScore = -1f;
            float bestQualifiedExitScore = -1f;
            string bestPairLabel = "None";
            string bestExitLabel = "None";

            float[] cumulativeDistances =
                BuildCumulativeDistances(sourcePath);
            int exitIndex = FindClosestPointIndex(
                sourcePath,
                sourceExitPosition);
            float sourceExitContinuation =
                exitIndex >= 0 &&
                exitIndex < cumulativeDistances.Length
                    ? cumulativeDistances[
                          cumulativeDistances.Length - 1] -
                      cumulativeDistances[exitIndex]
                    : 0f;

            for (int exitWallIndex = 0;
                 exitWallIndex < wallPortalEvaluationFrames.Count;
                 exitWallIndex++)
            {
                ShowcaseWallFrame exitWall =
                    wallPortalEvaluationFrames[exitWallIndex];
                if (!WallFitsEvaluatedPortal(exitWall))
                {
                    sizeRejectedWalls++;
                    continue;
                }

                if (TryScoreWallExitCandidate(
                        sourcePath,
                        sourceFocusPosition,
                        sourceExitPosition,
                        sourceExitContinuation,
                        run,
                        exitWall,
                        baselinePlacement,
                        viewer,
                        out float exitScore,
                        out bool exitQualified,
                        out ShowcaseStagePlacement exitPlacement))
                {
                    evaluatedWallExitCount++;
                    bestWallExitScore = Mathf.Max(
                        bestWallExitScore,
                        exitScore);
                    if (exitQualified)
                    {
                        qualifiedWallExitCount++;
                        if (exitScore > bestQualifiedExitScore)
                        {
                            bestQualifiedExitScore = exitScore;
                            bestExitLabel =
                                $"W{exitWallIndex + 1}";
                            recommendedWallExitPlacement =
                                exitPlacement;
                            recommendedWallExitFrame = exitWall;
                            hasRecommendedWallExitPlacement = true;
                        }
                    }
                    else
                    {
                        exitCompatibilityRejected++;
                    }
                }
                else
                {
                    exitPlacementRejected++;
                }

                for (int entryWallIndex = 0;
                     entryWallIndex < wallPortalEvaluationFrames.Count;
                     entryWallIndex++)
                {
                    if (entryWallIndex == exitWallIndex)
                        continue;

                    ShowcaseWallFrame entryWall =
                        wallPortalEvaluationFrames[entryWallIndex];
                    if (!WallFitsEvaluatedPortal(entryWall))
                        continue;

                    if (!TryCreateWallPairRun(
                            run,
                            entryWall,
                            exitWall,
                            out ShowcaseRun candidateRun) ||
                        !TryCreateEventStagePlacement(
                            sourcePath,
                            sourceEntryPosition,
                            sourceFocusPosition,
                            sourceExitPosition,
                            candidateRun,
                            out ShowcaseStagePlacement candidatePlacement,
                            out _))
                    {
                        pairPlacementRejected++;
                        continue;
                    }

                    evaluatedWallPairCount++;
                    float pairScore = ScoreWallPairCandidate(
                        candidatePlacement,
                        sourceFocusPosition,
                        baselineScale,
                        viewer);
                    bestWallPairScore = Mathf.Max(
                        bestWallPairScore,
                        pairScore);
                    bool pairQualified =
                        candidatePlacement.WallPairCompatible &&
                        IsPlacementFocusUsable(
                            candidatePlacement,
                            sourceFocusPosition,
                            viewer);
                    if (!pairQualified)
                    {
                        pairCompatibilityRejected++;
                        continue;
                    }

                    qualifiedWallPairCount++;
                    if (pairScore > bestQualifiedPairScore)
                    {
                        bestQualifiedPairScore = pairScore;
                        bestPairLabel =
                            $"W{entryWallIndex + 1}->" +
                            $"W{exitWallIndex + 1}";
                    }
                }
            }

            if (bestQualifiedPairScore >=
                wallPairRecommendationScore)
            {
                wallPortalRecommendation =
                    WallPortalRecommendationMode.WallPair;
            }
            else if (bestQualifiedExitScore >=
                     wallExitRecommendationScore &&
                     hasRecommendedWallExitPlacement)
            {
                wallPortalRecommendation =
                    WallPortalRecommendationMode.ExitWall;
            }

            Debug.Log(
                $"[WallPortalEvaluation] walls={evaluatedWallCount}, " +
                $"wallSizeRejected={sizeRejectedWalls}, " +
                $"pairs={qualifiedWallPairCount}/" +
                $"{evaluatedWallPairCount}, " +
                $"bestPair={bestPairLabel}:" +
                $"{Mathf.Max(0f, bestQualifiedPairScore):0.#}, " +
                $"pairRejected={pairPlacementRejected}/" +
                $"{pairCompatibilityRejected} placement/quality, " +
                $"exits={qualifiedWallExitCount}/" +
                $"{evaluatedWallExitCount}, " +
                $"bestExit={bestExitLabel}:" +
                $"{Mathf.Max(0f, bestQualifiedExitScore):0.#}, " +
                $"exitRejected={exitPlacementRejected}/" +
                $"{exitCompatibilityRejected} placement/quality, " +
                $"recommendation={wallPortalRecommendation}",
                this);
        }

        private bool WallFitsEvaluatedPortal(
            ShowcaseWallFrame wall)
        {
            return wall.IsValid &&
                wall.Width + 0.0001f >=
                minimumEvaluatedPortalWallWidth &&
                wall.Height + 0.0001f >=
                minimumEvaluatedPortalWallHeight;
        }

        private static bool TryCreateWallPairRun(
            ShowcaseRun source,
            ShowcaseWallFrame entryWall,
            ShowcaseWallFrame exitWall,
            out ShowcaseRun candidate)
        {
            candidate = default;
            Vector3 entryTravel = Flat(entryWall.InwardNormal);
            Vector3 exitTravel = Flat(-exitWall.InwardNormal);
            Vector3 roomSpan = Flat(
                exitWall.Center - entryWall.Center);
            if (entryTravel.sqrMagnitude <= 0.000001f ||
                exitTravel.sqrMagnitude <= 0.000001f ||
                roomSpan.sqrMagnitude <= 0.25f)
            {
                return false;
            }

            entryTravel.Normalize();
            exitTravel.Normalize();
            candidate = new ShowcaseRun(
                source.Timing,
                source.Route,
                new Pose(
                    entryWall.Center,
                    Quaternion.LookRotation(
                        entryTravel,
                        Vector3.up)),
                source.FocusPose,
                new Pose(
                    exitWall.Center,
                    Quaternion.LookRotation(
                        exitTravel,
                        Vector3.up)),
                entryTravel,
                exitTravel,
                source.FloorHeight,
                source.LayoutRevision,
                source.SourceRevision);
            return candidate.IsValid;
        }

        private float ScoreWallPairCandidate(
            ShowcaseStagePlacement placement,
            Vector3 sourceFocusPosition,
            float baselineScale,
            Camera viewer)
        {
            float angleScore = 1f - Mathf.Clamp01(
                (placement.EntryWallAngle +
                 placement.ExitWallAngle) /
                180f);
            float heroScore = 1f - Mathf.Clamp01(
                placement.HeroMissDistance /
                Mathf.Max(0.1f, placement.HeroMissLimit));
            float continuationScore =
                (ScoreRequirement(
                     placement.EntryContinuation,
                     placement.EntryContinuationTarget) +
                 ScoreRequirement(
                     placement.ExitContinuation,
                     placement.ExitContinuationTarget)) *
                0.5f;
            float scaleScore = ScoreScaleSimilarity(
                placement.UniformScale,
                baselineScale);
            Vector3 mappedFocus =
                placement.Position +
                placement.Rotation *
                sourceFocusPosition *
                placement.UniformScale;
            float viewScore = ScoreViewerPoint(viewer, mappedFocus);
            return 100f *
                (angleScore * 0.3f +
                 heroScore * 0.25f +
                 continuationScore * 0.15f +
                 scaleScore * 0.15f +
                 viewScore * 0.15f);
        }

        private bool TryScoreWallExitCandidate(
            IReadOnlyList<Vector3> sourcePath,
            Vector3 sourceFocusPosition,
            Vector3 sourceExitPosition,
            float sourceExitContinuation,
            ShowcaseRun run,
            ShowcaseWallFrame exitWall,
            ShowcaseStagePlacement baselinePlacement,
            Camera viewer,
            out float score,
            out bool qualified,
            out ShowcaseStagePlacement placement)
        {
            score = 0f;
            qualified = false;
            placement = default;
            int exitIndex = FindClosestPointIndex(
                sourcePath,
                sourceExitPosition);
            if (exitIndex < 0)
                return false;

            Vector3 heroForward = Flat(run.FocusPose.forward);
            if (heroForward.sqrMagnitude <= 0.000001f)
                return false;

            heroForward.Normalize();
            Vector3 heroTarget =
                run.FocusPose.position +
                heroForward * heroForwardOffset;
            Vector3 sourceExitDirection =
                FindDirectionAt(sourcePath, exitIndex);
            Vector3 desiredExitDirection = Flat(
                -exitWall.InwardNormal);
            sourceExitDirection = Flat(sourceExitDirection);
            if (sourceExitDirection.sqrMagnitude <= 0.000001f ||
                desiredExitDirection.sqrMagnitude <= 0.000001f)
            {
                return false;
            }

            sourceExitDirection.Normalize();
            desiredExitDirection.Normalize();
            float yaw = Vector3.SignedAngle(
                sourceExitDirection,
                desiredExitDirection,
                Vector3.up);
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
            float resolvedScale = baselinePlacement.UniformScale;
            if (!float.IsFinite(resolvedScale) ||
                resolvedScale <= 0f)
            {
                return false;
            }

            Vector3 rotatedExit =
                rotation * sourceExitPosition * resolvedScale;
            Vector3 position = exitWall.Center - rotatedExit;
            position.y =
                run.FloorHeight +
                roadFloorOffset -
                rotatedExit.y;
            Vector3 mappedExit =
                position + rotatedExit;
            Vector3 mappedFocus =
                position +
                rotation * sourceFocusPosition * resolvedScale;
            float focusMiss = Flat(
                mappedFocus - heroTarget).magnitude;
            float focusScore = 1f - Mathf.Clamp01(
                focusMiss / 3f);
            float focusViewScore = ScoreViewerPoint(
                viewer,
                mappedFocus);
            float wallViewScore = ScoreViewerPoint(
                viewer,
                mappedExit);
            float continuationScore = ScoreRequirement(
                sourceExitContinuation * resolvedScale,
                wallContinuationTarget *
                Mathf.Max(1f, showcaseTrackScaleMultiplier));
            float distanceScore = 1f - Mathf.Clamp01(
                Mathf.Abs(
                    Flat(mappedExit - heroTarget).magnitude - 3f) /
                5f);
            score = 100f *
                (focusScore * 0.35f +
                 focusViewScore * 0.25f +
                 wallViewScore * 0.15f +
                 continuationScore * 0.15f +
                 distanceScore * 0.1f);
            qualified =
                focusMiss <= 2.25f &&
                focusViewScore >= 0.35f &&
                wallViewScore >= 0.2f &&
                continuationScore >= 0.1f;
            placement = new ShowcaseStagePlacement(
                ShowcaseStagePlacementMode.RoomDioramaRigid,
                position,
                rotation,
                resolvedScale,
                sourceFocusPosition,
                baselinePlacement.EntryContinuation,
                baselinePlacement.ExitContinuation,
                baselinePlacement.EntryContinuationTarget,
                baselinePlacement.ExitContinuationTarget,
                focusMiss,
                2.25f,
                baselinePlacement.EntryWallAngle,
                0f,
                Flat(mappedExit - exitWall.Center).magnitude,
                false);
            if (!placement.IsValid)
            {
                placement = default;
                qualified = false;
                return false;
            }

            return true;
        }

        private static bool TryCreateWallExitPortalPose(
            Transform stage,
            Vector3 sourceExitPosition,
            ShowcaseWallFrame exitWall,
            out Pose pose)
        {
            pose = default;
            if (stage == null || !exitWall.IsValid)
                return false;

            Vector3 travelDirection = Flat(
                -exitWall.InwardNormal);
            if (travelDirection.sqrMagnitude <= 0.000001f)
                return false;

            travelDirection.Normalize();
            Vector3 worldPosition =
                stage.TransformPoint(sourceExitPosition);
            if (!float.IsFinite(worldPosition.x) ||
                !float.IsFinite(worldPosition.y) ||
                !float.IsFinite(worldPosition.z))
            {
                return false;
            }

            pose = new Pose(
                worldPosition,
                Quaternion.LookRotation(
                    travelDirection,
                    Vector3.up));
            return true;
        }

        private static bool IsPlacementFocusUsable(
            ShowcaseStagePlacement placement,
            Vector3 sourceFocusPosition,
            Camera viewer)
        {
            Vector3 mappedFocus =
                placement.Position +
                placement.Rotation *
                sourceFocusPosition *
                placement.UniformScale;
            return ScoreViewerPoint(viewer, mappedFocus) >= 0.2f;
        }

        private static float ScoreViewerPoint(
            Camera viewer,
            Vector3 worldPoint)
        {
            if (viewer == null)
                return 0.5f;

            Vector3 eyeRelative =
                viewer.transform.InverseTransformPoint(worldPoint);
            float horizontalAngle = Mathf.Abs(
                Mathf.Atan2(
                    eyeRelative.x,
                    eyeRelative.z) *
                Mathf.Rad2Deg);
            float angleScore = 1f - Mathf.InverseLerp(
                35f,
                135f,
                horizontalAngle);
            float distance = Flat(
                worldPoint -
                viewer.transform.position).magnitude;
            float distanceScore = 1f - Mathf.Clamp01(
                Mathf.Abs(distance - 2.5f) / 6f);
            return Mathf.Clamp01(
                angleScore * 0.75f +
                distanceScore * 0.25f);
        }

        private static float ScoreRequirement(
            float actual,
            float required)
        {
            return required <= 0.0001f
                ? 1f
                : Mathf.Clamp01(actual / required);
        }

        private static float ScoreScaleSimilarity(
            float candidateScale,
            float baselineScale)
        {
            if (candidateScale <= 0f || baselineScale <= 0f)
                return 0f;

            float ratio = candidateScale / baselineScale;
            return Mathf.Exp(-Mathf.Abs(Mathf.Log(ratio)));
        }

        private void ResetWallPortalEvaluation()
        {
            wallPortalEvaluationFrames.Clear();
            evaluatedWallCount = 0;
            evaluatedWallPairCount = 0;
            qualifiedWallPairCount = 0;
            bestWallPairScore = 0f;
            evaluatedWallExitCount = 0;
            qualifiedWallExitCount = 0;
            bestWallExitScore = 0f;
            hasRecommendedWallExitPlacement = false;
            recommendedWallExitPlacement = default;
            recommendedWallExitFrame = default;
            wallPortalRecommendation =
                WallPortalRecommendationMode.FreeTrackExit;
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

            Vector3 sourceFocusDirection = Flat(
                FindDirectionAt(sourcePath, focusIndex));
            if (sourceFocusDirection.sqrMagnitude <= 0.000001f)
            {
                failure =
                    "The source track has no stable focus direction.";
                return false;
            }

            sourceFocusDirection.Normalize();
            float scale = portalAlignedPlacement.UniformScale;
            Vector3 compositionAnchor = sourceFocusPosition;
            Vector3 compositionDirection =
                sourceFocusDirection;
            bool usesMultiExchangeFraming = false;
            if (compositionMode ==
                    RoomDioramaCompositionMode.Balanced &&
                eventReplay != null &&
                eventReplay.TryGetShowcaseExchangeSpan(
                    out Vector3 firstExchangePosition,
                    out Vector3 lastExchangePosition))
            {
                Vector3 exchangeCenter =
                    (firstExchangePosition +
                     lastExchangePosition) * 0.5f;
                int exchangeCenterIndex = FindClosestPointIndex(
                    sourcePath,
                    exchangeCenter);
                if (exchangeCenterIndex >= 0)
                {
                    Vector3 exchangeDirection = Flat(
                        FindDirectionAt(
                            sourcePath,
                            exchangeCenterIndex));
                    if (exchangeDirection.sqrMagnitude > 0.000001f)
                    {
                        compositionAnchor = exchangeCenter;
                        compositionDirection =
                            exchangeDirection.normalized;
                        usesMultiExchangeFraming = true;
                    }
                }
            }

            if (compositionMode ==
                    RoomDioramaCompositionMode
                        .TracksideImmersiveExperimental &&
                TryFindCriticalPathCenter(
                    sourcePath,
                    entryIndex,
                    focusIndex,
                    exitIndex,
                    out Vector3 criticalCenter,
                    out Vector3 criticalDirection))
            {
                criticalDirection = Flat(criticalDirection);
                if (criticalDirection.sqrMagnitude > 0.000001f)
                {
                    criticalDirection.Normalize();
                    float bias = Mathf.Clamp01(
                        criticalSegmentCenterBias);
                    compositionAnchor = Vector3.Lerp(
                        sourceFocusPosition,
                        criticalCenter,
                        bias);
                    compositionDirection = Vector3.Slerp(
                        sourceFocusDirection,
                        criticalDirection,
                        bias);
                }
            }

            if (compositionMode ==
                RoomDioramaCompositionMode.Balanced)
            {
                float physicalShift = Flat(
                    compositionAnchor -
                    sourceFocusPosition).magnitude * scale;
                float maximumShift = Mathf.Max(
                    0f,
                    usesMultiExchangeFraming
                        ? multiExchangeMaximumShift
                        : balancedMaximumShift);
                if (physicalShift > maximumShift &&
                    physicalShift > 0.000001f)
                {
                    float limitRatio = Mathf.Clamp01(
                        maximumShift / physicalShift);
                    compositionAnchor = Vector3.Lerp(
                        sourceFocusPosition,
                        compositionAnchor,
                        limitRatio);
                    compositionDirection = Vector3.Slerp(
                        sourceFocusDirection,
                        compositionDirection,
                        limitRatio);
                }
            }

            Vector3 heroForward = Flat(run.FocusPose.forward);
            if (heroForward.sqrMagnitude <= 0.000001f)
            {
                heroForward = Flat(
                    run.ExitPose.position -
                    run.EntryPose.position);
            }

            compositionDirection = Flat(compositionDirection);
            if (compositionDirection.sqrMagnitude <= 0.000001f ||
                heroForward.sqrMagnitude <= 0.000001f)
            {
                failure =
                    "The source track or Hero has no stable travel direction.";
                return false;
            }

            compositionDirection.Normalize();
            heroForward.Normalize();
            heroForward = ResolveReferencePassTravelDirection(
                heroForward,
                usesMultiExchangeFraming);
            float yaw = Vector3.SignedAngle(
                compositionDirection,
                heroForward,
                Vector3.up);
            Quaternion rotation =
                Quaternion.Euler(0f, yaw, 0f);
            Vector3 overtakeTarget =
                run.FocusPose.position +
                heroForward * heroForwardOffset;
            Vector3 position =
                overtakeTarget -
                rotation * compositionAnchor * scale;
            position.y =
                run.FloorHeight +
                roadFloorOffset -
                (rotation * compositionAnchor * scale).y;

            Vector3 mappedEntry =
                position +
                rotation * sourceEntryPosition * scale;
            Vector3 mappedExit =
                position +
                rotation * sourceExitPosition * scale;
            Vector3 mappedFocus =
                position +
                rotation * sourceFocusPosition * scale;
            Vector3 mappedCompositionAnchor =
                position +
                rotation * compositionAnchor * scale;
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
                mappedCompositionAnchor -
                overtakeTarget).magnitude;
            compositionShiftDistance = Flat(
                mappedFocus -
                mappedCompositionAnchor).magnitude;

            placement = new ShowcaseStagePlacement(
                ShowcaseStagePlacementMode.RoomDioramaRigid,
                position,
                rotation,
                scale,
                compositionAnchor,
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

        private static bool TryFindCriticalPathCenter(
            IReadOnlyList<Vector3> sourcePath,
            int entryIndex,
            int focusIndex,
            int exitIndex,
            out Vector3 center,
            out Vector3 direction)
        {
            center = Vector3.zero;
            direction = Vector3.forward;
            if (sourcePath == null ||
                entryIndex < 0 ||
                focusIndex < entryIndex ||
                exitIndex < focusIndex ||
                exitIndex >= sourcePath.Count)
            {
                return false;
            }

            float totalLength = 0f;
            for (int i = entryIndex + 1;
                 i <= exitIndex;
                 i++)
            {
                totalLength += Vector3.Distance(
                    sourcePath[i - 1],
                    sourcePath[i]);
            }

            if (totalLength <= 0.000001f)
                return false;

            float targetLength = totalLength * 0.5f;
            float accumulated = 0f;
            for (int i = entryIndex + 1;
                 i <= exitIndex;
                 i++)
            {
                Vector3 start = sourcePath[i - 1];
                Vector3 end = sourcePath[i];
                float segmentLength = Vector3.Distance(
                    start,
                    end);
                if (segmentLength <= 0.000001f)
                    continue;

                if (accumulated + segmentLength >=
                    targetLength)
                {
                    float progress = Mathf.Clamp01(
                        (targetLength - accumulated) /
                        segmentLength);
                    center = Vector3.Lerp(
                        start,
                        end,
                        progress);
                    direction = end - start;
                    return direction.sqrMagnitude >
                        0.000001f;
                }

                accumulated += segmentLength;
            }

            center = sourcePath[exitIndex];
            direction = FindDirectionAt(
                sourcePath,
                exitIndex);
            return direction.sqrMagnitude > 0.000001f;
        }

        private static bool TryCreateTrackExitPortalPose(
            Transform stage,
            IReadOnlyList<Vector3> sourcePath,
            Vector3 sourceExitPosition,
            out Pose pose)
        {
            pose = default;
            if (stage == null ||
                sourcePath == null ||
                sourcePath.Count < 2)
            {
                return false;
            }

            int exitIndex = FindClosestPointIndex(
                sourcePath,
                sourceExitPosition);
            if (exitIndex < 0)
                return false;

            Vector3 sourceDirection =
                FindDirectionAt(sourcePath, exitIndex);
            Vector3 worldDirection = Flat(
                stage.TransformDirection(sourceDirection));
            if (worldDirection.sqrMagnitude <= 0.000001f)
                return false;

            worldDirection.Normalize();
            Vector3 worldPosition =
                stage.TransformPoint(sourceExitPosition);
            if (!float.IsFinite(worldPosition.x) ||
                !float.IsFinite(worldPosition.y) ||
                !float.IsFinite(worldPosition.z))
            {
                return false;
            }

            pose = new Pose(
                worldPosition,
                Quaternion.LookRotation(
                    worldDirection,
                    Vector3.up));
            return true;
        }

        private Vector3 ResolveReferencePassTravelDirection(
            Vector3 fallbackDirection,
            bool usesMultiExchangeFraming)
        {
            appliedReferencePassYaw = 0f;
            referencePassTravelDirection = Vector3.zero;
            if (!referencePassProfileEnabled ||
                usesMultiExchangeFraming ||
                compositionMode != RoomDioramaCompositionMode.Balanced)
            {
                return fallbackDirection;
            }

            Vector3 baseDirection = fallbackDirection.normalized;
            Vector3 resolvedDirection =
                Quaternion.AngleAxis(
                    referencePassCrossingYaw,
                    Vector3.up) *
                baseDirection;
            resolvedDirection = Flat(resolvedDirection);
            if (resolvedDirection.sqrMagnitude <= 0.000001f)
                return fallbackDirection;

            referencePassTravelDirection = resolvedDirection.normalized;
            appliedReferencePassYaw = Vector3.SignedAngle(
                baseDirection,
                referencePassTravelDirection,
                Vector3.up);
            return referencePassTravelDirection;
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

        private void ResolveAdaptiveRoomPresentation(
            Transform stage,
            VehicleBinding first,
            VehicleBinding second,
            bool usesLifeSize)
        {
            resolvedShowcaseVehicleScale = showcaseVehicleScale;
            resolvedShowcasePlaybackSpeedMultiplier =
                showcasePlaybackSpeedMultiplier;
            measuredBattleTravelInVehicleLengthsPerSecond = 0f;
            if (!normalizeRoomPresentation ||
                usesLifeSize ||
                stage == null ||
                first == null ||
                second == null ||
                vehicleLengthBefore <= 0.0001f)
            {
                return;
            }

            float minimumLength = Mathf.Max(
                0.05f,
                minimumRoomVehicleLength);
            float maximumLength = Mathf.Max(
                minimumLength,
                maximumRoomVehicleLength);
            float requestedLength = Mathf.Clamp(
                vehicleLengthBefore * showcaseVehicleScale,
                minimumLength,
                maximumLength);
            resolvedShowcaseVehicleScale = Mathf.Clamp(
                requestedLength / vehicleLengthBefore,
                minimumVehiclePresentationScale,
                maximumVehiclePresentationScale);
            float resolvedVehicleLength =
                vehicleLengthBefore *
                resolvedShowcaseVehicleScale;
            if (!TryMeasureBattleTravelRate(
                    stage,
                    first.DriverNumber,
                    second.DriverNumber,
                    resolvedVehicleLength,
                    out float travelRate))
            {
                return;
            }

            float currentTravelRate =
                travelRate * showcasePlaybackSpeedMultiplier;
            float minimumTravelRate = Mathf.Max(
                0.1f,
                minimumBoostedBattleTravelInVehicleLengthsPerSecond);
            float targetTravelRate = Mathf.Max(
                currentTravelRate,
                minimumTravelRate);
            float maximumPlaybackSpeed = Mathf.Max(
                showcasePlaybackSpeedMultiplier,
                maximumBoostedPlaybackSpeed);
            resolvedShowcasePlaybackSpeedMultiplier = Mathf.Clamp(
                targetTravelRate / travelRate,
                showcasePlaybackSpeedMultiplier,
                maximumPlaybackSpeed);
            measuredBattleTravelInVehicleLengthsPerSecond =
                travelRate *
                resolvedShowcasePlaybackSpeedMultiplier;
        }

        private bool TryMeasureBattleTravelRate(
            Transform stage,
            int firstDriver,
            int secondDriver,
            float vehicleLength,
            out float vehicleLengthsPerSecond)
        {
            vehicleLengthsPerSecond = 0f;
            presentationCalibrationBeats.Clear();
            if (eventReplay == null ||
                stage == null ||
                vehicleLength <= 0.0001f ||
                !eventReplay.TryCopyShowcaseActionBeats(
                    presentationCalibrationBeats))
            {
                return false;
            }

            float totalDistance = 0f;
            float totalDuration = 0f;
            float sampleStep = Mathf.Max(
                0.05f,
                visibilitySampleSeconds);
            for (int beatIndex = 0;
                beatIndex < presentationCalibrationBeats.Count;
                beatIndex++)
            {
                ShowcaseActionBeat beat =
                    presentationCalibrationBeats[beatIndex];
                float startTime = beat.StartTime;
                float endTime = beat.EndTime;
                float duration = endTime - startTime;
                if (duration <= 0.0001f)
                    continue;

                int sampleCount = Mathf.Max(
                    1,
                    Mathf.CeilToInt(duration / sampleStep));
                bool hasPrevious = false;
                float previousTime = startTime;
                Vector3 previousFirst = Vector3.zero;
                Vector3 previousSecond = Vector3.zero;
                for (int sampleIndex = 0;
                    sampleIndex <= sampleCount;
                    sampleIndex++)
                {
                    float replayTime = Mathf.Lerp(
                        startTime,
                        endTime,
                        sampleIndex / (float)sampleCount);
                    if (!eventReplay.TryGetEventLocalVehiclePosition(
                            firstDriver,
                            replayTime,
                            out Vector3 firstLocal) ||
                        !eventReplay.TryGetEventLocalVehiclePosition(
                            secondDriver,
                            replayTime,
                            out Vector3 secondLocal))
                    {
                        hasPrevious = false;
                        continue;
                    }

                    Vector3 firstWorld =
                        stage.TransformPoint(firstLocal);
                    Vector3 secondWorld =
                        stage.TransformPoint(secondLocal);
                    if (hasPrevious)
                    {
                        float segmentDuration =
                            replayTime - previousTime;
                        if (segmentDuration > 0f)
                        {
                            totalDistance +=
                                (Vector3.Distance(
                                     previousFirst,
                                     firstWorld) +
                                 Vector3.Distance(
                                     previousSecond,
                                     secondWorld)) * 0.5f;
                            totalDuration += segmentDuration;
                        }
                    }

                    hasPrevious = true;
                    previousTime = replayTime;
                    previousFirst = firstWorld;
                    previousSecond = secondWorld;
                }
            }

            if (totalDistance <= 0.0001f ||
                totalDuration <= 0.0001f)
            {
                return false;
            }

            vehicleLengthsPerSecond =
                totalDistance /
                totalDuration /
                vehicleLength;
            return float.IsFinite(vehicleLengthsPerSecond) &&
                vehicleLengthsPerSecond > 0.0001f;
        }

        private float ResolveActiveShowcaseVehicleScale()
        {
            return resolvedShowcaseVehicleScale > 0f
                ? resolvedShowcaseVehicleScale
                : showcaseVehicleScale;
        }

        private float ResolveActiveShowcasePlaybackSpeed()
        {
            return resolvedShowcasePlaybackSpeedMultiplier > 0f
                ? resolvedShowcasePlaybackSpeedMultiplier
                : showcasePlaybackSpeedMultiplier;
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
                    ? ResolveActiveShowcaseVehicleScale() *
                      Mathf.Max(
                          1f,
                          portalPresentation.EvaluateImmersiveScale(
                              presentationAnchor))
                    : ResolveActiveShowcaseVehicleScale();
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

        private static float MeasureWorldVisualHeight(
            VehicleBinding binding,
            Vector3 up)
        {
            ReplayCarView car = ResolveCar(binding);
            return car != null &&
                car.TryGetVisualWorldHeight(up, out float height)
                    ? height
                    : 0f;
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
            PublishRuntimePerceptionMetrics(
                eventReplay != null &&
                eventReplay.CurrentTime >=
                GetFinalRuntimeActionTime() - 0.001f);
            Transform stageToRestore = boundStage;
            CancelBattleReframe();
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
            eventReplay?.ClearShowcasePresentationEndTime();
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
            resolvedShowcaseVehicleScale = 0f;
            resolvedShowcasePlaybackSpeedMultiplier = 0f;
            measuredBattleTravelInVehicleLengthsPerSecond = 0f;
            entryContinuation = 0f;
            exitContinuation = 0f;
            heroMissDistance = 0f;
            entryWallAngle = 0f;
            exitWallAngle = 0f;
            portalCrossingMiss = 0f;
            compositionShiftDistance = 0f;
            appliedReferencePassYaw = 0f;
            referencePassTravelDirection = Vector3.zero;
            connectedPortalCandidate = false;
            hasRecommendedWallExitPlacement = false;
            appliedRecommendedWallExitPlacement = false;
            recommendedWallExitPlacement = default;
            recommendedWallExitFrame = default;
            activePortalMode = "None";
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
            presentationCalibrationBeats.Clear();
            ResetShotVisibilityAnalysis();
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
