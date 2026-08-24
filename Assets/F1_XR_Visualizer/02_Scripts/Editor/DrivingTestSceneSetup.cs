#if UNITY_EDITOR
using F1XR.Driving;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;

namespace F1XR.Editor
{
    public static class DrivingTestSceneSetup
    {
        const string SceneName = "DrivingTest";
        const string XrOriginPrefabPath =
            "Assets/F1_XR_Visualizer/03_Prefabs/XR Origin/XR Origin (VR) Unified.prefab";
        const string EngineClipPath =
            "Assets/F1_XR_Visualizer/07_Sounds/Engine/Ferrari/Start/Ferrari_grid_start_full_raw.wav";
        const string XrInputActionsPath =
            "Assets/Samples/XR Interaction Toolkit/3.4.1/Starter Assets/XRI Default Input Actions.inputactions";

        [MenuItem("F1XR/Driving Test/Configure Prototype")]
        static void ConfigurePrototype()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.name != SceneName)
            {
                Debug.LogError($"[DrivingTest] Open {SceneName} before configuring the prototype.");
                return;
            }

            GameObject driverCam = FindSceneObject("DriverCam");
            if (driverCam == null)
            {
                Debug.LogError("[DrivingTest] DriverCam is required as the existing seat reference.");
                return;
            }

            Transform legacyCamera = driverCam.transform.Find("Main Camera");
            Transform ferrari = FindSceneObject("F1_Ferrari_Original")?.transform;
            if (ferrari == null)
            {
                Debug.LogError("[DrivingTest] F1_Ferrari_Original was not found under DriverCam.");
                return;
            }

            Vector3 seatLocalPosition = legacyCamera != null
                ? legacyCamera.localPosition
                : new Vector3(0f, 1.5f, 0.1f);
            Quaternion seatLocalRotation = legacyCamera != null
                ? legacyCamera.localRotation
                : Quaternion.identity;

            GameObject vehicle = FindOrCreateRoot("FerrariVehicle");
            vehicle.transform.SetPositionAndRotation(driverCam.transform.position, driverCam.transform.rotation);
            vehicle.transform.localScale = Vector3.one;

            Transform physics = FindOrCreateChild(vehicle.transform, "VehiclePhysics");
            physics.localPosition = Vector3.zero;
            physics.localRotation = Quaternion.identity;
            physics.localScale = Vector3.one;

            Transform visual = FindOrCreateChild(vehicle.transform, "VehicleVisual");
            ferrari.SetParent(visual, false);
            ferrari.localPosition = Vector3.zero;
            ferrari.localRotation = Quaternion.identity;

            ConfigureSurface();
            AlignVehicleToSurface(vehicle.transform, visual);

            Transform seat = FindOrCreateChild(vehicle.transform, "DriverSeatAnchor");
            seat.localPosition = seatLocalPosition;
            seat.localRotation = seatLocalRotation;
            seat.localScale = Vector3.one;

            Transform xrOrigin = ConfigureXrOrigin(seat);
            ConfigureSeatFollower(xrOrigin, seat);
            DisablePassthrough(xrOrigin);
            ConfigurePhysics(vehicle, visual);
            ConfigureVehicleInput(vehicle);
            ConfigureAudio(vehicle.transform);
            ConfigureRespawn(vehicle.transform);

            if (legacyCamera != null)
                legacyCamera.gameObject.SetActive(false);

