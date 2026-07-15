using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public partial class CarGroundSnap
    {
        private bool TrySnapToTrackSurface(
            ReplayCarView car,
            Vector3 worldPosition,
            Quaternion trackRotation,
            Quaternion baseRotation,
            out Vector3 snappedPosition,
            out Quaternion snappedRotation,
            out float bodyHeight)
        {
            snappedPosition = worldPosition;
            snappedRotation = trackRotation * baseRotation;
            bodyHeight = MinGroundOffset;

            Vector3 up = trackRotation * Vector3.up;
            Vector3 forward = Vector3.ProjectOnPlane(trackRotation * Vector3.forward, up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(trackRotation * Vector3.right, up).normalized;

            if (forward.sqrMagnitude <= 0.000001f || right.sqrMagnitude <= 0.000001f)
                return false;

            GetCarFootprint(car, forward, right, out float halfLength, out float halfWidth, out float groundOffset, out bodyHeight);

            Vector3 frontLeft = worldPosition + forward * halfLength - right * halfWidth;
            Vector3 frontRight = worldPosition + forward * halfLength + right * halfWidth;
            Vector3 rearLeft = worldPosition - forward * halfLength - right * halfWidth;
            Vector3 rearRight = worldPosition - forward * halfLength + right * halfWidth;
            float maxSurfaceOffset = GetMaxSurfaceProbeOffset(bodyHeight);

            bool hasFrontLeft = TryRaycastTrack(car, car.driverNumber, frontLeft, up, maxSurfaceOffset, out GroundHit hitFrontLeft);
            bool hasFrontRight = TryRaycastTrack(car, car.driverNumber, frontRight, up, maxSurfaceOffset, out GroundHit hitFrontRight);
            bool hasRearLeft = TryRaycastTrack(car, car.driverNumber, rearLeft, up, maxSurfaceOffset, out GroundHit hitRearLeft);
            bool hasRearRight = TryRaycastTrack(car, car.driverNumber, rearRight, up, maxSurfaceOffset, out GroundHit hitRearRight);
            int hitCount = CountGroundHits(hasFrontLeft, hasFrontRight, hasRearLeft, hasRearRight);

            if (hitCount < 2)
            {
                LogGroundSnapReject(car.driverNumber, "too few surface hits", null, 0f);
                return false;
            }

            if (GetHitHeightSpread(up, hasFrontLeft, hitFrontLeft, hasFrontRight, hitFrontRight, hasRearLeft, hitRearLeft, hasRearRight, hitRearRight) > GetMaxSurfaceHeightSpread(bodyHeight))
            {
                LogGroundSnapReject(car.driverNumber, "mixed surface heights", FirstGroundHitCollider(hasFrontLeft, hitFrontLeft, hasFrontRight, hitFrontRight, hasRearLeft, hitRearLeft, hasRearRight, hitRearRight), 0f);
                return false;
            }

            if (!TryGetSurfaceNormal(up, hasFrontLeft, hitFrontLeft, hasFrontRight, hitFrontRight, hasRearLeft, hitRearLeft, hasRearRight, hitRearRight, out Vector3 normal))
            {
                normal = up;
            }
            else if (Vector3.Angle(up, normal) > MaxTiltDegrees)
            {
                return false;
            }

            Vector3 projectedForward = Vector3.ProjectOnPlane(forward, normal);
            if (projectedForward.sqrMagnitude <= 0.000001f)
                return false;

            Vector3 hitCenter = AverageGroundHitPoint(hasFrontLeft, hitFrontLeft, hasFrontRight, hitFrontRight, hasRearLeft, hitRearLeft, hasRearRight, hitRearRight, hitCount);
            snappedPosition = hitCenter + normal * Mathf.Max(MinGroundOffset, groundOffset);
            snappedRotation = Quaternion.LookRotation(projectedForward.normalized, normal) * baseRotation;
            LogGroundSnapAccepted(car.driverNumber, FirstGroundHit(hasFrontLeft, hitFrontLeft, hasFrontRight, hitFrontRight, hasRearLeft, hitRearLeft, hasRearRight, hitRearRight), hitCount);
            return true;
        }

        private bool TryRaycastTrack(ReplayCarView car, int driverNumber, Vector3 origin, Vector3 up, float maxSurfaceOffset, out GroundHit groundHit)
        {
            groundHit = default;

            Vector3 rayOrigin = origin + up * GroundProbeHeight;
            RaycastHit[] hits = Physics.RaycastAll(rayOrigin, -up, GroundProbeDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float bestOffset = float.MaxValue;
            RaycastHit? bestHit = null;

            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null || IsIgnoredGroundHit(car, hit.collider))
                    continue;

                if (!(hit.collider is MeshCollider))
                    continue;

                if (!IsTrackSurfaceCollider(hit.collider))
                {
                    DrawGroundSnapRay(origin, hit.point, Color.red);
                    LogGroundSnapReject(driverNumber, "unregistered collider", hit.collider, 0f);
                    continue;
                }

                if (Vector3.Dot(hit.normal, up) < MinTrackSurfaceNormalDot)
                {
                    DrawGroundSnapRay(origin, hit.point, Color.red);
                    LogGroundSnapReject(driverNumber, "surface normal rejected", hit.collider, Vector3.Dot(hit.normal, up));
                    continue;
                }

                float offset = Mathf.Abs(Vector3.Dot(hit.point - origin, up));
                if (offset > maxSurfaceOffset)
                {
                    DrawGroundSnapRay(origin, hit.point, Color.red);
                    LogGroundSnapReject(driverNumber, "surface offset rejected", hit.collider, offset);
                    continue;
                }

                if (offset < bestOffset)
                {
                    bestOffset = offset;
                    bestHit = hit;
                }
            }

            if (!bestHit.HasValue)
            {
                DrawGroundSnapRay(origin, origin - up * maxSurfaceOffset, Color.red);
                LogGroundSnapReject(driverNumber, "no valid surface", null, 0f);
                return false;
            }

            RaycastHit selected = bestHit.Value;
            groundHit = new GroundHit(selected.point, selected.normal, selected.collider, bestOffset);
            DrawGroundSnapRay(origin, selected.point, Color.green);
            return true;
        }

        private static int CountGroundHits(bool a, bool b, bool c, bool d)
        {
            int count = 0;
            if (a)
                count++;
            if (b)
                count++;
            if (c)
                count++;
            if (d)
                count++;
            return count;
        }

        private static float GetHitHeightSpread(
            Vector3 up,
            bool hasA,
            GroundHit a,
            bool hasB,
            GroundHit b,
            bool hasC,
            GroundHit c,
            bool hasD,
            GroundHit d)
        {
            float min = 0f;
            float max = 0f;
            bool found = false;

            IncludeGroundHitHeight(up, hasA, a, ref min, ref max, ref found);
            IncludeGroundHitHeight(up, hasB, b, ref min, ref max, ref found);
            IncludeGroundHitHeight(up, hasC, c, ref min, ref max, ref found);
            IncludeGroundHitHeight(up, hasD, d, ref min, ref max, ref found);

            return found ? max - min : 0f;
        }

        private static void IncludeGroundHitHeight(Vector3 up, bool hasHit, GroundHit hit, ref float min, ref float max, ref bool found)
        {
            if (!hasHit)
                return;

            float height = Vector3.Dot(hit.point, up);
            if (!found)
            {
                min = max = height;
                found = true;
                return;
            }

            min = Mathf.Min(min, height);
            max = Mathf.Max(max, height);
        }

        private static Vector3 AverageGroundHitPoint(
            bool hasA,
            GroundHit a,
            bool hasB,
            GroundHit b,
            bool hasC,
            GroundHit c,
            bool hasD,
            GroundHit d,
            int hitCount)
        {
            Vector3 sum = Vector3.zero;

            if (hasA)
                sum += a.point;
            if (hasB)
                sum += b.point;
            if (hasC)
                sum += c.point;
            if (hasD)
                sum += d.point;

            return sum / Mathf.Max(1, hitCount);
        }

        private static GroundHit FirstGroundHit(
            bool hasA,
            GroundHit a,
            bool hasB,
            GroundHit b,
            bool hasC,
            GroundHit c,
            bool hasD,
            GroundHit d)
        {
            if (hasA)
                return a;
            if (hasB)
                return b;
            if (hasC)
                return c;
            return d;
        }

        private static Collider FirstGroundHitCollider(
            bool hasA,
            GroundHit a,
            bool hasB,
            GroundHit b,
            bool hasC,
            GroundHit c,
            bool hasD,
            GroundHit d)
        {
            return FirstGroundHit(hasA, a, hasB, b, hasC, c, hasD, d).collider;
        }

        private static bool TryGetSurfaceNormal(
            Vector3 up,
            bool hasA,
            GroundHit a,
            bool hasB,
            GroundHit b,
            bool hasC,
            GroundHit c,
            bool hasD,
            GroundHit d,
            out Vector3 normal)
        {
            normal = up;

            if (hasA && hasB && hasC && TryBuildSurfaceNormal(up, a.point, b.point, c.point, out normal))
                return true;
            if (hasA && hasB && hasD && TryBuildSurfaceNormal(up, a.point, b.point, d.point, out normal))
                return true;
            if (hasA && hasC && hasD && TryBuildSurfaceNormal(up, a.point, c.point, d.point, out normal))
                return true;
            if (hasB && hasC && hasD && TryBuildSurfaceNormal(up, b.point, c.point, d.point, out normal))
                return true;

            return false;
        }

        private static bool TryBuildSurfaceNormal(Vector3 up, Vector3 a, Vector3 b, Vector3 c, out Vector3 normal)
        {
            normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude <= 0.000001f)
                return false;

            normal.Normalize();
            if (Vector3.Dot(normal, up) < 0f)
                normal = -normal;

            return true;
        }

        private static bool IsIgnoredGroundHit(ReplayCarView car, Collider collider)
        {
            if (collider.transform.IsChildOf(car.transform))
                return true;

            ReplayCarView hitCar = collider.GetComponentInParent<ReplayCarView>();
            return hitCar != null;
        }
    }
}
