using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public partial class CarEngineSound : MonoBehaviour
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
            if (playing)
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

        private void OnDestroy()
        {
            if (audioObject != null)
                Destroy(audioObject);
        }

    }
}
