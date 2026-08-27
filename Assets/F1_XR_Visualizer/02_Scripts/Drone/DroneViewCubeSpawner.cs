using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace F1XR.RestAPI.Replay.Track.Build
{
    [DisallowMultipleComponent]
    public sealed class DroneViewCubeSpawner : MonoBehaviour
    {
        [SerializeField] TrackRevealPlacer trackPlacer;
        [SerializeField] Transform viewerCamera;
        [SerializeField, Min(0.001f)] float cubeSize = 0.05f / 3f;
        [SerializeField, Min(0.1f)] float droneVisualScaleMultiplier = 3f;
        [SerializeField, Min(0.001f)] float grabVolumeSize = 0.08f;
        [SerializeField, Min(1f)] float hoverScale = 2.5f;
        [SerializeField, Min(0.01f)] float hoverScaleSpeed = 12f;
        [SerializeField] Color grabVolumeColor =
            new(0.15f, 0.85f, 1f, 0.2f);
        [SerializeField, Min(0f)] float sideClearance = 0.15f;
        [SerializeField, Min(0f)] float surfaceClearance = 0.1f;
        [SerializeField] Color cubeColor = new(0.9f, 0.05f, 0.05f, 1f);

        GameObject cube;
        GameObject grabVolumeVisual;
        GameObject droneVisualPrefab;
        Transform cubeVisual;
        Transform observedPlacement;
        Material grabVolumeMaterial;
        float droneVisualYawOffset;
        bool isCubeHovered;
        bool showGrabVolumeVisual;

        public event Action<Transform> CubeReleased;

        void Awake()
        {
            ResolveReferences();
        }

        void OnEnable()
        {
            ResolveReferences();
        }

        void Update()
        {
            RefreshPlacement();
            UpdateCubeVisual();
        }

        void OnDestroy()
        {
            DestroyCube();

            if (grabVolumeMaterial != null)
                Destroy(grabVolumeMaterial);
        }

        void ResolveReferences()
        {
            if (trackPlacer == null)
                trackPlacer = GetComponent<TrackRevealPlacer>();

            if (viewerCamera == null && Camera.main != null)
                viewerCamera = Camera.main.transform;
        }

        public void Configure(
            TrackRevealPlacer source,
            Transform cameraTransform,
            bool showGrabVisual,
            GameObject visualPrefab,
            float visualYawOffset)
        {
            trackPlacer = source;
            viewerCamera = cameraTransform;
            showGrabVolumeVisual = showGrabVisual;
            droneVisualPrefab = visualPrefab;
            droneVisualYawOffset = visualYawOffset;
            observedPlacement = null;
        }

        public void SetGrabVolumeVisual(bool visible)
        {
            showGrabVolumeVisual = visible;
            UpdateGrabVolumeVisual();
        }

        void RefreshPlacement()
        {
            ResolveReferences();

            Transform placement = trackPlacer != null &&
                trackPlacer.HasPlacement
                    ? trackPlacer.PlacementTransform
                    : null;
            if (placement == observedPlacement)
                return;

            observedPlacement = placement;
            if (placement == null)
                DestroyCube();
            else
                CreateCube(placement);
        }

        void CreateCube(Transform placement)
        {
            DestroyCube();

            if (placement == null)
                return;

            if (viewerCamera == null && Camera.main != null)
                viewerCamera = Camera.main.transform;

            if (!TryGetTrackBounds(placement, out Bounds bounds))
                return;

            Vector3 up = placement.up.normalized;
            Vector3 viewDirection = viewerCamera != null
                ? bounds.center - viewerCamera.position
                : placement.forward;
            viewDirection = Vector3.ProjectOnPlane(viewDirection, up);
            if (viewDirection.sqrMagnitude < 0.0001f)
                viewDirection = placement.forward;
            viewDirection.Normalize();

            Vector3 left = Vector3.Cross(viewDirection, up).normalized;
            float edgeDistance = ProjectedExtent(bounds.extents, left);
            Vector3 position = bounds.center +
                left * (edgeDistance + sideClearance);
            position.y = bounds.min.y +
                cubeSize * 0.5f +
                surfaceClearance;

            Vector3 trackForward = Vector3.ProjectOnPlane(placement.forward, up);
            if (trackForward.sqrMagnitude < 0.0001f)
                trackForward = viewDirection;
            trackForward.Normalize();

            cube = new GameObject("Drone View Cube");
            cube.name = "Drone View Cube";
            cube.transform.SetPositionAndRotation(
                position,
                Quaternion.LookRotation(trackForward, up));
            cube.transform.SetParent(placement, true);

            SphereCollider grabCollider = cube.AddComponent<SphereCollider>();
            grabCollider.radius = grabVolumeSize * 0.5f;
            UpdateGrabVolumeVisual();

            GameObject visual = new GameObject("Visual");
            visual.transform.SetParent(cube.transform, false);
            if (!TryCreateDroneVisual(visual.transform))
                CreateFallbackCubeVisual(visual.transform);

            Rigidbody body = cube.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.isKinematic = true;

            XRGrabInteractable grab = cube.AddComponent<XRGrabInteractable>();
            grab.throwOnDetach = false;
            grab.retainTransformParent = true;
            grab.colliders.Add(grabCollider);
            Transform cubeTransform = cube.transform;
            grab.selectExited.AddListener(_ => CubeReleased?.Invoke(cubeTransform));
            grab.hoverEntered.AddListener(_ => isCubeHovered = true);
            grab.hoverExited.AddListener(_ => isCubeHovered = false);
            cubeVisual = visual.transform;
            isCubeHovered = false;
        }

        void DestroyCube()
        {
            if (cube != null)
                Destroy(cube);

            cube = null;
            grabVolumeVisual = null;
            cubeVisual = null;
            isCubeHovered = false;
        }

        bool TryCreateDroneVisual(Transform visualRoot)
        {
            if (droneVisualPrefab == null)
                return false;

            GameObject droneVisual = Instantiate(droneVisualPrefab, visualRoot, false);
            droneVisual.name = "Drone.fbx Visual";
            droneVisual.transform.localRotation = Quaternion.Euler(
                0f,
                droneVisualYawOffset,
                0f);

            foreach (Collider collider in droneVisual.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;

            if (!TryGetLocalRendererBounds(droneVisual, visualRoot, out Bounds localBounds))
            {
                Debug.LogWarning(
                    "[DroneView] Drone visual has no renderers; using the fallback cube.",
                    this);
                Destroy(droneVisual);
                return false;
            }

            float longestAxis = Mathf.Max(
                localBounds.size.x,
                localBounds.size.y,
                localBounds.size.z);
            if (longestAxis < 0.0001f)
            {
                Debug.LogWarning(
                    "[DroneView] Drone visual has invalid bounds; using the fallback cube.",
                    this);
                Destroy(droneVisual);
                return false;
            }

            droneVisual.transform.localScale *=
                cubeSize * droneVisualScaleMultiplier / longestAxis;
            TryGetLocalRendererBounds(droneVisual, visualRoot, out localBounds);
            droneVisual.transform.localPosition = new Vector3(
                -localBounds.center.x,
                -cubeSize * 0.5f - localBounds.min.y,
                -localBounds.center.z);
            cubeVisual = visualRoot;
            return true;
        }

        void CreateFallbackCubeVisual(Transform visualRoot)
        {
            if (droneVisualPrefab == null)
            {
                Debug.LogWarning(
                    "[DroneView] Drone visual is not assigned; using the fallback cube.",
                    this);
            }

            GameObject fallback = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fallback.name = "Fallback Cube";
            fallback.transform.SetParent(visualRoot, false);
            fallback.transform.localScale = Vector3.one * cubeSize;

            Collider fallbackCollider = fallback.GetComponent<Collider>();
            if (fallbackCollider != null)
                fallbackCollider.enabled = false;

            Renderer renderer = fallback.GetComponent<Renderer>();
            if (renderer != null)
                renderer.material.color = cubeColor;

            cubeVisual = visualRoot;
        }

        void UpdateCubeVisual()
        {
            if (cubeVisual == null)
                return;

            float targetScale = isCubeHovered ? hoverScale : 1f;
            float currentScale = cubeVisual.localScale.x;
            float nextScale = Mathf.MoveTowards(
                currentScale,
                targetScale,
                hoverScaleSpeed * Time.deltaTime);
            cubeVisual.localScale = Vector3.one * nextScale;
        }

        void UpdateGrabVolumeVisual()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (cube == null)
                return;

            if (showGrabVolumeVisual && grabVolumeVisual == null)
            {
                grabVolumeVisual = CreateGrabVolumeVisual(cube.transform);
            }
            else if (!showGrabVolumeVisual && grabVolumeVisual != null)
            {
                Destroy(grabVolumeVisual);
                grabVolumeVisual = null;
            }
#endif
        }

        GameObject CreateGrabVolumeVisual(Transform parent)
        {
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visual.name = "Grab Volume Visual";
            visual.transform.SetParent(parent, false);
            visual.transform.localScale = Vector3.one * grabVolumeSize;

            Collider visualCollider = visual.GetComponent<Collider>();
            if (visualCollider != null)
                visualCollider.enabled = false;

            Renderer renderer = visual.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = GetGrabVolumeMaterial();

            return visual;
        }

        Material GetGrabVolumeMaterial()
        {
            if (grabVolumeMaterial != null)
                return grabVolumeMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null)
                return null;

            grabVolumeMaterial = new Material(shader)
            {
                name = "Drone View Grab Volume (Runtime)",
                renderQueue = 3000
            };
            grabVolumeMaterial.SetOverrideTag("RenderType", "Transparent");
            if (grabVolumeMaterial.HasProperty("_Surface"))
                grabVolumeMaterial.SetFloat("_Surface", 1f);
            grabVolumeMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (grabVolumeMaterial.HasProperty("_SrcBlend"))
                grabVolumeMaterial.SetFloat(
                    "_SrcBlend",
                    (float)BlendMode.SrcAlpha);
            if (grabVolumeMaterial.HasProperty("_DstBlend"))
                grabVolumeMaterial.SetFloat(
                    "_DstBlend",
                    (float)BlendMode.OneMinusSrcAlpha);
            if (grabVolumeMaterial.HasProperty("_ZWrite"))
                grabVolumeMaterial.SetFloat("_ZWrite", 0f);
            if (grabVolumeMaterial.HasProperty("_BaseColor"))
                grabVolumeMaterial.SetColor("_BaseColor", grabVolumeColor);
            if (grabVolumeMaterial.HasProperty("_Color"))
                grabVolumeMaterial.SetColor("_Color", grabVolumeColor);
            return grabVolumeMaterial;
        }

        static float ProjectedExtent(Vector3 extents, Vector3 direction)
        {
            return Mathf.Abs(direction.x) * extents.x +
                Mathf.Abs(direction.y) * extents.y +
                Mathf.Abs(direction.z) * extents.z;
        }

        static bool TryGetTrackBounds(Transform placement, out Bounds bounds)
        {
            bounds = default;
            Transform visual = placement.Find("Visual") ?? placement;
            Transform cars = visual.Find("Cars");
            bool hasBounds = false;

            foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null ||
                    cars != null && renderer.transform.IsChildOf(cars))
                {
                    continue;
                }

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

        static bool TryGetLocalRendererBounds(
            GameObject target,
            Transform relativeTo,
            out Bounds bounds)
        {
            bounds = default;
            bool hasBounds = false;
            Matrix4x4 worldToLocal = relativeTo.worldToLocalMatrix;

            foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                    continue;

                Bounds rendererBounds = renderer.localBounds;
                Matrix4x4 toRelative = worldToLocal * renderer.localToWorldMatrix;
                Vector3 extents = rendererBounds.extents;
                Vector3 center = rendererBounds.center;

                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 corner = center + Vector3.Scale(
                        extents,
                        new Vector3(x, y, z));
                    Vector3 point = toRelative.MultiplyPoint3x4(corner);
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

            return hasBounds;
        }
    }
}
