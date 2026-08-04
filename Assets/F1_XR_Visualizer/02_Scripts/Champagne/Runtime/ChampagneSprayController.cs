using UnityEngine;

namespace F1XR.Champagne
{
    public sealed class ChampagneSprayController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] ParticleSystem liquidJetParticle;
        [SerializeField] ParticleSystem foamParticle;
        [SerializeField] ParticleSystem mistParticle;
        [SerializeField] AudioSource sprayAudioSource;

        [Header("Liquid Jet")]
        [SerializeField] float liquidJetConeAngle = 5f;
        [SerializeField] float liquidJetStartSpeedMin = 5f;
        [SerializeField] float liquidJetStartSpeedMax = 9f;
        [SerializeField] float liquidJetStartSizeMin = 0.008f;
        [SerializeField] float liquidJetStartSizeMax = 0.02f;
        [SerializeField] float liquidJetLifetimeMin = 0.25f;
        [SerializeField] float liquidJetLifetimeMax = 0.55f;
        [SerializeField] float liquidJetEmissionMin = 20f;
        [SerializeField] float liquidJetEmissionMax = 130f;
        [SerializeField] float liquidJetGravityModifier = 0.8f;
        [SerializeField] float liquidJetNoiseStrength = 0.25f;
        [SerializeField] float sprayVelocityRandomness = 0.18f;

        [Header("Foam")]
        [SerializeField] int foamBurstCount = 24;
        [SerializeField] float foamEmissionRate = 45f;
        [SerializeField] float foamStartSpeed = 2.2f;
        [SerializeField] float foamStartSize = 0.035f;
        [SerializeField] float foamLifetime = 0.45f;
        [SerializeField] bool initialFoamBurstEnabled = true;
        [SerializeField] bool endWithFoamOnly = true;

        [Header("Mist")]
        [SerializeField] float mistEmissionRate = 50f;
        [SerializeField] float mistStartSpeed = 3.5f;
        [SerializeField] float mistStartSize = 0.012f;
        [SerializeField] float mistLifetime = 0.4f;
        [SerializeField] float mistNoiseStrength = 0.45f;

        [Header("Direction")]
        [SerializeField] bool useDirectionSmoothing;
        [SerializeField] float sprayDirectionSmoothing = 16f;
        [SerializeField] float sprayDirectionLag = 0.03f;
        [SerializeField] bool inheritBottleVelocity = true;
        [SerializeField] float inheritedVelocityMultiplier = 0.08f;
        [SerializeField] float gravityModifier = 0.8f;

        [Header("Duration")]
        [SerializeField] float minimumSprayDuration = 1.5f;
        [SerializeField] float maximumSprayDuration = 5f;
        [SerializeField] float initialBurstDuration = 1.2f;
        [SerializeField] float sprayDecayDuration = 3f;
        [SerializeField] float endFoamDuration = 0.8f;
        [SerializeField] AnimationCurve sprayStrengthCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
        [SerializeField] AnimationCurve emissionCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
        [SerializeField] AnimationCurve speedCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0.25f);
        [SerializeField] AnimationCurve audioVolumeCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

        [Header("Collision")]
        [SerializeField] bool enableLiquidCollision;
        [SerializeField] ParticleSystemCollisionQuality liquidCollisionQuality = ParticleSystemCollisionQuality.Low;
        [SerializeField] LayerMask liquidCollisionLayerMask = ~0;

        [Header("Audio")]
        [SerializeField] float sprayVolumeMin = 0.15f;
        [SerializeField] float sprayVolumeMax = 0.75f;
        [SerializeField] float sprayPitchMin = 0.85f;
        [SerializeField] float sprayPitchMax = 1.15f;
        [SerializeField] float sprayFadeOutDuration = 0.35f;

        [Header("Debug")]
        [SerializeField] bool debugLogs;
        [SerializeField] float currentSprayStrength;
        [SerializeField] float elapsedSprayTime;

        float totalSprayDuration;
        float popPressure;
        bool isSpraying;
        bool warningLogged;
        Quaternion smoothedRotation;

        public bool IsSpraying => isSpraying;
        public float CurrentSprayStrength => currentSprayStrength;

        void Awake()
        {
            smoothedRotation = transform.rotation;
            ConfigureParticleSystem(liquidJetParticle, true);
            ConfigureParticleSystem(foamParticle, false);
            ConfigureParticleSystem(mistParticle, false);
            StopAndClear();
        }

        void Update()
        {
            if (!isSpraying)
                return;

            elapsedSprayTime += Time.deltaTime;
            var normalizedTime = totalSprayDuration > 0f ? Mathf.Clamp01(elapsedSprayTime / totalSprayDuration) : 1f;
            currentSprayStrength = Mathf.Clamp01(popPressure * sprayStrengthCurve.Evaluate(normalizedTime));

            if (useDirectionSmoothing)
                ApplyDirectionSmoothing();

            ApplySprayValues(normalizedTime, currentSprayStrength);

            if (elapsedSprayTime >= totalSprayDuration)
                FinishSpray();
        }

        public void StartSpray(float normalizedPressure)
        {
            if (isSpraying)
                return;

            if (!HasAnyParticleSystem())
            {
                LogMissingParticleWarning();
                return;
            }

            popPressure = Mathf.Clamp01(normalizedPressure);
            totalSprayDuration = Mathf.Lerp(minimumSprayDuration, maximumSprayDuration, popPressure);
            elapsedSprayTime = 0f;
            currentSprayStrength = popPressure;
            isSpraying = true;

            ConfigureParticleSystem(liquidJetParticle, true);
            ConfigureParticleSystem(foamParticle, false);
            ConfigureParticleSystem(mistParticle, false);

            PlayIfAssigned(liquidJetParticle);
            PlayIfAssigned(foamParticle);
            PlayIfAssigned(mistParticle);

            if (initialFoamBurstEnabled && foamParticle != null && foamBurstCount > 0)
                foamParticle.Emit(Mathf.RoundToInt(foamBurstCount * Mathf.Max(0.25f, popPressure)));

            if (sprayAudioSource != null && sprayAudioSource.clip != null)
            {
                sprayAudioSource.loop = true;
                sprayAudioSource.volume = Mathf.Lerp(sprayVolumeMin, sprayVolumeMax, popPressure);
                sprayAudioSource.pitch = Mathf.Lerp(sprayPitchMin, sprayPitchMax, popPressure);
                sprayAudioSource.Play();
            }

            if (debugLogs)
                Debug.Log($"[ChampagneSpray] start pressure={popPressure} duration={totalSprayDuration}", this);
        }

        void ApplySprayValues(float normalizedTime, float strength)
        {
            var emissionScale = Mathf.Clamp01(strength * emissionCurve.Evaluate(normalizedTime));
            var speedScale = Mathf.Clamp01(speedCurve.Evaluate(normalizedTime));
            var foamOnly = endWithFoamOnly && totalSprayDuration - elapsedSprayTime <= endFoamDuration;
            var burstScale = elapsedSprayTime <= initialBurstDuration ? 1.15f : 1f;
            var decayStart = Mathf.Max(initialBurstDuration, totalSprayDuration - sprayDecayDuration);
            var manualDecay = elapsedSprayTime <= decayStart
                ? 1f
                : Mathf.InverseLerp(totalSprayDuration, decayStart, elapsedSprayTime);

            SetEmission(liquidJetParticle, foamOnly ? 0f : Mathf.Lerp(liquidJetEmissionMin, liquidJetEmissionMax, emissionScale) * burstScale * manualDecay);
            SetEmission(foamParticle, foamEmissionRate * Mathf.Max(0.15f, emissionScale) * manualDecay);
            SetEmission(mistParticle, mistEmissionRate * emissionScale * manualDecay);

            SetSpeed(
                liquidJetParticle,
                liquidJetStartSpeedMin * speedScale * (1f - sprayVelocityRandomness),
                liquidJetStartSpeedMax * Mathf.Max(0.25f, speedScale) * (1f + sprayVelocityRandomness));
            SetSpeed(foamParticle, foamStartSpeed * Mathf.Max(0.2f, speedScale), foamStartSpeed * Mathf.Max(0.35f, speedScale));
            SetSpeed(mistParticle, mistStartSpeed * Mathf.Max(0.2f, speedScale), mistStartSpeed * Mathf.Max(0.5f, speedScale));

            if (sprayAudioSource != null && sprayAudioSource.isPlaying)
            {
                var fadeOutScale = sprayFadeOutDuration <= 0f
                    ? 1f
                    : Mathf.Clamp01((totalSprayDuration - elapsedSprayTime) / sprayFadeOutDuration);
                sprayAudioSource.volume = Mathf.Lerp(sprayVolumeMin, sprayVolumeMax, popPressure) *
                                          Mathf.Clamp01(audioVolumeCurve.Evaluate(normalizedTime)) *
                                          fadeOutScale;
            }
        }

        void ApplyDirectionSmoothing()
        {
            var laggedDelta = Mathf.Max(0f, Time.deltaTime - sprayDirectionLag);
            var t = sprayDirectionSmoothing <= 0f ? 1f : 1f - Mathf.Exp(-sprayDirectionSmoothing * Mathf.Max(Time.deltaTime, laggedDelta));
            smoothedRotation = Quaternion.Slerp(smoothedRotation, transform.rotation, t);
            transform.rotation = smoothedRotation;
        }

        void FinishSpray()
        {
            isSpraying = false;
            currentSprayStrength = 0f;
            SetEmission(liquidJetParticle, 0f);
            SetEmission(foamParticle, 0f);
            SetEmission(mistParticle, 0f);

            if (sprayAudioSource != null)
                sprayAudioSource.Stop();

            if (debugLogs)
                Debug.Log("[ChampagneSpray] complete", this);
        }

        public void StopAndClear()
        {
            isSpraying = false;
            elapsedSprayTime = 0f;
            currentSprayStrength = 0f;
            StopAndClear(liquidJetParticle);
            StopAndClear(foamParticle);
            StopAndClear(mistParticle);

            if (sprayAudioSource != null)
                sprayAudioSource.Stop();
        }

        void ConfigureParticleSystem(ParticleSystem system, bool liquid)
        {
            if (system == null)
                return;

            var main = system.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = liquid ? liquidJetGravityModifier : gravityModifier;
            main.maxParticles = liquid ? 280 : 160;

            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = liquid ? liquidJetConeAngle : liquidJetConeAngle * 1.8f;

            if (liquid)
            {
                main.startLifetime = new ParticleSystem.MinMaxCurve(liquidJetLifetimeMin, liquidJetLifetimeMax);
                main.startSize = new ParticleSystem.MinMaxCurve(liquidJetStartSizeMin, liquidJetStartSizeMax);
                SetSpeed(
                    system,
                    liquidJetStartSpeedMin * (1f - sprayVelocityRandomness),
                    liquidJetStartSpeedMax * (1f + sprayVelocityRandomness));
            }
            else if (system == foamParticle)
            {
                main.startLifetime = foamLifetime;
                main.startSize = foamStartSize;
                SetSpeed(system, foamStartSpeed, foamStartSpeed);
            }
            else
            {
                main.startLifetime = mistLifetime;
                main.startSize = mistStartSize;
                SetSpeed(system, mistStartSpeed, mistStartSpeed);
            }

            var noise = system.noise;
            noise.enabled = liquid ? liquidJetNoiseStrength > 0f : mistNoiseStrength > 0f;
            noise.strength = liquid ? liquidJetNoiseStrength : mistNoiseStrength;

            var inheritVelocityModule = system.inheritVelocity;
            inheritVelocityModule.enabled = inheritBottleVelocity;
            inheritVelocityModule.mode = ParticleSystemInheritVelocityMode.Initial;
            inheritVelocityModule.curveMultiplier = inheritedVelocityMultiplier;

            var collision = system.collision;
            collision.enabled = liquid && enableLiquidCollision;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = liquidCollisionQuality;
            collision.bounce = 0f;
            collision.lifetimeLoss = 0.75f;
            collision.collidesWith = liquidCollisionLayerMask;
            collision.sendCollisionMessages = false;
        }

        static void SetEmission(ParticleSystem system, float rate)
        {
            if (system == null)
                return;

            var emission = system.emission;
            emission.enabled = rate > 0f;
            emission.rateOverTime = Mathf.Max(0f, rate);
        }

        static void SetSpeed(ParticleSystem system, float min, float max)
        {
            if (system == null)
                return;

            var main = system.main;
            main.startSpeed = Mathf.Approximately(min, max)
                ? new ParticleSystem.MinMaxCurve(min)
                : new ParticleSystem.MinMaxCurve(min, max);
        }

        static void PlayIfAssigned(ParticleSystem system)
        {
            if (system != null && !system.isPlaying)
                system.Play();
        }

        static void StopAndClear(ParticleSystem system)
        {
            if (system == null)
                return;

            system.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        bool HasAnyParticleSystem()
        {
            return liquidJetParticle != null || foamParticle != null || mistParticle != null;
        }

        void LogMissingParticleWarning()
        {
            if (warningLogged)
                return;

            warningLogged = true;
            Debug.LogWarning("[ChampagneSpray] No particle systems are assigned.", this);
        }
    }
}
