using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.Samples.VisualizerSample;

namespace F1XR.Interaction.Input
{
    public sealed class HandInputModeSwitcher : MonoBehaviour
    {
        [SerializeField] bool showInputVisualMeshes;
        [SerializeField] bool controllerOnly;
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
        HandVisualizer handVisualizer;
        MeshRenderer[] leftControllerRenderers = System.Array.Empty<MeshRenderer>();
        MeshRenderer[] rightControllerRenderers = System.Array.Empty<MeshRenderer>();
        bool? appliedShowInputVisualMeshes;
        float nextResolveTime;

        void Awake()
        {
            ResolveSceneObjects();
            SetActive(leftHandRay, false);
            SetActive(rightHandRay, false);
            SetActive(handVisualizerRoot, !controllerOnly);
            ApplyInputVisualMeshes();

            if (controllerOnly)
            {
                ApplyControllerOnly();
                enabled = false;
                return;
            }

            ResolveHandSubsystem();
            ApplyModes();
        }

        void Update()
        {
            if (controllerOnly)
            {
                ApplyControllerOnly();
                enabled = false;
                return;
            }

            if (!HasSceneReferences() && Time.unscaledTime >= nextResolveTime)
            {
                ResolveSceneObjects();
                SetActive(handVisualizerRoot, true);
                ApplyInputVisualMeshes();
                nextResolveTime = Time.unscaledTime + 1f;
            }

            if (handSubsystem == null || !handSubsystem.running)
                ResolveHandSubsystem();

            ApplyInputVisualMeshes();
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

            handVisualizer = handVisualizerRoot != null
                ? handVisualizerRoot.GetComponent<HandVisualizer>()
                : null;
            leftControllerRenderers = GetControllerRenderers(leftController);
            rightControllerRenderers = GetControllerRenderers(rightController);
            appliedShowInputVisualMeshes = null;
        }

        static MeshRenderer[] GetControllerRenderers(GameObject controller)
        {
            if (controller == null)
                return System.Array.Empty<MeshRenderer>();

            var renderers = new List<MeshRenderer>();
            foreach (MeshRenderer renderer in controller.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.GetComponentInParent<OccludedInputVisualMarker>() == null)
                    renderers.Add(renderer);
            }

            return renderers.ToArray();
        }

        void ApplyInputVisualMeshes()
        {
            if (appliedShowInputVisualMeshes == showInputVisualMeshes)
                return;

            if (handVisualizer != null)
                handVisualizer.drawMeshes = showInputVisualMeshes;

            SetRenderersVisible(leftControllerRenderers, showInputVisualMeshes);
            SetRenderersVisible(rightControllerRenderers, showInputVisualMeshes);
            appliedShowInputVisualMeshes = showInputVisualMeshes;
        }

        static void SetRenderersVisible(MeshRenderer[] renderers, bool visible)
        {
            foreach (MeshRenderer item in renderers)
            {
                if (item != null)
                    item.enabled = visible;
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
            bool leftHandTracked = handSubsystem != null && handSubsystem.leftHand.isTracked;
            bool rightHandTracked = handSubsystem != null && handSubsystem.rightHand.isTracked;

            Apply(leftHandRoot, leftHandRay, leftController, !leftControllerTracked && leftHandTracked, leftControllerTracked);
            Apply(rightHandRoot, rightHandRay, rightController, !rightControllerTracked && rightHandTracked, rightControllerTracked);
        }

        void ApplyControllerOnly()
        {
            SetActive(handVisualizerRoot, false);
            Apply(leftHandRoot, leftHandRay, leftController, false, true);
            Apply(rightHandRoot, rightHandRay, rightController, false, true);
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
