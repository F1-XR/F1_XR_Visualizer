using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace F1XR.Interaction.World
{
    /// <summary>
    /// Drives a gear-shift stick that is skinned to a three-bone chain:
    ///   - Base bone (green section):  fixed to the ground, never moves.
    ///   - Bend bone (red section):    a soft rubber joint. Vertices in the red band are
    ///                                 weight-blended between the base and this bone, so the
    ///                                 collar bends smoothly (like rubber) as the stick leans.
    ///   - Knob bone (yellow section): the rigid top. It rides on the bend bone, so it can be
    ///                                 pushed to any direction around a full 360 circle.
    ///
    /// While an XR interactor holds the knob the stick leans toward the hand (clamped to
    /// <see cref="maxLeanAngle"/>); when released it springs back upright.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GearShiftController : MonoBehaviour
    {
        [Header("Bones")]
        [Tooltip("Green section root. Stays fixed - only referenced for its pivot.")]
        [SerializeField] Transform baseBone;
        [Tooltip("Red section joint. This is the bone that actually leans; the rubber blend lives around it.")]
        [SerializeField] Transform bendBone;
        [Tooltip("Yellow section. Rides rigidly on the bend bone.")]
        [SerializeField] Transform knobBone;

        [Header("Interaction")]
        [SerializeField] XRSimpleInteractable interactable;

        [Header("Lean")]
        [Tooltip("Maximum tilt of the stick from upright, in degrees.")]
        [SerializeField, Range(0f, 80f)] float maxLeanAngle = 35f;
        [Tooltip("Responsiveness while held. Smaller = snappier follow.")]
        [SerializeField, Min(0f)] float followSmoothTime = 0.05f;
        [Tooltip("Spring-back time after release. Smaller = quicker return.")]
        [SerializeField, Min(0f)] float returnSmoothTime = 0.12f;

        IXRSelectInteractor heldInteractor;
        Quaternion bendRest;
        Vector3 restUpLocal;

        void Awake()
        {
            if (interactable == null)
                interactable = GetComponentInChildren<XRSimpleInteractable>();

            if (bendBone != null)
            {
                bendRest = bendBone.localRotation;
                restUpLocal = bendRest * Vector3.up;
            }
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
            if (interactable == null)
                return;

            interactable.selectEntered.RemoveListener(OnSelectEntered);
            interactable.selectExited.RemoveListener(OnSelectExited);
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            heldInteractor = args.interactorObject;
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            if (heldInteractor == args.interactorObject)
                heldInteractor = null;
        }

        /// <summary>
        /// Optional external aim. When set, the knob bends toward this transform's position instead
        /// of a grabbing interactor. Used to drive the shift like a fixed lever (base stays put,
        /// only the knob leans toward the hand). Set to null to release.
        /// </summary>
        public Transform ExternalAimTarget { get; set; }

        void LateUpdate()
        {
            if (bendBone == null)
                return;

            var active = TryGetAimPosition(out var aimPos);
            var target = active ? ComputeAimRotation(aimPos) : bendRest;

            // Framerate-independent exponential smoothing toward the target.
            var smoothTime = active ? followSmoothTime : returnSmoothTime;
            var t = smoothTime <= 0f ? 1f : 1f - Mathf.Exp(-Time.deltaTime / smoothTime);
            bendBone.localRotation = Quaternion.Slerp(bendBone.localRotation, target, t);
        }

        bool TryGetAimPosition(out Vector3 position)
        {
            if (ExternalAimTarget != null)
            {
                position = ExternalAimTarget.position;
                return true;
            }

            if (heldInteractor != null && interactable != null)
            {
                var attach = heldInteractor.GetAttachTransform(interactable);
                if (attach != null)
                {
                    position = attach.position;
                    return true;
                }
            }

            position = default;
            return false;
        }

        Quaternion ComputeAimRotation(Vector3 aimWorldPosition)
        {
            if (bendBone.parent == null)
                return bendRest;

            // Direction from the joint pivot toward the aim, expressed in the bend bone's parent space.
            var worldDir = aimWorldPosition - bendBone.position;
            var localDir = bendBone.parent.InverseTransformDirection(worldDir);
            if (localDir.sqrMagnitude < 1e-10f)
                return bendRest;

            // Clamp the lean to the allowed cone around the upright rest axis.
            var want = Vector3.RotateTowards(restUpLocal, localDir.normalized, maxLeanAngle * Mathf.Deg2Rad, 0f);
            return Quaternion.FromToRotation(restUpLocal, want) * bendRest;
        }

        /// <summary>
        /// The knob's current lean axis in the joint's local space. It changes as the knob moves in
        /// any direction, so the angular difference between frames measures how fast the knob is moving.
        /// </summary>
        public Vector3 CurrentLeanAxisLocal => bendBone != null ? bendBone.localRotation * Vector3.up : Vector3.up;

        /// <summary>Current lean amount, 0 (upright) to 1 (at max angle). Useful for gear-state logic.</summary>
        public float NormalizedLean
        {
            get
            {
                if (bendBone == null || maxLeanAngle <= 0f)
                    return 0f;

                var up = bendBone.localRotation * Vector3.up;
                return Mathf.Clamp01(Vector3.Angle(restUpLocal, up) / maxLeanAngle);
            }
        }
    }
}
