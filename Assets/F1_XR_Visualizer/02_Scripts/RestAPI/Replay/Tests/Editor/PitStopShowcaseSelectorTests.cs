#if UNITY_EDITOR
using System.Collections.Generic;
using F1XR.RestAPI.Api;
using NUnit.Framework;

namespace F1XR.RestAPI.Replay.Tests
{
    public sealed class PitStopShowcaseSelectorTests
    {
        private readonly Dictionary<int, string> teams = new()
        {
            { 27, "Haas F1 Team" },
            { 55, "Ferrari" },
            { 1, "Red Bull Racing" }
        };

        [Test]
        public void PrefersEarliestFerrariOverEarlierOtherTeam()
        {
            ReplayEventDto selected =
                PitStopShowcaseSelector.SelectInitial(
                    new[]
                    {
                        Event("pit_haas", 27, 2459.236f),
                        Event("pit_ferrari_second", 55, 5486.689f),
                        Event("pit_ferrari_first", 55, 3427.268f)
                    },
                    _ => true,
                    ResolveTeam,
                    PitStopShowcaseSelector.PreferredTeam);

            Assert.That(
                selected.eventId,
                Is.EqualTo("pit_ferrari_first"));
        }

        [Test]
        public void FallsBackToEarliestEventWhenFerrariIsUnavailable()
        {
            ReplayEventDto selected =
                PitStopShowcaseSelector.SelectInitial(
                    new[]
                    {
                        Event("pit_red_bull", 1, 3515.571f),
                        Event("pit_haas", 27, 2459.236f)
                    },
                    _ => true,
                    ResolveTeam,
                    PitStopShowcaseSelector.PreferredTeam);

            Assert.That(selected.eventId, Is.EqualTo("pit_haas"));
        }

        [Test]
        public void IgnoresUnusableFerrariEvent()
        {
            ReplayEventDto selected =
                PitStopShowcaseSelector.SelectInitial(
                    new[]
                    {
                        Event("pit_red_flag_ferrari", 55, 1147.863f),
                        Event("pit_haas", 27, 2459.236f)
                    },
                    candidate =>
                        candidate.eventId != "pit_red_flag_ferrari",
                    ResolveTeam,
                    PitStopShowcaseSelector.PreferredTeam);

            Assert.That(selected.eventId, Is.EqualTo("pit_haas"));
        }

        [Test]
        public void UsesEventIdAsDeterministicTieBreaker()
        {
            ReplayEventDto selected =
                PitStopShowcaseSelector.SelectInitial(
                    new[]
                    {
                        Event("pit_ferrari_b", 55, 3427.268f),
                        Event("pit_ferrari_a", 55, 3427.268f)
                    },
                    _ => true,
                    ResolveTeam,
                    PitStopShowcaseSelector.PreferredTeam);

            Assert.That(selected.eventId, Is.EqualTo("pit_ferrari_a"));
        }

        private string ResolveTeam(int driverNumber)
        {
            teams.TryGetValue(driverNumber, out string team);
            return team;
        }

        private static ReplayEventDto Event(
            string eventId,
            int driverNumber,
            float anchorTime)
        {
            return new ReplayEventDto
            {
                eventId = eventId,
                eventType = "PitStop",
                anchorTime = anchorTime,
                startTime = anchorTime - 8f,
                endTime = anchorTime + 8f,
                driverNumbers = new[] { driverNumber },
                pitLaneDuration = 24f
            };
        }
    }
}
#endif
