using System;
using F1XR.RestAPI.Api;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public sealed class ReplayReadyStart
    {
        private bool enabled;
        private bool started;

        public void Reset(bool autoStart)
        {
            enabled = autoStart;
            started = false;
        }

        public void TryStart(
            DatasetManifestDto manifest,
            ReplayChunkLoader chunks,
            ReplayTimeline timeline,
            bool waitForTrackPlacement,
            bool trackPlaced,
            Action retry,
            Action play)
        {
            if (!enabled ||
                started ||
                manifest == null ||
                manifest.chunks == null)
                return;

            if (waitForTrackPlacement && !trackPlaced)
                return;

            if (timeline.IsPlaying)
            {
                started = true;
                return;
            }

            int firstIndex =
                chunks.FindReadyStartChunk(
                    manifest.playbackStartT);

            if (firstIndex < 0)
                return;

            if (!chunks.IsLoaded(firstIndex))
            {
                chunks.StartLoad(firstIndex, retry);
                return;
            }

            ChunkInfoDto startChunk =
                manifest.chunks[firstIndex];

            float startTime = manifest.playbackStartT;

            float readyTime =
                startTime >= startChunk.startT &&
                startTime <= startChunk.endT
                    ? startTime
                    : Mathf.Clamp(
                        startTime,
                        timeline.StartTime,
                        timeline.ReadyUntilTime);

            timeline.SetTime(readyTime);
            started = true;
            play?.Invoke();
        }
    }
}