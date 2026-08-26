using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Attachment;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Inputs;

namespace F1XR.Interaction.World
{
    public sealed class WorldGrabPolicy : MonoBehaviour, IXRSelectFilter
    {
        [SerializeField] XRGrabInteractable grab;
        [SerializeField] Transform target;
        [SerializeField] bool preserveGrabPoint;
        [SerializeField] bool allowFarGrab = true;
        [SerializeField] bool keepOnlyYRotation = true;
        [SerializeField] bool smoothWorldTransform = true;
        [SerializeField] float positionLerpSpeed = 18f;
        [SerializeField] float rotationLerpSpeed = 18f;
        [SerializeField] float farMoveSpeed = 0.7f;
        [SerializeField] float minimumFarGrabDistance = 0.1f;
        [SerializeField] float farRotateSpeed = 90f;
        [SerializeField] float farRotationFollowSpeed = 18f;
        [SerializeField] float leftPitchDirection = 1f;
        [SerializeField] float leftRollDirection = -1f;
        [SerializeField] InputActionReference leftThumbstickAction;
        [SerializeField] float thumbstickDeadzone = 0.15f;
        [FormerlySerializedAs("centerAttachOnWorldGrabTarget")]
        [SerializeField] bool centerAttachOnFarGrabTarget = true;
        [SerializeField] bool preferMoveOnDiagonalThumbstick = true;

        bool moving;
        bool hasStartInteractorYaw;
        bool hasGrabPivot;
        float startInteractorYaw;
        Vector3 startEulerAngles;
        Vector3 grabPivotLocal;
        Vector3 targetOffsetFromGrab;
        Vector3 farPivotRayLocal;
        bool farMoving;
        float farGrabDistance;
        float farYawOffset;
        float farPitchOffset;
        float farRollOffset;
        float farViewYaw;
        InputAction cachedLeftThumbstickAction;
        bool hadGrabSettings;
        bool startTrackPosition;
        bool startTrackRotation;
        bool startThrowOnDetach;
        bool hadAttachSettings;
        bool startUseDynamicAttach;
        bool startMatchAttachPosition;
        bool filterAdded;
        bool ownFarDistanceInput;
        bool hadFarAttachControllerSettings;
        bool startUseDistanceBasedVelocityScaling;
        bool startUseManipulationInput;
        IXRSelectInteractor farGrabInteractor;
        InteractionAttachController farAttachController;

        static readonly System.Collections.Generic.List<UnityEngine.XR.InputDevice> InputDevices = new();
        static readonly System.Collections.Generic.List<Collider> Colliders = new();

        public bool canProcess => isActiveAndEnabled;
        public bool IsFarGrabMoving => farMoving;

        public void UseGrabPoint(
            XRGrabInteractable value,
            Transform valueTarget = null)
        {
            if (grab != value && filterAdded && grab != null)
            {
                grab.selectFilters.Remove(this);
                filterAdded = false;
            }

            grab = value;
            target = valueTarget != null ? valueTarget : transform;
            preserveGrabPoint = true;

            if (isActiveAndEnabled && grab != null && !filterAdded)
            {
                grab.selectFilters.Add(this);
                filterAdded = true;
            }
        }

        public void ConfigurePreservedFarMovement(
            float minimumDistance)
        {
            minimumFarGrabDistance = Mathf.Max(0.1f, minimumDistance);
            ownFarDistanceInput = true;
        }

        void Awake()
        {
            if (grab == null)
                grab = GetComponent<XRGrabInteractable>();

            if (target == null)
                target = transform;
        }

        void OnEnable()
        {
            if (grab != null && !filterAdded)
            {
                grab.selectFilters.Add(this);
                filterAdded = true;
            }
        }

        void OnDisable()
        {
            if (grab != null)
                grab.selectFilters.Remove(this);
            filterAdded = false;

            StopMoving();
        }

        void LateUpdate()
        {
            if (grab == null || !grab.isSelected)
            {
                StopMoving();
                return;
            }

            if (preserveGrabPoint)
            {
                UpdatePreservedGrab();
                return;
            }

            var farGrab = farMoving || IsFarSelectingInteractor();
            if (farGrab)
                UpdateFarGrab();
            else
                UpdateNearGrab();
        }

