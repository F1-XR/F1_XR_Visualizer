using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Hands;

namespace F1XR.RestAPI.Replay.Track.Placement
{
    public sealed partial class ARPlanePlacementController
    {
        private static readonly List<UnityEngine.XR.InputDevice> s_InputDevices = new();

        private bool wasLeftTriggerPressed;
        private bool wasRightTriggerPressed;
        private bool placementInputsArmed;
        private float enableTime;
        private InputDeviceCharacteristics lastTriggerHand;
        private InputAction leftPointerPos;
        private InputAction leftPointerRot;
        private InputAction leftTrackingState;
        private InputAction rightPointerPos;
        private InputAction rightPointerRot;
        private InputAction rightTrackingState;

        bool IsAnyPlacementInputHeld()
        {
            return wasLeftTriggerPressed ||
                wasRightTriggerPressed ||
                wasLeftPinching ||
                wasRightPinching ||
                (placeAction.action != null && placeAction.action.IsPressed());
        }

        bool WasTriggerPressedThisFrame()
        {
            var leftPressed = IsControllerTriggerPressed(InputDeviceCharacteristics.Left);
            var rightPressed = IsControllerTriggerPressed(InputDeviceCharacteristics.Right);
            var leftPressedThisFrame = leftPressed && !wasLeftTriggerPressed;
            var rightPressedThisFrame = rightPressed && !wasRightTriggerPressed;

            if (rightPressedThisFrame)
                lastTriggerHand = InputDeviceCharacteristics.Right;
            else if (leftPressedThisFrame)
                lastTriggerHand = InputDeviceCharacteristics.Left;

            wasLeftTriggerPressed = leftPressed;
            wasRightTriggerPressed = rightPressed;

            return leftPressedThisFrame || rightPressedThisFrame;
        }

        static bool IsControllerTriggerPressed(InputDeviceCharacteristics handedness)
        {
            s_InputDevices.Clear();
            InputDevices.GetDevicesWithCharacteristics(
                handedness | InputDeviceCharacteristics.Controller,
                s_InputDevices);

            foreach (var device in s_InputDevices)
            {
                if (device.isValid &&
                    device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out var pressed) &&
                    pressed)
                {
                    return true;
                }
            }

            return false;
        }

        bool TryGetControllerRay(InputDeviceCharacteristics handedness, out Ray ray)
        {
            if (TryGetControllerPointerActionRay(handedness, out ray))
                return true;

            return TryGetControllerDeviceRay(handedness, out ray);
        }

        public bool CanUseControllers()
        {
            if (!useControllerTriggerPlacement)
                return false;

            if (inputSourcePriority == InputSourcePriority.HandOnly)
                return false;

            if (inputSourcePriority == InputSourcePriority.ControllerOnly)
                return true;

            if (inputSourcePriority == InputSourcePriority.HandFirst && IsAnyHandTracked())
                return false;

            return inputSourcePriority == InputSourcePriority.HandFirst ||
                IsAnyControllerTracked();
        }

        public bool CanUseHands()
        {
            if (!useHandPinchPlacement)
                return false;

            if (inputSourcePriority == InputSourcePriority.ControllerOnly)
                return false;

            if (inputSourcePriority == InputSourcePriority.HandOnly)
                return true;

            if (inputSourcePriority == InputSourcePriority.ControllerFirst && IsAnyControllerTracked())
                return false;

            return inputSourcePriority == InputSourcePriority.ControllerFirst ||
                IsAnyHandTracked();
        }

        public bool IsAnyControllerTracked()
        {
            return IsControllerTracked(InputDeviceCharacteristics.Left) ||
                IsControllerTracked(InputDeviceCharacteristics.Right);
        }

        public bool IsAnyControllerActive()
        {
            return IsControllerActive(InputDeviceCharacteristics.Left) ||
                IsControllerActive(InputDeviceCharacteristics.Right);
        }

        public bool IsAnyHandTracked()
        {
            if (handSubsystem == null)
                TrySubscribeHandSubsystem();

            return IsHandTracked(Handedness.Left) || IsHandTracked(Handedness.Right);
        }

        static bool IsControllerTracked(InputDeviceCharacteristics handedness)
        {
            s_InputDevices.Clear();
            InputDevices.GetDevicesWithCharacteristics(
                handedness | InputDeviceCharacteristics.Controller,
                s_InputDevices);

            foreach (var device in s_InputDevices)
            {
                if (!device.isValid)
                    continue;

                if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trackingState, out InputTrackingState trackingState))
                {
                    if ((trackingState & InputTrackingState.Position) != 0 ||
                        (trackingState & InputTrackingState.Rotation) != 0)
                    {
                        return true;
                    }
                }

