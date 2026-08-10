using System;
using System.Collections.Generic;
using F1XR.RestAPI.Api;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public enum PitStopPhase
    {
        Approach,
        Brake,
        Service,
        Release,
        Exit
    }

    public sealed class PitStopSequence
    {
        public PitStopSequence(
            float startTime,
            float brakeTime,
            float serviceStartTime,
            float serviceEndTime,
            float releaseEndTime,
            float endTime,
            float confidence,
            bool isReconstructed,
            bool isDriveThrough)
        {
            StartTime = startTime;
            BrakeTime = brakeTime;
            ServiceStartTime = serviceStartTime;
            ServiceEndTime = serviceEndTime;
            ReleaseEndTime = releaseEndTime;
            EndTime = endTime;
            Confidence = confidence;
            IsReconstructed = isReconstructed;
            IsDriveThrough = isDriveThrough;
        }

        public float StartTime { get; }
        public float BrakeTime { get; }
        public float ServiceStartTime { get; }
        public float ServiceEndTime { get; }
        public float ReleaseEndTime { get; }
        public float EndTime { get; }
        public float Confidence { get; }
        public bool IsReconstructed { get; }
        public bool IsDriveThrough { get; }

        public float FocusTime => IsDriveThrough
            ? Mathf.Clamp(
                (StartTime + EndTime) * 0.5f,
                StartTime,
                EndTime)
            : (ServiceStartTime + ServiceEndTime) * 0.5f;

        public PitStopPhase GetPhase(float replayTime)
        {
            if (IsDriveThrough)
                return replayTime < FocusTime
                    ? PitStopPhase.Approach
                    : PitStopPhase.Exit;
            if (replayTime < BrakeTime)
                return PitStopPhase.Approach;
            if (replayTime < ServiceStartTime)
                return PitStopPhase.Brake;
            if (replayTime < ServiceEndTime)
                return PitStopPhase.Service;
            if (replayTime < ReleaseEndTime)
                return PitStopPhase.Release;
            return PitStopPhase.Exit;
        }
    }

    public sealed class PitStopSequenceBuilder
    {
        private const float StopSpeedInVehicleLengthsPerSecond = 0.45f;
        private const float MinimumInferredStopSeconds = 0.65f;
        private const float MaximumSampleGapSeconds = 1.25f;
        private const float BrakeLeadSeconds = 1.5f;
        private const float ReleaseTailSeconds = 1.5f;
        private const float MinimumPhaseSpacingSeconds = 0.05f;

        public PitStopSequence Build(
            ReplayEventDto replayEvent,
            IReadOnlyList<LocationSample> samples,
            IReadOnlyList<float> longitudinalDistances,
            float vehicleLength)
        {
            if (replayEvent == null)
                throw new ArgumentNullException(nameof(replayEvent));

            float startTime = replayEvent.startTime;
            float endTime = replayEvent.endTime;
            if (endTime <= startTime)
                return CreateDriveThrough(startTime, endTime, replayEvent.confidence);

            List<SpeedInterval> speeds = BuildSpeeds(
                samples,
                longitudinalDistances,
                Mathf.Max(0.0001f, vehicleLength),
                startTime,
                endTime);
            bool hasAuthoritativeDuration = replayEvent.pitStopDuration > 0f;
            if (hasAuthoritativeDuration)
            {
                float serviceDuration = Mathf.Min(
                    replayEvent.pitStopDuration,
                    Mathf.Max(
                        MinimumPhaseSpacingSeconds,
                        endTime - startTime -
                        MinimumPhaseSpacingSeconds * 2f));
                float center = FindSlowestTime(
                    speeds,
                    Mathf.Clamp(replayEvent.anchorTime, startTime, endTime));
                float serviceStart = Mathf.Clamp(
                    center - serviceDuration * 0.5f,
                    startTime + MinimumPhaseSpacingSeconds,
                    endTime - serviceDuration - MinimumPhaseSpacingSeconds);
                return CreateStop(
                    startTime,
                    serviceStart,
                    serviceStart + serviceDuration,
                    endTime,
                    replayEvent.confidence > 0f
                        ? replayEvent.confidence
                        : 0.95f,
                    false);
            }

            if (!TryFindInferredStop(
                    speeds,
                    replayEvent.anchorTime,
                    out float inferredStart,
                    out float inferredEnd,
                    out float confidence))
            {
                return CreateDriveThrough(
                    startTime,
                    endTime,
                    Mathf.Max(0f, replayEvent.confidence));
            }

            return CreateStop(
                startTime,
                inferredStart,
                inferredEnd,
                endTime,
                confidence,
                true);
        }

        private static List<SpeedInterval> BuildSpeeds(
            IReadOnlyList<LocationSample> samples,
            IReadOnlyList<float> distances,
            float vehicleLength,
            float startTime,
            float endTime)
        {
            List<SpeedInterval> result = new();
            if (samples == null || distances == null ||
                samples.Count != distances.Count)
            {
                return result;
            }

            for (int i = 1; i < samples.Count; i++)
            {
                float intervalStart = samples[i - 1].t;
                float intervalEnd = samples[i].t;
                float duration = intervalEnd - intervalStart;
                if (duration <= 0f || duration > MaximumSampleGapSeconds ||
                    intervalEnd < startTime || intervalStart > endTime)
                {
                    continue;
                }

                float distance = Mathf.Max(
                    0f,
                    distances[i] - distances[i - 1]);
                result.Add(new SpeedInterval(
                    Mathf.Max(startTime, intervalStart),
                    Mathf.Min(endTime, intervalEnd),
                    distance / duration / vehicleLength));
            }

            return result;
        }

        private static float FindSlowestTime(
            IReadOnlyList<SpeedInterval> speeds,
            float fallback)
        {
            float slowest = float.PositiveInfinity;
            float result = fallback;
            for (int i = 0; i < speeds.Count; i++)
            {
                SpeedInterval sample = speeds[i];
                if (sample.Speed >= slowest)
                    continue;

                slowest = sample.Speed;
                result = (sample.StartTime + sample.EndTime) * 0.5f;
            }

            return result;
        }

        private static bool TryFindInferredStop(
            IReadOnlyList<SpeedInterval> speeds,
            float anchorTime,
            out float stopStart,
            out float stopEnd,
            out float confidence)
        {
            stopStart = 0f;
            stopEnd = 0f;
            confidence = 0f;
            float bestScore = float.NegativeInfinity;
            int index = 0;
            while (index < speeds.Count)
            {
                if (speeds[index].Speed > StopSpeedInVehicleLengthsPerSecond)
                {
                    index++;
                    continue;
                }

                float start = speeds[index].StartTime;
                float end = speeds[index].EndTime;
                float weightedSpeed = speeds[index].Speed *
                    Mathf.Max(0f, end - start);
                float duration = Mathf.Max(0f, end - start);
                index++;
                while (index < speeds.Count &&
                       speeds[index].Speed <= StopSpeedInVehicleLengthsPerSecond &&
                       speeds[index].StartTime <= end + 0.001f)
                {
                    float intervalDuration = Mathf.Max(
                        0f,
                        speeds[index].EndTime - speeds[index].StartTime);
                    weightedSpeed += speeds[index].Speed * intervalDuration;
                    duration += intervalDuration;
                    end = Mathf.Max(end, speeds[index].EndTime);
                    index++;
                }

                if (duration < MinimumInferredStopSeconds)
                    continue;

                float averageSpeed = duration > 0f
                    ? weightedSpeed / duration
                    : StopSpeedInVehicleLengthsPerSecond;
                float durationScore = Mathf.InverseLerp(
                    MinimumInferredStopSeconds,
                    2.5f,
                    duration);
                float speedScore = 1f - Mathf.Clamp01(
                    averageSpeed /
                    StopSpeedInVehicleLengthsPerSecond);
                float candidateConfidence =
                    durationScore * 0.55f +
                    speedScore * 0.45f;
                float center = (start + end) * 0.5f;
                float anchorPenalty = Mathf.Min(
                    0.25f,
                    Mathf.Abs(center - anchorTime) * 0.01f);
                float score = candidateConfidence - anchorPenalty;
                if (score <= bestScore)
                    continue;

                bestScore = score;
                stopStart = start;
                stopEnd = end;
                confidence = Mathf.Clamp01(candidateConfidence);
            }

            return bestScore >= 0.2f &&
                confidence >= 0.4f &&
                stopEnd > stopStart;
        }

        private static PitStopSequence CreateStop(
            float startTime,
            float serviceStart,
            float serviceEnd,
            float endTime,
            float confidence,
            bool reconstructed)
        {
            serviceStart = Mathf.Clamp(serviceStart, startTime, endTime);
            serviceEnd = Mathf.Clamp(serviceEnd, serviceStart, endTime);
            float brakeTime = Mathf.Max(
                startTime,
                serviceStart - BrakeLeadSeconds);
            float releaseEnd = Mathf.Min(
                endTime,
                serviceEnd + ReleaseTailSeconds);
            return new PitStopSequence(
                startTime,
                brakeTime,
                serviceStart,
                serviceEnd,
                releaseEnd,
                endTime,
                Mathf.Clamp01(confidence),
                reconstructed,
                false);
        }

        private static PitStopSequence CreateDriveThrough(
            float startTime,
            float endTime,
            float confidence)
        {
            float focus = (startTime + endTime) * 0.5f;
            return new PitStopSequence(
                startTime,
                focus,
                focus,
                focus,
                focus,
                endTime,
                Mathf.Clamp01(confidence),
                false,
                true);
        }

        private readonly struct SpeedInterval
        {
            public SpeedInterval(
                float startTime,
                float endTime,
                float speed)
            {
                StartTime = startTime;
                EndTime = endTime;
                Speed = speed;
            }

            public float StartTime { get; }
            public float EndTime { get; }
            public float Speed { get; }
        }
    }
}
