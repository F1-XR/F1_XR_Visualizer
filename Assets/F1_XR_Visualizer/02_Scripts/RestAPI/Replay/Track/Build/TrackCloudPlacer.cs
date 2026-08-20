using System.Collections;
using UnityEngine;

namespace F1XR.RestAPI.Replay.Track.Build
{
    public sealed class TrackCloudPlacer : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] TrackRevealPlacer trackPlacer;
        [SerializeField] GameObject cloudPrefab;

        [Header("Placement")]
        [SerializeField] float cloudHeightOffset = 0.05f;
        [SerializeField, Range(0.1f, 0.8f)] float cloudSizeRatio = 0.3f;
        [SerializeField] Vector3 cloudPositionOffset;

        GameObject cloudInstance;
        Coroutine spawnRoutine;

        void OnEnable()
        {
            if (trackPlacer == null)
                return;

            trackPlacer.PlacementRevealed += OnPlacementRevealed;

            if (trackPlacer.HasPlacement && cloudInstance == null)
                OnPlacementRevealed();
        }

        void OnDisable()
        {
            if (spawnRoutine != null)
            {
                StopCoroutine(spawnRoutine);
                spawnRoutine = null;
            }

            if (trackPlacer != null)
                trackPlacer.PlacementRevealed -= OnPlacementRevealed;
        }

        void OnPlacementRevealed()
        {
            if (spawnRoutine != null)
            {
                StopCoroutine(spawnRoutine);
                spawnRoutine = null;
            }

            if (cloudInstance != null)
            {
                Destroy(cloudInstance);
                cloudInstance = null;
            }

            if (cloudPrefab == null || trackPlacer == null)
                return;

            spawnRoutine = StartCoroutine(SpawnCloudRoutine());
        }

        IEnumerator SpawnCloudRoutine()
        {
            Transform placementRoot = trackPlacer.PlacementTransform;
            if (placementRoot == null)
                yield break;

            if (!TryComputeMapBounds(placementRoot, out Bounds mapBounds))
                yield break;

            cloudInstance = Instantiate(cloudPrefab);
            cloudInstance.name = "WeatherCloud";

            yield return null;
            yield return null;

            if (cloudInstance == null)
                yield break;

            placementRoot = trackPlacer.PlacementTransform;
            if (placementRoot == null)
            {
                Destroy(cloudInstance);
                cloudInstance = null;
                yield break;
            }

            float mapHorizontalSize = Mathf.Max(mapBounds.size.x, mapBounds.size.z);
            float targetCloudSize = mapHorizontalSize * cloudSizeRatio;

            float cloudHorizontalSize = GetRendererSpan(cloudInstance.transform);
            if (cloudHorizontalSize > 0.001f)
            {
                float scaleFactor = targetCloudSize / cloudHorizontalSize;
                cloudInstance.transform.localScale *= scaleFactor;
            }

            Vector3 position = new Vector3(
                mapBounds.center.x + cloudPositionOffset.x,
                mapBounds.max.y + cloudHeightOffset + cloudPositionOffset.y,
                mapBounds.center.z + cloudPositionOffset.z);
            cloudInstance.transform.position = position;

            cloudInstance.transform.SetParent(placementRoot, worldPositionStays: true);
            spawnRoutine = null;
        }

        static float GetRendererSpan(Transform root)
        {
            Bounds bounds = default;
            bool hasBounds = false;

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
            {
                if (renderer == null)
                    continue;

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return hasBounds ? Mathf.Max(bounds.size.x, bounds.size.z) : 0f;
        }

        static bool TryComputeMapBounds(Transform placementRoot, out Bounds bounds)
        {
            Transform visual = placementRoot.Find("Visual");
            Transform root = visual != null ? visual : placementRoot;
            Transform cars = root.Find("Cars");

            bounds = default;
            bool hasBounds = false;

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
            {
                if (renderer == null)
                    continue;
                if (cars != null && renderer.transform.IsChildOf(cars))
                    continue;

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return hasBounds;
        }
    }
}
