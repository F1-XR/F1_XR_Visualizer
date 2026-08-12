using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace F1XR.Experience.Room
{
    /// <summary>
    /// Collects the walls, floor and ceiling of the user's real room and nothing else.
    /// No destruction, no materials, no Passthrough.
    ///
    /// Source of the data: AR Foundation's <see cref="ARPlaneManager"/>, backed by the
    /// "Meta Quest: Planes" OpenXR feature, which maps Meta's semantic labels onto
    /// <see cref="PlaneClassifications"/>. MRUK is installed but is not used here: its
    /// <c>MRUK.Awake</c> logs an error unless an <c>OVRCameraRig</c> is present, and this
    /// project runs on an XR Origin rig that must not be replaced.
    ///
    /// The AR planes themselves are the cache. This component does not copy their pose or
    /// size into a parallel structure that could go stale; it only classifies them and
    /// reports when the set changes. Nothing here queries per frame.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RoomSurfaceProvider : MonoBehaviour
    {
        [SerializeField] ARPlaneManager planeManager;

        [Tooltip("Walls are vertical planes. The shared XR Origin prefab requests " +
            "horizontal planes only, so vertical detection is added here at runtime " +
            "instead of editing that prefab.")]
        [SerializeField] bool requestVerticalPlanes = true;

        [Tooltip("Step 2 verification: logs each discovered plane once, whatever its " +
            "classification, so the values the device actually returns can be read " +
            "rather than assumed. Never logs per frame.")]
        [SerializeField] bool logDiscoveredPlanes = true;

        [Tooltip("The AR plane prefab carries a MeshCollider on the Default layer. Before " +
            "vertical planes were requested there were no wall planes and therefore no " +
            "wall colliders, so turning vertical detection on silently drops an " +
            "invisible collider over every wall, which intercepts XR interactor rays and " +
            "the Physics.RaycastAll(~0) calls used by the panel and scale controllers. " +
            "Disabling those colliders restores the physics behaviour that existed before " +
            "this component. The plane visualization itself is left alone.")]
        [SerializeField] bool disableWallPlaneColliders = true;

        [Tooltip("Require alignment == Vertical before a wall-labelled plane counts as a " +
            "wall. Turn off if the device reports real walls as NotAxisAligned; any plane " +
            "rejected by this test is logged as a warning either way.")]
        [SerializeField] bool requireVerticalWalls = true;

        readonly List<ARPlane> walls = new();
        readonly HashSet<TrackableId> loggedPlanes = new();
        bool subscribed;
        bool hasSavedDetectionMode;
        PlaneDetectionMode savedDetectionMode;

        public IReadOnlyList<ARPlane> Walls => walls;

        public ARPlane Floor { get; private set; }

        public ARPlane Ceiling { get; private set; }

        public bool HasRoom => Floor != null || Ceiling != null || walls.Count > 0;

        /// <summary>Raised when the set of room surfaces changes. Never raised per frame.</summary>
        public event Action SurfacesChanged;

        /// <summary>
        /// Approximate middle of the room. Used to work out which way a surface normal
        /// points, rather than assuming a convention.
        /// </summary>
        public Vector3 RoomCenter { get; private set; }

        void Awake()
        {
            if (planeManager == null)
                planeManager = FindAnyObjectByType<ARPlaneManager>();
        }

        void OnEnable()
        {
            if (planeManager == null)
            {
                Debug.LogWarning(
                    "[RoomShell] No ARPlaneManager found. Room surfaces are unavailable.",
                    this);
                return;
            }

            if (requestVerticalPlanes)
            {
                // Only ever add modes. Other systems (track placement) rely on horizontal
                // planes, so nothing is taken away here. The original value is kept so
                // this component can put the scene back exactly as it found it.
                if (!hasSavedDetectionMode)
                {
                    savedDetectionMode = planeManager.requestedDetectionMode;
                    hasSavedDetectionMode = true;
                }

                planeManager.requestedDetectionMode |=
                    PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
            }

            if (!subscribed)
            {
                planeManager.trackablesChanged.AddListener(OnTrackablesChanged);
                subscribed = true;
            }

            // Planes discovered before this component woke up are already in trackables.
            Refresh();
        }

        void OnDisable()
        {
            if (planeManager != null && subscribed)
                planeManager.trackablesChanged.RemoveListener(OnTrackablesChanged);

            subscribed = false;

            // Leave plane detection the way it was found, so switching this component off
            // fully removes its footprint on the rest of the scene.
            //
            // Only while the plane subsystem is still alive, though. On play-mode exit the
            // OpenXR loader destroys the XrInstance before this OnDisable runs, and both the
            // getter and the setter of requestedDetectionMode call straight into the Meta
            // provider's native API, which then dereferences the freed instance and hard-kills
            // the Editor process. Nothing needs restoring once the subsystem is stopped.
            if (planeManager != null && hasSavedDetectionMode
                && planeManager.subsystem is { running: true })
            {
                planeManager.requestedDetectionMode = savedDetectionMode;
                hasSavedDetectionMode = false;
                Debug.Log(
                    $"[RoomShell] Disabled. requestedDetectionMode restored to " +
                    $"{planeManager.requestedDetectionMode} " +
                    $"(current={planeManager.currentDetectionMode}).",
                    this);
            }
        }

        void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARPlane> args)
        {
            Refresh();
        }

        /// <summary>Re-reads the current plane set and re-classifies it.</summary>
        public void Refresh()
        {
            walls.Clear();
            Floor = null;
            Ceiling = null;

            if (planeManager == null)
            {
                SurfacesChanged?.Invoke();
                return;
            }

            float bestFloorArea = 0f;
            float bestCeilingArea = 0f;
            int planeCount = 0;
            int unclassifiedCount = 0;
            int subsumedCount = 0;
            int invisibleWallCount = 0;
            int rejectedWallCount = 0;

            foreach (ARPlane plane in planeManager.trackables)
            {
                if (plane == null)
                    continue;

                planeCount++;

                if (logDiscoveredPlanes && loggedPlanes.Add(plane.trackableId))
                    LogPlane(plane);

                if (plane.classifications == PlaneClassifications.None)
                    unclassifiedCount++;

                if (plane.subsumedBy != null)
                {
                    subsumedCount++;
                    continue;
                }

                PlaneClassifications labels = plane.classifications;
                float area = plane.size.x * plane.size.y;

                // Device logs showed real room walls coming back as InvisibleWallFace, so
                // both labels count as a wall and both get a proxy, an inward normal and
                // their collider switched off.
                bool isWallLabel = (labels &
                    (PlaneClassifications.WallFace | PlaneClassifications.InvisibleWallFace)) != 0;
                bool isWall = isWallLabel &&
                    (!requireVerticalWalls || plane.alignment == PlaneAlignment.Vertical);

                if (disableWallPlaneColliders && isWallLabel)
                    DisablePlaneCollider(plane);

                if (isWall)
                {
                    walls.Add(plane);
                    if ((labels & PlaneClassifications.InvisibleWallFace) != 0)
                        invisibleWallCount++;
                }
                else if (isWallLabel)
                {
                    // Carries a wall label but failed the alignment test. Reported so a
                    // slanted or oddly reported wall does not disappear silently.
                    rejectedWallCount++;
                    Debug.LogWarning(
                        $"[RoomShell] Plane {plane.trackableId} is labelled " +
                        $"{labels} but alignment={plane.alignment}, not Vertical. " +
                        "Skipped as a wall.",
                        plane);
                }
                else if ((labels & PlaneClassifications.Floor) != 0)
                {
                    // A room can report more than one floor plane; keep the biggest.
                    if (Floor == null || area > bestFloorArea)
                    {
                        Floor = plane;
                        bestFloorArea = area;
                    }
                }
                else if ((labels & PlaneClassifications.Ceiling) != 0)
                {
                    if (Ceiling == null || area > bestCeilingArea)
                    {
                        Ceiling = plane;
                        bestCeilingArea = area;
                    }
                }
            }

            RoomCenter = CalculateRoomCenter();

            if (logDiscoveredPlanes)
            {
                Debug.Log(
                    $"[RoomShell][Planes] total={planeCount} walls={walls.Count} " +
                    $"(invisible={invisibleWallCount}, rejectedByAlignment={rejectedWallCount}) " +
                    $"floor={(Floor != null ? 1 : 0)} ceiling={(Ceiling != null ? 1 : 0)} " +
                    $"unclassified={unclassifiedCount} subsumed={subsumedCount}",
                    this);
            }

            SurfacesChanged?.Invoke();
        }

        /// <summary>
        /// Re-logs every plane currently known, including ones already logged. Use this
        /// to take a fresh reading of what the device reports.
        /// </summary>
        [ContextMenu("Log All Planes")]
        public void LogAllPlanes()
        {
            loggedPlanes.Clear();
            Refresh();
        }

        static void DisablePlaneCollider(ARPlane plane)
        {
            Collider planeCollider = plane.GetComponent<Collider>();
            if (planeCollider == null || !planeCollider.enabled)
                return;

            planeCollider.enabled = false;
            Debug.Log(
                $"[RoomShell] Disabled the collider on wall plane {plane.trackableId} " +
                $"({plane.classifications}) so it cannot intercept interactor rays.",
                plane);
        }

        void LogPlane(ARPlane plane)
        {
            int boundaryCount;
            try
            {
                boundaryCount = plane.boundary.Length;
            }
            catch (InvalidOperationException)
            {
                // Boundary data is not available for this plane yet.
                boundaryCount = -1;
            }

            Debug.Log(
                "[RoomShell][Plane]" +
                $" id={plane.trackableId}" +
                $" classification={plane.classifications}" +
                $" alignment={plane.alignment}" +
                $" position={plane.transform.position}" +
                $" normal={plane.normal}" +
                $" size=({plane.size.x:F2}, {plane.size.y:F2})" +
                $" boundary={boundaryCount}" +
                $" subsumed={(plane.subsumedBy != null)}",
                plane);
        }

        Vector3 CalculateRoomCenter()
        {
            bool any = false;
            Bounds bounds = default;

            void Encapsulate(ARPlane plane)
            {
                if (plane == null)
                    return;

                if (!any)
                {
                    bounds = new Bounds(plane.center, Vector3.zero);
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(plane.center);
                }
            }

            Encapsulate(Floor);
            Encapsulate(Ceiling);
            foreach (ARPlane wall in walls)
                Encapsulate(wall);

            return any ? bounds.center : Vector3.zero;
        }
    }
}
