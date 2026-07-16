using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;

namespace F1XR.Interaction.Input
{
    public sealed class HandInputModeSwitcher : MonoBehaviour
    {
        [SerializeField] GameObject handVisualizerRoot;
        [SerializeField] GameObject leftHandRoot;
        [SerializeField] GameObject leftHandRay;
        [SerializeField] GameObject leftController;
        [SerializeField] GameObject rightHandRoot;
        [SerializeField] GameObject rightHandRay;
        [SerializeField] GameObject rightController;

        readonly List<XRHandSubsystem> handSubsystems = new List<XRHandSubsystem>();
        readonly List<InputDevice> controllerDevices = new List<InputDevice>();

        XRHandSubsystem handSubsystem;
        float nextResolveTime;

        void Awake()
        {
            ResolveSceneObjects();
            SetActive(leftHandRay, false);
            SetActive(rightHandRay, false);
            SetActive(handVisualizerRoot, true);
            ResolveHandSubsystem();
            ApplyModes();
        }

        void Update()
        {
            if (!HasSceneReferences() && Time.unscaledTime >= nextResolveTime)
            {
                ResolveSceneObjects();
                SetActive(handVisualizerRoot, true);
                nextResolveTime = Time.unscaledTime + 1f;
            }

            if (handSubsystem == null || !handSubsystem.running)
                ResolveHandSubsystem();

            ApplyModes();
        }

        bool HasSceneReferences()
        {
            return handVisualizerRoot != null &&
                leftHandRoot != null &&
                rightHandRoot != null &&
                leftHandRay != null &&
                rightHandRay != null &&
                leftController != null &&
                rightController != null;
        }

        void ResolveSceneObjects()
        {
            foreach (Transform item in GetComponentsInChildren<Transform>(true))
            {
                if (handVisualizerRoot == null && item.name == "HandVisualizer")
                    handVisualizerRoot = item.gameObject;
                else if (leftHandRoot == null && item.name == "Left Hand Tracking")
                    leftHandRoot = item.gameObject;
                else if (rightHandRoot == null && item.name == "Right Hand Tracking")
                    rightHandRoot = item.gameObject;
                else if (leftHandRay == null && item.name == "LeftHand")
                    leftHandRay = item.gameObject;
                else if (rightHandRay == null && item.name == "RightHand")
                    rightHandRay = item.gameObject;
                else if (leftController == null && item.name == "Left Controller")
                    leftController = item.gameObject;
                else if (rightController == null && item.name == "Right Controller")
                    rightController = item.gameObject;
            }
        }

        void ResolveHandSubsystem()
        {
            handSubsystems.Clear();
            SubsystemManager.GetSubsystems(handSubsystems);

            handSubsystem = null;
            foreach (XRHandSubsystem subsystem in handSubsystems)
            {
                if (!subsystem.running)
                    continue;

                handSubsystem = subsystem;
                break;
            }
        }

        void ApplyModes()
        {
            bool leftControllerTracked = IsControllerTracked(InputDeviceCharacteristics.Left);
            bool rightControllerTracked = IsControllerTracked(InputDeviceCharacteristics.Right);
            bool leftHandTracked = !leftControllerTracked && handSubsystem != null && handSubsystem.leftHand.isTracked;
            bool rightHandTracked = !rightControllerTracked && handSubsystem != null && handSubsystem.rightHand.isTracked;

            Apply(leftHandRoot, leftHandRay, leftController, leftHandTracked, leftControllerTracked);
            Apply(rightHandRoot, rightHandRay, rightController, rightHandTracked, rightControllerTracked);
        }

        bool IsControllerTracked(InputDeviceCharacteristics handedness)
        {
            controllerDevices.Clear();
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.Controller | handedness,
                controllerDevices);

            foreach (InputDevice device in controllerDevices)
            {
                if (!device.isValid || (device.characteristics & InputDeviceCharacteristics.HandTracking) != 0)
                    continue;

                if (device.TryGetFeatureValue(CommonUsages.isTracked, out bool isTracked) && isTracked)
                    return true;

                if (!device.TryGetFeatureValue(CommonUsages.trackingState, out InputTrackingState trackingState))
                    continue;

                const InputTrackingState positionAndRotation = InputTrackingState.Position | InputTrackingState.Rotation;
                if ((trackingState & positionAndRotation) == positionAndRotation)
                    return true;
            }

            return false;
        }

        static void Apply(
            GameObject handRoot,
            GameObject handRay,
            GameObject controller,
            bool handTracked,
            bool controllerTracked)
        {
            SetActive(handRoot, handTracked);
            SetActive(handRay, handTracked);
            SetActive(controller, controllerTracked);
        }

        static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
                target.SetActive(active);
        }
    }
}
