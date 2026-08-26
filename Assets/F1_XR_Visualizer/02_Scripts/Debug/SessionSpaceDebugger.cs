using System;
using System.Collections;
using F1XR.Drone;
using F1XR.RestAPI.Replay.Track.Build;
using F1XR.RestAPI.Replay.Track.Placement;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace F1XR.Debugging
{
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public sealed class SessionSpaceDebugger : MonoBehaviour
    {
        [SerializeField] string vrDroneSceneName = "VRDroneSpace";
        [SerializeField] string drivingTestSceneName = "DrivingTest";
        [SerializeField, Min(1f)] float coordinatorWaitSeconds = 10f;
        [SerializeField] bool skipSpatialSetupOnPlay;
        [Header("Temporary Map")]
        [SerializeField] bool placeTemporaryMapInFrontOfVrOrigin;
        [SerializeField, Min(0.1f)] float temporaryMapDistance = 0.5f;
        [SerializeField] float temporaryMapHeightOffset = 0.5f;
        [SerializeField] Vector3 temporaryMapScale = Vector3.one * 2.5f;
        [SerializeField] float temporaryMapYawOffset = 90f;
        [SerializeField, Min(0f)] float temporaryMapModelWaitSeconds = 8f;
        [Header("VR Drone")]
        [SerializeField] bool enterVrDroneOnPlay;

        Coroutine droneEntryRoutine;

        public bool SkipSpatialSetupOnPlay =>
            skipSpatialSetupOnPlay || placeTemporaryMapInFrontOfVrOrigin;
        void Awake()
        {
            if (!SkipSpatialSetupOnPlay)
                return;

            var requesters = FindObjectsByType<QuestScenePermissionRequester>(
                FindObjectsInactive.Include);
            foreach (var requester in requesters)
            {
                if (requester.gameObject.scene == gameObject.scene)
                    requester.DisableSpatialSystemsForDebug(
                        placeTemporaryMapInFrontOfVrOrigin);
            }
        }

        void Start()
        {
            if (placeTemporaryMapInFrontOfVrOrigin)
            {
                StartCoroutine(PlaceTemporaryMapRoutine());
                return;
            }

            if (enterVrDroneOnPlay)
                EnterVrDroneWithoutPlacement();
        }

        public void EnableVrDroneBypassStart()
        {
            skipSpatialSetupOnPlay = true;
            enterVrDroneOnPlay = true;
        }

        public void EnterVrDroneWithoutPlacement()
        {
            if (!Application.isPlaying)
                return;

            if (droneEntryRoutine != null)
                return;

            droneEntryRoutine = StartCoroutine(EnterVrDroneRoutine());
        }

        public void LoadDrivingTest()
        {
            if (!Application.isPlaying)
                return;

            if (!Application.CanStreamedLevelBeLoaded(drivingTestSceneName))
            {
                Debug.LogError(
                    "[SessionDebugger] DrivingTest scene is not included in Build Settings.",
                    this);
                return;
            }

            SceneManager.LoadScene(drivingTestSceneName, LoadSceneMode.Single);
        }

        IEnumerator EnterVrDroneRoutine()
        {
            var timeoutAt = Time.unscaledTime + coordinatorWaitSeconds;

            while (Time.unscaledTime < timeoutAt)
            {
                Scene droneScene = SceneManager.GetSceneByName(vrDroneSceneName);
                var coordinator = droneScene.isLoaded
                    ? FindCoordinator(droneScene)
                    : null;

                if (coordinator != null)
                {
                    coordinator.ConfigureHostScene(gameObject.scene);
                    if (placeTemporaryMapInFrontOfVrOrigin)
                        coordinator.BeginDebugEntryFromExistingPlacement();
                    else
                        coordinator.BeginDebugEntryWithoutPlacement();

                    droneEntryRoutine = null;
                    yield break;
                }

                yield return null;
            }

            Debug.LogError(
                "[SessionDebugger] VRDroneCoordinator was not loaded. Check the BootstrapLoader host scene setup.",
                this);
            droneEntryRoutine = null;
        }

        IEnumerator PlaceTemporaryMapRoutine()
        {
            TrackRevealPlacer placer = null;
            XROrigin origin = null;
            var timeoutAt = Time.unscaledTime + coordinatorWaitSeconds;

            while (Time.unscaledTime < timeoutAt)
            {
                placer = FindInScene<TrackRevealPlacer>(gameObject.scene);
                origin = FindInScene<XROrigin>(gameObject.scene);
                if (placer != null && origin != null)
                    break;

                yield return null;
            }

            if (placer == null || origin == null)
            {
                Debug.LogError(
                    "[SessionDebugger] Temporary map needs TrackRevealPlacer and XROrigin in SessionSpace.",
                    this);
                yield break;
            }

            var mapReadyAt = Time.unscaledTime + temporaryMapModelWaitSeconds;
            while (!placer.HasTrackMapPrefab && Time.unscaledTime < mapReadyAt)
                yield return null;

            if (!placer.HasTrackMapPrefab)
            {
                Debug.LogWarning(
                    "[SessionDebugger] Track map model was not prepared in time. " +
                    "Creating the temporary map with the placement prefab only.",
                    this);
            }

            Transform view = origin.Camera != null
                ? origin.Camera.transform
                : origin.transform;
            Vector3 forward = Vector3.ProjectOnPlane(view.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;
            forward.Normalize();

            Vector3 position = view.position +
                forward * temporaryMapDistance +
                Vector3.up * temporaryMapHeightOffset;
            Quaternion rotation =
                Quaternion.LookRotation(forward, Vector3.up) *
                Quaternion.Euler(0f, temporaryMapYawOffset, 0f);
            bool revealCompleted = false;
            Action onPlacementRevealed = () => revealCompleted = true;
            if (enterVrDroneOnPlay)
                placer.PlacementRevealed += onPlacementRevealed;

            if (!placer.TryCreateRuntimeDebugPlacement(
                    position,
                    rotation,
                    temporaryMapScale))
            {
                if (enterVrDroneOnPlay)
                    placer.PlacementRevealed -= onPlacementRevealed;
                Debug.LogError(
                    "[SessionDebugger] Temporary map placement prefab is missing.",
                    this);
                yield break;
            }

            Debug.Log(
                $"[SessionDebugger] Temporary debug map created. " +
                $"position={position}, scale={placer.PlacementTransform.localScale}.",
                this);
            if (enterVrDroneOnPlay)
            {
                while (!revealCompleted)
                    yield return null;

                placer.PlacementRevealed -= onPlacementRevealed;
                EnterVrDroneWithoutPlacement();
            }
        }

        static VRDroneCoordinator FindCoordinator(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var coordinator = root.GetComponentInChildren<VRDroneCoordinator>(true);
                if (coordinator != null)
                    return coordinator;
            }

            return null;
        }

        static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (var component in UnityEngine.Object.FindObjectsByType<T>(
                         FindObjectsInactive.Include))
            {
                if (component.gameObject.scene == scene)
                    return component;
            }

            return null;
        }

    }
}
