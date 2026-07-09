using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace F1XR.AR
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

        static readonly List<XRHandSubsystem> HandSubsystems = new();
        static readonly List<InputDevice> InputDevices = new();

        XRHandSubsystem handSubsystem;
        bool scaling;
        bool leftPinching;
        bool rightPinching;
        bool hadGrab;
        bool wasGrabEnabled;
        bool waitForPinchRelease;
        bool controllerScaling;
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
        }

        void OnEnable()
        {
            FindHandSubsystem();
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
            if (TryUpdateControllerScale())
                return;

            if (handSubsystem == null || !handSubsystem.running)
                FindHandSubsystem();

            if (handSubsystem == null)
                return;

            var hasLeft = TryGetHandPoint(handSubsystem.leftHand, out var leftPoint);
            var hasRight = TryGetHandPoint(handSubsystem.rightHand, out var rightPoint);
            var hasLeftGrab = TryGetPinchPoint(handSubsystem.leftHand, out var leftGrabPoint);
            var hasRightGrab = TryGetPinchPoint(handSubsystem.rightHand, out var rightGrabPoint);

            leftPinching = hasLeftGrab && IsPinching(handSubsystem.leftHand, leftPinching);
            rightPinching = hasRightGrab && IsPinching(handSubsystem.rightHand, rightPinching);

            if (!leftPinching || !rightPinching)
            {
                waitForPinchRelease = false;
                StopScaling();
                return;
            }

            if (waitForPinchRelease)
                return;

            var handDistance = Vector3.Distance(leftPoint, rightPoint);
            if (handDistance <= Mathf.Epsilon)
                return;

            var handVector = rightPoint - leftPoint;
            if (!scaling)
            {
                if (handDistance < minScaleStartDistance)
                    return;

                if (!IsNearTarget(leftGrabPoint) || !IsNearTarget(rightGrabPoint))
                    return;

                scaling = true;
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

            if (!TryGetRayHit(leftInteractor, out var leftPoint) ||
                !TryGetRayHit(rightInteractor, out var rightPoint))
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

                scaling = true;
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

        void FindHandSubsystem()
        {
            HandSubsystems.Clear();
            SubsystemManager.GetSubsystems(HandSubsystems);

            foreach (var subsystem in HandSubsystems)
            {
                if (subsystem.running)
                {
                    handSubsystem = subsystem;
                    return;
                }
            }
        }

        bool IsNearTarget(Vector3 point)
        {
            return Vector3.Distance(target.position, point) <= grabRadius;
        }

        bool IsPinching(XRHand hand, bool wasPinching)
        {
            if (!TryGetJointPose(hand, XRHandJointID.ThumbTip, out var thumbPose) ||
                !TryGetJointPose(hand, XRHandJointID.IndexTip, out var indexPose))
                return false;

            var distance = Vector3.Distance(thumbPose.position, indexPose.position);
            return wasPinching
                ? distance <= pinchEndDistance
                : distance <= pinchStartDistance;
        }

        static bool TryGetHandPoint(XRHand hand, out Vector3 point)
        {
            if (TryGetJointPose(hand, XRHandJointID.Palm, out var palmPose))
            {
                point = palmPose.position;
                return true;
            }

            if (TryGetJointPose(hand, XRHandJointID.Wrist, out var wristPose))
            {
                point = wristPose.position;
                return true;
            }

            point = default;
            return false;
        }

        static bool TryGetPinchPoint(XRHand hand, out Vector3 point)
        {
            if (!TryGetJointPose(hand, XRHandJointID.ThumbTip, out var thumbPose) ||
                !TryGetJointPose(hand, XRHandJointID.IndexTip, out var indexPose))
            {
                point = default;
                return false;
            }

            point = Vector3.Lerp(thumbPose.position, indexPose.position, 0.5f);
            return true;
        }

        static bool TryGetJointPose(XRHand hand, XRHandJointID jointId, out Pose pose)
        {
            var joint = hand.GetJoint(jointId);
            return joint.TryGetPose(out pose);
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

            if (keepPositionWhileScaling)
                target.position = startPosition;
            else if (keepPivotFixed)
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
