using System.Collections.Generic;
using UnityEngine;

namespace F1XR.RestAPI.Replay.Room
{
    internal enum CollisionRoomPlacementMode
    {
        None,
        RoomFloorCorridor,
        RoomCompact,
        ViewerCompact
    }

    internal enum CollisionRoomPlacementFailure
    {
        None,
        InvalidContent,
        RoomUnavailable,
        RoomNotFrozen,
        RoomBoundaryUnavailable,
        WallDirectionsIncompatible,
        FloorUnavailable,
        RoomDoesNotFit,
        ViewerUnavailable
    }

    internal readonly struct CollisionRoomPlacementContent
    {
        public CollisionRoomPlacementContent(
            Vector3 localContact,
            Vector3 localForward,
            IReadOnlyList<Vector3> localFootprint,
            float localVehicleLength)
        {
            LocalContact = localContact;
            LocalForward = localForward;
            LocalFootprint = localFootprint;
            LocalVehicleLength = localVehicleLength;
        }

        public Vector3 LocalContact { get; }
        public Vector3 LocalForward { get; }
        public IReadOnlyList<Vector3> LocalFootprint { get; }
        public float LocalVehicleLength { get; }
    }

    internal struct CollisionRoomPlacementSettings
    {
        public float FullMinimumLengthMeters;
        public float FullPreferredLengthMeters;
        public float FullMaximumLengthMeters;
        public float FullMinimumVehicleLengthMeters;
        public float FullMaximumVehicleLengthMeters;
        public float FullMaximumWidthMeters;
        public float FullMaximumHeroShiftMeters;

        public float CompactMinimumLengthMeters;
        public float CompactPreferredLengthMeters;
        public float CompactMaximumLengthMeters;
        public float CompactMinimumVehicleLengthMeters;
        public float CompactMaximumVehicleLengthMeters;
        public float CompactMaximumWidthMeters;
        public float CompactMaximumHeroShiftMeters;

        public float ViewerPreferredVehicleLengthMeters;
        public float ViewerMinimumVehicleLengthMeters;
        public float ViewerMaximumVehicleLengthMeters;
        public float ViewerMaximumSpanMeters;
        public float ViewerMaximumWidthMeters;
        public float ViewerForwardDistanceMeters;
        public float ViewerBelowEyeMeters;

        public float FloorOffsetMeters;
        public float EntryExitWallMarginMeters;
        public float SideWallMarginMeters;
        public float MaximumFallbackFloorDeltaMeters;
        public float MaximumFloorTiltDegrees;
        public float MaximumWallTravelAngleDegrees;
        public float MaximumOpposingWallNormalDot;
        public float MaximumUniformScale;
        public int FitIterations;

        public static CollisionRoomPlacementSettings Default => new()
        {
            FullMinimumLengthMeters = 4f,
            FullPreferredLengthMeters = 5f,
            FullMaximumLengthMeters = 6f,
            FullMinimumVehicleLengthMeters = 0.5f,
            FullMaximumVehicleLengthMeters = 0.7f,
            FullMaximumWidthMeters = 2.4f,
            FullMaximumHeroShiftMeters = 0.6f,

            CompactMinimumLengthMeters = 2.4f,
            CompactPreferredLengthMeters = 3.2f,
            CompactMaximumLengthMeters = 3.8f,
            CompactMinimumVehicleLengthMeters = 0.36f,
            CompactMaximumVehicleLengthMeters = 0.5f,
            CompactMaximumWidthMeters = 1.8f,
            CompactMaximumHeroShiftMeters = 0.45f,

            ViewerPreferredVehicleLengthMeters = 0.42f,
            ViewerMinimumVehicleLengthMeters = 0.32f,
            ViewerMaximumVehicleLengthMeters = 0.46f,
            ViewerMaximumSpanMeters = 2.8f,
            ViewerMaximumWidthMeters = 1.8f,
            ViewerForwardDistanceMeters = 1.6f,
            ViewerBelowEyeMeters = 0.75f,

            FloorOffsetMeters = 0.02f,
            EntryExitWallMarginMeters = 0.35f,
            SideWallMarginMeters = 0.25f,
            MaximumFallbackFloorDeltaMeters = 0.15f,
            MaximumFloorTiltDegrees = 10f,
            MaximumWallTravelAngleDegrees = 55f,
            MaximumOpposingWallNormalDot = -0.35f,
            MaximumUniformScale = 512f,
            FitIterations = 12
        };

