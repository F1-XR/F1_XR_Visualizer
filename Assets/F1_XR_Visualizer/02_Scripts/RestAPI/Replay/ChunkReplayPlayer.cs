using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.Serialization;
using F1XR.RestAPI.Api;
using F1XR.RestAPI.Utility;
using F1XR.AR;
using F1XR.RestAPI.AR;

namespace F1XR.RestAPI.Replay
{
    public class ChunkReplayPlayer : MonoBehaviour
    {
        public ApiClient api;
        public GameObject carPrefab;
        public TeamCarPrefab[] teamCarPrefabs;
        public ARPlanePlacementController placement;
        public TrackCalibration trackCalibration;
        public GameObject trackVisualizerPrefab;
        public ARBuildRevealPlacer buildPlacer;
        public StartingLightSequence startingLights;
        public GameObject startingLightsPrefab;
        public TrackAsset[] trackAssets;

        public bool playOnReady = true;
        public bool waitForTrackPlacementBeforeStart = true;
        public bool playStartingLightsBeforeStart = true;
        public float startingLightLeadSeconds = 8f;
        public float startingLightFirstDelay = 1f;
        public float startingLightInterval = 1f;
        public float startingLightHideDelay = 2f;
        public float playbackSpeed = 6f;
        public int preloadChunksAhead = 3;
        public float manifestPollSeconds = 1f;
        public bool showCarLabels = true;
        public bool hideLeaderHighlightAfterRaceStart = true;
        public float leaderHighlightDelaySeconds = 10f;
        public CarEngineSoundSettings engineSound = new();
        
        public float positionScale = 0.01f;

        private string _datasetId;
        private DatasetManifestDto _manifest;

        private float _time;
        private bool _isPlaying;
        private bool _playOnReady;
        private bool _hasStarted;
        private bool _hasDriverMetadata;
        private bool _wasTrackPlaced;
        private bool _lastRedBullOnly;
        private bool _lastUseTeamBasedEngineAudio;
        private bool _lastEnableNewGridStartAudio;
        private string _lastTeamNameFilter;
        private EngineAudioProfile _lastRedBullProfile;
        private EngineAudioProfile _lastMercedesProfile;
        private EngineAudioProfile _lastFerrariProfile;
        private AudioClip _lastRedBullGridStartClip;
        private bool _hasStartingLightsRotationOffset;
        private Quaternion _startingLightsRotationOffset = Quaternion.identity;

        private Coroutine _manifestPollingCoroutine;
        private Coroutine _seekCoroutine;

        private readonly HashSet<int> _loadedChunks = new();
        private readonly HashSet<int> _loadingChunks = new();
        
        private readonly ReplaySamples replaySamples = new();
        private readonly ReplayPositions replayPositions = new();
        private readonly ReplayTires replayTires = new();
        private CarReplayView carView;
    
        public float CurrentTime => _time;
        public float TimelineStartTime => _manifest != null && _manifest.chunks != null && _manifest.chunks.Length > 0
            ? _manifest.chunks[0].startT
            : 0f;

        public float RaceStartTime
        {
            get
            {
                if (_manifest == null)
                    return 0f;

                return _manifest.raceStartT > 0f ? _manifest.raceStartT : _manifest.playbackStartT;
            }
        }

        public float RaceEndTime => _manifest != null ? _manifest.raceEndT : 0f;

        public RaceControlEventDto[] YellowFlags => _manifest != null ? _manifest.yellowFlags : null;

        public RaceControlEventDto[] RedFlags => _manifest != null ? _manifest.redFlags : null;
    
        public bool IsPlaying => _isPlaying;

        public float Duration
        {
            get
            {
                if (_manifest == null)
                    return 0f;

                float endTime = _manifest.requestedDurationSeconds > 0f
                    ? _manifest.requestedDurationSeconds
                    : _manifest.durationSeconds;

                return Mathf.Max(0f, endTime - TimelineStartTime);
            }
        }

