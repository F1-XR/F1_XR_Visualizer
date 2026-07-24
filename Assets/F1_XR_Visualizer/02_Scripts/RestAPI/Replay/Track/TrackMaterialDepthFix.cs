using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace F1XR.RestAPI.Replay.Track
{
    [DisallowMultipleComponent]
    public sealed class TrackMaterialDepthFix : MonoBehaviour
    {
        static readonly int SurfaceId = Shader.PropertyToID("_Surface");
        static readonly int AlphaClipId = Shader.PropertyToID("_AlphaClip");
        static readonly int AlphaCutoffId = Shader.PropertyToID("alphaCutoff");
        static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
        static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        static readonly int SrcBlendAlphaId = Shader.PropertyToID("_SrcBlendAlpha");
        static readonly int DstBlendAlphaId = Shader.PropertyToID("_DstBlendAlpha");

        [SerializeField, Range(0f, 1f)] float alphaCutoff = 0.5f;

        readonly List<Material> runtimeMaterials = new();

        void Awake()
        {
            Apply();
        }

        void OnDestroy()
        {
            foreach (Material material in runtimeMaterials)
            {
                if (material != null)
                    Destroy(material);
            }

            runtimeMaterials.Clear();
        }

        void Apply()
        {
            var replacements = new Dictionary<Material, Material>();

            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                Material[] materials = renderer.sharedMaterials;
                bool changed = false;

                for (int i = 0; i < materials.Length; i++)
                {
                    Material source = materials[i];
                    if (!NeedsDepthFix(source))
                        continue;

                    if (!replacements.TryGetValue(source, out Material replacement))
                    {
                        replacement = new Material(source)
                        {
                            name = source.name + " (Depth Fixed)"
                        };
                        ConfigureAlphaClip(replacement);
                        replacements.Add(source, replacement);
                        runtimeMaterials.Add(replacement);
                    }

                    materials[i] = replacement;
                    changed = true;
                }

                if (changed)
                    renderer.sharedMaterials = materials;
            }
        }

        bool NeedsDepthFix(Material material)
        {
            if (material == null ||
                material.shader == null ||
                material.shader.name.IndexOf("glTF", System.StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            return material.renderQueue >= (int)RenderQueue.Transparent ||
                material.GetTag("RenderType", false) == "Transparent";
        }

        void ConfigureAlphaClip(Material material)
        {
            material.SetOverrideTag("RenderType", "TransparentCutout");
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_DISABLE_SSR_TRANSPARENT");
            material.DisableKeyword("_ENABLE_FOG_ON_TRANSPARENT");
            material.EnableKeyword("_ALPHATEST_ON");

            SetFloatIfPresent(material, SurfaceId, 0f);
            SetFloatIfPresent(material, AlphaClipId, 1f);
            SetFloatIfPresent(material, AlphaCutoffId, alphaCutoff);
            SetFloatIfPresent(material, ZWriteId, 1f);
            SetFloatIfPresent(material, SrcBlendId, (float)BlendMode.One);
            SetFloatIfPresent(material, DstBlendId, (float)BlendMode.Zero);
            SetFloatIfPresent(material, SrcBlendAlphaId, (float)BlendMode.One);
            SetFloatIfPresent(material, DstBlendAlphaId, (float)BlendMode.Zero);

            material.SetShaderPassEnabled("DepthOnly", true);
            material.SetShaderPassEnabled("TransparentDepthPrepass", false);
            material.SetShaderPassEnabled("TransparentDepthPostpass", false);
            material.renderQueue = (int)RenderQueue.AlphaTest;
        }

        static void SetFloatIfPresent(Material material, int propertyId, float value)
        {
            if (material.HasProperty(propertyId))
                material.SetFloat(propertyId, value);
        }
    }
}
