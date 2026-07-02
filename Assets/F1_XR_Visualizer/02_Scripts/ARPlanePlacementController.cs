using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Hands;

namespace F1XR.AR
{
    public sealed class ARPlanePlacementController : MonoBehaviour
    {
        enum InputSourcePriority
        {
            ControllerFirst,
            HandFirst,
            ControllerOnly,
            HandOnly
        }

        [Header("AR Managers")]
        [SerializeField] ARRaycastManager raycastManager;
        [SerializeField] ARPlaneManager planeManager;
        [SerializeField] ARAnchorManager anchorManager;

        [Header("Placement")]
        [SerializeField] Transform rayOrigin;
        [SerializeField] GameObject cubePrefab;
        [SerializeField] bool allowReplaceExistingCube = false;
        [SerializeField] bool requireHorizontalUpPlane = true;
        [SerializeField] bool rejectFloorPlanes = true;
        [SerializeField] bool preferTableClassifiedPlanes = true;
        [SerializeField] float minimumPlacementHeight = 0.35f;
        [SerializeField] float verticalOffset = 0.04f;
        [SerializeField] float defaultCubeSize = 0.08f;

        [Header("Optional Input")]
        [SerializeField] InputActionProperty placeAction;
        [SerializeField] InputSourcePriority inputSourcePriority = InputSourcePriority.HandFirst;
        [SerializeField] bool useControllerTriggerPlacement = true;
        [SerializeField] bool useHandPinchPlacement = true;
        [SerializeField] float inputArmDelay = 0.5f;
        [SerializeField, Range(0f, 1f)] float pinchPressThreshold = 0.8f;
        [SerializeField, Range(0f, 1f)] float pinchReleaseThreshold = 0.55f;
        [SerializeField] float pinchDistancePressThreshold = 0.025f;
        [SerializeField] float pinchDistanceReleaseThreshold = 0.04f;

        static readonly List<ARRaycastHit> s_Hits = new();
        static readonly List<UnityEngine.XR.InputDevice> s_InputDevices = new();
        static readonly List<XRHandSubsystem> s_HandSubsystems = new();

        ARAnchor currentAnchor;
        GameObject spawnedCube;
        
        public bool HasPlacement => spawnedCube != null;
        public Transform PlacementTransform => spawnedCube != null ? spawnedCube.transform : null;
        public Vector3 PlacementPosition => spawnedCube != null ? spawnedCube.transform.position : Vector3.zero;
        
        bool wasLeftControllerTriggerPressed;
        bool wasRightControllerTriggerPressed;
        XRHandSubsystem handSubsystem;
        bool wasLeftPinching;
        bool wasRightPinching;
        bool placementInputsArmed;
        float enableTime;
        InputDeviceCharacteristics lastPressedControllerHandedness;
        Handedness lastPinchHandedness = Handedness.Invalid;
        InputAction leftControllerPointerPositionAction;
        InputAction leftControllerPointerRotationAction;
        InputAction leftControllerTrackingStateAction;
        InputAction rightControllerPointerPositionAction;
        InputAction rightControllerPointerRotationAction;
        InputAction rightControllerTrackingStateAction;

        void Reset()
        {
            raycastManager = GetComponent<ARRaycastManager>();
            planeManager = GetComponent<ARPlaneManager>();
            anchorManager = GetComponent<ARAnchorManager>();

            if (Camera.main != null)
                rayOrigin = Camera.main.transform;
        }

        void Awake()
        {
            if (raycastManager == null)
                raycastManager = GetComponent<ARRaycastManager>();

            if (planeManager == null)
                planeManager = GetComponent<ARPlaneManager>();

            if (anchorManager == null)
                anchorManager = GetComponent<ARAnchorManager>();

            if (rayOrigin == null && Camera.main != null)
                rayOrigin = Camera.main.transform;

            CreateControllerPointerActions();
        }

        void OnEnable()
        {
            if (placeAction.action != null)
                placeAction.action.Enable();

            SetControllerPointerActionsEnabled(true);

            enableTime = Time.time;
            placementInputsArmed = false;
            wasLeftControllerTriggerPressed = false;
            wasRightControllerTriggerPressed = false;
            wasLeftPinching = false;
            wasRightPinching = false;
            TrySubscribeHandSubsystem();
        }

