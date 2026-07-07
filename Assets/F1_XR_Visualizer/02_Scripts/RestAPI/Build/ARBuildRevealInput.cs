using System.Collections.Generic;
using UnityEngine.XR;

using XRInputDevice = UnityEngine.XR.InputDevice;
using XRInputDevices = UnityEngine.XR.InputDevices;
using XRCommonUsages = UnityEngine.XR.CommonUsages;
using XRInputDeviceCharacteristics = UnityEngine.XR.InputDeviceCharacteristics;

namespace F1XR.RestAPI.AR
{
    public sealed partial class ARBuildRevealPlacer
    {
        static readonly List<XRInputDevice> s_InputDevices = new();

        bool IsAnyPlacementInputHeld()
        {
            bool actionHeld = placeAction.action != null && placeAction.action.IsPressed();

            bool triggerHeld = useControllerTriggerFallback &&
                (IsControllerTriggerPressed(XRInputDeviceCharacteristics.Left) ||
                 IsControllerTriggerPressed(XRInputDeviceCharacteristics.Right));

            return actionHeld || triggerHeld;
        }

        void UpdateTriggerStateOnly()
        {
            if (!useControllerTriggerFallback)
                return;

            wasLeftTriggerPressed =
                IsControllerTriggerPressed(XRInputDeviceCharacteristics.Left);

            wasRightTriggerPressed =
                IsControllerTriggerPressed(XRInputDeviceCharacteristics.Right);
        }

        bool WasTriggerPressedThisFrame()
        {
            bool leftPressed =
                IsControllerTriggerPressed(XRInputDeviceCharacteristics.Left);

            bool rightPressed =
                IsControllerTriggerPressed(XRInputDeviceCharacteristics.Right);

            bool leftPressedThisFrame = leftPressed && !wasLeftTriggerPressed;
            bool rightPressedThisFrame = rightPressed && !wasRightTriggerPressed;

            wasLeftTriggerPressed = leftPressed;
            wasRightTriggerPressed = rightPressed;

            return leftPressedThisFrame || rightPressedThisFrame;
        }

        static bool IsControllerTriggerPressed(XRInputDeviceCharacteristics handedness)
        {
            s_InputDevices.Clear();

            XRInputDevices.GetDevicesWithCharacteristics(
                handedness | XRInputDeviceCharacteristics.Controller,
                s_InputDevices);

            foreach (XRInputDevice device in s_InputDevices)
            {
                if (!device.isValid)
                    continue;

                if (device.TryGetFeatureValue(XRCommonUsages.triggerButton, out bool pressed) && pressed)
                    return true;
            }

            return false;
        }
    }
}
