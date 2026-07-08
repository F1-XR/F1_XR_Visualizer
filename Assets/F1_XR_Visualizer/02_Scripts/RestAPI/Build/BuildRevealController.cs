using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

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
        [Tooltip("빌드 리빌 효과를 적용할 시각 모델 루트입니다. 비워두면 visualRootName으로 자식 오브젝트를 찾습니다.")]
        [SerializeField] Transform visualRoot;
        [Tooltip("visualRoot가 비어 있을 때 찾을 자식 이름입니다. 기본값 Visual이면 프리팹의 Visual 하위 Renderer에만 효과가 적용됩니다.")]
        [SerializeField] string visualRootName = "Visual";

        [Header("Build Reveal")]
        [Tooltip("아래에서 위로 생성되는 데 걸리는 시간입니다. 값을 키우면 더 천천히 올라옵니다.")]
        [SerializeField] float duration = 1.2f;
        [Tooltip("생성 경계선 두께입니다. 키우면 빛나는 라인이 두꺼워집니다.")]
        [SerializeField] float edgeWidth = 0.06f;
        [Tooltip("생성 경계선 색입니다. 밝은 HDR 색을 주면 더 강하게 빛납니다.")]
        [SerializeField] Color edgeColor = new Color(1f, 0.55f, 0.05f, 1f);
        [Tooltip("시작 높이를 모델 최저점보다 얼마나 더 아래에서 시작할지 정합니다. 키우면 등장 전 여백이 늘어납니다.")]
        [SerializeField] float startPadding = 0.00f;
        [Tooltip("끝 높이를 모델 최고점보다 얼마나 더 위까지 올릴지 정합니다. 일부 윗면이 늦게 잘리면 조금 키웁니다.")]
        [SerializeField] float endPadding = 0.005f;
        [FormerlySerializedAs("restoreOriginalMaterialsOnComplete")]
        [Tooltip("생성 완료 후 원래 머티리얼로 되돌릴지 정합니다. 끄면 리빌 셰이더 상태가 계속 유지됩니다.")]
        [SerializeField] bool restoreMaterialsOnComplete = true;
        [Tooltip("켜면 각 Renderer의 높이 범위를 Console에 출력합니다. 효과 범위가 이상할 때만 사용합니다.")]
        [SerializeField] bool logBoundsDetails;

        Renderer[] renderers;
        Material[][] originalMaterials;
        readonly List<Material> runtimeRevealMaterials = new();
        MaterialPropertyBlock propertyBlock;
        Coroutine revealRoutine;

        public event Action Completed;

        public void Configure(
            float newDuration,
            float newEdgeWidth,
            Color newEdgeColor,
            bool restoreMaterialsOnComplete)
        {
            duration = Mathf.Max(0.01f, newDuration);
            edgeWidth = Mathf.Max(0.001f, newEdgeWidth);
            edgeColor = newEdgeColor;
            this.restoreMaterialsOnComplete = restoreMaterialsOnComplete;
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
            {
                FinishReveal();
                yield break;
            }

            if (!TryCalculateWorldBounds(out var bounds))
            {
                FinishReveal();
                yield break;
            }

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

            if (restoreMaterialsOnComplete)
                RestoreOriginalMaterials();

            FinishReveal();
        }

        void FinishReveal()
        {
            revealRoutine = null;
            Completed?.Invoke();
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

            if (logBoundsDetails)
                Debug.Log("========== [BuildReveal] Renderer Bounds Check ==========", this);

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                    continue;

                Bounds rb = renderer.bounds;

                if (logBoundsDetails)
                {
                    Debug.Log(
                        $"[BuildReveal] Renderer: {GetTransformPath(renderer.transform)} / " +
                        $"minY={rb.min.y:F3}, maxY={rb.max.y:F3}, sizeY={rb.size.y:F3}",
                        renderer
                    );
                }

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

            if (logBoundsDetails)
            {
                Debug.Log(
                    $"[BuildReveal] FINAL BOUNDS / " +
                    $"minY={bounds.min.y:F3}, maxY={bounds.max.y:F3}, sizeY={bounds.size.y:F3}",
                    this
                );
            }

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
