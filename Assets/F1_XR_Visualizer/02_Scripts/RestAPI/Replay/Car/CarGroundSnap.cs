using System.Collections.Generic;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public class CarGroundSnap
    {
        private static readonly bool DebugGroundSnap = false;
        private const float GroundProbeHeight = 100f;
        private const float GroundProbeDistance = 220f;
        private const float MinGroundOffset = 0.005f;
        private const float MaxTiltDegrees = 35f;
        private const float MinTrackSurfaceNormalDot = 0.35f;
        private const float SurfaceHeightChangeSeconds = 0.3f;
        private const float SurfaceHeightChangeBodyRatio = 1f;
        private const float SurfaceHeightSpreadBodyRatio = 1f;
        private const float SurfaceProbeBodyRatio = 1.5f;
        private const float GroundOffsetBodyRatio = 0.75f;
        private const float PositionSnapLerp = 0.45f;
        private const float RotationSnapLerp = 0.35f;
        private const float GroundSnapDebugLogInterval = 0.5f;

        private readonly Dictionary<int, Vector3> snappedPositions = new();
        private readonly Dictionary<int, Quaternion> snappedRotations = new();
        private readonly HashSet<Transform> colliderReadyRoots = new();
        private readonly HashSet<Collider> trackSurfaceColliders = new();
        private readonly Dictionary<int, Collider> lastGroundSnapColliders = new();
        private readonly Dictionary<int, float> nextGroundSnapDebugLogTimes = new();
        private readonly Dictionary<int, int> groundSnapMissCounts = new();
        private Transform trackSurfaceRoot;

        private struct GroundHit
        {
            public Vector3 point;
            public Vector3 normal;
            public Collider collider;
            public float verticalOffset;

            public GroundHit(Vector3 point, Vector3 normal, Collider collider, float verticalOffset)
            {
                this.point = point;
                this.normal = normal;
                this.collider = collider;
                this.verticalOffset = verticalOffset;
            }
        }

        public void ResolvePose(
            ReplayCarView car,
            Transform surfaceRoot,
            bool hasDirection,
            Quaternion trackRotation,
            Quaternion baseRotation,
            ref Vector3 position,
            ref Quaternion rotation)
        {
            EnsureTrackSurfaceColliders(surfaceRoot);

            if (!hasDirection)
            {
                ClearPose(car.driverNumber);
                return;
            }

            Vector3 up = trackRotation * Vector3.up;

            if (TrySnapToTrackSurface(
                car,
                position,
                trackRotation,
                baseRotation,
                out Vector3 snappedPosition,
                out Quaternion snappedRotation,
                out float bodyHeight))
            {
                groundSnapMissCounts.Remove(car.driverNumber);
                SmoothSnap(
                    car.driverNumber,
                    snappedPosition,
                    snappedRotation,
                    up,
                    bodyHeight,
                    out position,
                    out rotation);
            }
            else if (TryHoldPreviousSnap(
                car.driverNumber,
                position,
                rotation,
                up,
                out Vector3 heldPosition,
                out Quaternion heldRotation))
            {
                position = heldPosition;
                rotation = heldRotation;
            }
            else
            {
                ClearPose(car.driverNumber);
            }
        }

        public void ClearPose(int driverNumber)
        {
            snappedPositions.Remove(driverNumber);
            snappedRotations.Remove(driverNumber);
            groundSnapMissCounts.Remove(driverNumber);
        }

        public void RemoveCar(int driverNumber)
        {
            ClearPose(driverNumber);
            lastGroundSnapColliders.Remove(driverNumber);
            nextGroundSnapDebugLogTimes.Remove(driverNumber);
        }

        public void ClearSurfaceCache()
        {
            colliderReadyRoots.Clear();
            trackSurfaceColliders.Clear();
            trackSurfaceRoot = null;
        }

        public void ResetForCalibration()
        {
            ClearSurfaceCache();
            lastGroundSnapColliders.Clear();
            nextGroundSnapDebugLogTimes.Clear();
            groundSnapMissCounts.Clear();
        }

        public void Clear()
        {
            snappedPositions.Clear();
            snappedRotations.Clear();
            ResetForCalibration();
        }

        private void SmoothSnap(
            int driverNumber,
            Vector3 snappedPosition,
            Quaternion snappedRotation,
            Vector3 up,
            float bodyHeight,
            out Vector3 position,
            out Quaternion rotation)
        {
            if (snappedPositions.TryGetValue(driverNumber, out Vector3 previousPosition))
            {
                position = Vector3.Lerp(previousPosition, snappedPosition, PositionSnapLerp);
                position = ClampSurfaceHeightChange(previousPosition, position, up, bodyHeight);
                rotation = snappedRotations.TryGetValue(driverNumber, out Quaternion oldRotation)
                    ? Quaternion.Slerp(oldRotation, snappedRotation, RotationSnapLerp)
                    : snappedRotation;
            }
            else
            {
                position = snappedPosition;
                rotation = snappedRotation;
            }

            snappedPositions[driverNumber] = position;
            snappedRotations[driverNumber] = rotation;
        }

        private static Vector3 ClampSurfaceHeightChange(Vector3 previousPosition, Vector3 targetPosition, Vector3 up, float bodyHeight)
        {
            float heightDelta = Vector3.Dot(targetPosition - previousPosition, up);
            float maxStep = GetMaxSurfaceHeightStep(bodyHeight);
            float clampedDelta = Mathf.Clamp(heightDelta, -maxStep, maxStep);
            return targetPosition + up * (clampedDelta - heightDelta);
        }

        private static float GetMaxSurfaceHeightStep(float bodyHeight)
        {
            float deltaTime = Time.deltaTime > 0f ? Time.deltaTime : Time.unscaledDeltaTime;
            float height = Mathf.Max(MinGroundOffset, bodyHeight * SurfaceHeightChangeBodyRatio);
            return height * Mathf.Clamp01(deltaTime / SurfaceHeightChangeSeconds);
        }

        private static float GetMaxSurfaceHeightSpread(float bodyHeight)
        {
            return Mathf.Max(MinGroundOffset, bodyHeight * SurfaceHeightSpreadBodyRatio);
        }

        private static float GetMaxSurfaceProbeOffset(float bodyHeight)
        {
            return Mathf.Max(MinGroundOffset * 2f, bodyHeight * SurfaceProbeBodyRatio);
        }

        private bool TryHoldPreviousSnap(
            int driverNumber,
            Vector3 fallbackPosition,
            Quaternion fallbackRotation,
            Vector3 up,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = fallbackPosition;
            rotation = fallbackRotation;

            if (!snappedPositions.TryGetValue(driverNumber, out Vector3 previousPosition))
                return false;

            int misses = groundSnapMissCounts.TryGetValue(driverNumber, out int oldMisses)
                ? oldMisses + 1
                : 1;
            groundSnapMissCounts[driverNumber] = misses;

            float heightDelta = Vector3.Dot(previousPosition - fallbackPosition, up);
            position = fallbackPosition + up * heightDelta;
            rotation = snappedRotations.TryGetValue(driverNumber, out Quaternion previousRotation)
                ? Quaternion.Slerp(previousRotation, fallbackRotation, RotationSnapLerp)
                : fallbackRotation;

            snappedPositions[driverNumber] = position;
            snappedRotations[driverNumber] = rotation;
            return true;
        }

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
            float originUp = Vector3.Dot(car.transform.position, up);

            foreach (Renderer item in renderers)
            {
                if (!IsCarBodyRenderer(item))
                    continue;

                Bounds bounds = item.bounds;
                Vector3[] corners = GetBoundsCorners(bounds);
                foreach (Vector3 corner in corners)
                {
                    Vector3 offset = corner - car.transform.position;
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

        private void EnsureTrackSurfaceColliders(Transform root)
        {
            if (root == null)
            {
                ClearSurfaceCache();
                return;
            }

            if (trackSurfaceRoot != root)
            {
                ClearSurfaceCache();
                trackSurfaceRoot = root;
            }

            PruneTrackSurfaceColliders(root);

            if (colliderReadyRoots.Contains(root))
                return;

            MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
            foreach (MeshFilter meshFilter in meshFilters)
            {
                if (meshFilter.sharedMesh == null)
                    continue;

                if (meshFilter.GetComponentInParent<ReplayCarView>() != null)
                    continue;

                MeshRenderer renderer = meshFilter.GetComponent<MeshRenderer>();
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;

                MeshCollider existingMeshCollider = meshFilter.GetComponent<MeshCollider>();
                if (existingMeshCollider != null)
                {
                    if (existingMeshCollider.enabled)
                        trackSurfaceColliders.Add(existingMeshCollider);

                    continue;
                }

                if (meshFilter.GetComponent<Collider>() != null)
                    continue;

                MeshCollider collider = meshFilter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = meshFilter.sharedMesh;
                trackSurfaceColliders.Add(collider);
            }

            colliderReadyRoots.Add(root);
        }

        private void PruneTrackSurfaceColliders(Transform root)
        {
            trackSurfaceColliders.RemoveWhere(collider =>
                collider == null ||
                root == null ||
                !collider.transform.IsChildOf(root));
        }

        private bool IsTrackSurfaceCollider(Collider collider)
        {
            if (collider == null)
                return false;

            if (trackSurfaceRoot == null)
                return true;

            if (!collider.transform.IsChildOf(trackSurfaceRoot))
                return false;

            if (!trackSurfaceColliders.Contains(collider))
                return false;

            return true;
        }

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
