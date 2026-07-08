using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace F1XR.AR
{
    public sealed class PanelYawGrabLock : MonoBehaviour
    {
        [SerializeField] XRGrabInteractable grab;
        [SerializeField] Transform target;

        bool moving;
        Quaternion startRotation;
        Vector3 startDirection;

        void Awake()
        {
            if (target == null)
                target = transform;

            if (grab == null)
                grab = GetComponent<XRGrabInteractable>();
        }

        void LateUpdate()
        {
            UpdateRotation();
        }

        void UpdateRotation()
        {
            if (grab == null || !grab.isSelected || grab.interactorsSelecting.Count == 0)
            {
                moving = false;
                return;
            }

            var interactor = grab.interactorsSelecting[0];
            var direction = Vector3.ProjectOnPlane(interactor.transform.position - target.position, Vector3.up);
            if (direction.sqrMagnitude <= Mathf.Epsilon)
                return;

            if (!moving)
            {
                moving = true;
                startRotation = target.rotation;
                startDirection = direction.normalized;
                return;
            }

            var yaw = Vector3.SignedAngle(startDirection, direction.normalized, Vector3.up);
            target.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * startRotation;
        }
    }
}
