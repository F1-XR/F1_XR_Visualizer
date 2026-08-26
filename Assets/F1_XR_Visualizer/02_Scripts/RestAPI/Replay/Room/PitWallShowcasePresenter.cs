using System.Collections.Generic;
using F1XR.Interaction.Input;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace F1XR.RestAPI.Replay.Room
{
    [DefaultExecutionOrder(1050)]
    [DisallowMultipleComponent]
    public sealed class PitWallShowcasePresenter : MonoBehaviour
    {
        private const string FitinSceneName = "SessionSpace_fitin";
        private const float FitinWallDistanceMeters = 3f;
        private const float FitinWallWidthMeters = 4.2f;
        private const float FitinWallHeightMeters = 2.4f;
        private const float TargetVehicleLengthMeters = 5.6f;
        private const float PitDepthBehindWallMeters = 6f;
        private const float VehicleGroundClearanceMeters = 0.04f;
        private const float FallbackVehicleOriginHeightMeters = 0.28f;
        private const float MaximumVehicleOriginHeightRatio = 0.22f;
        private const float MaximumFloorSnapMeters = 0.2f;

        private readonly List<ShowcaseWallFrame> walls = new();
        private ReplayPlayer replayPlayer;
        private ShowcaseLayout showcaseLayout;
        private WallDiscovery wallDiscovery;
        private ShowcasePortalPresentation portalPresentation;
        private EventPopoutReplay eventReplay;
        private Transform boundStage;
        private int boundSourceRevision = -1;
        private int boundLayoutRevision = -1;
        private int wallSelectionOffset;
        private Color boundTeamColor = Color.red;
        private PitWallOverlayLabels boundLabels;
        private int suitabilityLayoutRevision = int.MinValue;
        private int suitabilityCandidateRevision = int.MinValue;
        private float nextSuitabilityCheckTime;
        private bool hasSuitablePitWall;
        private bool fitinWallLocked;
        private ShowcaseWallFrame fitinWall;
        private string lastFailure = "";
        private bool pitReplayViewButtonWasPressed;
        private bool pitReplayRestartButtonWasPressed;

        public string LastFailure => lastFailure;
        public bool IsPortalEditMode =>
            portalPresentation != null &&
            portalPresentation.IsPitWallEditMode;
        public bool CanUndoPortalEdit =>
            portalPresentation != null &&
            portalPresentation.CanUndoPitWallEdit;
        public bool IsPortalManipulating =>
            portalPresentation != null &&
            portalPresentation.IsPitWallManipulating;
        public bool CanEditPortal =>
            portalPresentation != null &&
            portalPresentation.IsPitStopConfigured;
        public PitReplayViewMode PitReplayViewMode =>
            portalPresentation != null
                ? portalPresentation.PitReplayViewMode
                : PitReplayViewMode.Immersive;
        public bool CanChangePitReplayView =>
            portalPresentation != null &&
            portalPresentation.CanChangePitReplayView;
        public bool HasSuitablePitWall
        {
            get
            {
                ResolveReferences();
                if (showcaseLayout == null)
                    return false;

                int revision = showcaseLayout.LayoutRevision;
                int candidateRevision = wallDiscovery != null
                    ? wallDiscovery.CandidateRevision
                    : -1;
                if (suitabilityLayoutRevision != revision ||
                    suitabilityCandidateRevision != candidateRevision ||
                    Time.unscaledTime >= nextSuitabilityCheckTime)
                {
                    suitabilityLayoutRevision = revision;
                    suitabilityCandidateRevision = candidateRevision;
                    nextSuitabilityCheckTime = Time.unscaledTime + 0.5f;
                    hasSuitablePitWall =
                        EvaluateSuitablePitWall();
                }
                return hasSuitablePitWall;
            }
        }

        public void Configure(
            ReplayPlayer player,
            ShowcaseLayout layout,
            ShowcasePortalPresentation portal)
        {
            replayPlayer = player;
            showcaseLayout = layout;
            portalPresentation = portal;
            suitabilityLayoutRevision = int.MinValue;
            suitabilityCandidateRevision = int.MinValue;
            nextSuitabilityCheckTime = 0f;
        }

        public void SelectNextPitWall()
        {
            wallSelectionOffset++;
            ReleaseBinding();
        }

        public bool TogglePortalEditMode()
        {
            ResolveReferences();
            return portalPresentation != null &&
                   portalPresentation.TogglePitWallEditMode();
        }

        public bool UndoPortalEdit()
        {
            return portalPresentation != null &&
                   portalPresentation.UndoPitWallEdit();
        }

        public bool ResetPortalEdit()
        {
            return portalPresentation != null &&
                   portalPresentation.ResetPitWallEdit();
        }

        public bool TogglePitReplayView()
        {
            ResolveReferences();
            return portalPresentation != null &&
                   portalPresentation.TogglePitReplayView();
        }

        private void LateUpdate()
        {
            ResolveReferences();
            eventReplay = replayPlayer != null
                ? replayPlayer.EventReplay
                : null;
            UpdatePitReplayViewShortcut();
            UpdatePitReplayRestartShortcut();
            if (eventReplay == null ||
                !eventReplay.IsPitStopActive)
            {
                fitinWallLocked = false;
                fitinWall = default;
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
                UpdateLivingPitWall();
                return;
            }

            ReleaseBinding();
            TryBind(stage, layoutRevision);
        }

        private void UpdatePitReplayViewShortcut()
        {
            ProcessPitReplayViewShortcut(
                XRControllerButton.IsPressed(
                    MorphHoldButton.PrimaryButton,
                    false));
        }

        private bool ProcessPitReplayViewShortcut(bool isPressed)
        {
            bool pressedThisFrame =
                isPressed && !pitReplayViewButtonWasPressed;
            pitReplayViewButtonWasPressed = isPressed;
            if (!pressedThisFrame ||
                eventReplay == null ||
                !eventReplay.IsPitStopActive ||
                portalPresentation == null ||
                !portalPresentation.CanChangePitReplayView)
            {
                return false;
            }

            return TogglePitReplayView();
        }

        private void UpdatePitReplayRestartShortcut()
        {
            ProcessPitReplayRestartShortcut(
                XRControllerButton.IsPressed(
                    MorphHoldButton.SecondaryButton,
                    false));
        }

        private bool ProcessPitReplayRestartShortcut(bool isPressed)
        {
            bool pressedThisFrame =
                isPressed && !pitReplayRestartButtonWasPressed;
            pitReplayRestartButtonWasPressed = isPressed;
            if (!pressedThisFrame ||
                eventReplay == null ||
                !eventReplay.IsPitStopActive)
            {
                return false;
            }

            eventReplay.Restart();
            return true;
        }

        private bool TryBind(
            Transform stage,
            int layoutRevision)
        {
            if (portalPresentation == null ||
                showcaseLayout == null ||
                !eventReplay.TryGetPitStopVehicle(
                    out Transform vehicle,
                    out int driverNumber) ||
                !eventReplay.TryGetPitStopFocusLocalPosition(
                    out Vector3 localFocus) ||
                !eventReplay.TryGetPitStopVehicleLength(
                    out float localVehicleLength) ||
                !TrySelectWall(out ShowcaseWallFrame wall))
            {
                lastFailure =
                    "A pit vehicle, focus point, or suitable wall is unavailable.";
                eventReplay?.TryRestoreTableRelativePose();
                eventReplay?.RestoreTableTrackRendering();
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
                eventReplay.TryRestoreTableRelativePose();
                eventReplay.RestoreTableTrackRendering();
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
                eventReplay.TryRestoreTableRelativePose();
                eventReplay.RestoreTableTrackRendering();
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
                eventReplay.RestoreTableTrackRendering();
                stage.gameObject.SetActive(false);
                return false;
            }

            eventReplay.SuspendTableTrackRendering();
            stage.gameObject.SetActive(true);
            boundStage = stage;
            boundSourceRevision =
                eventReplay.SourceGeometryRevision;
            boundLayoutRevision = layoutRevision;
            ResolvePitWallIdentity(driverNumber);
            lastFailure = "";
            UpdateLivingPitWall();
            return true;
        }

        private void ResolvePitWallIdentity(int driverNumber)
        {
            var info = replayPlayer != null
                ? replayPlayer.GetDriverInfo(driverNumber)
                : null;
            boundTeamColor = replayPlayer != null
                ? replayPlayer.GetDriverColor(driverNumber)
                : Color.red;
            string team = info != null &&
                          !string.IsNullOrWhiteSpace(info.teamName)
                ? info.teamName
                : "PIT TEAM";
            string driver = info != null &&
                            !string.IsNullOrWhiteSpace(info.nameAcronym)
                ? info.nameAcronym
                : replayPlayer != null
                    ? replayPlayer.GetDriverLabel(driverNumber)
                    : driverNumber.ToString();
            int lap = eventReplay != null &&
                      eventReplay.CurrentEvent != null
                ? eventReplay.CurrentEvent.lapNumber
                : 0;
            boundLabels = new PitWallOverlayLabels(
                team,
                driver,
                lap,
                eventReplay != null &&
                eventReplay.PitShowcaseAssets != null
                    ? eventReplay.PitShowcaseAssets.DisplayFont
                    : null);
        }

        private void UpdateLivingPitWall()
        {
            if (portalPresentation == null ||
                eventReplay == null ||
                !eventReplay.TryGetPitStopPresentationState(
                    out PitStopPresentationState state))
            {
                return;
            }

            portalPresentation.ApplyPitWallOverlay(
                state,
                boundTeamColor,
                boundLabels);
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
            if (!IsFinite(wallUp) ||
                !IsFinite(floorPlane.normal))
            {
                return false;
            }

            float alignment = Vector3.Dot(
                floorPlane.normal,
                wallUp);
            if (!float.IsFinite(alignment) ||
                Mathf.Abs(alignment) <= 0.5f)
                return false;

            float distance = floorPlane.GetDistanceToPoint(floorAtWall);
            float verticalCorrection = distance / alignment;
            if (!float.IsFinite(verticalCorrection) ||
                Mathf.Abs(verticalCorrection) > MaximumFloorSnapMeters)
            {
                return false;
            }

            floorAtWall -= wallUp * verticalCorrection;

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

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) &&
            float.IsFinite(value.y) &&
            float.IsFinite(value.z);

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
            if (IsFitinScene)
                return TryGetFitinWall(out selected);

            selected = default;
            walls.Clear();
            showcaseLayout.CopyAvailableWallFrames(walls);
            for (int i = walls.Count - 1; i >= 0; i--)
            {
                ShowcaseWallFrame wall = walls[i];
                if (PitWallLayoutPolicy.Resolve(
                            wall.Width,
                            wall.Height) ==
                    PitWallOverlayLayout.None)
                {
                    walls.RemoveAt(i);
                }
            }
            if (walls.Count == 0 &&
                TryCreateEntryWallFrame(out ShowcaseWallFrame entryWall) &&
                PitWallLayoutPolicy.Resolve(
                    entryWall.Width,
                    entryWall.Height) != PitWallOverlayLayout.None)
            {
                walls.Add(entryWall);
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

        private bool TryCreateEntryWallFrame(
            out ShowcaseWallFrame wall)
        {
            wall = default;
            if (showcaseLayout == null ||
                !showcaseLayout.TryGetEntryPose(out _) ||
                !showcaseLayout.TryGetEntryWallGeometry(
                    out Vector2 size,
                    out Vector3 bottom,
                    out Vector3 vertical))
            {
                return false;
            }

            vertical.Normalize();
            Vector3 inward =
                showcaseLayout.EntryTravelDirection.normalized;
            Vector3 horizontal = Vector3.Cross(
                vertical,
                inward).normalized;
            if (vertical.sqrMagnitude <= 0.5f ||
                inward.sqrMagnitude <= 0.5f ||
                horizontal.sqrMagnitude <= 0.5f)
            {
                return false;
            }

            wall = new ShowcaseWallFrame(
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
                size.y * 0.5f);
            return wall.IsValid;
        }

        private bool EvaluateSuitablePitWall()
        {
            if (IsFitinScene)
                return Camera.main != null;

            walls.Clear();
            showcaseLayout.CopyAvailableWallFrames(walls);
            for (int i = 0; i < walls.Count; i++)
            {
                if (PitWallLayoutPolicy.Resolve(
                            walls[i].Width,
                            walls[i].Height) !=
                    PitWallOverlayLayout.None)
                {
                    return true;
                }
            }

            return TryCreateEntryWallFrame(
                       out ShowcaseWallFrame entryWall) &&
                   PitWallLayoutPolicy.Resolve(
                       entryWall.Width,
                       entryWall.Height) !=
                   PitWallOverlayLayout.None;
        }

        private bool TryGetFitinWall(out ShowcaseWallFrame wall)
        {
            if (fitinWallLocked)
            {
                wall = fitinWall;
                return wall.IsValid;
            }

            Camera viewer = Camera.main;
            if (viewer == null)
            {
                wall = default;
                return false;
            }

            Vector3 forward = Vector3.ProjectOnPlane(
                viewer.transform.forward,
                Vector3.up);
            if (forward.sqrMagnitude <= 0.0001f)
                forward = Vector3.forward;
            forward.Normalize();

            Vector3 inward = -forward;
            Vector3 horizontal = Vector3.Cross(
                Vector3.up,
                inward).normalized;
            Vector3 center = viewer.transform.position +
                forward * FitinWallDistanceMeters;
            fitinWall = new ShowcaseWallFrame(
                default,
                center,
                inward,
                horizontal,
                Vector3.up,
                FitinWallWidthMeters,
                FitinWallHeightMeters,
                -FitinWallWidthMeters * 0.5f,
                FitinWallWidthMeters * 0.5f,
                -FitinWallHeightMeters * 0.5f,
                FitinWallHeightMeters * 0.5f);
            fitinWallLocked = fitinWall.IsValid;
            wall = fitinWall;
            return fitinWallLocked;
        }

        private bool IsFitinScene =>
            gameObject.scene.name == FitinSceneName;

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
            boundTeamColor = Color.red;
            boundLabels = default;
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
            if (wallDiscovery == null)
                wallDiscovery = GetComponent<WallDiscovery>();
            if (portalPresentation == null)
            {
                portalPresentation =
                    GetComponent<ShowcasePortalPresentation>();
            }
        }

        private void OnEnable()
        {
            pitReplayViewButtonWasPressed =
                XRControllerButton.IsPressed(
                    MorphHoldButton.PrimaryButton,
                    false);
            pitReplayRestartButtonWasPressed =
                XRControllerButton.IsPressed(
                    MorphHoldButton.SecondaryButton,
                    false);
        }

        private void OnDisable()
        {
            pitReplayViewButtonWasPressed = false;
            pitReplayRestartButtonWasPressed = false;
            ReleaseBinding();
        }

        private void OnDestroy()
        {
            pitReplayViewButtonWasPressed = false;
            pitReplayRestartButtonWasPressed = false;
            ReleaseBinding();
        }
    }
}
