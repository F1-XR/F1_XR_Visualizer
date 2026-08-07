using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace F1XR.Interaction.Input
{
    /// <summary>
    /// Reads a controller button by hand. Legacy <see cref="InputDevices"/> first (real hardware),
    /// falling back to the new Input System so the XR Interaction Simulator works in-editor
    /// (it drives no XRInputSubsystem, so the legacy device list is empty there).
    /// </summary>
    public static class XRControllerButton
    {
        static readonly List<InputDevice> Devices = new List<InputDevice>();

        public static bool IsPressed(MorphHoldButton button, bool rightHand)
        {
            var handedness = rightHand ? InputDeviceCharacteristics.Right : InputDeviceCharacteristics.Left;
            Devices.Clear();
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller | handedness, Devices);

            InputFeatureUsage<bool> usage;
            switch (button)
            {
                case MorphHoldButton.Grip: usage = CommonUsages.gripButton; break;
                case MorphHoldButton.PrimaryButton: usage = CommonUsages.primaryButton; break;
                case MorphHoldButton.SecondaryButton: usage = CommonUsages.secondaryButton; break;
                default: usage = CommonUsages.triggerButton; break;
            }

            foreach (var device in Devices)
            {
                if (device.isValid && device.TryGetFeatureValue(usage, out var value) && value)
                    return true;
            }

            return IsPressedNewInput(button, rightHand);
        }

        static bool IsPressedNewInput(MorphHoldButton button, bool rightHand)
        {
            string controlName;
            switch (button)
            {
                case MorphHoldButton.Grip: controlName = "gripButton"; break;
                case MorphHoldButton.PrimaryButton: controlName = "primaryButton"; break;
                case MorphHoldButton.SecondaryButton: controlName = "secondaryButton"; break;
                default: controlName = "triggerButton"; break;
            }

            var wantUsage = rightHand
                ? UnityEngine.InputSystem.CommonUsages.RightHand
                : UnityEngine.InputSystem.CommonUsages.LeftHand;

            foreach (var device in UnityEngine.InputSystem.InputSystem.devices)
            {
                if (!(device is UnityEngine.InputSystem.XR.XRController controller))
                    continue;

                bool handMatches = false;
                foreach (var u in controller.usages)
                {
                    if (u == wantUsage) { handMatches = true; break; }
                }
                if (!handMatches)
                    continue;

                foreach (var child in controller.children)
                {
                    if (child.name == controlName &&
                        child is UnityEngine.InputSystem.Controls.ButtonControl pressed &&
                        pressed.isPressed)
                        return true;
                }
            }

            return false;
        }
    }
}
