using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace F1XR.Experience.Room
{
    public enum RoomSurfaceType
    {
        Wall,
        Floor,
        Ceiling
    }

    /// <summary>
    /// One proxy surface standing in for a real wall, floor or ceiling.
    /// The source <see cref="ARPlane"/> is kept so pose and size are always read from the
    /// live plane rather than a copy that can go stale.
    /// </summary>
    public readonly struct RoomShellProxy
    {
        public readonly RoomSurfaceType Type;
        public readonly GameObject GameObject;
        public readonly ARPlane SourcePlane;

        /// <summary>Surface normal in world space, pointing into the room.</summary>
        public readonly Vector3 InwardNormal;

        public RoomShellProxy(
            RoomSurfaceType type,
            GameObject gameObject,
            ARPlane sourcePlane,
            Vector3 inwardNormal)
        {
            Type = type;
            GameObject = gameObject;
            SourcePlane = sourcePlane;
            InwardNormal = inwardNormal;
        }
    }

    /// <summary>
    /// Builds a mesh proxy over each real room surface reported by
    /// <see cref="RoomSurfaceProvider"/>. Nothing is broken, moved or simulated here; this
    /// only produces the surfaces a later fracture system will consume.
    ///
    /// The real AR planes are never modified: proxies are separate GameObjects under
    /// their own root that copy the plane's world pose.
    ///
    /// Mesh construction uses <see cref="ARPlaneMeshGenerator.TryGenerateMesh"/>, AR
    /// Foundation's own ear-clipping triangulator, rather than a scaled primitive Plane.
    /// Three reasons: real walls and floors are not always rectangles; a scaled primitive
    /// carries a non-uniform scale that makes later fragment maths awkward; and the
    /// resulting vertices already sit on a flat local plane, which is what a fracture
    /// step wants as input. No extra tessellation is added at this stage.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RoomShellProxyGenerator : MonoBehaviour
    {
        const string RootName = "RoomShellProxyRoot";

        [SerializeField] RoomSurfaceProvider provider;

        [Tooltip("Rebuild automatically whenever the room surface set changes.")]
        [SerializeField] bool autoRebuild = true;

        [Tooltip("Distance the proxy is pushed off the real surface, along the surface " +
            "normal, towards the inside of the room. Keep this tiny: it only exists to " +
            "avoid coplanar overlap with the real surface.")]
        [SerializeField, Range(0f, 0.05f)] float surfaceOffset = 0.005f;

        readonly List<RoomShellProxy> proxies = new();
        Transform proxyRoot;
        bool rebuildPending;

        public IReadOnlyList<RoomShellProxy> Proxies => proxies;

        public Transform ProxyRoot => proxyRoot;

        public float SurfaceOffset => surfaceOffset;

        /// <summary>
        /// Changes the offset and moves every existing proxy immediately. Nothing is
        /// rebuilt.
        /// </summary>
        public void SetSurfaceOffset(float value)
        {
            surfaceOffset = value;
            ApplySurfaceOffset();
        }

        /// <summary>
        /// Re-places every existing proxy from its source plane using the current offset.
        /// </summary>
        public void ApplySurfaceOffset()
        {
            foreach (RoomShellProxy proxy in proxies)
                ApplyPose(proxy);
        }

        void OnValidate()
        {
            // Editing a serialized field in the Inspector writes the backing field
            // directly; it does not call any setter. Without this hook, dragging the
            // offset slider during Play mode changed the number and moved nothing.
            if (Application.isPlaying)
                ApplySurfaceOffset();
        }

        /// <summary>Raised after a rebuild completes, so visualisation can be reapplied.</summary>
        public event Action ProxiesBuilt;

        void Awake()
        {
            if (provider == null)
                provider = FindAnyObjectByType<RoomSurfaceProvider>();
        }

        void OnEnable()
        {
            if (provider == null)
                return;

            provider.SurfacesChanged += OnSurfacesChanged;

            if (autoRebuild && provider.HasRoom)
                BuildRoomProxies();
        }

        void OnDisable()
        {
            if (provider != null)
                provider.SurfacesChanged -= OnSurfacesChanged;
        }

        void OnSurfacesChanged()
        {
            if (!autoRebuild || rebuildPending)
                return;

            if (!isActiveAndEnabled)
                return;

            // Planes arrive a few at a time rather than all at once, so a burst of
            // change events would otherwise tear down and rebuild every proxy several
            // times in the same frame. Coalesce them into a single rebuild.
            rebuildPending = true;
            StartCoroutine(RebuildNextFrame());
        }

        IEnumerator RebuildNextFrame()
        {
            yield return null;
            rebuildPending = false;
            BuildRoomProxies();
        }

        /// <summary>
        /// Clears any existing proxies and rebuilds them from the current room. Safe to
        /// call repeatedly; proxies never accumulate.
        /// </summary>
        public void BuildRoomProxies()
        {
            ClearRoomProxies();

            if (provider == null)
            {
                Debug.LogWarning("[RoomShell] No RoomSurfaceProvider assigned.", this);
                return;
            }

            if (!provider.HasRoom)
            {
                Debug.LogWarning(
                    "[RoomShell] No room surfaces available yet. Complete Space Setup on " +
                    "the device, then rebuild.",
                    this);
                return;
            }

            EnsureRoot();

            int wallIndex = 0;
            foreach (ARPlane wall in provider.Walls)
                CreateProxy(RoomSurfaceType.Wall, wall, $"WallProxy_{wallIndex++:00}");

            CreateProxy(RoomSurfaceType.Floor, provider.Floor, "FloorProxy");
            CreateProxy(RoomSurfaceType.Ceiling, provider.Ceiling, "CeilingProxy");

            LogProxies();
            ProxiesBuilt?.Invoke();
        }

        /// <summary>Destroys every proxy and the proxy root.</summary>
        public void ClearRoomProxies()
        {
            // Destroying the GameObject does not destroy the Mesh it references, and a
            // rebuild allocates a fresh Mesh per surface. Release them explicitly so
            // repeated rebuilds do not accumulate meshes.
            foreach (RoomShellProxy proxy in proxies)
            {
                if (proxy.GameObject == null)
                    continue;

                MeshFilter filter = proxy.GameObject.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                    DestroyObject(filter.sharedMesh);
            }

            proxies.Clear();

            if (proxyRoot != null)
            {
                DestroyObject(proxyRoot.gameObject);
                proxyRoot = null;
            }

            // Catch a root left behind by a previous session or a domain reload.
            GameObject stale = GameObject.Find(RootName);
            if (stale != null)
                DestroyObject(stale);
        }

        void EnsureRoot()
        {
            if (proxyRoot != null)
                return;

            GameObject root = new GameObject(RootName);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;
            proxyRoot = root.transform;
        }

        void CreateProxy(RoomSurfaceType type, ARPlane plane, string proxyName)
        {
            if (plane == null)
            {
                if (type != RoomSurfaceType.Wall)
                {
                    Debug.LogWarning(
                        $"[RoomShell] No {type} surface reported by the device.",
                        this);
                }

                return;
            }

            GameObject go = new GameObject(proxyName);
            go.transform.SetParent(proxyRoot, false);

            Mesh mesh = new Mesh { name = proxyName + "_Mesh" };

            // Boundary vertices are in plane space, so the proxy shares the plane's pose
            // and needs no vertex transform of its own.
            if (!ARPlaneMeshGenerator.TryGenerateMesh(mesh, plane.boundary))
            {
                Debug.LogWarning(
                    $"[RoomShell] {proxyName} has no usable boundary; falling back to a " +
                    "rectangle from the reported plane size.",
                    this);
                BuildRectangleMesh(mesh, plane.size);
            }

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>();

            // No Rigidbody and no Collider: this step only needs geometry.

            Vector3 inwardNormal = ResolveInwardNormal(plane, proxyName);
            proxies.Add(new RoomShellProxy(type, go, plane, inwardNormal));
            ApplyPose(proxies[^1]);
        }

        /// <summary>
        /// Works out which side of the plane faces into the room by testing the reported
        /// normal against the direction of the room centre, instead of assuming a
        /// convention for walls, floors and ceilings.
        /// </summary>
        Vector3 ResolveInwardNormal(ARPlane plane, string proxyName)
        {
            Vector3 normal = plane.normal;
            Vector3 toCenter = provider.RoomCenter - plane.transform.position;

            if (toCenter.sqrMagnitude < 1e-6f)
                return normal;

            float alignment = Vector3.Dot(normal, toCenter.normalized);
            if (alignment >= 0f)
                return normal;

            Debug.Log(
                $"[RoomShell] {proxyName} normal points away from the room centre " +
                $"(dot={alignment:F2}); offsetting towards the inside instead.",
                this);
            return -normal;
        }

        void ApplyPose(RoomShellProxy proxy)
        {
            if (proxy.GameObject == null || proxy.SourcePlane == null)
                return;

            // Always recomputed from the source plane's current pose. The offset is never
            // added to the proxy's own position, so repeatedly changing it cannot drift.
            Transform plane = proxy.SourcePlane.transform;
            proxy.GameObject.transform.SetPositionAndRotation(
                plane.position + proxy.InwardNormal * surfaceOffset,
                plane.rotation);
            proxy.GameObject.transform.localScale = Vector3.one;
        }

        static void BuildRectangleMesh(Mesh mesh, Vector2 size)
        {
            float halfX = size.x * 0.5f;
            float halfZ = size.y * 0.5f;

            mesh.Clear();
            mesh.SetVertices(new List<Vector3>
            {
                new(-halfX, 0f, -halfZ),
                new(-halfX, 0f, halfZ),
                new(halfX, 0f, halfZ),
                new(halfX, 0f, -halfZ)
            });
            mesh.SetTriangles(new List<int> { 0, 1, 2, 0, 2, 3 }, 0, true);
            mesh.SetNormals(new List<Vector3>
            {
                Vector3.up, Vector3.up, Vector3.up, Vector3.up
            });
            mesh.SetUVs(0, new List<Vector2>
            {
                new(0f, 0f), new(0f, 1f), new(1f, 1f), new(1f, 0f)
            });
        }

        /// <summary>One log per rebuild, never per frame.</summary>
        void LogProxies()
        {
            foreach (RoomShellProxy proxy in proxies)
            {
                Vector2 size = proxy.SourcePlane.size;
                Debug.Log(
                    $"[RoomShell] {proxy.GameObject.name} Size=({size.x:F2}, {size.y:F2}) " +
                    $"Pos={proxy.GameObject.transform.position} " +
                    $"InwardNormal={proxy.InwardNormal}",
                    proxy.GameObject);
            }

            Debug.Log(
                $"[RoomShell] Built {proxies.Count} proxies " +
                $"(walls={provider.Walls.Count}, floor={provider.Floor != null}, " +
                $"ceiling={provider.Ceiling != null}).",
                this);
        }

        static void DestroyObject(UnityEngine.Object target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }
    }
}
