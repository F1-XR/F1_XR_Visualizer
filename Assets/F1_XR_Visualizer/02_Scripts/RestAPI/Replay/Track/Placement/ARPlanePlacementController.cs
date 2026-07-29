using System;
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
        private enum InputSourcePriority
        {
            ControllerFirst,
            HandFirst,
            ControllerOnly,
            HandOnly
        }

        [Header("AR Managers")]
        [SerializeField] private ARRaycastManager raycastManager;
        [SerializeField] private ARPlaneManager planeManager;
        [SerializeField] private ARAnchorManager anchorManager;

        [Header("Placement")]
        [SerializeField] private Transform rayOrigin;
        [SerializeField] private GameObject cubePrefab;
        [SerializeField] private GameObject trackMapPrefab;
        [SerializeField] private float trackMapScale = 1f;
        [SerializeField] private bool fitTrackMapToBounds;
        [SerializeField] private Vector2 trackMapTargetXZSize;
        [SerializeField] private bool allowReplaceExistingCube = false;
        [SerializeField] private bool requireHorizontalUpPlane = true;
        [SerializeField] private bool rejectFloorPlanes = true;
        [SerializeField] private bool preferTableClassifiedPlanes = true;
        [SerializeField] private float minimumPlacementHeight = 0.35f;
        [SerializeField] private float verticalOffset = 0.04f;
        [SerializeField] private float defaultCubeSize = 0.08f;

        [Header("No Room Data Fallback")]
        [SerializeField] private bool allowWorldFloorFallback;
        [SerializeField] private float fallbackFloorHeight;
        [SerializeField] private float fallbackMaximumDistance = 8f;

        [Header("Optional Input")]
        [SerializeField] private InputActionProperty placeAction;
        [SerializeField] private InputSourcePriority inputSourcePriority = InputSourcePriority.HandFirst;
        [SerializeField] private bool useControllerTriggerPlacement = true;
        [SerializeField] private bool useHandPinchPlacement = true;
        [SerializeField] private bool handlePlacementInput = true;
        [SerializeField] private float inputArmDelay = 0.5f;
        [SerializeField, Range(0f, 1f)] private float pinchPressThreshold = 0.8f;
        [SerializeField, Range(0f, 1f)] private float pinchReleaseThreshold = 0.55f;
        [SerializeField] private float pinchDistancePressThreshold = 0.025f;
        [SerializeField] private float pinchDistanceReleaseThreshold = 0.04f;

        public bool HasPlacement => spawnedCube != null;
        public Transform PlacementTransform => spawnedCube != null ? spawnedCube.transform : null;
        public Vector3 PlacementPosition => spawnedCube != null ? spawnedCube.transform.position : Vector3.zero;
        public GameObject PlacementPrefab => cubePrefab;
        public event Action PlacementRequested;

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

        private void Reset()
        {
            raycastManager = GetComponent<ARRaycastManager>();
            planeManager = GetComponent<ARPlaneManager>();
            anchorManager = GetComponent<ARAnchorManager>();

            if (Camera.main != null)
                rayOrigin = Camera.main.transform;
        }

        private void Awake()
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

        private void OnEnable()
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

        private void OnDisable()
        {
            if (placeAction.action != null)
                placeAction.action.Disable();

            SetControllerPointerActionsEnabled(false);
            UnsubscribeHandSubsystem();
        }

        private void OnDestroy()
        {
            DisposeControllerPointerActions();
        }

        private void Update()
        {
            if (useHandPinchPlacement && handSubsystem == null)
                TrySubscribeHandSubsystem();

            if (!placementInputsArmed)
            {
                if (Time.time >= enableTime + inputArmDelay && !IsAnyPlacementInputHeld())
                    placementInputsArmed = true;

                return;
            }

            if (!handlePlacementInput)
                return;

            var triggerPressedThisFrame = CanUseControllers() &&
                WasTriggerPressedThisFrame();

            if (placeAction.action != null && placeAction.action.WasPressedThisFrame())
            {
                RequestPlacement();
                return;
            }

            if (triggerPressedThisFrame)
            {
                RequestPlacement();
                return;
            }
        }

        private static readonly List<XRHandSubsystem> s_HandSubsystems = new();

        private XRHandSubsystem handSubsystem;
        private bool wasLeftPinching;
        private bool wasRightPinching;
        private Handedness lastPinchHandedness = Handedness.Invalid;

        private void TrySubscribeHandSubsystem()
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

        private void UnsubscribeHandSubsystem()
        {
            if (handSubsystem == null)
                return;

            handSubsystem.updatedHands -= OnUpdatedHands;
            handSubsystem = null;
            wasLeftPinching = false;
            wasRightPinching = false;
        }

        private void OnUpdatedHands(
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
                RequestPlacement();
        }

        private void RequestPlacement()
        {
            if (PlacementRequested != null)
                PlacementRequested.Invoke();
            else if (handlePlacementInput)
                TryPlaceCube();
        }

        private bool UpdatePinchState(XRHand hand, bool hasJointUpdate, ref bool wasPinching)
        {
            if (!hasJointUpdate)
                return false;

            var isPinching = IsHandPinching(hand, wasPinching);
            var startedPinching = isPinching && !wasPinching;
            wasPinching = isPinching;
            return startedPinching;
        }

        private bool IsHandPinching(XRHand hand, bool wasPinching)
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

        private static bool TryGetPinchDistance(XRHand hand, out float distance)
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

        private static bool HasUpdateSuccessFlag(
            XRHandSubsystem.UpdateSuccessFlags successFlags,
            XRHandSubsystem.UpdateSuccessFlags successFlag)
        {
            return (successFlags & successFlag) == successFlag;
        }

        private bool TryGetHandAimRay(Handedness handedness, out Ray ray)
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

        private bool IsHandTracked(Handedness handedness)
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

        private static readonly List<ARRaycastHit> s_Hits = new();
        private ARPlane preferredPlacementPlane;

        public bool TryGetPlacementHit(out Pose pose, out ARPlane plane)
        {
            pose = default;
            plane = null;

            if (!TryGetPlacementRay(out var ray))
                return false;

            return TryGetPlacementHit(ray, out pose, out plane);
        }

        public bool TryGetAutomaticTableHit(out Pose pose, out ARPlane plane)
        {
            pose = default;
            plane = null;

            if (planeManager == null)
                return false;

            ARPlane bestPlane = null;
            Pose bestPose = default;
            float bestScore = float.NegativeInfinity;
            foreach (ARPlane candidate in planeManager.trackables)
            {
                if (!TryUseAutomaticPlane(candidate, out Pose candidatePose))
                    continue;

                float score = ScoreAutomaticPlane(candidate);
                if (preferredPlacementPlane != null &&
                    candidate.trackableId ==
                    preferredPlacementPlane.trackableId)
                    score += 1f;

                if (score <= bestScore)
                    continue;

                bestPlane = candidate;
                bestPose = candidatePose;
                bestScore = score;
            }

            if (bestPlane == null)
                return false;

            preferredPlacementPlane = bestPlane;
            pose = bestPose;
            plane = bestPlane;
            return true;
        }

        private static float ScoreAutomaticPlane(ARPlane candidate)
        {
            float area =
                candidate.size.x * candidate.size.y;
            float score =
                Mathf.Min(area, 4f) * 4f;
            if (HasClassification(
                    candidate.classifications,
                    PlaneClassifications.Table))
            {
                score += 1000f;
            }

            Camera camera = Camera.main;
            if (camera == null)
                return score;

            Vector3 offset =
                candidate.center -
                camera.transform.position;
            score -= offset.magnitude * 4f;

            Vector3 forward = Vector3.ProjectOnPlane(
                camera.transform.forward,
                Vector3.up);
            Vector3 direction = Vector3.ProjectOnPlane(
                offset,
                Vector3.up);
            if (forward.sqrMagnitude > 0.0001f &&
                direction.sqrMagnitude > 0.0001f)
            {
                score -= Vector3.Angle(
                    forward,
                    direction) * 0.04f;
            }

            return score;
        }

        private bool TryUseAutomaticPlane(ARPlane candidate, out Pose pose)
        {
            pose = default;
            if (candidate == null ||
                !candidate.gameObject.activeInHierarchy)
            {
                return false;
            }

            pose = new Pose(candidate.center, Quaternion.identity);
            return ShouldAcceptPlane(pose, candidate);
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

            if (raycastManager != null && raycastManager.enabled &&
                raycastManager.Raycast(ray, s_Hits, TrackableType.PlaneWithinPolygon))
            {
                if (preferredPlacementPlane != null)
                {
                    foreach (var hit in s_Hits)
                    {
                        var hitPlane = planeManager != null ? planeManager.GetPlane(hit.trackableId) : null;
                        if (hitPlane == null ||
                            hitPlane.trackableId != preferredPlacementPlane.trackableId ||
                            !ShouldAcceptPlane(hit.pose, hitPlane))
                        {
                            continue;
                        }

                        pose = hit.pose;
                        plane = hitPlane;
                        return true;
                    }
                }

                foreach (var hit in s_Hits)
                {
                    var hitPlane = planeManager != null ? planeManager.GetPlane(hit.trackableId) : null;
                    if (!ShouldAcceptPlane(hit.pose, hitPlane))
                        continue;

                    pose = hit.pose;
                    plane = hitPlane;
                    preferredPlacementPlane = hitPlane;
                    return true;
                }
            }

            return TryGetWorldFloorFallback(ray, out pose);
        }

        private bool TryGetWorldFloorFallback(Ray ray, out Pose pose)
        {
            pose = default;
            if (!allowWorldFloorFallback)
                return false;

            var floor = new Plane(Vector3.up, new Vector3(0f, fallbackFloorHeight, 0f));
            if (!floor.Raycast(ray, out var distance) ||
                distance <= 0f ||
                distance > fallbackMaximumDistance)
            {
                return false;
            }

            pose = new Pose(ray.GetPoint(distance), Quaternion.identity);
            return true;
        }

        private bool TryGetPlacementRay(out Ray ray)
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

        private bool ShouldAcceptPlane(Pose hitPose, ARPlane plane)
        {
            if (plane == null)
                return hitPose.position.y >= minimumPlacementHeight;

            if (requireHorizontalUpPlane && plane.alignment != PlaneAlignment.HorizontalUp)
                return false;

            var classifications = plane.classifications;
            if (rejectFloorPlanes && HasClassification(classifications, PlaneClassifications.Floor))
                return false;

            if (preferTableClassifiedPlanes && HasClassification(classifications, PlaneClassifications.Table))
                return true;

            return hitPose.position.y >= minimumPlacementHeight;
        }

        private static bool HasClassification(PlaneClassifications classifications, PlaneClassifications classification)
        {
            return (classifications & classification) == classification;
        }

        private ARAnchor currentAnchor;
        private GameObject spawnedCube;

        public bool TryPlaceCube()
        {
            if (spawnedCube != null && !allowReplaceExistingCube)
                return false;

            if (!TryGetPlacementHit(out var pose, out var plane))
                return false;

            PlaceAt(pose, plane);
            return true;
        }

        private void PlaceAt(Pose pose, ARPlane plane)
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

        private ARAnchor CreateAnchor(Pose pose, ARPlane plane)
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

        private GameObject CreateCube(Vector3 position, Quaternion rotation)
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

        private void ApplyTrackMap(GameObject target)
        {
            if (trackMapPrefab == null || target == null)
                return;

            TrackMapView mapView = target.GetComponent<TrackMapView>();
            if (mapView != null)
                mapView.Show(trackMapPrefab, trackMapScale, fitTrackMapToBounds, trackMapTargetXZSize);
        }

        private static void ConfigureCubePhysics(GameObject cube)
        {
            foreach (var rigidbody in cube.GetComponentsInChildren<Rigidbody>(includeInactive: true))
            {
                rigidbody.useGravity = false;
                rigidbody.isKinematic = true;
            }
        }

        private GameObject CreateCube(Transform parent)
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
