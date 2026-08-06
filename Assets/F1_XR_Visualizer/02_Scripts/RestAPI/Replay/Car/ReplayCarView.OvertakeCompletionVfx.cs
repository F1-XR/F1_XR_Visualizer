using UnityEngine;
using UnityEngine.Rendering;
using static F1XR.RestAPI.Replay.ReplayCarVisualUtil;

namespace F1XR.RestAPI.Replay
{
    public partial class ReplayCarView
    {
        private const int CompletionPulseSegments = 24;
        private const int CompletionStreakCount = 3;
        private const float CompletionHudHeightInCarLengths = 2.4f;

        private Transform completionVfxRoot;
        private MeshRenderer completionPulseRenderer;
        private Mesh completionPulseMesh;
        private Vector3[] completionPulseVertices;
        private Material completionPulseMaterial;
        private Transform completionSweepRoot;
        private MeshRenderer completionSweepRenderer;
        private Mesh completionSweepMesh;
        private Material completionSweepMaterial;
        private MeshRenderer completionStreakRenderer;
        private Mesh completionStreakMesh;
        private Vector3[] completionStreakVertices;
        private Material completionStreakMaterial;
        private TextMesh completionHudText;
        private MeshRenderer completionHudRenderer;
        private Material completionHudMaterial;
        private MeshRenderer completionHudBackground;
        private Material completionHudBackgroundMaterial;
        private Camera completionHudCamera;
        private OvertakeCompletionVfxSettings completionVfxSettings;
        private float completionVfxStartTime = float.NaN;
        private float completionVfxLastReplayTime = float.NaN;
        private string completionHudOverride;
        private float completionIntensityScale = 1f;
        private OvertakeCompletionVfxProfile completionVfxProfile;

        public void TriggerOvertakeCompletionVfx(
            OvertakeCompletionVfxSettings settings,
            float replayTime)
        {
            TriggerOvertakeCompletionVfx(
                settings,
                replayTime,
                null,
                1f,
                OvertakeCompletionVfxProfile.Standard);
        }

        public void TriggerOvertakeCompletionVfx(
            OvertakeCompletionVfxSettings settings,
            float replayTime,
            string hudText,
            float intensityScale)
        {
            TriggerOvertakeCompletionVfx(
                settings,
                replayTime,
                hudText,
                intensityScale,
                OvertakeCompletionVfxProfile.Standard);
        }

        public void TriggerOvertakeCompletionVfx(
            OvertakeCompletionVfxSettings settings,
            float replayTime,
            string hudText,
            float intensityScale,
            OvertakeCompletionVfxProfile profile)
        {
            if (settings == null || !settings.enabled)
                return;

            completionVfxSettings = settings;
            completionHudOverride = hudText;
            completionIntensityScale = Mathf.Max(
                0.1f,
                intensityScale);
            completionVfxProfile = profile;
            EnsureCompletionVfx();
            completionVfxStartTime = replayTime;
            completionVfxLastReplayTime = replayTime;

            if (completionPulseRenderer != null)
                completionPulseRenderer.enabled = true;

            if (completionHudText != null)
            {
                string text = string.IsNullOrWhiteSpace(
                        completionHudOverride)
                    ? string.IsNullOrWhiteSpace(
                            settings.hudText)
                        ? "OVERTAKE"
                        : settings.hudText
                    : completionHudOverride;
                if (completionHudText.text != text)
                    completionHudText.text = text;

                completionHudText.gameObject.SetActive(true);
                if (completionHudBackground != null)
                    completionHudBackground.gameObject.SetActive(true);
            }

            UpdateOvertakeCompletionVfx(replayTime);
        }

        public void UpdateOvertakeCompletionVfx(float replayTime)
        {
            if (completionVfxSettings == null ||
                float.IsNaN(completionVfxStartTime))
            {
                return;
            }

            if (!float.IsNaN(completionVfxLastReplayTime) &&
                (replayTime < completionVfxLastReplayTime ||
                 replayTime - completionVfxLastReplayTime >
                 completionVfxSettings
                     .seekResetThresholdSeconds))
            {
                ResetOvertakeCompletionVfx();
                return;
            }

            completionVfxLastReplayTime = replayTime;
            float elapsed = Mathf.Max(
                0f,
                replayTime - completionVfxStartTime);
            UpdateCompletionPulse(elapsed);
            UpdateCompletionSweep(elapsed);
            UpdateCompletionStreaks(elapsed);
            UpdateCompletionHud(elapsed);

            float totalDuration = Mathf.Max(
                Mathf.Max(
                    completionVfxSettings
                        .pulseDurationReplaySeconds,
                    completionVfxSettings
                        .sweepDurationReplaySeconds),
                Mathf.Max(
                    completionVfxSettings
                        .streakDurationReplaySeconds,
                    completionVfxSettings
                        .hudDisplayDurationReplaySeconds));
            if (elapsed >= totalDuration)
                ResetOvertakeCompletionVfx();
        }

