using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;
using Unity.XR.CoreUtils;
using UnityEngine.XR.ARFoundation;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace F1XR.RestAPI.Replay.Room
{
    public enum MetaSceneRoomStatus
    {
        Idle,
        WaitingForPermission,
        Loading,
        OpeningSpaceSetup,
        Ready,
        PermissionDenied,
        NoSceneModel,
        Failed
    }

    public enum MetaSceneSurfaceKind
    {
        Wall,
        Floor,
        Ceiling,
        Table
    }

    public sealed class MetaSceneSurfaceSnapshot
    {
        readonly ReadOnlyCollection<Vector3> boundary;
        readonly ReadOnlyCollection<Vector2> localBoundary;

        internal MetaSceneSurfaceSnapshot(
            Guid id,
            MetaSceneSurfaceKind kind,
            OVRSemanticLabels.Classification classification,
            Vector3 center,
            Quaternion rotation,
            Vector3 normal,
            Vector3 horizontalAxis,
            Vector3 verticalAxis,
            Rect localBounds,
            List<Vector3> boundary,
            List<Vector2> localBoundary,
            Matrix4x4 worldToLocal)
        {
            Id = id;
            Kind = kind;
            Classification = classification;
            Center = center;
            Rotation = rotation;
            Normal = normal;
            HorizontalAxis = horizontalAxis;
            VerticalAxis = verticalAxis;
            LocalBounds = localBounds;
            this.boundary = boundary.AsReadOnly();
            this.localBoundary = localBoundary.AsReadOnly();
            WorldToLocal = worldToLocal;
        }

        public Guid Id { get; }
        public MetaSceneSurfaceKind Kind { get; }
        public OVRSemanticLabels.Classification Classification { get; }
        public Vector3 Center { get; }
        public Quaternion Rotation { get; }
        public Vector3 Normal { get; internal set; }
        public Vector3 HorizontalAxis { get; internal set; }
        public Vector3 VerticalAxis { get; internal set; }
        public Rect LocalBounds { get; }
        public IReadOnlyList<Vector3> Boundary => boundary;
        public IReadOnlyList<Vector2> LocalBoundary => localBoundary;
        public Matrix4x4 WorldToLocal { get; }
        public float Width => LocalBounds.width;
        public float Height => LocalBounds.height;

        internal bool ContainsProjectedPoint(
            Vector3 worldPoint,
            float maximumHeight,
            out float area)
        {
            area = PolygonArea(localBoundary);
            if (localBoundary.Count < 3)
                return false;

            Vector3 local = WorldToLocal.MultiplyPoint3x4(worldPoint);
            float height = Mathf.Abs(local.z);
            return height <= maximumHeight &&
                ContainsPoint(localBoundary, new Vector2(local.x, local.y));
        }

        internal float DistanceToProjectedPoint(Vector3 worldPoint)
        {
            Vector3 local = WorldToLocal.MultiplyPoint3x4(worldPoint);
            var point = new Vector2(local.x, local.y);
            float planarDistance = 0f;
            if (!ContainsPoint(localBoundary, point))
            {
                planarDistance = float.PositiveInfinity;
                for (int i = 0; i < localBoundary.Count; i++)
                {
                    int next = (i + 1) % localBoundary.Count;
                    planarDistance = Mathf.Min(
                        planarDistance,
                        DistanceToSegment(
                            point,
                            localBoundary[i],
                            localBoundary[next]));
                }
            }

            return Mathf.Sqrt(
                planarDistance * planarDistance + local.z * local.z);
        }

        static bool ContainsPoint(
            IReadOnlyList<Vector2> polygon,
            Vector2 point)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1;
                 i < polygon.Count;
                 j = i++)
            {
                Vector2 first = polygon[i];
                Vector2 second = polygon[j];
                if ((first.y > point.y) == (second.y > point.y))
                    continue;

                float intersectionX =
                    (second.x - first.x) *
                    (point.y - first.y) /
                    (second.y - first.y) +
                    first.x;
                if (point.x < intersectionX)
                    inside = !inside;
            }

            return inside;
        }

        static float PolygonArea(IReadOnlyList<Vector2> polygon)
        {
            float twiceArea = 0f;
            for (int i = 0; i < polygon.Count; i++)
            {
                int next = (i + 1) % polygon.Count;
                twiceArea +=
                    polygon[i].x * polygon[next].y -
                    polygon[next].x * polygon[i].y;
            }

            return Mathf.Abs(twiceArea) * 0.5f;
        }

        static float DistanceToSegment(
            Vector2 point,
            Vector2 start,
            Vector2 end)
        {
            Vector2 edge = end - start;
            float squareLength = edge.sqrMagnitude;
            if (squareLength <= 0.000001f)
                return Vector2.Distance(point, start);

            float interpolation = Mathf.Clamp01(
                Vector2.Dot(point - start, edge) / squareLength);
            return Vector2.Distance(
                point,
                start + edge * interpolation);
        }
    }

    public sealed class MetaSceneRoomSnapshot
    {
        readonly ReadOnlyCollection<MetaSceneSurfaceSnapshot> walls;
        readonly ReadOnlyCollection<MetaSceneSurfaceSnapshot> floors;
        readonly ReadOnlyCollection<MetaSceneSurfaceSnapshot> ceilings;
        readonly ReadOnlyCollection<MetaSceneSurfaceSnapshot> tables;

        internal MetaSceneRoomSnapshot(
            Guid id,
            List<MetaSceneSurfaceSnapshot> walls,
            List<MetaSceneSurfaceSnapshot> floors,
            List<MetaSceneSurfaceSnapshot> ceilings,
            List<MetaSceneSurfaceSnapshot> tables)
        {
            Id = id;
            this.walls = walls.AsReadOnly();
            this.floors = floors.AsReadOnly();
            this.ceilings = ceilings.AsReadOnly();
            this.tables = tables.AsReadOnly();
            Center = CalculateCenter(walls, floors, ceilings);
            OrientWallsTowardCenter();
            Signature = CalculateSignature();
        }

        public Guid Id { get; }
        public IReadOnlyList<MetaSceneSurfaceSnapshot> Walls => walls;
        public IReadOnlyList<MetaSceneSurfaceSnapshot> Floors => floors;
        public IReadOnlyList<MetaSceneSurfaceSnapshot> Ceilings => ceilings;
        public IReadOnlyList<MetaSceneSurfaceSnapshot> Tables => tables;
        public Vector3 Center { get; }
        internal int Signature { get; }

        internal bool Contains(
            Vector3 worldPoint,
            float maximumHeight,
            out float containingFloorArea)
        {
            containingFloorArea = float.PositiveInfinity;
            bool contains = false;
            for (int i = 0; i < floors.Count; i++)
            {
                if (!floors[i].ContainsProjectedPoint(
                        worldPoint,
                        maximumHeight,
                        out float area))
                {
                    continue;
                }

                contains = true;
                containingFloorArea = Mathf.Min(containingFloorArea, area);
            }

            return contains;
        }

        internal float DistanceToClosestFloor(Vector3 worldPoint)
        {
            float closest = float.PositiveInfinity;
            for (int i = 0; i < floors.Count; i++)
            {
                MetaSceneSurfaceSnapshot floor = floors[i];
                closest = Mathf.Min(
                    closest,
                    floor.DistanceToProjectedPoint(worldPoint));
            }

            return closest;
        }

        void OrientWallsTowardCenter()
        {
            for (int i = 0; i < walls.Count; i++)
            {
                MetaSceneSurfaceSnapshot wall = walls[i];
                Vector3 normal = Vector3.ProjectOnPlane(
                    wall.Normal,
                    Vector3.up).normalized;
                if (normal.sqrMagnitude < 0.5f)
                    normal = wall.Normal.normalized;
                if (Vector3.Dot(Center - wall.Center, normal) < 0f)
                    normal = -normal;

                Vector3 vertical = Vector3.ProjectOnPlane(
                    Vector3.up,
                    normal).normalized;
                if (vertical.sqrMagnitude < 0.5f)
                    vertical = wall.VerticalAxis.normalized;
                if (Vector3.Dot(vertical, Vector3.up) < 0f)
                    vertical = -vertical;

                wall.Normal = normal;
                wall.VerticalAxis = vertical;
                wall.HorizontalAxis = Vector3.Cross(vertical, normal).normalized;
            }
        }

        int CalculateSignature()
        {
            unchecked
            {
                int hash = Id.GetHashCode();
                HashSurfaces(walls, ref hash);
                HashSurfaces(floors, ref hash);
                HashSurfaces(ceilings, ref hash);
                HashSurfaces(tables, ref hash);
                return hash;
            }
        }

        static void HashSurfaces(
            IReadOnlyList<MetaSceneSurfaceSnapshot> surfaces,
            ref int hash)
        {
            for (int i = 0; i < surfaces.Count; i++)
            {
                MetaSceneSurfaceSnapshot surface = surfaces[i];
                hash = hash * 31 + surface.Id.GetHashCode();
                hash = hash * 31 + Mathf.RoundToInt(surface.Center.x * 100f);
                hash = hash * 31 + Mathf.RoundToInt(surface.Center.y * 100f);
                hash = hash * 31 + Mathf.RoundToInt(surface.Center.z * 100f);
                hash = hash * 31 + Mathf.RoundToInt(surface.Width * 100f);
                hash = hash * 31 + Mathf.RoundToInt(surface.Height * 100f);
            }
        }

        static Vector3 CalculateCenter(
            IReadOnlyList<MetaSceneSurfaceSnapshot> walls,
            IReadOnlyList<MetaSceneSurfaceSnapshot> floors,
            IReadOnlyList<MetaSceneSurfaceSnapshot> ceilings)
        {
            Vector3 sum = Vector3.zero;
            int count = 0;
            AddCenters(walls, ref sum, ref count);
            AddCenters(floors, ref sum, ref count);
            AddCenters(ceilings, ref sum, ref count);
            return count > 0 ? sum / count : Vector3.zero;
        }

        static void AddCenters(
            IReadOnlyList<MetaSceneSurfaceSnapshot> surfaces,
            ref Vector3 sum,
            ref int count)
        {
            for (int i = 0; i < surfaces.Count; i++)
            {
                sum += surfaces[i].Center;
                count++;
            }
        }
    }

    [DefaultExecutionOrder(-800)]
    [DisallowMultipleComponent]
    public sealed class MetaSceneRoomSource : MonoBehaviour
    {
        const string ScenePermission = "com.oculus.permission.USE_SCENE";

        [SerializeField] Transform trackingSpace;
        [SerializeField] Camera roomCamera;
        [SerializeField] ARPlaneManager legacyPlaneManager;
        [SerializeField] ARRaycastManager legacyRaycastManager;
        [SerializeField, Min(1f)] float runtimeReadyTimeout = 10f;
        [SerializeField, Min(1f)] float loadTimeout = 15f;
        [SerializeField, Min(1f)] float maximumCameraHeightAboveFloor = 3.5f;
        [SerializeField] bool openSpaceSetupWhenMissing = true;
        [SerializeField] bool logRoomSummary = true;

        readonly List<MetaSceneSurfaceSnapshot> emptySurfaces = new();
        ReadOnlyCollection<MetaSceneSurfaceSnapshot> readOnlyEmptySurfaces;
        MetaSceneRoomSnapshot currentRoom;
        bool isLoading;
        bool spaceSetupAttempted;
        bool legacyManagersSuspended;
        bool restoreLegacyPlaneManager;
        bool restoreLegacyRaycastManager;
        int loadGeneration;
#if UNITY_ANDROID && !UNITY_EDITOR
        PermissionCallbacks permissionCallbacks;
        bool permissionRequestPending;
        bool permissionDeniedUntilRetry;
#endif

        public MetaSceneRoomStatus Status { get; private set; } =
            MetaSceneRoomStatus.Idle;
        public string StatusMessage { get; private set; }
        public MetaSceneRoomSnapshot CurrentRoom => currentRoom;
        public IReadOnlyList<MetaSceneSurfaceSnapshot> Walls =>
            currentRoom?.Walls ?? EmptySurfaces;
        public IReadOnlyList<MetaSceneSurfaceSnapshot> Floors =>
            currentRoom?.Floors ?? EmptySurfaces;
        public IReadOnlyList<MetaSceneSurfaceSnapshot> Ceilings =>
            currentRoom?.Ceilings ?? EmptySurfaces;
        public IReadOnlyList<MetaSceneSurfaceSnapshot> Tables =>
            currentRoom?.Tables ?? EmptySurfaces;

        IReadOnlyList<MetaSceneSurfaceSnapshot> EmptySurfaces =>
            readOnlyEmptySurfaces ??= emptySurfaces.AsReadOnly();

        public event Action RoomChanged;
        public event Action<MetaSceneRoomStatus> StatusChanged;

        void Awake()
        {
#if UNITY_EDITOR
            // Quest Link reads the saved Scene Model through Unity Meta
            // OpenXR/ARFoundation. Do not start a second Meta Core spatial
            // stack or add OVRManager in the Editor.
            return;
#else
            ResolveReferences();
            SuspendLegacySceneManagers();
            EnsureMetaRuntime();
#endif
        }

        void Start()
        {
#if !UNITY_EDITOR
            _ = RefreshRoomAsync();
#endif
        }

        void OnDisable()
        {
            loadGeneration++;
            isLoading = false;
            RestoreLegacySceneManagers();
#if UNITY_ANDROID && !UNITY_EDITOR
            permissionRequestPending = false;
            ClearPermissionCallbacks();
#endif
        }

        void OnApplicationFocus(bool hasFocus)
        {
#if UNITY_EDITOR
            // Game/Scene view focus changes are not an application resume.
            // Querying again here can leave a native spatial-entity request
            // alive while the Editor is exiting Play Mode.
            return;
#else
            if (!hasFocus || !isActiveAndEnabled || isLoading)
                return;

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(ScenePermission))
                return;
#endif
            _ = RefreshRoomAsync();
#endif
        }

        public async Task RefreshRoomAsync()
        {
#if UNITY_EDITOR
            // WallDiscovery and table placement use ARPlaneManager in Link.
            await Task.CompletedTask;
            return;
#else
            if (!isActiveAndEnabled)
                return;

            ResolveReferences();
            EnsureMetaRuntime();
            if (!EnsureScenePermission())
                return;

            if (isLoading)
                return;

            SuspendLegacySceneManagers();
            isLoading = true;
            int generation = ++loadGeneration;
            try
            {
                if (!await WaitForMetaRuntimeAsync(generation))
                {
                    if (IsCurrent(generation))
                    {
                        StatusMessage =
                            "Meta XR Core did not initialize. Verify that Meta XR Feature and Spatial Data over Meta Quest Link are enabled, then restart Play Mode.";
                        Debug.LogWarning($"[MetaScene] {StatusMessage}", this);
                        SetStatus(MetaSceneRoomStatus.Failed);
                    }
                    return;
                }

                SetStatus(MetaSceneRoomStatus.Loading);
                List<MetaSceneRoomSnapshot> rooms =
                    await FetchRoomsWithTimeoutAsync(generation);
                if (!IsCurrent(generation))
                    return;

                if (rooms == null)
                {
                    StatusMessage = ResolveSceneQueryFailureMessage();
                    SetStatus(MetaSceneRoomStatus.Failed);
                    return;
                }

                if (rooms.Count == 0 &&
                    openSpaceSetupWhenMissing &&
                    !spaceSetupAttempted)
                {
                    spaceSetupAttempted = true;
#if UNITY_ANDROID && !UNITY_EDITOR
                    SetStatus(MetaSceneRoomStatus.OpeningSpaceSetup);
                    bool captured = await OVRScene.RequestSpaceSetup();
                    if (!IsCurrent(generation))
                        return;

                    if (!captured)
                    {
                        SetStatus(MetaSceneRoomStatus.NoSceneModel);
                        return;
                    }

                    SetStatus(MetaSceneRoomStatus.Loading);
                    rooms = await FetchRoomsWithTimeoutAsync(generation);
                    if (!IsCurrent(generation))
                        return;
#endif
                }

                if (rooms == null)
                {
                    StatusMessage = ResolveSceneQueryFailureMessage();
                    SetStatus(MetaSceneRoomStatus.Failed);
                    return;
                }

                if (rooms.Count == 0)
                {
                    PublishRoom(null);
                    StatusMessage = ResolveMissingSceneMessage();
                    SetStatus(MetaSceneRoomStatus.NoSceneModel);
                    return;
                }

                MetaSceneRoomSnapshot selected = SelectCurrentRoom(rooms);
                PublishRoom(selected);
                StatusMessage = null;
                SetStatus(MetaSceneRoomStatus.Ready);
                if (logRoomSummary && selected != null)
                {
                    Debug.Log(
                        $"[MetaScene] Room {selected.Id} ready: " +
                        $"walls={selected.Walls.Count} floors={selected.Floors.Count} " +
                        $"ceilings={selected.Ceilings.Count} tables={selected.Tables.Count}.",
                        this);
                }
            }
            catch (Exception exception)
            {
                if (IsCurrent(generation))
                {
                    Debug.LogException(exception, this);
                    StatusMessage = $"Meta room loading failed: {exception.Message}";
                    SetStatus(MetaSceneRoomStatus.Failed);
                }
            }
            finally
            {
                if (generation == loadGeneration)
                {
                    isLoading = false;
                    RestoreLegacySceneManagers();
                }
            }
#endif
        }

        public void RetryRoomSetup()
        {
            spaceSetupAttempted = false;
#if UNITY_ANDROID && !UNITY_EDITOR
            permissionDeniedUntilRetry = false;
#endif
            _ = RefreshRoomAsync();
        }

        bool EnsureScenePermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Permission.HasUserAuthorizedPermission(ScenePermission))
            {
                permissionRequestPending = false;
                permissionDeniedUntilRetry = false;
                ClearPermissionCallbacks();
                return true;
            }

            if (permissionDeniedUntilRetry)
            {
                SetStatus(MetaSceneRoomStatus.PermissionDenied);
                return false;
            }

            SetStatus(MetaSceneRoomStatus.WaitingForPermission);
            if (permissionRequestPending)
                return false;

            permissionRequestPending = true;
            permissionCallbacks = new PermissionCallbacks();
            permissionCallbacks.PermissionGranted += OnPermissionGranted;
            permissionCallbacks.PermissionDenied += OnPermissionDenied;
            permissionCallbacks.PermissionDeniedAndDontAskAgain +=
                OnPermissionDenied;
            Permission.RequestUserPermission(
                ScenePermission,
                permissionCallbacks);
            return false;
