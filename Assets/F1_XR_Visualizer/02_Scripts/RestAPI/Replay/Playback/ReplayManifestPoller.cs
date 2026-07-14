using System;
using System.Collections;
using F1XR.RestAPI.Api;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public sealed class ReplayManifestPoller
    {
        private readonly MonoBehaviour coroutineHost;
        private Coroutine routine;

        public ReplayManifestPoller(MonoBehaviour coroutineHost)
        {
            this.coroutineHost = coroutineHost;
        }

        public void Start(
            ApiClient api,
            string datasetId,
            float pollSeconds,
            Action<DatasetManifestDto> onUpdated)
        {
            Stop();

            routine = coroutineHost.StartCoroutine(
                Poll(
                    api,
                    datasetId,
                    pollSeconds,
                    onUpdated));
        }

        public void Stop()
        {
            if (routine == null)
                return;

            coroutineHost.StopCoroutine(routine);
            routine = null;
        }

        private IEnumerator Poll(
            ApiClient api,
            string datasetId,
            float pollSeconds,
            Action<DatasetManifestDto> onUpdated)
        {
            DatasetManifestDto latestManifest = null;

            while (!string.IsNullOrEmpty(datasetId))
            {
                yield return api.GetManifest(
                    datasetId,
                    manifest =>
                    {
                        latestManifest = manifest;
                        onUpdated?.Invoke(manifest);
                    },
                    error =>
                    {
                        Debug.LogWarning(
                            $"Manifest poll failed: {error}");
                    });

                if (latestManifest != null &&
                    (latestManifest.status == "complete" ||
                     latestManifest.status == "failed"))
                {
                    break;
                }

                yield return new WaitForSeconds(pollSeconds);
            }

            routine = null;
        }
    }
}