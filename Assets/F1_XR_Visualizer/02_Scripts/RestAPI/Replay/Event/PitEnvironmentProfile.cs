using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    [CreateAssetMenu(
        fileName = "PitEnvironmentProfile",
        menuName = "F1 XR/Replay/Pit Environment Profile")]
    public sealed class PitEnvironmentProfile : ScriptableObject
    {
        public string circuit;
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
