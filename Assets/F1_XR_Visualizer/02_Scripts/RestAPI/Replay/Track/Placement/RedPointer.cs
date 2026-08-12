using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using Unity.VisualScripting;

namespace F1XR.RestAPI.Replay.Track.Placement
{
    [RenamedFrom("F1XR.AR.RedPointer")]
    public sealed class RedPointer : MonoBehaviour
    {
        [SerializeField] ARPlanePlacementController placementController;
        [SerializeField] bool showPointer = true;
        [SerializeField] bool showControllerPointers = true;
        [SerializeField] bool showHandPointers = true;
        [SerializeField] float pointerSize = 0.025f;
        [SerializeField] float surfaceOffset = 0.005f;
        [SerializeField] Color pointerColor = Color.red;
        [SerializeField] float previewLineWidth = 0.01f;
        [SerializeField] Color validPreviewColor = new(0.1f, 1f, 0.35f, 0.9f);
        [SerializeField] Color invalidPreviewColor = new(1f, 0.1f, 0.05f, 0.9f);
        [SerializeField, Range(0.1f, 0.8f)] float prefabPreviewAlpha = 0.4f;

        Material pointerMaterial;
        GameObject placementPreview;
        static readonly Vector3[] s_FallbackPoints =
        {
            new(-0.5f, 0f, -0.5f),
            new(-0.5f, 0f, 0.5f),
            new(0.5f, 0f, 0.5f),
            new(0.5f, 0f, -0.5f),
            new(-0.5f, 0f, -0.5f)
        };

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
            if (!showPointer || placementController == null || placementController.HasPlacement)
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

            if (rightControllerHit)
                UpdatePointer(ref placementPreview, true, rightControllerPose, "Track Placement Preview");
            else if (leftControllerHit)
                UpdatePointer(ref placementPreview, true, leftControllerPose, "Track Placement Preview");
            else if (rightHandHit)
                UpdatePointer(ref placementPreview, true, rightHandPose, "Track Placement Preview");
            else
                UpdatePointer(ref placementPreview, leftHandHit, leftHandPose, "Track Placement Preview");
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
                pose.rotation);
            UpdatePreviewLine(pointer, canPlace: true);
            pointer.SetActive(true);
        }

        void EnsurePointer(ref GameObject pointer, string pointerName)
        {
            if (pointer != null)
                return;

            pointer = new GameObject(pointerName);
            pointer.name = pointerName;
            pointer.transform.localScale = Vector3.one;

            if (TryCreatePrefabPreview(pointer))
            {
                pointer.SetActive(false);
                return;
            }

            var line = pointer.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = false;
            line.positionCount = s_FallbackPoints.Length;
            line.SetPositions(s_FallbackPoints);
            line.startWidth = Mathf.Max(previewLineWidth, pointerSize);
            line.endWidth = Mathf.Max(previewLineWidth, pointerSize);
            line.numCornerVertices = 5;
            line.numCapVertices = 5;
            line.material = GetPointerMaterial();
            SetPreviewColor(line, validPreviewColor);

            pointer.SetActive(false);
        }

        bool TryCreatePrefabPreview(GameObject pointer)
        {
            var prefab = placementController != null ? placementController.PlacementPrefab : null;
            if (prefab == null)
                return false;

            var preview = Instantiate(prefab, pointer.transform);
            preview.name = "Placement Prefab Preview";
            preview.transform.localPosition = Vector3.zero;
            preview.transform.localRotation = Quaternion.identity;
            preview.transform.localScale = prefab.transform.localScale;

            StripPreviewBehaviours(preview);
            var renderers = preview.GetComponentsInChildren<Renderer>(includeInactive: true);
            if (renderers.Length == 0)
            {
                Destroy(preview);
                return false;
            }

            foreach (var renderer in renderers)
            {
                renderer.enabled = true;
                renderer.sharedMaterials = CreatePrefabPreviewMaterials(renderer.sharedMaterials);
            }

            return true;
        }

        static void StripPreviewBehaviours(GameObject preview)
        {
            foreach (var collider in preview.GetComponentsInChildren<Collider>(includeInactive: true))
            {
                collider.enabled = false;
                Destroy(collider);
            }

            foreach (var rigidbody in preview.GetComponentsInChildren<Rigidbody>(includeInactive: true))
            {
                rigidbody.useGravity = false;
                rigidbody.isKinematic = true;
                Destroy(rigidbody);
            }

            foreach (var behaviour in preview.GetComponentsInChildren<MonoBehaviour>(includeInactive: true))
            {
                behaviour.enabled = false;
                Destroy(behaviour);
            }
        }

        void UpdatePreviewLine(GameObject pointer, bool canPlace)
        {
            var line = pointer.GetComponent<LineRenderer>();
            pointer.transform.localScale = Vector3.one;

            var color = canPlace ? validPreviewColor : invalidPreviewColor;
            if (line != null)
            {
                line.startWidth = Mathf.Max(previewLineWidth, pointerSize);
                line.endWidth = Mathf.Max(previewLineWidth, pointerSize);
                SetPreviewColor(line, color);
            }

        }

        Material[] CreatePrefabPreviewMaterials(Material[] sourceMaterials)
        {
            var previewMaterials = new Material[sourceMaterials.Length];
            for (var i = 0; i < previewMaterials.Length; i++)
            {
                previewMaterials[i] = CreatePrefabPreviewMaterial(sourceMaterials[i]);
            }

            return previewMaterials;
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
                color = validPreviewColor
            };
            return pointerMaterial;
        }

        Material CreatePrefabPreviewMaterial(Material sourceMaterial)
        {
            Material material;
            if (sourceMaterial != null)
            {
                material = new Material(sourceMaterial);
            }
            else
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null)
                    shader = Shader.Find("Unlit/Color");
                if (shader == null)
                    shader = Shader.Find("Standard");

                material = new Material(shader);
            }

            SetMaterialAlpha(material, prefabPreviewAlpha);
            return material;
        }

        void SetPreviewColor(LineRenderer line, Color color)
        {
            var previewColor = color.a > 0f ? color : pointerColor;
            line.startColor = previewColor;
            line.endColor = previewColor;

            if (pointerMaterial != null)
                pointerMaterial.color = previewColor;
        }

        static void SetMaterialAlpha(Material material, float alpha)
        {
            if (material.HasProperty("_BaseColor"))
            {
                var baseColor = material.GetColor("_BaseColor");
                baseColor.a = alpha;
                material.SetColor("_BaseColor", baseColor);
            }

            if (material.HasProperty("_Color"))
            {
                var materialColor = material.GetColor("_Color");
                materialColor.a = alpha;
                material.SetColor("_Color", materialColor);
            }

            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);

            if (material.HasProperty("_Mode"))
                material.SetFloat("_Mode", 3f);

            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        void HideAllPointers()
        {
            HidePointer(placementPreview);
        }

        static void HidePointer(GameObject pointer)
        {
            if (pointer != null)
                pointer.SetActive(false);
        }
    }
}
