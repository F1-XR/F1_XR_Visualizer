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
        [Tooltip("Ignore controller motion when it is closer than this (m) to the knob centre, where the " +
                 "swept-angle direction becomes numerically unstable.")]
        [SerializeField, Min(0.001f)] float minInteractRadius = 0.02f;
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
            attachTransform = activeInteractor.GetAttachTransform(interactable);
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
        /// Direction from the knob centre to the controller, projected onto the rotation plane and
        /// normalised. Returns false when the controller is too close to the centre to be reliable.
        /// </summary>
        bool TryGetPlaneDir(out Vector3 dir)
        {
            dir = Vector3.zero;
            if (attachTransform == null || knob == null)
                return false;

            Vector3 axis = knob.RotationAxis;
            Vector3 toController = attachTransform.position - knob.Center;
            Vector3 planar = Vector3.ProjectOnPlane(toController, axis);
            if (planar.sqrMagnitude < minInteractRadius * minInteractRadius)
                return false;

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
