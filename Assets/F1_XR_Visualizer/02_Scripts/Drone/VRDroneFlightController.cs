using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace F1XR.Drone
{
    [DisallowMultipleComponent]
    public sealed class VRDroneFlightController : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] float moveSpeed = 95f;
        [SerializeField, Min(0.1f)] float verticalSpeed = 20f;
        [SerializeField, Min(1f)] float yawSpeed = 75f;
        [SerializeField, Min(0.1f)] float horizontalAcceleration = 72f;
        [SerializeField, Min(0.1f)] float horizontalDeceleration = 120f;
        [SerializeField, Min(0.1f)] float verticalAcceleration = 32f;
        [SerializeField, Min(0.1f)] float verticalDeceleration = 60f;
        [SerializeField, Min(1f)] float yawAcceleration = 180f;
        [SerializeField, Min(1f)] float yawDeceleration = 270f;
        [SerializeField, Range(0f, 0.9f)] float deadzone = 0.15f;

        readonly List<InputDevice> inputDevices = new();

        VRDroneCoordinator coordinator;
        Vector3 horizontalVelocity;
        float verticalVelocity;
        float yawVelocity;
        public void Configure(VRDroneCoordinator value)
        {
            coordinator = value;
        }

        public void ResetFlight()
        {
            horizontalVelocity = Vector3.zero;
            verticalVelocity = 0f;
            yawVelocity = 0f;
        }

        void Update()
        {
            if (coordinator == null || !coordinator.IsVrActive)
                return;

            Vector2 leftStick = ReadThumbstick(XRNode.LeftHand);
            Vector2 rightStick = ReadThumbstick(XRNode.RightHand);
            float throttle = ReadTrigger(XRNode.RightHand);

            Transform cameraTransform = coordinator.VrCameraTransform;
            if (cameraTransform == null)
                return;

            Vector3 forward = Vector3.ProjectOnPlane(
                cameraTransform.forward,
                Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;
            forward.Normalize();

            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 targetHorizontalVelocity =
                (forward * leftStick.y + right * leftStick.x) *
                (moveSpeed * throttle);
            float targetVerticalVelocity = rightStick.y * verticalSpeed * throttle;
            float targetYawVelocity = rightStick.x * yawSpeed;

            horizontalVelocity = MoveVelocity(
                horizontalVelocity,
                targetHorizontalVelocity,
                horizontalAcceleration,
                horizontalDeceleration);
            verticalVelocity = MoveVelocity(
                verticalVelocity,
                targetVerticalVelocity,
                verticalAcceleration,
                verticalDeceleration);
            yawVelocity = MoveVelocity(
                yawVelocity,
                targetYawVelocity,
                yawAcceleration,
                yawDeceleration);

            Vector3 movement = horizontalVelocity * Time.deltaTime;
            movement.y = verticalVelocity * Time.deltaTime;
            coordinator.ApplyDroneMotion(movement, yawVelocity * Time.deltaTime);
        }

        Vector3 MoveVelocity(
            Vector3 current,
            Vector3 target,
            float acceleration,
            float deceleration)
        {
            float rate = target.sqrMagnitude > 0.0001f
                ? acceleration
                : deceleration;
            return Vector3.MoveTowards(
                current,
                target,
                rate * Time.deltaTime);
        }

        float MoveVelocity(
            float current,
            float target,
            float acceleration,
            float deceleration)
        {
            float rate = Mathf.Abs(target) > Mathf.Epsilon
                ? acceleration
                : deceleration;
            return Mathf.MoveTowards(
                current,
                target,
                rate * Time.deltaTime);
        }

        Vector2 ReadThumbstick(XRNode node)
        {
            inputDevices.Clear();
            InputDevices.GetDevicesAtXRNode(node, inputDevices);

            foreach (InputDevice device in inputDevices)
            {
                if (device.TryGetFeatureValue(
                        CommonUsages.primary2DAxis,
                        out Vector2 value))
                {
                    return value.sqrMagnitude <= deadzone * deadzone
                        ? Vector2.zero
                        : value;
                }
            }

            return Vector2.zero;
        }

        float ReadTrigger(XRNode node)
        {
            inputDevices.Clear();
            InputDevices.GetDevicesAtXRNode(node, inputDevices);

            foreach (InputDevice device in inputDevices)
            {
                if (device.TryGetFeatureValue(
                        CommonUsages.trigger,
                        out float value))
                {
                    return value;
                }
            }

            return 0f;
        }
    }
}
