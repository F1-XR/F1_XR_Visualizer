using System;
using UnityEngine;
using UnityEngine.Events;

namespace F1XR.OriginalKnob
{
    /// <summary>
    /// Owns the knob's rotation state. It rotates <see cref="knobPivot"/> in place about its own
    /// local Z (the panel-front axis), accumulates an unbounded rotation angle so the knob can spin
    /// any number of turns, and raises events other systems (volume, playback speed, ...) can later
    /// subscribe to without this component knowing anything about them.
    ///
    /// Only <c>KnobPivot.localRotation</c> is ever touched - never its position or scale - so the knob
    /// stays glued to the panel while it turns. <see cref="ControllerRotaryInput"/> feeds per-frame
    /// signed angle deltas into <see cref="ApplyDelta"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RotaryKnobController : MonoBehaviour
    {
        [Serializable] public class RotationChangedEvent : UnityEvent<float, float> { }

        [Header("References")]
        [Tooltip("The pivot that actually rotates. Its local Z axis is the rotation axis (panel front).")]
        [SerializeField] Transform knobPivot;

        [Header("Direction")]
        [Tooltip("Flip the mapping between controller motion and the knob's visible rotation.")]
        [SerializeField] bool invertRotation;
        [Tooltip("When on, a clockwise visible turn is reported as a positive value to consumers " +
                 "(events / TotalAngle). Purely a reporting convention - it does not change the visuals.")]
        [SerializeField] bool clockwiseIsPositive = true;

        [Header("Velocity")]
        [Tooltip("Smoothing time (s) for the reported angular velocity. Smaller = snappier.")]
        [SerializeField, Min(0f)] float velocitySmoothTime = 0.06f;
        [Tooltip("How quickly the angular velocity relaxes to zero once rotation stops (s).")]
        [SerializeField, Min(0f)] float velocityReleaseTime = 0.12f;

        [Header("Events")]
        [SerializeField] UnityEvent onRotationStarted = new UnityEvent();
        [SerializeField] RotationChangedEvent onRotationChanged = new RotationChangedEvent();
        [SerializeField] UnityEvent onRotationEnded = new UnityEvent();

        // C# events for code subscribers (visuals, haptics, future feature adapters).
        public event Action RotationStarted;
        public event Action<float, float> RotationChanged; // (reportedDelta, reportedTotal)
        public event Action RotationEnded;

        Quaternion basePivotLocalRotation = Quaternion.identity;
        float visualTotal;    // signed accumulated angle actually applied to the pivot (CCW+ about local Z)
        float reportedTotal;  // same, remapped to the consumer-facing convention
        float reportedDelta;
        float angularVelocity; // signed, visual convention (deg/s)
        int direction;         // -1 / 0 / +1, visual convention
        bool isRotating;

        public Transform Pivot => knobPivot;
        public Vector3 Center => knobPivot != null ? knobPivot.position : transform.position;
        /// <summary>World-space rotation axis (panel front). Stays fixed as the knob spins about it.</summary>
        public Vector3 RotationAxis => knobPivot != null ? knobPivot.forward : transform.forward;

        public bool IsRotating => isRotating;
        /// <summary>Unbounded accumulated angle in the reporting convention. Never clamped or reset.</summary>
        public float TotalAngle => reportedTotal;
        public float DeltaAngle => reportedDelta;
        /// <summary>Displayed angle wrapped to 0..360 (visual convention). Handy for gauges.</summary>
        public float DisplayAngle => Mathf.Repeat(visualTotal, 360f);
        /// <summary>0..1 position within a single turn (visual convention).</summary>
        public float NormalizedValue => DisplayAngle / 360f;
        /// <summary>Signed angular velocity in the visual convention (CCW positive), deg/s.</summary>
        public float AngularVelocity => angularVelocity;
        /// <summary>-1, 0 or +1 in the visual convention: which way the knob is currently turning.</summary>
        public int Direction => direction;

        void Awake()
        {
            if (knobPivot == null)
                knobPivot = transform;

            basePivotLocalRotation = knobPivot.localRotation;
        }

        /// <summary>Called when a controller grabs the knob.</summary>
        public void BeginRotation()
        {
            isRotating = true;
            reportedDelta = 0f;
            RotationStarted?.Invoke();
            onRotationStarted?.Invoke();
        }

        /// <summary>
        /// Feed a signed angle delta (degrees) measured about <see cref="RotationAxis"/> for this frame.
        /// Positive = counter-clockwise about the panel-front axis.
        /// </summary>
        public void ApplyDelta(float rawSignedDelta)
        {
            if (!isRotating)
                return;

            float applied = invertRotation ? -rawSignedDelta : rawSignedDelta;

            visualTotal += applied;
            ApplyToPivot();

            float sign = clockwiseIsPositive ? -1f : 1f; // CW is -angle about +Z; make it positive if requested
            reportedDelta = applied * sign;
            reportedTotal += reportedDelta;

            float dt = Mathf.Max(Time.deltaTime, 1e-5f);
            float instantVelocity = applied / dt;
            float t = velocitySmoothTime <= 0f ? 1f : 1f - Mathf.Exp(-dt / velocitySmoothTime);
            angularVelocity = Mathf.Lerp(angularVelocity, instantVelocity, t);

            const float eps = 1e-4f;
            direction = applied > eps ? 1 : applied < -eps ? -1 : 0;

            RotationChanged?.Invoke(reportedDelta, reportedTotal);
            onRotationChanged?.Invoke(reportedDelta, reportedTotal);
        }

        /// <summary>Called when the controller releases the knob. The knob keeps its final angle.</summary>
        public void EndRotation()
        {
            if (!isRotating)
                return;

            isRotating = false;
            reportedDelta = 0f;
            direction = 0;
            RotationEnded?.Invoke();
            onRotationEnded?.Invoke();
        }

        void Update()
        {
            // Relax velocity to zero when idle so speed-driven visuals fade out naturally.
            if (!isRotating && angularVelocity != 0f)
            {
                float dt = Mathf.Max(Time.deltaTime, 1e-5f);
                float t = velocityReleaseTime <= 0f ? 1f : 1f - Mathf.Exp(-dt / velocityReleaseTime);
                angularVelocity = Mathf.Lerp(angularVelocity, 0f, t);
                if (Mathf.Abs(angularVelocity) < 0.01f)
                    angularVelocity = 0f;
            }
        }

        void ApplyToPivot()
        {
            if (knobPivot == null)
                return;

            // Rotate about the pivot's own local Z. Because we only ever rotate about that axis, the
            // axis itself never drifts and position/scale are untouched.
            knobPivot.localRotation = basePivotLocalRotation * Quaternion.AngleAxis(visualTotal, Vector3.forward);
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (!Application.isPlaying && knobPivot != null)
                basePivotLocalRotation = knobPivot.localRotation;
        }
#endif
    }
}