        void OnDisable()
        {
            if (placeAction.action != null)
                placeAction.action.Disable();

            SetControllerPointerActionsEnabled(false);
            UnsubscribeHandSubsystem();
        }

        void OnDestroy()
        {
            DisposeControllerPointerActions();
        }

        void Update()
        {
            var controllerTriggerPressedThisFrame = CanUseControllers() &&
                WasControllerTriggerPressedThisFrame();

            if (!placementInputsArmed)
            {
                if (Time.time >= enableTime + inputArmDelay && !IsAnyPlacementInputHeld())
                    placementInputsArmed = true;

                return;
            }

            if (placeAction.action != null && placeAction.action.WasPressedThisFrame())
            {
                TryPlaceCube();
                return;
            }

            if (controllerTriggerPressedThisFrame)
            {
                TryPlaceCube();
                return;
            }

            if (useHandPinchPlacement && handSubsystem == null)
                TrySubscribeHandSubsystem();
        }

        bool IsAnyPlacementInputHeld()
        {
            return wasLeftControllerTriggerPressed ||
                wasRightControllerTriggerPressed ||
                wasLeftPinching ||
                wasRightPinching ||
                (placeAction.action != null && placeAction.action.IsPressed());
        }

        bool WasControllerTriggerPressedThisFrame()
        {
            var leftPressed = IsControllerTriggerPressed(InputDeviceCharacteristics.Left);
            var rightPressed = IsControllerTriggerPressed(InputDeviceCharacteristics.Right);
            var leftPressedThisFrame = leftPressed && !wasLeftControllerTriggerPressed;
            var rightPressedThisFrame = rightPressed && !wasRightControllerTriggerPressed;

            if (rightPressedThisFrame)
                lastPressedControllerHandedness = InputDeviceCharacteristics.Right;
            else if (leftPressedThisFrame)
                lastPressedControllerHandedness = InputDeviceCharacteristics.Left;

            wasLeftControllerTriggerPressed = leftPressed;
            wasRightControllerTriggerPressed = rightPressed;

            return leftPressedThisFrame || rightPressedThisFrame;
        }

        static bool IsControllerTriggerPressed(InputDeviceCharacteristics handedness)
        {
            s_InputDevices.Clear();
            InputDevices.GetDevicesWithCharacteristics(
                handedness | InputDeviceCharacteristics.Controller,
                s_InputDevices);

            foreach (var device in s_InputDevices)
            {
                if (device.isValid &&
                    device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out var pressed) &&
                    pressed)
                {
                    return true;
                }
            }

            return false;
        }

        void TrySubscribeHandSubsystem()
        {
            if (!useHandPinchPlacement || handSubsystem != null)
                return;

            s_HandSubsystems.Clear();
            SubsystemManager.GetSubsystems(s_HandSubsystems);
            foreach (var subsystem in s_HandSubsystems)
            {
                if (subsystem == null)
                    continue;

                handSubsystem = subsystem;
                handSubsystem.updatedHands += OnUpdatedHands;
                return;
            }
        }

        void UnsubscribeHandSubsystem()
        {
            if (handSubsystem == null)
                return;

            handSubsystem.updatedHands -= OnUpdatedHands;
            handSubsystem = null;
            wasLeftPinching = false;
            wasRightPinching = false;
        }

        void OnUpdatedHands(
            XRHandSubsystem subsystem,
            XRHandSubsystem.UpdateSuccessFlags updateSuccessFlags,
            XRHandSubsystem.UpdateType updateType)
        {
            if (!CanUseHands() || updateType != XRHandSubsystem.UpdateType.Dynamic)
                return;

            var leftPinchStarted = UpdatePinchState(
                subsystem.leftHand,
                HasUpdateSuccessFlag(updateSuccessFlags, XRHandSubsystem.UpdateSuccessFlags.LeftHandJoints),
                ref wasLeftPinching);
            var rightPinchStarted = UpdatePinchState(
                subsystem.rightHand,
                HasUpdateSuccessFlag(updateSuccessFlags, XRHandSubsystem.UpdateSuccessFlags.RightHandJoints),
                ref wasRightPinching);

            if (rightPinchStarted)
                lastPinchHandedness = Handedness.Right;
            else if (leftPinchStarted)
                lastPinchHandedness = Handedness.Left;

            if (placementInputsArmed && (leftPinchStarted || rightPinchStarted))
                TryPlaceCube();
        }

