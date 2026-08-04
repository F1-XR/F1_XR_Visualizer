using System;
using System.Collections.Generic;
using UnityEngine;

namespace F1XR.RestAPI.Replay.Room
{
    [Serializable]
    internal sealed class LifeSizeDriveBySettings
    {
        [SerializeField, Min(0.5f)] private float roadWidth = 3.6f;
        [SerializeField, Min(0.01f)] private float roadThickness = 0.08f;
        [SerializeField, Min(0.5f)] private float portalHeight = 2.1f;
        [SerializeField, Min(0f)] private float wallMargin = 0.1f;
        [SerializeField, Min(0.001f)] private float seamPositionTolerance = 0.03f;
        [SerializeField, Range(0.1f, 10f)] private float seamAngleTolerance = 2f;

        public float RoadWidth => roadWidth;
        public float RoadThickness => roadThickness;
        public float PortalHeight => portalHeight;
        public float WallMargin => wallMargin;
        public float SeamPositionTolerance => seamPositionTolerance;
        public float SeamAngleTolerance => seamAngleTolerance;

        public void ClampValues()
        {
            roadWidth = Mathf.Max(0.5f, roadWidth);
            roadThickness = Mathf.Max(0.01f, roadThickness);
            portalHeight = Mathf.Max(0.5f, portalHeight);
            wallMargin = Mathf.Max(0f, wallMargin);
            seamPositionTolerance = Mathf.Max(
                0.001f,
                seamPositionTolerance);
            seamAngleTolerance = Mathf.Clamp(
                seamAngleTolerance,
                0.1f,
                10f);
        }
    }

