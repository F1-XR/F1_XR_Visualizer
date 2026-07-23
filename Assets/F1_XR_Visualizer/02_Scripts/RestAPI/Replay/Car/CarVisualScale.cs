using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    internal static class CarVisualScale
    {
        private const float VisualSizeRatio = 0.8f;

        public static float NormalizeRatio(float ratio)
        {
            return ratio > 0f ? ratio : 1f;
        }

        public static void Apply(
            Transform visualRoot,
            Vector3 authoredScale,
            float mapScaleRatio)
        {
            if (visualRoot == null)
                return;

            visualRoot.localScale =
                authoredScale *
                NormalizeRatio(mapScaleRatio) *
                VisualSizeRatio;
        }
    }
}