        public bool IsValid =>
            FullMinimumLengthMeters > 0f &&
            FullPreferredLengthMeters >= FullMinimumLengthMeters &&
            FullMaximumLengthMeters >= FullPreferredLengthMeters &&
            FullMinimumVehicleLengthMeters > 0f &&
            FullMaximumVehicleLengthMeters >=
            FullMinimumVehicleLengthMeters &&
            FullMaximumWidthMeters > 0f &&
            FullMaximumHeroShiftMeters >= 0f &&
            CompactMinimumLengthMeters > 0f &&
            CompactPreferredLengthMeters >=
            CompactMinimumLengthMeters &&
            CompactMaximumLengthMeters >=
            CompactPreferredLengthMeters &&
            CompactMinimumVehicleLengthMeters > 0f &&
            CompactMaximumVehicleLengthMeters >=
            CompactMinimumVehicleLengthMeters &&
            CompactMaximumWidthMeters > 0f &&
            CompactMaximumHeroShiftMeters >= 0f &&
            ViewerPreferredVehicleLengthMeters > 0f &&
            ViewerMinimumVehicleLengthMeters > 0f &&
            ViewerMaximumVehicleLengthMeters >=
            ViewerMinimumVehicleLengthMeters &&
            ViewerPreferredVehicleLengthMeters >=
            ViewerMinimumVehicleLengthMeters &&
            ViewerPreferredVehicleLengthMeters <=
            ViewerMaximumVehicleLengthMeters &&
            ViewerMaximumSpanMeters > 0f &&
            ViewerMaximumWidthMeters > 0f &&
            ViewerForwardDistanceMeters > 0f &&
            ViewerBelowEyeMeters >= 0f &&
            FloorOffsetMeters >= 0f &&
            EntryExitWallMarginMeters >= 0f &&
            SideWallMarginMeters >= 0f &&
            MaximumFallbackFloorDeltaMeters >= 0f &&
            MaximumFloorTiltDegrees >= 0f &&
            MaximumFloorTiltDegrees < 90f &&
            MaximumWallTravelAngleDegrees > 0f &&
            MaximumWallTravelAngleDegrees < 90f &&
            MaximumOpposingWallNormalDot >= -1f &&
            MaximumOpposingWallNormalDot <= 0f &&
            MaximumUniformScale > 0f &&
            FitIterations > 0;
    }

