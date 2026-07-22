using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering;

namespace F1XR.Showroom
{
    [ExecuteAlways]
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class TireGlowRing : MonoBehaviour
    {
        [SerializeField] TextMesh floatingLabel;
        [SerializeField] int textureSize = 512;
        [SerializeField, Range(0f, 1f)] float ringRadius = 0.7f;
        [SerializeField, Range(0f, 0.15f)] float lineGap = 0.035f;
        [SerializeField, Range(0f, 0.05f)] float lineThickness = 0.01f;
        [SerializeField, Range(0f, 0.3f)] float outerGlowSoftness = 0.08f;
        [SerializeField, Range(0f, 1f)] float ambientFillAlpha = 0.08f;
        [SerializeField] Color offColor = new Color(0.05f, 0.2f, 0.08f, 0.35f);
        [SerializeField] Color onColor = new Color(0.25f, 1f, 0.35f, 0.95f);
        [SerializeField] float fadeDuration = 0.2f;

        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        float progress;
        Tween progressTween;

        void OnEnable()
        {
            EnsureMaterial();
            progress = 0f;
            Apply(progress);
        }

        void OnDisable()
        {
            progressTween?.Kill();
        }

        void EnsureMaterial()
        {
            var renderer = GetComponent<MeshRenderer>();
            if (renderer.sharedMaterial != null && renderer.sharedMaterial.name.StartsWith("M_TireGlowRing"))
                return;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            var ringMaterial = new Material(shader) { name = "M_TireGlowRing (Generated)" };
            ringMaterial.SetFloat("_Surface", 1f);
            ringMaterial.SetFloat("_Blend", 0f);
            ringMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            ringMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            ringMaterial.SetInt("_ZWrite", 0);
            ringMaterial.renderQueue = (int)RenderQueue.Transparent;
            ringMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            ringMaterial.SetTexture(BaseMapId, GenerateRingTexture());

            renderer.sharedMaterial = ringMaterial;
        }

        Texture2D GenerateRingTexture()
        {
            var tex = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false)
            {
                name = "T_TireGlowRing (Generated)",
                wrapMode = TextureWrapMode.Clamp
            };

            Vector2 center = new Vector2(textureSize - 1, textureSize - 1) * 0.5f;
            float maxDist = textureSize * 0.5f;
            var pixels = new Color[textureSize * textureSize];

            float innerLineRadius = ringRadius - lineGap * 0.5f;
            float outerLineRadius = ringRadius + lineGap * 0.5f;

            for (int y = 0; y < textureSize; y++)
            {
                for (int x = 0; x < textureSize; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), center) / maxDist;

                    float innerLine = LineMask(dist, innerLineRadius);
                    float outerLine = LineMask(dist, outerLineRadius);
                    float lineAlpha = Mathf.Max(innerLine, outerLine);

                    float bandIn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(innerLineRadius - outerGlowSoftness, innerLineRadius, dist));
                    float bandOut = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(outerLineRadius, outerLineRadius + outerGlowSoftness, dist));
                    float ambientAlpha = bandIn * bandOut * ambientFillAlpha;

                    float alpha = Mathf.Max(lineAlpha, ambientAlpha);
                    pixels[y * textureSize + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        float LineMask(float dist, float radius)
        {
            float d = Mathf.Abs(dist - radius);
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, lineThickness, d));
        }

        public void SetGlow(bool isOn)
        {
            EnsureMaterial();

            progressTween?.Kill();
            float target = isOn ? 1f : 0f;

            if (!Application.isPlaying)
            {
                progress = target;
                Apply(progress);
                return;
            }

            progressTween = DOTween.To(() => progress, p =>
            {
                progress = p;
                Apply(p);
            }, target, fadeDuration).SetEase(Ease.OutQuad);
        }

        void Apply(float t)
        {
            Color c = Color.Lerp(offColor, onColor, t);
            var renderer = GetComponent<MeshRenderer>();
            if (renderer.sharedMaterial != null)
                renderer.sharedMaterial.SetColor(BaseColorId, c);

            if (floatingLabel != null)
                floatingLabel.color = new Color(onColor.r, onColor.g, onColor.b, t);
        }
    }
}
