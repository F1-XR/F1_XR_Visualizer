using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Unity.VisualScripting;
using F1XR.RestAPI.Replay.Room;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace F1XR.RestAPI.Replay.Track.Placement
{
    [RenamedFrom("F1XR.AR.QuestScenePermissionRequester")]
    [DefaultExecutionOrder(-1000)]
    public sealed class QuestScenePermissionRequester : MonoBehaviour
    {
        const string ScenePermission = "com.oculus.permission.USE_SCENE";

        [SerializeField] ARPlaneManager planeManager;
        [SerializeField] ARRaycastManager raycastManager;
        [SerializeField] ARAnchorManager anchorManager;
        [SerializeField] bool disableManagersUntilPermissionGranted = true;

        bool scenePermissionGranted;
        void Reset()
        {
            planeManager = GetComponentInParent<ARPlaneManager>();
            raycastManager = GetComponentInParent<ARRaycastManager>();
            anchorManager = GetComponentInParent<ARAnchorManager>();
        }

        void Awake()
        {
            scenePermissionGranted = !disableManagersUntilPermissionGranted;
            ResolveReferences();
            ApplySceneManagerState();
        }

        void OnEnable()
        {
            ResolveReferences();
        }

        void Start()
        {
            ResolveReferences();
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Permission.HasUserAuthorizedPermission(ScenePermission))
            {
                scenePermissionGranted = true;
                ApplySceneManagerState();
                Debug.Log("Quest scene permission already granted.");
                return;
            }

            if (disableManagersUntilPermissionGranted)
                SetSceneManagersEnabled(false);

            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += OnScenePermissionGranted;
            callbacks.PermissionDenied += OnScenePermissionDenied;
            callbacks.PermissionDeniedAndDontAskAgain += OnScenePermissionDenied;

            Debug.Log($"Requesting Quest scene permission: {ScenePermission}");
            Permission.RequestUserPermission(ScenePermission, callbacks);
#else
            scenePermissionGranted = true;
            ApplySceneManagerState();
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        void OnScenePermissionGranted(string permission)
        {
            Debug.Log($"Quest scene permission granted: {permission}");
            scenePermissionGranted = true;
            ApplySceneManagerState();
        }

        void OnScenePermissionDenied(string permission)
        {
            scenePermissionGranted = false;
            ApplySceneManagerState();
            Debug.LogWarning($"Quest scene permission denied: {permission}. Plane, mesh, and environment raycast data will not be available.");
        }
#endif

        void ResolveReferences()
        {
            if (planeManager == null)
                planeManager = GetComponentInParent<ARPlaneManager>();

            if (raycastManager == null)
                raycastManager = GetComponentInParent<ARRaycastManager>();

            if (anchorManager == null)
                anchorManager = GetComponentInParent<ARAnchorManager>();
        }

        void ApplySceneManagerState()
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            // Quest Link has one spatial source: Unity Meta OpenXR planes.
            // Environment raycasts have caused native shutdown failures,
            // while walls and TABLE AUTO only need the plane manager.
            if (planeManager != null)
                planeManager.enabled = true;
            if (raycastManager != null)
                raycastManager.enabled = false;
            if (anchorManager != null && anchorManager.enabled)
            {
                anchorManager.enabled = false;
                Debug.Log(
                    "[QuestScene] ARAnchorManager disabled for Editor shutdown isolation.",
                    this);
            }
#else
            SetSceneManagersEnabled(scenePermissionGranted);
#endif
        }

        void SetSceneManagersEnabled(bool enabled)
        {
            if (planeManager != null)
                planeManager.enabled = enabled;

            if (raycastManager != null)
                raycastManager.enabled = enabled;
        }
    }
}
