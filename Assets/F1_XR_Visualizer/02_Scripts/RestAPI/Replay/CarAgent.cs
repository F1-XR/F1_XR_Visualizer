using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public class CarAgent : MonoBehaviour
    {
        public int driverNumber;
        public Vector3 rawPosition;

        public void Init(int number)
        {
            driverNumber = number;
            name = $"Car_{number}";
        }

        public void SetPosition(Vector3 position)
        {
            rawPosition = position;
            transform.position = position;
        }
    }
}