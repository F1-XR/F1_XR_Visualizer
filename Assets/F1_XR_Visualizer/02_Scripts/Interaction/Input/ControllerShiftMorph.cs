using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using F1XR.Interaction.World;

namespace F1XR.Interaction.Input
{
    public enum MorphExitButton
    {
        Trigger,
        Grip,
        PrimaryButton,
        SecondaryButton
    }

    /// <summary>
    /// Swaps a controller's visual model for a gear-shift prefab when the controller is tilted
    /// past a threshold. Once morphed it STAYS a gear shift (regardless of angle) until the exit
    /// button is pressed, then it swaps back to the controller. The gear shift is spawned once as a
    /// child of the controller pose, so it follows the hand like a held object.
    ///
    /// Put this on the controller pose object (e.g. "Right Controller"). The gear-shift copy is a
    /// static visual by default (its grab/bend interaction is disabled on the attached instance so
    /// it doesn't fight the hand it's parented to).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ControllerShiftMorph : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Transform whose orientation is read for the tilt gesture. Defaults to this transform (the controller pose).")]
        [SerializeField] Transform poseSource;
        [Tooltip("The controller's visual model root that gets hidden while morphed (e.g. \"XR Controller Right\").")]
        [SerializeField] GameObject controllerModelRoot;
        [Tooltip("Gear-shift prefab spawned onto the controller.")]
        [SerializeField] GameObject gearShiftPrefab;

        [Header("Base plant (captured when morphing on)")]
        [Tooltip("Where the base plants, as an offset in the controller's local space at the moment of morph.")]
        [SerializeField] Vector3 attachLocalPosition = Vector3.zero;
        [Tooltip("World orientation of the planted base (default = upright). The base does NOT rotate with the wrist.")]
        [SerializeField] Vector3 attachLocalEuler = Vector3.zero;
        [SerializeField] Vector3 attachLocalScale = new Vector3(10f, 10f, 10f);
        [Tooltip("Extra vertical offset of the planted base, in multiples of the gear shift's own height. " +
            "-0.5 spawns it half a gear shift lower. Negative = lower, positive = higher.")]
        [SerializeField, Range(-2f, 2f)] float baseHeightOffset = -0.5f;

        [Header("Tilt gesture (auto-calibrated neutral)")]
        [Tooltip("Which local axis of the controller is measured against world up. Forward (0,0,1) by default.")]
        [SerializeField] Vector3 tiltMeasureAxis = Vector3.forward;
        [Tooltip("How fast the neutral hold adapts to however you're currently holding the controller (seconds). " +
            "Larger = a held pose stays neutral longer, so only quicker/bigger tilts trigger. " +
            "Steady holding always reads ~0 deviation, so it never triggers on its own.")]
        [SerializeField, Min(0.05f)] float neutralFollowTime = 1.5f;
        [Tooltip("How far (deg) the controller must rotate away from the adapting neutral to turn the gear shift ON.")]
        [SerializeField, Range(0f, 180f)] float tiltOnDelta = 35f;

        [Header("Exit (return to controller)")]
        [Tooltip("Once morphed, the gear shift stays until this controller button is pressed.")]
        [SerializeField] MorphExitButton exitButton = MorphExitButton.Trigger;
        [Tooltip("Which hand's button to read. Right by default (this is the right controller).")]
        [SerializeField] bool useRightHand = true;

        [Header("Detent haptics (a click when the direction changes)")]
        [Tooltip("While morphed, fire one crisp click when the knob is pushed into a new direction " +
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

        [Header("Debug")]
        [Tooltip("Force the morph on to position the attach transform.")]
        [SerializeField] bool debugForceMorph;
        [Tooltip("Live readout: current angle (deg) between the measured axis and world up.")]
        [SerializeField] float debugCurrentAxisAngle;
        [Tooltip("Live readout: the auto-adapting neutral angle (deg).")]
        [SerializeField] float debugNeutralAngle;
        [Tooltip("Live readout: current deviation (deg) from neutral. Morph turns ON when this crosses On-delta.")]
        [SerializeField] float debugCurrentDeviation;

        GameObject instance;
        GearShiftController instanceShift;
        bool morphed;
        float neutralAngle;
        bool neutralInit;
        bool exitPressedLast;
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
            // Leave the controller visible if this object is deactivated mid-morph.
            if (morphed)
                SetMorph(false);
        }

        void Update()
        {
            var axis = poseSource.TransformDirection(tiltMeasureAxis);
            var angle = Vector3.Angle(axis, Vector3.up);

            // Seed neutral on the first valid frame so startup never spikes a morph.
            if (!neutralInit)
            {
                neutralAngle = angle;
                neutralInit = true;
            }

            // While not morphed, the neutral eases toward the current pose: whatever you hold
            // steadily becomes "neutral", so a steady grip reads ~0 deviation. Only a tilt faster
            // than this follow time builds up deviation. Neutral is frozen while morphed.
            if (!morphed)
            {
                var k = 1f - Mathf.Exp(-Time.deltaTime / neutralFollowTime);
                neutralAngle = Mathf.Lerp(neutralAngle, angle, k);
            }

            var deviation = Mathf.Abs(angle - neutralAngle);
            debugCurrentAxisAngle = angle;
            debugNeutralAngle = neutralAngle;
            debugCurrentDeviation = deviation;

            // Edge-detect the exit button every frame so a press is never missed.
            var exitEdge = ReadExitButtonEdge();

            if (debugForceMorph)
            {
                if (!morphed) SetMorph(true);
                return;
            }

            if (!morphed)
            {
                if (deviation >= tiltOnDelta)
                    SetMorph(true);
            }
            else if (exitEdge)
            {
                // Stay morphed regardless of angle until the exit button returns us to the controller.
                SetMorph(false);
                neutralInit = false; // re-seed neutral to the current pose so it doesn't instantly re-trigger
            }

            if (morphed && leanHaptics)
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

        bool ReadExitButtonEdge()
        {
            var pressed = ReadExitButton();
            var edge = pressed && !exitPressedLast;
            exitPressedLast = pressed;
            return edge;
        }

        bool ReadExitButton()
        {
            var handedness = useRightHand ? InputDeviceCharacteristics.Right : InputDeviceCharacteristics.Left;
            devices.Clear();
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller | handedness, devices);

            InputFeatureUsage<bool> usage;
            switch (exitButton)
            {
                case MorphExitButton.Grip: usage = CommonUsages.gripButton; break;
                case MorphExitButton.PrimaryButton: usage = CommonUsages.primaryButton; break;
                case MorphExitButton.SecondaryButton: usage = CommonUsages.secondaryButton; break;
                default: usage = CommonUsages.triggerButton; break;
            }

            foreach (var device in devices)
            {
                if (device.isValid && device.TryGetFeatureValue(usage, out var value) && value)
                    return true;
            }

            return false;
        }

        void SetMorph(bool on)
        {
            morphed = on;
            leanEngaged = false; // reset direction tracking so a morph never spikes a haptic

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
