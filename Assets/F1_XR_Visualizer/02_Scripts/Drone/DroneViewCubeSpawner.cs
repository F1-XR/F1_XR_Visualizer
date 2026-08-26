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
        Transform cubeVisual;
        Transform observedPlacement;
        Material grabVolumeMaterial;
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
            bool showGrabVisual)
        {
            trackPlacer = source;
            viewerCamera = cameraTransform;
            showGrabVolumeVisual = showGrabVisual;
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

            cube = new GameObject("Drone View Cube");
            cube.name = "Drone View Cube";
            cube.transform.SetPositionAndRotation(position, Quaternion.identity);
            cube.transform.SetParent(placement, true);

            SphereCollider grabCollider = cube.AddComponent<SphereCollider>();
            grabCollider.radius = grabVolumeSize * 0.5f;
            UpdateGrabVolumeVisual();

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual";
            visual.transform.SetParent(cube.transform, false);
            visual.transform.localScale = Vector3.one * cubeSize;
            Collider visualCollider = visual.GetComponent<Collider>();
            if (visualCollider != null)
                visualCollider.enabled = false;

            Renderer renderer = visual.GetComponent<Renderer>();
            if (renderer != null)
                renderer.material.color = cubeColor;

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

        void UpdateCubeVisual()
        {
            if (cubeVisual == null)
                return;

            float targetScale = isCubeHovered ? hoverScale : 1f;
            float currentScale = cubeVisual.localScale.x / cubeSize;
            float nextScale = Mathf.MoveTowards(
                currentScale,
                targetScale,
                hoverScaleSpeed * Time.deltaTime);
            cubeVisual.localScale = Vector3.one * cubeSize * nextScale;
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
    }
}
