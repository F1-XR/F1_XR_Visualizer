using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace F1XR.RestAPI.Replay.Room
{
    [DefaultExecutionOrder(1100)]
    [DisallowMultipleComponent]
    public sealed partial class ShowcasePortalPresentation : MonoBehaviour
    {
        private const int PortalSceneLayer = 30;
        private const int PortalSurfaceLayer = 2;
        private const int TextureSize = 512;
        private const int PitTextureMinimumWidth = 1280;
        private const int PitTextureMinimumHeight = 768;
        private const int PitTextureMaximumDimension = 2048;
        private const float PitTexturePixelsPerMeter = 520f;
        private const int TextureDepthBits = 16;
        private const int PortalArchSegments = 40;
        private const float FallbackPortalWidth = 2.8f;
        private const float FallbackPortalHeight = 2.1f;
        private const float MinimumPortalWidth = 2f;
        private const float MinimumPortalHeight = 1.7f;
        private const float MaximumPortalWidth = 5.5f;
        private const float MaximumPortalHeight = 3.5f;
        private const float MaximumPitPortalWidth = 8f;
        private const float MaximumPitPortalHeight = 4f;
        private const float PitPortalWallFill = 0.96f;
        private const float PortalWallFill = 1.1f;
        private const float MaximumPortalSideOverflow = 0.35f;
        private const float MaximumPortalTopOverflow = 0.5f;
        private const float PortalBottomOffset = 0.02f;
        private const float PortalClipTolerance = 0.12f;
        private const float PortalRoadOverlap = 0.08f;
        private const float PortalApertureCropDepth = 2f;
        private const float PortalApertureExpansion = 1f;
        private const float RoomPlaneTolerance = 0.06f;
        private const float TrackExitLongitudinalTolerance = 0.15f;
        internal const float ImmersiveMaximumScale = 3f;
        private const float ImmersiveScaleRampDistance = 3f;

        private readonly List<LayerBinding> sourceLayers = new();
        private readonly HashSet<Transform> capturedLayerTransforms = new();
        private readonly List<LabelRendererState> labelRendererStates = new();
        private readonly List<RendererBinding> roomCarRenderers = new();
        private readonly List<TmpRendererBinding> roomCarTmpRenderers = new();
        private readonly List<GameObject> rendererProxies = new();
        private readonly List<Mesh> runtimeMeshes = new();
        private readonly List<Material> runtimeMaterials = new();
        private readonly Dictionary<Renderer, Material[]>
            roomTrackOriginalMaterials = new();
        private readonly Dictionary<Renderer, Material[]>
            roomVehicleOriginalMaterials = new();
        private readonly HashSet<Material>
            suppressedRoomOccluderMaterials = new();
        private readonly HashSet<Material>
            desiredRoomOccluderMaterials = new();
        private readonly HashSet<Material>
            plannedRoomOccluderMaterials = new();
        private readonly HashSet<Material>
            combinedRoomOccluderMaterials = new();
        private readonly List<RenderTexture> renderTextures = new();
        private readonly List<ARPlaneMeshVisualizer> suspendedPlaneVisualizers = new();
        private readonly List<ARPlaneManager> subscribedPlaneManagers = new();
        private readonly List<Vector3> roomTrackLeftBoundary = new();
        private readonly List<Vector3> roomTrackRightBoundary = new();
        private readonly List<float> roomTrackDistances = new();
        private readonly Plane[] viewerLeftFrustumPlanes = new Plane[6];
        private readonly Plane[] viewerRightFrustumPlanes = new Plane[6];

        private Transform presentationRoot;
        private Transform firstVehicle;
        private Transform secondVehicle;
        private Camera viewerCamera;
        private Camera entryCamera;
        private Camera exitCamera;
        private Transform entrySurface;
        private Transform exitSurface;
        private Renderer entrySurfaceRenderer;
        private Renderer exitSurfaceRenderer;
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
        private bool pitStopOnly;
        private Material roomOccluderClearMaterial;
        private Material firstVehicleXRayMaterial;
        private Material secondVehicleXRayMaterial;
        private bool firstVehicleXRayApplied;
        private bool secondVehicleXRayApplied;
        private bool trackExitOnly;
        private float trackExitDistance;
        private bool trackExitDistanceValid;
        private bool exitPortalVisible = true;
        [SerializeField] private GameObject decorativePortalPrefab;

        public bool IsConfigured => configured;
        public bool IsPitStopConfigured =>
            configured && pitStopOnly;
        public bool ExitPortalVisible =>
            configured && exitPortalVisible;
        public bool ImmersiveScaleEnabled { get; set; }
        public int AuthoritativeVehicleCount =>
            !configured || firstVehicle == null
                ? 0
                : secondVehicle != null
                    ? 2
                    : 1;

        public void SetExitPortalVisible(bool visible)
        {
            exitPortalVisible = visible;
            if (exitSurface != null)
            {
                exitSurface.gameObject.SetActive(visible);
            }
            if (!visible && exitCamera != null)
                exitCamera.enabled = false;
        }

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

            CaptureRoomTrackCorridor(stage);
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
            entrySurfaceRenderer =
                entrySurface != null
                    ? entrySurface.GetComponent<Renderer>()
                    : null;
            exitSurfaceRenderer =
                exitSurface != null
                    ? exitSurface.GetComponent<Renderer>()
                    : null;

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

            CreateDecorativePortalVfx();

            originalViewerMask = viewerCamera.cullingMask;
            viewerMaskCaptured = true;
            viewerCamera.cullingMask &=
                ~(1 << PortalSceneLayer);
            CaptureAndHideSourceRenderers(stage);
            configured = true;
            SuspendPlaneMeshVisualizers();
            Debug.Log(
                $"[RoomTrackFilter] renderers={roomTrackRendererCount}, " +
                $"includedSubmeshes={includedRoomTrackSubmeshes}, " +
                $"excludedSubmeshes={excludedRoomTrackSubmeshes}, " +
                $"roadSeams={FormatHeight(entryRoadHeight, hasEntryRoadHeight)}/" +
                $"{FormatHeight(exitRoadHeight, hasExitRoadHeight)}",
                this);
            RefreshPortalViews();
            RefreshRoomVehicleVisibility();
            return true;
        }

        public bool ConfigureTrackExit(
            Transform stage,
            Pose trackExitPose,
            Transform firstVehicleRoot,
            Transform secondVehicleRoot,
            out string failure)
        {
            Clear();
            failure = "";

            if (stage == null ||
                firstVehicleRoot == null ||
                secondVehicleRoot == null)
            {
                failure =
                    "The stage or authoritative vehicles are unavailable for the track Exit portal.";
                return false;
            }

            viewerCamera = Camera.main;
            if (viewerCamera == null)
            {
                failure = "The XR main camera is unavailable.";
                return false;
            }

            Vector3 travelDirection = Flat(trackExitPose.forward);
            if (travelDirection.sqrMagnitude <= 0.000001f)
            {
                failure =
                    "The track Exit has no stable travel direction.";
                return false;
            }

            travelDirection.Normalize();
            trackExitOnly = true;
            exitPosition = trackExitPose.position;
            exitInward = -travelDirection;
            exitPortalSize = new Vector2(
                FallbackPortalWidth,
                FallbackPortalHeight);
            Pose exitPortalPose = new Pose(
                trackExitPose.position,
                Quaternion.LookRotation(
                    travelDirection,
                    Vector3.up));
            exitPortalPose = AlignPortalBottom(
                exitPortalPose,
                exitPortalSize,
                trackExitPose.position.y -
                PortalRoadOverlap);
            exitPortalRight = exitPortalPose.right;
            firstVehicle = firstVehicleRoot;
            secondVehicle = secondVehicleRoot;

            presentationRoot =
                new GameObject("TrackExitPortalPresentation")
                    .transform;
            presentationRoot.SetParent(transform, true);

            CaptureRoomTrackCorridor(stage);
            int roomTrackRendererCount =
                CreateRoomTrackRenderers(stage);
            int firstProxyCount =
                CreateRoomVehicleRenderers(firstVehicle);
            int secondProxyCount =
                CreateRoomVehicleRenderers(secondVehicle);

            exitSurface = CreatePortal(
                "TrackExitPortal",
                exitPortalPose,
                exitPortalSize,
                out exitCamera);
            exitSurfaceRenderer =
                exitSurface != null
                    ? exitSurface.GetComponent<Renderer>()
                    : null;

            if (exitSurface == null ||
                exitCamera == null ||
                roomTrackRendererCount == 0 ||
                firstProxyCount == 0 ||
                secondProxyCount == 0)
            {
                failure =
                    "The track Exit portal or its render-only geometry could not be built.";
                Clear();
                return false;
            }

            CreateDecorativePortalVfx();

            originalViewerMask = viewerCamera.cullingMask;
            viewerMaskCaptured = true;
            viewerCamera.cullingMask &=
                ~(1 << PortalSceneLayer);
            CaptureAndHideSourceRenderers(stage);
            presentationRoot.SetParent(stage, true);
            configured = true;
            SuspendPlaneMeshVisualizers();
            RefreshPortalViews();
            RefreshRoomVehicleVisibility();
            return true;
        }

        public bool ConfigureSingleWall(
            Transform stage,
            ShowcaseWallFrame wall,
            Transform vehicleRoot,
            out string failure)
        {
            Clear();
            failure = "";
            if (stage == null ||
                vehicleRoot == null ||
                !wall.IsValid)
            {
                failure =
                    "The pit stage, vehicle, or selected wall is unavailable.";
                return false;
            }

            viewerCamera = Camera.main;
            if (viewerCamera == null)
            {
                failure = "The XR main camera is unavailable.";
                return false;
            }

            Vector3 inward = Flat(wall.InwardNormal);
            Vector3 up = wall.VerticalAxis.normalized;
            if (inward.sqrMagnitude <= 0.000001f ||
                up.sqrMagnitude <= 0.5f)
            {
                failure =
                    "The selected pit wall has no stable frame.";
                return false;
            }

            inward.Normalize();
            entryPosition = wall.Center;
            entryInward = inward;
            PitWallOverlayLayout wallLayout =
                PitWallLayoutPolicy.Resolve(
                    wall.Width,
                    wall.Height);
            if (wallLayout == PitWallOverlayLayout.None)
            {
                failure =
                    "The selected wall is too small for the pit wall presentation.";
                return false;
            }
            entryPortalSize = ResolvePitPortalSize(
                new Vector2(wall.Width, wall.Height));
            Vector3 verticalMidpoint =
                wall.Center +
                up * ((wall.MinVertical + wall.MaxVertical) * 0.5f);
            if (entryPortalSize.y + PortalBottomOffset * 2f < wall.Height)
            {
                verticalMidpoint =
                    wall.Center +
                    up * wall.MinVertical +
                    up * (PortalBottomOffset + entryPortalSize.y * 0.5f);
            }
            Pose wallPose = new(
                verticalMidpoint,
                Quaternion.LookRotation(inward, up));
            Pose portalPose = wallPose;
            entryPortalRight = portalPose.right;
            firstVehicle = vehicleRoot;
            secondVehicle = null;

            presentationRoot =
                new GameObject("PitStopWallPresentation")
                    .transform;
            presentationRoot.SetParent(transform, true);
            entrySurface = CreatePortal(
                "PitStopPortal",
                portalPose,
                entryPortalSize,
                out entryCamera,
                true,
                true);
            entrySurfaceRenderer =
                entrySurface != null
                    ? entrySurface.GetComponent<Renderer>()
                    : null;
            if (entrySurface == null || entryCamera == null)
            {
                failure = "The pit stop portal could not be built.";
                Clear();
                return false;
            }
            // Keep the pit wall aperture visually clean. The former gantry,
            // scan line, accent rails, floor ribbon, and broadcast header were
            // decorative overlays and competed with the pit-stop choreography.
            CreatePitWallEditor(wall, stage);

            originalViewerMask = viewerCamera.cullingMask;
            viewerMaskCaptured = true;
            viewerCamera.cullingMask &=
                ~(1 << PortalSceneLayer);
            CaptureAndHideSourceRenderers(stage);
            configured = true;
            pitStopOnly = true;
            exitPortalVisible = false;
            SuspendPlaneMeshVisualizers();
            RefreshPortalViews();
            return true;
        }

        public void Clear()
        {
            ClearPitWallEditor();
            ClearPitWallOverlay();
            plannedRoomOccluderMaterials.Clear();
            ResetRoomOcclusionAssistance();
            DisablePortalCamera(entryCamera);
            DisablePortalCamera(exitCamera);
            if (presentationRoot != null)
                presentationRoot.gameObject.SetActive(false);

            for (int i = 0; i < rendererProxies.Count; i++)
            {
                if (rendererProxies[i] != null)
                    rendererProxies[i].SetActive(false);
            }

            ClearOvertakePortalTransition();
            configured = false;
            pitStopOnly = false;
            RestorePlaneMeshVisualizers();

            if (viewerMaskCaptured && viewerCamera != null)
                viewerCamera.cullingMask = originalViewerMask;

            viewerMaskCaptured = false;
            viewerCamera = null;
            entryCamera = null;
            exitCamera = null;
            entrySurface = null;
            exitSurface = null;
            entrySurfaceRenderer = null;
            exitSurfaceRenderer = null;
            firstVehicle = null;
            secondVehicle = null;
            entryPortalRight = Vector3.zero;
            exitPortalRight = Vector3.zero;
            includedRoomTrackSubmeshes = 0;
            excludedRoomTrackSubmeshes = 0;
            trackExitOnly = false;
            exitPortalVisible = true;

            for (int i = 0; i < sourceLayers.Count; i++)
            {
                LayerBinding binding = sourceLayers[i];
                if (binding.Transform != null)
                    binding.Transform.gameObject.layer = binding.Layer;
            }
            sourceLayers.Clear();
            capturedLayerTransforms.Clear();

            for (int i = 0; i < labelRendererStates.Count; i++)
                labelRendererStates[i].Restore();

            labelRendererStates.Clear();
            roomCarRenderers.Clear();
            roomCarTmpRenderers.Clear();
            roomTrackOriginalMaterials.Clear();
            roomVehicleOriginalMaterials.Clear();
            suppressedRoomOccluderMaterials.Clear();
            desiredRoomOccluderMaterials.Clear();
            combinedRoomOccluderMaterials.Clear();
            roomTrackLeftBoundary.Clear();
            roomTrackRightBoundary.Clear();
            roomTrackDistances.Clear();
            trackExitDistance = 0f;
            trackExitDistanceValid = false;

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
            roomOccluderClearMaterial = null;
            firstVehicleXRayMaterial = null;
            secondVehicleXRayMaterial = null;

            for (int i = 0; i < runtimeMeshes.Count; i++)
            {
                if (runtimeMeshes[i] != null)
                    Destroy(runtimeMeshes[i]);
            }
            runtimeMeshes.Clear();
        }

        private static void DisablePortalCamera(Camera portalCamera)
        {
            if (portalCamera == null)
                return;

            portalCamera.enabled = false;
            portalCamera.targetTexture = null;
        }

        private void SuspendPlaneMeshVisualizers()
        {
            RestorePlaneMeshVisualizers();
            ARPlaneManager[] managers =
                FindObjectsByType<ARPlaneManager>(
                    FindObjectsInactive.Exclude);
            for (int i = 0; i < managers.Length; i++)
            {
                ARPlaneManager manager = managers[i];
                if (manager == null)
                    continue;

                manager.trackablesChanged.AddListener(
                    OnPlaneTrackablesChanged);
                subscribedPlaneManagers.Add(manager);
            }

            ARPlaneMeshVisualizer[] visualizers =
                FindObjectsByType<ARPlaneMeshVisualizer>(
                    FindObjectsInactive.Include);
            for (int i = 0; i < visualizers.Length; i++)
                SuspendPlaneMeshVisualizer(visualizers[i]);
        }

        private void OnPlaneTrackablesChanged(
            ARTrackablesChangedEventArgs<ARPlane> changes)
        {
            foreach (ARPlane plane in changes.added)
                SuspendPlaneMeshVisualizer(
                    plane.GetComponent<ARPlaneMeshVisualizer>());

            foreach (ARPlane plane in changes.updated)
                SuspendPlaneMeshVisualizer(
                    plane.GetComponent<ARPlaneMeshVisualizer>());
        }

        private void SuspendPlaneMeshVisualizer(
            ARPlaneMeshVisualizer visualizer)
        {
            if (visualizer == null ||
                !visualizer.enabled)
            {
                return;
            }

            if (!suspendedPlaneVisualizers.Contains(visualizer))
                suspendedPlaneVisualizers.Add(visualizer);
            visualizer.enabled = false;
        }

        private void RestorePlaneMeshVisualizers()
        {
            for (int i = 0;
                 i < subscribedPlaneManagers.Count;
                 i++)
            {
                ARPlaneManager manager =
                    subscribedPlaneManagers[i];
                if (manager != null)
                {
                    manager.trackablesChanged.RemoveListener(
                        OnPlaneTrackablesChanged);
                }
            }
            subscribedPlaneManagers.Clear();

            for (int i = 0;
                 i < suspendedPlaneVisualizers.Count;
                 i++)
            {
                ARPlaneMeshVisualizer visualizer =
                    suspendedPlaneVisualizers[i];
                if (visualizer != null)
                    visualizer.enabled = true;
            }
            suspendedPlaneVisualizers.Clear();
        }

        private void CaptureRoomTrackCorridor(
            Transform stage)
        {
            roomTrackLeftBoundary.Clear();
            roomTrackRightBoundary.Clear();
            roomTrackDistances.Clear();
            trackExitDistance = 0f;
            trackExitDistanceValid = false;
            if (stage == null)
                return;

            Transform apron =
                stage.Find("EventRoadSafetyApron");
            if (apron == null)
                apron = stage.Find("EventRoad");
            MeshFilter filter =
                apron != null
                    ? apron.GetComponent<MeshFilter>()
                    : null;
            Mesh mesh =
                filter != null
                    ? filter.sharedMesh
                    : null;
            if (mesh == null ||
                mesh.vertexCount < 4)
            {
                return;
            }

            Vector3[] vertices = mesh.vertices;
            for (int i = 0;
                 i + 1 < vertices.Length;
                 i += 2)
            {
                Vector3 left =
                    filter.transform.TransformPoint(
                        vertices[i]);
                Vector3 right =
                    filter.transform.TransformPoint(
                        vertices[i + 1]);
                roomTrackLeftBoundary.Add(left);
                roomTrackRightBoundary.Add(right);
            }

            if (roomTrackLeftBoundary.Count < 2)
                return;

            roomTrackDistances.Add(0f);
            for (int i = 1;
                 i < roomTrackLeftBoundary.Count;
                 i++)
            {
                Vector3 previous =
                    (roomTrackLeftBoundary[i - 1] +
                     roomTrackRightBoundary[i - 1]) *
                    0.5f;
                Vector3 current =
                    (roomTrackLeftBoundary[i] +
                     roomTrackRightBoundary[i]) *
                    0.5f;
                roomTrackDistances.Add(
                    roomTrackDistances[i - 1] +
                    Vector3.Distance(previous, current));
            }

            trackExitDistanceValid =
                trackExitOnly &&
                TryGetTrackDistance(
                    exitPosition,
                    out trackExitDistance);
        }

        private void LateUpdate()
        {
            if (!configured)
                return;

            RefreshPortalViews();
            RefreshRoomVehicleVisibility();
            UpdateDecorativePortalVfx();
        }

        private void RefreshPortalViews()
        {
            if (viewerCamera == null)
                return;

            bool stereo = viewerCamera.stereoEnabled;
            if (stereo)
            {
                Matrix4x4 leftViewProjection =
                    viewerCamera.GetStereoProjectionMatrix(
                        Camera.StereoscopicEye.Left) *
                    viewerCamera.GetStereoViewMatrix(
                        Camera.StereoscopicEye.Left);
                Matrix4x4 rightViewProjection =
                    viewerCamera.GetStereoProjectionMatrix(
                        Camera.StereoscopicEye.Right) *
                    viewerCamera.GetStereoViewMatrix(
                        Camera.StereoscopicEye.Right);
                GeometryUtility.CalculateFrustumPlanes(
                    leftViewProjection,
                    viewerLeftFrustumPlanes);
                GeometryUtility.CalculateFrustumPlanes(
                    rightViewProjection,
                    viewerRightFrustumPlanes);
            }
            else
            {
                GeometryUtility.CalculateFrustumPlanes(
                    viewerCamera,
                    viewerLeftFrustumPlanes);
            }

            UpdatePortalView(
                entryCamera,
                entrySurface,
                entrySurfaceRenderer,
                entryPortalSize,
                stereo);
            UpdatePortalView(
                exitCamera,
                exitSurface,
                exitSurfaceRenderer,
                exitPortalSize,
                stereo);
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
                if (renderer == null ||
                    rendererProxies.Contains(
                        renderer.gameObject))
                    continue;

                Transform current = renderer.transform;
                if (!capturedLayerTransforms.Add(current))
                    continue;

                sourceLayers.Add(new LayerBinding(
                    current,
                    current.gameObject.layer));
                current.gameObject.layer = PortalSceneLayer;

                if (!trackExitOnly &&
                    IsDriverLabelRenderer(renderer))
                {
                    labelRendererStates.Add(
                        new LabelRendererState(renderer));
                    renderer.forceRenderingOff = true;
                }
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
                    sourceRenderer,
                    out Material[] clippedMaterials);
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
                proxyRenderer.sharedMaterials =
                    clippedMaterials;
                roomTrackOriginalMaterials[proxyRenderer] =
                    proxyRenderer.sharedMaterials;
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
                if (source is ParticleSystemRenderer sourceParticlesRenderer)
                {
                    ParticleSystem sourceParticles =
                        sourceParticlesRenderer
                            .GetComponent<ParticleSystem>();
                    if (sourceParticles == null)
                        continue;

                    GameObject proxy =
                        CreateRendererProxyObject(source);
                    ParticleSystem proxyParticles =
                        proxy.AddComponent<ParticleSystem>();
                    ParticleSystemRenderer proxyParticlesRenderer =
                        proxy.GetComponent<ParticleSystemRenderer>();
                    CopyRendererSettings(
                        sourceParticlesRenderer,
                        proxyParticlesRenderer);
                    CopyParticleSystemSettings(
                        sourceParticles,
                        sourceParticlesRenderer,
                        proxyParticles,
                        proxyParticlesRenderer);
                    roomCarRenderers.Add(
                        new RendererBinding(
                            sourceParticlesRenderer,
                            proxyParticlesRenderer,
                            vehicleRoot,
                            sourceParticles,
                            proxyParticles));
                    created++;
                }
                else if (source is MeshRenderer meshRenderer)
                {
                    TextMeshPro sourceTmp =
                        source.GetComponent<TextMeshPro>();
                    if (sourceTmp != null)
                    {
                        GameObject textProxy =
                            CreateRendererProxyObject(source);
                        TextMeshPro proxyTmp =
                            textProxy.AddComponent<TextMeshPro>();
                        SyncTextMeshPro(sourceTmp, proxyTmp);
                        MeshRenderer textProxyRenderer =
                            textProxy.GetComponent<MeshRenderer>();
                        CopyRendererSettings(
                            meshRenderer,
                            textProxyRenderer);
                        roomCarRenderers.Add(
                            new RendererBinding(
                                source,
                                textProxyRenderer,
                                vehicleRoot));
                        roomCarTmpRenderers.Add(
                            new TmpRendererBinding(
                                sourceTmp,
                                proxyTmp));
                        created++;
                        continue;
                    }

                    TextMesh sourceText =
                        source.GetComponent<TextMesh>();
                    if (sourceText != null)
                    {
                        GameObject textProxy =
                            CreateRendererProxyObject(source);
                        TextMesh proxyText =
                            textProxy.AddComponent<TextMesh>();
                        SyncTextMesh(sourceText, proxyText);
                        MeshRenderer textProxyRenderer =
                            textProxy.GetComponent<MeshRenderer>();
                        CopyRendererSettings(
                            meshRenderer,
                            textProxyRenderer);
                        roomCarRenderers.Add(
                            new RendererBinding(
                                source,
                                textProxyRenderer,
                                vehicleRoot,
                                sourceText,
                                proxyText));
                        created++;
                        continue;
                    }

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
                    roomVehicleOriginalMaterials[proxyRenderer] =
                        proxyRenderer.sharedMaterials;
                    roomCarRenderers.Add(
                        new RendererBinding(
                            source,
                            proxyRenderer,
                            vehicleRoot));
                    created++;
                }
                else if (source is TrailRenderer sourceTrail)
                {
                    GameObject proxy =
                        CreateRendererProxyObject(source);
                    TrailRenderer proxyTrail =
                        proxy.AddComponent<TrailRenderer>();
                    CopyRendererSettings(
                        sourceTrail,
                        proxyTrail);
                    CopyTrailRendererSettings(
                        sourceTrail,
                        proxyTrail);
                    SyncTrailRenderer(
                        sourceTrail,
                        proxyTrail);
                    roomCarRenderers.Add(
                        new RendererBinding(
                            sourceTrail,
                            proxyTrail,
                            vehicleRoot));
                    created++;
                }
                else if (source is LineRenderer sourceLine)
                {
                    GameObject proxy =
                        CreateRendererProxyObject(source);
                    LineRenderer proxyLine =
                        proxy.AddComponent<LineRenderer>();
                    CopyRendererSettings(
                        sourceLine,
                        proxyLine);
                    CopyLineRendererSettings(
                        sourceLine,
                        proxyLine);
                    SyncLineRenderer(
                        sourceLine,
                        proxyLine);
                    roomCarRenderers.Add(
                        new RendererBinding(
                            source,
                            proxyLine,
                            vehicleRoot,
                            null,
                            null,
                            sourceLine,
                            proxyLine));
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
                    roomVehicleOriginalMaterials[proxyRenderer] =
                        proxyRenderer.sharedMaterials;
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
            MeshRenderer sourceRenderer,
            out Material[] clippedMaterials)
        {
            clippedMaterials =
                System.Array.Empty<Material>();
            if (!source.isReadable)
                return null;

            Vector3[] vertices = source.vertices;
            Mesh copy = Instantiate(source);
            copy.name = $"{source.name}_Room";
            List<List<int>> submeshes =
                new(source.subMeshCount);
            List<Material> materials =
                new(source.subMeshCount);
            HashSet<int> keptVertices = new();
            Material[] sourceMaterials =
                sourceRenderer.sharedMaterials;
            int keptTriangles = 0;

            for (int submesh = 0;
                 submesh < source.subMeshCount;
                 submesh++)
            {
                bool trackSubmesh =
                    ShouldIncludeRoomTrackSubmesh(
                        sourceRenderer,
                        submesh);
                if (!trackExitOnly && !trackSubmesh)
                {
                    excludedRoomTrackSubmeshes++;
                    continue;
                }

                if (source.GetTopology(submesh) !=
                    MeshTopology.Triangles)
                {
                    excludedRoomTrackSubmeshes++;
                    continue;
                }

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
                            worldC) ||
                        (!trackExitOnly &&
                         !TriangleTouchesTrackCorridor(
                            worldA,
                            worldB,
                            worldC)))
                    {
                        continue;
                    }

                    kept.Add(a);
                    kept.Add(b);
                    kept.Add(c);
                    keptVertices.Add(a);
                    keptVertices.Add(b);
                    keptVertices.Add(c);
                    keptTriangles++;
                }

                if (kept.Count == 0)
                {
                    excludedRoomTrackSubmeshes++;
                    continue;
                }

                includedRoomTrackSubmeshes++;
                submeshes.Add(kept);
                materials.Add(
                    sourceMaterials.Length > 0
                        ? sourceMaterials[
                            Mathf.Min(
                                submesh,
                                sourceMaterials.Length - 1)]
                        : null);
            }

            if (keptTriangles == 0)
            {
                Destroy(copy);
                return null;
            }

            WidenRoomTrack(
                copy,
                sourceTransform,
                keptVertices);
            copy.subMeshCount = submeshes.Count;
            for (int submesh = 0;
                 submesh < submeshes.Count;
                 submesh++)
            {
                copy.SetIndices(
                    submeshes[submesh],
                    MeshTopology.Triangles,
                    submesh,
                    false);
            }
            copy.RecalculateBounds();
            clippedMaterials = materials.ToArray();
            runtimeMeshes.Add(copy);
            return copy;
        }

        private void WidenRoomTrack(
            Mesh mesh,
            Transform sourceTransform,
            HashSet<int> keptVertices)
        {
            if (mesh == null ||
                sourceTransform == null ||
                keptVertices == null ||
                keptVertices.Count == 0 ||
                !ImmersiveScaleEnabled ||
                ImmersiveMaximumScale <= 1f ||
                roomTrackLeftBoundary.Count < 2 ||
                roomTrackRightBoundary.Count !=
                roomTrackLeftBoundary.Count)
            {
                return;
            }

            Vector3[] vertices = mesh.vertices;
            foreach (int index in keptVertices)
            {
                if (index < 0 || index >= vertices.Length)
                    continue;

                Vector3 world =
                    sourceTransform.TransformPoint(
                        vertices[index]);
                if (!TryGetTrackCrossSection(
                        world,
                        out Vector3 center,
                        out Vector3 side))
                {
                    continue;
                }

                float lateral =
                    Vector3.Dot(world - center, side);
                float widthScale =
                    EvaluateImmersiveScale(world);
                world +=
                    side *
                    lateral *
                    (widthScale - 1f);
                vertices[index] =
                    sourceTransform.InverseTransformPoint(
                        world);
            }

            mesh.vertices = vertices;
        }

        private bool TryGetTrackCrossSection(
            Vector3 world,
            out Vector3 center,
            out Vector3 side)
        {
            center = Vector3.zero;
            side = Vector3.zero;
            float bestDistance = float.PositiveInfinity;

            for (int i = 0;
                 i < roomTrackLeftBoundary.Count - 1;
                 i++)
            {
                Vector3 start =
                    (roomTrackLeftBoundary[i] +
                     roomTrackRightBoundary[i]) *
                    0.5f;
                Vector3 end =
                    (roomTrackLeftBoundary[i + 1] +
                     roomTrackRightBoundary[i + 1]) *
                    0.5f;
                Vector3 flatSegment = Flat(end - start);
                float lengthSquared =
                    flatSegment.sqrMagnitude;
                float t = lengthSquared > 0.000001f
                    ? Mathf.Clamp01(
                        Vector3.Dot(
                            Flat(world - start),
                            flatSegment) /
                        lengthSquared)
                    : 0f;
                Vector3 candidateCenter =
                    Vector3.Lerp(start, end, t);
                float distance =
                    Flat(world - candidateCenter)
                        .sqrMagnitude;
                if (distance >= bestDistance)
                    continue;

                Vector3 candidateSide = Flat(
                    Vector3.Lerp(
                        roomTrackRightBoundary[i] -
                        roomTrackLeftBoundary[i],
                        roomTrackRightBoundary[i + 1] -
                        roomTrackLeftBoundary[i + 1],
                        t));
                if (candidateSide.sqrMagnitude <=
                    0.000001f)
                {
                    candidateSide = Vector3.Cross(
                        Vector3.up,
                        flatSegment);
                }

                if (candidateSide.sqrMagnitude <=
                    0.000001f)
                {
                    continue;
                }

                bestDistance = distance;
                center = candidateCenter;
                side = candidateSide.normalized;
            }

            return side.sqrMagnitude > 0.000001f;
        }

        private bool TriangleTouchesTrackCorridor(
            Vector3 a,
            Vector3 b,
            Vector3 c)
        {
            if (roomTrackLeftBoundary.Count < 2 ||
                roomTrackRightBoundary.Count !=
                roomTrackLeftBoundary.Count)
            {
                return true;
            }

            Vector3 center = (a + b + c) / 3f;
            return IsInsideTrackCorridor(a) ||
                IsInsideTrackCorridor(b) ||
                IsInsideTrackCorridor(c) ||
                IsInsideTrackCorridor(center);
        }

        private bool IsInsideTrackCorridor(
            Vector3 point)
        {
            point.y = 0f;
            for (int i = 0;
                 i < roomTrackLeftBoundary.Count - 1;
                 i++)
            {
                Vector3 left =
                    roomTrackLeftBoundary[i];
                Vector3 right =
                    roomTrackRightBoundary[i];
                Vector3 nextLeft =
                    roomTrackLeftBoundary[i + 1];
                Vector3 nextRight =
                    roomTrackRightBoundary[i + 1];
                left.y = 0f;
                right.y = 0f;
                nextLeft.y = 0f;
                nextRight.y = 0f;
                if (PointInsideTriangleXZ(
                        point,
                        left,
                        nextLeft,
                        right) ||
                    PointInsideTriangleXZ(
                        point,
                        right,
                        nextLeft,
                        nextRight))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryGetTrackDistance(
            Vector3 point,
            out float distance)
        {
            distance = 0f;
            if (roomTrackLeftBoundary.Count < 2 ||
                roomTrackRightBoundary.Count !=
                    roomTrackLeftBoundary.Count ||
                roomTrackDistances.Count !=
                    roomTrackLeftBoundary.Count)
            {
                return false;
            }

            float bestDistanceSquared =
                float.PositiveInfinity;
            for (int i = 0;
                 i < roomTrackLeftBoundary.Count - 1;
                 i++)
            {
                Vector3 start = Flat(
                    (roomTrackLeftBoundary[i] +
                     roomTrackRightBoundary[i]) *
                    0.5f);
                Vector3 end = Flat(
                    (roomTrackLeftBoundary[i + 1] +
                     roomTrackRightBoundary[i + 1]) *
                    0.5f);
                Vector3 segment = end - start;
                float segmentLengthSquared =
                    segment.sqrMagnitude;
                float progress =
                    segmentLengthSquared > 0.000001f
                        ? Mathf.Clamp01(
                            Vector3.Dot(
                                Flat(point) - start,
                                segment) /
                            segmentLengthSquared)
                        : 0f;
                Vector3 closest =
                    Vector3.Lerp(start, end, progress);
                float candidateDistanceSquared =
                    (Flat(point) - closest).sqrMagnitude;
                if (candidateDistanceSquared >=
                    bestDistanceSquared)
                {
                    continue;
                }

                bestDistanceSquared =
                    candidateDistanceSquared;
                distance = Mathf.Lerp(
                    roomTrackDistances[i],
                    roomTrackDistances[i + 1],
                    progress);
            }

            return float.IsFinite(distance) &&
                bestDistanceSquared < float.PositiveInfinity;
        }

        private static bool PointInsideTriangleXZ(
            Vector3 point,
            Vector3 a,
            Vector3 b,
            Vector3 c)
        {
            Vector2 v0 =
                new Vector2(b.x - a.x, b.z - a.z);
            Vector2 v1 =
                new Vector2(c.x - a.x, c.z - a.z);
            Vector2 v2 =
                new Vector2(
                    point.x - a.x,
                    point.z - a.z);
            float denominator =
                v0.x * v1.y -
                v1.x * v0.y;
            if (Mathf.Abs(denominator) <=
                0.00000001f)
            {
                return false;
            }

            float bWeight =
                (v2.x * v1.y -
                 v1.x * v2.y) /
                denominator;
            float cWeight =
                (v0.x * v2.y -
                 v2.x * v0.y) /
                denominator;
            float aWeight =
                1f -
                bWeight -
                cWeight;
            return aWeight >= 0f &&
                bWeight >= 0f &&
                cWeight >= 0f;
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
                IsInsideRoomGeometry(a) ||
                IsInsideRoomGeometry(b) ||
                IsInsideRoomGeometry(c) ||
                IsInsideRoomGeometry(center);
            return touchesRoom &&
                IsInsidePortalApertures(center);
        }

        private bool IsInsideRoomGeometry(Vector3 position)
        {
            bool insideExit = Vector3.Dot(
                                  position - exitPosition,
                                  exitInward) >=
                              -RoomPlaneTolerance;
            if (trackExitOnly)
                return insideExit;

            return Vector3.Dot(
                       position - entryPosition,
                       entryInward) >=
                   -RoomPlaneTolerance &&
                insideExit;
        }

        private bool IsInsideRoom(Vector3 position)
        {
            bool insideExit = Vector3.Dot(
                                  position - exitPosition,
                                  exitInward) >=
                              -RoomPlaneTolerance;
            if (trackExitOnly)
            {
                return trackExitDistanceValid &&
                       TryGetTrackDistance(
                           position,
                           out float distance)
                    ? distance <=
                      trackExitDistance +
                      TrackExitLongitudinalTolerance
                    : insideExit;
            }

            return Vector3.Dot(
                       position - entryPosition,
                       entryInward) >=
                   -RoomPlaneTolerance &&
                insideExit;
        }

        public float EvaluateImmersiveScale(
            Vector3 worldPosition)
        {
            if (!ImmersiveScaleEnabled ||
                !IsInsideRoom(worldPosition))
                return 1f;

            float entryDepth = Mathf.Max(
                0f,
                Vector3.Dot(
                    worldPosition - entryPosition,
                    entryInward));
            float exitDepth = Mathf.Max(
                0f,
                Vector3.Dot(
                    worldPosition - exitPosition,
                    exitInward));
            float blend = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(
                    Mathf.Min(entryDepth, exitDepth) /
                    ImmersiveScaleRampDistance));
            return Mathf.Lerp(
                1f,
                ImmersiveMaximumScale,
                blend);
        }

        private bool IsInsidePortalApertures(
            Vector3 position)
        {
            if (trackExitOnly)
            {
                return IsInsidePortalAperture(
                    position,
                    exitPosition,
                    exitInward,
                    exitPortalRight,
                    exitPortalSize.x * 0.5f);
            }

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
            out Camera portalCamera,
            bool highQuality = false,
            bool rectangular = false)
        {
            ResolvePortalTextureSize(
                size,
                highQuality,
                out int textureWidth,
                out int textureHeight);
            RenderTexture texture = new RenderTexture(
                textureWidth,
                textureHeight,
                TextureDepthBits,
                RenderTextureFormat.ARGB32)
            {
                name = $"{name}Texture",
                antiAliasing = highQuality
                    ? ResolvePitTextureAntiAliasing()
                    : 1,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
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
            filter.sharedMesh = rectangular
                ? CreateRectangularPitPortalMesh(
                    name,
                    size,
                    mirrorHorizontally)
                : CreatePortalMesh(
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
            // The rectangular pit wall is a clean opening into the pit scene.
            // The persistent glow/core frame reads as a TV border at room scale,
            // so keep that treatment only for the other portal presentations.
            if (!rectangular)
            {
                CreatePersistentPortalFrame(
                    name,
                    size,
                    surface.transform,
                    false);
            }

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
                new Color(0.015f, 0.025f, 0.04f, 0f);
            portalCamera.allowHDR = false;
            portalCamera.allowMSAA = highQuality;
            portalCamera.allowDynamicResolution = false;
            portalCamera.useOcclusionCulling = false;
            portalCamera.depth =
                viewerCamera.depth - 10f;
            portalCamera.targetTexture = texture;

            UniversalAdditionalCameraData cameraData =
                portalCamera.GetUniversalAdditionalCameraData();
            cameraData.renderShadows = false;
            cameraData.requiresDepthOption =
                CameraOverrideOption.Off;
            cameraData.requiresColorOption =
                CameraOverrideOption.Off;
            cameraData.renderPostProcessing = false;
            cameraData.allowXRRendering = false;

            return surface.transform;
        }

        private void CreatePersistentPortalFrame(
            string name,
            Vector2 size,
            Transform parent,
            bool rectangular = false)
        {
            float referenceSize = Mathf.Min(size.x, size.y);
            float glowWidth = Mathf.Clamp(
                referenceSize * 0.045f,
                0.045f,
                0.11f);
            float coreWidth = Mathf.Clamp(
                referenceSize * 0.014f,
                0.018f,
                0.04f);
            Material glowMaterial =
                CreatePortalFrameMaterial(
                    $"{name}FrameGlow",
                    new Color(0.05f, 0.55f, 1f, 0.24f),
                    3120);
            Material coreMaterial =
                CreatePortalFrameMaterial(
                    $"{name}FrameCore",
                    new Color(0.84f, 0.97f, 1f, 0.92f),
                    3130);
            if (glowMaterial == null || coreMaterial == null)
                return;

            CreatePersistentPortalFrameRenderer(
                $"{name}FrameGlow",
                rectangular
                    ? CreateRectangularPitPortalFrameMesh(
                        size,
                        glowWidth,
                        $"Runtime_{name}FrameGlow")
                    : CreatePortalFrameMesh(
                        size,
                        glowWidth,
                        $"Runtime_{name}FrameGlow"),
                glowMaterial,
                parent,
                -0.012f,
                20);
            CreatePersistentPortalFrameRenderer(
                $"{name}FrameCore",
                rectangular
                    ? CreateRectangularPitPortalFrameMesh(
                        size,
                        coreWidth,
                        $"Runtime_{name}FrameCore")
                    : CreatePortalFrameMesh(
                        size,
                        coreWidth,
                        $"Runtime_{name}FrameCore"),
                coreMaterial,
                parent,
                -0.016f,
                21);
        }

        private void CreatePersistentPortalFrameRenderer(
            string name,
            Mesh mesh,
            Material material,
            Transform parent,
            float surfaceOffset,
            int sortingOrder)
        {
            GameObject frame = new GameObject(name);
            frame.layer = PortalSurfaceLayer;
            frame.transform.SetParent(parent, false);
            frame.transform.localPosition =
                new Vector3(0f, 0f, surfaceOffset);
            MeshFilter filter = frame.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer =
                frame.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage =
                ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            renderer.sortingOrder = sortingOrder;
        }

        private Material CreatePortalFrameMaterial(
            string name,
            Color color,
            int renderQueue)
        {
            Shader shader = Shader.Find(
                "Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null)
                return null;

            Material material = new Material(shader)
            {
                name = $"{name}Material"
            };
            material.SetOverrideTag("RenderType", "Transparent");
            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend"))
                material.SetFloat("_Blend", 1f);
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
                    (float)BlendMode.One);
            }
            if (material.HasProperty("_ZWrite"))
                material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_Cull"))
                material.SetFloat("_Cull", (float)CullMode.Off);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = renderQueue;
            runtimeMaterials.Add(material);
            return material;
        }

        private Mesh CreatePortalMesh(
            string name,
            Vector2 size,
            bool mirrorHorizontally)
        {
            float halfWidth = size.x * 0.5f;
            float halfHeight = size.y * 0.5f;
            float archHeight = Mathf.Min(
                halfWidth,
                size.y * 0.42f);
            float springY = halfHeight - archHeight;

            List<Vector3> vertices = new List<Vector3>(
                PortalArchSegments + 4)
            {
                Vector3.zero,
                new Vector3(-halfWidth, -halfHeight, 0f),
                new Vector3(halfWidth, -halfHeight, 0f),
                new Vector3(halfWidth, springY, 0f)
            };

            for (int i = 1; i <= PortalArchSegments; i++)
            {
                float angle =
                    Mathf.PI * i / PortalArchSegments;
                vertices.Add(new Vector3(
                    Mathf.Cos(angle) * halfWidth,
                    springY +
                    Mathf.Sin(angle) * archHeight,
                    0f));
            }

            List<Vector2> uvs = new List<Vector2>(
                vertices.Count);
            for (int i = 0; i < vertices.Count; i++)
            {
                float u =
                    vertices[i].x / size.x + 0.5f;
                if (mirrorHorizontally)
                    u = 1f - u;

                uvs.Add(new Vector2(
                    u,
                    vertices[i].y / size.y + 0.5f));
            }

            int boundaryCount = vertices.Count - 1;
            List<int> triangles = new List<int>(
                boundaryCount * 6);
            for (int i = 0; i < boundaryCount; i++)
            {
                int current = i + 1;
                int next = (i + 1) % boundaryCount + 1;
                triangles.Add(0);
                triangles.Add(current);
                triangles.Add(next);
                triangles.Add(0);
                triangles.Add(next);
                triangles.Add(current);
            }

            Mesh mesh = new Mesh
            {
                name = $"{name}Mesh",
                vertices = vertices.ToArray(),
                uv = uvs.ToArray(),
                triangles = triangles.ToArray()
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
            material.SetOverrideTag("RenderType", "Transparent");
            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend"))
                material.SetFloat("_Blend", 0f);
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
                    (float)BlendMode.OneMinusSrcAlpha);
            }
            if (material.HasProperty("_ZWrite"))
                material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            runtimeMaterials.Add(material);
            return material;
        }

        private void UpdatePortalView(
            Camera portalCamera,
            Transform surface,
            Renderer surfaceRenderer,
            Vector2 size,
            bool stereo)
        {
            if (viewerCamera == null ||
                portalCamera == null ||
                surface == null ||
                !IsVisibleToViewer(surfaceRenderer, stereo))
            {
                if (portalCamera != null)
                    portalCamera.enabled = false;
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

        private bool IsVisibleToViewer(
            Renderer surfaceRenderer,
            bool stereo)
        {
            if (surfaceRenderer == null ||
                !surfaceRenderer.enabled ||
                !surfaceRenderer.gameObject.activeInHierarchy)
            {
                return false;
            }

            Bounds bounds = surfaceRenderer.bounds;
            bounds.Expand(0.05f);
            return GeometryUtility.TestPlanesAABB(
                       viewerLeftFrustumPlanes,
                       bounds) ||
                   stereo &&
                   GeometryUtility.TestPlanesAABB(
                       viewerRightFrustumPlanes,
                       bounds);
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
                wallSize.x +
                MaximumPortalSideOverflow * 2f);
            float availableHeight = Mathf.Max(
                0.1f,
                wallSize.y +
                MaximumPortalTopOverflow);
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

        private static Vector2 ResolvePitPortalSize(
            Vector2 wallSize)
        {
            if (wallSize.x <= 0f || wallSize.y <= 0f)
            {
                return new Vector2(
                    FallbackPortalWidth,
                    FallbackPortalHeight);
            }

            return new Vector2(
                Mathf.Min(
                    wallSize.x * PitPortalWallFill,
                    MaximumPitPortalWidth),
                Mathf.Min(
                    Mathf.Max(
                        0.1f,
                        wallSize.y - PortalBottomOffset * 2f),
                    MaximumPitPortalHeight));
        }

        private static void ResolvePortalTextureSize(
            Vector2 portalSize,
            bool highQuality,
            out int width,
            out int height)
        {
            if (!highQuality)
            {
                width = TextureSize;
                height = TextureSize;
                return;
            }

            float safeWidth = Mathf.Max(0.1f, portalSize.x);
            float safeHeight = Mathf.Max(0.1f, portalSize.y);
            float targetWidth =
                safeWidth * PitTexturePixelsPerMeter;
            float targetHeight =
                safeHeight * PitTexturePixelsPerMeter;
            float minimumScale = Mathf.Max(
                1f,
                Mathf.Max(
                    PitTextureMinimumWidth / targetWidth,
                    PitTextureMinimumHeight / targetHeight));
            targetWidth *= minimumScale;
            targetHeight *= minimumScale;
            float maximumScale = Mathf.Min(
                1f,
                PitTextureMaximumDimension /
                Mathf.Max(targetWidth, targetHeight));
            targetWidth *= maximumScale;
            targetHeight *= maximumScale;

            width = Mathf.Min(
                PitTextureMaximumDimension,
                RoundTextureDimension(
                    Mathf.CeilToInt(targetWidth)));
            height = Mathf.Min(
                PitTextureMaximumDimension,
                RoundTextureDimension(
                    Mathf.CeilToInt(targetHeight)));
        }

        private static int RoundTextureDimension(int value)
        {
            return Mathf.CeilToInt(value / 32f) * 32;
        }

        private static int ResolvePitTextureAntiAliasing()
        {
            return Application.platform == RuntimePlatform.Android
                ? 2
                : 4;
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
            for (int i = 0;
                 i < roomCarTmpRenderers.Count;
                 i++)
            {
                TmpRendererBinding binding =
                    roomCarTmpRenderers[i];
                SyncTextMeshPro(
                    binding.Source,
                    binding.Proxy);
            }

            for (int i = 0; i < roomCarRenderers.Count; i++)
            {
                RendererBinding binding =
                    roomCarRenderers[i];
                SyncTextMesh(
                    binding.SourceText,
                    binding.ProxyText);
                SyncLineRenderer(
                    binding.SourceLine,
                    binding.ProxyLine);
                SyncTrailRenderer(
                    binding.SourceTrail,
                    binding.ProxyTrail);
                SyncParticleSystem(
                    binding.SourceParticles,
                    binding.ProxyParticles,
                    binding.ParticleBuffer);
                bool visible =
                    binding.Source != null &&
                    binding.Proxy != null &&
                    binding.VehicleRoot != null &&
                    binding.Source.enabled &&
                    binding.Source.gameObject.activeInHierarchy;
                if (visible)
                    visible = BoundsTouchesRoom(
                        binding.Source.bounds);
                if (binding.Proxy != null)
                    binding.Proxy.enabled = visible;
            }
        }

        internal void SetRoomOcclusionAssistance(
            ShowcaseOcclusionHit firstHit,
            ShowcaseOcclusionHit secondHit)
        {
            if (!configured)
            {
                ResetRoomOcclusionAssistance();
                return;
            }

            desiredRoomOccluderMaterials.Clear();
            bool firstDecorativeSuppressed =
                TryAddDecorativeSuppression(
                    firstHit,
                    desiredRoomOccluderMaterials);
            bool secondDecorativeSuppressed =
                TryAddDecorativeSuppression(
                    secondHit,
                    desiredRoomOccluderMaterials);
            ApplyCombinedRoomOccluderSuppression();
            SetVehicleXRay(
                firstVehicle,
                firstHit.IsOccluded &&
                !firstDecorativeSuppressed,
                GetVehicleXRayMaterial(true),
                ref firstVehicleXRayApplied);
            SetVehicleXRay(
                secondVehicle,
                secondHit.IsOccluded &&
                !secondDecorativeSuppressed,
                GetVehicleXRayMaterial(false),
                ref secondVehicleXRayApplied);
        }

        internal void ResetRoomOcclusionAssistance()
        {
            desiredRoomOccluderMaterials.Clear();
            ApplyCombinedRoomOccluderSuppression();
            SetVehicleXRay(
                firstVehicle,
                false,
                null,
                ref firstVehicleXRayApplied);
            SetVehicleXRay(
                secondVehicle,
                false,
                null,
                ref secondVehicleXRayApplied);
        }

        internal void SetPlannedRoomOccluders(
            IReadOnlyCollection<Material> materials)
        {
            plannedRoomOccluderMaterials.Clear();
            if (materials != null)
            {
                foreach (Material material in materials)
                {
                    if (material != null)
                        plannedRoomOccluderMaterials.Add(material);
                }
            }

            ApplyCombinedRoomOccluderSuppression();
        }

        private void ApplyCombinedRoomOccluderSuppression()
        {
            combinedRoomOccluderMaterials.Clear();
            combinedRoomOccluderMaterials.UnionWith(
                plannedRoomOccluderMaterials);
            combinedRoomOccluderMaterials.UnionWith(
                desiredRoomOccluderMaterials);
            ApplyRoomOccluderSuppression(
                combinedRoomOccluderMaterials);
        }

        private bool TryAddDecorativeSuppression(
            ShowcaseOcclusionHit hit,
            HashSet<Material> destination)
        {
            if (!hit.IsOccluded ||
                hit.Kind != ShowcaseOccluderKind.Decorative ||
                hit.Material == null ||
                GetRoomOccluderClearMaterial() == null)
            {
                return false;
            }

            foreach (KeyValuePair<Renderer, Material[]> pair in
                     roomTrackOriginalMaterials)
            {
                Material[] materials = pair.Value;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] != hit.Material)
                        continue;

                    destination.Add(hit.Material);
                    return true;
                }
            }

            return false;
        }

        private void ApplyRoomOccluderSuppression(
            HashSet<Material> desired)
        {
            bool hasDesired = desired != null && desired.Count > 0;
            if ((!hasDesired &&
                 suppressedRoomOccluderMaterials.Count == 0) ||
                hasDesired &&
                suppressedRoomOccluderMaterials.SetEquals(desired))
            {
                return;
            }

            suppressedRoomOccluderMaterials.Clear();
            if (hasDesired)
                suppressedRoomOccluderMaterials.UnionWith(desired);

            Material clearMaterial = hasDesired
                ? GetRoomOccluderClearMaterial()
                : null;
            foreach (KeyValuePair<Renderer, Material[]> pair in
                     roomTrackOriginalMaterials)
            {
                Renderer renderer = pair.Key;
                if (renderer == null)
                    continue;

                Material[] originals = pair.Value;
                Material[] applied =
                    (Material[])originals.Clone();
                if (clearMaterial != null)
                {
                    for (int i = 0; i < applied.Length; i++)
                    {
                        if (suppressedRoomOccluderMaterials.Contains(
                                originals[i]))
                        {
                            applied[i] = clearMaterial;
                        }
                    }
                }

                renderer.sharedMaterials = applied;
            }
        }

        private void SetVehicleXRay(
            Transform vehicleRoot,
            bool enabled,
            Material xRayMaterial,
            ref bool appliedState)
        {
            enabled &= xRayMaterial != null;
            if (appliedState == enabled)
                return;

            foreach (KeyValuePair<Renderer, Material[]> pair in
                     roomVehicleOriginalMaterials)
            {
                Renderer proxy = pair.Key;
                if (proxy == null ||
                    vehicleRoot == null ||
                    !proxy.transform.IsChildOf(vehicleRoot))
                {
                    continue;
                }

                if (enabled)
                {
                    Material[] xRayMaterials =
                        new Material[pair.Value.Length];
                    for (int i = 0;
                         i < xRayMaterials.Length;
                         i++)
                    {
                        xRayMaterials[i] = xRayMaterial;
                    }
                    proxy.sharedMaterials = xRayMaterials;
                    proxy.SetPropertyBlock(null);
                }
                else
                {
                    proxy.sharedMaterials = pair.Value;
                    Renderer source = FindSourceRenderer(proxy);
                    if (source != null)
                    {
                        MaterialPropertyBlock block =
                            new MaterialPropertyBlock();
                        source.GetPropertyBlock(block);
                        proxy.SetPropertyBlock(block);
                    }
                }
            }

            appliedState = enabled;
        }

        private Renderer FindSourceRenderer(Renderer proxy)
        {
            for (int i = 0; i < roomCarRenderers.Count; i++)
            {
                if (roomCarRenderers[i].Proxy == proxy)
                    return roomCarRenderers[i].Source;
            }

            return null;
        }

        private Material GetRoomOccluderClearMaterial()
        {
            if (roomOccluderClearMaterial == null)
            {
                roomOccluderClearMaterial =
                    CreateOcclusionAssistMaterial(
                        "Runtime_RoomOccluderClear",
                        new Color(0f, 0f, 0f, 0f),
                        false,
                        3000);
            }

            return roomOccluderClearMaterial;
        }

        private Material GetVehicleXRayMaterial(bool first)
        {
            Material material = first
                ? firstVehicleXRayMaterial
                : secondVehicleXRayMaterial;
            if (material != null)
                return material;

            Color color = first
                ? new Color(0.05f, 0.78f, 1f, 0.62f)
                : new Color(1f, 0.12f, 0.62f, 0.62f);
            material = CreateOcclusionAssistMaterial(
                first
                    ? "Runtime_FirstVehicleXRay"
                    : "Runtime_SecondVehicleXRay",
                color,
                true,
                first ? 3250 : 3251);
            if (first)
                firstVehicleXRayMaterial = material;
            else
                secondVehicleXRayMaterial = material;
            return material;
        }

        private Material CreateOcclusionAssistMaterial(
            string name,
            Color color,
            bool depthAlways,
            int renderQueue)
        {
            Shader shader = Shader.Find(
                "Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null)
                return null;

            Material material = new(shader) { name = name };
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
                    (float)BlendMode.OneMinusSrcAlpha);
            }
            if (material.HasProperty("_ZWrite"))
                material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_ZTest") && depthAlways)
            {
                material.SetFloat(
                    "_ZTest",
                    (float)CompareFunction.Always);
            }
            if (material.HasProperty("_Cull"))
                material.SetFloat("_Cull", (float)CullMode.Off);
            if (material.HasProperty(BaseColorId))
                material.SetColor(BaseColorId, color);
            if (material.HasProperty(ColorId))
                material.SetColor(ColorId, color);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = renderQueue;
            runtimeMaterials.Add(material);
            return material;
        }

        private bool BoundsTouchesRoom(Bounds bounds)
        {
            if (IsInsideRoom(bounds.center))
                return true;

            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = new(
                    (corner & 1) == 0 ? min.x : max.x,
                    (corner & 2) == 0 ? min.y : max.y,
                    (corner & 4) == 0 ? min.z : max.z);
                if (IsInsideRoom(point))
                    return true;
            }

            return false;
        }

        private static void SyncLineRenderer(
            LineRenderer source,
            LineRenderer destination)
        {
            if (source == null ||
                destination == null)
            {
                return;
            }

            destination.startWidth = source.startWidth;
            destination.endWidth = source.endWidth;
            destination.startColor = source.startColor;
            destination.endColor = source.endColor;

            if (destination.positionCount != source.positionCount)
                destination.positionCount = source.positionCount;

            for (int i = 0; i < source.positionCount; i++)
                destination.SetPosition(i, source.GetPosition(i));
        }

        private static void CopyLineRendererSettings(
            LineRenderer source,
            LineRenderer destination)
        {
            destination.useWorldSpace = source.useWorldSpace;
            destination.loop = source.loop;
            destination.widthMultiplier = source.widthMultiplier;
            destination.widthCurve = source.widthCurve;
            destination.colorGradient = source.colorGradient;
            destination.numCornerVertices = source.numCornerVertices;
            destination.numCapVertices = source.numCapVertices;
            destination.alignment = source.alignment;
            destination.textureMode = source.textureMode;
            destination.generateLightingData =
                source.generateLightingData;
        }

        private static void SyncTrailRenderer(
            TrailRenderer source,
            TrailRenderer destination)
        {
            if (source == null ||
                destination == null)
            {
                return;
            }

            destination.Clear();
            destination.widthMultiplier =
                source.widthMultiplier;
            destination.time = source.time;
            destination.emitting = false;

            for (int i = 0; i < source.positionCount; i++)
                destination.AddPosition(source.GetPosition(i));
        }

        private static void CopyTrailRendererSettings(
            TrailRenderer source,
            TrailRenderer destination)
        {
            destination.autodestruct = false;
            destination.emitting = false;
            destination.time = source.time;
            destination.minVertexDistance =
                source.minVertexDistance;
            destination.widthMultiplier =
                source.widthMultiplier;
            destination.widthCurve = source.widthCurve;
            destination.colorGradient =
                source.colorGradient;
            destination.numCornerVertices =
                source.numCornerVertices;
            destination.numCapVertices =
                source.numCapVertices;
            destination.alignment = source.alignment;
            destination.textureMode =
                source.textureMode;
            destination.generateLightingData =
                source.generateLightingData;
        }

        private static void SyncParticleSystem(
            ParticleSystem source,
            ParticleSystem destination,
            ParticleSystem.Particle[] buffer)
        {
            if (source == null ||
                destination == null ||
                buffer == null)
            {
                return;
            }

            int count = source.GetParticles(buffer);
            if (count > 0)
                destination.SetParticles(buffer, count);
            else
                destination.Clear(false);
        }

        private static void CopyParticleSystemSettings(
            ParticleSystem source,
            ParticleSystemRenderer sourceRenderer,
            ParticleSystem destination,
            ParticleSystemRenderer destinationRenderer)
        {
            ParticleSystem.MainModule sourceMain =
                source.main;
            ParticleSystem.MainModule destinationMain =
                destination.main;
            destinationMain.loop = false;
            destinationMain.playOnAwake = false;
            destinationMain.simulationSpace =
                sourceMain.simulationSpace;
            destinationMain.simulationSpeed = 0f;
            destinationMain.maxParticles =
                sourceMain.maxParticles;
            destinationMain.stopAction =
                ParticleSystemStopAction.None;

            ParticleSystem.EmissionModule emission =
                destination.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape =
                destination.shape;
            shape.enabled = false;
            ParticleSystem.CollisionModule collision =
                destination.collision;
            collision.enabled = false;
            ParticleSystem.TrailModule trails =
                destination.trails;
            trails.enabled = false;
            ParticleSystem.LightsModule lights =
                destination.lights;
            lights.enabled = false;

            destinationRenderer.renderMode =
                sourceRenderer.renderMode;
            destinationRenderer.velocityScale =
                sourceRenderer.velocityScale;
            destinationRenderer.lengthScale =
                sourceRenderer.lengthScale;
            destinationRenderer.cameraVelocityScale =
                sourceRenderer.cameraVelocityScale;
            destinationRenderer.sortingFudge =
                sourceRenderer.sortingFudge;
            destination.Play(false);
        }

        private static void SyncTextMesh(
            TextMesh source,
            TextMesh destination)
        {
            if (source == null ||
                destination == null)
            {
                return;
            }

            if (destination.text != source.text)
                destination.text = source.text;
            if (destination.font != source.font)
                destination.font = source.font;
            if (destination.fontSize != source.fontSize)
                destination.fontSize = source.fontSize;
            if (destination.fontStyle != source.fontStyle)
                destination.fontStyle = source.fontStyle;
            if (destination.anchor != source.anchor)
                destination.anchor = source.anchor;
            if (destination.alignment != source.alignment)
                destination.alignment = source.alignment;
            if (destination.characterSize != source.characterSize)
                destination.characterSize = source.characterSize;
            if (destination.lineSpacing != source.lineSpacing)
                destination.lineSpacing = source.lineSpacing;
            if (destination.tabSize != source.tabSize)
                destination.tabSize = source.tabSize;
            if (destination.richText != source.richText)
                destination.richText = source.richText;
            if (destination.color != source.color)
                destination.color = source.color;
        }

        private static void SyncTextMeshPro(
            TextMeshPro source,
            TextMeshPro destination)
        {
            if (source == null || destination == null)
                return;

            destination.text = source.text;
            destination.font = source.font;
            destination.fontSharedMaterial =
                source.fontSharedMaterial;
            destination.fontSize = source.fontSize;
            destination.fontStyle = source.fontStyle;
            destination.alignment = source.alignment;
            destination.color = source.color;
            destination.richText = source.richText;
            destination.autoSizeTextContainer =
                source.autoSizeTextContainer;
            destination.enableAutoSizing =
                source.enableAutoSizing;
            destination.fontSizeMin = source.fontSizeMin;
            destination.fontSizeMax = source.fontSizeMax;
            destination.characterSpacing =
                source.characterSpacing;
            destination.wordSpacing = source.wordSpacing;
            destination.lineSpacing = source.lineSpacing;
            destination.overflowMode = source.overflowMode;
            destination.margin = source.margin;
            destination.ForceMeshUpdate();
        }

        private static void CopyRendererSettings(
            Renderer source,
            Renderer destination)
        {
            destination.sharedMaterials =
                source.sharedMaterials;
            destination.shadowCastingMode =
                ShadowCastingMode.Off;
            destination.receiveShadows = false;
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

        private static bool IsDriverLabelRenderer(
            Renderer renderer)
        {
            return renderer != null &&
                renderer.gameObject.name.StartsWith(
                    "DriverLabel",
                    System.StringComparison.Ordinal);
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

        private readonly struct LabelRendererState
        {
            private readonly Renderer renderer;
            private readonly bool forceRenderingOff;

            public LabelRendererState(Renderer source)
            {
                renderer = source;
                forceRenderingOff =
                    source != null &&
                    source.forceRenderingOff;
            }

            public void Restore()
            {
                if (renderer != null)
                    renderer.forceRenderingOff =
                        forceRenderingOff;
            }
        }

        private readonly struct TmpRendererBinding
        {
            public TmpRendererBinding(
                TextMeshPro source,
                TextMeshPro proxy)
            {
                Source = source;
                Proxy = proxy;
            }

            public TextMeshPro Source { get; }
            public TextMeshPro Proxy { get; }
        }

        private readonly struct RendererBinding
        {
            public readonly Renderer Source;
            public readonly Renderer Proxy;
            public readonly Transform VehicleRoot;
            public readonly TextMesh SourceText;
            public readonly TextMesh ProxyText;
            public readonly LineRenderer SourceLine;
            public readonly LineRenderer ProxyLine;
            public readonly TrailRenderer SourceTrail;
            public readonly TrailRenderer ProxyTrail;
            public readonly ParticleSystem SourceParticles;
            public readonly ParticleSystem ProxyParticles;
            public readonly ParticleSystem.Particle[] ParticleBuffer;

            public RendererBinding(
                Renderer source,
                Renderer proxy,
                Transform vehicleRoot)
                : this(
                    source,
                    proxy,
                    vehicleRoot,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null)
            {
            }

            public RendererBinding(
                TrailRenderer source,
                TrailRenderer proxy,
                Transform vehicleRoot)
                : this(
                    source,
                    proxy,
                    vehicleRoot,
                    null,
                    null,
                    null,
                    null,
                    source,
                    proxy,
                    null,
                    null)
            {
            }

            public RendererBinding(
                ParticleSystemRenderer source,
                ParticleSystemRenderer proxy,
                Transform vehicleRoot,
                ParticleSystem sourceParticles,
                ParticleSystem proxyParticles)
                : this(
                    source,
                    proxy,
                    vehicleRoot,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    sourceParticles,
                    proxyParticles)
            {
            }

            public RendererBinding(
                Renderer source,
                Renderer proxy,
                Transform vehicleRoot,
                TextMesh sourceText,
                TextMesh proxyText)
                : this(
                    source,
                    proxy,
                    vehicleRoot,
                    sourceText,
                    proxyText,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null)
            {
            }

            public RendererBinding(
                Renderer source,
                Renderer proxy,
                Transform vehicleRoot,
                TextMesh sourceText,
                TextMesh proxyText,
                LineRenderer sourceLine,
                LineRenderer proxyLine,
                TrailRenderer sourceTrail = null,
                TrailRenderer proxyTrail = null,
                ParticleSystem sourceParticles = null,
                ParticleSystem proxyParticles = null)
            {
                Source = source;
                Proxy = proxy;
                VehicleRoot = vehicleRoot;
                SourceText = sourceText;
                ProxyText = proxyText;
                SourceLine = sourceLine;
                ProxyLine = proxyLine;
                SourceTrail = sourceTrail;
                ProxyTrail = proxyTrail;
                SourceParticles = sourceParticles;
                ProxyParticles = proxyParticles;
                ParticleBuffer =
                    sourceParticles != null
                        ? new ParticleSystem.Particle[
                            Mathf.Max(
                                1,
                                sourceParticles.main
                                    .maxParticles)]
                        : null;
            }
        }
    }
}
