using System.Collections.Generic;
using F1XR.RestAPI.Api;

namespace F1XR.RestAPI.Replay
{
    public class ReplayTires
    {
        private readonly Dictionary<int, List<TireSampleDto>> byDriver = new();

        public void Add(ReplayChunkDto chunk)
        {
            if (chunk.tires == null)
                return;

            foreach (TireSampleDto sample in chunk.tires)
            {
                if (!byDriver.TryGetValue(sample.driverNumber, out List<TireSampleDto> list))
                {
                    list = new List<TireSampleDto>();
                    byDriver.Add(sample.driverNumber, list);
                }

                list.Add(sample);
            }

            foreach (List<TireSampleDto> list in byDriver.Values)
                list.Sort((a, b) => a.t.CompareTo(b.t));
        }

        public TireSampleDto Get(int driverNumber, float time)
        {
            if (!byDriver.TryGetValue(driverNumber, out List<TireSampleDto> list))
                return null;

            TireSampleDto latest = null;

            foreach (TireSampleDto sample in list)
            {
                if (sample.t > time)
                    break;

                latest = sample;
            }

            if (latest == null && list.Count > 0)
                latest = list[0];

            return latest;
        }

        public void Clear()
        {
            byDriver.Clear();
        }
    }
}
