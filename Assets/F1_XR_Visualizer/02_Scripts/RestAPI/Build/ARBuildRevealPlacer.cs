using F1XR.AR;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using UnityEngine.XR.ARFoundation;

namespace F1XR.RestAPI.AR
{
    public sealed partial class ARBuildRevealPlacer : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] ARPlanePlacementController placementController;
        [SerializeField] ARAnchorManager anchorManager;
        [SerializeField] GameObject placementPrefab;

        [Header("Input")]
        [SerializeField] InputActionProperty placeAction;
        [SerializeField] bool useControllerTriggerFallback = true;
        [SerializeField] float inputArmDelay = 0.5f;

        [Header("Preview")]
        [SerializeField] Material previewMaterial;
        [SerializeField] Color previewColor = new Color(0.2f, 1f, 0.35f, 0.35f);
        [SerializeField] float verticalOffset = 0.04f;
        [SerializeField] bool useHitRotation = true;

        [Header("Build Animation")]
        [SerializeField] float buildDuration = 2.0f;
        [SerializeField] float buildEdgeWidth = 0.18f;
        [SerializeField, ColorUsage(true, true)]
        Color buildEdgeColor = new Color(4f, 1.8f, 0.1f, 1f);
        [FormerlySerializedAs("restoreOriginalMaterialsAfterBuild")]
        [SerializeField] bool restoreMaterialsAfterBuild = true;
        [SerializeField] bool allowReplaceExisting = false;

        public bool HasPlacement => spawnedInstance != null;
        public Transform PlacementTransform => spawnedInstance != null ? spawnedInstance.transform : null;

        GameObject previewInstance;
        GameObject spawnedInstance;
        ARAnchor currentAnchor;

        Pose currentPose;
        ARPlane currentPlane;
        bool hasCurrentHit;

        bool inputsArmed;
        float enableTime;
        bool wasLeftTriggerPressed;
        bool wasRightTriggerPressed;

        Material runtimePreviewMaterial;

        public void SetPlacementPrefab(GameObject prefab)
        {
            placementPrefab = prefab;
            HidePreview();
        }

        void Reset()
        {
            placementController = GetComponent<ARPlanePlacementController>();
            anchorManager = GetComponent<ARAnchorManager>();
        }

        void Awake()
        {
            if (placementController == null)
                placementController = GetComponent<ARPlanePlacementController>();

            if (anchorManager == null)
                anchorManager = GetComponent<ARAnchorManager>();
        }

        void OnEnable()
        {
            if (placeAction.action != null)
                placeAction.action.Enable();

            enableTime = Time.time;
            inputsArmed = false;
            wasLeftTriggerPressed = false;
            wasRightTriggerPressed = false;
        }

        void OnDisable()
        {
            if (placeAction.action != null)
                placeAction.action.Disable();

            HidePreview();
        }

        void OnDestroy()
        {
            if (previewInstance != null)
                Destroy(previewInstance);

            if (runtimePreviewMaterial != null)
                Destroy(runtimePreviewMaterial);
        }

        void Update()
        {
            // 이미 하나 배치했고 교체 허용이 꺼져 있으면 더 이상 preview를 보여주지 않음
            if (spawnedInstance != null && !allowReplaceExisting)
            {
                HidePreview();
                return;
            }

            UpdatePlacementHit();
            UpdatePreview();

            if (!inputsArmed)
            {
                if (Time.time >= enableTime + inputArmDelay && !IsAnyPlacementInputHeld())
                    inputsArmed = true;

                UpdateTriggerStateOnly();
                return;
            }

            bool pressedThisFrame = false;

            if (placeAction.action != null && placeAction.action.WasPressedThisFrame())
                pressedThisFrame = true;

            if (useControllerTriggerFallback && WasTriggerPressedThisFrame())
                pressedThisFrame = true;

            if (pressedThisFrame)
                ConfirmPlacement();
        }

        void UpdatePlacementHit()
        {
            hasCurrentHit = placementController != null &&
                placementController.TryGetPlacementHit(out currentPose, out currentPlane);
        }

    }
}
