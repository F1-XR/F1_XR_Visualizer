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
        [Tooltip("World orientation of the planted base (default = upright). The base does NOT rotate with the wrist.")]
        [SerializeField] Vector3 attachLocalEuler = Vector3.zero;
        [SerializeField] Vector3 attachLocalScale = new Vector3(10f, 10f, 10f);
        [Tooltip("Extra vertical offset of the planted base, in multiples of the gear shift's own height. " +
            "-0.5 spawns it half a gear shift lower. Negative = lower, positive = higher.")]
        [SerializeField, Range(-2f, 2f)] float baseHeightOffset = -0.5f;

        [Header("Hold to show")]
        [Tooltip("The gear shift is shown only while this controller button is held down; releasing it restores the controller.")]
        [SerializeField] MorphHoldButton holdButton = MorphHoldButton.Trigger;
        [Tooltip("Which hand's button to read. Right by default (this is the right controller).")]
        [SerializeField] bool useRightHand = true;

        [Header("Detent haptics (a click when the direction changes)")]
        [Tooltip("While shown, fire one crisp click when the knob is pushed into a new direction " +
            "(not continuously while moving). Holding a direction = silent.")]
        [SerializeField] bool leanHaptics = true;
        [Tooltip("Lean (deg from upright) needed to engage a direction. Higher = the heading is more " +
            "stable when the first click fires (less double-click).")]
        [SerializeField, Min(0f)] float directionDeadzoneAngle = 10f;
        [Tooltip("How much the lean direction (azimuth) must change before another click fires.")]
        [SerializeField, Range(5f, 180f)] float directionChangeAngle = 45f;
        [Tooltip("Minimum time between clicks (s). Guarantees one crisp click instead of a quick double.")]
        [SerializeField, Min(0f)] float minClickInterval = 0.12f;
        [Tooltip("Also click when the knob returns to center/neutral.")]
        [SerializeField] bool clickOnReturnToCenter = true;
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
        bool leanEngaged;
        float lastClickAzimuth;
        float lastClickTime = -999f;
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

        void Update()
        {
            var held = ReadHoldButton();

            if (held && !shown)
                SetShown(true);
            else if (!held && shown)
                SetShown(false);

            if (shown && leanHaptics)
                UpdateLeanHaptics();
        }

        void UpdateLeanHaptics()
        {
            if (instanceShift == null)
                return;

            // A click fires only on a direction change: pushing out of center, swinging to a new
            // heading, or returning to center. Holding a direction stays silent.
            var axis = instanceShift.CurrentLeanAxisLocal;
            var leanAngle = Vector3.Angle(Vector3.up, axis);

            // Engage/disengage hysteresis so a lean hovering at the boundary can't chatter.
            var disengageAngle = directionDeadzoneAngle * 0.6f;
            var engagedRegion = leanEngaged ? leanAngle >= disengageAngle : leanAngle >= directionDeadzoneAngle;

            if (engagedRegion)
            {
                // Heading of the lean in the joint's horizontal plane.
                var azimuth = Mathf.Atan2(axis.z, axis.x) * Mathf.Rad2Deg;

                if (!leanEngaged)
                {
                    // Just pushed out of center -> one click.
                    leanEngaged = true;
                    TryClick();
                    lastClickAzimuth = azimuth;
                }
                else if (Time.time - lastClickTime < minClickInterval)
                {
                    // Refractory window right after a click: let the heading settle so the noisy
                    // engage azimuth can't be mistaken for a fresh direction change (kills "따닥").
                    lastClickAzimuth = azimuth;
                }
                else if (Mathf.Abs(Mathf.DeltaAngle(lastClickAzimuth, azimuth)) >= directionChangeAngle)
                {
                    // Swung into a meaningfully different direction -> click.
                    if (TryClick())
                        lastClickAzimuth = azimuth;
                }
            }
            else if (leanEngaged)
            {
                leanEngaged = false;
                if (clickOnReturnToCenter)
                    TryClick();
            }
        }

        bool TryClick()
        {
            if (Time.time - lastClickTime < minClickInterval)
                return false;

            SendHaptic(detentAmplitude, detentDuration);
            lastClickTime = Time.time;
            return true;
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
            var handedness = useRightHand ? InputDeviceCharacteristics.Right : InputDeviceCharacteristics.Left;
            devices.Clear();
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller | handedness, devices);

            InputFeatureUsage<bool> usage;
            switch (holdButton)
            {
                case MorphHoldButton.Grip: usage = CommonUsages.gripButton; break;
                case MorphHoldButton.PrimaryButton: usage = CommonUsages.primaryButton; break;
                case MorphHoldButton.SecondaryButton: usage = CommonUsages.secondaryButton; break;
                default: usage = CommonUsages.triggerButton; break;
            }

            foreach (var device in devices)
            {
                if (device.isValid && device.TryGetFeatureValue(usage, out var value) && value)
                    return true;
            }

            return false;
        }

        void SetShown(bool on)
        {
            shown = on;
            leanEngaged = false; // reset direction tracking so appearing never spikes a haptic

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

            if (disableInteractionOnAttach)
                StripInteraction(instance);
        }

        void PlantBase()
        {
            if (instance == null || poseSource == null)
                return;

            var t = instance.transform;
            t.rotation = Quaternion.Euler(attachLocalEuler);
            t.localScale = attachLocalScale;

            var pos = poseSource.TransformPoint(attachLocalPosition);
            pos += Vector3.up * (GetInstanceHeight() * baseHeightOffset);
            t.position = pos;
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