        void UpdatePreservedGrab()
        {
            var isFarGrab = IsFarSelectingInteractor();
            ApplyManualGrabSettings(isFarGrab);
            ApplyFarAttachControllerSettings(isFarGrab);

            if (!moving || farMoving != isFarGrab)
            {
                moving = true;
                farMoving = isFarGrab;
                farGrabInteractor = isFarGrab ? GetSelectingInteractor() : null;
                startEulerAngles = target.eulerAngles;
                hasStartInteractorYaw = TryGetSelectingInteractorTransform(out var interactorTransform);
                if (hasStartInteractorYaw)
                    startInteractorYaw = interactorTransform.eulerAngles.y;

                hasGrabPivot = TryGetGrabPivotWorld(out var initialPivotWorld);
                farYawOffset = 0f;

                if (hasGrabPivot)
                {
                    targetOffsetFromGrab = Quaternion.Inverse(target.rotation) *
                        (target.position - initialPivotWorld);

                    if (isFarGrab &&
                        TryGetFarRayFrame(out var rayOrigin, out var rayRotation))
                    {
                        farPivotRayLocal = Quaternion.Inverse(rayRotation) *
                            (initialPivotWorld - rayOrigin);
                        farGrabDistance = Mathf.Max(
                            minimumFarGrabDistance,
                            farPivotRayLocal.z);
                        farPivotRayLocal.z = farGrabDistance;
                    }
                }
            }

            farMoving = isFarGrab;
            if (!hasGrabPivot)
                return;

            var farAxis = isFarGrab
                ? ApplyThumbstickDeadzone(GetRightThumbstick())
                : Vector2.zero;
            var rotation = Quaternion.Euler(
                startEulerAngles.x,
                GetPreservedYaw(isFarGrab, farAxis),
                startEulerAngles.z);
            if (!TryGetPreservedPivot(isFarGrab, farAxis, out var pivotWorld))
                return;

            ApplyWorldPose(pivotWorld + rotation * targetOffsetFromGrab, rotation);
        }

        float GetPreservedYaw(bool isFarGrab, Vector2 farAxis)
        {
            if (!isFarGrab)
                return GetYaw();

            farYawOffset += farAxis.x * farRotateSpeed * Time.deltaTime;
            return startEulerAngles.y + farYawOffset;
        }

        bool TryGetPreservedPivot(
            bool isFarGrab,
            Vector2 farAxis,
            out Vector3 pivotWorld)
        {
            if (!isFarGrab)
                return TryGetCurrentAttachPosition(out pivotWorld);

            pivotWorld = default;
            farGrabDistance = Mathf.Max(
                minimumFarGrabDistance,
                farGrabDistance + farAxis.y * farMoveSpeed * Time.deltaTime);

            if (!TryGetFarRayFrame(out var rayOrigin, out var rayRotation))
                return false;

            farPivotRayLocal.z = farGrabDistance;
            pivotWorld = rayOrigin + rayRotation * farPivotRayLocal;
            return true;
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
                    hasGrabPivot = TryGetGrabPivotLocal(out grabPivotLocal);
                }
                else
                {
                    hasGrabPivot = false;
                }
            }