        public void ResetOvertakeCompletionVfx()
        {
            completionVfxStartTime = float.NaN;
            completionVfxLastReplayTime = float.NaN;
            completionVfxSettings = null;
            completionHudOverride = null;
            completionIntensityScale = 1f;
            completionVfxProfile =
                OvertakeCompletionVfxProfile.Standard;

            if (completionPulseRenderer != null)
                completionPulseRenderer.enabled = false;
            if (completionSweepRenderer != null)
                completionSweepRenderer.enabled = false;
            if (completionStreakRenderer != null)
                completionStreakRenderer.enabled = false;
            if (completionHudText != null)
                completionHudText.gameObject.SetActive(false);
            if (completionHudBackground != null)
                completionHudBackground.gameObject.SetActive(false);
        }

        private void EnsureCompletionVfx()
        {
            if (completionVfxRoot == null)
            {
                GameObject root =
                    new("OvertakeCompletionVfx");
                root.transform.SetParent(transform, false);
                completionVfxRoot = root.transform;
            }

            EnsureCompletionPulse();
            EnsureCompletionSweep();
            EnsureCompletionStreaks();
            EnsureCompletionHud();
        }

        private void EnsureCompletionPulse()
        {
            if (completionPulseRenderer != null)
                return;

            GameObject pulse = new(
                "CompletionGlint",
                typeof(MeshFilter),
                typeof(MeshRenderer));
            pulse.transform.SetParent(
                completionVfxRoot,
                false);
            completionPulseMesh = CreateRingMesh(
                "OvertakeCompletionGlintMesh",
                CompletionPulseSegments,
                out completionPulseVertices);
            pulse.GetComponent<MeshFilter>().sharedMesh =
                completionPulseMesh;

            completionPulseRenderer =
                pulse.GetComponent<MeshRenderer>();
            completionPulseRenderer.shadowCastingMode =
                ShadowCastingMode.Off;
            completionPulseRenderer.receiveShadows = false;
            completionPulseRenderer
                .motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            completionPulseMaterial =
                CreateSelectionMaterial(Color.clear);
            completionPulseMaterial.name =
                "Runtime_OvertakeCompletionGlint";
            completionPulseRenderer.sharedMaterial =
                completionPulseMaterial;
            completionPulseRenderer.enabled = false;
        }

        private void EnsureCompletionSweep()
        {
            if (completionSweepRenderer != null)
                return;

            GameObject sweep =
                new("CompletionBodySweep");
            sweep.transform.SetParent(
                completionVfxRoot,
                false);
            completionSweepRoot = sweep.transform;

            MeshFilter filter =
                sweep.AddComponent<MeshFilter>();
            completionSweepMesh =
                CreateSideBySideSweepMesh();
            completionSweepMesh.name =
                "Runtime_OvertakeCompletionSweepMesh";
            filter.sharedMesh = completionSweepMesh;

            completionSweepMaterial =
                CreateSelectionMaterial(Color.clear);
            completionSweepMaterial.name =
                "Runtime_OvertakeCompletionSweep";
            completionSweepRenderer =
                sweep.AddComponent<MeshRenderer>();
            completionSweepRenderer.sharedMaterial =
                completionSweepMaterial;
            ConfigureCompletionRenderer(
                completionSweepRenderer);
            completionSweepRenderer.enabled = false;
        }

        private void EnsureCompletionStreaks()
        {
            if (completionStreakRenderer != null)
                return;

            GameObject streaks =
                new("CompletionSpeedStreaks");
            streaks.transform.SetParent(
                completionVfxRoot,
                false);

            MeshFilter filter =
                streaks.AddComponent<MeshFilter>();
            completionStreakMesh =
                CreateCompletionStreakMesh(
                    out completionStreakVertices);
            filter.sharedMesh = completionStreakMesh;

            completionStreakMaterial =
                CreateSelectionMaterial(Color.clear);
            completionStreakMaterial.name =
                "Runtime_OvertakeCompletionSpeedStreaks";
            completionStreakRenderer =
                streaks.AddComponent<MeshRenderer>();
            completionStreakRenderer.sharedMaterial =
                completionStreakMaterial;
            ConfigureCompletionRenderer(
                completionStreakRenderer);
            completionStreakRenderer.enabled = false;
        }

