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

        [Header("Placement Reticle")]
        [SerializeField] bool showPlacementReticle = true;
        [SerializeField] float reticleSize = 0.025f;
        [SerializeField] float reticleSurfaceOffset = 0.005f;
        [SerializeField] Color reticleColor = Color.red;

        [Header("Optional Input")]
        [SerializeField] InputActionProperty placeAction;
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
        bool wasLeftControllerTriggerPressed;
        bool wasRightControllerTriggerPressed;
        XRHandSubsystem handSubsystem;
        bool wasLeftPinching;
        bool wasRightPinching;
        bool placementInputsArmed;
        float enableTime;
        GameObject placementReticle;
        Material placementReticleMaterial;

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
        }

        void OnEnable()
        {
            if (placeAction.action != null)
                placeAction.action.Enable();

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

            HidePlacementReticle();
            UnsubscribeHandSubsystem();
        }

        void Update()
        {
            var controllerTriggerPressedThisFrame = useControllerTriggerPlacement &&
                WasControllerTriggerPressedThisFrame();

            if (!placementInputsArmed)
            {
                if (Time.time >= enableTime + inputArmDelay && !IsAnyPlacementInputHeld())
                    placementInputsArmed = true;

                HidePlacementReticle();
                return;
            }

            UpdatePlacementReticle();

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

        void UpdatePlacementReticle()
        {
            if (!showPlacementReticle ||
                spawnedCube != null ||
                !TryGetPlacementHit(out var pose, out _))
            {
                HidePlacementReticle();
                return;
            }

            EnsurePlacementReticle();
            placementReticle.transform.SetPositionAndRotation(
                pose.position + pose.up * reticleSurfaceOffset,
                Quaternion.identity);
            placementReticle.SetActive(true);
        }

        void EnsurePlacementReticle()
        {
            if (placementReticle != null)
                return;

            placementReticle = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            placementReticle.name = "AR Placement Reticle";
            placementReticle.transform.localScale = Vector3.one * reticleSize;

            var collider = placementReticle.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            placementReticleMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard"))
            {
                color = reticleColor
            };

            var renderer = placementReticle.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.sharedMaterial = placementReticleMaterial;

            placementReticle.SetActive(false);
        }

        void HidePlacementReticle()
        {
            if (placementReticle != null)
                placementReticle.SetActive(false);
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
            if (!useHandPinchPlacement || updateType != XRHandSubsystem.UpdateType.Dynamic)
                return;

            var leftPinchStarted = UpdatePinchState(
                subsystem.leftHand,
                HasUpdateSuccessFlag(updateSuccessFlags, XRHandSubsystem.UpdateSuccessFlags.LeftHandJoints),
                ref wasLeftPinching);
            var rightPinchStarted = UpdatePinchState(
                subsystem.rightHand,
                HasUpdateSuccessFlag(updateSuccessFlags, XRHandSubsystem.UpdateSuccessFlags.RightHandJoints),
                ref wasRightPinching);

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
            HidePlacementReticle();
            return true;
        }

        bool TryGetPlacementHit(out Pose pose, out ARPlane plane)
        {
            pose = default;
            plane = null;

            if (raycastManager == null || rayOrigin == null)
                return false;

            var ray = new Ray(rayOrigin.position, rayOrigin.forward);
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
