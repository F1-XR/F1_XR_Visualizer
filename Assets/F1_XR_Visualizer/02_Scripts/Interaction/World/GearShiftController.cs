using System;
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
    ///                                 collar stretches like rubber as the bend bone moves.
    ///   - Knob bone (yellow section): the rigid top. It rides on the bend bone.
    ///
    /// The stick behaves like an H-pattern gate shifter: the base stays planted and the bend bone
    /// TRANSLATES straight forward/back along a single horizontal line (not a rotation), so the knob
    /// slides between a straight line of discrete gear slots at a constant height - it never arcs
    /// up-and-over. The rubber collar stretches to follow. Pushing the hand builds resistance until
    /// it crosses a detent boundary, then the lever clicks into the next gear and fires
    /// <see cref="GearChanged"/> (used for a haptic click).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GearShiftController : MonoBehaviour
    {
        [Header("Bones")]
        [Tooltip("Green section root. Stays planted - only referenced for its pivot.")]
        [SerializeField] Transform baseBone;
        [Tooltip("Red section joint. This bone slides forward/back; its rubber collar stretches to follow.")]
        [SerializeField] Transform bendBone;
        [Tooltip("Yellow section. Rides rigidly on the bend bone, staying upright as it slides.")]
        [SerializeField] Transform knobBone;

        [Header("Interaction")]
        [SerializeField] XRSimpleInteractable interactable;

        [Header("Gears")]
        [Tooltip("Number of gear slots in the straight line.")]
        [SerializeField, Range(2, 8)] int gearCount = 4;
        [Tooltip("Spacing between gear slots, as a fraction of the stick's length. The whole gate spans " +
                 "(gearCount - 1) * this.")]
        [SerializeField, Range(0.05f, 1f)] float gearThrow = 0.3f;
        [Tooltip("Gear the lever rests in when it first appears (0 = frontmost).")]
        [SerializeField] int initialGear = 0;

        [Tooltip("How much the knob tilts to follow the slide so the shaft and knob line up as one " +
                 "leaning lever instead of an S-kink. 0 = knob stays bolt upright (S-shape), 1 = fully " +
                 "aligned with the leaning shaft.")]
        [SerializeField, Range(0f, 1f)] float leanFollow = 1f;

        [Header("Detent feel")]
        [Tooltip("Hand push (deg from upright) between adjacent gears. Controls how far you swing the " +
                 "hand to change gear - independent of the visual slot spacing.")]
        [SerializeField, Range(2f, 40f)] float stepAngle = 18f;
        [Tooltip("Extra push past the halfway point needed to click into the next gear, in degrees. " +
                 "Higher = firmer detents / more resistance before it gives.")]
        [SerializeField, Range(0f, 15f)] float detentHysteresis = 4f;
        [Tooltip("How quickly the knob slides to its slot. Smaller = snappier click.")]
        [SerializeField, Min(0f)] float snapSmoothTime = 0.04f;

        [Header("Plane")]
        [Tooltip("Hinge axis in the bend bone's parent space. The gate line runs perpendicular to this, " +
                 "in the horizontal plane. Default (1,0,0) = the knob slides forward/back (along Z).")]
        [SerializeField] Vector3 hingeAxisLocal = Vector3.right;

        /// <summary>Raised when the lever clicks into a different gear. Argument is the new gear index.</summary>
        public event Action<int> GearChanged;

        IXRSelectInteractor heldInteractor;
        Quaternion bendRest;
        Quaternion knobRest;
        Vector3 bendRestPos;
        Vector3 restUpLocal;
        Vector3 slideDirLocal;   // horizontal gate direction in the bend bone's parent space
        float slotSpacing;       // distance between slots, in local units
        int currentGear;

        void Awake()
        {
            if (interactable == null)
                interactable = GetComponentInChildren<XRSimpleInteractable>();

            if (bendBone != null)
            {
                bendRest = bendBone.localRotation;
                bendRestPos = bendBone.localPosition;
                restUpLocal = bendRest * Vector3.up;
            }

            if (knobBone != null)
                knobRest = knobBone.localRotation;

            ResolveGate();

            currentGear = Mathf.Clamp(initialGear, 0, gearCount - 1);
            if (bendBone != null)
            {
                bendBone.localRotation = bendRest;
                bendBone.localPosition = GearPosition(currentGear);
            }
        }

        // Precompute the gate's horizontal slide direction and slot spacing from the rig.
        void ResolveGate()
        {
            var hinge = Hinge();

            // The gate line is perpendicular to the hinge and horizontal (so height stays constant).
            slideDirLocal = Vector3.Cross(hinge, Vector3.up);
            if (slideDirLocal.sqrMagnitude < 1e-10f)
                slideDirLocal = Vector3.forward;
            slideDirLocal.Normalize();

            // Slot spacing scales with the stick length so it reads the same at any model scale.
            // Use the REST position (bendBone slides at runtime, so its live position isn't the length).
            var stickLen = bendRestPos.magnitude +
                           (knobBone != null ? knobBone.localPosition.magnitude : 0f);
            if (stickLen < 1e-6f)
                stickLen = bendRestPos.magnitude;
            slotSpacing = stickLen * gearThrow;
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
        /// Optional external aim. When set, the knob shifts toward this transform's position instead
        /// of a grabbing interactor. Used to drive the shift like a fixed lever (base stays put,
        /// only the knob slides toward the hand). Set to null to release.
        /// </summary>
        public Transform ExternalAimTarget { get; set; }

        void LateUpdate()
        {
            if (bendBone == null)
                return;

            // Recompute the gate every frame so tweaking Gear Throw / Gear Count / Hinge in the
            // Inspector updates live during Play (they'd otherwise only apply on spawn).
            ResolveGate();
            currentGear = Mathf.Clamp(currentGear, 0, gearCount - 1);

            // While aimed, let the hand drive the detent state machine; otherwise hold the current gear.
            if (TryGetAimPosition(out var aimPos))
                UpdateGear(AimAngle(aimPos));

            // Slide toward the current gear's slot. The knob holds at a detent (resistance) until
            // UpdateGear advances it, then springs into the new slot (click). Rotation stays at rest
            // so the knob keeps upright and travels in a dead-straight horizontal line.
            var target = GearPosition(currentGear);
            var t = snapSmoothTime <= 0f ? 1f : 1f - Mathf.Exp(-Time.deltaTime / snapSmoothTime);
            bendBone.localPosition = Vector3.Lerp(bendBone.localPosition, target, t);
            bendBone.localRotation = bendRest;

            // Tilt the knob to follow the slide so the shaft and knob read as one leaning lever, not an
            // S-kink (the shaft shears while an upright knob would stick out sideways). This is purely
            // cosmetic - the knob still tracks the flat gate line, it just leans to match the shear.
            if (knobBone != null)
            {
                var offset = Vector3.Dot(bendBone.localPosition - bendRestPos, slideDirLocal);
                var restHeight = bendRestPos.magnitude;
                var leanDeg = restHeight > 1e-6f
                    ? Mathf.Atan2(offset, restHeight) * Mathf.Rad2Deg * leanFollow
                    : 0f;
                knobBone.localRotation = Quaternion.AngleAxis(leanDeg, Hinge()) * knobRest;
            }
        }

        /// <summary>Local position of gear slot i: the rest position offset along the gate line.</summary>
        Vector3 GearPosition(int i) =>
            bendRestPos + slideDirLocal * ((i - (gearCount - 1) * 0.5f) * slotSpacing);

        /// <summary>Hand-push angle (deg from upright, signed about the hinge) mapped to gear i, for detents.</summary>
        float GearAngle(int i) => (i - (gearCount - 1) * 0.5f) * stepAngle;

        Vector3 Hinge() => hingeAxisLocal.sqrMagnitude > 1e-10f ? hingeAxisLocal.normalized : Vector3.right;

        /// <summary>
        /// Advances or retreats the current gear based on the aim angle, with hysteresis so the lever
        /// resists at each detent and only clicks over once pushed past the boundary. Fires
        /// <see cref="GearChanged"/> on each click.
        /// </summary>
        void UpdateGear(float aimAngle)
        {
            var changed = false;

            // Push toward higher gears: cross the halfway point plus the hysteresis margin.
            while (currentGear < gearCount - 1 &&
                   aimAngle > GearAngle(currentGear) + stepAngle * 0.5f + detentHysteresis)
            {
                currentGear++;
                changed = true;
            }

            // Pull toward lower gears.
            while (currentGear > 0 &&
                   aimAngle < GearAngle(currentGear) - stepAngle * 0.5f - detentHysteresis)
            {
                currentGear--;
                changed = true;
            }

            if (changed)
                GearChanged?.Invoke(currentGear);
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

        /// <summary>
        /// The hand's push angle (deg, signed about the hinge, within the lever plane), measured from a
        /// stable pivot so the moving knob doesn't feed back into the reading. Returns the current
        /// gear's angle when the aim is degenerate so the state machine holds.
        /// </summary>
        float AimAngle(Vector3 aimWorldPosition)
        {
            if (bendBone.parent == null)
                return GearAngle(currentGear);

            // Pivot at the stick's rest base (fixed), not the sliding bone, to avoid feedback.
            var pivotWorld = bendBone.parent.TransformPoint(bendRestPos);
            var worldDir = aimWorldPosition - pivotWorld;
            var localDir = bendBone.parent.InverseTransformDirection(worldDir);

            // Collapse to the lever plane (drop the component along the hinge) and measure the signed
            // tilt from upright. Only the in-plane push decides the gear.
            var hinge = Hinge();
            var planar = Vector3.ProjectOnPlane(localDir, hinge);
            if (planar.sqrMagnitude < 1e-10f)
                return GearAngle(currentGear);

            return Vector3.SignedAngle(restUpLocal, planar.normalized, hinge);
        }

        /// <summary>Current gear index, 0 (frontmost) to <see cref="GearCount"/> - 1.</summary>
        public int CurrentGear => currentGear;

        /// <summary>Total number of gear positions.</summary>
        public int GearCount => gearCount;
    }
}
