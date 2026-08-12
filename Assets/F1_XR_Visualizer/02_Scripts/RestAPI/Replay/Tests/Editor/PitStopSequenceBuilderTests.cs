#if UNITY_EDITOR
using System.Collections.Generic;
using F1XR.RestAPI.Api;
using NUnit.Framework;

namespace F1XR.RestAPI.Replay.Tests
{
    public sealed class PitStopSequenceBuilderTests
    {
        private readonly PitStopSequenceBuilder builder = new();

        [Test]
        public void AuthoritativeDurationBuildsServiceWindow()
        {
            ReplayEventDto replayEvent = Event(2f);
            PitStopSequence sequence = builder.Build(
                replayEvent,
                Samples(0f, 1f, 2f, 3f, 4f, 5f, 6f),
                new[] { 0f, 1f, 2f, 2.01f, 2.02f, 3f, 4f },
                1f);

            Assert.That(sequence.IsDriveThrough, Is.False);
            Assert.That(sequence.IsReconstructed, Is.False);
            Assert.That(
                sequence.ServiceEndTime - sequence.ServiceStartTime,
                Is.EqualTo(2f).Within(0.001f));
        }

        [Test]
        public void SustainedLowSpeedReconstructsStop()
        {
            PitStopSequence sequence = builder.Build(
                Event(-1f),
                Samples(0f, 1f, 2f, 3f, 4f),
                new[] { 0f, 1f, 1.05f, 1.08f, 2f },
                1f);

            Assert.That(sequence.IsDriveThrough, Is.False);
            Assert.That(sequence.IsReconstructed, Is.True);
            Assert.That(sequence.Confidence, Is.GreaterThanOrEqualTo(0.4f));
            Assert.That(
                sequence.GetPhase(sequence.FocusTime),
                Is.EqualTo(PitStopPhase.Service));
        }

        [Test]
        public void NoSustainedStopDowngradesToDriveThrough()
        {
            PitStopSequence sequence = builder.Build(
                Event(-1f),
                Samples(0f, 1f, 2f, 3f, 4f),
                new[] { 0f, 1f, 2f, 3f, 4f },
                1f);

            Assert.That(sequence.IsDriveThrough, Is.True);
            Assert.That(
                sequence.GetPhase(1f),
                Is.EqualTo(PitStopPhase.Approach));
            Assert.That(
                sequence.GetPhase(4f),
                Is.EqualTo(PitStopPhase.Exit));
        }

        [Test]
        public void PhaseIsReconstructedDirectlyAfterSeek()
        {
            PitStopSequence sequence = builder.Build(
                Event(1.5f),
                Samples(0f, 1f, 2f, 3f, 4f, 5f, 6f),
                new[] { 0f, 1f, 2f, 2f, 2f, 3f, 4f },
                1f);

            Assert.That(
                sequence.GetPhase(sequence.ServiceStartTime + 0.1f),
                Is.EqualTo(PitStopPhase.Service));
            Assert.That(
                sequence.GetPhase(sequence.EndTime),
                Is.EqualTo(PitStopPhase.Exit));
            Assert.That(
                sequence.GetPhase(sequence.ServiceStartTime + 0.1f),
                Is.EqualTo(PitStopPhase.Service));
        }

        [Test]
        public void ManifestPitEventDoesNotHideFixtureOvertake()
        {
            ReplayEventDto fixture = new()
            {
                eventId = "fixture_overtake",
                eventType = "Overtake",
                anchorTime = 10f
            };
            ReplayEventDto pit = new()
            {
                eventId = "pit_1_63_12",
                eventType = "PitStop",
                anchorTime = 20f
            };

            ReplayEventDto[] merged = ReplayEventMerger.Merge(
                new[] { fixture },
                new[] { pit });

            Assert.That(merged, Has.Length.EqualTo(2));
            Assert.That(merged[0].eventId, Is.EqualTo("fixture_overtake"));
            Assert.That(merged[1].eventId, Is.EqualTo("pit_1_63_12"));
        }

        [Test]
        public void ManifestEventWinsDuplicateId()
        {
            ReplayEventDto fixture = new()
            {
                eventId = "same",
                displayTitle = "fixture"
            };
            ReplayEventDto manifest = new()
            {
                eventId = "same",
                displayTitle = "manifest"
            };

            ReplayEventDto[] merged = ReplayEventMerger.Merge(
                new[] { fixture },
                new[] { manifest });

            Assert.That(merged, Has.Length.EqualTo(1));
            Assert.That(merged[0].displayTitle, Is.EqualTo("manifest"));
        }

        [Test]
        public void MissingPitDataPreservesOtherShowcaseEvents()
        {
            ReplayEventDto overtake = new()
            {
                eventId = "fixture_overtake",
                eventType = "Overtake",
                anchorTime = 10f
            };
            ReplayEventDto collision = new()
            {
                eventId = "fixture_collision",
                eventType = "Collision",
                anchorTime = 20f
            };

            ReplayEventDto[] merged = ReplayEventMerger.Merge(
                new[] { collision, overtake },
                null);

            Assert.That(merged, Has.Length.EqualTo(2));
            Assert.That(merged[0].eventId, Is.EqualTo("fixture_overtake"));
            Assert.That(merged[1].eventId, Is.EqualTo("fixture_collision"));
        }

        private static ReplayEventDto Event(float stopDuration)
        {
            return new ReplayEventDto
            {
                eventId = "pit",
                eventType = "PitStop",
                anchorTime = 3f,
                startTime = 0f,
                endTime = 6f,
                confidence = 0.9f,
                pitStopDuration = stopDuration,
                driverNumbers = new[] { 63 }
            };
        }

        private static List<LocationSample> Samples(
            params float[] times)
        {
            List<LocationSample> result = new(times.Length);
            for (int i = 0; i < times.Length; i++)
            {
                result.Add(new LocationSample
                {
                    t = times[i],
                    driverNumber = 63
                });
            }
            return result;
        }
    }
}
#endif
