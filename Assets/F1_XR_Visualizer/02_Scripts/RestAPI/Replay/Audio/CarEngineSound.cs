using System;
using System.Text;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public class CarEngineSound : MonoBehaviour
    {
        private const int SampleRate = 44100;
        private const string AudioRootName = "Audio";
        private const float SourceRecoveryLogInterval = 1f;

        private static AudioClip fallbackLow;
        private static AudioClip fallbackMid;
        private static AudioClip fallbackHigh;
        private static AudioClip fallbackLoad;
        private static AudioClip fallbackCoast;
        private static AudioClip fallbackShift;
        private static int telemetryLogCount;
        private static int missingSampleLogCount;

        private readonly RuntimeLoopLayer[] loopLayers = new RuntimeLoopLayer[9];

        private CarEngineSoundSettings settings;
        private AudioSource shiftSource;
        private AudioSource idleSource;
        private AudioSource lowOnSource;
        private AudioSource lowOffSource;
        private AudioSource midOnSource;
        private AudioSource midOffSource;
        private AudioSource highOnSource;
        private AudioSource highOffSource;
        private AudioSource veryHighOnSource;
        private AudioSource veryHighOffSource;
        private AudioSource gearboxSource;
        private AudioSource proceduralSource;
        private AudioClip proceduralClip;
        private GameObject audioObject;
        private AudioListener dopplerListener;
        private Vector3 lastListenerPosition;
        private bool hasLastListenerPosition;

        private float targetRpm;
        private float targetThrottle01;
        private float targetBrake01;
        private float targetSpeedMps;
        private bool targetHasRpmTelemetry;
        private int currentGear;
        private int lastShiftGear;
        private int lastUpshiftClip = -1;
        private int lastDownshiftClip = -1;
        private float smoothRpm;
        private float smoothThrottle01;
        private float smoothBrake01;
        private float smoothSpeedMps;
        private float smoothLoad01;
        private float lastTelemetryTime;
        private float lastShiftTime;
        private float fallbackFlareEndTime;
        private float fallbackFlareDuration;
        private bool playing = true;
        private bool audible = true;
        private float audibility = 1f;
        private volatile float audioRpm01;
        private volatile float audioThrottle01;
        private volatile float audioSpeed01;
        private volatile float audioBrake01;
        private volatile float audioMaster;
        private double enginePhase;
        private double roughPhase;
        private uint noiseState = 22222u;
        private float pitchVariation = 1f;
        private float volumeVariation = 1f;
        private int sourceRecoveryLogBurst = 8;
        private float nextSourceRecoveryLogTime;

        public void SetVariation(float pitchMultiplier, float volumeMultiplier)
        {
            pitchVariation = Mathf.Clamp(pitchMultiplier, 0.94f, 1.06f);
            volumeVariation = Mathf.Clamp(volumeMultiplier, 0.75f, 1.15f);
        }
        
        public void Configure(CarEngineSoundSettings source)
        {
            settings = source;

            if (settings == null || !settings.useEngineSound)
            {
                StopAll();
                return;
            }

            settings.EnsureDefaults();
            sourceRecoveryLogBurst = 8;
            nextSourceRecoveryLogTime = 0f;
            EnsureSources();
            ApplySourceSettings();
            UpdateAudioObjectPosition();

            if (settings.mode == EngineAudioMode.Procedural)
                ConfigureProceduralSound();
            else
                ConfigureSampleLoopSound();

            targetRpm = ClampRpm(settings.idleRpm);
            smoothRpm = targetRpm;
            targetThrottle01 = 0f;
            smoothThrottle01 = 0f;
            targetBrake01 = 0f;
            smoothBrake01 = 0f;
            smoothLoad01 = 0f;
        }

        public void StopAudioNow()
        {
            StopAll();
        }

        public void UpdateTelemetry(float rpm, float throttle, float speed, int gear, int brake, int drs)
        {
            if (settings == null || !settings.useEngineSound)
                return;

            EngineTelemetryData telemetry = EngineTelemetryAdapter.FromServer(rpm, throttle, speed, gear, brake);
            targetHasRpmTelemetry = telemetry.hasRpm;
            targetRpm = ClampRpm(telemetry.hasRpm
                ? telemetry.rpm
                : EstimateFallbackRpm(telemetry.speedMps));
            targetThrottle01 = telemetry.throttle01;
            targetBrake01 = telemetry.brake01;
            targetSpeedMps = telemetry.speedMps;
            lastTelemetryTime = Time.time;

            if (telemetryLogCount < 3)
            {
                Debug.Log($"[EngineSound] telemetry rpm={targetRpm:0}, throttle01={targetThrottle01:0.00}, brake01={targetBrake01:0.00}, gear={telemetry.gear}, speedMps={targetSpeedMps:0.0}");
                telemetryLogCount++;
            }

            UpdateShiftFx(telemetry.gear, telemetry.hasRpm);
        }

        public void SetPlaying(bool value)
        {
            playing = value;

            if (playing)
                EnsureConfiguredSourcesPlaying();
        }

        private void OnEnable()
        {
            EnsureConfiguredSourcesPlaying();
        }

        public void SetAudible(bool value)
        {
            audible = value;
            audibility = value ? 1f : 0f;
        }

        public void SetAudibility(float value)
        {
            audibility = Mathf.Clamp01(value);
            audible = audibility > 0f;
        }

        private void Update()
        {
            if (settings == null || !settings.useEngineSound)
                return;

            EnsureSources();
            UpdateAudioObjectPosition();

            if (playing)
                EnsureConfiguredSourcesPlaying();

            float responseValue = ExpResponse(settings.response);
            smoothRpm = Mathf.Lerp(smoothRpm, targetRpm, responseValue);
            smoothThrottle01 = Mathf.Lerp(smoothThrottle01, targetThrottle01, responseValue);
            smoothBrake01 = Mathf.Lerp(smoothBrake01, targetBrake01, responseValue);
            smoothSpeedMps = Mathf.Lerp(smoothSpeedMps, targetSpeedMps, responseValue);

            float loadStart = Mathf.Clamp(settings.loadThreshold, 0f, 0.99f);
            float targetLoad = smoothBrake01 <= 0.05f
                ? Mathf.InverseLerp(loadStart, 1f, smoothThrottle01)
                : 0f;

            smoothLoad01 = Mathf.Lerp(smoothLoad01, targetLoad, ExpResponse(settings.loadResponse));

            float rpm01 = Mathf.InverseLerp(settings.minRpm, RpmCeiling(), smoothRpm);
            float speed01 = Mathf.InverseLerp(0f, 95f, smoothSpeedMps);
            float master = playing && audible && Time.time - lastTelemetryTime < 0.5f
                ? Mathf.Clamp01(settings.masterVolume * volumeVariation) * audibility
                : 0f;

            if (settings.mode == EngineAudioMode.Procedural)
            {
                UpdateProceduralSound(rpm01, smoothThrottle01, speed01, master, responseValue);
                return;
            }

            UpdateSampleLoopSound(smoothRpm, smoothLoad01, speed01, master, responseValue);
        }

        private void ConfigureProceduralSound()
        {
            if (proceduralClip == null)
                proceduralClip = AudioClip.Create("Procedural F1 Engine", SampleRate, 1, SampleRate, true, ReadProceduralAudio);

            proceduralSource.clip = proceduralClip;
            proceduralSource.pitch = 1f;
            PlayLoop(proceduralSource);
            MuteSampleLoops(1f);
        }

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

            loopLayers[index] = new RuntimeLoopLayer(source, sample, clip);
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

        private void UpdateShiftFx(int gear, bool hasServerRpm)
        {
            if (gear <= 0)
                return;

            currentGear = gear;

            if (lastShiftGear <= 0)
            {
                lastShiftGear = currentGear;
                return;
            }

            if (currentGear == lastShiftGear)
                return;

            int gearDelta = currentGear - lastShiftGear;
            lastShiftGear = currentGear;

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

        private void EnsureSources()
        {
            EnsureAudioObject();

            EnsureSource(ref shiftSource, "ShiftOneShot", false);
            EnsureSource(ref idleSource, "EngineIdle", true);
            EnsureSource(ref lowOnSource, "LowOn", true);
            EnsureSource(ref lowOffSource, "LowOff", true);
            EnsureSource(ref midOnSource, "MidOn", true);
            EnsureSource(ref midOffSource, "MidOff", true);
            EnsureSource(ref highOnSource, "HighOn", true);
            EnsureSource(ref highOffSource, "HighOff", true);
            EnsureSource(ref veryHighOnSource, "VeryHighOn", true);
            EnsureSource(ref veryHighOffSource, "VeryHighOff", true);
            EnsureSource(ref gearboxSource, "Gearbox", true);
            EnsureSource(ref proceduralSource, "Procedural", true);
        }

        private void EnsureAudioObject()
        {
            if (audioObject != null)
                return;

            Transform existing = transform.Find(AudioRootName);
            if (existing != null)
            {
                audioObject = existing.gameObject;
                return;
            }

            audioObject = new GameObject(AudioRootName);
            audioObject.transform.SetParent(transform, false);
            UpdateAudioObjectPosition();
        }

        private void UpdateAudioObjectPosition()
        {
            if (audioObject == null)
                return;

            audioObject.transform.localPosition = Vector3.zero;
            audioObject.transform.localRotation = Quaternion.identity;
        }

        private void EnsureSource(ref AudioSource source, string sourceName, bool loop)
        {
            if (source != null)
                return;

            Transform child = audioObject.transform.Find(sourceName);
            GameObject sourceObject = child != null ? child.gameObject : new GameObject(sourceName);
            sourceObject.transform.SetParent(audioObject.transform, false);

            source = sourceObject.GetComponent<AudioSource>();
            if (source == null)
                source = sourceObject.AddComponent<AudioSource>();

            source.playOnAwake = false;
            source.loop = loop;
            source.volume = 0f;
        }

        private void ApplySourceSettings()
        {
            ApplySourceSettings(shiftSource);
            ApplySourceSettings(idleSource);
            ApplySourceSettings(lowOnSource);
            ApplySourceSettings(lowOffSource);
            ApplySourceSettings(midOnSource);
            ApplySourceSettings(midOffSource);
            ApplySourceSettings(highOnSource);
            ApplySourceSettings(highOffSource);
            ApplySourceSettings(veryHighOnSource);
            ApplySourceSettings(veryHighOffSource);
            ApplySourceSettings(gearboxSource);
            ApplySourceSettings(proceduralSource);
        }

        private void ApplySourceSettings(AudioSource source)
        {
            if (source == null)
                return;

            float maxDistance = settings.maximumAudibleDistance > 0f
                ? settings.maximumAudibleDistance
                : settings.maxDistance;

            source.spatialBlend = Mathf.Clamp01(settings.spatialBlend);
            source.minDistance = Mathf.Max(0.01f, settings.minDistance);
            source.maxDistance = Mathf.Max(source.minDistance, maxDistance);
            source.rolloffMode = AudioRolloffMode.Custom;
            source.dopplerLevel = settings.enableCustomDoppler ? 0f : 0.1f;
            source.priority = Mathf.Clamp(settings.priority, 0, 256);
            source.SetCustomCurve(
                AudioSourceCurveType.CustomRolloff,
                new AnimationCurve(
                    new Keyframe(0f, 1f),
                    new Keyframe(source.minDistance, 1f),
                    new Keyframe(source.maxDistance, 0f)
                )
            );
        }

        private AudioClip ResolveClip(AudioClip clip, Func<AudioClip> fallback)
        {
            if (clip != null)
                return clip;

            return settings.generateFallbackClips ? fallback() : null;
        }

        private static AudioClip FirstClip(params AudioClip[] clips)
        {
            foreach (AudioClip clip in clips)
            {
                if (clip != null)
                    return clip;
            }

            return null;
        }

        private static void PlayLoop(AudioSource source)
        {
            if (source == null || source.clip == null || source.isPlaying)
                return;

            source.Play();
        }

        private void EnsureConfiguredSourcesPlaying()
        {
            if (settings == null || !settings.useEngineSound || !isActiveAndEnabled)
                return;

            if (!gameObject.activeInHierarchy || audioObject == null || !audioObject.activeInHierarchy)
                return;

            if (settings.mode == EngineAudioMode.Procedural)
            {
                EnsureSourcePlaying("Procedural", proceduralSource);
                return;
            }

            EnsureSourcePlaying("EngineIdle", idleSource);
            EnsureSourcePlaying("LowOn", lowOnSource);
            EnsureSourcePlaying("LowOff", lowOffSource);
            EnsureSourcePlaying("MidOn", midOnSource);
            EnsureSourcePlaying("MidOff", midOffSource);
            EnsureSourcePlaying("HighOn", highOnSource);
            EnsureSourcePlaying("HighOff", highOffSource);
            EnsureSourcePlaying("VeryHighOn", veryHighOnSource);
            EnsureSourcePlaying("VeryHighOff", veryHighOffSource);
            EnsureSourcePlaying("Gearbox", gearboxSource);
        }

        private void EnsureSourcePlaying(string label, AudioSource source)
        {
            if (source == null)
                return;

            bool hadClip = source.clip != null;
            bool wasPlaying = source.isPlaying;
            if (hadClip && source.enabled && !wasPlaying)
            {
                LogSourceRecovery(label, source, "restart");
                PlayLoop(source);
            }
            else if (hadClip && !source.enabled)
            {
                LogSourceRecovery(label, source, "blocked");
            }
        }

        private void LogSourceRecovery(string label, AudioSource source, string action)
        {
            float now = Time.unscaledTime;
            if (sourceRecoveryLogBurst <= 0 && now < nextSourceRecoveryLogTime)
                return;

            if (sourceRecoveryLogBurst > 0)
                sourceRecoveryLogBurst--;

            nextSourceRecoveryLogTime = now + SourceRecoveryLogInterval;

            bool audioActive = audioObject != null && audioObject.activeInHierarchy;
            Debug.Log(
                $"[EngineSound] source {action} name={label}, " +
                $"gameObjectActive={gameObject.activeInHierarchy}, audioObjectActive={audioActive}, " +
                $"sourceEnabled={source.enabled}, hasClip={source.clip != null}, isPlaying={source.isPlaying}, " +
                $"playing={playing}, audible={audible}"
            );
        }

        private void StopAll()
        {
            Stop(shiftSource);
            Stop(idleSource);
            Stop(lowOnSource);
            Stop(lowOffSource);
            Stop(midOnSource);
            Stop(midOffSource);
            Stop(highOnSource);
            Stop(highOffSource);
            Stop(veryHighOnSource);
            Stop(veryHighOffSource);
            Stop(gearboxSource);
            Stop(proceduralSource);
        }

        private void MuteSampleLoops(float responseValue)
        {
            SetVolume(idleSource, 0f, responseValue);
            SetVolume(lowOnSource, 0f, responseValue);
            SetVolume(lowOffSource, 0f, responseValue);
            SetVolume(midOnSource, 0f, responseValue);
            SetVolume(midOffSource, 0f, responseValue);
            SetVolume(highOnSource, 0f, responseValue);
            SetVolume(highOffSource, 0f, responseValue);
            SetVolume(veryHighOnSource, 0f, responseValue);
            SetVolume(veryHighOffSource, 0f, responseValue);
            SetVolume(gearboxSource, 0f, responseValue);
        }

        private static void Stop(AudioSource source)
        {
            if (source != null)
                source.Stop();
        }

        private static void SetPitch(AudioSource source, float pitch, float response)
        {
            if (source == null)
                return;

            float target = Mathf.Clamp(pitch, 0.2f, 3f);
            source.pitch = Mathf.Lerp(source.pitch, target, response);
        }

        private static void SetVolume(AudioSource source, float target, float response)
        {
            if (source != null)
                source.volume = Mathf.Lerp(source.volume, Mathf.Clamp01(target), response);
        }

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

        private void OnDestroy()
        {
            if (audioObject != null)
                Destroy(audioObject);
        }

        private sealed class RuntimeLoopLayer
        {
            public readonly AudioSource source;
            public readonly EngineLoopSample sample;
            public readonly AudioClip clip;
            public float rawWeight;

            public RuntimeLoopLayer(AudioSource source, EngineLoopSample sample, AudioClip clip)
            {
                this.source = source;
                this.sample = sample;
                this.clip = clip;
            }
        }
    }
}
