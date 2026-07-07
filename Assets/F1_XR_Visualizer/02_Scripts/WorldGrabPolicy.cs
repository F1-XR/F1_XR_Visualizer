using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace F1XR.AR
{
    public sealed class WorldGrabPolicy : MonoBehaviour, IXRSelectFilter
    {
        [SerializeField] XRGrabInteractable grab;
        [SerializeField] Transform target;
        [SerializeField] bool allowFarGrab = true;
        [SerializeField] bool keepOnlyYRotation = true;
        [SerializeField] float farMoveSpeed = 0.7f;
        [SerializeField] float farRotateSpeed = 90f;
        [SerializeField] float thumbstickDeadzone = 0.15f;

        bool moving;
        bool hasStartInteractorYaw;
        float startInteractorYaw;
        Vector3 startEulerAngles;
        bool farMoving;
        bool hadGrabSettings;
        bool startTrackPosition;
        bool startTrackRotation;
        bool startThrowOnDetach;

        static readonly System.Collections.Generic.List<InputDevice> InputDevices = new();

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

            StopMoving();
        }

        void LateUpdate()
        {
            if (grab == null || !grab.isSelected)
            {
                StopMoving();
                return;
            }

            var farGrab = IsFarSelectingInteractor();
            if (farGrab)
                UpdateFarGrab();
            else
                UpdateNearGrab();
        }

        void UpdateNearGrab()
        {
            RestoreGrabSettings();

            if (!keepOnlyYRotation)
            {
                StopMoving();
                return;
            }

            if (!moving)
            {
                moving = true;
                farMoving = false;
                startEulerAngles = target.eulerAngles;
                hasStartInteractorYaw = TryGetSelectingInteractorTransform(out var interactorTransform);
                if (hasStartInteractorYaw)
                    startInteractorYaw = interactorTransform.eulerAngles.y;
            }

            target.rotation = Quaternion.Euler(startEulerAngles.x, GetYaw(), startEulerAngles.z);
        }

        void UpdateFarGrab()
        {
            ApplyFarGrabSettings();

            if (!farMoving)
            {
                moving = false;
                farMoving = true;
                startEulerAngles = target.eulerAngles;
            }

            var axis = GetRightThumbstick();
            if (axis.sqrMagnitude < thumbstickDeadzone * thumbstickDeadzone)
                axis = Vector2.zero;

            var deltaTime = Time.deltaTime;
            target.position += GetFlatForward() * axis.y * farMoveSpeed * deltaTime;

            if (keepOnlyYRotation)
            {
                var yaw = target.eulerAngles.y + axis.x * farRotateSpeed * deltaTime;
                target.rotation = Quaternion.Euler(startEulerAngles.x, yaw, startEulerAngles.z);
            }
        }

        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
        {
            if (!ReferenceEquals(interactable, grab) || allowFarGrab)
                return true;

            if (interactor is XRRayInteractor)
                return false;

            return interactor is not NearFarInteractor nearFarInteractor ||
                   nearFarInteractor.selectionRegion.Value != NearFarInteractor.Region.Far;
        }

        void ApplyFarGrabSettings()
        {
            if (grab == null)
                return;

            if (!hadGrabSettings)
            {
                hadGrabSettings = true;
                startTrackPosition = grab.trackPosition;
                startTrackRotation = grab.trackRotation;
                startThrowOnDetach = grab.throwOnDetach;
            }

            grab.trackPosition = false;
            grab.trackRotation = false;
            grab.throwOnDetach = false;
        }

        void RestoreGrabSettings()
        {
            if (!hadGrabSettings || grab == null)
                return;

            grab.trackPosition = startTrackPosition;
            grab.trackRotation = startTrackRotation;
            grab.throwOnDetach = startThrowOnDetach;
            hadGrabSettings = false;
        }

        void StopMoving()
        {
            moving = false;
            farMoving = false;
            RestoreGrabSettings();
        }

        float GetYaw()
        {
            if (!hasStartInteractorYaw || !TryGetSelectingInteractorTransform(out var interactorTransform))
                return target.eulerAngles.y;

            return startEulerAngles.y + Mathf.DeltaAngle(startInteractorYaw, interactorTransform.eulerAngles.y);
        }

        Vector2 GetRightThumbstick()
        {
            InputDevices.Clear();
            UnityEngine.XR.InputDevices.GetDevicesAtXRNode(XRNode.RightHand, InputDevices);

            foreach (var device in InputDevices)
            {
                if (device.TryGetFeatureValue(CommonUsages.primary2DAxis, out var axis))
                    return axis;
            }

            return Vector2.zero;
        }

        Vector3 GetFlatForward()
        {
            var cameraTransform = Camera.main != null ? Camera.main.transform : null;
            var forward = cameraTransform != null ? cameraTransform.forward : target.forward;
            forward.y = 0f;

            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        }

        bool IsFarSelectingInteractor()
        {
            if (grab == null || grab.interactorsSelecting.Count == 0)
                return false;

            var interactor = grab.interactorsSelecting[0];
            if (interactor is XRRayInteractor)
                return true;

            return interactor is NearFarInteractor nearFarInteractor &&
                   nearFarInteractor.selectionRegion.Value == NearFarInteractor.Region.Far;
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
