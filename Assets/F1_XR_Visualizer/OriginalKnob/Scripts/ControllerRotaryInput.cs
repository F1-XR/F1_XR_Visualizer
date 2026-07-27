using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace F1XR.OriginalKnob
{
    /// <summary>
    /// Turns a held controller's circular motion around the knob centre into a per-frame signed angle,
    /// which it hands to <see cref="RotaryKnobController"/>. It deliberately does NOT move the knob
    /// toward the controller (unlike a grab interactable) - it only reads the controller's position and
    /// measures how far it swept around the knob's rotation axis.
    ///
    /// Angle is derived from the direction vector (knob centre -> controller), projected onto the knob's
    /// rotation plane, so the knob turns the same amount regardless of how the hand approaches. A minimum
    /// radius guards against the unstable region right at the centre, and the first direction is seeded on
    /// grab so there is no jump.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ControllerRotaryInput : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] XRSimpleInteractable interactable;
        [SerializeField] RotaryKnobController knob;

        [Header("Stability")]
        [Tooltip("Per-frame angle changes smaller than this (deg) are treated as zero to kill jitter.")]
        [SerializeField, Min(0f)] float angleDeadzone = 0.02f;
        [Tooltip("Clamp of the per-frame angle (deg) to reject glitchy tracking spikes.")]
        [SerializeField, Min(0f)] float maxDeltaPerFrame = 45f;

        IXRSelectInteractor activeInteractor;
        Transform attachTransform;
        Vector3 previousDir;
        bool hasPreviousDir;

        public IXRSelectInteractor ActiveInteractor => activeInteractor;

        void Reset()
        {
            interactable = GetComponentInChildren<XRSimpleInteractable>();
            knob = GetComponentInParent<RotaryKnobController>();
        }

        void Awake()
        {
            if (interactable == null)
                interactable = GetComponentInChildren<XRSimpleInteractable>();
            if (knob == null)
                knob = GetComponentInParent<RotaryKnobController>();
        }

        void OnEnable()
        {
            if (interactable == null)
                return;

            interactable.selectEntered.AddListener(OnSelectEntered);
            interactable.selectExited.AddListener(OnSelectExited);
        }

        void OnDisable()
        {
            if (interactable != null)
            {
                interactable.selectEntered.RemoveListener(OnSelectEntered);
                interactable.selectExited.RemoveListener(OnSelectExited);
            }

            // Make sure we never leave the knob stuck in a grabbed state when disabled.
            ForceEnd();
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            // Only one controller may drive the knob at a time.
            if (activeInteractor != null)
                return;

            activeInteractor = args.interactorObject;
            // Track the interactor's OWN transform (the physical controller), not GetAttachTransform:
            // the NearFar interactor's InteractionAttachController snaps its attach anchor onto the
            // grabbed knob's centre on select, which would collapse the orbit vector to zero and make
            // the knob feel "grabbed at the centre". The controller transform always orbits the centre.
            attachTransform = activeInteractor.transform;
            hasPreviousDir = TryGetPlaneDir(out previousDir);
            knob.BeginRotation();
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            if (args.interactorObject == activeInteractor)
                ForceEnd();
        }

        void LateUpdate()
        {
            if (activeInteractor == null || knob == null)
                return;

            // Tracking lost / interactor went away: release rather than freeze in the grabbed state.
            if (attachTransform == null)
            {
                ForceEnd();
                return;
            }

            if (!TryGetPlaneDir(out Vector3 currentDir))
                return; // inside the dead centre - hold position, keep last good direction

            if (!hasPreviousDir)
            {
                previousDir = currentDir;
                hasPreviousDir = true;
                return;
            }

            float delta = Vector3.SignedAngle(previousDir, currentDir, knob.RotationAxis);
            previousDir = currentDir;

            if (Mathf.Abs(delta) < angleDeadzone)
                return;
            if (maxDeltaPerFrame > 0f)
                delta = Mathf.Clamp(delta, -maxDeltaPerFrame, maxDeltaPerFrame);

            knob.ApplyDelta(delta);
        }

        /// <summary>
        /// Direction, in the knob's rotation plane, from the knob centre to the point where the controller's
        /// ray crosses that plane. Sweeping the ray (or moving the hand) moves this point around the knob,
        /// and we track how its angle changes between frames to turn the knob. Works for both the far ray and
        /// near interaction, is independent of grab distance and of any attach snapping to the knob centre.
        /// Returns false when the ray is parallel to the plane or aimed at the exact centre.
        /// </summary>
        bool TryGetPlaneDir(out Vector3 dir)
        {
            dir = Vector3.zero;
            if (attachTransform == null || knob == null)
                return false;

            Vector3 axis = knob.RotationAxis;
            Vector3 origin = attachTransform.position;
            Vector3 rayDir = attachTransform.forward;

            // Intersect the controller ray with the knob's rotation plane (through the centre, normal = axis).
            float denom = Vector3.Dot(rayDir, axis);
            if (Mathf.Abs(denom) < 1e-4f)
                return false; // ray runs parallel to the plane

            float t = Vector3.Dot(knob.Center - origin, axis) / denom;
            Vector3 hit = origin + rayDir * t;

            Vector3 planar = Vector3.ProjectOnPlane(hit - knob.Center, axis);
            if (planar.sqrMagnitude < 1e-6f)
                return false; // aimed at the exact centre - angle undefined

            dir = planar.normalized;
            return true;
        }

        void ForceEnd()
        {
            if (activeInteractor == null)
                return;

            activeInteractor = null;
            attachTransform = null;
            hasPreviousDir = false;
            if (knob != null)
                knob.EndRotation();
        }
    }
}