        public float ReadyUntilTime
        {
            get
            {
                if (_manifest == null)
                    return 0f;

                return _manifest.readyUntilT;
            }
        }

        public float TimelineToNormalized(float time)
        {
            float duration = Duration;
            return duration > 0f ? Mathf.Clamp01((time - TimelineStartTime) / duration) : 0f;
        }

        public float NormalizedToTimeline(float normalized)
        {
            return TimelineStartTime + Mathf.Clamp01(normalized) * Duration;
        }
        
        private void Awake()
        {
            EnsureEngineSound();

            if (placement == null)
                placement = FindAnyObjectByType<ARPlanePlacementController>();

            carView = new CarReplayView(carPrefab);
            carView.SetTeamPrefabs(teamCarPrefabs);
            carView.SetPlacement(placement);
            if (buildPlacer == null)
                buildPlacer = FindAnyObjectByType<ARBuildRevealPlacer>();

            carView.SetBuildPlacer(buildPlacer);
            carView.SetCalibration(trackCalibration);
            carView.SetLabelsVisible(showCarLabels);
            carView.SetLeaderHighlightVisible(false);
            RefreshEngineSound();
            _wasTrackPlaced = HasPlacedTrack();
            carView.SetSoundPlacementReady(_wasTrackPlaced);
        }
    
        public void LoadDataset(DatasetManifestDto manifest, bool playOnReady = true)
        {
            LoadDataset(manifest, null, playOnReady);
        }

        public void LoadDataset(DatasetManifestDto manifest, TrackOption track, bool playOnReady = true)
        {
            EnsureEngineSound();
            ReplayCoordinate.scale = positionScale;
            
            _manifest = manifest;
            ApplyTrack(track, manifest);
            _datasetId = manifest.datasetId;
            _time = manifest.playbackStartT;
            _isPlaying = false;
            _playOnReady = playOnReady;
            _hasStarted = false;
            _hasDriverMetadata = false;
            _wasTrackPlaced = false;
            _hasStartingLightsRotationOffset = false;
            _startingLightsRotationOffset = Quaternion.identity;

            ClearReplay();

            if (placement == null)
                placement = FindAnyObjectByType<ARPlanePlacementController>();

            carView.SetPlacement(placement);
            carView.SetTeamPrefabs(teamCarPrefabs);
            if (buildPlacer == null)
                buildPlacer = FindAnyObjectByType<ARBuildRevealPlacer>();

            carView.SetBuildPlacer(buildPlacer);
            carView.SetCalibration(trackCalibration);
            carView.SetLabelsVisible(showCarLabels);
            carView.SetLeaderHighlightVisible(false);
            RefreshEngineSound();
            _wasTrackPlaced = HasPlacedTrack();
            carView.SetSoundPlacementReady(_wasTrackPlaced);
            carView.SetSoundPlaying(false);

            if (_manifestPollingCoroutine != null)
                StopCoroutine(_manifestPollingCoroutine);

            _manifestPollingCoroutine = StartCoroutine(PollManifestLoop());
        }

