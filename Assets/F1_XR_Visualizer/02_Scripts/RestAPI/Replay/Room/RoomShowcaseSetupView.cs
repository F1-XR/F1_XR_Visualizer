using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using F1XR.UI.WorldPanel;

namespace F1XR.RestAPI.Replay.Room
{
    public readonly struct RoomShowcaseSetupPresentation
    {
        public RoomShowcaseSetupPresentation(
            RoomShowcaseSetupState state,
            string status,
            int candidateUserFacingNumber,
            int userFacingCandidateCount)
        {
            State = state;
            Status = status;
            CandidateUserFacingNumber = candidateUserFacingNumber;
            UserFacingCandidateCount = userFacingCandidateCount;
        }

        public RoomShowcaseSetupState State { get; }
        public string Status { get; }
        public int CandidateUserFacingNumber { get; }
        public int UserFacingCandidateCount { get; }
    }

    [DisallowMultipleComponent]
    public sealed class RoomShowcaseSetupView : MonoBehaviour
    {
        [Header("Panel")]
        [SerializeField] private RectTransform setupPanel;
        [SerializeField] private Transform xrMainCamera;
        [SerializeField, Min(0.5f)] private float panelDistance = 1.4f;
        [SerializeField] private float panelHeightOffset = -0.05f;

        private RoomShowcaseSetupController controller;
        private RectTransform contentRoot;
        private TMP_Text titleText;
        private TMP_Text instructionText;
        private TMP_Text candidateText;
        private TMP_Text statusText;
        private Button previousButton;
        private Button nextButton;
        private Button confirmEntryButton;
        private Button confirmExitButton;
        private Button backToEntryButton;
        private Button captureHeroButton;
        private Button backToExitButton;
        private Button confirmSetupButton;
        private Button recaptureHeroButton;
        private Button reselectEntryButton;
        private Button reselectExitButton;
        private Button resetButton;
        private Button reconfigureButton;
        private Button recenterButton;
        private readonly List<Button> buttons = new();
        private bool initialPanelPlacementPending;

        public bool IsConfigured => setupPanel != null;

        private void Start()
        {
            initialPanelPlacementPending = true;
            TryPlacePanelInitially();
        }

        private void Update()
        {
            TryPlacePanelInitially();
        }

        public void Initialize(RoomShowcaseSetupController owner)
        {
            if (controller == null)
                controller = owner;

            ResolveReferences();
            BuildPanel();
        }

        public void Refresh(RoomShowcaseSetupPresentation presentation)
        {
            if (contentRoot == null)
                return;

            titleText.text = ResolveTitle(presentation.State);
            instructionText.text = ResolveInstruction(presentation.State);
            statusText.text = presentation.Status;
            candidateText.text = ResolveCandidateText(presentation);

            foreach (var button in buttons)
                button.gameObject.SetActive(false);

            var slot = 0;
            switch (presentation.State)
            {
                case RoomShowcaseSetupState.SelectEntry:
                    ShowButton(previousButton, ref slot);
                    ShowButton(nextButton, ref slot);
                    ShowButton(confirmEntryButton, ref slot);
                    ShowButton(recenterButton, ref slot);
                    break;
                case RoomShowcaseSetupState.SelectExit:
                    ShowButton(previousButton, ref slot);
                    ShowButton(nextButton, ref slot);
                    ShowButton(confirmExitButton, ref slot);
                    ShowButton(backToEntryButton, ref slot);
                    ShowButton(recenterButton, ref slot);
                    break;
                case RoomShowcaseSetupState.CaptureHero:
                    ShowButton(captureHeroButton, ref slot);
                    ShowButton(backToExitButton, ref slot);
                    ShowButton(recenterButton, ref slot);
                    break;
                case RoomShowcaseSetupState.Review:
                    ShowButton(confirmSetupButton, ref slot);
                    ShowButton(recaptureHeroButton, ref slot);
                    ShowButton(reselectEntryButton, ref slot);
                    ShowButton(reselectExitButton, ref slot);
                    ShowButton(resetButton, ref slot);
                    ShowButton(recenterButton, ref slot);
                    break;
                case RoomShowcaseSetupState.Ready:
                    ShowButton(reconfigureButton, ref slot);
                    ShowButton(recenterButton, ref slot);
                    break;
                case RoomShowcaseSetupState.Error:
                    ShowButton(resetButton, ref slot);
                    ShowButton(recenterButton, ref slot);
                    break;
                default:
                    ShowButton(recenterButton, ref slot);
                    break;
            }

            candidateText.gameObject.SetActive(
                presentation.State == RoomShowcaseSetupState.SelectEntry ||
                presentation.State == RoomShowcaseSetupState.SelectExit);
            setupPanel.sizeDelta =
                presentation.State == RoomShowcaseSetupState.Ready
                    ? new Vector2(760f, 410f)
                    : new Vector2(760f, 650f);
        }

