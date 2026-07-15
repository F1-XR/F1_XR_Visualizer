using System;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public partial class CarEngineSound
    {
        private void ConfigureSampleLoopSound()
        {
            Stop(proceduralSource);
            audioMaster = 0f;

            ConfigureLayer(0, idleSource, settings.idle, FirstClip(settings.idle.clip, settings.idleLoop), () => FallbackLow);
            ConfigureLayer(1, lowOnSource, settings.lowOn, FirstClip(settings.lowOn.clip, settings.lowOnLoop, settings.lowLoop, settings.loadLoop), () => FallbackLoad);
            ConfigureLayer(2, lowOffSource, settings.lowOff, FirstClip(settings.lowOff.clip, settings.lowOffLoop, settings.lowLoop, settings.coastLoop), () => FallbackLow);
            ConfigureLayer(3, midOnSource, settings.midOn, FirstClip(settings.midOn.clip, settings.midOnLoop, settings.midLoop, settings.loadLoop), () => FallbackLoad);
            ConfigureLayer(4, midOffSource, settings.midOff, FirstClip(settings.midOff.clip, settings.midOffLoop, settings.midLoop, settings.coastLoop), () => FallbackMid);
            ConfigureLayer(5, highOnSource, settings.highOn, FirstClip(settings.highOn.clip, settings.highOnLoop, settings.highLoop, settings.loadLoop), () => FallbackHigh);
            ConfigureLayer(6, highOffSource, settings.highOff, FirstClip(settings.highOff.clip, settings.highOffLoop, settings.highLoop, settings.coastLoop), () => FallbackHigh);
            ConfigureLayer(7, veryHighOnSource, settings.veryHighOn, FirstClip(settings.veryHighOn.clip, settings.veryHighOnLoop, settings.maxRpmLoop, settings.highLoop), () => FallbackHigh);
            ConfigureLayer(8, veryHighOffSource, settings.veryHighOff, FirstClip(settings.veryHighOff.clip, settings.veryHighOffLoop, settings.highOffLoop, settings.coastLoop), () => FallbackCoast);

            gearboxSource.clip = settings.gearboxWhine;
            PlayLoop(gearboxSource);
            LogMissingSamples();
        }

        private void ConfigureLayer(int index, AudioSource source, EngineLoopSample sample, AudioClip clip, Func<AudioClip> fallback)
        {
            clip = ResolveClip(clip, fallback);

            loopLayers[index] = new RuntimeLoopLayer(source, sample);
            source.clip = clip;
            source.loop = true;
            source.volume = 0f;
            PlayLoop(source);
        }

        private void UpdateSampleLoopSound(float rpm, float load01, float speed01, float master, float responseValue)
        {
            float mixRpm = ApplyFallbackFlare(rpm);
            float onSum = 0f;
            float offSum = 0f;
            float idleSum = 0f;
            RuntimeLoopLayer bestOn = null;
            RuntimeLoopLayer bestOff = null;
            float bestOnWeight = 0f;
            float bestOffWeight = 0f;

            for (int i = 0; i < loopLayers.Length; i++)
            {
                RuntimeLoopLayer layer = loopLayers[i];
                if (layer == null || layer.source == null || layer.source.clip == null)
                {
                    if (layer != null)
                        layer.rawWeight = 0f;
                    continue;
                }

                layer.rawWeight = RawRpmWeight(mixRpm, layer.sample.baseRpm);

                if (layer.sample.loadType == EngineLoadType.Idle)
                {
                    layer.rawWeight *= 1f - SmoothEdge(mixRpm, settings.idleRpm * 0.95f, settings.lowOn.baseRpm * 0.92f);
                    idleSum += layer.rawWeight;
                }
                else if (layer.sample.loadType == EngineLoadType.OnLoad)
                {
                    onSum += layer.rawWeight;
                    if (layer.rawWeight > bestOnWeight)
                    {
                        bestOnWeight = layer.rawWeight;
                        bestOn = layer;
                    }
                }
                else
                {
                    offSum += layer.rawWeight;
                    if (layer.rawWeight > bestOffWeight)
                    {
                        bestOffWeight = layer.rawWeight;
                        bestOff = layer;
                    }
                }
            }

            if (!settings.enableFullEngineLayers)
            {
                onSum = bestOnWeight;
                offSum = bestOffWeight;
            }

            float offLoad = 1f - load01;
            float offScale = Mathf.Clamp01(settings.offVolumeScale);
            float rpmVolume = RpmVolumeScale(mixRpm);
            float dopplerPitch = CustomDopplerPitch(smoothSpeedMps);

            for (int i = 0; i < loopLayers.Length; i++)
            {
                RuntimeLoopLayer layer = loopLayers[i];
                if (layer == null || layer.source == null)
                    continue;

                float targetVolume = 0f;
                if (layer.source.clip != null)
                {
                    float rpmWeight = layer.rawWeight;
                    if (!settings.enableFullEngineLayers &&
                        layer.sample.loadType == EngineLoadType.OnLoad &&
                        layer != bestOn)
                    {
                        rpmWeight = 0f;
                    }
                    else if (!settings.enableFullEngineLayers &&
                        layer.sample.loadType == EngineLoadType.OffLoad &&
                        layer != bestOff)
                    {
                        rpmWeight = 0f;
                    }

                    if (layer.sample.loadType == EngineLoadType.Idle)
                    {
                        float idleWeight = idleSum > 0.0001f ? rpmWeight / idleSum : 0f;
                        targetVolume = idleWeight * master * layer.sample.gain * (1f - speed01 * 0.45f);
                    }
                    else if (layer.sample.loadType == EngineLoadType.OnLoad)
                    {
                        float normalized = onSum > 0.0001f ? rpmWeight / onSum : 0f;
                        targetVolume = normalized * load01 * master * layer.sample.gain;
                    }
                    else
                    {
                        float normalized = offSum > 0.0001f ? rpmWeight / offSum : 0f;
                        targetVolume = normalized * offLoad * offScale * master * layer.sample.gain;
                    }

                    targetVolume *= rpmVolume;

                    float pitch = Mathf.Clamp(
                        mixRpm / Mathf.Max(1f, layer.sample.baseRpm),
                        layer.sample.minimumPitch,
                        layer.sample.maximumPitch
                    );
                    SetPitch(layer.source, pitch * pitchVariation * dopplerPitch, responseValue);
                }

                SetVolume(layer.source, targetVolume, responseValue);
            }

            UpdateGearbox(speed01, master, responseValue);
            SetVolume(proceduralSource, 0f, responseValue);
        }

        private void UpdateGearbox(float speed01, float master, float responseValue)
        {
            if (gearboxSource == null || gearboxSource.clip == null)
                return;

            SetPitch(gearboxSource, Mathf.Lerp(0.75f, 1.35f, speed01) * pitchVariation, responseValue);
            SetVolume(gearboxSource, speed01 * master * 0.18f, responseValue);
        }

        private float ApplyFallbackFlare(float rpm)
        {
            if (targetHasRpmTelemetry || Time.time >= fallbackFlareEndTime || fallbackFlareDuration <= 0f)
                return rpm;

            float remaining01 = Mathf.Clamp01((fallbackFlareEndTime - Time.time) / fallbackFlareDuration);
            float flare = 1f + Mathf.Clamp(settings.downshiftFallbackFlare, 0f, 0.2f) * remaining01;
            return ClampRpm(rpm * flare);
        }

        private void UpdateShiftFx(int gear, bool hasServerRpm)
        {
            if (gear <= 0)
                return;

            if (lastShiftGear <= 0)
            {
                lastShiftGear = gear;
                return;
            }

            if (gear == lastShiftGear)
                return;

            int gearDelta = gear - lastShiftGear;
            lastShiftGear = gear;

            if (Mathf.Abs(gearDelta) > 3)
                return;

            float minInterval = Mathf.Max(0.02f, settings.shiftMinInterval);
            if (Time.time - lastShiftTime < minInterval)
                return;

            PlayShift(gearDelta > 0);
            lastShiftTime = Time.time;

            if (gearDelta < 0 && !hasServerRpm)
            {
                fallbackFlareDuration = Mathf.Max(0.05f, settings.fallbackFlareSeconds);
                fallbackFlareEndTime = Time.time + fallbackFlareDuration;
            }
        }

        private void PlayShift(bool upshift)
        {
            if (shiftSource == null)
                return;

            AudioClip clip = PickShiftClip(upshift);
            if (clip == null)
                return;

            float random = Mathf.Abs(settings.shiftPitchRandom);
            shiftSource.pitch = UnityEngine.Random.Range(1f - random, 1f + random);
            shiftSource.PlayOneShot(clip, Mathf.Clamp01(settings.masterVolume) * Mathf.Clamp01(settings.shiftVolume));
        }

        private AudioClip PickShiftClip(bool upshift)
        {
            return upshift
                ? PickShiftClip(settings.upshift01, settings.upshift02, ref lastUpshiftClip)
                : PickShiftClip(settings.downshift01, settings.downshift02, ref lastDownshiftClip);
        }

        private AudioClip PickShiftClip(AudioClip first, AudioClip second, ref int last)
        {
            if (first == null && second == null)
                return ResolveClip(settings.gearShift, () => FallbackShift);

            if (first != null && second != null)
            {
                int next = UnityEngine.Random.Range(0, 2);
                if (next == last)
                    next = 1 - next;

                last = next;
                return next == 0 ? first : second;
            }

            last = first != null ? 0 : 1;
            return first != null ? first : second;
        }

        private sealed class RuntimeLoopLayer
        {
            public readonly AudioSource source;
            public readonly EngineLoopSample sample;
            public float rawWeight;

            public RuntimeLoopLayer(AudioSource source, EngineLoopSample sample)
            {
                this.source = source;
                this.sample = sample;
            }
        }
    }
}
