using System.Collections.Generic;
using F1XR.RestAPI.Api;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public class ReplaySamples
    {
        private readonly Dictionary<int, List<LocationSample>> samples = new();
        private readonly Dictionary<int, int> indices = new();

        public Dictionary<int, List<LocationSample>> ByDriver => samples;
        public Dictionary<int, int> Indices => indices;

        public void Add(ReplayChunkDto chunk)
        {
            foreach (LocationSample sample in chunk.samples)
            {
                if (!samples.TryGetValue(sample.driverNumber, out List<LocationSample> list))
                {
                    list = new List<LocationSample>();
                    samples.Add(sample.driverNumber, list);
                    indices.Add(sample.driverNumber, 0);
                }

                list.Add(sample);
            }

            foreach (List<LocationSample> list in samples.Values)
            {
                list.Sort((a, b) => a.t.CompareTo(b.t));
                RemoveDuplicates(list);
            }
        }

        public void ResetIndices()
        {
            List<int> drivers = new List<int>(indices.Keys);

            foreach (int driver in drivers)
                indices[driver] = 0;
        }

        public void Clear()
        {
            samples.Clear();
            indices.Clear();
        }

        private static void RemoveDuplicates(List<LocationSample> list)
        {
            if (list.Count <= 1)
                return;

            int write = 1;

            for (int read = 1; read < list.Count; read++)
            {
                if (Mathf.Abs(list[read].t - list[write - 1].t) < 0.001f)
                    continue;

                list[write] = list[read];
                write++;
            }

            if (write < list.Count)
                list.RemoveRange(write, list.Count - write);
        }
    }
}