        public void SetVisible(bool visible)
        {
            if (setupPanel != null)
                setupPanel.gameObject.SetActive(visible);
        }

        public void RecenterPanel()
        {
            ResolveReferences();
            if (TryPlacePanel())
                initialPanelPlacementPending = false;
        }

        private void ResolveReferences()
        {
            if (xrMainCamera == null && Camera.main != null)
                xrMainCamera = Camera.main.transform;
        }

        private void BuildPanel()
        {
            if (setupPanel == null || contentRoot != null ||
                controller == null)
            {
                return;
            }

            setupPanel.sizeDelta = new Vector2(760f, 650f);
            var content = new GameObject(
                "SetupContent",
                typeof(RectTransform),
                typeof(Image));
            content.transform.SetParent(setupPanel, false);
            contentRoot = content.GetComponent<RectTransform>();
            Fill(contentRoot);
            Image contentImage = content.GetComponent<Image>();
            contentImage.color =
                new Color(0.015f, 0.018f, 0.026f, 0.94f);
            contentImage.raycastTarget = false;

            titleText = CreateText(
                "Title",
                30f,
                FontStyles.Bold,
                TextAlignmentOptions.Center);
            SetRect(titleText.rectTransform, 30f, -24f, 700f, 48f);

            instructionText = CreateText(
                "Instruction",
                24f,
                FontStyles.Normal,
                TextAlignmentOptions.Center);
            SetRect(
                instructionText.rectTransform,
                45f,
                -86f,
                670f,
                80f);

            candidateText = CreateText(
                "Candidate",
                22f,
                FontStyles.Bold,
                TextAlignmentOptions.Center);
            SetRect(candidateText.rectTransform, 45f, -176f, 670f, 38f);

            statusText = CreateText(
                "Status",
                19f,
                FontStyles.Normal,
                TextAlignmentOptions.Center);
            statusText.color = new Color(0.55f, 0.85f, 1f);
            SetRect(statusText.rectTransform, 45f, -222f, 670f, 54f);

            previousButton = CreateButton(
                "Previous Candidate",
                "Previous Wall",
                controller.PreviousCandidate);
            nextButton = CreateButton(
                "Next Candidate",
                "Next Wall",
                controller.NextCandidate);
            confirmEntryButton = CreateButton(
                "Confirm Entry",
                "Confirm Entry",
                controller.ConfirmEntry);
            confirmExitButton = CreateButton(
                "Confirm Exit",
                "Confirm Exit",
                controller.ConfirmExit);
            backToEntryButton = CreateButton(
                "Back To Entry",
                "Back to Entry",
                controller.BackToEntry);
            captureHeroButton = CreateButton(
                "Capture Hero",
                "Capture Hero",
                controller.CaptureHero);
            backToExitButton = CreateButton(
                "Back To Exit",
                "Back to Exit",
                controller.BackToExit);
            confirmSetupButton = CreateButton(
                "Confirm Setup",
                "Confirm Setup",
                controller.ConfirmSetup);
            recaptureHeroButton = CreateButton(
                "Recapture Hero",
                "Recapture Hero",
                controller.RecaptureHero);
            reselectEntryButton = CreateButton(
                "Reselect Entry",
                "Reselect Entry",
                controller.ReselectEntry);
            reselectExitButton = CreateButton(
                "Reselect Exit",
                "Reselect Exit",
                controller.ReselectExit);
            resetButton = CreateButton(
                "Reset Setup",
                "Reset Setup",
                controller.ResetSetup);
            reconfigureButton = CreateButton(
                "Reconfigure Room",
                "Reconfigure Room",
                controller.ReconfigureRoom);
            recenterButton = CreateButton(
                "Recenter Panel",
                "Recenter Panel",
                controller.RecenterPanel);

            ConfigurePanelGrab();
        }

