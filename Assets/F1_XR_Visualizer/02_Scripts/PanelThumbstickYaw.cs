using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace F1XR.AR
{
    public sealed class PanelThumbstickYaw : MonoBehaviour
    {
        [SerializeField] XRGrabInteractable grab;
        [SerializeField] Transform target;
        [SerializeField] float yawSpeed = 90f;
        [SerializeField] float thumbstickDeadzone = 0.15f;

        static readonly List<UnityEngine.XR.InputDevice> InputDevices = new();

        void Awake()
        {
            if (target == null)
                target = transform;

            if (grab == null)
                grab = GetComponent<XRGrabInteractable>();
        }

        void Update()
        {
            if (grab == null || !grab.isSelected)
                return;

            var axis = GetThumbstick();
            if (Mathf.Abs(axis.x) < thumbstickDeadzone)
                return;

            target.Rotate(Vector3.up, axis.x * yawSpeed * Time.deltaTime, Space.World);
        }

        static Vector2 GetThumbstick()
        {
            var right = GetThumbstick(XRNode.RightHand);
            if (right.sqrMagnitude > 0f)
                return right;

            return GetThumbstick(XRNode.LeftHand);
        }

        static Vector2 GetThumbstick(XRNode node)
        {
            InputDevices.Clear();
            UnityEngine.XR.InputDevices.GetDevicesAtXRNode(node, InputDevices);

            foreach (var device in InputDevices)
            {
                if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out var axis))
                    return axis;
            }

            return Vector2.zero;
        }
    }
}
