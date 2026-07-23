using System;
using System.Collections;
using System.Collections.Generic;
using F1XR.RestAPI.Api;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public sealed class ReplayChunkLoader
    {
        private readonly MonoBehaviour coroutineHost;
        private readonly HashSet<int> loadedChunks = new();
        private readonly HashSet<int> loadingChunks = new();

        private readonly LocationReplaySamples locationSamples = new();
        private readonly PositionReplaySamples positionSamples = new();
        private readonly TireReplaySamples tireSamples = new();

        private ApiClient api;
        private string datasetId;
        private DatasetManifestDto manifest;

        public Dictionary<int, List<LocationSample>> LocationsByDriver =>
            locationSamples.ByDriver;

        public Dictionary<int, int> LocationIndices =>
            locationSamples.Indices;

        public ReplayChunkLoader(MonoBehaviour coroutineHost)
        {
            this.coroutineHost = coroutineHost;
        }

        public void Reset(
            ApiClient sourceApi,
            string sourceDatasetId,
            DatasetManifestDto sourceManifest)
        {
            api = sourceApi;
            datasetId = sourceDatasetId;
            manifest = sourceManifest;
            Clear();
        }

        public void SetManifest(DatasetManifestDto sourceManifest)
        {
            manifest = sourceManifest;
        }

        public void Clear()
        {
            loadedChunks.Clear();
            loadingChunks.Clear();
            locationSamples.Clear();
            positionSamples.Clear();
            tireSamples.Clear();
        }

        public void ResetLocationIndices()
        {
            locationSamples.ResetIndices();
        }

        public bool IsLoaded(int chunkIndex)
        {
            return loadedChunks.Contains(chunkIndex);
        }

        public bool IsLoading(int chunkIndex)
        {
            return loadingChunks.Contains(chunkIndex);
        }

        public bool CanLoad(int chunkIndex)
        {
            if (manifest == null ||
                manifest.chunks == null ||
                chunkIndex < 0 ||
                chunkIndex >= manifest.chunks.Length)
                return false;

            return CanLoad(manifest.chunks[chunkIndex]);
        }

        public int FindChunk(float time)
        {
            if (manifest == null ||
                manifest.chunks == null ||
                manifest.chunks.Length == 0)
                return 0;

            for (int i = 0; i < manifest.chunks.Length; i++)
            {
                ChunkInfoDto chunk = manifest.chunks[i];

                if (time >= chunk.startT && time <= chunk.endT)
                    return i;
            }

            if (time < manifest.chunks[0].startT)
                return 0;

            return manifest.chunks.Length - 1;
        }

        public int FindReadyStartChunk(float startTime)
        {
            if (manifest == null || manifest.chunks == null)
                return -1;

            int fallbackIndex = -1;
            int firstSampleIndex = -1;
            bool requiresStartChunk = startTime > 0f;

            for (int i = 0; i < manifest.chunks.Length; i++)
            {
                ChunkInfoDto chunk = manifest.chunks[i];

                if (!CanLoad(chunk))
                    continue;

                if (fallbackIndex < 0)
                    fallbackIndex = i;

                if (firstSampleIndex < 0 &&
                    chunk.sampleCount > 0 &&
                    chunk.endT >= startTime)
                {
                    firstSampleIndex = i;
                }

                if (startTime >= chunk.startT && startTime <= chunk.endT)
                    return i;
            }

            if (firstSampleIndex >= 0)
                return firstSampleIndex;

            return requiresStartChunk ? -1 : fallbackIndex;
        }

        public void LoadNear(
            float time,
            int preloadChunksAhead,
            Action onLoaded)
        {
            if (manifest == null ||
                manifest.chunks == null ||
                manifest.chunks.Length == 0)
                return;

            int currentIndex = FindChunk(time);

            for (int i = currentIndex;
                 i <= currentIndex + preloadChunksAhead;
                 i++)
            {
                if (!CanLoad(i) || IsLoaded(i) || IsLoading(i))
                    continue;

                StartLoad(i, onLoaded);
            }
        }

        public void StartLoad(int chunkIndex, Action onLoaded)
        {
            if (!CanLoad(chunkIndex) ||
                IsLoaded(chunkIndex) ||
                IsLoading(chunkIndex))
                return;

            coroutineHost.StartCoroutine(Load(chunkIndex, onLoaded));
        }

        public IEnumerator LoadRange(float startTime, float endTime, Action<bool> onComplete)
        {
            if (manifest == null || manifest.chunks == null || endTime <= startTime)
            {
                onComplete?.Invoke(false);
                yield break;
            }

            bool foundRange = false;

            for (int i = 0; i < manifest.chunks.Length; i++)
            {
                ChunkInfoDto chunk = manifest.chunks[i];
                if (chunk.endT < startTime || chunk.startT > endTime || !CanLoad(i))
                    continue;

                foundRange = true;

                while (IsLoading(i))
                    yield return null;

                if (!IsLoaded(i))
                {
                    if (api == null)
                    {
                        onComplete?.Invoke(false);
                        yield break;
                    }

                    yield return Load(i, null);
                }
            }

            onComplete?.Invoke(foundRange);
        }

        public IEnumerator Load(int chunkIndex, Action onLoaded)
        {
            if (string.IsNullOrEmpty(datasetId))
                yield break;

            if (IsLoaded(chunkIndex) || IsLoading(chunkIndex))
                yield break;

            loadingChunks.Add(chunkIndex);

            ReplayChunkDto loadedChunk = null;
            string loadError = null;

            yield return api.GetChunk(
                datasetId,
                chunkIndex,
                chunk => loadedChunk = chunk,
                error => loadError = error);

            loadingChunks.Remove(chunkIndex);

            if (!string.IsNullOrEmpty(loadError))
            {
                Debug.LogWarning(
                    $"Chunk {chunkIndex} load failed: {loadError}");

                yield break;
            }

            if (loadedChunk == null)
                yield break;

            if (loadedChunk.samples != null &&
                loadedChunk.samples.Length > 0)
            {
                locationSamples.Add(loadedChunk);
                positionSamples.Add(loadedChunk);
                tireSamples.Add(loadedChunk);

                Debug.Log(
                    $"Loaded chunk {loadedChunk.chunkIndex}, " +
                    $"samples={loadedChunk.samples.Length}");
            }

            loadedChunks.Add(chunkIndex);
            onLoaded?.Invoke();
        }

        public List<PositionSampleDto> GetPositions(float time)
        {
            return positionSamples.Get(time);
        }

        public TireSampleDto GetTire(int driverNumber, float time)
        {
            return tireSamples.Get(driverNumber, time);
        }

        private static bool CanLoad(ChunkInfoDto chunk)
        {
            return chunk.status == "ready" || chunk.status == "empty";
        }
    }
}