        private void ConfigurePanelGrab()
        {
            Rigidbody body =
                setupPanel.GetComponent<Rigidbody>();
            if (body == null)
                body = setupPanel.gameObject.AddComponent<Rigidbody>();

            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.constraints = RigidbodyConstraints.FreezeRotation;

            RectTransform handle = CreateGrabHandle();
            BoxCollider handleCollider =
                handle.GetComponent<BoxCollider>();

            XRGrabInteractable grab =
                setupPanel.GetComponent<XRGrabInteractable>();
            if (grab == null)
            {
                grab =
                    setupPanel.gameObject
                        .AddComponent<XRGrabInteractable>();
            }

            grab.colliders.Clear();
            grab.colliders.Add(handleCollider);
            grab.useDynamicAttach = true;
            grab.matchAttachPosition = true;
            grab.matchAttachRotation = false;
            grab.trackRotation = false;
            grab.snapToColliderVolume = true;
            grab.attachEaseInTime = 0.15f;
            grab.throwOnDetach = false;

            if (setupPanel.GetComponent<PanelYawGrabLock>() == null)
            {
                setupPanel.gameObject
                    .AddComponent<PanelYawGrabLock>();
            }
        }

        private RectTransform CreateGrabHandle()
        {
            const string handleName = "Room Setup Move Handle";
            Transform existing = setupPanel.Find(handleName);
            RectTransform handle;

            if (existing != null)
            {
                handle = existing as RectTransform;
            }
            else
            {
                GameObject handleObject = new(
                    handleName,
                    typeof(RectTransform),
                    typeof(Image),
                    typeof(BoxCollider));
                handleObject.layer = setupPanel.gameObject.layer;
                handleObject.transform.SetParent(
                    setupPanel,
                    worldPositionStays: false);
                handle =
                    handleObject.GetComponent<RectTransform>();
            }

            handle.anchorMin = new Vector2(0.5f, 0f);
            handle.anchorMax = new Vector2(0.5f, 0f);
            handle.pivot = new Vector2(0.5f, 0f);
            handle.anchoredPosition = new Vector2(0f, 8f);
            handle.sizeDelta = new Vector2(680f, 34f);
            handle.SetAsLastSibling();

            Image image = handle.GetComponent<Image>();
            image.color =
                new Color(0.92f, 0.95f, 0.95f, 0.08f);
            image.raycastTarget = false;

            BoxCollider collider =
                handle.GetComponent<BoxCollider>();
            collider.center = Vector3.zero;
            collider.size = new Vector3(680f, 34f, 20f);
            collider.isTrigger = false;
            CreateGrabHandleVisual(handle);
            return handle;
        }

        private void CreateGrabHandleVisual(
            RectTransform handle)
        {
            const string visualName = "Move Handle Bar";
            Transform existing = handle.Find(visualName);
            RectTransform visual;

            if (existing != null)
            {
                visual = existing as RectTransform;
            }
            else
            {
                GameObject visualObject = new(
                    visualName,
                    typeof(RectTransform),
                    typeof(Image));
                visualObject.layer = setupPanel.gameObject.layer;
                visualObject.transform.SetParent(
                    handle,
                    worldPositionStays: false);
                visual =
                    visualObject.GetComponent<RectTransform>();
            }

            visual.anchorMin = new Vector2(0.5f, 0.5f);
            visual.anchorMax = new Vector2(0.5f, 0.5f);
            visual.pivot = new Vector2(0.5f, 0.5f);
            visual.anchoredPosition = Vector2.zero;
            visual.sizeDelta = new Vector2(140f, 6f);

            Image image = visual.GetComponent<Image>();
            image.color =
                new Color(0.92f, 0.95f, 0.95f, 0.72f);
            image.raycastTarget = false;
        }