    internal readonly struct LifeSizePortalSeam
    {
        public LifeSizePortalSeam(
            Vector3 roadCenter,
            Vector3 travelDirection,
            Vector3 up,
            Vector2 apertureSize)
        {
            RoadCenter = roadCenter;
            TravelDirection = travelDirection.normalized;
            Up = up.normalized;
            Right = Vector3.Cross(Up, TravelDirection).normalized;
            ApertureSize = apertureSize;
            PortalPose = new Pose(
                RoadCenter + Up * (apertureSize.y * 0.5f),
                Quaternion.LookRotation(TravelDirection, Up));
        }

        public Vector3 RoadCenter { get; }
        public Vector3 TravelDirection { get; }
        public Vector3 Right { get; }
        public Vector3 Up { get; }
        public Vector2 ApertureSize { get; }
        public Pose PortalPose { get; }
        public bool IsValid =>
            IsFinite(RoadCenter) &&
            IsFinite(TravelDirection) &&
            IsFinite(Right) &&
            IsFinite(Up) &&
            IsFinite(PortalPose.position) &&
            IsFinite(PortalPose.rotation) &&
            TravelDirection.sqrMagnitude > 0.99f &&
            Right.sqrMagnitude > 0.99f &&
            Up.sqrMagnitude > 0.99f &&
            ApertureSize.x > 0f &&
            ApertureSize.y > 0f;

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

    internal sealed class LifeSizeDriveByPlan
    {
        private readonly List<Vector3> centerline;

        public LifeSizeDriveByPlan(
            ShowcasePlaybackWindow timing,
            ShowcaseRoute route,
            LifeSizePortalSeam entrySeam,
            LifeSizePortalSeam exitSeam,
            float roadWidth,
            float roadThickness,
            int layoutRevision,
            int sourceRevision)
        {
            Timing = timing;
            Route = route;
            EntrySeam = entrySeam;
            ExitSeam = exitSeam;
            RoadWidth = roadWidth;
            RoadThickness = roadThickness;
            LayoutRevision = layoutRevision;
            SourceRevision = sourceRevision;
            centerline = route != null
                ? new List<Vector3>(route.Centerline)
                : new List<Vector3>();
        }

        public ShowcasePlaybackWindow Timing { get; }
        public ShowcaseRoute Route { get; }
        public LifeSizePortalSeam EntrySeam { get; }
        public LifeSizePortalSeam ExitSeam { get; }
        public float RoadWidth { get; }
        public float RoadThickness { get; }
        public int LayoutRevision { get; }
        public int SourceRevision { get; }
        public IReadOnlyList<Vector3> Centerline => centerline;
        public bool IsValid =>
            Timing.IsValid &&
            Route != null &&
            Route.IsValid &&
            EntrySeam.IsValid &&
            ExitSeam.IsValid &&
            RoadWidth > 0f &&
            RoadThickness > 0f &&
            LayoutRevision >= 0 &&
            SourceRevision >= 0 &&
            centerline.Count >= 5;

        public bool TryEvaluateVehiclePose(
            float sourceLongitudinal,
            out Pose pose)
        {
            pose = default;
            if (!IsValid ||
                !Route.TryEvaluate(
                    sourceLongitudinal,
                    out Vector3 position,
                    out Vector3 tangent) ||
                tangent.sqrMagnitude <= 0.000001f)
            {
                return false;
            }

            pose = new Pose(
                position,
                Quaternion.LookRotation(
                    tangent.normalized,
                    Vector3.up));
            return true;
        }
    }

    internal static class LifeSizeDriveByPlanner
    {
        private const float MinimumSegmentLength = 0.005f;

        public static bool TryPrepare(
            ShowcaseRun run,
            ShowcaseLayout layout,
            LifeSizeDriveBySettings settings,
            out LifeSizeDriveByPlan plan,
            out string failure)
        {
            plan = null;
            failure = "";
            if (!run.IsValid ||
                layout == null ||
                !layout.IsLayoutValid ||
                settings == null)
            {
                failure =
                    "The run, room layout, or LifeSize settings are unavailable.";
                return false;
            }

            settings.ClampValues();
            if (run.LayoutRevision != layout.LayoutRevision)
            {
                failure =
                    "The LifeSize candidate was prepared from a stale room layout.";
                return false;
            }

            if (!layout.TryGetEntryWallGeometry(
                    out Vector2 entryWallSize,
                    out Vector3 entryWallBottom,
                    out Vector3 entryWallUp) ||
                !layout.TryGetExitWallGeometry(
                    out Vector2 exitWallSize,
                    out Vector3 exitWallBottom,
                    out Vector3 exitWallUp))
            {
                failure =
                    "The selected Entry or Exit wall geometry is unavailable.";
                return false;
            }

            if (!TryCreateSeam(
                    run.Route.EntryPosition,
                    run.Route.EntryDirection,
                    entryWallSize,
                    entryWallBottom,
                    entryWallUp,
                    settings,
                    "Entry",
                    out LifeSizePortalSeam entrySeam,
                    out failure) ||
                !TryCreateSeam(
                    run.Route.ExitPosition,
                    run.Route.ExitDirection,
                    exitWallSize,
                    exitWallBottom,
                    exitWallUp,
                    settings,
                    "Exit",
                    out LifeSizePortalSeam exitSeam,
                    out failure))
            {
                return false;
            }

            LifeSizeDriveByPlan candidate = new(
                run.Timing,
                run.Route,
                entrySeam,
                exitSeam,
                settings.RoadWidth,
                settings.RoadThickness,
                run.LayoutRevision,
                run.SourceRevision);
            if (!TryValidate(candidate, settings, out failure))
                return false;

            plan = candidate;
            return true;
        }

        private static bool TryCreateSeam(
            Vector3 roadCenter,
            Vector3 travelDirection,
            Vector2 wallSize,
            Vector3 wallBottom,
            Vector3 wallUp,
            LifeSizeDriveBySettings settings,
            string label,
            out LifeSizePortalSeam seam,
            out string failure)
        {
            seam = default;
            failure = "";
            Vector3 up = wallUp.normalized;
            Vector3 travel = Vector3.ProjectOnPlane(
                travelDirection,
                up);
            if (up.sqrMagnitude <= 0.99f ||
                travel.sqrMagnitude <= 0.000001f)
            {
                failure =
                    $"The {label} wall has no stable portal frame.";
                return false;
            }

            travel.Normalize();
            Vector3 right = Vector3.Cross(up, travel).normalized;
            float usableWidth =
                wallSize.x - settings.WallMargin * 2f;
            float verticalFromBottom = Vector3.Dot(
                roadCenter - wallBottom,
                up);
            float usableHeightAboveRoad =
                wallSize.y -
                settings.WallMargin -
                Mathf.Max(0f, verticalFromBottom);
            float horizontalFromCenter = Vector3.Dot(
                roadCenter - wallBottom,
                right);
            float allowedHorizontal =
                wallSize.x * 0.5f -
                settings.WallMargin -
                settings.RoadWidth * 0.5f;

            if (usableWidth + 0.0001f < settings.RoadWidth)
            {
                failure =
                    $"The {label} wall is too narrow for the requested LifeSize road width.";
                return false;
            }

            if (usableHeightAboveRoad + 0.0001f <
                settings.PortalHeight)
            {
                failure =
                    $"The {label} wall is too short above the road seam.";
                return false;
            }

            if (verticalFromBottom < -settings.SeamPositionTolerance ||
                Mathf.Abs(horizontalFromCenter) >
                allowedHorizontal + settings.SeamPositionTolerance)
            {
                failure =
                    $"The {label} road seam does not fit inside the selected wall aperture.";
                return false;
            }

            seam = new LifeSizePortalSeam(
                roadCenter,
                travel,
                up,
                new Vector2(
                    settings.RoadWidth,
                    settings.PortalHeight));
            return seam.IsValid;
        }

        private static bool TryValidate(
            LifeSizeDriveByPlan plan,
            LifeSizeDriveBySettings settings,
            out string failure)
        {
            failure = "";
            if (plan == null || !plan.IsValid)
            {
                failure =
                    "The prepared LifeSize drive-by contract is invalid.";
                return false;
            }

            if (!TryValidateRouteSeam(
                    plan.Route,
                    plan.Route.SourceEntry,
                    plan.EntrySeam,
                    settings,
                    "Entry",
                    out failure) ||
                !TryValidateRouteSeam(
                    plan.Route,
                    plan.Route.SourceExit,
                    plan.ExitSeam,
                    settings,
                    "Exit",
                    out failure))
            {
                return false;
            }

            IReadOnlyList<Vector3> centerline = plan.Centerline;
            for (int i = 0; i < centerline.Count; i++)
            {
                Vector3 point = centerline[i];
                if (!IsFinite(point))
                {
                    failure =
                        "The LifeSize authoritative centerline contains a non-finite point.";
                    return false;
                }

                if (i > 0 &&
                    Vector3.Distance(centerline[i - 1], point) <
                    MinimumSegmentLength)
                {
                    failure =
                        "The LifeSize authoritative centerline contains a collapsed segment.";
                    return false;
                }
            }

            return true;
        }

        private static bool TryValidateRouteSeam(
            ShowcaseRoute route,
            float sourceLongitudinal,
            LifeSizePortalSeam seam,
            LifeSizeDriveBySettings settings,
            string label,
            out string failure)
        {
            failure = "";
            if (!route.TryEvaluate(
                    sourceLongitudinal,
                    out Vector3 position,
                    out Vector3 tangent))
            {
                failure =
                    $"The {label} route landmark cannot be evaluated.";
                return false;
            }

            float positionError = Vector3.Distance(
                position,
                seam.RoadCenter);
            float angleError = Vector3.Angle(
                tangent,
                seam.TravelDirection);
            if (positionError > settings.SeamPositionTolerance ||
                angleError > settings.SeamAngleTolerance)
            {
                failure =
                    $"The {label} route seam violates its position or tangent contract.";
                return false;
            }

            return true;
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) &&
                float.IsFinite(value.y) &&
                float.IsFinite(value.z);
        }
    }
}
