using System.Collections.Generic;
using UnityEngine;

namespace F1XR.Showroom
{
    public sealed class CarWheelSpin : MonoBehaviour
    {
        [SerializeField] Transform[] wheels;
        [SerializeField] float wheelRadius = 0.35f;
        [SerializeField] Vector3 rotationAxis = Vector3.right;
        [SerializeField] float movementThreshold = 0.0005f;

        Vector3 lastPosition;
        readonly Dictionary<Transform, Vector3> wheelLocalPivots = new();

        void Start()
        {
            lastPosition = transform.position;

            foreach (Transform wheel in wheels)
            {
                if (wheel != null)
                    RegisterWheel(wheel);
            }
        }

        public void RegisterWheel(Transform wheel)
        {
            if (wheel == null || wheel.parent == null || wheelLocalPivots.ContainsKey(wheel))
                return;

            Bounds bounds = default;
            bool hasBounds = false;
            foreach (Renderer r in wheel.GetComponentsInChildren<Renderer>(true))
            {
                if (!hasBounds)
                {
                    bounds = r.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }

            Vector3 localPivot = hasBounds
                ? wheel.parent.InverseTransformPoint(bounds.center)
                : wheel.parent.InverseTransformPoint(wheel.position);

            wheelLocalPivots[wheel] = localPivot;
        }

        public void UnregisterWheel(Transform wheel)
        {
            if (wheel != null)
                wheelLocalPivots.Remove(wheel);
        }

        void LateUpdate()
        {
            Vector3 currentPosition = transform.position;
            float signedDistance = Vector3.Dot(currentPosition - lastPosition, transform.forward);
            lastPosition = currentPosition;

            if (Mathf.Abs(signedDistance) < movementThreshold)
                return;

            float angle = (signedDistance / wheelRadius) * Mathf.Rad2Deg;

            foreach (KeyValuePair<Transform, Vector3> entry in wheelLocalPivots)
            {
                Transform wheel = entry.Key;
                if (wheel == null || wheel.parent == null)
                    continue;

                Vector3 worldPivot = wheel.parent.TransformPoint(entry.Value);
                Vector3 worldAxis = wheel.TransformDirection(rotationAxis);
                wheel.RotateAround(worldPivot, worldAxis, angle);
            }
        }
    }
}
