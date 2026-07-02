using F1XR.AR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace F1XR.Editor
{
    public static class ARPlanePlacementSceneSetup
    {
        const string ScenePath = "Assets/F1_XR_Visualizer/01_Scenes/SampleScene.unity";
        const string PrefabPath = "Assets/F1_XR_Visualizer/03_Prefabs/ARPlanePlacementCube.prefab";
        const string MaterialPath = "Assets/F1_XR_Visualizer/08_Materials/ARPlanePlacementCube.mat";
        const string PlanePrefabPath = "Assets/F1_XR_Visualizer/03_Prefabs/ARPlaneVisualizer.prefab";
        const string PlaneMaterialPath = "Assets/F1_XR_Visualizer/08_Materials/ARPlaneVisualizer.mat";

        public static void ConfigureSampleScene()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
                scene = EditorSceneManager.OpenScene(ScenePath);

            var xrOrigin = GameObject.Find("XR Origin (VR)");
            if (xrOrigin == null)
            {
                Debug.LogError("Could not find 'XR Origin (VR)' in SampleScene.");
                return;
            }

            var controller = xrOrigin.GetComponent<ARPlanePlacementController>();
            if (controller == null)
                controller = xrOrigin.AddComponent<ARPlanePlacementController>();

            var redPointerType = System.Type.GetType("F1XR.AR.RedPointer, Assembly-CSharp");
            Component redPointer = null;
            if (redPointerType != null)
            {
                redPointer = xrOrigin.GetComponent(redPointerType);
                if (redPointer == null)
                    redPointer = xrOrigin.AddComponent(redPointerType);
            }

            var raycastManager = xrOrigin.GetComponent<ARRaycastManager>();
            if (raycastManager == null)
                raycastManager = xrOrigin.AddComponent<ARRaycastManager>();

            var planeManager = xrOrigin.GetComponent<ARPlaneManager>();
            if (planeManager == null)
                planeManager = xrOrigin.AddComponent<ARPlaneManager>();

            var anchorManager = xrOrigin.GetComponent<ARAnchorManager>();
            if (anchorManager == null)
                anchorManager = xrOrigin.AddComponent<ARAnchorManager>();
            anchorManager.anchorPrefab = null;

            planeManager.requestedDetectionMode =
                PlaneDetectionMode.Horizontal |
                PlaneDetectionMode.Vertical |
                PlaneDetectionMode.NotAxisAligned;
            planeManager.planePrefab = EnsurePlanePrefab();

            var permissionRequesterType = System.Type.GetType("F1XR.AR.QuestScenePermissionRequester, Assembly-CSharp");
            if (permissionRequesterType != null && xrOrigin.GetComponent(permissionRequesterType) == null)
                xrOrigin.AddComponent(permissionRequesterType);

            var serialized = new SerializedObject(controller);
            serialized.FindProperty("raycastManager").objectReferenceValue = raycastManager;
            serialized.FindProperty("planeManager").objectReferenceValue = planeManager;
            serialized.FindProperty("anchorManager").objectReferenceValue = anchorManager;

            var mainCamera = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
            if (mainCamera != null)
                serialized.FindProperty("rayOrigin").objectReferenceValue = mainCamera.transform;

            serialized.FindProperty("cubePrefab").objectReferenceValue = EnsureCubePrefab();
            serialized.FindProperty("allowReplaceExistingCube").boolValue = false;
            serialized.FindProperty("requireHorizontalUpPlane").boolValue = true;
            serialized.FindProperty("rejectFloorPlanes").boolValue = true;
            serialized.FindProperty("preferTableClassifiedPlanes").boolValue = true;
            serialized.FindProperty("minimumPlacementHeight").floatValue = 0.35f;
            serialized.FindProperty("verticalOffset").floatValue = 0.04f;
            serialized.FindProperty("defaultCubeSize").floatValue = 0.08f;
            serialized.FindProperty("useControllerTriggerPlacement").boolValue = true;
            serialized.FindProperty("useHandPinchPlacement").boolValue = true;
            serialized.FindProperty("inputArmDelay").floatValue = 0.5f;
            serialized.FindProperty("pinchPressThreshold").floatValue = 0.8f;
            serialized.FindProperty("pinchReleaseThreshold").floatValue = 0.55f;
            serialized.FindProperty("pinchDistancePressThreshold").floatValue = 0.025f;
            serialized.FindProperty("pinchDistanceReleaseThreshold").floatValue = 0.04f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            if (redPointer != null)
            {
                var pointerSerialized = new SerializedObject(redPointer);
                pointerSerialized.FindProperty("placementController").objectReferenceValue = controller;
                pointerSerialized.FindProperty("showPointer").boolValue = true;
                pointerSerialized.FindProperty("showControllerPointers").boolValue = true;
                pointerSerialized.FindProperty("showHandPointers").boolValue = true;
                pointerSerialized.FindProperty("pointerSize").floatValue = 0.025f;
                pointerSerialized.FindProperty("surfaceOffset").floatValue = 0.005f;
                pointerSerialized.FindProperty("pointerColor").colorValue = Color.red;
                pointerSerialized.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorUtility.SetDirty(xrOrigin);
            EditorUtility.SetDirty(controller);
            if (redPointer != null)
                EditorUtility.SetDirty(redPointer);
            EditorUtility.SetDirty(anchorManager);
            EditorUtility.SetDirty(raycastManager);
            EditorUtility.SetDirty(planeManager);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Debug.Log("AR plane placement is configured on XR Origin (VR).");
        }

        static GameObject EnsurePlanePrefab()
        {
            var existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlanePrefabPath);
            if (existingPrefab != null)
                return existingPrefab;

            var material = EnsurePlaneMaterial();
            var plane = new GameObject("ARPlaneVisualizer");
            plane.AddComponent<ARPlane>();
            plane.AddComponent<ARPlaneMeshVisualizer>();
            plane.AddComponent<MeshFilter>();

            var meshRenderer = plane.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.enabled = false;

            plane.AddComponent<MeshCollider>();

            var lineRenderer = plane.AddComponent<LineRenderer>();
            lineRenderer.sharedMaterial = material;
            lineRenderer.enabled = false;
            lineRenderer.loop = true;
            lineRenderer.useWorldSpace = false;
            lineRenderer.widthMultiplier = 0f;

            var prefab = PrefabUtility.SaveAsPrefabAsset(plane, PlanePrefabPath);
            Object.DestroyImmediate(plane);
            return prefab;
        }

        static Material EnsurePlaneMaterial()
        {
            var existingMaterial = AssetDatabase.LoadAssetAtPath<Material>(PlaneMaterialPath);
            if (existingMaterial != null)
            {
                ConfigurePlaneMaterial(existingMaterial);
                return existingMaterial;
            }

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Standard");

            var material = new Material(shader)
            {
                name = "ARPlaneVisualizer"
            };
            ConfigurePlaneMaterial(material);

            AssetDatabase.CreateAsset(material, PlaneMaterialPath);
            return material;
        }

        static void ConfigurePlaneMaterial(Material material)
        {
            material.color = new Color(0f, 0.9f, 0.8f, 0f);

            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_SrcBlend"))
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend"))
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite"))
                material.SetFloat("_ZWrite", 0f);

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        static GameObject EnsureCubePrefab()
        {
            var existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existingPrefab != null)
                return existingPrefab;

            var material = EnsureCubeMaterial();
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "ARPlanePlacementCube";
            cube.transform.localScale = Vector3.one * 0.08f;

            var renderer = cube.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.sharedMaterial = material;

            var prefab = PrefabUtility.SaveAsPrefabAsset(cube, PrefabPath);
            Object.DestroyImmediate(cube);
            return prefab;
        }

        static Material EnsureCubeMaterial()
        {
            var existingMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (existingMaterial != null)
                return existingMaterial;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");

            var material = new Material(shader)
            {
                name = "ARPlanePlacementCube"
            };
            material.color = new Color(0.1f, 0.55f, 1f, 1f);

            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }
    }
}
