using System;
using System.Collections;
using System.Collections.Generic;
using F1XR.RestAPI.Replay.Track.Build;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace F1XR.Drone
{
    [DisallowMultipleComponent]
    public sealed class VRDroneCoordinator : MonoBehaviour
    {
        const string EnvironmentName = "VRDroneEnvironment";

        [SerializeField, Min(1f)] float vrScaleMultiplier = 1000f;

        TrackRevealPlacer trackPlacer;
        XROrigin xrOrigin;
        Camera xrCamera;
        GameObject passthroughLayer;
        GameObject environment;
        DroneViewCubeSpawner cubeSpawner;
        VRDroneFlightController flightController;
        Transform placementRoot;
        Transform visualRoot;
        Transform hiddenCube;
        GameObject ground;
        VRDroneHud droneHud;
        readonly List<XRBaseInteractable> disabledInteractables = new();

        Vector3 savedOriginPosition;
        Quaternion savedOriginRotation;
        Vector3 savedPlacementLocalScale;
        Vector3 savedVisualLocalPosition;
        Quaternion savedVisualLocalRotation;
        Vector3 savedVisualLocalScale;
        CameraClearFlags savedClearFlags;
        Color savedBackgroundColor;
        bool savedPassthroughActive;
        bool savedTrackEditMode;
        bool isVrActive;
        Scene hostScene;
        bool hasHostScene;

        public bool IsVrActive => isVrActive;
        public Transform VrCameraTransform =>
            xrCamera != null ? xrCamera.transform : null;

        public void ConfigureHostScene(Scene scene)
        {
            if (!scene.isLoaded)
            {
                Debug.LogError(
                    "[VRDrone] The configured host scene is not loaded.",
                    this);
                return;
            }

            hostScene = scene;
            hasHostScene = true;
        }

        void Start()
        {
            StartCoroutine(Initialize());
        }

        void OnDestroy()
        {
            if (cubeSpawner != null)
                cubeSpawner.CubeReleased -= EnterVr;
        }

        IEnumerator Initialize()
        {
            while (!TryResolveReferences())
                yield return null;

            cubeSpawner = trackPlacer.GetComponent<DroneViewCubeSpawner>();
            if (cubeSpawner == null)
                cubeSpawner = trackPlacer.gameObject.AddComponent<DroneViewCubeSpawner>();

            cubeSpawner.Configure(trackPlacer, xrCamera.transform);
            cubeSpawner.CubeReleased -= EnterVr;
            cubeSpawner.CubeReleased += EnterVr;

            flightController = GetComponent<VRDroneFlightController>();
            if (flightController == null)
            {
                Debug.LogError(
                    "[VRDrone] VRDroneFlightController is missing on VRDroneRuntime.",
                    this);
            }
            else
            {
                flightController.Configure(this);
            }

            environment.SetActive(false);
            EnsureEnvironment();
            droneHud = GetComponent<VRDroneHud>();
            if (droneHud == null)
                droneHud = gameObject.AddComponent<VRDroneHud>();
            droneHud.Configure(environment.transform);
        }

        bool TryResolveReferences()
        {
            Scene vrScene = gameObject.scene;
            if (!hasHostScene || !hostScene.isLoaded || !vrScene.isLoaded)
                return false;

            trackPlacer ??= FindInScene<TrackRevealPlacer>(hostScene);
            xrOrigin ??= FindInScene<XROrigin>(hostScene);
            environment ??= FindRoot(vrScene, EnvironmentName);
            passthroughLayer ??= FindInScene(hostScene, "Passthrough Layer");
            xrCamera ??= xrOrigin != null ? xrOrigin.Camera : Camera.main;

            return trackPlacer != null &&
                xrOrigin != null &&
                xrCamera != null &&
                environment != null &&
                passthroughLayer != null;
        }

        void EnterVr(Transform cubeTransform)
        {
            if (isVrActive || cubeTransform == null ||
                trackPlacer == null || !trackPlacer.HasPlacement)
            {
                return;
            }

            Transform placement = trackPlacer.PlacementTransform;
            placementRoot = placement;
            visualRoot = placementRoot != null
                ? placementRoot.Find("Visual") ?? placementRoot
                : null;
            if (visualRoot == null)
                return;

            Vector3 cubePlacementLocal =
                placementRoot.InverseTransformPoint(cubeTransform.position);
            SaveMrState();
            LockTrackInteraction();

            hiddenCube = cubeTransform;
            hiddenCube.gameObject.SetActive(false);
            passthroughLayer.SetActive(false);
            environment.SetActive(true);

            placementRoot.localScale = Vector3.Scale(
                savedPlacementLocalScale,
                Vector3.one * vrScaleMultiplier);

            Vector3 target = placementRoot.TransformPoint(cubePlacementLocal);
            xrOrigin.MoveCameraToWorldLocation(target);
            xrCamera.clearFlags = CameraClearFlags.Skybox;
            xrCamera.backgroundColor = new Color(0.015f, 0.02f, 0.04f, 1f);

            PlaceGround(placementRoot.up);
            isVrActive = true;
            droneHud?.Show(xrCamera);
            flightController?.ResetFlight();
        }

        public void ExitVr()
        {
            if (!isVrActive)
                return;

            if (visualRoot != null)
            {
                visualRoot.localPosition = savedVisualLocalPosition;
                visualRoot.localRotation = savedVisualLocalRotation;
                visualRoot.localScale = savedVisualLocalScale;
            }

            if (placementRoot != null)
                placementRoot.localScale = savedPlacementLocalScale;

            xrOrigin.transform.SetPositionAndRotation(
                savedOriginPosition,
                savedOriginRotation);
            xrCamera.clearFlags = savedClearFlags;
            xrCamera.backgroundColor = savedBackgroundColor;

            droneHud?.Hide();
            environment.SetActive(false);
            passthroughLayer.SetActive(savedPassthroughActive);
            if (hiddenCube != null)
                hiddenCube.gameObject.SetActive(true);

            RestoreTrackInteraction();
            isVrActive = false;
            flightController?.ResetFlight();
        }

        public void ApplyDroneMotion(Vector3 movement, float yaw)
        {
            if (!isVrActive || xrOrigin == null || xrCamera == null)
                return;

            if (Mathf.Abs(yaw) > Mathf.Epsilon)
            {
                xrOrigin.transform.RotateAround(
                    xrCamera.transform.position,
                    Vector3.up,
                    yaw);
            }

            xrOrigin.transform.position += movement;
        }

        public void SetExitHoldProgress(float normalizedProgress)
        {
            droneHud?.SetExitHoldProgress(normalizedProgress);
        }

        void SaveMrState()
        {
            savedOriginPosition = xrOrigin.transform.position;
            savedOriginRotation = xrOrigin.transform.rotation;
            savedPlacementLocalScale = placementRoot.localScale;
            savedVisualLocalPosition = visualRoot.localPosition;
            savedVisualLocalRotation = visualRoot.localRotation;
            savedVisualLocalScale = visualRoot.localScale;
            savedClearFlags = xrCamera.clearFlags;
            savedBackgroundColor = xrCamera.backgroundColor;
            savedPassthroughActive = passthroughLayer.activeSelf;
            savedTrackEditMode = trackPlacer.IsEditMode;
        }

        float GetTrackBaseLocalY()
        {
            return GetTrackLocalY(highest: false);
        }

        float GetTrackLocalY(bool highest)
        {
            Transform cars = visualRoot.Find("Cars");
            bool hasRenderer = false;
            float result = 0f;

            foreach (Renderer renderer in
                visualRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null ||
                    cars != null && renderer.transform.IsChildOf(cars))
                {
                    continue;
                }

                Bounds bounds = renderer.bounds;
                for (int x = -1; x <= 1; x += 2)
                {
                    for (int y = -1; y <= 1; y += 2)
                    {
                        for (int z = -1; z <= 1; z += 2)
                        {
                            Vector3 worldPoint = bounds.center +
                                Vector3.Scale(bounds.extents,
                                    new Vector3(x, y, z));
                            float localY = placementRoot
                                .InverseTransformPoint(worldPoint).y;
                            bool isMoreExtreme = highest
                                ? localY > result
                                : localY < result;
                            if (!hasRenderer || isMoreExtreme)
                            {
                                result = localY;
                            }
                            hasRenderer = true;
                        }
                    }
                }
            }

            return hasRenderer ? result : 0f;
        }

        void LockTrackInteraction()
        {
            if (savedTrackEditMode)
                trackPlacer.ToggleEditMode();

            disabledInteractables.Clear();
            foreach (XRBaseInteractable interactable in
                placementRoot.GetComponentsInChildren<XRBaseInteractable>(true))
            {
                if (interactable == null || !interactable.enabled)
                    continue;

                interactable.enabled = false;
                disabledInteractables.Add(interactable);
            }
        }

        void RestoreTrackInteraction()
        {
            foreach (XRBaseInteractable interactable in disabledInteractables)
            {
                if (interactable != null)
                    interactable.enabled = true;
            }
            disabledInteractables.Clear();

            if (savedTrackEditMode && !trackPlacer.IsEditMode)
                trackPlacer.ToggleEditMode();
        }

        void EnsureEnvironment()
        {
            if (ground == null)
            {
                ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = "VR Drone Ground";
                ground.transform.SetParent(environment.transform, false);
                ground.transform.localScale = Vector3.one * 1000f;
                Renderer renderer = ground.GetComponent<Renderer>();
                if (renderer != null)
                    renderer.material.color = new Color(0.025f, 0.035f, 0.06f, 1f);
            }

        }

        void PlaceGround(Vector3 up)
        {
            float localY = GetTrackBaseLocalY() - 1f;
            ground.transform.position = placementRoot.TransformPoint(
                new Vector3(0f, localY, 0f));
            ground.transform.rotation = Quaternion.FromToRotation(
                Vector3.up,
                up);
        }

        static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T found = root.GetComponentInChildren<T>(true);
                if (found != null)
                    return found;
            }

            return null;
        }

        static GameObject FindInScene(Scene scene, string objectName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform found = FindChild(root.transform, objectName);
                if (found != null)
                    return found.gameObject;
            }

            return null;
        }

        static GameObject FindRoot(Scene scene, string objectName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == objectName)
                    return root;
            }

            return null;
        }

        static Transform FindChild(Transform root, string objectName)
        {
            if (root.name == objectName)
                return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindChild(root.GetChild(i), objectName);
                if (found != null)
                    return found;
            }

            return null;
        }

    }
}
