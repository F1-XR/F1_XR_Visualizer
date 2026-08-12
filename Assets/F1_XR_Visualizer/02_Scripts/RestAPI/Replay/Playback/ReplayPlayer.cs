using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;
using TMPro;
using F1XR.RestAPI.Api;
using F1XR.RestAPI.Utility;
using F1XR.RestAPI.Replay.Playback;
using F1XR.RestAPI.Replay.Track;
using F1XR.RestAPI.Replay.Track.Placement;
using F1XR.RestAPI.Replay.Track.Build;

namespace F1XR.RestAPI.Replay
{
    public class ReplayPlayer : MonoBehaviour
    {
        private static readonly ProfilerMarker AudioStateMarker =
            new("F1XR.Replay.AudioState");
        private static readonly ProfilerMarker ShowCarsMarker =
            new("F1XR.Replay.ShowCars");
        private static readonly ProfilerMarker LoadChunksMarker =
            new("F1XR.Replay.LoadChunks");
        private static readonly ProfilerMarker StartingLightsMarker =
            new("F1XR.Replay.StartingLights");
        private static readonly ProfilerMarker GridStartMarker =
            new("F1XR.Replay.GridStart");
        private const float TrackAlignmentSampleSeconds = 180f;

        public ApiClient api;
        public GameObject carPrefab;
        public TMP_FontAsset carLabelFont;
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
        [Header("Red Flag Timeline")]
        public bool compressRedFlagDowntime = true;
        [Min(0f)] public float redFlagVisibleTailSeconds = 15f;
        [Min(0f)] public float restartVisibleLeadSeconds = 15f;
        [Min(0f)] public float minimumRedFlagSkipSeconds = 60f;
        public bool showCarLabels = true;
        public bool enableCarLod = true;
        public bool hideLeaderHighlightAfterRaceStart = true;
        public float leaderHighlightDelaySeconds = 10f;
        public CarEngineSoundSettings engineSound = new();
        public OvertakeMotionSettings overtakeMotion = new();
        public OvertakeApproachRibbonSettings overtakeApproachRibbon =
            new();
        public OvertakeSideBySideVfxSettings overtakeSideBySideVfx =
            new();
        public OvertakeCompletionVfxSettings overtakeCompletionVfx =
            new();
        
        public float positionScale = 0.01f;

        private string _datasetId;
        private DatasetManifestDto _manifest;
        private ReplayEventDto[] replayEvents;

        private bool _hasDriverMetadata;
        private ReplayStartingLights _replayStartingLights;

        private Coroutine _seekCoroutine;
        private Coroutine _trackAlignmentCoroutine;
        private bool trackAlignmentReady = true;

        private ReplayChunkLoader replayChunks;
        private ReplayManifestPoller manifestPoller;
        private ReplayCarSet replayCars;
        private ReplayAudio replayAudio;
        private float engineAudioDistanceScale = 1f;
        private EventPopoutReplay eventReplay;
        private int selectedDriverNumber;
        private bool eventPresentationSuppressed;
        private readonly ReplayTimeline timeline = new();
        private readonly ReplayReadyStart readyStart = new();
        private readonly List<Vector3> fallbackOvertakeCenterline = new();
    
        public float CurrentTime => timeline.CurrentTime;
        public float TimelineStartTime => timeline.StartTime;
        public float RaceStartTime => timeline.RaceStartTime;
        public float RaceEndTime => timeline.RaceEndTime;

        public RaceControlEventDto[] YellowFlags => _manifest != null ? _manifest.yellowFlags : null;

        public RaceControlEventDto[] RedFlags => _manifest != null ? _manifest.redFlags : null;

        public ReplayEventDto[] Events => replayEvents;
    
        public bool IsPlaying => timeline.IsPlaying;
        public float Duration => timeline.Duration;
        public float PlaybackElapsedTime => timeline.ElapsedTime;
        public float ReadyUntilTime => timeline.ReadyUntilTime;
        public float TimelineEndTime => timeline.EndTime;
        public IReadOnlyList<ReplayTimelineGap> TimelineGaps => timeline.Gaps;
        public bool HasDataset => _manifest != null;
        public EventPopoutReplay EventReplay => eventReplay;
        internal DatasetManifestDto Manifest => _manifest;
        internal Dictionary<int, List<LocationSample>> LocationsByDriver =>
            replayChunks != null ? replayChunks.LocationsByDriver : null;

