using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using F1XR.RestAPI.Api;
using F1XR.RestAPI.Utility;
using F1XR.AR;

namespace F1XR.RestAPI.Replay
{
    public class ChunkReplayPlayer : MonoBehaviour
    {
        public ApiClient api;
        public GameObject carPrefab;
        public ARPlanePlacementController placement;

        public bool playOnReady = true;
        public float playbackSpeed = 1f;
        public int preloadChunksAhead = 3;
        public float manifestPollSeconds = 1f;
        
        public float positionScale = 0.01f;

        private string _datasetId;
        private DatasetManifestDto _manifest;

        private float _time;
        private bool _isPlaying;
        private bool _playOnReady;
        private bool _hasStarted;

        private Coroutine _manifestPollingCoroutine;
        private Coroutine _seekCoroutine;

        private readonly HashSet<int> _loadedChunks = new();
        private readonly HashSet<int> _loadingChunks = new();
        
        private readonly ReplaySamples replaySamples = new();
        private readonly ReplayPositions replayPositions = new();
        private readonly ReplayTires replayTires = new();
        private CarReplayView carView;
    
        public float CurrentTime => _time;
    
        public bool IsPlaying => _isPlaying;

        public float Duration
        {
            get
            {
                if (_manifest == null)
                    return 0f;

                return _manifest.requestedDurationSeconds > 0f
                    ? _manifest.requestedDurationSeconds
                    : _manifest.durationSeconds;
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
        
        private void Awake()
        {
            if (placement == null)
                placement = FindFirstObjectByType<ARPlanePlacementController>();

            carView = new CarReplayView(carPrefab);
            carView.SetPlacement(placement);
        }
    
        public void LoadDataset(DatasetManifestDto manifest, bool playOnReady = true)
        {
            CoordinateUtil.scale = positionScale;
            
            _manifest = manifest;
            carView.SetDrivers(manifest.drivers);
            _datasetId = manifest.datasetId;
            _time = manifest.playbackStartT;
            _isPlaying = false;
            _playOnReady = playOnReady;
            _hasStarted = false;

            ClearReplay();

            if (placement == null)
                placement = FindFirstObjectByType<ARPlanePlacementController>();

            carView.SetPlacement(placement);

            if (_manifestPollingCoroutine != null)
                StopCoroutine(_manifestPollingCoroutine);

            _manifestPollingCoroutine = StartCoroutine(PollManifestLoop());
        }

        public void Play()
        {
            if (_manifest == null)
                return;

            _isPlaying = true;
        }

        public void Pause()
        {
            _isPlaying = false;
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
            float seekTime = Mathf.Clamp(targetTime, 0f, ReadyUntilTime);
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
            carView.Show(replaySamples.ByDriver, replaySamples.Indices, _time);

            _seekCoroutine = null;
        }



        private void Update()
        {
            if (!_isPlaying || _manifest == null)
                return;

            _time += Time.deltaTime * playbackSpeed;

            float maxTime = _manifest.requestedDurationSeconds > 0f
                ? _manifest.requestedDurationSeconds
                : _manifest.durationSeconds;

            if (_time > maxTime)
            {
                _time = maxTime;
                _isPlaying = false;
            }

            LoadNearChunks();
            carView.Show(replaySamples.ByDriver, replaySamples.Indices, _time);
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

            int firstIndex = FindReadyStartChunk();

            if (firstIndex < 0)
                return;

            if (!_loadedChunks.Contains(firstIndex))
            {
                if (!_loadingChunks.Contains(firstIndex))
                    StartCoroutine(LoadChunk(firstIndex));

                return;
            }

            ChunkInfoDto startChunk = _manifest.chunks[firstIndex];
            _time = Mathf.Clamp(_manifest.playbackStartT, startChunk.startT, startChunk.endT);
            _hasStarted = true;
            Play();
        }

        private int FindReadyStartChunk()
        {
            int fallbackIndex = -1;
            bool requiresStartChunk = _manifest.playbackStartT > 0.0f;

            foreach (ChunkInfoDto chunk in _manifest.chunks)
            {
                if (chunk.status != "ready" || chunk.sampleCount <= 0)
                    continue;

                if (fallbackIndex < 0)
                    fallbackIndex = chunk.index;

                if (_manifest.playbackStartT >= chunk.startT && _manifest.playbackStartT <= chunk.endT)
                    return chunk.index;
            }

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
        }

        private void OnDestroy()
        {
            if (_manifestPollingCoroutine != null)
                StopCoroutine(_manifestPollingCoroutine);

            if (_seekCoroutine != null)
                StopCoroutine(_seekCoroutine);
        }
    }
}
