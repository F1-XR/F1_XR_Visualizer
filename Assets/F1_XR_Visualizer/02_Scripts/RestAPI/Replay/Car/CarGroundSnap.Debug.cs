using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public partial class CarGroundSnap
    {
        private static void DrawGroundSnapRay(Vector3 origin, Vector3 hitPoint, Color color)
        {
            if (!DebugGroundSnap)
                return;

            Debug.DrawLine(origin, hitPoint, color, 0f, false);
        }

        private void LogGroundSnapAccepted(int driverNumber, GroundHit hit, int hitCount)
        {
            if (!DebugGroundSnap)
                return;

            bool changedCollider = !lastGroundSnapColliders.TryGetValue(driverNumber, out Collider previous) || previous != hit.collider;
            if (!changedCollider && !ShouldLogGroundSnap(driverNumber))
                return;

            lastGroundSnapColliders[driverNumber] = hit.collider;
            Debug.Log(
                $"[GroundSnap] driver={driverNumber}, hits={hitCount}, collider={ColliderName(hit.collider)}, " +
                $"height={hit.point.y:0.000}, offset={hit.verticalOffset:0.000}, changedCollider={changedCollider}"
            );
        }

        private void LogGroundSnapReject(int driverNumber, string reason, Collider collider, float value)
        {
            if (!DebugGroundSnap || !ShouldLogGroundSnap(driverNumber))
                return;

            Debug.Log(
                $"[GroundSnap] reject driver={driverNumber}, reason={reason}, " +
                $"collider={ColliderName(collider)}, value={value:0.000}"
            );
        }

        private bool ShouldLogGroundSnap(int driverNumber)
        {
            float now = Time.unscaledTime;
            if (nextGroundSnapDebugLogTimes.TryGetValue(driverNumber, out float nextTime) && now < nextTime)
                return false;

            nextGroundSnapDebugLogTimes[driverNumber] = now + GroundSnapDebugLogInterval;
            return true;
        }

        private static string ColliderName(Collider collider)
        {
            return collider != null ? collider.name : "<none>";
        }
    }
}
