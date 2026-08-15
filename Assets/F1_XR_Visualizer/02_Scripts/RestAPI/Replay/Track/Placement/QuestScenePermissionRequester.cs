using System.Collections;
using UnityEngine;
using UnityEngine.XR;
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
        [SerializeField] bool disableManagersUntilPermissionGranted = true;

        bool scenePermissionGranted;
        Coroutine planeRecoveryRoutine;

        const float PlaneSubsystemReadyTimeout = 10f;
        const float EmptyPlaneRetryDelay = 5f;

        void Reset()
        {
            planeManager = GetComponentInParent<ARPlaneManager>();
            raycastManager = GetComponentInParent<ARRaycastManager>();
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

        void OnDisable()
        {
            if (planeRecoveryRoutine != null)
            {
                StopCoroutine(planeRecoveryRoutine);
                planeRecoveryRoutine = null;
            }

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
#if UNITY_EDITOR || UNITY_STANDALONE
            planeRecoveryRoutine =
                StartCoroutine(RestartPlaneManagerOnceIfEmpty());
#endif
#endif
        }

#if UNITY_EDITOR || UNITY_STANDALONE
        IEnumerator RestartPlaneManagerOnceIfEmpty()
        {
            float readyUntil = Time.realtimeSinceStartup +
                PlaneSubsystemReadyTimeout;
            while (Time.realtimeSinceStartup < readyUntil &&
                   !IsPlaneDiscoveryReady())
            {
                yield return null;
            }

            if (!IsPlaneDiscoveryReady())
            {
                planeRecoveryRoutine = null;
                yield break;
            }

            float retryAt = Time.realtimeSinceStartup +
                EmptyPlaneRetryDelay;
            while (Time.realtimeSinceStartup < retryAt &&
                   planeManager.trackables.count == 0)
            {
                yield return null;
            }

            if (IsPlaneDiscoveryReady() &&
                planeManager.trackables.count == 0)
            {
                planeManager.enabled = false;
                planeManager.enabled = true;
                Debug.Log(
                    "[QuestScene] Restarted ARPlaneManager after an empty Link query.",
                    this);
            }

            planeRecoveryRoutine = null;
        }

        bool IsPlaneDiscoveryReady()
        {
            return planeManager != null &&
                planeManager.isActiveAndEnabled &&
                planeManager.subsystem != null &&
                planeManager.subsystem.running &&
                IsHeadsetTracked();
        }

        static bool IsHeadsetTracked()
        {
            InputDevice headset =
                InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!headset.isValid)
                return false;

            if (headset.TryGetFeatureValue(
                    CommonUsages.trackingState,
                    out InputTrackingState trackingState))
            {
                const InputTrackingState required =
                    InputTrackingState.Position |
                    InputTrackingState.Rotation;
                return (trackingState & required) == required;
            }

            return headset.TryGetFeatureValue(
                       CommonUsages.isTracked,
                       out bool isTracked) &&
                isTracked;
        }
#endif

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