        bool UpdatePinchState(XRHand hand, bool hasJointUpdate, ref bool wasPinching)
        {
            if (!hasJointUpdate)
                return false;

            var isPinching = IsHandPinching(hand, wasPinching);
            var startedPinching = isPinching && !wasPinching;
            wasPinching = isPinching;
            return startedPinching;
        }

        bool IsHandPinching(XRHand hand, bool wasPinching)
        {
            var commonHandGestures = hand.handedness == Handedness.Left
                ? handSubsystem.leftHandCommonGestures
                : hand.handedness == Handedness.Right
                    ? handSubsystem.rightHandCommonGestures
                    : null;

            if (commonHandGestures != null && commonHandGestures.TryGetPinchValue(out var pinchValue))
            {
                return wasPinching
                    ? pinchValue > pinchReleaseThreshold
                    : pinchValue >= pinchPressThreshold;
            }

            if (!TryGetPinchDistance(hand, out var pinchDistance))
                return false;

            return wasPinching
                ? pinchDistance <= pinchDistanceReleaseThreshold
                : pinchDistance <= pinchDistancePressThreshold;
        }

        static bool TryGetPinchDistance(XRHand hand, out float distance)
        {
            var thumbTip = hand.GetJoint(XRHandJointID.ThumbTip);
            var indexTip = hand.GetJoint(XRHandJointID.IndexTip);
            if (thumbTip.TryGetPose(out var thumbPose) &&
                indexTip.TryGetPose(out var indexPose))
            {
                distance = Vector3.Distance(thumbPose.position, indexPose.position);
                return true;
            }

            distance = 0f;
            return false;
        }

        static bool HasUpdateSuccessFlag(
            XRHandSubsystem.UpdateSuccessFlags successFlags,
            XRHandSubsystem.UpdateSuccessFlags successFlag)
        {
            return (successFlags & successFlag) == successFlag;
        }

        public bool TryPlaceCube()
        {
            if (spawnedCube != null && !allowReplaceExistingCube)
                return false;

            if (!TryGetPlacementHit(out var pose, out var plane))
                return false;

            PlaceAt(pose, plane);
            return true;
        }

        public bool TryGetPlacementHit(out Pose pose, out ARPlane plane)
        {
            pose = default;
            plane = null;

            if (raycastManager == null || !TryGetPlacementRay(out var ray))
                return false;

            return TryGetPlacementHit(ray, out pose, out plane);
        }

        public bool TryGetControllerPlacementHit(InputDeviceCharacteristics handedness, out Pose pose, out ARPlane plane)
        {
            pose = default;
            plane = null;

            if (!CanUseControllers())
                return false;

            return TryGetControllerRay(handedness, out var ray) &&
                TryGetPlacementHit(ray, out pose, out plane);
        }

        public bool TryGetHandPlacementHit(Handedness handedness, out Pose pose, out ARPlane plane)
        {
            pose = default;
            plane = null;

            if (!CanUseHands())
                return false;

            if (handSubsystem == null)
                TrySubscribeHandSubsystem();

            return TryGetHandAimRay(handedness, out var ray) &&
                TryGetPlacementHit(ray, out pose, out plane);
        }

        public bool TryGetPlacementHit(Ray ray, out Pose pose, out ARPlane plane)
        {
            pose = default;
            plane = null;

            if (raycastManager == null)
                return false;

            if (!raycastManager.Raycast(ray, s_Hits, TrackableType.PlaneWithinPolygon))
                return false;

            foreach (var hit in s_Hits)
            {
                var hitPlane = planeManager != null ? planeManager.GetPlane(hit.trackableId) : null;
                if (!ShouldAcceptPlane(hit.pose, hitPlane))
                    continue;

                pose = hit.pose;
                plane = hitPlane;
                return true;
            }

            return false;
        }

