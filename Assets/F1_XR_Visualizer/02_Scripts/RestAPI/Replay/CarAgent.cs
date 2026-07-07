using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public class CarAgent : MonoBehaviour
    {
        public int driverNumber;
        public Vector3 rawPosition;

        private const float LabelSizeRatio = 2.1f;
        private const float LabelGapRatio = 0.55f;
        private const float LabelLineGapRatio = 0.08f;
        private const float LabelLineWidthRatio = 0.014f;
        private const float LabelBackgroundDepthRatio = 0.03f;
        private TextMesh label;
        private LineRenderer labelLine;
        private MeshRenderer labelBackground;
        private MeshRenderer labelRenderer;
        private MeshRenderer labelTopDot;
        private MeshRenderer labelBottomDot;
        private Material labelTextMaterial;
        private Material labelLineMaterial;
        private Material labelBackgroundMaterial;
        private Material labelDotMaterial;
        private Color labelColor = Color.white;
        private string driverLabel;
        private int rank;
        private bool labelVisible = true;

        public void Init(int number)
        {
            driverNumber = number;
            name = $"Car_{number}";
            SetLabel(number.ToString());
        }

        public void SetPosition(Vector3 position)
        {
            rawPosition = position;
            transform.position = position;
        }

        public void SetLocalPosition(Vector3 position)
        {
            rawPosition = position;
            transform.localPosition = position;
        }

        public void SetLabel(string text)
        {
            if (label == null)
                label = CreateLabel();

            driverLabel = string.IsNullOrWhiteSpace(text)
                ? driverNumber.ToString()
                : text;

            RefreshLabelText();
        }

        public void SetRank(int value)
        {
            rank = value;
            RefreshLabelText();
        }

        public void SetLabelVisible(bool visible)
        {
            labelVisible = visible;
            SetLabelObjectsActive(visible);
        }

        private void RefreshLabelText()
        {
            if (label == null)
                return;

            label.text = rank > 0
                ? $"{rank}  {driverLabel}"
                : driverLabel;
        }
        
        public void SetColor(Color color)
        {
            labelColor = color;
            if (label != null)
                label.color = labelColor;

            SetMaterialColor(labelTextMaterial, labelColor);
            SetMaterialColor(labelDotMaterial, labelColor);

            Renderer[] renderers = GetComponentsInChildren<Renderer>();

            foreach (Renderer item in renderers)
            {
                if (label != null && item.gameObject == label.gameObject)
                    continue;

                if (labelBackground != null && item.gameObject == labelBackground.gameObject)
                    continue;

                if (labelLine != null && item.gameObject == labelLine.gameObject)
                    continue;

                if (labelTopDot != null && item.gameObject == labelTopDot.gameObject)
                    continue;

                if (labelBottomDot != null && item.gameObject == labelBottomDot.gameObject)
                    continue;

                MaterialPropertyBlock block = new();
                item.GetPropertyBlock(block);
                block.SetColor("_BaseColor", color);
                block.SetColor("_Color", color);
                item.SetPropertyBlock(block);
            }
        }

        private TextMesh CreateLabel()
        {
            GameObject obj = new GameObject("DriverLabel");
            obj.transform.SetParent(transform, false);
            obj.transform.localRotation = Quaternion.identity;
            obj.transform.localScale = Vector3.one;

            TextMesh text = obj.AddComponent<TextMesh>();
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = 32;
            text.characterSize = 0.01f;
            text.color = labelColor;
            text.text = driverNumber.ToString();
            labelRenderer = obj.GetComponent<MeshRenderer>();
            labelRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            labelRenderer.receiveShadows = false;
            labelTextMaterial = CreateTextMaterial(text, labelColor);
            labelRenderer.material = labelTextMaterial;

            return text;
        }

        private LineRenderer CreateLabelLine()
        {
            GameObject obj = new GameObject("DriverLabelLine");
            obj.transform.SetParent(transform, false);

            LineRenderer line = obj.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.numCapVertices = 4;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            Color lineColor = new Color(1f, 1f, 1f, 0.72f);
            labelLineMaterial = CreateUnlitMaterial(lineColor);
            line.material = labelLineMaterial;
            line.startColor = lineColor;
            line.endColor = new Color(1f, 1f, 1f, 0.42f);

            return line;
        }

        private MeshRenderer CreateLabelBackground()
        {
            GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            obj.name = "DriverLabelBackground";
            obj.transform.SetParent(label.transform, false);
            obj.transform.localRotation = Quaternion.identity;

            Collider collider = obj.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            MeshRenderer renderer = obj.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            labelBackgroundMaterial = CreateUnlitMaterial(new Color(0f, 0f, 0f, 0.34f));
            labelBackgroundMaterial.renderQueue = 2990;
            renderer.material = labelBackgroundMaterial;

            return renderer;
        }

        private static Material CreateUnlitMaterial(Color color)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");

            Material material = new Material(shader);
            material.color = color;

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);

            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);

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

        private static Material CreateTextMaterial(TextMesh text, Color color)
        {
            Shader shader = Shader.Find("GUI/Text Shader");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            Material material = new Material(shader);
            if (text.font != null && text.font.material != null)
                material.mainTexture = text.font.material.mainTexture;

            SetMaterialColor(material, color);
            material.renderQueue = 3000;
            return material;
        }

        private void OnDestroy()
        {
            if (labelTextMaterial != null)
                Destroy(labelTextMaterial);

            if (labelLineMaterial != null)
                Destroy(labelLineMaterial);

            if (labelBackgroundMaterial != null)
                Destroy(labelBackgroundMaterial);

            if (labelDotMaterial != null)
                Destroy(labelDotMaterial);
        }

        private void LateUpdate()
        {
            if (!labelVisible || label == null || Camera.main == null)
                return;

            labelLine ??= CreateLabelLine();
            labelBackground ??= CreateLabelBackground();
            labelTopDot ??= CreateLabelDot("DriverLabelTopDot");
            labelBottomDot ??= CreateLabelDot("DriverLabelBottomDot");

            UpdateLabelLayout();
            label.transform.rotation = Camera.main.transform.rotation;
        }

        private void UpdateLabelLayout()
        {
            if (!TryGetCarBounds(out Bounds bounds))
                return;

            float carSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            float textHeight = carSize * LabelSizeRatio;
            float inheritedScale = Mathf.Max(
                0.0001f,
                Mathf.Max(
                    Mathf.Abs(transform.lossyScale.x),
                    Mathf.Max(Mathf.Abs(transform.lossyScale.y), Mathf.Abs(transform.lossyScale.z))
                )
            );

            label.characterSize = textHeight / (label.fontSize * inheritedScale);
            float lineStartWidth = textHeight * LabelLineWidthRatio;
            float lineEndWidth = textHeight * 0.008f;
            float dotSize = lineStartWidth * 3f;

            Vector3 labelPosition = new Vector3(
                bounds.center.x,
                bounds.max.y + textHeight * LabelGapRatio,
                bounds.center.z
            );
            label.transform.position = labelPosition;

            Vector3 lineStart = new Vector3(
                bounds.center.x,
                bounds.max.y + dotSize * 0.8f,
                bounds.center.z
            );
            Vector3 lineEnd = new Vector3(
                bounds.center.x,
                labelPosition.y - textHeight * LabelLineGapRatio,
                bounds.center.z
            );

            labelLine.startWidth = lineStartWidth;
            labelLine.endWidth = lineEndWidth;
            labelLine.SetPosition(0, lineStart);
            labelLine.SetPosition(1, lineEnd);

            SetDot(labelBottomDot, lineStart, dotSize);
            SetDot(labelTopDot, lineEnd, dotSize);

            label.transform.rotation = Camera.main.transform.rotation;

            Bounds textBounds = labelRenderer != null ? labelRenderer.localBounds : default;
            float fallbackHeight = Mathf.Max(0.0001f, label.characterSize * label.fontSize);
            float textWidth = textBounds.size.x > 0f
                ? textBounds.size.x
                : fallbackHeight * Mathf.Max(1.2f, label.text.Length * 0.42f);
            float textLocalHeight = textBounds.size.y > 0f
                ? textBounds.size.y
                : fallbackHeight * 0.82f;
            float horizontalPadding = textLocalHeight * 0.16f;
            float verticalPadding = textLocalHeight * 0.12f;
            float labelWidth = textWidth + horizontalPadding * 2f;
            float labelHeight = textLocalHeight + verticalPadding * 2f;

            labelBackground.transform.localPosition = new Vector3(
                textBounds.center.x,
                textBounds.center.y,
                -textLocalHeight * LabelBackgroundDepthRatio
            );
            labelBackground.transform.localScale = new Vector3(labelWidth, labelHeight, 1f);
        }

        private static Vector3 ToLocalScale(Transform target, float worldWidth, float worldHeight)
        {
            Transform parent = target.parent;
            if (parent == null)
                return new Vector3(worldWidth, worldHeight, 1f);

            Vector3 scale = parent.lossyScale;
            return new Vector3(
                worldWidth / Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
                worldHeight / Mathf.Max(0.0001f, Mathf.Abs(scale.y)),
                1f
            );
        }

        private MeshRenderer CreateLabelDot(string objectName)
        {
            GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            obj.name = objectName;
            obj.transform.SetParent(transform, false);

            Collider collider = obj.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            MeshRenderer renderer = obj.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            labelDotMaterial ??= CreateUnlitMaterial(labelColor);
            renderer.material = labelDotMaterial;
            return renderer;
        }

        private static void SetDot(MeshRenderer dot, Vector3 position, float worldSize)
        {
            dot.transform.position = position;
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

        private void SetLabelObjectsActive(bool active)
        {
            if (label != null)
                label.gameObject.SetActive(active);

            if (labelLine != null)
                labelLine.gameObject.SetActive(active);

            if (labelBackground != null)
                labelBackground.gameObject.SetActive(active);

            if (labelTopDot != null)
                labelTopDot.gameObject.SetActive(active);

            if (labelBottomDot != null)
                labelBottomDot.gameObject.SetActive(active);
        }

        private bool TryGetCarBounds(out Bounds bounds)
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            bounds = default;
            bool hasBounds = false;

            foreach (Renderer item in renderers)
            {
                if (label != null && item.gameObject == label.gameObject)
                    continue;

                if (labelBackground != null && item.gameObject == labelBackground.gameObject)
                    continue;

                if (labelLine != null && item.gameObject == labelLine.gameObject)
                    continue;

                if (labelTopDot != null && item.gameObject == labelTopDot.gameObject)
                    continue;

                if (labelBottomDot != null && item.gameObject == labelBottomDot.gameObject)
                    continue;

                if (!hasBounds)
                {
                    bounds = item.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(item.bounds);
                }
            }

            return hasBounds;
        }
    }
}
