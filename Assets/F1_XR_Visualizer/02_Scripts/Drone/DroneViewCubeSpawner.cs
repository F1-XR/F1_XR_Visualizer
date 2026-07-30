using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace F1XR.RestAPI.Replay.Track.Build
{
    [DisallowMultipleComponent]
    public sealed class DroneViewCubeSpawner : MonoBehaviour
    {
        [SerializeField] TrackRevealPlacer trackPlacer;
        [SerializeField] Transform viewerCamera;
        [SerializeField, Min(0.001f)] float cubeSize = 0.05f / 3f;
        [SerializeField, Min(0f)] float sideClearance = 0.06f;
        [SerializeField, Min(0f)] float surfaceClearance = 0.005f;
        [SerializeField] Color cubeColor = new(0.9f, 0.05f, 0.05f, 1f);

        GameObject cube;
        Transform observedPlacement;

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
        }

        void OnDestroy()
        {
            DestroyCube();
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
            Transform cameraTransform)
        {
            trackPlacer = source;
            viewerCamera = cameraTransform;
            observedPlacement = null;
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

            cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Drone View Cube";
            cube.transform.SetPositionAndRotation(position, Quaternion.identity);
            cube.transform.localScale = Vector3.one * cubeSize;
            cube.transform.SetParent(placement, true);

            Renderer renderer = cube.GetComponent<Renderer>();
            if (renderer != null)
                renderer.material.color = cubeColor;

            Rigidbody body = cube.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.isKinematic = true;

            XRGrabInteractable grab = cube.AddComponent<XRGrabInteractable>();
            grab.throwOnDetach = false;
            grab.retainTransformParent = true;
            Transform cubeTransform = cube.transform;
            grab.selectExited.AddListener(_ => CubeReleased?.Invoke(cubeTransform));
        }

        void DestroyCube()
        {
            if (cube != null)
                Destroy(cube);

            cube = null;
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