                if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out var isTracked) && isTracked)
                    return true;
            }

            return false;
        }

        static bool IsControllerActive(InputDeviceCharacteristics handedness)
        {
            s_InputDevices.Clear();
            InputDevices.GetDevicesWithCharacteristics(
                handedness | InputDeviceCharacteristics.Controller,
                s_InputDevices);

            foreach (var device in s_InputDevices)
            {
                if (!device.isValid)
                    continue;

                if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.userPresence, out var userPresent) &&
                    !userPresent)
                {
                    continue;
                }

                if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out var trigger) && trigger)
                    return true;

                if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out var grip) && grip)
                    return true;

                if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out var primary) && primary)
                    return true;

                if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out var secondary) && secondary)
                    return true;

                if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxisTouch, out var axisTouch) && axisTouch)
                    return true;
            }

            return false;
        }

        static bool TryGetControllerDeviceRay(InputDeviceCharacteristics handedness, out Ray ray)
        {
            s_InputDevices.Clear();
            InputDevices.GetDevicesWithCharacteristics(
                handedness | InputDeviceCharacteristics.Controller,
                s_InputDevices);

            foreach (var device in s_InputDevices)
            {
                if (device.isValid &&
                    device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out var position) &&
                    device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out var rotation))
                {
                    ray = new Ray(position, rotation * Vector3.forward);
                    return true;
                }
            }

            ray = default;
            return false;
        }

        bool TryGetControllerPointerActionRay(InputDeviceCharacteristics handedness, out Ray ray)
        {
            var positionAction = (handedness & InputDeviceCharacteristics.Left) != 0
                ? leftPointerPos
                : rightPointerPos;
            var rotationAction = (handedness & InputDeviceCharacteristics.Left) != 0
                ? leftPointerRot
                : rightPointerRot;
            var trackingStateAction = (handedness & InputDeviceCharacteristics.Left) != 0
                ? leftTrackingState
                : rightTrackingState;

            if (positionAction == null ||
                rotationAction == null ||
                positionAction.controls.Count == 0 ||
                rotationAction.controls.Count == 0)
            {
                ray = default;
                return false;
            }

            if (trackingStateAction != null && trackingStateAction.controls.Count > 0)
            {
                var trackingState = (InputTrackingState)trackingStateAction.ReadValue<int>();
                var hasPositionAndRotation =
                    (trackingState & InputTrackingState.Position) != 0 &&
                    (trackingState & InputTrackingState.Rotation) != 0;
                if (!hasPositionAndRotation)
                {
                    ray = default;
                    return false;
                }
            }

            var position = positionAction.ReadValue<Vector3>();
            var rotation = rotationAction.ReadValue<Quaternion>();
            ray = new Ray(position, rotation * Vector3.forward);
            return true;
        }

        void CreateControllerPointerActions()
        {
            leftPointerPos ??= CreateValueAction(
                "Left Controller Pointer Position",
                "<XRController>{LeftHand}/pointerPosition",
                "Vector3");
            leftPointerRot ??= CreateValueAction(
                "Left Controller Pointer Rotation",
                "<XRController>{LeftHand}/pointerRotation",
                "Quaternion");
            leftTrackingState ??= CreateValueAction(
                "Left Controller Tracking State",
                "<XRController>{LeftHand}/trackingState",
                "Integer");
            rightPointerPos ??= CreateValueAction(
                "Right Controller Pointer Position",
                "<XRController>{RightHand}/pointerPosition",
                "Vector3");
            rightPointerRot ??= CreateValueAction(
                "Right Controller Pointer Rotation",
                "<XRController>{RightHand}/pointerRotation",
                "Quaternion");
            rightTrackingState ??= CreateValueAction(
                "Right Controller Tracking State",
                "<XRController>{RightHand}/trackingState",
                "Integer");
        }

        static InputAction CreateValueAction(string name, string binding, string expectedControlType)
        {
            return new InputAction(
                name,
                InputActionType.Value,
                binding,
                expectedControlType: expectedControlType);
        }

        void SetControllerPointerActionsEnabled(bool enabled)
        {
            SetActionEnabled(leftPointerPos, enabled);
            SetActionEnabled(leftPointerRot, enabled);
            SetActionEnabled(leftTrackingState, enabled);
            SetActionEnabled(rightPointerPos, enabled);
            SetActionEnabled(rightPointerRot, enabled);
            SetActionEnabled(rightTrackingState, enabled);
        }

        static void SetActionEnabled(InputAction action, bool enabled)
        {
            if (action == null)
                return;

            if (enabled)
                action.Enable();
            else
                action.Disable();
        }

        void DisposeControllerPointerActions()
        {
            leftPointerPos?.Dispose();
            leftPointerRot?.Dispose();
            leftTrackingState?.Dispose();
            rightPointerPos?.Dispose();
            rightPointerRot?.Dispose();
            rightTrackingState?.Dispose();
        }
    }
}
