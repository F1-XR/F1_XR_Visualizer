using System;
using System.Collections.Generic;
using F1XR.RestAPI.Api;
using F1XR.RestAPI.Utility;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    internal enum CollisionEvidenceTier
    {
        ContactUnresolved,
        ObservedContactRequiresReconstruction,
        ObservedContactAndPost
    }

    internal enum CollisionEvidenceSliceKind
    {
        CorridorStart,
        VehicleReveal,
        PreContact,
        Contact,
        ImmediateResponse,
        VehicleHold,
        CorridorEnd
    }

    internal sealed class CollisionTrajectoryForensicsOptions
    {
        public float locationUnitsPerMeter = 10f;
        public float maximumSourceGapSeconds = 0.5f;
        public float maximumContactSeparationMeters = 3f;
        public float contactSearchEdgeMarginSeconds = 0.5f;
        public float contactDistanceProbeSeconds = 0.25f;
        public float minimumContactDistanceChangeMeters = 2f;
        public float contactUniquenessWindowSeconds = 0.5f;
        public float minimumOtherCandidateSeparationMeters = 1f;
        public float maximumAnchorOffsetSeconds = 8f;
        public float smoothingContextLeadSeconds = 1.4f;
        public float smoothingContextTailSeconds = 0.85f;
        public float visibleLeadSeconds = 0.9f;
        public float visibleTailSeconds = 0.45f;
        public float vehicleRevealLeadSeconds = 0.55f;
        public float vehicleHoldTailSeconds = 0.35f;
        public float visibleSampleStepSeconds = 0.05f;
        public float maximumCurveDeviationMeters = 0.3f;
        public float maximumObservedPostSpeedMetersPerSecond = 120f;

        internal CollisionTrajectoryForensicsOptions Normalized()
        {
            float safeVisibleLead = Mathf.Max(0f, visibleLeadSeconds);
            float safeVisibleTail = Mathf.Max(0f, visibleTailSeconds);
            return new CollisionTrajectoryForensicsOptions
            {
                locationUnitsPerMeter = Mathf.Max(
                    0.001f,
                    locationUnitsPerMeter),
                maximumSourceGapSeconds = Mathf.Max(
                    0.01f,
                    maximumSourceGapSeconds),
                maximumContactSeparationMeters = Mathf.Max(
                    0f,
                    maximumContactSeparationMeters),
                contactSearchEdgeMarginSeconds = Mathf.Max(
                    0f,
                    contactSearchEdgeMarginSeconds),
                contactDistanceProbeSeconds = Mathf.Max(
                    0.01f,
                    contactDistanceProbeSeconds),
                minimumContactDistanceChangeMeters = Mathf.Max(
                    0f,
                    minimumContactDistanceChangeMeters),
                contactUniquenessWindowSeconds = Mathf.Max(
                    0f,
                    contactUniquenessWindowSeconds),
                minimumOtherCandidateSeparationMeters = Mathf.Max(
                    0f,
                    minimumOtherCandidateSeparationMeters),
                maximumAnchorOffsetSeconds = Mathf.Max(
                    0f,
                    maximumAnchorOffsetSeconds),
                smoothingContextLeadSeconds = Mathf.Max(
                    safeVisibleLead,
                    smoothingContextLeadSeconds),
                smoothingContextTailSeconds = Mathf.Max(
                    safeVisibleTail,
                    smoothingContextTailSeconds),
                visibleLeadSeconds = safeVisibleLead,
                visibleTailSeconds = safeVisibleTail,
                vehicleRevealLeadSeconds = Mathf.Clamp(
                    vehicleRevealLeadSeconds,
                    0f,
                    safeVisibleLead),
                vehicleHoldTailSeconds = Mathf.Clamp(
                    vehicleHoldTailSeconds,
                    0f,
                    safeVisibleTail),
                visibleSampleStepSeconds = Mathf.Clamp(
                    visibleSampleStepSeconds,
                    0.01f,
                    0.25f),
                maximumCurveDeviationMeters = Mathf.Max(
                    0f,
                    maximumCurveDeviationMeters),
                maximumObservedPostSpeedMetersPerSecond = Mathf.Max(
                    1f,
                    maximumObservedPostSpeedMetersPerSecond)
            };
        }
    }

    internal readonly struct CollisionForensicTelemetry
    {
        public CollisionForensicTelemetry(
            bool available,
            float speedKph,
            float throttlePercent,
            float rpm,
            int gear,
            int brake,
            int drs,
            float sourceBeforeTime,
            float sourceAfterTime)
        {
            Available = available;
            SpeedKph = speedKph;
            ThrottlePercent = throttlePercent;
            Rpm = rpm;
            Gear = gear;
            Brake = brake;
            Drs = drs;
            SourceBeforeTime = sourceBeforeTime;
            SourceAfterTime = sourceAfterTime;
        }

        public bool Available { get; }
        public float SpeedKph { get; }
        public float ThrottlePercent { get; }
        public float Rpm { get; }
        public int Gear { get; }
        public int Brake { get; }
        public int Drs { get; }
        public float SourceBeforeTime { get; }
        public float SourceAfterTime { get; }
        public float SourceGapSeconds =>
            Mathf.Max(0f, SourceAfterTime - SourceBeforeTime);
    }

    internal readonly struct CollisionTrajectorySample
    {
        public CollisionTrajectorySample(
            float time,
            Vector3 sourcePosition,
            Vector3 sourceTangent,
            CollisionForensicTelemetry telemetry)
        {
            Time = time;
            SourcePosition = sourcePosition;
            SourceTangent = sourceTangent;
            Telemetry = telemetry;
        }

        public float Time { get; }

        // Uses the same axis order and scale as ReplayCoordinate.ToUnity().
        public Vector3 SourcePosition { get; }
        public Vector3 SourceTangent { get; }
        public CollisionForensicTelemetry Telemetry { get; }
    }

    internal readonly struct CollisionContactEvidence
    {
        public CollisionContactEvidence(
            bool valid,
            float time,
            float separationMeters,
            Vector3 firstSourcePosition,
            Vector3 secondSourcePosition)
        {
            Valid = valid;
            Time = time;
            SeparationMeters = separationMeters;
            FirstSourcePosition = firstSourcePosition;
            SecondSourcePosition = secondSourcePosition;
        }

        public bool Valid { get; }
        public float Time { get; }
        public float SeparationMeters { get; }
        public Vector3 FirstSourcePosition { get; }
        public Vector3 SecondSourcePosition { get; }
        public Vector3 MidpointSourcePosition =>
            (FirstSourcePosition + SecondSourcePosition) * 0.5f;
    }

    internal readonly struct CollisionEvidenceSlice
    {
        public CollisionEvidenceSlice(
            CollisionEvidenceSliceKind kind,
            float time,
            CollisionTrajectorySample first,
            CollisionTrajectorySample second)
        {
            Kind = kind;
            Time = time;
            First = first;
            Second = second;
        }

        public CollisionEvidenceSliceKind Kind { get; }
        public float Time { get; }
        public CollisionTrajectorySample First { get; }
        public CollisionTrajectorySample Second { get; }
    }

    internal sealed class CollisionObservedTrajectory
    {
        private readonly CollisionTrajectoryForensics.CollisionSampleSeries
            source;
        private readonly CollisionTrajectoryForensics.CollisionBoundedPath
            preContact;
        private readonly CollisionTrajectoryForensics.CollisionBoundedPath
            postContact;
        private readonly CollisionTrajectorySample[] visibleSamples;

        internal CollisionObservedTrajectory(
            int driverNumber,
            float contactTime,
            float visibleStartTime,
            float visibleEndTime,
            CollisionTrajectoryForensics.CollisionSampleSeries source,
            CollisionTrajectoryForensics.CollisionBoundedPath preContact,
            CollisionTrajectoryForensics.CollisionBoundedPath postContact,
            CollisionTrajectorySample[] visibleSamples)
        {
            DriverNumber = driverNumber;
            ContactTime = contactTime;
            VisibleStartTime = visibleStartTime;
            VisibleEndTime = visibleEndTime;
            this.source = source;
            this.preContact = preContact;
            this.postContact = postContact;
            this.visibleSamples = visibleSamples ??
                Array.Empty<CollisionTrajectorySample>();
        }

        public int DriverNumber { get; }
        public float ContactTime { get; }
        public float VisibleStartTime { get; }
        public float VisibleEndTime { get; }
        public IReadOnlyList<CollisionTrajectorySample> VisibleSamples =>
            visibleSamples;
        public bool HasObservedPost => postContact != null;

        public bool TryEvaluate(
            float time,
            out CollisionTrajectorySample sample)
        {
            sample = default;
            CollisionTrajectoryForensics.CollisionBoundedPath path =
                time <= ContactTime
                ? preContact
                : postContact;
            if (path == null ||
                !path.TryEvaluate(time, out Vector3 rawPosition))
            {
                return false;
            }

            path.TryEvaluateTangent(time, out Vector3 rawTangent);
            source.TryEvaluateTelemetry(
                time,
                out CollisionForensicTelemetry telemetry);
            sample = new CollisionTrajectorySample(
                time,
                CollisionTrajectoryForensics.ToSourcePosition(rawPosition),
                CollisionTrajectoryForensics.ToSourceTangent(rawTangent),
                telemetry);
            return true;
        }

        public bool TryGetTelemetry(
            float time,
            out CollisionForensicTelemetry telemetry)
        {
            return source.TryEvaluateTelemetry(time, out telemetry);
        }
    }

    internal sealed class CollisionTrajectoryAnalysis
    {
        private readonly CollisionEvidenceSlice[] evidenceSlices;

        internal CollisionTrajectoryAnalysis(
            CollisionEvidenceTier tier,
            float metadataAnchorTime,
            CollisionContactEvidence contact,
            float vehicleRevealTime,
            float vehicleHoldTime,
            CollisionObservedTrajectory first,
            CollisionObservedTrajectory second,
            CollisionEvidenceSlice[] evidenceSlices)
        {
            Tier = tier;
            MetadataAnchorTime = metadataAnchorTime;
            Contact = contact;
            VehicleRevealTime = vehicleRevealTime;
            VehicleHoldTime = vehicleHoldTime;
            First = first;
            Second = second;
            this.evidenceSlices = evidenceSlices ??
                Array.Empty<CollisionEvidenceSlice>();
        }

        public CollisionEvidenceTier Tier { get; }
        public float MetadataAnchorTime { get; }
        public CollisionContactEvidence Contact { get; }
        public float PresentationTime => Contact.Time;
        public float VehicleRevealTime { get; }
        public float VehicleHoldTime { get; }
        public bool HasObservedContact => Contact.Valid;
        public bool HasObservedPost =>
            Tier == CollisionEvidenceTier.ObservedContactAndPost;
        public bool RequiresReconstructedPost =>
            Tier ==
            CollisionEvidenceTier.ObservedContactRequiresReconstruction;
        public CollisionObservedTrajectory First { get; }
        public CollisionObservedTrajectory Second { get; }
        public IReadOnlyList<CollisionEvidenceSlice> EvidenceSlices =>
            evidenceSlices;

        public bool TryEvaluate(
            int driverNumber,
            float time,
            out CollisionTrajectorySample sample)
        {
            if (First != null && First.DriverNumber == driverNumber)
                return First.TryEvaluate(time, out sample);
            if (Second != null && Second.DriverNumber == driverNumber)
                return Second.TryEvaluate(time, out sample);

            sample = default;
            return false;
        }

        public bool TryGetTelemetry(
            int driverNumber,
            float time,
            out CollisionForensicTelemetry telemetry)
        {
            if (First != null && First.DriverNumber == driverNumber)
                return First.TryGetTelemetry(time, out telemetry);
            if (Second != null && Second.DriverNumber == driverNumber)
                return Second.TryGetTelemetry(time, out telemetry);

            telemetry = default;
            return false;
        }
    }

    internal static class CollisionTrajectoryForensics
    {
        private const float DuplicateTimeEpsilon = 0.001f;
        private const float PositionEpsilon = 0.0001f;
        private const float TangentProbeSeconds = 0.01f;

        public static bool TryAnalyze(
            IReadOnlyList<LocationSample> firstSamples,
            int firstDriverNumber,
            IReadOnlyList<LocationSample> secondSamples,
            int secondDriverNumber,
            float metadataAnchorTime,
            float fixtureStartTime,
            float fixtureEndTime,
            out CollisionTrajectoryAnalysis analysis)
        {
            return TryAnalyze(
                firstSamples,
                firstDriverNumber,
                secondSamples,
                secondDriverNumber,
                metadataAnchorTime,
                fixtureStartTime,
                fixtureEndTime,
                new CollisionTrajectoryForensicsOptions(),
                out analysis);
        }

        public static bool TryAnalyze(
            IReadOnlyList<LocationSample> firstSamples,
            int firstDriverNumber,
            IReadOnlyList<LocationSample> secondSamples,
            int secondDriverNumber,
            float metadataAnchorTime,
            float fixtureStartTime,
            float fixtureEndTime,
            CollisionTrajectoryForensicsOptions options,
            out CollisionTrajectoryAnalysis analysis)
        {
            analysis = null;
            CollisionTrajectoryForensicsOptions safe =
                (options ?? new CollisionTrajectoryForensicsOptions())
                .Normalized();
            float searchStart = Mathf.Min(
                fixtureStartTime,
                fixtureEndTime);
            float searchEnd = Mathf.Max(
                fixtureStartTime,
                fixtureEndTime);
            if (searchEnd - searchStart <= DuplicateTimeEpsilon)
                return false;

            CollisionSampleSeries first = CollisionSampleSeries.Create(
                firstSamples,
                firstDriverNumber);
            CollisionSampleSeries second = CollisionSampleSeries.Create(
                secondSamples,
                secondDriverNumber);
            if (first == null || second == null)
                return false;

            List<CollisionContactCandidate> candidates =
                FindContactCandidates(
                    first,
                    second,
                    searchStart,
                    searchEnd,
                    safe,
                    metadataAnchorTime);
            CollisionContactCandidate candidate = candidates.Count > 0
                ? candidates[0]
                : default;
            bool contactValid = candidates.Count > 0 &&
                IsContactValid(
                    candidate,
                    candidates,
                    first,
                    second,
                    metadataAnchorTime,
                    searchStart,
                    searchEnd,
                    safe);
            float presentationTime = contactValid
                ? candidate.Time
                : metadataAnchorTime;

            bool observedPost = contactValid &&
                HasValidObservedPost(first, presentationTime, safe) &&
                HasValidObservedPost(second, presentationTime, safe);
            CollisionEvidenceTier tier = !contactValid
                ? CollisionEvidenceTier.ContactUnresolved
                : observedPost
                    ? CollisionEvidenceTier.ObservedContactAndPost
                    : CollisionEvidenceTier
                        .ObservedContactRequiresReconstruction;

            if (!TryBuildTrajectory(
                    first,
                    presentationTime,
                    observedPost,
                    safe,
                    out CollisionObservedTrajectory firstTrajectory) ||
                !TryBuildTrajectory(
                    second,
                    presentationTime,
                    observedPost,
                    safe,
                    out CollisionObservedTrajectory secondTrajectory))
            {
                return false;
            }

            if (!TryEvaluateLinearPair(
                    first,
                    second,
                    presentationTime,
                    safe.maximumSourceGapSeconds,
                    out Vector3 firstContactRaw,
                    out Vector3 secondContactRaw))
            {
                return false;
            }

            float separationMeters = Vector3.Distance(
                    firstContactRaw,
                    secondContactRaw) /
                safe.locationUnitsPerMeter;
            CollisionContactEvidence contact =
                new CollisionContactEvidence(
                    contactValid,
                    presentationTime,
                    separationMeters,
                    ToSourcePosition(firstContactRaw),
                    ToSourcePosition(secondContactRaw));
            float vehicleRevealTime = presentationTime -
                safe.vehicleRevealLeadSeconds;
            float vehicleHoldTime = observedPost
                ? presentationTime + safe.vehicleHoldTailSeconds
                : presentationTime;
            CollisionEvidenceSlice[] slices = BuildEvidenceSlices(
                firstTrajectory,
                secondTrajectory,
                presentationTime,
                observedPost,
                safe);
            analysis = new CollisionTrajectoryAnalysis(
                tier,
                metadataAnchorTime,
                contact,
                vehicleRevealTime,
                vehicleHoldTime,
                firstTrajectory,
                secondTrajectory,
                slices);
            return true;
        }

        internal static Vector3 ToSourcePosition(Vector3 rawPosition)
        {
            return new Vector3(
                rawPosition.x,
                rawPosition.z,
                rawPosition.y) * ReplayCoordinate.scale;
        }

        internal static Vector3 ToSourceTangent(Vector3 rawTangent)
        {
            Vector3 result = new(
                rawTangent.x,
                rawTangent.z,
                rawTangent.y);
            return result.sqrMagnitude > PositionEpsilon
                ? result.normalized
                : Vector3.forward;
        }

        private static List<CollisionContactCandidate>
            FindContactCandidates(
                CollisionSampleSeries first,
                CollisionSampleSeries second,
                float searchStart,
                float searchEnd,
                CollisionTrajectoryForensicsOptions options,
                float metadataAnchorTime)
        {
            List<float> times = new();
            times.Add(searchStart);
            times.Add(searchEnd);
            first.AddTimes(searchStart, searchEnd, times);
            second.AddTimes(searchStart, searchEnd, times);
            times.Sort();
            RemoveDuplicateTimes(times);

            List<CollisionContactCandidate> candidates = new();
            for (int i = 0; i < times.Count - 1; i++)
            {
                float start = times[i];
                float end = times[i + 1];
                if (end - start <= DuplicateTimeEpsilon)
                    continue;

                float midpoint = (start + end) * 0.5f;
                if (!first.TryGetBracket(
                        midpoint,
                        options.maximumSourceGapSeconds,
                        out CollisionSampleBracket firstBracket) ||
                    !second.TryGetBracket(
                        midpoint,
                        options.maximumSourceGapSeconds,
                        out CollisionSampleBracket secondBracket))
                {
                    continue;
                }

                Vector3 firstStart = firstBracket.EvaluatePosition(start);
                Vector3 firstEnd = firstBracket.EvaluatePosition(end);
                Vector3 secondStart = secondBracket.EvaluatePosition(start);
                Vector3 secondEnd = secondBracket.EvaluatePosition(end);
                Vector3 relativeStart = firstStart - secondStart;
                Vector3 relativeDelta =
                    (firstEnd - secondEnd) - relativeStart;
                float denominator = relativeDelta.sqrMagnitude;
                float interpolation = denominator > PositionEpsilon
                    ? Mathf.Clamp01(
                        -Vector3.Dot(
                            relativeStart,
                            relativeDelta) /
                        denominator)
                    : 0f;
                Vector3 firstPosition = Vector3.Lerp(
                    firstStart,
                    firstEnd,
                    interpolation);
                Vector3 secondPosition = Vector3.Lerp(
                    secondStart,
                    secondEnd,
                    interpolation);
                candidates.Add(new CollisionContactCandidate(
                    Mathf.Lerp(start, end, interpolation),
                    Vector3.Distance(firstPosition, secondPosition) /
                    options.locationUnitsPerMeter,
                    firstPosition,
                    secondPosition));
            }

            candidates.Sort((a, b) =>
                CompareCandidates(a, b, metadataAnchorTime));
            RemoveDuplicateCandidates(candidates);
            return candidates;
        }

        private static int CompareCandidates(
            CollisionContactCandidate a,
            CollisionContactCandidate b,
            float metadataAnchorTime)
        {
            int distanceResult = a.SeparationMeters.CompareTo(
                b.SeparationMeters);
            if (distanceResult != 0)
                return distanceResult;

            float aAnchor = Mathf.Abs(a.Time - metadataAnchorTime);
            float bAnchor = Mathf.Abs(b.Time - metadataAnchorTime);
            int anchorResult = aAnchor.CompareTo(bAnchor);
            return anchorResult != 0
                ? anchorResult
                : a.Time.CompareTo(b.Time);
        }

        private static bool IsContactValid(
            CollisionContactCandidate candidate,
            List<CollisionContactCandidate> candidates,
            CollisionSampleSeries first,
            CollisionSampleSeries second,
            float metadataAnchorTime,
            float searchStart,
            float searchEnd,
            CollisionTrajectoryForensicsOptions options)
        {
            if (candidate.SeparationMeters >
                    options.maximumContactSeparationMeters ||
                candidate.Time < searchStart +
                    options.contactSearchEdgeMarginSeconds ||
                candidate.Time > searchEnd -
                    options.contactSearchEdgeMarginSeconds ||
                Mathf.Abs(candidate.Time - metadataAnchorTime) >
                    options.maximumAnchorOffsetSeconds ||
                !first.HasContactSupport(
                    candidate.Time,
                    options.maximumSourceGapSeconds) ||
                !second.HasContactSupport(
                    candidate.Time,
                    options.maximumSourceGapSeconds))
            {
                return false;
            }

            float probe = options.contactDistanceProbeSeconds;
            if (!TryGetSeparationMeters(
                    first,
                    second,
                    candidate.Time - probe,
                    options,
                    out float before) ||
                !TryGetSeparationMeters(
                    first,
                    second,
                    candidate.Time + probe,
                    options,
                    out float after) ||
                before - candidate.SeparationMeters <
                    options.minimumContactDistanceChangeMeters ||
                after - candidate.SeparationMeters <
                    options.minimumContactDistanceChangeMeters)
            {
                return false;
            }

            float uniquenessStart = candidate.Time -
                options.contactUniquenessWindowSeconds;
            float uniquenessEnd = candidate.Time +
                options.contactUniquenessWindowSeconds;
            float otherMinimum = float.PositiveInfinity;
            for (int i = 0; i < candidates.Count; i++)
            {
                CollisionContactCandidate other = candidates[i];
                if (other.Time >= uniquenessStart &&
                    other.Time <= uniquenessEnd)
                {
                    continue;
                }

                otherMinimum = Mathf.Min(
                    otherMinimum,
                    other.SeparationMeters);
            }

            return float.IsPositiveInfinity(otherMinimum) ||
                otherMinimum - candidate.SeparationMeters >=
                options.minimumOtherCandidateSeparationMeters;
        }

        private static bool HasValidObservedPost(
            CollisionSampleSeries series,
            float contactTime,
            CollisionTrajectoryForensicsOptions options)
        {
            float postEnd = contactTime + options.visibleTailSeconds;
            if (!series.HasContinuousCoverage(
                    contactTime,
                    postEnd,
                    options.maximumSourceGapSeconds) ||
                series.CountSamples(
                    contactTime + DuplicateTimeEpsilon,
                    postEnd + options.maximumSourceGapSeconds) < 2)
            {
                return false;
            }

            return series.MaximumSegmentSpeedMetersPerSecond(
                    contactTime,
                    postEnd,
                    options.locationUnitsPerMeter) <=
                options.maximumObservedPostSpeedMetersPerSecond &&
                !series.HasDirectionReversal(
                    contactTime,
                    postEnd,
                    options.locationUnitsPerMeter * 0.1f);
        }

        private static bool TryBuildTrajectory(
            CollisionSampleSeries series,
            float contactTime,
            bool includeObservedPost,
            CollisionTrajectoryForensicsOptions options,
            out CollisionObservedTrajectory trajectory)
        {
            trajectory = null;
            float preContextStart = contactTime -
                options.smoothingContextLeadSeconds;
            if (!CollisionBoundedPath.TryCreate(
                    series,
                    preContextStart,
                    contactTime,
                    options.maximumSourceGapSeconds,
                    options.maximumCurveDeviationMeters *
                    options.locationUnitsPerMeter,
                    out CollisionBoundedPath preContact))
            {
                if (!CollisionBoundedPath.TryCreate(
                        series,
                        contactTime - options.visibleLeadSeconds,
                        contactTime,
                        options.maximumSourceGapSeconds,
                        options.maximumCurveDeviationMeters *
                        options.locationUnitsPerMeter,
                        out preContact))
                {
                    return false;
                }
            }

            CollisionBoundedPath postContact = null;
            if (includeObservedPost &&
                !CollisionBoundedPath.TryCreate(
                    series,
                    contactTime,
                    contactTime + options.smoothingContextTailSeconds,
                    options.maximumSourceGapSeconds,
                    options.maximumCurveDeviationMeters *
                    options.locationUnitsPerMeter,
                    out postContact))
            {
                if (!CollisionBoundedPath.TryCreate(
                        series,
                        contactTime,
                        contactTime + options.visibleTailSeconds,
                        options.maximumSourceGapSeconds,
                        options.maximumCurveDeviationMeters *
                        options.locationUnitsPerMeter,
                        out postContact))
                {
                    return false;
                }
            }

            float visibleStart = contactTime -
                options.visibleLeadSeconds;
            float visibleEnd = includeObservedPost
                ? contactTime + options.visibleTailSeconds
                : contactTime;
            CollisionObservedTrajectory temporary =
                new CollisionObservedTrajectory(
                    series.DriverNumber,
                    contactTime,
                    visibleStart,
                    visibleEnd,
                    series,
                    preContact,
                    postContact,
                    null);
            CollisionTrajectorySample[] samples =
                BuildVisibleSamples(
                    temporary,
                    visibleStart,
                    contactTime,
                    visibleEnd,
                    options.visibleSampleStepSeconds);
            trajectory = new CollisionObservedTrajectory(
                series.DriverNumber,
                contactTime,
                visibleStart,
                visibleEnd,
                series,
                preContact,
                postContact,
                samples);
            return true;
        }

        private static CollisionTrajectorySample[] BuildVisibleSamples(
            CollisionObservedTrajectory trajectory,
            float start,
            float contact,
            float end,
            float step)
        {
            List<float> times = new();
            AddUniformTimes(start, contact, step, times);
            if (end > contact + DuplicateTimeEpsilon)
                AddUniformTimes(contact, end, step, times);
            times.Sort();
            RemoveDuplicateTimes(times);

            List<CollisionTrajectorySample> samples = new(times.Count);
            for (int i = 0; i < times.Count; i++)
            {
                if (trajectory.TryEvaluate(
                        times[i],
                        out CollisionTrajectorySample sample))
                {
                    samples.Add(sample);
                }
            }

            return samples.ToArray();
        }

        private static CollisionEvidenceSlice[] BuildEvidenceSlices(
            CollisionObservedTrajectory first,
            CollisionObservedTrajectory second,
            float contactTime,
            bool observedPost,
            CollisionTrajectoryForensicsOptions options)
        {
            List<CollisionEvidenceSlice> slices = new();
            AddEvidenceSlice(
                CollisionEvidenceSliceKind.CorridorStart,
                contactTime - options.visibleLeadSeconds,
                first,
                second,
                slices);
            AddEvidenceSlice(
                CollisionEvidenceSliceKind.VehicleReveal,
                contactTime - options.vehicleRevealLeadSeconds,
                first,
                second,
                slices);
            AddEvidenceSlice(
                CollisionEvidenceSliceKind.PreContact,
                contactTime - options.contactDistanceProbeSeconds,
                first,
                second,
                slices);
            AddEvidenceSlice(
                CollisionEvidenceSliceKind.Contact,
                contactTime,
                first,
                second,
                slices);
            if (observedPost)
            {
                AddEvidenceSlice(
                    CollisionEvidenceSliceKind.ImmediateResponse,
                    contactTime + 0.2f,
                    first,
                    second,
                    slices);
                AddEvidenceSlice(
                    CollisionEvidenceSliceKind.VehicleHold,
                    contactTime + options.vehicleHoldTailSeconds,
                    first,
                    second,
                    slices);
                AddEvidenceSlice(
                    CollisionEvidenceSliceKind.CorridorEnd,
                    contactTime + options.visibleTailSeconds,
                    first,
                    second,
                    slices);
            }

            return slices.ToArray();
        }

        private static void AddEvidenceSlice(
            CollisionEvidenceSliceKind kind,
            float time,
            CollisionObservedTrajectory first,
            CollisionObservedTrajectory second,
            List<CollisionEvidenceSlice> slices)
        {
            if (first.TryEvaluate(
                    time,
                    out CollisionTrajectorySample firstSample) &&
                second.TryEvaluate(
                    time,
                    out CollisionTrajectorySample secondSample))
            {
                slices.Add(new CollisionEvidenceSlice(
                    kind,
                    time,
                    firstSample,
                    secondSample));
            }
        }

        private static bool TryGetSeparationMeters(
            CollisionSampleSeries first,
            CollisionSampleSeries second,
            float time,
            CollisionTrajectoryForensicsOptions options,
            out float separationMeters)
        {
            separationMeters = 0f;
            if (!TryEvaluateLinearPair(
                    first,
                    second,
                    time,
                    options.maximumSourceGapSeconds,
                    out Vector3 firstPosition,
                    out Vector3 secondPosition))
            {
                return false;
            }

            separationMeters = Vector3.Distance(
                    firstPosition,
                    secondPosition) /
                options.locationUnitsPerMeter;
            return true;
        }

        private static bool TryEvaluateLinearPair(
            CollisionSampleSeries first,
            CollisionSampleSeries second,
            float time,
            float maximumGap,
            out Vector3 firstPosition,
            out Vector3 secondPosition)
        {
            firstPosition = Vector3.zero;
            secondPosition = Vector3.zero;
            return first.TryEvaluateLinear(
                    time,
                    maximumGap,
                    out firstPosition) &&
                second.TryEvaluateLinear(
                    time,
                    maximumGap,
                    out secondPosition);
        }

        private static void AddUniformTimes(
            float start,
            float end,
            float step,
            List<float> times)
        {
            times.Add(start);
            int count = Mathf.FloorToInt((end - start) / step);
            for (int i = 1; i <= count; i++)
            {
                float time = start + i * step;
                if (time < end - DuplicateTimeEpsilon)
                    times.Add(time);
            }
            times.Add(end);
        }

        private static void RemoveDuplicateTimes(List<float> times)
        {
            if (times.Count <= 1)
                return;

            int write = 1;
            for (int read = 1; read < times.Count; read++)
            {
                if (Mathf.Abs(times[read] - times[write - 1]) <
                    DuplicateTimeEpsilon)
                {
                    continue;
                }

                times[write] = times[read];
                write++;
            }

            if (write < times.Count)
                times.RemoveRange(write, times.Count - write);
        }

        private static void RemoveDuplicateCandidates(
            List<CollisionContactCandidate> candidates)
        {
            if (candidates.Count <= 1)
                return;

            List<CollisionContactCandidate> unique = new(
                candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                CollisionContactCandidate candidate = candidates[i];
                bool duplicate = false;
                for (int j = 0; j < unique.Count; j++)
                {
                    if (Mathf.Abs(unique[j].Time - candidate.Time) <
                        DuplicateTimeEpsilon)
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (!duplicate)
                    unique.Add(candidate);
            }

            candidates.Clear();
            candidates.AddRange(unique);
        }

        private struct CollisionContactCandidate
        {
            public CollisionContactCandidate(
                float time,
                float separationMeters,
                Vector3 firstPosition,
                Vector3 secondPosition)
            {
                Time = time;
                SeparationMeters = separationMeters;
                FirstPosition = firstPosition;
                SecondPosition = secondPosition;
            }

            public float Time;
            public float SeparationMeters;
            public Vector3 FirstPosition;
            public Vector3 SecondPosition;
        }

        internal sealed class CollisionSampleSeries
        {
            private readonly CollisionSourceNode[] nodes;

            private CollisionSampleSeries(
                int driverNumber,
                CollisionSourceNode[] nodes)
            {
                DriverNumber = driverNumber;
                this.nodes = nodes;
            }

            public int DriverNumber { get; }
            public int Count => nodes.Length;

            public static CollisionSampleSeries Create(
                IReadOnlyList<LocationSample> samples,
                int driverNumber)
            {
                if (samples == null || samples.Count < 2)
                    return null;

                List<CollisionSourceNode> copied = new(samples.Count);
                for (int i = 0; i < samples.Count; i++)
                {
                    LocationSample sample = samples[i];
                    if (sample == null ||
                        sample.driverNumber != driverNumber ||
                        !IsFinite(sample.t) ||
                        !IsFinite(sample.x) ||
                        !IsFinite(sample.y) ||
                        !IsFinite(sample.z))
                    {
                        continue;
                    }

                    copied.Add(new CollisionSourceNode(sample));
                }
                copied.Sort(CompareSourceNodes);

                List<CollisionSourceNode> unique = new(copied.Count);
                for (int i = 0; i < copied.Count; i++)
                {
                    if (unique.Count > 0 &&
                        Mathf.Abs(
                            copied[i].Time -
                            unique[unique.Count - 1].Time) <
                        DuplicateTimeEpsilon)
                    {
                        continue;
                    }

                    unique.Add(copied[i]);
                }

                return unique.Count >= 2
                    ? new CollisionSampleSeries(
                        driverNumber,
                        unique.ToArray())
                    : null;
            }

            public void AddTimes(
                float start,
                float end,
                List<float> output)
            {
                for (int i = 0; i < nodes.Length; i++)
                {
                    float time = nodes[i].Time;
                    if (time > start && time < end)
                        output.Add(time);
                }
            }

            public bool TryGetBracket(
                float time,
                float maximumGap,
                out CollisionSampleBracket bracket)
            {
                bracket = default;
                int index = FindLowerIndex(time);
                if (index < 0)
                    return false;
                if (index >= nodes.Length - 1)
                {
                    if (Mathf.Abs(time - nodes[nodes.Length - 1].Time) >
                        DuplicateTimeEpsilon)
                    {
                        return false;
                    }
                    index = nodes.Length - 2;
                }

                CollisionSourceNode before = nodes[index];
                CollisionSourceNode after = nodes[index + 1];
                float gap = after.Time - before.Time;
                if (gap <= 0f ||
                    gap > maximumGap + DuplicateTimeEpsilon ||
                    time < before.Time - DuplicateTimeEpsilon ||
                    time > after.Time + DuplicateTimeEpsilon)
                {
                    return false;
                }

                bracket = new CollisionSampleBracket(before, after);
                return true;
            }

            public bool TryEvaluateLinear(
                float time,
                float maximumGap,
                out Vector3 position)
            {
                position = Vector3.zero;
                if (!TryGetBracket(time, maximumGap, out var bracket))
                    return false;

                position = bracket.EvaluatePosition(time);
                return true;
            }

            public bool TryEvaluateTelemetry(
                float time,
                out CollisionForensicTelemetry telemetry)
            {
                telemetry = default;
                int index = FindLowerIndex(time);
                if (index < 0)
                    return false;
                if (index >= nodes.Length - 1)
                    index = nodes.Length - 2;

                CollisionSourceNode before = nodes[index];
                CollisionSourceNode after = nodes[index + 1];
                float duration = Mathf.Max(
                    DuplicateTimeEpsilon,
                    after.Time - before.Time);
                float interpolation = Mathf.Clamp01(
                    (time - before.Time) / duration);
                CollisionSourceNode nearest = interpolation < 0.5f
                    ? before
                    : after;
                bool available = before.HasTelemetry ||
                    after.HasTelemetry;
                telemetry = new CollisionForensicTelemetry(
                    available,
                    Mathf.Lerp(
                        before.SpeedKph,
                        after.SpeedKph,
                        interpolation),
                    Mathf.Lerp(
                        before.ThrottlePercent,
                        after.ThrottlePercent,
                        interpolation),
                    Mathf.Lerp(
                        before.Rpm,
                        after.Rpm,
                        interpolation),
                    nearest.Gear,
                    nearest.Brake,
                    nearest.Drs,
                    before.Time,
                    after.Time);
                return true;
            }

            public bool HasContactSupport(
                float time,
                float maximumGap)
            {
                if (!TryGetBracket(time, maximumGap, out var bracket))
                    return false;

                return time - bracket.Before.Time <= maximumGap &&
                    bracket.After.Time - time <= maximumGap;
            }

            public bool HasContinuousCoverage(
                float start,
                float end,
                float maximumGap)
            {
                if (end < start)
                {
                    float swap = start;
                    start = end;
                    end = swap;
                }
                if (!TryGetBracket(start, maximumGap, out _) ||
                    !TryGetBracket(end, maximumGap, out _))
                {
                    return false;
                }

                int index = Mathf.Max(0, FindLowerIndex(start));
                while (index < nodes.Length - 1 &&
                    nodes[index].Time < end)
                {
                    if (nodes[index + 1].Time - nodes[index].Time >
                        maximumGap + DuplicateTimeEpsilon)
                    {
                        return false;
                    }
                    index++;
                }
                return true;
            }

            public int CountSamples(float start, float end)
            {
                int count = 0;
                for (int i = 0; i < nodes.Length; i++)
                {
                    if (nodes[i].Time >= start && nodes[i].Time <= end)
                        count++;
                }
                return count;
            }

            public float MaximumSegmentSpeedMetersPerSecond(
                float start,
                float end,
                float unitsPerMeter)
            {
                List<CollisionSourceNode> range = CopyRange(start, end);
                float maximum = 0f;
                for (int i = 0; i < range.Count - 1; i++)
                {
                    float duration = range[i + 1].Time - range[i].Time;
                    if (duration <= DuplicateTimeEpsilon)
                        continue;
                    maximum = Mathf.Max(
                        maximum,
                        Vector3.Distance(
                            range[i].RawPosition,
                            range[i + 1].RawPosition) /
                        Mathf.Max(0.001f, unitsPerMeter) /
                        duration);
                }
                return maximum;
            }

            public bool HasDirectionReversal(
                float start,
                float end,
                float minimumDistance)
            {
                List<CollisionSourceNode> range = CopyRange(start, end);
                Vector3 previous = Vector3.zero;
                bool hasPrevious = false;
                for (int i = 0; i < range.Count - 1; i++)
                {
                    Vector3 direction =
                        range[i + 1].RawPosition -
                        range[i].RawPosition;
                    direction.z = 0f;
                    if (direction.magnitude < minimumDistance)
                        continue;
                    direction.Normalize();
                    if (hasPrevious &&
                        Vector3.Dot(previous, direction) <= -0.8660254f)
                    {
                        return true;
                    }
                    previous = direction;
                    hasPrevious = true;
                }
                return false;
            }

            public List<CollisionSourceNode> CopyRange(
                float start,
                float end)
            {
                List<CollisionSourceNode> range = new();
                if (!TryEvaluateLinear(
                        start,
                        float.PositiveInfinity,
                        out Vector3 startPosition) ||
                    !TryEvaluateLinear(
                        end,
                        float.PositiveInfinity,
                        out Vector3 endPosition))
                {
                    return range;
                }

                range.Add(CreateSyntheticNode(start, startPosition));
                for (int i = 0; i < nodes.Length; i++)
                {
                    if (nodes[i].Time > start + DuplicateTimeEpsilon &&
                        nodes[i].Time < end - DuplicateTimeEpsilon)
                    {
                        range.Add(nodes[i]);
                    }
                }
                range.Add(CreateSyntheticNode(end, endPosition));
                return range;
            }

            private CollisionSourceNode CreateSyntheticNode(
                float time,
                Vector3 position)
            {
                TryEvaluateTelemetry(time, out var telemetry);
                return new CollisionSourceNode(
                    time,
                    position,
                    telemetry);
            }

            private int FindLowerIndex(float time)
            {
                int low = 0;
                int high = nodes.Length - 1;
                while (low <= high)
                {
                    int middle = low + (high - low) / 2;
                    if (nodes[middle].Time <= time)
                        low = middle + 1;
                    else
                        high = middle - 1;
                }
                return high;
            }

            private static int CompareSourceNodes(
                CollisionSourceNode a,
                CollisionSourceNode b)
            {
                int time = a.Time.CompareTo(b.Time);
                if (time != 0)
                    return time;
                int telemetry = b.HasTelemetry.CompareTo(a.HasTelemetry);
                if (telemetry != 0)
                    return telemetry;
                int x = a.RawPosition.x.CompareTo(b.RawPosition.x);
                if (x != 0)
                    return x;
                int y = a.RawPosition.y.CompareTo(b.RawPosition.y);
                return y != 0
                    ? y
                    : a.RawPosition.z.CompareTo(b.RawPosition.z);
            }
        }

        internal readonly struct CollisionSourceNode
        {
            public CollisionSourceNode(LocationSample sample)
            {
                Time = sample.t;
                RawPosition = new Vector3(
                    sample.x,
                    sample.y,
                    sample.z);
                SpeedKph = sample.speed;
                ThrottlePercent = sample.throttle;
                Rpm = sample.rpm;
                Gear = sample.nGear != 0
                    ? sample.nGear
                    : sample.n_gear;
                Brake = sample.brake;
                Drs = sample.drs;
                HasTelemetry = sample.speed > 0f || sample.rpm > 0f;
            }

            public CollisionSourceNode(
                float time,
                Vector3 rawPosition,
                CollisionForensicTelemetry telemetry)
            {
                Time = time;
                RawPosition = rawPosition;
                SpeedKph = telemetry.SpeedKph;
                ThrottlePercent = telemetry.ThrottlePercent;
                Rpm = telemetry.Rpm;
                Gear = telemetry.Gear;
                Brake = telemetry.Brake;
                Drs = telemetry.Drs;
                HasTelemetry = telemetry.Available;
            }

            public float Time { get; }
            public Vector3 RawPosition { get; }
            public float SpeedKph { get; }
            public float ThrottlePercent { get; }
            public float Rpm { get; }
            public int Gear { get; }
            public int Brake { get; }
            public int Drs { get; }
            public bool HasTelemetry { get; }
        }

        internal readonly struct CollisionSampleBracket
        {
            public CollisionSampleBracket(
                CollisionSourceNode before,
                CollisionSourceNode after)
            {
                Before = before;
                After = after;
            }

            public CollisionSourceNode Before { get; }
            public CollisionSourceNode After { get; }

            public Vector3 EvaluatePosition(float time)
            {
                float duration = Mathf.Max(
                    DuplicateTimeEpsilon,
                    After.Time - Before.Time);
                float interpolation = Mathf.Clamp01(
                    (time - Before.Time) / duration);
                return Vector3.Lerp(
                    Before.RawPosition,
                    After.RawPosition,
                    interpolation);
            }
        }

        internal sealed class CollisionBoundedPath
        {
            private readonly float[] times;
            private readonly Vector3[] positions;
            private readonly float[] distances;
            private readonly float[] distanceSlopes;
            private readonly float maximumDeviation;

            private CollisionBoundedPath(
                float[] times,
                Vector3[] positions,
                float maximumDeviation)
            {
                this.times = times;
                this.positions = positions;
                this.maximumDeviation = Mathf.Max(
                    0f,
                    maximumDeviation);
                distances = BuildDistances(positions);
                distanceSlopes = BuildMonotoneSlopes(times, distances);
            }

            public static bool TryCreate(
                CollisionSampleSeries series,
                float start,
                float end,
                float maximumGap,
                float maximumDeviation,
                out CollisionBoundedPath path)
            {
                path = null;
                if (!series.HasContinuousCoverage(start, end, maximumGap))
                    return false;

                List<CollisionSourceNode> nodes = series.CopyRange(
                    start,
                    end);
                if (nodes.Count < 2)
                    return false;

                float[] times = new float[nodes.Count];
                Vector3[] positions = new Vector3[nodes.Count];
                for (int i = 0; i < nodes.Count; i++)
                {
                    times[i] = nodes[i].Time;
                    positions[i] = nodes[i].RawPosition;
                }
                path = new CollisionBoundedPath(
                    times,
                    positions,
                    maximumDeviation);
                return true;
            }

            public bool TryEvaluate(float time, out Vector3 position)
            {
                position = Vector3.zero;
                if (time < times[0] - DuplicateTimeEpsilon ||
                    time > times[times.Length - 1] +
                    DuplicateTimeEpsilon)
                {
                    return false;
                }

                int timeSegment = FindSegment(times, time);
                float duration = Mathf.Max(
                    DuplicateTimeEpsilon,
                    times[timeSegment + 1] - times[timeSegment]);
                float interpolation = Mathf.Clamp01(
                    (time - times[timeSegment]) / duration);
                float distance = EvaluateHermite(
                    distances[timeSegment],
                    distances[timeSegment + 1],
                    distanceSlopes[timeSegment] * duration,
                    distanceSlopes[timeSegment + 1] * duration,
                    interpolation);
                distance = Mathf.Clamp(
                    distance,
                    distances[timeSegment],
                    distances[timeSegment + 1]);

                int spatialSegment = FindDistanceSegment(distance);
                float segmentLength = distances[spatialSegment + 1] -
                    distances[spatialSegment];
                if (segmentLength <= PositionEpsilon)
                {
                    position = positions[spatialSegment + 1];
                    return true;
                }

                float spatialInterpolation = Mathf.Clamp01(
                    (distance - distances[spatialSegment]) /
                    segmentLength);
                Vector3 baseline = Vector3.Lerp(
                    positions[spatialSegment],
                    positions[spatialSegment + 1],
                    spatialInterpolation);
                Vector3 candidate = EvaluateSpatialHermite(
                    spatialSegment,
                    spatialInterpolation,
                    segmentLength);
                Vector3 segment =
                    positions[spatialSegment + 1] -
                    positions[spatialSegment];
                Vector3 direction = segment / segmentLength;
                Vector3 lateral = candidate - baseline;
                lateral -= direction * Vector3.Dot(lateral, direction);
                if (lateral.magnitude > maximumDeviation)
                    lateral = lateral.normalized * maximumDeviation;
                position = baseline + lateral;
                return IsFinite(position.x) &&
                    IsFinite(position.y) &&
                    IsFinite(position.z);
            }

            public bool TryEvaluateTangent(
                float time,
                out Vector3 tangent)
            {
                tangent = Vector3.forward;
                float start = times[0];
                float end = times[times.Length - 1];
                float beforeTime = Mathf.Max(
                    start,
                    time - TangentProbeSeconds);
                float afterTime = Mathf.Min(
                    end,
                    time + TangentProbeSeconds);
                if (afterTime - beforeTime <= DuplicateTimeEpsilon ||
                    !TryEvaluate(beforeTime, out Vector3 before) ||
                    !TryEvaluate(afterTime, out Vector3 after))
                {
                    return false;
                }

                Vector3 direction = after - before;
                if (direction.sqrMagnitude <= PositionEpsilon)
                    return false;
                tangent = direction.normalized;
                return true;
            }

            private Vector3 EvaluateSpatialHermite(
                int segment,
                float interpolation,
                float segmentLength)
            {
                Vector3 startTangent = ResolveSpatialTangent(segment);
                Vector3 endTangent = ResolveSpatialTangent(segment + 1);
                float t2 = interpolation * interpolation;
                float t3 = t2 * interpolation;
                float h00 = 2f * t3 - 3f * t2 + 1f;
                float h10 = t3 - 2f * t2 + interpolation;
                float h01 = -2f * t3 + 3f * t2;
                float h11 = t3 - t2;
                return h00 * positions[segment] +
                    h10 * segmentLength * startTangent +
                    h01 * positions[segment + 1] +
                    h11 * segmentLength * endTangent;
            }

            private Vector3 ResolveSpatialTangent(int index)
            {
                Vector3 direction;
                if (index <= 0)
                    direction = positions[1] - positions[0];
                else if (index >= positions.Length - 1)
                {
                    direction = positions[positions.Length - 1] -
                        positions[positions.Length - 2];
                }
                else
                {
                    direction = positions[index + 1] -
                        positions[index - 1];
                }

                return direction.sqrMagnitude > PositionEpsilon
                    ? direction.normalized
                    : Vector3.zero;
            }

            private int FindDistanceSegment(float distance)
            {
                int segment = FindSegment(distances, distance);
                while (segment < distances.Length - 2 &&
                    distances[segment + 1] - distances[segment] <=
                    PositionEpsilon)
                {
                    segment++;
                }
                while (segment > 0 &&
                    distances[segment + 1] - distances[segment] <=
                    PositionEpsilon)
                {
                    segment--;
                }
                return segment;
            }

            private static float[] BuildDistances(Vector3[] points)
            {
                float[] result = new float[points.Length];
                for (int i = 1; i < points.Length; i++)
                {
                    result[i] = result[i - 1] +
                        Vector3.Distance(points[i - 1], points[i]);
                }
                return result;
            }

            private static float[] BuildMonotoneSlopes(
                float[] x,
                float[] y)
            {
                int count = x.Length;
                float[] slopes = new float[count];
                if (count == 2)
                {
                    float delta = SafeSecant(
                        x[0],
                        x[1],
                        y[0],
                        y[1]);
                    slopes[0] = delta;
                    slopes[1] = delta;
                    return slopes;
                }

                float[] intervals = new float[count - 1];
                float[] secants = new float[count - 1];
                for (int i = 0; i < count - 1; i++)
                {
                    intervals[i] = Mathf.Max(
                        DuplicateTimeEpsilon,
                        x[i + 1] - x[i]);
                    secants[i] = (y[i + 1] - y[i]) /
                        intervals[i];
                }

                slopes[0] = ResolveEndpointSlope(
                    intervals[0],
                    intervals[1],
                    secants[0],
                    secants[1]);
                slopes[count - 1] = ResolveEndpointSlope(
                    intervals[count - 2],
                    intervals[count - 3],
                    secants[count - 2],
                    secants[count - 3]);
                for (int i = 1; i < count - 1; i++)
                {
                    float before = secants[i - 1];
                    float after = secants[i];
                    if (before <= 0f || after <= 0f)
                    {
                        slopes[i] = 0f;
                        continue;
                    }

                    float weightBefore =
                        2f * intervals[i] + intervals[i - 1];
                    float weightAfter =
                        intervals[i] + 2f * intervals[i - 1];
                    slopes[i] = (weightBefore + weightAfter) /
                        (weightBefore / before + weightAfter / after);
                }
                return slopes;
            }

            private static float ResolveEndpointSlope(
                float interval,
                float adjacentInterval,
                float secant,
                float adjacentSecant)
            {
                float slope =
                    ((2f * interval + adjacentInterval) * secant -
                     interval * adjacentSecant) /
                    Mathf.Max(
                        DuplicateTimeEpsilon,
                        interval + adjacentInterval);
                if (Mathf.Sign(slope) != Mathf.Sign(secant))
                    return 0f;
                if (Mathf.Sign(secant) != Mathf.Sign(adjacentSecant) &&
                    Mathf.Abs(slope) > Mathf.Abs(3f * secant))
                {
                    return 3f * secant;
                }
                return Mathf.Max(0f, slope);
            }

            private static float SafeSecant(
                float x0,
                float x1,
                float y0,
                float y1)
            {
                return (y1 - y0) /
                    Mathf.Max(DuplicateTimeEpsilon, x1 - x0);
            }

            private static float EvaluateHermite(
                float start,
                float end,
                float startTangent,
                float endTangent,
                float interpolation)
            {
                float t2 = interpolation * interpolation;
                float t3 = t2 * interpolation;
                return (2f * t3 - 3f * t2 + 1f) * start +
                    (t3 - 2f * t2 + interpolation) * startTangent +
                    (-2f * t3 + 3f * t2) * end +
                    (t3 - t2) * endTangent;
            }

            private static int FindSegment(float[] values, float value)
            {
                int low = 0;
                int high = values.Length - 1;
                while (low <= high)
                {
                    int middle = low + (high - low) / 2;
                    if (values[middle] <= value)
                        low = middle + 1;
                    else
                        high = middle - 1;
                }
                return Mathf.Clamp(high, 0, values.Length - 2);
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
