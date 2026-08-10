using System;
using F1XR.RestAPI.Api;
using UnityEngine;
using UnityEngine.Rendering;

namespace F1XR.RestAPI.Replay
{
    [Serializable]
    public sealed class CollisionShowcaseVfxSettings
    {
        public bool enabled = true;

        [Header("Playback")]
        [Min(0f)] public float leadSeconds = 5f;
        [Min(0f)] public float tailSeconds = 10f;
        [Range(0.1f, 1f)] public float slowMotionSpeed = 0.32f;
        [Min(0f)] public float slowMotionLeadSeconds = 0.35f;
        [Min(0f)] public float slowMotionTailSeconds = 0.7f;
        [Min(0.01f)] public float slowMotionBlendSeconds = 0.18f;

        [Header("Contact Sparks")]
        [Range(1, 16)] public int sparkBurstPerCar = 16;
        [Range(0.05f, 0.8f)] public float sparkLifetime = 0.42f;
        [Range(0.005f, 0.12f)]
        public float sparkSizeInCarWidths = 0.05f;
        [Range(0.1f, 4f)]
        public float sparkSpeedInCarLengthsPerSecond = 2.6f;
        public Color sparkColor =
            new(1.45f, 0.55f, 0.08f, 0.96f);

        [Header("Impact Pulse")]
        [Min(0.05f)] public float pulseDuration = 0.72f;
        [Min(0.01f)] public float pulseStartInRoadWidths = 0.55f;
        [Min(0.01f)] public float pulseEndInRoadWidths = 5.5f;
        [Range(0.05f, 0.95f)] public float pulseInnerRatio = 0.62f;
        public Color pulseColor =
            new(1.4f, 0.22f, 0.035f, 0.92f);

        [Header("Carbon Debris")]
        [Range(0, 16)] public int debrisCount = 10;
        [Min(0.05f)] public float debrisLifetime = 1.15f;
        [Min(0f)] public float debrisHorizontalSpeedInRoadWidths = 3.8f;
        [Min(0f)] public float debrisVerticalSpeedInRoadWidths = 3.1f;
        [Min(0f)] public float debrisGravityInRoadWidths = 8.5f;
        [Min(0.001f)] public float debrisSizeInRoadWidths = 0.11f;
        public Color debrisColor =
            new(0.045f, 0.05f, 0.06f, 1f);

        [Header("Impact Audio")]
        public bool playImpactAudio = true;
        [Range(0f, 1f)] public float impactVolume = 0.72f;
        [Range(0f, 1f)] public float impactSpatialBlend = 0.92f;
        [Min(0.05f)] public float impactMinDistance = 0.12f;
        [Min(0.1f)] public float impactMaxDistance = 4f;

        [Header("Reset")]
        [Min(0.05f)] public float seekResetThresholdSeconds = 0.5f;
    }

    public sealed partial class EventPopoutReplay
    {
        private const int CollisionPulseSegments = 64;

        [Header("Collision Showcase")]
        public CollisionShowcaseVfxSettings collisionShowcase = new();

        private Transform collisionVfxRoot;
        private MeshRenderer collisionPulseRenderer;
        private Mesh collisionPulseMesh;
        private Vector3[] collisionPulseVertices;
        private Material collisionPulseMaterial;
        private Mesh collisionDebrisMesh;
        private Material collisionDebrisMaterial;
        private Transform[] collisionDebris;
        private Vector3[] collisionDebrisVelocities;
        private Vector3[] collisionDebrisSpins;
        private AudioSource collisionAudio;
        private AudioClip collisionImpactClip;
        private OvertakeSideBySideVfxSettings collisionSparkSettings;
        private float lastCollisionVfxReplayTime = float.NaN;

        public bool HasCollision =>
            FindClosestCollision(
                player != null ? player.Events : null,
                player != null ? player.CurrentTime : 0f,
                player != null
                    ? player.TimelineStartTime
                    : float.NegativeInfinity,
                player != null
                    ? player.ReadyUntilTime
                    : float.PositiveInfinity) != null;

        public bool HasNextCollision =>
            TryFindNextCollision(out _);