        private void ApplyTrack(TrackOption track, DatasetManifestDto manifest)
        {
            if (placement == null)
                placement = FindAnyObjectByType<ARPlanePlacementController>();

            TrackAsset asset = default;
            bool found = false;

            if (trackAssets != null)
            {
                foreach (TrackAsset candidate in trackAssets)
                {
                    if (!candidate.Matches(track, manifest))
                        continue;

                    asset = candidate;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                Debug.LogWarning($"Track asset not found. circuit={manifest?.circuit}, circuitKey={track?.circuitKey}");
                return;
            }

            GameObject visualizerPrefab = asset.visualizerPrefab != null
                ? asset.visualizerPrefab
                : trackVisualizerPrefab;

            if (visualizerPrefab == null && placement != null)
                visualizerPrefab = placement.PlacementPrefab;

            if (asset.calibration != null)
                trackCalibration = asset.calibration;

            bool fitMapToBounds = asset.TryGetMapTargetXZSize(out Vector2 mapTargetXZSize);

            if (visualizerPrefab != null)
            {
                if (placement != null)
                {
                    placement.SetPlacementPrefab(
                        visualizerPrefab,
                        asset.mapPrefab,
                        asset.MapScale,
                        fitMapToBounds,
                        mapTargetXZSize);
                }

                if (buildPlacer == null)
                    buildPlacer = FindAnyObjectByType<ARBuildRevealPlacer>();

                if (buildPlacer != null)
                {
                    buildPlacer.SetPlacementPrefab(
                        visualizerPrefab,
                        asset.mapPrefab,
                        asset.MapScale,
                        fitMapToBounds,
                        mapTargetXZSize);
                }
            }
        }

        public void Play()
        {
            if (_manifest == null)
                return;

            _isPlaying = true;
            LoadNearChunks();
            carView.SetLeaderHighlightVisible(ShouldShowLeaderHighlight());
            carView.SetSoundPlaying(true);
            ApplyGridStartTimeline();
        }

        public void Pause()
        {
            _isPlaying = false;
            carView.SetSoundPlaying(false);
            ApplyGridStartTimeline();
        }
    
        public void TogglePlay()
        {
            if (_isPlaying)
                Pause();
            else
                Play();
        }

        public void SetSpeed(float speed)
        {
            playbackSpeed = Mathf.Max(0.01f, speed);
        }
    
        public void Seek(float targetTime)
        {
            if (_manifest == null)
                return;

            if (_seekCoroutine != null)
                StopCoroutine(_seekCoroutine);

            _seekCoroutine = StartCoroutine(SeekRoutine(targetTime));
        }

        private IEnumerator SeekRoutine(float targetTime)
        {
            float seekTime = Mathf.Clamp(targetTime, TimelineStartTime, ReadyUntilTime);
            int chunkIndex = FindChunk(seekTime);

            if (_manifest.chunks == null || chunkIndex < 0 || chunkIndex >= _manifest.chunks.Length)
                yield break;

            ChunkInfoDto chunk = _manifest.chunks[chunkIndex];

            if (!CanLoad(chunk))
                yield break;

            _time = seekTime;
            replaySamples.ResetIndices();

            if (!_loadedChunks.Contains(chunkIndex))
                yield return LoadChunk(chunkIndex);

            LoadNearChunks();
            carView.SetLeaderHighlightVisible(ShouldShowLeaderHighlight());
            carView.Show(replaySamples.ByDriver, replaySamples.Indices, _time, replayPositions.Get(_time));
            ApplyStartingLightTimeline();
            ApplyGridStartTimeline();

            _seekCoroutine = null;
        }



        private void Update()
        {
            ApplyEngineSoundFilterChange();
            carView.SetLabelsVisible(showCarLabels);
            bool trackPlaced = HasPlacedTrack();
            carView.SetSoundPlacementReady(trackPlaced);
            if (!_wasTrackPlaced && trackPlaced)
                RefreshEngineSound();

            _wasTrackPlaced = trackPlaced;
            carView.SetSoundPlaying(_isPlaying);

            if (trackPlaced)
                TryAutoPlay();

            ApplyStartingLightTimeline();
            ApplyGridStartTimeline();

            if (!_isPlaying || _manifest == null)
                return;

            _time += Time.deltaTime * playbackSpeed;
            ApplyStartingLightTimeline();
            ApplyGridStartTimeline();

            float maxTime = _manifest.requestedDurationSeconds > 0f
                ? _manifest.requestedDurationSeconds
                : _manifest.durationSeconds;

            if (_time > maxTime)
            {
                _time = maxTime;
                _isPlaying = false;
                carView.SetSoundPlaying(false);
                ApplyGridStartTimeline();
            }

            LoadNearChunks();
            carView.SetLeaderHighlightVisible(ShouldShowLeaderHighlight());
            carView.Show(replaySamples.ByDriver, replaySamples.Indices, _time, replayPositions.Get(_time));
            ApplyGridStartTimeline();
        }

        private bool ShouldShowLeaderHighlight()
        {
            if (_manifest == null)
                return false;

            float showTime = RaceStartTime;
            if (hideLeaderHighlightAfterRaceStart)
                showTime += Mathf.Max(0f, leaderHighlightDelaySeconds);

            return _time >= showTime;
        }

        private IEnumerator PollManifestLoop()
        {
            while (!string.IsNullOrEmpty(_datasetId))
            {
                yield return api.GetManifest(
                    _datasetId,
                    manifest =>
                    {
                        _manifest = manifest;
                        ApplyDriverMetadata();
                        LoadNearChunks();
                        TryAutoPlay();
                    },
                    error =>
                    {
                        Debug.LogWarning($"Manifest poll failed: {error}");
                    }
                );

                if (_manifest != null && (_manifest.status == "complete" || _manifest.status == "failed"))
                    break;

                yield return new WaitForSeconds(manifestPollSeconds);
            }

            _manifestPollingCoroutine = null;
        }

        private void LoadNearChunks()
        {
            if (_manifest == null || _manifest.chunks == null || _manifest.chunks.Length == 0)
                return;

            int currentIndex = FindChunk(_time);

            for (int i = currentIndex; i <= currentIndex + preloadChunksAhead; i++)
            {
                if (i < 0 || i >= _manifest.chunks.Length)
                    continue;

                ChunkInfoDto chunk = _manifest.chunks[i];

                if (!CanLoad(chunk))
                    continue;

                if (_loadedChunks.Contains(i) || _loadingChunks.Contains(i))
                    continue;

                StartCoroutine(LoadChunk(i));
            }
        }

        private void TryAutoPlay()
        {
            if (!_playOnReady || _hasStarted || _manifest == null || _manifest.chunks == null)
                return;

            if (waitForTrackPlacementBeforeStart && !HasPlacedTrack())
                return;

            if (_isPlaying)
            {
                _hasStarted = true;
                return;
            }

            int firstIndex = FindReadyStartChunk(_manifest.playbackStartT);

            if (firstIndex < 0)
                return;

            if (!_loadedChunks.Contains(firstIndex))
            {
                if (!_loadingChunks.Contains(firstIndex))
                    StartCoroutine(LoadChunk(firstIndex));

                return;
            }

            ChunkInfoDto startChunk = _manifest.chunks[firstIndex];
            float startTime = _manifest.playbackStartT;
            _time = startTime >= startChunk.startT && startTime <= startChunk.endT
                ? startTime
                : Mathf.Clamp(startTime, TimelineStartTime, ReadyUntilTime);
            _hasStarted = true;
            Play();
        }

        private void ApplyStartingLightTimeline()
        {
            if (!playStartingLightsBeforeStart || _manifest == null)
                return;

            if (!HasPlacedTrack())
                return;

            float showTime = Mathf.Max(TimelineStartTime, RaceStartTime - Mathf.Max(0f, startingLightLeadSeconds));
            float hideTime = RaceStartTime + Mathf.Max(0f, startingLightHideDelay);
            bool inWindow = _time >= showTime && _time < hideTime;

            if (startingLights == null && !inWindow)
                return;

            StartingLightSequence sequence = ResolveStartingLights();
            if (sequence == null)
                return;

            PositionStartingLights(sequence.transform);
            sequence.ApplyTimeline(
                _time,
                RaceStartTime,
                _isPlaying,
                playbackSpeed,
                startingLightLeadSeconds,
                startingLightFirstDelay,
                startingLightInterval,
                startingLightHideDelay);
        }

        private void PositionStartingLights(Transform target)
        {
            if (target == null)
                return;

            Transform mapTransform = buildPlacer != null && buildPlacer.HasPlacement
                ? buildPlacer.PlacementTransform
                : placement != null && placement.HasPlacement
                    ? placement.PlacementTransform
                    : null;

            if (mapTransform == null)
                return;

            Vector3 position = mapTransform.position + Vector3.up * 0.5f;
            target.position = position;

            Camera camera = Camera.main;
            if (camera == null)
                return;

            Vector3 toCamera = camera.transform.position - position;
            toCamera.y = 0f;

            if (toCamera.sqrMagnitude > 0.0001f)
            {
                RememberStartingLightsRotation(target);
                Quaternion lookRotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
                target.rotation = lookRotation * _startingLightsRotationOffset;
            }
        }

        private void RememberStartingLightsRotation(Transform target)
        {
            if (_hasStartingLightsRotationOffset || target == null)
                return;

            _startingLightsRotationOffset = target.rotation;
            _hasStartingLightsRotationOffset = true;
        }

        private StartingLightSequence ResolveStartingLights()
        {
            if (startingLights != null)
                return startingLights;

            StartingLightSequence[] candidates = FindObjectsByType<StartingLightSequence>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            if (candidates == null || candidates.Length == 0)
            {
                if (startingLightsPrefab == null)
                    return null;

                GameObject instance = Instantiate(startingLightsPrefab);
                instance.name = startingLightsPrefab.name;
                startingLights = instance.GetComponent<StartingLightSequence>();
                return startingLights;
            }

            startingLights = candidates[0];
            return startingLights;
        }

        private int FindReadyStartChunk(float startTime)
        {
            int fallbackIndex = -1;
            int firstSampleIndex = -1;
            bool requiresStartChunk = startTime > 0.0f;

            for (int i = 0; i < _manifest.chunks.Length; i++)
            {
                ChunkInfoDto chunk = _manifest.chunks[i];

                if (!CanLoad(chunk))
                    continue;

                if (fallbackIndex < 0)
                    fallbackIndex = i;

                if (firstSampleIndex < 0 && chunk.sampleCount > 0 && chunk.endT >= startTime)
                    firstSampleIndex = i;

                if (startTime >= chunk.startT && startTime <= chunk.endT)
                    return i;
            }

            if (firstSampleIndex >= 0)
                return firstSampleIndex;

            return requiresStartChunk ? -1 : fallbackIndex;
        }

        private IEnumerator LoadChunk(int chunkIndex)
        {
            if (string.IsNullOrEmpty(_datasetId))
                yield break;

            if (_loadedChunks.Contains(chunkIndex) || _loadingChunks.Contains(chunkIndex))
                yield break;

            _loadingChunks.Add(chunkIndex);

            ReplayChunkDto loadedChunk = null;
            string loadError = null;

            yield return api.GetChunk(
                _datasetId,
                chunkIndex,
                chunk => loadedChunk = chunk,
                error => loadError = error
            );

            _loadingChunks.Remove(chunkIndex);

            if (!string.IsNullOrEmpty(loadError))
            {
                Debug.LogWarning($"Chunk {chunkIndex} load failed: {loadError}");
                yield break;
            }

            if (loadedChunk == null)
                yield break;

            if (loadedChunk.samples != null && loadedChunk.samples.Length > 0)
            {
                replaySamples.Add(loadedChunk);
                replayPositions.Add(loadedChunk);
                replayTires.Add(loadedChunk);
                Debug.Log($"Loaded chunk {loadedChunk.chunkIndex}, samples={loadedChunk.samples.Length}");
            }

            _loadedChunks.Add(chunkIndex);

            TryAutoPlay();
        }

        private bool CanLoad(ChunkInfoDto chunk)
        {
            return chunk.status == "ready" || chunk.status == "empty";
        }

        private int FindChunk(float time)
        {
            if (_manifest == null || _manifest.chunks == null || _manifest.chunks.Length == 0)
                return 0;

            for (int i = 0; i < _manifest.chunks.Length; i++)
            {
                ChunkInfoDto chunk = _manifest.chunks[i];

                if (time >= chunk.startT && time <= chunk.endT)
                    return i;
            }

            if (time < _manifest.chunks[0].startT)
                return 0;

            return _manifest.chunks.Length - 1;
        }
        
        public List<PositionSampleDto> GetPositions()
        {
            return replayPositions.Get(_time);
        }

        public TireSampleDto GetTire(int driverNumber)
        {
            return replayTires.Get(driverNumber, _time);
        }

        public void SetSelectedDriver(int driverNumber)
        {
            carView.SetSelectedDriver(driverNumber);
        }

        public bool TryGetCarTransform(int driverNumber, out Transform carTransform)
        {
            carTransform = null;
            return carView != null && carView.TryGetCarTransform(driverNumber, out carTransform);
        }

        public string GetDriverLabel(int driverNumber)
        {
            if (_manifest == null || _manifest.drivers == null)
                return $"#{driverNumber}";

            foreach (DriverInfoDto driver in _manifest.drivers)
            {
                if (driver.driverNumber != driverNumber)
                    continue;

                return string.IsNullOrWhiteSpace(driver.nameAcronym)
                    ? $"#{driverNumber}"
                    : driver.nameAcronym;
            }

            return $"#{driverNumber}";
        }

        public DriverInfoDto GetDriverInfo(int driverNumber)
        {
            if (_manifest == null || _manifest.drivers == null)
                return null;

            foreach (DriverInfoDto driver in _manifest.drivers)
            {
                if (driver.driverNumber == driverNumber)
                    return driver;
            }

            return null;
        }

        public Color GetDriverColor(int driverNumber)
        {
            if (_manifest == null || _manifest.drivers == null)
                return new Color(0.25f, 0.28f, 0.34f);

            foreach (DriverInfoDto driver in _manifest.drivers)
            {
                if (driver.driverNumber != driverNumber)
                    continue;

                if (!string.IsNullOrWhiteSpace(driver.teamColour) &&
                    ColorUtility.TryParseHtmlString("#" + driver.teamColour, out Color color))
                    return color;

                break;
            }

            return new Color(0.25f, 0.28f, 0.34f);
        }

        private void ClearReplay()
        {
            if (_seekCoroutine != null)
            {
                StopCoroutine(_seekCoroutine);
                _seekCoroutine = null;
            }

            replaySamples.Clear();
            _loadedChunks.Clear();
            _loadingChunks.Clear();
            carView.Clear();
            replayPositions.Clear();
            replayTires.Clear();
            carView.SetLeaderHighlightVisible(false);
            carView.SetSoundPlaying(false);
            carView.StopGridStartAudio();
        }

        private void EnsureEngineSound()
        {
            engineSound ??= new CarEngineSoundSettings();
        }

        private void RememberEngineSoundFilter()
        {
            _lastRedBullOnly = engineSound != null && engineSound.redBullOnly;
            _lastUseTeamBasedEngineAudio = engineSound != null && engineSound.useTeamBasedEngineAudio;
            _lastEnableNewGridStartAudio = engineSound != null && engineSound.enableNewGridStartAudio;
            _lastTeamNameFilter = engineSound != null ? engineSound.teamNameFilter : null;
            _lastRedBullProfile = engineSound != null ? engineSound.redBullProfile : null;
            _lastMercedesProfile = engineSound != null ? engineSound.mercedesProfile : null;
            _lastFerrariProfile = engineSound != null ? engineSound.ferrariProfile : null;
            _lastRedBullGridStartClip = engineSound != null ? engineSound.redBullGridStartClip : null;
        }

        private void ApplyEngineSoundFilterChange()
        {
            EnsureEngineSound();

            bool modeChanged = _lastUseTeamBasedEngineAudio != engineSound.useTeamBasedEngineAudio;
            if (_lastRedBullOnly == engineSound.redBullOnly &&
                _lastUseTeamBasedEngineAudio == engineSound.useTeamBasedEngineAudio &&
                _lastEnableNewGridStartAudio == engineSound.enableNewGridStartAudio &&
                _lastRedBullProfile == engineSound.redBullProfile &&
                _lastMercedesProfile == engineSound.mercedesProfile &&
                _lastFerrariProfile == engineSound.ferrariProfile &&
                _lastRedBullGridStartClip == engineSound.redBullGridStartClip &&
                string.Equals(_lastTeamNameFilter, engineSound.teamNameFilter, StringComparison.Ordinal))
            {
                return;
            }

            if (modeChanged)
                Debug.Log(engineSound.useTeamBasedEngineAudio ? "[EngineAudio] Mode changed: TeamBased" : "[EngineAudio] Mode changed: Legacy");

            carView.StopGridStartAudio();
            RefreshEngineSound();
        }

        private void RefreshEngineSound()
        {
            ApplyDriverMetadata();
            carView.SetEngineSound(engineSound);
            RememberEngineSoundFilter();
        }

        private void ApplyGridStartTimeline()
        {
            if (carView == null || _manifest == null)
                return;

            carView.ApplyGridStartTimeline(_time, RaceStartTime, _isPlaying, playbackSpeed);
        }

        private void ApplyDriverMetadata()
        {
            if (_hasDriverMetadata || _manifest == null || _manifest.drivers == null || _manifest.drivers.Length == 0)
                return;

            if (carView != null)
                carView.SetDrivers(_manifest.drivers);

            _hasDriverMetadata = true;
        }

        private bool HasPlacedTrack()
        {
            return buildPlacer != null && buildPlacer.HasPlacement ||
                placement != null && placement.HasPlacement;
        }

        private void OnDestroy()
        {
            if (_manifestPollingCoroutine != null)
                StopCoroutine(_manifestPollingCoroutine);

            if (_seekCoroutine != null)
                StopCoroutine(_seekCoroutine);
        }
    }

