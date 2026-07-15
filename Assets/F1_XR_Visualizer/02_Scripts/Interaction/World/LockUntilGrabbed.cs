using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace F1XR.Interaction.World
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class LockUntilGrabbed : MonoBehaviour
    {
        [SerializeField] XRGrabInteractable grabInteractable;
        [SerializeField] float respawnDistance = 10f;

        Rigidbody body;
        Vector3 lockedPosition;
        Quaternion lockedRotation;
        bool everGrabbed;
        bool wasHeld;

        void Awake()
        {
            body = GetComponent<Rigidbody>();

            if (grabInteractable == null)
                grabInteractable = GetComponent<XRGrabInteractable>();

            lockedPosition = body.position;
            lockedRotation = body.rotation;

            IgnoreCollisionWithSiblings();
        }

        void IgnoreCollisionWithSiblings()
        {
            var myColliders = GetComponentsInChildren<Collider>();
            foreach (var other in FindObjectsByType<LockUntilGrabbed>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (other == this)
                    continue;

                var otherColliders = other.GetComponentsInChildren<Collider>();
                foreach (var myCol in myColliders)
                {
                    foreach (var otherCol in otherColliders)
                        Physics.IgnoreCollision(myCol, otherCol, true);
                }
            }
        }

        void FixedUpdate()
        {
            bool held = grabInteractable != null && grabInteractable.isSelected;

            if (!everGrabbed)
            {
                if (held)
                {
                    everGrabbed = true;
                    return;
                }

                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.position = lockedPosition;
                body.rotation = lockedRotation;
                return;
            }

            if (held)
            {
                wasHeld = true;
                return;
            }

            // The pre-grab lock above (and the kinematic toggling XR grab does while held)
            // can leave the Rigidbody flagged asleep right as it's released, so gravity
            // never gets a chance to run. Wake it up only on the release frame itself;
            // waking it every frame would stop it from ever settling back to sleep.
            if (wasHeld)
            {
                body.WakeUp();
                wasHeld = false;
            }

            if (Vector3.Distance(body.position, lockedPosition) >= respawnDistance)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.position = lockedPosition;
                body.rotation = lockedRotation;
            }
        }
    }
}