        public bool IsCurrentCollision =>
            IsCollisionEvent(currentEvent);

        private float CollisionLeadSeconds =>
            collisionShowcase != null
                ? collisionShowcase.leadSeconds
                : eventLeadSeconds;

        private float CollisionTailSeconds =>
            collisionShowcase != null
                ? collisionShowcase.tailSeconds
                : eventTailSeconds;

        public void OpenTestCollision()
        {
            if (player == null || !player.HasDataset)
            {
                Debug.LogWarning(
                    "[EventReplay] Cannot open a collision before the replay dataset is ready.",
                    this);
                return;
            }

            ReplayEventDto definition = FindClosestCollision(
                player.Events,
                player.CurrentTime,
                player.TimelineStartTime,
                player.ReadyUntilTime);
            if (definition == null)
            {
                definition = FindClosestCollision(
                    ReplayEventFixtures.Load(player.Manifest),
                    player.CurrentTime,
                    player.TimelineStartTime,
                    player.ReadyUntilTime);
            }

            if (definition == null)
            {
                Debug.LogWarning(
                    "[EventReplay] No collision event is available for this session.",
                    this);
                return;
            }

            Open(definition);
        }

        public void OpenNextCollision()
        {
            if (isLoading ||
                player == null ||
                !player.HasDataset)
            {
                return;
            }

            if (TryFindNextCollision(
                    out ReplayEventDto definition))
            {
                Open(definition);
            }
        }

        private void EnsureCollisionShowcase()
        {
            if (!isActive ||
                !IsCollisionEvent(currentEvent) ||
                collisionVfxRoot != null)
            {
                return;
            }

            collisionShowcase ??=
                new CollisionShowcaseVfxSettings();
            if (!collisionShowcase.enabled ||
                PresentationRoot == null ||
                eventCars == null)
            {
                return;
            }

            Vector3 contactPosition =
                ResolveCollisionContactPosition();
            GameObject root =
                new("CollisionShowcaseVfx");
            root.transform.SetParent(
                PresentationRoot,
                false);
            root.transform.localPosition =
                contactPosition +
                Vector3.up * roadWidth * 0.08f;
            root.transform.localRotation =
                Quaternion.identity;
            collisionVfxRoot = root.transform;

            CreateCollisionPulse();
            CreateCollisionDebris();
            CreateCollisionAudio();
            collisionSparkSettings =
                CreateCollisionSparkSettings();
            ResetCollisionShowcasePlayback(
                timeline.CurrentTime);
        }

        private Vector3 ResolveCollisionContactPosition()
        {
            int[] drivers = currentEvent != null
                ? currentEvent.driverNumbers
                : null;
            if (drivers != null &&
                drivers.Length >= 2 &&
                TryGetEventLocalVehiclePosition(
                    drivers[0],
                    currentEvent.anchorTime,
                    out Vector3 first) &&
                TryGetEventLocalVehiclePosition(
                    drivers[1],
                    currentEvent.anchorTime,
                    out Vector3 second))
            {
                return (first + second) * 0.5f;
            }

            return TryGetEventLocalPathPosition(
                    currentEvent.anchorTime,
                    out Vector3 pathPosition)
                ? pathPosition
                : Vector3.zero;
        }

        private void CreateCollisionPulse()
        {
            GameObject pulse =
                new("ImpactPulse");
            pulse.transform.SetParent(
                collisionVfxRoot,
                false);
            MeshFilter filter =
                pulse.AddComponent<MeshFilter>();
            collisionPulseMesh =
                ReplayCarVisualUtil.CreateRingMesh(
                    "Runtime_CollisionImpactPulse",
                    CollisionPulseSegments,
                    out collisionPulseVertices);
            filter.sharedMesh = collisionPulseMesh;
            collisionPulseMaterial =
                ReplayCarVisualUtil.CreateSelectionMaterial(
                    collisionShowcase.pulseColor);
            collisionPulseMaterial.name =
                "Runtime_CollisionImpactPulse";
            collisionPulseRenderer =
                pulse.AddComponent<MeshRenderer>();
            collisionPulseRenderer.sharedMaterial =
                collisionPulseMaterial;
            collisionPulseRenderer.shadowCastingMode =
                ShadowCastingMode.Off;
            collisionPulseRenderer.receiveShadows = false;
            collisionPulseRenderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            collisionPulseRenderer.lightProbeUsage =
                LightProbeUsage.Off;
            collisionPulseRenderer.reflectionProbeUsage =
                ReflectionProbeUsage.Off;
            collisionPulseRenderer.enabled = false;
        }

