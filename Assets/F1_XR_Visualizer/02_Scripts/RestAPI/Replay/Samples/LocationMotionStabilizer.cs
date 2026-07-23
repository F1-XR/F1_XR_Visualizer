using System.Collections.Generic;
using F1XR.RestAPI.Api;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public static class LocationMotionStabilizer
    {
        private const float ContinuityGap = 1f;
        private const float SmoothingWindow = 0.75f;
        private const float LocationUnitsPerMeter = 10f;
        private const float MinimumGlitchDistance = 5f;
        private const float OppositeDirectionDot = -0.8660254f;
        private const float MatchingDirectionDot = 0.8660254f;

        public static bool Apply(List<LocationSample> samples)
        {
            if (samples == null || samples.Count < 5)
                return false;

            bool applied = false;
            int start = 0;
            while (start < samples.Count)
            {
                int end = start + 1;
                while (end < samples.Count &&
                       samples[end].t - samples[end - 1].t <= ContinuityGap)
                {
                    end++;
                }

                applied |= ApplyRange(samples, start, end);
                start = end;
            }

            return applied;
        }

        private static bool ApplyRange(
            List<LocationSample> samples,
            int start,
            int end)
        {
            int count = end - start;
            if (count < 5 || !HasTelemetry(samples, start, end))
                return false;

            List<int> pathIndices = CleanPathIndices(samples, start, end);
            if (pathIndices.Count < 2)
                return false;

            Vector3[] pathPositions = new Vector3[pathIndices.Count];
            float[] pathProgress = new float[pathIndices.Count];
            for (int i = 0; i < pathIndices.Count; i++)
            {
                pathPositions[i] = Position(samples[pathIndices[i]]);
                if (i > 0)
                {
                    pathProgress[i] = pathProgress[i - 1] +
                        Vector3.Distance(pathPositions[i - 1], pathPositions[i]);
                }
            }

            float maximum = pathProgress[pathProgress.Length - 1];
            if (maximum <= Mathf.Epsilon)
                return false;

            float[] observed = ObservedProgress(
                samples,
                start,
                end,
                pathIndices,
                pathProgress);
            float[] expected = ExpectedProgress(samples, start, end, observed);
            float[] errors = new float[count];
            for (int i = 0; i < count; i++)
                errors[i] = observed[i] - expected[i];

            float[] smoothErrors = SmoothErrors(samples, start, end, errors);
            float[] targets = new float[count];
            for (int i = 0; i < count; i++)
            {
                float target = Mathf.Clamp(expected[i] + smoothErrors[i], 0f, maximum);
                targets[i] = i > 0 ? Mathf.Max(targets[i - 1], target) : target;
            }

            targets[0] = 0f;
            targets[count - 1] = maximum;
            ApplyProgress(
                samples,
                start,
                end,
                pathPositions,
                pathProgress,
                targets);
            return true;
        }

        private static bool HasTelemetry(
            List<LocationSample> samples,
            int start,
            int end)
        {
            int valid = 0;
            for (int i = start; i < end; i++)
            {
                if (samples[i].speed > 0f)
                    valid++;
            }

            return valid >= Mathf.Max(3, (end - start) / 2);
        }

        private static List<int> CleanPathIndices(
            List<LocationSample> samples,
            int start,
            int end)
        {
            List<int> active = new(end - start);
            for (int i = start; i < end; i++)
                active.Add(i);

            int index = 1;
            while (index < active.Count - 2)
            {
                if (!IsDirectionGlitch(
                        samples[active[index - 1]],
                        samples[active[index]],
                        samples[active[index + 1]],
                        samples[active[index + 2]]))
                {
                    index++;
                    continue;
                }

                active.RemoveAt(index + 1);
                index = Mathf.Max(1, index - 1);
            }

            return active;
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

            Vector2 before = Position2(b) - Position2(a);
            Vector2 reversed = Position2(c) - Position2(b);
            Vector2 after = Position2(d) - Position2(c);
            float minimumSquared = MinimumGlitchDistance * MinimumGlitchDistance;
            if (before.sqrMagnitude < minimumSquared ||
                reversed.sqrMagnitude < minimumSquared ||
                after.sqrMagnitude < minimumSquared)
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
            return duration > 0f && duration <= ContinuityGap;
        }

        private static float[] ObservedProgress(
            List<LocationSample> samples,
            int start,
            int end,
            List<int> pathIndices,
            float[] pathProgress)
        {
            int count = end - start;
            float[] result = new float[count];
            int path = 0;

            for (int index = start; index < end; index++)
            {
                while (path < pathIndices.Count && pathIndices[path] < index)
                    path++;

                if (path < pathIndices.Count && pathIndices[path] == index)
                {
                    result[index - start] = pathProgress[path];
                    continue;
                }

                int beforePath = Mathf.Max(0, path - 1);
                int afterPath = Mathf.Min(pathIndices.Count - 1, path);
                LocationSample before = samples[pathIndices[beforePath]];
                LocationSample after = samples[pathIndices[afterPath]];
                float duration = Mathf.Max(0.001f, after.t - before.t);
                float interpolation = Mathf.Clamp01((samples[index].t - before.t) / duration);
                result[index - start] = Mathf.Lerp(
                    pathProgress[beforePath],
                    pathProgress[afterPath],
                    interpolation);
            }

            return result;
        }

        private static float[] ExpectedProgress(
            List<LocationSample> samples,
            int start,
            int end,
            float[] observed)
        {
            float[] result = new float[end - start];
            for (int index = start + 1; index < end; index++)
            {
                LocationSample previous = samples[index - 1];
                LocationSample current = samples[index];
                float duration = Mathf.Max(0f, current.t - previous.t);
                float distance;

                if (previous.speed > 0f && current.speed > 0f)
                {
                    float averageSpeed = (previous.speed + current.speed) * 0.5f;
                    distance = averageSpeed / 3.6f * duration * LocationUnitsPerMeter;
                }
                else
                {
                    int local = index - start;
                    distance = Mathf.Max(0f, observed[local] - observed[local - 1]);
                }

                result[index - start] = result[index - start - 1] + distance;
            }

            return result;
        }

        private static float[] SmoothErrors(
            List<LocationSample> samples,
            int start,
            int end,
            float[] errors)
        {
            float[] result = new float[end - start];
            int windowStart = start;
            int windowEnd = start;

            for (int index = start; index < end; index++)
            {
                float time = samples[index].t;
                while (windowStart < end &&
                       samples[windowStart].t < time - SmoothingWindow)
                {
                    windowStart++;
                }
                while (windowEnd < end &&
                       samples[windowEnd].t <= time + SmoothingWindow)
                {
                    windowEnd++;
                }

                float weightedSum = 0f;
                float weightSum = 0f;
                for (int other = windowStart; other < windowEnd; other++)
                {
                    float distance = Mathf.Abs(samples[other].t - time);
                    float weight = Mathf.Max(0f, 1f - distance / SmoothingWindow);
                    weightedSum += errors[other - start] * weight;
                    weightSum += weight;
                }

                result[index - start] = weightSum > 0f
                    ? weightedSum / weightSum
                    : errors[index - start];
            }

            return result;
        }

        private static void ApplyProgress(
            List<LocationSample> samples,
            int start,
            int end,
            Vector3[] pathPositions,
            float[] pathProgress,
            float[] targets)
        {
            int segment = 0;
            for (int index = start; index < end; index++)
            {
                float target = targets[index - start];
                while (segment < pathProgress.Length - 2 &&
                       pathProgress[segment + 1] < target)
                {
                    segment++;
                }

                float duration = Mathf.Max(
                    0.0001f,
                    pathProgress[segment + 1] - pathProgress[segment]);
                float interpolation = Mathf.Clamp01(
                    (target - pathProgress[segment]) / duration);
                Vector3 position = Vector3.Lerp(
                    pathPositions[segment],
                    pathPositions[segment + 1],
                    interpolation);
                samples[index].x = position.x;
                samples[index].y = position.y;
                samples[index].z = position.z;
            }
        }

        private static Vector3 Position(LocationSample sample)
        {
            return new Vector3(sample.x, sample.y, sample.z);
        }

        private static Vector2 Position2(LocationSample sample)
        {
            return new Vector2(sample.x, sample.y);
        }
    }
}
