using UnityEngine;
using UnityEngine.XR.Hands;

namespace F1XR
{
    // The XR rig has both a hand-tracking interactor and a controller interactor
    // active on each hand at the same time, which lets them fight over the same
    // grabbable objects. This enables only the one matching the input currently
    // in use, based on XRHandTrackingEvents.handIsTracked.
    public sealed class HandInputModeSwitcher : MonoBehaviour
    {
        [SerializeField] XRHandTrackingEvents leftHandTrackingEvents;
        [SerializeField] XRHandTrackingEvents rightHandTrackingEvents;
        [SerializeField] GameObject leftHandInteractor;
        [SerializeField] GameObject leftController;
        [SerializeField] GameObject rightHandInteractor;
        [SerializeField] GameObject rightController;

        void Update()
        {
            Apply(leftHandTrackingEvents.handIsTracked, leftHandInteractor, leftController);
            Apply(rightHandTrackingEvents.handIsTracked, rightHandInteractor, rightController);
        }

        static void Apply(bool handTracked, GameObject handInteractor, GameObject controller)
        {
            if (handInteractor.activeSelf != handTracked)
                handInteractor.SetActive(handTracked);

            if (controller.activeSelf == handTracked)
                controller.SetActive(!handTracked);
        }
    }
}