        private void CreateCollisionDebris()
        {
            int count = Mathf.Clamp(
                collisionShowcase.debrisCount,
                0,
                16);
            collisionDebris = new Transform[count];
            collisionDebrisVelocities =
                new Vector3[count];
            collisionDebrisSpins =
                new Vector3[count];
            if (count == 0)
                return;

            collisionDebrisMesh =
                CreateCollisionDebrisMesh();
            collisionDebrisMaterial =
                ReplayCarVisualUtil.CreateUnlitMaterial(
                    collisionShowcase.debrisColor);
            collisionDebrisMaterial.name =
                "Runtime_CollisionCarbonDebris";

            for (int i = 0; i < count; i++)
            {
                GameObject shard =
                    new($"CarbonShard_{i:00}");
                shard.transform.SetParent(
                    collisionVfxRoot,
                    false);
                shard.AddComponent<MeshFilter>()
                    .sharedMesh = collisionDebrisMesh;
                MeshRenderer renderer =
                    shard.AddComponent<MeshRenderer>();
                renderer.sharedMaterial =
                    collisionDebrisMaterial;
                renderer.shadowCastingMode =
                    ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.motionVectorGenerationMode =
                    MotionVectorGenerationMode.ForceNoMotion;
                renderer.lightProbeUsage =
                    LightProbeUsage.Off;
                renderer.reflectionProbeUsage =
                    ReflectionProbeUsage.Off;

                float sizeVariation =
                    0.72f + (i % 5) * 0.11f;
                shard.transform.localScale =
                    Vector3.one *
                    roadWidth *
                    collisionShowcase
                        .debrisSizeInRoadWidths *
                    sizeVariation;
                collisionDebris[i] = shard.transform;

                float angle =
                    (i * 137.508f + 19f) *
                    Mathf.Deg2Rad;
                float speedVariation =
                    0.72f + (i % 4) * 0.13f;
                float horizontalSpeed =
                    roadWidth *
                    collisionShowcase
                        .debrisHorizontalSpeedInRoadWidths *
                    speedVariation;
                collisionDebrisVelocities[i] =
                    new Vector3(
                        Mathf.Cos(angle) * horizontalSpeed,
                        roadWidth *
                        collisionShowcase
                            .debrisVerticalSpeedInRoadWidths *
                        (0.78f + (i % 3) * 0.16f),
                        Mathf.Sin(angle) * horizontalSpeed);
                collisionDebrisSpins[i] =
                    new Vector3(
                        210f + i * 17f,
                        -260f + i * 29f,
                        145f + i * 23f);
                shard.SetActive(false);
            }
        }

        private void CreateCollisionAudio()
        {
            collisionAudio =
                collisionVfxRoot.gameObject
                    .AddComponent<AudioSource>();
            collisionAudio.playOnAwake = false;
            collisionAudio.loop = false;
            collisionAudio.spatialBlend =
                collisionShowcase.impactSpatialBlend;
            collisionAudio.volume =
                collisionShowcase.impactVolume;
            collisionAudio.dopplerLevel = 0f;
            collisionAudio.rolloffMode =
                AudioRolloffMode.Linear;
            collisionAudio.minDistance =
                collisionShowcase.impactMinDistance;
            collisionAudio.maxDistance = Mathf.Max(
                collisionAudio.minDistance + 0.01f,
                collisionShowcase.impactMaxDistance);
            collisionImpactClip =
                CreateCollisionImpactClip();
            collisionAudio.clip = collisionImpactClip;
        }

