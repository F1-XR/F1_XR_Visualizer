using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using F1XR.Interaction.World;

namespace F1XR.Interaction.Input
{
    public enum MorphHoldButton
    {
        Trigger,
        Grip,
        PrimaryButton,
        SecondaryButton
    }

    /// <summary>
    /// Shows a gear-shift on the controller WHILE a button is held. Press-and-hold the button and
    /// the controller's visual model is swapped for the gear-shift prefab; release it and the gear
    /// shift disappears, restoring the controller. The gear shift is spawned once as a child of the
    /// controller pose, so it follows the hand like a held object.
    ///
    /// While shown, the knob leans toward the hand (via <see cref="GearShiftController"/>) and fires
    /// detent haptics as the lean direction changes.
    ///
    /// Put this on the controller pose object (e.g. "Right Controller"). The gear-shift copy is a
    /// static visual by default (its grab/bend interaction is disabled on the attached instance so
    /// it doesn't fight the hand it's parented to).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ControllerShiftMorph : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Transform whose pose the gear shift follows. Defaults to this transform (the controller pose).")]
        [SerializeField] Transform poseSource;
        [Tooltip("The controller's visual model root that gets hidden while shown (e.g. \"XR Controller Right\").")]
        [SerializeField] GameObject controllerModelRoot;
        [Tooltip("Gear-shift prefab spawned onto the controller.")]
        [SerializeField] GameObject gearShiftPrefab;

        [Header("Base plant (captured when shown)")]
        [Tooltip("Where the base plants, as an offset in the controller's local space at the moment it appears.")]
        [SerializeField] Vector3 attachLocalPosition = Vector3.zero;
        [Tooltip("World orientation of the planted base (default = upright). Only the Y (yaw) is applied - " +
            "pitch/roll are stripped so the base stays perfectly vertical and never leans with the wrist. " +
            "When Face User is on, Y is added on top of the user-facing yaw.")]
        [SerializeField] Vector3 attachLocalEuler = Vector3.zero;
        [Tooltip("Aim the base's front-back plane toward the user (headset) when it appears, so pushing " +
            "the hand forward/back tilts the lever forward/back instead of a direction that gets clipped.")]
        [SerializeField] bool faceUserOnPlant = true;
        [SerializeField] Vector3 attachLocalScale = new Vector3(10f, 10f, 10f);
        [Tooltip("Extra vertical offset of the planted base, in multiples of the gear shift's own height. " +
            "-0.5 spawns it half a gear shift lower. Negative = lower, positive = higher.")]
        [SerializeField, Range(-2f, 2f)] float baseHeightOffset = -0.5f;

        [Header("Toggle to show")]
        [Tooltip("When on, a single press of Toggle Button shows the gear shift and the next press hides it " +
            "(instead of the hold-to-show behavior below). Default: press A on the right controller.")]
        [SerializeField] bool toggleMode = true;
        [Tooltip("Button that toggles the gear shift on/off when Toggle Mode is on. A button = Primary Button.")]
        [SerializeField] MorphHoldButton toggleButton = MorphHoldButton.PrimaryButton;

        [Header("Hold to show")]
        [Tooltip("The gear shift is shown only while this controller button is held down; releasing it restores the controller.")]
        [SerializeField] MorphHoldButton holdButton = MorphHoldButton.Trigger;
        [Tooltip("Require a second button held at the same time (e.g. Grip + Trigger). The gear shift " +
            "only appears while BOTH are held.")]
        [SerializeField] bool requireSecondButton = false;
        [Tooltip("The second button that must also be held when Require Second Button is on.")]
        [SerializeField] MorphHoldButton secondButton = MorphHoldButton.Trigger;
        [Tooltip("Which hand's button to read. Right by default (this is the right controller).")]
        [SerializeField] bool useRightHand = true;

        [Header("Detent haptics (a click when the gear changes)")]
        [Tooltip("While shown, fire one crisp click each time the lever snaps into a new gear.")]
        [SerializeField] bool leanHaptics = true;
        [Tooltip("Click strength (0-1). Higher = firmer 'clunk'.")]
        [SerializeField, Range(0f, 1f)] float detentAmplitude = 0.7f;
        [Tooltip("Click length (seconds). Short = crisp click, long = softer thud.")]
        [SerializeField, Min(0.005f)] float detentDuration = 0.02f;

        [Header("Attached copy")]
        [Tooltip("Disable colliders / interactable / controller script on the spawned copy so it's a pure visual.")]
        [SerializeField] bool disableInteractionOnAttach = true;

        GameObject instance;
        GearShiftController instanceShift;
        bool shown;
        bool togglePrevPressed;
        readonly List<InputDevice> devices = new List<InputDevice>();

        void Awake()
        {
            if (poseSource == null)
                poseSource = transform;
        }

        void OnDisable()
        {
            // Leave the controller visible if this object is deactivated while the gear shift is up.
            if (shown)
                SetShown(false);
        }

        void OnDestroy()
        {
            if (instanceShift != null)
            {
                instanceShift.HoverDirectionChanged -= OnHoverDirectionChanged;
                instanceShift.GearChanged -= OnGearDetent;
            }
        }

