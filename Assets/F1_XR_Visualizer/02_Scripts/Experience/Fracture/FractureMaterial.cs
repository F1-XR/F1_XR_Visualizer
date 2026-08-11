using UnityEngine;
using UnityEngine.Rendering;

namespace F1XR.Experience.Fracture
{
    /// <summary>
    /// Builds the one shared material the shell fragments use. Transparent, because each
    /// fragment fades out through a MaterialPropertyBlock alpha, and double-sided, because
    /// the fragments have no thickness and would otherwise vanish edge on.
    /// </summary>
    public static class FractureMaterial
    {
        public static Material Create(Shader shader, Color color, string name)
        {
            if (shader == null)
                return null;

            var material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            material.color = color;

            // URP Lit -> Transparent, alpha blend, no depth write. This is what makes the
            // per-fragment alpha fade visible.
            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.DisableKeyword("_ALPHATEST_ON");
                material.renderQueue = (int)RenderQueue.Transparent;
            }

            if (material.HasProperty("_Cull"))
                material.SetFloat("_Cull", (float)CullMode.Off);

            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0.1f);

            return material;
        }
    }
}