        private void EnsureCompletionHud()
        {
            if (completionHudText != null)
                return;

            GameObject textObject =
                new("OvertakeCompletionHud");
            textObject.transform.SetParent(null, false);
            textObject.transform.localScale = Vector3.one;
            completionHudText =
                textObject.AddComponent<TextMesh>();
            completionHudText.anchor =
                TextAnchor.MiddleCenter;
            completionHudText.alignment =
                TextAlignment.Center;
            completionHudText.fontSize = 48;
            completionHudText.characterSize = 0.01f;
            completionHudText.lineSpacing = 0.82f;
            completionHudText.color = Color.white;

            completionHudRenderer =
                textObject.GetComponent<MeshRenderer>();
            completionHudRenderer.shadowCastingMode =
                ShadowCastingMode.Off;
            completionHudRenderer.receiveShadows = false;
            completionHudRenderer
                .motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            completionHudMaterial =
                CreateTextMaterial(
                    completionHudText,
                    Color.white);
            completionHudMaterial.name =
                "Runtime_OvertakeCompletionHud";
            completionHudMaterial.renderQueue = 3200;
            completionHudRenderer.sharedMaterial =
                completionHudMaterial;
            completionHudRenderer.sortingOrder = 20;

            GameObject background =
                GameObject.CreatePrimitive(
                    PrimitiveType.Quad);
            background.name =
                "OvertakeCompletionHudBackground";
            background.transform.SetParent(
                textObject.transform,
                false);
            Collider collider =
                background.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            completionHudBackground =
                background.GetComponent<MeshRenderer>();
            completionHudBackground.shadowCastingMode =
                ShadowCastingMode.Off;
            completionHudBackground.receiveShadows = false;
            completionHudBackground
                .motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            completionHudBackgroundMaterial =
                CreateUnlitMaterial(
                    new Color(0.01f, 0.03f, 0.05f, 0f));
            completionHudBackgroundMaterial.name =
                "Runtime_OvertakeCompletionHudBackground";
            completionHudBackgroundMaterial.renderQueue =
                3190;
            completionHudBackground.sharedMaterial =
                completionHudBackgroundMaterial;
            completionHudBackground.sortingOrder = 19;

            textObject.SetActive(false);
            background.SetActive(false);
        }

        private void UpdateCompletionPulse(float elapsed)
        {
            if (completionPulseRenderer == null)
                return;

            float duration = Mathf.Max(
                0.01f,
                completionVfxSettings
                    .pulseDurationReplaySeconds);
            float progress = Mathf.Clamp01(
                elapsed / duration);
            if (progress >= 1f)
            {
                completionPulseRenderer.enabled = false;
                return;
            }

            completionPulseRenderer.enabled = true;
            if (TryGetCarBounds(out Bounds bounds))
            {
                Vector3 worldCenter = new(
                    bounds.center.x,
                    bounds.max.y +
                    bounds.size.y * 0.04f,
                    bounds.center.z);
                float radius = Mathf.Max(
                    bounds.size.x,
                    bounds.size.z) *
                    Mathf.Lerp(
                        0.58f,
                        1.05f,
                        Mathf.SmoothStep(
                            0f,
                            1f,
                            progress)) *
                    GetCompletionPulseRadiusScale();
                UpdateRingMesh(
                    transform,
                    completionPulseMesh,
                    completionPulseVertices,
                    CompletionPulseSegments,
                    transform.InverseTransformPoint(
                        worldCenter),
                    radius,
                    GetCompletionPulseThickness(),
                    progress *
                    GetCompletionPulseRotation());
            }

            float envelope = GetCompletionPulseEnvelope(
                progress);
            float intensity = Mathf.Max(
                0f,
                completionVfxSettings
                    .pulseIntensity) *
                completionIntensityScale;
            Color color = ResolveCompletionProfileColor(
                completionVfxSettings.pulseColor);
            color.r *= intensity;
            color.g *= intensity;
            color.b *= intensity;
            color.a *=
                envelope *
                Mathf.Clamp01(intensity);
            SetMaterialColor(
                completionPulseMaterial,
                color);
        }

