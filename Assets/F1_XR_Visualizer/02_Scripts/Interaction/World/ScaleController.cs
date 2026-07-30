using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using F1XR.Interaction.Input;

namespace F1XR.Interaction.World
{
    public sealed class ScaleController : MonoBehaviour
    {
        [SerializeField] Transform target;
        [SerializeField] float grabRadius = 0.2f;
        [SerializeField] float pinchStartDistance = 0.035f;
        [SerializeField] float pinchEndDistance = 0.05f;
        [SerializeField] float minScaleStartDistance = 0.08f;
        [SerializeField] float minScale = 0.02f;
        [SerializeField] float maxScale = 5f;
        [SerializeField, Min(0.01f)] float minHandleScaleRatio = 0.05f;
        [SerializeField] bool smoothWorldTransform = true;
        [SerializeField] float positionLerpSpeed = 18f;
        [SerializeField] float rotationLerpSpeed = 18f;
        [SerializeField] float scaleLerpSpeed = 18f;
        [SerializeField] bool keepPivotFixed = true;
        [SerializeField] bool keepPositionWhileScaling;
        [SerializeField] bool keepRotationWhileScaling;
        [SerializeField] bool keepOnlyYRotationWhileScaling;
        [SerializeField] bool keepRotationWhileMoving;
        [SerializeField] bool keepOnlyYRotationWhileMoving;
        [SerializeField] float controllerRayDistance = 20f;
        [SerializeField] LayerMask controllerRayMask = ~0;
        [SerializeField] XRBaseInputInteractor leftInteractor;
        [SerializeField] XRBaseInputInteractor rightInteractor;
        [SerializeField] XRGrabInteractable grab;
        [SerializeField] Rigidbody body;
        [SerializeField] WorldGrabPolicy worldGrabPolicy;

        static readonly List<InputDevice> InputDevices = new();

        readonly List<Collider> targetColliders = new();
        XRHandSubsystem handSubsystem;
        bool scaling;
        bool leftPinching;
        bool rightPinching;
        bool hadGrab;
        bool wasGrabEnabled;
        bool waitForPinchRelease;
        bool controllerScaling;
        bool handleScaling;
        bool handlePivotFixed;
        float handleStartDistance;
        Vector3 handleStartDirection;
        bool moving;
        float startHandDistance;
        Vector3 startHandVector;
        Vector3 startPivotWorld;
        Vector3 startPivotLocal;
        Vector3 startScale;
        Vector3 startPosition;
        Quaternion startRotation;
        Vector3 startEulerAngles;
        Quaternion moveStartRotation;
        Vector3 moveStartEulerAngles;
        RigidbodyConstraints startConstraints;

        public bool IsScaling => scaling;
        public bool IsHandleScaling => handleScaling;
        public event Action ScaleStarted;

        /// <summary>
        /// Starts a one-handed scale driven by a single grab point (a corner handle) around a fixed
        /// pivot. The two-handed path stays untouched and simply yields while this is active.
        /// </summary>
        public bool BeginHandleScale(Vector3 grabPoint, Vector3 pivot, bool keepPivot = true)
        {
            if (scaling || target == null)
                return false;

            var offset = grabPoint - pivot;
            handleStartDistance = offset.magnitude;
            if (handleStartDistance <= Mathf.Epsilon)
                return false;

            handleStartDirection = offset / handleStartDistance;
            scaling = true;
            handleScaling = true;
            handlePivotFixed = keepPivot;
            controllerScaling = false;
            ScaleStarted?.Invoke();

            startPivotWorld = pivot;
            startPivotLocal = target.InverseTransformPoint(pivot);
            startScale = target.localScale;
            startPosition = target.position;
            startRotation = target.rotation;
            startEulerAngles = target.eulerAngles;

            // The grab that started this stays selected so the caller can tell when it ends, so unlike
            // the two-hand path the XRGrabInteractable is left enabled here.
            return true;
        }

