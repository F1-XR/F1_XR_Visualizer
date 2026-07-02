using System.Collections.Generic;
using F1XR.RestAPI.Api;

namespace F1XR.RestAPI.Replay
{
    public class ReplayPositions
    {
        private readonly Dictionary<int, List<PositionSampleDto>> byDriver = new();

        public void Add(ReplayChunkDto chunk)
        {
            if (chunk.positions == null)
                return;

            foreach (PositionSampleDto sample in chunk.positions)
            {
                if (!byDriver.TryGetValue(sample.driverNumber, out List<PositionSampleDto> list))
                {
                    list = new List<PositionSampleDto>();
                    byDriver.Add(sample.driverNumber, list);
                }

                list.Add(sample);
            }

            foreach (List<PositionSampleDto> list in byDriver.Values)
                list.Sort((a, b) => a.t.CompareTo(b.t));
        }

        public List<PositionSampleDto> Get(float time)
        {
            List<PositionSampleDto> result = new();

            foreach (var pair in byDriver)
            {
                PositionSampleDto latest = null;

                foreach (PositionSampleDto sample in pair.Value)
                {
                    if (sample.t > time)
                        break;

                    latest = sample;
                }

                if (latest != null)
                    result.Add(latest);
            }

            result.Sort((a, b) => a.position.CompareTo(b.position));
            return result;
        }

        public void Clear()
        {
            byDriver.Clear();
        }
    }
}