            var rotation = Quaternion.Euler(startEulerAngles.x, GetYaw(), startEulerAngles.z);
            if (hasGrabPivot && TryGetCurrentAttachPosition(out var currentAttachPosition))
                ApplyWorldPose(currentAttachPosition - rotation * grabPivotLocal, rotation);
            else
                ApplyWorldRotation(rotation);
        }

        void UpdateFarGrab()
        {
            ApplyManualGrabSettings(true);

            if (!farMoving)
            {
                moving = false;
                farMoving = true;
                farGrabInteractor = GetSelectingInteractor();
                startEulerAngles = target.eulerAngles;
                farGrabDistance = TryGetFarRayPose(out var rayOrigin, out _)
                    ? Vector3.Distance(rayOrigin, target.position)
                    : 0f;
                farViewYaw = GetFaceYaw();
                farYawOffset = 0f;
                farPitchOffset = 0f;
                farRollOffset = 0f;
            }

            var axis = ApplyThumbstickDeadzone(GetRightThumbstick());
            var leftAxis = ApplyThumbstickDeadzone(GetLeftThumbstick());

            var deltaTime = Time.deltaTime;
            farGrabDistance = Mathf.Max(
                minimumFarGrabDistance,
                farGrabDistance + axis.y * farMoveSpeed * deltaTime);
            var hasPosition = false;
            var position = target.position;
            if (TryGetFarRayPose(out var currentRayOrigin, out var currentRayForward))
            {
                position = currentRayOrigin + currentRayForward * farGrabDistance;
                hasPosition = true;
            }

            if (keepOnlyYRotation)
            {
                farYawOffset += axis.x * farRotateSpeed * deltaTime;
                farPitchOffset += leftAxis.y * leftPitchDirection * farRotateSpeed * deltaTime;
                farRollOffset += leftAxis.x * leftRollDirection * farRotateSpeed * deltaTime;
                farViewYaw = SmoothAngle(farViewYaw, GetFaceYaw(), farRotationFollowSpeed, deltaTime);

                var yawRotation = Quaternion.Euler(startEulerAngles.x, farViewYaw + farYawOffset, startEulerAngles.z);
                var viewTilt = Quaternion.AngleAxis(farPitchOffset, GetViewRight()) *
                               Quaternion.AngleAxis(farRollOffset, GetViewForward());
                var rotation = viewTilt * yawRotation;
                if (hasPosition)
                    ApplyWorldPose(position, rotation);
                else
                    ApplyWorldRotation(rotation);
            }
            else if (hasPosition)
            {
                ApplyWorldPosition(position);
            }
        }

        void ApplyWorldPose(Vector3 position, Quaternion rotation)
        {
            if (!smoothWorldTransform)
            {
                target.SetPositionAndRotation(position, rotation);
                return;
            }

            var deltaTime = Time.deltaTime;
            target.SetPositionAndRotation(
                Vector3.Lerp(target.position, position, GetLerpT(positionLerpSpeed, deltaTime)),
                Quaternion.Slerp(target.rotation, rotation, GetLerpT(rotationLerpSpeed, deltaTime)));
        }

        void ApplyWorldPosition(Vector3 position)
        {
            target.position = smoothWorldTransform
                ? Vector3.Lerp(target.position, position, GetLerpT(positionLerpSpeed, Time.deltaTime))
                : position;
        }

        void ApplyWorldRotation(Quaternion rotation)
        {
            target.rotation = smoothWorldTransform
                ? Quaternion.Slerp(target.rotation, rotation, GetLerpT(rotationLerpSpeed, Time.deltaTime))
                : rotation;
        }

        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
        {
            if (!ReferenceEquals(interactable, grab))
                return true;

            if (farMoving && !ReferenceEquals(interactor, farGrabInteractor))
                return false;

            if (allowFarGrab)
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

        void ApplyFarAttachControllerSettings(bool farGrab)
        {
            if (!ownFarDistanceInput || !farGrab)
            {
                RestoreFarAttachControllerSettings();
                return;
            }

            if (hadFarAttachControllerSettings)
                return;

            if (GetSelectingInteractor() is not Component component ||
                !component.TryGetComponent(out farAttachController))
            {
                farAttachController = null;
                return;
            }

            hadFarAttachControllerSettings = true;
            startUseDistanceBasedVelocityScaling =
                farAttachController.useDistanceBasedVelocityScaling;
            startUseManipulationInput =
                farAttachController.useManipulationInput;
            farAttachController.useDistanceBasedVelocityScaling = false;
            farAttachController.useManipulationInput = false;
        }

        void RestoreFarAttachControllerSettings()
        {
            if (!hadFarAttachControllerSettings ||
                farAttachController == null)
            {
                hadFarAttachControllerSettings = false;
                farAttachController = null;
                return;
            }

            farAttachController.useDistanceBasedVelocityScaling =
                startUseDistanceBasedVelocityScaling;
            farAttachController.useManipulationInput =
                startUseManipulationInput;
            hadFarAttachControllerSettings = false;
            farAttachController = null;
        }

        void StopMoving()
        {
            moving = false;
            farMoving = false;
            hasGrabPivot = false;
            farGrabInteractor = null;
            RestoreFarAttachControllerSettings();
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
                if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out var axis))
                    return axis;
            }

            return Vector2.zero;
        }

        Vector2 GetLeftThumbstick()
        {
            var action = GetLeftThumbstickAction();
            if (action != null)
                return action.ReadValue<Vector2>();

            InputDevices.Clear();
            UnityEngine.XR.InputDevices.GetDevicesAtXRNode(XRNode.LeftHand, InputDevices);

            foreach (var device in InputDevices)
            {
                if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out var axis))
                    return axis;
            }

            return Vector2.zero;
        }

        InputAction GetLeftThumbstickAction()
        {
            if (leftThumbstickAction != null && leftThumbstickAction.action != null)
                return leftThumbstickAction.action;

            if (cachedLeftThumbstickAction != null)
                return cachedLeftThumbstickAction;

            var managers = FindObjectsByType<InputActionManager>();
            foreach (var manager in managers)
            {
                foreach (var asset in manager.actionAssets)
                {
                    var action = asset.FindActionMap("XRI Left", false)?.FindAction("Thumbstick", false);
                    if (action == null)
                        continue;

                    cachedLeftThumbstickAction = action;
                    return cachedLeftThumbstickAction;
                }
            }

            return null;
        }

        Vector2 ApplyThumbstickDeadzone(Vector2 axis)
        {
            if (axis.sqrMagnitude < thumbstickDeadzone * thumbstickDeadzone)
                return Vector2.zero;

            return preferMoveOnDiagonalThumbstick ? DominantAxis(axis) : axis;
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

        static float GetLerpT(float speed, float deltaTime)
        {
            return speed <= 0f ? 1f : 1f - Mathf.Exp(-speed * deltaTime);
        }

        float GetFaceYaw()
        {
            var viewTransform = Camera.main != null ? Camera.main.transform : null;
            var forward = viewTransform != null ? viewTransform.position - target.position : target.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude < 0.0001f)
                return target.eulerAngles.y;

            return Quaternion.LookRotation(forward.normalized, Vector3.up).eulerAngles.y;
        }

        Vector3 GetViewForward()
        {
            var viewTransform = Camera.main != null ? Camera.main.transform : null;
            var forward = viewTransform != null ? viewTransform.forward : target.forward;

            if (forward.sqrMagnitude < 0.0001f)
                return Vector3.forward;

            return forward.normalized;
        }

        Vector3 GetViewRight()
        {
            var viewTransform = Camera.main != null ? Camera.main.transform : null;
            var right = viewTransform != null ? viewTransform.right : target.right;

            if (right.sqrMagnitude < 0.0001f)
                return Vector3.right;

            return right.normalized;
        }

        bool IsFarSelectingInteractor()
        {
            var interactor = GetSelectingInteractor();
            if (interactor == null)
                return false;

            if (interactor is XRRayInteractor)
                return true;

            return interactor is NearFarInteractor nearFarInteractor &&
                   nearFarInteractor.selectionRegion.Value == NearFarInteractor.Region.Far;
        }

        bool TryGetFarRayPose(out Vector3 origin, out Vector3 forward)
        {
            origin = Vector3.zero;
            forward = Vector3.forward;

            if (!TryGetFarRayFrame(out origin, out var rotation))
                return false;

            forward = rotation * Vector3.forward;
            return true;
        }

        bool TryGetFarRayFrame(out Vector3 origin, out Quaternion rotation)
        {
            origin = Vector3.zero;
            rotation = Quaternion.identity;

            var interactor = farGrabInteractor ?? GetSelectingInteractor();
            if (interactor == null)
                return false;

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
            rotation = rayTransform.rotation;
            return true;
        }

        IXRSelectInteractor GetSelectingInteractor()
        {
            if (grab == null || grab.interactorsSelecting.Count == 0)
                return null;

            return grab.interactorsSelecting[0];
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

        bool TryGetGrabPivotLocal(out Vector3 pivotLocal)
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

        bool TryGetGrabPivotWorld(out Vector3 pivotWorld)
        {
            pivotWorld = default;
            if (grab == null || grab.interactorsSelecting.Count == 0)
                return false;

            var attach = grab.GetAttachTransform(grab.interactorsSelecting[0]);
            if (attach == null)
                return false;

            pivotWorld = attach.position;
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
