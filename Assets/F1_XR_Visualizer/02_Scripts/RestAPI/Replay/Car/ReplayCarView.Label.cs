using UnityEngine;
using static F1XR.RestAPI.Replay.ReplayCarVisualUtil;

namespace F1XR.RestAPI.Replay
{
    public partial class ReplayCarView
    {
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
        private MaterialPropertyBlock labelDotBlock;
        private Color labelColor = Color.white;
        private string driverLabel;
        private int rank;
        private bool labelVisible = true;
        private bool leaderHighlightVisible;
        private bool labelLayoutDirty = true;

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
            if (rank == value)
                return;

            rank = value;
            RefreshLabelText();

            if (rank != 1)
                SetLeaderObjectsActive(false);

            SetLabelObjectsActive(ShouldShowLabel());
        }

        public void SetLabelVisible(bool visible)
        {
            labelVisible = visible;
            SetLabelObjectsActive(ShouldShowLabel());
        }

        public void SetLeaderHighlightVisible(bool visible)
        {
            leaderHighlightVisible = visible;

            if (!leaderHighlightVisible)
                SetLeaderObjectsActive(false);

            SetLabelObjectsActive(ShouldShowLabel());
        }

        public void SetColor(Color color)
        {
            labelColor = color;
            if (label != null)
                label.color = labelColor;

            SetLabelDotColor(labelTopDot);
            SetLabelDotColor(labelBottomDot);
            SetSelectionColor(color);
            ApplyBodyHighlight();
        }

        private void RefreshLabelText()
        {
            if (label == null)
                return;

            string text = rank > 0
                ? $"{rank}  {driverLabel}"
                : driverLabel;

            if (label.text == text)
                return;

            label.text = text;
            labelLayoutDirty = true;
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
            labelTextMaterial = GetLabelTextMaterial(text);
            labelRenderer.sharedMaterial = labelTextMaterial;

            return text;
        }

        private LineRenderer CreateLabelLine()
        {
            GameObject obj = new GameObject("DriverLabelLine");
            obj.transform.SetParent(transform, false);

            LineRenderer line = obj.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.numCapVertices = 4;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            Color lineColor = new Color(1f, 1f, 1f, 0.72f);
            labelLineMaterial = GetLabelLineMaterial();
            line.sharedMaterial = labelLineMaterial;
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

            labelBackgroundMaterial = GetLabelBackgroundMaterial();
            renderer.sharedMaterial = labelBackgroundMaterial;

            return renderer;
        }

        private bool UpdateLabelLayout()
        {
            if (!TryGetCarBounds(out Bounds bounds))
                return false;

            float carSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            float textHeight = carSize * LabelSizeRatio;
            float inheritedScale = MaxAbsComponent(transform.lossyScale);

            label.characterSize = textHeight / (label.fontSize * inheritedScale);
            float lineStartWidth = textHeight * LabelLineWidthRatio;
            float lineEndWidth = textHeight * 0.008f;
            float dotSize = lineStartWidth * 3f;

            Vector3 labelPosition = new Vector3(
                bounds.center.x,
                bounds.max.y + textHeight * LabelGapRatio,
                bounds.center.z
            );
            label.transform.localPosition = transform.InverseTransformPoint(labelPosition);

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
            labelLine.SetPosition(0, transform.InverseTransformPoint(lineStart));
            labelLine.SetPosition(1, transform.InverseTransformPoint(lineEnd));

            SetDot(labelBottomDot, lineStart, dotSize);
            SetDot(labelTopDot, lineEnd, dotSize);

            label.transform.rotation = Camera.main.transform.rotation;

            GetTextBackgroundTransform(
                label,
                labelRenderer,
                LabelBackgroundDepthRatio,
                out Vector3 backgroundPosition,
                out Vector3 backgroundScale);
            labelBackground.transform.localPosition = backgroundPosition;
            labelBackground.transform.localScale = backgroundScale;
            return true;
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

            labelDotMaterial = GetLabelDotMaterial();
            renderer.sharedMaterial = labelDotMaterial;
            SetLabelDotColor(renderer);
            return renderer;
        }

        private void SetLabelDotColor(MeshRenderer renderer)
        {
            if (renderer == null)
                return;

            labelDotBlock ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(labelDotBlock);
            labelDotBlock.SetColor("_BaseColor", labelColor);
            labelDotBlock.SetColor("_Color", labelColor);
            renderer.SetPropertyBlock(labelDotBlock);
        }

        private static void SetDot(MeshRenderer dot, Vector3 position, float worldSize)
        {
            Transform parent = dot.transform.parent;
            dot.transform.localPosition = parent != null
                ? parent.InverseTransformPoint(position)
                : position;
            dot.transform.localScale = ToLocalScale(dot.transform, worldSize);
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

        private bool ShouldShowLabel()
        {
            return labelVisible || hovered || selected || rank == 1;
        }
    }
}
