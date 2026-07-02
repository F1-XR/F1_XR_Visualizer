using F1XR.RestAPI.Api;
using UnityEngine;

namespace F1XR.RestAPI.Utility
{
    public static class CoordinateUtil
    {
        public static float scale = 0.01f;

        public static Vector3 ToUnity(LocationSample sample)
        {
            return new Vector3(
                sample.x * scale,
                sample.z * scale,
                sample.y * scale
            );
        }
    }
}