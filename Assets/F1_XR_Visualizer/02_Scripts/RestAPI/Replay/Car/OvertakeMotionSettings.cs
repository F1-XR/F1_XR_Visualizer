using System;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public enum OvertakeBlendMode
    {
        ClampSum,
        Strongest
    }

    [Serializable]
    public sealed class OvertakeMotionSettings
    {
        public bool enableOvertakeVisuals = true;
        [Min(0f)] public float targetSeparationInVehicleWidths = 1.05f;
        [Min(0f)] public float maximumCorrectionInVehicleWidths = 2f;
        [Min(0f)] public float maximumOffsetInVehicleWidths = 1f;
        [Range(0f, 1f)] public float overtakerShare = 0.5f;
        [Range(0f, 1f)] public float defenderShare = 0.5f;
        [Range(0.05f, 0.9f)] public float approachPortion = 0.3f;
        [Range(0f, 0.9f)] public float parallelPortion = 0.4f;
        [Range(0.05f, 0.9f)] public float returnPortion = 0.3f;
        [Range(0f, 45f)] public float maximumVisualYawDegrees = 14f;
        public OvertakeBlendMode overlapBlendMode = OvertakeBlendMode.ClampSum;
        public bool debugOvertakeVisuals;
    }
}
