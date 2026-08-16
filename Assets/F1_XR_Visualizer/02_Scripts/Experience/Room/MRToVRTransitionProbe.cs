using System.Collections.Generic;
using F1XR.Experience.Fracture;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace F1XR.Experience.Room
{
    /// <summary>
    /// Measures whether the real room is a usable thing to break, before any of the MR to VR
    /// transition is built on the assumption that it is.
    ///
    /// The question is not how many fragments the fracture would produce. Three tables cut
    /// into ninety pieces is ninety fragments and still reads as "some furniture broke", not
    /// as the room coming apart. What decides it is how much of what the viewer is actually
    /// looking at is covered by surfaces that can break, which is why the coverage numbers
    /// below matter more than the counts.
    ///
    /// Reads only. Nothing is enabled, moved, built or destroyed, and this component has no
    /// effect at all unless the menu item is invoked.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MRToVRTransitionProbe : MonoBehaviour
    {
        [Tooltip("Resolution of the viewport grid the coverage estimate is sampled on. A " +
            "polygon union would be exact and far more code; at one probe per run a grid is " +
            "accurate enough to tell 15% from 60%, which is the only distinction that matters.")]
        [SerializeField, Range(8, 64)] int coverageGridResolution = 32;

        [Tooltip("Metres. A room surface closer than this to where the drone ground plane " +
            "would sit is a z-fighting risk, because the ground is ten kilometres wide and " +
            "would be fighting the real floor across the whole room.")]
        [SerializeField, Min(0.01f)] float groundClearanceWarning = 0.3f;

        [ContextMenu("Probe MR To VR Transition")]
        public void Probe()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[MR2VR][Probe] Enter play mode first: none of this exists " +
                    "until the AR planes have been found.", this);
                return;
            }

            Camera camera = Camera.main;
            var provider = FindAnyObjectByType<RoomSurfaceProvider>();
            var generator = FindAnyObjectByType<RoomShellProxyGenerator>();
            var fracture = FindAnyObjectByType<RoomShellFractureController>();

            if (camera == null)
            {
                Debug.LogError("[MR2VR][Probe] No main camera.", this);
                return;
            }

            LogSurfaces(provider);
            LogProxies(generator, fracture, camera);
            LogGround(generator);
            LogSky();
        }

        void LogSurfaces(RoomSurfaceProvider provider)
        {
            if (provider == null)
            {
                Debug.LogWarning(
                    "[MR2VR][Probe] No RoomSurfaceProvider in the scene, so there is nothing " +
                    "to break and the room fracture route cannot be used at all.", this);
                return;
            }

            int table = 0, wall = 0, floor = 0, ceiling = 0, other = 0;

            foreach (ARPlane plane in FindObjectsByType<ARPlane>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                string label = plane.classifications.ToString();
                if (label.Contains("Table")) table++;
                else if (label.Contains("WallFace")) wall++;
                else if (label.Contains("Floor")) floor++;
                else if (label.Contains("Ceiling")) ceiling++;
                else other++;
            }

            Debug.Log(
                $"[MR2VR][Probe] surfacesTotal={table + wall + floor + ceiling + other} " +
                $"hasRoom={provider.HasRoom} " +
                $"providerWalls={provider.Walls.Count} " +
                $"providerFloor={(provider.Floor != null)} " +
                $"providerCeiling={(provider.Ceiling != null)} " +
                $"roomCenter={provider.RoomCenter}",
                this);

            Debug.Log(
                $"[MR2VR][Probe] classification: Table={table} Wall={wall} Floor={floor} " +
                $"Ceiling={ceiling} Other={other}",
                this);
        }

        void LogProxies(
            RoomShellProxyGenerator generator,
            RoomShellFractureController fracture,
            Camera camera)
        {
            if (generator == null)
            {
                Debug.LogWarning(
                    "[MR2VR][Probe] No RoomShellProxyGenerator; proxyCount=0.", this);
                return;
            }

            IReadOnlyList<RoomShellProxy> proxies = generator.Proxies;

            // The fracture cuts each surface into the same number of cells regardless of how
            // big it is, so the estimate is just that setting times the surface count. A one
            // metre table and a whole wall therefore cost the same and look very different.
            const int fragmentsPerSurface = 30;

            var renderers = new List<Renderer>();
            int visible = 0;

            foreach (RoomShellProxy proxy in proxies)
            {
                if (proxy.GameObject == null)
                    continue;

                var renderer = proxy.GameObject.GetComponent<Renderer>();
                if (renderer == null)
                    continue;

                renderers.Add(renderer);
                if (IsAnyCornerInView(renderer.bounds, camera))
                    visible++;
            }

            float coverage = EstimateCoverage(renderers, camera, forwardOnly: false);
            float frontCoverage = EstimateCoverage(renderers, camera, forwardOnly: true);

            Debug.Log(
                $"[MR2VR][Probe] proxyCount={proxies.Count} " +
                $"visibleProxies={visible} " +
                $"fragmentCountEstimated={proxies.Count * fragmentsPerSurface} " +
                $"visibleFragmentsEstimated={visible * fragmentsPerSurface} " +
                $"rigCount={(fracture != null ? fracture.RigCount : -1)}",
                this);

            Debug.Log(
                $"[MR2VR][Probe] cameraVisibleCoverage={coverage * 100f:F1}% " +
                $"frontHemisphereCoverage={frontCoverage * 100f:F1}% " +
                $"grid={coverageGridResolution}x{coverageGridResolution}",
                this);

            Debug.Log($"[MR2VR][Probe] {Verdict(proxies.Count, coverage)}", this);
        }

        static string Verdict(int proxyCount, float coverage)
        {
            if (proxyCount <= 5 || coverage < 0.1f)
            {
                return "VERDICT=C - too little real surface to break. The room fracture " +
                    "cannot carry this transition; a virtual shell around the viewer would " +
                    "have to stand in for the room.";
            }

            if (coverage < 0.5f)
            {
                return "VERDICT=B - enough fragments, but they cover too little of the view. " +
                    "This would read as furniture breaking rather than the room coming apart.";
            }

            return "VERDICT=A - the surfaces cover enough of the view for the room fracture " +
                "to carry the transition.";
        }

        /// <summary>
        /// Fraction of the viewport covered by any proxy, sampled on a grid.
        ///
        /// Each surface is treated as its projected bounding rectangle. That over-estimates a
        /// non-rectangular wall and under-estimates nothing, so a low number here is decisive
        /// while a high one is optimistic - which is the right way round for a go/no-go.
        /// </summary>
        float EstimateCoverage(List<Renderer> renderers, Camera camera, bool forwardOnly)
        {
            int n = coverageGridResolution;
            var covered = new bool[n * n];

            foreach (Renderer renderer in renderers)
            {
                Bounds bounds = renderer.bounds;

                float minX = float.MaxValue, minY = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue;
                bool anyInFront = false;

                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((c & 1) == 0 ? -1 : 1,
                                    (c & 2) == 0 ? -1 : 1,
                                    (c & 4) == 0 ? -1 : 1));

                    Vector3 viewport = camera.WorldToViewportPoint(corner);

                    // Behind the eye projects to a mirrored point in front of it, which would
                    // paint coverage where there is none.
                    if (viewport.z <= 0f)
                        continue;

                    anyInFront = true;
                    minX = Mathf.Min(minX, viewport.x);
                    maxX = Mathf.Max(maxX, viewport.x);
                    minY = Mathf.Min(minY, viewport.y);
                    maxY = Mathf.Max(maxY, viewport.y);
                }

                if (!anyInFront)
                    continue;

                if (forwardOnly &&
                    Vector3.Dot((bounds.center - camera.transform.position).normalized,
                        camera.transform.forward) < 0.5f)
                {
                    continue;
                }

                int x0 = Mathf.Clamp(Mathf.FloorToInt(minX * n), 0, n - 1);
                int x1 = Mathf.Clamp(Mathf.CeilToInt(maxX * n), 0, n - 1);
                int y0 = Mathf.Clamp(Mathf.FloorToInt(minY * n), 0, n - 1);
                int y1 = Mathf.Clamp(Mathf.CeilToInt(maxY * n), 0, n - 1);

                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                        covered[y * n + x] = true;
                }
            }

            int count = 0;
            foreach (bool cell in covered)
            {
                if (cell)
                    count++;
            }

            return (float)count / covered.Length;
        }

        static bool IsAnyCornerInView(Bounds bounds, Camera camera)
        {
            for (int c = 0; c < 8; c++)
            {
                Vector3 corner = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((c & 1) == 0 ? -1 : 1,
                                (c & 2) == 0 ? -1 : 1,
                                (c & 4) == 0 ? -1 : 1));

                Vector3 viewport = camera.WorldToViewportPoint(corner);
                if (viewport.z > 0f &&
                    viewport.x is >= 0f and <= 1f &&
                    viewport.y is >= 0f and <= 1f)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Where the drone's ground plane would land, and how close that is to the surfaces
        /// the fracture would be breaking.
        ///
        /// The ground is a ten kilometre plane. If it comes up at nearly the same height as
        /// the real floor or a table, the two fight for the depth buffer across the entire
        /// room, and the answer is not to nudge it but to bring it up later - once the map has
        /// grown enough that the ground is nowhere near anything real.
        /// </summary>
        void LogGround(RoomShellProxyGenerator generator)
        {
            GameObject ground = GameObject.Find("VR Drone Ground");
            bool environmentActive = ground != null && ground.activeInHierarchy;

            float groundY = ground != null ? ground.transform.position.y : float.NaN;
            var groundRenderer = ground != null ? ground.GetComponent<Renderer>() : null;

            Debug.Log(
                $"[MR2VR][Probe] groundExists={(ground != null)} " +
                $"environmentActive={environmentActive} " +
                $"groundWorldY={groundY:F3} " +
                $"groundBoundsMinY={(groundRenderer != null ? groundRenderer.bounds.min.y : float.NaN):F3} " +
                $"groundBoundsMaxY={(groundRenderer != null ? groundRenderer.bounds.max.y : float.NaN):F3}",
                this);

            if (generator == null || ground == null)
                return;

            float minY = float.MaxValue, maxY = float.MinValue;
            float closest = float.MaxValue;
            string closestType = "none";

            foreach (RoomShellProxy proxy in generator.Proxies)
            {
                if (proxy.GameObject == null)
                    continue;

                float y = proxy.GameObject.transform.position.y;
                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y);

                float gap = Mathf.Abs(y - groundY);
                if (gap < closest)
                {
                    closest = gap;
                    closestType = proxy.Type.ToString();
                }
            }

            Debug.Log(
                $"[MR2VR][Probe] roomMaskDepthMin={minY:F3} roomMaskDepthMax={maxY:F3} " +
                $"nearestGroundDistance={closest:F3}m to {closestType}",
                this);

            if (closest < groundClearanceWarning)
            {
                Debug.LogWarning(
                    $"[MR2VR][Probe][GroundRisk] The drone ground would sit {closest:F3}m from " +
                    $"the nearest {closestType} surface. Bringing the environment up before the " +
                    $"fracture would put a ten kilometre plane into a depth fight with the real " +
                    $"room; hold it back until the map has grown.",
                    this);
            }
        }

        void LogSky()
        {
            var sky = FindAnyObjectByType<Drone.SkyShell.VRSkyShellFractureController>();

            Debug.Log(
                $"[MR2VR][Probe] skyShellPresent={(sky != null)} " +
                $"skyRadius={(sky != null ? sky.ShellRadius : -1f)} " +
                $"skyVisible={(sky != null && sky.IsVisible)} " +
                $"plannedTransitionQueue=2050 (mask 1999 writes depth first; " +
                $"Background 1000 would paint alpha 1 over the whole view before the mask runs)",
                this);
        }
    }
}