    internal readonly struct CollisionRoomPlacementResult
    {
        public CollisionRoomPlacementResult(
            CollisionRoomPlacementMode mode,
            Pose stagePose,
            float uniformScale,
            Vector3 contactWorldPosition,
            Vector3 presentationForward,
            float physicalLengthMeters,
            float physicalWidthMeters,
            float targetVehicleLengthMeters,
            float heroShiftMeters,
            int layoutRevision,
            int contentRevision,
            CollisionRoomPlacementFailure roomFallbackReason)
        {
            Mode = mode;
            StagePose = stagePose;
            UniformScale = uniformScale;
            ContactWorldPosition = contactWorldPosition;
            PresentationForward = presentationForward;
            PhysicalLengthMeters = physicalLengthMeters;
            PhysicalWidthMeters = physicalWidthMeters;
            TargetVehicleLengthMeters = targetVehicleLengthMeters;
            HeroShiftMeters = heroShiftMeters;
            LayoutRevision = layoutRevision;
            ContentRevision = contentRevision;
            RoomFallbackReason = roomFallbackReason;
        }

        public CollisionRoomPlacementMode Mode { get; }
        public Pose StagePose { get; }
        public float UniformScale { get; }
        public Vector3 ContactWorldPosition { get; }
        public Vector3 PresentationForward { get; }
        public float PhysicalLengthMeters { get; }
        public float PhysicalWidthMeters { get; }
        public float TargetVehicleLengthMeters { get; }
        public float HeroShiftMeters { get; }
        public int LayoutRevision { get; }
        public int ContentRevision { get; }
        public CollisionRoomPlacementFailure RoomFallbackReason { get; }
        public bool UsesRoomFloor =>
            Mode == CollisionRoomPlacementMode.RoomFloorCorridor ||
            Mode == CollisionRoomPlacementMode.RoomCompact;
        public bool AllowsStageGrab =>
            Mode == CollisionRoomPlacementMode.ViewerCompact;
        public bool IsValid =>
            Mode != CollisionRoomPlacementMode.None &&
            UniformScale > 0f &&
            float.IsFinite(UniformScale) &&
            IsFinite(StagePose.position) &&
            IsFinite(StagePose.rotation);

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

    internal sealed class CollisionRoomPlacementResolver
    {
        private const int MaximumFootprintPoints = 32;
        private const int InitialWallCapacity = 16;
        private const float MinimumMetric = 0.0001f;
        private const float PlaneCoefficientEpsilon = 0.00001f;

        private readonly List<ShowcaseWallFrame> wallFrames =
            new(InitialWallCapacity);
        private readonly Vector3[] localFootprint =
            new Vector3[MaximumFootprintPoints];

        private ShowcaseLayout cachedLayout;
        private CollisionRoomPlacementSettings cachedSettings;
        private CollisionRoomPlacementResult cachedResult;
        private Vector3 localContact;
        private Vector3 localForward;
        private float localLength;
        private float localWidth;
        private float localVehicleLength;
        private int localFootprintCount;
        private int cachedLayoutRevision = -1;
        private int cachedContentRevision = -1;
        private bool contentPrepared;
        private CollisionRoomPlacementFailure lastFailure;
        private CollisionRoomPlacementFailure lastRoomFailure;

        public CollisionRoomPlacementResult CachedResult => cachedResult;
        public int WallSnapshotCount => wallFrames.Count;
        public CollisionRoomPlacementFailure LastFailure => lastFailure;
        public CollisionRoomPlacementFailure LastRoomFailure =>
            lastRoomFailure;

        public bool Prepare(
            ShowcaseLayout layout,
            Camera viewer,
            int contentRevision,
            in CollisionRoomPlacementContent content,
            out CollisionRoomPlacementResult placement)
        {
            CollisionRoomPlacementSettings settings =
                CollisionRoomPlacementSettings.Default;
            return Prepare(
                layout,
                viewer,
                contentRevision,
                content,
                settings,
                out placement);
        }

        public bool Prepare(
            ShowcaseLayout layout,
            Camera viewer,
            int contentRevision,
            in CollisionRoomPlacementContent content,
            in CollisionRoomPlacementSettings settings,
            out CollisionRoomPlacementResult placement)
        {
            cachedLayout = layout;
            cachedLayoutRevision = layout != null
                ? layout.LayoutRevision
                : -1;
            cachedContentRevision = contentRevision;
            cachedSettings = settings.IsValid
                ? settings
                : CollisionRoomPlacementSettings.Default;
            cachedResult = default;
            lastFailure = CollisionRoomPlacementFailure.None;
            lastRoomFailure = CollisionRoomPlacementFailure.None;
            wallFrames.Clear();

            if (!TryCaptureContent(content))
            {
                contentPrepared = false;
                lastFailure = CollisionRoomPlacementFailure
                    .InvalidContent;
                placement = default;
                return false;
            }

            contentPrepared = true;
            CollisionRoomPlacementFailure roomFailure =
                CollisionRoomPlacementFailure.RoomUnavailable;
            if (TryCaptureRoom(
                    layout,
                    out RoomSnapshot room,
                    out roomFailure))
            {
                bool fullEligible =
                    layout.WallFramesFrozen &&
                    wallFrames.Count >= 3;
                if (fullEligible &&
                    TryBuildRoomPlacement(
                        CollisionRoomPlacementMode
                            .RoomFloorCorridor,
                        room,
                        cachedSettings.FullMinimumLengthMeters,
                        cachedSettings.FullPreferredLengthMeters,
                        cachedSettings.FullMaximumLengthMeters,
                        cachedSettings
                            .FullMinimumVehicleLengthMeters,
                        cachedSettings
                            .FullMaximumVehicleLengthMeters,
                        cachedSettings.FullMaximumWidthMeters,
                        cachedSettings.FullMaximumHeroShiftMeters,
                        CollisionRoomPlacementFailure.None,
                        out placement))
                {
                    cachedResult = placement;
                    return true;
                }

                roomFailure = !layout.WallFramesFrozen
                    ? CollisionRoomPlacementFailure.RoomNotFrozen
                    : wallFrames.Count < 3
                        ? CollisionRoomPlacementFailure
                            .RoomBoundaryUnavailable
                        : CollisionRoomPlacementFailure.RoomDoesNotFit;
                if (wallFrames.Count >= 3 &&
                    TryBuildRoomPlacement(
                        CollisionRoomPlacementMode.RoomCompact,
                        room,
                        cachedSettings.CompactMinimumLengthMeters,
                        cachedSettings.CompactPreferredLengthMeters,
                        cachedSettings.CompactMaximumLengthMeters,
                        cachedSettings
                            .CompactMinimumVehicleLengthMeters,
                        cachedSettings
                            .CompactMaximumVehicleLengthMeters,
                        cachedSettings.CompactMaximumWidthMeters,
                        cachedSettings.CompactMaximumHeroShiftMeters,
                        roomFailure,
                        out placement))
                {
                    lastRoomFailure = roomFailure;
                    cachedResult = placement;
                    return true;
                }

                if (wallFrames.Count >= 3)
                {
                    roomFailure = CollisionRoomPlacementFailure
                        .RoomDoesNotFit;
                }
            }

            lastRoomFailure = roomFailure;

            if (TryBuildViewerPlacement(
                    viewer,
                    roomFailure,
                    out placement))
            {
                cachedResult = placement;
                return true;
            }

            lastFailure = CollisionRoomPlacementFailure
                .ViewerUnavailable;
            placement = default;
            return false;
        }

        public bool TryGetCached(
            ShowcaseLayout layout,
            int contentRevision,
            out CollisionRoomPlacementResult placement)
        {
            int layoutRevision = layout != null
                ? layout.LayoutRevision
                : -1;
            if (!cachedResult.IsValid ||
                cachedLayout != layout ||
                cachedLayoutRevision != layoutRevision ||
                cachedContentRevision != contentRevision)
            {
                placement = default;
                return false;
            }

            placement = cachedResult;
            return true;
        }

        public bool TryRefreshViewerCompact(
            Camera viewer,
            out CollisionRoomPlacementResult placement)
        {
            if (!contentPrepared ||
                cachedResult.Mode !=
                CollisionRoomPlacementMode.ViewerCompact ||
                !TryBuildViewerPlacement(
                    viewer,
                    cachedResult.RoomFallbackReason,
                    out placement))
            {
                placement = default;
                return false;
            }

            cachedResult = placement;
            return true;
        }

        public void Invalidate()
        {
            cachedLayout = null;
            cachedLayoutRevision = -1;
            cachedContentRevision = -1;
            localFootprintCount = 0;
            contentPrepared = false;
            cachedResult = default;
            lastFailure = CollisionRoomPlacementFailure.None;
            lastRoomFailure = CollisionRoomPlacementFailure.None;
            wallFrames.Clear();
        }

        private bool TryCaptureContent(
            in CollisionRoomPlacementContent content)
        {
            IReadOnlyList<Vector3> footprint =
                content.LocalFootprint;
            if (footprint == null ||
                footprint.Count < 3 ||
                footprint.Count > MaximumFootprintPoints ||
                !IsFinite(content.LocalContact) ||
                !IsFinite(content.LocalForward) ||
                !float.IsFinite(content.LocalVehicleLength) ||
                content.LocalVehicleLength <= MinimumMetric)
            {
                return false;
            }

            Vector3 forward = Flat(content.LocalForward);
            if (forward.sqrMagnitude <= MinimumMetric * MinimumMetric)
                return false;

            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            float minimumLongitudinal = float.PositiveInfinity;
            float maximumLongitudinal = float.NegativeInfinity;
            float minimumLateral = float.PositiveInfinity;
            float maximumLateral = float.NegativeInfinity;
            for (int index = 0; index < footprint.Count; index++)
            {
                Vector3 point = footprint[index];
                if (!IsFinite(point))
                    return false;

                localFootprint[index] = point;
                Vector3 offset = point - content.LocalContact;
                float longitudinal = Vector3.Dot(offset, forward);
                float lateral = Vector3.Dot(offset, right);
                minimumLongitudinal = Mathf.Min(
                    minimumLongitudinal,
                    longitudinal);
                maximumLongitudinal = Mathf.Max(
                    maximumLongitudinal,
                    longitudinal);
                minimumLateral = Mathf.Min(
                    minimumLateral,
                    lateral);
                maximumLateral = Mathf.Max(
                    maximumLateral,
                    lateral);
            }

            float length =
                maximumLongitudinal - minimumLongitudinal;
            float width = maximumLateral - minimumLateral;
            if (length <= MinimumMetric ||
                width <= MinimumMetric ||
                !float.IsFinite(length) ||
                !float.IsFinite(width))
            {
                return false;
            }

            localContact = content.LocalContact;
            localForward = forward;
            localLength = length;
            localWidth = width;
            localVehicleLength = content.LocalVehicleLength;
            localFootprintCount = footprint.Count;
            return true;
        }

        private bool TryCaptureRoom(
            ShowcaseLayout layout,
            out RoomSnapshot room,
            out CollisionRoomPlacementFailure failure)
        {
            room = default;
            failure = CollisionRoomPlacementFailure.RoomUnavailable;
            if (layout == null ||
                !layout.IsLayoutValid ||
                !layout.TryGetEntryPose(out Pose entryPose) ||
                !layout.TryGetHeroPose(out Pose heroPose) ||
                !layout.TryGetExitPose(out Pose exitPose))
            {
                return false;
            }

            layout.CopyAvailableWallFrames(wallFrames);
            if (!TryFindWall(
                    layout.EntryWallId,
                    out ShowcaseWallFrame entryWall) ||
                !TryFindWall(
                    layout.ExitWallId,
                    out ShowcaseWallFrame exitWall))
            {
                failure = CollisionRoomPlacementFailure
                    .RoomBoundaryUnavailable;
                return false;
            }

            Vector3 axis = Flat(
                exitPose.position - entryPose.position);
            Vector3 entryTravel = Flat(
                layout.EntryTravelDirection);
            Vector3 exitTravel = Flat(
                layout.ExitTravelDirection);
            Vector3 entryInward = Flat(entryWall.InwardNormal);
            Vector3 exitInward = Flat(exitWall.InwardNormal);
            if (!TryNormalize(ref axis) ||
                !TryNormalize(ref entryTravel) ||
                !TryNormalize(ref exitTravel) ||
                !TryNormalize(ref entryInward) ||
                !TryNormalize(ref exitInward) ||
                Vector3.Angle(axis, entryTravel) >
                cachedSettings.MaximumWallTravelAngleDegrees ||
                Vector3.Angle(axis, exitTravel) >
                cachedSettings.MaximumWallTravelAngleDegrees ||
                Vector3.Dot(entryInward, exitInward) >
                cachedSettings.MaximumOpposingWallNormalDot)
            {
                failure = CollisionRoomPlacementFailure
                    .WallDirectionsIncompatible;
                return false;
            }

            if (!TryResolveFloor(
                    layout,
                    entryWall,
                    exitWall,
                    out FloorSnapshot floor))
            {
                failure = CollisionRoomPlacementFailure
                    .FloorUnavailable;
                return false;
            }

            Vector3 heroFloor = floor.Project(heroPose.position) +
                floor.Normal * cachedSettings.FloorOffsetMeters;
            if (!IsFinite(heroFloor))
            {
                failure = CollisionRoomPlacementFailure
                    .FloorUnavailable;
                return false;
            }

            Quaternion floorTilt = Quaternion.FromToRotation(
                Vector3.up,
                floor.Normal);
            Vector3 tiltedLocalForward = Vector3.ProjectOnPlane(
                floorTilt * localForward,
                floor.Normal);
            Vector3 floorAxis = Vector3.ProjectOnPlane(
                axis,
                floor.Normal);
            if (!TryNormalize(ref tiltedLocalForward) ||
                !TryNormalize(ref floorAxis))
            {
                failure = CollisionRoomPlacementFailure
                    .FloorUnavailable;
                return false;
            }
            float yaw = Vector3.SignedAngle(
                tiltedLocalForward,
                floorAxis,
                floor.Normal);
            Quaternion rotation = Quaternion.AngleAxis(
                yaw,
                floor.Normal) * floorTilt;
            room = new RoomSnapshot(
                axis,
                heroPose.position,
                heroFloor,
                rotation,
                floor,
                layout.EntryWallId,
                layout.ExitWallId);
            failure = CollisionRoomPlacementFailure.None;
            return true;
        }

        private bool TryResolveFloor(
            ShowcaseLayout layout,
            ShowcaseWallFrame entryWall,
            ShowcaseWallFrame exitWall,
            out FloorSnapshot floor)
        {
            floor = default;
            if (layout.TryGetDetectedFloorPlane(
                    out Plane detectedFloor))
            {
                Vector3 normal = detectedFloor.normal;
                if (!TryNormalize(ref normal) ||
                    Vector3.Angle(normal, Vector3.up) >
                    cachedSettings.MaximumFloorTiltDegrees)
                {
                    return false;
                }

                floor = new FloorSnapshot(detectedFloor);
                return true;
            }

            Vector3 entryBottom = entryWall.Center +
                entryWall.VerticalAxis * entryWall.MinVertical;
            Vector3 exitBottom = exitWall.Center +
                exitWall.VerticalAxis * exitWall.MinVertical;
            if (!IsFinite(entryBottom) ||
                !IsFinite(exitBottom) ||
                Mathf.Abs(entryBottom.y - exitBottom.y) >
                cachedSettings.MaximumFallbackFloorDeltaMeters ||
                !layout.TryGetRoomFloorHeight(
                    out float floorHeight) ||
                !float.IsFinite(floorHeight))
            {
                return false;
            }

            floor = new FloorSnapshot(floorHeight);
            return true;
        }

        private bool TryBuildRoomPlacement(
            CollisionRoomPlacementMode mode,
            in RoomSnapshot room,
            float minimumLength,
            float preferredLength,
            float maximumLength,
            float minimumVehicleLength,
            float maximumVehicleLength,
            float maximumWidth,
            float maximumHeroShift,
            CollisionRoomPlacementFailure fallbackReason,
            out CollisionRoomPlacementResult placement)
        {
            placement = default;
            float minimumScale = Mathf.Max(
                minimumLength / localLength,
                minimumVehicleLength / localVehicleLength);
            float maximumScale = Mathf.Min(
                cachedSettings.MaximumUniformScale,
                Mathf.Min(
                    maximumLength / localLength,
                    Mathf.Min(
                        maximumVehicleLength /
                        localVehicleLength,
                        maximumWidth / localWidth)));
            if (!float.IsFinite(minimumScale) ||
                !float.IsFinite(maximumScale) ||
                maximumScale < minimumScale ||
                maximumScale <= MinimumMetric)
            {
                return false;
            }

            if (!TryResolveMaximumRoomScale(
                    room,
                    maximumScale,
                    maximumHeroShift,
                    out float roomMaximumScale) ||
                roomMaximumScale + MinimumMetric < minimumScale)
            {
                return false;
            }

            float desiredScale = Mathf.Clamp(
                preferredLength / localLength,
                minimumScale,
                roomMaximumScale);
            if (!TryResolveHeroShift(
                    room,
                    desiredScale,
                    maximumHeroShift,
                    out float heroShift))
            {
                return false;
            }

            Vector3 shiftedHero = room.HeroWorldPosition +
                room.Axis * heroShift;
            Vector3 contactWorld = room.Floor.Project(shiftedHero) +
                room.Floor.Normal *
                cachedSettings.FloorOffsetMeters;
            Vector3 stagePosition = contactWorld -
                room.StageRotation * localContact * desiredScale;
            Pose stagePose = new(
                stagePosition,
                room.StageRotation);
            placement = new CollisionRoomPlacementResult(
                mode,
                stagePose,
                desiredScale,
                contactWorld,
                room.Axis,
                localLength * desiredScale,
                localWidth * desiredScale,
                localVehicleLength * desiredScale,
                heroShift,
                cachedLayoutRevision,
                cachedContentRevision,
                fallbackReason);
            return placement.IsValid;
        }

        private bool TryResolveMaximumRoomScale(
            in RoomSnapshot room,
            float maximumScale,
            float maximumHeroShift,
            out float resolvedScale)
        {
            resolvedScale = 0f;
            if (!TryResolveHeroShift(
                    room,
                    0f,
                    maximumHeroShift,
                    out _))
            {
                return false;
            }

            if (TryResolveHeroShift(
                    room,
                    maximumScale,
                    maximumHeroShift,
                    out _))
            {
                resolvedScale = maximumScale;
                return true;
            }

            float lower = 0f;
            float upper = maximumScale;
            int iterations = Mathf.Clamp(
                cachedSettings.FitIterations,
                1,
                24);
            for (int iteration = 0;
                 iteration < iterations;
                 iteration++)
            {
                float middle = (lower + upper) * 0.5f;
                if (TryResolveHeroShift(
                        room,
                        middle,
                        maximumHeroShift,
                        out _))
                {
                    lower = middle;
                }
                else
                {
                    upper = middle;
                }
            }

            resolvedScale = lower;
            return resolvedScale > MinimumMetric;
        }

        private bool TryResolveHeroShift(
            in RoomSnapshot room,
            float scale,
            float maximumHeroShift,
            out float heroShift)
        {
            float minimumShift = -maximumHeroShift;
            float maximumShift = maximumHeroShift;
            for (int wallIndex = 0;
                 wallIndex < wallFrames.Count;
                 wallIndex++)
            {
                ShowcaseWallFrame wall = wallFrames[wallIndex];
                Vector3 inward = Flat(wall.InwardNormal);
                if (!wall.IsValid || !TryNormalize(ref inward))
                {
                    heroShift = 0f;
                    return false;
                }

                bool selected = wall.Id == room.EntryWallId ||
                    wall.Id == room.ExitWallId;
                float margin = selected
                    ? cachedSettings.EntryExitWallMarginMeters
                    : cachedSettings.SideWallMarginMeters;
                float shiftCoefficient = Vector3.Dot(
                    room.Axis,
                    inward);
                for (int pointIndex = 0;
                     pointIndex < localFootprintCount;
                     pointIndex++)
                {
                    Vector3 localOffset =
                        localFootprint[pointIndex] - localContact;
                    Vector3 worldOffset =
                        room.StageRotation * localOffset * scale;
                    float baseDistance = Vector3.Dot(
                        room.HeroFloorPosition +
                        worldOffset - wall.Center,
                        inward);
                    if (Mathf.Abs(shiftCoefficient) <=
                        PlaneCoefficientEpsilon)
                    {
                        if (baseDistance + MinimumMetric < margin)
                        {
                            heroShift = 0f;
                            return false;
                        }

                        continue;
                    }

                    float bound =
                        (margin - baseDistance) /
                        shiftCoefficient;
                    if (shiftCoefficient > 0f)
                        minimumShift = Mathf.Max(minimumShift, bound);
                    else
                        maximumShift = Mathf.Min(maximumShift, bound);

                    if (minimumShift >
                        maximumShift + MinimumMetric)
                    {
                        heroShift = 0f;
                        return false;
                    }
                }
            }

            heroShift = Mathf.Clamp(
                0f,
                minimumShift,
                maximumShift);
            return float.IsFinite(heroShift);
        }

        private bool TryBuildViewerPlacement(
            Camera viewer,
            CollisionRoomPlacementFailure roomFailure,
            out CollisionRoomPlacementResult placement)
        {
            placement = default;
            if (viewer == null)
                return false;

            Transform viewerTransform = viewer.transform;
            Vector3 viewForward = Flat(viewerTransform.forward);
            if (!TryNormalize(ref viewForward))
                viewForward = Vector3.forward;
            Vector3 presentationForward = Flat(
                viewerTransform.right);
            if (!TryNormalize(ref presentationForward))
            {
                presentationForward = Vector3.Cross(
                    Vector3.up,
                    viewForward);
            }

            float desiredScale =
                cachedSettings.ViewerPreferredVehicleLengthMeters /
                localVehicleLength;
            float minimumScale =
                cachedSettings.ViewerMinimumVehicleLengthMeters /
                localVehicleLength;
            float maximumScale = Mathf.Min(
                cachedSettings.MaximumUniformScale,
                Mathf.Min(
                    cachedSettings
                        .ViewerMaximumVehicleLengthMeters /
                    localVehicleLength,
                    cachedSettings.ViewerMaximumWidthMeters /
                    localWidth));
            if (!float.IsFinite(maximumScale) ||
                maximumScale <= MinimumMetric)
            {
                return false;
            }

            desiredScale = maximumScale >= minimumScale
                ? Mathf.Clamp(
                    desiredScale,
                    minimumScale,
                    maximumScale)
                : maximumScale;
            float yaw = Vector3.SignedAngle(
                localForward,
                presentationForward,
                Vector3.up);
            Quaternion rotation = Quaternion.AngleAxis(
                yaw,
                Vector3.up);
            Vector3 contactWorld =
                viewerTransform.position +
                viewForward *
                cachedSettings.ViewerForwardDistanceMeters -
                Vector3.up *
                cachedSettings.ViewerBelowEyeMeters;
            Vector3 stagePosition = contactWorld -
                rotation * localContact * desiredScale;
            float uncompressedLength = localLength * desiredScale;
            float physicalLength = Mathf.Min(
                cachedSettings.ViewerMaximumSpanMeters,
                uncompressedLength);
            float lengthCompression = uncompressedLength > MinimumMetric
                ? physicalLength / uncompressedLength
                : 1f;
            placement = new CollisionRoomPlacementResult(
                CollisionRoomPlacementMode.ViewerCompact,
                new Pose(stagePosition, rotation),
                desiredScale,
                contactWorld,
                presentationForward,
                physicalLength,
                Mathf.Min(
                    cachedSettings.ViewerMaximumWidthMeters,
                    localWidth * desiredScale * lengthCompression),
                localVehicleLength * desiredScale,
                0f,
                cachedLayoutRevision,
                cachedContentRevision,
                roomFailure);
            return placement.IsValid;
        }

        private bool TryFindWall(
            UnityEngine.XR.ARSubsystems.TrackableId id,
            out ShowcaseWallFrame wall)
        {
            for (int index = 0;
                 index < wallFrames.Count;
                 index++)
            {
                if (wallFrames[index].Id != id)
                    continue;

                wall = wallFrames[index];
                return wall.IsValid;
            }

            wall = default;
            return false;
        }

        private static Vector3 Flat(Vector3 value)
        {
            value.y = 0f;
            return value;
        }

        private static bool TryNormalize(ref Vector3 value)
        {
            if (!IsFinite(value) ||
                value.sqrMagnitude <=
                MinimumMetric * MinimumMetric)
            {
                return false;
            }

            value.Normalize();
            return true;
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) &&
                float.IsFinite(value.y) &&
                float.IsFinite(value.z);
        }

        private readonly struct FloorSnapshot
        {
            public FloorSnapshot(Plane plane)
            {
                Plane = plane;
                Height = 0f;
                UsesPlane = true;
                Normal = plane.normal.normalized;
            }

            public FloorSnapshot(float height)
            {
                Plane = default;
                Height = height;
                UsesPlane = false;
                Normal = Vector3.up;
            }

            private Plane Plane { get; }
            private float Height { get; }
            private bool UsesPlane { get; }
            public Vector3 Normal { get; }

            public Vector3 Project(Vector3 position)
            {
                if (UsesPlane)
                    return Plane.ClosestPointOnPlane(position);

                position.y = Height;
                return position;
            }
        }

        private readonly struct RoomSnapshot
        {
            public RoomSnapshot(
                Vector3 axis,
                Vector3 heroWorldPosition,
                Vector3 heroFloorPosition,
                Quaternion stageRotation,
                FloorSnapshot floor,
                UnityEngine.XR.ARSubsystems.TrackableId entryWallId,
                UnityEngine.XR.ARSubsystems.TrackableId exitWallId)
            {
                Axis = axis;
                HeroWorldPosition = heroWorldPosition;
                HeroFloorPosition = heroFloorPosition;
                StageRotation = stageRotation;
                Floor = floor;
                EntryWallId = entryWallId;
                ExitWallId = exitWallId;
            }

            public Vector3 Axis { get; }
            public Vector3 HeroWorldPosition { get; }
            public Vector3 HeroFloorPosition { get; }
            public Quaternion StageRotation { get; }
            public FloorSnapshot Floor { get; }
            public UnityEngine.XR.ARSubsystems.TrackableId EntryWallId
            {
                get;
            }
            public UnityEngine.XR.ARSubsystems.TrackableId ExitWallId
            {
                get;
            }
        }
    }
}
