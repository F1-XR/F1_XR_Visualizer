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
        [Min(0f)] public float roadEndPadding;
        [Min(0f)] public float trackPaddingSeconds = 1.5f;
        [Min(3)] public int maxTrackPoints = 192;
        [Min(1f)] public float maxEventDuration = 30f;
        [Min(0.01f)] public float eventPlaybackSpeed = 1f;
        [Min(0f)] public float eventLeadSeconds = 6f;
        [Min(0f)] public float eventTailSeconds = 5f;

        private readonly ReplayTimeline timeline = new();
        private readonly Dictionary<int, List<LocationSample>> eventSamples = new();
        private readonly Dictionary<int, int> eventIndices = new();
        private readonly HashSet<int> eventDrivers = new();
        private readonly List<Vector3> mappedPath = new();
        private readonly List<float> mappedPathDistances = new();
        private readonly Dictionary<int, List<float>> eventLongitudinals = new();

        private ReplayPlayer player;
        private ReplayCarSet eventCars;
        private ReplayAudio eventAudio;
        private ReplayEventDto currentEvent;
        private GameObject stageRoot;
        private EventTrackSegment trackSegment;
        private Mesh roadMesh;
        private Material roadMaterial;
        private Material edgeMaterial;
        private Coroutine openRoutine;
        private ReplaySnapshot snapshot;
        private int referenceDriverNumber;
        private bool hasSnapshot;
        private bool isLoading;
        private bool isActive;

        public bool IsLoading => isLoading;
        public bool IsActive => isActive;
        public bool IsPlaying => isActive && timeline.IsPlaying;
        public float CurrentTime => isActive ? timeline.CurrentTime : 0f;
        public float StartTime => isActive ? timeline.StartTime : 0f;
        public float EndTime => isActive ? timeline.RaceEndTime : 0f;
        public float NormalizedTime => isActive
            ? timeline.ToNormalized(timeline.CurrentTime)
            : 0f;
        public ReplayEventDto CurrentEvent => currentEvent;
        public Transform PresentationRoot => stageRoot != null
            ? stageRoot.transform
            : null;

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

            ReplayEventDto definition = FindClosestOvertake(
                player.Events,
                player.CurrentTime);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (definition == null)
                definition = FindClosestOvertake(
                    ReplayEventFixtures.Load(player.Manifest),
                    player.CurrentTime);

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

            DestroyStage();
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

            timeline.Reset(definition.startTime, definition.endTime);
            isLoading = false;
            isActive = true;
            openRoutine = null;
            ApplyCars();
            Play();
        }

        private void Update()
        {
            if (!isActive)
                return;

            if (timeline.IsPlaying)
            {
                timeline.Advance(Time.deltaTime, eventPlaybackSpeed);
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
            eventCars.SetMapScaleRatio(
                player.GetTrackMapScaleRatio());
            eventCars.SetTeamPrefabs(player.teamCarPrefabs);
            eventCars.SetCalibration(player.trackCalibration, false);
            eventCars.SetLabelsVisible(true);
            eventCars.SetLeaderHighlightVisible(false);
            eventCars.SetDrivers(player.Manifest != null ? player.Manifest.drivers : null);
            eventCars.SetOvertakeSettings(player.overtakeMotion);
            eventCars.SetReplayEvents(new[] { definition });

            List<LocationSample> referenceSamples = FindReferenceSamples();
            if (!BuildMappedPath(
                    referenceSamples,
                    trackStartTime,
                    trackEndTime))
                return false;
            if (!BuildEventLongitudinals())
                return false;

            GetPathFrame(out Vector3 center, out Quaternion sourceToLocalRotation);
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
                for (int i = 0; i < samples.Count; i++)
                {
                    if (!eventCars.TryGetMappedPosition(
                            samples[i],
                            out Vector3 position))
                    {
                        eventLongitudinals.Clear();
                        return false;
                    }

                    longitudinals.Add(ProjectSourcePathDistance(position));
                }

                eventLongitudinals.Add(pair.Key, longitudinals);
            }

            return eventLongitudinals.Count > 0;
        }

        private float ProjectSourcePathDistance(Vector3 position)
        {
            float closestSqrDistance = float.PositiveInfinity;
            float closestPathDistance = 0f;

            for (int i = 0; i < mappedPath.Count - 1; i++)
            {
                Vector3 start = mappedPath[i];
                Vector3 segment = mappedPath[i + 1] - start;
                float segmentSqrLength = segment.sqrMagnitude;
                if (segmentSqrLength <= 0.000001f)
                    continue;

                float interpolation = Mathf.Clamp01(
                    Vector3.Dot(position - start, segment) /
                    segmentSqrLength);
                Vector3 projected = start + segment * interpolation;
                float sqrDistance =
                    Vector3.SqrMagnitude(position - projected);
                if (sqrDistance >= closestSqrDistance)
                    continue;

                closestSqrDistance = sqrDistance;
                closestPathDistance =
                    mappedPathDistances[i] +
                    Mathf.Sqrt(segmentSqrLength) * interpolation;
            }

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
            CreateEdge("LeftEdge", vertices, 0, edgeMaterial);
            CreateEdge("RightEdge", vertices, 1, edgeMaterial);

        }

        private bool CreateActualTrackRegion(
            Vector3 center,
            Quaternion sourceToLocalRotation,
            out Bounds stageBounds)
        {
            trackSegment = new EventTrackSegment();
            bool created = trackSegment.Build(
                stageRoot.transform,
                player.GetTrackPlacementTransform(),
                mappedPath,
                center,
                sourceToLocalRotation,
                trackRegionPadding,
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

        private void CreateEdge(
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
        }

        private void ConfigureStageInteraction(Bounds bounds)
        {
            BoxCollider collider = stageRoot.AddComponent<BoxCollider>();
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

            stageRoot.AddComponent<WorldGrabTarget>();
            WorldGrabPolicy policy = stageRoot.AddComponent<WorldGrabPolicy>();
            policy.UseGrabPoint(grab, stageRoot.transform);
            stageRoot.AddComponent<ScaleController>();
        }

        private void ApplyCars()
        {
            if (!isActive || eventCars == null)
                return;

            eventCars.Show(
                eventSamples,
                eventIndices,
                timeline.CurrentTime,
                null,
                eventDrivers);
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

        private void DestroyStage()
        {
            isLoading = false;
            isActive = false;
            timeline.Pause();
            eventAudio?.Clear();
            eventCars?.Clear();
            eventAudio = null;
            eventCars = null;

            if (stageRoot != null)
                Destroy(stageRoot);
            trackSegment?.Clear();
            if (roadMesh != null)
                Destroy(roadMesh);
            if (roadMaterial != null)
                Destroy(roadMaterial);
            if (edgeMaterial != null)
                Destroy(edgeMaterial);

            stageRoot = null;
            trackSegment = null;
            roadMesh = null;
            roadMaterial = null;
            edgeMaterial = null;
            currentEvent = null;
            eventSamples.Clear();
            eventIndices.Clear();
            eventDrivers.Clear();
            referenceDriverNumber = 0;
            mappedPath.Clear();
            mappedPathDistances.Clear();
            eventLongitudinals.Clear();
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
            float time)
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
