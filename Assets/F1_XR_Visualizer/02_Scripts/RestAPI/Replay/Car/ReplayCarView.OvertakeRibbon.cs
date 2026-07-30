using UnityEngine;
using UnityEngine.Rendering;
using static F1XR.RestAPI.Replay.ReplayCarVisualUtil;

namespace F1XR.RestAPI.Replay
{
    public partial class ReplayCarView
    {
        private Transform overtakeRibbonRoot;
        private TrailRenderer overtakeRibbonGlow;
        private TrailRenderer overtakeRibbonCore;
        private Material overtakeRibbonGlowMaterial;
        private Material overtakeRibbonCoreMaterial;
        private float overtakeRibbonLastReplayTime = float.NaN;
        private float overtakeRibbonWorldWidth = 0.001f;
        private float overtakeRibbonWorldHeight = 0.001f;
        private float overtakeRibbonWorldLength = 0.001f;
        private Bounds overtakeVfxWorldBounds;
        private bool overtakeVfxHasWorldBounds;
        private bool overtakeRibbonLayoutResolved;

        public void SetOvertakeApproachRibbon(
            OvertakeApproachRibbonSettings settings,
            bool overtaker,
            float intensity,
            float replayTime,
            float trailTimeMultiplier = 1f,
            float glowWidthMultiplier = 1f,
            float coreWidthMultiplier = 1f,
            bool allowEmission = true)
        {
            if (settings == null || !settings.enabled)
            {
                ClearOvertakeApproachRibbon();
                return;
            }

            EnsureOvertakeRibbon(settings, overtaker);
            if (overtakeRibbonGlow == null || overtakeRibbonCore == null)
                return;

            if (!float.IsNaN(overtakeRibbonLastReplayTime) &&
                (replayTime < overtakeRibbonLastReplayTime ||
                 replayTime - overtakeRibbonLastReplayTime >
                 settings.seekClearThresholdSeconds))
            {
                ClearOvertakeRibbonTrails();
            }

            overtakeRibbonLastReplayTime = replayTime;
            intensity = Mathf.Max(
                0f,
                intensity *
                (overtaker ? 1f : settings.defenderIntensity));

            ResolveOvertakeRibbonLayout(settings);

            float minimumVertexDistance =
                overtakeRibbonWorldLength *
                settings.minimumVertexDistanceInCarLengths;
            overtakeRibbonGlow.minVertexDistance =
                minimumVertexDistance;
            overtakeRibbonCore.minVertexDistance =
                minimumVertexDistance;

            float trailSeconds = overtaker
                ? settings.overtakerTrailSeconds
                : settings.defenderTrailSeconds;
            trailSeconds *=
                Mathf.Max(0.01f, trailTimeMultiplier);
            float glowWidth = overtaker
                ? settings.overtakerGlowWidthInCarWidths
                : settings.defenderGlowWidthInCarWidths;
            glowWidth *=
                Mathf.Max(0.01f, glowWidthMultiplier);
            float coreWidth = overtaker
                ? settings.overtakerCoreWidthInCarWidths
                : settings.defenderCoreWidthInCarWidths;
            coreWidth *=
                Mathf.Max(0.01f, coreWidthMultiplier);

            float brightnessBoost =
                Mathf.Max(1f, intensity);
            Color glowColor = overtaker
                ? settings.overtakerGlowColor
                : settings.defenderGlowColor;
            glowColor.r *= brightnessBoost;
            glowColor.g *= brightnessBoost;
            glowColor.b *= brightnessBoost;
            glowColor.a = Mathf.Clamp01(
                glowColor.a *
                (allowEmission
                    ? brightnessBoost
                    : Mathf.Clamp01(intensity)));
            Color coreColor = settings.coreColor;
            coreColor.r *= brightnessBoost;
            coreColor.g *= brightnessBoost;
            coreColor.b *= brightnessBoost;
            coreColor.a = Mathf.Clamp01(
                coreColor.a *
                (allowEmission
                    ? brightnessBoost
                    : Mathf.Clamp01(intensity)));
            SetMaterialColor(
                overtakeRibbonGlowMaterial,
                glowColor);
            SetMaterialColor(
                overtakeRibbonCoreMaterial,
                coreColor);

            bool emitting =
                allowEmission &&
                intensity > 0.001f;
            overtakeRibbonGlow.emitting = emitting;
            overtakeRibbonCore.emitting = emitting;
            overtakeRibbonGlow.time = trailSeconds;
            overtakeRibbonCore.time = trailSeconds;
            overtakeRibbonGlow.widthMultiplier =
                overtakeRibbonWorldWidth *
                glowWidth *
                intensity;
            overtakeRibbonCore.widthMultiplier =
                overtakeRibbonWorldWidth *
                coreWidth *
                intensity;
        }