        private void UpdateCollisionShowcase(float replayTime)
        {
            if (collisionVfxRoot == null ||
                !IsCollisionEvent(currentEvent))
            {
                return;
            }

            bool hasPrevious =
                !float.IsNaN(lastCollisionVfxReplayTime);
            float resetThreshold = Mathf.Max(
                0.05f,
                collisionShowcase
                    .seekResetThresholdSeconds);
            bool discontinuity = hasPrevious &&
                (replayTime < lastCollisionVfxReplayTime ||
                 replayTime - lastCollisionVfxReplayTime >
                 resetThreshold);
            if (!hasPrevious || discontinuity)
            {
                ResetCollisionShowcasePlayback(replayTime);
            }
            else if (
                lastCollisionVfxReplayTime <
                    currentEvent.anchorTime &&
                replayTime >= currentEvent.anchorTime)
            {
                TriggerCollisionImpact();
            }

            UpdateCollisionPulse(replayTime);
            UpdateCollisionDebris(replayTime);
            lastCollisionVfxReplayTime = replayTime;
        }

        private void TriggerCollisionImpact()
        {
            int[] drivers = currentEvent.driverNumbers;
            if (drivers != null && drivers.Length >= 2)
            {
                eventCars.TriggerCollisionContactVfx(
                    drivers[0],
                    drivers[1],
                    collisionSparkSettings);
            }

            if (collisionShowcase.playImpactAudio &&
                collisionAudio != null &&
                collisionImpactClip != null)
            {
                collisionAudio.Stop();
                collisionAudio.Play();
            }
        }

        private void UpdateCollisionPulse(float replayTime)
        {
            if (collisionPulseRenderer == null)
                return;

            float age = replayTime -
                currentEvent.anchorTime;
            float duration = Mathf.Max(
                0.05f,
                collisionShowcase.pulseDuration);
            bool visible = age >= 0f && age <= duration;
            collisionPulseRenderer.enabled = visible;
            if (!visible)
                return;

            float progress = Mathf.Clamp01(age / duration);
            float eased =
                1f - Mathf.Pow(1f - progress, 3f);
            float localRadius = roadWidth * Mathf.Lerp(
                collisionShowcase
                    .pulseStartInRoadWidths,
                collisionShowcase
                    .pulseEndInRoadWidths,
                eased);
            Vector3 scale =
                collisionPulseRenderer
                    .transform.lossyScale;
            float worldRadius = localRadius * Mathf.Max(
                Mathf.Abs(scale.x),
                Mathf.Abs(scale.z));
            ReplayCarVisualUtil.UpdateRingMesh(
                collisionPulseRenderer.transform,
                collisionPulseMesh,
                collisionPulseVertices,
                CollisionPulseSegments,
                Vector3.zero,
                worldRadius,
                collisionShowcase.pulseInnerRatio,
                progress * 35f);
            Color color =
                collisionShowcase.pulseColor;
            color.a *= 1f - progress;
            ReplayCarVisualUtil.SetMaterialColor(
                collisionPulseMaterial,
                color);
        }

        private void UpdateCollisionDebris(float replayTime)
        {
            if (collisionDebris == null)
                return;

            float age = replayTime -
                currentEvent.anchorTime;
            float duration = Mathf.Max(
                0.05f,
                collisionShowcase.debrisLifetime);
            bool visible = age >= 0f && age <= duration;
            float gravity =
                roadWidth *
                collisionShowcase
                    .debrisGravityInRoadWidths;
            for (int i = 0;
                 i < collisionDebris.Length;
                 i++)
            {
                Transform shard = collisionDebris[i];
                if (shard == null)
                    continue;

                shard.gameObject.SetActive(visible);
                if (!visible)
                    continue;

                shard.localPosition =
                    collisionDebrisVelocities[i] * age +
                    Vector3.down *
                    (0.5f * gravity * age * age);
                shard.localRotation =
                    Quaternion.Euler(
                        collisionDebrisSpins[i] * age);
            }
        }

        private void ResetCollisionShowcasePlayback(
            float replayTime)
        {
            lastCollisionVfxReplayTime = replayTime;
            if (collisionAudio != null)
                collisionAudio.Stop();

            int[] drivers = currentEvent != null
                ? currentEvent.driverNumbers
                : null;
            if (eventCars != null &&
                drivers != null &&
                drivers.Length >= 2)
            {
                eventCars.ResetCollisionContactVfx(
                    drivers[0],
                    drivers[1]);
            }

            UpdateCollisionPulse(replayTime);
            UpdateCollisionDebris(replayTime);
        }

