using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace F1XR.Interaction.Input
{
    // Before the first valid device pose arrives, TrackedPoseDriver leaves the controller
    // transform at its local origin under Camera Offset (floor height, no rotation). Left
    // enabled, the NearFarInteractor casts from that phantom position and can hover/select
    // whatever happens to be there (e.g. lifting a tire via TireHoverLift) before the player
    // is even in place. Disabling the whole interactor GameObject - not just its line
    // renderer - stops both the visual flash and the phantom casts until XRNode reports
    // isTracked.
    public sealed class ControllerTrackingVisibility : MonoBehaviour
    {
        [SerializeField] GameObject leftControllerVisual;
        [SerializeField] GameObject leftInteractor;
        [SerializeField] GameObject rightControllerVisual;
        [SerializeField] GameObject rightInteractor;

        static readonly List<InputDevice> Devices = new();

        void Update()
        {
            Apply(XRNode.LeftHand, leftControllerVisual, leftInteractor);
            Apply(XRNode.RightHand, rightControllerVisual, rightInteractor);
        }

        static void Apply(XRNode node, GameObject controllerVisual, GameObject interactor)
        {
            if (controllerVisual == null && interactor == null)
                return;

            bool tracked = IsTracked(node);

            if (controllerVisual != null && controllerVisual.activeSelf != tracked)
                controllerVisual.SetActive(tracked);

            if (interactor != null && interactor.activeSelf != tracked)
                interactor.SetActive(tracked);
        }

        static bool IsTracked(XRNode node)
        {
            Devices.Clear();
            InputDevices.GetDevicesAtXRNode(node, Devices);

            foreach (InputDevice device in Devices)
            {
                if (device.TryGetFeatureValue(CommonUsages.isTracked, out bool isTracked) && isTracked)
                    return true;
            }

            return false;
        }
    }
}
