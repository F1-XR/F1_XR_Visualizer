using System;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    [Serializable]
    public class CarEngineSoundSettings
    {
        public bool useEngineSound = true;
        public bool generateFallbackClips;

        [Header("Team-Based Mode")]
        [SerializeField]
        public bool useTeamBasedEngineAudio = true;
        [SerializeField]
        public bool enableNewGridStartAudio = true;
        public EngineAudioProfile redBullProfile;
        public EngineAudioProfile mercedesProfile;
        public EngineAudioProfile ferrariProfile;

        [Header("Grid Start")]
        public AudioClip redBullGridStartClip;
        public float gridStartLaunchOffsetSeconds = 1.1f;
        public float redBullStartGainA = 0.22f;
        public float redBullStartGainB = 0.18f;
        public float redBullStartSecondDelay = 0.04f;
        public float selectedStartGain = 0.24f;

        [Header("Mode")]
        public EngineAudioMode mode = EngineAudioMode.SampleLoop;

        [Header("Playback Target")]
        public bool redBullOnly = true;
        public string teamNameFilter = "Red Bull";

        [Header("Sample Loop Layers")]
        public EngineLoopSample idle = new EngineLoopSample(5000f, EngineLoadType.Idle, 0.8f);
        public EngineLoopSample lowOn = new EngineLoopSample(7000f, EngineLoadType.OnLoad);
        public EngineLoopSample lowOff = new EngineLoopSample(7000f, EngineLoadType.OffLoad, 0.75f);
        public EngineLoopSample midOn = new EngineLoopSample(11000f, EngineLoadType.OnLoad);
        public EngineLoopSample midOff = new EngineLoopSample(11000f, EngineLoadType.OffLoad, 0.8f);
        public EngineLoopSample highOn = new EngineLoopSample(15000f, EngineLoadType.OnLoad);
        public EngineLoopSample highOff = new EngineLoopSample(15000f, EngineLoadType.OffLoad, 0.85f);
        public EngineLoopSample veryHighOn = new EngineLoopSample(18000f, EngineLoadType.OnLoad, 0.9f);
        public EngineLoopSample veryHighOff = new EngineLoopSample(18000f, EngineLoadType.OffLoad, 0.75f);

        [Header("Legacy Clip Slots")]
        public AudioClip idleLoop;
        public AudioClip lowOnLoop;
        public AudioClip lowOffLoop;
        public AudioClip midOnLoop;
        public AudioClip midOffLoop;
        public AudioClip highOnLoop;
        public AudioClip highOffLoop;
        public AudioClip maxRpmLoop;
        public AudioClip veryHighOnLoop;
        public AudioClip veryHighOffLoop;

        public AudioClip lowLoop;
        public AudioClip midLoop;
        public AudioClip highLoop;
        public AudioClip loadLoop;
        public AudioClip coastLoop;
        public AudioClip gearShift;
        public AudioClip upshift01;
        public AudioClip upshift02;
        public AudioClip downshift01;
        public AudioClip downshift02;
        public AudioClip gearboxWhine;
        public AudioClip revLimiter;
        public AudioClip overrunCrackle;

        [Header("Procedural Test")]
        public float proceduralTone = 0.85f;
        public float proceduralRoughness = 0.55f;

        [Header("Mix")]
        public float masterVolume = 0.65f;
        public int maxActiveCars = 8;
        public float minRpm = 4500f;
        public float maxRpm = 18000f;
        public float idleRpm = 5000f;
        public float offVolumeScale = 0.75f;
        public float response = 10f;
        public float loadThreshold = 0.05f;
        public float minimumRpmVolume = 0.35f;
        public float fullVolumeRpm = 12000f;
        public float loadResponse = 12f;
        public bool useSpeedRpmFallback = true;

        [Header("Shift FX")]
        public float shiftMinInterval = 0.12f;
        public float shiftPitchRandom = 0.03f;
        public float shiftVolume = 0.45f;
        public float downshiftFallbackFlare = 0.08f;
        public float fallbackFlareSeconds = 0.18f;

        [Header("3D Audio")]
        public float spatialBlend = 1f;
        public float minDistance = 0.2f;
        public float maxDistance = 12f;
        public float maximumAudibleDistance = 12f;
        public int priority = 128;

        [Header("Custom Doppler")]
        public bool enableCustomDoppler = true;
        public float speedOfSound = 343f;
        public float dopplerStrength = 0.6f;
        public float minimumDopplerPitch = 0.88f;
        public float maximumDopplerPitch = 1.15f;

        [Header("Audio LOD")]
        public bool enableFullEngineLayers = true;
        public bool selectedCarGetsFullAudio;
        public int fadeOutCars = 2;
        public float fadeOutVolume = 0.35f;

        public void EnsureDefaults()
        {
            idle = EnsureSample(idle, 5000f, EngineLoadType.Idle, 0.8f);
            lowOn = EnsureSample(lowOn, 7000f, EngineLoadType.OnLoad, 1f);
            lowOff = EnsureSample(lowOff, 7000f, EngineLoadType.OffLoad, 0.75f);
            midOn = EnsureSample(midOn, 11000f, EngineLoadType.OnLoad, 1f);
            midOff = EnsureSample(midOff, 11000f, EngineLoadType.OffLoad, 0.8f);
            highOn = EnsureSample(highOn, 15000f, EngineLoadType.OnLoad, 1f);
            highOff = EnsureSample(highOff, 15000f, EngineLoadType.OffLoad, 0.85f);
            veryHighOn = EnsureSample(veryHighOn, 18000f, EngineLoadType.OnLoad, 0.9f);
            veryHighOff = EnsureSample(veryHighOff, 18000f, EngineLoadType.OffLoad, 0.75f);
        }

        private static EngineLoopSample EnsureSample(EngineLoopSample sample, float baseRpm, EngineLoadType loadType, float gain)
        {
            if (sample == null)
                sample = new EngineLoopSample(baseRpm, loadType, gain);

            if (sample.baseRpm <= 0f)
                sample.baseRpm = baseRpm;

            sample.loadType = loadType;
            sample.isLoop = true;

            if (sample.minimumPitch <= 0f)
                sample.minimumPitch = 0.82f;

            if (sample.maximumPitch <= sample.minimumPitch)
                sample.maximumPitch = 1.15f;

            if (sample.gain <= 0f)
                sample.gain = gain;

            return sample;
        }

        public CarEngineSoundSettings CloneForProfile(EngineAudioProfile profile)
        {
            CarEngineSoundSettings copy = (CarEngineSoundSettings)MemberwiseClone();
            copy.idle = CloneSample(idle);
            copy.lowOn = CloneSample(lowOn);
            copy.lowOff = CloneSample(lowOff);
            copy.midOn = CloneSample(midOn);
            copy.midOff = CloneSample(midOff);
            copy.highOn = CloneSample(highOn);
            copy.highOff = CloneSample(highOff);
            copy.veryHighOn = CloneSample(veryHighOn);
            copy.veryHighOff = CloneSample(veryHighOff);
            copy.EnsureDefaults();
            profile?.ApplyTo(copy);
            return copy;
        }

        private static EngineLoopSample CloneSample(EngineLoopSample source)
        {
            if (source == null)
                return null;

            return new EngineLoopSample
            {
                clip = source.clip,
                baseRpm = source.baseRpm,
                loadType = source.loadType,
                isLoop = source.isLoop,
                minimumPitch = source.minimumPitch,
                maximumPitch = source.maximumPitch,
                gain = source.gain,
                perspective = source.perspective
            };
        }
    }
}