#else
            return true;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        void OnPermissionGranted(string permission)
        {
            permissionRequestPending = false;
            ClearPermissionCallbacks();
            if (!isActiveAndEnabled)
                return;

            Debug.Log($"[MetaScene] Scene permission granted: {permission}.", this);
            _ = RefreshRoomAsync();
        }

        void OnPermissionDenied(string permission)
        {
            permissionRequestPending = false;
            permissionDeniedUntilRetry = true;
            ClearPermissionCallbacks();
            if (!isActiveAndEnabled)
                return;

            Debug.LogWarning(
                $"[MetaScene] Scene permission denied: {permission}.",
                this);
            SetStatus(MetaSceneRoomStatus.PermissionDenied);
        }

        void ClearPermissionCallbacks()
        {
            if (permissionCallbacks == null)
                return;

            permissionCallbacks.PermissionGranted -= OnPermissionGranted;
            permissionCallbacks.PermissionDenied -= OnPermissionDenied;
            permissionCallbacks.PermissionDeniedAndDontAskAgain -=
                OnPermissionDenied;
            permissionCallbacks = null;
        }
#endif

        async Task<bool> WaitForMetaRuntimeAsync(int generation)
        {
            float deadline = Time.realtimeSinceStartup + runtimeReadyTimeout;
            while (IsCurrent(generation) &&
                !IsMetaRuntimeReady() &&
                Time.realtimeSinceStartup < deadline)
            {
                await Task.Yield();
            }

            return IsCurrent(generation) && IsMetaRuntimeReady();
        }

        bool IsMetaRuntimeReady()
        {
            return OVRPlugin.initialized && OVRManager.OVRManagerinitialized;
        }

        async Task<List<MetaSceneRoomSnapshot>>
            FetchRoomsWithTimeoutAsync(int generation)
        {
            float startedAt = Time.realtimeSinceStartup;
            List<MetaSceneRoomSnapshot> rooms =
                await FetchRoomsAsync(generation);
            float elapsed = Time.realtimeSinceStartup - startedAt;
            if (elapsed > loadTimeout)
            {
                Debug.LogWarning(
                    $"[MetaScene] Room load completed after {elapsed:0.#} " +
                    $"seconds (target {loadTimeout:0.#} seconds).",
                    this);
            }

            return rooms;
        }

        async Task<List<MetaSceneRoomSnapshot>> FetchRoomsAsync(int generation)
        {
            var roomAnchors = new List<OVRAnchor>();
            try
            {
                var roomResult = await OVRAnchor.FetchAnchorsAsync(
                    roomAnchors,
                    new OVRAnchor.FetchOptions
                    {
                        SingleComponentType = typeof(OVRRoomLayout)
                    });
                if (!roomResult.Success)
                {
                    StatusMessage =
                        $"Meta Scene anchor query failed ({roomResult.Status}). " +
                        ResolveSceneAccessHint();
                    Debug.LogWarning($"[MetaScene] {StatusMessage}", this);
                    return null;
                }

                if (logRoomSummary)
                {
                    Debug.Log(
                        $"[MetaScene] Room anchor query returned {roomAnchors.Count} room(s).",
                        this);
                }

                if (!IsCurrent(generation))
                    return new List<MetaSceneRoomSnapshot>();

                roomAnchors.Sort((first, second) =>
                    first.Uuid.CompareTo(second.Uuid));
                var rooms = new List<MetaSceneRoomSnapshot>(roomAnchors.Count);
                for (int i = 0; i < roomAnchors.Count; i++)
                {
                    if (!IsCurrent(generation))
                        return rooms;

                    MetaSceneRoomSnapshot room = await BuildRoomAsync(
                        roomAnchors[i],
                        generation);
                    if (room != null)
                        rooms.Add(room);
                }

                return rooms;
            }
            finally
            {
                DisposeFetchedAnchors(roomAnchors, generation);
            }
        }

        async Task<MetaSceneRoomSnapshot> BuildRoomAsync(
            OVRAnchor roomAnchor,
            int generation)
        {
            if (!roomAnchor.TryGetComponent(out OVRRoomLayout _) ||
                !roomAnchor.TryGetComponent(out OVRAnchorContainer container))
            {
                Debug.LogWarning(
                    $"[MetaScene] Room {roomAnchor.Uuid} has no anchor container.",
                    this);
                return null;
            }

            var childAnchors = new List<OVRAnchor>();
            try
            {
                var childResult = await container.FetchAnchorsAsync(childAnchors);
                if (!childResult.Success)
                {
                    Debug.LogWarning(
                        $"[MetaScene] Child anchor query failed for room " +
                        $"{roomAnchor.Uuid} ({childResult.Status}).",
                        this);
                    return null;
                }

                if (!IsCurrent(generation))
                    return null;

                childAnchors.Sort((first, second) =>
                    first.Uuid.CompareTo(second.Uuid));
                var walls = new List<MetaSceneSurfaceSnapshot>();
                var floors = new List<MetaSceneSurfaceSnapshot>();
                var ceilings = new List<MetaSceneSurfaceSnapshot>();
                var tables = new List<MetaSceneSurfaceSnapshot>();
                for (int i = 0; i < childAnchors.Count; i++)
                {
                    MetaSceneSurfaceSnapshot surface = await BuildSurfaceAsync(
                        childAnchors[i],
                        generation);
                    if (surface == null)
                        continue;

                    switch (surface.Kind)
                    {
                        case MetaSceneSurfaceKind.Wall:
                            walls.Add(surface);
                            break;
                        case MetaSceneSurfaceKind.Floor:
                            floors.Add(surface);
                            break;
                        case MetaSceneSurfaceKind.Ceiling:
                            ceilings.Add(surface);
                            break;
                        case MetaSceneSurfaceKind.Table:
                            tables.Add(surface);
                            break;
                    }
                }

                if (walls.Count == 0 && floors.Count == 0 &&
                    ceilings.Count == 0 && tables.Count == 0)
                    return null;

                return new MetaSceneRoomSnapshot(
                    roomAnchor.Uuid,
                    walls,
                    floors,
                    ceilings,
                    tables);
            }
            finally
            {
                DisposeFetchedAnchors(childAnchors, generation);
            }
        }

        private void DisposeFetchedAnchors(
            IReadOnlyList<OVRAnchor> anchors,
            int generation)
        {
            if (anchors == null ||
                !IsCurrent(generation) ||
                !OVRPlugin.initialized ||
                !OVRManager.OVRManagerinitialized)
                return;

            var disposedIds = new HashSet<Guid>();
            for (int i = 0; i < anchors.Count; i++)
            {
                OVRAnchor anchor = anchors[i];
                if (anchor == OVRAnchor.Null ||
                    !disposedIds.Add(anchor.Uuid))
                {
                    continue;
                }

                anchor.Dispose();
            }
        }

        async Task<MetaSceneSurfaceSnapshot> BuildSurfaceAsync(
            OVRAnchor anchor,
            int generation)
        {
            if (!anchor.TryGetComponent(out OVRLocatable locatable) ||
                !anchor.TryGetComponent(out OVRSemanticLabels labels))
            {
                return null;
            }

            // Scene-model bounds and labels are read-only components. Saved
            // scene anchors should already expose them as enabled; unlike
            // Locatable, Meta does not allow enabling them from the app.
            if (!labels.IsEnabled)
                return null;
            if (!locatable.IsEnabled && !await locatable.SetEnabledAsync(true))
                return null;
            if (!IsCurrent(generation) ||
                !locatable.TryGetSceneAnchorPose(out var trackingPose))
            {
                return null;
            }

            var classifications = new HashSet<OVRSemanticLabels.Classification>();
            labels.GetClassifications(classifications);
            AddLegacyTableClassification(labels, classifications);
            if (!TryResolveKind(
                    classifications,
                    out MetaSceneSurfaceKind kind,
                    out OVRSemanticLabels.Classification classification))
            {
                return null;
            }

            Vector3? position = trackingPose.ComputeWorldPosition(trackingSpace);
            Quaternion? rotation = trackingPose.ComputeWorldRotation(trackingSpace);
            if (!position.HasValue || !rotation.HasValue)
                return null;

            bool hasPlaneBounds =
                anchor.TryGetComponent(out OVRBounded2D bounded2D) &&
                bounded2D.IsEnabled;
            if (!hasPlaneBounds)
            {
                if (kind == MetaSceneSurfaceKind.Table &&
                    anchor.TryGetComponent(out OVRBounded3D bounded3D) &&
                    bounded3D.IsEnabled)
                {
                    try
                    {
                        return BuildVolumeTopSurface(
                            anchor.Uuid,
                            classification,
                            bounded3D.BoundingBox,
                            position.Value,
                            rotation.Value);
                    }
                    catch (InvalidOperationException exception)
                    {
                        Debug.LogWarning(
                            $"[MetaScene] Table volume bounds are unavailable: " +
                            exception.Message,
                            this);
                    }
                }

                return null;
            }

            Rect rect = bounded2D.BoundingBox;
            var worldBoundary = new List<Vector3>();
            var localBoundary = new List<Vector2>();
            CopyBoundary(bounded2D, rect, position.Value, rotation.Value,
                worldBoundary, localBoundary);

            Vector3 center = position.Value +
                rotation.Value * new Vector3(rect.center.x, rect.center.y, 0f);
            Vector3 normal = (rotation.Value * Vector3.forward).normalized;
            if ((kind == MetaSceneSurfaceKind.Floor ||
                 kind == MetaSceneSurfaceKind.Table) &&
                Vector3.Dot(normal, Vector3.up) < 0f)
            {
                normal = -normal;
            }
            else if (kind == MetaSceneSurfaceKind.Ceiling &&
                Vector3.Dot(normal, Vector3.down) < 0f)
            {
                normal = -normal;
            }

            Matrix4x4 localToWorld = Matrix4x4.TRS(
                position.Value,
                rotation.Value,
                Vector3.one);
            return new MetaSceneSurfaceSnapshot(
                anchor.Uuid,
                kind,
                classification,
                center,
                rotation.Value,
                normal,
                (rotation.Value * Vector3.right).normalized,
                (rotation.Value * Vector3.up).normalized,
                rect,
                worldBoundary,
                localBoundary,
                localToWorld.inverse);
        }

        static MetaSceneSurfaceSnapshot BuildVolumeTopSurface(
            Guid id,
            OVRSemanticLabels.Classification classification,
            Bounds volume,
            Vector3 anchorPosition,
            Quaternion anchorRotation)
        {
            Quaternion surfaceRotation =
                anchorRotation * Quaternion.Euler(-90f, 0f, 0f);
            Vector3 surfacePosition = anchorPosition +
                anchorRotation * new Vector3(0f, volume.max.y, 0f);
            Rect rect = Rect.MinMaxRect(
                volume.min.x,
                -volume.max.z,
                volume.max.x,
                -volume.min.z);

            var localBoundary = new List<Vector2>
            {
                rect.min,
                new Vector2(rect.xMin, rect.yMax),
                rect.max,
                new Vector2(rect.xMax, rect.yMin)
            };
            var worldBoundary = new List<Vector3>(localBoundary.Count);
            for (int i = 0; i < localBoundary.Count; i++)
            {
                Vector2 point = localBoundary[i];
                worldBoundary.Add(surfacePosition +
                    surfaceRotation * new Vector3(point.x, point.y, 0f));
            }

            Matrix4x4 localToWorld = Matrix4x4.TRS(
                surfacePosition,
                surfaceRotation,
                Vector3.one);
            return new MetaSceneSurfaceSnapshot(
                id,
                MetaSceneSurfaceKind.Table,
                classification,
                surfacePosition +
                    surfaceRotation *
                    new Vector3(rect.center.x, rect.center.y, 0f),
                surfaceRotation,
                (surfaceRotation * Vector3.forward).normalized,
                (surfaceRotation * Vector3.right).normalized,
                (surfaceRotation * Vector3.up).normalized,
                rect,
                worldBoundary,
                localBoundary,
                localToWorld.inverse);
        }

        static void AddLegacyTableClassification(
            OVRSemanticLabels labels,
            HashSet<OVRSemanticLabels.Classification> classifications)
        {
            if (classifications.Contains(
                    OVRSemanticLabels.Classification.Table))
            {
                return;
            }

            foreach (OVRSemanticLabels.Classification classification in
                classifications)
            {
                if (classification !=
                        OVRSemanticLabels.Classification.Other &&
                    classification !=
                        OVRSemanticLabels.Classification.Unknown)
                {
                    return;
                }
            }

            string legacyLabels;
            try
            {
#pragma warning disable CS0618
                legacyLabels = labels.Labels;
#pragma warning restore CS0618
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[MetaScene] Legacy semantic labels are unavailable: " +
                    exception.Message);
                return;
            }

            string[] values = legacyLabels.Split(',');
            for (int i = 0; i < values.Length; i++)
            {
                if (!string.Equals(
                        values[i],
                        "TABLE",
                        StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(
                        values[i],
                        "DESK",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                classifications.Add(
                    OVRSemanticLabels.Classification.Table);
                return;
            }
        }

        static void CopyBoundary(
            OVRBounded2D bounded2D,
            Rect rect,
            Vector3 position,
            Quaternion rotation,
            List<Vector3> worldBoundary,
            List<Vector2> localBoundary)
        {
            if (bounded2D.TryGetBoundaryPointsCount(out int count) && count >= 3)
            {
                using var points = new NativeArray<Vector2>(count, Allocator.Temp);
                if (bounded2D.TryGetBoundaryPoints(points))
                {
                    for (int i = 0; i < points.Length; i++)
                    {
                        Vector2 point = points[i];
                        localBoundary.Add(point);
                        worldBoundary.Add(position +
                            rotation * new Vector3(point.x, point.y, 0f));
                    }

                    return;
                }
            }

            localBoundary.Add(rect.min);
            localBoundary.Add(new Vector2(rect.xMin, rect.yMax));
            localBoundary.Add(rect.max);
            localBoundary.Add(new Vector2(rect.xMax, rect.yMin));
            for (int i = 0; i < localBoundary.Count; i++)
            {
                Vector2 point = localBoundary[i];
                worldBoundary.Add(position +
                    rotation * new Vector3(point.x, point.y, 0f));
            }
        }

        static bool TryResolveKind(
            HashSet<OVRSemanticLabels.Classification> classifications,
            out MetaSceneSurfaceKind kind,
            out OVRSemanticLabels.Classification classification)
        {
            if (TryFindClassification(
                    classifications,
                    out classification,
                    OVRSemanticLabels.Classification.InnerWallFace,
                    OVRSemanticLabels.Classification.InvisibleWallFace,
                    OVRSemanticLabels.Classification.WallFace))
            {
                kind = MetaSceneSurfaceKind.Wall;
                return true;
            }

            if (classifications.Contains(
                    OVRSemanticLabels.Classification.Floor))
            {
                kind = MetaSceneSurfaceKind.Floor;
                classification = OVRSemanticLabels.Classification.Floor;
                return true;
            }

            if (classifications.Contains(
                    OVRSemanticLabels.Classification.Ceiling))
            {
                kind = MetaSceneSurfaceKind.Ceiling;
                classification = OVRSemanticLabels.Classification.Ceiling;
                return true;
            }

            if (classifications.Contains(
                    OVRSemanticLabels.Classification.Table))
            {
                kind = MetaSceneSurfaceKind.Table;
                classification = OVRSemanticLabels.Classification.Table;
                return true;
            }

            kind = default;
            classification = default;
            return false;
        }

        static bool TryFindClassification(
            HashSet<OVRSemanticLabels.Classification> available,
            out OVRSemanticLabels.Classification result,
            params OVRSemanticLabels.Classification[] accepted)
        {
            for (int i = 0; i < accepted.Length; i++)
            {
                if (!available.Contains(accepted[i]))
                    continue;

                result = accepted[i];
                return true;
            }

            result = default;
            return false;
        }

        MetaSceneRoomSnapshot SelectCurrentRoom(
            IReadOnlyList<MetaSceneRoomSnapshot> rooms)
        {
            Vector3 viewerPosition = roomCamera != null
                ? roomCamera.transform.position
                : trackingSpace.position;
            MetaSceneRoomSnapshot selected = null;
            float bestArea = float.PositiveInfinity;
            for (int i = 0; i < rooms.Count; i++)
            {
                if (!rooms[i].Contains(
                        viewerPosition,
                        maximumCameraHeightAboveFloor,
                        out float area) ||
                    area >= bestArea)
                {
                    continue;
                }

                selected = rooms[i];
                bestArea = area;
            }

            if (selected != null)
                return selected;

            float closestFloor = float.PositiveInfinity;
            for (int i = 0; i < rooms.Count; i++)
            {
                float distance = rooms[i].DistanceToClosestFloor(viewerPosition);
                if (distance >= closestFloor)
                    continue;

                closestFloor = distance;
                selected = rooms[i];
            }

            return selected ?? rooms[0];
        }

        void PublishRoom(MetaSceneRoomSnapshot room)
        {
            if (currentRoom != null && room != null &&
                currentRoom.Id == room.Id &&
                currentRoom.Signature == room.Signature)
            {
                currentRoom = room;
                return;
            }

            currentRoom = room;
            RoomChanged?.Invoke();
        }

        void SetStatus(MetaSceneRoomStatus status)
        {
            if (Status == status)
                return;

            Status = status;
            StatusChanged?.Invoke(status);
        }

        static string ResolveSceneQueryFailureMessage() =>
            $"Meta Scene anchor query failed. {ResolveSceneAccessHint()}";

        static string ResolveMissingSceneMessage() =>
#if UNITY_EDITOR
            "No saved room was returned over Quest Link. Enable Spatial Data over Meta Quest Link in the Meta Quest Link app (Settings > Beta), and run Space Setup on the headset first.";
#else
            "No saved room scan was found. Run Meta Space Setup, then retry.";
#endif

        static string ResolveSceneAccessHint() =>
#if UNITY_EDITOR
            "Enable Spatial Data over Meta Quest Link (Settings > Beta) and make sure the headset has a saved Space Setup.";
#else
            "Check Spatial Data permission and the saved Space Setup.";
#endif

        bool IsCurrent(int generation) =>
            isActiveAndEnabled && generation == loadGeneration;

        void ResolveReferences()
        {
            XROrigin xrOrigin = FindAnyObjectByType<XROrigin>(
                FindObjectsInactive.Include);
            if (trackingSpace == null && xrOrigin != null)
            {
                trackingSpace = xrOrigin.CameraFloorOffsetObject != null
                    ? xrOrigin.CameraFloorOffsetObject.transform
                    : xrOrigin.transform;
            }

            if (roomCamera == null)
                roomCamera = xrOrigin?.Camera ?? Camera.main;

            if (legacyPlaneManager == null)
            {
                legacyPlaneManager = FindAnyObjectByType<ARPlaneManager>(
                    FindObjectsInactive.Include);
            }

            if (legacyRaycastManager == null)
            {
                legacyRaycastManager = FindAnyObjectByType<ARRaycastManager>(
                    FindObjectsInactive.Include);
            }
        }

        void SuspendLegacySceneManagers()
        {
            if (legacyManagersSuspended)
                return;

            ResolveReferences();
            restoreLegacyPlaneManager =
                legacyPlaneManager != null && legacyPlaneManager.enabled;
            restoreLegacyRaycastManager =
                legacyRaycastManager != null && legacyRaycastManager.enabled;

            if (legacyPlaneManager != null)
                legacyPlaneManager.enabled = false;
            if (legacyRaycastManager != null)
                legacyRaycastManager.enabled = false;

            legacyManagersSuspended = true;
        }

        void RestoreLegacySceneManagers()
        {
            if (!legacyManagersSuspended)
                return;

            // Do not force a manager back off here. The permission requester
            // may already have enabled it after the Meta query reached a
            // terminal status.
            if (legacyPlaneManager != null && restoreLegacyPlaneManager)
                legacyPlaneManager.enabled = true;
            if (legacyRaycastManager != null && restoreLegacyRaycastManager)
                legacyRaycastManager.enabled = true;

            legacyManagersSuspended = false;
        }

        void EnsureMetaRuntime()
        {
            if (FindAnyObjectByType<OVRManager>(FindObjectsInactive.Include) != null)
                return;

            XROrigin xrOrigin = FindAnyObjectByType<XROrigin>(
                FindObjectsInactive.Include);
            GameObject host = xrOrigin != null ? xrOrigin.gameObject : gameObject;
            host.AddComponent<OVRManager>();
            Debug.Log(
                "[MetaScene] Added OVRManager to the existing XR Origin. " +
                "No OVRCameraRig or additional camera was created.",
                host);
        }
    }
}