        void Update()
        {
            if (toggleMode)
            {
                bool pressed = ReadButton(toggleButton);
                if (pressed && !togglePrevPressed) // rising edge: one flip per press
                    SetShown(!shown);
                togglePrevPressed = pressed;
                return;
            }

            var held = ReadHoldButton();

            if (held && !shown)
                SetShown(true);
            else if (!held && shown)
                SetShown(false);
        }

        // One crisp click each time the lever leans into a new direction (detent feedback).
        void OnHoverDirectionChanged(GearDirection direction)
        {
            if (leanHaptics)
                SendHaptic(detentAmplitude, detentDuration);
        }

        // One crisp click each time the lever snaps into a new front/back gear (detent feedback).
        void OnGearDetent(int gear)
        {
            if (leanHaptics)
                SendHaptic(detentAmplitude, detentDuration);
        }

        void SendHaptic(float amplitude, float duration)
        {
            var handedness = useRightHand ? InputDeviceCharacteristics.Right : InputDeviceCharacteristics.Left;
            devices.Clear();
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller | handedness, devices);

            foreach (var device in devices)
            {
                if (device.isValid &&
                    device.TryGetHapticCapabilities(out var caps) && caps.supportsImpulse)
                {
                    device.SendHapticImpulse(0u, Mathf.Clamp01(amplitude), duration);
                }
            }
        }

        bool ReadHoldButton()
        {
            if (!ReadButton(holdButton))
                return false;
            if (requireSecondButton && !ReadButton(secondButton))
                return false;
            return true;
        }

        bool ReadButton(MorphHoldButton button) => XRControllerButton.IsPressed(button, useRightHand);

        void SetShown(bool on)
        {
            shown = on;

            if (on)
            {
                EnsureInstance();
                PlantBase();
                // Knob leans toward the hand; the planted base stays put -> real gear-lever feel.
                if (instanceShift != null)
                    instanceShift.ExternalAimTarget = poseSource;
            }
            else if (instanceShift != null)
            {
                instanceShift.ExternalAimTarget = null;
            }

            if (instance != null)
                instance.SetActive(on);

            if (controllerModelRoot != null)
                controllerModelRoot.SetActive(!on);
        }

        void EnsureInstance()
        {
            if (instance != null || gearShiftPrefab == null)
                return;

            // Parent to a stable anchor (the controller's parent, e.g. Camera Offset) rather than the
            // controller itself, so the base does NOT travel/rotate with the wrist once planted.
            var anchor = poseSource != null ? poseSource.parent : null;
            instance = Instantiate(gearShiftPrefab, anchor);
            instance.name = gearShiftPrefab.name + " (Controller)";
            instance.transform.localScale = attachLocalScale;
            instanceShift = instance.GetComponentInChildren<GearShiftController>(true);

            if (instanceShift != null)
            {
                instanceShift.HoverDirectionChanged += OnHoverDirectionChanged;
                instanceShift.GearChanged += OnGearDetent;
            }

            if (disableInteractionOnAttach)
                StripInteraction(instance);
        }

        void PlantBase()
        {
            if (instance == null || poseSource == null)
                return;

            var t = instance.transform;
            // Plant perfectly upright: strip any pitch/roll so the base never leans and the lever's
            // front-back plane stays vertical. Only yaw (which way the plane faces) is honored.
            // Face the user so the front-back plane lines up with the way the hand pushes; otherwise a
            // forward/back push lands perpendicular to the plane and gets clipped (lever won't move).
            float yaw = attachLocalEuler.y;
            if (faceUserOnPlant)
                yaw += UserFacingYaw();
            t.rotation = Quaternion.Euler(0f, yaw, 0f);
            t.localScale = attachLocalScale;

            var pos = poseSource.TransformPoint(attachLocalPosition);
            pos += Vector3.up * (GetInstanceHeight() * baseHeightOffset);
            t.position = pos;
        }

        // Horizontal yaw (deg) that makes the base's local +Z point along the user's gaze direction,
        // so the lever's front-back plane faces the user. Falls back to the pose source, then to 0.
        float UserFacingYaw()
        {
            Vector3 fwd = Vector3.zero;

            var cam = Camera.main;
            if (cam != null)
                fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);

            if (fwd.sqrMagnitude < 1e-6f && poseSource != null)
                fwd = Vector3.ProjectOnPlane(poseSource.forward, Vector3.up);

            if (fwd.sqrMagnitude < 1e-6f)
                return 0f;

            return Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
        }

        float GetInstanceHeight()
        {
            if (instance == null)
                return 0f;

            var smr = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr != null && smr.sharedMesh != null)
                return smr.sharedMesh.bounds.size.y * smr.transform.lossyScale.y;

            return 0f;
        }

        // Disables grab/registration on the attached copy but leaves the GearShiftController running,
        // so the knob can still bend toward the hand via ExternalAimTarget.
        static void StripInteraction(GameObject root)
        {
            foreach (var col in root.GetComponentsInChildren<Collider>(true))
                col.enabled = false;

            foreach (var interactable in root.GetComponentsInChildren<XRBaseInteractable>(true))
                interactable.enabled = false;

            foreach (var manager in root.GetComponentsInChildren<XRInteractionManager>(true))
                manager.enabled = false;
        }
    }
}
