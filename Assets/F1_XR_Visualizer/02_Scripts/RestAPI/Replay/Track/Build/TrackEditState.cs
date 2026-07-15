using F1XR.Interaction.World;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace F1XR.RestAPI.Replay.Track.Build
{
    public sealed class TrackEditState : MonoBehaviour
    {
        static readonly Color EditColor = new(0.15f, 0.9f, 1f, 0.9f);
        static readonly Color ActiveColor = new(1f, 0.55f, 0.08f, 1f);

        Transform target;
        XRGrabInteractable grab;
        ScaleController scale;
        WorldGrabPolicy grabPolicy;
        TrackAnchorStabilizer stabilizer;
        LineRenderer boundary;
        Material boundaryMaterial;
        Snapshot undoSnapshot;
        bool hasUndo;
        bool wasScaling;

        public bool IsEditMode { get; private set; }
        public bool CanUndo => hasUndo;

        public void Configure(Transform value)
        {
            target = value != null ? value : transform;
            grab = target.GetComponent<XRGrabInteractable>();
            scale = target.GetComponent<ScaleController>();
            grabPolicy = target.GetComponent<WorldGrabPolicy>();
            stabilizer = target.GetComponent<TrackAnchorStabilizer>();
            EnsureBoundary();
        }

        void Awake()
        {
            Configure(transform);
        }

        void OnEnable()
        {
            if (grab != null)
            {
                grab.selectEntered.AddListener(OnSelectEntered);
                grab.selectExited.AddListener(OnSelectExited);
            }

            if (scale != null)
                scale.ScaleStarted += OnScaleStarted;
        }

        void OnDisable()
        {
            if (grab != null)
            {
                grab.selectEntered.RemoveListener(OnSelectEntered);
                grab.selectExited.RemoveListener(OnSelectExited);
            }

            if (scale != null)
                scale.ScaleStarted -= OnScaleStarted;
        }

        void OnDestroy()
        {
            if (boundaryMaterial != null)
                Destroy(boundaryMaterial);
        }

        void Update()
        {
            bool isScaling = IsEditMode && scale != null && scale.IsScaling;
            if (isScaling && !wasScaling)
            {
                SetBoundaryColor(ActiveColor);
            }
            else if (!isScaling && wasScaling && (grab == null || !grab.isSelected))
            {
                SetBoundaryColor(EditColor);
            }

            wasScaling = isScaling;
        }

        public void SetEditMode(bool enabled)
        {
            IsEditMode = enabled;

            if (stabilizer != null)
                stabilizer.SetPaused(enabled);

            if (grabPolicy != null)
                grabPolicy.enabled = enabled;

            if (scale != null)
                scale.enabled = enabled;

            if (grab != null)
                grab.enabled = enabled;

            SetManipulationCollidersEnabled(enabled);

            foreach (ReplayCarInteractable car in target.GetComponentsInChildren<ReplayCarInteractable>(true))
                car.enabled = !enabled;

            EnsureBoundary();
            if (boundary != null)
                boundary.gameObject.SetActive(enabled);

            SetBoundaryColor(EditColor);
            wasScaling = false;
        }

        void SetManipulationCollidersEnabled(bool enabled)
        {
            if (grab != null && grab.colliders.Count > 0)
            {
                foreach (Collider collider in grab.colliders)
                {
                    if (collider != null)
                        collider.enabled = enabled;
                }
                return;
            }

            foreach (Collider collider in target.GetComponentsInChildren<Collider>(true))
            {
                if (collider != null && collider.name.Contains("GrabBox"))
                    collider.enabled = enabled;
            }
        }

        public void Undo()
        {
            if (!hasUndo || target == null)
                return;

            target.localPosition = undoSnapshot.Position;
            target.localRotation = undoSnapshot.Rotation;
            target.localScale = undoSnapshot.Scale;
            hasUndo = false;
            SetBoundaryColor(EditColor);
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (!IsEditMode)
                return;

            CaptureUndo();
            SetBoundaryColor(ActiveColor);
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            if (IsEditMode && !wasScaling)
                SetBoundaryColor(EditColor);
        }

        void OnScaleStarted()
        {
            if (!IsEditMode)
                return;

            CaptureUndo();
            SetBoundaryColor(ActiveColor);
        }

        void CaptureUndo()
        {
            if (target == null)
                return;

            undoSnapshot = new Snapshot(
                target.localPosition,
                target.localRotation,
                target.localScale);
            hasUndo = true;
        }

        void EnsureBoundary()
        {
            if (boundary != null || target == null)
                return;

            GameObject boundaryObject = new("Track Edit Boundary");
            boundaryObject.transform.SetParent(target, false);
            boundary = boundaryObject.AddComponent<LineRenderer>();
            boundary.useWorldSpace = false;
            boundary.loop = false;
            boundary.positionCount = 16;
            boundary.widthMultiplier = 0.006f;
            boundary.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            boundary.receiveShadows = false;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            if (shader != null)
            {
                boundaryMaterial = new Material(shader);
                boundary.sharedMaterial = boundaryMaterial;
            }

            UpdateBoundaryShape();
            boundaryObject.SetActive(false);
        }

        void UpdateBoundaryShape()
        {
            bool hasBounds = false;
            Bounds localBounds = default;

            foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || renderer == boundary)
                    continue;

                Bounds bounds = renderer.bounds;
                for (int x = -1; x <= 1; x += 2)
                {
                    for (int y = -1; y <= 1; y += 2)
                    {
                        for (int z = -1; z <= 1; z += 2)
                        {
                            Vector3 world = bounds.center + Vector3.Scale(
                                bounds.extents,
                                new Vector3(x, y, z));
                            Vector3 local = target.InverseTransformPoint(world);
                            if (!hasBounds)
                            {
                                localBounds = new Bounds(local, Vector3.zero);
                                hasBounds = true;
                            }
                            else
                            {
                                localBounds.Encapsulate(local);
                            }
                        }
                    }
                }
            }

            if (!hasBounds)
                localBounds = new Bounds(Vector3.zero, Vector3.one);

            Vector3 min = localBounds.min;
            Vector3 max = localBounds.max;
            Vector3[] points =
            {
                new(min.x, min.y, min.z), new(max.x, min.y, min.z),
                new(max.x, min.y, max.z), new(min.x, min.y, max.z),
                new(min.x, min.y, min.z), new(min.x, max.y, min.z),
                new(max.x, max.y, min.z), new(max.x, min.y, min.z),
                new(max.x, max.y, min.z), new(max.x, max.y, max.z),
                new(max.x, min.y, max.z), new(max.x, max.y, max.z),
                new(min.x, max.y, max.z), new(min.x, min.y, max.z),
                new(min.x, max.y, max.z), new(min.x, max.y, min.z)
            };
            boundary.SetPositions(points);
        }

        void SetBoundaryColor(Color color)
        {
            if (boundary == null)
                return;

            boundary.startColor = color;
            boundary.endColor = color;
            if (boundaryMaterial != null)
                boundaryMaterial.color = color;
        }

        readonly struct Snapshot
        {
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly Vector3 Scale;

            public Snapshot(Vector3 position, Quaternion rotation, Vector3 scale)
            {
                Position = position;
                Rotation = rotation;
                Scale = scale;
            }
        }
    }
}
