using F1XR.RaceFlags;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace F1XR.RaceFlags.Editor
{
    public static class RaceFlagPrefabBuilder
    {
        private const string RootFolder = "Assets/F1_XR_Visualizer/RaceFlags";
        private const string ScriptsFolder = RootFolder + "/Scripts";
        private const string ShadersFolder = RootFolder + "/Shaders";
        private const string MaterialsFolder = RootFolder + "/Materials";
        private const string PrefabsFolder = RootFolder + "/Prefabs";
        private const string EditorFolder = RootFolder + "/Editor";

        private const string ShaderPath = ShadersFolder + "/RaceFlagLightweightURP.shader";
        private const string FlagMaterialPath = MaterialsFolder + "/RaceFlagLightweightURP.mat";
        private const string PoleMaterialPath = MaterialsFolder + "/RaceFlagPole.mat";
        private const string PoleMeshPath = PrefabsFolder + "/RaceFlagPoleMesh.asset";
        private const string PrefabPath = PrefabsFolder + "/RaceFlagAlert.prefab";
        private const string TestObjectName = "RaceFlagAlert_TEST";

        [MenuItem("Tools/F1 XR/Race Flags/Create or Update Prefab")]
        public static void CreateOrUpdatePrefab()
        {
            EnsureFolders();

            Material flagMaterial = LoadOrCreateFlagMaterial();
            Material poleMaterial = LoadOrCreatePoleMaterial();
            Mesh poleMesh = LoadOrCreatePoleMesh();

            bool prefabExists = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
            GameObject root = prefabExists
                ? PrefabUtility.LoadPrefabContents(PrefabPath)
                : new GameObject("RaceFlagAlert");

            root.name = "RaceFlagAlert";
            ConfigurePrefabRoot(root, flagMaterial, poleMaterial, poleMesh);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);

            if (prefabExists)
                PrefabUtility.UnloadPrefabContents(root);
            else
                Object.DestroyImmediate(root);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
        }

        [MenuItem("Tools/F1 XR/Race Flags/Place Prefab in Active Test Scene")]
        public static void PlacePrefabInActiveTestScene()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid())
            {
                EditorUtility.DisplayDialog(
                    "Race Flag Placement Aborted",
                    "The active scene is not valid. No scene was opened or modified.",
                    "OK");
                return;
            }

            if (string.IsNullOrEmpty(activeScene.path))
            {
                EditorUtility.DisplayDialog(
                    "Save Test Scene First",
                    "The active scene has no saved asset path. Save your test scene first, then run this command again.",
                    "OK");
                return;
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                EditorUtility.DisplayDialog(
                    "Race Flag Prefab Missing",
                    "Create the prefab first with Tools > F1 XR > Race Flags > Create or Update Prefab.",
                    "OK");
                return;
            }

            GameObject existing = FindInSceneByName(activeScene, TestObjectName);
            bool createDuplicate = false;
            if (existing != null)
            {
                Selection.activeGameObject = existing;
                createDuplicate = EditorUtility.DisplayDialog(
                    "Race Flag Test Object Exists",
                    "An object named RaceFlagAlert_TEST already exists in the active scene and has been selected.\n\nCreate another instance anyway?",
                    "Create Another",
                    "Cancel");

                if (!createDuplicate)
                    return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "Place Race Flag Prefab",
                "Only the currently active scene will be changed.\n\n" +
                $"Active scene: {activeScene.name}\n" +
                $"Scene path: {activeScene.path}\n" +
                $"Prefab path: {PrefabPath}\n\n" +
                "The scene will be marked dirty but will not be saved automatically.",
                "Place Prefab",
                "Cancel");

            if (!confirmed)
                return;

            GameObject instance = PrefabUtility.InstantiatePrefab(prefab, activeScene) as GameObject;
            if (instance == null)
            {
                EditorUtility.DisplayDialog(
                    "Race Flag Placement Failed",
                    "Prefab instantiation failed. No other scene was opened or modified.",
                    "OK");
                return;
            }

            Undo.RegisterCreatedObjectUndo(instance, "Place Race Flag Alert Test Prefab");
            instance.name = createDuplicate
                ? GetUniqueRootObjectName(activeScene, TestObjectName)
                : TestObjectName;
            instance.transform.position = GetVisibleScenePosition();
            instance.transform.rotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            Selection.activeGameObject = instance;
            EditorSceneManager.MarkSceneDirty(activeScene);
        }

        [MenuItem("Tools/F1 XR/Race Flags/Show Selected as Yellow")]
        public static void ShowSelectedAsYellow()
        {
            ShowSelected(RaceFlagType.Yellow);
        }

        [MenuItem("Tools/F1 XR/Race Flags/Show Selected as Red")]
        public static void ShowSelectedAsRed()
        {
            ShowSelected(RaceFlagType.Red);
        }

        [MenuItem("Tools/F1 XR/Race Flags/Show Selected as Checkered")]
        public static void ShowSelectedAsCheckered()
        {
            ShowSelected(RaceFlagType.Checkered);
        }

        private static void ShowSelected(RaceFlagType type)
        {
            RaceFlagAlert alert = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponentInParent<RaceFlagAlert>()
                : null;

            if (alert == null || EditorUtility.IsPersistent(alert))
            {
                EditorUtility.DisplayDialog(
                    "Race Flag Test",
                    "Select a scene object with a RaceFlagAlert component.",
                    "OK");
                return;
            }

            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog(
                    "Enter Play Mode",
                    "RaceFlagAlert.Show runs in Play Mode. Enter Play Mode, select the scene instance, then run this command again.",
                    "OK");
                return;
            }

            alert.Show(type);
        }

        private static void ConfigurePrefabRoot(GameObject root, Material flagMaterial, Material poleMaterial, Mesh poleMesh)
        {
            Transform motionPivot = FindOrCreateChild(root.transform, "MotionPivot");
            Transform pole = FindOrCreateChild(motionPivot, "Pole");
            Transform flagMesh = FindOrCreateChild(motionPivot, "FlagMesh");

            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            motionPivot.localPosition = Vector3.zero;
            motionPivot.localRotation = Quaternion.identity;
            motionPivot.localScale = Vector3.one;

            pole.localPosition = Vector3.zero;
            pole.localRotation = Quaternion.identity;
            pole.localScale = Vector3.one;

            flagMesh.localPosition = new Vector3(0.011f, 0.29f, 0.0f);
            flagMesh.localRotation = Quaternion.identity;
            flagMesh.localScale = Vector3.one;

            MeshFilter poleFilter = GetOrAdd<MeshFilter>(pole.gameObject);
            MeshRenderer poleRenderer = GetOrAdd<MeshRenderer>(pole.gameObject);
            poleFilter.sharedMesh = poleMesh;
            ConfigureRenderer(poleRenderer, poleMaterial, false);
            RemoveColliders(pole.gameObject);

            MeshFilter flagFilter = GetOrAdd<MeshFilter>(flagMesh.gameObject);
            MeshRenderer flagRenderer = GetOrAdd<MeshRenderer>(flagMesh.gameObject);
            ProceduralRaceFlagMesh proceduralMesh = GetOrAdd<ProceduralRaceFlagMesh>(flagMesh.gameObject);
            proceduralMesh.RebuildMesh();
            ConfigureRenderer(flagRenderer, flagMaterial, false);
            flagRenderer.allowOcclusionWhenDynamic = false;

            RaceFlagAlert alert = GetOrAdd<RaceFlagAlert>(root);
            SerializedObject serializedAlert = new SerializedObject(alert);
            serializedAlert.FindProperty("motionPivot").objectReferenceValue = motionPivot;
            serializedAlert.FindProperty("flagRenderer").objectReferenceValue = flagRenderer;
            serializedAlert.FindProperty("initialType").enumValueIndex = (int)RaceFlagType.Yellow;
            serializedAlert.FindProperty("playOnEnable").boolValue = false;
            serializedAlert.FindProperty("visibleDuration").floatValue = 5.0f;
            serializedAlert.ApplyModifiedPropertiesWithoutUndo();

            flagFilter.sharedMesh = flagMesh.GetComponent<MeshFilter>().sharedMesh;
        }

        private static void ConfigureRenderer(Renderer renderer, Material material, bool allowDynamicOcclusion)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic = allowDynamicOcclusion;
        }

        private static Material LoadOrCreateFlagMaterial()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null)
                shader = Shader.Find("F1XR/RaceFlagLightweightURP");

            Material material = AssetDatabase.LoadAssetAtPath<Material>(FlagMaterialPath);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "RaceFlagLightweightURP"
                };
                AssetDatabase.CreateAsset(material, FlagMaterialPath);
            }
            else if (shader != null && material.shader != shader)
            {
                material.shader = shader;
            }

            material.SetFloat("_FlagMode", 0.0f);
            material.SetColor("_FlagColor", new Color(1.0f, 0.75f, 0.03f, 1.0f));
            material.SetFloat("_WaveAmplitude", 0.04f);
            material.SetFloat("_WaveFrequency", 10.0f);
            material.SetFloat("_WaveSpeed", 6.0f);
            material.SetFloat("_SecondaryWave", 0.35f);
            material.SetFloat("_MotionPhase", 0.0f);
            EditorUtility.SetDirty(material);

            return material;
        }

        private static Material LoadOrCreatePoleMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(PoleMaterialPath);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null)
                    shader = Shader.Find("Unlit/Color");

                material = new Material(shader)
                {
                    name = "RaceFlagPole"
                };
                AssetDatabase.CreateAsset(material, PoleMaterialPath);
            }

            Color poleColor = new Color(0.045f, 0.045f, 0.05f, 1.0f);
            material.color = poleColor;
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", poleColor);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", poleColor);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Mesh LoadOrCreatePoleMesh()
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(PoleMeshPath);
            if (mesh == null)
            {
                mesh = new Mesh
                {
                    name = "RaceFlagPoleMesh"
                };
                AssetDatabase.CreateAsset(mesh, PoleMeshPath);
            }

            BuildPoleMesh(mesh, 0.01f, 0.62f, 8);
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static void BuildPoleMesh(Mesh mesh, float radius, float length, int segments)
        {
            int ringVertexCount = segments * 2;
            int capVertexStart = ringVertexCount;
            int vertexCount = ringVertexCount + 2;
            int sideIndexCount = segments * 6;
            int capIndexCount = segments * 6;

            Vector3[] vertices = new Vector3[vertexCount];
            Vector3[] normals = new Vector3[vertexCount];
            Vector2[] uvs = new Vector2[vertexCount];
            int[] indices = new int[sideIndexCount + capIndexCount];

            for (int i = 0; i < segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2.0f;
                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;
                Vector3 normal = new Vector3(x, 0.0f, z).normalized;

                vertices[i] = new Vector3(x, 0.0f, z);
                vertices[i + segments] = new Vector3(x, length, z);
                normals[i] = normal;
                normals[i + segments] = normal;
                uvs[i] = new Vector2(i / (float)segments, 0.0f);
                uvs[i + segments] = new Vector2(i / (float)segments, 1.0f);
            }

            vertices[capVertexStart] = Vector3.zero;
            vertices[capVertexStart + 1] = new Vector3(0.0f, length, 0.0f);
            normals[capVertexStart] = Vector3.down;
            normals[capVertexStart + 1] = Vector3.up;
            uvs[capVertexStart] = new Vector2(0.5f, 0.5f);
            uvs[capVertexStart + 1] = new Vector2(0.5f, 0.5f);

            int index = 0;
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                int bottom = i;
                int top = i + segments;
                int nextBottom = next;
                int nextTop = next + segments;

                indices[index++] = bottom;
                indices[index++] = top;
                indices[index++] = nextBottom;
                indices[index++] = nextBottom;
                indices[index++] = top;
                indices[index++] = nextTop;

                indices[index++] = capVertexStart;
                indices[index++] = nextBottom;
                indices[index++] = bottom;

                indices[index++] = capVertexStart + 1;
                indices[index++] = top;
                indices[index++] = nextTop;
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(indices, 0, true);
            mesh.bounds = new Bounds(new Vector3(0.0f, length * 0.5f, 0.0f), new Vector3(radius * 2.0f, length, radius * 2.0f));
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets/F1_XR_Visualizer", "RaceFlags");
            EnsureFolder(RootFolder, "Scripts");
            EnsureFolder(RootFolder, "Shaders");
            EnsureFolder(RootFolder, "Materials");
            EnsureFolder(RootFolder, "Prefabs");
            EnsureFolder(RootFolder, "Editor");
        }

        private static void EnsureFolder(string parent, string name)
        {
            string path = parent + "/" + name;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, name);
        }

        private static Transform FindOrCreateChild(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null)
                return child;

            GameObject childObject = new GameObject(name);
            childObject.transform.SetParent(parent, false);
            return childObject.transform;
        }

        private static T GetOrAdd<T>(GameObject gameObject) where T : Component
        {
            T component = gameObject.GetComponent<T>();
            return component != null ? component : gameObject.AddComponent<T>();
        }

        private static void RemoveColliders(GameObject gameObject)
        {
            Collider[] colliders = gameObject.GetComponents<Collider>();
            for (int i = 0; i < colliders.Length; i++)
                Object.DestroyImmediate(colliders[i]);
        }

        private static GameObject FindInSceneByName(Scene scene, string objectName)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                Transform found = FindChildRecursive(roots[i].transform, objectName);
                if (found != null)
                    return found.gameObject;
            }

            return null;
        }

        private static string GetUniqueRootObjectName(Scene scene, string baseName)
        {
            if (FindInSceneByName(scene, baseName) == null)
                return baseName;

            int index = 1;
            string candidate;
            do
            {
                candidate = $"{baseName}_{index}";
                index++;
            }
            while (FindInSceneByName(scene, candidate) != null);

            return candidate;
        }

        private static Transform FindChildRecursive(Transform root, string objectName)
        {
            if (root.name == objectName)
                return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindChildRecursive(root.GetChild(i), objectName);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static Vector3 GetVisibleScenePosition()
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
                return Vector3.zero;

            return sceneView.pivot + Vector3.up * 0.1f;
        }
    }
}
