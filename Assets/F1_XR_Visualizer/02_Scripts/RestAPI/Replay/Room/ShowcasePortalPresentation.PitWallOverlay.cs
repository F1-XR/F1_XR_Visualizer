using System.Collections.Generic;
using F1XR.RestAPI.Replay;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace F1XR.RestAPI.Replay.Room
{
    public enum PitWallOverlayLayout
    {
        None,
        Compact,
        Full
    }

    public readonly struct PitWallOverlayLabels
    {
        public PitWallOverlayLabels(
            string team,
            string driver,
            int lap,
            TMP_FontAsset font)
        {
            Team = team ?? "";
            Driver = driver ?? "";
            TeamDisplay = Team.ToUpperInvariant();
            DriverDisplay = Driver.ToUpperInvariant();
            Lap = lap;
            Font = font;
        }

        public string Team { get; }
        public string Driver { get; }
        public string TeamDisplay { get; }
        public string DriverDisplay { get; }
        public int Lap { get; }
        public TMP_FontAsset Font { get; }
    }

    public static class PitWallLayoutPolicy
    {
        public const float MinimumWidth = 1.8f;
        public const float MinimumHeight = 1.3f;
        public const float FullWidth = 2.5f;
        public const float FullHeight = 1.6f;

        public static PitWallOverlayLayout Resolve(
            float wallWidth,
            float wallHeight)
        {
            if (wallWidth >= FullWidth &&
                wallHeight >= FullHeight)
            {
                return PitWallOverlayLayout.Full;
            }

            return wallWidth >= MinimumWidth &&
                   wallHeight >= MinimumHeight
                ? PitWallOverlayLayout.Compact
                : PitWallOverlayLayout.None;
        }
    }

    public sealed partial class ShowcasePortalPresentation
    {
        private const float PitWallTimerUpdatesPerSecond = 20f;

        private Transform pitWallOverlayRoot;
        private Renderer pitWallGantryRenderer;
        private Renderer pitWallAccentRenderer;
        private Renderer pitWallFloorRenderer;
        private Renderer pitWallScanRenderer;
        private Transform pitWallScanTransform;
        private TextMeshPro pitWallOverlayText;
        private MaterialPropertyBlock pitWallProperties;
        private PitWallOverlayLayout pitWallLayout;
        private Vector2 pitWallApertureSize;
        private Color32 pitWallCachedColor;
        private PitStopPhase pitWallCachedPhase;
        private int pitWallCachedLabelHash;
        private int pitWallCachedTimerTick = int.MinValue;
        private bool pitWallCachedReconstructed;
        private bool pitWallCachedDriveThrough;
        private bool pitWallCacheValid;

        public PitWallOverlayLayout PitWallLayout => pitWallLayout;

        internal void CreatePitWallOverlay(
            Transform portalSurface,
            Vector2 apertureSize,
            PitWallOverlayLayout layout)
        {
            ClearPitWallOverlay();
            if (portalSurface == null ||
                layout == PitWallOverlayLayout.None)
            {
                return;
            }

            pitWallLayout = layout;
            pitWallApertureSize = apertureSize;
            pitWallProperties = new MaterialPropertyBlock();
            pitWallOverlayRoot = new GameObject(
                "LivingPitWallOverlay").transform;
            pitWallOverlayRoot.gameObject.layer = PortalSurfaceLayer;
            pitWallOverlayRoot.SetParent(portalSurface, false);

            Material gantryMaterial = CreatePitWallMaterial(
                "PitWallGantry",
                new Color(0.018f, 0.022f, 0.03f, 0.94f),
                false,
                3140);
            Material accentMaterial = CreatePitWallMaterial(
                "PitWallAccent",
                new Color(0.9f, 0.08f, 0.08f, 0.88f),
                true,
                3150);

            pitWallGantryRenderer = CreatePitWallRenderer(
                "PitWallGantry",
                BuildPitWallGantryMesh(apertureSize, layout),
                gantryMaterial,
                -0.02f,
                30);
            pitWallAccentRenderer = CreatePitWallRenderer(
                "PitWallAccent",
                BuildPitWallAccentMesh(apertureSize, layout),
                accentMaterial,
                -0.024f,
                31);
            pitWallScanRenderer = CreatePitWallRenderer(
                "PitWallScan",
                BuildPitWallScanMesh(apertureSize),
                accentMaterial,
                -0.028f,
                32);
            pitWallScanTransform = pitWallScanRenderer != null
                ? pitWallScanRenderer.transform
                : null;

            pitWallFloorRenderer = CreatePitWallRenderer(
                layout == PitWallOverlayLayout.Full
                    ? "PitWallExitArrows"
                    : "PitWallFloorRibbon",
                layout == PitWallOverlayLayout.Full
                    ? BuildPitWallFloorArrowMesh(apertureSize)
                    : BuildPitWallCompactFloorRibbonMesh(apertureSize),
                accentMaterial,
                0f,
                29);

            GameObject textObject = new GameObject(
                "PitWallBroadcast",
                typeof(TextMeshPro));
            textObject.layer = PortalSurfaceLayer;
            textObject.transform.SetParent(
                pitWallOverlayRoot,
                false);
            float halfHeight = apertureSize.y * 0.5f;
            float headerHeight = ResolvePitHeaderHeight(
                apertureSize,
                layout);
            textObject.transform.localPosition = new Vector3(
                0f,
                halfHeight - headerHeight * 0.51f,
                -0.03f);
            textObject.transform.localRotation =
                Quaternion.Euler(0f, 180f, 0f);
            textObject.transform.localScale = Vector3.one * 0.1f;
            pitWallOverlayText = textObject.GetComponent<TextMeshPro>();
            pitWallOverlayText.alignment =
                TextAlignmentOptions.Center;
            pitWallOverlayText.fontSize =
                layout == PitWallOverlayLayout.Full ? 2f : 1.65f;
            pitWallOverlayText.enableAutoSizing = false;
            pitWallOverlayText.color = Color.white;
            pitWallOverlayText.text = "PIT WALL  //  STANDBY";
            pitWallOverlayText.rectTransform.sizeDelta = new Vector2(
                apertureSize.x * 9.2f,
                headerHeight * 8f);
            ConfigurePitWallRenderer(pitWallOverlayText.renderer, 33);
        }

        internal void ApplyPitWallOverlay(
            PitStopPresentationState state,
            Color teamColor,
            PitWallOverlayLabels labels)
        {
            if (pitWallOverlayRoot == null ||
                pitWallLayout == PitWallOverlayLayout.None)
            {
                return;
            }

            int timerTick = ResolvePitWallTimerTick(state);
            int labelHash = ComputePitWallLabelHash(labels);
            Color32 colorKey = teamColor;
            bool contentChanged =
                !pitWallCacheValid ||
                pitWallCachedPhase != state.Phase ||
                pitWallCachedColor.r != colorKey.r ||
                pitWallCachedColor.g != colorKey.g ||
                pitWallCachedColor.b != colorKey.b ||
                pitWallCachedLabelHash != labelHash ||
                pitWallCachedTimerTick != timerTick ||
                pitWallCachedReconstructed != state.IsReconstructed ||
                pitWallCachedDriveThrough != state.IsDriveThrough;

            if (contentChanged && pitWallOverlayText != null)
            {
                if (labels.Font != null &&
                    pitWallOverlayText.font != labels.Font)
                {
                    pitWallOverlayText.font = labels.Font;
                }

                string status = ResolvePitWallStatus(state);
                string timer = ResolvePitWallTimer(state, timerTick);
                string reconstruction = state.IsReconstructed
                    ? "  //  RECONSTRUCTED"
                    : "";
                pitWallOverlayText.text =
                    pitWallLayout == PitWallOverlayLayout.Full
                        ? $"{labels.TeamDisplay}  //  " +
                          $"{labels.DriverDisplay}  //  " +
                          $"LAP {labels.Lap}\n" +
                          $"{status}     {timer}{reconstruction}"
                        : $"{labels.DriverDisplay}  L{labels.Lap}" +
                          $"  |  {status}  |  {timer}";
            }

            Color activeColor = ResolvePitWallColor(
                state,
                teamColor);
            ApplyPitWallColor(
                pitWallAccentRenderer,
                activeColor);
            ApplyPitWallColor(
                pitWallFloorRenderer,
                state.Phase == PitStopPhase.Release ||
                state.Phase == PitStopPhase.Exit
                    ? new Color(0.18f, 1f, 0.36f, 0.94f)
                    : new Color(
                        teamColor.r,
                        teamColor.g,
                        teamColor.b,
                        0.34f));
            ApplyPitWallColor(
                pitWallScanRenderer,
                activeColor);

            UpdatePitWallMotion(state);
            pitWallCachedPhase = state.Phase;
            pitWallCachedColor = colorKey;
            pitWallCachedLabelHash = labelHash;
            pitWallCachedTimerTick = timerTick;
            pitWallCachedReconstructed = state.IsReconstructed;
            pitWallCachedDriveThrough = state.IsDriveThrough;
            pitWallCacheValid = true;
        }

        private void UpdatePitWallMotion(
            PitStopPresentationState state)
        {
            if (pitWallScanRenderer != null)
            {
                bool scanning = state.Phase == PitStopPhase.Approach ||
                    state.Phase == PitStopPhase.Brake;
                pitWallScanRenderer.enabled = scanning;
                if (scanning && pitWallScanTransform != null)
                {
                    float halfHeight = pitWallApertureSize.y * 0.5f;
                    float header = ResolvePitHeaderHeight(
                        pitWallApertureSize,
                        pitWallLayout);
                    pitWallScanTransform.localPosition = new Vector3(
                        0f,
                        Mathf.Lerp(
                            -halfHeight + 0.08f,
                            halfHeight - header - 0.06f,
                            state.PhaseProgress),
                        -0.028f);
                }
            }

            if (pitWallFloorRenderer != null)
            {
                pitWallFloorRenderer.enabled =
                    state.Phase == PitStopPhase.Brake ||
                    state.Phase == PitStopPhase.Release ||
                    state.Phase == PitStopPhase.Exit;
            }
        }

        private void ClearPitWallOverlay()
        {
            if (pitWallOverlayRoot != null)
                pitWallOverlayRoot.gameObject.SetActive(false);
            pitWallOverlayRoot = null;
            pitWallGantryRenderer = null;
            pitWallAccentRenderer = null;
            pitWallFloorRenderer = null;
            pitWallScanRenderer = null;
            pitWallScanTransform = null;
            pitWallOverlayText = null;
            pitWallProperties = null;
            pitWallLayout = PitWallOverlayLayout.None;
            pitWallApertureSize = Vector2.zero;
            pitWallCacheValid = false;
            pitWallCachedTimerTick = int.MinValue;
            pitWallCachedReconstructed = false;
            pitWallCachedDriveThrough = false;
        }

        private Renderer CreatePitWallRenderer(
            string name,
            Mesh mesh,
            Material material,
            float localDepth,
            int sortingOrder)
        {
            if (mesh == null || material == null ||
                pitWallOverlayRoot == null)
            {
                return null;
            }

            GameObject instance = new GameObject(name);
            instance.layer = PortalSurfaceLayer;
            instance.transform.SetParent(pitWallOverlayRoot, false);
            instance.transform.localPosition =
                new Vector3(0f, 0f, localDepth);
            MeshFilter filter = instance.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer =
                instance.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            ConfigurePitWallRenderer(renderer, sortingOrder);
            return renderer;
        }

        private static void ConfigurePitWallRenderer(
            Renderer renderer,
            int sortingOrder)
        {
            if (renderer == null)
                return;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            renderer.sortingOrder = sortingOrder;
        }

        private Material CreatePitWallMaterial(
            string name,
            Color color,
            bool additive,
            int renderQueue)
        {
            Shader shader = Shader.Find(
                "Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                return null;

            Material material = new(shader)
            {
                name = name,
                color = color,
                renderQueue = renderQueue
            };
            material.SetOverrideTag("RenderType", "Transparent");
            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_SrcBlend"))
            {
                material.SetFloat(
                    "_SrcBlend",
                    (float)BlendMode.SrcAlpha);
            }
            if (material.HasProperty("_DstBlend"))
            {
                material.SetFloat(
                    "_DstBlend",
                    additive
                        ? (float)BlendMode.One
                        : (float)BlendMode.OneMinusSrcAlpha);
            }
            if (material.HasProperty("_ZWrite"))
                material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_Cull"))
                material.SetFloat("_Cull", (float)CullMode.Off);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            runtimeMaterials.Add(material);
            return material;
        }

        private void ApplyPitWallColor(
            Renderer renderer,
            Color color)
        {
            if (renderer == null || pitWallProperties == null)
                return;
            renderer.GetPropertyBlock(pitWallProperties);
            pitWallProperties.SetColor("_BaseColor", color);
            pitWallProperties.SetColor("_Color", color);
            renderer.SetPropertyBlock(pitWallProperties);
            pitWallProperties.Clear();
        }

        private Mesh BuildPitWallGantryMesh(
            Vector2 size,
            PitWallOverlayLayout layout)
        {
            float halfWidth = size.x * 0.5f;
            float halfHeight = size.y * 0.5f;
            float header = ResolvePitHeaderHeight(size, layout);
            float pillar = Mathf.Clamp(
                size.x * (layout == PitWallOverlayLayout.Full
                    ? 0.035f
                    : 0.026f),
                0.035f,
                0.095f);
            List<Vector3> vertices = new();
            List<Vector2> uvs = new();
            List<int> triangles = new();
            AddPitWallRect(
                vertices,
                uvs,
                triangles,
                -halfWidth,
                halfHeight - header,
                halfWidth,
                halfHeight);
            AddPitWallRect(
                vertices,
                uvs,
                triangles,
                -halfWidth,
                -halfHeight,
                -halfWidth + pillar,
                halfHeight - header);
            AddPitWallRect(
                vertices,
                uvs,
                triangles,
                halfWidth - pillar,
                -halfHeight,
                halfWidth,
                halfHeight - header);
            return CreatePitWallMesh(
                "LivingPitWallGantryMesh",
                vertices,
                uvs,
                triangles);
        }

        private Mesh BuildPitWallAccentMesh(
            Vector2 size,
            PitWallOverlayLayout layout)
        {
            float halfWidth = size.x * 0.5f;
            float halfHeight = size.y * 0.5f;
            float header = ResolvePitHeaderHeight(size, layout);
            float line = Mathf.Clamp(
                Mathf.Min(size.x, size.y) * 0.015f,
                0.014f,
                0.034f);
            float inset = line * 1.7f;
            List<Vector3> vertices = new();
            List<Vector2> uvs = new();
            List<int> triangles = new();
            AddPitWallRect(
                vertices,
                uvs,
                triangles,
                -halfWidth + inset,
                halfHeight - header - line,
                halfWidth - inset,
                halfHeight - header);
            AddPitWallRect(
                vertices,
                uvs,
                triangles,
                -halfWidth + inset,
                -halfHeight + inset,
                -halfWidth + inset + line,
                halfHeight - header - line);
            AddPitWallRect(
                vertices,
                uvs,
                triangles,
                halfWidth - inset - line,
                -halfHeight + inset,
                halfWidth - inset,
                halfHeight - header - line);
            return CreatePitWallMesh(
                "LivingPitWallAccentMesh",
                vertices,
                uvs,
                triangles);
        }

        private Mesh BuildPitWallScanMesh(Vector2 size)
        {
            float halfWidth = size.x * 0.5f;
            float thickness = Mathf.Clamp(
                size.y * 0.009f,
                0.008f,
                0.018f);
            List<Vector3> vertices = new();
            List<Vector2> uvs = new();
            List<int> triangles = new();
            AddPitWallRect(
                vertices,
                uvs,
                triangles,
                -halfWidth * 0.92f,
                -thickness,
                halfWidth * 0.92f,
                thickness);
            return CreatePitWallMesh(
                "LivingPitWallScanMesh",
                vertices,
                uvs,
                triangles);
        }

        private Mesh BuildPitWallFloorArrowMesh(Vector2 size)
        {
            float halfWidth = Mathf.Min(size.x * 0.32f, 1.05f);
            float floorY = -size.y * 0.5f + 0.008f;
            float depth = Mathf.Clamp(size.y * 0.42f, 0.48f, 0.78f);
            List<Vector3> vertices = new();
            List<Vector2> uvs = new();
            List<int> triangles = new();
            for (int i = 0; i < 3; i++)
            {
                float start = 0.08f + i * depth * 0.29f;
                float end = start + depth * 0.19f;
                AddPitWallFloorChevron(
                    vertices,
                    uvs,
                    triangles,
                    floorY,
                    halfWidth * (1f - i * 0.12f),
                    start,
                    end);
            }
            return CreatePitWallMesh(
                "LivingPitWallFloorArrowsMesh",
                vertices,
                uvs,
                triangles);
        }

        private Mesh BuildPitWallCompactFloorRibbonMesh(Vector2 size)
        {
            float floorY = -size.y * 0.5f + 0.008f;
            float nearHalfWidth = Mathf.Min(size.x * 0.27f, 0.52f);
            float farHalfWidth = nearHalfWidth * 0.64f;
            float near = 0.06f;
            float far = Mathf.Clamp(size.y * 0.28f, 0.28f, 0.44f);
            List<Vector3> vertices = new()
            {
                new Vector3(-nearHalfWidth, floorY, near),
                new Vector3(nearHalfWidth, floorY, near),
                new Vector3(farHalfWidth, floorY, far),
                new Vector3(-farHalfWidth, floorY, far)
            };
            List<Vector2> uvs = new()
            {
                Vector2.zero,
                Vector2.right,
                Vector2.one,
                Vector2.up
            };
            List<int> triangles = new();
            AddPitWallQuadTriangles(triangles, 0);
            return CreatePitWallMesh(
                "LivingPitWallCompactFloorRibbonMesh",
                vertices,
                uvs,
                triangles);
        }

        internal Mesh CreateRectangularPitPortalMesh(
            string name,
            Vector2 size,
            bool mirrorHorizontally)
        {
            float halfWidth = size.x * 0.5f;
            float halfHeight = size.y * 0.5f;
            Vector3[] vertices =
            {
                new(-halfWidth, -halfHeight, 0f),
                new(halfWidth, -halfHeight, 0f),
                new(halfWidth, halfHeight, 0f),
                new(-halfWidth, halfHeight, 0f)
            };
            float leftU = mirrorHorizontally ? 1f : 0f;
            float rightU = mirrorHorizontally ? 0f : 1f;
            Vector2[] uvs =
            {
                new(leftU, 0f),
                new(rightU, 0f),
                new(rightU, 1f),
                new(leftU, 1f)
            };
            int[] triangles =
            {
                0, 2, 1,
                0, 3, 2,
                0, 1, 2,
                0, 2, 3
            };
            Mesh mesh = new()
            {
                name = $"{name}RectangularMesh",
                vertices = vertices,
                uv = uvs,
                triangles = triangles
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            runtimeMeshes.Add(mesh);
            return mesh;
        }

        internal Mesh CreateRectangularPitPortalFrameMesh(
            Vector2 size,
            float width,
            string name)
        {
            float outerX = size.x * 0.5f;
            float outerY = size.y * 0.5f;
            float innerX = Mathf.Max(0f, outerX - width);
            float innerY = Mathf.Max(0f, outerY - width);
            List<Vector3> vertices = new();
            List<Vector2> uvs = new();
            List<int> triangles = new();
            AddPitWallRectRing(
                vertices,
                uvs,
                triangles,
                outerX,
                outerY,
                innerX,
                innerY);
            return CreatePitWallMesh(
                name,
                vertices,
                uvs,
                triangles);
        }

        private Mesh CreatePitWallMesh(
            string name,
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<int> triangles)
        {
            Mesh mesh = new()
            {
                name = name,
                vertices = vertices.ToArray(),
                uv = uvs.ToArray(),
                triangles = triangles.ToArray()
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            runtimeMeshes.Add(mesh);
            return mesh;
        }

        private static void AddPitWallRect(
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<int> triangles,
            float minX,
            float minY,
            float maxX,
            float maxY)
        {
            int start = vertices.Count;
            vertices.Add(new Vector3(minX, minY, 0f));
            vertices.Add(new Vector3(maxX, minY, 0f));
            vertices.Add(new Vector3(maxX, maxY, 0f));
            vertices.Add(new Vector3(minX, maxY, 0f));
            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(1f, 0f));
            uvs.Add(new Vector2(1f, 1f));
            uvs.Add(new Vector2(0f, 1f));
            AddPitWallQuadTriangles(triangles, start);
        }

        private static void AddPitWallFloorChevron(
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<int> triangles,
            float floorY,
            float halfWidth,
            float startZ,
            float endZ)
        {
            int start = vertices.Count;
            vertices.Add(new Vector3(-halfWidth, floorY, startZ));
            vertices.Add(new Vector3(0f, floorY, endZ));
            vertices.Add(new Vector3(halfWidth, floorY, startZ));
            vertices.Add(new Vector3(0f, floorY, endZ * 0.78f));
            uvs.Add(Vector2.zero);
            uvs.Add(Vector2.up);
            uvs.Add(Vector2.right);
            uvs.Add(Vector2.one);
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 3);
            triangles.Add(start + 3);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
            triangles.Add(start + 1);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 1);
            triangles.Add(start + 3);
        }

        private static void AddPitWallRectRing(
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<int> triangles,
            float outerX,
            float outerY,
            float innerX,
            float innerY)
        {
            AddPitWallRect(
                vertices,
                uvs,
                triangles,
                -outerX,
                innerY,
                outerX,
                outerY);
            AddPitWallRect(
                vertices,
                uvs,
                triangles,
                -outerX,
                -outerY,
                outerX,
                -innerY);
            AddPitWallRect(
                vertices,
                uvs,
                triangles,
                -outerX,
                -innerY,
                -innerX,
                innerY);
            AddPitWallRect(
                vertices,
                uvs,
                triangles,
                innerX,
                -innerY,
                outerX,
                innerY);
        }

        private static void AddPitWallQuadTriangles(
            List<int> triangles,
            int start)
        {
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 1);
            triangles.Add(start);
            triangles.Add(start + 3);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }

        private static float ResolvePitHeaderHeight(
            Vector2 size,
            PitWallOverlayLayout layout)
        {
            return Mathf.Clamp(
                size.y * (layout == PitWallOverlayLayout.Full
                    ? 0.14f
                    : 0.12f),
                0.16f,
                0.34f);
        }

        private static string ResolvePitWallStatus(
            PitStopPresentationState state)
        {
            if (state.IsDriveThrough)
            {
                return state.Phase == PitStopPhase.Exit
                    ? "PIT LANE CLEAR"
                    : "DRIVE THROUGH";
            }

            return state.Phase switch
            {
                PitStopPhase.Approach => "INBOUND",
                PitStopPhase.Brake => "BOX",
                PitStopPhase.Service => "SERVICE",
                PitStopPhase.Release => "RELEASE",
                _ => "PIT STOP COMPLETE"
            };
        }

        private static int ResolvePitWallTimerTick(
            PitStopPresentationState state)
        {
            if (state.IsDriveThrough ||
                state.Phase == PitStopPhase.Approach ||
                state.Phase == PitStopPhase.Brake)
            {
                return 0;
            }

            return state.Phase == PitStopPhase.Service
                ? Mathf.FloorToInt(
                    state.ServiceElapsedSeconds *
                    PitWallTimerUpdatesPerSecond + 0.0001f)
                : Mathf.RoundToInt(state.ServiceTotalSeconds * 1000f);
        }

        private static string ResolvePitWallTimer(
            PitStopPresentationState state,
            int timerTick)
        {
            if (state.IsDriveThrough)
                return "NO SERVICE";
            if (state.Phase == PitStopPhase.Approach ||
                state.Phase == PitStopPhase.Brake)
            {
                return "READY";
            }

            float seconds = state.Phase == PitStopPhase.Service
                ? timerTick / PitWallTimerUpdatesPerSecond
                : state.ServiceTotalSeconds;
            return $"{(state.IsReconstructed ? "~" : "")}" +
                   $"{seconds:0.000} s";
        }

        private static Color ResolvePitWallColor(
            PitStopPresentationState state,
            Color teamColor)
        {
            return state.Phase switch
            {
                PitStopPhase.Brake =>
                    Color.Lerp(teamColor, Color.white, state.PhaseProgress),
                PitStopPhase.Service =>
                    Color.Lerp(
                        teamColor,
                        Color.white,
                        0.25f +
                        Mathf.Sin(state.ServiceProgress * Mathf.PI * 8f) *
                        0.18f),
                PitStopPhase.Release =>
                    Color.Lerp(
                        Color.white,
                        new Color(0.18f, 1f, 0.36f, 0.96f),
                        state.PhaseProgress),
                PitStopPhase.Exit =>
                    new Color(0.18f, 1f, 0.36f, 0.72f),
                _ => new Color(
                    teamColor.r,
                    teamColor.g,
                    teamColor.b,
                    0.78f)
            };
        }

        private static int ComputePitWallLabelHash(
            PitWallOverlayLabels labels)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + labels.Team.GetHashCode();
                hash = hash * 31 + labels.Driver.GetHashCode();
                hash = hash * 31 + labels.Lap;
                hash = hash * 31 +
                    (labels.Font != null
                        ? labels.Font.GetHashCode()
                        : 0);
                return hash;
            }
        }
    }
}
