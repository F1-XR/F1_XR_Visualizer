using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public partial class CarGroundSnap
    {
        private void GetCarFootprint(
            ReplayCarView car,
            Vector3 forward,
            Vector3 right,
            out float halfLength,
            out float halfWidth,
            out float groundOffset,
            out float bodyHeight)
        {
            halfLength = 0.02f;
            halfWidth = 0.01f;
            groundOffset = MinGroundOffset;
            bodyHeight = MinGroundOffset;

            Renderer[] renderers = car.GetComponentsInChildren<Renderer>();
            bool found = false;
            float minForward = 0f;
            float maxForward = 0f;
            float minRight = 0f;
            float maxRight = 0f;
            float minUp = 0f;
            float maxUp = 0f;
            Vector3 up = Vector3.Cross(forward, right).normalized;
            float originUp = Vector3.Dot(car.LogicalRoot.position, up);

            foreach (Renderer item in renderers)
            {
                if (!IsCarBodyRenderer(item))
                    continue;

                Bounds bounds = item.bounds;
                Vector3[] corners = GetBoundsCorners(bounds);
                foreach (Vector3 corner in corners)
                {
                    Vector3 offset = corner - car.LogicalRoot.position;
                    float forwardValue = Vector3.Dot(offset, forward);
                    float rightValue = Vector3.Dot(offset, right);
                    float upValue = Vector3.Dot(corner, up) - originUp;

                    if (!found)
                    {
                        minForward = maxForward = forwardValue;
                        minRight = maxRight = rightValue;
                        minUp = maxUp = upValue;
                        found = true;
                    }
                    else
                    {
                        minForward = Mathf.Min(minForward, forwardValue);
                        maxForward = Mathf.Max(maxForward, forwardValue);
                        minRight = Mathf.Min(minRight, rightValue);
                        maxRight = Mathf.Max(maxRight, rightValue);
                        minUp = Mathf.Min(minUp, upValue);
                        maxUp = Mathf.Max(maxUp, upValue);
                    }
                }
            }

            if (!found)
                return;

            halfLength = Mathf.Max(halfLength, (maxForward - minForward) * 0.35f);
            halfWidth = Mathf.Max(halfWidth, (maxRight - minRight) * 0.35f);
            bodyHeight = Mathf.Max(MinGroundOffset, maxUp - minUp);
            groundOffset = Mathf.Clamp(
                -minUp + MinGroundOffset,
                MinGroundOffset,
                Mathf.Max(MinGroundOffset, bodyHeight * GroundOffsetBodyRatio)
            );
        }

        private static Vector3[] GetBoundsCorners(Bounds bounds)
        {
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            return new[]
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(min.x, min.y, max.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(min.x, max.y, max.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(max.x, max.y, max.z)
            };
        }

        private static bool IsCarBodyRenderer(Renderer renderer)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                return false;

            if (renderer is LineRenderer || renderer.GetComponent<TextMesh>() != null)
                return false;

            if (renderer.GetComponent<MeshFilter>() == null)
                return false;

            Transform current = renderer.transform;
            while (current != null)
            {
                string objectName = current.name;
                if (objectName.StartsWith("DriverLabel") ||
                    objectName.StartsWith("SelectionFx") ||
                    objectName.StartsWith("GroundRing") ||
                    objectName.StartsWith("SelectionPulse") ||
                    objectName.StartsWith("SelectedCar"))
                {
                    return false;
                }

                if (current.GetComponent<ReplayCarView>() != null)
                    break;

                current = current.parent;
            }

            return true;
        }
    }
}
