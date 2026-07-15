using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using F1XR.RestAPI.Api;
using F1XR.RestAPI.Utility;
using F1XR.RestAPI.Replay.Playback;
using F1XR.RestAPI.Replay.Track.Placement;
using F1XR.RestAPI.Replay.Track.Build;

namespace F1XR.RestAPI.Replay
{
    public class ReplayPlayer : MonoBehaviour
    {
        public ApiClient api;
        public GameObject carPrefab;
        public TeamCarPrefab[] teamCarPrefabs;
        public ARPlanePlacementController placement;
        public TrackCalibration trackCalibration;
        public GameObject trackVisualizerPrefab;
        public TrackRevealPlacer buildPlacer;
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

        private bool _hasDriverMetadata;
        private ReplayStartingLights _replayStartingLights;

        private Coroutine _seekCoroutine;

        private ReplayChunkLoader replayChunks;
        private ReplayManifestPoller manifestPoller;
        private ReplayCarSet replayCars;
        private ReplayAudio replayAudio;
        private readonly ReplayTimeline timeline = new();
        private readonly ReplayReadyStart readyStart = new();
    
        public float CurrentTime => timeline.CurrentTime;
        public float TimelineStartTime => timeline.StartTime;
        public float RaceStartTime => timeline.RaceStartTime;
        public float RaceEndTime => timeline.RaceEndTime;

        public RaceControlEventDto[] YellowFlags => _manifest != null ? _manifest.yellowFlags : null;

        public RaceControlEventDto[] RedFlags => _manifest != null ? _manifest.redFlags : null;
    
        public bool IsPlaying => timeline.IsPlaying;
        public float Duration => timeline.Duration;
        public float ReadyUntilTime => timeline.ReadyUntilTime;

        public float TimelineToNormalized(float time)
        {
            return timeline.ToNormalized(time);
        }

        public float NormalizedToTimeline(float normalized)
        {
            return timeline.FromNormalized(normalized);
        }
        
        private void Awake()
        {
            EnsureEngineSound();
            replayChunks = new ReplayChunkLoader(this);
            manifestPoller = new ReplayManifestPoller(this);

            if (placement == null)
                placement = FindAnyObjectByType<ARPlanePlacementController>();

            replayCars = new ReplayCarSet(carPrefab);
            replayCars.SetTeamPrefabs(teamCarPrefabs);
            replayCars.SetPlacement(placement);
            if (buildPlacer == null)
                buildPlacer = FindAnyObjectByType<TrackRevealPlacer>();

            replayCars.SetBuildPlacer(buildPlacer);
            replayCars.SetCalibration(trackCalibration);
            replayCars.SetLabelsVisible(showCarLabels);
            replayCars.SetLeaderHighlightVisible(false);
            replayAudio = new ReplayAudio(replayCars);
            replayAudio.Reset(
                engineSound,
                HasPlacedTrack(),
                ApplyDriverMetadata);
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
            TrackAssets.Apply(
                trackAssets,
                track,
                manifest,
                trackVisualizerPrefab,
                ref placement,
                ref buildPlacer,
                ref trackCalibration);
            _datasetId = manifest.datasetId;
            timeline.Reset(manifest);
            readyStart.Reset(playOnReady);
            _hasDriverMetadata = false;
            if (_replayStartingLights == null)
                _replayStartingLights = new ReplayStartingLights(startingLights, startingLightsPrefab);
            else
                _replayStartingLights.Reset(startingLights, startingLightsPrefab);

            replayChunks ??= new ReplayChunkLoader(this);
            replayChunks.Reset(api, _datasetId, _manifest);
            ClearReplay();

            if (placement == null)
                placement = FindAnyObjectByType<ARPlanePlacementController>();

            replayCars.SetPlacement(placement);
            replayCars.SetTeamPrefabs(teamCarPrefabs);
            if (buildPlacer == null)
                buildPlacer = FindAnyObjectByType<TrackRevealPlacer>();

            replayCars.SetBuildPlacer(buildPlacer);
            replayCars.SetCalibration(trackCalibration);
            replayCars.SetLabelsVisible(showCarLabels);
            replayCars.SetLeaderHighlightVisible(false);
            replayAudio ??= new ReplayAudio(replayCars);
            replayAudio.Reset(
                engineSound,
                HasPlacedTrack(),
                ApplyDriverMetadata);

            manifestPoller ??= new ReplayManifestPoller(this);
            manifestPoller.Start(api, _datasetId, manifestPollSeconds, ApplyManifest);
        }

        public void Play()
        {
            if (_manifest == null)
                return;

            timeline.Play();
            LoadNearChunks();
            replayCars.SetLeaderHighlightVisible(ShouldShowLeaderHighlight());
            replayAudio.SetPlaying(true);
            ApplyGridStartTimeline();
        }

        public void Pause()
        {
            timeline.Pause();
            replayAudio.SetPlaying(false);
            ApplyGridStartTimeline();
        }
    
        public void TogglePlay()
        {
            if (timeline.IsPlaying)
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
            float seekTime = timeline.ClampToReady(targetTime);
            int chunkIndex = replayChunks.FindChunk(seekTime);

            if (_manifest.chunks == null || chunkIndex < 0 || chunkIndex >= _manifest.chunks.Length)
                yield break;

            if (!replayChunks.CanLoad(chunkIndex))
                yield break;

            timeline.SetTime(seekTime);
            replayChunks.ResetLocationIndices();

            if (!replayChunks.IsLoaded(chunkIndex))
                yield return replayChunks.Load(chunkIndex, TryAutoPlay);

            LoadNearChunks();
            replayCars.SetLeaderHighlightVisible(ShouldShowLeaderHighlight());
            replayCars.Show(
                replayChunks.LocationsByDriver,
                replayChunks.LocationIndices,
                timeline.CurrentTime,
                replayChunks.GetPositions(timeline.CurrentTime));
            ApplyStartingLightTimeline();
            ApplyGridStartTimeline();

            _seekCoroutine = null;
        }



