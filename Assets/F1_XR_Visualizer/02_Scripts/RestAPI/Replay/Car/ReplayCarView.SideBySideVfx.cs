using UnityEngine;
using UnityEngine.Rendering;
using static F1XR.RestAPI.Replay.ReplayCarVisualUtil;

namespace F1XR.RestAPI.Replay
{
    public partial class ReplayCarView
    {
        private const int MaximumSideBySideSparkCount = 16;

        private Transform sideBySideVfxRoot;
        private Transform sideBySideSweepRoot;
        private MeshRenderer sideBySideSweepRenderer;
        private Mesh sideBySideSweepMesh;
        private Material sideBySideSweepMaterial;
        private ParticleSystem sideBySideSparks;
        private ParticleSystemRenderer sideBySideSparkRenderer;
        private Material sideBySideSparkMaterial;
        private bool sideBySideSweepActive;
        private float sideBySideSweepElapsed;
        private float sideBySideSweepLastReplayTime = float.NaN;

        public void PrepareOvertakeSideBySideVfx(
            OvertakeApproachRibbonSettings ribbonSettings,
            OvertakeSideBySideVfxSettings settings,
            bool overtaker)
        {
            if (!overtaker || settings == null || !settings.enabled)
                return;

            if (ribbonSettings != null)
            {
                EnsureOvertakeRibbon(ribbonSettings, true);
                ResolveOvertakeRibbonLayout(ribbonSettings);
            }

            EnsureSideBySideVfxRoot();
            EnsureSideBySideSweep(settings);
            EnsureSideBySideSparks(settings);
        }

        public void TriggerOvertakeLightSweep(
            OvertakeSideBySideVfxSettings settings,
            float replayTime)
        {
            if (settings == null || !settings.enabled)
                return;

            EnsureSideBySideVfxRoot();
            EnsureSideBySideSweep(settings);
            if (sideBySideSweepRenderer == null)
                return;

            sideBySideSweepActive = true;
            sideBySideSweepElapsed = 0f;
            sideBySideSweepLastReplayTime = replayTime;
            sideBySideSweepRenderer.enabled = true;
            ApplySideBySideSweep(settings, 0f);
        }

        public void UpdateOvertakeLightSweep(
            OvertakeSideBySideVfxSettings settings,
            float replayTime)
        {
            if (!sideBySideSweepActive ||
                sideBySideSweepRenderer == null ||
                settings == null)
            {
                return;
            }

            float replayDelta = float.IsNaN(
                    sideBySideSweepLastReplayTime)
                ? 0f
                : replayTime - sideBySideSweepLastReplayTime;
            if (replayDelta < 0f ||
                replayDelta >
                settings.seekResetThresholdSeconds)
            {
                StopSideBySideSweep();
                return;
            }

            if (replayDelta > 0.00001f)
            {
                sideBySideSweepElapsed +=
                    Mathf.Min(Time.deltaTime, 0.05f);
            }

            sideBySideSweepLastReplayTime = replayTime;
            float progress = Mathf.Clamp01(
                sideBySideSweepElapsed /
                Mathf.Max(0.01f, settings.lightSweepDuration));
            ApplySideBySideSweep(settings, progress);
            if (progress >= 1f)
                StopSideBySideSweep();
        }

        public void TriggerOvertakeUnderfloorSparks(
            OvertakeSideBySideVfxSettings settings)
        {
            if (settings == null || !settings.enabled)
                return;

            EnsureSideBySideVfxRoot();
            EnsureSideBySideSparks(settings);
            if (sideBySideSparks == null ||
                !overtakeVfxHasWorldBounds)
            {
                return;
            }

            sideBySideSparks.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear);
            sideBySideSparks.Play(false);
            SetMaterialColor(
                sideBySideSparkMaterial,
                settings.sparkEmissionColor);

            int count = Mathf.Clamp(
                settings.sparkBurstCount,
                1,
                MaximumSideBySideSparkCount);
            float speed =
                overtakeRibbonWorldLength *
                settings.sparkSpeedInCarLengthsPerSecond;
            float size =
                overtakeRibbonWorldWidth *
                settings.sparkSizeInCarWidths;
            Vector3 origin =
                overtakeVfxWorldBounds.center -
                transform.forward *
                overtakeRibbonWorldLength *
                settings.sparkRearOffsetInCarLengths -
                transform.up *
                overtakeRibbonWorldHeight *
                (0.5f -
                 settings.sparkFloorOffsetInCarHeights);