        private float ResolveCollisionPlaybackSpeedMultiplier(
            float replayTime)
        {
            if (!IsCollisionEvent(currentEvent) ||
                collisionShowcase == null ||
                !collisionShowcase.enabled)
            {
                return 1f;
            }

            float blend = Mathf.Max(
                0.01f,
                collisionShowcase
                    .slowMotionBlendSeconds);
            float start = currentEvent.anchorTime -
                Mathf.Max(
                    0f,
                    collisionShowcase
                        .slowMotionLeadSeconds);
            float end = currentEvent.anchorTime +
                Mathf.Max(
                    0f,
                    collisionShowcase
                        .slowMotionTailSeconds);
            float enter = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    start - blend,
                    start,
                    replayTime));
            float exit = 1f - Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    end,
                    end + blend,
                    replayTime));
            float weight = Mathf.Min(enter, exit);
            return Mathf.Lerp(
                1f,
                Mathf.Clamp(
                    collisionShowcase
                        .slowMotionSpeed,
                    0.1f,
                    1f),
                weight);
        }

        private void DestroyCollisionShowcase()
        {
            if (collisionAudio != null)
                collisionAudio.Stop();

            int[] drivers = currentEvent != null
                ? currentEvent.driverNumbers
                : null;
            if (eventCars != null &&
                drivers != null &&
                drivers.Length >= 2)
            {
                eventCars.ResetCollisionContactVfx(
                    drivers[0],
                    drivers[1]);
            }

            if (collisionVfxRoot != null)
                Destroy(collisionVfxRoot.gameObject);
            if (collisionPulseMesh != null)
                Destroy(collisionPulseMesh);
            if (collisionPulseMaterial != null)
                Destroy(collisionPulseMaterial);
            if (collisionDebrisMesh != null)
                Destroy(collisionDebrisMesh);
            if (collisionDebrisMaterial != null)
                Destroy(collisionDebrisMaterial);
            if (collisionImpactClip != null)
                Destroy(collisionImpactClip);

            collisionVfxRoot = null;
            collisionPulseRenderer = null;
            collisionPulseMesh = null;
            collisionPulseVertices = null;
            collisionPulseMaterial = null;
            collisionDebrisMesh = null;
            collisionDebrisMaterial = null;
            collisionDebris = null;
            collisionDebrisVelocities = null;
            collisionDebrisSpins = null;
            collisionAudio = null;
            collisionImpactClip = null;
            collisionSparkSettings = null;
            lastCollisionVfxReplayTime = float.NaN;
        }

        private OvertakeSideBySideVfxSettings
            CreateCollisionSparkSettings()
        {
            return new OvertakeSideBySideVfxSettings
            {
                enabled = true,
                sparkBurstCount = Mathf.Clamp(
                    collisionShowcase
                        .sparkBurstPerCar,
                    1,
                    16),
                sparkLifetime =
                    collisionShowcase.sparkLifetime,
                sparkSizeInCarWidths =
                    collisionShowcase
                        .sparkSizeInCarWidths,
                sparkSpeedInCarLengthsPerSecond =
                    collisionShowcase
                        .sparkSpeedInCarLengthsPerSecond,
                sparkEmissionColor =
                    collisionShowcase.sparkColor,
                sparkRearOffsetInCarLengths = 0.08f,
                sparkFloorOffsetInCarHeights = 0.12f
            };
        }

        private static Mesh CreateCollisionDebrisMesh()
        {
            Mesh mesh = new()
            {
                name = "Runtime_CollisionCarbonShard",
                vertices = new[]
                {
                    new Vector3(-0.55f, -0.12f, -0.3f),
                    new Vector3(0.62f, -0.08f, -0.2f),
                    new Vector3(-0.12f, 0.18f, 0.72f),
                    new Vector3(0.08f, 0.26f, -0.05f)
                },
                triangles = new[]
                {
                    0, 1, 2,
                    0, 3, 1,
                    1, 3, 2,
                    2, 3, 0
                }
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static AudioClip CreateCollisionImpactClip()
        {
            const int sampleRate = 24000;
            const float duration = 0.52f;
            int sampleCount =
                Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[sampleCount];
            var random = new System.Random(19780407);
            for (int i = 0; i < sampleCount; i++)
            {
                float time = i / (float)sampleRate;
                float noiseEnvelope =
                    Mathf.Exp(-time * 17f);
                float metalEnvelope =
                    Mathf.Exp(-time * 8.5f);
                float thumpEnvelope =
                    Mathf.Exp(-time * 12f);
                float noise =
                    (float)(random.NextDouble() * 2.0 - 1.0) *
                    noiseEnvelope * 0.62f;
                float thump = Mathf.Sin(
                    Mathf.PI * 2f *
                    (92f - time * 48f) * time) *
                    thumpEnvelope * 0.72f;
                float metal =
                    (Mathf.Sin(
                         Mathf.PI * 2f * 760f * time) *
                     0.24f +
                     Mathf.Sin(
                         Mathf.PI * 2f * 1280f * time) *
                     0.13f) *
                    metalEnvelope;
                samples[i] = Mathf.Clamp(
                    noise + thump + metal,
                    -1f,
                    1f);
            }

            AudioClip clip = AudioClip.Create(
                "Runtime_CollisionImpact",
                sampleCount,
                1,
                sampleRate,
                false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static bool IsCollisionEvent(
            ReplayEventDto replayEvent)
        {
            return replayEvent != null &&
                (string.Equals(
                     replayEvent.eventType,
                     "Collision",
                     StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(
                     replayEvent.eventType,
                     "Contact",
                     StringComparison.OrdinalIgnoreCase));
        }

        private static ReplayEventDto FindClosestCollision(
            ReplayEventDto[] events,
            float time,
            float minimumAnchorTime =
                float.NegativeInfinity,
            float maximumAnchorTime =
                float.PositiveInfinity)
        {
            if (events == null)
                return null;

            ReplayEventDto closest = null;
            float closestDistance =
                float.PositiveInfinity;
            for (int i = 0; i < events.Length; i++)
            {
                ReplayEventDto candidate = events[i];
                if (!IsCollisionEvent(candidate) ||
                    candidate.anchorTime < minimumAnchorTime ||
                    candidate.anchorTime > maximumAnchorTime)
                    continue;

                float distance = Mathf.Abs(
                    candidate.anchorTime - time);
                if (distance < closestDistance ||
                    Mathf.Approximately(
                        distance,
                        closestDistance) &&
                    string.CompareOrdinal(
                        candidate.eventId,
                        closest?.eventId) < 0)
                {
                    closest = candidate;
                    closestDistance = distance;
                }
            }

            return closest;
        }

        private bool TryFindNextCollision(
            out ReplayEventDto next)
        {
            next = null;
            ReplayEventDto[] events =
                player != null ? player.Events : null;
            if (events == null || events.Length == 0)
                return false;

            float currentAnchor = currentEvent != null
                ? currentEvent.anchorTime
                : player.CurrentTime;
            string currentId = currentEvent != null
                ? currentEvent.eventId
                : string.Empty;
            for (int i = 0; i < events.Length; i++)
            {
                ReplayEventDto candidate = events[i];
                if (!IsCollisionEvent(candidate) ||
                    candidate.anchorTime <
                        player.TimelineStartTime ||
                    candidate.anchorTime >
                        player.ReadyUntilTime)
                    continue;

                bool followsCurrent =
                    candidate.anchorTime >
                        currentAnchor + 0.0001f ||
                    Mathf.Approximately(
                        candidate.anchorTime,
                        currentAnchor) &&
                    string.CompareOrdinal(
                        candidate.eventId,
                        currentId) > 0;
                if (!followsCurrent)
                    continue;

                if (next == null ||
                    candidate.anchorTime < next.anchorTime ||
                    Mathf.Approximately(
                        candidate.anchorTime,
                        next.anchorTime) &&
                    string.CompareOrdinal(
                        candidate.eventId,
                        next.eventId) < 0)
                {
                    next = candidate;
                }
            }

            return next != null;
        }
    }
}