        public void UpdateHandleScale(Vector3 grabPoint)
        {
            if (!handleScaling)
                return;

            // Project onto the diagonal captured at grab time so the scale tracks the pull linearly and
            // sideways drift does not inflate it the way a raw distance would.
            var reach = Vector3.Dot(grabPoint - startPivotWorld, handleStartDirection);
            ApplyScale(Mathf.Max(reach / handleStartDistance, minHandleScaleRatio));
        }

        public void EndHandleScale()
        {
            if (!handleScaling)
                return;

            StopScaling();
        }

        void Awake()
        {
            if (target == null)
                target = transform;

            if (grab == null)
                grab = GetComponent<XRGrabInteractable>();

            if (body == null)
                body = GetComponent<Rigidbody>();

            if (worldGrabPolicy == null)
                worldGrabPolicy = GetComponent<WorldGrabPolicy>();

            RefreshTargetColliders();
        }

        void OnEnable()
        {
            handSubsystem = XRHandInput.FindRunningSubsystem();
            Application.onBeforeRender += UpdateMoveRotationLock;
        }

        void OnDisable()
        {
            Application.onBeforeRender -= UpdateMoveRotationLock;
            StopMoving();
            StopScaling();
        }

        void Update()
        {
            if (handleScaling)
                return;

            if (handSubsystem == null || !handSubsystem.running)
                handSubsystem = XRHandInput.FindRunningSubsystem();

            bool bothHandsTracked = handSubsystem != null &&
                handSubsystem.leftHand.isTracked &&
                handSubsystem.rightHand.isTracked;
            if (!bothHandsTracked && TryUpdateControllerScale())
                return;

            if (handSubsystem == null)
                return;

            var hasLeft = XRHandInput.TryGetPalmOrWristPoint(handSubsystem.leftHand, out var leftPoint);
            var hasRight = XRHandInput.TryGetPalmOrWristPoint(handSubsystem.rightHand, out var rightPoint);
            var hasLeftGrab = XRHandInput.TryGetPinchPoint(handSubsystem.leftHand, out var leftGrabPoint);
            var hasRightGrab = XRHandInput.TryGetPinchPoint(handSubsystem.rightHand, out var rightGrabPoint);

            leftPinching = hasLeftGrab && XRHandInput.IsPinching(
                handSubsystem.leftHand,
                leftPinching,
                pinchStartDistance,
                pinchEndDistance);
            rightPinching = hasRightGrab && XRHandInput.IsPinching(
                handSubsystem.rightHand,
                rightPinching,
                pinchStartDistance,
                pinchEndDistance);

            if (!leftPinching || !rightPinching)
            {
                waitForPinchRelease = false;
                StopScaling();
                return;
            }

            if (waitForPinchRelease)
                return;

            var leftScalePoint = hasLeft ? leftPoint : leftGrabPoint;
            var rightScalePoint = hasRight ? rightPoint : rightGrabPoint;
            var handDistance = Vector3.Distance(leftScalePoint, rightScalePoint);
            if (handDistance <= Mathf.Epsilon)
                return;

            var handVector = rightScalePoint - leftScalePoint;
            if (!scaling)
            {
                if (handDistance < minScaleStartDistance)
                    return;

                bool alreadySelected = grab != null && grab.isSelected;
                if (!alreadySelected &&
                    (!IsNearTarget(leftGrabPoint) || !IsNearTarget(rightGrabPoint)))
                    return;

                scaling = true;
                ScaleStarted?.Invoke();
                startHandDistance = handDistance;
                startHandVector = handVector;
                startPivotWorld = Vector3.Lerp(leftGrabPoint, rightGrabPoint, 0.5f);
                startPivotLocal = target.InverseTransformPoint(startPivotWorld);
                startScale = target.localScale;
                startPosition = target.position;
                startRotation = target.rotation;
                startEulerAngles = target.eulerAngles;
                controllerScaling = false;
                SetGrabEnabled(false);
                return;
            }

            if (Vector3.Dot(startHandVector.normalized, handVector.normalized) <= 0.25f)
            {
                waitForPinchRelease = true;
                StopScaling();
                return;
            }

            var scaleRatio = handDistance / startHandDistance;
            ApplyScale(scaleRatio);
        }