        private void UpdateCompletionSweep(float elapsed)
        {
            if (completionSweepRoot == null ||
                completionSweepRenderer == null)
            {
                return;
            }

            float duration = Mathf.Max(
                0.01f,
                completionVfxSettings
                    .sweepDurationReplaySeconds);
            float progress = Mathf.Clamp01(
                elapsed / duration);
            if (progress >= 1f ||
                !TryGetCompletionLayout(
                    out Bounds bounds,
                    out float width,
                    out float length,
                    out float height))
            {
                completionSweepRenderer.enabled = false;
                return;
            }

            completionSweepRenderer.enabled = true;
            float eased = GetCompletionSweepProgress(
                progress);
            bool reverseSweep =
                completionVfxProfile ==
                OvertakeCompletionVfxProfile.Counter;
            float sweepStart = reverseSweep ? -0.52f : 0.52f;
            float sweepEnd = -sweepStart;
            Vector3 position =
                bounds.center +
                transform.forward *
                length *
                Mathf.Lerp(sweepStart, sweepEnd, eased) +
                transform.up *
                height *
                0.58f;
            completionSweepRoot.localPosition =
                transform.InverseTransformPoint(position);
            completionSweepRoot.localRotation =
                Quaternion.identity;

            Vector3 scale = transform.lossyScale;
            completionSweepRoot.localScale =
                new Vector3(
                    width * 1.18f /
                    Mathf.Max(
                        0.0001f,
                        Mathf.Abs(scale.x)),
                    1f,
                    length *
                    completionVfxSettings
                        .sweepWidthInCarLengths /
                    Mathf.Max(
                        0.0001f,
                        Mathf.Abs(scale.z)));

            float envelope =
                Mathf.Sin(Mathf.PI * progress);
            Color color = ResolveCompletionProfileColor(
                completionVfxSettings.sweepColor);
            float intensity =
                Mathf.Max(
                    0f,
                    completionVfxSettings
                        .sweepIntensity) *
                completionIntensityScale *
                envelope;
            color.r *= intensity;
            color.g *= intensity;
            color.b *= intensity;
            color.a *= envelope;
            SetMaterialColor(
                completionSweepMaterial,
                color);
        }

        private void UpdateCompletionStreaks(float elapsed)
        {
            if (completionStreakRenderer == null ||
                completionStreakMesh == null ||
                completionStreakVertices == null)
            {
                return;
            }

            float duration = Mathf.Max(
                0.01f,
                completionVfxSettings
                    .streakDurationReplaySeconds);
            float progress = Mathf.Clamp01(
                elapsed / duration);
            if (progress >= 1f ||
                !TryGetCompletionLayout(
                    out Bounds bounds,
                    out float width,
                    out float length,
                    out float height))
            {
                completionStreakRenderer.enabled = false;
                return;
            }

            completionStreakRenderer.enabled = true;
            float eased = Mathf.SmoothStep(
                0f,
                1f,
                progress);
            float baseLength =
                length *
                completionVfxSettings
                    .streakLengthInCarLengths;
            float halfWidth =
                width *
                completionVfxSettings
                    .streakWidthInCarWidths *
                Mathf.Lerp(1f, 0.45f, eased);
            Vector3 right = transform.right;
            Vector3 rear = -transform.forward;
            Vector3 up = transform.up;
            Vector3 origin =
                bounds.center +
                up * height * 0.56f +
                rear *
                length *
                Mathf.Lerp(0.38f, 0.62f, eased);

            for (int i = 0;
                 i < CompletionStreakCount;
                 i++)
            {
                float lateral =
                    (i - 1) * width * 0.32f;
                float lengthVariation =
                    i == 1 ? 1f : 0.78f;
                float rearVariation =
                    i == 1 ? 0f : 0.1f;
                Vector3 head =
                    origin +
                    right * lateral +
                    rear *
                    length *
                    rearVariation;
                Vector3 tail =
                    head +
                    rear *
                    baseLength *
                    lengthVariation *
                    Mathf.Lerp(
                        0.42f,
                        1f,
                        eased);
                int vertex = i * 4;
                completionStreakVertices[vertex] =
                    transform.InverseTransformPoint(
                        head - right * halfWidth);
                completionStreakVertices[vertex + 1] =
                    transform.InverseTransformPoint(
                        head + right * halfWidth);
                completionStreakVertices[vertex + 2] =
                    transform.InverseTransformPoint(
                        tail +
                        right *
                        halfWidth *
                        0.12f);
                completionStreakVertices[vertex + 3] =
                    transform.InverseTransformPoint(
                        tail -
                        right *
                        halfWidth *
                        0.12f);
            }

            completionStreakMesh.vertices =
                completionStreakVertices;
            completionStreakMesh.RecalculateBounds();

            float envelope =
                Mathf.Sin(Mathf.PI * progress);
            Color color = ResolveCompletionProfileColor(
                completionVfxSettings.streakColor);
            color.r *= completionIntensityScale;
            color.g *= completionIntensityScale;
            color.b *= completionIntensityScale;
            color.a *= envelope;
            SetMaterialColor(
                completionStreakMaterial,
                color);
        }