    [Serializable]
    public struct TeamCarPrefab
    {
        public string teamName;
        public GameObject prefab;
    }

    [Serializable]
    public struct TrackAsset
    {
        public int circuitKey;
        public string circuitName;
        public string circuitShortName;
        [FormerlySerializedAs("prefab")]
        public GameObject visualizerPrefab;
        public GameObject mapPrefab;
        public float mapScale;
        public bool fitMapToCalibrationBounds;
        public float mapFitScaleMultiplier;
        public TrackCalibration calibration;

        public float MapScale => mapScale > 0f ? mapScale : 1f;
        public float MapFitScaleMultiplier => mapFitScaleMultiplier > 0f ? mapFitScaleMultiplier : 1f;

        public bool TryGetMapTargetXZSize(out Vector2 size)
        {
            size = default;

            if (!fitMapToCalibrationBounds || calibration == null || calibration.points == null)
                return false;

            bool found = false;
            Vector2 min = default;
            Vector2 max = default;

            foreach (TrackCalibration.Point point in calibration.points)
            {
                if (string.IsNullOrWhiteSpace(point.name))
                    continue;

                Vector2 position = new Vector2(point.targetLocalPosition.x, point.targetLocalPosition.z);
                if (!found)
                {
                    min = position;
                    max = position;
                    found = true;
                    continue;
                }

                min = Vector2.Min(min, position);
                max = Vector2.Max(max, position);
            }

            if (!found)
                return false;

            size = (max - min) * MapFitScaleMultiplier;
            return size.x > 0.0001f && size.y > 0.0001f;
        }

        public bool Matches(TrackOption track, DatasetManifestDto manifest)
        {
            if (track != null)
            {
                if (circuitKey > 0 && circuitKey == track.circuitKey)
                    return true;

                if (MatchesName(circuitShortName, track.circuitShortName) ||
                    MatchesName(circuitName, track.meetingName) ||
                    MatchesName(circuitName, track.location))
                {
                    return true;
                }
            }

            return manifest != null &&
                (MatchesName(circuitName, manifest.circuit) ||
                MatchesName(circuitShortName, manifest.circuit));
        }

        static bool MatchesName(string a, string b)
        {
            return !string.IsNullOrWhiteSpace(a) &&
                !string.IsNullOrWhiteSpace(b) &&
                string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
