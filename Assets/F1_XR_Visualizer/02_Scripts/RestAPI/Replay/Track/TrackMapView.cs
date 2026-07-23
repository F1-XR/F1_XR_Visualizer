using UnityEngine;
using UnityEngine.Rendering;

namespace F1XR.RestAPI.Replay.Track
{
    public sealed class TrackMapView : MonoBehaviour
    {
        [SerializeField] Transform visualRoot;
        [SerializeField] BoxCollider grabBox;
        [SerializeField] GameObject mapPrefab;
        [SerializeField] Vector3 colliderPadding = new Vector3(0.02f, 0.02f, 0.02f);
        [SerializeField] float minColliderHeight = 0.04f;

        GameObject mapInstance;
        float authoredMapScale = 1f;

        public float MapScaleRatio { get; private set; } = 1f;

        void Awake()
        {
            ResolveReferences();

            if (mapPrefab != null)
                Show(mapPrefab);
            else
                FitGrabBox();
        }

        public void Show(
            GameObject prefab,
            float scale = 1f,
            bool fitToTargetBounds = false,
            Vector2 targetXZSize = default)
        {
            ResolveReferences();
            ClearMap();

            mapPrefab = prefab;

            if (visualRoot == null || mapPrefab == null)
            {
                FitGrabBox();
                return;
            }

            authoredMapScale = GetUniformScale(
                mapPrefab.transform.localScale);
            mapInstance = Instantiate(mapPrefab, visualRoot);
            mapInstance.name = mapPrefab.name;
            mapInstance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            mapInstance.transform.localScale = Vector3.one * Mathf.Max(0.0001f, scale);
            DisableShadowCasting(mapInstance);

            if (fitToTargetBounds)
                FitMapScale(targetXZSize);

            MapScaleRatio =
                GetUniformScale(mapInstance.transform.localScale) /
                authoredMapScale;
            FitGrabBox();
        }

        public void ClearMap()
        {
            if (mapInstance == null)
                return;

            mapInstance.transform.SetParent(null);
            Destroy(mapInstance);
            mapInstance = null;
            authoredMapScale = 1f;
            MapScaleRatio = 1f;
        }

        public void FitGrabBox()
        {
            ResolveReferences();

            if (grabBox == null || visualRoot == null)
                return;

            if (!TryGetVisualBounds(out Bounds bounds))
                return;

            Vector3 size = bounds.size + colliderPadding;
            size.y = Mathf.Max(size.y, minColliderHeight);

            grabBox.center = bounds.center;
            grabBox.size = size;
        }

        bool TryGetVisualBounds(out Bounds bounds)
        {
            bounds = default;
            return mapInstance != null &&
                TryGetBounds(
                    mapInstance.transform,
                    grabBox.transform,
                    out bounds);
        }

        bool TryGetVisualRootBounds(out Bounds bounds)
        {
            bounds = default;
            return mapInstance != null &&
                TryGetBounds(
                    mapInstance.transform,
                    visualRoot,
                    out bounds);
        }

        bool TryGetBounds(
            Transform renderRoot,
            Transform targetSpace,
            out Bounds bounds)
        {
            bounds = default;
            bool found = false;

            if (renderRoot == null || targetSpace == null)
                return false;

            Renderer[] renderers =
                renderRoot.GetComponentsInChildren<Renderer>(includeInactive: true);
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                    continue;

                EncapsulateRenderer(renderer, targetSpace, ref bounds, ref found);
            }

            return found;
        }

        void EncapsulateRenderer(Renderer renderer, Transform targetSpace, ref Bounds bounds, ref bool found)
        {
            Bounds worldBounds = renderer.bounds;
            Vector3 min = worldBounds.min;
            Vector3 max = worldBounds.max;

            EncapsulatePoint(new Vector3(min.x, min.y, min.z), targetSpace, ref bounds, ref found);
            EncapsulatePoint(new Vector3(min.x, min.y, max.z), targetSpace, ref bounds, ref found);
            EncapsulatePoint(new Vector3(min.x, max.y, min.z), targetSpace, ref bounds, ref found);
            EncapsulatePoint(new Vector3(min.x, max.y, max.z), targetSpace, ref bounds, ref found);
            EncapsulatePoint(new Vector3(max.x, min.y, min.z), targetSpace, ref bounds, ref found);
            EncapsulatePoint(new Vector3(max.x, min.y, max.z), targetSpace, ref bounds, ref found);
            EncapsulatePoint(new Vector3(max.x, max.y, min.z), targetSpace, ref bounds, ref found);
            EncapsulatePoint(new Vector3(max.x, max.y, max.z), targetSpace, ref bounds, ref found);
        }

        void EncapsulatePoint(Vector3 worldPoint, Transform targetSpace, ref Bounds bounds, ref bool found)
        {
            Vector3 localPoint = targetSpace.InverseTransformPoint(worldPoint);

            if (!found)
            {
                bounds = new Bounds(localPoint, Vector3.zero);
                found = true;
                return;
            }

            bounds.Encapsulate(localPoint);
        }

        void FitMapScale(Vector2 targetXZSize)
        {
            if (mapInstance == null || targetXZSize.x <= 0f || targetXZSize.y <= 0f)
                return;

            if (!TryGetVisualRootBounds(out Bounds bounds))
                return;

            Vector2 currentSize = new Vector2(bounds.size.x, bounds.size.z);
            float targetSize = Mathf.Max(targetXZSize.x, targetXZSize.y);
            float currentSizeMax = Mathf.Max(currentSize.x, currentSize.y);

            if (targetSize <= 0f || currentSizeMax <= 0.0001f)
                return;

            float scaleRatio = targetSize / currentSizeMax;
            mapInstance.transform.localScale *= scaleRatio;

            Debug.Log(
                $"[TrackMapView] Fit map scale. map={mapInstance.name}, targetXZ={targetXZSize}, " +
                $"currentXZ={currentSize}, finalScale={mapInstance.transform.localScale.x}",
                this);
        }

        void ResolveReferences()
        {
            if (visualRoot == null)
            {
                Transform found = transform.Find("Visual");
                if (found != null)
                    visualRoot = found;
            }

            if (grabBox == null)
            {
                Transform found = transform.Find("GrabBox");
                if (found != null)
                    grabBox = found.GetComponent<BoxCollider>();
            }
        }

        static float GetUniformScale(Vector3 scale)
        {
            float value = Mathf.Max(
                Mathf.Abs(scale.x),
                Mathf.Abs(scale.y),
                Mathf.Abs(scale.z));
            return value > 0.000001f ? value : 1f;
        }

        static void DisableShadowCasting(GameObject target)
        {
            foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(includeInactive: true))
                renderer.shadowCastingMode = ShadowCastingMode.Off;
        }
    }
}