        void LateUpdate()
        {
            UpdateMoveRotationLock();
        }

        void StopScaling()
        {
            if (!scaling)
                return;

            scaling = false;
            controllerScaling = false;
            handleScaling = false;
            SetGrabEnabled(true);
        }

        void SetGrabEnabled(bool enabled)
        {
            if (grab == null)
                return;

            if (!enabled)
            {
                hadGrab = true;
                wasGrabEnabled = grab.enabled;
                grab.enabled = false;
                return;
            }

            if (hadGrab)
                grab.enabled = wasGrabEnabled;

            hadGrab = false;
        }

        void UpdateMoveRotationLock()
        {
            if (scaling || grab == null || !grab.isSelected || IsWorldFarGrabMoving())
            {
                StopMoving();
                return;
            }

            if (!moving)
            {
                moving = true;
                moveStartRotation = target.rotation;
                moveStartEulerAngles = target.eulerAngles;
                LockMoveRotationConstraints();
            }

            if (keepRotationWhileMoving)
                ApplyWorldRotation(moveStartRotation);
            else if (keepOnlyYRotationWhileMoving)
                ApplyWorldRotation(Quaternion.Euler(moveStartEulerAngles.x, target.eulerAngles.y, moveStartEulerAngles.z));
        }

        void StopMoving()
        {
            if (!moving)
                return;

            moving = false;

            if (body != null)
                body.constraints = startConstraints;
        }

        void LockMoveRotationConstraints()
        {
            if (body == null)
                return;

            startConstraints = body.constraints;

            if (keepRotationWhileMoving)
                body.constraints |= RigidbodyConstraints.FreezeRotation;
            else if (keepOnlyYRotationWhileMoving)
                body.constraints |= RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }

        bool IsWorldFarGrabMoving()
        {
            return worldGrabPolicy != null && worldGrabPolicy.IsFarGrabMoving;
        }

        bool TryUpdateControllerScale()
        {
            if (scaling && !controllerScaling)
                return false;

            if (!TryGetTrigger(XRNode.LeftHand, out var leftDevicePoint) ||
                !TryGetTrigger(XRNode.RightHand, out var rightDevicePoint))
            {
                if (controllerScaling)
                    StopScaling();

                return false;
            }

            if (leftInteractor == null || rightInteractor == null ||
                !IsInteractorNear(leftInteractor, leftDevicePoint) ||
                !IsInteractorNear(rightInteractor, rightDevicePoint))
                FindControllerInteractors(leftDevicePoint, rightDevicePoint);

            if (!TryGetRayOriginPoint(leftInteractor, out var leftPoint) ||
                !TryGetRayOriginPoint(rightInteractor, out var rightPoint))
            {
                if (controllerScaling)
                    StopScaling();

                return false;
            }

            var controllerDistance = Vector3.Distance(leftPoint, rightPoint);
            if (controllerDistance <= Mathf.Epsilon)
                return true;

            var controllerVector = rightPoint - leftPoint;
            if (!scaling)
            {
                if (controllerDistance < minScaleStartDistance)
                    return true;

                if (!TryGetRayHit(leftInteractor, out _) || !TryGetRayHit(rightInteractor, out _))
                    return true;

                scaling = true;
                ScaleStarted?.Invoke();
                startHandDistance = controllerDistance;
                startHandVector = controllerVector;
                startPivotWorld = Vector3.Lerp(leftPoint, rightPoint, 0.5f);
                startPivotLocal = target.InverseTransformPoint(startPivotWorld);
                startScale = target.localScale;
                startPosition = target.position;
                startRotation = target.rotation;
                startEulerAngles = target.eulerAngles;
                controllerScaling = true;
                SetGrabEnabled(false);
                return true;
            }

            if (Vector3.Dot(startHandVector.normalized, controllerVector.normalized) <= 0.25f)
            {
                StopScaling();
                return true;
            }

            ApplyScale(controllerDistance / startHandDistance);
            return true;
        }

