using System;
using System.Collections.Generic;
using F1XR.RestAPI.Api;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public readonly struct VisualMotionPose
    {
        public static readonly VisualMotionPose None = new(
            0f,
            0f,
            false,
            null,
            null,
            null,
            null,
            0f);

        public readonly float lateralOffset;
        public readonly float localYaw;
        public readonly bool active;
        public readonly string sourceEventId;
        public readonly string passingSide;
        public readonly string sideSource;
        public readonly string role;
        public readonly float confidence;

        public VisualMotionPose(
            float lateralOffset,
            float localYaw,
            bool active,
            string sourceEventId,
            string passingSide,
            string sideSource,
            string role,
            float confidence)
        {
            this.lateralOffset = lateralOffset;
            this.localYaw = localYaw;
            this.active = active;
            this.sourceEventId = sourceEventId;
            this.passingSide = passingSide;
            this.sideSource = sideSource;
            this.role = role;
            this.confidence = confidence;
        }
    }

    public sealed class OvertakeMotion
    {
        private ReplayEventDto[] events;
        private OvertakeMotionSettings settings = new();

        public void SetEvents(ReplayEventDto[] source)
        {
            if (source == null || source.Length == 0)
            {
                events = source;
                return;
            }

            events = (ReplayEventDto[])source.Clone();
            Array.Sort(events, CompareEvents);
        }

        public void SetSettings(OvertakeMotionSettings source)
        {
            settings = source ?? new OvertakeMotionSettings();
        }

        public VisualMotionPose Resolve(
            int driverNumber,
            float time,
            IReadOnlyDictionary<int, ReplayCarPose> poses,
            IReadOnlyDictionary<int, float> visualWidths,
            IReadOnlyDictionary<int, float> visualLengths)
        {
            if (!settings.enableOvertakeVisuals || events == null || events.Length == 0)
                return VisualMotionPose.None;

            float sumOffset = 0f;
            float sumVelocity = 0f;
            float strongestOffset = 0f;
            float strongestVelocity = 0f;
            float strongestScore = -1f;
            ReplayEventDto strongestEvent = null;
            string strongestSide = null;
            string strongestSource = null;
            string strongestRole = null;
            float strongestConfidence = 0f;

            foreach (ReplayEventDto replayEvent in events)
            {
                if (!TryGetRole(replayEvent, driverNumber, time, out bool overtaker))
                    continue;

                int side = PassingSide(replayEvent);
                EvaluateEnvelope(
                    replayEvent,
                    time,
                    out float weight,
                    out float velocity,
                    out bool returning);
                if (!returning)
                {
                    float guardWeight = CollisionGuardWeight(
                        replayEvent,
                        poses,
                        visualWidths,
                        visualLengths);
                    float guardActivation = SmoothStep(
                        Mathf.Clamp01(weight * 2f));
                    weight = Mathf.Max(
                        weight,
                        guardWeight * guardActivation);
                }
                if (weight <= 0f && Mathf.Abs(velocity) <= Mathf.Epsilon)
                    continue;

                float correction = RequiredCorrection(
                    replayEvent,
                    side,
                    poses,
                    visualWidths);
                float share = DriverShare(replayEvent, overtaker);
                float roleSign = overtaker ? side : -side;
                float offset = roleSign * correction * share * weight;
                float contributionVelocity = roleSign * correction * share * velocity;
                float confidence = SideConfidence(replayEvent);
                float score = Mathf.Abs(offset) * (1f + confidence);

                sumOffset += offset;
                sumVelocity += contributionVelocity;

                if (score > strongestScore ||
                    Mathf.Approximately(score, strongestScore) &&
                    CompareEventIds(replayEvent, strongestEvent) < 0)
                {
                    strongestScore = score;
                    strongestOffset = offset;
                    strongestVelocity = contributionVelocity;
                    strongestEvent = replayEvent;
                    strongestSide = side > 0 ? "Right" : "Left";
                    strongestSource = SideSource(replayEvent);
                    strongestRole = overtaker ? "Overtaker" : "Defender";
                    strongestConfidence = confidence;
                }
            }

            if (strongestEvent == null)
                return VisualMotionPose.None;

            float lateralOffset = settings.overlapBlendMode == OvertakeBlendMode.Strongest
                ? strongestOffset
                : sumOffset;
            float lateralVelocity = settings.overlapBlendMode == OvertakeBlendMode.Strongest
                ? strongestVelocity
                : sumVelocity;
            float vehicleWidth = GetVehicleWidth(driverNumber, visualWidths);
            float maximumOffset = vehicleWidth * Mathf.Max(
                0f,
                settings.maximumOffsetInVehicleWidths);

            if (maximumOffset <= 0f)
            {
                lateralOffset = 0f;
                lateralVelocity = 0f;
            }
            else if (Mathf.Abs(lateralOffset) > maximumOffset)
            {
                float scale = maximumOffset / Mathf.Abs(lateralOffset);
                lateralOffset *= scale;
                lateralVelocity *= scale;
            }

            float forwardSpeed = 0f;
            if (poses != null && poses.TryGetValue(driverNumber, out ReplayCarPose pose))
                forwardSpeed = pose.localSpeed;

            float yaw = Mathf.Atan2(
                    lateralVelocity,
                    Mathf.Max(0.001f, forwardSpeed)) *
                Mathf.Rad2Deg;
            yaw = Mathf.Clamp(
                yaw,
                -settings.maximumVisualYawDegrees,
                settings.maximumVisualYawDegrees);

            return new VisualMotionPose(
                lateralOffset,
                yaw,
                true,
                strongestEvent.eventId,
                strongestSide,
                strongestSource,
                strongestRole,
                strongestConfidence);
        }

        private float RequiredCorrection(
            ReplayEventDto replayEvent,
            int side,
            IReadOnlyDictionary<int, ReplayCarPose> poses,
            IReadOnlyDictionary<int, float> visualWidths)
        {
            if (replayEvent.driverNumbers == null ||
                replayEvent.driverNumbers.Length < 2 ||
                poses == null ||
                visualWidths == null ||
                !poses.TryGetValue(replayEvent.driverNumbers[0], out ReplayCarPose overtaker) ||
                !poses.TryGetValue(replayEvent.driverNumbers[1], out ReplayCarPose defender) ||
                !visualWidths.TryGetValue(replayEvent.driverNumbers[0], out float overtakerWidth) ||
                !visualWidths.TryGetValue(replayEvent.driverNumbers[1], out float defenderWidth))
                return 0f;

            float vehicleWidth = Mathf.Max(
                0.001f,
                (overtakerWidth + defenderWidth) * 0.5f);
            float targetSeparation = vehicleWidth * Mathf.Max(
                0f,
                settings.targetSeparationInVehicleWidths);
            float maximumCorrection = vehicleWidth * Mathf.Max(
                0f,
                settings.maximumCorrectionInVehicleWidths);

            Vector3 forward = overtaker.localForward + defender.localForward;
            if (forward.sqrMagnitude <= 0.000001f)
                forward = overtaker.localForward;
            if (forward.sqrMagnitude <= 0.000001f)
                return 0f;

            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            float existingSeparation = Vector3.Dot(
                overtaker.rawPosition - defender.rawPosition,
                right * side);
            float correction = targetSeparation - existingSeparation;

            return Mathf.Clamp(
                correction,
                -maximumCorrection,
                maximumCorrection);
        }

        private float CollisionGuardWeight(
            ReplayEventDto replayEvent,
            IReadOnlyDictionary<int, ReplayCarPose> poses,
            IReadOnlyDictionary<int, float> visualWidths,
            IReadOnlyDictionary<int, float> visualLengths)
        {
            if (replayEvent.driverNumbers == null ||
                replayEvent.driverNumbers.Length < 2 ||
                poses == null ||
                visualWidths == null ||
                visualLengths == null ||
                !poses.TryGetValue(replayEvent.driverNumbers[0], out ReplayCarPose overtaker) ||
                !poses.TryGetValue(replayEvent.driverNumbers[1], out ReplayCarPose defender) ||
                !visualWidths.TryGetValue(replayEvent.driverNumbers[0], out float overtakerWidth) ||
                !visualWidths.TryGetValue(replayEvent.driverNumbers[1], out float defenderWidth) ||
                !visualLengths.TryGetValue(replayEvent.driverNumbers[0], out float overtakerLength) ||
                !visualLengths.TryGetValue(replayEvent.driverNumbers[1], out float defenderLength))
                return 0f;

            Vector3 forward = overtaker.localForward + defender.localForward;
            if (forward.sqrMagnitude <= 0.000001f)
                forward = overtaker.localForward;
            if (forward.sqrMagnitude <= 0.000001f)
                return 0f;

            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 separation = overtaker.rawPosition - defender.rawPosition;
            float lateralDistance = Mathf.Abs(Vector3.Dot(separation, right));
            float vehicleWidth = Mathf.Max(
                0.001f,
                (overtakerWidth + defenderWidth) * 0.5f);
            float targetLateralDistance =
                vehicleWidth *
                Mathf.Max(0f, settings.targetSeparationInVehicleWidths);
            if (lateralDistance >= targetLateralDistance)
                return 0f;

            float overlapDistance = Mathf.Max(
                0.001f,
                (overtakerLength + defenderLength) * 0.5f);
            float releaseDistance = overlapDistance + vehicleWidth;
            float longitudinalDistance = Mathf.Abs(
                Vector3.Dot(separation, forward));
            if (longitudinalDistance <= overlapDistance)
                return 1f;
            if (longitudinalDistance >= releaseDistance)
                return 0f;

            float release = Mathf.InverseLerp(
                overlapDistance,
                releaseDistance,
                longitudinalDistance);
            return 1f - SmoothStep(release);
        }

        private float DriverShare(ReplayEventDto replayEvent, bool overtaker)
        {
            float eventTotal = replayEvent.overtakerShare + replayEvent.defenderShare;
            bool hasEventSplit = eventTotal > 0.0001f;
            float overtakerShare = hasEventSplit
                ? Mathf.Max(0f, replayEvent.overtakerShare)
                : settings.overtakerShare;
            float defenderShare = hasEventSplit
                ? Mathf.Max(0f, replayEvent.defenderShare)
                : settings.defenderShare;
            float share = overtaker ? overtakerShare : defenderShare;
            float total = Mathf.Max(0.0001f, overtakerShare + defenderShare);
            return Mathf.Clamp01(share / total);
        }

        private void EvaluateEnvelope(
            ReplayEventDto replayEvent,
            float time,
            out float weight,
            out float velocity,
            out bool returning)
        {
            weight = 0f;
            velocity = 0f;
            returning = false;

            float duration = replayEvent.endTime - replayEvent.startTime;
            if (duration <= 0f || time < replayEvent.startTime || time > replayEvent.endTime)
                return;

            float totalPortion = Mathf.Max(
                0.0001f,
                settings.approachPortion +
                settings.parallelPortion +
                settings.returnPortion);
            float approachEnd = settings.approachPortion / totalPortion;
            float returnStart =
                (settings.approachPortion + settings.parallelPortion) /
                totalPortion;
            float anchor = Mathf.Clamp01(
                (replayEvent.anchorTime - replayEvent.startTime) / duration);

            approachEnd = Mathf.Clamp(Mathf.Min(approachEnd, anchor), 0.0001f, 0.9998f);
            returnStart = Mathf.Clamp(Mathf.Max(returnStart, anchor), approachEnd + 0.0001f, 0.9999f);

            float normalized = Mathf.Clamp01((time - replayEvent.startTime) / duration);
            if (normalized < approachEnd)
            {
                float t = normalized / approachEnd;
                weight = SmoothStep(t);
                velocity = SmoothStepDerivative(t) / (approachEnd * duration);
                return;
            }

            if (normalized <= returnStart)
            {
                weight = 1f;
                return;
            }

            returning = true;
            float returnT = (normalized - returnStart) / (1f - returnStart);
            weight = 1f - SmoothStep(returnT);
            velocity = -SmoothStepDerivative(returnT) / ((1f - returnStart) * duration);
        }

        private static bool TryGetRole(
            ReplayEventDto replayEvent,
            int driverNumber,
            float time,
            out bool overtaker)
        {
            overtaker = false;
            if (replayEvent == null ||
                !string.Equals(replayEvent.eventType, "Overtake", StringComparison.OrdinalIgnoreCase) ||
                replayEvent.driverNumbers == null ||
                replayEvent.driverNumbers.Length < 2 ||
                replayEvent.endTime <= replayEvent.startTime ||
                time < replayEvent.startTime ||
                time > replayEvent.endTime)
                return false;

            if (driverNumber == replayEvent.driverNumbers[0])
            {
                overtaker = true;
                return true;
            }

            return driverNumber == replayEvent.driverNumbers[1];
        }

        private static int PassingSide(ReplayEventDto replayEvent)
        {
            if (string.Equals(replayEvent.passingSide, "Right", StringComparison.OrdinalIgnoreCase))
                return 1;
            if (string.Equals(replayEvent.passingSide, "Left", StringComparison.OrdinalIgnoreCase))
                return -1;

            uint hash = 2166136261;
            string value = replayEvent.eventId ?? string.Empty;
            foreach (char character in value)
            {
                hash ^= character;
                hash *= 16777619;
            }

            return (hash & 1) == 0 ? -1 : 1;
        }

        private static float SideConfidence(ReplayEventDto replayEvent)
        {
            if (replayEvent.sideConfidence > 0f)
                return Mathf.Clamp01(replayEvent.sideConfidence);
            if (string.Equals(
                    replayEvent.sideSource,
                    "DeterministicFallback",
                    StringComparison.OrdinalIgnoreCase))
                return 0f;
            if (replayEvent.confidence >= 0f &&
                !string.IsNullOrWhiteSpace(replayEvent.passingSide))
                return Mathf.Clamp01(replayEvent.confidence);

            return Mathf.Clamp01(replayEvent.sideConfidence);
        }

        private static string SideSource(ReplayEventDto replayEvent)
        {
            if (!string.IsNullOrWhiteSpace(replayEvent.sideSource))
                return replayEvent.sideSource;

            return string.Equals(replayEvent.passingSide, "Left", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(replayEvent.passingSide, "Right", StringComparison.OrdinalIgnoreCase)
                    ? "EventMetadata"
                    : "DeterministicFallback";
        }

        private static float GetVehicleWidth(
            int driverNumber,
            IReadOnlyDictionary<int, float> visualWidths)
        {
            if (visualWidths == null ||
                !visualWidths.TryGetValue(driverNumber, out float width))
                return 0f;

            return Mathf.Max(0f, width);
        }

        private static float SmoothStep(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        private static float SmoothStepDerivative(float value)
        {
            value = Mathf.Clamp01(value);
            return 6f * value * (1f - value);
        }

        private static int CompareEventIds(
            ReplayEventDto a,
            ReplayEventDto b)
        {
            if (b == null)
                return -1;

            return string.CompareOrdinal(a?.eventId, b.eventId);
        }

        private static int CompareEvents(ReplayEventDto a, ReplayEventDto b)
        {
            if (ReferenceEquals(a, b))
                return 0;
            if (a == null)
                return -1;
            if (b == null)
                return 1;

            int result = string.CompareOrdinal(a.eventId, b.eventId);
            if (result != 0)
                return result;

            result = a.startTime.CompareTo(b.startTime);
            if (result != 0)
                return result;

            result = a.endTime.CompareTo(b.endTime);
            if (result != 0)
                return result;

            result = a.anchorTime.CompareTo(b.anchorTime);
            if (result != 0)
                return result;

            int aDriverCount = a.driverNumbers?.Length ?? 0;
            int bDriverCount = b.driverNumbers?.Length ?? 0;
            int sharedDriverCount = Math.Min(aDriverCount, bDriverCount);
            for (int i = 0; i < sharedDriverCount; i++)
            {
                result = a.driverNumbers[i].CompareTo(b.driverNumbers[i]);
                if (result != 0)
                    return result;
            }

            result = aDriverCount.CompareTo(bDriverCount);
            if (result != 0)
                return result;

            result = string.CompareOrdinal(a.passingSide, b.passingSide);
            if (result != 0)
                return result;

            result = string.CompareOrdinal(a.sideSource, b.sideSource);
            if (result != 0)
                return result;

            result = a.sideConfidence.CompareTo(b.sideConfidence);
            if (result != 0)
                return result;

            result = a.confidence.CompareTo(b.confidence);
            if (result != 0)
                return result;

            result = a.overtakerShare.CompareTo(b.overtakerShare);
            return result != 0
                ? result
                : a.defenderShare.CompareTo(b.defenderShare);
        }
    }
}