        internal bool CopyLocationSourceRange(
            int driverNumber,
            float startTime,
            float endTime,
            List<LocationSample> destination)
        {
            return replayChunks != null &&
                replayChunks.CopyLocationSourceRange(
                    driverNumber,
                    startTime,
                    endTime,
                    destination);
        }
        public int SelectedDriverNumber => selectedDriverNumber;
        public bool IsTrackPlaced => HasPlacedTrack();
        public bool IsTrackPlacementActive => buildPlacer != null && buildPlacer.IsPlacementActive;
        public bool HasValidTrackSurface => buildPlacer != null && buildPlacer.HasValidSurface;
        public bool IsAutomaticTrackPlacement => buildPlacer == null ||
            buildPlacer.PlacementMode != TrackPlacementMode.Free;
        public bool IsTrackEditMode => buildPlacer != null && buildPlacer.IsEditMode;
        public bool CanUndoTrackManipulation => buildPlacer != null && buildPlacer.CanUndo;
        public event Action<int> SelectedDriverChanged;

        public void SetEngineAudioDistanceScale(float value)
        {
            float nextScale = Mathf.Max(0.0001f, value);
            if (Mathf.Approximately(engineAudioDistanceScale, nextScale))
                return;

            engineAudioDistanceScale = nextScale;
            replayAudio?.SetDistanceScale(
                engineAudioDistanceScale,
                engineSound,
                ApplyDriverMetadata);
        }

        public float TimelineToNormalized(float time)
        {
            return timeline.ToNormalized(time);
        }

        public float NormalizedToTimeline(float normalized)
        {
            return timeline.FromNormalized(normalized);
        }

        public float TimelineToPlaybackTime(float sourceTime)
        {
            return timeline.ToElapsedTime(sourceTime);
        }
        
        private void Awake()
        {
            EnsureEngineSound();
            EnsureOvertakeMotion();
            replayChunks = new ReplayChunkLoader(this);
            manifestPoller = new ReplayManifestPoller(this);

            if (placement == null)
                placement = FindAnyObjectByType<ARPlanePlacementController>();

            replayCars = new ReplayCarSet(carPrefab, this);
            replayCars.SetTeamPrefabs(teamCarPrefabs);
            replayCars.SetPlacement(placement);
            if (buildPlacer == null)
                buildPlacer = FindAnyObjectByType<TrackRevealPlacer>();

            replayCars.SetBuildPlacer(buildPlacer);
            replayCars.SetCalibration(trackCalibration);
            replayCars.SetLabelsVisible(showCarLabels);
            replayCars.SetRenderLodEnabled(enableCarLod);
            replayCars.SetLeaderHighlightVisible(false);
            replayCars.SetOvertakeSettings(overtakeMotion);
            replayAudio = new ReplayAudio(replayCars);
            replayAudio.SetDistanceScale(engineAudioDistanceScale);
            replayAudio.Reset(
                engineSound,
                AreReplayCarsReady(),
                ApplyDriverMetadata);

            eventReplay = GetComponent<EventPopoutReplay>();
            if (eventReplay == null)
                eventReplay = gameObject.AddComponent<EventPopoutReplay>();
            eventReplay.Configure(this);
            ConfigureFallbackOvertakeCorridor();
        }
    
        public void LoadDataset(DatasetManifestDto manifest, bool playOnReady = true)
        {
            LoadDataset(manifest, null, playOnReady);
        }

        public void LoadDataset(DatasetManifestDto manifest, TrackOption track, bool playOnReady = true)
        {
            SetEventPresentationSuppressed(false);
            EnsureEngineSound();
            EnsureOvertakeMotion();
            StopTrackAlignment();
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
            ConfigureTimelineCompression();
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
            ConfigureFallbackOvertakeCorridor();
            trackAlignmentReady =
                trackCalibration == null ||
                !trackCalibration.RequiresRuntimeSourceAlignment(
                    manifest.sessionKey);
            replayCars.SetLabelsVisible(showCarLabels);
            replayCars.SetLeaderHighlightVisible(false);
            replayEvents = ResolveReplayEvents(manifest);
            eventReplay?.NotifyDatasetChanged();
            replayCars.SetOvertakeSettings(overtakeMotion);
            replayCars.SetReplayEvents(replayEvents);
            replayAudio ??= new ReplayAudio(replayCars);
            replayAudio.SetDistanceScale(engineAudioDistanceScale);
            replayAudio.Reset(
                engineSound,
                AreReplayCarsReady(),
                ApplyDriverMetadata);

            manifestPoller ??= new ReplayManifestPoller(this);
            manifestPoller.Start(api, _datasetId, manifestPollSeconds, ApplyManifest);

            if (!trackAlignmentReady)
            {
                _trackAlignmentCoroutine =
                    StartCoroutine(PrepareTrackAlignment());
            }
        }

