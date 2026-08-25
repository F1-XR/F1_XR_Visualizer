using System;
using F1XR.RestAPI.Api;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public static class PitStopShowcaseSelector
    {
        public const string PreferredTeam = "Ferrari";

        public static ReplayEventDto SelectInitial(
            ReplayEventDto[] events,
            Predicate<ReplayEventDto> isUsable,
            Func<int, string> resolveTeam,
            string preferredTeam)
        {
            if (events == null || events.Length == 0)
                return null;

            ReplayEventDto earliest = null;
            ReplayEventDto preferred = null;
            for (int i = 0; i < events.Length; i++)
            {
                ReplayEventDto candidate = events[i];
                if (candidate == null ||
                    isUsable != null && !isUsable(candidate))
                {
                    continue;
                }

                if (IsEarlier(candidate, earliest))
                    earliest = candidate;

                if (IsPreferredTeam(
                        candidate,
                        resolveTeam,
                        preferredTeam) &&
                    IsEarlier(candidate, preferred))
                {
                    preferred = candidate;
                }
            }

            return preferred ?? earliest;
        }

        private static bool IsPreferredTeam(
            ReplayEventDto candidate,
            Func<int, string> resolveTeam,
            string preferredTeam)
        {
            if (candidate.driverNumbers == null ||
                candidate.driverNumbers.Length == 0 ||
                resolveTeam == null ||
                string.IsNullOrWhiteSpace(preferredTeam))
            {
                return false;
            }

            string team = resolveTeam(candidate.driverNumbers[0]);
            return !string.IsNullOrWhiteSpace(team) &&
                team.IndexOf(
                    preferredTeam,
                    StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsEarlier(
            ReplayEventDto candidate,
            ReplayEventDto current)
        {
            return current == null ||
                candidate.anchorTime < current.anchorTime ||
                Mathf.Approximately(
                    candidate.anchorTime,
                    current.anchorTime) &&
                string.CompareOrdinal(
                    candidate.eventId,
                    current.eventId) < 0;
        }
    }
}
