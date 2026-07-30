using UnityEngine;
using UnityEngine.XR;

namespace F1XR.UI.WorldPanel
{
    public sealed class PanelInitialHeadsetPlacement : MonoBehaviour
    {
        [SerializeField] Vector3 viewOffset =
            new(0f, -0.2f, 1.1f);
        [SerializeField] bool requireTrackedHead = true;

        Transform viewer;
        bool placementPending;

        void Start()
        {
            placementPending = true;
        }

        void LateUpdate()
        {
            TryPlace();
        }

        void TryPlace()
        {
            if (!placementPending)
                return;

            if (viewer == null)
            {
                Camera mainCamera = Camera.main;
                if (mainCamera != null)
                    viewer = mainCamera.transform;
            }

            if (!IsViewerPoseUsable())
                return;

            Vector3 up = Vector3.up;
            Vector3 forward = Vector3.ProjectOnPlane(
                viewer.forward,
                up);
            if (forward.sqrMagnitude < 0.0001f)
                return;

            forward.Normalize();
            Vector3 right = Vector3.Cross(up, forward);
            Vector3 position =
                viewer.position +
                right * viewOffset.x +
                up * viewOffset.y +
                forward * viewOffset.z;
            Quaternion rotation =
                Quaternion.LookRotation(forward, up);

            transform.SetPositionAndRotation(
                position,
                rotation);
            if (TryGetComponent(out Rigidbody body))
            {
                body.position = position;
                body.rotation = rotation;
            }

            placementPending = false;
            enabled = false;
        }

        bool IsViewerPoseUsable()
        {
            if (viewer == null ||
                !IsFinite(viewer.position) ||
                !IsFinite(viewer.forward) ||
                viewer.forward.sqrMagnitude < 0.5f)
            {
                return false;
            }

            if (!requireTrackedHead)
                return true;

            InputDevice head =
                InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!head.isValid)
                return false;

            if (head.TryGetFeatureValue(
                    CommonUsages.trackingState,
                    out InputTrackingState trackingState))
            {
                const InputTrackingState required =
                    InputTrackingState.Position |
                    InputTrackingState.Rotation;
                return (trackingState & required) == required;
            }

            return head.TryGetFeatureValue(
                    CommonUsages.isTracked,
                    out bool isTracked) &&
                isTracked;
        }

        static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                IsFinite(value.y) &&
                IsFinite(value.z);
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                !float.IsInfinity(value);
        }
    }
}
