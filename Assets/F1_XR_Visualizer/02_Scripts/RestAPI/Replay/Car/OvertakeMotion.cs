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
        private readonly Dictionary<int, List<ReplayEventDto>> eventsByDriver = new();
        private readonly Dictionary<ReplayEventDto, int> resolvedPassingSides = new();
        private readonly OvertakeFallbackCorridor fallbackCorridor = new();
        private OvertakeMotionSettings settings = new();

        public void SetEvents(ReplayEventDto[] source)
        {
            eventsByDriver.Clear();
            resolvedPassingSides.Clear();

            if (source == null || source.Length == 0)
                return;

            ReplayEventDto[] sortedEvents = (ReplayEventDto[])source.Clone();
            Array.Sort(sortedEvents, CompareEvents);

            foreach (ReplayEventDto replayEvent in sortedEvents)
            {
                if (replayEvent == null ||
                    !string.Equals(
                        replayEvent.eventType,
                        "Overtake",
                        StringComparison.OrdinalIgnoreCase) ||
                    replayEvent.driverNumbers == null ||
                    replayEvent.driverNumbers.Length < 2)
                    continue;

                AddDriverEvent(replayEvent.driverNumbers[0], replayEvent);
                if (replayEvent.driverNumbers[1] != replayEvent.driverNumbers[0])
                    AddDriverEvent(replayEvent.driverNumbers[1], replayEvent);
            }
        }

        public void SetSettings(OvertakeMotionSettings source)
        {
            settings = source ?? new OvertakeMotionSettings();
        }

        public void SetFallbackCorridor(
            IReadOnlyList<Vector3> centerline,
            float roadWidth,
            bool loop)
        {
            fallbackCorridor.Set(centerline, roadWidth, loop);
            resolvedPassingSides.Clear();
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
            resolvedPassingSides.Clear();
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

        public VisualMotionPose Resolve(
            int driverNumber,
            float time,
            IReadOnlyDictionary<int, ReplayCarPose> poses,
            IReadOnlyDictionary<int, float> visualWidths,
            IReadOnlyDictionary<int, float> visualLengths)
        {
            if (!settings.enableOvertakeVisuals ||
                !eventsByDriver.TryGetValue(
                    driverNumber,
                    out List<ReplayEventDto> driverEvents))
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
            Vector3 strongestDirection = Vector3.right;

            foreach (ReplayEventDto replayEvent in driverEvents)
            {
                if (!TryGetEventRole(
                        replayEvent,
                        driverNumber,
                        out bool overtaker))
                    continue;

                if (time > replayEvent.endTime)
                    continue;

                int side = ResolvePassingSide(
                    replayEvent,
                    poses,
                    visualWidths,
                    visualLengths);
                float minimumApproachDuration =
                    RequiredApproachDuration(
                        replayEvent,
                        side,
                        poses,
                        visualWidths,
                        visualLengths);
                EvaluateEnvelope(
                    replayEvent,
                    time,
                    minimumApproachDuration,
                    out float weight,
                    out float velocity,
                    out _);
                if (IsCollisionGuardEvent(
                        driverEvents,
                        replayEvent,
                        driverNumber,
                        time))
                {
                    CollisionGuardEnvelope(
                        replayEvent,
                        poses,
                        visualWidths,
                        visualLengths,
                        minimumApproachDuration,
                        out float guardWeight,
                        out float guardVelocity);
                    if (guardWeight > weight)
                    {
                        weight = guardWeight;
                        velocity = guardVelocity;
                    }
                }
                if (weight <= 0f && Mathf.Abs(velocity) <= Mathf.Epsilon)
                    continue;

                if (!TryResolvePairMotion(
                    replayEvent,
                    driverNumber,
                    side,
                    weight,
                    velocity,
                    poses,
                    visualWidths,
                    visualLengths,
                    out float offset,
                    out float contributionVelocity,
                    out Vector3 lateralDirection))
                {
                    continue;
                }
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
                    strongestDirection = lateralDirection;
                }
            }

            float vehicleWidth =
                GetVehicleWidth(
                    driverNumber,
                    visualWidths);
            float vehicleLength =
                GetVehicleLength(
                    driverNumber,
                    visualLengths);
            if (strongestEvent == null)
                return VisualMotionPose.None;

            float lateralOffset = settings.overlapBlendMode == OvertakeBlendMode.Strongest
                ? strongestOffset
                : sumOffset;
            float lateralVelocity = settings.overlapBlendMode == OvertakeBlendMode.Strongest
                ? strongestVelocity
                : sumVelocity;
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

            if (string.Equals(
                    strongestRole,
                    "Overtaker",
                    StringComparison.Ordinal) &&
                poses != null &&
                poses.TryGetValue(driverNumber, out ReplayCarPose corridorPose))
            {
                float safetyMargin =
                    vehicleWidth *
                    Mathf.Max(
                        0f,
                        settings.targetSeparationInVehicleWidths - 1f);
                float requestedOffset = lateralOffset;
                lateralOffset = fallbackCorridor.ClampOffset(
                    corridorPose.rawPosition,
                    strongestDirection,
                    corridorPose.localForward,
                    vehicleWidth,
                    vehicleLength,
                    safetyMargin,
                    lateralOffset,
                    true);
                if (!Mathf.Approximately(
                        lateralOffset,
                        requestedOffset))
                {
                    lateralVelocity = 0f;
                }
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
                strongestConfidence,
                strongestDirection);
        }

        private void AddDriverEvent(
            int driverNumber,
            ReplayEventDto replayEvent)
        {
            if (!eventsByDriver.TryGetValue(
                    driverNumber,
                    out List<ReplayEventDto> driverEvents))
            {
                driverEvents = new List<ReplayEventDto>();
                eventsByDriver.Add(driverNumber, driverEvents);
            }

            driverEvents.Add(replayEvent);
        }

        private static bool IsCollisionGuardEvent(
            IReadOnlyList<ReplayEventDto> driverEvents,
            ReplayEventDto candidate,
            int driverNumber,
            float time)
        {
            int counterpart = CounterpartDriver(
                candidate,
                driverNumber);
            if (counterpart <= 0)
                return false;

            float candidateDistance =
                DistanceToEvent(candidate, time);
            for (int i = 0; i < driverEvents.Count; i++)
            {
                ReplayEventDto other = driverEvents[i];
                if (ReferenceEquals(other, candidate) ||
                    CounterpartDriver(other, driverNumber) !=
                    counterpart)
                {
                    continue;
                }

                float otherDistance =
                    DistanceToEvent(other, time);
                if (otherDistance <
                        candidateDistance -
                        Mathf.Epsilon ||
                    Mathf.Approximately(
                        otherDistance,
                        candidateDistance) &&
                    CompareEventIds(other, candidate) < 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static int CounterpartDriver(
            ReplayEventDto replayEvent,
            int driverNumber)
        {
            if (!TryGetEventRole(
                    replayEvent,
                    driverNumber,
                    out bool overtaker))
            {
                return 0;
            }

            return overtaker
                ? replayEvent.driverNumbers[1]
                : replayEvent.driverNumbers[0];
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

        private bool TryResolvePairMotion(
            ReplayEventDto replayEvent,
            int driverNumber,
            int side,
            float weight,
            float velocity,
            IReadOnlyDictionary<int, ReplayCarPose> poses,
            IReadOnlyDictionary<int, float> visualWidths,
            IReadOnlyDictionary<int, float> visualLengths,
            out float driverOffset,
            out float driverVelocity,
            out Vector3 lateralDirection)
        {
            driverOffset = 0f;
            driverVelocity = 0f;
            lateralDirection = Vector3.right;
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
            {
                return false;
            }

            float vehicleWidth = Mathf.Max(
                0.001f,
                (overtakerWidth + defenderWidth) * 0.5f);
            float maximumCorrection = vehicleWidth * Mathf.Max(
                0f,
                settings.maximumCorrectionInVehicleWidths);

            Vector3 forward = overtaker.localForward + defender.localForward;
            if (forward.sqrMagnitude <= 0.000001f)
                forward = overtaker.localForward;
            if (forward.sqrMagnitude <= 0.000001f)
                return false;

            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            lateralDirection = right;
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
            float correction = Mathf.Clamp(
                targetSeparation - existingSeparation,
                0f,
                maximumCorrection);
            float desiredOvertakerOffset =
                side *
                correction *
                weight;
            float desiredDefenderOffset = 0f;

            GetOffsetRange(
                overtaker,
                right,
                overtakerWidth,
                overtakerLength,
                true,
                out float overtakerMinimum,
                out float overtakerMaximum);
            float overtakerOffset = Mathf.Clamp(
                desiredOvertakerOffset,
                overtakerMinimum,
                overtakerMaximum);
            float defenderOffset =
                desiredDefenderOffset;

            bool isOvertaker =
                replayEvent.driverNumbers[0] ==
                driverNumber;
            float desiredOffset = isOvertaker
                ? desiredOvertakerOffset
                : desiredDefenderOffset;
            driverOffset = isOvertaker
                ? overtakerOffset
                : defenderOffset;
            float desiredVelocity =
                (isOvertaker ? side : 0f) *
                correction *
                velocity;
            driverVelocity = Mathf.Approximately(
                    driverOffset,
                    desiredOffset)
                ? desiredVelocity
                : 0f;
            return true;

            void GetOffsetRange(
                ReplayCarPose pose,
                Vector3 offsetDirection,
                float width,
                float length,
                bool includeFutureLimits,
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
                        offsetDirection,
                        pose.localForward,
                        width,
                        length,
                        safetyMargin,
                        out float corridorMinimum,
                        out float corridorMaximum,
                        includeFutureLimits))
                {
                    return;
                }

                minimum = Mathf.Max(
                    minimum,
                    corridorMinimum);
                maximum = Mathf.Min(
                    maximum,
                    corridorMaximum);
                if (minimum <= maximum)
                    return;

                float collapsed =
                    (minimum + maximum) * 0.5f;
                minimum = collapsed;
                maximum = collapsed;
            }
        }

        private float RequiredApproachDuration(
            ReplayEventDto replayEvent,
            int side,
            IReadOnlyDictionary<int, ReplayCarPose> poses,
            IReadOnlyDictionary<int, float> visualWidths,
            IReadOnlyDictionary<int, float> visualLengths)
        {
            if (replayEvent.driverNumbers == null ||
                replayEvent.driverNumbers.Length < 2 ||
                poses == null ||
                !poses.TryGetValue(
                    replayEvent.driverNumbers[0],
                    out ReplayCarPose overtaker) ||
                !TryResolvePairMotion(
                    replayEvent,
                    replayEvent.driverNumbers[0],
                    side,
                    1f,
                    0f,
                    poses,
                    visualWidths,
                    visualLengths,
                    out float fullOffset,
                    out _,
                    out _))
            {
                return 0f;
            }

            float maximumYaw = Mathf.Clamp(
                Mathf.Abs(settings.maximumVisualYawDegrees),
                0f,
                89f);
            float maximumLateralSpeed =
                Mathf.Abs(overtaker.localSpeed) *
                Mathf.Tan(maximumYaw * Mathf.Deg2Rad);
            if (maximumLateralSpeed <= 0.000001f)
                return 0f;

            float peakEnvelopeSpeed =
                SmoothStepDerivative(0.5f);
            return
                Mathf.Abs(fullOffset) *
                peakEnvelopeSpeed /
                maximumLateralSpeed;
        }

        private int ResolvePassingSide(
            ReplayEventDto replayEvent,
            IReadOnlyDictionary<int, ReplayCarPose> poses,
            IReadOnlyDictionary<int, float> visualWidths,
            IReadOnlyDictionary<int, float> visualLengths)
        {
            if (resolvedPassingSides.TryGetValue(
                    replayEvent,
                    out int resolved))
            {
                return resolved;
            }

            int preferred = PassingSide(replayEvent);
            if (!fallbackCorridor.IsAvailable ||
                replayEvent.driverNumbers == null ||
                replayEvent.driverNumbers.Length < 2 ||
                poses == null ||
                visualWidths == null ||
                visualLengths == null ||
                !poses.TryGetValue(
                    replayEvent.driverNumbers[0],
                    out ReplayCarPose overtaker) ||
                !poses.TryGetValue(
                    replayEvent.driverNumbers[1],
                    out ReplayCarPose defender) ||
                !visualWidths.TryGetValue(
                    replayEvent.driverNumbers[0],
                    out float overtakerWidth) ||
                !visualWidths.TryGetValue(
                    replayEvent.driverNumbers[1],
                    out float defenderWidth) ||
                !visualLengths.TryGetValue(
                    replayEvent.driverNumbers[0],
                    out float overtakerLength) ||
                !visualLengths.TryGetValue(
                    replayEvent.driverNumbers[1],
                    out float defenderLength))
            {
                resolvedPassingSides[replayEvent] =
                    preferred;
                return preferred;
            }

            Vector3 forward =
                overtaker.localForward +
                defender.localForward;
            if (forward.sqrMagnitude <= 0.000001f)
                forward = overtaker.localForward;
            if (forward.sqrMagnitude <= 0.000001f)
            {
                resolvedPassingSides[replayEvent] =
                    preferred;
                return preferred;
            }

            forward.Normalize();
            Vector3 right =
                Vector3.Cross(Vector3.up, forward).normalized;
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
            float safetyMargin =
                vehicleWidth *
                Mathf.Max(
                    0f,
                    settings.targetSeparationInVehicleWidths - 1f);
            float preferredOvertakerResult =
                AchievableSeparation(preferred);
            float alternateOvertakerResult =
                AchievableSeparation(-preferred);
            bool preferredFitsOvertaker =
                preferredOvertakerResult +
                Mathf.Epsilon >=
                targetSeparation;
            bool alternateFitsOvertaker =
                alternateOvertakerResult +
                Mathf.Epsilon >=
                targetSeparation;
            if (!preferredFitsOvertaker &&
                alternateFitsOvertaker)
            {
                resolved = -preferred;
            }
            else if (!preferredFitsOvertaker &&
                     !alternateFitsOvertaker &&
                     alternateOvertakerResult >
                     preferredOvertakerResult +
                     Mathf.Epsilon)
            {
                resolved = -preferred;
            }
            else
            {
                resolved = preferred;
            }

            resolvedPassingSides[replayEvent] =
                resolved;
            return resolved;

            float AchievableSeparation(int side)
            {
                float existing = Vector3.Dot(
                    overtaker.rawPosition -
                    defender.rawPosition,
                    right * side);
                float correction = Mathf.Max(
                    0f,
                    targetSeparation - existing);
                float overtakerCapacity =
                    Mathf.Min(
                        overtakerWidth *
                        Mathf.Max(
                            0f,
                            settings.maximumOffsetInVehicleWidths),
                        Mathf.Min(
                            fallbackCorridor.AvailableOffset(
                                overtaker.rawPosition,
                                right,
                                overtaker.localForward,
                                overtakerWidth,
                                overtakerLength,
                                safetyMargin,
                                side,
                                true),
                            fallbackCorridor.MinimumAvailableOffset(
                                overtakerWidth,
                                overtakerLength,
                                safetyMargin,
                                side)));
                float overtakerCorrection =
                    Mathf.Min(
                        correction,
                        overtakerCapacity);
                float result =
                    existing +
                    overtakerCorrection;
                return result;
            }
        }

        private void CollisionGuardEnvelope(
            ReplayEventDto replayEvent,
            IReadOnlyDictionary<int, ReplayCarPose> poses,
            IReadOnlyDictionary<int, float> visualWidths,
            IReadOnlyDictionary<int, float> visualLengths,
            float approachDuration,
            out float weight,
            out float velocity)
        {
            weight = 0f;
            velocity = 0f;
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
                return;

            Vector3 forward = overtaker.localForward + defender.localForward;
            if (forward.sqrMagnitude <= 0.000001f)
                forward = overtaker.localForward;
            if (forward.sqrMagnitude <= 0.000001f)
                return;

            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 separation = overtaker.rawPosition - defender.rawPosition;
            float lateralDistance = Mathf.Abs(Vector3.Dot(separation, right));
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
            if (lateralDistance >= targetLateralDistance)
                return;

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
            float longitudinalSeparation =
                Vector3.Dot(separation, forward);
            if (Mathf.Abs(longitudinalSeparation) <= overlapDistance)
            {
                weight = 1f;
                return;
            }

            float relativeSpeed =
                overtaker.localSpeed -
                defender.localSpeed;
            if (relativeSpeed <= 0.000001f ||
                approachDuration <= 0.000001f)
            {
                return;
            }

            if (longitudinalSeparation < -overlapDistance)
            {
                float timeToOverlap =
                    (-overlapDistance -
                     longitudinalSeparation) /
                    relativeSpeed;
                if (timeToOverlap >= approachDuration)
                    return;

                float progress = 1f -
                    Mathf.Clamp01(
                        timeToOverlap /
                        approachDuration);
                weight = SmoothStep(progress);
                velocity =
                    SmoothStepDerivative(progress) /
                    approachDuration;
                return;
            }

            float timeSinceClear =
                (longitudinalSeparation -
                 overlapDistance) /
                relativeSpeed;
            if (timeSinceClear >= approachDuration)
                return;

            float releaseProgress = Mathf.Clamp01(
                timeSinceClear /
                approachDuration);
            weight = 1f -
                SmoothStep(releaseProgress);
            velocity =
                -SmoothStepDerivative(releaseProgress) /
                approachDuration;
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
                return Mathf.Max(
                    0f,
                    vehicleWidth) * 0.5f;
            }

            axis.Normalize();
            forward.Normalize();
            Vector3 right =
                Vector3.Cross(
                    Vector3.up,
                    forward);
            return
                Mathf.Abs(
                    Vector3.Dot(
                        right,
                        axis)) *
                Mathf.Max(0f, vehicleWidth) * 0.5f +
                Mathf.Abs(
                    Vector3.Dot(
                        forward,
                        axis)) *
                Mathf.Max(0f, vehicleLength) * 0.5f;
        }

        private void EvaluateEnvelope(
            ReplayEventDto replayEvent,
            float time,
            float minimumApproachDuration,
            out float weight,
            out float velocity,
            out bool returning)
        {
            weight = 0f;
            velocity = 0f;
            returning = false;

            float duration = replayEvent.endTime - replayEvent.startTime;
            if (duration <= 0f || time > replayEvent.endTime)
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

            float approachEndTime =
                replayEvent.startTime +
                approachEnd *
                duration;
            float approachDuration = Mathf.Max(
                approachEndTime -
                replayEvent.startTime,
                minimumApproachDuration);
            float approachStartTime =
                approachEndTime -
                approachDuration;
            if (time < approachStartTime)
                return;

            if (time < approachEndTime)
            {
                float t = Mathf.InverseLerp(
                    approachStartTime,
                    approachEndTime,
                    time);
                weight = SmoothStep(t);
                velocity =
                    SmoothStepDerivative(t) /
                    approachDuration;
                return;
            }

            float normalized = Mathf.Clamp01(
                (time - replayEvent.startTime) /
                duration);
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

        private static bool TryGetEventRole(
            ReplayEventDto replayEvent,
            int driverNumber,
            out bool overtaker)
        {
            overtaker = false;
            if (replayEvent == null ||
                !string.Equals(
                    replayEvent.eventType,
                    "Overtake",
                    StringComparison.OrdinalIgnoreCase) ||
                replayEvent.driverNumbers == null ||
                replayEvent.driverNumbers.Length < 2 ||
                replayEvent.endTime <= replayEvent.startTime)
            {
                return false;
            }

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

        private static float GetVehicleLength(
            int driverNumber,
            IReadOnlyDictionary<int, float> visualLengths)
        {
            if (visualLengths == null ||
                !visualLengths.TryGetValue(
                    driverNumber,
                    out float length))
            {
                return 0f;
            }

            return Mathf.Max(0f, length);
        }

        private static float SmoothStep(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * value *
                (value * (value * 6f - 15f) + 10f);
        }

        private static float SmoothStepDerivative(float value)
        {
            value = Mathf.Clamp01(value);
            float inverse = 1f - value;
            return 30f *
                value * value *
                inverse * inverse;
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