        bool TryGetPlacementRay(out Ray ray)
        {
            if (CanUseHands() && lastPinchHandedness == Handedness.Right && TryGetHandAimRay(Handedness.Right, out ray))
                return true;

            if (CanUseHands() && lastPinchHandedness == Handedness.Left && TryGetHandAimRay(Handedness.Left, out ray))
                return true;

            if (CanUseControllers() && lastPressedControllerHandedness != 0 && TryGetControllerRay(lastPressedControllerHandedness, out ray))
                return true;

            if (CanUseControllers() &&
                (TryGetControllerRay(InputDeviceCharacteristics.Right, out ray) ||
                TryGetControllerRay(InputDeviceCharacteristics.Left, out ray)))
            {
                return true;
            }

            if (CanUseHands() &&
                (TryGetHandAimRay(Handedness.Right, out ray) ||
                TryGetHandAimRay(Handedness.Left, out ray)))
            {
                return true;
            }

            ray = default;
            return false;
        }

        bool TryGetControllerRay(InputDeviceCharacteristics handedness, out Ray ray)
        {
            if (TryGetControllerPointerActionRay(handedness, out ray))
                return true;

            return TryGetControllerDeviceRay(handedness, out ray);
        }

        public bool CanUseControllers()
        {
            if (!useControllerTriggerPlacement)
                return false;

            if (inputSourcePriority == InputSourcePriority.HandOnly)
                return false;

            if (inputSourcePriority == InputSourcePriority.ControllerOnly)
                return true;

            if (inputSourcePriority == InputSourcePriority.HandFirst && IsAnyHandTracked())
                return false;

            return inputSourcePriority == InputSourcePriority.HandFirst ||
                IsAnyControllerTracked();
        }

        public bool CanUseHands()
        {
            if (!useHandPinchPlacement)
                return false;

            if (inputSourcePriority == InputSourcePriority.ControllerOnly)
                return false;

            if (inputSourcePriority == InputSourcePriority.HandOnly)
                return true;

            if (inputSourcePriority == InputSourcePriority.ControllerFirst && IsAnyControllerTracked())
                return false;

            return inputSourcePriority == InputSourcePriority.ControllerFirst ||
                IsAnyHandTracked();
        }

        public bool IsAnyControllerTracked()
        {
            return IsControllerTracked(InputDeviceCharacteristics.Left) ||
                IsControllerTracked(InputDeviceCharacteristics.Right);
        }

        public bool IsAnyControllerActive()
        {
            return IsControllerActive(InputDeviceCharacteristics.Left) ||
                IsControllerActive(InputDeviceCharacteristics.Right);
        }

        public bool IsAnyHandTracked()
        {
            if (handSubsystem == null)
                TrySubscribeHandSubsystem();

            return IsHandTracked(Handedness.Left) || IsHandTracked(Handedness.Right);
        }

        static bool IsControllerTracked(InputDeviceCharacteristics handedness)
        {
            s_InputDevices.Clear();
            InputDevices.GetDevicesWithCharacteristics(
                handedness | InputDeviceCharacteristics.Controller,
                s_InputDevices);

            foreach (var device in s_InputDevices)
            {
                if (!device.isValid)
                    continue;

                if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trackingState, out InputTrackingState trackingState))
                {
                    if ((trackingState & InputTrackingState.Position) != 0 ||
                        (trackingState & InputTrackingState.Rotation) != 0)
                    {
                        return true;
                    }
                }