        public void ClearOvertakeApproachRibbon()
        {
            if (overtakeRibbonGlow != null)
                overtakeRibbonGlow.emitting = false;
            if (overtakeRibbonCore != null)
                overtakeRibbonCore.emitting = false;

            overtakeRibbonLastReplayTime = float.NaN;
            ClearOvertakeRibbonTrails();
        }

        private void EnsureOvertakeRibbon(
            OvertakeApproachRibbonSettings settings,
            bool overtaker)
        {
            if (overtakeRibbonRoot == null)
            {
                GameObject root = new("OvertakeApproachRibbon");
                root.transform.SetParent(transform, false);
                overtakeRibbonRoot = root.transform;
            }

            Color glowColor = overtaker
                ? settings.overtakerGlowColor
                : settings.defenderGlowColor;
            overtakeRibbonGlowMaterial ??=
                CreateSelectionMaterial(glowColor);
            overtakeRibbonCoreMaterial ??=
                CreateUnlitMaterial(settings.coreColor);

            overtakeRibbonGlow ??= CreateOvertakeTrail(
                "Glow",
                overtakeRibbonGlowMaterial,
                0);
            overtakeRibbonCore ??= CreateOvertakeTrail(
                "WhiteCore",
                overtakeRibbonCoreMaterial,
                1);

            SetMaterialColor(
                overtakeRibbonGlowMaterial,
                glowColor);
            SetMaterialColor(
                overtakeRibbonCoreMaterial,
                settings.coreColor);

        }

        private void ResolveOvertakeRibbonLayout(
            OvertakeApproachRibbonSettings settings)
        {
            if (overtakeRibbonLayoutResolved)
                return;

            Renderer[] renderers =
                GetComponentsInChildren<Renderer>(true);
            float worldWidth = GetOvertakeRibbonWorldSize(
                renderers,
                transform.right);
            float worldHeight = GetOvertakeRibbonWorldSize(
                renderers,
                transform.up);
            float worldLength = GetOvertakeRibbonWorldSize(
                renderers,
                transform.forward);
            if (worldWidth > overtakeRibbonWorldWidth)
                overtakeRibbonWorldWidth = worldWidth;
            if (worldHeight > overtakeRibbonWorldHeight)
                overtakeRibbonWorldHeight = worldHeight;
            if (worldLength > overtakeRibbonWorldLength)
                overtakeRibbonWorldLength = worldLength;

            Bounds worldBounds = default;
            bool hasBounds = false;
            foreach (Renderer item in renderers)
            {
                if (!ShouldIncludeOvertakeRibbonSize(item))
                    continue;

                if (hasBounds)
                    worldBounds.Encapsulate(item.bounds);
                else
                {
                    worldBounds = item.bounds;
                    hasBounds = true;
                }
            }

            if (hasBounds)
            {
                overtakeVfxWorldBounds = worldBounds;
                overtakeVfxHasWorldBounds = true;
                Vector3 rearWorld =
                    worldBounds.center -
                    transform.forward *
                    overtakeRibbonWorldLength *
                    settings.rearOffsetInCarLengths +
                    transform.up *
                    overtakeRibbonWorldHeight *
                    settings.verticalOffsetInCarHeights;
                overtakeRibbonRoot.localPosition =
                    transform.InverseTransformPoint(rearWorld);
            }

            overtakeRibbonLayoutResolved =
                overtakeRibbonWorldWidth >= 0.01f &&
                overtakeRibbonWorldHeight >= 0.01f &&
                overtakeRibbonWorldLength >= 0.01f;
        }

