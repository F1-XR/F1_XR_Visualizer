using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Hands;
using F1XR.RestAPI.Replay.Track;

namespace F1XR.RestAPI.Replay.Track.Placement
{
    public sealed partial class ARPlanePlacementController : MonoBehaviour
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
        [SerializeField] GameObject trackMapPrefab;
        [SerializeField] float trackMapScale = 1f;
        [SerializeField] bool fitTrackMapToBounds;
        [SerializeField] Vector2 trackMapTargetXZSize;
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
        [SerializeField] bool handlePlacementInput = true;
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
        public GameObject PlacementPrefab => cubePrefab;

        public void SetPlacementPrefab(
            GameObject prefab,
            GameObject mapPrefab = null,
            float mapScale = 1f,
            bool fitMapToBounds = false,
            Vector2 mapTargetXZSize = default)
        {
            cubePrefab = prefab;
            trackMapPrefab = mapPrefab;
            trackMapScale = mapScale > 0f ? mapScale : 1f;
            fitTrackMapToBounds = fitMapToBounds;
            trackMapTargetXZSize = mapTargetXZSize;
        }
        
        bool wasLeftTriggerPressed;
        bool wasRightTriggerPressed;
        XRHandSubsystem handSubsystem;
        bool wasLeftPinching;
        bool wasRightPinching;
        bool placementInputsArmed;
        float enableTime;
        InputDeviceCharacteristics lastTriggerHand;
        Handedness lastPinchHandedness = Handedness.Invalid;
        InputAction leftPointerPos;
        InputAction leftPointerRot;
        InputAction leftTrackingState;
        InputAction rightPointerPos;
        InputAction rightPointerRot;
        InputAction rightTrackingState;

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
            wasLeftTriggerPressed = false;
            wasRightTriggerPressed = false;
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
            if (!handlePlacementInput)
            {
                if (useHandPinchPlacement && handSubsystem == null)
                    TrySubscribeHandSubsystem();

                return;
            }

            var triggerPressedThisFrame = CanUseControllers() &&
                                          WasTriggerPressedThisFrame();

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

            if (triggerPressedThisFrame)
            {
                TryPlaceCube();
                return;
            }

            if (useHandPinchPlacement && handSubsystem == null)
                TrySubscribeHandSubsystem();
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

            if (CanUseControllers() && lastTriggerHand != 0 && TryGetControllerRay(lastTriggerHand, out ray))
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
                ApplyTrackMap(cube);
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

        void ApplyTrackMap(GameObject target)
        {
            if (trackMapPrefab == null || target == null)
                return;

            TrackMapView mapView = target.GetComponent<TrackMapView>();
            if (mapView != null)
                mapView.Show(trackMapPrefab, trackMapScale, fitTrackMapToBounds, trackMapTargetXZSize);
        }

        static void ConfigureCubePhysics(GameObject cube)
        {
            foreach (var rigidbody in cube.GetComponentsInChildren<Rigidbody>(includeInactive: true))
            {
                rigidbody.useGravity = false;
                rigidbody.isKinematic = true;
            }
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
