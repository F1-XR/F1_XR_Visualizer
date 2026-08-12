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
    public sealed class QuestScenePermissionRequester : MonoBehaviour
    {
        const string ScenePermission = "com.oculus.permission.USE_SCENE";

        [SerializeField] ARPlaneManager planeManager;
        [SerializeField] ARRaycastManager raycastManager;
        [SerializeField] MetaSceneRoomSource metaSceneSource;
        [SerializeField] bool disableManagersUntilPermissionGranted = true;

        bool scenePermissionGranted;
        bool metaSceneSubscribed;

        void Reset()
        {
            planeManager = GetComponentInParent<ARPlaneManager>();
            raycastManager = GetComponentInParent<ARRaycastManager>();
        }

        void Awake()
        {
            scenePermissionGranted = !disableManagersUntilPermissionGranted;
            ResolveReferences();
            SubscribeMetaScene();
            ApplySceneManagerState();
        }

        void OnEnable()
        {
            ResolveReferences();
            SubscribeMetaScene();
        }

        void OnDisable()
        {
            UnsubscribeMetaScene();
        }

        void Start()
        {
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

            if (metaSceneSource == null)
            {
                metaSceneSource = FindAnyObjectByType<MetaSceneRoomSource>(
                    FindObjectsInactive.Include);
            }
        }

        void SubscribeMetaScene()
        {
            if (metaSceneSubscribed || metaSceneSource == null)
                return;

            metaSceneSource.StatusChanged += OnMetaSceneStatusChanged;
            metaSceneSubscribed = true;
        }

        void UnsubscribeMetaScene()
        {
            if (!metaSceneSubscribed || metaSceneSource == null)
                return;

            metaSceneSource.StatusChanged -= OnMetaSceneStatusChanged;
            metaSceneSubscribed = false;
        }

        void OnMetaSceneStatusChanged(MetaSceneRoomStatus status)
        {
            ApplySceneManagerState();
        }

        void ApplySceneManagerState()
        {
            bool metaSceneQueryActive = metaSceneSource != null &&
                metaSceneSource.isActiveAndEnabled &&
                IsMetaSceneQueryStatus(metaSceneSource.Status);
            SetSceneManagersEnabled(
                scenePermissionGranted && !metaSceneQueryActive);
        }

        static bool IsMetaSceneQueryStatus(MetaSceneRoomStatus status)
        {
            return status == MetaSceneRoomStatus.Idle ||
                status == MetaSceneRoomStatus.WaitingForPermission ||
                status == MetaSceneRoomStatus.Loading ||
                status == MetaSceneRoomStatus.OpeningSpaceSetup;
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
