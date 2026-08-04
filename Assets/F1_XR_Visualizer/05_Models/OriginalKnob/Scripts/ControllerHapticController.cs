using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace F1XR.OriginalKnob
{
    /// <summary>
    /// Minimal mechanical haptics for the knob: a faint tick on hover, a crisp click on grab, and light
    /// detent ticks every few degrees while turning - all routed only to the controller currently
    /// interacting, and rate-limited so fast spins do not flood the actuator.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ControllerHapticController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] XRSimpleInteractable interactable;
        [SerializeField] RotaryKnobController knob;

        [Header("Hover")]
        [SerializeField, Range(0f, 1f)] float hoverAmplitude = 0.08f;
        [SerializeField, Min(0f)] float hoverDuration = 0.03f;

        [Header("Grab")]
        [SerializeField, Range(0f, 1f)] float grabAmplitude = 0.4f;
        [SerializeField, Min(0f)] float grabDuration = 0.06f;

        [Header("Rotation detent tick")]
        [Tooltip("Fire a tick every time the knob turns this many degrees.")]
        [SerializeField, Min(1f)] float tickDegrees = 12f;
        [SerializeField, Range(0f, 1f)] float tickAmplitude = 0.15f;
        [SerializeField, Min(0f)] float tickDuration = 0.02f;
        [Tooltip("Minimum time (s) between any two haptic pulses so fast spins don't overload the motor.")]
        [SerializeField, Min(0f)] float minPulseInterval = 0.03f;

        IXRSelectInteractor activeInteractor;
        float degreesSinceTick;
        float lastPulseTime = -999f;

        void Awake()
        {
            if (interactable == null)
                interactable = GetComponentInChildren<XRSimpleInteractable>();
            if (knob == null)
                knob = GetComponentInParent<RotaryKnobController>();
        }

        void OnEnable()
        {
            if (interactable != null)
            {
                interactable.hoverEntered.AddListener(OnHoverEntered);
                interactable.selectEntered.AddListener(OnSelectEntered);
                interactable.selectExited.AddListener(OnSelectExited);
            }

            if (knob != null)
                knob.RotationChanged += OnRotationChanged;
        }

        void OnDisable()
        {
            if (interactable != null)
            {
                interactable.hoverEntered.RemoveListener(OnHoverEntered);
                interactable.selectEntered.RemoveListener(OnSelectEntered);
                interactable.selectExited.RemoveListener(OnSelectExited);
            }

            if (knob != null)
                knob.RotationChanged -= OnRotationChanged;

            activeInteractor = null;
        }

        void OnHoverEntered(HoverEnterEventArgs args)
        {
            Pulse(args.interactorObject as XRBaseInputInteractor, hoverAmplitude, hoverDuration);
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (activeInteractor != null)
                return;

            activeInteractor = args.interactorObject;
            degreesSinceTick = 0f;
            Pulse(activeInteractor as XRBaseInputInteractor, grabAmplitude, grabDuration);
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            if (args.interactorObject == activeInteractor)
                activeInteractor = null;
        }

        void OnRotationChanged(float deltaAngle, float totalAngle)
        {
            if (activeInteractor == null)
                return;

            degreesSinceTick += Mathf.Abs(deltaAngle);
            if (degreesSinceTick < tickDegrees)
                return;

            degreesSinceTick = 0f;
            Pulse(activeInteractor as XRBaseInputInteractor, tickAmplitude, tickDuration);
        }

        void Pulse(XRBaseInputInteractor inputInteractor, float amplitude, float duration)
        {
            if (inputInteractor == null || amplitude <= 0f || duration <= 0f)
                return;
            if (Time.time - lastPulseTime < minPulseInterval)
                return;

            lastPulseTime = Time.time;
            inputInteractor.SendHapticImpulse(Mathf.Clamp01(amplitude), duration);
        }
    }
}
