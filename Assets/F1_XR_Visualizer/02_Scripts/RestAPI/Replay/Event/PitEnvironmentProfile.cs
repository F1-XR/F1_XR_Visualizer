using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    [CreateAssetMenu(
        fileName = "PitEnvironmentProfile",
        menuName = "F1 XR/Replay/Pit Environment Profile")]
    public sealed class PitEnvironmentProfile : ScriptableObject
    {
        public string circuit;

        [Header("Pit Track")]
        public GameObject pitTrackPrefab;
        public Vector3 pitTrackLocalPosition;
        public Vector3 pitTrackLocalEulerAngles;
        public Vector3 pitTrackLocalScale = Vector3.one;

        [Header("Pit Building")]
        public GameObject pitBuildingPrefab;
        public Vector3 pitBuildingLocalPosition;
        public Vector3 pitBuildingLocalEulerAngles;
        public Vector3 pitBuildingLocalScale = Vector3.one;

        [Header("Legacy Background")]
        public GameObject backgroundPrefab;
        public Vector3 localPosition;
        public Vector3 localEulerAngles;
        public Vector3 localScale = Vector3.one;

        public bool Matches(string circuitName)
        {
            return !string.IsNullOrWhiteSpace(circuit) &&
                !string.IsNullOrWhiteSpace(circuitName) &&
                string.Equals(
                    circuit.Trim(),
                    circuitName.Trim(),
                    System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
