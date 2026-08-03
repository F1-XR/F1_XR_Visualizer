using System;
using System.Collections;
using System.Collections.Generic;
using F1XR.Interaction.World;
using F1XR.RestAPI.Api;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace F1XR.RestAPI.Replay
{
    public sealed class EventPopoutReplay : MonoBehaviour
    {
        private const int MaxEventDrivers = 4;
        private const float RelativeTrackRegionPadding = 0.12f;
        private const float MinimumTrackRegionPadding = 0.00005f;

        [Header("Development")]
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public bool allowDevelopmentFallbackEvent;
#else
        public bool allowDevelopmentFallbackEvent;
#endif

        [Header("Stage")]
        public Transform stageAnchor;
        [Min(0.1f)] public float stageScale = 3f;
        [Min(0.1f)] public float stageSideOffset = 0.9f;
        [Min(0.1f)] public float stageForwardDistance = 1.1f;
        public float stageHeightOffset = 0.25f;
        [Min(0.001f)] public float trackRegionPadding = 0.04f;
        [Min(0.001f)] public float roadWidth = 0.035f;
        public Color safetyApronColor =
            new(0.16f, 0.18f, 0.22f, 1f);
        [Min(0f)] public float safetyApronSurfaceOffset = 0.0002f;
        [Min(0f)] public float roadEndPadding;
        [Min(0f)] public float trackPaddingSeconds = 5f;
        [Min(3)] public int maxTrackPoints = 192;
        [Min(1f)] public float maxEventDuration = 30f;
        [Min(0.01f)] public float eventPlaybackSpeed = 1f;
        [Min(0f)] public float eventLeadSeconds = 6f;
        [Min(0f)] public float eventTailSeconds = 3f;
        [Min(0f)] public float overtakeMotionLeadSeconds = 3f;
        [Min(0f)] public float raceStartMotionGraceSeconds = 1f;
        [Min(0f)] public float minimumMovingLeadSeconds = 4f;
        [Min(1f)] public float eventMinimumSeparationInVehicleWidths = 1.05f;

        private readonly ReplayTimeline timeline = new();
        private readonly Dictionary<int, List<LocationSample>> eventSamples = new();
        private readonly Dictionary<int, int> eventIndices = new();
        private readonly HashSet<int> eventDrivers = new();
        private readonly List<Vector3> mappedPath = new();
        private readonly List<float> mappedPathDistances = new();
        private readonly List<Vector3> fallbackCorridorPath = new();
        private readonly List<Vector3> safetyApronPath = new();
        private readonly List<Vector3> safetyApronLeftEdge = new();
        private readonly List<Vector3> safetyApronRightEdge = new();
        private readonly List<Vector3> drivableLeftEdge = new();
        private readonly List<Vector3> drivableRightEdge = new();
        private readonly Dictionary<int, List<float>> eventLongitudinals = new();
        private readonly List<TableTrackRendererState> tableTrackRendererStates = new();
        private readonly OvertakeCompletionDetector
            completionDetector = new();

        private ReplayPlayer player;
        private ReplayCarSet eventCars;
        private ReplayAudio eventAudio;
        private int showcaseAudioFocusDriver;
        private ReplayEventDto currentEvent;
        private ReplayEventDto motionEvent;
        private OvertakeMotionSettings eventOvertakeSettings;
        private GameObject stageRoot;
        private BoxCollider stageInteractionCollider;
        private EventTrackSegment trackSegment;
        private Mesh roadMesh;
        private LineRenderer leftRoadEdge;
        private LineRenderer rightRoadEdge;
        private Material roadMaterial;
        private Material edgeMaterial;
        private Mesh safetyApronMesh;
        private Material safetyApronMaterial;
        private Coroutine openRoutine;
        private ReplaySnapshot snapshot;
        private int referenceDriverNumber;
        private int sourceGeometryRevision;
        private Vector3 eventSpaceCenter;
        private Quaternion sourceToEventRotation = Quaternion.identity;
        private bool hasSnapshot;
        private bool isLoading;
        private bool isActive;
        private float showcasePlaybackSpeedMultiplier = 1f;
        private float overtakeVehicleSizeScale = 1f;

        public bool IsLoading => isLoading;
        public bool IsActive => isActive;
        public bool IsPlaying => isActive && timeline.IsPlaying;
        public float CurrentTime => isActive ? timeline.CurrentTime : 0f;
        public bool OvertakeCompletionConfirmed =>
            isActive &&
            completionDetector.HasTriggered;
        public float StartTime => isActive ? timeline.StartTime : 0f;
        public float EndTime => isActive ? timeline.RaceEndTime : 0f;
        public float NormalizedTime => isActive
            ? timeline.ToNormalized(timeline.CurrentTime)
            : 0f;
        public ReplayEventDto CurrentEvent => currentEvent;
        public Transform PresentationRoot => stageRoot != null
            ? stageRoot.transform
            : null;
        public int SourceGeometryRevision => sourceGeometryRevision;
        public int SourceGeometryPointCount => mappedPath.Count;
        public float OrderingTransitionTime => isActive
            ? ResolveOrderingTransitionTime()
            : 0f;

        public bool TryCopyEventLocalCenterPath(
            List<Vector3> destination,
            out Vector3 overtakePosition,
            out float transitionReplayProgress)
        {
            overtakePosition = Vector3.zero;
            if (!TryCopySourceCenterPath(
                    destination,
                    out _,
                    out transitionReplayProgress))
            {
                return false;
            }

            for (int i = 0; i < destination.Count; i++)
            {
                destination[i] =
                    sourceToEventRotation *
                    (destination[i] - eventSpaceCenter);
            }

            float transitionTime = ResolveOrderingTransitionTime();
            if (referenceDriverNumber > 0 &&
                TryGetSourceLongitudinalAtTime(
                    referenceDriverNumber,
                    transitionTime,
                    out float transitionDistance))
            {
                overtakePosition =
                    sourceToEventRotation *
                    (EvaluateSourcePathDistance(transitionDistance) -
                     eventSpaceCenter);
            }
            else
            {
                overtakePosition =
                    destination[destination.Count / 2];
            }

            return true;
        }

        public bool TryCopySourceCenterPath(
            List<Vector3> destination,
            out float transitionPathProgress,
            out float transitionReplayProgress)
        {
            transitionPathProgress = 0.5f;
            transitionReplayProgress = 0.5f;
            destination?.Clear();
            if (!isActive ||
                destination == null ||
                mappedPath.Count < 3 ||
                mappedPathDistances.Count != mappedPath.Count)
            {
                return false;
            }

            if (!TryGetSourceLongitudinalAtTime(
                    referenceDriverNumber,
                    timeline.StartTime,
                    out float windowStart) ||
                !TryGetSourceLongitudinalAtTime(
                    referenceDriverNumber,
                    timeline.RaceEndTime,
                    out float windowEnd) ||
                windowEnd - windowStart <= 0.0001f)
            {
                return false;
            }

            CopySourcePathWindow(
                destination,
                windowStart,
                windowEnd);
            if (destination.Count < 3)
                return false;

            float transitionTime = ResolveOrderingTransitionTime();
            transitionReplayProgress =
                timeline.ToNormalized(transitionTime);

            if (referenceDriverNumber > 0 &&
                TryGetSourceLongitudinalAtTime(
                    referenceDriverNumber,
                    transitionTime,
                    out float transitionDistance))
            {
                transitionPathProgress = Mathf.InverseLerp(
                    windowStart,
                    windowEnd,
                    transitionDistance);
            }

            return true;
        }

        private void CopySourcePathWindow(
            List<Vector3> destination,
            float startDistance,
            float endDistance)
        {
            destination.Add(EvaluateSourcePathDistance(startDistance));
            for (int i = 1; i < mappedPath.Count - 1; i++)
            {
                float distance = mappedPathDistances[i];
                if (distance <= startDistance ||
                    distance >= endDistance)
                {
                    continue;
                }

                Vector3 point = mappedPath[i];
                if (Vector3.SqrMagnitude(
                        point - destination[destination.Count - 1]) >
                    0.000001f)
                {
                    destination.Add(point);
                }
            }

            Vector3 end = EvaluateSourcePathDistance(endDistance);
            if (Vector3.SqrMagnitude(
                    end - destination[destination.Count - 1]) >
                0.000001f)
            {
                destination.Add(end);
            }
        }

        private Vector3 EvaluateSourcePathDistance(float distance)
        {
            float clamped = Mathf.Clamp(
                distance,
                mappedPathDistances[0],
                mappedPathDistances[mappedPathDistances.Count - 1]);
            int upper = 1;
            while (upper < mappedPathDistances.Count - 1 &&
                   mappedPathDistances[upper] < clamped)
            {
                upper++;
            }

            int lower = upper - 1;
            float blend = Mathf.InverseLerp(
                mappedPathDistances[lower],
                mappedPathDistances[upper],
                clamped);
            return Vector3.Lerp(
                mappedPath[lower],
                mappedPath[upper],
                blend);
        }

        public bool TryGetSourceLongitudinal(
            int driverNumber,
            out float longitudinal)
        {
            longitudinal = 0f;
            return isActive &&
                TryGetSourceLongitudinalAtTime(
                    driverNumber,
                    timeline.CurrentTime,
                    out longitudinal);
        }

        public bool TryGetReferenceSourceLongitudinal(
            float normalizedTime,
            out float longitudinal)
        {
            longitudinal = 0f;
            if (!isActive || referenceDriverNumber <= 0)
                return false;

            float time = Mathf.Lerp(
                timeline.StartTime,
                timeline.RaceEndTime,
                Mathf.Clamp01(normalizedTime));
            return TryGetSourceLongitudinalAtTime(
                referenceDriverNumber,
                time,
                out longitudinal);
        }

        public void Configure(ReplayPlayer replayPlayer)
        {
            player = replayPlayer;
        }

        public bool TrySetPresentationPose(
            Vector3 position,
            Quaternion rotation,
            float uniformScale)
        {
            if (PresentationRoot == null)
                return false;

            PresentationRoot.SetPositionAndRotation(position, rotation);
            PresentationRoot.localScale =
                Vector3.one * Mathf.Max(0.1f, uniformScale);
            return true;
        }

        public bool TryRestoreTableRelativePose()
        {
            if (PresentationRoot == null)
                return false;

            ResolveStagePose(out Vector3 position, out Quaternion rotation);
            rotation = Quaternion.Euler(0f, rotation.eulerAngles.y, 0f);
            return TrySetPresentationPose(position, rotation, stageScale);
        }

        private void Awake()
        {
            if (player == null)
                player = GetComponent<ReplayPlayer>();
        }

        public void OpenTestOvertake()
        {
            if (player == null || !player.HasDataset)
            {
                Debug.LogWarning("[EventReplay] Cannot open an event before the replay dataset is ready.", this);
                return;
            }

            float earliestUsableAnchor =
                ResolveEarliestAutomaticOvertakeAnchor();
            ReplayEventDto definition = FindClosestOvertake(
                player.Events,
                player.CurrentTime,
                earliestUsableAnchor);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (definition == null)
                definition = FindClosestOvertake(
                    ReplayEventFixtures.Load(player.Manifest),
                    player.CurrentTime,
                    earliestUsableAnchor);

            if (definition == null && allowDevelopmentFallbackEvent)
                definition = CreateDevelopmentEvent();
#endif

            if (definition == null)
            {
                Debug.LogWarning("[EventReplay] No overtake event is available in the manifest.", this);
                return;
            }

            Open(definition);
        }

        public void Open(ReplayEventDto definition)
        {
            ReplayEventDto presentation = CreatePresentationEvent(definition);
            if (!TryValidate(presentation, out string error))
            {
                Debug.LogWarning($"[EventReplay] Invalid event: {error}", this);
                return;
            }

            if (!hasSnapshot)
            {
                snapshot = new ReplaySnapshot(player);
                hasSnapshot = true;
            }

            player.Pause();

            if (openRoutine != null)
                StopCoroutine(openRoutine);

            DestroyStage(false);
            currentEvent = presentation;
            openRoutine = StartCoroutine(OpenRoutine(presentation));
        }

        public void Play()
        {
            if (!isActive)
                return;

            timeline.Play();
            eventAudio?.SetPlaying(true);
        }

        public void Pause()
        {
            timeline.Pause();
            eventAudio?.SetPlaying(false);
        }

        public void SetShowcaseAudioFocus(int driverNumber)
        {
            driverNumber = Mathf.Max(0, driverNumber);
            if (showcaseAudioFocusDriver == driverNumber)
                return;

            showcaseAudioFocusDriver = driverNumber;
            eventCars?.SetAudioFocusDriver(driverNumber);
        }

        public void SetShowcaseDrivingPresentation(
            int firstDriver,
            int secondDriver,
            bool enabled)
        {
            eventCars?.SetShowcaseDrivingPresentation(
                firstDriver,
                secondDriver,
                enabled);
        }

        public void SetShowcasePlaybackSpeedMultiplier(
            float multiplier)
        {
            showcasePlaybackSpeedMultiplier =
                Mathf.Max(1f, multiplier);
        }

        public void SetOvertakeVehicleSizeScale(float scale)
        {
            overtakeVehicleSizeScale = Mathf.Max(0.01f, scale);
            eventCars?.SetOvertakeVehicleSizeScale(
                overtakeVehicleSizeScale);
        }

        public void TogglePlay()
        {
            if (IsPlaying)
                Pause();
            else
                Play();
        }

        public void Restart()
        {
            if (!isActive)
                return;

            timeline.SetTime(timeline.StartTime);
            ResetIndices();
            Play();
            ApplyCars();
        }

        public void SeekNormalized(float normalized)
        {
            if (!isActive)
                return;

            timeline.SetTime(timeline.FromNormalized(normalized));
            ResetIndices();
            ApplyCars();
        }

        public void Close()
        {
            if (openRoutine != null)
            {
                StopCoroutine(openRoutine);
                openRoutine = null;
            }

            DestroyStage();
            RestoreReplay();
        }

        internal void SuspendTableTrackRendering()
        {
            if (player == null ||
                tableTrackRendererStates.Count > 0)
                return;

            Transform tableTrack = player.GetTrackPlacementTransform();
            if (tableTrack == null)
                return;

            Renderer[] renderers =
                tableTrack.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                tableTrackRendererStates.Add(
                    new TableTrackRendererState(renderer));
                renderer.forceRenderingOff = true;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        internal void RestoreTableTrackRendering()
        {
            for (int i = 0; i < tableTrackRendererStates.Count; i++)
                tableTrackRendererStates[i].Restore();

            tableTrackRendererStates.Clear();
        }

        private IEnumerator OpenRoutine(ReplayEventDto definition)
        {
            isLoading = true;
            float loadStart = Mathf.Max(player.TimelineStartTime, definition.startTime - trackPaddingSeconds);
            float loadEnd = Mathf.Min(player.ReadyUntilTime, definition.endTime + trackPaddingSeconds);
            bool loaded = false;

            yield return player.LoadEventRange(loadStart, loadEnd, value => loaded = value);

            if (loaded && IsDevelopmentEvent(definition))
                SelectClosestDevelopmentDrivers(
                    definition,
                    definition.startTime,
                    definition.endTime);

            if (!loaded || !BuildEventSamples(definition, loadStart, loadEnd))
            {
                Debug.LogWarning(
                    $"[EventReplay] Event '{definition.eventId}' has no usable vehicle samples in " +
                    $"{definition.startTime:0.00}-{definition.endTime:0.00}.",
                    this);
                isLoading = false;
                openRoutine = null;
                Close();
                yield break;
            }

            if (!BuildStage(definition, loadStart, loadEnd))
            {
                Debug.LogWarning(
                    $"[EventReplay] Event '{definition.eventId}' did not produce enough mapped track points.",
                    this);
                isLoading = false;
                openRoutine = null;
                Close();
                yield break;
            }

            timeline.Reset(loadStart, loadEnd);
            isLoading = false;
            isActive = true;
            openRoutine = null;
            ApplyCars();
            CreateSafetyApron();
            Play();
        }

        private void Update()
        {
            if (!isActive)
                return;

            if (timeline.IsPlaying)
            {
                timeline.Advance(
                    Time.deltaTime,
                    eventPlaybackSpeed *
                    showcasePlaybackSpeedMultiplier);
                if (timeline.StopAtEnd())
                    eventAudio?.SetPlaying(false);
            }

            eventAudio?.Update(
                player != null ? player.engineSound : null,
                true,
                timeline.IsPlaying,
                null);
            ApplyCars();
        }

        private bool BuildEventSamples(
            ReplayEventDto definition,
            float loadStart,
            float loadEnd)
        {
            eventSamples.Clear();
            eventIndices.Clear();
            eventDrivers.Clear();
            referenceDriverNumber = 0;

            Dictionary<int, List<LocationSample>> source = player.LocationsByDriver;
            if (source == null)
                return false;

            int[] requestedDrivers = definition.driverNumbers;
            if (requestedDrivers == null || requestedDrivers.Length == 0)
                return false;

            for (int i = 0; i < requestedDrivers.Length && eventDrivers.Count < MaxEventDrivers; i++)
            {
                int driver = requestedDrivers[i];
                if (driver <= 0 || eventDrivers.Contains(driver))
                    continue;

                if (!source.TryGetValue(driver, out List<LocationSample> samples))
                {
                    Debug.LogWarning(
                        $"[EventReplay] Driver {driver} is unavailable for event '{definition.eventId}'.",
                        this);
                    continue;
                }

                List<LocationSample> clip = CopyRange(samples, loadStart, loadEnd);
                if (clip.Count < 2)
                {
                    Debug.LogWarning(
                        $"[EventReplay] Driver {driver} has fewer than two samples for event '{definition.eventId}'.",
                        this);
                    continue;
                }

                eventDrivers.Add(driver);
                eventSamples.Add(driver, clip);
                eventIndices.Add(driver, 0);
                if (referenceDriverNumber == 0)
                    referenceDriverNumber = driver;
            }

            return eventDrivers.Count > 0;
        }

        private bool BuildStage(
            ReplayEventDto definition,
            float trackStartTime,
            float trackEndTime)
        {
            eventCars = new ReplayCarSet(
                player.carPrefab,
                null,
                false);
            eventCars.SetOvertakePresentationMode(
                OvertakePresentationMode.Showcase);
            eventCars.SetOvertakeVehicleSizeScale(
                overtakeVehicleSizeScale);
            eventCars.SetMapScaleRatio(
                player.GetTrackMapScaleRatio());
            eventCars.SetTeamPrefabs(player.teamCarPrefabs);
            eventCars.SetCalibration(player.trackCalibration, false);
            eventCars.SetLabelsVisible(true);
            eventCars.SetLeaderHighlightVisible(false);
            eventCars.SetDrivers(player.Manifest != null ? player.Manifest.drivers : null);
            eventOvertakeSettings =
                CreateEventOvertakeSettings(
                    player.overtakeMotion);
            eventCars.SetOvertakeSettings(
                eventOvertakeSettings);

            List<LocationSample> referenceSamples = FindReferenceSamples();
            if (!BuildMappedPath(
                    referenceSamples,
                    trackStartTime,
                    trackEndTime))
                return false;
            if (!BuildEventLongitudinals())
                return false;
            float transitionTime =
                ResolveOrderingTransitionTime(
                    definition,
                    definition.startTime,
                    definition.endTime);
            motionEvent = CreateMotionEvent(
                definition,
                transitionTime);
            eventCars.SetReplayEvents(
                new[] { motionEvent });
            eventCars.SetOvertakeApproachRibbon(
                motionEvent,
                player.overtakeApproachRibbon);
            eventCars.SetOvertakeSideBySideVfx(
                motionEvent,
                player.overtakeSideBySideVfx);
            completionDetector.Configure(
                player.overtakeCompletionVfx);
            eventCars.SetOvertakeCompletionVfx(
                player.overtakeCompletionVfx);
            sourceGeometryRevision++;

            GetPathFrame(out Vector3 center, out Quaternion sourceToLocalRotation);
            eventSpaceCenter = center;
            sourceToEventRotation = sourceToLocalRotation;
            bool fallbackCorridorLoops =
                BuildFallbackCorridorPath(
                    center,
                    sourceToLocalRotation);
            BuildSafetyApronPath(
                center,
                sourceToLocalRotation);
            eventCars.SetFallbackOvertakeCorridor(
                fallbackCorridorPath,
                roadWidth,
                fallbackCorridorLoops);
            CreateStageRoot();

            TryGetMappedPosition(
                referenceSamples,
                definition.startTime,
                out Vector3 eventStartPosition);
            TryGetMappedPosition(
                referenceSamples,
                definition.anchorTime,
                out Vector3 eventAnchorPosition);
            Debug.Log(
                $"[EventReplayFrame] event={definition.eventId}, " +
                $"center={center:F4}, " +
                $"startLocal={(sourceToLocalRotation * (eventStartPosition - center)):F4}, " +
                $"anchorLocal={(sourceToLocalRotation * (eventAnchorPosition - center)):F4}, " +
                $"stageScale={stageRoot.transform.localScale.x:F4}",
                this);

            Transform carsRoot = new GameObject("Cars").transform;
            carsRoot.SetParent(stageRoot.transform, false);
            eventCars.SetCustomSpace(carsRoot, center, sourceToLocalRotation);

            if (!CreateActualTrackRegion(
                    center,
                    sourceToLocalRotation,
                    out Bounds stageBounds))
            {
                CreateRoad(center, sourceToLocalRotation);
                stageBounds = roadMesh.bounds;
            }

            ConfigureStageInteraction(stageBounds);

            eventAudio = new ReplayAudio(eventCars);
            eventAudio.Reset(player.engineSound, true, null);
            stageRoot.SetActive(false);

            Debug.Log(
                $"[EventReplay] Opened '{definition.eventId}' with {eventDrivers.Count} vehicle(s) " +
                $"and {mappedPath.Count} track points.",
                this);
            return true;
        }

        private bool BuildMappedPath(
            List<LocationSample> samples,
            float startTime,
            float endTime)
        {
            mappedPath.Clear();
            if (samples == null)
                return false;

            Vector3 last = default;
            bool hasLast = false;
            AddMappedPathPoint(samples, startTime, ref last, ref hasLast);

            int stride = Mathf.Max(
                1,
                Mathf.CeilToInt(samples.Count / (float)Mathf.Max(3, maxTrackPoints)));

            for (int i = 0; i < samples.Count; i += stride)
            {
                LocationSample sample = samples[i];
                if (sample.t <= startTime || sample.t >= endTime ||
                    !eventCars.TryGetMappedPosition(sample, out Vector3 position))
                    continue;

                if (hasLast && Vector3.SqrMagnitude(position - last) < 0.000004f)
                    continue;

                mappedPath.Add(position);
                last = position;
                hasLast = true;
            }

            AddMappedPathPoint(samples, endTime, ref last, ref hasLast);

            return mappedPath.Count >= 3;
        }

        private bool BuildEventLongitudinals()
        {
            mappedPathDistances.Clear();
            eventLongitudinals.Clear();
            if (mappedPath.Count < 2)
                return false;

            float distance = 0f;
            mappedPathDistances.Add(distance);
            for (int i = 1; i < mappedPath.Count; i++)
            {
                distance += Vector3.Distance(
                    mappedPath[i - 1],
                    mappedPath[i]);
                mappedPathDistances.Add(distance);
            }

            if (distance <= 0.0001f)
                return false;

            foreach (KeyValuePair<int, List<LocationSample>> pair in eventSamples)
            {
                List<LocationSample> samples = pair.Value;
                List<float> longitudinals = new(samples.Count);
                float previousLongitudinal = 0f;
                int previousSegment = 0;
                for (int i = 0; i < samples.Count; i++)
                {
                    if (!eventCars.TryGetMappedPosition(
                            samples[i],
                            out Vector3 position))
                    {
                        eventLongitudinals.Clear();
                        return false;
                    }

                    float longitudinal = ProjectSourcePathDistance(
                        position,
                        previousLongitudinal,
                        ref previousSegment);
                    previousLongitudinal = Mathf.Max(
                        previousLongitudinal,
                        longitudinal);
                    longitudinals.Add(previousLongitudinal);
                }

                eventLongitudinals.Add(pair.Key, longitudinals);
            }

            return eventLongitudinals.Count > 0;
        }

        private float ProjectSourcePathDistance(
            Vector3 position,
            float minimumPathDistance,
            ref int segmentHint)
        {
            float closestSqrDistance = float.PositiveInfinity;
            float closestPathDistance = minimumPathDistance;
            int closestSegment = segmentHint;

            int firstSegment = Mathf.Max(0, segmentHint - 1);
            for (int i = firstSegment; i < mappedPath.Count - 1; i++)
            {
                Vector3 start = mappedPath[i];
                Vector3 segment = mappedPath[i + 1] - start;
                float segmentSqrLength = segment.sqrMagnitude;
                if (segmentSqrLength <= 0.000001f)
                    continue;

                float interpolation = Mathf.Clamp01(
                    Vector3.Dot(position - start, segment) /
                    segmentSqrLength);
                float pathDistance =
                    mappedPathDistances[i] +
                    Mathf.Sqrt(segmentSqrLength) * interpolation;
                if (pathDistance + 0.00001f < minimumPathDistance)
                    continue;

                Vector3 projected = start + segment * interpolation;
                float sqrDistance =
                    Vector3.SqrMagnitude(position - projected);
                if (sqrDistance >= closestSqrDistance)
                    continue;

                closestSqrDistance = sqrDistance;
                closestPathDistance = pathDistance;
                closestSegment = i;
            }

            segmentHint = closestSegment;
            return closestPathDistance;
        }

        private bool TryGetSourceLongitudinalAtTime(
            int driverNumber,
            float time,
            out float longitudinal)
        {
            longitudinal = 0f;
            if (!eventSamples.TryGetValue(
                    driverNumber,
                    out List<LocationSample> samples) ||
                !eventLongitudinals.TryGetValue(
                    driverNumber,
                    out List<float> longitudinals) ||
                samples.Count < 2 ||
                longitudinals.Count != samples.Count)
            {
                return false;
            }

            if (time <= samples[0].t)
            {
                longitudinal = longitudinals[0];
                return true;
            }

            int last = samples.Count - 1;
            if (time >= samples[last].t)
            {
                longitudinal = longitudinals[last];
                return true;
            }

            int low = 0;
            int high = last;
            while (low + 1 < high)
            {
                int middle = (low + high) / 2;
                if (samples[middle].t <= time)
                    low = middle;
                else
                    high = middle;
            }

            float duration = Mathf.Max(
                0.001f,
                samples[high].t - samples[low].t);
            float interpolation = Mathf.Clamp01(
                (time - samples[low].t) / duration);
            longitudinal = Mathf.Lerp(
                longitudinals[low],
                longitudinals[high],
                interpolation);
            return true;
        }

        public bool TryConfigureRoomStageInteraction(
            Vector3 eventLocalFocus,
            float physicalWidth = 0.6f)
        {
            if (stageInteractionCollider == null ||
                PresentationRoot == null)
            {
                return false;
            }

            Vector3 worldScale = PresentationRoot.lossyScale;
            float uniformWorldScale = Mathf.Max(
                Mathf.Abs(worldScale.x),
                Mathf.Abs(worldScale.y),
                Mathf.Abs(worldScale.z));
            if (uniformWorldScale <= 0.000001f)
                return false;

            float localWidth =
                Mathf.Max(0.1f, physicalWidth) /
                uniformWorldScale;
            float localHeight = 0.12f / uniformWorldScale;
            stageInteractionCollider.center =
                eventLocalFocus +
                Vector3.up * localHeight * 0.5f;
            stageInteractionCollider.size =
                new Vector3(
                    localWidth,
                    localHeight,
                    localWidth);
            return true;
        }

        private float ResolveOrderingTransitionTime()
        {
            return ResolveOrderingTransitionTime(
                currentEvent,
                timeline.StartTime,
                timeline.RaceEndTime);
        }

        private float ResolveOrderingTransitionTime(
            ReplayEventDto definition,
            float startTime,
            float endTime)
        {
            float fallback = definition != null
                ? definition.anchorTime
                : Mathf.Lerp(startTime, endTime, 0.5f);
            int[] drivers = definition != null
                ? definition.driverNumbers
                : null;
            if (drivers == null || drivers.Length < 2)
                return fallback;

            const int sampleCount = 96;
            float previousTime = startTime;
            if (!TryGetSourceGap(
                    drivers[0],
                    drivers[1],
                    previousTime,
                    out float previousGap))
            {
                return fallback;
            }

            for (int i = 1; i <= sampleCount; i++)
            {
                float time = Mathf.Lerp(
                    startTime,
                    endTime,
                    i / (float)sampleCount);
                if (!TryGetSourceGap(
                        drivers[0],
                        drivers[1],
                        time,
                        out float gap))
                {
                    continue;
                }

                if (GapOrder(previousGap) != 0 &&
                    GapOrder(gap) != 0 &&
                    GapOrder(previousGap) != GapOrder(gap))
                {
                    float blend =
                        Mathf.Abs(previousGap) /
                        (Mathf.Abs(previousGap) + Mathf.Abs(gap));
                    return Mathf.Lerp(previousTime, time, blend);
                }

                previousTime = time;
                previousGap = gap;
            }

            return fallback;
        }

        private bool TryGetSourceGap(
            int firstDriver,
            int secondDriver,
            float time,
            out float gap)
        {
            gap = 0f;
            if (!TryGetSourceLongitudinalAtTime(
                    firstDriver,
                    time,
                    out float first) ||
                !TryGetSourceLongitudinalAtTime(
                    secondDriver,
                    time,
                    out float second))
            {
                return false;
            }

            gap = first - second;
            return true;
        }

        private static int GapOrder(float gap)
        {
            if (gap > 0.0001f)
                return 1;
            if (gap < -0.0001f)
                return -1;
            return 0;
        }

        private void AddMappedPathPoint(
            List<LocationSample> samples,
            float time,
            ref Vector3 last,
            ref bool hasLast)
        {
            if (!TryGetMappedPosition(samples, time, out Vector3 position) ||
                hasLast && Vector3.SqrMagnitude(position - last) < 0.000004f)
                return;

            mappedPath.Add(position);
            last = position;
            hasLast = true;
        }

        private bool TryGetMappedPosition(
            List<LocationSample> samples,
            float time,
            out Vector3 position)
        {
            position = default;
            if (!TryGetSamplePair(samples, time, out LocationSample a, out LocationSample b))
                return false;

            if (!eventCars.TryGetMappedPosition(a, out Vector3 positionA) ||
                !eventCars.TryGetMappedPosition(b, out Vector3 positionB))
                return false;

            float duration = Mathf.Max(0.001f, b.t - a.t);
            float interpolation = Mathf.Clamp01((time - a.t) / duration);
            position = Vector3.Lerp(positionA, positionB, interpolation);
            return true;
        }

        private void GetPathFrame(
            out Vector3 center,
            out Quaternion sourceToLocalRotation)
        {
            Bounds bounds = new Bounds(mappedPath[0], Vector3.zero);
            for (int i = 1; i < mappedPath.Count; i++)
                bounds.Encapsulate(mappedPath[i]);

            center = bounds.center;
            Vector3 forward = mappedPath[mappedPath.Count - 1] - mappedPath[0];
            forward.y = 0f;

            if (forward.sqrMagnitude < 0.000001f)
            {
                for (int i = 1; i < mappedPath.Count; i++)
                {
                    Vector3 candidate = mappedPath[i] - mappedPath[i - 1];
                    candidate.y = 0f;
                    if (candidate.sqrMagnitude > forward.sqrMagnitude)
                        forward = candidate;
                }
            }

            sourceToLocalRotation = forward.sqrMagnitude > 0.000001f
                ? Quaternion.Inverse(Quaternion.LookRotation(forward.normalized, Vector3.up))
                : Quaternion.identity;
        }

        private void CreateStageRoot()
        {
            stageRoot = new GameObject("EventReplayStage");
            TryRestoreTableRelativePose();
        }

        private void ResolveStagePose(out Vector3 position, out Quaternion rotation)
        {
            if (stageAnchor != null)
            {
                position = stageAnchor.position;
                rotation = stageAnchor.rotation;
                return;
            }

            Transform cameraTransform = Camera.main != null ? Camera.main.transform : null;
            Transform trackTransform = player.GetTrackPlacementTransform();
            Vector3 flatForward = cameraTransform != null
                ? Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized
                : Vector3.forward;
            Vector3 flatRight = cameraTransform != null
                ? Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized
                : Vector3.right;

            if (flatForward.sqrMagnitude < 0.001f)
                flatForward = Vector3.forward;
            if (flatRight.sqrMagnitude < 0.001f)
                flatRight = Vector3.right;

            if (trackTransform != null)
            {
                position = trackTransform.position + flatRight * stageSideOffset + Vector3.up * stageHeightOffset;
            }
            else if (cameraTransform != null)
            {
                position = cameraTransform.position + flatForward * stageForwardDistance;
                position.y += stageHeightOffset - 0.5f;
            }
            else
            {
                position = transform.position + transform.forward * stageForwardDistance;
                position.y += stageHeightOffset;
            }

            rotation = Quaternion.LookRotation(flatRight, Vector3.up);
        }

        private void CreateRoad(
            Vector3 center,
            Quaternion sourceToLocalRotation)
        {
            int count = mappedPath.Count;
            Vector3[] vertices = new Vector3[count * 2];
            Vector2[] uv = new Vector2[count * 2];
            int[] triangles = new int[(count - 1) * 6];
            Vector3[] localPath = new Vector3[count];

            for (int i = 0; i < count; i++)
                localPath[i] = sourceToLocalRotation * (mappedPath[i] - center);

            ExtendRoadEnd(localPath);

            for (int i = 0; i < count; i++)
            {
                Vector3 before = localPath[Mathf.Max(0, i - 1)];
                Vector3 after = localPath[Mathf.Min(count - 1, i + 1)];
                Vector3 tangent = after - before;
                tangent.y = 0f;
                if (tangent.sqrMagnitude < 0.000001f)
                    tangent = Vector3.forward;

                Vector3 side = Vector3.Cross(Vector3.up, tangent.normalized);
                vertices[i * 2] = localPath[i] - side * roadWidth * 0.5f;
                vertices[i * 2 + 1] = localPath[i] + side * roadWidth * 0.5f;
                float v = i / (float)(count - 1);
                uv[i * 2] = new Vector2(0f, v);
                uv[i * 2 + 1] = new Vector2(1f, v);

                if (i >= count - 1)
                    continue;

                int triangle = i * 6;
                int vertex = i * 2;
                triangles[triangle] = vertex;
                triangles[triangle + 1] = vertex + 2;
                triangles[triangle + 2] = vertex + 1;
                triangles[triangle + 3] = vertex + 1;
                triangles[triangle + 4] = vertex + 2;
                triangles[triangle + 5] = vertex + 3;
            }

            roadMesh = new Mesh { name = "EventRoadMesh" };
            roadMesh.vertices = vertices;
            roadMesh.uv = uv;
            roadMesh.triangles = triangles;
            roadMesh.RecalculateNormals();
            roadMesh.RecalculateBounds();

            GameObject road = new GameObject("EventRoad", typeof(MeshFilter), typeof(MeshRenderer));
            road.transform.SetParent(stageRoot.transform, false);
            road.GetComponent<MeshFilter>().sharedMesh = roadMesh;
            roadMaterial = CreateMaterial(new Color(0.12f, 0.13f, 0.15f, 1f));
            MeshRenderer renderer = road.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = roadMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            edgeMaterial = CreateMaterial(new Color(0.8f, 0.08f, 0.04f, 1f));
            leftRoadEdge =
                CreateEdge(
                    "LeftEdge",
                    vertices,
                    0,
                    edgeMaterial);
            rightRoadEdge =
                CreateEdge(
                    "RightEdge",
                    vertices,
                    1,
                    edgeMaterial);

        }

        private bool CreateActualTrackRegion(
            Vector3 center,
            Quaternion sourceToLocalRotation,
            out Bounds stageBounds)
        {
            float effectivePadding =
                ResolveTrackRegionPadding();
            trackSegment = new EventTrackSegment();
            bool created = trackSegment.Build(
                stageRoot.transform,
                player.GetTrackPlacementTransform(),
                mappedPath,
                center,
                sourceToLocalRotation,
                effectivePadding,
                out stageBounds);
            if (created)
                return true;

            trackSegment.Clear();
            trackSegment = null;
            Debug.LogWarning(
                "[EventReplay] Actual track geometry was unavailable; using the generated road fallback.",
                this);
            return false;
        }

        private bool BuildFallbackCorridorPath(
            Vector3 center,
            Quaternion sourceToLocalRotation)
        {
            fallbackCorridorPath.Clear();
            TrackCalibration calibration =
                player != null
                    ? player.trackCalibration
                    : null;
            if (calibration != null &&
                calibration.active &&
                calibration.mappingMode ==
                TrackCalibration.MappingMode.Route &&
                calibration.points != null &&
                calibration.points.Length >= 2)
            {
                float scale = calibration.OutputScale;
                for (int i = 0;
                     i < calibration.points.Length;
                     i++)
                {
                    Vector3 target =
                        calibration.points[i]
                            .targetLocalPosition;
                    target = new Vector3(
                        target.x * scale,
                        target.y * scale +
                        calibration.heightOffset,
                        target.z * scale);
                    fallbackCorridorPath.Add(
                        sourceToLocalRotation *
                        (target - center));
                }

                return calibration.loopMappingSegments;
            }

            for (int i = 0; i < mappedPath.Count; i++)
            {
                fallbackCorridorPath.Add(
                    sourceToLocalRotation *
                    (mappedPath[i] - center));
            }

            return false;
        }

        private void BuildSafetyApronPath(
            Vector3 center,
            Quaternion sourceToLocalRotation)
        {
            safetyApronPath.Clear();
            for (int i = 0; i < mappedPath.Count; i++)
            {
                safetyApronPath.Add(
                    sourceToLocalRotation *
                    (mappedPath[i] - center));
            }
        }

        private void CreateSafetyApron()
        {
            if (stageRoot == null ||
                trackSegment == null ||
                safetyApronMesh != null ||
                safetyApronPath.Count < 2 ||
                eventCars == null)
            {
                return;
            }

            float vehicleWidth = 0f;
            foreach (int driver in eventDrivers)
            {
                if (!eventCars.TryGetVisualTransform(
                        driver,
                        out Transform visual) ||
                    visual == null ||
                    !visual.TryGetComponent(
                        out ReplayCarView car))
                {
                    continue;
                }

                vehicleWidth = Mathf.Max(
                    vehicleWidth,
                    car.GetVisualWidth());
            }

            if (vehicleWidth <= 0f)
                return;

            bool hasDrivableEdges =
                trackSegment.TryBuildDrivableEdges(
                    safetyApronPath,
                    vehicleWidth,
                    drivableLeftEdge,
                    drivableRightEdge);
            bool hasReliableRoadEdges =
                !hasDrivableEdges &&
                trackSegment.TryBuildReliableRoadEdges(
                    safetyApronPath,
                    vehicleWidth,
                    drivableLeftEdge,
                    drivableRightEdge);
            if (hasDrivableEdges ||
                hasReliableRoadEdges)
            {
                eventCars.SetActualOvertakeCorridor(
                    safetyApronPath,
                    drivableLeftEdge,
                    drivableRightEdge,
                    false);
                eventCars.ResetResolvedOvertakeSides();
                ApplyCars();
                if (hasReliableRoadEdges)
                {
                    Debug.LogWarning(
                        "[EventReplay] Drivable-only boundaries were incomplete; overtake motion is using detected road-surface boundaries.",
                        this);
                }
            }
            else
            {
                Debug.LogWarning(
                    "[EventReplay] Exact drivable boundaries were unavailable; overtake motion is using the event roadWidth fallback.",
                    this);
            }

            bool hasRoadEdges =
                trackSegment.TryBuildSafetyApronEdges(
                    safetyApronPath,
                    vehicleWidth,
                    safetyApronLeftEdge,
                    safetyApronRightEdge);
            if (!hasRoadEdges)
            {
                Debug.LogWarning(
                    "[EventReplay] Safety apron was skipped because actual road edges were unavailable.",
                    this);
                return;
            }

            if (motionEvent == null ||
                !eventCars.TryGetResolvedOvertakeSide(
                    motionEvent,
                    out int passingSide) ||
                !TryGetSafetyApronDistances(
                    out float motionStartDistance,
                    out float approachEndDistance,
                    out float returnStartDistance,
                    out float motionEndDistance))
            {
                Debug.LogWarning(
                    "[EventReplay] Safety apron was skipped because the resolved overtake path was unavailable.",
                    this);
                return;
            }

            int count = safetyApronPath.Count;
            Vector3[] vertices = new Vector3[count * 2];
            Vector2[] uv = new Vector2[count * 2];
            int[] triangles = new int[(count - 1) * 6];
            Vector3 surfaceOffset =
                Vector3.down *
                Mathf.Max(
                    0f,
                    safetyApronSurfaceOffset);

            for (int i = 0; i < count; i++)
            {
                Vector3 left =
                    safetyApronLeftEdge[i];
                Vector3 right =
                    safetyApronRightEdge[i];
                float extensionWeight =
                    SafetyApronExtensionWeight(
                        mappedPathDistances[i],
                        motionStartDistance,
                        approachEndDistance,
                        returnStartDistance,
                        motionEndDistance);
                if (extensionWeight > 0f)
                {
                    Vector3 side = right - left;
                    side.y = 0f;
                    if (side.sqrMagnitude >
                        0.000001f)
                    {
                        side.Normalize();
                        if (passingSide > 0)
                        {
                            right +=
                                side *
                                vehicleWidth *
                                extensionWeight;
                        }
                        else
                        {
                            left -=
                                side *
                                vehicleWidth *
                                extensionWeight;
                        }
                    }
                }

                vertices[i * 2] =
                    left + surfaceOffset;
                vertices[i * 2 + 1] =
                    right + surfaceOffset;
                float v = i / (float)(count - 1);
                uv[i * 2] = new Vector2(0f, v);
                uv[i * 2 + 1] = new Vector2(1f, v);

                if (i >= count - 1)
                    continue;

                int triangle = i * 6;
                int vertex = i * 2;
                triangles[triangle] = vertex;
                triangles[triangle + 1] = vertex + 2;
                triangles[triangle + 2] = vertex + 1;
                triangles[triangle + 3] = vertex + 1;
                triangles[triangle + 4] = vertex + 2;
                triangles[triangle + 5] = vertex + 3;
            }

            safetyApronMesh = new Mesh
            {
                name = "EventSafetyApronMesh"
            };
            safetyApronMesh.vertices = vertices;
            safetyApronMesh.uv = uv;
            safetyApronMesh.triangles = triangles;
            safetyApronMesh.RecalculateNormals();
            safetyApronMesh.RecalculateBounds();

            GameObject apron = new GameObject(
                "EventRoadSafetyApron",
                typeof(MeshFilter),
                typeof(MeshRenderer));
            apron.transform.SetParent(
                stageRoot.transform,
                false);
            apron.GetComponent<MeshFilter>()
                .sharedMesh = safetyApronMesh;
            safetyApronMaterial =
                CreateMaterial(safetyApronColor);
            MeshRenderer renderer =
                apron.GetComponent<MeshRenderer>();
            renderer.sharedMaterial =
                safetyApronMaterial;
            renderer.shadowCastingMode =
                ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private bool TryGetSafetyApronDistances(
            out float motionStart,
            out float approachEnd,
            out float returnStart,
            out float motionEnd)
        {
            motionStart = 0f;
            approachEnd = 0f;
            returnStart = 0f;
            motionEnd = 0f;
            if (motionEvent == null ||
                eventOvertakeSettings == null ||
                referenceDriverNumber <= 0)
            {
                return false;
            }

            float duration =
                motionEvent.endTime -
                motionEvent.startTime;
            if (duration <= 0f)
                return false;

            float totalPortion = Mathf.Max(
                0.0001f,
                eventOvertakeSettings.approachPortion +
                eventOvertakeSettings.parallelPortion +
                eventOvertakeSettings.returnPortion);
            float approachProgress =
                eventOvertakeSettings.approachPortion /
                totalPortion;
            float returnProgress =
                (eventOvertakeSettings.approachPortion +
                 eventOvertakeSettings.parallelPortion) /
                totalPortion;
            float anchorProgress = Mathf.Clamp01(
                (motionEvent.anchorTime -
                 motionEvent.startTime) /
                duration);
            approachProgress = Mathf.Clamp(
                Mathf.Min(
                    approachProgress,
                    anchorProgress),
                0.0001f,
                0.9998f);
            returnProgress = Mathf.Clamp(
                Mathf.Max(
                    returnProgress,
                    anchorProgress),
                approachProgress + 0.0001f,
                0.9999f);

            return TryGetSourceLongitudinalAtTime(
                    referenceDriverNumber,
                    motionEvent.startTime,
                    out motionStart) &&
                TryGetSourceLongitudinalAtTime(
                    referenceDriverNumber,
                    Mathf.Lerp(
                        motionEvent.startTime,
                        motionEvent.endTime,
                        approachProgress),
                    out approachEnd) &&
                TryGetSourceLongitudinalAtTime(
                    referenceDriverNumber,
                    Mathf.Lerp(
                        motionEvent.startTime,
                        motionEvent.endTime,
                        returnProgress),
                    out returnStart) &&
                TryGetSourceLongitudinalAtTime(
                    referenceDriverNumber,
                    motionEvent.endTime,
                    out motionEnd) &&
                motionEnd - motionStart >
                0.0001f;
        }

        private static float SafetyApronExtensionWeight(
            float distance,
            float motionStart,
            float approachEnd,
            float returnStart,
            float motionEnd)
        {
            if (distance <= motionStart ||
                distance >= motionEnd)
            {
                return 0f;
            }

            if (distance < approachEnd)
            {
                return Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(
                        motionStart,
                        approachEnd,
                        distance));
            }

            if (distance <= returnStart)
                return 1f;

            return Mathf.SmoothStep(
                1f,
                0f,
                Mathf.InverseLerp(
                    returnStart,
                    motionEnd,
                    distance));
        }

        private float ResolveTrackRegionPadding()
        {
            float pathLength = 0f;
            for (int i = 1; i < mappedPath.Count; i++)
            {
                Vector3 segment =
                    mappedPath[i] - mappedPath[i - 1];
                segment.y = 0f;
                pathLength += segment.magnitude;
            }

            float relativePadding = Mathf.Max(
                MinimumTrackRegionPadding,
                pathLength * RelativeTrackRegionPadding);
            return Mathf.Min(
                trackRegionPadding,
                relativePadding);
        }

        private void ExtendRoadEnd(Vector3[] localPath)
        {
            if (localPath == null || localPath.Length < 2 || roadEndPadding <= 0f)
                return;

            int last = localPath.Length - 1;
            Vector3 direction = localPath[last] - localPath[last - 1];
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.000001f)
                return;

            localPath[last] += direction.normalized * roadEndPadding;
        }

        private LineRenderer CreateEdge(
            string name,
            Vector3[] roadVertices,
            int side,
            Material material)
        {
            GameObject edge = new GameObject(name, typeof(LineRenderer));
            edge.transform.SetParent(stageRoot.transform, false);
            LineRenderer line = edge.GetComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = mappedPath.Count;
            line.widthMultiplier = roadWidth * 0.06f;
            line.numCapVertices = 2;
            line.sharedMaterial = material;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;

            for (int i = 0; i < mappedPath.Count; i++)
                line.SetPosition(i, roadVertices[i * 2 + side] + Vector3.up * 0.001f);

            return line;
        }

        private void ConfigureStageInteraction(Bounds bounds)
        {
            BoxCollider collider = stageRoot.AddComponent<BoxCollider>();
            stageInteractionCollider = collider;
            bounds.Expand(new Vector3(0f, 0.04f, 0f));
            collider.center = bounds.center;
            collider.size = bounds.size;

            Rigidbody body = stageRoot.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            XRGrabInteractable grab = stageRoot.AddComponent<XRGrabInteractable>();
            if (!grab.colliders.Contains(collider))
                grab.colliders.Add(collider);

            grab.useDynamicAttach = true;
            grab.matchAttachPosition = true;
            grab.matchAttachRotation = false;
            grab.trackRotation = false;
            grab.snapToColliderVolume = false;
            grab.attachEaseInTime = 0f;
            grab.throwOnDetach = false;

            stageRoot.AddComponent<WorldGrabTarget>();
            WorldGrabPolicy policy = stageRoot.AddComponent<WorldGrabPolicy>();
            policy.UseGrabPoint(grab, stageRoot.transform);
            stageRoot.AddComponent<ScaleController>();
        }

        private void ApplyCars()
        {
            if (!isActive || eventCars == null)
                return;

            float replayTime = timeline.CurrentTime;
            eventCars.Show(
                eventSamples,
                eventIndices,
                replayTime,
                null,
                eventDrivers);
            UpdateOvertakeCompletion(replayTime);
            eventCars.UpdateOvertakeCompletionVfx(
                replayTime);
        }

        private void UpdateOvertakeCompletion(
            float replayTime)
        {
            int[] drivers = motionEvent != null
                ? motionEvent.driverNumbers
                : null;
            if (drivers == null ||
                drivers.Length < 2 ||
                !TryGetSourceLongitudinalAtTime(
                    drivers[0],
                    replayTime,
                    out float overtakerProgress) ||
                !TryGetSourceLongitudinalAtTime(
                    drivers[1],
                    replayTime,
                    out float defenderProgress) ||
                !eventCars.TryGetVisualLength(
                    drivers[0],
                    out float overtakerLength) ||
                !eventCars.TryGetVisualLength(
                    drivers[1],
                    out float defenderLength))
            {
                return;
            }

            float clearanceDistance =
                overtakerProgress -
                defenderProgress -
                (overtakerLength + defenderLength) *
                0.5f;
            float centerLeadDistance =
                overtakerProgress -
                defenderProgress;
            float referenceVehicleLength =
                Mathf.Max(
                    overtakerLength,
                    defenderLength);
            bool orderingConfirmed =
                currentEvent != null &&
                replayTime >= currentEvent.anchorTime;
            OvertakeCompletionResult result =
                completionDetector.Update(
                    replayTime,
                    clearanceDistance,
                    centerLeadDistance,
                    referenceVehicleLength,
                    orderingConfirmed);
            if (result == OvertakeCompletionResult.Reset ||
                result == OvertakeCompletionResult.Suppressed)
            {
                eventCars.ResetOvertakeCompletionVfx();
            }
            else if (
                result ==
                OvertakeCompletionResult.Triggered)
            {
                eventCars.TriggerOvertakeCompletionVfx(
                    drivers[0],
                    replayTime);
            }
        }

        private void ResetIndices()
        {
            if (eventDrivers.Count == 0)
                return;

            foreach (int driver in eventDrivers)
                eventIndices[driver] = 0;
        }

        private List<LocationSample> FindReferenceSamples()
        {
            if (referenceDriverNumber > 0 &&
                eventSamples.TryGetValue(referenceDriverNumber, out List<LocationSample> reference))
                return reference;

            foreach (int driver in eventDrivers)
            {
                if (eventSamples.TryGetValue(driver, out List<LocationSample> samples))
                    return samples;
            }

            return null;
        }

        private void DestroyStage(
            bool restoreTableTrack = true)
        {
            if (restoreTableTrack)
                RestoreTableTrackRendering();
            isLoading = false;
            isActive = false;
            timeline.Pause();
            eventAudio?.Clear();
            completionDetector.Reset();
            eventCars?.Clear();
            eventAudio = null;
            eventCars = null;
            showcaseAudioFocusDriver = 0;
            showcasePlaybackSpeedMultiplier = 1f;

            if (stageRoot != null)
                Destroy(stageRoot);
            trackSegment?.Clear();
            if (roadMesh != null)
                Destroy(roadMesh);
            if (roadMaterial != null)
                Destroy(roadMaterial);
            if (edgeMaterial != null)
                Destroy(edgeMaterial);
            if (safetyApronMesh != null)
                Destroy(safetyApronMesh);
            if (safetyApronMaterial != null)
                Destroy(safetyApronMaterial);

            stageRoot = null;
            stageInteractionCollider = null;
            trackSegment = null;
            roadMesh = null;
            leftRoadEdge = null;
            rightRoadEdge = null;
            roadMaterial = null;
            edgeMaterial = null;
            safetyApronMesh = null;
            safetyApronMaterial = null;
            currentEvent = null;
            motionEvent = null;
            eventOvertakeSettings = null;
            eventSamples.Clear();
            eventIndices.Clear();
            eventDrivers.Clear();
            referenceDriverNumber = 0;
            mappedPath.Clear();
            mappedPathDistances.Clear();
            fallbackCorridorPath.Clear();
            safetyApronPath.Clear();
            safetyApronLeftEdge.Clear();
            safetyApronRightEdge.Clear();
            drivableLeftEdge.Clear();
            drivableRightEdge.Clear();
            eventLongitudinals.Clear();
            eventSpaceCenter = Vector3.zero;
            sourceToEventRotation = Quaternion.identity;
            sourceGeometryRevision++;
        }

        private void RestoreReplay()
        {
            if (!hasSnapshot || player == null)
                return;

            player.SetSpeed(snapshot.Speed);
            player.Seek(snapshot.Time);
            player.SetSelectedDriver(snapshot.SelectedDriver);
            if (snapshot.WasPlaying)
                player.Play();
            else
                player.Pause();

            hasSnapshot = false;
        }

        private ReplayEventDto CreatePresentationEvent(ReplayEventDto source)
        {
            if (source == null || player == null)
                return source;

            float anchor = Mathf.Clamp(
                source.anchorTime,
                player.TimelineStartTime,
                player.ReadyUntilTime);
            float start = Mathf.Max(
                player.TimelineStartTime,
                anchor - Mathf.Max(0f, eventLeadSeconds));
            float movingStart =
                player.RaceStartTime +
                Mathf.Max(0f, raceStartMotionGraceSeconds);
            if (movingStart > player.TimelineStartTime &&
                anchor > player.RaceStartTime)
            {
                start = Mathf.Min(
                    anchor,
                    Mathf.Max(start, movingStart));
            }
            float end = Mathf.Min(
                player.ReadyUntilTime,
                anchor + Mathf.Max(0f, eventTailSeconds));

            return new ReplayEventDto
            {
                eventId = source.eventId,
                eventType = source.eventType,
                anchorTime = anchor,
                startTime = start,
                endTime = end,
                driverNumbers = source.driverNumbers != null
                    ? (int[])source.driverNumbers.Clone()
                    : null,
                progressStart = source.progressStart,
                progressEnd = source.progressEnd,
                confidence = source.confidence,
                displayTitle = source.displayTitle,
                displayDescription = source.displayDescription,
                passingSide = source.passingSide,
                sideSource = source.sideSource,
                sideConfidence = source.sideConfidence,
                motionProfile = source.motionProfile,
                overtakerShare = source.overtakerShare,
                defenderShare = source.defenderShare
            };
        }

        private ReplayEventDto CreateMotionEvent(
            ReplayEventDto source,
            float transitionTime)
        {
            if (source == null)
                return null;

            float motionAnchor = Mathf.Clamp(
                transitionTime,
                source.startTime,
                source.endTime);
            float motionStart = Mathf.Clamp(
                motionAnchor -
                Mathf.Max(0f, overtakeMotionLeadSeconds),
                source.startTime,
                motionAnchor);
            return new ReplayEventDto
            {
                eventId = source.eventId,
                eventType = source.eventType,
                anchorTime = motionAnchor,
                startTime = motionStart,
                endTime = source.endTime,
                driverNumbers = source.driverNumbers != null
                    ? (int[])source.driverNumbers.Clone()
                    : null,
                progressStart = source.progressStart,
                progressEnd = source.progressEnd,
                confidence = source.confidence,
                displayTitle = source.displayTitle,
                displayDescription = source.displayDescription,
                passingSide = source.passingSide,
                sideSource = source.sideSource,
                sideConfidence = source.sideConfidence,
                motionProfile = source.motionProfile,
                overtakerShare = source.overtakerShare,
                defenderShare = source.defenderShare
            };
        }

        private OvertakeMotionSettings CreateEventOvertakeSettings(
            OvertakeMotionSettings source)
        {
            source ??= new OvertakeMotionSettings();
            return new OvertakeMotionSettings
            {
                enableOvertakeVisuals =
                    source.enableOvertakeVisuals,
                targetSeparationInVehicleWidths =
                    Mathf.Max(
                        source.targetSeparationInVehicleWidths,
                        eventMinimumSeparationInVehicleWidths),
                maximumCorrectionInVehicleWidths =
                    source.maximumCorrectionInVehicleWidths,
                maximumOffsetInVehicleWidths =
                    source.maximumOffsetInVehicleWidths,
                overtakerShare = source.overtakerShare,
                defenderShare = source.defenderShare,
                approachPortion = source.approachPortion,
                parallelPortion = source.parallelPortion,
                returnPortion = source.returnPortion,
                maximumVisualYawDegrees =
                    source.maximumVisualYawDegrees,
                overlapBlendMode = source.overlapBlendMode,
                debugOvertakeVisuals =
                    source.debugOvertakeVisuals
            };
        }

        private float ResolveEarliestAutomaticOvertakeAnchor()
        {
            if (player == null ||
                player.RaceStartTime <=
                player.TimelineStartTime + 0.001f)
            {
                return float.NegativeInfinity;
            }

            return player.RaceStartTime +
                Mathf.Max(0f, raceStartMotionGraceSeconds) +
                Mathf.Max(0f, minimumMovingLeadSeconds);
        }

        private bool TryValidate(ReplayEventDto definition, out string error)
        {
            if (player == null || !player.HasDataset)
            {
                error = "dataset is not ready";
                return false;
            }

            if (definition == null)
            {
                error = "definition is null";
                return false;
            }

            if (string.IsNullOrWhiteSpace(definition.eventId))
            {
                error = "eventId is empty";
                return false;
            }

            if (definition.endTime <= definition.startTime)
            {
                error = $"empty time range {definition.startTime:0.00}-{definition.endTime:0.00}";
                return false;
            }

            if (definition.endTime - definition.startTime > maxEventDuration)
            {
                error = $"event duration exceeds {maxEventDuration:0.#} seconds";
                return false;
            }

            if (definition.driverNumbers == null || definition.driverNumbers.Length == 0)
            {
                error = "driverNumbers is empty";
                return false;
            }

            if (definition.startTime < player.TimelineStartTime ||
                definition.endTime > player.ReadyUntilTime)
            {
                error = $"time range is outside ready data {player.TimelineStartTime:0.00}-{player.ReadyUntilTime:0.00}";
                return false;
            }

            error = null;
            return true;
        }

        private ReplayEventDto CreateDevelopmentEvent()
        {
            DatasetManifestDto manifest = player.Manifest;
            if (manifest == null || manifest.drivers == null || manifest.drivers.Length < 2)
                return null;

            float earliest = player.TimelineStartTime;
            float latest = player.ReadyUntilTime;
            if (latest - earliest < 2f)
                return null;

            float anchor = Mathf.Clamp(player.CurrentTime, earliest + 1f, latest - 1f);
            float start = Mathf.Max(earliest, anchor - 6f);
            float end = Mathf.Min(latest, anchor + 4f);

            return new ReplayEventDto
            {
                eventId = $"development_overtake_{manifest.datasetId}",
                eventType = "Overtake",
                anchorTime = anchor,
                startTime = start,
                endTime = end,
                driverNumbers = new[]
                {
                    manifest.drivers[0].driverNumber,
                    manifest.drivers[1].driverNumber
                },
                progressStart = -1f,
                progressEnd = -1f,
                confidence = -1f,
                passingSide = "Unknown",
                sideSource = "DeterministicFallback",
                sideConfidence = 0f,
                motionProfile = "Default",
                displayTitle = "Development Close Battle Test",
                displayDescription = "Development fixture using nearby cars in the current replay window; not an automatically detected overtake."
            };
        }

        private void SelectClosestDevelopmentDrivers(
            ReplayEventDto definition,
            float startTime,
            float endTime)
        {
            Dictionary<int, List<LocationSample>> source = player.LocationsByDriver;
            if (source == null || source.Count < 2)
                return;

            List<DriverPoint> points = new(source.Count);
            float bestDistance = float.MaxValue;
            float bestTime = definition.anchorTime;
            int bestDriverA = 0;
            int bestDriverB = 0;

            for (float time = startTime; time <= endTime + 0.001f; time += 0.5f)
            {
                points.Clear();
                foreach (KeyValuePair<int, List<LocationSample>> pair in source)
                {
                    if (TryGetSourcePosition(pair.Value, time, out Vector2 position))
                        points.Add(new DriverPoint(pair.Key, position));
                }

                for (int i = 0; i < points.Count - 1; i++)
                {
                    for (int j = i + 1; j < points.Count; j++)
                    {
                        float distance = Vector2.SqrMagnitude(
                            points[i].Position - points[j].Position);
                        if (distance >= bestDistance)
                            continue;

                        bestDistance = distance;
                        bestTime = time;
                        bestDriverA = points[i].Driver;
                        bestDriverB = points[j].Driver;
                    }
                }
            }

            if (bestDriverA <= 0 || bestDriverB <= 0)
                return;

            definition.driverNumbers = new[] { bestDriverA, bestDriverB };
            definition.anchorTime = bestTime;
            definition.displayDescription =
                $"Development fixture showing nearby drivers {bestDriverA} and {bestDriverB}; " +
                "not an automatically detected overtake.";

            Debug.Log(
                $"[EventReplay] Development fixture selected closest drivers " +
                $"{bestDriverA} and {bestDriverB} at {bestTime:0.00}.",
                this);
        }

        private static bool TryGetSourcePosition(
            List<LocationSample> samples,
            float time,
            out Vector2 position)
        {
            position = default;
            if (!TryGetSamplePair(samples, time, out LocationSample a, out LocationSample b))
                return false;

            float duration = Mathf.Max(0.001f, b.t - a.t);
            float interpolation = Mathf.Clamp01((time - a.t) / duration);
            position = Vector2.Lerp(
                new Vector2(a.x, a.y),
                new Vector2(b.x, b.y),
                interpolation);
            return true;
        }

        private static bool TryGetSamplePair(
            List<LocationSample> samples,
            float time,
            out LocationSample a,
            out LocationSample b)
        {
            a = null;
            b = null;
            if (samples == null || samples.Count < 2 ||
                time < samples[0].t || time > samples[samples.Count - 1].t)
                return false;

            int low = 0;
            int high = samples.Count - 1;
            while (low + 1 < high)
            {
                int middle = (low + high) / 2;
                if (samples[middle].t <= time)
                    low = middle;
                else
                    high = middle;
            }

            a = samples[low];
            b = samples[high];
            return b.t > a.t;
        }

        private static bool IsDevelopmentEvent(ReplayEventDto definition)
        {
            return definition != null &&
                !string.IsNullOrEmpty(definition.eventId) &&
                definition.eventId.StartsWith(
                    "development_",
                    StringComparison.OrdinalIgnoreCase);
        }

        private static ReplayEventDto FindClosestOvertake(
            ReplayEventDto[] events,
            float time,
            float minimumAnchorTime =
                float.NegativeInfinity)
        {
            if (events == null)
                return null;

            ReplayEventDto closest = null;
            float closestDistance = float.PositiveInfinity;
            foreach (ReplayEventDto item in events)
            {
                if (item == null ||
                    !string.Equals(item.eventType, "Overtake", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (item.anchorTime < minimumAnchorTime)
                    continue;

                float distance = Mathf.Abs(item.anchorTime - time);
                if (distance < closestDistance ||
                    Mathf.Approximately(distance, closestDistance) &&
                    string.CompareOrdinal(item.eventId, closest?.eventId) < 0)
                {
                    closest = item;
                    closestDistance = distance;
                }
            }

            return closest;
        }

        private static List<LocationSample> CopyRange(
            List<LocationSample> source,
            float startTime,
            float endTime)
        {
            List<LocationSample> result = new();
            LocationSample before = null;

            foreach (LocationSample sample in source)
            {
                if (sample.t < startTime)
                {
                    before = sample;
                    continue;
                }

                if (before != null)
                {
                    result.Add(before);
                    before = null;
                }

                result.Add(sample);
                if (sample.t > endTime)
                    break;
            }

            return result;
        }

        private static Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            Material material = new Material(shader)
            {
                color = color
            };
            return material;
        }

        private void OnDestroy()
        {
            if (openRoutine != null)
                StopCoroutine(openRoutine);

            DestroyStage();
            RestoreReplay();
        }

        private readonly struct ReplaySnapshot
        {
            public readonly float Time;
            public readonly float Speed;
            public readonly int SelectedDriver;
            public readonly bool WasPlaying;

            public ReplaySnapshot(ReplayPlayer source)
            {
                Time = source.CurrentTime;
                Speed = source.playbackSpeed;
                SelectedDriver = source.SelectedDriverNumber;
                WasPlaying = source.IsPlaying;
            }
        }

        private readonly struct TableTrackRendererState
        {
            private readonly Renderer renderer;
            private readonly bool forceRenderingOff;
            private readonly ShadowCastingMode shadowCastingMode;
            private readonly bool receiveShadows;

            public TableTrackRendererState(Renderer renderer)
            {
                this.renderer = renderer;
                forceRenderingOff =
                    renderer.forceRenderingOff;
                shadowCastingMode = renderer.shadowCastingMode;
                receiveShadows = renderer.receiveShadows;
            }

            public void Restore()
            {
                if (renderer == null)
                    return;

                renderer.shadowCastingMode = shadowCastingMode;
                renderer.receiveShadows = receiveShadows;
                renderer.forceRenderingOff =
                    forceRenderingOff;
            }
        }

        private readonly struct DriverPoint
        {
            public readonly int Driver;
            public readonly Vector2 Position;

            public DriverPoint(int driver, Vector2 position)
            {
                Driver = driver;
                Position = position;
            }
        }
    }

    public static class ReplayEventFixtures
    {
        public static ReplayEventDto[] Load(DatasetManifestDto manifest)
        {
            if (manifest == null || manifest.sessionKey <= 0)
                return null;

            TextAsset asset = Resources.Load<TextAsset>(
                $"ReplayEvents/{manifest.sessionKey}");
            if (asset == null)
                return null;

            ReplayEventFixtureDto fixture;
            try
            {
                fixture = JsonUtility.FromJson<ReplayEventFixtureDto>(asset.text);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[EventReplay] Failed to parse fixture '{asset.name}': {exception.Message}");
                return null;
            }

            if (fixture == null || fixture.sessionKey != manifest.sessionKey)
            {
                Debug.LogWarning(
                    $"[EventReplay] Fixture session mismatch for '{asset.name}'.");
                return null;
            }

            return fixture.events;
        }

        [Serializable]
        private sealed class ReplayEventFixtureDto
        {
            public int sessionKey;
            public ReplayEventDto[] events;
        }
    }
}
