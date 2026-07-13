using System.Collections.Generic;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public sealed class ReplayCarLabel
    {
        private const float SizeRatio = 2.1f;
        private const float GapRatio = 0.55f;
        private const float LineGapRatio = 0.08f;
        private const float LineWidthRatio = 0.014f;
        private const float BackgroundDepthRatio = 0.03f;

        private readonly Transform owner;

        private TextMesh text;
        private LineRenderer line;
        private MeshRenderer background;
        private MeshRenderer textRenderer;
        private MeshRenderer topDot;
        private MeshRenderer bottomDot;

        private Material textMaterial;
        private Material lineMaterial;
        private Material backgroundMaterial;
        private Material dotMaterial;

        private Color color = Color.white;
        private string labelText;
        private int driverNumber;
        private int rank;
        private bool visible = true;

        public ReplayCarLabel(Transform owner)
        {
            this.owner = owner;
        }

        public Color Color => color;

        public bool ShouldShow(bool selected)
        {
            return visible || selected || rank == 1;
        }

        public void SetDriverNumber(int value)
        {
            driverNumber = value;
            if (string.IsNullOrWhiteSpace(labelText))
                SetText(value.ToString());
        }

        public void SetText(string value)
        {
            EnsureText();

            labelText = string.IsNullOrWhiteSpace(value)
                ? driverNumber.ToString()
                : value;

            RefreshText();
        }

        public void SetRank(int value)
        {
            rank = value;
            RefreshText();
        }

        public void SetVisible(bool value, bool selected)
        {
            visible = value;
            SetActive(ShouldShow(selected));
        }

        public void SetColor(Color value)
        {
            color = value;

            if (text != null)
                text.color = color;

            SetMaterialColor(textMaterial, color);
            SetMaterialColor(dotMaterial, color);
        }

        public void CollectRenderers(List<Renderer> renderers)
        {
            if (renderers == null)
                return;

            AddRenderer(renderers, textRenderer);
            AddRenderer(renderers, line);
            AddRenderer(renderers, background);
            AddRenderer(renderers, topDot);
            AddRenderer(renderers, bottomDot);
        }

        public bool Owns(Renderer renderer)
        {
            if (renderer == null)
                return false;

            return text != null && renderer.gameObject == text.gameObject ||
                background != null && renderer.gameObject == background.gameObject ||
                line != null && renderer.gameObject == line.gameObject ||
                topDot != null && renderer.gameObject == topDot.gameObject ||
                bottomDot != null && renderer.gameObject == bottomDot.gameObject;
        }

        public void UpdateLayout(Bounds bounds)
        {
            if (!ShouldShow(selected: false) && text == null)
                return;

            Camera viewCamera = ResolveViewCamera();
            if (text == null || viewCamera == null)
                return;

            line ??= CreateLine();
            background ??= CreateBackground();
            topDot ??= CreateDot("DriverLabelTopDot");
            bottomDot ??= CreateDot("DriverLabelBottomDot");

            float carSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            float textHeight = carSize * SizeRatio;
            float inheritedScale = Mathf.Max(
                0.0001f,
                Mathf.Max(
                    Mathf.Abs(owner.lossyScale.x),
                    Mathf.Max(Mathf.Abs(owner.lossyScale.y), Mathf.Abs(owner.lossyScale.z))
                )
            );

            text.characterSize = textHeight / (text.fontSize * inheritedScale);
            float lineStartWidth = textHeight * LineWidthRatio;
            float lineEndWidth = textHeight * 0.008f;
            float dotSize = lineStartWidth * 3f;

            Vector3 labelPosition = new Vector3(
                bounds.center.x,
                bounds.max.y + textHeight * GapRatio,
                bounds.center.z
            );
            text.transform.localPosition = owner.InverseTransformPoint(labelPosition);

            Vector3 lineStart = new Vector3(
                bounds.center.x,
                bounds.max.y + dotSize * 0.8f,
                bounds.center.z
            );
            Vector3 lineEnd = new Vector3(
                bounds.center.x,
                labelPosition.y - textHeight * LineGapRatio,
                bounds.center.z
            );

            line.startWidth = lineStartWidth;
            line.endWidth = lineEndWidth;
            line.SetPosition(0, owner.InverseTransformPoint(lineStart));
            line.SetPosition(1, owner.InverseTransformPoint(lineEnd));

            SetDot(bottomDot, lineStart, dotSize);
            SetDot(topDot, lineEnd, dotSize);

            text.transform.rotation = viewCamera.transform.rotation;

            Bounds textBounds = textRenderer != null ? textRenderer.localBounds : default;
            float fallbackHeight = Mathf.Max(0.0001f, text.characterSize * text.fontSize);
            float textWidth = textBounds.size.x > 0f
                ? textBounds.size.x
                : fallbackHeight * Mathf.Max(1.2f, text.text.Length * 0.42f);
            float textLocalHeight = textBounds.size.y > 0f
                ? textBounds.size.y
                : fallbackHeight * 0.82f;
            float horizontalPadding = textLocalHeight * 0.16f;
            float verticalPadding = textLocalHeight * 0.12f;
            float labelWidth = textWidth + horizontalPadding * 2f;
            float labelHeight = textLocalHeight + verticalPadding * 2f;

            background.transform.localPosition = new Vector3(
                textBounds.center.x,
                textBounds.center.y,
                -textLocalHeight * BackgroundDepthRatio
            );
            background.transform.localScale = new Vector3(labelWidth, labelHeight, 1f);
        }

        public void LookAtCamera()
        {
            Camera viewCamera = ResolveViewCamera();
            if (text != null && viewCamera != null)
                text.transform.rotation = viewCamera.transform.rotation;
        }

        public void SetActive(bool active)
        {
            if (text != null)
                text.gameObject.SetActive(active);

            if (line != null)
                line.gameObject.SetActive(active);

            if (background != null)
                background.gameObject.SetActive(active);

            if (topDot != null)
                topDot.gameObject.SetActive(active);

            if (bottomDot != null)
                bottomDot.gameObject.SetActive(active);
        }

        public void Dispose()
        {
            DestroyMaterial(textMaterial);
            DestroyMaterial(lineMaterial);
            DestroyMaterial(backgroundMaterial);
            DestroyMaterial(dotMaterial);
        }

        private void EnsureText()
        {
            if (text != null)
                return;

            GameObject obj = new GameObject("DriverLabel");
            obj.transform.SetParent(owner, false);
            obj.transform.localRotation = Quaternion.identity;
            obj.transform.localScale = Vector3.one;

            text = obj.AddComponent<TextMesh>();
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = 32;
            text.characterSize = 0.01f;
            text.color = color;
            text.text = driverNumber.ToString();

            textRenderer = obj.GetComponent<MeshRenderer>();
            textRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            textRenderer.receiveShadows = false;
            textMaterial = CreateTextMaterial(text, color);
            textRenderer.material = textMaterial;
        }

        private void RefreshText()
        {
            if (text == null)
                return;

            text.text = rank > 0
                ? $"{rank}  {labelText}"
                : labelText;
        }

        private LineRenderer CreateLine()
        {
            GameObject obj = new GameObject("DriverLabelLine");
            obj.transform.SetParent(owner, false);

            LineRenderer result = obj.AddComponent<LineRenderer>();
            result.useWorldSpace = false;
            result.positionCount = 2;
            result.numCapVertices = 4;
            result.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            result.receiveShadows = false;

            Color lineColor = new Color(1f, 1f, 1f, 0.72f);
            lineMaterial = CreateUnlitMaterial(lineColor);
            result.material = lineMaterial;
            result.startColor = lineColor;
            result.endColor = new Color(1f, 1f, 1f, 0.42f);

            return result;
        }

        private MeshRenderer CreateBackground()
        {
            GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            obj.name = "DriverLabelBackground";
            obj.transform.SetParent(text.transform, false);
            obj.transform.localRotation = Quaternion.identity;

            Collider collider = obj.GetComponent<Collider>();
            if (collider != null)
                Object.Destroy(collider);

            MeshRenderer result = obj.GetComponent<MeshRenderer>();
            result.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            result.receiveShadows = false;

            backgroundMaterial = CreateUnlitMaterial(new Color(0f, 0f, 0f, 0.34f));
            backgroundMaterial.renderQueue = 2990;
            result.material = backgroundMaterial;

            return result;
        }

        private MeshRenderer CreateDot(string objectName)
        {
            GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            obj.name = objectName;
            obj.transform.SetParent(owner, false);

            Collider collider = obj.GetComponent<Collider>();
            if (collider != null)
                Object.Destroy(collider);

            MeshRenderer result = obj.GetComponent<MeshRenderer>();
            result.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            result.receiveShadows = false;

            dotMaterial ??= CreateUnlitMaterial(color);
            result.material = dotMaterial;
            return result;
        }

        private void SetDot(MeshRenderer dot, Vector3 position, float worldSize)
        {
            Transform parent = dot.transform.parent;
            dot.transform.localPosition = parent != null
                ? parent.InverseTransformPoint(position)
                : position;
            dot.transform.localScale = ToLocalScale(dot.transform, worldSize);
        }

        private static Vector3 ToLocalScale(Transform target, float worldSize)
        {
            Transform parent = target.parent;
            if (parent == null)
                return Vector3.one * worldSize;

            Vector3 scale = parent.lossyScale;
            return new Vector3(
                worldSize / Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
                worldSize / Mathf.Max(0.0001f, Mathf.Abs(scale.y)),
                worldSize / Mathf.Max(0.0001f, Mathf.Abs(scale.z))
            );
        }

        private static Material CreateUnlitMaterial(Color color)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");

            Material material = new Material(shader);
            SetMaterialColor(material, color);

            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);

            if (material.HasProperty("_SrcBlend"))
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);

            if (material.HasProperty("_DstBlend"))
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

            if (material.HasProperty("_ZWrite"))
                material.SetFloat("_ZWrite", 0f);

            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 0f);

            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0f);

            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness", 0f);

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = 3000;
            return material;
        }

        private static Material CreateTextMaterial(TextMesh source, Color color)
        {
            Shader shader = Shader.Find("GUI/Text Shader");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            Material material = new Material(shader);
            if (source.font != null && source.font.material != null)
                material.mainTexture = source.font.material.mainTexture;

            SetMaterialColor(material, color);
            material.renderQueue = 3000;
            return material;
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material == null)
                return;

            material.color = color;

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);

            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
        }

        private static void AddRenderer(List<Renderer> renderers, Renderer renderer)
        {
            if (renderer != null)
                renderers.Add(renderer);
        }

        private static Camera ResolveViewCamera()
        {
            if (Camera.main != null)
                return Camera.main;

            if (Camera.current != null)
                return Camera.current;

            return Object.FindAnyObjectByType<Camera>();
        }

        private static void DestroyMaterial(Material material)
        {
            if (material != null)
                Object.Destroy(material);
        }
    }
}
