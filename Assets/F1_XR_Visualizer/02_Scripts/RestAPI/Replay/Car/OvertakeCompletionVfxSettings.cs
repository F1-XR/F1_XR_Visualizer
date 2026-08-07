using System;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public enum OvertakeCompletionHudAnchor
    {
        Above,
        AboveLeft,
        AboveRight
    }

    public enum OvertakeCompletionVfxProfile
    {
        Standard,
        Counter,
        Repass,
        Victory
    }

    [Serializable]
    public sealed class OvertakeCompletionVfxSettings
    {
        public bool enabled = true;

        [Header("Completion Detection")]
        [Min(0f)]
        public float clearanceInCarLengths = 0.15f;
        [Min(0f)]
        public float stabilityDurationReplaySeconds = 0.2f;
        [Min(0f)]
        public float hysteresisInCarLengths = 0.05f;
        public bool allowOrderingLeadFallback = true;
        [Min(0f)]
        public float orderingLeadInCarLengths = 0.08f;

        [Header("Completion Accent Flash")]
        public bool accentFlashEnabled = true;
        [Range(0.12f, 0.45f)]
        public float accentFlashDurationReplaySeconds = 0.22f;
        [Range(0.35f, 1.4f)]
        public float accentFlashSizeInCarWidths = 0.82f;
        [Min(0f)]
        public float accentFlashIntensity = 2.1f;
        [ColorUsage(true, true)]
        public Color accentFlashColor =
            new(1f, 1f, 1f, 0.96f);

        [Header("Completion Pulse")]
        [Range(0.25f, 0.6f)]
        public float pulseDurationReplaySeconds = 0.4f;
        [Min(0f)]
        public float pulseIntensity = 1.35f;
        [ColorUsage(true, true)]
        public Color pulseColor =
            new(0.32f, 0.88f, 1.25f, 0.86f);

        [Header("Completion Sweep")]
        [Range(0.25f, 0.8f)]
        public float sweepDurationReplaySeconds = 0.55f;
        [Range(0.04f, 0.3f)]
        public float sweepWidthInCarLengths = 0.16f;
        [Min(0f)]
        public float sweepIntensity = 1.8f;
        [ColorUsage(true, true)]
        public Color sweepColor =
            new(0.72f, 1.05f, 1.35f, 0.92f);

        [Header("Completion Speed Streaks")]
        [Range(0.25f, 0.9f)]
        public float streakDurationReplaySeconds = 0.62f;
        [Range(0.25f, 1.5f)]
        public float streakLengthInCarLengths = 0.95f;
        [Range(0.01f, 0.15f)]
        public float streakWidthInCarWidths = 0.055f;
        [ColorUsage(true, true)]
        public Color streakColor =
            new(0.35f, 0.92f, 1.35f, 0.88f);

        [Header("World HUD")]
        [TextArea(1, 2)]
        public string hudText = "OVERTAKE\n+1 POSITION";
        [Range(0.7f, 1.2f)]
        public float hudDisplayDurationReplaySeconds = 1.2f;
        [Min(0f)]
        public float hudFadeInReplaySeconds = 0.12f;
        [Min(0f)]
        public float hudFadeOutReplaySeconds = 0.25f;
        [Range(0.5f, 2f)]
        public float hudScale = 1.3f;
        public OvertakeCompletionHudAnchor hudAnchor =
            OvertakeCompletionHudAnchor.Above;
        public Vector3 hudWorldOffset =
            new(0f, 0.012f, 0f);

        [Header("Seek / Reset")]
        [Min(0.05f)]
        public float seekResetThresholdSeconds = 0.5f;
        public bool suppressForwardSeekConfirmation = true;
    }
}
