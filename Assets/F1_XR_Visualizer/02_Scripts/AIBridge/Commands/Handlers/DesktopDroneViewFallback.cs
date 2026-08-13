// AIBridge/Commands/Handlers/DesktopDroneViewFallback.cs
// Meta/XR 없이 AICommandTest에서 droneView 명령을 시연하기 위한 데스크톱 카메라 fallback.
#if AIBRIDGE_READY
using F1XR.RestAPI.Replay;
using F1XR.RestAPI.UI;
using UnityEngine;

namespace F1XR.AIBridge.Commands
{
    [DisallowMultipleComponent]
    public sealed class DesktopDroneViewFallback : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] float heightMultiplier = 1.35f;
        [SerializeField, Min(0.1f)] float minimumHeight = 4f;
        [SerializeField, Range(15f, 90f)] float pitch = 68f;

        Vector3 savedPosition;
        Quaternion savedRotation;
        bool savedCameraControllerEnabled;
        bool active;
        ReplayDesktopCamera desktopCamera;

        public void SetDroneView(bool on)
        {
            if (on)
                Enter();
            else
                Exit();
        }

        void Enter()
        {
            Camera camera = GetComponent<Camera>() ?? Camera.main;
            if (camera == null)
            {
                Debug.LogWarning("[AIBridge] Desktop drone view skipped: no camera.");
                return;
            }

            if (active)
                return;

            savedPosition = camera.transform.position;
            savedRotation = camera.transform.rotation;
            desktopCamera = camera.GetComponent<ReplayDesktopCamera>();
            savedCameraControllerEnabled =
                desktopCamera != null && desktopCamera.enabled;
            if (desktopCamera != null)
                desktopCamera.enabled = false;

            if (!TryGetReplayBounds(out Bounds bounds))
                bounds = new Bounds(Vector3.zero, new Vector3(6f, 0.5f, 6f));

            float radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
            float height = Mathf.Max(radius * heightMultiplier, minimumHeight);
            Vector3 center = bounds.center;
            Vector3 offset = new Vector3(0f, height, -height * 0.55f);

            camera.transform.position = center + offset;
            camera.transform.rotation =
                Quaternion.Euler(pitch, 0f, 0f);
            active = true;
            Debug.Log("[AIBridge] Desktop drone view enabled.");
        }

        void Exit()
        {
            Camera camera = GetComponent<Camera>() ?? Camera.main;
            if (!active || camera == null)
                return;

            camera.transform.SetPositionAndRotation(savedPosition, savedRotation);
            if (desktopCamera != null)
                desktopCamera.enabled = savedCameraControllerEnabled;
            active = false;
            Debug.Log("[AIBridge] Desktop drone view disabled.");
        }

        bool TryGetReplayBounds(out Bounds bounds)
        {
            bounds = new Bounds();
            bool hasBounds = false;

            ReplayCarView[] cars =
                FindObjectsByType<ReplayCarView>(FindObjectsSortMode.None);
            foreach (ReplayCarView car in cars)
                EncapsulateRenderers(car.GetComponentsInChildren<Renderer>(true),
                    ref bounds, ref hasBounds);

            if (hasBounds)
                return true;

            EncapsulateRenderers(
                FindObjectsByType<Renderer>(FindObjectsSortMode.None),
                ref bounds,
                ref hasBounds);
            return hasBounds;
        }

        static void EncapsulateRenderers(
            Renderer[] renderers,
            ref Bounds bounds,
            ref bool hasBounds)
        {
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !renderer.enabled ||
                    renderer.GetComponentInParent<Canvas>() != null)
                {
                    continue;
                }

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
        }
    }
}
#endif
