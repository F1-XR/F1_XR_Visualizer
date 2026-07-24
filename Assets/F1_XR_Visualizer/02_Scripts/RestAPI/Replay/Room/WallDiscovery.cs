using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace F1XR.RestAPI.Replay.Room
{
    public sealed class WallCandidate
    {
        private readonly List<Vector3> boundary = new();
        private readonly ReadOnlyCollection<Vector3> readOnlyBoundary;
        private bool hasOrientation;
        private float inwardNormalSign = 1f;

        public WallCandidate(TrackableId trackableId)
        {
            TrackableId = trackableId;
            readOnlyBoundary = boundary.AsReadOnly();
        }

        public TrackableId TrackableId { get; }
        public ARPlane SourcePlane { get; private set; }
        public bool IsSemanticWall { get; private set; }
        public bool IsFallback { get; private set; }
        public Vector3 Center { get; private set; }
        public Quaternion Rotation { get; private set; }
        public Vector3 InwardNormal { get; private set; }
        public Vector3 HorizontalAxis { get; private set; }
        public Vector3 VerticalAxis { get; private set; }
        public float Width { get; private set; }
        public float Height { get; private set; }
        public IReadOnlyList<Vector3> Boundary => readOnlyBoundary;
        public bool IsValid { get; private set; }

        internal void UpdateFromPlane(
            ARPlane plane,
            bool isSemanticWall,
            Transform orientationCamera)
        {
            SourcePlane = plane;
            IsSemanticWall = isSemanticWall;
            IsFallback = !isSemanticWall;
            Center = plane.center;

            var planeNormal = plane.normal.normalized;
            if (!hasOrientation)
            {
                if (orientationCamera != null &&
                    Vector3.Dot(orientationCamera.position - Center, planeNormal) < 0f)
                {
                    inwardNormalSign = -1f;
                }

                hasOrientation = true;
            }

            InwardNormal = planeNormal * inwardNormalSign;
            VerticalAxis = Vector3.ProjectOnPlane(Vector3.up, InwardNormal).normalized;
            if (VerticalAxis.sqrMagnitude < 0.5f)
                VerticalAxis = plane.transform.forward.normalized;
            if (Vector3.Dot(VerticalAxis, Vector3.up) < 0f)
                VerticalAxis = -VerticalAxis;

            HorizontalAxis = Vector3.Cross(VerticalAxis, InwardNormal).normalized;
            Rotation = Quaternion.LookRotation(InwardNormal, VerticalAxis);

            CopyBoundary(plane);
            MeasureBoundary();
            IsValid = true;
        }

        internal void Invalidate()
        {
            IsValid = false;
        }

        private void CopyBoundary(ARPlane plane)
        {
            boundary.Clear();
            var planeBoundary = plane.boundary;
            for (var i = 0; i < planeBoundary.Length; i++)
            {
                var point = planeBoundary[i];
                boundary.Add(
                    plane.transform.TransformPoint(new Vector3(point.x, 0f, point.y)));
            }

            if (boundary.Count >= 3)
                return;

            var halfSize = plane.size * 0.5f;
            var right = plane.transform.right * halfSize.x;
            var forward = plane.transform.forward * halfSize.y;
            boundary.Clear();
            boundary.Add(Center - right - forward);
            boundary.Add(Center - right + forward);
            boundary.Add(Center + right + forward);
            boundary.Add(Center + right - forward);
        }

        private void MeasureBoundary()
        {
            var minHorizontal = float.PositiveInfinity;
            var maxHorizontal = float.NegativeInfinity;
            var minVertical = float.PositiveInfinity;
            var maxVertical = float.NegativeInfinity;

            foreach (var point in boundary)
            {
                var offset = point - Center;
                var horizontal = Vector3.Dot(offset, HorizontalAxis);
                var vertical = Vector3.Dot(offset, VerticalAxis);
                minHorizontal = Mathf.Min(minHorizontal, horizontal);
                maxHorizontal = Mathf.Max(maxHorizontal, horizontal);
                minVertical = Mathf.Min(minVertical, vertical);
                maxVertical = Mathf.Max(maxVertical, vertical);
            }

            Width = maxHorizontal - minHorizontal;
            Height = maxVertical - minVertical;
        }
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(ARPlaneManager))]
    public sealed class WallDiscovery : MonoBehaviour
    {
        private static readonly PlaneClassifications WallClassifications =
            PlaneClassifications.WallFace |
            PlaneClassifications.InvisibleWallFace |
            PlaneClassifications.InnerWallFace;

        private static readonly PlaneClassifications ExcludedClassifications =
            PlaneClassifications.Floor |
            PlaneClassifications.Ceiling |
            PlaneClassifications.Table;

        [Header("Sources")]
        [SerializeField] private ARPlaneManager planeManager;
        [SerializeField] private Transform orientationCamera;

        [Header("Wall Qualification")]
        [SerializeField] private bool allowVerticalFallback = true;
        [SerializeField, Min(0.1f)] private float minimumWidth = 0.75f;
        [SerializeField, Min(0.1f)] private float minimumHeight = 1.2f;

        [Header("Manual Selection")]
        [SerializeField] private int entryCandidateIndex = -1;
        [SerializeField] private int exitCandidateIndex = -1;

        [Header("Development Debug")]
        [SerializeField] private bool showDebug = true;
        [SerializeField, Min(0.001f)] private float debugLineWidth = 0.015f;
        [SerializeField, Min(0.05f)] private float normalLength = 0.35f;

        private readonly List<WallCandidate> candidates = new();
        private readonly Dictionary<TrackableId, WallCandidate> candidatesById = new();
        private readonly Dictionary<TrackableId, WallDebugView> debugViews = new();
        private ReadOnlyCollection<WallCandidate> readOnlyCandidates;
        private TrackableId? entryWallId;
        private TrackableId? exitWallId;
        private Material debugMaterial;
        private bool isSubscribed;
        private bool managerWasActive;
        private bool debugWasVisible;
        private int observedEntryIndex;
        private int observedExitIndex;
        private Vector3 lastRootPosition;
        private Quaternion lastRootRotation;
        private Vector3 lastRootScale;

        public IReadOnlyList<WallCandidate> Candidates =>
            readOnlyCandidates ??= candidates.AsReadOnly();

        public TrackableId? EntryWallId => entryWallId;
        public TrackableId? ExitWallId => exitWallId;
        public bool BothSelectionsValid =>
            TryGetEntryWall(out _) &&
            TryGetExitWall(out _) &&
            entryWallId != exitWallId;

        private void Reset()
        {
            ResolveReferences();
        }

        private void Awake()
        {
            ResolveReferences();
            readOnlyCandidates = candidates.AsReadOnly();
        }

        private void OnEnable()
        {
            if (planeManager == null)
            {
                Debug.LogError("[WallDiscovery] ARPlaneManager is required.", this);
                enabled = false;
                return;
            }

            RequestWallAndTablePlanes();
            Subscribe();
            managerWasActive = planeManager.isActiveAndEnabled;
            debugWasVisible = showDebug;
            observedEntryIndex = entryCandidateIndex;
            observedExitIndex = exitCandidateIndex;
            RememberRootPose();

            if (managerWasActive)
                SyncExistingPlanes();
        }

        private void OnDisable()
        {
            Unsubscribe();
            ClearCandidates("Wall discovery disabled.");
            DestroyDebugMaterial();
        }

        private void Update()
        {
            var managerIsActive =
                planeManager != null &&
                planeManager.isActiveAndEnabled;

            if (managerWasActive && !managerIsActive)
                ClearCandidates("ARPlaneManager was disabled.");
            else if (!managerWasActive && managerIsActive)
            {
                RequestWallAndTablePlanes();
                SyncExistingPlanes();
            }

            managerWasActive = managerIsActive;

            if (managerIsActive && HasRootPoseChanged())
            {
                RefreshCandidatesAfterRootMove();
                RememberRootPose();
            }

            if (debugWasVisible != showDebug)
            {
                debugWasVisible = showDebug;
                RefreshAllDebugViews();
            }

            if (observedEntryIndex != entryCandidateIndex)
            {
                observedEntryIndex = entryCandidateIndex;
                SelectEntryByIndex(entryCandidateIndex);
            }

            if (observedExitIndex != exitCandidateIndex)
            {
                observedExitIndex = exitCandidateIndex;
                SelectExitByIndex(exitCandidateIndex);
            }
        }

        private void LateUpdate()
        {
            if (!showDebug || orientationCamera == null)
                return;

            foreach (var view in debugViews.Values)
                view.FaceLabelTowards(orientationCamera);
        }

        private void OnValidate()
        {
            minimumWidth = Mathf.Max(0.1f, minimumWidth);
            minimumHeight = Mathf.Max(0.1f, minimumHeight);
            debugLineWidth = Mathf.Max(0.001f, debugLineWidth);
            normalLength = Mathf.Max(0.05f, normalLength);
        }

        public bool TryGetEntryWall(out WallCandidate wall)
        {
            return TryGetSelectedWall(entryWallId, out wall);
        }

        public bool TryGetExitWall(out WallCandidate wall)
        {
            return TryGetSelectedWall(exitWallId, out wall);
        }

        public bool SelectEntryByIndex(int index)
        {
            if (!TryGetCandidate(index, "Entry", out var wall))
                return false;

            entryWallId = wall.TrackableId;
            SetEntryIndex(index);
            WarnIfSameWallSelected();
            RefreshAllDebugViews();
            return true;
        }

        public bool SelectExitByIndex(int index)
        {
            if (!TryGetCandidate(index, "Exit", out var wall))
                return false;

            exitWallId = wall.TrackableId;
            SetExitIndex(index);
            WarnIfSameWallSelected();
            RefreshAllDebugViews();
            return true;
        }

        [ContextMenu("Apply Candidate Indices")]
        public void ApplyCandidateIndices()
        {
            if (entryCandidateIndex < 0)
                ClearEntrySelection();
            else
                SelectEntryByIndex(entryCandidateIndex);

            if (exitCandidateIndex < 0)
                ClearExitSelection();
            else
                SelectExitByIndex(exitCandidateIndex);
        }

        [ContextMenu("Next Entry Wall")]
        public void NextEntryWall()
        {
            SelectRelativeWall(true, 1);
        }

        [ContextMenu("Previous Entry Wall")]
        public void PreviousEntryWall()
        {
            SelectRelativeWall(true, -1);
        }

        [ContextMenu("Next Exit Wall")]
        public void NextExitWall()
        {
            SelectRelativeWall(false, 1);
        }

        [ContextMenu("Previous Exit Wall")]
        public void PreviousExitWall()
        {
            SelectRelativeWall(false, -1);
        }

        [ContextMenu("Clear Entry Wall")]
        public void ClearEntrySelection()
        {
            entryWallId = null;
            SetEntryIndex(-1);
            RefreshAllDebugViews();
        }

        [ContextMenu("Clear Exit Wall")]
        public void ClearExitSelection()
        {
            exitWallId = null;
            SetExitIndex(-1);
            RefreshAllDebugViews();
        }

        public void SetDebugVisible(bool visible)
        {
            showDebug = visible;
            debugWasVisible = visible;
            RefreshAllDebugViews();
        }

        private void ResolveReferences()
        {
            if (planeManager == null)
                planeManager = GetComponent<ARPlaneManager>();

            if (orientationCamera == null && Camera.main != null)
                orientationCamera = Camera.main.transform;
        }

        private void RequestWallAndTablePlanes()
        {
            planeManager.requestedDetectionMode =
                PlaneDetectionMode.Horizontal |
                PlaneDetectionMode.Vertical;
        }

        private void Subscribe()
        {
            if (isSubscribed)
                return;

            planeManager.trackablesChanged.AddListener(OnTrackablesChanged);
            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed || planeManager == null)
                return;

            planeManager.trackablesChanged.RemoveListener(OnTrackablesChanged);
            isSubscribed = false;
        }

        private void OnTrackablesChanged(
            ARTrackablesChangedEventArgs<ARPlane> changes)
        {
            foreach (var plane in changes.added)
                AddOrUpdateCandidate(plane);

            foreach (var plane in changes.updated)
                AddOrUpdateCandidate(plane);

            foreach (var removed in changes.removed)
                RemoveCandidate(removed.Key, "Source AR plane was removed.");
        }

        private void SyncExistingPlanes()
        {
            foreach (var plane in planeManager.trackables)
                AddOrUpdateCandidate(plane);
        }

        private void RefreshCandidatesAfterRootMove()
        {
            for (var i = candidates.Count - 1; i >= 0; i--)
            {
                var plane = candidates[i].SourcePlane;
                if (plane == null)
                    RemoveCandidate(
                        candidates[i].TrackableId,
                        "Source AR plane is no longer available.");
                else
                    AddOrUpdateCandidate(plane);
            }
        }

        private bool HasRootPoseChanged()
        {
            return transform.position != lastRootPosition ||
                transform.rotation != lastRootRotation ||
                transform.lossyScale != lastRootScale;
        }

        private void RememberRootPose()
        {
            lastRootPosition = transform.position;
            lastRootRotation = transform.rotation;
            lastRootScale = transform.lossyScale;
        }

        private void AddOrUpdateCandidate(ARPlane plane)
        {
            if (!TryQualify(plane, out var isSemanticWall))
            {
                RemoveCandidate(plane.trackableId, "Plane no longer qualifies as a wall.");
                return;
            }

            if (!candidatesById.TryGetValue(plane.trackableId, out var candidate))
            {
                candidate = new WallCandidate(plane.trackableId);
                candidates.Add(candidate);
                candidatesById.Add(plane.trackableId, candidate);
            }

            candidate.UpdateFromPlane(plane, isSemanticWall, orientationCamera);
            if (candidate.Width < minimumWidth || candidate.Height < minimumHeight)
            {
                RemoveCandidate(
                    plane.trackableId,
                    $"Wall is below the minimum {minimumWidth:0.##} m x {minimumHeight:0.##} m.");
                return;
            }

            RefreshSelectionIndices();
            RefreshDebugView(candidate);
        }

        private bool TryQualify(ARPlane plane, out bool isSemanticWall)
        {
            isSemanticWall = false;
            if (plane == null ||
                plane.subsumedBy != null ||
                plane.alignment != PlaneAlignment.Vertical)
            {
                return false;
            }

            var classifications = plane.classifications;
            if (HasAny(classifications, ExcludedClassifications))
                return false;

            isSemanticWall = HasAny(classifications, WallClassifications);
            return isSemanticWall || allowVerticalFallback;
        }

        private void RemoveCandidate(TrackableId id, string reason)
        {
            if (!candidatesById.Remove(id, out var candidate))
                return;

            candidate.Invalidate();
            candidates.Remove(candidate);
            DestroyDebugView(id);

            if (entryWallId == id)
            {
                entryWallId = null;
                SetEntryIndex(-1);
                Debug.LogWarning($"[WallDiscovery] Entry Wall cleared. {reason}", this);
            }

            if (exitWallId == id)
            {
                exitWallId = null;
                SetExitIndex(-1);
                Debug.LogWarning($"[WallDiscovery] Exit Wall cleared. {reason}", this);
            }

            RefreshSelectionIndices();
            RefreshAllDebugViews();
        }

        private void ClearCandidates(string reason)
        {
            foreach (var candidate in candidates)
                candidate.Invalidate();

            candidates.Clear();
            candidatesById.Clear();
            DestroyAllDebugViews();

            if (entryWallId.HasValue)
                Debug.LogWarning($"[WallDiscovery] Entry Wall cleared. {reason}", this);
            if (exitWallId.HasValue)
                Debug.LogWarning($"[WallDiscovery] Exit Wall cleared. {reason}", this);

            entryWallId = null;
            exitWallId = null;
            SetEntryIndex(-1);
            SetExitIndex(-1);
        }

        private bool TryGetSelectedWall(
            TrackableId? selectedId,
            out WallCandidate wall)
        {
            wall = null;
            return selectedId.HasValue &&
                candidatesById.TryGetValue(selectedId.Value, out wall) &&
                wall.IsValid;
        }

        private bool TryGetCandidate(
            int index,
            string selectionName,
            out WallCandidate wall)
        {
            wall = null;
            if (index < 0)
            {
                if (selectionName == "Entry")
                    ClearEntrySelection();
                else
                    ClearExitSelection();
                return false;
            }

            if (index >= candidates.Count)
            {
                Debug.LogWarning(
                    $"[WallDiscovery] {selectionName} Wall index {index} is invalid. " +
                    $"Candidate count: {candidates.Count}.",
                    this);
                return false;
            }

            wall = candidates[index];
            return wall.IsValid;
        }

        private void SelectRelativeWall(bool entry, int direction)
        {
            if (candidates.Count == 0)
            {
                Debug.LogWarning("[WallDiscovery] No wall candidates are available.", this);
                return;
            }

            var selectedId = entry ? entryWallId : exitWallId;
            var currentIndex = FindCandidateIndex(selectedId);
            var nextIndex = currentIndex < 0
                ? 0
                : (currentIndex + direction + candidates.Count) % candidates.Count;

            if (entry)
                SelectEntryByIndex(nextIndex);
            else
                SelectExitByIndex(nextIndex);
        }

        private int FindCandidateIndex(TrackableId? id)
        {
            if (!id.HasValue)
                return -1;

            for (var i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].TrackableId == id.Value)
                    return i;
            }

            return -1;
        }

        private void RefreshSelectionIndices()
        {
            SetEntryIndex(FindCandidateIndex(entryWallId));
            SetExitIndex(FindCandidateIndex(exitWallId));
        }

        private void SetEntryIndex(int index)
        {
            entryCandidateIndex = index;
            observedEntryIndex = index;
        }

        private void SetExitIndex(int index)
        {
            exitCandidateIndex = index;
            observedExitIndex = index;
        }

        private void WarnIfSameWallSelected()
        {
            if (entryWallId.HasValue &&
                exitWallId.HasValue &&
                entryWallId.Value == exitWallId.Value)
            {
                Debug.LogWarning(
                    "[WallDiscovery] Entry Wall and Exit Wall reference the same AR plane.",
                    this);
            }
        }

        private static bool HasAny(
            PlaneClassifications value,
            PlaneClassifications mask)
        {
            return (value & mask) != 0;
        }

        private void RefreshAllDebugViews()
        {
            if (!showDebug)
            {
                DestroyAllDebugViews();
                return;
            }

            foreach (var candidate in candidates)
                RefreshDebugView(candidate);
        }

        private void RefreshDebugView(WallCandidate candidate)
        {
            if (!showDebug || !candidate.IsValid)
                return;

            if (!TryGetDebugMaterial(out var material))
                return;

            if (!debugViews.TryGetValue(candidate.TrackableId, out var view))
            {
                view = new WallDebugView(transform, candidate.TrackableId, material);
                debugViews.Add(candidate.TrackableId, view);
            }

            var index = candidates.IndexOf(candidate);
            var isEntry = entryWallId == candidate.TrackableId;
            var isExit = exitWallId == candidate.TrackableId;
            view.Refresh(
                candidate,
                index,
                isEntry,
                isExit,
                debugLineWidth,
                normalLength);
        }

        private bool TryGetDebugMaterial(out Material material)
        {
            if (debugMaterial != null)
            {
                material = debugMaterial;
                return true;
            }

            var shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                Debug.LogWarning(
                    "[WallDiscovery] No debug-compatible shader was found.",
                    this);
                material = null;
                return false;
            }

            debugMaterial = new Material(shader)
            {
                name = "Wall Discovery Debug Material",
                hideFlags = HideFlags.HideAndDontSave
            };
            material = debugMaterial;
            return true;
        }

        private void DestroyDebugView(TrackableId id)
        {
            if (!debugViews.Remove(id, out var view))
                return;

            view.Dispose();
        }

        private void DestroyAllDebugViews()
        {
            foreach (var view in debugViews.Values)
                view.Dispose();

            debugViews.Clear();
        }

        private void DestroyDebugMaterial()
        {
            if (debugMaterial == null)
                return;

            Destroy(debugMaterial);
            debugMaterial = null;
        }

        private sealed class WallDebugView : IDisposable
        {
            private static readonly Color SemanticColor =
                new(0.1f, 0.8f, 1f, 0.9f);
            private static readonly Color FallbackColor =
                new(1f, 0.55f, 0.1f, 0.9f);
            private static readonly Color EntryColor =
                new(0.2f, 1f, 0.3f, 0.95f);
            private static readonly Color ExitColor =
                new(1f, 0.2f, 0.8f, 0.95f);
            private static readonly Color SameWallColor =
                new(1f, 0.9f, 0.1f, 0.95f);

            private readonly GameObject root;
            private readonly LineRenderer boundaryLine;
            private readonly LineRenderer normalLine;
            private readonly Transform centerMarker;
            private readonly MeshRenderer centerRenderer;
            private readonly TextMesh label;
            private readonly MaterialPropertyBlock colorProperties = new();
            private Vector3[] boundaryPositions = Array.Empty<Vector3>();
            private readonly Vector3[] normalPositions = new Vector3[5];

            public WallDebugView(
                Transform parent,
                TrackableId id,
                Material material)
            {
                root = new GameObject($"WallDebug_{id}");
                root.layer = 2;
                root.transform.SetParent(parent, false);

                boundaryLine = CreateLine("Boundary", material);
                normalLine = CreateLine("Inward Normal", material);

                var center = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                center.name = "Center";
                center.layer = 2;
                center.transform.SetParent(root.transform, false);
                var collider = center.GetComponent<Collider>();
                if (collider != null)
                {
                    collider.enabled = false;
                    Destroy(collider);
                }
                centerMarker = center.transform;
                centerRenderer = center.GetComponent<MeshRenderer>();
                centerRenderer.sharedMaterial = material;
                centerRenderer.shadowCastingMode = ShadowCastingMode.Off;
                centerRenderer.receiveShadows = false;

                var labelObject = new GameObject("Label");
                labelObject.layer = 2;
                labelObject.transform.SetParent(root.transform, false);
                label = labelObject.AddComponent<TextMesh>();
                label.anchor = TextAnchor.MiddleCenter;
                label.alignment = TextAlignment.Center;
                label.characterSize = 0.025f;
                label.fontSize = 36;
            }

            public void Refresh(
                WallCandidate candidate,
                int index,
                bool isEntry,
                bool isExit,
                float lineWidth,
                float inwardNormalLength)
            {
                var color = ResolveColor(candidate, isEntry, isExit);
                ApplyLine(boundaryLine, color, lineWidth);
                ApplyLine(normalLine, color, lineWidth * 0.8f);

                var boundary = candidate.Boundary;
                if (boundaryPositions.Length != boundary.Count)
                    boundaryPositions = new Vector3[boundary.Count];
                for (var i = 0; i < boundary.Count; i++)
                    boundaryPositions[i] =
                        root.transform.InverseTransformPoint(boundary[i]);

                boundaryLine.positionCount = boundaryPositions.Length;
                boundaryLine.SetPositions(boundaryPositions);
                boundaryLine.loop = boundaryPositions.Length >= 3;

                var tip = candidate.Center +
                    candidate.InwardNormal * inwardNormalLength;
                var arrowBack = tip -
                    candidate.InwardNormal * inwardNormalLength * 0.25f;
                var arrowHalfWidth = inwardNormalLength * 0.12f;
                normalPositions[0] =
                    root.transform.InverseTransformPoint(candidate.Center);
                normalPositions[1] =
                    root.transform.InverseTransformPoint(tip);
                normalPositions[2] = root.transform.InverseTransformPoint(
                    arrowBack + candidate.HorizontalAxis * arrowHalfWidth);
                normalPositions[3] =
                    root.transform.InverseTransformPoint(tip);
                normalPositions[4] = root.transform.InverseTransformPoint(
                    arrowBack - candidate.HorizontalAxis * arrowHalfWidth);
                normalLine.positionCount = normalPositions.Length;
                normalLine.SetPositions(normalPositions);

                centerMarker.localPosition =
                    root.transform.InverseTransformPoint(candidate.Center);
                centerMarker.localScale = Vector3.one * 0.05f;
                SetRendererColor(centerRenderer, color);

                var role = isEntry && isExit
                    ? "ENTRY + EXIT"
                    : isEntry
                        ? "ENTRY"
                        : isExit
                            ? "EXIT"
                            : "WALL";
                var source = candidate.IsFallback ? "FALLBACK" : "SEMANTIC";
                label.text = $"{index}: {role}\n{source}";
                label.color = color;
                label.transform.localPosition =
                    root.transform.InverseTransformPoint(
                        candidate.Center +
                        candidate.VerticalAxis *
                        (candidate.Height * 0.5f + 0.12f) +
                        candidate.InwardNormal * 0.01f);
            }

            public void FaceLabelTowards(Transform cameraTransform)
            {
                var direction = label.transform.position - cameraTransform.position;
                if (direction.sqrMagnitude < 0.001f)
                    return;

                label.transform.rotation =
                    Quaternion.LookRotation(direction.normalized, Vector3.up);
            }

            public void Dispose()
            {
                if (root != null)
                    Destroy(root);
            }

            private LineRenderer CreateLine(string name, Material material)
            {
                var lineObject = new GameObject(name);
                lineObject.layer = 2;
                lineObject.transform.SetParent(root.transform, false);
                var line = lineObject.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.sharedMaterial = material;
                line.textureMode = LineTextureMode.Stretch;
                line.alignment = LineAlignment.View;
                line.numCornerVertices = 2;
                line.numCapVertices = 2;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                return line;
            }

            private static Color ResolveColor(
                WallCandidate candidate,
                bool isEntry,
                bool isExit)
            {
                if (isEntry && isExit)
                    return SameWallColor;
                if (isEntry)
                    return EntryColor;
                if (isExit)
                    return ExitColor;
                return candidate.IsFallback ? FallbackColor : SemanticColor;
            }

            private static void ApplyLine(
                LineRenderer line,
                Color color,
                float width)
            {
                line.startColor = color;
                line.endColor = color;
                line.startWidth = width;
                line.endWidth = width;
            }

            private void SetRendererColor(Renderer renderer, Color color)
            {
                colorProperties.SetColor("_Color", color);
                colorProperties.SetColor("_BaseColor", color);
                renderer.SetPropertyBlock(colorProperties);
            }
        }
    }
}
