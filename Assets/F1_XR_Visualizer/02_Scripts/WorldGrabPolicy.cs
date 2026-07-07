using UnityEngine;
using UnityEngine.Serialization;
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
        [SerializeField] float farRotationFollowSpeed = 18f;
        [SerializeField] float thumbstickDeadzone = 0.15f;
        [FormerlySerializedAs("centerAttachOnWorldGrabTarget")]
        [SerializeField] bool centerAttachOnFarGrabTarget = true;
        [SerializeField] bool preferMoveOnDiagonalThumbstick = true;

        bool moving;
        bool hasStartInteractorYaw;
        bool hasNearGrabPivot;
        float startInteractorYaw;
        Vector3 startEulerAngles;
        Vector3 nearGrabPivotLocal;
        bool farMoving;
        float farGrabDistance;
        float farYawOffset;
        float farViewYaw;
        bool hadGrabSettings;
        bool startTrackPosition;
        bool startTrackRotation;
        bool startThrowOnDetach;
        bool hadAttachSettings;
        bool startUseDynamicAttach;
        bool startMatchAttachPosition;

        static readonly System.Collections.Generic.List<UnityEngine.XR.InputDevice> InputDevices = new();
        static readonly System.Collections.Generic.List<Collider> Colliders = new();

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
            ApplyManualGrabSettings(false);

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
                {
                    startInteractorYaw = interactorTransform.eulerAngles.y;
                    hasNearGrabPivot = TryGetNearGrabPivotLocal(out nearGrabPivotLocal);
                }
                else
                {
                    hasNearGrabPivot = false;
                }
            }

            var rotation = Quaternion.Euler(startEulerAngles.x, GetYaw(), startEulerAngles.z);
            target.rotation = rotation;

            if (hasNearGrabPivot && TryGetCurrentAttachPosition(out var currentAttachPosition))
                target.position = currentAttachPosition - rotation * nearGrabPivotLocal;
        }

        void UpdateFarGrab()
        {
            ApplyManualGrabSettings(true);

            if (!farMoving)
            {
                moving = false;
                farMoving = true;
                startEulerAngles = target.eulerAngles;
                farGrabDistance = TryGetFarRayPose(out var rayOrigin, out _)
                    ? Vector3.Distance(rayOrigin, target.position)
                    : 0f;
                farViewYaw = GetViewYaw();
                farYawOffset = Mathf.DeltaAngle(farViewYaw, startEulerAngles.y);
            }

            var axis = GetRightThumbstick();
            if (axis.sqrMagnitude < thumbstickDeadzone * thumbstickDeadzone)
                axis = Vector2.zero;
            else if (preferMoveOnDiagonalThumbstick)
                axis = DominantAxis(axis);

            var deltaTime = Time.deltaTime;
            farGrabDistance = Mathf.Max(0.1f, farGrabDistance + axis.y * farMoveSpeed * deltaTime);
            if (TryGetFarRayPose(out var currentRayOrigin, out var currentRayForward))
                target.position = currentRayOrigin + currentRayForward * farGrabDistance;

            if (keepOnlyYRotation)
            {
                farYawOffset += axis.x * farRotateSpeed * deltaTime;
                farViewYaw = SmoothAngle(farViewYaw, GetViewYaw(), farRotationFollowSpeed, deltaTime);
                target.rotation = Quaternion.Euler(startEulerAngles.x, farViewYaw + farYawOffset, startEulerAngles.z);
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

        void ApplyManualGrabSettings(bool farGrab)
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

            if (farGrab)
                ApplyFarAttachSettings();
            else
                RestoreFarAttachSettings();
        }

        void RestoreGrabSettings()
        {
            if (!hadGrabSettings || grab == null)
                return;

            grab.trackPosition = startTrackPosition;
            grab.trackRotation = startTrackRotation;
            grab.throwOnDetach = startThrowOnDetach;
            hadGrabSettings = false;
            RestoreFarAttachSettings();
        }

        void ApplyFarAttachSettings()
        {
            if (!centerAttachOnFarGrabTarget || grab == null || GetComponent<WorldGrabTarget>() == null)
                return;

            if (!hadAttachSettings)
            {
                hadAttachSettings = true;
                startUseDynamicAttach = grab.useDynamicAttach;
                startMatchAttachPosition = grab.matchAttachPosition;
            }

            grab.useDynamicAttach = false;
            grab.matchAttachPosition = false;
        }

        void RestoreFarAttachSettings()
        {
            if (!hadAttachSettings || grab == null)
                return;

            grab.useDynamicAttach = startUseDynamicAttach;
            grab.matchAttachPosition = startMatchAttachPosition;
            hadAttachSettings = false;
        }

        void StopMoving()
        {
            moving = false;
            farMoving = false;
            hasNearGrabPivot = false;
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

        static Vector2 DominantAxis(Vector2 axis)
        {
            return Mathf.Abs(axis.x) > Mathf.Abs(axis.y)
                ? new Vector2(axis.x, 0f)
                : new Vector2(0f, axis.y);
        }

        static float SmoothAngle(float current, float targetAngle, float followSpeed, float deltaTime)
        {
            if (followSpeed <= 0f)
                return targetAngle;

            var t = 1f - Mathf.Exp(-followSpeed * deltaTime);
            return Mathf.LerpAngle(current, targetAngle, t);
        }

        float GetViewYaw()
        {
            var viewTransform = Camera.main != null ? Camera.main.transform : null;
            var forward = viewTransform != null ? viewTransform.forward : target.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude < 0.0001f)
                return target.eulerAngles.y;

            return Quaternion.LookRotation(forward.normalized, Vector3.up).eulerAngles.y;
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

        bool TryGetFarRayPose(out Vector3 origin, out Vector3 forward)
        {
            origin = Vector3.zero;
            forward = Vector3.forward;

            if (grab == null || grab.interactorsSelecting.Count == 0)
                return false;

            var interactor = grab.interactorsSelecting[0];
            Transform rayTransform = null;

            if (interactor is IXRRayProvider rayProvider)
                rayTransform = rayProvider.GetOrCreateRayOrigin();

            if (rayTransform == null && interactor is XRBaseInteractor baseInteractor)
            {
                rayTransform = baseInteractor.GetAttachTransform(grab);
                if (rayTransform == null)
                    rayTransform = baseInteractor.transform;
            }

            if (rayTransform == null)
                return false;

            origin = rayTransform.position;
            forward = rayTransform.forward.sqrMagnitude > 0.0001f
                ? rayTransform.forward.normalized
                : Vector3.forward;
            return true;
        }

        bool TryGetSelectingInteractorTransform(out Transform interactorTransform)
        {
            if (grab != null &&
                grab.interactorsSelecting.Count > 0 &&
                grab.interactorsSelecting[0] is XRBaseInteractor baseInteractor)
            {
                interactorTransform = baseInteractor.GetAttachTransform(grab);
                if (interactorTransform == null)
                    interactorTransform = baseInteractor.transform;
                return true;
            }

            interactorTransform = null;
            return false;
        }

        bool TryGetNearGrabPivotLocal(out Vector3 pivotLocal)
        {
            pivotLocal = Vector3.zero;

            if (grab == null || grab.interactorsSelecting.Count == 0)
                return false;

            var interactor = grab.interactorsSelecting[0];
            var pivotWorld = grab.GetAttachTransform(interactor).position;

            if (TryGetClosestColliderPoint(pivotWorld, out var closestPoint))
                pivotWorld = closestPoint;

            pivotLocal = target.InverseTransformPoint(pivotWorld);
            return true;
        }

        bool TryGetCurrentAttachPosition(out Vector3 position)
        {
            position = Vector3.zero;

            if (grab == null ||
                grab.interactorsSelecting.Count == 0 ||
                grab.interactorsSelecting[0] is not XRBaseInteractor baseInteractor)
                return false;

            var attachTransform = baseInteractor.GetAttachTransform(grab);
            position = attachTransform != null ? attachTransform.position : baseInteractor.transform.position;
            return true;
        }

        bool TryGetClosestColliderPoint(Vector3 point, out Vector3 closestPoint)
        {
            closestPoint = point;

            var closestDistance = float.MaxValue;
            var bestPoint = point;
            var found = false;

            if (grab.colliders.Count > 0)
            {
                foreach (var collider in grab.colliders)
                    CheckCollider(collider);
            }
            else
            {
                Colliders.Clear();
                grab.GetComponentsInChildren(Colliders);

                foreach (var collider in Colliders)
                    CheckCollider(collider);
            }

            if (found)
                closestPoint = bestPoint;

            return found;

            void CheckCollider(Collider collider)
            {
                if (collider == null || !collider.enabled || collider.isTrigger)
                    return;

                var candidate = collider.ClosestPoint(point);
                var distance = (candidate - point).sqrMagnitude;
                if (distance >= closestDistance)
                    return;

                closestDistance = distance;
                bestPoint = candidate;
                found = true;
            }
        }
    }
}