                if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out var isTracked) && isTracked)
                    return true;
            }

            return false;
        }

        static bool IsControllerActive(InputDeviceCharacteristics handedness)
        {
            s_InputDevices.Clear();
            InputDevices.GetDevicesWithCharacteristics(
                handedness | InputDeviceCharacteristics.Controller,
                s_InputDevices);

            foreach (var device in s_InputDevices)
            {
                if (!device.isValid)
                    continue;

                if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.userPresence, out var userPresent) &&
                    !userPresent)
                {
                    continue;
                }

                if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out var trigger) && trigger)
                    return true;

                if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out var grip) && grip)
                    return true;

                if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out var primary) && primary)
                    return true;

                if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out var secondary) && secondary)
                    return true;

                if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxisTouch, out var axisTouch) && axisTouch)
                    return true;
            }

            return false;
        }

        static bool TryGetControllerDeviceRay(InputDeviceCharacteristics handedness, out Ray ray)
        {
            s_InputDevices.Clear();
            InputDevices.GetDevicesWithCharacteristics(
                handedness | InputDeviceCharacteristics.Controller,
                s_InputDevices);

            foreach (var device in s_InputDevices)
            {
                if (device.isValid &&
                    device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out var position) &&
                    device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out var rotation))
                {
                    ray = new Ray(position, rotation * Vector3.forward);
                    return true;
                }
            }

            ray = default;
            return false;
        }

        bool TryGetControllerPointerActionRay(InputDeviceCharacteristics handedness, out Ray ray)
        {
            var positionAction = (handedness & InputDeviceCharacteristics.Left) != 0
                ? leftControllerPointerPositionAction
                : rightControllerPointerPositionAction;
            var rotationAction = (handedness & InputDeviceCharacteristics.Left) != 0
                ? leftControllerPointerRotationAction
                : rightControllerPointerRotationAction;
            var trackingStateAction = (handedness & InputDeviceCharacteristics.Left) != 0
                ? leftControllerTrackingStateAction
                : rightControllerTrackingStateAction;

            if (positionAction == null ||
                rotationAction == null ||
                positionAction.controls.Count == 0 ||
                rotationAction.controls.Count == 0)
            {
                ray = default;
                return false;
            }

            if (trackingStateAction != null && trackingStateAction.controls.Count > 0)
            {
                var trackingState = (InputTrackingState)trackingStateAction.ReadValue<int>();
                var hasPositionAndRotation =
                    (trackingState & InputTrackingState.Position) != 0 &&
                    (trackingState & InputTrackingState.Rotation) != 0;
                if (!hasPositionAndRotation)
                {
                    ray = default;
                    return false;
                }
            }

            var position = positionAction.ReadValue<Vector3>();
            var rotation = rotationAction.ReadValue<Quaternion>();
            ray = new Ray(position, rotation * Vector3.forward);
            return true;
        }

        void CreateControllerPointerActions()
        {
            leftControllerPointerPositionAction ??= CreateValueAction(
                "Left Controller Pointer Position",
                "<XRController>{LeftHand}/pointerPosition",
                "Vector3");
            leftControllerPointerRotationAction ??= CreateValueAction(
                "Left Controller Pointer Rotation",
                "<XRController>{LeftHand}/pointerRotation",
                "Quaternion");
            leftControllerTrackingStateAction ??= CreateValueAction(
                "Left Controller Tracking State",
                "<XRController>{LeftHand}/trackingState",
                "Integer");
            rightControllerPointerPositionAction ??= CreateValueAction(
                "Right Controller Pointer Position",
                "<XRController>{RightHand}/pointerPosition",
                "Vector3");
            rightControllerPointerRotationAction ??= CreateValueAction(
                "Right Controller Pointer Rotation",
                "<XRController>{RightHand}/pointerRotation",
                "Quaternion");
            rightControllerTrackingStateAction ??= CreateValueAction(
                "Right Controller Tracking State",
                "<XRController>{RightHand}/trackingState",
                "Integer");
        }

        static InputAction CreateValueAction(string name, string binding, string expectedControlType)
        {
            return new InputAction(
                name,
                InputActionType.Value,
                binding,
                expectedControlType: expectedControlType);
        }

        void SetControllerPointerActionsEnabled(bool enabled)
        {
            SetActionEnabled(leftControllerPointerPositionAction, enabled);
            SetActionEnabled(leftControllerPointerRotationAction, enabled);
            SetActionEnabled(leftControllerTrackingStateAction, enabled);
            SetActionEnabled(rightControllerPointerPositionAction, enabled);
            SetActionEnabled(rightControllerPointerRotationAction, enabled);
            SetActionEnabled(rightControllerTrackingStateAction, enabled);
        }

        static void SetActionEnabled(InputAction action, bool enabled)
        {
            if (action == null)
                return;

            if (enabled)
                action.Enable();
            else
                action.Disable();
        }

        void DisposeControllerPointerActions()
        {
            leftControllerPointerPositionAction?.Dispose();
            leftControllerPointerRotationAction?.Dispose();
            leftControllerTrackingStateAction?.Dispose();
            rightControllerPointerPositionAction?.Dispose();
            rightControllerPointerRotationAction?.Dispose();
            rightControllerTrackingStateAction?.Dispose();
        }

        bool TryGetHandAimRay(Handedness handedness, out Ray ray)
        {
            if (handSubsystem == null)
            {
                ray = default;
                return false;
            }

            var commonHandGestures = handedness == Handedness.Left
                ? handSubsystem.leftHandCommonGestures
                : handedness == Handedness.Right
                    ? handSubsystem.rightHandCommonGestures
                    : null;

            if (commonHandGestures != null && commonHandGestures.TryGetAimPose(out var aimPose))
            {
                ray = new Ray(aimPose.position, aimPose.rotation * Vector3.forward);
                return true;
            }

            ray = default;
            return false;
        }

        bool IsHandTracked(Handedness handedness)
        {
            if (handSubsystem == null)
                return false;

            var hand = handedness == Handedness.Left
                ? handSubsystem.leftHand
                : handedness == Handedness.Right
                    ? handSubsystem.rightHand
                    : default;

            return hand.isTracked;
        }

        bool ShouldAcceptPlane(Pose hitPose, ARPlane plane)
        {
            if (plane == null)
                return hitPose.position.y >= minimumPlacementHeight;

            if (requireHorizontalUpPlane && plane.alignment != PlaneAlignment.HorizontalUp)
                return false;

            var classifications = plane.classifications;
            if (preferTableClassifiedPlanes && HasClassification(classifications, PlaneClassifications.Table))
                return true;

            if (rejectFloorPlanes && HasClassification(classifications, PlaneClassifications.Floor))
                return false;

            return hitPose.position.y >= minimumPlacementHeight;
        }

        static bool HasClassification(PlaneClassifications classifications, PlaneClassifications classification)
        {
            return (classifications & classification) == classification;
        }

        void PlaceAt(Pose pose, ARPlane plane)
        {
            if (spawnedCube != null)
                ClearPlacement();

            currentAnchor = CreateAnchor(pose, plane);
            if (currentAnchor != null)
            {
                spawnedCube = CreateCube(currentAnchor.transform);
            }
            else
            {
                var position = pose.position + Vector3.up * verticalOffset;
                spawnedCube = CreateCube(position, Quaternion.identity);
            }
        }

        ARAnchor CreateAnchor(Pose pose, ARPlane plane)
        {
            if (anchorManager == null)
                return null;

            if (plane != null)
            {
                var anchor = anchorManager.AttachAnchor(plane, pose);
                if (anchor != null)
                    return anchor;
            }

            var anchorObject = new GameObject("Placed AR Cube Anchor");
            anchorObject.transform.SetPositionAndRotation(pose.position, pose.rotation);
            return anchorObject.AddComponent<ARAnchor>();
        }

        GameObject CreateCube(Vector3 position, Quaternion rotation)
        {
            GameObject cube;
            if (cubePrefab != null)
            {
                cube = Instantiate(cubePrefab, position, rotation);
            }
            else
            {
                cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetPositionAndRotation(position, rotation);
                cube.transform.localScale = Vector3.one * defaultCubeSize;
            }

            cube.name = "Placed AR Cube";
            ConfigureCubePhysics(cube);
            return cube;
        }

        static void ConfigureCubePhysics(GameObject cube)
        {
            var rigidbody = cube.GetComponent<Rigidbody>();
            if (rigidbody == null)
                return;

            rigidbody.useGravity = false;
            rigidbody.isKinematic = true;
        }

        GameObject CreateCube(Transform parent)
        {
            var cube = CreateCube(parent.position, parent.rotation);
            cube.transform.SetParent(parent, worldPositionStays: false);
            cube.transform.localPosition = Vector3.up * verticalOffset;
            cube.transform.localRotation = Quaternion.identity;
            return cube;
        }

        public void ClearPlacement()
        {
            if (currentAnchor != null)
            {
                Destroy(currentAnchor.gameObject);
                currentAnchor = null;
                spawnedCube = null;
                return;
            }

            if (spawnedCube != null)
            {
                Destroy(spawnedCube);
                spawnedCube = null;
            }
        }
    }
}
