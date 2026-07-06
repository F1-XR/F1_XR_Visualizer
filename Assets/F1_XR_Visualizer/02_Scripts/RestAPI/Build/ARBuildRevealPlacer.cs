using System.Collections.Generic;
using F1XR.AR;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;

using XRInputDevice = UnityEngine.XR.InputDevice;
using XRInputDevices = UnityEngine.XR.InputDevices;
using XRCommonUsages = UnityEngine.XR.CommonUsages;
using XRInputDeviceCharacteristics = UnityEngine.XR.InputDeviceCharacteristics;

namespace F1XR.RestAPI.AR
{
    public sealed class ARBuildRevealPlacer : MonoBehaviour
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
        [SerializeField] bool restoreOriginalMaterialsAfterBuild = true;
        [SerializeField] bool allowReplaceExisting = false;

        static readonly List<XRInputDevice> s_InputDevices = new();

        readonly Dictionary<Renderer, Material[]> previewOriginalMaterials = new();
        readonly List<Behaviour> previewDisabledBehaviours = new();
        readonly List<Collider> previewDisabledColliders = new();

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

            if (useControllerTriggerFallback && WasControllerTriggerPressedThisFrame())
                pressedThisFrame = true;

            if (pressedThisFrame)
                ConfirmPlacement();
        }

        void UpdatePlacementHit()
        {
            hasCurrentHit = placementController != null &&
                placementController.TryGetPlacementHit(out currentPose, out currentPlane);
        }

        void UpdatePreview()
        {
            if (!hasCurrentHit || placementPrefab == null)
            {
                HidePreview();
                return;
            }

            if (previewInstance == null)
                CreatePreview();

            if (previewInstance == null)
                return;

            Vector3 position = currentPose.position + Vector3.up * verticalOffset;
            Quaternion rotation = useHitRotation ? currentPose.rotation : Quaternion.identity;

            previewInstance.transform.SetPositionAndRotation(position, rotation);

            if (!previewInstance.activeSelf)
                previewInstance.SetActive(true);
        }

        void CreatePreview()
        {
            ClearPreviewCaches();

            previewInstance = Instantiate(placementPrefab);
            previewInstance.name = placementPrefab.name + " Preview";

            DisablePreviewBehaviours(previewInstance);
            ApplyPreviewMaterial(previewInstance);

            previewInstance.SetActive(false);
        }

        void DisablePreviewBehaviours(GameObject target)
        {
            foreach (Collider col in target.GetComponentsInChildren<Collider>(includeInactive: true))
            {
                if (!col.enabled)
                    continue;

                previewDisabledColliders.Add(col);
                col.enabled = false;
            }

            foreach (Animator animator in target.GetComponentsInChildren<Animator>(includeInactive: true))
            {
                if (!animator.enabled)
                    continue;

                previewDisabledBehaviours.Add(animator);
                animator.enabled = false;
            }

            foreach (MonoBehaviour behaviour in target.GetComponentsInChildren<MonoBehaviour>(includeInactive: true))
            {
                if (!behaviour.enabled)
                    continue;

                previewDisabledBehaviours.Add(behaviour);
                behaviour.enabled = false;
            }

            foreach (Rigidbody rigidbody in target.GetComponentsInChildren<Rigidbody>(includeInactive: true))
            {
                rigidbody.useGravity = false;
                rigidbody.isKinematic = true;
            }
        }

        void RestorePreviewForRealUse(GameObject target)
        {
            RestorePreviewMaterials(target);

            foreach (Collider col in previewDisabledColliders)
            {
                if (col != null)
                    col.enabled = true;
            }

            foreach (Behaviour behaviour in previewDisabledBehaviours)
            {
                if (behaviour != null)
                    behaviour.enabled = true;
            }

            previewDisabledColliders.Clear();
            previewDisabledBehaviours.Clear();

            ConfigurePhysics(target);
        }

        void ApplyPreviewMaterial(GameObject target)
        {
            Material material = previewMaterial != null ? previewMaterial : GetOrCreatePreviewMaterial();

            if (material == null)
                return;

            foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                if (!previewOriginalMaterials.ContainsKey(renderer))
                    previewOriginalMaterials.Add(renderer, renderer.sharedMaterials);

                int materialCount = Mathf.Max(1, renderer.sharedMaterials.Length);
                Material[] materials = new Material[materialCount];

                for (int i = 0; i < materials.Length; i++)
                    materials[i] = material;

                renderer.sharedMaterials = materials;
            }
        }

        void RestorePreviewMaterials(GameObject target)
        {
            foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                if (renderer != null &&
                    previewOriginalMaterials.TryGetValue(renderer, out Material[] originalMaterials))
                {
                    renderer.sharedMaterials = originalMaterials;
                }
            }

            previewOriginalMaterials.Clear();
        }

        Material GetOrCreatePreviewMaterial()
        {
            if (runtimePreviewMaterial != null)
                return runtimePreviewMaterial;

            Shader shader = Shader.Find("F1XR/PreviewTransparentURP");

            if (shader == null)
            {
                Debug.LogError("[ARBuildRevealPlacer] Shader 'F1XR/PreviewTransparentURP'를 찾지 못했습니다.", this);
                return null;
            }

            runtimePreviewMaterial = new Material(shader);
            runtimePreviewMaterial.SetColor("_BaseColor", previewColor);
            runtimePreviewMaterial.renderQueue = 3000;

            return runtimePreviewMaterial;
        }

        void HidePreview()
        {
            if (previewInstance != null && previewInstance.activeSelf)
                previewInstance.SetActive(false);
        }

        void ConfirmPlacement()
        {
            if (!hasCurrentHit || placementPrefab == null)
                return;

            if (spawnedInstance != null && !allowReplaceExisting)
                return;

            if (allowReplaceExisting)
                ClearSpawned();

            GameObject target;

            // 핵심 변경:
            // 새 prefab을 다시 Instantiate하지 않고, 이미 떠 있던 preview를 실제 오브젝트로 전환한다.
            if (previewInstance != null)
            {
                target = previewInstance;
                previewInstance = null;

                target.name = placementPrefab.name;
                target.SetActive(true);

                RestorePreviewForRealUse(target);
            }
            else
            {
                target = Instantiate(placementPrefab);
                target.name = placementPrefab.name;
                ConfigurePhysics(target);
            }

            currentAnchor = CreateAnchor(currentPose, currentPlane);

            if (currentAnchor != null)
            {
                target.transform.SetParent(currentAnchor.transform, worldPositionStays: false);
                target.transform.localPosition = Vector3.up * verticalOffset;
                target.transform.localRotation = Quaternion.identity;
            }
            else
            {
                Vector3 position = currentPose.position + Vector3.up * verticalOffset;
                Quaternion rotation = useHitRotation ? currentPose.rotation : Quaternion.identity;
                target.transform.SetPositionAndRotation(position, rotation);
            }

            spawnedInstance = target;

            BuildRevealController revealController =
                spawnedInstance.GetComponent<BuildRevealController>();

            if (revealController == null)
                revealController = spawnedInstance.AddComponent<BuildRevealController>();

            revealController.Configure(
                buildDuration,
                buildEdgeWidth,
                buildEdgeColor,
                restoreOriginalMaterialsAfterBuild);

            revealController.Play();
        }

        ARAnchor CreateAnchor(Pose pose, ARPlane plane)
        {
            if (anchorManager == null)
                return null;

            if (plane != null)
            {
                ARAnchor attachedAnchor = anchorManager.AttachAnchor(plane, pose);
                if (attachedAnchor != null)
                    return attachedAnchor;
            }

            GameObject anchorObject = new GameObject("Placed Build Anchor");
            anchorObject.transform.SetPositionAndRotation(pose.position, pose.rotation);
            return anchorObject.AddComponent<ARAnchor>();
        }

        static void ConfigurePhysics(GameObject target)
        {
            foreach (Rigidbody rigidbody in target.GetComponentsInChildren<Rigidbody>(includeInactive: true))
            {
                rigidbody.useGravity = false;
                rigidbody.isKinematic = true;
            }
        }

        public void ClearSpawned()
        {
            if (currentAnchor != null)
            {
                Destroy(currentAnchor.gameObject);
                currentAnchor = null;
                spawnedInstance = null;
                return;
            }

            if (spawnedInstance != null)
            {
                Destroy(spawnedInstance);
                spawnedInstance = null;
            }
        }

        void ClearPreviewCaches()
        {
            previewOriginalMaterials.Clear();
            previewDisabledBehaviours.Clear();
            previewDisabledColliders.Clear();
        }

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

        bool WasControllerTriggerPressedThisFrame()
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