        private void ConfigureFallbackOvertakeCorridor()
        {
            fallbackOvertakeCenterline.Clear();
            if (replayCars == null ||
                eventReplay == null ||
                trackCalibration == null ||
                !trackCalibration.active ||
                trackCalibration.mappingMode !=
                TrackCalibration.MappingMode.Route ||
                trackCalibration.points == null)
            {
                replayCars?.SetFallbackOvertakeCorridor(
                    fallbackOvertakeCenterline,
                    0f,
                    false);
                return;
            }

            float scale =
                trackCalibration.OutputScale;
            for (int i = 0;
                 i < trackCalibration.points.Length;
                 i++)
            {
                Vector3 target =
                    trackCalibration
                        .points[i]
                        .targetLocalPosition;
                fallbackOvertakeCenterline.Add(
                    new Vector3(
                        target.x * scale,
                        target.y * scale +
                        trackCalibration.heightOffset,
                        target.z * scale));
            }

            replayCars.SetFallbackOvertakeCorridor(
                fallbackOvertakeCenterline,
                eventReplay.roadWidth,
                trackCalibration.loopMappingSegments);
        }

        public void Play()
        {
            if (_manifest == null || !trackAlignmentReady)
                return;

            timeline.Play();
            LoadNearChunks();
            replayCars.SetLeaderHighlightVisible(ShouldShowLeaderHighlight());
            replayAudio.SetPlaying(!eventPresentationSuppressed);
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

        internal IEnumerator LoadEventRange(
            float startTime,
            float endTime,
            Action<bool> onComplete)
        {
            if (_manifest == null || replayChunks == null)
            {
                onComplete?.Invoke(false);
                yield break;
            }

            yield return replayChunks.LoadRange(startTime, endTime, onComplete);
        }

        internal Transform GetTrackPlacementTransform()
        {
            if (buildPlacer != null && buildPlacer.HasPlacement)
                return buildPlacer.PlacementTransform;

            return placement != null && placement.HasPlacement
                ? placement.PlacementTransform
                : null;
        }

        internal float GetTrackMapScaleRatio()
        {
            Transform track = GetTrackPlacementTransform();
            if (track == null)
                return 1f;

            TrackMapView mapView =
                track.GetComponent<TrackMapView>();
            return mapView != null
                ? mapView.MapScaleRatio
                : 1f;
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

            ChunkInfoDto chunk = _manifest.chunks[chunkIndex];
            bool loadedSeekRange = false;
            yield return replayChunks.LoadRange(
                chunk.startT - 0.001f,
                chunk.endT + 0.001f,
                loaded => loadedSeekRange = loaded);

            if (!loadedSeekRange)
            {
                _seekCoroutine = null;
                yield break;
            }

            LoadNearChunks();
            if (AreReplayCarsReady())
                ShowReplayCars();
            ApplyStartingLightTimeline();
            ApplyGridStartTimeline();

            _seekCoroutine = null;
        }



        private void Update()
        {
            EnsureEngineSound();
            replayCars.SetLabelsVisible(showCarLabels);
            replayCars.SetRenderLodEnabled(enableCarLod);
            bool trackPlaced = HasPlacedTrack();
            bool replayCarsReady = AreReplayCarsReady();
            AudioStateMarker.Begin();
            replayAudio.Update(
                engineSound,
                replayCarsReady,
                timeline.IsPlaying &&
                !timeline.IsWaitingForGapData &&
                !eventPresentationSuppressed,
                ApplyDriverMetadata);
            AudioStateMarker.End();

            if (trackPlaced)
                TryAutoPlay();

            ApplyStartingLightTimeline();
            ApplyGridStartTimeline();

            if (_manifest == null)
                return;

            if (eventPresentationSuppressed)
                return;

            if (!timeline.IsPlaying)
            {
                if (replayCarsReady && !replayCars.HasCars)
                    ShowReplayCars();

                return;
            }

            timeline.Advance(
                Time.deltaTime,
                playbackSpeed,
                IsTimelineGapTargetLoaded);
            ApplyStartingLightTimeline();
            ApplyGridStartTimeline();

            if (timeline.StopAtEnd())
            {
                replayAudio.SetPlaying(false);
                ApplyGridStartTimeline();
            }

            LoadNearChunks();
            if (replayCarsReady)
                ShowReplayCars();
        }

        private void ShowReplayCars()
        {
            using var marker = ShowCarsMarker.Auto();
            replayCars.SetLeaderHighlightVisible(ShouldShowLeaderHighlight());
            replayCars.SetMapScaleRatio(GetTrackMapScaleRatio());
            replayCars.Show(
                replayChunks.LocationsByDriver,
                replayChunks.LocationIndices,
                timeline.CurrentTime,
                replayChunks.GetPositions(timeline.CurrentTime));
            ApplyGridStartTimeline();
        }

        internal void SetEventPresentationSuppressed(bool suppressed)
        {
            if (eventPresentationSuppressed == suppressed)
                return;

            eventPresentationSuppressed = suppressed;
            replayCars?.SetPresentationVisible(!suppressed);
            if (suppressed)
                replayAudio?.SetPlaying(false);
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
            replayEvents = ResolveReplayEvents(manifest);
            ConfigureTimelineCompression();
            timeline.SetManifest(manifest);
            replayChunks.SetManifest(manifest);
            replayCars.SetReplayEvents(replayEvents);
            ApplyDriverMetadata();
            LoadNearChunks();
            TryAutoPlay();
        }

        private void LoadNearChunks()
        {
            using var marker = LoadChunksMarker.Auto();
            replayChunks.LoadNear(timeline.CurrentTime, preloadChunksAhead, TryAutoPlay);
        }

        private bool IsTimelineGapTargetLoaded(float targetTime)
        {
            int chunkIndex = replayChunks.FindChunk(targetTime);
            if (!replayChunks.CanLoad(chunkIndex))
                return false;

            if (!replayChunks.IsLoaded(chunkIndex) &&
                !replayChunks.IsLoading(chunkIndex))
            {
                replayChunks.StartLoad(chunkIndex, TryAutoPlay);
            }

            return replayChunks.IsLoaded(chunkIndex);
        }

        private void ConfigureTimelineCompression()
        {
            timeline.ConfigureRedFlagCompression(
                compressRedFlagDowntime,
                redFlagVisibleTailSeconds,
                restartVisibleLeadSeconds,
                minimumRedFlagSkipSeconds);
        }

        private void TryAutoPlay()
        {
            if (!trackAlignmentReady)
                return;

            readyStart.TryStart(
                _manifest,
                replayChunks,
                timeline,
                waitForTrackPlacementBeforeStart,
                HasPlacedTrack(),
                TryAutoPlay,
                Play);
        }

        private IEnumerator PrepareTrackAlignment()
        {
            string alignmentDatasetId = _datasetId;
            float startTime =
                _manifest.raceStartT > 0f
                    ? _manifest.raceStartT
                    : _manifest.playbackStartT;
            float availableEnd =
                _manifest.requestedDurationSeconds > 0f
                    ? _manifest.requestedDurationSeconds
                    : _manifest.durationSeconds;
            float endTime =
                Mathf.Min(
                    startTime + TrackAlignmentSampleSeconds,
                    availableEnd);

            while (_datasetId == alignmentDatasetId &&
                timeline.ReadyUntilTime + 0.001f < endTime &&
                _manifest.status != "complete" &&
                _manifest.status != "failed")
            {
                yield return new WaitForSeconds(
                    Mathf.Max(0.1f, manifestPollSeconds));
            }

            if (_datasetId != alignmentDatasetId)
                yield break;

            bool loadedRange = false;
            if (endTime > startTime)
            {
                yield return replayChunks.LoadRange(
                    startTime,
                    endTime,
                    loaded => loadedRange = loaded);
            }

            if (loadedRange &&
                trackCalibration.TryEstimateRuntimeSourceTranslation(
                    replayChunks.LocationsByDriver,
                    startTime,
                    out Vector2 translation,
                    out float rmsError,
                    out int driverNumber))
            {
                Vector2 sessionCorrection =
                    trackCalibration.GetSourceTranslationCorrection(
                        _manifest.sessionKey);
                translation += sessionCorrection;
                trackCalibration.SetRuntimeSourceTranslation(
                    translation);
                Debug.Log(
                    $"[TrackAlignment] dataset={alignmentDatasetId}, " +
                    $"driver={driverNumber}, translation={translation}, " +
                    $"sessionCorrection={sessionCorrection}, " +
                    $"rms={rmsError:0.###}");
            }
            else
            {
                trackCalibration.ResetRuntimeSourceTranslation();
                Debug.LogWarning(
                    $"[TrackAlignment] dataset={alignmentDatasetId}, " +
                    "a complete reference lap was not available. " +
                    "Using the calibration source coordinates unchanged.");
            }

            trackAlignmentReady = true;
            _trackAlignmentCoroutine = null;
            TryAutoPlay();
        }

        private void StopTrackAlignment()
        {
            if (_trackAlignmentCoroutine == null)
                return;

            StopCoroutine(_trackAlignmentCoroutine);
            _trackAlignmentCoroutine = null;
        }

        private void ApplyStartingLightTimeline()
        {
            using var marker = StartingLightsMarker.Auto();
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
            driverNumber = Mathf.Max(0, driverNumber);
            if (selectedDriverNumber == driverNumber)
                return;

            selectedDriverNumber = driverNumber;
            replayCars.SetSelectedDriver(driverNumber);
            SelectedDriverChanged?.Invoke(driverNumber);
        }

        public void ConfirmTrackPlacement()
        {
            buildPlacer?.ConfirmPlacement();
        }

        public void CancelTrackPlacement()
        {
            buildPlacer?.CancelPlacement();
        }

        public void BeginTrackPlacement()
        {
            buildPlacer?.BeginPlacement();
        }

        public void SetAutomaticTrackPlacement(bool automatic)
        {
            buildPlacer?.SetPlacementMode(automatic
                ? TrackPlacementMode.TableAutomatic
                : TrackPlacementMode.Free);
        }

        public void ToggleTrackEditMode()
        {
            buildPlacer?.ToggleEditMode();
        }

        public void UndoTrackManipulation()
        {
            buildPlacer?.UndoManipulation();
        }

        public void ResetTrackPlacement()
        {
            if (buildPlacer == null)
                return;

            SetSelectedDriver(0);
            replayAudio?.ResetPlacement();
            buildPlacer.ResetPlacement();
        }

        public bool TryGetCarTransform(int driverNumber, out Transform carTransform)
        {
            carTransform = null;
            return replayCars != null && replayCars.TryGetCarTransform(driverNumber, out carTransform);
        }

        public bool TryGetVisualCarTransform(int driverNumber, out Transform carTransform)
        {
            carTransform = null;
            return replayCars != null && replayCars.TryGetVisualTransform(driverNumber, out carTransform);
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

            SetSelectedDriver(0);
            replayCars.Clear();
            replayCars.SetLeaderHighlightVisible(false);
            replayAudio.Clear();
        }

        private void EnsureEngineSound()
        {
            engineSound ??= new CarEngineSoundSettings();
        }

        private void EnsureOvertakeMotion()
        {
            overtakeMotion ??= new OvertakeMotionSettings();
            overtakeApproachRibbon ??=
                new OvertakeApproachRibbonSettings();
            overtakeSideBySideVfx ??=
                new OvertakeSideBySideVfxSettings();
        }

        private static ReplayEventDto[] ResolveReplayEvents(DatasetManifestDto manifest)
        {
            if (manifest == null)
                return null;

            ReplayEventDto[] fixtures = ReplayEventFixtures.Load(manifest);
            return ReplayEventMerger.Merge(
                fixtures,
                manifest.events);
        }

        private void ApplyGridStartTimeline()
        {
            using var marker = GridStartMarker.Auto();
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

        private bool AreReplayCarsReady()
        {
            if (buildPlacer != null && buildPlacer.HasPlacement)
            {
                Transform carsTransform = buildPlacer.CarsTransform;
                return carsTransform != null && carsTransform.gameObject.activeInHierarchy;
            }

            return placement != null && placement.HasPlacement;
        }

        private void OnDestroy()
        {
            manifestPoller?.Stop();
            StopTrackAlignment();

            if (_seekCoroutine != null)
                StopCoroutine(_seekCoroutine);
        }
    }

}
