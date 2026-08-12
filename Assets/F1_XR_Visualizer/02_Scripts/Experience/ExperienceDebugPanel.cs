using System;
using System.Collections;
using System.Collections.Generic;
using F1XR.Experience.Room;
using F1XR.RestAPI.Replay;
using F1XR.UI.WorldPanel;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace F1XR.Experience
{
    /// <summary>
    /// A world-space panel that exposes the Step 1 and Step 2 debug actions as buttons
    /// that can be pressed from inside the headset.
    ///
    /// The context menu items on <see cref="ExperienceModeDebugTrigger"/> and
    /// <see cref="RoomShellProxyDebug"/> only exist in the Editor Inspector, which is not
    /// reachable while wearing the headset even over Quest Link. This panel calls exactly
    /// the same public methods, so the two paths cannot drift apart.
    ///
    /// Built at runtime rather than authored in the scene, because it is verification
    /// scaffolding and should be easy to delete once the steps are signed off.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ExperienceDebugPanel : MonoBehaviour
    {
        [SerializeField] ExperienceModeDebugTrigger experienceTrigger;
        [SerializeField] RoomShellProxyDebug roomDebug;
        [SerializeField] RoomSurfaceProvider roomProvider;

        [Tooltip("Sideways nudge from the spot PanelInitialHeadsetPlacement picks. Pushed " +
            "well off to the side so the panel is not sitting in the middle of where the " +
            "controllers are being used.")]
        [SerializeField] Vector3 localNudge = new(0.75f, -0.05f, -0.15f);

        [Tooltip("Turn this off to keep the debug panel out of the scene entirely.")]
        [SerializeField] bool buildOnStart = true;

        ExperienceModeManager manager;
        ReplayPlayer player;
        RoomShellProxyGenerator generator;
        TextMeshProUGUI statusLabel;
        GameObject panelRoot;
        Fracture.VRWorldDepthDebug depthDebug;

        void Awake()
        {
            if (experienceTrigger == null)
                experienceTrigger = FindAnyObjectByType<ExperienceModeDebugTrigger>();
            if (roomDebug == null)
                roomDebug = FindAnyObjectByType<RoomShellProxyDebug>();
            if (roomProvider == null)
                roomProvider = FindAnyObjectByType<RoomSurfaceProvider>();

            manager = FindAnyObjectByType<ExperienceModeManager>();
            player = FindAnyObjectByType<ReplayPlayer>();
            generator = FindAnyObjectByType<RoomShellProxyGenerator>();
        }

        void Start()
        {
            if (buildOnStart)
                BuildPanel();
        }

        void OnEnable()
        {
            StartCoroutine(RefreshStatusLoop());
        }

        [ContextMenu("Build Debug Panel")]
        public void BuildPanel()
        {
            if (panelRoot != null)
                Destroy(panelRoot);

            // The holder is what gets placed in front of the head; the canvas hangs off it
            // with a local nudge, so no private field on the placement helper is touched.
            panelRoot = new GameObject("Experience Debug Panel");
            panelRoot.transform.SetParent(transform, false);
            panelRoot.AddComponent<PanelInitialHeadsetPlacement>();

            GameObject canvasObject = new GameObject("Canvas");
            canvasObject.transform.SetParent(panelRoot.transform, false);
            canvasObject.transform.localPosition = localNudge;

            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasObject.AddComponent<CanvasScaler>();

            // Only the tracked device raycaster. A plain GraphicRaycaster on a world space
            // canvas also feeds the input module from the mouse pointer, which competes
            // with the controller ray for UI focus.
            canvasObject.AddComponent<TrackedDeviceGraphicRaycaster>();

            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(640f, 720f);
            canvasRect.localScale = Vector3.one * 0.001f;
            canvasRect.localPosition = localNudge;

            AddBackground(canvasObject, new Color(0.05f, 0.06f, 0.09f, 0.92f));

            statusLabel = AddLabel(canvasObject.transform, "status");
            RectTransform statusRect = statusLabel.rectTransform;
            statusRect.anchorMin = new Vector2(0f, 1f);
            statusRect.anchorMax = new Vector2(1f, 1f);
            statusRect.pivot = new Vector2(0.5f, 1f);
            statusRect.anchoredPosition = new Vector2(0f, -12f);
            statusRect.sizeDelta = new Vector2(-24f, 170f);

            GameObject grid = new GameObject("Buttons", typeof(RectTransform));
            grid.transform.SetParent(canvasObject.transform, false);
            RectTransform gridRect = grid.GetComponent<RectTransform>();
            gridRect.anchorMin = Vector2.zero;
            gridRect.anchorMax = Vector2.one;
            gridRect.offsetMin = new Vector2(12f, 12f);
            gridRect.offsetMax = new Vector2(-12f, -186f);

            // Three columns, not two. At two the button list outgrew the six rows that fit
            // inside the panel and everything past Room Shell simply rendered off the bottom
            // of the background, which looks exactly like the missing buttons were never
            // added. Eighteen buttons need six rows of three.
            GridLayoutGroup layout = grid.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(200f, 74f);
            layout.spacing = new Vector2(8f, 8f);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 3;
            layout.childAlignment = TextAnchor.UpperCenter;

            Color step1 = new(0.16f, 0.34f, 0.62f, 1f);
            Color step2 = new(0.42f, 0.24f, 0.10f, 1f);

            Color probe = new(0.30f, 0.30f, 0.34f, 1f);
            Color danger = new(0.45f, 0.10f, 0.10f, 1f);

            // Button order matters. A device log showed Clear Selection being pressed
            // between Select Vehicle and Enter VR Game purely because the grid put it in
            // the seat next door, which wiped the selection and made every VR attempt fail
            // validation. Destructive actions now sit at the very bottom in red, well away
            // from the path the hand travels through the main flow.
            AddButton(grid.transform, "Select Vehicle", step1, () => Run("Select Vehicle", () => experienceTrigger?.TestSelectVehicle()));
            AddButton(grid.transform, "Enter VR Game", step1, () => Run("Enter VR Game", () => experienceTrigger?.TestEnterVRGame()));
            AddButton(grid.transform, "Return MR", step1, () => Run("Return MR", () => experienceTrigger?.TestReturnMR()));
            AddButton(grid.transform, "Log State", step1, () => Run("Log State", () => experienceTrigger?.LogCurrentState()));

            AddButton(grid.transform, "Log Existing Cars", step1, () => Run("Log Existing Cars", () => experienceTrigger?.LogExistingCars()));
            AddButton(grid.transform, "Probe Rays", probe, () => Run("Probe Rays", ProbeRays));
            AddButton(grid.transform, "Force PT off (VR)", probe, () => Run("Force PT off", ForcePassthroughOff));
            AddButton(grid.transform, "Force PT on (MR)", probe, () => Run("Force PT on", ForcePassthroughOn));

            AddButton(grid.transform, "Log Planes", step2, () => Run("Log Planes", () => roomProvider?.LogAllPlanes()));
            AddButton(grid.transform, "Toggle Proxy Debug", step2, () => Run("Toggle Proxy Debug", () => roomDebug?.ToggleProxyDebug()));
            AddButton(grid.transform, "Rebuild Proxies", step2, () => Run("Rebuild Proxies", () => roomDebug?.RebuildRoomProxies()));
            AddButton(grid.transform, "Room Shell OFF/ON", probe, () => Run("Room Shell toggle", ToggleRoomShell));
            AddButton(grid.transform, "Depth Debug ON/OFF", probe, () => Run("Depth Debug toggle", ToggleDepthDebug));

            AddButton(grid.transform, "Offset 0", step2, () => Run("Offset 0", () => roomDebug?.ApplyOffsetZero()));
            AddButton(grid.transform, "Offset 0.005", step2, () => Run("Offset 0.005", () => roomDebug?.ApplyOffsetSmall()));
            AddButton(grid.transform, "Offset 0.02", step2, () => Run("Offset 0.02", () => roomDebug?.ApplyOffsetLarge()));
            AddButton(grid.transform, "HIDE PANEL", probe, () => Run("Hide Panel", HidePanel));

            // Destructive, last row, red. Clear Selection is deliberately absent: it kept
            // wiping a valid selection right before Enter VR Game.
            AddButton(grid.transform, "! Clear Proxies", danger, () => Run("Clear Proxies", () => roomDebug?.ClearRoomProxies()));

            RefreshStatus();
        }

        void Run(string label, Action action)
        {
            // Logged before anything else runs, so a device trace distinguishes "the
            // button was never pressed" from "the button ran and the action did nothing".
            Debug.Log($"[ExperienceDebug] {label} CLICK", this);
            action?.Invoke();
            RefreshStatus();
        }

        /// <summary>
        /// Drives the Passthrough controller on its own, bypassing the mode manager. This
        /// separates "the fade itself does not hide the real room on this device" from
        /// "the transition sequence never got that far". Uses the controller's existing
        /// public snap methods; nothing in it is modified.
        /// </summary>
        void ForcePassthroughOff() => ForcePassthrough(true);

        void ForcePassthroughOn() => ForcePassthrough(false);

        void ForcePassthrough(bool hide)
        {
            var passthrough = FindAnyObjectByType<PassthroughTransitionController>();
            if (passthrough == null)
            {
                Debug.LogWarning("[ExperienceDebug] No PassthroughTransitionController.", this);
                return;
            }

            if (hide)
                passthrough.ApplyVRImmediate();
            else
                passthrough.ApplyMRImmediate();

            // GameObject.Find skips inactive objects, and hiding Passthrough deactivates
            // the layer, so walk the scene roots instead.
            GameObject layer = null;
            foreach (GameObject root in
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name == "Passthrough Layer")
                    layer = root;
            }

            Camera cam = Camera.main;
            Debug.Log(
                $"[ForcePT] hide={hide} state={passthrough.State} " +
                $"cameraAlpha={(cam != null ? cam.backgroundColor.a : -1f):F2} " +
                $"clearFlags={(cam != null ? cam.clearFlags.ToString() : "no camera")} " +
                $"passthroughLayerFound={(layer != null)} " +
                $"layerActive={(layer != null && layer.activeSelf)}",
                this);
        }

        /// <summary>Hides the panel so it stops competing for the controller ray.</summary>
        [ContextMenu("Hide Debug Panel")]
        public void HidePanel()
        {
            SetPanelVisible(false);
        }

        [ContextMenu("Show Debug Panel")]
        public void ShowPanel()
        {
            if (panelRoot == null)
                BuildPanel();
            else
                SetPanelVisible(true);
        }

        public void SetPanelVisible(bool visible)
        {
            if (panelRoot != null && panelRoot.activeSelf != visible)
                panelRoot.SetActive(visible);

            Debug.Log($"[ExperienceDebug] Panel visible = {visible}", this);
        }

        /// <summary>
        /// Reports what each ray interactor is currently pointing at, so it is obvious
        /// whether an AR plane collider is intercepting the ray before it reaches a car,
        /// a panel button, or anything else.
        /// </summary>
        void ProbeRays()
        {
            var interactors = FindObjectsByType<XRRayInteractor>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            Debug.Log($"[Probe] ray interactors found = {interactors.Length}", this);

            foreach (XRRayInteractor interactor in interactors)
            {
                string physics = interactor.TryGetCurrent3DRaycastHit(out RaycastHit hit)
                    ? $"{hit.collider.name} (layer {LayerMask.LayerToName(hit.collider.gameObject.layer)}) " +
                      $"dist={hit.distance:F2} on '{PathOf(hit.collider.transform)}'"
                    : "none";

                string ui = interactor.TryGetCurrentUIRaycastResult(out RaycastResult uiHit)
                    ? $"{uiHit.gameObject.name} dist={uiHit.distance:F2}"
                    : "none";

                Debug.Log(
                    $"[Probe] '{interactor.name}' enabled={interactor.isActiveAndEnabled} " +
                    $"uiInteraction={interactor.enableUIInteraction} " +
                    $"physicsHit={physics} uiHit={ui}",
                    interactor);
            }
        }

        /// <summary>
        /// Switches the whole Step 2 room shell off and on, for an A/B test of whether
        /// Step 2 has any effect on the Step 1 flow.
        /// </summary>
        /// <summary>
        /// Created on demand rather than wired into the scene, so the depth reconstruction
        /// test can be flown to the headset without touching the scene file.
        /// </summary>
        void ToggleDepthDebug()
        {
            if (depthDebug == null)
            {
                depthDebug = FindAnyObjectByType<Fracture.VRWorldDepthDebug>();
                if (depthDebug == null)
                {
                    var host = new GameObject("VRWorldDepthDebug");
                    depthDebug = host.AddComponent<Fracture.VRWorldDepthDebug>();
                }
            }

            depthDebug.Toggle();
            Debug.Log($"[ExperienceDebug] Depth Debug active = {depthDebug.IsActive}.", this);
        }

        void ToggleRoomShell()
        {
            GameObject roomShell = roomProvider != null
                ? roomProvider.gameObject
                : (roomDebug != null ? roomDebug.gameObject : null);

            if (roomShell == null)
            {
                Debug.LogWarning("[ExperienceDebug] No Room Shell object found.", this);
                return;
            }

            bool next = !roomShell.activeSelf;
            roomShell.SetActive(next);
            Debug.Log(
                $"[ExperienceDebug] Room Shell active = {next}. " +
                "With it off, Step 2 contributes nothing: no vertical plane request, no " +
                "proxies, no collider changes.",
                this);
        }

        static string PathOf(Transform t)
        {
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }

            return path;
        }

        IEnumerator RefreshStatusLoop()
        {
            var wait = new WaitForSeconds(0.25f);
            while (enabled)
            {
                RefreshStatus();
                yield return wait;
            }
        }

        void RefreshStatus()
        {
            if (statusLabel == null)
                return;

            string mode = manager != null ? manager.Mode.ToString() : "no manager";
            string time = player != null ? player.CurrentTime.ToString("F2") : "-";
            string playing = player != null ? player.IsPlaying.ToString() : "-";

            int proxies = generator != null ? generator.Proxies.Count : 0;
            float offset = generator != null ? generator.SurfaceOffset : 0f;
            int wallCount = roomProvider != null ? roomProvider.Walls.Count : 0;

            string replayLine = "<color=#ff6666>Replay NO TRIGGER</color>";
            string vrLine = "";
            string selectLine = "";

            if (experienceTrigger != null)
            {
                var r = experienceTrigger.GetReadiness();
                string ready = r.IsReady
                    ? "<color=#66ff88>Replay READY</color>"
                    : $"<color=#ffaa44>Replay NOT READY</color> ({r.Blocker})";

                replayLine =
                    $"{ready}  Dataset={r.HasDataset} Track={r.TrackPlaced} " +
                    $"Cars={r.CarCount} Selected={(r.Selected > 0 ? "#" + r.Selected : "0")}";

                vrLine = experienceTrigger.IsVehicleReadyForVR()
                    ? $"<color=#66ff88>VR READY</color>  {experienceTrigger.DescribeSelected()}"
                    : "<color=#ff6666>VR BLOCKED - No Vehicle Selected</color>";

                if (!string.IsNullOrEmpty(experienceTrigger.LastSelectResult))
                    selectLine = experienceTrigger.LastSelectResult.Replace("\n", "  ");
            }

            statusLabel.text =
                $"{replayLine}\n" +
                $"{vrLine}\n" +
                $"<b>Mode</b> {mode}   t={time} playing={playing}   {selectLine}\n" +
                $"<b>Proxies</b> {proxies}  walls={wallCount}  offset={offset:F3}";
        }

        static void AddBackground(GameObject target, Color color)
        {
            Image image = target.AddComponent<Image>();
            image.color = color;

            // The backing plate must not be a raycast target. As one it swallows every
            // controller ray that crosses the panel, and because a UI hit outranks a 3D
            // hit the ray then never reaches the cars, the track or the gear. Only the
            // buttons themselves need to receive rays.
            image.raycastTarget = false;
        }

        static TextMeshProUGUI AddLabel(Transform parent, string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
            text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = 26f;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.raycastTarget = false;
            return text;
        }

        static void AddButton(Transform parent, string label, Color color, Action onClick)
        {
            GameObject go = new GameObject(label, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            Image image = go.AddComponent<Image>();
            image.color = color;

            Button button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick?.Invoke());

            TextMeshProUGUI text = AddLabel(go.transform, "Label");
            text.text = label;
            // Narrower cells since the grid went to three columns, so the longest labels
            // ("Depth Debug ON/OFF") still fit on two lines instead of being clipped.
            text.fontSize = 20f;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.alignment = TextAlignmentOptions.Center;
            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(8f, 4f);
            textRect.offsetMax = new Vector2(-8f, -4f);
        }
    }
}
