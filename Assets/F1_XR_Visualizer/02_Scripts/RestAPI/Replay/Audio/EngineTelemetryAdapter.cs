using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public struct EngineTelemetryData
    {
        public float rpm;
        public bool hasRpm;
        public float throttle01;
        public float brake01;
        public int gear;
        public float speedMps;
    }

    public static class EngineTelemetryAdapter
    {
        public static EngineTelemetryData FromServer(float rpm, float throttle, float speedKph, int gear, int brake)
        {
            return new EngineTelemetryData
            {
                rpm = SanitizeRpm(rpm, out bool hasRpm),
                hasRpm = hasRpm,
                throttle01 = Normalize01(throttle),
                brake01 = Normalize01(brake),
                gear = gear,
                speedMps = Mathf.Max(0f, Safe(speedKph) / 3.6f)
            };
        }

        private static float SanitizeRpm(float rpm, out bool hasRpm)
        {
            rpm = Safe(rpm);
            hasRpm = rpm > 1000f;
            return hasRpm ? rpm : 0f;
        }

        private static float Normalize01(float value)
        {
            value = Safe(value);
            if (value > 1.5f)
                value *= 0.01f;

            return Mathf.Clamp01(value);
        }

        private static float Safe(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }
    }
}