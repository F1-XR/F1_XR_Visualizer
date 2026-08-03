using System;
using System.Collections.Generic;
using F1XR.RestAPI.Api;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    internal enum OvertakePresentationMode
    {
        FullTrack,
        Showcase
    }

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
            0f,
            Vector3.right);

        public readonly float lateralOffset;
        public readonly float localYaw;
        public readonly bool active;
        public readonly string sourceEventId;
        public readonly string passingSide;
        public readonly string sideSource;
        public readonly string role;
        public readonly float confidence;
        public readonly Vector3 localLateralDirection;

        public VisualMotionPose(
            float lateralOffset,
            float localYaw,
            bool active,
            string sourceEventId,
            string passingSide,
            string sideSource,
            string role,
            float confidence,
            Vector3 localLateralDirection)
        {
            this.lateralOffset = lateralOffset;
            this.localYaw = localYaw;
            this.active = active;
            this.sourceEventId = sourceEventId;
            this.passingSide = passingSide;
            this.sideSource = sideSource;
            this.role = role;
            this.confidence = confidence;
            this.localLateralDirection = localLateralDirection;
        }
    }

    public sealed class OvertakeMotion
    {
        private const float CollisionPlanningWindowSeconds = 5f;

        private readonly List<ReplayEventDto> events = new();
        private readonly List<ReplayEventDto> frameEvents = new();
        private readonly HashSet<int> claimedDrivers = new();
        private readonly Dictionary<int, VisualMotionPose> framePoses = new();
        private readonly Dictionary<ReplayEventDto, int>
            resolvedPassingSides = new();
        private readonly OvertakeFallbackCorridor fallbackCorridor = new();

        private OvertakeMotionSettings settings = new();
        private OvertakePresentationMode presentationMode =
            OvertakePresentationMode.FullTrack;
        private float preparedTime = float.NaN;

        public void SetEvents(ReplayEventDto[] source)
        {
            events.Clear();
            frameEvents.Clear();
            claimedDrivers.Clear();
            framePoses.Clear();
            resolvedPassingSides.Clear();
            preparedTime = float.NaN;

            if (source == null)
                return;

            for (int i = 0; i < source.Length; i++)
            {
                ReplayEventDto replayEvent = source[i];
                if (!IsValidOvertake(replayEvent))
                    continue;

                events.Add(replayEvent);
            }

            events.Sort(CompareEvents);
        }

        public void SetSettings(OvertakeMotionSettings source)
        {
            settings = source ?? new OvertakeMotionSettings();
            preparedTime = float.NaN;
        }

        internal void SetPresentationMode(
            OvertakePresentationMode mode)
        {
            presentationMode = mode;
            framePoses.Clear();
            preparedTime = float.NaN;
        }

        internal void ResetResolvedPassingSides()
        {
            resolvedPassingSides.Clear();
            framePoses.Clear();
            preparedTime = float.NaN;
        }

        public void SetFallbackCorridor(
            IReadOnlyList<Vector3> centerline,
            float roadWidth,
            bool loop)
        {
            fallbackCorridor.Set(centerline, roadWidth, loop);
            preparedTime = float.NaN;
        }

        public void SetTrackCorridor(
            IReadOnlyList<Vector3> centerline,
            IReadOnlyList<Vector3> leftBoundary,
            IReadOnlyList<Vector3> rightBoundary,
            bool loop)
        {
            fallbackCorridor.SetBoundaries(
                centerline,
                leftBoundary,
                rightBoundary,
                loop);
            preparedTime = float.NaN;
        }

        public bool TryGetResolvedPassingSide(
            ReplayEventDto replayEvent,
            out int side)
        {
            side = 0;
            return replayEvent != null &&
                resolvedPassingSides.TryGetValue(
                    replayEvent,
                    out side);
        }

        public void PrepareFrame(
            float time,
            IReadOnlyDictionary<int, ReplayCarPose> poses,
            IReadOnlyDictionary<int, float> visualWidths,
            IReadOnlyDictionary<int, float> visualLengths)
        {
            preparedTime = time;
            framePoses.Clear();
            frameEvents.Clear();
            claimedDrivers.Clear();

            if (!settings.enableOvertakeVisuals ||
                poses == null ||
                visualWidths == null ||
                visualLengths == null)
            {
                return;
            }

            for (int i = 0; i < events.Count; i++)
            {
                ReplayEventDto replayEvent = events[i];
                if (time < replayEvent.startTime -
                        CollisionPlanningWindowSeconds ||
                    time > replayEvent.endTime +
                        CollisionPlanningWindowSeconds)
                {
                    continue;
                }

                frameEvents.Add(replayEvent);
            }

            frameEvents.Sort((a, b) =>
                CompareFrameEvents(a, b, time));
            for (int i = 0; i < frameEvents.Count; i++)
            {
                ReplayEventDto replayEvent = frameEvents[i];
                int overtaker = replayEvent.driverNumbers[0];
                int defender = replayEvent.driverNumbers[1];
                if (claimedDrivers.Contains(overtaker) ||
                    claimedDrivers.Contains(defender))
                {
                    continue;
                }

                if (!TryResolvePair(
                        replayEvent,
                        time,
                        poses,
                        visualWidths,
                        visualLengths,
                        out VisualMotionPose overtakerPose,
                        out VisualMotionPose defenderPose))
                {
                    continue;
                }

                framePoses[overtaker] = overtakerPose;
                framePoses[defender] = defenderPose;
                claimedDrivers.Add(overtaker);
                claimedDrivers.Add(defender);
            }

        }

        public VisualMotionPose Resolve(
            int driverNumber,
            float time,
            IReadOnlyDictionary<int, ReplayCarPose> poses,
            IReadOnlyDictionary<int, float> visualWidths,
            IReadOnlyDictionary<int, float> visualLengths)
        {
            if (float.IsNaN(preparedTime) ||
                !Mathf.Approximately(preparedTime, time))
            {
                PrepareFrame(
                    time,
                    poses,
                    visualWidths,
                    visualLengths);
            }

            return framePoses.TryGetValue(
                    driverNumber,
                    out VisualMotionPose pose)
                ? pose
                : VisualMotionPose.None;
        }

        private bool TryResolvePair(
            ReplayEventDto replayEvent,
            float time,
            IReadOnlyDictionary<int, ReplayCarPose> poses,
            IReadOnlyDictionary<int, float> visualWidths,
            IReadOnlyDictionary<int, float> visualLengths,
            out VisualMotionPose overtakerVisualPose,
            out VisualMotionPose defenderVisualPose)
        {
            overtakerVisualPose = VisualMotionPose.None;
            defenderVisualPose = VisualMotionPose.None;

            int overtakerDriver = replayEvent.driverNumbers[0];
            int defenderDriver = replayEvent.driverNumbers[1];
            if (!poses.TryGetValue(
                    overtakerDriver,
                    out ReplayCarPose overtaker) ||
                !poses.TryGetValue(
                    defenderDriver,
                    out ReplayCarPose defender) ||
                !visualWidths.TryGetValue(
                    overtakerDriver,
                    out float overtakerWidth) ||
                !visualWidths.TryGetValue(
                    defenderDriver,
                    out float defenderWidth) ||
                !visualLengths.TryGetValue(
                    overtakerDriver,
                    out float overtakerLength) ||
                !visualLengths.TryGetValue(
                    defenderDriver,
                    out float defenderLength))
            {
                return false;
            }

            Vector3 forward =
                overtaker.localForward +
                defender.localForward;
            forward.y = 0f;
            if (forward.sqrMagnitude <= 0.000001f)
                forward = overtaker.localForward;
            if (forward.sqrMagnitude <= 0.000001f)
                return false;

            forward.Normalize();
            Vector3 right =
                Vector3.Cross(Vector3.up, forward).normalized;
            int side = ResolvePassingSide(
                replayEvent,
                overtaker,
                defender,
                right,
                overtakerWidth,
                overtakerLength,
                defenderWidth,
                defenderLength);

            EvaluateEventEnvelope(
                replayEvent,
                time,
                out float eventWeight,
                out float eventVelocity);
            EvaluateCollisionEnvelope(
                overtaker,
                defender,
                forward,
                right,
                overtakerWidth,
                overtakerLength,
                defenderWidth,
                defenderLength,
                out float collisionWeight,
                out float collisionVelocity,
                out bool longitudinalOverlap);

            float weight;
            float weightVelocity;
            if (collisionWeight > eventWeight)
            {
                weight = collisionWeight;
                weightVelocity = collisionVelocity;
            }
            else
            {
                weight = eventWeight;
                weightVelocity = eventVelocity;
            }

            if (weight <= 0f &&
                Mathf.Abs(weightVelocity) <= Mathf.Epsilon)
            {
                return false;
            }

            float vehicleWidth = Mathf.Max(
                0.001f,
                (overtakerWidth + defenderWidth) * 0.5f);
            float targetSeparation =
                ProjectedHalfExtent(
                    overtaker,
                    right,
                    overtakerWidth,
                    overtakerLength) +
                ProjectedHalfExtent(
                    defender,
                    right,
                    defenderWidth,
                    defenderLength) +
                vehicleWidth *
                Mathf.Max(
                    0f,
                    settings.targetSeparationInVehicleWidths - 1f);
            float existingSeparation = Vector3.Dot(
                overtaker.rawPosition - defender.rawPosition,
                right * side);
            float missingSeparation = Mathf.Max(
                0f,
                targetSeparation - existingSeparation);
            bool hardSeparation =
                presentationMode == OvertakePresentationMode.Showcase ||
                longitudinalOverlap;
            float correction = hardSeparation
                ? missingSeparation
                : Mathf.Min(
                    missingSeparation,
                    vehicleWidth *
                    Mathf.Max(
                        0f,
                        settings.maximumCorrectionInVehicleWidths));
            float requiredOffset = correction * weight;

            GetOffsetRange(
                overtaker,
                right,
                overtakerWidth,
                overtakerLength,
                out float overtakerMinimum,
                out float overtakerMaximum);
            GetOffsetRange(
                defender,
                right,
                defenderWidth,
                defenderLength,
                out float defenderMinimum,
                out float defenderMaximum);
            ResolveShares(
                replayEvent,
                out float overtakerShare,
                out float defenderShare);
            AllocateSeparation(
                requiredOffset,
                side,
                overtakerShare,
                defenderShare,
                overtakerMinimum,
                overtakerMaximum,
                defenderMinimum,
                defenderMaximum,
                hardSeparation,
                out float overtakerAmount,
                out float defenderAmount);

            float overtakerOffset = side * overtakerAmount;
            float defenderOffset = -side * defenderAmount;
            float resolvedAmount = overtakerAmount + defenderAmount;
            float overtakerVelocityShare = resolvedAmount > 0.000001f
                ? overtakerAmount / resolvedAmount
                : overtakerShare;
            float defenderVelocityShare = resolvedAmount > 0.000001f
                ? defenderAmount / resolvedAmount
                : defenderShare;
            float overtakerVelocity =
                side *
                correction *
                weightVelocity *
                overtakerVelocityShare;
            float defenderVelocity =
                -side *
                correction *
                weightVelocity *
                defenderVelocityShare;
            float confidence = SideConfidence(replayEvent);
            string passingSide = side > 0 ? "Right" : "Left";
            string sideSource = SideSource(replayEvent);

            overtakerVisualPose = new VisualMotionPose(
                overtakerOffset,
                ResolveYaw(overtakerVelocity, overtaker.localSpeed),
                true,
                replayEvent.eventId,
                passingSide,
                sideSource,
                "Overtaker",
                confidence,
                right);
            defenderVisualPose = new VisualMotionPose(
                defenderOffset,
                ResolveYaw(defenderVelocity, defender.localSpeed),
                true,
                replayEvent.eventId,
                passingSide,
                sideSource,
                "Defender",
                confidence,
                right);
            return true;
        }

        private int ResolvePassingSide(
            ReplayEventDto replayEvent,
            ReplayCarPose overtaker,
            ReplayCarPose defender,
            Vector3 right,
            float overtakerWidth,
            float overtakerLength,
            float defenderWidth,
            float defenderLength)
        {
            if (resolvedPassingSides.TryGetValue(
                    replayEvent,
                    out int resolved))
            {
                return resolved;
            }

            int preferred = PassingSide(replayEvent);
            if (IsAuthoritativePassingSide(replayEvent))
            {
                resolvedPassingSides[replayEvent] = preferred;
                return preferred;
            }

            GetOffsetRange(
                overtaker,
                right,
                overtakerWidth,
                overtakerLength,
                out float overtakerMinimum,
                out float overtakerMaximum);
            GetOffsetRange(
                defender,
                right,
                defenderWidth,
                defenderLength,
                out float defenderMinimum,
                out float defenderMaximum);
            float preferredCapacity = PairCapacity(
                preferred,
                overtakerMinimum,
                overtakerMaximum,
                defenderMinimum,
                defenderMaximum);
            float alternateCapacity = PairCapacity(
                -preferred,
                overtakerMinimum,
                overtakerMaximum,
                defenderMinimum,
                defenderMaximum);
            float preferredExisting = Vector3.Dot(
                overtaker.rawPosition - defender.rawPosition,
                right * preferred);
            float alternateExisting = -preferredExisting;
            resolved = alternateExisting + alternateCapacity >
                    preferredExisting + preferredCapacity +
                    Mathf.Epsilon
                ? -preferred
                : preferred;
            resolvedPassingSides[replayEvent] = resolved;
            return resolved;
        }

        private void GetOffsetRange(
            ReplayCarPose pose,
            Vector3 right,
            float width,
            float length,
            out float minimum,
            out float maximum)
        {
            float maximumOffset =
                width *
                Mathf.Max(
                    0f,
                    settings.maximumOffsetInVehicleWidths);
            minimum = -maximumOffset;
            maximum = maximumOffset;
            float safetyMargin =
                width *
                Mathf.Max(
                    0f,
                    settings.targetSeparationInVehicleWidths - 1f);
            if (!fallbackCorridor.TryGetOffsetRange(
                    pose.rawPosition,
                    right,
                    pose.localForward,
                    width,
                    length,
                    safetyMargin,
                    out float corridorMinimum,
                    out float corridorMaximum,
                    true))
            {
                return;
            }

            minimum = Mathf.Max(minimum, corridorMinimum);
            maximum = Mathf.Min(maximum, corridorMaximum);
            if (minimum <= maximum)
                return;

            float collapsed = (minimum + maximum) * 0.5f;
            minimum = collapsed;
            maximum = collapsed;
        }

        private static void AllocateSeparation(
            float required,
            int side,
            float overtakerShare,
            float defenderShare,
            float overtakerMinimum,
            float overtakerMaximum,
            float defenderMinimum,
            float defenderMaximum,
            bool forceExact,
            out float overtakerAmount,
            out float defenderAmount)
        {
            float overtakerCapacity = DirectionalCapacity(
                side,
                overtakerMinimum,
                overtakerMaximum);
            float defenderCapacity = DirectionalCapacity(
                -side,
                defenderMinimum,
                defenderMaximum);
            overtakerAmount = Mathf.Min(
                required * overtakerShare,
                overtakerCapacity);
            defenderAmount = Mathf.Min(
                required * defenderShare,
                defenderCapacity);

            float remaining = Mathf.Max(
                0f,
                required - overtakerAmount - defenderAmount);
            AddWithinCapacity(
                ref overtakerAmount,
                overtakerCapacity,
                ref remaining);
            AddWithinCapacity(
                ref defenderAmount,
                defenderCapacity,
                ref remaining);
            if (!forceExact || remaining <= 0.000001f)
                return;

            overtakerAmount += remaining * overtakerShare;
            defenderAmount += remaining * defenderShare;
        }

        private void EvaluateCollisionEnvelope(
            ReplayCarPose overtaker,
            ReplayCarPose defender,
            Vector3 forward,
            Vector3 right,
            float overtakerWidth,
            float overtakerLength,
            float defenderWidth,
            float defenderLength,
            out float weight,
            out float velocity,
            out bool longitudinalOverlap)
        {
            weight = 0f;
            velocity = 0f;
            longitudinalOverlap = false;
            Vector3 separation =
                overtaker.rawPosition - defender.rawPosition;
            float vehicleWidth = Mathf.Max(
                0.001f,
                (overtakerWidth + defenderWidth) * 0.5f);
            float targetLateralDistance =
                ProjectedHalfExtent(
                    overtaker,
                    right,
                    overtakerWidth,
                    overtakerLength) +
                ProjectedHalfExtent(
                    defender,
                    right,
                    defenderWidth,
                    defenderLength) +
                vehicleWidth *
                Mathf.Max(
                    0f,
                    settings.targetSeparationInVehicleWidths - 1f);
            if (Mathf.Abs(Vector3.Dot(separation, right)) >=
                targetLateralDistance)
            {
                return;
            }

            float overlapDistance = Mathf.Max(
                0.001f,
                ProjectedHalfExtent(
                    overtaker,
                    forward,
                    overtakerWidth,
                    overtakerLength) +
                ProjectedHalfExtent(
                    defender,
                    forward,
                    defenderWidth,
                    defenderLength));
            float releaseDistance =
                overlapDistance +
                Mathf.Max(
                    0.001f,
                    (overtakerLength + defenderLength) * 0.5f);
            float signedDistance = Vector3.Dot(separation, forward);
            float distance = Mathf.Abs(signedDistance);
            longitudinalOverlap = distance <= overlapDistance;
            if (longitudinalOverlap)
            {
                weight = 1f;
                return;
            }
            if (distance >= releaseDistance)
                return;

            float t = Mathf.InverseLerp(
                overlapDistance,
                releaseDistance,
                distance);
            weight = 1f - SmoothStep(t);
            float relativeSpeed =
                overtaker.localSpeed - defender.localSpeed;
            float distanceVelocity =
                Mathf.Sign(signedDistance) * relativeSpeed;
            velocity =
                -SmoothStepDerivative(t) *
                distanceVelocity /
                Mathf.Max(
                    0.0001f,
                    releaseDistance - overlapDistance);
        }

        private void EvaluateEventEnvelope(
            ReplayEventDto replayEvent,
            float time,
            out float weight,
            out float velocity)
        {
            weight = 0f;
            velocity = 0f;
            float duration =
                replayEvent.endTime - replayEvent.startTime;
            if (duration <= 0f ||
                time < replayEvent.startTime ||
                time > replayEvent.endTime)
            {
                return;
            }

            float totalPortion = Mathf.Max(
                0.0001f,
                settings.approachPortion +
                settings.parallelPortion +
                settings.returnPortion);
            float approachPortion = Mathf.Clamp01(
                Mathf.Max(
                    0.0001f,
                    settings.approachPortion) /
                totalPortion);
            float returnStartPortion = Mathf.Clamp01(
                (settings.approachPortion +
                 settings.parallelPortion) /
                totalPortion);
            float anchor = Mathf.Clamp01(
                (replayEvent.anchorTime - replayEvent.startTime) /
                duration);
            float approachEnd = Mathf.Clamp(
                Mathf.Min(approachPortion, anchor),
                0.0001f,
                0.9998f);
            float returnStart = Mathf.Clamp(
                Mathf.Max(returnStartPortion, anchor),
                approachEnd + 0.0001f,
                0.9999f);
            float normalized = Mathf.Clamp01(
                (time - replayEvent.startTime) / duration);
            if (normalized < approachEnd)
            {
                float t = normalized / approachEnd;
                weight = SmoothStep(t);
                velocity =
                    SmoothStepDerivative(t) /
                    (approachEnd * duration);
                return;
            }
            if (normalized <= returnStart)
            {
                weight = 1f;
                return;
            }

            float returnT =
                (normalized - returnStart) /
                (1f - returnStart);
            weight = 1f - SmoothStep(returnT);
            velocity =
                -SmoothStepDerivative(returnT) /
                ((1f - returnStart) * duration);
        }

        private void ResolveShares(
            ReplayEventDto replayEvent,
            out float overtakerShare,
            out float defenderShare)
        {
            float eventTotal =
                replayEvent.overtakerShare +
                replayEvent.defenderShare;
            overtakerShare = eventTotal > 0.0001f
                ? Mathf.Max(0f, replayEvent.overtakerShare)
                : Mathf.Max(0f, settings.overtakerShare);
            defenderShare = eventTotal > 0.0001f
                ? Mathf.Max(0f, replayEvent.defenderShare)
                : Mathf.Max(0f, settings.defenderShare);
            float total = overtakerShare + defenderShare;
            if (total <= 0.0001f)
            {
                overtakerShare = 1f;
                defenderShare = 0f;
                return;
            }

            overtakerShare /= total;
            defenderShare /= total;
        }

        private float ResolveYaw(
            float lateralVelocity,
            float forwardSpeed)
        {
            float yaw = Mathf.Atan2(
                    lateralVelocity,
                    Mathf.Max(0.001f, Mathf.Abs(forwardSpeed))) *
                Mathf.Rad2Deg;
            return Mathf.Clamp(
                yaw,
                -settings.maximumVisualYawDegrees,
                settings.maximumVisualYawDegrees);
        }

        private static float ProjectedHalfExtent(
            ReplayCarPose pose,
            Vector3 axis,
            float vehicleWidth,
            float vehicleLength)
        {
            axis.y = 0f;
            Vector3 forward = pose.localForward;
            forward.y = 0f;
            if (axis.sqrMagnitude <= 0.000001f ||
                forward.sqrMagnitude <= 0.000001f)
            {
                return Mathf.Max(0f, vehicleWidth) * 0.5f;
            }

            axis.Normalize();
            forward.Normalize();
            Vector3 right =
                Vector3.Cross(Vector3.up, forward);
            return
                Mathf.Abs(Vector3.Dot(right, axis)) *
                Mathf.Max(0f, vehicleWidth) * 0.5f +
                Mathf.Abs(Vector3.Dot(forward, axis)) *
                Mathf.Max(0f, vehicleLength) * 0.5f;
        }

        private static float PairCapacity(
            int side,
            float overtakerMinimum,
            float overtakerMaximum,
            float defenderMinimum,
            float defenderMaximum)
        {
            return
                DirectionalCapacity(
                    side,
                    overtakerMinimum,
                    overtakerMaximum) +
                DirectionalCapacity(
                    -side,
                    defenderMinimum,
                    defenderMaximum);
        }

        private static float DirectionalCapacity(
            int direction,
            float minimum,
            float maximum)
        {
            return direction > 0
                ? Mathf.Max(0f, maximum)
                : Mathf.Max(0f, -minimum);
        }

        private static void AddWithinCapacity(
            ref float amount,
            float capacity,
            ref float remaining)
        {
            float addition = Mathf.Min(
                remaining,
                Mathf.Max(0f, capacity - amount));
            amount += addition;
            remaining -= addition;
        }

        private static bool IsValidOvertake(
            ReplayEventDto replayEvent)
        {
            return replayEvent != null &&
                string.Equals(
                    replayEvent.eventType,
                    "Overtake",
                    StringComparison.OrdinalIgnoreCase) &&
                replayEvent.driverNumbers != null &&
                replayEvent.driverNumbers.Length >= 2 &&
                replayEvent.driverNumbers[0] !=
                    replayEvent.driverNumbers[1] &&
                replayEvent.endTime > replayEvent.startTime;
        }

        private static bool IsAuthoritativePassingSide(
            ReplayEventDto replayEvent)
        {
            return
                (string.Equals(
                     replayEvent.passingSide,
                     "Left",
                     StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(
                     replayEvent.passingSide,
                     "Right",
                     StringComparison.OrdinalIgnoreCase)) &&
                !string.Equals(
                    replayEvent.sideSource,
                    "DeterministicFallback",
                    StringComparison.OrdinalIgnoreCase);
        }

        private static int PassingSide(ReplayEventDto replayEvent)
        {
            if (string.Equals(
                    replayEvent.passingSide,
                    "Right",
                    StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }
            if (string.Equals(
                    replayEvent.passingSide,
                    "Left",
                    StringComparison.OrdinalIgnoreCase))
            {
                return -1;
            }

            uint hash = 2166136261;
            string value = replayEvent.eventId ?? string.Empty;
            for (int i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= 16777619;
            }

            return (hash & 1) == 0 ? -1 : 1;
        }

        private static float SideConfidence(
            ReplayEventDto replayEvent)
        {
            if (replayEvent.sideConfidence > 0f)
                return Mathf.Clamp01(replayEvent.sideConfidence);
            if (string.Equals(
                    replayEvent.sideSource,
                    "DeterministicFallback",
                    StringComparison.OrdinalIgnoreCase))
            {
                return 0f;
            }
            if (replayEvent.confidence >= 0f &&
                !string.IsNullOrWhiteSpace(replayEvent.passingSide))
            {
                return Mathf.Clamp01(replayEvent.confidence);
            }

            return Mathf.Clamp01(replayEvent.sideConfidence);
        }

        private static string SideSource(
            ReplayEventDto replayEvent)
        {
            if (!string.IsNullOrWhiteSpace(replayEvent.sideSource))
                return replayEvent.sideSource;

            return IsAuthoritativePassingSide(replayEvent)
                ? "EventMetadata"
                : "DeterministicFallback";
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

        private static float DistanceToEvent(
            ReplayEventDto replayEvent,
            float time)
        {
            if (time < replayEvent.startTime)
                return replayEvent.startTime - time;
            if (time > replayEvent.endTime)
                return time - replayEvent.endTime;

            return 0f;
        }

        private static int CompareFrameEvents(
            ReplayEventDto a,
            ReplayEventDto b,
            float time)
        {
            int result = DistanceToEvent(a, time).CompareTo(
                DistanceToEvent(b, time));
            if (result != 0)
                return result;

            result = Mathf.Abs(a.anchorTime - time).CompareTo(
                Mathf.Abs(b.anchorTime - time));
            return result != 0
                ? result
                : CompareEvents(a, b);
        }

        private static int CompareEvents(
            ReplayEventDto a,
            ReplayEventDto b)
        {
            if (ReferenceEquals(a, b))
                return 0;
            if (a == null)
                return -1;
            if (b == null)
                return 1;

            int result = a.startTime.CompareTo(b.startTime);
            if (result != 0)
                return result;

            result = a.anchorTime.CompareTo(b.anchorTime);
            if (result != 0)
                return result;

            result = a.endTime.CompareTo(b.endTime);
            return result != 0
                ? result
                : string.CompareOrdinal(a.eventId, b.eventId);
        }
    }
}