            driverCam.SetActive(false);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[DrivingTest] VR vehicle prototype configured.");
        }

        static Transform ConfigureXrOrigin(Transform seat)
        {
            Transform existing = null;
            foreach (Transform child in seat)
            {
                if (child.name != "XR Origin" && child.name != "XR Origin (VR) Unified")
                    continue;

                if (existing == null)
                {
                    existing = child;
                    continue;
                }

                Undo.DestroyObjectImmediate(child.gameObject);
            }

            if (existing == null)
            {
                foreach (VehicleSeatFollower follower in Object.FindObjectsByType<VehicleSeatFollower>(
                             FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    existing = follower.transform;
                    break;
                }
            }

            if (existing == null)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(XrOriginPrefabPath);
                if (prefab == null)
                {
                    Debug.LogError("[DrivingTest] XR Origin prefab is missing.");
                    return null;
                }

                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.name = "XR Origin";
                existing = instance.transform;
            }

            if (existing.parent != null)
                Undo.SetTransformParent(existing, null, "Detach XR Origin From Vehicle");

            existing.SetPositionAndRotation(seat.position, seat.rotation);
            existing.localScale = Vector3.one;

            XROrigin xrOrigin = existing.GetComponent<XROrigin>();
            if (xrOrigin == null)
                return existing;

            xrOrigin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;
            xrOrigin.CameraYOffset = 0f;
            if (xrOrigin.CameraFloorOffsetObject != null)
                xrOrigin.CameraFloorOffsetObject.transform.localPosition = Vector3.zero;
            EditorUtility.SetDirty(xrOrigin);
            return existing;
        }

        static void ConfigureSeatFollower(Transform xrOrigin, Transform seat)
        {
            if (xrOrigin == null)
                return;

            VehicleSeatFollower follower = xrOrigin.GetComponent<VehicleSeatFollower>();
            if (follower == null)
                follower = Undo.AddComponent<VehicleSeatFollower>(xrOrigin.gameObject);

            follower.Configure(seat);
            EditorUtility.SetDirty(follower);
        }

        static void DisablePassthrough(Transform xrOrigin)
        {
            if (xrOrigin == null)
                return;

            foreach (Camera camera in xrOrigin.GetComponentsInChildren<Camera>(true))
            {
                camera.clearFlags = CameraClearFlags.Skybox;
                Color color = camera.backgroundColor;
                color.a = 1f;
                camera.backgroundColor = color;
                EditorUtility.SetDirty(camera);
            }

            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name == "Passthrough Layer")
                    root.SetActive(false);
            }
        }

        static void ConfigurePhysics(GameObject vehicle, Transform visual)
        {
            Rigidbody body = vehicle.GetComponent<Rigidbody>();
            if (body == null)
                body = Undo.AddComponent<Rigidbody>(vehicle);

            body.mass = 800f;
            body.linearDamping = 0.2f;
            body.angularDamping = 2f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.centerOfMass = new Vector3(0f, -0.35f, 0f);
            body.useGravity = true;
            body.isKinematic = false;

            BoxCollider collider = vehicle.GetComponent<BoxCollider>();
            if (collider == null)
                collider = Undo.AddComponent<BoxCollider>(vehicle);

            Bounds bounds = CalculateBounds(visual);
            collider.center = vehicle.transform.InverseTransformPoint(bounds.center);
            collider.size = bounds.size;

            if (vehicle.GetComponent<VRVehicleDriver>() == null)
                Undo.AddComponent<VRVehicleDriver>(vehicle);
        }

        static void ConfigureVehicleInput(GameObject vehicle)
        {
            InputActionAsset actions =
                AssetDatabase.LoadAssetAtPath<InputActionAsset>(XrInputActionsPath);
            if (actions == null)
            {
                Debug.LogError("[DrivingTest] XRI Default Input Actions asset is missing.");
                return;
            }

            vehicle.GetComponent<VRVehicleDriver>()?.ConfigureInputActions(actions);
        }

        static void ConfigureAudio(Transform vehicle)
        {
            Transform audioRoot = FindOrCreateChild(vehicle, "VehicleAudio");
            audioRoot.localPosition = new Vector3(0f, 0.25f, -1.2f);
            AudioSource source = audioRoot.GetComponent<AudioSource>();
            if (source == null)
                source = Undo.AddComponent<AudioSource>(audioRoot.gameObject);

            source.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(EngineClipPath);
            source.loop = true;
            source.spatialBlend = 1f;
            source.playOnAwake = false;
            source.maxDistance = 40f;
        }

        static void ConfigureSurface()
        {
            GameObject surface = GameObject.Find("TestDrivingSurface");
            if (surface == null)
            {
                surface = GameObject.CreatePrimitive(PrimitiveType.Cube);
                surface.name = "TestDrivingSurface";
            }

            surface.transform.SetPositionAndRotation(new Vector3(0f, -0.1f, 0f), Quaternion.identity);
            surface.transform.localScale = new Vector3(200f, 0.2f, 200f);
        }

        static void AlignVehicleToSurface(Transform vehicle, Transform visual)
        {
            const float surfaceTopY = 0f;
            const float clearance = 0.02f;

            Bounds bounds = CalculateBounds(visual);
            vehicle.position += Vector3.up * (surfaceTopY + clearance - bounds.min.y);
        }

        static void ConfigureRespawn(Transform vehicle)
        {
            GameObject anchor = FindOrCreateRoot("RespawnAnchor");
            anchor.transform.SetPositionAndRotation(vehicle.position, vehicle.rotation);
        }

        static Bounds CalculateBounds(Transform root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = new Bounds(root.position, Vector3.one);
            bool hasBounds = false;
            foreach (Renderer renderer in renderers)
            {
                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return bounds;
        }

        static GameObject FindOrCreateRoot(string name)
        {
            GameObject existing = GameObject.Find(name);
            return existing != null ? existing : new GameObject(name);
        }

        static GameObject FindSceneObject(string name)
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                Transform found = FindChildRecursive(root.transform, name);
                if (found != null)
                    return found.gameObject;
            }

            return null;
        }

        static Transform FindOrCreateChild(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
                return existing;

            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        static Transform FindChild(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name)
                    return child;
            }

            return null;
        }

        static Transform FindChildRecursive(Transform parent, string name)
        {
            if (parent.name == name)
                return parent;

            foreach (Transform child in parent)
            {
                Transform found = FindChildRecursive(child, name);
                if (found != null)
                    return found;
            }

            return null;
        }
    }
}
#endif
