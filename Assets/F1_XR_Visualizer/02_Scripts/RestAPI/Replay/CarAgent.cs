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
        
        public void SetColor(Color color)
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>();

            foreach (Renderer item in renderers)
            {
                MaterialPropertyBlock block = new();
                item.GetPropertyBlock(block);
                block.SetColor("_BaseColor", color);
                block.SetColor("_Color", color);
                item.SetPropertyBlock(block);
            }
        }
    }
}