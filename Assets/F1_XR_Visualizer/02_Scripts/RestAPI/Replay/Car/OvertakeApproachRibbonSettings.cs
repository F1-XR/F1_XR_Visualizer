using System;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    [Serializable]
    public sealed class OvertakeApproachRibbonSettings
    {
        public bool enabled = true;
        public AnimationCurve growth =
            AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Overtaker")]
        [Min(0.01f)] public float overtakerTrailSeconds = 0.95f;
        [Min(0.001f)] public float overtakerGlowWidthInCarWidths = 0.34f;
        [Min(0.001f)] public float overtakerCoreWidthInCarWidths = 0.1f;
        public Color overtakerGlowColor =
            new(0.04f, 0.68f, 1f, 0.82f);

        [Header("Defender")]
        [Min(0.01f)] public float defenderTrailSeconds = 0.65f;
        [Min(0.001f)] public float defenderGlowWidthInCarWidths = 0.22f;
        [Min(0.001f)] public float defenderCoreWidthInCarWidths = 0.065f;
        public Color defenderGlowColor =
            new(0.2f, 1f, 0.62f, 0.65f);

        [Header("Shared")]
        public Color coreColor =
            new(1f, 1f, 1f, 0.96f);
        [Min(0f)] public float preRollSeconds = 1.25f;
        [Range(0f, 0.5f)] public float startIntensity = 0.22f;
        [Range(0f, 1f)] public float defenderIntensity = 0.68f;
        [Range(0.01f, 0.5f)]
        public float minimumVertexDistanceInCarLengths = 0.065f;
        [Range(0.1f, 0.55f)]
        public float rearOffsetInCarLengths = 0.42f;
        [Range(0f, 0.5f)]
        public float verticalOffsetInCarHeights = 0.24f;
        [Min(0.05f)] public float seekClearThresholdSeconds = 0.5f;
    }
}