        void FindControllerInteractors(Vector3 leftDevicePoint, Vector3 rightDevicePoint)
        {
            var interactors = FindObjectsByType<XRBaseInputInteractor>(FindObjectsInactive.Exclude);
            var leftDistance = float.MaxValue;
            var rightDistance = float.MaxValue;

            foreach (var interactor in interactors)
            {
                if (interactor is not IXRRayProvider)
                    continue;

                var objectName = interactor.name;
                if (objectName.Contains("Left"))
                    SetNearestInteractor(interactor, leftDevicePoint, ref leftInteractor, ref leftDistance);
                else if (objectName.Contains("Right"))
                    SetNearestInteractor(interactor, rightDevicePoint, ref rightInteractor, ref rightDistance);
            }
        }

        static void SetNearestInteractor(XRBaseInputInteractor interactor, Vector3 devicePoint, ref XRBaseInputInteractor nearest, ref float nearestDistance)
        {
            var distance = Vector3.Distance(GetInteractorPoint(interactor), devicePoint);
            if (distance >= nearestDistance)
                return;

            nearest = interactor;
            nearestDistance = distance;
        }

        static bool IsInteractorNear(XRBaseInputInteractor interactor, Vector3 devicePoint)
        {
            return interactor != null && Vector3.Distance(GetInteractorPoint(interactor), devicePoint) <= 0.35f;
        }

        static Vector3 GetInteractorPoint(XRBaseInputInteractor interactor)
        {
            if (interactor is IXRRayProvider rayProvider)
            {
                var origin = rayProvider.GetOrCreateRayOrigin();
                if (origin != null)
                    return origin.position;
            }

            return interactor.transform.position;
        }

        static bool TryGetTrigger(XRNode node, out Vector3 point)
        {
            InputDevices.Clear();
            UnityEngine.XR.InputDevices.GetDevicesAtXRNode(node, InputDevices);

            foreach (var device in InputDevices)
            {
                if (!device.TryGetFeatureValue(CommonUsages.triggerButton, out var pressed) || !pressed)
                    continue;

                if (!device.TryGetFeatureValue(CommonUsages.devicePosition, out point))
                    point = default;

                return true;
            }

            point = default;
            return false;
        }

        static bool TryGetRayOriginPoint(XRBaseInputInteractor interactor, out Vector3 point)
        {
            if (interactor == null)
            {
                point = default;
                return false;
            }

            if (interactor is IXRRayProvider rayProvider)
            {
                var origin = rayProvider.GetOrCreateRayOrigin();
                if (origin != null)
                {
                    point = origin.position;
                    return true;
                }
            }

            point = interactor.transform.position;
            return true;
        }

        bool TryGetRayHit(XRBaseInputInteractor interactor, out Vector3 point)
        {
            if (interactor == null)
            {
                point = default;
                return false;
            }

            if (interactor is IXRRayProvider rayProvider)
            {
                var origin = rayProvider.GetOrCreateRayOrigin();
                if (origin != null && TryRaycastTarget(origin, out point))
                    return true;

                var hitTransform = rayProvider.rayEndTransform;
                if (hitTransform != null && IsTargetHit(hitTransform))
                {
                    point = rayProvider.rayEndPoint;
                    return true;
                }
            }

            if (interactor is XRRayInteractor rayInteractor &&
                rayInteractor.TryGetCurrent3DRaycastHit(out var hit) &&
                IsTargetHit(hit.transform))
            {
                point = hit.point;
                return true;
            }

            point = default;
            return false;
        }

        bool TryRaycastTarget(Transform rayOrigin, out Vector3 point)
        {
            var hits = Physics.RaycastAll(rayOrigin.position, rayOrigin.forward, controllerRayDistance, controllerRayMask, QueryTriggerInteraction.Ignore);
            var bestDistance = float.MaxValue;
            point = default;

            foreach (var hit in hits)
            {
                if (!IsTargetHit(hit.transform) || hit.distance >= bestDistance)
                    continue;

                bestDistance = hit.distance;
                point = hit.point;
            }

            return bestDistance < float.MaxValue;
        }

