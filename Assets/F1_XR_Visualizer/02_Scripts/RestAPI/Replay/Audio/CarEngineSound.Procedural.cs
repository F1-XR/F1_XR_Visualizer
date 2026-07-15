using System;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public partial class CarEngineSound
    {
        private void ConfigureProceduralSound()
        {
            if (proceduralClip == null)
                proceduralClip = AudioClip.Create("Procedural F1 Engine", SampleRate, 1, SampleRate, true, ReadProceduralAudio);

            proceduralSource.clip = proceduralClip;
            proceduralSource.pitch = 1f;
            PlayLoop(proceduralSource);
            MuteSampleLoops(1f);
        }

        private void UpdateProceduralSound(float rpm01, float throttle01, float speed01, float master, float responseValue)
        {
            audioRpm01 = Mathf.Lerp(audioRpm01, rpm01, responseValue);
            audioThrottle01 = Mathf.Lerp(audioThrottle01, throttle01, responseValue);
            audioSpeed01 = Mathf.Lerp(audioSpeed01, speed01, responseValue);
            audioBrake01 = Mathf.Lerp(audioBrake01, smoothBrake01, responseValue);
            audioMaster = Mathf.Lerp(audioMaster, master, responseValue);

            SetVolume(proceduralSource, master > 0f ? 1f : 0f, responseValue);
            MuteSampleLoops(responseValue);
        }

        private void ReadProceduralAudio(float[] data)
        {
            float rpm01 = Mathf.Clamp01(audioRpm01);
            float throttle01 = Mathf.Clamp01(audioThrottle01);
            float speed01 = Mathf.Clamp01(audioSpeed01);
            float brake01 = Mathf.Clamp01(audioBrake01);
            float master = Mathf.Clamp01(audioMaster);
            float roughness = settings != null ? Mathf.Clamp01(settings.proceduralRoughness) : 0.55f;
            float tone = settings != null ? Mathf.Clamp(settings.proceduralTone, 0.2f, 1.4f) : 0.85f;
            float baseFrequency = Mathf.Lerp(95f, 720f, rpm01) * tone;
            float load = Mathf.Lerp(0.04f, 1f, throttle01);
            float coast = 1f - throttle01;
            float amplitude = master * Mathf.Lerp(0.08f, 0.36f, speed01) * Mathf.Lerp(0.08f, 1.25f, throttle01);

            for (int i = 0; i < data.Length; i++)
            {
                enginePhase += baseFrequency / SampleRate;
                roughPhase += Mathf.Lerp(23f, 71f, rpm01) / SampleRate;

                if (enginePhase >= 1.0)
                    enginePhase -= Math.Floor(enginePhase);
                if (roughPhase >= 1.0)
                    roughPhase -= Math.Floor(roughPhase);

                float p = (float)enginePhase;
                float sine = Mathf.Sin(Mathf.PI * 2f * p);
                float saw = 2f * (p - Mathf.Floor(p + 0.5f));
                float pulse = sine >= 0f ? 1f : -1f;
                float harmonics =
                    sine * 0.42f +
                    Mathf.Sin(Mathf.PI * 4f * p) * 0.28f +
                    Mathf.Sin(Mathf.PI * 6f * p) * 0.18f +
                    Mathf.Sin(Mathf.PI * 12f * p) * 0.08f;
                float bite = saw * 0.36f + pulse * 0.16f + harmonics;
                float rough = NextNoise() * (0.02f + roughness * 0.05f + coast * 0.07f + brake01 * 0.06f);
                float coastRumble = Mathf.Sin(Mathf.PI * 2f * (float)roughPhase) * coast * (0.12f + brake01 * 0.12f);
                float sample = (bite * load + coastRumble + rough) * amplitude;

                data[i] = Mathf.Clamp(sample, -0.85f, 0.85f);
            }
        }

        private float NextNoise()
        {
            noiseState = noiseState * 1664525u + 1013904223u;
            return ((noiseState >> 9) / 8388608f) * 2f - 1f;
        }
    }
}
