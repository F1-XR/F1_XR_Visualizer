using System;
using System.Collections.Generic;
using F1XR.RestAPI.Api;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public readonly struct ReplayTimelineGap
    {
        public ReplayTimelineGap(float sourceStart, float sourceEnd)
        {
            SourceStart = sourceStart;
            SourceEnd = sourceEnd;
        }

        public float SourceStart { get; }
        public float SourceEnd { get; }
        public float Duration => Mathf.Max(0f, SourceEnd - SourceStart);
    }

    public sealed class ReplayTimeline
    {
        private DatasetManifestDto manifest;
        private bool hasRange;
        private float rangeStart;
        private float rangeEnd;
        private bool compressRedFlagDowntime;
        private float redFlagTailSeconds = 15f;
        private float restartLeadSeconds = 15f;
        private float minimumGapSeconds = 60f;
        private readonly List<ReplayTimelineGap> gaps = new();

        public float CurrentTime { get; private set; }
        public bool IsPlaying { get; private set; }
        public bool IsWaitingForGapData { get; private set; }
        public IReadOnlyList<ReplayTimelineGap> Gaps => gaps;

        public float StartTime =>
            hasRange
                ? rangeStart
                : manifest != null &&
            manifest.chunks != null &&
            manifest.chunks.Length > 0
                ? manifest.chunks[0].startT
                : 0f;

        public float RaceStartTime
        {
            get
            {
                if (manifest == null)
                    return hasRange ? rangeStart : 0f;

                return manifest.raceStartT > 0f
                    ? manifest.raceStartT
                    : manifest.playbackStartT;
            }
        }

        public float RaceEndTime =>
            hasRange
                ? rangeEnd
                : manifest != null
                ? manifest.raceEndT
                : 0f;

        public float EndTime =>
            hasRange
                ? rangeEnd
                : ResolveManifestEndTime();

        public float ReadyUntilTime =>
            hasRange
                ? rangeEnd
                : manifest != null
                ? manifest.readyUntilT
                : 0f;

        public float Duration
        {
            get
            {
                if (hasRange)
                    return Mathf.Max(0f, rangeEnd - rangeStart);

                if (manifest == null)
                    return 0f;

                float duration = Mathf.Max(0f, EndTime - StartTime);
                for (int i = 0; i < gaps.Count; i++)
                    duration -= gaps[i].Duration;

                return Mathf.Max(0f, duration);
            }
        }

        public float ElapsedTime => ToElapsedTime(CurrentTime);

        public void ConfigureRedFlagCompression(
            bool enabled,
            float visibleTailSeconds,
            float visibleRestartLeadSeconds,
            float minimumRemovedSeconds)
        {
            compressRedFlagDowntime = enabled;
            redFlagTailSeconds = Mathf.Max(0f, visibleTailSeconds);
            restartLeadSeconds = Mathf.Max(0f, visibleRestartLeadSeconds);
            minimumGapSeconds = Mathf.Max(0f, minimumRemovedSeconds);
            RebuildGaps();
        }

        public void Reset(DatasetManifestDto sourceManifest)
        {
            manifest = sourceManifest;
            hasRange = false;
            CurrentTime = sourceManifest != null
                ? sourceManifest.playbackStartT
                : 0f;
            IsPlaying = false;
            IsWaitingForGapData = false;
            RebuildGaps();
        }

        public void Reset(float startTime, float endTime)
        {
            manifest = null;
            hasRange = true;
            rangeStart = startTime;
            rangeEnd = Mathf.Max(startTime, endTime);
            CurrentTime = rangeStart;
            IsPlaying = false;
            IsWaitingForGapData = false;
            gaps.Clear();
        }

        public void SetManifest(DatasetManifestDto sourceManifest)
        {
            manifest = sourceManifest;
            hasRange = false;
            RebuildGaps();
        }

        public void Play()
        {
            IsPlaying = true;
        }

        public void Pause()
        {
            IsPlaying = false;
            IsWaitingForGapData = false;
        }

        public void SetTime(float time)
        {
            CurrentTime = time;
            IsWaitingForGapData = false;
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
            Advance(deltaTime, speed, null);
        }

        public void Advance(
            float deltaTime,
            float speed,
            Func<float, bool> isGapTargetLoaded)
        {
            IsWaitingForGapData = false;
            float targetTime = CurrentTime + deltaTime * speed;

            for (int i = 0; i < gaps.Count; i++)
            {
                ReplayTimelineGap gap = gaps[i];
                if (CurrentTime >= gap.SourceEnd ||
                    targetTime < gap.SourceStart)
                {
                    continue;
                }

                bool sourceReady = ReadyUntilTime + 0.001f >= gap.SourceEnd;
                bool targetLoaded =
                    isGapTargetLoaded == null ||
                    isGapTargetLoaded(gap.SourceEnd);
                if (!sourceReady || !targetLoaded)
                {
                    CurrentTime = gap.SourceStart;
                    IsWaitingForGapData = true;
                    return;
                }

                float visibleOvershoot = CurrentTime < gap.SourceStart
                    ? targetTime - gap.SourceStart
                    : targetTime - CurrentTime;
                targetTime = gap.SourceEnd +
                    Mathf.Max(0f, visibleOvershoot);
            }

            CurrentTime = targetTime;
        }

        public bool StopAtEnd()
        {
            if (!hasRange && manifest == null)
                return false;

            float endTime = EndTime;

            if (CurrentTime <= endTime)
                return false;

            CurrentTime = endTime;
            IsPlaying = false;
            return true;
        }

        public float ToNormalized(float time)
        {
            return Duration > 0f
                ? Mathf.Clamp01(ToElapsedTime(time) / Duration)
                : 0f;
        }

        public float FromNormalized(float normalized)
        {
            float remaining = Mathf.Clamp01(normalized) * Duration;
            float sourceCursor = StartTime;

            for (int i = 0; i < gaps.Count; i++)
            {
                ReplayTimelineGap gap = gaps[i];
                float visibleSegment = Mathf.Max(
                    0f,
                    gap.SourceStart - sourceCursor);
                if (remaining <= visibleSegment)
                    return sourceCursor + remaining;

                remaining -= visibleSegment;
                sourceCursor = gap.SourceEnd;
            }

            return Mathf.Min(EndTime, sourceCursor + remaining);
        }

        public float ToElapsedTime(float sourceTime)
        {
            float clampedTime = Mathf.Clamp(
                sourceTime,
                StartTime,
                EndTime);
            float elapsed = clampedTime - StartTime;

            for (int i = 0; i < gaps.Count; i++)
            {
                ReplayTimelineGap gap = gaps[i];
                if (clampedTime >= gap.SourceEnd)
                {
                    elapsed -= gap.Duration;
                    continue;
                }

                if (clampedTime > gap.SourceStart)
                    elapsed -= clampedTime - gap.SourceStart;

                break;
            }

            return Mathf.Max(0f, elapsed);
        }

        private float ResolveManifestEndTime()
        {
            if (manifest == null)
                return 0f;

            return manifest.requestedDurationSeconds > 0f
                ? manifest.requestedDurationSeconds
                : manifest.durationSeconds;
        }

        private void RebuildGaps()
        {
            gaps.Clear();
            if (!compressRedFlagDowntime ||
                hasRange ||
                manifest == null ||
                manifest.redFlags == null)
            {
                return;
            }

            float timelineStart = StartTime;
            float timelineEnd = ResolveManifestEndTime();
            for (int i = 0; i < manifest.redFlags.Length; i++)
            {
                RaceControlEventDto redFlag = manifest.redFlags[i];
                if (redFlag == null)
                    continue;

                float redStart = redFlag.startT > 0f
                    ? redFlag.startT
                    : redFlag.t;
                float restartTime = redFlag.endT;
                float gapStart = redStart + redFlagTailSeconds;
                float gapEnd = restartTime - restartLeadSeconds;
                if (gapStart < timelineStart ||
                    gapEnd > timelineEnd ||
                    gapEnd - gapStart < minimumGapSeconds)
                {
                    continue;
                }

                gaps.Add(new ReplayTimelineGap(gapStart, gapEnd));
            }

            gaps.Sort((left, right) =>
                left.SourceStart.CompareTo(right.SourceStart));
        }
    }
}
