using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace F1XR.RestAPI.Replay.Room
{
    public enum WallSelectionState
    {
        None,
        Selected,
        Reacquiring
    }

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
        public PlaneClassifications Classifications { get; private set; }
        public bool IsSemanticWall { get; private set; }
        public bool IsFallback { get; private set; }
        public Vector3 Center { get; private set; }
        public Quaternion Rotation { get; private set; }
        public Vector3 InwardNormal { get; private set; }
        public Vector3 HorizontalAxis { get; private set; }
        public Vector3 VerticalAxis { get; private set; }
        public float Width { get; private set; }
        public float Height { get; private set; }
        public float MinHorizontal { get; private set; }
        public float MaxHorizontal { get; private set; }
        public float MinVertical { get; private set; }
        public float MaxVertical { get; private set; }
        public IReadOnlyList<Vector3> Boundary => readOnlyBoundary;
        public bool IsValid { get; private set; }

        internal void UpdateFromPlane(
            ARPlane plane,
            bool isSemanticWall,
            Transform orientationCamera)
        {
            SourcePlane = plane;
            Classifications = plane.classifications;
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

        internal void AlignInwardNormal(Vector3 referenceNormal)
        {
            if (Vector3.Dot(InwardNormal, referenceNormal) >= 0f)
                return;

            inwardNormalSign = -inwardNormalSign;
            InwardNormal = -InwardNormal;
            HorizontalAxis = Vector3.Cross(
                VerticalAxis,
                InwardNormal).normalized;
            Rotation = Quaternion.LookRotation(
                InwardNormal,
                VerticalAxis);
            MeasureBoundary();
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
            MinHorizontal = minHorizontal;
            MaxHorizontal = maxHorizontal;
            MinVertical = minVertical;
            MaxVertical = maxVertical;
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

        [Header("Selection Reacquisition")]
        [SerializeField] private bool reacquisitionEnabled = true;
        [SerializeField, Min(0.1f)] private float reacquisitionGraceDuration = 2f;
        [SerializeField, Min(0.01f)] private float planeDistanceTolerance = 0.15f;
        [SerializeField, Range(1f, 45f)] private float normalAngleTolerance = 12f;
        [SerializeField, Range(0.1f, 1f)] private float minimumOverlap = 0.55f;
        [SerializeField, Range(0f, 0.9f)] private float sizeDifferenceTolerance = 0.65f;
        [SerializeField, Range(0f, 1f)] private float minimumMatchScore = 0.65f;
        [SerializeField, Range(0.01f, 1f)] private float ambiguityMargin = 0.12f;

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
        private int selectionRevision;
        private readonly SelectionData entrySelection = new();
        private readonly SelectionData exitSelection = new();

        private const float SelectedPositionTolerance = 0.01f;
        private const float SelectedAngleTolerance = 1f;
        private const float SelectedSizeTolerance = 0.02f;

        public IReadOnlyList<WallCandidate> Candidates =>
            readOnlyCandidates ??= candidates.AsReadOnly();

        public TrackableId? EntryWallId => entryWallId;
        public TrackableId? ExitWallId => exitWallId;
        public WallSelectionState EntrySelectionState =>
            entrySelection.State;
        public WallSelectionState ExitSelectionState =>
            exitSelection.State;
        public bool IsEntryReacquiring =>
            entrySelection.State == WallSelectionState.Reacquiring;
        public bool IsExitReacquiring =>
            exitSelection.State == WallSelectionState.Reacquiring;
        public TrackableId? EntrySelectedTrackableId =>
            GetDiagnosticTrackableId(entryWallId, entrySelection);
        public TrackableId? ExitSelectedTrackableId =>
            GetDiagnosticTrackableId(exitWallId, exitSelection);
        public int SelectionRevision => selectionRevision;
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

            UpdateReacquisitionTimeouts();
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
            reacquisitionGraceDuration =
                Mathf.Max(0.1f, reacquisitionGraceDuration);
            planeDistanceTolerance =
                Mathf.Max(0.01f, planeDistanceTolerance);
            normalAngleTolerance =
                Mathf.Clamp(normalAngleTolerance, 1f, 45f);
            minimumOverlap =
                Mathf.Clamp(minimumOverlap, 0.1f, 1f);
            sizeDifferenceTolerance =
                Mathf.Clamp(sizeDifferenceTolerance, 0f, 0.9f);
            minimumMatchScore =
                Mathf.Clamp01(minimumMatchScore);
            ambiguityMargin =
                Mathf.Clamp(ambiguityMargin, 0.01f, 1f);
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

        public bool TryGetEntryWallFrame(out WallCandidate wall)
        {
            return TryGetEntryWall(out wall);
        }

        public bool TryGetExitWallFrame(out WallCandidate wall)
        {
            return TryGetExitWall(out wall);
        }

        public bool SelectEntryByIndex(int index)
        {
            if (!TryGetCandidate(index, "Entry", out var wall))
                return false;

            CancelReacquisition(entrySelection);
            entryWallId = wall.TrackableId;
            entrySelection.State = WallSelectionState.Selected;
            entrySelection.Snapshot = CaptureSnapshot(wall);
            SetEntryIndex(index);
            IncrementSelectionRevision();
            WarnIfSameWallSelected();
            RefreshAllDebugViews();
            return true;
        }

        public bool SelectExitByIndex(int index)
        {
            if (!TryGetCandidate(index, "Exit", out var wall))
                return false;

            CancelReacquisition(exitSelection);
            exitWallId = wall.TrackableId;
            exitSelection.State = WallSelectionState.Selected;
            exitSelection.Snapshot = CaptureSnapshot(wall);
            SetExitIndex(index);
            IncrementSelectionRevision();
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
            var hadSelection =
                entryWallId.HasValue ||
                entrySelection.State != WallSelectionState.None;
            entryWallId = null;
            ResetSelection(entrySelection);
            SetEntryIndex(-1);
            if (hadSelection)
                IncrementSelectionRevision();
            RefreshAllDebugViews();
        }

        [ContextMenu("Clear Exit Wall")]
        public void ClearExitSelection()
        {
            var hadSelection =
                exitWallId.HasValue ||
                exitSelection.State != WallSelectionState.None;
            exitWallId = null;
            ResetSelection(exitSelection);
            SetExitIndex(-1);
            if (hadSelection)
                IncrementSelectionRevision();
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
            foreach (var removed in changes.removed)
                RemoveCandidate(removed.Key, "Source AR plane was removed.");

            foreach (var plane in changes.added)
                AddOrUpdateCandidate(plane);

            foreach (var plane in changes.updated)
                AddOrUpdateCandidate(plane);

            AttemptPendingReacquisitions();
        }

        private void SyncExistingPlanes()
        {
            foreach (var plane in planeManager.trackables)
                AddOrUpdateCandidate(plane);

            AttemptPendingReacquisitions();
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

            AttemptPendingReacquisitions();
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

            RefreshSelectedFrame(candidate);

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

            var removedSnapshot = CaptureSnapshot(candidate);
            candidate.Invalidate();
            candidates.Remove(candidate);
            DestroyDebugView(id);

            if (entryWallId == id)
            {
                entryWallId = null;
                SetEntryIndex(-1);
                if (reacquisitionEnabled)
                {
                    BeginReacquisition(
                        entrySelection,
                        removedSnapshot,
                        "Entry");
                }
                else
                {
                    ResetSelection(entrySelection);
                    IncrementSelectionRevision();
                    Debug.LogWarning(
                        $"[WallDiscovery] Entry Wall cleared. {reason}",
                        this);
                }
            }

            if (exitWallId == id)
            {
                exitWallId = null;
                SetExitIndex(-1);
                if (reacquisitionEnabled)
                {
                    BeginReacquisition(
                        exitSelection,
                        removedSnapshot,
                        "Exit");
                }
                else
                {
                    ResetSelection(exitSelection);
                    IncrementSelectionRevision();
                    Debug.LogWarning(
                        $"[WallDiscovery] Exit Wall cleared. {reason}",
                        this);
                }
            }

            RefreshSelectionIndices();
            RefreshAllDebugViews();
        }

        private void ClearCandidates(string reason)
        {
            var hadSelection =
                entryWallId.HasValue ||
                exitWallId.HasValue ||
                entrySelection.State != WallSelectionState.None ||
                exitSelection.State != WallSelectionState.None;
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
            ResetSelection(entrySelection);
            ResetSelection(exitSelection);
            SetEntryIndex(-1);
            SetExitIndex(-1);
            if (hadSelection)
                IncrementSelectionRevision();
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

        private void IncrementSelectionRevision()
        {
            unchecked
            {
                selectionRevision++;
            }
        }

        private void BeginReacquisition(
            SelectionData selection,
            WallSnapshot snapshot,
            string selectionName)
        {
            snapshot.RemovedAt = Time.unscaledTime;
            selection.State = WallSelectionState.Reacquiring;
            selection.Snapshot = snapshot;
            selection.ReacquireUntil =
                snapshot.RemovedAt + reacquisitionGraceDuration;
            selection.AmbiguityLogged = false;
            IncrementSelectionRevision();
            Debug.LogWarning(
                $"[WallDiscovery] {selectionName} Wall source removed; " +
                $"reacquiring for {reacquisitionGraceDuration:0.0} s.",
                this);
        }

        private void UpdateReacquisitionTimeouts()
        {
            var refreshDebug = false;
            refreshDebug |= TryTimeoutReacquisition(
                entrySelection,
                "Entry");
            refreshDebug |= TryTimeoutReacquisition(
                exitSelection,
                "Exit");

            if (refreshDebug)
                RefreshAllDebugViews();
        }

        private bool TryTimeoutReacquisition(
            SelectionData selection,
            string selectionName)
        {
            if (selection.State != WallSelectionState.Reacquiring ||
                Time.unscaledTime < selection.ReacquireUntil)
            {
                return false;
            }

            ResetSelection(selection);
            IncrementSelectionRevision();
            Debug.LogWarning(
                $"[WallDiscovery] {selectionName} Wall reacquisition " +
                "timed out; selection cleared.",
                this);
            return true;
        }

        private void AttemptPendingReacquisitions()
        {
            if (!IsEntryReacquiring && !IsExitReacquiring)
                return;

            TryRecoverExactId(entrySelection, true, "Entry");
            TryRecoverExactId(exitSelection, false, "Exit");

            var entryMatch = IsEntryReacquiring
                ? FindBestPhysicalMatch(entrySelection, true)
                : default;
            var exitMatch = IsExitReacquiring
                ? FindBestPhysicalMatch(exitSelection, false)
                : default;

            if (entryMatch.IsConfident &&
                exitMatch.IsConfident &&
                entryMatch.Candidate.TrackableId ==
                exitMatch.Candidate.TrackableId)
            {
                LogAmbiguousOnce(entrySelection, "Entry");
                LogAmbiguousOnce(exitSelection, "Exit");
                return;
            }

            ApplyPhysicalMatch(
                entrySelection,
                entryMatch,
                true,
                "Entry");
            ApplyPhysicalMatch(
                exitSelection,
                exitMatch,
                false,
                "Exit");
        }

        private bool TryRecoverExactId(
            SelectionData selection,
            bool entry,
            string selectionName)
        {
            if (selection.State != WallSelectionState.Reacquiring ||
                !selection.Snapshot.IsValid ||
                !candidatesById.TryGetValue(
                    selection.Snapshot.TrackableId,
                    out var candidate) ||
                !candidate.IsValid ||
                IsReservedByOtherSelection(candidate.TrackableId, entry))
            {
                return false;
            }

            RecoverSelection(
                selection,
                candidate,
                entry,
                selectionName,
                true,
                1f);
            return true;
        }

        private WallMatch FindBestPhysicalMatch(
            SelectionData selection,
            bool entry)
        {
            var result = new WallMatch();
            if (!selection.Snapshot.IsValid)
                return result;

            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (!candidate.IsValid ||
                    candidate.TrackableId ==
                    selection.Snapshot.TrackableId ||
                    IsReservedByOtherSelection(
                        candidate.TrackableId,
                        entry) ||
                    !TryScorePhysicalMatch(
                        selection.Snapshot,
                        candidate,
                        out var score))
                {
                    continue;
                }

                if (result.Candidate == null ||
                    score > result.Score)
                {
                    result.SecondScore =
                        result.Candidate == null
                            ? float.NegativeInfinity
                            : result.Score;
                    result.Candidate = candidate;
                    result.Score = score;
                }
                else if (score > result.SecondScore)
                {
                    result.SecondScore = score;
                }
            }

            if (result.Candidate == null)
                return result;

            result.IsAmbiguous =
                result.SecondScore > float.NegativeInfinity &&
                result.Score - result.SecondScore < ambiguityMargin;
            result.IsConfident =
                !result.IsAmbiguous &&
                result.Score >= minimumMatchScore;
            return result;
        }

        private void ApplyPhysicalMatch(
            SelectionData selection,
            WallMatch match,
            bool entry,
            string selectionName)
        {
            if (selection.State != WallSelectionState.Reacquiring)
                return;

            if (match.IsAmbiguous)
            {
                LogAmbiguousOnce(selection, selectionName);
                return;
            }

            if (!match.IsConfident)
                return;

            RecoverSelection(
                selection,
                match.Candidate,
                entry,
                selectionName,
                false,
                match.Score);
        }

        private void RecoverSelection(
            SelectionData selection,
            WallCandidate candidate,
            bool entry,
            string selectionName,
            bool sameTrackableId,
            float score)
        {
            var previousId = selection.Snapshot.TrackableId;
            candidate.AlignInwardNormal(
                selection.Snapshot.InwardNormal);

            if (entry)
            {
                entryWallId = candidate.TrackableId;
                SetEntryIndex(
                    FindCandidateIndex(entryWallId));
            }
            else
            {
                exitWallId = candidate.TrackableId;
                SetExitIndex(
                    FindCandidateIndex(exitWallId));
            }

            selection.State = WallSelectionState.Selected;
            selection.Snapshot = CaptureSnapshot(candidate);
            selection.ReacquireUntil = 0f;
            selection.AmbiguityLogged = false;
            IncrementSelectionRevision();

            if (sameTrackableId)
            {
                Debug.Log(
                    $"[WallDiscovery] {selectionName} Wall recovered " +
                    "with the same TrackableId.",
                    this);
            }
            else
            {
                Debug.Log(
                    $"[WallDiscovery] {selectionName} Wall rebound from " +
                    $"{previousId} to {candidate.TrackableId}; " +
                    $"physical-wall score={score:0.000}.",
                    this);
            }

            RefreshAllDebugViews();
        }

        private bool IsReservedByOtherSelection(
            TrackableId candidateId,
            bool entry)
        {
            var otherId = entry ? exitWallId : entryWallId;
            return otherId.HasValue &&
                otherId.Value == candidateId;
        }

        private bool TryScorePhysicalMatch(
            WallSnapshot snapshot,
            WallCandidate candidate,
            out float score)
        {
            score = 0f;
            var normalDot = Mathf.Clamp(
                Mathf.Abs(Vector3.Dot(
                    snapshot.InwardNormal,
                    candidate.InwardNormal)),
                0f,
                1f);
            var normalAngle = Mathf.Acos(normalDot) * Mathf.Rad2Deg;
            if (normalAngle > normalAngleTolerance)
                return false;

            var centerOffset = candidate.Center - snapshot.Center;
            var planeDistance = Mathf.Abs(Vector3.Dot(
                centerOffset,
                snapshot.InwardNormal));
            if (planeDistance > planeDistanceTolerance)
                return false;

            if (!TryMeasureProjectedOverlap(
                    snapshot,
                    candidate,
                    out var horizontalOverlap,
                    out var verticalOverlap))
            {
                return false;
            }

            if (horizontalOverlap < minimumOverlap ||
                verticalOverlap < minimumOverlap)
            {
                return false;
            }

            var widthRatio = SizeRatio(
                snapshot.Width,
                candidate.Width);
            var heightRatio = SizeRatio(
                snapshot.Height,
                candidate.Height);
            var minimumSizeRatio = 1f - sizeDifferenceTolerance;
            if (widthRatio < minimumSizeRatio ||
                heightRatio < minimumSizeRatio)
            {
                return false;
            }

            var semanticModeChanged =
                snapshot.IsSemanticWall != candidate.IsSemanticWall;
            if (semanticModeChanged)
            {
                var strongOverlap =
                    Mathf.Min(1f, minimumOverlap + 0.2f);
                if (normalAngle > normalAngleTolerance * 0.5f ||
                    planeDistance > planeDistanceTolerance * 0.5f ||
                    horizontalOverlap < strongOverlap ||
                    verticalOverlap < strongOverlap ||
                    widthRatio < Mathf.Max(minimumSizeRatio, 0.5f) ||
                    heightRatio < Mathf.Max(minimumSizeRatio, 0.5f))
                {
                    return false;
                }
            }

            var normalScore =
                1f - normalAngle / normalAngleTolerance;
            var planeScore =
                1f - planeDistance / planeDistanceTolerance;
            var overlapScore =
                (horizontalOverlap + verticalOverlap) * 0.5f;
            var sizeScore =
                (widthRatio + heightRatio) * 0.5f;
            var horizontalCenter = Vector3.Dot(
                centerOffset,
                snapshot.HorizontalAxis);
            var verticalCenter = Vector3.Dot(
                centerOffset,
                snapshot.VerticalAxis);
            var planarCenterDistance = Mathf.Sqrt(
                horizontalCenter * horizontalCenter +
                verticalCenter * verticalCenter);
            var centerScale = Mathf.Max(
                0.25f,
                Mathf.Sqrt(
                    snapshot.Width * snapshot.Width +
                    snapshot.Height * snapshot.Height) * 0.5f);
            var centerScore =
                1f / (1f + planarCenterDistance / centerScale);
            var semanticScore = GetSemanticScore(
                snapshot,
                candidate);

            score =
                normalScore * 0.18f +
                planeScore * 0.18f +
                overlapScore * 0.28f +
                sizeScore * 0.14f +
                centerScore * 0.12f +
                semanticScore * 0.10f;
            return true;
        }

        private static bool TryMeasureProjectedOverlap(
            WallSnapshot snapshot,
            WallCandidate candidate,
            out float horizontalOverlapRatio,
            out float verticalOverlapRatio)
        {
            horizontalOverlapRatio = 0f;
            verticalOverlapRatio = 0f;
            var boundary = candidate.Boundary;
            if (boundary.Count < 3)
                return false;

            var candidateMinHorizontal = float.PositiveInfinity;
            var candidateMaxHorizontal = float.NegativeInfinity;
            var candidateMinVertical = float.PositiveInfinity;
            var candidateMaxVertical = float.NegativeInfinity;
            for (var i = 0; i < boundary.Count; i++)
            {
                var offset = boundary[i] - snapshot.Center;
                var horizontal = Vector3.Dot(
                    offset,
                    snapshot.HorizontalAxis);
                var vertical = Vector3.Dot(
                    offset,
                    snapshot.VerticalAxis);
                candidateMinHorizontal = Mathf.Min(
                    candidateMinHorizontal,
                    horizontal);
                candidateMaxHorizontal = Mathf.Max(
                    candidateMaxHorizontal,
                    horizontal);
                candidateMinVertical = Mathf.Min(
                    candidateMinVertical,
                    vertical);
                candidateMaxVertical = Mathf.Max(
                    candidateMaxVertical,
                    vertical);
            }

            var horizontalOverlap = Mathf.Max(
                0f,
                Mathf.Min(
                    snapshot.MaxHorizontal,
                    candidateMaxHorizontal) -
                Mathf.Max(
                    snapshot.MinHorizontal,
                    candidateMinHorizontal));
            var verticalOverlap = Mathf.Max(
                0f,
                Mathf.Min(
                    snapshot.MaxVertical,
                    candidateMaxVertical) -
                Mathf.Max(
                    snapshot.MinVertical,
                    candidateMinVertical));
            var candidateWidth =
                candidateMaxHorizontal -
                candidateMinHorizontal;
            var candidateHeight =
                candidateMaxVertical -
                candidateMinVertical;
            var horizontalBase = Mathf.Min(
                snapshot.Width,
                candidateWidth);
            var verticalBase = Mathf.Min(
                snapshot.Height,
                candidateHeight);
            if (horizontalBase <= Mathf.Epsilon ||
                verticalBase <= Mathf.Epsilon)
            {
                return false;
            }

            horizontalOverlapRatio =
                horizontalOverlap / horizontalBase;
            verticalOverlapRatio =
                verticalOverlap / verticalBase;
            return true;
        }

        private static float GetSemanticScore(
            WallSnapshot snapshot,
            WallCandidate candidate)
        {
            if (snapshot.Classifications == candidate.Classifications)
                return 1f;

            if (snapshot.IsSemanticWall &&
                candidate.IsSemanticWall &&
                HasAny(
                    snapshot.Classifications,
                    candidate.Classifications))
            {
                return 0.8f;
            }

            if (snapshot.IsFallback && candidate.IsFallback)
                return 0.6f;

            return 0f;
        }

        private void RefreshSelectedFrame(WallCandidate candidate)
        {
            if (entryWallId == candidate.TrackableId &&
                entrySelection.State == WallSelectionState.Selected)
            {
                candidate.AlignInwardNormal(
                    entrySelection.Snapshot.InwardNormal);
                if (SelectedFrameChanged(
                        entrySelection.Snapshot,
                        candidate))
                {
                    entrySelection.Snapshot =
                        CaptureSnapshot(candidate);
                    IncrementSelectionRevision();
                }
            }

            if (exitWallId == candidate.TrackableId &&
                exitSelection.State == WallSelectionState.Selected)
            {
                candidate.AlignInwardNormal(
                    exitSelection.Snapshot.InwardNormal);
                if (SelectedFrameChanged(
                        exitSelection.Snapshot,
                        candidate))
                {
                    exitSelection.Snapshot =
                        CaptureSnapshot(candidate);
                    IncrementSelectionRevision();
                }
            }
        }

        private static bool SelectedFrameChanged(
            WallSnapshot snapshot,
            WallCandidate candidate)
        {
            return
                (snapshot.Center - candidate.Center).sqrMagnitude >
                SelectedPositionTolerance *
                SelectedPositionTolerance ||
                Vector3.Angle(
                    snapshot.InwardNormal,
                    candidate.InwardNormal) >
                SelectedAngleTolerance ||
                Mathf.Abs(snapshot.Width - candidate.Width) >
                SelectedSizeTolerance ||
                Mathf.Abs(snapshot.Height - candidate.Height) >
                SelectedSizeTolerance ||
                snapshot.Classifications != candidate.Classifications;
        }

        private static WallSnapshot CaptureSnapshot(
            WallCandidate candidate)
        {
            return new WallSnapshot
            {
                IsValid = true,
                TrackableId = candidate.TrackableId,
                Center = candidate.Center,
                InwardNormal = candidate.InwardNormal,
                HorizontalAxis = candidate.HorizontalAxis,
                VerticalAxis = candidate.VerticalAxis,
                Width = candidate.Width,
                Height = candidate.Height,
                MinHorizontal = candidate.MinHorizontal,
                MaxHorizontal = candidate.MaxHorizontal,
                MinVertical = candidate.MinVertical,
                MaxVertical = candidate.MaxVertical,
                Classifications = candidate.Classifications,
                IsSemanticWall = candidate.IsSemanticWall,
                IsFallback = candidate.IsFallback
            };
        }

        private static float SizeRatio(float first, float second)
        {
            var largest = Mathf.Max(first, second);
            return largest > Mathf.Epsilon
                ? Mathf.Min(first, second) / largest
                : 0f;
        }

        private void LogAmbiguousOnce(
            SelectionData selection,
            string selectionName)
        {
            if (selection.AmbiguityLogged)
                return;

            selection.AmbiguityLogged = true;
            Debug.LogWarning(
                $"[WallDiscovery] {selectionName} Wall replacement " +
                "was ambiguous; waiting.",
                this);
        }

        private static void CancelReacquisition(
            SelectionData selection)
        {
            selection.ReacquireUntil = 0f;
            selection.AmbiguityLogged = false;
        }

        private static void ResetSelection(
            SelectionData selection)
        {
            selection.State = WallSelectionState.None;
            selection.Snapshot = default;
            selection.ReacquireUntil = 0f;
            selection.AmbiguityLogged = false;
        }

        private static TrackableId? GetDiagnosticTrackableId(
            TrackableId? selectedId,
            SelectionData selection)
        {
            if (selectedId.HasValue)
                return selectedId;

            return selection.State == WallSelectionState.Reacquiring &&
                selection.Snapshot.IsValid
                    ? selection.Snapshot.TrackableId
                    : null;
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

        private sealed class SelectionData
        {
            public WallSelectionState State;
            public WallSnapshot Snapshot;
            public float ReacquireUntil;
            public bool AmbiguityLogged;
        }

        private struct WallSnapshot
        {
            public bool IsValid;
            public TrackableId TrackableId;
            public Vector3 Center;
            public Vector3 InwardNormal;
            public Vector3 HorizontalAxis;
            public Vector3 VerticalAxis;
            public float Width;
            public float Height;
            public float MinHorizontal;
            public float MaxHorizontal;
            public float MinVertical;
            public float MaxVertical;
            public PlaneClassifications Classifications;
            public bool IsSemanticWall;
            public bool IsFallback;
            public float RemovedAt;
        }

        private struct WallMatch
        {
            public WallCandidate Candidate;
            public float Score;
            public float SecondScore;
            public bool IsConfident;
            public bool IsAmbiguous;
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
