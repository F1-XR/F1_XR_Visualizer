using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace F1XR.RestAPI.Replay.Track.Build
{
    public sealed partial class TrackRevealPlacer
    {
        public void ConfirmPlacement()
        {
            if (!placementActive ||
                !hasCurrentHit ||
                !IsPlacementVisualReady())
                return;

            if (spawnedInstance != null && !allowReplaceExisting)
                return;

            if (allowReplaceExisting)
                ClearSpawned();

            GameObject target;

            if (previewInstance != null)
            {
                target = previewInstance;
                previewInstance = null;

                target.name = placementPrefab.name;
                target.SetActive(true);

                RestorePreviewForRealUse(target);
            }
            else
            {
                target = Instantiate(placementPrefab);
                target.name = placementPrefab.name;
                ApplyTrackMap(target);
                ConfigurePhysics(target);
            }

            Quaternion rotation = CalculatePlacementRotation(target);
            target.transform.localScale = CalculatePlacementScale(target);
            Vector3 surfaceCenter = CalculatePlacementCenter();
            Vector3 position = surfaceCenter + Vector3.up * CalculateSurfaceYOffset(target);
            target.transform.SetPositionAndRotation(position, rotation);

            bool canCreateAnchor = !hasAutomaticSurface ||
                currentAutomaticSurface.CanCreateAnchor;
            currentAnchor = canCreateAnchor
                ? CreateAnchor(new Pose(surfaceCenter, rotation), currentPlane)
                : null;

            if (currentAnchor != null)
            {
                if (stabilizeAnchor)
                {
                    TrackAnchorStabilizer stabilizer = target.GetComponent<TrackAnchorStabilizer>();
                    if (stabilizer == null)
                        stabilizer = target.AddComponent<TrackAnchorStabilizer>();

                    stabilizer.Configure(
                        currentAnchor.transform,
                        anchorPositionSpeed,
                        anchorRotationSpeed,
                        anchorPositionDeadZone,
                        anchorRotationDeadZone);
                }
                else
                {
                    target.transform.SetParent(currentAnchor.transform, worldPositionStays: true);
                }
            }

            FinishPlacement(target);
        }

        public bool TryPlaceFixed()
        {
            if (placementMode != TrackPlacementMode.Fixed ||
                spawnedInstance != null ||
                !IsPlacementVisualReady())
            {
                return false;
            }

            ClearPreview();

            GameObject target = Instantiate(placementPrefab);
            target.name = placementPrefab.name;
            ApplyTrackMap(target);
            ConfigurePhysics(target);

            Vector3 scale = fixedScale;
            if (scale.x <= 0f || scale.y <= 0f || scale.z <= 0f)
                scale = Vector3.one;

            target.transform.SetPositionAndRotation(
                fixedPosition,
                Quaternion.Euler(fixedEulerAngles));
            target.transform.localScale = scale;

            FinishPlacement(target);
            return true;
        }

        void FinishPlacement(GameObject target)
        {
            spawnedInstance = target;
            placementActive = false;
            ConfigureEditState(spawnedInstance);
            ObserveCurrentTransform();
            SavePlacement();

            Transform carsRoot = CarsTransform;
            if (carsRoot != null)
                carsRoot.gameObject.SetActive(false);

            TrackRevealView revealView =
                spawnedInstance.GetComponent<TrackRevealView>();

            if (revealView == null)
                revealView = spawnedInstance.AddComponent<TrackRevealView>();

            revealView.Configure(
                buildDuration,
                buildEdgeWidth,
                buildEdgeColor,
                restoreMaterialsAfterBuild);

            revealView.Completed -= ShowCarsAfterReveal;
            revealView.Completed += ShowCarsAfterReveal;
            revealView.Play();
        }

        void ShowCarsAfterReveal()
        {
            Transform carsRoot = CarsTransform;
            if (carsRoot != null)
                carsRoot.gameObject.SetActive(true);
        }

        Quaternion CalculatePlacementRotation(GameObject target)
        {
            Quaternion fallback = useHitRotation ? currentPose.rotation : Quaternion.identity;
            if (placementMode == TrackPlacementMode.Free)
            {
                if (!hasPlacementFallbackRotation)
                {
                    placementFallbackRotation = fallback;
                    hasPlacementFallbackRotation = true;
                }

                return placementFallbackRotation;
            }

            if (!useHitRotation || !alignLongAxisToSurface || target == null)
                return fallback;

            if (currentPlane == null)
            {
                return hasAutomaticSurface &&
                    TryGetSurfaceLongAxis(
                        out Vector3 managedLongAxis,
                        out float managedAspectRatio) &&
                    managedAspectRatio >= minimumSurfaceAspectRatio
                        ? CalculateSurfaceAlignedRotation(
                            target,
                            managedLongAxis,
                            fallback)
                        : fallback;
            }

            if (!hasPlacementFallbackRotation)
            {
                placementFallbackRotation = fallback;
                hasPlacementFallbackRotation = true;
            }

            if (hasLockedPlacementRotation)
            {
                bool samePlane = lockedRotationPlane == currentPlane;
                Vector3 surfaceCenter = currentPlane.center;
                bool sameSurface = samePlane ||
                    (Vector2.Distance(
                        new Vector2(surfaceCenter.x, surfaceCenter.z),
                        new Vector2(lockedSurfaceCenter.x, lockedSurfaceCenter.z)) <= surfaceSwitchDistance &&
                    Mathf.Abs(surfaceCenter.y - lockedSurfaceCenter.y) <= surfaceSwitchHeight);
                if (sameSurface)
                    return lockedPlacementRotation;

                ResetPlacementRotation();
                placementFallbackRotation = target.transform.rotation;
                hasPlacementFallbackRotation = true;
            }

            if (!TryGetSurfaceLongAxis(currentPlane, out Vector3 surfaceLongAxis, out float aspectRatio) ||
                aspectRatio < minimumSurfaceAspectRatio)
            {
                hasRotationCandidate = false;
                return placementFallbackRotation;
            }

            if (!TryCalculateLocalBounds(target, out Bounds trackBounds))
                return placementFallbackRotation;

            bool trackLongAxisIsX = trackBounds.size.x >= trackBounds.size.z;
            Vector3 fallbackLongAxis = placementFallbackRotation *
                (trackLongAxisIsX ? Vector3.right : Vector3.forward);
            fallbackLongAxis = Vector3.ProjectOnPlane(fallbackLongAxis, Vector3.up);
            if (Vector3.Dot(surfaceLongAxis, fallbackLongAxis) < 0f)
                surfaceLongAxis = -surfaceLongAxis;

            Quaternion candidate = trackLongAxisIsX
                ? Quaternion.LookRotation(Vector3.Cross(surfaceLongAxis, Vector3.up), Vector3.up)
                : Quaternion.LookRotation(surfaceLongAxis, Vector3.up);

            bool candidateChanged = !hasRotationCandidate ||
                Quaternion.Angle(rotationCandidate, candidate) > surfaceRotationStabilityAngle;
            if (candidateChanged)
            {
                rotationCandidatePlane = currentPlane;
                rotationCandidate = candidate;
                rotationCandidateSince = Time.unscaledTime;
                hasRotationCandidate = true;
                return placementFallbackRotation;
            }

            rotationCandidate = candidate;
            if (Time.unscaledTime - rotationCandidateSince < surfaceRotationLockDelay)
                return placementFallbackRotation;

            lockedRotationPlane = currentPlane;
            lockedPlacementRotation = rotationCandidate;
            hasLockedPlacementRotation = true;
            lockedSurfaceCenter = currentPlane.center;
            return lockedPlacementRotation;
        }

        static bool TryGetSurfaceLongAxis(ARPlane plane, out Vector3 axis, out float aspectRatio)
        {
            axis = default;
            aspectRatio = 1f;
            if (plane == null)
                return false;

            var boundary = plane.boundary;
            if (boundary.IsCreated && boundary.Length >= 3)
            {
                Vector2 center = Vector2.zero;
                for (int i = 0; i < boundary.Length; i++)
                    center += boundary[i];
                center /= boundary.Length;

                float xx = 0f;
                float yy = 0f;
                float xy = 0f;
                for (int i = 0; i < boundary.Length; i++)
                {
                    Vector2 delta = boundary[i] - center;
                    xx += delta.x * delta.x;
                    yy += delta.y * delta.y;
                    xy += delta.x * delta.y;
                }

                float trace = xx + yy;
                float spread = Mathf.Sqrt((xx - yy) * (xx - yy) + 4f * xy * xy);
                float major = (trace + spread) * 0.5f;
                float minor = (trace - spread) * 0.5f;
                if (major > 0.001f)
                {
                    aspectRatio = minor > 0.001f
                        ? Mathf.Sqrt(major / minor)
                        : float.PositiveInfinity;
                    float angle = 0.5f * Mathf.Atan2(2f * xy, xx - yy);
                    Vector3 localAxis = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    axis = plane.transform.TransformDirection(localAxis);
                }
            }

            if (axis.sqrMagnitude <= 0.001f)
            {
                Vector2 size = plane.size;
                float shortSide = Mathf.Min(size.x, size.y);
                float longSide = Mathf.Max(size.x, size.y);
                if (shortSide <= 0.001f)
                    return false;

                aspectRatio = longSide / shortSide;
                axis = size.x >= size.y ? plane.transform.right : plane.transform.forward;
            }

            axis = Vector3.ProjectOnPlane(axis, Vector3.up);
            if (axis.sqrMagnitude <= 0.001f)
                return false;

            axis.Normalize();
            return true;
        }

        bool TryGetSurfaceLongAxis(
            out Vector3 axis,
            out float aspectRatio)
        {
            if (currentPlane != null)
                return TryGetSurfaceLongAxis(currentPlane, out axis, out aspectRatio);

            axis = default;
            aspectRatio = 1f;
            if (!hasAutomaticSurface || !currentAutomaticSurface.HasBounds)
                return false;

            float shortSide = Mathf.Min(
                currentAutomaticSurface.Size.x,
                currentAutomaticSurface.Size.y);
            float longSide = Mathf.Max(
                currentAutomaticSurface.Size.x,
                currentAutomaticSurface.Size.y);
            if (shortSide <= 0.001f)
                return false;

            axis = Vector3.ProjectOnPlane(
                currentAutomaticSurface.LongAxis,
                Vector3.up);
            if (axis.sqrMagnitude <= 0.001f)
                return false;

            axis.Normalize();
            aspectRatio = longSide / shortSide;
            return true;
        }

        static Quaternion CalculateSurfaceAlignedRotation(
            GameObject target,
            Vector3 surfaceLongAxis,
            Quaternion fallback)
        {
            if (!TryCalculateLocalBounds(target, out Bounds trackBounds))
                return fallback;

            bool trackLongAxisIsX = trackBounds.size.x >= trackBounds.size.z;
            Vector3 fallbackLongAxis = fallback *
                (trackLongAxisIsX ? Vector3.right : Vector3.forward);
            fallbackLongAxis = Vector3.ProjectOnPlane(
                fallbackLongAxis,
                Vector3.up);
            if (Vector3.Dot(surfaceLongAxis, fallbackLongAxis) < 0f)
                surfaceLongAxis = -surfaceLongAxis;

            return trackLongAxisIsX
                ? Quaternion.LookRotation(
                    Vector3.Cross(surfaceLongAxis, Vector3.up),
                    Vector3.up)
                : Quaternion.LookRotation(surfaceLongAxis, Vector3.up);
        }

        Vector3 CalculatePlacementCenter()
        {
            if (placementMode == TrackPlacementMode.Free || !centerOnSurface)
                return currentPose.position;

            return currentPlane != null
                ? currentPlane.center
                : currentPose.position;
        }

        Vector3 CalculatePlacementScale(GameObject target)
        {
            if (placementMode == TrackPlacementMode.Free)
            {
                return placementPrefab != null
                    ? placementPrefab.transform.localScale
                    : target != null ? target.transform.localScale : Vector3.one;
            }

            if (!fitToSurfaceBounds || target == null ||
                !TryCalculateLocalBounds(target, out Bounds trackBounds))
            {
                return target != null ? target.transform.localScale : Vector3.one;
            }

            float trackLong = Mathf.Max(trackBounds.size.x, trackBounds.size.z);
            float trackShort = Mathf.Min(trackBounds.size.x, trackBounds.size.z);
            Vector2 surfaceSize = currentPlane != null
                ? currentPlane.size
                : hasAutomaticSurface
                    ? currentAutomaticSurface.Size
                    : Vector2.zero;
            float surfaceLong = Mathf.Max(surfaceSize.x, surfaceSize.y);
            float surfaceShort = Mathf.Min(surfaceSize.x, surfaceSize.y);
            if (trackLong <= 0.001f || trackShort <= 0.001f ||
                surfaceLong <= 0.001f || surfaceShort <= 0.001f)
            {
                return target.transform.localScale;
            }

            float scale = Mathf.Min(
                surfaceLong / trackLong,
                surfaceShort / trackShort) * surfaceFit;
            return Vector3.one * scale;
        }

        static bool TryCalculateLocalBounds(GameObject target, out Bounds bounds)
        {
            bounds = default;
            bool hasBounds = false;
            Transform root = target.transform;

            foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                if (renderer == null)
                    continue;

                Bounds rendererBounds = renderer.localBounds;
                Matrix4x4 toRoot = root.worldToLocalMatrix * renderer.localToWorldMatrix;

                for (int x = -1; x <= 1; x += 2)
                {
                    for (int y = -1; y <= 1; y += 2)
                    {
                        for (int z = -1; z <= 1; z += 2)
                        {
                            Vector3 point = rendererBounds.center + Vector3.Scale(
                                rendererBounds.extents,
                                new Vector3(x, y, z));
                            point = toRoot.MultiplyPoint3x4(point);

                            if (!hasBounds)
                            {
                                bounds = new Bounds(point, Vector3.zero);
                                hasBounds = true;
                            }
                            else
                            {
                                bounds.Encapsulate(point);
                            }
                        }
                    }
                }
            }

            return hasBounds && (bounds.size.x > 0.001f || bounds.size.z > 0.001f);
        }

        ARAnchor CreateAnchor(Pose pose, ARPlane plane)
        {
            if (anchorManager == null)
                return null;

            if (plane != null)
            {
                ARAnchor attachedAnchor = anchorManager.AttachAnchor(plane, pose);
                if (attachedAnchor != null)
                    return attachedAnchor;
            }

            GameObject anchorObject = new GameObject("Placed Build Anchor");
            anchorObject.transform.SetPositionAndRotation(pose.position, pose.rotation);
            return anchorObject.AddComponent<ARAnchor>();
        }

        static void ConfigurePhysics(GameObject target)
        {
            foreach (Rigidbody rigidbody in target.GetComponentsInChildren<Rigidbody>(includeInactive: true))
            {
                rigidbody.useGravity = false;
                rigidbody.isKinematic = true;
            }
        }

        void ApplyTrackMap(GameObject target)
        {
            if (trackMapPrefab == null || target == null)
                return;

            F1XR.RestAPI.Replay.Track.TrackMapView mapView = target.GetComponent<F1XR.RestAPI.Replay.Track.TrackMapView>();
            if (mapView != null)
                mapView.Show(trackMapPrefab, trackMapScale, fitTrackMapToBounds, trackMapTargetXZSize);
        }

        public void ClearSpawned()
        {
            editState = null;
            placementActive = true;

            if (currentAnchor != null)
            {
                Destroy(currentAnchor.gameObject);
                currentAnchor = null;
            }

            if (spawnedInstance != null)
            {
                Destroy(spawnedInstance);
                spawnedInstance = null;
            }

        }

        void ConfigureEditState(GameObject target)
        {
            if (target == null)
                return;

            editState = target.GetComponent<TrackEditState>();
            if (editState == null)
                editState = target.AddComponent<TrackEditState>();
            editState.Configure(target.transform);
            editState.SetEditMode(false);
        }
    }

    public sealed class TrackAnchorStabilizer : MonoBehaviour
    {
        Transform anchor;
        Vector3 anchorLocalPosition;
        Quaternion anchorLocalRotation;
        float positionSpeed;
        float rotationSpeed;
        float positionDeadZone;
        float rotationDeadZone;
        bool paused;

        public void Configure(
            Transform anchorTransform,
            float followPositionSpeed,
            float followRotationSpeed,
            float positionThreshold,
            float rotationThreshold)
        {
            anchor = anchorTransform;
            positionSpeed = followPositionSpeed;
            rotationSpeed = followRotationSpeed;
            positionDeadZone = positionThreshold;
            rotationDeadZone = rotationThreshold;
            CaptureOffset();
        }

        public void SetPaused(bool value)
        {
            if (paused == value)
                return;

            paused = value;
            if (!paused)
                CaptureOffset();
        }

        void LateUpdate()
        {
            if (paused || anchor == null)
                return;

            Vector3 targetPosition = anchor.TransformPoint(anchorLocalPosition);
            Quaternion targetRotation = anchor.rotation * anchorLocalRotation;
            float deltaTime = Time.unscaledDeltaTime;

            if (Vector3.Distance(transform.position, targetPosition) > positionDeadZone)
            {
                transform.position = Vector3.Lerp(
                    transform.position,
                    targetPosition,
                    FollowT(positionSpeed, deltaTime));
            }

            if (Quaternion.Angle(transform.rotation, targetRotation) > rotationDeadZone)
            {
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    targetRotation,
                    FollowT(rotationSpeed, deltaTime));
            }
        }

        void CaptureOffset()
        {
            if (anchor == null)
                return;

            anchorLocalPosition = anchor.InverseTransformPoint(transform.position);
            anchorLocalRotation = Quaternion.Inverse(anchor.rotation) * transform.rotation;
        }

        static float FollowT(float speed, float deltaTime)
        {
            return speed <= 0f ? 1f : 1f - Mathf.Exp(-speed * deltaTime);
        }
    }
}
