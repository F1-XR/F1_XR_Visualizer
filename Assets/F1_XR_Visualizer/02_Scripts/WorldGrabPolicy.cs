using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace F1XR.AR
{
    public sealed class WorldGrabPolicy : MonoBehaviour, IXRSelectFilter
    {
        [SerializeField] XRGrabInteractable grab;
        [SerializeField] Transform target;
        [SerializeField] bool blockFarGrab = true;
        [SerializeField] bool keepOnlyYRotation = true;

        bool moving;
        bool hasStartInteractorYaw;
        float startInteractorYaw;
        Vector3 startEulerAngles;

        public bool canProcess => isActiveAndEnabled;

        void Awake()
        {
            if (grab == null)
                grab = GetComponent<XRGrabInteractable>();

            if (target == null)
                target = transform;
        }

        void OnEnable()
        {
            if (grab != null)
                grab.selectFilters.Add(this);
        }

        void OnDisable()
        {
            if (grab != null)
                grab.selectFilters.Remove(this);

            moving = false;
        }

        void LateUpdate()
        {
            if (!keepOnlyYRotation || grab == null || !grab.isSelected)
            {
                moving = false;
                return;
            }

            if (!moving)
            {
                moving = true;
                startEulerAngles = target.eulerAngles;
                hasStartInteractorYaw = TryGetSelectingInteractorTransform(out var interactorTransform);
                if (hasStartInteractorYaw)
                    startInteractorYaw = interactorTransform.eulerAngles.y;
            }

            target.rotation = Quaternion.Euler(startEulerAngles.x, GetYaw(), startEulerAngles.z);
        }

        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
        {
            if (!blockFarGrab || !ReferenceEquals(interactable, grab))
                return true;

            if (interactor is XRRayInteractor)
                return false;

            return interactor is not NearFarInteractor nearFarInteractor ||
                   nearFarInteractor.selectionRegion.Value != NearFarInteractor.Region.Far;
        }

        float GetYaw()
        {
            if (!hasStartInteractorYaw || !TryGetSelectingInteractorTransform(out var interactorTransform))
                return target.eulerAngles.y;

            return startEulerAngles.y + Mathf.DeltaAngle(startInteractorYaw, interactorTransform.eulerAngles.y);
        }

        bool TryGetSelectingInteractorTransform(out Transform interactorTransform)
        {
            if (grab != null &&
                grab.interactorsSelecting.Count > 0 &&
                grab.interactorsSelecting[0] is XRBaseInteractor baseInteractor)
            {
                interactorTransform = baseInteractor.transform;
                return true;
            }

            interactorTransform = null;
            return false;
        }
    }
}
