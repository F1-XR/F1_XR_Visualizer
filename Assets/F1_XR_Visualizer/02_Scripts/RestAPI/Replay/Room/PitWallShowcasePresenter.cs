using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace F1XR.RestAPI.Replay.Room
{
    [DefaultExecutionOrder(1050)]
    [DisallowMultipleComponent]
    public sealed class PitWallShowcasePresenter : MonoBehaviour
    {
        private const float TargetVehicleLengthMeters = 5.6f;
        private const float PitDepthBehindWallMeters = 6f;
        private const float VehicleGroundClearanceMeters = 0.04f;
        private const float FallbackVehicleOriginHeightMeters = 0.28f;
        private const float MaximumVehicleOriginHeightRatio = 0.22f;

        private readonly List<ShowcaseWallFrame> walls = new();
        private ReplayPlayer replayPlayer;
        private ShowcaseLayout showcaseLayout;
        private ShowcasePortalPresentation portalPresentation;
        private EventPopoutReplay eventReplay;
        private Transform boundStage;
        private int boundSourceRevision = -1;
        private int boundLayoutRevision = -1;
        private int wallSelectionOffset;
        private string lastFailure = "";

        public string LastFailure => lastFailure;

        public void Configure(
            ReplayPlayer player,
            ShowcaseLayout layout,
            ShowcasePortalPresentation portal)
        {
            replayPlayer = player;
            showcaseLayout = layout;
            portalPresentation = portal;
        }

        public void SelectNextPitWall()
        {
            wallSelectionOffset++;
            ReleaseBinding();
        }

        private void LateUpdate()
        {
            ResolveReferences();
            eventReplay = replayPlayer != null
                ? replayPlayer.EventReplay
                : null;
            if (eventReplay == null ||
                !eventReplay.IsPitStopActive)
            {
                ReleaseBinding();
                return;
            }

            Transform stage = eventReplay.PresentationRoot;
            if (stage == null)
            {
                ReleaseBinding();
                lastFailure = "Pit stop stage is unavailable.";
                return;
            }

            int layoutRevision = showcaseLayout != null
                ? showcaseLayout.LayoutRevision
                : -1;
            if (boundStage == stage &&
                boundSourceRevision ==
                    eventReplay.SourceGeometryRevision &&
                boundLayoutRevision == layoutRevision &&
                portalPresentation != null &&
                portalPresentation.IsPitStopConfigured)
            {
                return;
            }

            ReleaseBinding();
            TryBind(stage, layoutRevision);
        }

        private bool TryBind(
            Transform stage,
            int layoutRevision)
        {
            if (portalPresentation == null ||
                showcaseLayout == null ||
                !eventReplay.TryGetPitStopVehicle(
                    out Transform vehicle,
                    out _) ||
                !eventReplay.TryGetPitStopFocusLocalPosition(
                    out Vector3 localFocus) ||
                !eventReplay.TryGetPitStopVehicleLength(
                    out float localVehicleLength) ||
                !TrySelectWall(out ShowcaseWallFrame wall))
            {
                lastFailure =
                    "A pit vehicle, focus point, or suitable wall is unavailable.";
                stage.gameObject.SetActive(false);
                return false;
            }

            float targetScale =
                TargetVehicleLengthMeters /
                Mathf.Max(0.0001f, localVehicleLength);

            ShowcaseWallFrame portalWall = wall;
            Vector3 wallBottom =
                wall.Center +
                wall.VerticalAxis * wall.MinVertical;
            Vector3 up = wall.VerticalAxis.normalized;
            if (showcaseLayout.TryGetDetectedFloorPlane(
                    out Plane floorPlane) &&
                TryAlignWallToFloor(
                    wall,
                    floorPlane,
                    out ShowcaseWallFrame floorAlignedWall,
                    out Vector3 floorAtWall))
            {
                portalWall = floorAlignedWall;
                wallBottom = floorAtWall;
                up = floorPlane.normal.normalized;
            }

            Vector3 inward = Vector3.ProjectOnPlane(
                wall.InwardNormal,
                up).normalized;
            Vector3 laneDirection = Vector3.ProjectOnPlane(
                wall.HorizontalAxis,
                up).normalized;
            if (inward.sqrMagnitude <= 0.5f ||
                laneDirection.sqrMagnitude <= 0.5f)
            {
                lastFailure = "The selected pit wall frame is unstable.";
                stage.gameObject.SetActive(false);
                return false;
            }

            Quaternion rotation = Quaternion.LookRotation(
                laneDirection,
                up);
            float vehicleOriginHeight =
                ResolveVehicleOriginHeight(
                    stage,
                    vehicle,
                    targetScale);
            Vector3 desiredFocus =
                wallBottom +
                up * vehicleOriginHeight -
                inward * PitDepthBehindWallMeters;
            Vector3 position =
                desiredFocus -
                rotation * (localFocus * targetScale);
            if (!eventReplay.TryApplyRoomStagePlacement(
                    position,
                    rotation,
                    targetScale,
                    localFocus,
                    1.2f))
            {
                lastFailure = "The pit stage pose could not be applied.";
                stage.gameObject.SetActive(false);
                return false;
            }

            if (!portalPresentation.ConfigureSingleWall(
                    stage,
                    portalWall,
                    vehicle,
                    out string failure))
            {
                lastFailure = failure;
                eventReplay.TryRestoreTableRelativePose();
                stage.gameObject.SetActive(false);
                return false;
            }

            eventReplay.SuspendTableTrackRendering();
            stage.gameObject.SetActive(true);
            boundStage = stage;
            boundSourceRevision =
                eventReplay.SourceGeometryRevision;
            boundLayoutRevision = layoutRevision;
            lastFailure = "";
            return true;
        }

        private static bool TryAlignWallToFloor(
            ShowcaseWallFrame wall,
            Plane floorPlane,
            out ShowcaseWallFrame alignedWall,
            out Vector3 floorAtWall)
        {
            alignedWall = wall;
            floorAtWall =
                wall.Center +
                wall.VerticalAxis * wall.MinVertical;

            Vector3 wallUp = wall.VerticalAxis.normalized;
            float alignment = Vector3.Dot(
                floorPlane.normal,
                wallUp);
            if (Mathf.Abs(alignment) <= 0.5f)
                return false;

            float distance = floorPlane.GetDistanceToPoint(floorAtWall);
            floorAtWall -= wallUp * (distance / alignment);

            float floorVertical = Vector3.Dot(
                floorAtWall - wall.Center,
                wallUp);
            float alignedHeight = wall.MaxVertical - floorVertical;
            if (!float.IsFinite(alignedHeight) || alignedHeight <= 0.5f)
                return false;

            alignedWall = new ShowcaseWallFrame(
                wall.Id,
                wall.Center,
                wall.InwardNormal,
                wall.HorizontalAxis,
                wall.VerticalAxis,
                wall.Width,
                alignedHeight,
                wall.MinHorizontal,
                wall.MaxHorizontal,
                floorVertical,
                wall.MaxVertical);
            return true;
        }

        private static float ResolveVehicleOriginHeight(
            Transform stage,
            Transform vehicle,
            float targetScale)
        {
            if (stage == null ||
                vehicle == null ||
                !vehicle.TryGetComponent(
                    out ReplayCarView vehicleView) ||
                !vehicleView.TryGetVisualGroundOffset(
                    stage,
                    out float localGroundOffset))
            {
                return FallbackVehicleOriginHeightMeters;
            }

            float measuredHeight =
                -localGroundOffset * targetScale +
                VehicleGroundClearanceMeters;
            if (float.IsInfinity(measuredHeight) ||
                float.IsNaN(measuredHeight))
            {
                return FallbackVehicleOriginHeightMeters;
            }

            return Mathf.Clamp(
                measuredHeight,
                VehicleGroundClearanceMeters,
                TargetVehicleLengthMeters *
                MaximumVehicleOriginHeightRatio);
        }

        private bool TrySelectWall(out ShowcaseWallFrame selected)
        {
            selected = default;
            walls.Clear();
            showcaseLayout.CopyAvailableWallFrames(walls);
            if (walls.Count == 0 &&
                showcaseLayout.TryGetEntryPose(out Pose pose) &&
                showcaseLayout.TryGetEntryWallGeometry(
                    out Vector2 size,
                    out Vector3 bottom,
                    out Vector3 vertical))
            {
                Vector3 inward =
                    showcaseLayout.EntryTravelDirection.normalized;
                Vector3 horizontal = Vector3.Cross(
                    vertical,
                    inward).normalized;
                walls.Add(new ShowcaseWallFrame(
                    default(TrackableId),
                    bottom + vertical * size.y * 0.5f,
                    inward,
                    horizontal,
                    vertical,
                    size.x,
                    size.y,
                    -size.x * 0.5f,
                    size.x * 0.5f,
                    -size.y * 0.5f,
                    size.y * 0.5f));
            }

            if (walls.Count == 0)
                return false;

            Camera viewer = Camera.main;
            walls.Sort((left, right) =>
                ScoreWall(right, viewer)
                    .CompareTo(ScoreWall(left, viewer)));
            int index = Mathf.Abs(wallSelectionOffset) % walls.Count;
            selected = walls[index];
            return selected.IsValid;
        }

        private static float ScoreWall(
            ShowcaseWallFrame wall,
            Camera viewer)
        {
            float area = wall.Width * wall.Height;
            if (viewer == null)
                return area;

            Vector3 toViewer =
                viewer.transform.position - wall.Center;
            toViewer.y = 0f;
            if (toViewer.sqrMagnitude <= 0.0001f)
                return area;

            float facing = Mathf.Clamp01(Vector3.Dot(
                wall.InwardNormal.normalized,
                toViewer.normalized));
            return area * Mathf.Lerp(0.55f, 1f, facing);
        }

        private void ReleaseBinding()
        {
            bool ownsPortal =
                portalPresentation != null &&
                portalPresentation.IsPitStopConfigured;
            if (boundStage == null && !ownsPortal)
                return;

            if (ownsPortal)
                portalPresentation.Clear();
            eventReplay?.RestoreTableTrackRendering();
            if (boundStage != null)
                boundStage.gameObject.SetActive(false);
            boundStage = null;
            boundSourceRevision = -1;
            boundLayoutRevision = -1;
        }

        private void ResolveReferences()
        {
            if (replayPlayer == null)
            {
                replayPlayer = Object.FindAnyObjectByType<ReplayPlayer>(
                    FindObjectsInactive.Include);
            }
            if (showcaseLayout == null)
                showcaseLayout = GetComponent<ShowcaseLayout>();
            if (portalPresentation == null)
            {
                portalPresentation =
                    GetComponent<ShowcasePortalPresentation>();
            }
        }

        private void OnDisable()
        {
            ReleaseBinding();
        }

        private void OnDestroy()
        {
            ReleaseBinding();
        }
    }
}