        private void UpdateCompletionHud(float elapsed)
        {
            if (completionHudText == null)
                return;

            float duration = Mathf.Max(
                0.01f,
                completionVfxSettings
                    .hudDisplayDurationReplaySeconds);
            if (elapsed >= duration)
            {
                completionHudText.gameObject.SetActive(
                    false);
                if (completionHudBackground != null)
                {
                    completionHudBackground.gameObject
                        .SetActive(false);
                }
                return;
            }

            if (!completionHudText.gameObject.activeSelf)
                completionHudText.gameObject.SetActive(true);
            if (completionHudBackground != null)
            {
                if (!completionHudBackground.gameObject
                        .activeSelf)
                {
                    completionHudBackground.gameObject
                        .SetActive(true);
                }
            }

            float fadeIn = Mathf.Max(
                0f,
                completionVfxSettings
                    .hudFadeInReplaySeconds);
            float fadeOut = Mathf.Max(
                0f,
                completionVfxSettings
                    .hudFadeOutReplaySeconds);
            float fadeInAlpha = fadeIn <= 0.0001f
                ? 1f
                : Mathf.Clamp01(elapsed / fadeIn);
            float fadeOutStart =
                Mathf.Max(0f, duration - fadeOut);
            float fadeOutAlpha =
                fadeOut <= 0.0001f ||
                elapsed <= fadeOutStart
                    ? 1f
                    : 1f -
                      Mathf.Clamp01(
                          (elapsed - fadeOutStart) /
                          fadeOut);
            float alpha = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Min(
                    fadeInAlpha,
                    fadeOutAlpha));

            Color hudColor = GetCompletionHudColor(alpha);
            SetMaterialColor(
                completionHudMaterial,
                hudColor);
            completionHudText.color =
                hudColor;
            Color backgroundColor =
                GetCompletionHudBackgroundColor(alpha);
            SetMaterialColor(
                completionHudBackgroundMaterial,
                backgroundColor);
            UpdateCompletionHudLayout(
                Mathf.Clamp01(elapsed / duration));
        }

        private float GetCompletionPulseRadiusScale()
        {
            return completionVfxProfile switch
            {
                OvertakeCompletionVfxProfile.Counter => 0.92f,
                OvertakeCompletionVfxProfile.Repass => 1.12f,
                OvertakeCompletionVfxProfile.Victory => 1.28f,
                _ => 1f
            };
        }

        private float GetCompletionPulseThickness()
        {
            return completionVfxProfile switch
            {
                OvertakeCompletionVfxProfile.Counter => 0.5f,
                OvertakeCompletionVfxProfile.Repass => 0.82f,
                OvertakeCompletionVfxProfile.Victory => 0.95f,
                _ => 0.7f
            };
        }

        private float GetCompletionPulseRotation()
        {
            return completionVfxProfile switch
            {
                OvertakeCompletionVfxProfile.Counter => -65f,
                OvertakeCompletionVfxProfile.Repass => 125f,
                OvertakeCompletionVfxProfile.Victory => 22f,
                _ => 35f
            };
        }

        private float GetCompletionPulseEnvelope(float progress)
        {
            if (completionVfxProfile ==
                OvertakeCompletionVfxProfile.Repass)
            {
                return Mathf.Abs(
                    Mathf.Sin(progress * Mathf.PI * 2f));
            }

            return Mathf.Sin(progress * Mathf.PI);
        }

        private float GetCompletionSweepProgress(float progress)
        {
            float travel = completionVfxProfile ==
                OvertakeCompletionVfxProfile.Repass
                    ? Mathf.PingPong(progress * 2f, 1f)
                    : progress;
            return Mathf.SmoothStep(0f, 1f, travel);
        }

