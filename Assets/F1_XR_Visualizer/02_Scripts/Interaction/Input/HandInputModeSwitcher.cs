using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace F1XR.Interaction.Input
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

        float nextResolveTime;

        void Awake()
        {
            ResolveMissingReferences();
        }

        void Update()
        {
            if (!HasRequiredReferences() && Time.unscaledTime >= nextResolveTime)
            {
                ResolveMissingReferences();
                nextResolveTime = Time.unscaledTime + 1f;
            }

            Apply(leftHandTrackingEvents, leftHandInteractor, leftController);
            Apply(rightHandTrackingEvents, rightHandInteractor, rightController);
        }

        bool HasRequiredReferences()
        {
            return leftHandTrackingEvents != null &&
                rightHandTrackingEvents != null &&
                leftHandInteractor != null &&
                rightHandInteractor != null &&
                leftController != null &&
                rightController != null;
        }

        void ResolveMissingReferences()
        {
            foreach (XRHandTrackingEvents trackingEvents in GetComponentsInChildren<XRHandTrackingEvents>(true))
            {
                string side = HierarchyName(trackingEvents.transform);
                if (leftHandTrackingEvents == null && side.Contains("Left"))
                    leftHandTrackingEvents = trackingEvents;
                else if (rightHandTrackingEvents == null && side.Contains("Right"))
                    rightHandTrackingEvents = trackingEvents;
            }

            foreach (XRDirectInteractor interactor in GetComponentsInChildren<XRDirectInteractor>(true))
            {
                string side = HierarchyName(interactor.transform);
                if (leftHandInteractor == null && side.Contains("Left"))
                    leftHandInteractor = interactor.gameObject;
                else if (rightHandInteractor == null && side.Contains("Right"))
                    rightHandInteractor = interactor.gameObject;
            }

            foreach (Transform item in GetComponentsInChildren<Transform>(true))
            {
                if (leftController == null && item.name == "Left Controller")
                    leftController = item.gameObject;
                else if (rightController == null && item.name == "Right Controller")
                    rightController = item.gameObject;
            }
        }

        static string HierarchyName(Transform item)
        {
            string result = item != null ? item.name : string.Empty;
            for (Transform parent = item != null ? item.parent : null; parent != null; parent = parent.parent)
                result += "/" + parent.name;
            return result;
        }

        static void Apply(XRHandTrackingEvents trackingEvents, GameObject handInteractor, GameObject controller)
        {
            if (trackingEvents == null)
                return;

            bool handTracked = trackingEvents.handIsTracked;
            if (handInteractor == null || controller == null)
                return;

            if (handInteractor.activeSelf != handTracked)
                handInteractor.SetActive(handTracked);

            if (controller.activeSelf == handTracked)
                controller.SetActive(!handTracked);
        }
    }
}
