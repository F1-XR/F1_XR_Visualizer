using System.Collections.Generic;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public class ReplayCarView : MonoBehaviour
    {
        public int driverNumber;
        public Vector3 rawPosition;

        private const float LabelSizeRatio = 2.1f;
        private const float LabelGapRatio = 0.55f;
        private const float LabelLineGapRatio = 0.08f;
        private const float LabelLineWidthRatio = 0.014f;
        private const float LabelBackgroundDepthRatio = 0.03f;
        private const float SelectionRingHeightRatio = 0.02f;
        private const float SelectionPulseHeightRatio = 0.026f;
        private const float SelectionRingOuterRatio = 0.95f;
        private const float SelectionPulseOuterRatio = 1.8f;
        private const float SelectionRingInnerRatio = 0.56f;
        private const float SelectionPulseInnerRatio = 0.72f;
        private const float SelectionRingAlpha = 0.88f;
        private const float SelectionPulseAlpha = 0.82f;
        private const float SelectionRingRotationSpeed = 32f;
        private const float SelectionPulseDuration = 0.58f;
        private const float LeaderRingHeightRatio = 0.035f;
        private const float LeaderRingOuterRatio = 1.28f;
        private const float LeaderRingInnerRatio = 0.76f;
        private const float LeaderRingAlpha = 0.34f;
        private const float LeaderRingPulseAlpha = 0.12f;
        private const float LeaderRingRotationSpeed = -18f;
        private const float SelectionBodyTint = 0.48f;
        private const float SelectionBodyEmission = 0.9f;
        private const int SelectionRingSegments = 96;
        private static readonly Color LeaderFxColor = new Color(1f, 0.78f, 0.12f, 1f);
        private static readonly bool TintCarBody = false;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
        private static readonly int CullId = Shader.PropertyToID("_Cull");
        private TextMesh label;
        private LineRenderer labelLine;
        private MeshRenderer labelBackground;
        private MeshRenderer labelRenderer;
        private MeshRenderer labelTopDot;
        private MeshRenderer labelBottomDot;
        private Transform selectionRoot;
        private Transform leaderRoot;
        private MeshRenderer selectionRing;
        private MeshRenderer selectionPulse;
        private MeshRenderer leaderRing;
        private Material labelTextMaterial;
        private Material labelLineMaterial;
        private Material labelBackgroundMaterial;
        private Material labelDotMaterial;
        private Material selectionRingMaterial;
        private Material selectionPulseMaterial;
        private Material leaderRingMaterial;
        private Mesh selectionRingMesh;
        private Mesh selectionPulseMesh;
        private Mesh leaderRingMesh;
        private Vector3[] selectionRingVertices;
        private Vector3[] selectionPulseVertices;
        private Vector3[] leaderRingVertices;
        private readonly List<Renderer> bodyRenderers = new();
        private readonly Dictionary<Renderer, MaterialPropertyBlock> bodyBlocks = new();
        private Color labelColor = Color.white;
        private Color selectionColor = Color.white;
        private string driverLabel;
        private int rank;
        private float leaderAge;
        private float selectionAge;
        private float selectionPulseAge = SelectionPulseDuration;
        private bool labelVisible = true;
        private bool selected;
        private bool leaderHighlightVisible;
        private bool bodyRenderersDirty = true;

        public void Init(int number)
        {
            driverNumber = number;
            name = $"Car_{number}";
            bodyRenderersDirty = true;
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

        public void CollectOnboardHiddenRenderers(List<Renderer> renderers)
        {
            if (renderers == null)
                return;

            AddRenderer(renderers, labelRenderer);
            AddRenderer(renderers, labelLine);
            AddRenderer(renderers, labelBackground);
            AddRenderer(renderers, labelTopDot);
            AddRenderer(renderers, labelBottomDot);
            AddRenderer(renderers, selectionRing);
            AddRenderer(renderers, selectionPulse);
            AddRenderer(renderers, leaderRing);
        }

        public void SetSelected(bool value)
        {
            SetSelected(value, labelColor);
        }

        public void SetSelected(bool value, Color color)
        {
            if (selected == value)
            {
                SetSelectionColor(color);
                SetLabelObjectsActive(ShouldShowLabel());
                ApplyBodyHighlight();
                return;
            }

            SetSelectionColor(color);
            selected = value;

            if (selected)
                selectionPulseAge = 0f;

            SetSelectionObjectsActive(false);
            SetLabelObjectsActive(ShouldShowLabel());
            ApplyBodyHighlight();
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
            SetSelectionColor(color);
            ApplyBodyHighlight();
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
            line.useWorldSpace = false;
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

        private static Material CreateSelectionMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            Material material = new Material(shader);
            material.name = "Runtime_SelectedCarFx";
            SetMaterialColor(material, color);

            if (material.HasProperty(SurfaceId))
                material.SetFloat(SurfaceId, 1f);

            if (material.HasProperty(SrcBlendId))
                material.SetFloat(SrcBlendId, (float)UnityEngine.Rendering.BlendMode.SrcAlpha);

            if (material.HasProperty(DstBlendId))
                material.SetFloat(DstBlendId, (float)UnityEngine.Rendering.BlendMode.One);

            if (material.HasProperty(ZWriteId))
                material.SetFloat(ZWriteId, 0f);

            if (material.HasProperty(CullId))
                material.SetFloat(CullId, (float)UnityEngine.Rendering.CullMode.Off);

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = 3100;
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

            if (selectionRingMaterial != null)
                Destroy(selectionRingMaterial);

            if (selectionPulseMaterial != null)
                Destroy(selectionPulseMaterial);

            if (selectionRingMesh != null)
                Destroy(selectionRingMesh);

            if (selectionPulseMesh != null)
                Destroy(selectionPulseMesh);

            if (leaderRingMaterial != null)
                Destroy(leaderRingMaterial);

            if (leaderRingMesh != null)
                Destroy(leaderRingMesh);
        }

        private void LateUpdate()
        {
            if (selected)
                UpdateSelectionEffect();

            if (leaderHighlightVisible && rank == 1)
                UpdateLeaderEffect();

            if (!ShouldShowLabel() || label == null || Camera.main == null)
                return;

            labelLine ??= CreateLabelLine();
            labelBackground ??= CreateLabelBackground();
            labelTopDot ??= CreateLabelDot("DriverLabelTopDot");
            labelBottomDot ??= CreateLabelDot("DriverLabelBottomDot");

            UpdateLabelLayout();
            label.transform.rotation = Camera.main.transform.rotation;
        }

        private void EnsureSelectionEffect()
        {
            bool created = false;
            selectionRingMaterial ??= CreateSelectionMaterial(WithAlpha(CurrentSelectionFxColor(), SelectionRingAlpha));
            selectionPulseMaterial ??= CreateSelectionMaterial(WithAlpha(CurrentSelectionFxColor(), SelectionPulseAlpha));

            if (selectionRoot == null)
            {
                GameObject root = new GameObject("SelectionFx");
                root.transform.SetParent(transform, false);
                selectionRoot = root.transform;
                created = true;
            }

            if (selectionRing == null)
            {
                GameObject ring = new GameObject("GroundRing");
                ring.transform.SetParent(selectionRoot, false);
                MeshFilter ringFilter = ring.AddComponent<MeshFilter>();
                selectionRing = ring.AddComponent<MeshRenderer>();
                selectionRing.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                selectionRing.receiveShadows = false;
                selectionRing.material = selectionRingMaterial;
                selectionRingMesh = CreateRingMesh("SelectedCarGroundRing", out selectionRingVertices);
                ringFilter.sharedMesh = selectionRingMesh;
                created = true;
            }

            if (selectionPulse == null)
            {
                GameObject pulse = new GameObject("SelectionPulse");
                pulse.transform.SetParent(selectionRoot, false);
                MeshFilter pulseFilter = pulse.AddComponent<MeshFilter>();
                selectionPulse = pulse.AddComponent<MeshRenderer>();
                selectionPulse.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                selectionPulse.receiveShadows = false;
                selectionPulse.material = selectionPulseMaterial;
                selectionPulseMesh = CreateRingMesh("SelectedCarPulse", out selectionPulseVertices);
                pulseFilter.sharedMesh = selectionPulseMesh;
                pulse.SetActive(false);
                created = true;
            }

            if (created)
                bodyRenderersDirty = true;

            ApplySelectionColor();
        }

        private void EnsureLeaderEffect()
        {
            bool created = false;
            leaderRingMaterial ??= CreateSelectionMaterial(WithAlpha(LeaderFxColor, LeaderRingAlpha));

            if (leaderRoot == null)
            {
                GameObject root = new GameObject("RaceLeaderFx");
                root.transform.SetParent(transform, false);
                leaderRoot = root.transform;
                created = true;
            }

            if (leaderRing == null)
            {
                GameObject ring = new GameObject("LeaderGroundRing");
                ring.transform.SetParent(leaderRoot, false);
                MeshFilter ringFilter = ring.AddComponent<MeshFilter>();
                leaderRing = ring.AddComponent<MeshRenderer>();
                leaderRing.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                leaderRing.receiveShadows = false;
                leaderRing.material = leaderRingMaterial;
                leaderRingMesh = CreateRingMesh("RaceLeaderGroundRing", out leaderRingVertices);
                ringFilter.sharedMesh = leaderRingMesh;
                created = true;
            }

            if (created)
                bodyRenderersDirty = true;
        }

        private void UpdateLeaderEffect()
        {
            EnsureLeaderEffect();
            SetLeaderObjectsActive(true);

            if (!TryGetCarBounds(out Bounds bounds))
                return;

            leaderAge += Time.deltaTime;
            float radius = Mathf.Max(bounds.size.x, bounds.size.z) * LeaderRingOuterRatio;
            float alpha = LeaderRingAlpha + Mathf.Sin(leaderAge * Mathf.PI * 2f) * LeaderRingPulseAlpha;
            Vector3 worldCenter = new Vector3(
                bounds.center.x,
                bounds.min.y + Mathf.Max(radius * LeaderRingHeightRatio, 0.0012f),
                bounds.center.z
            );
            Vector3 localCenter = transform.InverseTransformPoint(worldCenter);

            SetMaterialColor(leaderRingMaterial, WithAlpha(LeaderFxColor, alpha));
            UpdateRingMesh(
                leaderRingMesh,
                leaderRingVertices,
                localCenter,
                radius,
                LeaderRingInnerRatio,
                leaderAge * LeaderRingRotationSpeed
            );
        }

        private void SetSelectionColor(Color color)
        {
            selectionColor = color;
            ApplySelectionColor();
        }

        private void ApplySelectionColor()
        {
            SetMaterialColor(selectionRingMaterial, WithAlpha(CurrentSelectionFxColor(), SelectionRingAlpha));
            SetMaterialColor(selectionPulseMaterial, WithAlpha(CurrentSelectionFxColor(), SelectionPulseAlpha));
        }

        private void UpdateSelectionEffect()
        {
            EnsureSelectionEffect();
            SetSelectionObjectsActive(true);
            ApplyBodyHighlight();

            if (!TryGetCarBounds(out Bounds bounds))
                return;

            selectionAge += Time.deltaTime;
            float radius = Mathf.Max(bounds.size.x, bounds.size.z) * SelectionRingOuterRatio;
            Vector3 worldCenter = new Vector3(
                bounds.center.x,
                bounds.min.y + Mathf.Max(radius * SelectionRingHeightRatio, 0.001f),
                bounds.center.z
            );
            Vector3 localCenter = transform.InverseTransformPoint(worldCenter);

            UpdateRingMesh(
                selectionRingMesh,
                selectionRingVertices,
                localCenter,
                radius,
                SelectionRingInnerRatio,
                selectionAge * SelectionRingRotationSpeed
            );
            UpdateSelectionPulse(localCenter, radius);
        }

        private void UpdateSelectionPulse(Vector3 localCenter, float radius)
        {
            if (selectionPulse == null)
                return;

            if (selectionPulseAge >= SelectionPulseDuration)
            {
                selectionPulse.gameObject.SetActive(false);
                return;
            }

            selectionPulse.gameObject.SetActive(true);

            float t = Mathf.Clamp01(selectionPulseAge / SelectionPulseDuration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            float pulseRadius = radius * Mathf.Lerp(0.78f, SelectionPulseOuterRatio, eased);
            float pulseAlpha = Mathf.Lerp(SelectionPulseAlpha, 0f, t);
            Color color = CurrentSelectionFxColor();
            SetMaterialColor(selectionPulseMaterial, WithAlpha(color, pulseAlpha));
            Vector3 pulseCenter = localCenter + Vector3.up * LocalDistance(Mathf.Max(radius * SelectionPulseHeightRatio, 0.0008f));
            UpdateRingMesh(selectionPulseMesh, selectionPulseVertices, pulseCenter, pulseRadius, SelectionPulseInnerRatio, -selectionAge * SelectionRingRotationSpeed * 0.65f);
            selectionPulseAge += Time.deltaTime;
        }

        private float LocalDistance(float worldDistance)
        {
            return worldDistance / Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.y));
        }

        private Color CurrentSelectionFxColor()
        {
            return selectionColor;
        }

        private void ApplyBodyHighlight()
        {
            if (!TintCarBody)
                return;

            RefreshBodyRenderers();

            Color fxColor = CurrentSelectionFxColor();
            Color bodyColor = selected
                ? Color.Lerp(labelColor, fxColor, SelectionBodyTint)
                : labelColor;
            Color emissionColor = selected
                ? WithAlpha(fxColor * SelectionBodyEmission, 1f)
                : Color.black;

            foreach (Renderer item in bodyRenderers)
            {
                if (item == null)
                    continue;

                MaterialPropertyBlock block = BodyBlock(item);
                item.GetPropertyBlock(block);
                block.SetColor(BaseColorId, bodyColor);
                block.SetColor(ColorId, bodyColor);
                block.SetColor(EmissionColorId, emissionColor);
                item.SetPropertyBlock(block);
            }
        }

        private void RefreshBodyRenderers()
        {
            if (!bodyRenderersDirty)
                return;

            bodyRenderers.Clear();
            bodyBlocks.Clear();

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            foreach (Renderer item in renderers)
            {
                if (item == null || IsIgnoredRenderer(item))
                    continue;

                bodyRenderers.Add(item);
            }

            bodyRenderersDirty = false;
        }

        private MaterialPropertyBlock BodyBlock(Renderer renderer)
        {
            if (!bodyBlocks.TryGetValue(renderer, out MaterialPropertyBlock block) || block == null)
            {
                block = new MaterialPropertyBlock();
                bodyBlocks[renderer] = block;
            }

            return block;
        }

        private bool IsIgnoredRenderer(Renderer renderer)
        {
            return label != null && renderer.gameObject == label.gameObject ||
                labelBackground != null && renderer.gameObject == labelBackground.gameObject ||
                labelLine != null && renderer.gameObject == labelLine.gameObject ||
                labelTopDot != null && renderer.gameObject == labelTopDot.gameObject ||
                labelBottomDot != null && renderer.gameObject == labelBottomDot.gameObject ||
                IsSelectionEffectRenderer(renderer) ||
                IsLeaderEffectRenderer(renderer);
        }

        private static Mesh CreateRingMesh(string meshName, out Vector3[] vertices)
        {
            Mesh mesh = new Mesh { name = meshName };
            vertices = new Vector3[SelectionRingSegments * 2];
            Vector2[] uvs = new Vector2[vertices.Length];
            Color[] colors = new Color[vertices.Length];
            int[] triangles = new int[SelectionRingSegments * 6];

            for (int i = 0; i < SelectionRingSegments; i++)
            {
                float angle = i / (float)SelectionRingSegments * Mathf.PI * 2f;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);
                int inner = i * 2;
                int outer = inner + 1;

                vertices[inner] = new Vector3(cos * 0.72f, 0f, sin * 0.72f);
                vertices[outer] = new Vector3(cos, 0f, sin);
                uvs[inner] = new Vector2(0f, i / (float)SelectionRingSegments);
                uvs[outer] = new Vector2(1f, i / (float)SelectionRingSegments);
                colors[inner] = new Color(1f, 1f, 1f, 0.24f);
                colors[outer] = Color.white;

                int nextInner = (i + 1) % SelectionRingSegments * 2;
                int nextOuter = nextInner + 1;
                int triangle = i * 6;
                triangles[triangle] = inner;
                triangles[triangle + 1] = outer;
                triangles[triangle + 2] = nextOuter;
                triangles[triangle + 3] = inner;
                triangles[triangle + 4] = nextOuter;
                triangles[triangle + 5] = nextInner;
            }

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void UpdateRingMesh(Mesh mesh, Vector3[] vertices, Vector3 localCenter, float worldOuterRadius, float innerRatio, float yawDegrees)
        {
            if (mesh == null || vertices == null)
                return;

            Vector3 scale = transform.lossyScale;
            float outerX = worldOuterRadius / Mathf.Max(0.0001f, Mathf.Abs(scale.x));
            float outerZ = worldOuterRadius / Mathf.Max(0.0001f, Mathf.Abs(scale.z));
            float innerX = outerX * innerRatio;
            float innerZ = outerZ * innerRatio;
            float yaw = yawDegrees * Mathf.Deg2Rad;

            for (int i = 0; i < SelectionRingSegments; i++)
            {
                float angle = i / (float)SelectionRingSegments * Mathf.PI * 2f + yaw;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);
                int inner = i * 2;
                int outer = inner + 1;

                vertices[inner] = localCenter + new Vector3(cos * innerX, 0f, sin * innerZ);
                vertices[outer] = localCenter + new Vector3(cos * outerX, 0f, sin * outerZ);
            }

            mesh.vertices = vertices;
            mesh.RecalculateBounds();
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

        private static Vector3 ToLocalScale(Transform target, float worldWidth, float worldHeight, float worldDepth)
        {
            Transform parent = target.parent;
            if (parent == null)
                return new Vector3(worldWidth, worldHeight, worldDepth);

            Vector3 scale = parent.lossyScale;
            return new Vector3(
                worldWidth / Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
                worldHeight / Mathf.Max(0.0001f, Mathf.Abs(scale.y)),
                worldDepth / Mathf.Max(0.0001f, Mathf.Abs(scale.z))
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

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
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
            return labelVisible || selected || rank == 1;
        }

        private void SetSelectionObjectsActive(bool active)
        {
            if (selectionRoot != null)
                selectionRoot.gameObject.SetActive(active);

            if (selectionRing != null)
                selectionRing.gameObject.SetActive(active);

            if (selectionPulse != null)
                selectionPulse.gameObject.SetActive(active && selectionPulseAge < SelectionPulseDuration);
        }

        private void SetLeaderObjectsActive(bool active)
        {
            if (leaderRoot != null)
                leaderRoot.gameObject.SetActive(active);

            if (leaderRing != null)
                leaderRing.gameObject.SetActive(active);
        }

        private bool TryGetCarBounds(out Bounds bounds)
        {
            RefreshBodyRenderers();
            bounds = default;
            bool hasBounds = false;

            foreach (Renderer item in bodyRenderers)
            {
                if (item == null)
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

        private bool IsSelectionEffectRenderer(Renderer renderer)
        {
            return renderer != null &&
                selectionRoot != null &&
                renderer.transform.IsChildOf(selectionRoot);
        }

        private bool IsLeaderEffectRenderer(Renderer renderer)
        {
            return renderer != null &&
                leaderRoot != null &&
                renderer.transform.IsChildOf(leaderRoot);
        }
    }
}