        private float GetOvertakeRibbonWorldSize(
            Renderer[] renderers,
            Vector3 worldAxis)
        {
            if (worldAxis.sqrMagnitude <= 0.000001f)
                return 0.001f;

            worldAxis.Normalize();
            float minimum = float.PositiveInfinity;
            float maximum = float.NegativeInfinity;
            foreach (Renderer item in renderers)
            {
                if (!ShouldIncludeOvertakeRibbonSize(item))
                    continue;

                Bounds bounds = item.localBounds;
                Matrix4x4 rendererToWorld =
                    item.transform.localToWorldMatrix;
                Vector3 min = bounds.min;
                Vector3 max = bounds.max;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = new(
                        (corner & 1) == 0 ? min.x : max.x,
                        (corner & 2) == 0 ? min.y : max.y,
                        (corner & 4) == 0 ? min.z : max.z);
                    float value = Vector3.Dot(
                        rendererToWorld.MultiplyPoint3x4(point),
                        worldAxis);
                    minimum = Mathf.Min(minimum, value);
                    maximum = Mathf.Max(maximum, value);
                }
            }

            return minimum <= maximum
                ? Mathf.Max(0.001f, maximum - minimum)
                : 0.001f;
        }

        private bool ShouldIncludeOvertakeRibbonSize(
            Renderer renderer)
        {
            if (renderer == null || IsIgnoredRenderer(renderer))
                return false;

            Transform parent = renderer.transform.parent;
            if (!renderer.name.StartsWith(
                    "RoomVehicleProxy_",
                    System.StringComparison.Ordinal) ||
                parent == null)
            {
                return true;
            }

            Renderer source = parent.GetComponent<Renderer>();
            return source == null || !IsIgnoredRenderer(source);
        }

        private TrailRenderer CreateOvertakeTrail(
            string objectName,
            Material material,
            int sortingOrder)
        {
            GameObject trailObject = new(objectName);
            trailObject.transform.SetParent(
                overtakeRibbonRoot,
                false);
            TrailRenderer trail =
                trailObject.AddComponent<TrailRenderer>();
            trail.sharedMaterial = material;
            trail.alignment = LineAlignment.View;
            trail.textureMode = LineTextureMode.Stretch;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            trail.generateLightingData = false;
            trail.numCapVertices = 0;
            trail.numCornerVertices = 0;
            trail.startWidth = 1f;
            trail.endWidth = 0f;
            trail.sortingOrder = sortingOrder;
            trail.emitting = false;
            trail.autodestruct = false;
            return trail;
        }

        private void ClearOvertakeRibbonTrails()
        {
            overtakeRibbonGlow?.Clear();
            overtakeRibbonCore?.Clear();
        }

        private bool IsOvertakeRibbonRenderer(
            Renderer renderer)
        {
            return renderer != null &&
                overtakeRibbonRoot != null &&
                renderer.transform.IsChildOf(overtakeRibbonRoot);
        }

        private void DisposeOvertakeRibbon()
        {
            if (overtakeRibbonGlowMaterial != null)
                Destroy(overtakeRibbonGlowMaterial);
            if (overtakeRibbonCoreMaterial != null)
                Destroy(overtakeRibbonCoreMaterial);

            overtakeRibbonGlowMaterial = null;
            overtakeRibbonCoreMaterial = null;
        }
    }
}
