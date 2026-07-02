using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;

namespace F1XR.AR
{
    public sealed class RedPointer : MonoBehaviour
    {
        [SerializeField] ARPlanePlacementController placementController;
        [SerializeField] bool showPointer = true;
        [SerializeField] bool showControllerPointers = true;
        [SerializeField] bool showHandPointers = true;
        [SerializeField] float pointerSize = 0.025f;
        [SerializeField] float surfaceOffset = 0.005f;
        [SerializeField] Color pointerColor = Color.red;

        Material pointerMaterial;
        GameObject leftControllerPointer;
        GameObject rightControllerPointer;
        GameObject leftHandPointer;
        GameObject rightHandPointer;

        void Reset()
        {
            placementController = GetComponent<ARPlanePlacementController>();
        }

        void Awake()
        {
            if (placementController == null)
                placementController = GetComponent<ARPlanePlacementController>();
        }

        void OnDisable()
        {
            HideAllPointers();
        }

        void Update()
        {
            if (!showPointer || placementController == null)
            {
                HideAllPointers();
                return;
            }

            var rightControllerPose = default(Pose);
            var leftControllerPose = default(Pose);
            var rightHandPose = default(Pose);
            var leftHandPose = default(Pose);
            var useControllers = showControllerPointers && placementController.CanUseControllers();
            var useHands = showHandPointers && placementController.CanUseHands();
            var rightControllerHit = useControllers &&
                placementController.TryGetControllerPlacementHit(
                    InputDeviceCharacteristics.Right,
                    out rightControllerPose,
                    out _);
            var leftControllerHit = useControllers &&
                placementController.TryGetControllerPlacementHit(
                    InputDeviceCharacteristics.Left,
                    out leftControllerPose,
                    out _);
            var rightHandHit = useHands &&
                placementController.TryGetHandPlacementHit(
                    Handedness.Right,
                    out rightHandPose,
                    out _);
            var leftHandHit = useHands &&
                placementController.TryGetHandPlacementHit(
                    Handedness.Left,
                    out leftHandPose,
                    out _);

            UpdatePointer(
                ref rightControllerPointer,
                rightControllerHit,
                rightControllerPose,
                "Right Controller Red Pointer");

            UpdatePointer(
                ref leftControllerPointer,
                leftControllerHit,
                leftControllerPose,
                "Left Controller Red Pointer");

            UpdatePointer(
                ref rightHandPointer,
                rightHandHit,
                rightHandPose,
                "Right Hand Red Pointer");

            UpdatePointer(
                ref leftHandPointer,
                leftHandHit,
                leftHandPose,
                "Left Hand Red Pointer");
        }

        void UpdatePointer(ref GameObject pointer, bool hasHit, Pose pose, string pointerName)
        {
            if (!hasHit)
            {
                HidePointer(pointer);
                return;
            }

            EnsurePointer(ref pointer, pointerName);
            pointer.transform.SetPositionAndRotation(
                pose.position + pose.up * surfaceOffset,
                Quaternion.identity);
            pointer.SetActive(true);
        }

        void EnsurePointer(ref GameObject pointer, string pointerName)
        {
            if (pointer != null)
                return;

            pointer = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pointer.name = pointerName;
            pointer.transform.localScale = Vector3.one * pointerSize;

            var collider = pointer.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            var renderer = pointer.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.sharedMaterial = GetPointerMaterial();

            pointer.SetActive(false);
        }

        Material GetPointerMaterial()
        {
            if (pointerMaterial != null)
                return pointerMaterial;

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Standard");

            pointerMaterial = new Material(shader)
            {
                color = pointerColor
            };
            return pointerMaterial;
        }

        void HideAllPointers()
        {
            HidePointer(leftControllerPointer);
            HidePointer(rightControllerPointer);
            HidePointer(leftHandPointer);
            HidePointer(rightHandPointer);
        }

        static void HidePointer(GameObject pointer)
        {
            if (pointer != null)
                pointer.SetActive(false);
        }
    }
}
