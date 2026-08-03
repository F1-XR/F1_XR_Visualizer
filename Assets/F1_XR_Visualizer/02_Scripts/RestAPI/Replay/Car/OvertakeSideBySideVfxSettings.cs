using System;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    [Serializable]
    public sealed class OvertakeSideBySideVfxSettings
    {
        public bool enabled = true;

        [Header("Phase")]
        [Range(-0.15f, 0.15f)]
        public float startOffsetNormalized;
        [Range(-0.15f, 0.15f)]
        public float endOffsetNormalized;
        [Min(0.01f)]
        public float transitionBlendSeconds = 0.35f;

        [Header("Overtaker Ribbon")]
        [Min(0.01f)]
        public float overtakerTrailTimeMultiplier = 1.45f;
        [Min(0.01f)]
        public float overtakerGlowWidthMultiplier = 1.25f;
        [Min(0.01f)]
        public float overtakerCoreWidthMultiplier = 1.15f;
        [Min(0.01f)]
        public float overtakerIntensityMultiplier = 1.3f;

        [Header("Defender Ribbon")]
        [Min(0.01f)]
        public float defenderTrailTimeMultiplier = 1.1f;
        [Min(0.01f)]
        public float defenderGlowWidthMultiplier = 1.05f;
        [Min(0.01f)]
        public float defenderCoreWidthMultiplier = 1.03f;
        [Min(0.01f)]
        public float defenderIntensityMultiplier = 1.05f;

        [Header("Light Sweep")]
        [Range(0.3f, 0.6f)]
        public float lightSweepDuration = 0.45f;
        [Range(0.03f, 0.3f)]
        public float lightSweepWidthInCarLengths = 0.12f;
        [Min(0f)]
        public float lightSweepIntensity = 1.45f;
        [ColorUsage(true, true)]
        public Color lightSweepColor =
            new(0.45f, 0.9f, 1.35f, 0.82f);
        [Range(0f, 0.3f)]
        public float lightSweepTopOffsetInCarHeights = 0.08f;

        [Header("Underfloor Sparks")]
        [Range(1, 16)]
        public int sparkBurstCount = 8;
        [Range(0.05f, 0.5f)]
        public float sparkLifetime = 0.22f;
        [Range(0.005f, 0.12f)]
        public float sparkSizeInCarWidths = 0.035f;
        [Range(0.1f, 4f)]
        public float sparkSpeedInCarLengthsPerSecond = 1.5f;
        public Color sparkEmissionColor =
            new(1f, 0.62f, 0.16f, 0.9f);
        [Range(0f, 1f)]
        public float sparkTriggerNormalized = 0.55f;
        [Range(0f, 0.5f)]
        public float sparkRearOffsetInCarLengths = 0.28f;
        [Range(0f, 0.3f)]
        public float sparkFloorOffsetInCarHeights = 0.08f;

        [Header("Reset")]
        [Min(0.05f)]
        public float seekResetThresholdSeconds = 0.5f;
    }
}
