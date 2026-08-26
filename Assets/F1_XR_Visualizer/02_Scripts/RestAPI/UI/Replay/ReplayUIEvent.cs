using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.UI;
using F1XR.RestAPI.Api;
using F1XR.RestAPI.Replay;
using F1XR.RestAPI.Replay.Room;
using F1XR.UI.WorldPanel;

namespace F1XR.RestAPI.UI
{
    public partial class ReplayUI
    {
        private RectTransform eventControls;
        private TMP_Text eventStatus;
        private Button eventOpenButton;
        private Button eventCollisionButton;
        private Button eventOpenPitButton;
        private Button eventPlayButton;
        private Button eventRestartButton;
        private Button eventNextButton;
        private Button eventCloseButton;
        private Button eventPitWallButton;
        private Button eventPitEditButton;
        private Button eventPitUndoButton;
        private Button eventPitResetButton;
        private Button eventPitViewButton;
        private Slider eventSlider;
        private bool refreshingEventSlider;
        private RoomShowcaseSetupController roomSetup;
        private PitWallShowcasePresenter pitWallPresenter;
        private AutoReplayStarter replayStarter;
        private Canvas pitPortalControlsCanvas;
        private XRGrabInteractable pitPortalControlsGrab;
        private bool pitPortalControlsManuallyPlaced;
        private bool collisionDatasetLoading;
        private Coroutine collisionOpenWhenPreparedRoutine;
        private string eventLoadMessage;

        private const string PitPortalSurfaceName =
            "PitStopPortalSurface";
        private const float PitPortalControlsScale = 0.0017f;
        private const float PitPortalControlsBottomOffset = 0.42f;

        private void EnsureEventControls()
        {
            if (eventControls != null)
                return;

            eventControls = new GameObject(
                "EventReplayControls",
                typeof(RectTransform),
                typeof(Image))
                .GetComponent<RectTransform>();
            eventControls.SetParent(transform, false);
            eventControls.anchorMin = new Vector2(0.5f, 1f);
            eventControls.anchorMax = new Vector2(0.5f, 1f);
            eventControls.pivot = new Vector2(0.5f, 1f);
            eventControls.anchoredPosition = new Vector2(0f, 372f);
            eventControls.sizeDelta = new Vector2(300f, 142f);
            eventControls.GetComponent<Image>().color = new Color(0.015f, 0.018f, 0.026f, 0.92f);

            eventStatus = CreateText(
                "Status",
                eventControls,
                14,
                FontStyles.Bold,
                TextAlignmentOptions.Center);
            eventStatus.richText = true;
            SetRect(eventStatus.rectTransform, 8f, -8f, 284f, 26f);

            eventOpenButton = CreateEventButton(
                "Overtake",
                8f,
                -42f,
                136f,
                OpenTestEvent);
            eventCollisionButton = CreateEventButton(
                "Collision",
                156f,
                -42f,
                136f,
                OpenCollisionEvent);
            eventOpenPitButton = CreateEventButton(
                "Pit Stop",
                8f,
                -84f,
                284f,
                OpenPitStop);
            eventPlayButton = CreateEventButton("Play", 8f, -42f, 86f, ToggleEventPlay);
            eventRestartButton = CreateEventButton("Restart", 104f, -42f, 86f, RestartEvent);
            eventCloseButton = CreateEventButton("Close", 200f, -42f, 92f, CloseEvent);
            eventNextButton = CreateEventButton(
                "Next Overtake",
                8f,
                -84f,
                284f,
                OpenNextEvent);
            eventPitWallButton = CreateEventButton(
                "Change Wall",
                200f,
                -84f,
                92f,
                SelectNextPitWall);
            eventPitEditButton = CreateEventButton(
                "Edit Pit",
                8f,
                -126f,
                86f,
                TogglePitPortalEdit);
            eventPitUndoButton = CreateEventButton(
                "Undo",
                104f,
                -126f,
                86f,
                UndoPitPortalEdit);
            eventPitResetButton = CreateEventButton(
                "Reset",
                200f,
                -126f,
                92f,
                ResetPitPortalEdit);
            eventPitViewButton = CreateEventButton(
                "VIEW: IMMERSIVE",
                8f,
                -168f,
                284f,
                TogglePitReplayView);
            eventSlider = CreateEventSlider();
            eventSlider.onValueChanged.AddListener(SeekEvent);
            RefreshEventControls();
        }