        private void Update()
        {
            EnsureEngineSound();
            replayCars.SetLabelsVisible(showCarLabels);
            bool trackPlaced = HasPlacedTrack();
            replayAudio.Update(
                engineSound,
                trackPlaced,
                timeline.IsPlaying,
                ApplyDriverMetadata);

            if (trackPlaced)
                TryAutoPlay();

            ApplyStartingLightTimeline();
            ApplyGridStartTimeline();

            if (!timeline.IsPlaying || _manifest == null)
                return;

            timeline.Advance(Time.deltaTime, playbackSpeed);
            ApplyStartingLightTimeline();
            ApplyGridStartTimeline();

            if (timeline.StopAtEnd())
            {
                replayAudio.SetPlaying(false);
                ApplyGridStartTimeline();
            }

            LoadNearChunks();
            replayCars.SetLeaderHighlightVisible(ShouldShowLeaderHighlight());
            replayCars.Show(
                replayChunks.LocationsByDriver,
                replayChunks.LocationIndices,
                timeline.CurrentTime,
                replayChunks.GetPositions(timeline.CurrentTime));
            ApplyGridStartTimeline();
        }

        private bool ShouldShowLeaderHighlight()
        {
            if (_manifest == null)
                return false;

            float showTime = RaceStartTime;
            if (hideLeaderHighlightAfterRaceStart)
                showTime += Mathf.Max(0f, leaderHighlightDelaySeconds);

            return timeline.CurrentTime >= showTime;
        }

        private void ApplyManifest(DatasetManifestDto manifest)
        {
            _manifest = manifest;
            timeline.SetManifest(manifest);
            replayChunks.SetManifest(manifest);
            ApplyDriverMetadata();
            LoadNearChunks();
            TryAutoPlay();
        }

        private void LoadNearChunks()
        {
            replayChunks.LoadNear(timeline.CurrentTime, preloadChunksAhead, TryAutoPlay);
        }

        private void TryAutoPlay()
        {
            readyStart.TryStart(
                _manifest,
                replayChunks,
                timeline,
                waitForTrackPlacementBeforeStart,
                HasPlacedTrack(),
                TryAutoPlay,
                Play);
        }

        private void ApplyStartingLightTimeline()
        {
            if (_manifest == null)
                return;

            if (_replayStartingLights == null)
                _replayStartingLights = new ReplayStartingLights(startingLights, startingLightsPrefab);

            startingLights = _replayStartingLights.ApplyTimeline(
                playStartingLightsBeforeStart,
                HasPlacedTrack(),
                placement,
                buildPlacer,
                timeline.CurrentTime,
                TimelineStartTime,
                RaceStartTime,
                timeline.IsPlaying,
                playbackSpeed,
                startingLightLeadSeconds,
                startingLightFirstDelay,
                startingLightInterval,
                startingLightHideDelay);
        }

        public List<PositionSampleDto> GetPositions()
        {
            return replayChunks.GetPositions(timeline.CurrentTime);
        }

        public TireSampleDto GetTire(int driverNumber)
        {
            return replayChunks.GetTire(driverNumber, timeline.CurrentTime);
        }

        public void SetSelectedDriver(int driverNumber)
        {
            replayCars.SetSelectedDriver(driverNumber);
        }

        public bool TryGetCarTransform(int driverNumber, out Transform carTransform)
        {
            carTransform = null;
            return replayCars != null && replayCars.TryGetCarTransform(driverNumber, out carTransform);
        }

        public string GetDriverLabel(int driverNumber)
        {
            return replayCars.GetDriverLabel(driverNumber);
        }

        public DriverInfoDto GetDriverInfo(int driverNumber)
        {
            return replayCars.GetDriverInfo(driverNumber);
        }

        public Color GetDriverColor(int driverNumber)
        {
            return replayCars.GetDriverColor(driverNumber);
        }

        private void ClearReplay()
        {
            if (_seekCoroutine != null)
            {
                StopCoroutine(_seekCoroutine);
                _seekCoroutine = null;
            }

            replayCars.Clear();
            replayCars.SetLeaderHighlightVisible(false);
            replayAudio.Clear();
        }

        private void EnsureEngineSound()
        {
            engineSound ??= new CarEngineSoundSettings();
        }

        private void ApplyGridStartTimeline()
        {
            if (replayAudio == null || _manifest == null)
                return;

            replayAudio.ApplyGridStart(
                timeline.CurrentTime,
                RaceStartTime,
                timeline.IsPlaying,
                playbackSpeed);
        }

        private void ApplyDriverMetadata()
        {
            if (_hasDriverMetadata || _manifest == null || _manifest.drivers == null || _manifest.drivers.Length == 0)
                return;

            if (replayCars != null)
                replayCars.SetDrivers(_manifest.drivers);

            _hasDriverMetadata = true;
        }

        private bool HasPlacedTrack()
        {
            return buildPlacer != null && buildPlacer.HasPlacement ||
                placement != null && placement.HasPlacement;
        }

        private void OnDestroy()
        {
            manifestPoller?.Stop();

            if (_seekCoroutine != null)
                StopCoroutine(_seekCoroutine);
        }
    }

}
