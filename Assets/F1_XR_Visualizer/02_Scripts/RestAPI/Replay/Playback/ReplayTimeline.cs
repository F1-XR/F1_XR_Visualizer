using F1XR.RestAPI.Api;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public sealed class ReplayTimeline
    {
        private DatasetManifestDto manifest;

        public float CurrentTime { get; private set; }
        public bool IsPlaying { get; private set; }

        public float StartTime =>
            manifest != null &&
            manifest.chunks != null &&
            manifest.chunks.Length > 0
                ? manifest.chunks[0].startT
                : 0f;

        public float RaceStartTime
        {
            get
            {
                if (manifest == null)
                    return 0f;

                return manifest.raceStartT > 0f
                    ? manifest.raceStartT
                    : manifest.playbackStartT;
            }
        }

        public float RaceEndTime =>
            manifest != null
                ? manifest.raceEndT
                : 0f;

        public float ReadyUntilTime =>
            manifest != null
                ? manifest.readyUntilT
                : 0f;

        public float Duration
        {
            get
            {
                if (manifest == null)
                    return 0f;

                float endTime =
                    manifest.requestedDurationSeconds > 0f
                        ? manifest.requestedDurationSeconds
                        : manifest.durationSeconds;

                return Mathf.Max(0f, endTime - StartTime);
            }
        }

        public void Reset(DatasetManifestDto sourceManifest)
        {
            manifest = sourceManifest;
            CurrentTime = sourceManifest != null
                ? sourceManifest.playbackStartT
                : 0f;
            IsPlaying = false;
        }

        public void SetManifest(DatasetManifestDto sourceManifest)
        {
            manifest = sourceManifest;
        }

        public void Play()
        {
            IsPlaying = true;
        }

        public void Pause()
        {
            IsPlaying = false;
        }

        public void SetTime(float time)
        {
            CurrentTime = time;
        }

        public float ClampToReady(float time)
        {
            return Mathf.Clamp(
                time,
                StartTime,
                ReadyUntilTime);
        }

        public void Advance(float deltaTime, float speed)
        {
            CurrentTime += deltaTime * speed;
        }

        public bool StopAtEnd()
        {
            if (manifest == null)
                return false;

            float endTime =
                manifest.requestedDurationSeconds > 0f
                    ? manifest.requestedDurationSeconds
                    : manifest.durationSeconds;

            if (CurrentTime <= endTime)
                return false;

            CurrentTime = endTime;
            IsPlaying = false;
            return true;
        }

        public float ToNormalized(float time)
        {
            return Duration > 0f
                ? Mathf.Clamp01(
                    (time - StartTime) / Duration)
                : 0f;
        }

        public float FromNormalized(float normalized)
        {
            return StartTime +
                Mathf.Clamp01(normalized) * Duration;
        }
    }
}