        bool IsTargetHit(Transform hitTransform)
        {
            return hitTransform == target || hitTransform.IsChildOf(target);
        }

        bool IsNearTarget(Vector3 point)
        {
            if (targetColliders.Count == 0)
                RefreshTargetColliders();

            var maxDistanceSqr = grabRadius * grabRadius;
            var hasCollider = false;

            foreach (var targetCollider in targetColliders)
            {
                if (targetCollider == null || !targetCollider.enabled)
                    continue;

                hasCollider = true;
                var closestPoint = targetCollider.ClosestPoint(point);
                if ((closestPoint - point).sqrMagnitude <= maxDistanceSqr)
                    return true;
            }

            return !hasCollider && (target.position - point).sqrMagnitude <= maxDistanceSqr;
        }

        void RefreshTargetColliders()
        {
            targetColliders.Clear();

            if (grab != null && grab.colliders.Count > 0)
            {
                foreach (var grabCollider in grab.colliders)
                {
                    if (grabCollider != null)
                        targetColliders.Add(grabCollider);
                }

                return;
            }

            if (target != null)
                targetColliders.AddRange(target.GetComponentsInChildren<Collider>(true));
        }

        Vector3 ClampScale(Vector3 scale)
        {
            var size = Mathf.Max(scale.x, scale.y, scale.z);
            if (size <= Mathf.Epsilon)
                return scale;

            var ratio = Mathf.Clamp(size, minScale, maxScale) / size;
            return scale * ratio;
        }

        void ApplyScale(float scaleRatio)
        {
            var currentPosition = target.position;
            var currentRotation = target.rotation;
            var currentScale = target.localScale;

            ApplyScaleImmediate(scaleRatio);

            if (!smoothWorldTransform)
                return;

            var nextPosition = target.position;
            var nextRotation = target.rotation;
            var nextScale = target.localScale;

            target.SetPositionAndRotation(currentPosition, currentRotation);
            target.localScale = currentScale;

            ApplyWorldTransform(nextPosition, nextRotation, nextScale);
        }

        void ApplyScaleImmediate(float scaleRatio)
        {
            target.localScale = ClampScale(startScale * scaleRatio);

            if (keepRotationWhileScaling)
                target.rotation = startRotation;
            else if (keepOnlyYRotationWhileScaling)
                target.rotation = Quaternion.Euler(startEulerAngles.x, target.eulerAngles.y, startEulerAngles.z);

            // The handle path pins the opposite corner instead of obeying the two-hand pivot options,
            // so the grabbed corner tracks the hand while the rest of the panel stays put.
            var pivotFixed = handleScaling ? handlePivotFixed : keepPivotFixed;

            if (!handleScaling && keepPositionWhileScaling)
                target.position = startPosition;
            else if (pivotFixed)
                target.position += startPivotWorld - target.TransformPoint(startPivotLocal);
        }

        void ApplyWorldTransform(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            var deltaTime = Time.deltaTime;
            target.SetPositionAndRotation(
                Vector3.Lerp(target.position, position, GetLerpT(positionLerpSpeed, deltaTime)),
                Quaternion.Slerp(target.rotation, rotation, GetLerpT(rotationLerpSpeed, deltaTime)));
            target.localScale = Vector3.Lerp(target.localScale, scale, GetLerpT(scaleLerpSpeed, deltaTime));
        }

        void ApplyWorldRotation(Quaternion rotation)
        {
            target.rotation = smoothWorldTransform
                ? Quaternion.Slerp(target.rotation, rotation, GetLerpT(rotationLerpSpeed, Time.deltaTime))
                : rotation;
        }

        static float GetLerpT(float speed, float deltaTime)
        {
            return speed <= 0f ? 1f : 1f - Mathf.Exp(-speed * deltaTime);
        }
    }
}
