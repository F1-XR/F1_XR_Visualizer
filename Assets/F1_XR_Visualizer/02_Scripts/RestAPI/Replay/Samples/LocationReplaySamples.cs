using System.Collections.Generic;
using F1XR.RestAPI.Api;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public class LocationReplaySamples
    {
        private const float MaximumGlitchSampleGap = 0.8f;
        private const float MinimumReversedDistance = 5f;
        private const float OppositeDirectionDot = -0.8660254f;
        private const float MatchingDirectionDot = 0.8660254f;
        private const float ProgressRepairDirectionDot = 0.94f;
        private const float ProgressRepairFractionError = 0.38f;

        private readonly Dictionary<int, List<LocationSample>> samples = new();
        private readonly Dictionary<int, List<LocationSample>> sourceSamples = new();
        private readonly Dictionary<int, int> indices = new();

        public Dictionary<int, List<LocationSample>> ByDriver => samples;
        public Dictionary<int, int> Indices => indices;

        public void Add(ReplayChunkDto chunk)
        {
            foreach (LocationSample sample in chunk.samples)
            {
                if (!sourceSamples.TryGetValue(sample.driverNumber, out List<LocationSample> source))
                {
                    source = new List<LocationSample>();
                    sourceSamples.Add(sample.driverNumber, source);
                    samples.Add(sample.driverNumber, new List<LocationSample>());
                    indices.Add(sample.driverNumber, 0);
                }

                source.Add(Copy(sample));
            }

            foreach (KeyValuePair<int, List<LocationSample>> pair in sourceSamples)
            {
                List<LocationSample> source = pair.Value;
                source.Sort(CompareSamples);
                RemoveDuplicates(source);

                List<LocationSample> list = samples[pair.Key];
                list.Clear();
                foreach (LocationSample sample in source)
                    list.Add(Copy(sample));

                RemoveDirectionGlitches(list);
                RepairProgressSpikes(list);
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
            sourceSamples.Clear();
            indices.Clear();
        }

        private static int CompareSamples(LocationSample a, LocationSample b)
        {
            int result = a.t.CompareTo(b.t);
            if (result != 0)
                return result;

            result = HasTelemetry(b).CompareTo(HasTelemetry(a));
            if (result != 0)
                return result;

            result = a.x.CompareTo(b.x);
            if (result != 0)
                return result;
            result = a.y.CompareTo(b.y);
            return result != 0 ? result : a.z.CompareTo(b.z);
        }

        private static bool HasTelemetry(LocationSample sample)
        {
            return sample.speed > 0f || sample.rpm > 0f;
        }

        private static LocationSample Copy(LocationSample source)
        {
            return new LocationSample
            {
                t = source.t,
                driverNumber = source.driverNumber,
                x = source.x,
                y = source.y,
                z = source.z,
                rpm = source.rpm,
                throttle = source.throttle,
                speed = source.speed,
                nGear = source.nGear,
                n_gear = source.n_gear,
                brake = source.brake,
                drs = source.drs
            };
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

        private static void RemoveDirectionGlitches(List<LocationSample> list)
        {
            int index = 1;

            while (index < list.Count - 2)
            {
                LocationSample a = list[index - 1];
                LocationSample b = list[index];
                LocationSample c = list[index + 1];
                LocationSample d = list[index + 2];

                if (!IsDirectionGlitch(a, b, c, d))
                {
                    index++;
                    continue;
                }

                list.RemoveAt(index + 1);
                index = Mathf.Max(1, index - 1);
            }
        }

        private static bool IsDirectionGlitch(
            LocationSample a,
            LocationSample b,
            LocationSample c,
            LocationSample d)
        {
            if (!HasShortPositiveGap(a, b) ||
                !HasShortPositiveGap(b, c) ||
                !HasShortPositiveGap(c, d))
                return false;

            Vector2 before = Position(b) - Position(a);
            Vector2 reversed = Position(c) - Position(b);
            Vector2 after = Position(d) - Position(c);

            float minimumDistanceSquared = MinimumReversedDistance * MinimumReversedDistance;
            if (before.sqrMagnitude < minimumDistanceSquared ||
                reversed.sqrMagnitude < minimumDistanceSquared ||
                after.sqrMagnitude < minimumDistanceSquared)
                return false;

            before.Normalize();
            reversed.Normalize();
            after.Normalize();

            return Vector2.Dot(before, reversed) <= OppositeDirectionDot &&
                   Vector2.Dot(reversed, after) <= OppositeDirectionDot &&
                   Vector2.Dot(before, after) >= MatchingDirectionDot;
        }

        private static bool HasShortPositiveGap(LocationSample a, LocationSample b)
        {
            float duration = b.t - a.t;
            return duration > 0f && duration <= MaximumGlitchSampleGap;
        }

        private static void RepairProgressSpikes(List<LocationSample> list)
        {
            if (list.Count < 3)
                return;

            List<int> repairIndices = new();
            List<Vector3> replacements = new();
            int lastRepair = -2;

            for (int index = 1; index < list.Count - 1; index++)
            {
                if (index <= lastRepair + 1)
                    continue;

                LocationSample a = list[index - 1];
                LocationSample b = list[index];
                LocationSample c = list[index + 1];
                if (!HasShortPositiveGap(a, b) ||
                    !HasShortPositiveGap(b, c))
                    continue;

                Vector2 first = Position(b) - Position(a);
                Vector2 second = Position(c) - Position(b);
                if (first.sqrMagnitude <= MinimumReversedDistance * MinimumReversedDistance ||
                    second.sqrMagnitude <= MinimumReversedDistance * MinimumReversedDistance)
                    continue;

                Vector2 firstDirection = first.normalized;
                Vector2 secondDirection = second.normalized;
                if (Vector2.Dot(firstDirection, secondDirection) < ProgressRepairDirectionDot)
                    continue;

                float firstExpected =
                    Mathf.Max(0f, (a.speed + b.speed) * 0.5f) *
                    (b.t - a.t);
                float secondExpected =
                    Mathf.Max(0f, (b.speed + c.speed) * 0.5f) *
                    (c.t - b.t);
                float expectedTotal = firstExpected + secondExpected;
                float actualTotal = first.magnitude + second.magnitude;
                if (expectedTotal <= 0.001f || actualTotal <= 0.001f)
                    continue;

                float expectedFraction = firstExpected / expectedTotal;
                float actualFraction = first.magnitude / actualTotal;
                if (Mathf.Abs(actualFraction - expectedFraction) <
                    ProgressRepairFractionError)
                    continue;

                repairIndices.Add(index);
                replacements.Add(Vector3.Lerp(
                    new Vector3(a.x, a.y, a.z),
                    new Vector3(c.x, c.y, c.z),
                    expectedFraction));
                lastRepair = index;
            }

            for (int repair = 0; repair < repairIndices.Count; repair++)
            {
                int index = repairIndices[repair];
                Vector3 position = replacements[repair];
                list[index].x = position.x;
                list[index].y = position.y;
                list[index].z = position.z;
            }
        }

        private static Vector2 Position(LocationSample sample)
        {
            return new Vector2(sample.x, sample.y);
        }
    }
}
