using System.Collections.Generic;
using F1XR.RestAPI.Api;
using UnityEngine;
using Unity.Profiling;

namespace F1XR.RestAPI.Replay
{
    public class PositionReplaySamples
    {
        private static readonly ProfilerMarker GetMarker =
            new("F1XR.Positions.Get");

        private readonly Dictionary<int, List<PositionSampleDto>> byDriver = new();
        private readonly Dictionary<int, int> indices = new();
        private readonly List<PositionSampleDto> current = new();
        private bool indicesDirty = true;
        private bool hasPreviousTime;
        private float previousTime;

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
                    indices.Add(sample.driverNumber, 0);
                }

                list.Add(sample);
            }

            foreach (KeyValuePair<int, List<PositionSampleDto>> pair in byDriver)
            {
                List<PositionSampleDto> list = pair.Value;
                list.Sort((a, b) => a.t.CompareTo(b.t));
            }

            indicesDirty = true;
        }

        public List<PositionSampleDto> Get(float time)
        {
            using var marker = GetMarker.Auto();
            current.Clear();
            bool seek = indicesDirty ||
                !hasPreviousTime ||
                Mathf.Abs(time - previousTime) > 1f;

            foreach (KeyValuePair<int, List<PositionSampleDto>> pair in byDriver)
            {
                List<PositionSampleDto> samples = pair.Value;
                if (samples.Count == 0)
                    continue;

                int index = seek
                    ? FindIndex(samples, time)
                    : indices.TryGetValue(pair.Key, out int value)
                        ? Mathf.Clamp(value, 0, samples.Count - 1)
                        : 0;

                while (index > 0 && samples[index].t > time)
                    index--;

                while (index < samples.Count - 1 &&
                    samples[index + 1].t <= time)
                {
                    index++;
                }

                indices[pair.Key] = index;
                current.Add(samples[index]);
            }

            indicesDirty = false;
            hasPreviousTime = true;
            previousTime = time;
            current.Sort((a, b) => a.position.CompareTo(b.position));
            return current;
        }

        private static int FindIndex(List<PositionSampleDto> samples, float time)
        {
            int low = 0;
            int high = samples.Count - 1;

            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                if (samples[middle].t <= time)
                    low = middle;
                else
                    high = middle - 1;
            }

            return low;
        }

        public void Clear()
        {
            byDriver.Clear();
            indices.Clear();
            current.Clear();
            indicesDirty = true;
            hasPreviousTime = false;
            previousTime = 0f;
        }
    }
}