        private TMP_Text CreateText(
            string name,
            float fontSize,
            FontStyles style,
            TextAlignmentOptions alignment)
        {
            var text = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI))
                .GetComponent<TextMeshProUGUI>();
            text.transform.SetParent(contentRoot, false);
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = new Color(0.92f, 0.94f, 0.98f);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;
            return text;
        }

        private Button CreateButton(
            string name,
            string labelText,
            UnityEngine.Events.UnityAction action)
        {
            var button = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Image),
                typeof(Button))
                .GetComponent<Button>();
            button.transform.SetParent(contentRoot, false);
            button.targetGraphic = button.GetComponent<Image>();
            button.onClick.AddListener(action);
            StyleButton(button);

            var label = new GameObject(
                "Label",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI))
                .GetComponent<TextMeshProUGUI>();
            label.transform.SetParent(button.transform, false);
            Fill(label.rectTransform);
            label.text = labelText;
            label.fontSize = 20f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(0.92f, 0.94f, 0.98f);
            label.raycastTarget = false;
            buttons.Add(button);
            return button;
        }

        private static void StyleButton(Button button)
        {
            var image = button.GetComponent<Image>();
            image.color = new Color(0.09f, 0.1f, 0.13f, 0.98f);

            var colors = button.colors;
            colors.normalColor = new Color(0.09f, 0.1f, 0.13f, 0.98f);
            colors.highlightedColor =
                new Color(0.16f, 0.18f, 0.24f, 1f);
            colors.pressedColor = new Color(0.62f, 0.04f, 0.07f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.05f, 0.055f, 0.07f, 0.65f);
            button.colors = colors;
        }

        private void ShowButton(Button button, ref int slot)
        {
            button.gameObject.SetActive(true);
            var row = slot / 2;
            var column = slot % 2;
            var rect = button.GetComponent<RectTransform>();
            SetRect(
                rect,
                45f + column * 345f,
                -300f - row * 74f,
                325f,
                56f);
            slot++;
        }

        private bool TryPlacePanel()
        {
            if (setupPanel == null || xrMainCamera == null)
                return false;

            var up = transform.up.normalized;
            var forward = Vector3.ProjectOnPlane(xrMainCamera.forward, up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = transform.forward;

            forward.Normalize();
            setupPanel.SetPositionAndRotation(
                xrMainCamera.position +
                forward * panelDistance +
                up * panelHeightOffset,
                Quaternion.LookRotation(forward, up));
            return true;
        }

        private void TryPlacePanelInitially()
        {
            TryPlacePanelInitially(IsHeadsetPoseUsable());
        }

        private void TryPlacePanelInitially(bool headsetPoseUsable)
        {
            if (!initialPanelPlacementPending || !headsetPoseUsable)
                return;

            if (TryPlacePanel())
                initialPanelPlacementPending = false;
        }

        private bool IsHeadsetPoseUsable()
        {
            if (xrMainCamera == null ||
                !IsFinite(xrMainCamera.position) ||
                !IsFinite(xrMainCamera.forward) ||
                xrMainCamera.forward.sqrMagnitude < 0.5f)
            {
                return false;
            }

            var headDevice = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!headDevice.isValid)
                return false;

            if (headDevice.TryGetFeatureValue(
                    CommonUsages.trackingState,
                    out InputTrackingState trackingState))
            {
                const InputTrackingState required =
                    InputTrackingState.Position |
                    InputTrackingState.Rotation;
                return (trackingState & required) == required;
            }

            return headDevice.TryGetFeatureValue(
                    CommonUsages.isTracked,
                    out var isTracked) &&
                isTracked;
        }

        private static string ResolveTitle(RoomShowcaseSetupState state)
        {
            return state switch
            {
                RoomShowcaseSetupState.WaitingForRoom => "ROOM SETUP",
                RoomShowcaseSetupState.SelectEntry => "SELECT ENTRY",
                RoomShowcaseSetupState.SelectExit => "SELECT EXIT",
                RoomShowcaseSetupState.CaptureHero => "CAPTURE HERO",
                RoomShowcaseSetupState.Review => "REVIEW ROOM PATH",
                RoomShowcaseSetupState.Ready => "ROOM READY",
                RoomShowcaseSetupState.TemporarilyReacquiring =>
                    "REACQUIRING WALL",
                _ => "ROOM SETUP ERROR"
            };
        }

        private static string ResolveInstruction(
            RoomShowcaseSetupState state)
        {
            return state switch
            {
                RoomShowcaseSetupState.WaitingForRoom =>
                    "Please wait while room wall candidates are loaded.",
                RoomShowcaseSetupState.SelectEntry =>
                    "Choose the wall where the vehicles enter.",
                RoomShowcaseSetupState.SelectExit =>
                    "Choose a different wall where the vehicles exit.",
                RoomShowcaseSetupState.CaptureHero =>
                    "Face the desired vehicle travel direction and capture Hero.",
                RoomShowcaseSetupState.Review =>
                    "Check the ENTRY > HERO > EXIT markers and path.",
                RoomShowcaseSetupState.Ready =>
                    "Use the existing Open button to start event replay.",
                RoomShowcaseSetupState.TemporarilyReacquiring =>
                    "The selected physical wall is being reacquired.",
                _ => "Use Reset to restart the room setup."
            };
        }

        private static string ResolveCandidateText(
            RoomShowcaseSetupPresentation presentation)
        {
            if (presentation.CandidateUserFacingNumber < 0)
                return "No wall preview available";

            return
                $"Wall {presentation.CandidateUserFacingNumber} / " +
                $"{presentation.UserFacingCandidateCount}";
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                IsFinite(value.y) &&
                IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static void SetRect(
            RectTransform rect,
            float x,
            float y,
            float width,
            float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void Fill(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
