using System;
using UnityEngine;

namespace F1XR.RestAPI.Replay.Room
{
    public enum OvertakePortalCrossingDirection
    {
        RoomInsideToPortalOutside,
        PortalOutsideToRoomInside
    }

    public enum OvertakePortalLargeForwardSeekPolicy
    {
        None,
        PortalEdgePulseOnly
    }

    [Serializable]
    public sealed class OvertakePortalTransitionVfxSettings
    {
        public bool enabled = true;
        public bool overtakingCarOnly = true;
        public OvertakePortalCrossingDirection crossingDirection =
            OvertakePortalCrossingDirection
                .RoomInsideToPortalOutside;
        [Min(0.001f)]
        public float crossingHysteresis = 0.02f;

        [Header("Localized Ripple")]
        [Range(0.35f, 0.8f)]
        public float rippleDurationReplaySeconds = 0.55f;
        [Min(0.01f)]
        public float rippleStartRadius = 0.08f;
        [Min(0.01f)]
        public float rippleEndRadius = 0.7f;
        [Min(0.005f)]
        public float rippleWidth = 0.055f;
        [Min(0f)]
        public float rippleIntensity = 1.6f;
        [ColorUsage(true, true)]
        public Color rippleCoreColor =
            new(1.35f, 1.45f, 1.55f, 0.95f);
        [ColorUsage(true, true)]
        public Color rippleGlowColor =
            new(0.12f, 0.82f, 1.4f, 0.72f);

        [Header("Portal Wake")]
        [Range(0.2f, 0.6f)]
        public float wakeDurationReplaySeconds = 0.46f;
        [Min(0.01f)]
        public float wakeLength = 0.8f;
        [Min(0.005f)]
        public float wakeWidth = 0.24f;
        [Min(0f)]
        public float wakeIntensity = 1.5f;
        public AnimationCurve wakeFadeCurve =
            AnimationCurve.EaseInOut(
                0f,
                1f,
                1f,
                0f);

        [Header("Portal Surface Sweep")]
        [Range(0.2f, 0.6f)]
        public float surfaceSweepDurationReplaySeconds = 0.36f;
        [Min(0.005f)]
        public float surfaceSweepWidth = 0.045f;
        [Min(0f)]
        public float surfaceSweepIntensity = 1.15f;

        [Header("Portal Edge")]
        [Range(0.2f, 0.8f)]
        public float edgePulseDurationReplaySeconds = 0.52f;
        [Min(0f)]
        public float edgePulseIntensity = 0.9f;
        [ColorUsage(true, true)]
        public Color edgePulseColor =
            new(0.18f, 0.72f, 1.15f, 0.68f);

        [Header("Seek / Reset")]
        [Min(0.05f)]
        public float largeForwardSeekThresholdSeconds = 0.75f;
        public OvertakePortalLargeForwardSeekPolicy
            largeForwardSeekPolicy =
                OvertakePortalLargeForwardSeekPolicy.None;
        [Min(0.05f)]
        public float seekResetThresholdSeconds = 0.5f;

        internal void ClampValues()
        {
            crossingHysteresis =
                Mathf.Max(
                    0.001f,
                    crossingHysteresis);
            rippleDurationReplaySeconds =
                Mathf.Clamp(
                    rippleDurationReplaySeconds,
                    0.35f,
                    0.8f);
            rippleStartRadius =
                Mathf.Max(
                    0.01f,
                    rippleStartRadius);
            rippleEndRadius =
                Mathf.Max(
                    rippleStartRadius,
                    rippleEndRadius);
            rippleWidth =
                Mathf.Clamp(
                    rippleWidth,
                    0.005f,
                    rippleEndRadius * 0.8f);
            rippleIntensity =
                Mathf.Max(
                    0f,
                    rippleIntensity);
            wakeDurationReplaySeconds =
                Mathf.Clamp(
                    wakeDurationReplaySeconds,
                    0.2f,
                    0.6f);
            wakeLength =
                Mathf.Max(
                    0.01f,
                    wakeLength);
            wakeWidth =
                Mathf.Max(
                    0.005f,
                    wakeWidth);
            wakeIntensity =
                Mathf.Max(
                    0f,
                    wakeIntensity);
            wakeFadeCurve ??=
                AnimationCurve.EaseInOut(
                    0f,
                    1f,
                    1f,
                    0f);
            surfaceSweepDurationReplaySeconds =
                Mathf.Clamp(
                    surfaceSweepDurationReplaySeconds,
                    0.2f,
                    0.6f);
            surfaceSweepWidth =
                Mathf.Max(
                    0.005f,
                    surfaceSweepWidth);
            surfaceSweepIntensity =
                Mathf.Max(
                    0f,
                    surfaceSweepIntensity);
            edgePulseDurationReplaySeconds =
                Mathf.Clamp(
                    edgePulseDurationReplaySeconds,
                    0.2f,
                    0.8f);
            edgePulseIntensity =
                Mathf.Max(
                    0f,
                    edgePulseIntensity);
            seekResetThresholdSeconds =
                Mathf.Max(
                    0.05f,
                    seekResetThresholdSeconds);
            largeForwardSeekThresholdSeconds =
                Mathf.Max(
                    seekResetThresholdSeconds,
                    largeForwardSeekThresholdSeconds);
        }
    }
}
