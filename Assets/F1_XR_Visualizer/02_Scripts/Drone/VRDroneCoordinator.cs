using System;
using System.Collections;
using System.Collections.Generic;
using F1XR.RestAPI.Replay.Track.Build;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace F1XR.Drone
{
    [DisallowMultipleComponent]
    public sealed class VRDroneCoordinator : MonoBehaviour
    {
        const string SessionSceneName = "SessionSpace";
        const string VrSceneName = "VRDroneSpace";
        const string EnvironmentName = "VRDroneEnvironment";

        [SerializeField, Min(1f)] float vrScaleMultiplier = 1000f;
        [SerializeField, Min(0.5f)] float exitUiDistance = 1.2f;

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
        Canvas exitCanvas;
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

        public bool IsVrActive => isVrActive;
        public Transform VrCameraTransform =>
            xrCamera != null ? xrCamera.transform : null;

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
        }

        bool TryResolveReferences()
        {
            Scene sessionScene = SceneManager.GetSceneByName(SessionSceneName);
            Scene vrScene = SceneManager.GetSceneByName(VrSceneName);
            if (!sessionScene.isLoaded || !vrScene.isLoaded)
                return false;

            trackPlacer ??= FindInScene<TrackRevealPlacer>(sessionScene);
            xrOrigin ??= FindInScene<XROrigin>(sessionScene);
            environment ??= FindRoot(vrScene, EnvironmentName);
            passthroughLayer ??= FindInScene(sessionScene, "Passthrough Layer");
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
            PlaceExitUi();
            isVrActive = true;
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

            if (exitCanvas == null)
                exitCanvas = CreateExitCanvas();
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

        void PlaceExitUi()
        {
            Transform cameraTransform = xrCamera.transform;
            Vector3 forward = Vector3.ProjectOnPlane(
                cameraTransform.forward,
                Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = cameraTransform.forward;
            forward.Normalize();

            Transform uiTransform = exitCanvas.transform;
            uiTransform.position = cameraTransform.position +
                forward * exitUiDistance;
            uiTransform.rotation = Quaternion.LookRotation(
                cameraTransform.position - uiTransform.position,
                Vector3.up);
            exitCanvas.gameObject.SetActive(true);
        }

        Canvas CreateExitCanvas()
        {
            GameObject canvasObject = new(
                "VR Drone Exit UI",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(TrackedDeviceGraphicRaycaster));
            canvasObject.transform.SetParent(environment.transform, false);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(500f, 160f);
            canvasRect.localScale = Vector3.one * 0.002f;

            Image panel = canvasObject.AddComponent<Image>();
            panel.color = new Color(0.02f, 0.025f, 0.04f, 0.94f);

            GameObject buttonObject = new(
                "Exit Button",
                typeof(RectTransform),
                typeof(Image),
                typeof(Button));
            buttonObject.transform.SetParent(canvasObject.transform, false);
            RectTransform buttonRect =
                buttonObject.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.2f, 0.2f);
            buttonRect.anchorMax = new Vector2(0.8f, 0.8f);
            buttonRect.offsetMin = Vector2.zero;
            buttonRect.offsetMax = Vector2.zero;

            Image buttonImage = buttonObject.GetComponent<Image>();
            buttonImage.color = new Color(0.8f, 0.05f, 0.06f, 1f);
            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = buttonImage;
            button.onClick.AddListener(ExitVr);

            GameObject labelObject = new(
                "Label",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(buttonObject.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            TextMeshProUGUI label =
                labelObject.GetComponent<TextMeshProUGUI>();
            label.font = TMP_Settings.defaultFontAsset;
            label.text = "나가기";
            label.fontSize = 54f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.raycastTarget = false;
            return canvas;
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