        private Button CreateEventButton(
            string name,
            float x,
            float y,
            float width,
            UnityEngine.Events.UnityAction action)
        {
            Button button = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Image),
                typeof(Button))
                .GetComponent<Button>();
            button.transform.SetParent(eventControls, false);
            SetRect(button.GetComponent<RectTransform>(), x, y, width, 36f);
            button.targetGraphic = button.GetComponent<Image>();
            button.onClick.AddListener(action);

            TMP_Text label = CreateText(
                "Label",
                button.transform,
                14,
                FontStyles.Bold,
                TextAlignmentOptions.Center);
            label.text = name;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
            StyleButton(button);
            return button;
        }

        private Slider CreateEventSlider()
        {
            Slider slider = new GameObject(
                "EventScrub",
                typeof(RectTransform),
                typeof(Slider))
                .GetComponent<Slider>();
            slider.transform.SetParent(eventControls, false);
            SetRect(slider.GetComponent<RectTransform>(), 12f, -134f, 276f, 30f);
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.direction = Slider.Direction.LeftToRight;

            Image background = CreateSliderImage(
                "Background",
                slider.transform,
                new Color(0.11f, 0.12f, 0.15f, 1f));
            FillRect(background.rectTransform, 0f, 0.35f, 1f, 0.65f);

            RectTransform fillArea = new GameObject("FillArea", typeof(RectTransform)).GetComponent<RectTransform>();
            fillArea.SetParent(slider.transform, false);
            FillRect(fillArea, 0f, 0.35f, 1f, 0.65f);
            fillArea.offsetMin = new Vector2(6f, 0f);
            fillArea.offsetMax = new Vector2(-6f, 0f);

            Image fill = CreateSliderImage(
                "Fill",
                fillArea,
                new Color(0.95f, 0.08f, 0.08f, 1f));
            FillRect(fill.rectTransform, 0f, 0f, 1f, 1f);

            RectTransform handleArea = new GameObject("HandleArea", typeof(RectTransform)).GetComponent<RectTransform>();
            handleArea.SetParent(slider.transform, false);
            FillRect(handleArea, 0f, 0f, 1f, 1f);
            handleArea.offsetMin = new Vector2(6f, 0f);
            handleArea.offsetMax = new Vector2(-6f, 0f);

            Image handle = CreateSliderImage("Handle", handleArea, Color.white);
            RectTransform handleRect = handle.rectTransform;
            handleRect.anchorMin = new Vector2(0f, 0.5f);
            handleRect.anchorMax = new Vector2(0f, 0.5f);
            handleRect.pivot = new Vector2(0.5f, 0.5f);
            handleRect.sizeDelta = new Vector2(14f, 24f);

            slider.fillRect = fill.rectTransform;
            slider.handleRect = handleRect;
            slider.targetGraphic = handle;
            return slider;
        }

        private static Image CreateSliderImage(
            string name,
            Transform parent,
            Color color)
        {
            Image image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.color = color;
            return image;
        }

        private static void FillRect(
            RectTransform rect,
            float minX,
            float minY,
            float maxX,
            float maxY)
        {
            rect.anchorMin = new Vector2(minX, minY);
            rect.anchorMax = new Vector2(maxX, maxY);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private void UpdateEventControlsPlacement(bool pitActive)
        {
            if (!pitActive)
            {
                ReleasePitPortalEventControls();
                return;
            }

            GameObject portalSurface =
                GameObject.Find(PitPortalSurfaceName);
            if (portalSurface == null)
            {
                ReleasePitPortalEventControls();
                return;
            }

            MeshFilter portalFilter =
                portalSurface.GetComponent<MeshFilter>();
            if (portalFilter == null ||
                portalFilter.sharedMesh == null)
            {
                ReleasePitPortalEventControls();
                return;
            }

            EnsurePitPortalControlsCanvas();
            if (pitPortalControlsCanvas == null)
                return;

            RectTransform canvasRect =
                pitPortalControlsCanvas.GetComponent<RectTransform>();
            canvasRect.localScale =
                Vector3.one * PitPortalControlsScale;
            canvasRect.sizeDelta = eventControls.sizeDelta;

            Bounds portalBounds = portalFilter.sharedMesh.bounds;
            float panelHalfHeight =
                eventControls.sizeDelta.y *
                PitPortalControlsScale * 0.5f;
            float verticalOffset = Mathf.Max(
                PitPortalControlsBottomOffset,
                panelHalfHeight + 0.08f);
            Vector3 localPosition = new Vector3(
                0f,
                portalBounds.min.y + verticalOffset,
                -0.045f);
            Camera viewCamera = Camera.main;
            if (!pitPortalControlsManuallyPlaced)
            {
                canvasRect.position =
                    portalSurface.transform.TransformPoint(localPosition);

                if (viewCamera != null)
                {
                    Vector3 awayFromViewer =
                        canvasRect.position -
                        viewCamera.transform.position;
                    if (awayFromViewer.sqrMagnitude > 0.0001f)
                    {
                        canvasRect.rotation = Quaternion.LookRotation(
                            awayFromViewer.normalized,
                            portalSurface.transform.up);
                    }
                }
                else
                {
                    canvasRect.rotation = portalSurface.transform.rotation;
                }
            }

            if (viewCamera != null)
                pitPortalControlsCanvas.worldCamera = viewCamera;

            if (eventControls.parent == canvasRect)
                return;

            eventControls.SetParent(canvasRect, false);
            eventControls.anchorMin = new Vector2(0.5f, 0.5f);
            eventControls.anchorMax = new Vector2(0.5f, 0.5f);
            eventControls.pivot = new Vector2(0.5f, 0.5f);
            eventControls.anchoredPosition = Vector2.zero;
            eventControls.localRotation = Quaternion.identity;
            eventControls.localScale = Vector3.one;
        }

        private void LateUpdate()
        {
            if (eventControls == null ||
                player == null ||
                pitWallPresenter == null ||
                !pitWallPresenter.IsPortalEditMode)
            {
                return;
            }

            EventPopoutReplay eventReplay = player.EventReplay;
            if (eventReplay != null &&
                eventReplay.IsActive &&
                eventReplay.IsPitStopActive)
            {
                UpdateEventControlsPlacement(true);
            }
        }

        private void EnsurePitPortalControlsCanvas()
        {
            if (pitPortalControlsCanvas != null)
                return;

            GameObject root = new GameObject(
                "PitPortalEventControls",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(GraphicRaycaster),
                typeof(TrackedDeviceGraphicRaycaster));
            pitPortalControlsCanvas = root.GetComponent<Canvas>();
            pitPortalControlsCanvas.renderMode =
                RenderMode.WorldSpace;
            pitPortalControlsCanvas.overrideSorting = true;
            pitPortalControlsCanvas.sortingOrder = 500;

            RectTransform canvasRect =
                pitPortalControlsCanvas.GetComponent<RectTransform>();
            canvasRect.sizeDelta = eventControls.sizeDelta;

            Rigidbody body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.constraints = RigidbodyConstraints.FreezeRotation;

            BoxCollider panelCollider = root.AddComponent<BoxCollider>();
            panelCollider.center = Vector3.zero;
            panelCollider.size = new Vector3(
                eventControls.sizeDelta.x,
                eventControls.sizeDelta.y,
                12f);

            pitPortalControlsGrab =
                root.AddComponent<XRGrabInteractable>();
            pitPortalControlsGrab.colliders.Clear();
            pitPortalControlsGrab.colliders.Add(panelCollider);
            pitPortalControlsGrab.useDynamicAttach = true;
            pitPortalControlsGrab.matchAttachPosition = true;
            pitPortalControlsGrab.matchAttachRotation = false;
            pitPortalControlsGrab.trackRotation = false;
            pitPortalControlsGrab.snapToColliderVolume = true;
            pitPortalControlsGrab.attachEaseInTime = 0.12f;
            pitPortalControlsGrab.throwOnDetach = false;
            pitPortalControlsGrab.selectEntered.AddListener(
                _ => pitPortalControlsManuallyPlaced = true);

            PanelEdgeGrab edgeGrab =
                root.AddComponent<PanelEdgeGrab>();
            edgeGrab.Configure(
                pitPortalControlsGrab,
                panelCollider,
                canvasRect,
                true,
                true);
        }

        private void ReleasePitPortalEventControls()
        {
            if (eventControls != null &&
                eventControls.parent != transform)
            {
                eventControls.SetParent(transform, false);
                eventControls.anchorMin = new Vector2(0.5f, 1f);
                eventControls.anchorMax = new Vector2(0.5f, 1f);
                eventControls.pivot = new Vector2(0.5f, 1f);
                eventControls.anchoredPosition =
                    new Vector2(0f, 372f);
                eventControls.localRotation = Quaternion.identity;
                eventControls.localScale = Vector3.one;
            }

            if (pitPortalControlsCanvas != null)
                Destroy(pitPortalControlsCanvas.gameObject);

            pitPortalControlsCanvas = null;
            pitPortalControlsGrab = null;
            pitPortalControlsManuallyPlaced = false;
        }

        private void RefreshEventControls()
        {
            if (eventControls == null || player == null)
                return;

            ResolveRoomSetup();
            EventPopoutReplay eventReplay = player.EventReplay;
            bool eventLoading =
                eventReplay != null && eventReplay.IsLoading;
            bool loading = eventLoading || collisionDatasetLoading;
            bool active = eventReplay != null && eventReplay.IsActive;
            bool roomReady = roomSetup == null || roomSetup.IsSetupReady;
            bool collisionActive = active &&
                eventReplay != null &&
                eventReplay.IsCurrentCollision;
            bool collisionRevealComplete = collisionActive &&
                eventReplay.IsCollisionRevealComplete;
            bool collisionTimeLensGrabbed = collisionActive &&
                eventReplay.IsCollisionTimeLensGrabbed;
            bool pitActive = active && eventReplay.IsPitStopActive;
            bool pitEditing = pitActive &&
                pitWallPresenter != null &&
                pitWallPresenter.IsPortalEditMode;
            bool pitManipulating = pitEditing &&
                pitWallPresenter.IsPortalManipulating;

            eventControls.gameObject.SetActive(
                !pitActive || !eventReplay.IsPlaying);

            eventOpenButton.gameObject.SetActive(!active && !loading);
            eventCollisionButton.gameObject.SetActive(
                !active && !loading);
            eventOpenPitButton.gameObject.SetActive(!active && !loading);
            eventPlayButton.gameObject.SetActive(
                active &&
                (!collisionActive || collisionRevealComplete));
            eventRestartButton.gameObject.SetActive(
                active &&
                (!collisionActive || collisionRevealComplete));
            eventNextButton.gameObject.SetActive(
                active && !collisionActive);
            eventPitWallButton.gameObject.SetActive(pitActive);
            eventPitEditButton.gameObject.SetActive(pitActive);
            eventPitUndoButton.gameObject.SetActive(pitActive);
            eventPitResetButton.gameObject.SetActive(pitActive);
            eventPitViewButton.gameObject.SetActive(pitActive);
            eventCloseButton.gameObject.SetActive(active || loading);
            eventSlider.gameObject.SetActive(
                active && !collisionActive);
            eventControls.sizeDelta = new Vector2(
                300f,
                pitActive ? 268f : 184f);
            SetRect(
                eventSlider.GetComponent<RectTransform>(),
                12f,
                pitActive ? -218f : -134f,
                276f,
                30f);
            UpdateEventControlsPlacement(pitActive);

            if (loading)
            {
                eventStatus.text = collisionDatasetLoading
                    ? "LOADING 2024 SUZUKA COLLISION"
                    : "LOADING EVENT REPLAY";
                return;
            }

            if (!active)
            {
                bool hasPitStop =
                    eventReplay != null &&
                    eventReplay.HasPitStop;
                bool hasCollision =
                    eventReplay != null &&
                    eventReplay.HasCollision;
                string collisionFailure = hasCollision
                    ? eventReplay.CollisionPreparationFailure
                    : null;
                if (hasCollision &&
                    string.IsNullOrWhiteSpace(collisionFailure))
                    eventReplay.PrepareTestCollision();
                bool pitWallReady =
                    (roomSetup == null ||
                     roomSetup.HasPitWallCandidate) &&
                    (pitWallPresenter == null ||
                     pitWallPresenter.HasSuitablePitWall);
                eventStatus.text = !string.IsNullOrWhiteSpace(
                        eventLoadMessage)
                    ? eventLoadMessage
                    : !string.IsNullOrWhiteSpace(collisionFailure)
                        ? $"COLLISION FAILED  /  {collisionFailure}"
                    : hasCollision &&
                      eventReplay.IsCollisionPrepared
                        ? "COLLISION READY"
                    : hasCollision &&
                      eventReplay.IsCollisionPreloading
                        ? "PREPARING COLLISION"
                    : !roomReady
                        ? "ROOM SETUP REQUIRED"
                        : player.HasDataset
                            ? "SHOWCASE EVENT"
                            : "EVENT DATA NOT READY";
                eventOpenButton.interactable =
                    player.HasDataset &&
                    roomReady &&
                    eventReplay != null &&
                    eventReplay.HasOvertake;
                SetButton(
                    eventCollisionButton,
                    hasCollision
                        ? !string.IsNullOrWhiteSpace(collisionFailure)
                            ? "Retry Collision"
                        : !eventReplay.IsCollisionPrepared
                            ? "Preparing Collision"
                            : "Collision"
                        : "Load Collision Test",
                    hasCollision
                        ? eventReplay.IsCollisionPrepared ||
                          !string.IsNullOrWhiteSpace(collisionFailure)
                        : replayStarter != null);
                SetButton(
                    eventOpenPitButton,
                    hasPitStop
                        ? "Open Pit Stop"
                        : "No Pit Stops In Loaded Range",
                    player.HasDataset &&
                    hasPitStop &&
                    pitWallReady);
                return;
            }

            string title = FormatEventTitle(
                eventReplay.CurrentEvent);
            eventStatus.text = pitEditing
                ? "EDIT PIT  /  GRAB TOP  /  TWO-HAND"
                : pitActive &&
                               pitWallPresenter != null &&
                               !string.IsNullOrWhiteSpace(
                                   pitWallPresenter.LastFailure)
                ? $"PIT WALL UNAVAILABLE  /  " +
                  pitWallPresenter.LastFailure
                : collisionActive
                    ? FormatCollisionStatus(eventReplay)
                    : FormatActiveEventStatus(
                        eventReplay,
                        title);
            SetButton(
                eventPitEditButton,
                pitEditing ? "Done" : "Edit Pit",
                pitWallPresenter != null &&
                pitWallPresenter.CanEditPortal);
            SetButton(
                eventPitUndoButton,
                "Undo",
                pitEditing &&
                !pitManipulating &&
                pitWallPresenter != null &&
                pitWallPresenter.CanUndoPortalEdit);
            SetButton(
                eventPitResetButton,
                "Reset",
                pitEditing && !pitManipulating);
            SetButton(
                eventPitWallButton,
                "Change Wall",
                pitActive && !pitEditing);
            SetButton(
                eventPitViewButton,
                pitWallPresenter != null
                    ? pitWallPresenter.PitReplayViewMode switch
                    {
                        PitReplayViewMode.Overhead =>
                            "VIEW: OVERHEAD",
                        PitReplayViewMode.TopDown =>
                            "VIEW: TOPDOWN",
                        _ => "VIEW: IMMERSIVE"
                    }
                    : "VIEW: IMMERSIVE",
                pitActive &&
                !pitEditing &&
                pitWallPresenter != null &&
                pitWallPresenter.CanChangePitReplayView);
            SetButton(
                eventPlayButton,
                collisionActive
                    ? "Replay Impact"
                    : eventReplay.IsPlaying
                        ? "Pause"
                        : "Play",
                !collisionActive ||
                (!eventReplay.IsCollisionImpactReplaying &&
                 !collisionTimeLensGrabbed));
            SetButton(
                eventRestartButton,
                collisionActive
                    ? "Restart Reveal"
                    : "Restart",
                !collisionActive ||
                (!eventReplay.IsCollisionImpactReplaying &&
                 !collisionTimeLensGrabbed));
            bool hasNext = pitActive
                ? eventReplay.HasNextPitStop
                : collisionActive
                    ? eventReplay.HasNextCollision
                    : eventReplay.HasNextOvertake;
            string eventName = pitActive
                ? "Pit Stop"
                : collisionActive
                    ? "Collision"
                    : "Overtake";
            string noMoreLabel = pitActive
                ? "No More Pit Stops"
                : collisionActive
                    ? "No More Collisions"
                    : "No More Overtakes";
            SetRect(
                eventNextButton.GetComponent<RectTransform>(),
                8f,
                -84f,
                pitActive ? 182f : 284f,
                36f);
            bool pitResultVisible = pitActive &&
                eventReplay.TryGetPitStopPresentationState(
                    out PitStopPresentationState pitState) &&
                pitState.Phase == PitStopPhase.Exit;
            SetButton(
                eventNextButton,
                hasNext
                    ? pitResultVisible
                        ? "Watch Next Pit Stop"
                        : $"Next {eventName}"
                    : noMoreLabel,
                hasNext && !pitEditing);
            ApplyPitNextButtonStyle(
                pitResultVisible,
                eventReplay.CurrentEvent);

            refreshingEventSlider = true;
            eventSlider.SetValueWithoutNotify(eventReplay.NormalizedTime);
            refreshingEventSlider = false;
        }

        private static string FormatCollisionStatus(
            EventPopoutReplay eventReplay)
        {
            return eventReplay.CollisionPhase switch
            {
                CollisionPresentationPhase.IslandReveal =>
                    "INCIDENT STAGE  /  INITIALIZING",
                CollisionPresentationPhase.PreImpact =>
                    "OBSERVED PATH  /  PRE-IMPACT",
                CollisionPresentationPhase.Impact =>
                    "CONTACT  /  IMPACT",
                CollisionPresentationPhase.PostImpact =>
                    "AFTERMATH  /  EVIDENCE",
                CollisionPresentationPhase.ForensicHold =>
                    FormatCollisionForensicHoldStatus(eventReplay),
                CollisionPresentationPhase.ImpactReplay =>
                    "REPLAYING IMPACT",
                _ => "PREPARING COLLISION"
            };
        }

        private static string FormatCollisionForensicHoldStatus(
            EventPopoutReplay eventReplay)
        {
            string status = eventReplay.CollisionTimeLensStatus;
            if (!eventReplay.IsCollisionTimeLensAvailable)
            {
                return string.IsNullOrWhiteSpace(status)
                    ? "INCIDENT  /  FORENSIC HOLD"
                    : $"INCIDENT  /  {status.Trim()}";
            }

            string lensLabel = eventReplay.IsCollisionTimeLensGrabbed
                ? "LENS GRAB"
                : "TIME LENS";
            string detail = string.IsNullOrWhiteSpace(status)
                ? "FORENSIC HOLD"
                : status.Trim();
            return $"{lensLabel}  /  {detail}";
        }

        private string FormatEventTitle(
            ReplayEventDto replayEvent)
        {
            string fallbackTitle = "Overtake Event";
            if (replayEvent != null &&
                string.Equals(
                    replayEvent.eventType,
                    "PitStop",
                    System.StringComparison.OrdinalIgnoreCase))
            {
                fallbackTitle = "Pit Stop Event";
            }
            else if (replayEvent != null &&
                     (string.Equals(
                          replayEvent.eventType,
                          "Collision",
                          System.StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(
                          replayEvent.eventType,
                          "Contact",
                          System.StringComparison.OrdinalIgnoreCase)))
            {
                fallbackTitle = "Collision Event";
            }

            string title = replayEvent != null &&
                           !string.IsNullOrWhiteSpace(
                               replayEvent.displayTitle)
                ? replayEvent.displayTitle
                : fallbackTitle;
            int[] drivers = replayEvent != null
                ? replayEvent.driverNumbers
                : null;
            if (drivers == null || drivers.Length == 0)
                return title;

            bool colored = false;
            int count = Mathf.Min(2, drivers.Length);
            for (int i = 0; i < count; i++)
            {
                int driver = drivers[i];
                string color = ColorUtility.ToHtmlStringRGB(
                    player.GetDriverColor(driver));
                var info = player.GetDriverInfo(driver);
                if (info != null &&
                    !string.IsNullOrWhiteSpace(info.fullName))
                {
                    title = ColorizeTitleToken(
                        title,
                        info.fullName,
                        color,
                        ref colored);
                }

                title = ColorizeTitleToken(
                    title,
                    player.GetDriverLabel(driver),
                    color,
                    ref colored);
            }

            if (colored || count < 2)
                return title;

            return $"{ColorizeDriverLabel(drivers[0])}  VS  " +
                   ColorizeDriverLabel(drivers[1]);
        }

        private string FormatActiveEventStatus(
            EventPopoutReplay eventReplay,
            string title)
        {
            if (eventReplay == null)
                return title;
            if (!eventReplay.IsPitStopActive)
            {
                return $"{title}  {FormatTime(eventReplay.CurrentTime)}";
            }

            ReplayEventDto replayEvent = eventReplay.CurrentEvent;
            if (replayEvent == null)
                return title;

            int driver = replayEvent.driverNumbers != null &&
                         replayEvent.driverNumbers.Length > 0
                ? replayEvent.driverNumbers[0]
                : 0;
            DriverInfoDto info = player.GetDriverInfo(driver);
            string team = info != null &&
                          !string.IsNullOrWhiteSpace(info.teamName)
                ? info.teamName
                : "TEAM";
            string timing;
            if (eventReplay.TryGetPitStopPresentationState(
                    out PitStopPresentationState state))
            {
                string prefix = state.IsReconstructed ? "~" : "";
                timing = state.IsDriveThrough
                    ? state.Phase == PitStopPhase.Exit
                        ? "DRIVE THROUGH COMPLETE"
                        : "DRIVE THROUGH"
                    : state.Phase == PitStopPhase.Service
                        ? $"SERVICE {state.ServiceElapsedSeconds:0.000} s"
                    : state.Phase == PitStopPhase.Exit
                        ? $"PIT STOP COMPLETE  {prefix}" +
                          $"{state.ServiceTotalSeconds:0.000} s"
                    : state.IsReconstructed
                        ? $"{state.Phase.ToString().ToUpperInvariant()}  " +
                          "RECONSTRUCTED"
                        : state.Phase.ToString().ToUpperInvariant();
            }
            else
            {
                timing = eventReplay.CurrentPitStopPhase
                    .ToString()
                    .ToUpperInvariant();
            }
            return $"{title} | {team} | L{replayEvent.lapNumber} | " +
                   $"{timing}  {FormatTime(eventReplay.CurrentTime)}";
        }

        private void ApplyPitNextButtonStyle(
            bool resultVisible,
            ReplayEventDto replayEvent)
        {
            if (eventNextButton == null)
                return;

            StyleButton(eventNextButton);
            if (!resultVisible)
                return;

            int driver = replayEvent != null &&
                         replayEvent.driverNumbers != null &&
                         replayEvent.driverNumbers.Length > 0
                ? replayEvent.driverNumbers[0]
                : 0;
            Color team = player != null
                ? player.GetDriverColor(driver)
                : new Color(0.9f, 0.08f, 0.08f, 1f);
            Color normal = Color.Lerp(team, Color.black, 0.28f);
            if (eventNextButton.targetGraphic is Image image)
                image.color = Color.white;
            ColorBlock colors = eventNextButton.colors;
            colors.normalColor = normal;
            colors.highlightedColor =
                Color.Lerp(team, Color.white, 0.18f);
            colors.selectedColor = colors.highlightedColor;
            colors.pressedColor =
                Color.Lerp(team, Color.black, 0.12f);
            eventNextButton.colors = colors;
        }

        private string ColorizeDriverLabel(int driver)
        {
            string color = ColorUtility.ToHtmlStringRGB(
                player.GetDriverColor(driver));
            return $"<color=#{color}>" +
                   $"{player.GetDriverLabel(driver)}</color>";
        }

        private static string ColorizeTitleToken(
            string title,
            string token,
            string color,
            ref bool colored)
        {
            if (string.IsNullOrWhiteSpace(title) ||
                string.IsNullOrWhiteSpace(token))
            {
                return title;
            }

            int index = title.IndexOf(
                token,
                System.StringComparison.OrdinalIgnoreCase);
            while (index >= 0)
            {
                string visibleText = title.Substring(
                    index,
                    token.Length);
                string replacement =
                    $"<color=#{color}>{visibleText}</color>";
                title = title.Substring(0, index) +
                        replacement +
                        title.Substring(index + token.Length);
                colored = true;
                index = title.IndexOf(
                    token,
                    index + replacement.Length,
                    System.StringComparison.OrdinalIgnoreCase);
            }

            return title;
        }

        private void OpenTestEvent()
        {
            ResolveRoomSetup();
            if (roomSetup != null && !roomSetup.IsSetupReady)
            {
                roomSetup.NotifyOpenBlocked();
                RefreshEventControls();
                return;
            }

            player?.EventReplay?.OpenTestOvertake();
            RefreshEventControls();
        }

        private void OpenCollisionEvent()
        {
            EventPopoutReplay eventReplay =
                player != null ? player.EventReplay : null;
            if (eventReplay != null && eventReplay.HasCollision)
            {
                eventLoadMessage = null;
                eventReplay.OpenTestCollision();
                RefreshEventControls();
                return;
            }

            if (replayStarter == null || collisionDatasetLoading)
            {
                eventLoadMessage =
                    "COLLISION TEST LOADER UNAVAILABLE";
                RefreshEventControls();
                return;
            }

            collisionDatasetLoading = true;
            eventLoadMessage = null;
            eventReplay?.Close();
            replayStarter.ReloadEventReplayTestSession(success =>
            {
                EventPopoutReplay loadedReplay =
                    player != null ? player.EventReplay : null;
                if (success &&
                    loadedReplay != null &&
                    loadedReplay.HasCollision)
                {
                    eventLoadMessage = null;
                    loadedReplay.PrepareTestCollision();
                    if (collisionOpenWhenPreparedRoutine != null)
                    {
                        StopCoroutine(
                            collisionOpenWhenPreparedRoutine);
                    }
                    collisionOpenWhenPreparedRoutine =
                        StartCoroutine(
                            OpenCollisionWhenPrepared(
                                loadedReplay));
                }
                else
                {
                    collisionDatasetLoading = false;
                    eventLoadMessage = success
                        ? "COLLISION DATA NOT READY"
                        : "COLLISION LOAD FAILED — REPLAY PRESERVED";
                }

                RefreshEventControls();
            });
            RefreshEventControls();
        }

        private IEnumerator OpenCollisionWhenPrepared(
            EventPopoutReplay expectedReplay)
        {
            float timeoutAt = Time.realtimeSinceStartup + 10f;
            while (expectedReplay != null &&
                   ReferenceEquals(
                       player != null
                           ? player.EventReplay
                           : null,
                       expectedReplay) &&
                   !expectedReplay.IsCollisionPrepared &&
                   string.IsNullOrWhiteSpace(
                       expectedReplay.CollisionPreparationFailure) &&
                   Time.realtimeSinceStartup < timeoutAt)
            {
                if (!expectedReplay.IsCollisionPreloading)
                    expectedReplay.PrepareTestCollision();
                yield return null;
            }

            collisionDatasetLoading = false;
            collisionOpenWhenPreparedRoutine = null;
            if (expectedReplay != null &&
                expectedReplay.IsCollisionPrepared)
            {
                eventLoadMessage = null;
                expectedReplay.OpenTestCollision();
            }
            else
            {
                eventLoadMessage =
                    "COLLISION PREPARATION FAILED";
            }
            RefreshEventControls();
        }

        private void ResolveRoomSetup()
        {
            if (roomSetup == null)
            {
                roomSetup =
                    Object.FindAnyObjectByType<RoomShowcaseSetupController>(
                        FindObjectsInactive.Include);
            }
            if (pitWallPresenter == null)
            {
                pitWallPresenter =
                    Object.FindAnyObjectByType<PitWallShowcasePresenter>(
                        FindObjectsInactive.Include);
            }
            if (replayStarter == null)
            {
                replayStarter =
                    Object.FindAnyObjectByType<AutoReplayStarter>(
                        FindObjectsInactive.Include);
            }
        }

        private void ToggleEventPlay()
        {
            EventPopoutReplay eventReplay =
                player?.EventReplay;
            if (eventReplay != null &&
                eventReplay.IsCurrentCollision)
            {
                if (!eventReplay.IsCollisionTimeLensGrabbed)
                    eventReplay.ReplayCollisionImpact();
            }
            else
            {
                eventReplay?.TogglePlay();
            }
            RefreshEventControls();
        }

        private void RestartEvent()
        {
            EventPopoutReplay eventReplay =
                player?.EventReplay;
            if (eventReplay != null &&
                eventReplay.IsCurrentCollision)
            {
                if (!eventReplay.IsCollisionTimeLensGrabbed)
                    eventReplay.RestartCollisionReveal();
            }
            else
            {
                eventReplay?.Restart();
            }
            RefreshEventControls();
        }

        private void OpenPitStop()
        {
            ResolveRoomSetup();
            if (roomSetup != null &&
                !roomSetup.HasPitWallCandidate)
            {
                roomSetup.NotifyOpenBlocked();
                RefreshEventControls();
                return;
            }
            if (pitWallPresenter != null &&
                !pitWallPresenter.HasSuitablePitWall)
            {
                eventLoadMessage =
                    "PIT WALL NEEDS AT LEAST 1.8 M x 1.3 M";
                RefreshEventControls();
                return;
            }

            eventLoadMessage = null;
            player?.EventReplay?.OpenTestPitStop();
            RefreshEventControls();
        }

        private void OpenNextEvent()
        {
            EventPopoutReplay eventReplay = player?.EventReplay;
            if (eventReplay != null && eventReplay.IsPitStopActive)
                eventReplay.OpenNextPitStop();
            else if (eventReplay != null && eventReplay.IsCurrentCollision)
                eventReplay.OpenNextCollision();
            else
                eventReplay?.OpenNextOvertake();
            RefreshEventControls();
        }

        private void SelectNextPitWall()
        {
            ResolveRoomSetup();
            pitWallPresenter?.SelectNextPitWall();
            RefreshEventControls();
        }

        private void TogglePitPortalEdit()
        {
            ResolveRoomSetup();
            pitWallPresenter?.TogglePortalEditMode();
            RefreshEventControls();
        }

        private void UndoPitPortalEdit()
        {
            pitWallPresenter?.UndoPortalEdit();
            RefreshEventControls();
        }

        private void ResetPitPortalEdit()
        {
            pitWallPresenter?.ResetPortalEdit();
            RefreshEventControls();
        }

        private void TogglePitReplayView()
        {
            ResolveRoomSetup();
            pitWallPresenter?.TogglePitReplayView();
            RefreshEventControls();
        }

        private void CloseEvent()
        {
            player?.EventReplay?.Close();
            RefreshEventControls();
        }

        private void SeekEvent(float normalized)
        {
            if (refreshingEventSlider)
                return;

            player?.EventReplay?.SeekNormalized(normalized);
            RefreshEventControls();
        }
    }
}