            for (int i = 0; i < count; i++)
            {
                float spread = count <= 1
                    ? 0f
                    : (float)i / (count - 1) - 0.5f;
                float variation =
                    0.86f + (i % 3) * 0.07f;
                ParticleSystem.EmitParams spark =
                    new ParticleSystem.EmitParams
                    {
                        position =
                            origin +
                            transform.right *
                            overtakeRibbonWorldWidth *
                            spread *
                            0.28f,
                        velocity =
                            -transform.forward *
                            speed *
                            variation +
                            transform.right *
                            speed *
                            spread *
                            0.22f -
                            transform.up *
                            speed *
                            (0.06f + (i % 2) * 0.025f),
                        startLifetime =
                            settings.sparkLifetime *
                            (0.85f + (i % 4) * 0.05f),
                        startSize =
                            size *
                            (0.8f + (i % 3) * 0.1f),
                        startColor =
                            settings.sparkEmissionColor,
                        applyShapeToPosition = false
                    };
                sideBySideSparks.Emit(spark, 1);
            }
        }

        public void ResetOvertakeSideBySideVfx()
        {
            StopSideBySideSweep();
            if (sideBySideSparks != null)
            {
                sideBySideSparks.Stop(
                    true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void EnsureSideBySideVfxRoot()
        {
            if (sideBySideVfxRoot != null)
                return;

            GameObject root =
                new("OvertakeSideBySideVfx");
            root.transform.SetParent(transform, false);
            sideBySideVfxRoot = root.transform;
        }

        private void EnsureSideBySideSweep(
            OvertakeSideBySideVfxSettings settings)
        {
            if (sideBySideSweepRenderer != null)
                return;

            GameObject sweep =
                new("BodyLightSweep");
            sweep.transform.SetParent(
                sideBySideVfxRoot,
                false);
            sideBySideSweepRoot = sweep.transform;

            MeshFilter filter =
                sweep.AddComponent<MeshFilter>();
            sideBySideSweepMesh =
                CreateSideBySideSweepMesh();
            filter.sharedMesh =
                sideBySideSweepMesh;
            sideBySideSweepMaterial =
                CreateSelectionMaterial(
                    settings.lightSweepColor);
            sideBySideSweepMaterial.name =
                "Runtime_OvertakeBodyLightSweep";
            sideBySideSweepRenderer =
                sweep.AddComponent<MeshRenderer>();
            sideBySideSweepRenderer.sharedMaterial =
                sideBySideSweepMaterial;
            sideBySideSweepRenderer.shadowCastingMode =
                ShadowCastingMode.Off;
            sideBySideSweepRenderer.receiveShadows = false;
            sideBySideSweepRenderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            sideBySideSweepRenderer.lightProbeUsage =
                LightProbeUsage.Off;
            sideBySideSweepRenderer.reflectionProbeUsage =
                ReflectionProbeUsage.Off;
            sideBySideSweepRenderer.enabled = false;
        }

        private void EnsureSideBySideSparks(
            OvertakeSideBySideVfxSettings settings)
        {
            if (sideBySideSparks != null)
                return;

            GameObject sparks =
                new("UnderfloorSparks");
            sparks.transform.SetParent(
                sideBySideVfxRoot,
                false);
            sideBySideSparks =
                sparks.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main =
                sideBySideSparks.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace =
                ParticleSystemSimulationSpace.World;
            main.maxParticles =
                MaximumSideBySideSparkCount;
            main.startLifetime =
                settings.sparkLifetime;
            main.startSpeed = 0f;
            main.startSize = 0.01f;
            main.startColor =
                settings.sparkEmissionColor;
            main.gravityModifier = 0.12f;
            main.stopAction =
                ParticleSystemStopAction.None;

            ParticleSystem.EmissionModule emission =
                sideBySideSparks.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape =
                sideBySideSparks.shape;
            shape.enabled = false;
            ParticleSystem.CollisionModule collision =
                sideBySideSparks.collision;
            collision.enabled = false;
            ParticleSystem.TrailModule trails =
                sideBySideSparks.trails;
            trails.enabled = false;
            ParticleSystem.LightsModule lights =
                sideBySideSparks.lights;
            lights.enabled = false;

            sideBySideSparkRenderer =
                sparks.GetComponent<ParticleSystemRenderer>();
            sideBySideSparkMaterial =
                CreateSelectionMaterial(
                    settings.sparkEmissionColor);
            sideBySideSparkMaterial.name =
                "Runtime_OvertakeUnderfloorSparks";
            sideBySideSparkRenderer.sharedMaterial =
                sideBySideSparkMaterial;
            sideBySideSparkRenderer.renderMode =
                ParticleSystemRenderMode.Stretch;
            sideBySideSparkRenderer.velocityScale = 0.08f;
            sideBySideSparkRenderer.lengthScale = 1.8f;
            sideBySideSparkRenderer.cameraVelocityScale = 0f;
            sideBySideSparkRenderer.shadowCastingMode =
                ShadowCastingMode.Off;
            sideBySideSparkRenderer.receiveShadows = false;
            sideBySideSparkRenderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            sideBySideSparkRenderer.lightProbeUsage =
                LightProbeUsage.Off;
            sideBySideSparkRenderer.reflectionProbeUsage =
                ReflectionProbeUsage.Off;
            sideBySideSparks.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void ApplySideBySideSweep(
            OvertakeSideBySideVfxSettings settings,
            float progress)
        {
            if (sideBySideSweepRoot == null ||
                sideBySideSweepRenderer == null ||
                !overtakeVfxHasWorldBounds)
            {
                StopSideBySideSweep();
                return;
            }

            float longitudinal =
                Mathf.Lerp(0.46f, -0.46f, progress);
            Vector3 position =
                overtakeVfxWorldBounds.center +
                transform.forward *
                overtakeRibbonWorldLength *
                longitudinal +
                transform.up *
                overtakeRibbonWorldHeight *
                (0.5f +
                 settings.lightSweepTopOffsetInCarHeights);
            sideBySideSweepRoot.localPosition =
                transform.InverseTransformPoint(position);
            sideBySideSweepRoot.localRotation =
                Quaternion.identity;

            Vector3 scale = transform.lossyScale;
            float localWidth =
                overtakeRibbonWorldWidth *
                1.08f /
                Mathf.Max(0.0001f, Mathf.Abs(scale.x));
            float localLength =
                overtakeRibbonWorldLength *
                settings.lightSweepWidthInCarLengths /
                Mathf.Max(0.0001f, Mathf.Abs(scale.z));
            sideBySideSweepRoot.localScale =
                new Vector3(localWidth, 1f, localLength);

            float envelope =
                Mathf.Sin(Mathf.PI * progress);
            Color color =
                settings.lightSweepColor;
            color.r *=
                settings.lightSweepIntensity *
                envelope;
            color.g *=
                settings.lightSweepIntensity *
                envelope;
            color.b *=
                settings.lightSweepIntensity *
                envelope;
            color.a *= envelope;
            SetMaterialColor(
                sideBySideSweepMaterial,
                color);
        }

        private void StopSideBySideSweep()
        {
            sideBySideSweepActive = false;
            sideBySideSweepElapsed = 0f;
            sideBySideSweepLastReplayTime = float.NaN;
            if (sideBySideSweepRenderer != null)
                sideBySideSweepRenderer.enabled = false;
        }

        private static Mesh CreateSideBySideSweepMesh()
        {
            Mesh mesh =
                new Mesh
                {
                    name = "Runtime_OvertakeBodyLightSweepMesh",
                    vertices = new[]
                    {
                        new Vector3(-0.5f, 0f, -0.5f),
                        new Vector3(0.5f, 0f, -0.5f),
                        new Vector3(0.5f, 0f, 0.5f),
                        new Vector3(-0.5f, 0f, 0.5f)
                    },
                    triangles = new[]
                    {
                        0, 2, 1,
                        0, 3, 2
                    },
                    uv = new[]
                    {
                        new Vector2(0f, 0f),
                        new Vector2(1f, 0f),
                        new Vector2(1f, 1f),
                        new Vector2(0f, 1f)
                    }
                };
            mesh.RecalculateBounds();
            return mesh;
        }

        private bool IsOvertakeSideBySideVfxRenderer(
            Renderer renderer)
        {
            return renderer != null &&
                sideBySideVfxRoot != null &&
                renderer.transform.IsChildOf(sideBySideVfxRoot);
        }

        private void DisposeOvertakeSideBySideVfx()
        {
            if (sideBySideSweepMaterial != null)
                Destroy(sideBySideSweepMaterial);
            if (sideBySideSparkMaterial != null)
                Destroy(sideBySideSparkMaterial);
            if (sideBySideSweepMesh != null)
                Destroy(sideBySideSweepMesh);

            sideBySideSweepMaterial = null;
            sideBySideSparkMaterial = null;
            sideBySideSweepMesh = null;
        }
    }
}
