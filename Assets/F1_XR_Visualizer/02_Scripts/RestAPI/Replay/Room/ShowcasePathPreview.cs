using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace F1XR.RestAPI.Replay.Room
{
    [DisallowMultipleComponent]
    public sealed class ShowcasePathPreview : MonoBehaviour
    {
        private const string DebugRootName = "ShowcasePathPreviewDebug";
        private const int IgnoreRaycastLayer = 2;
        private const float SourcePositionTolerance = 0.0025f;
        private const float SourceAngleTolerance = 0.2f;
        private const int SourceSampleCount = 61;
        private const float ConnectorFraction = 0.22f;
        private static readonly Color PathColor =
            new(0.1f, 0.8f, 1f, 0.95f);
        private static readonly Color CapsuleColor =
            new(1f, 0.35f, 0.08f, 1f);

        [Header("Sources")]
        [SerializeField] private ShowcaseLayout showcaseLayout;
        [SerializeField] private Transform xrOrigin;

        [Header("Path")]
        [SerializeField, Min(0f)] private float entryHandleLength = 1f;
        [SerializeField, Min(0f)] private float heroIncomingHandleLength = 1f;
        [SerializeField, Min(0f)] private float heroOutgoingHandleLength = 1f;
        [SerializeField, Min(0f)] private float exitHandleLength = 1f;
        [SerializeField, Min(2)] private int samplesPerSegment = 24;
        [SerializeField] private Vector3 pathPositionOffset;

        [Header("Debug")]
        [SerializeField] private bool showDebug = true;
        [SerializeField, Min(0.001f)] private float debugLineWidth = 0.025f;
        [SerializeField] private Vector3 capsuleSize =
            new(0.12f, 0.12f, 0.3f);

        [Header("Preview")]
        [SerializeField] private bool previewMovementEnabled = true;
        [SerializeField] private bool autoplay = true;
        [SerializeField] private bool loop = true;
        [SerializeField, Min(0.1f)] private float autoplayDuration = 8f;

        private Vector3[] sampledPositions;
        private Vector3[] sampledTangents;
        private float[] cumulativeDistances;
        private Vector3[] firstSegment;
        private Vector3[] secondSegment;
        private float pathLength;
        private float previewProgress;
        private bool isPathValid;
        private bool isPreviewPlaying;
        private bool parametersDirty = true;
        private ReplayPlayer replayPlayer;
        private bool hasSourceSnapshot;
        private int observedLayoutRevision = -1;
        private int observedSourceRevision = -1;
        private Pose lastEntryPose;
        private Pose lastHeroPose;
        private Pose lastExitPose;
        private Vector3 lastEntryDirection;
        private Vector3 lastExitDirection;

        private Transform debugRoot;
        private LineRenderer pathLine;
        private Transform previewRoot;
        private Transform capsuleTransform;
        private MeshRenderer capsuleRenderer;
        private Material debugMaterial;
        private MaterialPropertyBlock capsuleProperties;
        private readonly List<Vector3> sourcePositions = new();
        private readonly List<Vector3> resampledSourcePositions = new();
        private readonly List<Vector3> candidatePositions = new();

        public bool IsPathValid => isPathValid;
        public float PathLength => pathLength;
        public int SampleCount =>
            sampledPositions != null ? sampledPositions.Length : 0;
        public Vector3 StartPosition =>
            TryEvaluatePosition(0f, out var position)
                ? position
                : Vector3.zero;
        public Vector3 EndPosition =>
            TryEvaluatePosition(1f, out var position)
                ? position
                : Vector3.zero;
        public bool UsesSourceGeometry { get; private set; }
        public string FallbackReason { get; private set; } = "";
        public bool GeometrySafetyPassed { get; private set; }
        public float TransitionPathProgress { get; private set; } = 0.5f;
        public float TransitionReplayProgress { get; private set; } = 0.5f;
        public float HeroMissDistance { get; private set; }

        private void Reset()
        {
            ResolveReferences();
        }

        private void Awake()
        {
            ResolveReferences();
            capsuleProperties = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            parametersDirty = true;
            observedLayoutRevision = -1;
            InvalidatePath();
        }

        private void Update()
        {
            ResolveReferences();

            var layoutReady =
                showcaseLayout != null &&
                showcaseLayout.isActiveAndEnabled &&
                showcaseLayout.IsLayoutValid;
            var revision =
                showcaseLayout != null
                    ? showcaseLayout.LayoutRevision
                    : -1;
            var sourceRevision = GetSourceRevision();
            if (sourceRevision != observedSourceRevision)
                parametersDirty = true;

            if (!layoutReady)
            {
                if (isPathValid)
                    InvalidatePath();

                observedLayoutRevision = revision;
                return;
            }

            if (parametersDirty ||
                !isPathValid ||
                HasMeaningfulSourceChange(revision))
            {
                RebuildPath();
            }
            else
            {
                observedLayoutRevision = revision;
            }

            if (!isPathValid || !previewMovementEnabled)
                return;

            if (isPreviewPlaying)
                AdvancePreview();
        }

        private void OnDisable()
        {
            InvalidatePath();
            DestroyDebug();
        }

        private void OnValidate()
        {
            entryHandleLength = Mathf.Max(0f, entryHandleLength);
            heroIncomingHandleLength =
                Mathf.Max(0f, heroIncomingHandleLength);
            heroOutgoingHandleLength =
                Mathf.Max(0f, heroOutgoingHandleLength);
            exitHandleLength = Mathf.Max(0f, exitHandleLength);
            samplesPerSegment = Mathf.Max(2, samplesPerSegment);
            debugLineWidth = Mathf.Max(0.001f, debugLineWidth);
            capsuleSize.x = Mathf.Max(0.01f, capsuleSize.x);
            capsuleSize.y = Mathf.Max(0.01f, capsuleSize.y);
            capsuleSize.z = Mathf.Max(0.02f, capsuleSize.z);
            autoplayDuration = Mathf.Max(0.1f, autoplayDuration);
            parametersDirty = true;
        }

        public void RebuildPath()
        {
            ResolveReferences();
            parametersDirty = false;
            observedLayoutRevision =
                showcaseLayout != null
                    ? showcaseLayout.LayoutRevision
                    : -1;
            observedSourceRevision = GetSourceRevision();

            if (showcaseLayout == null ||
                xrOrigin == null ||
                !showcaseLayout.isActiveAndEnabled ||
                !showcaseLayout.IsLayoutValid)
            {
                InvalidatePath();
                return;
            }

            var wasValid = isPathValid;
            if (!TryBuildSourcePath(out string fallbackReason))
                BuildBezierFallback(fallbackReason);

            if (sampledPositions == null ||
                sampledPositions.Length < 2 ||
                pathLength <= Mathf.Epsilon)
            {
                InvalidatePath();
                return;
            }

            isPathValid = true;
            CaptureSourceSnapshot();
            EnsureDebug();
            RefreshDebug();
            SetPreviewProgress(previewProgress);

            if (!wasValid && autoplay && previewMovementEnabled)
                PlayPreview();
        }

        public void SetDebugVisible(bool visible)
        {
            if (showDebug == visible)
                return;

            showDebug = visible;
            if (!showDebug)
            {
                DestroyDebug();
                return;
            }

            if (isPathValid)
            {
                EnsureDebug();
                RefreshDebug();
                SetPreviewProgress(previewProgress);
            }
        }

        /// <summary>
        /// Evaluates a world-space pose. normalizedT is clamped to [0, 1].
        /// </summary>
        public bool TryEvaluate(float normalizedT, out Pose pose)
        {
            pose = new Pose(Vector3.zero, Quaternion.identity);
            if (!TryEvaluateLocal(
                    Mathf.Clamp01(normalizedT),
                    out var localPosition,
                    out var localTangent))
            {
                return false;
            }

            var worldPosition = xrOrigin.TransformPoint(localPosition);
            var worldTangent =
                xrOrigin.TransformDirection(localTangent).normalized;
            pose = new Pose(
                worldPosition,
                BuildStableRotation(worldTangent, xrOrigin.up));
            return true;
        }

        /// <summary>
        /// Evaluates a world-space position. normalizedT is clamped to [0, 1].
        /// </summary>
        public bool TryEvaluatePosition(
            float normalizedT,
            out Vector3 position)
        {
            position = Vector3.zero;
            if (!TryEvaluateLocal(
                    Mathf.Clamp01(normalizedT),
                    out var localPosition,
                    out _))
            {
                return false;
            }

            position = xrOrigin.TransformPoint(localPosition);
            return true;
        }

        /// <summary>
        /// Evaluates a world-space tangent. normalizedT is clamped to [0, 1].
        /// </summary>
        public bool TryEvaluateTangent(
            float normalizedT,
            out Vector3 tangent)
        {
            tangent = Vector3.zero;
            if (!TryEvaluateLocal(
                    Mathf.Clamp01(normalizedT),
                    out _,
                    out var localTangent))
            {
                return false;
            }

            tangent =
                xrOrigin.TransformDirection(localTangent).normalized;
            return true;
        }

        public void SetPreviewProgress(float normalizedT)
        {
            previewProgress = Mathf.Clamp01(normalizedT);
            RefreshPreviewPose();
        }

        public void PlayPreview()
        {
            if (!isPathValid || !previewMovementEnabled)
                return;

            if (!loop && previewProgress >= 1f)
                previewProgress = 0f;

            isPreviewPlaying = true;
            RefreshPreviewPose();
        }

        public void PausePreview()
        {
            isPreviewPlaying = false;
        }

        public void ResetPreview()
        {
            isPreviewPlaying = false;
            previewProgress = 0f;
            RefreshPreviewPose();
        }

        private void ResolveReferences()
        {
            if (showcaseLayout == null)
                showcaseLayout = GetComponent<ShowcaseLayout>();

            if (replayPlayer == null)
                replayPlayer = FindFirstObjectByType<ReplayPlayer>();

            if (xrOrigin == null)
                xrOrigin = transform;
        }

        private void AdvancePreview()
        {
            var next =
                previewProgress +
                Time.deltaTime / autoplayDuration;

            if (next >= 1f)
            {
                if (loop)
                {
                    next = Mathf.Repeat(next, 1f);
                }
                else
                {
                    next = 1f;
                    isPreviewPlaying = false;
                }
            }

            SetPreviewProgress(next);
        }

        private bool TryBuildSourcePath(out string failure)
        {
            failure = "";
            EventPopoutReplay eventReplay = replayPlayer != null
                ? replayPlayer.EventReplay
                : null;
            if (eventReplay == null || !eventReplay.IsActive)
            {
                failure = "No active event replay source geometry is available.";
                return false;
            }

            if (!eventReplay.TryCopySourceCenterPath(
                    sourcePositions,
                    out float transitionSourceProgress,
                    out float transitionReplayProgress))
            {
                failure =
                    $"Event source geometry is unusable " +
                    $"({eventReplay.SourceGeometryPointCount} point(s)).";
                return false;
            }

            if (!ResampleSourcePath(
                    sourcePositions,
                    SourceSampleCount,
                    resampledSourcePositions))
            {
                failure = "Event source geometry could not be arc-length resampled.";
                return false;
            }

            Vector3 entry =
                ToLocalPosition(showcaseLayout.EntryPose.position);
            Vector3 hero =
                ToLocalPosition(showcaseLayout.HeroPose.position);
            Vector3 exit =
                ToLocalPosition(showcaseLayout.ExitPose.position);
            Vector3 roomForward = exit - entry;
            float roomDistance = roomForward.magnitude;
            if (roomDistance <= 0.1f)
            {
                failure = "Entry and Exit are too close to map source geometry.";
                return false;
            }

            Vector3 sourceForward =
                resampledSourcePositions[resampledSourcePositions.Count - 1] -
                resampledSourcePositions[0];
            if (sourceForward.sqrMagnitude <= 0.0001f)
            {
                failure = "Source center path has no stable movement direction.";
                return false;
            }

            float sourceLength = GetPolylineLength(
                resampledSourcePositions);
            float baseScale = roomDistance * 1.15f /
                Mathf.Max(0.001f, sourceLength);
            Vector3 preferredForward = roomForward.normalized;
            Vector3 heroForward =
                ToLocalDirection(showcaseLayout.HeroPose.forward);
            if (Vector3.Dot(heroForward, preferredForward) > 0f)
            {
                preferredForward = Vector3.Slerp(
                    preferredForward,
                    heroForward,
                    0.15f).normalized;
            }

            Quaternion sourceToRoom = Quaternion.FromToRotation(
                sourceForward.normalized,
                preferredForward);
            int transitionIndex = Mathf.Clamp(
                Mathf.RoundToInt(
                    transitionSourceProgress *
                    (resampledSourcePositions.Count - 1)),
                1,
                resampledSourcePositions.Count - 2);

            float[] scaleFactors = { 1f, 0.82f, 0.65f };
            float[] heroInfluences = { 0.7f, 0.5f, 0.3f };
            for (int attempt = 0;
                 attempt < scaleFactors.Length;
                 attempt++)
            {
                BuildSourceCandidate(
                    entry,
                    hero,
                    exit,
                    sourceToRoom,
                    baseScale * scaleFactors[attempt],
                    transitionIndex,
                    heroInfluences[attempt]);
                if (!IsGeometrySafe(candidatePositions))
                    continue;

                BuildSamplesFromPolyline(candidatePositions);
                UsesSourceGeometry = true;
                FallbackReason = "";
                GeometrySafetyPassed = true;
                TransitionReplayProgress = transitionReplayProgress;
                TransitionPathProgress =
                    cumulativeDistances[transitionIndex] /
                    Mathf.Max(0.001f, pathLength);
                HeroMissDistance = Vector3.Distance(
                    xrOrigin.TransformPoint(
                        sampledPositions[transitionIndex]),
                    showcaseLayout.HeroPose.position);
                return true;
            }

            failure =
                "Source geometry could not satisfy room endpoint geometry safety.";
            return false;
        }

        private void BuildSourceCandidate(
            Vector3 entry,
            Vector3 hero,
            Vector3 exit,
            Quaternion sourceToRoom,
            float scale,
            int transitionIndex,
            float heroInfluence)
        {
            candidatePositions.Clear();
            Vector3 sourceTransition =
                resampledSourcePositions[transitionIndex];
            float transitionProgress =
                transitionIndex /
                (float)(resampledSourcePositions.Count - 1);
            Vector3 linearTransition = Vector3.Lerp(
                entry,
                exit,
                transitionProgress);
            Vector3 heroOffset = hero - linearTransition;
            float maximumHeroOffset =
                Vector3.Distance(entry, exit) * 0.3f;
            heroOffset = Vector3.ClampMagnitude(
                heroOffset,
                maximumHeroOffset);
            Vector3 transitionTarget =
                linearTransition + heroOffset * heroInfluence;

            for (int i = 0; i < resampledSourcePositions.Count; i++)
            {
                Vector3 position =
                    transitionTarget +
                    sourceToRoom *
                    (resampledSourcePositions[i] - sourceTransition) *
                    scale;
                candidatePositions.Add(position);
            }

            Vector3 entryCorrection = entry - candidatePositions[0];
            Vector3 exitCorrection =
                exit - candidatePositions[candidatePositions.Count - 1];
            float entryConnectorFraction = Mathf.Clamp(
                transitionProgress * 0.45f,
                0.04f,
                ConnectorFraction);
            float exitConnectorFraction = Mathf.Clamp(
                (1f - transitionProgress) * 0.45f,
                0.04f,
                ConnectorFraction);
            for (int i = 0; i < candidatePositions.Count; i++)
            {
                float progress =
                    i / (float)(candidatePositions.Count - 1);
                float entryWeight =
                    1f - Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.Clamp01(
                            progress / entryConnectorFraction));
                float exitWeight =
                    Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.Clamp01(
                            (progress -
                             (1f - exitConnectorFraction)) /
                            exitConnectorFraction));
                candidatePositions[i] +=
                    entryCorrection * entryWeight +
                    exitCorrection * exitWeight;
            }

            candidatePositions[0] = entry;
            candidatePositions[candidatePositions.Count - 1] =
                exit;
        }

        private void BuildBezierFallback(string reason)
        {
            Vector3 entry =
                ToLocalPosition(showcaseLayout.EntryPose.position);
            Vector3 hero =
                ToLocalPosition(showcaseLayout.HeroPose.position);
            Vector3 exit =
                ToLocalPosition(showcaseLayout.ExitPose.position);
            Vector3 entryDirection =
                ToLocalDirection(showcaseLayout.EntryTravelDirection);
            Vector3 heroDirection =
                ToLocalDirection(showcaseLayout.HeroPose.forward);
            Vector3 exitDirection =
                ToLocalDirection(showcaseLayout.ExitTravelDirection);

            if (!DirectionsAreValid(
                    entryDirection,
                    heroDirection,
                    exitDirection))
            {
                InvalidatePath();
                return;
            }

            EnsureControlPointCapacity();
            float firstHandleLimit =
                Vector3.Distance(entry, hero) / 3f;
            float secondHandleLimit =
                Vector3.Distance(hero, exit) / 3f;
            firstSegment[0] = entry;
            firstSegment[1] =
                entry +
                entryDirection *
                Mathf.Min(entryHandleLength, firstHandleLimit);
            firstSegment[2] =
                hero -
                heroDirection *
                Mathf.Min(heroIncomingHandleLength, firstHandleLimit);
            firstSegment[3] = hero;
            secondSegment[0] = hero;
            secondSegment[1] =
                hero +
                heroDirection *
                Mathf.Min(heroOutgoingHandleLength, secondHandleLimit);
            secondSegment[2] =
                exit -
                exitDirection *
                Mathf.Min(exitHandleLength, secondHandleLimit);
            secondSegment[3] = exit;

            SampleBezierFallback();
            UsesSourceGeometry = false;
            FallbackReason = reason;
            GeometrySafetyPassed = IsGeometrySafe(sampledPositions);
            TransitionPathProgress =
                cumulativeDistances[samplesPerSegment] /
                Mathf.Max(0.001f, pathLength);
            TransitionReplayProgress = 0.5f;
            HeroMissDistance = Vector3.Distance(
                xrOrigin.TransformPoint(
                    sampledPositions[samplesPerSegment]),
                showcaseLayout.HeroPose.position);
            Debug.LogWarning(
                $"[ShowcasePathPreview] Using Bézier fallback: {reason}",
                this);
        }

        private void SampleBezierFallback()
        {
            var count = samplesPerSegment * 2 + 1;
            EnsureSampleCapacity(count);

            var index = 0;
            for (var i = 0; i <= samplesPerSegment; i++)
            {
                var t = i / (float)samplesPerSegment;
                SampleSegment(firstSegment, t, index++);
            }

            for (var i = 1; i <= samplesPerSegment; i++)
            {
                var t = i / (float)samplesPerSegment;
                SampleSegment(secondSegment, t, index++);
            }

            cumulativeDistances[0] = 0f;
            pathLength = 0f;
            var previous =
                xrOrigin.TransformPoint(sampledPositions[0]);
            for (var i = 1; i < count; i++)
            {
                var current =
                    xrOrigin.TransformPoint(sampledPositions[i]);
                pathLength += Vector3.Distance(previous, current);
                cumulativeDistances[i] = pathLength;
                previous = current;
            }
        }

        private void BuildSamplesFromPolyline(
            List<Vector3> positions)
        {
            EnsureSampleCapacity(positions.Count);
            for (int i = 0; i < positions.Count; i++)
            {
                sampledPositions[i] = positions[i];
                Vector3 before = positions[Mathf.Max(0, i - 1)];
                Vector3 after = positions[Mathf.Min(
                    positions.Count - 1,
                    i + 1)];
                sampledTangents[i] = (after - before).normalized;
            }

            RebuildCumulativeDistances();
        }

        private void RebuildCumulativeDistances()
        {
            cumulativeDistances[0] = 0f;
            pathLength = 0f;
            Vector3 previous =
                xrOrigin.TransformPoint(sampledPositions[0]);
            for (int i = 1; i < sampledPositions.Length; i++)
            {
                Vector3 current =
                    xrOrigin.TransformPoint(sampledPositions[i]);
                pathLength += Vector3.Distance(previous, current);
                cumulativeDistances[i] = pathLength;
                previous = current;
            }
        }

        private static bool ResampleSourcePath(
            List<Vector3> source,
            int sampleCount,
            List<Vector3> destination)
        {
            destination.Clear();
            if (source == null || source.Count < 3 || sampleCount < 3)
                return false;

            var clean = new List<Vector3>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                if (clean.Count == 0 ||
                    Vector3.SqrMagnitude(
                        source[i] - clean[clean.Count - 1]) >
                    0.000001f)
                {
                    clean.Add(source[i]);
                }
            }

            if (clean.Count < 3)
                return false;

            var distances = new float[clean.Count];
            for (int i = 1; i < clean.Count; i++)
            {
                distances[i] =
                    distances[i - 1] +
                    Vector3.Distance(clean[i - 1], clean[i]);
            }

            float length = distances[distances.Length - 1];
            if (length <= 0.001f)
                return false;

            int upper = 1;
            for (int i = 0; i < sampleCount; i++)
            {
                float target =
                    length * i / (sampleCount - 1f);
                while (upper < distances.Length - 1 &&
                       distances[upper] < target)
                {
                    upper++;
                }

                int lower = Mathf.Max(0, upper - 1);
                float blend = Mathf.InverseLerp(
                    distances[lower],
                    distances[upper],
                    target);
                destination.Add(Vector3.Lerp(
                    clean[lower],
                    clean[upper],
                    blend));
            }

            return destination.Count == sampleCount;
        }

        private static float GetPolylineLength(
            IList<Vector3> positions)
        {
            float length = 0f;
            for (int i = 1; i < positions.Count; i++)
            {
                length += Vector3.Distance(
                    positions[i - 1],
                    positions[i]);
            }

            return length;
        }

        private static bool IsGeometrySafe(
            IList<Vector3> positions)
        {
            if (positions == null || positions.Count < 3)
                return false;

            Vector3 overall =
                positions[positions.Count - 1] - positions[0];
            if (overall.sqrMagnitude <= 0.0001f)
                return false;

            Vector3 previousDirection = Vector3.zero;
            for (int i = 1; i < positions.Count; i++)
            {
                Vector3 segment = positions[i] - positions[i - 1];
                if (segment.sqrMagnitude <= 0.000001f)
                    return false;

                Vector3 direction = segment.normalized;
                if (Vector3.Dot(direction, overall.normalized) < -0.15f)
                    return false;
                if (previousDirection.sqrMagnitude > 0f &&
                    Vector3.Dot(previousDirection, direction) < -0.2f)
                {
                    return false;
                }

                previousDirection = direction;
            }

            for (int first = 0;
                 first < positions.Count - 3;
                 first++)
            {
                for (int second = first + 2;
                     second < positions.Count - 1;
                     second++)
                {
                    if (SegmentsIntersectXZ(
                            positions[first],
                            positions[first + 1],
                            positions[second],
                            positions[second + 1]))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool SegmentsIntersectXZ(
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector3 d)
        {
            Vector2 firstStart = new(a.x, a.z);
            Vector2 firstDelta = new(b.x - a.x, b.z - a.z);
            Vector2 secondStart = new(c.x, c.z);
            Vector2 secondDelta = new(d.x - c.x, d.z - c.z);
            float denominator = Cross(firstDelta, secondDelta);
            if (Mathf.Abs(denominator) <= 0.000001f)
                return false;

            Vector2 offset = secondStart - firstStart;
            float firstT = Cross(offset, secondDelta) / denominator;
            float secondT = Cross(offset, firstDelta) / denominator;
            const float endpointTolerance = 0.001f;
            return firstT > endpointTolerance &&
                firstT < 1f - endpointTolerance &&
                secondT > endpointTolerance &&
                secondT < 1f - endpointTolerance;
        }

        private static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        private int GetSourceRevision()
        {
            EventPopoutReplay eventReplay = replayPlayer != null
                ? replayPlayer.EventReplay
                : null;
            return eventReplay != null
                ? eventReplay.SourceGeometryRevision
                : -1;
        }

        private void EnsureControlPointCapacity()
        {
            firstSegment ??= new Vector3[4];
            secondSegment ??= new Vector3[4];
        }

        private void EnsureSampleCapacity(int count)
        {
            if (sampledPositions != null &&
                sampledPositions.Length == count)
            {
                return;
            }

            sampledPositions = new Vector3[count];
            sampledTangents = new Vector3[count];
            cumulativeDistances = new float[count];
        }

        private void SampleSegment(
            Vector3[] points,
            float t,
            int index)
        {
            sampledPositions[index] =
                EvaluateBezier(points, t) + pathPositionOffset;
            sampledTangents[index] =
                EvaluateBezierTangent(points, t);
        }

        private bool TryEvaluateLocal(
            float normalizedT,
            out Vector3 position,
            out Vector3 tangent)
        {
            position = Vector3.zero;
            tangent = Vector3.forward;
            if (!isPathValid ||
                sampledPositions == null ||
                sampledTangents == null ||
                cumulativeDistances == null ||
                sampledPositions.Length < 2)
            {
                return false;
            }

            if (normalizedT <= 0f)
            {
                position = sampledPositions[0];
                tangent = sampledTangents[0];
                return true;
            }

            var last = sampledPositions.Length - 1;
            if (normalizedT >= 1f)
            {
                position = sampledPositions[last];
                tangent = sampledTangents[last];
                return true;
            }

            var targetDistance = normalizedT * pathLength;
            var upper = FindUpperDistanceIndex(targetDistance);
            var lower = upper - 1;
            var distanceRange =
                cumulativeDistances[upper] -
                cumulativeDistances[lower];
            var blend =
                distanceRange > Mathf.Epsilon
                    ? (targetDistance - cumulativeDistances[lower]) /
                      distanceRange
                    : 0f;

            position = Vector3.LerpUnclamped(
                sampledPositions[lower],
                sampledPositions[upper],
                blend);
            tangent = Vector3.Slerp(
                sampledTangents[lower],
                sampledTangents[upper],
                blend).normalized;
            return true;
        }

        private int FindUpperDistanceIndex(float targetDistance)
        {
            var low = 1;
            var high = cumulativeDistances.Length - 1;
            while (low < high)
            {
                var middle = (low + high) / 2;
                if (cumulativeDistances[middle] < targetDistance)
                    low = middle + 1;
                else
                    high = middle;
            }

            return low;
        }

        private void EnsureDebug()
        {
            if (!showDebug || debugRoot != null)
                return;

            var rootObject = new GameObject(DebugRootName)
            {
                layer = IgnoreRaycastLayer,
                hideFlags = HideFlags.DontSave
            };
            debugRoot = rootObject.transform;
            debugRoot.SetParent(xrOrigin, false);

            var shader =
                Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogWarning(
                    "[ShowcasePathPreview] No debug-compatible shader was found.",
                    this);
                DestroyDebug();
                return;
            }

            debugMaterial = new Material(shader)
            {
                name = "Showcase Path Preview Material",
                hideFlags = HideFlags.HideAndDontSave
            };

            var lineObject = new GameObject("Path")
            {
                layer = IgnoreRaycastLayer
            };
            lineObject.transform.SetParent(debugRoot, false);
            pathLine = lineObject.AddComponent<LineRenderer>();
            pathLine.useWorldSpace = false;
            pathLine.loop = false;
            pathLine.sharedMaterial = debugMaterial;
            pathLine.startColor = PathColor;
            pathLine.endColor = PathColor;
            pathLine.alignment = LineAlignment.View;
            pathLine.numCornerVertices = 3;
            pathLine.numCapVertices = 3;
            pathLine.shadowCastingMode = ShadowCastingMode.Off;
            pathLine.receiveShadows = false;

            var previewObject = new GameObject("Preview Capsule")
            {
                layer = IgnoreRaycastLayer
            };
            previewRoot = previewObject.transform;
            previewRoot.SetParent(debugRoot, false);

            var capsuleObject =
                GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsuleObject.name = "Capsule";
            capsuleObject.layer = IgnoreRaycastLayer;
            capsuleTransform = capsuleObject.transform;
            capsuleTransform.SetParent(previewRoot, false);
            capsuleTransform.localRotation =
                Quaternion.Euler(90f, 0f, 0f);

            var collider = capsuleObject.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                Destroy(collider);
            }

            capsuleRenderer =
                capsuleObject.GetComponent<MeshRenderer>();
            capsuleRenderer.sharedMaterial = debugMaterial;
            capsuleRenderer.shadowCastingMode =
                ShadowCastingMode.Off;
            capsuleRenderer.receiveShadows = false;
            capsuleProperties ??= new MaterialPropertyBlock();
            capsuleProperties.SetColor("_BaseColor", CapsuleColor);
            capsuleProperties.SetColor("_Color", CapsuleColor);
            capsuleRenderer.SetPropertyBlock(capsuleProperties);
        }

        private void RefreshDebug()
        {
            if (!showDebug || debugRoot == null || pathLine == null)
                return;

            debugRoot.gameObject.SetActive(isPathValid);
            pathLine.startWidth = debugLineWidth;
            pathLine.endWidth = debugLineWidth;
            pathLine.positionCount = sampledPositions.Length;
            pathLine.SetPositions(sampledPositions);

            if (previewRoot != null)
            {
                previewRoot.gameObject.SetActive(
                    previewMovementEnabled);
            }

            if (capsuleTransform != null)
            {
                var diameter =
                    Mathf.Max(capsuleSize.x, capsuleSize.y);
                capsuleTransform.localScale = new Vector3(
                    diameter,
                    capsuleSize.z * 0.5f,
                    diameter);
            }
        }

        private void RefreshPreviewPose()
        {
            if (!isPathValid ||
                previewRoot == null ||
                !previewMovementEnabled)
            {
                return;
            }

            if (!TryEvaluateLocal(
                    previewProgress,
                    out var position,
                    out var tangent))
            {
                return;
            }

            previewRoot.localPosition = position;
            previewRoot.localRotation =
                BuildStableRotation(tangent, Vector3.up);
        }

        private void InvalidatePath()
        {
            isPathValid = false;
            isPreviewPlaying = false;
            previewProgress = 0f;
            pathLength = 0f;
            hasSourceSnapshot = false;

            if (debugRoot != null)
                debugRoot.gameObject.SetActive(false);
        }

        private void DestroyDebug()
        {
            pathLine = null;
            previewRoot = null;
            capsuleTransform = null;
            capsuleRenderer = null;

            if (debugRoot != null)
            {
                Destroy(debugRoot.gameObject);
                debugRoot = null;
            }

            if (debugMaterial != null)
            {
                Destroy(debugMaterial);
                debugMaterial = null;
            }
        }

        private Vector3 ToLocalPosition(Vector3 worldPosition)
        {
            return xrOrigin.InverseTransformPoint(worldPosition);
        }

        private Vector3 ToLocalDirection(Vector3 worldDirection)
        {
            return xrOrigin
                .InverseTransformDirection(worldDirection)
                .normalized;
        }

        private bool HasMeaningfulSourceChange(int revision)
        {
            if (!hasSourceSnapshot)
                return true;

            if (observedLayoutRevision == revision)
                return false;

            return PoseChanged(lastEntryPose, showcaseLayout.EntryPose) ||
                PoseChanged(lastHeroPose, showcaseLayout.HeroPose) ||
                PoseChanged(lastExitPose, showcaseLayout.ExitPose) ||
                DirectionChanged(
                    lastEntryDirection,
                    showcaseLayout.EntryTravelDirection) ||
                DirectionChanged(
                    lastExitDirection,
                    showcaseLayout.ExitTravelDirection);
        }

        private void CaptureSourceSnapshot()
        {
            lastEntryPose = showcaseLayout.EntryPose;
            lastHeroPose = showcaseLayout.HeroPose;
            lastExitPose = showcaseLayout.ExitPose;
            lastEntryDirection = showcaseLayout.EntryTravelDirection;
            lastExitDirection = showcaseLayout.ExitTravelDirection;
            hasSourceSnapshot = true;
        }

        private static bool PoseChanged(Pose previous, Pose current)
        {
            return
                (previous.position - current.position).sqrMagnitude >
                SourcePositionTolerance * SourcePositionTolerance ||
                Quaternion.Angle(previous.rotation, current.rotation) >
                SourceAngleTolerance;
        }

        private static bool DirectionChanged(
            Vector3 previous,
            Vector3 current)
        {
            return Vector3.Angle(previous, current) >
                SourceAngleTolerance;
        }

        private static bool DirectionsAreValid(
            Vector3 entry,
            Vector3 hero,
            Vector3 exit)
        {
            return entry.sqrMagnitude > 0.0001f &&
                hero.sqrMagnitude > 0.0001f &&
                exit.sqrMagnitude > 0.0001f;
        }

        private static Vector3 EvaluateBezier(
            Vector3[] points,
            float t)
        {
            var oneMinusT = 1f - t;
            return
                oneMinusT * oneMinusT * oneMinusT * points[0] +
                3f * oneMinusT * oneMinusT * t * points[1] +
                3f * oneMinusT * t * t * points[2] +
                t * t * t * points[3];
        }

        private static Vector3 EvaluateBezierTangent(
            Vector3[] points,
            float t)
        {
            var oneMinusT = 1f - t;
            var tangent =
                3f * oneMinusT * oneMinusT *
                (points[1] - points[0]) +
                6f * oneMinusT * t *
                (points[2] - points[1]) +
                3f * t * t *
                (points[3] - points[2]);

            if (tangent.sqrMagnitude < 0.0001f)
                tangent = points[3] - points[0];

            return tangent.normalized;
        }

        private static Quaternion BuildStableRotation(
            Vector3 forward,
            Vector3 preferredUp)
        {
            forward.Normalize();
            var up =
                Vector3.ProjectOnPlane(preferredUp, forward);
            if (up.sqrMagnitude < 0.0001f)
                up = Vector3.ProjectOnPlane(Vector3.forward, forward);
            if (up.sqrMagnitude < 0.0001f)
                up = Vector3.ProjectOnPlane(Vector3.right, forward);

            return Quaternion.LookRotation(
                forward,
                up.normalized);
        }
    }
}
