using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace F1XR.RestAPI.AR
{
    public sealed class BuildRevealController : MonoBehaviour
    {
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int BuildHeightId = Shader.PropertyToID("_BuildHeight");
        static readonly int EdgeWidthId = Shader.PropertyToID("_EdgeWidth");
        static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
        static readonly int AlphaId = Shader.PropertyToID("_Alpha");

        [Header("Target")]
        [SerializeField] Transform visualRoot;
        [SerializeField] string visualRootName = "Visual";

        [Header("Build Reveal")]
        [SerializeField] float duration = 1.2f;
        [SerializeField] float edgeWidth = 0.06f;
        [SerializeField] Color edgeColor = new Color(1f, 0.55f, 0.05f, 1f);
        [SerializeField] float startPadding = 0.00f;
        [SerializeField] float endPadding = 0.005f;
        [SerializeField] bool restoreOriginalMaterialsOnComplete = true;

        Renderer[] renderers;
        Material[][] originalMaterials;
        readonly List<Material> runtimeRevealMaterials = new();
        MaterialPropertyBlock propertyBlock;
        Coroutine revealRoutine;

        public void Configure(
            float newDuration,
            float newEdgeWidth,
            Color newEdgeColor,
            bool restoreMaterialsOnComplete)
        {
            duration = Mathf.Max(0.01f, newDuration);
            edgeWidth = Mathf.Max(0.001f, newEdgeWidth);
            edgeColor = newEdgeColor;
            restoreOriginalMaterialsOnComplete = restoreMaterialsOnComplete;
        }

        public void Play()
        {
            if (!isActiveAndEnabled)
                enabled = true;

            if (revealRoutine != null)
                StopCoroutine(revealRoutine);

            revealRoutine = StartCoroutine(PlayRoutine());
        }

        IEnumerator PlayRoutine()
        {
            if (!PrepareRenderers())
                yield break;

            if (!TryCalculateWorldBounds(out var bounds))
                yield break;

            float minY = bounds.min.y - startPadding;
            float maxY = bounds.max.y + endPadding;

            ApplyBuildHeight(minY);

            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;

                float t = Mathf.Clamp01(elapsed / duration);
                t = Mathf.SmoothStep(0f, 1f, t);

                float height = Mathf.Lerp(minY, maxY, t);
                ApplyBuildHeight(height);

                yield return null;
            }

            ApplyBuildHeight(maxY);

            if (restoreOriginalMaterialsOnComplete)
                RestoreOriginalMaterials();

            revealRoutine = null;
        }

        bool PrepareRenderers()
        {
            ResolveVisualRoot();

            Transform root = visualRoot != null ? visualRoot : transform;
            renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);

            if (renderers == null || renderers.Length == 0)
            {
                Debug.LogWarning("[BuildRevealController] Renderer를 찾지 못했습니다.", this);
                return false;
            }

            Shader revealShader = Shader.Find("F1XR/BuildRevealURP");
            if (revealShader == null)
            {
                Debug.LogError("[BuildRevealController] Shader 'F1XR/BuildRevealURP'를 찾지 못했습니다.", this);
                return false;
            }

            propertyBlock ??= new MaterialPropertyBlock();

            originalMaterials = new Material[renderers.Length][];
            runtimeRevealMaterials.Clear();

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];

                originalMaterials[i] = renderer.sharedMaterials;

                Material[] revealMaterials = new Material[originalMaterials[i].Length];

                for (int j = 0; j < revealMaterials.Length; j++)
                {
                    Material original = originalMaterials[i][j];
                    Material revealMaterial = new Material(revealShader);

                    CopyBaseVisualProperties(original, revealMaterial);

                    revealMaterial.SetFloat(EdgeWidthId, edgeWidth);
                    revealMaterial.SetColor(EdgeColorId, edgeColor);
                    revealMaterial.SetFloat(AlphaId, 1f);

                    revealMaterials[j] = revealMaterial;
                    runtimeRevealMaterials.Add(revealMaterial);
                }

                renderer.materials = revealMaterials;
            }

            return true;
        }

        void ResolveVisualRoot()
        {
            if (visualRoot != null)
                return;

            if (string.IsNullOrWhiteSpace(visualRootName))
                return;

            Transform found = transform.Find(visualRootName);
            if (found != null)
                visualRoot = found;
        }

        static void CopyBaseVisualProperties(Material source, Material destination)
        {
            if (source == null || destination == null)
                return;

            Texture baseTexture = null;

            if (source.HasProperty("_BaseMap"))
                baseTexture = source.GetTexture("_BaseMap");
            else if (source.HasProperty("_MainTex"))
                baseTexture = source.GetTexture("_MainTex");

            if (baseTexture != null)
                destination.SetTexture(BaseMapId, baseTexture);

            Color baseColor = Color.white;

            if (source.HasProperty("_BaseColor"))
                baseColor = source.GetColor("_BaseColor");
            else if (source.HasProperty("_Color"))
                baseColor = source.GetColor("_Color");

            destination.SetColor(BaseColorId, baseColor);
        }

        bool TryCalculateWorldBounds(out Bounds bounds)
        {
            bounds = default;
            bool hasBounds = false;

            Debug.Log("========== [BuildReveal] Renderer Bounds Check ==========", this);

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                    continue;

                Bounds rb = renderer.bounds;

                Debug.Log(
                    $"[BuildReveal] Renderer: {GetTransformPath(renderer.transform)} / " +
                    $"minY={rb.min.y:F3}, maxY={rb.max.y:F3}, sizeY={rb.size.y:F3}",
                    renderer
                );

                if (!hasBounds)
                {
                    bounds = rb;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(rb);
                }
            }

            if (!hasBounds)
            {
                Debug.LogWarning("[BuildRevealController] Bounds 계산 실패.", this);
                return false;
            }

            Debug.Log(
                $"[BuildReveal] FINAL BOUNDS / " +
                $"minY={bounds.min.y:F3}, maxY={bounds.max.y:F3}, sizeY={bounds.size.y:F3}",
                this
            );

            return true;
        }

        static string GetTransformPath(Transform target)
        {
            if (target == null)
                return "";

            string path = target.name;
            Transform current = target.parent;

            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }

        void ApplyBuildHeight(float height)
        {
            if (renderers == null)
                return;

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                    continue;

                renderer.GetPropertyBlock(propertyBlock);
                propertyBlock.SetFloat(BuildHeightId, height);
                propertyBlock.SetFloat(EdgeWidthId, edgeWidth);
                propertyBlock.SetColor(EdgeColorId, edgeColor);
                propertyBlock.SetFloat(AlphaId, 1f);
                renderer.SetPropertyBlock(propertyBlock);
            }
        }

        void RestoreOriginalMaterials()
        {
            if (renderers != null && originalMaterials != null)
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] != null && i < originalMaterials.Length)
                        renderers[i].sharedMaterials = originalMaterials[i];
                }
            }

            foreach (Material material in runtimeRevealMaterials)
            {
                if (material != null)
                    Destroy(material);
            }

            runtimeRevealMaterials.Clear();
        }
    }
}