        private Color ResolveCompletionProfileColor(Color source)
        {
            if (completionVfxSettings != null &&
                completionVfxSettings.useDriverColor)
            {
                Color driverColor = ResolveOvertakeDriverColor(
                    source,
                    true,
                    completionVfxSettings.driverColorBlend,
                    completionVfxSettings
                        .minimumDriverColorBrightness);
                float profileIntensity = completionVfxProfile switch
                {
                    OvertakeCompletionVfxProfile.Counter => 1.08f,
                    OvertakeCompletionVfxProfile.Repass => 1.14f,
                    OvertakeCompletionVfxProfile.Victory => 1.2f,
                    _ => 1f
                };
                driverColor.r *= profileIntensity;
                driverColor.g *= profileIntensity;
                driverColor.b *= profileIntensity;
                driverColor.a = source.a;
                return driverColor;
            }

            Color accent;
            float blend;
            switch (completionVfxProfile)
            {
                case OvertakeCompletionVfxProfile.Counter:
                    accent = new Color(
                        1.4f,
                        0.28f,
                        0.06f,
                        source.a);
                    blend = 0.78f;
                    break;
                case OvertakeCompletionVfxProfile.Repass:
                    accent = new Color(
                        0.72f,
                        0.28f,
                        1.45f,
                        source.a);
                    blend = 0.78f;
                    break;
                case OvertakeCompletionVfxProfile.Victory:
                    accent = new Color(
                        1.55f,
                        1.02f,
                        0.16f,
                        source.a);
                    blend = 0.86f;
                    break;
                default:
                    return source;
            }

            Color result = Color.Lerp(source, accent, blend);
            result.a = source.a;
            return result;
        }

        private Color GetCompletionHudColor(float alpha)
        {
            Color color = completionVfxProfile switch
            {
                OvertakeCompletionVfxProfile.Counter =>
                    new Color(1f, 0.42f, 0.12f, alpha),
                OvertakeCompletionVfxProfile.Repass =>
                    new Color(0.76f, 0.52f, 1f, alpha),
                OvertakeCompletionVfxProfile.Victory =>
                    new Color(1f, 0.86f, 0.22f, alpha),
                _ => new Color(1f, 1f, 1f, alpha)
            };
            color.a = alpha;
            return color;
        }

        private Color GetCompletionHudBackgroundColor(float alpha)
        {
            Color color = completionVfxProfile switch
            {
                OvertakeCompletionVfxProfile.Counter =>
                    new Color(0.11f, 0.015f, 0.005f, 1f),
                OvertakeCompletionVfxProfile.Repass =>
                    new Color(0.045f, 0.01f, 0.09f, 1f),
                OvertakeCompletionVfxProfile.Victory =>
                    new Color(0.1f, 0.065f, 0.005f, 1f),
                _ => new Color(0.01f, 0.03f, 0.05f, 1f)
            };
            color.a = 0.58f * alpha;
            return color;
        }

        private void UpdateCompletionHudLayout(
            float progress)
        {
            if (!TryGetCompletionLayout(
                    out Bounds bounds,
                    out float width,
                    out float length,
                    out float height))
            {
                return;
            }

            float carWorldSize =
                Mathf.Max(
                    bounds.size.x,
                    bounds.size.y,
                    bounds.size.z);
            float targetWorldHeight =
                carWorldSize *
                CompletionHudHeightInCarLengths *
                Mathf.Max(
                    0.1f,
                    completionVfxSettings.hudScale);

            Vector3 offset =
                completionVfxSettings.hudWorldOffset;
            switch (completionVfxSettings.hudAnchor)
            {
                case OvertakeCompletionHudAnchor.Above:
                    offset.x = 0f;
                    break;
                case OvertakeCompletionHudAnchor.AboveLeft:
                    offset.x =
                        -width * 0.68f -
                        Mathf.Abs(offset.x);
                    break;
                default:
                    offset.x =
                        width * 0.68f +
                        Mathf.Abs(offset.x);
                    break;
            }

            Vector3 anchor = new(
                bounds.center.x,
                bounds.max.y,
                bounds.center.z);
            completionHudText.transform.position =
                anchor +
                transform.right * offset.x +
                Vector3.up *
                (offset.y +
                 height * 0.32f +
                 targetWorldHeight *
                 Mathf.Lerp(
                     0.68f,
                     1.02f,
                     Mathf.SmoothStep(
                         0f,
                         1f,
                         progress))) +
                transform.forward * offset.z;

            if (completionHudCamera == null ||
                !completionHudCamera.isActiveAndEnabled)
            {
                completionHudCamera = Camera.main;
            }

            if (completionHudCamera != null)
            {
                completionHudText.transform.rotation =
                    completionHudCamera.transform.rotation;
            }

            float popScale =
                Mathf.Lerp(
                    0.82f,
                    1f,
                    Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.Clamp01(
                            progress / 0.16f)));
            float targetCharacterSize =
                targetWorldHeight *
                popScale /
                completionHudText.fontSize;
            if (!Mathf.Approximately(
                    completionHudText.characterSize,
                    targetCharacterSize))
            {
                completionHudText.characterSize =
                    targetCharacterSize;
            }

