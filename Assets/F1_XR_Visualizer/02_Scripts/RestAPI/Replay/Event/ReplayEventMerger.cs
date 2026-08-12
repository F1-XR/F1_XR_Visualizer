using System;
using System.Collections.Generic;
using F1XR.RestAPI.Api;

namespace F1XR.RestAPI.Replay
{
    public static class ReplayEventMerger
    {
        public static ReplayEventDto[] Merge(
            ReplayEventDto[] fixtures,
            ReplayEventDto[] manifestEvents)
        {
            Dictionary<string, ReplayEventDto> byId =
                new(StringComparer.OrdinalIgnoreCase);
            AddById(byId, fixtures);
            AddById(byId, manifestEvents);
            if (byId.Count == 0)
                return null;

            List<ReplayEventDto> merged = new(byId.Values);
            merged.Sort((left, right) =>
            {
                int time = left.anchorTime.CompareTo(right.anchorTime);
                return time != 0
                    ? time
                    : string.CompareOrdinal(left.eventId, right.eventId);
            });
            return merged.ToArray();
        }

        private static void AddById(
            IDictionary<string, ReplayEventDto> destination,
            ReplayEventDto[] source)
        {
            if (source == null)
                return;

            for (int i = 0; i < source.Length; i++)
            {
                ReplayEventDto replayEvent = source[i];
                if (replayEvent == null ||
                    string.IsNullOrWhiteSpace(replayEvent.eventId))
                {
                    continue;
                }

                destination[replayEvent.eventId] = replayEvent;
            }
        }
    }
}
