using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace F1XR.RestAPI.Replay.Room
{
    [DefaultExecutionOrder(1100)]
    [DisallowMultipleComponent]
    public sealed class ShowcasePortalPresentation : MonoBehaviour
    {
        private const int PortalSceneLayer = 30;
        private const int PortalSurfaceLayer = 2;
        private const int TextureSize = 512;
        private const float FallbackPortalWidth = 2.4f;
        private const float FallbackPortalHeight = 1.8f;
        private const float MinimumPortalWidth = 1.8f;
        private const float MinimumPortalHeight = 1.5f;
        private const float MaximumPortalWidth = 4f;
        private const float MaximumPortalHeight = 2.8f;
        private const float PortalWallFill = 0.9f;
        private const float PortalWallMargin = 0.1f;
        private const float PortalBottomOffset = 0.02f;
        private const float PortalClipTolerance = 0.12f;
        private const float PortalRoadOverlap = 0.08f;
        private const float PortalApertureCropDepth = 2f;
        private const float PortalApertureExpansion = 1f;
        private const float RoomPlaneTolerance = 0.06f;

        private readonly List<LayerBinding> sourceLayers = new();
        private readonly HashSet<Transform> capturedLayerTransforms = new();
        private readonly List<RendererBinding> roomCarRenderers = new();
        private readonly List<GameObject> rendererProxies = new();
        private readonly List<Mesh> runtimeMeshes = new();
        private readonly List<Material> runtimeMaterials = new();
        private readonly List<RenderTexture> renderTextures = new();

        private Transform presentationRoot;
        private Transform firstVehicle;
        private Transform secondVehicle;
        private Camera viewerCamera;
        private Camera entryCamera;
        private Camera exitCamera;
        private Transform entrySurface;
        private Transform exitSurface;
        private Vector3 entryPosition;
        private Vector3 entryInward;
        private Vector3 exitPosition;
        private Vector3 exitInward;
        private Vector2 entryPortalSize;
        private Vector2 exitPortalSize;
        private Vector3 entryPortalRight;
        private Vector3 exitPortalRight;
        private int originalViewerMask;
        private int includedRoomTrackSubmeshes;
        private int excludedRoomTrackSubmeshes;
        private bool viewerMaskCaptured;
        private bool configured;

        public bool IsConfigured => configured;
        public int AuthoritativeVehicleCount =>
            configured && firstVehicle != null && secondVehicle != null
                ? 2
                : 0;

        public bool Configure(
            Transform stage,
            ShowcaseLayout layout,
            Transform firstVehicleRoot,
            Transform secondVehicleRoot,
            out string failure)
        {
            Clear();
            failure = "";

            if (stage == null ||
                layout == null ||
                !layout.IsLayoutValid ||
                firstVehicleRoot == null ||
                secondVehicleRoot == null)
            {
                failure =
                    "The stage, selected walls, or authoritative vehicles are unavailable.";
                return false;
            }

            viewerCamera = Camera.main;
            if (viewerCamera == null)
            {
                failure = "The XR main camera is unavailable.";
                return false;
            }

            entryPosition = layout.EntryPose.position;
            entryInward = Flat(layout.EntryTravelDirection);
            exitPosition = layout.ExitPose.position;
            exitInward = Flat(-layout.ExitTravelDirection);
            if (entryInward.sqrMagnitude <= 0.000001f ||
                exitInward.sqrMagnitude <= 0.000001f)
            {
                failure =
                    "The selected Entry or Exit wall has no stable inward normal.";
                return false;
            }

            entryInward.Normalize();
            exitInward.Normalize();
            bool hasEntryGeometry =
                layout.TryGetEntryWallGeometry(
                    out Vector2 entryWallSize,
                    out Vector3 entryWallBottom,
                    out Vector3 entryWallUp);
            bool hasExitGeometry =
                layout.TryGetExitWallGeometry(
                    out Vector2 exitWallSize,
                    out Vector3 exitWallBottom,
                    out Vector3 exitWallUp);
            entryPortalSize = ResolvePortalSize(
                hasEntryGeometry
                    ? entryWallSize
                    : Vector2.zero);
            exitPortalSize = ResolvePortalSize(
                hasExitGeometry
                    ? exitWallSize
                    : Vector2.zero);
            Pose entryPortalPose = ResolvePortalPose(
                layout.EntryPose,
                entryPortalSize,
                hasEntryGeometry,
                entryWallBottom,
                entryWallUp);
            Pose exitPortalPose = ResolvePortalPose(
                layout.ExitPose,
                exitPortalSize,
                hasExitGeometry,
                exitWallBottom,
                exitWallUp);
            bool hasEntryRoadHeight =
                TryResolveRoadSeamHeight(
                    stage,
                    entryPosition,
                    entryInward,
                    entryPortalPose.right,
                    entryPortalSize.x * 0.5f,
                    out float entryRoadHeight);
            bool hasExitRoadHeight =
                TryResolveRoadSeamHeight(
                    stage,
                    exitPosition,
                    exitInward,
                    exitPortalPose.right,
                    exitPortalSize.x * 0.5f,
                    out float exitRoadHeight);
            if (hasEntryRoadHeight)
            {
                entryPortalPose = AlignPortalBottom(
                    entryPortalPose,
                    entryPortalSize,
                    entryRoadHeight - PortalRoadOverlap);
            }
            if (hasExitRoadHeight)
            {
                exitPortalPose = AlignPortalBottom(
                    exitPortalPose,
                    exitPortalSize,
                    exitRoadHeight - PortalRoadOverlap);
            }
            entryPortalRight = entryPortalPose.right;
            exitPortalRight = exitPortalPose.right;
            firstVehicle = firstVehicleRoot;
            secondVehicle = secondVehicleRoot;

            presentationRoot =
                new GameObject("WallRoomWallPresentation").transform;
            presentationRoot.SetParent(transform, true);

            originalViewerMask = viewerCamera.cullingMask;
            viewerMaskCaptured = true;
            viewerCamera.cullingMask &=
                ~(1 << PortalSceneLayer);

            CaptureAndHideSourceRenderers(stage);
            int roomTrackRendererCount =
                CreateRoomTrackRenderers(stage);
            int firstProxyCount =
                CreateRoomVehicleRenderers(firstVehicle);
            int secondProxyCount =
                CreateRoomVehicleRenderers(secondVehicle);

            entrySurface = CreatePortal(
                "EntryPortal",
                entryPortalPose,
                entryPortalSize,
                out entryCamera);
            exitSurface = CreatePortal(
                "ExitPortal",
                exitPortalPose,
                exitPortalSize,
                out exitCamera);

            if (entrySurface == null ||
                exitSurface == null ||
                entryCamera == null ||
                exitCamera == null ||
                roomTrackRendererCount == 0 ||
                firstProxyCount == 0 ||
                secondProxyCount == 0)
            {
                failure =
                    "Portal surfaces or render-only room geometry could not be built.";
                Clear();
                return false;
            }

            configured = true;
            Debug.Log(
                $"[RoomTrackFilter] renderers={roomTrackRendererCount}, " +
                $"includedSubmeshes={includedRoomTrackSubmeshes}, " +
                $"excludedSubmeshes={excludedRoomTrackSubmeshes}, " +
                $"roadSeams={FormatHeight(entryRoadHeight, hasEntryRoadHeight)}/" +
                $"{FormatHeight(exitRoadHeight, hasExitRoadHeight)}",
                this);
            UpdatePortalView(
                entryCamera,
                entrySurface,
                entryPortalSize);
            UpdatePortalView(
                exitCamera,
                exitSurface,
                exitPortalSize);
            RefreshRoomVehicleVisibility();
            return true;
        }

        public void Clear()
        {
            configured = false;

            if (viewerMaskCaptured && viewerCamera != null)
                viewerCamera.cullingMask = originalViewerMask;

            viewerMaskCaptured = false;
            viewerCamera = null;
            entryCamera = null;
            exitCamera = null;
            entrySurface = null;
            exitSurface = null;
            firstVehicle = null;
            secondVehicle = null;
            entryPortalRight = Vector3.zero;
            exitPortalRight = Vector3.zero;
            includedRoomTrackSubmeshes = 0;
            excludedRoomTrackSubmeshes = 0;

            for (int i = 0; i < sourceLayers.Count; i++)
            {
                LayerBinding binding = sourceLayers[i];
                if (binding.Transform != null)
                    binding.Transform.gameObject.layer = binding.Layer;
            }
            sourceLayers.Clear();
            capturedLayerTransforms.Clear();
            roomCarRenderers.Clear();

            for (int i = 0; i < rendererProxies.Count; i++)
            {
                if (rendererProxies[i] != null)
                    Destroy(rendererProxies[i]);
            }
            rendererProxies.Clear();

            if (presentationRoot != null)
                Destroy(presentationRoot.gameObject);
            presentationRoot = null;

            for (int i = 0; i < renderTextures.Count; i++)
            {
                RenderTexture texture = renderTextures[i];
                if (texture == null)
                    continue;

                texture.Release();
                Destroy(texture);
            }
            renderTextures.Clear();

            for (int i = 0; i < runtimeMaterials.Count; i++)
            {
                if (runtimeMaterials[i] != null)
                    Destroy(runtimeMaterials[i]);
            }
            runtimeMaterials.Clear();

            for (int i = 0; i < runtimeMeshes.Count; i++)
            {
                if (runtimeMeshes[i] != null)
                    Destroy(runtimeMeshes[i]);
            }
            runtimeMeshes.Clear();
        }

        private void LateUpdate()
        {
            if (!configured)
                return;

            UpdatePortalView(
                entryCamera,
                entrySurface,
                entryPortalSize);
            UpdatePortalView(
                exitCamera,
                exitSurface,
                exitPortalSize);
            RefreshRoomVehicleVisibility();
        }

        private void OnDisable()
        {
            Clear();
        }

        private void OnDestroy()
        {
            Clear();
        }

        private void CaptureAndHideSourceRenderers(Transform stage)
        {
            Renderer[] renderers =
                stage.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                Transform current = renderer.transform;
                if (!capturedLayerTransforms.Add(current))
                    continue;

                sourceLayers.Add(new LayerBinding(
                    current,
                    current.gameObject.layer));
                current.gameObject.layer = PortalSceneLayer;
            }
        }

        private int CreateRoomTrackRenderers(Transform stage)
        {
            Transform carsRoot = stage.Find("Cars");
            MeshFilter[] filters =
                stage.GetComponentsInChildren<MeshFilter>(true);
            int created = 0;

            for (int i = 0; i < filters.Length; i++)
            {
                MeshFilter filter = filters[i];
                if (filter == null ||
                    filter.sharedMesh == null ||
                    (carsRoot != null &&
                     filter.transform.IsChildOf(carsRoot)))
                {
                    continue;
                }

                MeshRenderer sourceRenderer =
                    filter.GetComponent<MeshRenderer>();
                if (sourceRenderer == null)
                    continue;

                Mesh clipped = CreateRoomMesh(
                    filter.sharedMesh,
                    filter.transform,
                    sourceRenderer);
                if (clipped == null)
                    continue;

                GameObject proxy =
                    new GameObject($"RoomTrackProxy_{filter.name}");
                proxy.layer = PortalSurfaceLayer;
                proxy.transform.SetParent(
                    filter.transform,
                    false);
                proxy.transform.localPosition = Vector3.zero;
                proxy.transform.localRotation = Quaternion.identity;
                proxy.transform.localScale = Vector3.one;
                rendererProxies.Add(proxy);

                MeshFilter proxyFilter =
                    proxy.AddComponent<MeshFilter>();
                proxyFilter.sharedMesh = clipped;
                MeshRenderer proxyRenderer =
                    proxy.AddComponent<MeshRenderer>();
                CopyRendererSettings(
                    sourceRenderer,
                    proxyRenderer);
                created++;
            }

            return created;
        }

        private int CreateRoomVehicleRenderers(
            Transform vehicleRoot)
        {
            Renderer[] sources =
                vehicleRoot.GetComponentsInChildren<Renderer>(true);
            int created = 0;

            for (int i = 0; i < sources.Length; i++)
            {
                Renderer source = sources[i];
                if (source is MeshRenderer meshRenderer)
                {
                    MeshFilter sourceFilter =
                        source.GetComponent<MeshFilter>();
                    if (sourceFilter == null ||
                        sourceFilter.sharedMesh == null)
                    {
                        continue;
                    }

                    GameObject proxy =
                        CreateRendererProxyObject(source);
                    MeshFilter proxyFilter =
                        proxy.AddComponent<MeshFilter>();
                    proxyFilter.sharedMesh =
                        sourceFilter.sharedMesh;
                    MeshRenderer proxyRenderer =
                        proxy.AddComponent<MeshRenderer>();
                    CopyRendererSettings(
                        meshRenderer,
                        proxyRenderer);
                    roomCarRenderers.Add(
                        new RendererBinding(
                            source,
                            proxyRenderer,
                            vehicleRoot));
                    created++;
                }
                else if (source is SkinnedMeshRenderer skinned &&
                         skinned.sharedMesh != null)
                {
                    GameObject proxy =
                        CreateRendererProxyObject(source);
                    SkinnedMeshRenderer proxyRenderer =
                        proxy.AddComponent<SkinnedMeshRenderer>();
                    proxyRenderer.sharedMesh =
                        skinned.sharedMesh;
                    proxyRenderer.bones = skinned.bones;
                    proxyRenderer.rootBone = skinned.rootBone;
                    proxyRenderer.localBounds =
                        skinned.localBounds;
                    proxyRenderer.updateWhenOffscreen =
                        skinned.updateWhenOffscreen;
                    CopyRendererSettings(
                        skinned,
                        proxyRenderer);
                    roomCarRenderers.Add(
                        new RendererBinding(
                            source,
                            proxyRenderer,
                            vehicleRoot));
                    created++;
                }
            }

            return created;
        }

        private GameObject CreateRendererProxyObject(
            Renderer source)
        {
            GameObject proxy =
                new GameObject($"RoomVehicleProxy_{source.name}");
            proxy.layer = PortalSurfaceLayer;
            proxy.transform.SetParent(
                source.transform,
                false);
            proxy.transform.localPosition = Vector3.zero;
            proxy.transform.localRotation = Quaternion.identity;
            proxy.transform.localScale = Vector3.one;
            rendererProxies.Add(proxy);
            return proxy;
        }

        private Mesh CreateRoomMesh(
            Mesh source,
            Transform sourceTransform,
            MeshRenderer sourceRenderer)
        {
            if (!source.isReadable)
                return null;

            Vector3[] vertices = source.vertices;
            Mesh copy = Instantiate(source);
            copy.name = $"{source.name}_Room";
            int keptTriangles = 0;

            for (int submesh = 0;
                 submesh < source.subMeshCount;
                 submesh++)
            {
                if (!ShouldIncludeRoomTrackSubmesh(
                        sourceRenderer,
                        submesh))
                {
                    copy.SetIndices(
                        System.Array.Empty<int>(),
                        source.GetTopology(submesh),
                        submesh,
                        false);
                    excludedRoomTrackSubmeshes++;
                    continue;
                }

                if (source.GetTopology(submesh) !=
                    MeshTopology.Triangles)
                {
                    copy.SetIndices(
                        System.Array.Empty<int>(),
                        source.GetTopology(submesh),
                        submesh,
                        false);
                    continue;
                }

                includedRoomTrackSubmeshes++;
                int[] sourceIndices =
                    source.GetIndices(submesh);
                List<int> kept =
                    new List<int>(sourceIndices.Length);
                for (int index = 0;
                     index + 2 < sourceIndices.Length;
                     index += 3)
                {
                    int a = sourceIndices[index];
                    int b = sourceIndices[index + 1];
                    int c = sourceIndices[index + 2];
                    Vector3 worldA =
                        sourceTransform.TransformPoint(vertices[a]);
                    Vector3 worldB =
                        sourceTransform.TransformPoint(vertices[b]);
                    Vector3 worldC =
                        sourceTransform.TransformPoint(vertices[c]);
                    if (!TriangleTouchesRoom(
                            worldA,
                            worldB,
                            worldC))
                    {
                        continue;
                    }

                    kept.Add(a);
                    kept.Add(b);
                    kept.Add(c);
                    keptTriangles++;
                }

                copy.SetIndices(
                    kept,
                    MeshTopology.Triangles,
                    submesh,
                    false);
            }

            if (keptTriangles == 0)
            {
                Destroy(copy);
                return null;
            }

            copy.RecalculateBounds();
            runtimeMeshes.Add(copy);
            return copy;
        }

        private static bool ShouldIncludeRoomTrackSubmesh(
            MeshRenderer renderer,
            int submesh)
        {
            if (renderer == null)
                return false;

            string rendererName =
                renderer.name.ToLowerInvariant();
            if (rendererName.Contains("eventroad"))
                return true;
            if (rendererName.Contains("rect_fill") ||
                rendererName.Contains("rect_wall") ||
                rendererName.Contains("ferris") ||
                rendererName.Contains("cube"))
            {
                return false;
            }

            Material[] materials =
                renderer.sharedMaterials;
            if (materials == null ||
                materials.Length == 0)
            {
                return false;
            }

            Material material =
                materials[Mathf.Min(
                    submesh,
                    materials.Length - 1)];
            if (material == null)
                return false;

            string name =
                material.name.ToLowerInvariant();
            if (name.Contains("pit") ||
                name.Contains("grass") ||
                name.Contains("ground") ||
                name.Contains("grvl") ||
                name.Contains("gravel") ||
                name.Contains("terrain") ||
                name.Contains("tree") ||
                name.Contains("forest"))
            {
                return false;
            }

            return name.Contains("road") ||
                name.Contains("asphalt") ||
                name.Contains("tarmac") ||
                name.Contains("track") ||
                name.Contains("curb") ||
                name.Contains("kerb") ||
                name.Contains("rumble") ||
                name.Contains("rmbl") ||
                name.Contains("rdcp") ||
                name.Contains("skid") ||
                name.Contains("groove") ||
                name.Contains("runoff") ||
                name.Contains("pitexitline") ||
                name == "grid" ||
                name.StartsWith("line");
        }

        private bool TriangleTouchesRoom(
            Vector3 a,
            Vector3 b,
            Vector3 c)
        {
            Vector3 center = (a + b + c) / 3f;
            bool touchesRoom =
                IsInsideRoom(a) ||
                IsInsideRoom(b) ||
                IsInsideRoom(c) ||
                IsInsideRoom(center);
            return touchesRoom &&
                IsInsidePortalApertures(center);
        }

        private bool IsInsideRoom(Vector3 position)
        {
            return Vector3.Dot(
                       position - entryPosition,
                       entryInward) >=
                   -RoomPlaneTolerance &&
                Vector3.Dot(
                       position - exitPosition,
                       exitInward) >=
                   -RoomPlaneTolerance;
        }

        private bool IsInsidePortalApertures(
            Vector3 position)
        {
            return IsInsidePortalAperture(
                    position,
                    entryPosition,
                    entryInward,
                    entryPortalRight,
                    entryPortalSize.x * 0.5f) &&
                IsInsidePortalAperture(
                    position,
                    exitPosition,
                    exitInward,
                    exitPortalRight,
                    exitPortalSize.x * 0.5f);
        }

        private static bool IsInsidePortalAperture(
            Vector3 position,
            Vector3 portalPosition,
            Vector3 roomInward,
            Vector3 portalRight,
            float halfWidth)
        {
            float depth = Vector3.Dot(
                position - portalPosition,
                roomInward);
            if (depth >= PortalApertureCropDepth)
                return true;

            float allowedHalfWidth =
                halfWidth +
                Mathf.Max(0f, depth) *
                PortalApertureExpansion;
            float lateral = Mathf.Abs(
                Vector3.Dot(
                    position - portalPosition,
                    portalRight));
            return lateral <= allowedHalfWidth;
        }

        private Transform CreatePortal(
            string name,
            Pose pose,
            Vector2 size,
            out Camera portalCamera)
        {
            RenderTexture texture = new RenderTexture(
                TextureSize,
                TextureSize,
                24,
                RenderTextureFormat.ARGB32)
            {
                name = $"{name}Texture",
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false
            };
            texture.Create();
            renderTextures.Add(texture);

            GameObject surface =
                new GameObject($"{name}Surface");
            surface.layer = PortalSurfaceLayer;
            surface.transform.SetParent(
                presentationRoot,
                false);
            surface.transform.SetPositionAndRotation(
                pose.position,
                pose.rotation);

            MeshFilter filter =
                surface.AddComponent<MeshFilter>();
            bool mirrorHorizontally =
                Vector3.Dot(
                    pose.position -
                    viewerCamera.transform.position,
                    pose.forward) < 0f;
            filter.sharedMesh = CreatePortalMesh(
                name,
                size,
                mirrorHorizontally);
            MeshRenderer renderer =
                surface.AddComponent<MeshRenderer>();
            renderer.sharedMaterial =
                CreatePortalMaterial(name, texture);
            renderer.shadowCastingMode =
                ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            GameObject cameraObject =
                new GameObject($"{name}Camera");
            cameraObject.layer = PortalSurfaceLayer;
            cameraObject.transform.SetParent(
                presentationRoot,
                false);
            portalCamera =
                cameraObject.AddComponent<Camera>();
            portalCamera.enabled = true;
            portalCamera.stereoTargetEye =
                StereoTargetEyeMask.None;
            portalCamera.cullingMask =
                1 << PortalSceneLayer;
            portalCamera.clearFlags =
                CameraClearFlags.SolidColor;
            portalCamera.backgroundColor =
                new Color(0.015f, 0.025f, 0.04f, 1f);
            portalCamera.allowHDR = false;
            portalCamera.allowMSAA = false;
            portalCamera.useOcclusionCulling = false;
            portalCamera.depth =
                viewerCamera.depth - 10f;
            portalCamera.targetTexture = texture;

            return surface.transform;
        }

        private Mesh CreatePortalMesh(
            string name,
            Vector2 size,
            bool mirrorHorizontally)
        {
            float halfWidth = size.x * 0.5f;
            float halfHeight = size.y * 0.5f;
            float leftU = mirrorHorizontally ? 1f : 0f;
            float rightU = mirrorHorizontally ? 0f : 1f;
            Mesh mesh = new Mesh
            {
                name = $"{name}Mesh",
                vertices = new[]
                {
                    new Vector3(-halfWidth, -halfHeight, 0f),
                    new Vector3(halfWidth, -halfHeight, 0f),
                    new Vector3(-halfWidth, halfHeight, 0f),
                    new Vector3(halfWidth, halfHeight, 0f)
                },
                uv = new[]
                {
                    new Vector2(leftU, 0f),
                    new Vector2(rightU, 0f),
                    new Vector2(leftU, 1f),
                    new Vector2(rightU, 1f)
                },
                triangles = new[]
                {
                    0, 2, 1,
                    1, 2, 3,
                    1, 2, 0,
                    3, 2, 1
                }
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            runtimeMeshes.Add(mesh);
            return mesh;
        }

        private Material CreatePortalMaterial(
            string name,
            RenderTexture texture)
        {
            Shader shader =
                Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Texture");
            if (shader == null)
                return null;

            Material material =
                new Material(shader)
                {
                    name = $"{name}Material"
                };
            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", texture);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", Color.white);
            runtimeMaterials.Add(material);
            return material;
        }

        private void UpdatePortalView(
            Camera portalCamera,
            Transform surface,
            Vector2 size)
        {
            if (viewerCamera == null ||
                portalCamera == null ||
                surface == null)
            {
                return;
            }

            Vector3 eye = viewerCamera.transform.position;
            Vector3 center = surface.position;
            Vector3 surfaceForward = surface.forward.normalized;
            Vector3 cameraForward =
                Vector3.Dot(center - eye, surfaceForward) >= 0f
                    ? surfaceForward
                    : -surfaceForward;
            float portalDistance =
                Vector3.Dot(center - eye, cameraForward);
            if (portalDistance <= 0.03f)
            {
                portalCamera.enabled = false;
                return;
            }

            portalCamera.enabled = true;
            Vector3 up = surface.up;
            if (Vector3.Cross(cameraForward, up).sqrMagnitude <=
                0.000001f)
            {
                up = Vector3.up;
            }

            portalCamera.transform.SetPositionAndRotation(
                eye,
                Quaternion.LookRotation(cameraForward, up));

            Vector3 right = surface.right * (size.x * 0.5f);
            Vector3 vertical =
                surface.up * (size.y * 0.5f);
            Vector3[] corners =
            {
                center - right - vertical,
                center + right - vertical,
                center - right + vertical,
                center + right + vertical
            };

            float minimumZ = float.PositiveInfinity;
            float minimumX = float.PositiveInfinity;
            float maximumX = float.NegativeInfinity;
            float minimumY = float.PositiveInfinity;
            float maximumY = float.NegativeInfinity;
            Vector3[] localCorners = new Vector3[4];
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 local =
                    portalCamera.transform.InverseTransformPoint(
                        corners[i]);
                localCorners[i] = local;
                minimumZ = Mathf.Min(minimumZ, local.z);
            }

            if (minimumZ <= 0.01f)
            {
                portalCamera.enabled = false;
                return;
            }

            float near = Mathf.Max(
                0.03f,
                portalDistance - PortalClipTolerance);
            for (int i = 0; i < localCorners.Length; i++)
            {
                Vector3 local = localCorners[i];
                float x = near * local.x / local.z;
                float y = near * local.y / local.z;
                minimumX = Mathf.Min(minimumX, x);
                maximumX = Mathf.Max(maximumX, x);
                minimumY = Mathf.Min(minimumY, y);
                maximumY = Mathf.Max(maximumY, y);
            }

            portalCamera.nearClipPlane = near;
            portalCamera.farClipPlane = Mathf.Max(
                near + 1f,
                viewerCamera.farClipPlane);
            portalCamera.projectionMatrix = Matrix4x4.Frustum(
                minimumX,
                maximumX,
                minimumY,
                maximumY,
                near,
                portalCamera.farClipPlane);
        }

        private static Vector2 ResolvePortalSize(
            Vector2 wallSize)
        {
            if (wallSize.x <= 0f || wallSize.y <= 0f)
            {
                return new Vector2(
                    FallbackPortalWidth,
                    FallbackPortalHeight);
            }

            float availableWidth = Mathf.Max(
                0.1f,
                wallSize.x - PortalWallMargin * 2f);
            float availableHeight = Mathf.Max(
                0.1f,
                wallSize.y - PortalWallMargin * 2f);
            float width = Mathf.Min(
                availableWidth,
                Mathf.Clamp(
                    wallSize.x * PortalWallFill,
                    MinimumPortalWidth,
                    MaximumPortalWidth));
            float height = Mathf.Min(
                availableHeight,
                Mathf.Clamp(
                    wallSize.y * PortalWallFill,
                    MinimumPortalHeight,
                    MaximumPortalHeight));
            return new Vector2(width, height);
        }

        private static Pose ResolvePortalPose(
            Pose wallPose,
            Vector2 portalSize,
            bool hasWallGeometry,
            Vector3 wallBottom,
            Vector3 wallUp)
        {
            if (!hasWallGeometry)
                return wallPose;

            wallUp.Normalize();
            Vector3 desiredCenter =
                wallBottom +
                wallUp *
                (PortalBottomOffset + portalSize.y * 0.5f);
            float verticalShift =
                Vector3.Dot(
                    desiredCenter - wallPose.position,
                    wallUp);
            wallPose.position += wallUp * verticalShift;
            return wallPose;
        }

        private static bool TryResolveRoadSeamHeight(
            Transform stage,
            Vector3 portalPosition,
            Vector3 roomInward,
            Vector3 portalRight,
            float halfWidth,
            out float height)
        {
            height = 0f;
            if (stage == null)
                return false;

            Transform carsRoot = stage.Find("Cars");
            MeshFilter[] filters =
                stage.GetComponentsInChildren<MeshFilter>(true);
            float heightSum = 0f;
            int sampleCount = 0;

            for (int filterIndex = 0;
                 filterIndex < filters.Length;
                 filterIndex++)
            {
                MeshFilter filter = filters[filterIndex];
                if (filter == null ||
                    filter.sharedMesh == null ||
                    !filter.sharedMesh.isReadable ||
                    (carsRoot != null &&
                     filter.transform.IsChildOf(carsRoot)))
                {
                    continue;
                }

                MeshRenderer renderer =
                    filter.GetComponent<MeshRenderer>();
                if (renderer == null)
                    continue;

                Mesh mesh = filter.sharedMesh;
                Vector3[] vertices = mesh.vertices;
                for (int submesh = 0;
                     submesh < mesh.subMeshCount;
                     submesh++)
                {
                    if (mesh.GetTopology(submesh) !=
                            MeshTopology.Triangles ||
                        !IsMainRoadSubmesh(renderer, submesh))
                    {
                        continue;
                    }

                    int[] indices = mesh.GetIndices(submesh);
                    for (int index = 0;
                         index < indices.Length;
                         index++)
                    {
                        Vector3 world =
                            filter.transform.TransformPoint(
                                vertices[indices[index]]);
                        Vector3 offset = world - portalPosition;
                        float depth =
                            Vector3.Dot(offset, roomInward);
                        if (depth < -PortalClipTolerance ||
                            depth > 0.75f)
                        {
                            continue;
                        }

                        float lateral = Mathf.Abs(
                            Vector3.Dot(offset, portalRight));
                        if (lateral > halfWidth)
                            continue;

                        heightSum += world.y;
                        sampleCount++;
                    }
                }
            }

            if (sampleCount < 3)
                return false;

            height = heightSum / sampleCount;
            return !float.IsNaN(height) &&
                !float.IsInfinity(height);
        }

        private static bool IsMainRoadSubmesh(
            MeshRenderer renderer,
            int submesh)
        {
            if (renderer == null)
                return false;

            string rendererName =
                renderer.name.ToLowerInvariant();
            if (rendererName.Contains("eventroad"))
                return true;

            Material[] materials = renderer.sharedMaterials;
            if (materials == null ||
                materials.Length == 0)
            {
                return false;
            }

            Material material =
                materials[Mathf.Min(
                    submesh,
                    materials.Length - 1)];
            if (material == null)
                return false;

            string name = material.name.ToLowerInvariant();
            if (name.Contains("pit") ||
                name.Contains("grass") ||
                name.Contains("ground") ||
                name.Contains("gravel") ||
                name.Contains("runoff"))
            {
                return false;
            }

            return name.Contains("road") ||
                name.Contains("asphalt") ||
                name.Contains("tarmac");
        }

        private static Pose AlignPortalBottom(
            Pose pose,
            Vector2 portalSize,
            float desiredBottomHeight)
        {
            Vector3 up = pose.up.normalized;
            if (Mathf.Abs(up.y) <= 0.0001f)
                return pose;

            Vector3 currentBottom =
                pose.position -
                up * (portalSize.y * 0.5f);
            float shift =
                (desiredBottomHeight - currentBottom.y) /
                up.y;
            pose.position += up * shift;
            return pose;
        }

        private static string FormatHeight(
            float height,
            bool isValid)
        {
            return isValid
                ? height.ToString("0.###")
                : "n/a";
        }

        private void RefreshRoomVehicleVisibility()
        {
            for (int i = 0; i < roomCarRenderers.Count; i++)
            {
                RendererBinding binding =
                    roomCarRenderers[i];
                bool visible =
                    binding.Source != null &&
                    binding.Proxy != null &&
                    binding.VehicleRoot != null &&
                    IsInsideRoom(
                        binding.VehicleRoot.position) &&
                    binding.Source.enabled &&
                    binding.Source.gameObject.activeInHierarchy;
                if (binding.Proxy != null)
                    binding.Proxy.enabled = visible;
            }
        }

        private static void CopyRendererSettings(
            Renderer source,
            Renderer destination)
        {
            destination.sharedMaterials =
                source.sharedMaterials;
            destination.shadowCastingMode =
                source.shadowCastingMode;
            destination.receiveShadows =
                source.receiveShadows;
            destination.lightProbeUsage =
                LightProbeUsage.Off;
            destination.reflectionProbeUsage =
                ReflectionProbeUsage.Off;
            destination.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;

            MaterialPropertyBlock block =
                new MaterialPropertyBlock();
            source.GetPropertyBlock(block);
            destination.SetPropertyBlock(block);
        }

        private static Vector3 Flat(Vector3 value)
        {
            value.y = 0f;
            return value;
        }

        private readonly struct LayerBinding
        {
            public readonly Transform Transform;
            public readonly int Layer;

            public LayerBinding(
                Transform transform,
                int layer)
            {
                Transform = transform;
                Layer = layer;
            }
        }

        private readonly struct RendererBinding
        {
            public readonly Renderer Source;
            public readonly Renderer Proxy;
            public readonly Transform VehicleRoot;

            public RendererBinding(
                Renderer source,
                Renderer proxy,
                Transform vehicleRoot)
            {
                Source = source;
                Proxy = proxy;
                VehicleRoot = vehicleRoot;
            }
        }
    }
}
