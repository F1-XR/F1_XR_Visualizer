using UnityEngine;
using F1XR.RestAPI.Replay.Track.Placement;

namespace F1XR.Legacy
{
    public sealed class XRInputViewGate : MonoBehaviour
    {
        [SerializeField] ARPlanePlacementController placementController;
        [SerializeField] GameObject[] controllerViews;
        [SerializeField] GameObject[] handViews;

        void Reset()
        {
            placementController = GetComponent<ARPlanePlacementController>();
        }

        void Awake()
        {
            if (placementController == null)
                placementController = GetComponent<ARPlanePlacementController>();
        }

        void Update()
        {
            if (placementController == null)
                return;

            var showControllers = placementController.CanUseControllers() &&
                placementController.IsAnyControllerTracked();
            var showHands = placementController.CanUseHands() &&
                placementController.IsAnyHandTracked();

            SetActive(controllerViews, showControllers);
            SetActive(handViews, showHands);
        }

        static void SetActive(GameObject[] views, bool active)
        {
            if (views == null)
                return;

            foreach (var view in views)
            {
                if (view != null && view.activeSelf != active)
                    view.SetActive(active);
            }
        }
    }
}
