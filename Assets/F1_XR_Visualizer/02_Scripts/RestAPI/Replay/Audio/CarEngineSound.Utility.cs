using System.Text;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public partial class CarEngineSound
    {
        private float EstimateFallbackRpm(float speedMps)
        {
            if (!settings.useSpeedRpmFallback)
                return settings.idleRpm;

            float speed01 = Mathf.InverseLerp(0f, 95f, speedMps);
            return Mathf.Lerp(settings.idleRpm, RpmCeiling() * 0.92f, speed01);
        }

        private float ClampRpm(float rpm)
        {
            if (float.IsNaN(rpm) || float.IsInfinity(rpm) || rpm < 0f)
                rpm = settings != null ? settings.idleRpm : 5000f;

            return Mathf.Clamp(rpm, 0f, RpmCeiling() * 1.15f);
        }

        private float CustomDopplerPitch(float speedMps)
        {
            if (settings == null || !settings.enableCustomDoppler)
                return 1f;

            Transform listener = DopplerListenerTransform();
            if (listener == null)
                return 1f;

            Vector3 sourcePosition = audioObject != null ? audioObject.transform.position : transform.position;
            Vector3 toListener = listener.position - sourcePosition;
            if (toListener.sqrMagnitude < 0.0001f)
                return 1f;

            float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
            Vector3 directionToListener = toListener.normalized;
            Vector3 sourceVelocity = transform.forward * Mathf.Max(0f, speedMps);
            float sourceTowardListener = Vector3.Dot(sourceVelocity, directionToListener);
            float listenerTowardSource = 0f;

            if (hasLastListenerPosition)
            {
                Vector3 listenerVelocity = (listener.position - lastListenerPosition) / deltaTime;
                listenerTowardSource = Vector3.Dot(listenerVelocity, -directionToListener);
            }

            lastListenerPosition = listener.position;
            hasLastListenerPosition = true;

            float speedOfSound = Mathf.Max(1f, settings.speedOfSound);
            float rawPitch = (speedOfSound + listenerTowardSource) / Mathf.Max(1f, speedOfSound - sourceTowardListener);
            float strength = Mathf.Clamp01(settings.dopplerStrength);
            float pitch = Mathf.Lerp(1f, rawPitch, strength);
            float minPitch = Mathf.Max(0.01f, settings.minimumDopplerPitch);
            float maxPitch = Mathf.Max(minPitch, settings.maximumDopplerPitch);

            return Mathf.Clamp(pitch, minPitch, maxPitch);
        }

        private Transform DopplerListenerTransform()
        {
            if (dopplerListener == null || !dopplerListener.isActiveAndEnabled)
            {
                dopplerListener = UnityEngine.Object.FindAnyObjectByType<AudioListener>();
                hasLastListenerPosition = false;
            }

            return dopplerListener != null ? dopplerListener.transform : null;
        }

        private float RpmVolumeScale(float rpm)
        {
            if (settings == null)
                return 1f;

            float minVolume = Mathf.Clamp01(settings.minimumRpmVolume);
            float fullRpm = Mathf.Max(settings.minRpm + 1f, settings.fullVolumeRpm);
            float rpm01 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(settings.minRpm, fullRpm, rpm));
            return Mathf.Lerp(minVolume, 1f, rpm01);
        }

        private float RpmCeiling()
        {
            if (settings == null)
                return 18000f;

            float ceiling = Mathf.Max(settings.maxRpm, settings.idleRpm + 1000f);
            ceiling = Mathf.Max(ceiling, settings.idle.baseRpm);
            ceiling = Mathf.Max(ceiling, settings.lowOn.baseRpm);
            ceiling = Mathf.Max(ceiling, settings.lowOff.baseRpm);
            ceiling = Mathf.Max(ceiling, settings.midOn.baseRpm);
            ceiling = Mathf.Max(ceiling, settings.midOff.baseRpm);
            ceiling = Mathf.Max(ceiling, settings.highOn.baseRpm);
            ceiling = Mathf.Max(ceiling, settings.highOff.baseRpm);
            ceiling = Mathf.Max(ceiling, settings.veryHighOn.baseRpm);
            ceiling = Mathf.Max(ceiling, settings.veryHighOff.baseRpm);
            return Mathf.Max(1000f, ceiling);
        }

        private float ExpResponse(float response)
        {
            return 1f - Mathf.Exp(-Mathf.Max(0.01f, response) * Time.deltaTime);
        }

        private static float RawRpmWeight(float rpm, float baseRpm)
        {
            rpm = Mathf.Max(1f, rpm);
            baseRpm = Mathf.Max(1f, baseRpm);
            float logRatio = Mathf.Abs(Mathf.Log(rpm / baseRpm));
            return 1f / (0.015f + logRatio * logRatio * 5f);
        }

        private static float SmoothEdge(float value, float start, float end)
        {
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, end, value));
        }

        private void LogMissingSamples()
        {
            if (missingSampleLogCount >= 1)
                return;

            StringBuilder builder = new StringBuilder();
            AppendMissing(builder, "idle", idleSource);
            AppendMissing(builder, "high_on", highOnSource);
            AppendMissing(builder, "high_off", highOffSource);
            AppendMissing(builder, "mid_off", midOffSource);
            AppendMissing(builder, "downshift_01", settings.downshift01);
            AppendMissing(builder, "downshift_02", settings.downshift02);

            if (builder.Length == 0)
                return;

            Debug.LogWarning("[EngineSound] SampleLoop is active but these sample slots are empty: " + builder);
            missingSampleLogCount++;
        }

        private static void AppendMissing(StringBuilder builder, string label, AudioSource source)
        {
            if (source != null && source.clip != null)
                return;

            AppendMissing(builder, label);
        }

        private static void AppendMissing(StringBuilder builder, string label, AudioClip clip)
        {
            if (clip != null)
                return;

            AppendMissing(builder, label);
        }

        private static void AppendMissing(StringBuilder builder, string label)
        {
            if (builder.Length > 0)
                builder.Append(", ");

            builder.Append(label);
        }

        private AudioClip FallbackLow => fallbackLow ??= CreateEngineLoop("Generated Engine Low", 82f, 0.55f);
        private AudioClip FallbackMid => fallbackMid ??= CreateEngineLoop("Generated Engine Mid", 138f, 0.72f);
        private AudioClip FallbackHigh => fallbackHigh ??= CreateEngineLoop("Generated Engine High", 235f, 0.86f);
        private AudioClip FallbackLoad => fallbackLoad ??= CreateEngineLoop("Generated Engine Load", 176f, 1f);
        private AudioClip FallbackCoast => fallbackCoast ??= CreateEngineLoop("Generated Engine Coast", 118f, 0.35f);
        private AudioClip FallbackShift => fallbackShift ??= CreateShiftClip();

        private static AudioClip CreateEngineLoop(string name, float baseFrequency, float bite)
        {
            int length = SampleRate;
            float[] data = new float[length];

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;
                float cycle = t * baseFrequency;
                float saw = 2f * (cycle - Mathf.Floor(cycle + 0.5f));
                float pulse = Mathf.Sign(Mathf.Sin(Mathf.PI * 2f * cycle * 3f));
                float harmonic = Mathf.Sin(Mathf.PI * 2f * cycle * 2f) * 0.35f
                    + Mathf.Sin(Mathf.PI * 2f * cycle * 4f) * 0.18f
                    + Mathf.Sin(Mathf.PI * 2f * cycle * 6f) * 0.08f;
                float roughness = Mathf.Sin(Mathf.PI * 2f * 31f * t) * 0.035f;
                data[i] = Mathf.Clamp((saw * 0.45f + pulse * 0.12f + harmonic + roughness) * 0.22f * bite, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create(name, length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip CreateShiftClip()
        {
            int length = Mathf.RoundToInt(SampleRate * 0.14f);
            float[] data = new float[length];

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;
                float u = i / (float)(length - 1);
                float frequency = Mathf.Lerp(950f, 260f, u);
                float envelope = Mathf.Exp(-u * 8f);
                data[i] = Mathf.Sin(Mathf.PI * 2f * frequency * t) * envelope * 0.4f;
            }

            AudioClip clip = AudioClip.Create("Generated Gear Shift", length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
