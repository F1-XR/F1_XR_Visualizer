using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using static F1XR.RestAPI.Replay.ReplayCarVisualUtil;

namespace F1XR.RestAPI.Replay
{
    public partial class ReplayCarView
    {
        private const float SelectionBodyTint = 0.48f;
        private const float SelectionBodyEmission = 0.9f;

        private static readonly bool TintCarBody = false;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private readonly List<Renderer> bodyRenderers = new();
        private readonly Dictionary<Renderer, MaterialPropertyBlock> bodyBlocks = new();
        private bool bodyRenderersDirty = true;
        private float visualWidth;
        private float visualLength;

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
            visualWidth = 0f;
            visualLength = 0f;

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            foreach (Renderer item in renderers)
            {
                if (item == null || IsIgnoredRenderer(item))
                    continue;

                item.shadowCastingMode = ShadowCastingMode.Off;
                item.receiveShadows = false;
                item.motionVectorGenerationMode =
                    MotionVectorGenerationMode.ForceNoMotion;
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
            return IsRenderLodRenderer(renderer) ||
                label != null && renderer.gameObject == label.gameObject ||
                labelBackground != null && renderer.gameObject == labelBackground.gameObject ||
                labelLine != null && renderer.gameObject == labelLine.gameObject ||
                labelTopDot != null && renderer.gameObject == labelTopDot.gameObject ||
                labelBottomDot != null && renderer.gameObject == labelBottomDot.gameObject ||
                drivingPresentation != null &&
                    drivingPresentation.OwnsRenderer(renderer) ||
                IsSelectionEffectRenderer(renderer) ||
                IsLeaderEffectRenderer(renderer) ||
                IsOvertakeRibbonRenderer(renderer) ||
                IsOvertakeSideBySideVfxRenderer(renderer);
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

        public float GetVisualWidth()
        {
            if (!bodyRenderersDirty && visualWidth > 0f)
                return visualWidth;

            visualWidth = GetVisualSize(Vector3.right);
            return visualWidth;
        }

        public float GetVisualLength()
        {
            if (!bodyRenderersDirty && visualLength > 0f)
                return visualLength;

            visualLength = GetVisualSize(Vector3.forward);
            return visualLength;
        }

        private float GetVisualSize(Vector3 axis)
        {
            RefreshBodyRenderers();
            float minimum = float.PositiveInfinity;
            float maximum = float.NegativeInfinity;

            foreach (Renderer item in bodyRenderers)
            {
                if (item == null)
                    continue;

                Bounds bounds = item.localBounds;
                Matrix4x4 rendererToCar =
                    LogicalRoot.worldToLocalMatrix * item.transform.localToWorldMatrix;
                Vector3 min = bounds.min;
                Vector3 max = bounds.max;

                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = new(
                        (corner & 1) == 0 ? min.x : max.x,
                        (corner & 2) == 0 ? min.y : max.y,
                        (corner & 4) == 0 ? min.z : max.z);
                    float value = Vector3.Dot(
                        rendererToCar.MultiplyPoint3x4(point),
                        axis);
                    minimum = Mathf.Min(minimum, value);
                    maximum = Mathf.Max(maximum, value);
                }
            }

            return minimum <= maximum
                ? Mathf.Max(0.001f, maximum - minimum)
                : 0f;
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

    public partial class ReplayCarView
    {
        private const float RenderLodUpdateInterval = 0.12f;
        private const float BudgetDetailEnterViewportRatio =
            0.045f;
        private const float BudgetDetailExitViewportRatio =
            0.035f;
        private const float LabelEnterViewportRatio = 0.014f;
        private const float LabelExitViewportRatio = 0.01f;

        private readonly List<Renderer> renderLodSources = new();
        private readonly List<bool> renderLodSourceStates = new();
        private readonly List<GameObject>
            renderLodDetailRoots = new();
        private readonly List<bool>
            renderLodDetailRootStates = new();
        private Mesh renderLodProxyMesh;
        private Material renderLodProxyMaterial;
        private MeshRenderer renderLodProxyRenderer;
        private bool renderLodConfigured;
        private bool renderLodEnabled = true;
        private bool renderLodUsingProxy;
        private bool renderLodForceDetailed;
        private bool renderLodBudgetDetailed;
        private bool renderLodLabelVisible = true;
        private float nextRenderLodUpdateTime;

        public bool RequiresDetailedRenderLod =>
            selected ||
            hovered ||
            renderLodForceDetailed;

        public bool ShouldApplyMotionThisFrame()
        {
            if (!renderLodEnabled ||
                !renderLodUsingProxy ||
                RequiresDetailedRenderLod)
            {
                return true;
            }

            return ((Time.frameCount + driverNumber) & 1) == 0;
        }

        public bool QualifiesForDetailedRenderLod(
            Camera camera)
        {
            if (camera == null ||
                !TryGetRenderLodViewportRatio(
                    camera,
                    out float viewportRatio))
            {
                return false;
            }

            float threshold = renderLodBudgetDetailed
                ? BudgetDetailExitViewportRatio
                : BudgetDetailEnterViewportRatio;
            return viewportRatio >= threshold;
        }

        public void ConfigureRenderLod()
        {
            if (renderLodConfigured)
                return;

            RefreshBodyRenderers();
            foreach (Renderer bodyRenderer in bodyRenderers)
            {
                if (bodyRenderer == null)
                    continue;

                renderLodSources.Add(bodyRenderer);
                renderLodSourceStates.Add(
                    bodyRenderer.enabled);

                Transform detailRoot =
                    GetRenderLodDetailRoot(bodyRenderer);
                if (detailRoot != null &&
                    !renderLodDetailRoots.Contains(
                        detailRoot.gameObject))
                {
                    renderLodDetailRoots.Add(
                        detailRoot.gameObject);
                    renderLodDetailRootStates.Add(
                        detailRoot.gameObject.activeSelf);
                }
            }

            if (renderLodSources.Count == 0 ||
                !TryGetRenderLodLocalBounds(
                    out Bounds localBounds))
            {
                renderLodSources.Clear();
                renderLodSourceStates.Clear();
                return;
            }

            Shader shader =
                Shader.Find(
                    "Universal Render Pipeline/Unlit") ??
                Shader.Find("Unlit/Color");
            if (shader == null)
            {
                renderLodSources.Clear();
                renderLodSourceStates.Clear();
                return;
            }

            renderLodProxyMesh =
                CreateRenderLodProxyMesh(localBounds);

            GameObject proxy = new(
                "RenderLodProxy",
                typeof(MeshFilter),
                typeof(MeshRenderer));
            proxy.layer = gameObject.layer;
            proxy.transform.SetParent(transform, false);
            proxy.GetComponent<MeshFilter>().sharedMesh =
                renderLodProxyMesh;

            renderLodProxyRenderer =
                proxy.GetComponent<MeshRenderer>();
            renderLodProxyMaterial =
                new Material(shader)
                {
                    name =
                        $"Car {driverNumber} Render LOD Material"
                };
            if (renderLodProxyMaterial.HasProperty(
                    "_Cull"))
            {
                renderLodProxyMaterial.SetFloat(
                    "_Cull",
                    (float)CullMode.Off);
            }
            renderLodProxyRenderer.sharedMaterial =
                renderLodProxyMaterial;
            renderLodProxyRenderer.shadowCastingMode =
                ShadowCastingMode.Off;
            renderLodProxyRenderer.receiveShadows = false;
            renderLodProxyRenderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            renderLodProxyRenderer.enabled = false;
            UpdateRenderLodColor();

            renderLodConfigured = true;
            nextRenderLodUpdateTime =
                Time.unscaledTime +
                (driverNumber & 7) *
                (RenderLodUpdateInterval / 8f);
            RefreshRuntimeUpdateState();
        }

        private void UpdateRenderLod(bool immediate = false)
        {
            if (!renderLodConfigured)
                return;

            float now = Time.unscaledTime;
            if (!immediate &&
                now < nextRenderLodUpdateTime)
            {
                return;
            }

            nextRenderLodUpdateTime =
                now + RenderLodUpdateInterval;

            bool forceDetailed =
                !renderLodEnabled ||
                selected ||
                hovered ||
                renderLodForceDetailed;
            if (forceDetailed)
            {
                SetRenderLodProxyActive(false);
                SetLabelLodVisible(true);
                renderLodLabelVisible = true;
                return;
            }

            SetRenderLodProxyActive(
                !renderLodBudgetDetailed);

            if (labelCamera == null ||
                !labelCamera.isActiveAndEnabled)
            {
                labelCamera = Camera.main;
            }

            if (labelCamera == null ||
                !TryGetRenderLodViewportRatio(
                    labelCamera,
                    out float viewportRatio))
            {
                return;
            }

            bool showLabel = renderLodLabelVisible
                ? viewportRatio >= LabelExitViewportRatio
                : viewportRatio >= LabelEnterViewportRatio;
            renderLodLabelVisible = showLabel;
            SetLabelLodVisible(showLabel);
        }

        private void SetRenderLodProxyActive(bool active)
        {
            if (!renderLodConfigured ||
                renderLodUsingProxy == active)
            {
                return;
            }

            renderLodUsingProxy = active;

            if (!active)
            {
                for (int i = 0;
                     i < renderLodDetailRoots.Count;
                     i++)
                {
                    GameObject root =
                        renderLodDetailRoots[i];
                    if (root != null)
                    {
                        root.SetActive(
                            renderLodDetailRootStates[i]);
                    }
                }
            }

            for (int i = 0;
                 i < renderLodSources.Count;
                 i++)
            {
                Renderer source = renderLodSources[i];
                if (source != null)
                {
                    source.enabled =
                        !active &&
                        renderLodSourceStates[i];
                }
            }

            if (active)
            {
                for (int i = 0;
                     i < renderLodDetailRoots.Count;
                     i++)
                {
                    GameObject root =
                        renderLodDetailRoots[i];
                    if (root != null)
                        root.SetActive(false);
                }
            }

            if (renderLodProxyRenderer != null)
                renderLodProxyRenderer.enabled = active;
        }

        private void SetRenderLodForceDetailed(bool enabled)
        {
            if (renderLodForceDetailed == enabled)
                return;

            renderLodForceDetailed = enabled;
            UpdateRenderLod(true);
        }

        public void SetRenderLodBudgetDetailed(
            bool enabled)
        {
            if (renderLodBudgetDetailed == enabled)
                return;

            renderLodBudgetDetailed = enabled;
            UpdateRenderLod(true);
        }

        public void SetRenderLodEnabled(bool enabled)
        {
            if (renderLodEnabled == enabled)
                return;

            renderLodEnabled = enabled;
            UpdateRenderLod(true);
        }

        private bool IsRenderLodRenderer(
            Renderer renderer)
        {
            return renderer != null &&
                renderer == renderLodProxyRenderer;
        }

        private void UpdateRenderLodColor()
        {
            if (renderLodProxyMaterial == null)
                return;

            Color color = new(
                labelColor.r,
                labelColor.g,
                labelColor.b,
                1f);
            if (renderLodProxyMaterial.HasProperty(
                    BaseColorId))
            {
                renderLodProxyMaterial.SetColor(
                    BaseColorId,
                    color);
            }
            if (renderLodProxyMaterial.HasProperty(
                    ColorId))
            {
                renderLodProxyMaterial.SetColor(
                    ColorId,
                    color);
            }
        }

        private bool TryGetRenderLodLocalBounds(
            out Bounds bounds)
        {
            bounds = default;
            bool hasBounds = false;

            foreach (Renderer source in renderLodSources)
            {
                if (source == null)
                    continue;

                Bounds sourceBounds = source.localBounds;
                Matrix4x4 sourceToCar =
                    transform.worldToLocalMatrix *
                    source.transform.localToWorldMatrix;
                Vector3 min = sourceBounds.min;
                Vector3 max = sourceBounds.max;

                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = new(
                        (corner & 1) == 0 ? min.x : max.x,
                        (corner & 2) == 0 ? min.y : max.y,
                        (corner & 4) == 0 ? min.z : max.z);
                    point =
                        sourceToCar.MultiplyPoint3x4(point);
                    if (hasBounds)
                        bounds.Encapsulate(point);
                    else
                    {
                        bounds = new Bounds(
                            point,
                            Vector3.zero);
                        hasBounds = true;
                    }
                }
            }

            return hasBounds;
        }

        private bool TryGetRenderLodViewportRatio(
            Camera camera,
            out float viewportRatio)
        {
            viewportRatio = 0f;
            if (camera == null ||
                !TryGetCarBounds(out Bounds bounds))
            {
                return false;
            }

            float distance = Vector3.Distance(
                camera.transform.position,
                bounds.center);
            float halfFov =
                camera.fieldOfView *
                0.5f *
                Mathf.Deg2Rad;
            viewportRatio =
                Mathf.Max(
                    bounds.size.x,
                    bounds.size.y,
                    bounds.size.z) /
                Mathf.Max(
                    0.001f,
                    2f *
                    distance *
                    Mathf.Tan(halfFov));
            return true;
        }

        private Transform GetRenderLodDetailRoot(
            Renderer source)
        {
            if (source == null ||
                source.transform == transform)
            {
                return null;
            }

            Transform root = source.transform;
            while (root.parent != null &&
                   root.parent != transform)
            {
                root = root.parent;
            }

            return root.parent == transform
                ? root
                : null;
        }

        private Mesh CreateRenderLodProxyMesh(
            Bounds bounds)
        {
            List<Vector3> vertices = new(72);
            List<int> triangles = new(324);

            float width = Mathf.Max(
                0.001f,
                bounds.size.x);
            float height = Mathf.Max(
                width * 0.16f,
                bounds.size.y);
            float length = Mathf.Max(
                0.001f,
                bounds.size.z);
            float floor = bounds.min.y;
            float centerX = bounds.center.x;
            float centerZ = bounds.center.z;

            AddProxyBox(
                vertices,
                triangles,
                new Vector3(
                    centerX,
                    floor + height * 0.31f,
                    centerZ - length * 0.04f),
                new Vector3(
                    width * 0.38f,
                    height * 0.42f,
                    length * 0.56f));
            AddProxyBox(
                vertices,
                triangles,
                new Vector3(
                    centerX,
                    floor + height * 0.24f,
                    centerZ + length * 0.32f),
                new Vector3(
                    width * 0.17f,
                    height * 0.22f,
                    length * 0.30f));
            AddProxyBox(
                vertices,
                triangles,
                new Vector3(
                    centerX,
                    floor + height * 0.29f,
                    centerZ - length * 0.34f),
                new Vector3(
                    width * 0.58f,
                    height * 0.32f,
                    length * 0.15f));
            AddProxyBox(
                vertices,
                triangles,
                new Vector3(
                    centerX,
                    floor + height * 0.17f,
                    centerZ + length * 0.47f),
                new Vector3(
                    width * 0.94f,
                    height * 0.08f,
                    length * 0.09f));
            AddProxyBox(
                vertices,
                triangles,
                new Vector3(
                    centerX,
                    floor + height * 0.56f,
                    centerZ - length * 0.45f),
                new Vector3(
                    width * 0.68f,
                    height * 0.12f,
                    length * 0.08f));

            float wheelX = width * 0.39f;
            float wheelY = floor + height * 0.18f;
            float wheelZ = length * 0.29f;
            Vector3 wheelSize = new(
                width * 0.20f,
                height * 0.34f,
                length * 0.14f);
            AddProxyBox(
                vertices,
                triangles,
                new Vector3(
                    centerX - wheelX,
                    wheelY,
                    centerZ + wheelZ),
                wheelSize);
            AddProxyBox(
                vertices,
                triangles,
                new Vector3(
                    centerX + wheelX,
                    wheelY,
                    centerZ + wheelZ),
                wheelSize);
            AddProxyBox(
                vertices,
                triangles,
                new Vector3(
                    centerX - wheelX,
                    wheelY,
                    centerZ - wheelZ),
                wheelSize);
            AddProxyBox(
                vertices,
                triangles,
                new Vector3(
                    centerX + wheelX,
                    wheelY,
                    centerZ - wheelZ),
                wheelSize);

            Mesh mesh = new()
            {
                name =
                    $"Car {driverNumber} Low Detail Proxy"
            };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddProxyBox(
            List<Vector3> vertices,
            List<int> triangles,
            Vector3 center,
            Vector3 size)
        {
            int start = vertices.Count;
            Vector3 half = size * 0.5f;

            vertices.Add(
                center + new Vector3(
                    -half.x, -half.y, -half.z));
            vertices.Add(
                center + new Vector3(
                    half.x, -half.y, -half.z));
            vertices.Add(
                center + new Vector3(
                    half.x, half.y, -half.z));
            vertices.Add(
                center + new Vector3(
                    -half.x, half.y, -half.z));
            vertices.Add(
                center + new Vector3(
                    -half.x, -half.y, half.z));
            vertices.Add(
                center + new Vector3(
                    half.x, -half.y, half.z));
            vertices.Add(
                center + new Vector3(
                    half.x, half.y, half.z));
            vertices.Add(
                center + new Vector3(
                    -half.x, half.y, half.z));

            int[] boxTriangles =
            {
                0, 2, 1, 0, 3, 2,
                4, 5, 6, 4, 6, 7,
                0, 4, 7, 0, 7, 3,
                1, 2, 6, 1, 6, 5,
                0, 1, 5, 0, 5, 4,
                3, 7, 6, 3, 6, 2
            };
            foreach (int index in boxTriangles)
                triangles.Add(start + index);
        }

        private void DisposeRenderLod()
        {
            if (renderLodProxyMesh != null)
                Destroy(renderLodProxyMesh);
            if (renderLodProxyMaterial != null)
                Destroy(renderLodProxyMaterial);
        }
    }
}
