using F1XR.Champagne;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace F1XR.Champagne.Editor
{
    public static class ChampagnePrefabBuilder
    {
        const string RootFolder = "Assets/F1_XR_Visualizer/Champagne";
        const string PrefabPath = RootFolder + "/Prefabs/ChampagneBottle.prefab";
        const string BottleMaterialPath = RootFolder + "/Materials/ChampagneBottle_Green.mat";
        const string CorkMaterialPath = RootFolder + "/Materials/ChampagneCork.mat";
        const string FoilMaterialPath = RootFolder + "/Materials/ChampagneFoil_Gold.mat";
        const string LiquidMaterialPath = RootFolder + "/Materials/ChampagneLiquid_Particle.mat";
        const string FoamMaterialPath = RootFolder + "/Materials/ChampagneFoam_Particle.mat";

        [MenuItem("Tools/F1 XR/Champagne/Create Champagne Prefab")]
        public static void CreateChampagnePrefab()
        {
            EnsureFolders();

            var bottleMaterial = CreateMaterial(BottleMaterialPath, new Color(0.02f, 0.22f, 0.11f, 1f), false);
            var corkMaterial = CreateMaterial(CorkMaterialPath, new Color(0.62f, 0.44f, 0.25f, 1f), false);
            var foilMaterial = CreateMaterial(FoilMaterialPath, new Color(1f, 0.76f, 0.25f, 1f), false);
            var liquidMaterial = CreateMaterial(LiquidMaterialPath, new Color(1f, 0.92f, 0.58f, 0.55f), true);
            var foamMaterial = CreateMaterial(FoamMaterialPath, new Color(1f, 0.97f, 0.86f, 0.65f), true);

            var root = new GameObject("ChampagneBottle");
            var body = root.AddComponent<Rigidbody>();
            body.mass = 0.75f;
            body.linearDamping = 0.4f;
            body.angularDamping = 0.8f;
            body.useGravity = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            var grab = root.AddComponent<XRGrabInteractable>();
            grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
            grab.throwOnDetach = true;

            var mainCollider = root.AddComponent<CapsuleCollider>();
            mainCollider.direction = 1;
            mainCollider.center = new Vector3(0f, 0.12f, 0f);
            mainCollider.radius = 0.055f;
            mainCollider.height = 0.32f;

            var visualRoot = new GameObject("BottleVisual");
            visualRoot.transform.SetParent(root.transform, false);
            CreatePrimitive(PrimitiveType.Cylinder, "Body", visualRoot.transform, new Vector3(0f, 0.08f, 0f), new Vector3(0.11f, 0.24f, 0.11f), bottleMaterial);
            CreatePrimitive(PrimitiveType.Cylinder, "Neck", visualRoot.transform, new Vector3(0f, 0.27f, 0f), new Vector3(0.045f, 0.18f, 0.045f), bottleMaterial);
            CreatePrimitive(PrimitiveType.Cylinder, "Foil", visualRoot.transform, new Vector3(0f, 0.37f, 0f), new Vector3(0.05f, 0.08f, 0.05f), foilMaterial);

            var attach = new GameObject("GrabAttachTransform").transform;
            attach.SetParent(root.transform, false);
            attach.localPosition = new Vector3(0f, 0.13f, -0.03f);
            attach.localRotation = Quaternion.Euler(25f, 0f, 0f);
            grab.attachTransform = attach;

            var corkSocket = new GameObject("CorkSocket").transform;
            corkSocket.SetParent(root.transform, false);
            corkSocket.localPosition = new Vector3(0f, 0.43f, 0f);
            corkSocket.localRotation = Quaternion.identity;

            var cork = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            cork.name = "Cork";
            cork.transform.SetParent(corkSocket, false);
            cork.transform.localPosition = Vector3.zero;
            cork.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            cork.transform.localScale = new Vector3(0.028f, 0.045f, 0.028f);
            AssignMaterial(cork, corkMaterial);
            var corkBody = cork.AddComponent<Rigidbody>();
            corkBody.isKinematic = true;
            var corkController = cork.AddComponent<CorkController>();

            var sprayOrigin = new GameObject("SprayOrigin").transform;
            sprayOrigin.SetParent(root.transform, false);
            sprayOrigin.localPosition = new Vector3(0f, 0.46f, 0f);
            sprayOrigin.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            var liquid = CreateParticleSystem("LiquidJetParticle", sprayOrigin, liquidMaterial);
            var foam = CreateParticleSystem("FoamParticle", sprayOrigin, foamMaterial);
            var mist = CreateParticleSystem("MistParticle", sprayOrigin, liquidMaterial);

            var sprayAudio = new GameObject("SprayAudioSource").AddComponent<AudioSource>();
            sprayAudio.transform.SetParent(sprayOrigin, false);
            sprayAudio.spatialBlend = 1f;

            var popAudio = new GameObject("PopAudioSource").AddComponent<AudioSource>();
            popAudio.transform.SetParent(root.transform, false);
            popAudio.spatialBlend = 1f;

            var shakeDetector = root.AddComponent<BottleShakeDetector>();
            var sprayController = sprayOrigin.gameObject.AddComponent<ChampagneSprayController>();
            var bottleController = root.AddComponent<ChampagneBottleController>();

            SetObject(shakeDetector, "targetBody", body);
            SetObject(sprayController, "liquidJetParticle", liquid);
            SetObject(sprayController, "foamParticle", foam);
            SetObject(sprayController, "mistParticle", mist);
            SetObject(sprayController, "sprayAudioSource", sprayAudio);
            SetObject(corkController, "corkBody", corkBody);
            SetObject(bottleController, "grabInteractable", grab);
            SetObject(bottleController, "bottleBody", body);
            SetObject(bottleController, "sprayOrigin", sprayOrigin);
            SetObject(bottleController, "shakeDetector", shakeDetector);
            SetObject(bottleController, "sprayController", sprayController);
            SetObject(bottleController, "corkController", corkController);
            SetObject(bottleController, "popAudioSource", popAudio);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[ChampagneBuilder] Created prefab: {PrefabPath}");
        }

        [MenuItem("Tools/F1 XR/Champagne/Place Test Setup In Active Scene")]
        public static void PlaceTestSetupInActiveScene()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
            {
                EditorUtility.DisplayDialog("Save Scene First", "Open and save your personal test scene before placing the champagne setup.", "OK");
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
                CreateChampagnePrefab();

            CreateTableIfMissing();
            CreateFloorIfMissing();

            if (GameObject.Find("ChampagneBottle_Test") == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                var instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
                instance.name = "ChampagneBottle_Test";
                instance.transform.position = new Vector3(0f, 0.95f, 0.25f);
                instance.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[ChampagneBuilder] Placed test setup in active scene: {scene.path}");
        }

        static void CreateTableIfMissing()
        {
            if (GameObject.Find("ChampagneTempTable") != null)
                return;

            var table = GameObject.CreatePrimitive(PrimitiveType.Cube);
            table.name = "ChampagneTempTable";
            table.transform.position = new Vector3(0f, 0.72f, 0.45f);
            table.transform.localScale = new Vector3(0.9f, 0.06f, 0.55f);
        }

        static void CreateFloorIfMissing()
        {
            if (GameObject.Find("ChampagneTempFloor") != null)
                return;

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "ChampagneTempFloor";
            floor.transform.position = new Vector3(0f, -0.03f, 0f);
            floor.transform.localScale = new Vector3(3f, 0.04f, 3f);
        }

        static ParticleSystem CreateParticleSystem(string name, Transform parent, Material material)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            var particles = obj.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var renderer = obj.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            return particles;
        }

        static GameObject CreatePrimitive(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 localScale, Material material)
        {
            var obj = GameObject.CreatePrimitive(type);
            obj.name = name;
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = localPosition;
            obj.transform.localScale = localScale;
            AssignMaterial(obj, material);
            Object.DestroyImmediate(obj.GetComponent<Collider>());
            return obj;
        }

        static void AssignMaterial(GameObject obj, Material material)
        {
            var renderer = obj.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = material;
        }

        static Material CreateMaterial(string path, Color color, bool transparent)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find(transparent ? "Universal Render Pipeline/Particles/Unlit" : "Universal Render Pipeline/Lit");
                if (shader == null)
                    shader = Shader.Find("Standard");

                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", color);
            material.color = color;
            return material;
        }

        static void EnsureFolders()
        {
            CreateFolder("Assets/F1_XR_Visualizer", "Champagne");
            CreateFolder(RootFolder, "Prefabs");
            CreateFolder(RootFolder, "Materials");
            CreateFolder(RootFolder, "VFX");
            CreateFolder(RootFolder, "Audio");
            CreateFolder(RootFolder, "Models");
        }

        static void CreateFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + child))
                AssetDatabase.CreateFolder(parent, child);
        }

        static void SetObject(Object target, string propertyName, Object value)
        {
            var serializedObject = new SerializedObject(target);
            serializedObject.FindProperty(propertyName).objectReferenceValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