            GetTextBackgroundTransform(
                completionHudText,
                completionHudRenderer,
                0.04f,
                out Vector3 backgroundPosition,
                out Vector3 backgroundScale);
            completionHudBackground.transform
                .localPosition = backgroundPosition;
            completionHudBackground.transform
                .localScale = backgroundScale;
        }

        private bool TryGetCompletionLayout(
            out Bounds bounds,
            out float width,
            out float length,
            out float height)
        {
            width = GetVisualWidth();
            length = GetVisualLength();
            if (!TryGetCarBounds(out bounds))
            {
                height = 0f;
                return false;
            }

            width = Mathf.Max(
                0.001f,
                width);
            length = Mathf.Max(
                0.001f,
                length);
            height = Mathf.Max(
                0.001f,
                bounds.size.y);
            return true;
        }

        private static void ConfigureCompletionRenderer(
            MeshRenderer renderer)
        {
            renderer.shadowCastingMode =
                ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            renderer.lightProbeUsage =
                LightProbeUsage.Off;
            renderer.reflectionProbeUsage =
                ReflectionProbeUsage.Off;
        }

        private static Mesh CreateCompletionStreakMesh(
            out Vector3[] vertices)
        {
            vertices =
                new Vector3[
                    CompletionStreakCount * 4];
            int[] triangles =
                new int[
                    CompletionStreakCount * 6];
            for (int i = 0;
                 i < CompletionStreakCount;
                 i++)
            {
                int vertex = i * 4;
                int triangle = i * 6;
                triangles[triangle] = vertex;
                triangles[triangle + 1] =
                    vertex + 2;
                triangles[triangle + 2] =
                    vertex + 1;
                triangles[triangle + 3] =
                    vertex;
                triangles[triangle + 4] =
                    vertex + 3;
                triangles[triangle + 5] =
                    vertex + 2;
            }

            Mesh mesh =
                new()
                {
                    name =
                        "Runtime_OvertakeCompletionStreakMesh",
                    vertices = vertices,
                    triangles = triangles
                };
            mesh.MarkDynamic();
            mesh.RecalculateBounds();
            return mesh;
        }

        private bool IsOvertakeCompletionVfxRenderer(
            Renderer renderer)
        {
            return renderer != null &&
                completionVfxRoot != null &&
                renderer.transform.IsChildOf(
                    completionVfxRoot);
        }

        private void DisposeOvertakeCompletionVfx()
        {
            if (completionHudText != null)
                Destroy(completionHudText.gameObject);
            if (completionPulseMaterial != null)
                Destroy(completionPulseMaterial);
            if (completionSweepMaterial != null)
                Destroy(completionSweepMaterial);
            if (completionStreakMaterial != null)
                Destroy(completionStreakMaterial);
            if (completionHudMaterial != null)
                Destroy(completionHudMaterial);
            if (completionHudBackgroundMaterial != null)
                Destroy(completionHudBackgroundMaterial);
            if (completionPulseMesh != null)
                Destroy(completionPulseMesh);
            if (completionSweepMesh != null)
                Destroy(completionSweepMesh);
            if (completionStreakMesh != null)
                Destroy(completionStreakMesh);

            completionPulseMaterial = null;
            completionSweepMaterial = null;
            completionStreakMaterial = null;
            completionHudMaterial = null;
            completionHudBackgroundMaterial = null;
            completionPulseMesh = null;
            completionSweepMesh = null;
            completionStreakMesh = null;
            completionStreakVertices = null;
            completionHudText = null;
            completionHudRenderer = null;
            completionHudBackground = null;
        }
    }
}
