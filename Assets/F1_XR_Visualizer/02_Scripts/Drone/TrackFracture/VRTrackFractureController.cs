using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace F1XR.Drone.TrackFracture
{
    /// <summary>
    /// Breaks the actual circuit the viewer is standing in, on the way back to the real room.
    ///
    /// The geometry that falls is the Bahrain mesh itself - same vertices, same UVs, same
    /// materials - regrouped into spatial cells rather than cut. Where a piece leaves, an
    /// alpha-only mask opens onto passthrough. Nothing is captured, nothing is proxied, and
    /// no part of the real room is used as a stand-in for the virtual one.
    ///
    /// Everything expensive happens in Prepare, on the way in, while the viewer is busy
    /// arriving. Pressing exit only flips renderers and starts a timer; building a hundred
    /// and fifty meshes at that moment would stall the frame exactly when motion is most
    /// visible.
    ///
    /// If a track has no matching profile the controller simply reports that it is not ready
    /// and the existing blink exit runs untouched. A circuit nobody has profiled yet must
    /// never be able to trap the user in VR.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VRTrackFractureController : MonoBehaviour
    {
        [Header("Profiles")]
        [Tooltip("Extra or overriding profiles authored in the scene. The built-in ones below " +
            "are always consulted as well.")]
        [SerializeField] TrackFractureProfile[] profiles = System.Array.Empty<TrackFractureProfile>();

        /// <summary>
        /// Checked in addition to whatever the scene serialized. A component placed before a
        /// track was profiled keeps the array it was serialized with, so widening only the
        /// field initialiser silently leaves that instance behind - which is exactly what
        /// happened when Suzuka was added and the scene kept asking for Bahrain.
        /// </summary>
        static readonly TrackFractureProfile[] BuiltInProfiles =
        {
            new()
            {
                profileName = "Suzuka",
                mapNameContains = "Suzuka",
                targetRendererNames = new[]
                {
                    "suzuka_2001",        // road surface, 29.1% of the view - and 296 submeshes
                    "suzuka_rect_fill",   // 15.4%
                    "Cube",               //  4.5%  ground slab, Ground.mat
                    "suzuka_rect_walls",  //  2.6%
                    "suzuka_2001_groove"  //  0.6%  skid marks, sits on the road
                },

                // Eight, not six. At the immersive scale a sixth of the circuit is a slab tens
                // of metres across, and one of those coming away reads as a map being torn
                // into plates rather than a surface collapsing. Stepped one notch at a time so
                // the fragment count and prepare cost stay legible.
                gridResolution = 8
            },
            new()   // Bahrain, from the profile's own defaults
        };

        [Header("Timing")]
        [Tooltip("Seconds added each time the crack steps out to the next ring of cells.")]
        [SerializeField, Min(0f)] float propagationStep = 0.12f;

        [Tooltip("Gap between a cell cracking and its pieces actually letting go.")]
        [SerializeField, Min(0f)] float looseDuration = 0.15f;

        [SerializeField, Min(0f)] float timingJitter = 0.06f;

        [Tooltip("Seconds a piece takes to fall once it has detached.")]
        [SerializeField, Min(0.05f)] float fallDuration = 1.1f;

        [Header("Motion")]
        [Tooltip("How far a piece rises before it drops, as a fraction of one cell. Zero by " +
            "default. Every piece lifts by the same amount at nearly the same moment, so any " +
            "value at all makes the whole circuit rise and fall together - it reads as the " +
            "map bouncing once, not as pieces coming loose. Raise it only once the timing is " +
            "spread out enough for the lift to look like separate pieces letting go.")]
        [SerializeField, Range(0f, 0.5f)] float liftInCellSizes;

        [Tooltip("How far a piece falls, as a fraction of one cell. Measured against the cell " +
            "rather than in raw units because the map is a thousand times larger in VR than " +
            "on the table - a distance that reads as a nudge on the tabletop is a " +
            "hundreds-of-metres plunge from inside the circuit.")]
        [SerializeField, Range(0f, 4f)] float fallInCellSizes = 0.9f;

        [SerializeField, Range(0f, 90f)] float maxTumbleDegrees = 25f;

        [Tooltip("Holds every piece level. Only for isolating a pivot fault: with it on, " +
            "anything wrong is a distance problem; with it off, anything new is a pivot " +
            "problem. Left on it also makes the break read as slabs settling in place rather " +
            "than pieces coming away, so it belongs off in normal use.")]
        [SerializeField] bool disableTumbleForTest;

        [Tooltip("Fall shape. Ease-in reads as gravity taking hold; anything with a bounce " +
            "or an overshoot reads as motion sickness.")]
        [SerializeField] AnimationCurve fallCurve = GravityCurve();

        [Header("Origin")]
        [Tooltip("Metres in front of the viewer, in placement-root units, for the point the " +
            "break starts from. Zero starts it right where they are standing.")]
        [SerializeField] float fractureOriginForwardOffset;

        [Header("Dust")]
        [SerializeField] ShellDustEmitterBridge dust;

        readonly List<MeshRenderer> targets = new();
        readonly TrackFractureCells cells = new();
        readonly TrackFragmentBuilder.Result built = new();
        readonly List<GameObject> revealObjects = new();
        readonly List<bool> revealShown = new();
        readonly List<Vector3> fragmentLiftAxis = new();
        readonly List<Quaternion> fragmentTumble = new();

        Transform placementRoot;
        Material revealMaterial;
        TrackFractureProfile activeProfile;

        bool isPrepared;
        bool isRunning;
        float elapsed;
        float totalDuration;
        float totalWeight;

        /// <summary>True once fragments exist and the break can be played.</summary>
        public bool IsPrepared => isPrepared;

        public bool IsRunning => isRunning;

        public int FragmentCount => built.Fragments.Count;

        public int RevealCount
        {
            get
            {
                int n = 0;
                foreach (GameObject go in revealObjects)
                {
                    if (go != null)
                        n++;
                }

                return n;
            }
        }

        /// <summary>
        /// How much of the target geometry has started to come away, measured by surface
        /// area rather than by counting cells.
        ///
        /// This is what decides when the exit may commit. A cell holding two lamp posts and a
        /// cell holding half the terrain are one cell each, so a cell count can read
        /// three-quarters done while the view is still almost entirely virtual - and the
        /// blink then has to do the work the break was supposed to do.
        /// </summary>
        public float Coverage
        {
            get
            {
                if (!isRunning || totalWeight <= 0f)
                    return 0f;

                return Mathf.Clamp01(cells.DetachedWeight(elapsed) / totalWeight);
            }
        }

        /// <summary>Fraction of the used cells whose pieces have started to come away.</summary>
        public float Progress
        {
            get
            {
                int used = cells.UsedCount();
                if (!isRunning || used == 0)
                    return 0f;

                int detached = 0;
                for (int i = 0; i < cells.Count; i++)
                {
                    if (cells[i].Used && elapsed >= cells[i].DetachTime)
                        detached++;
                }

                return (float)detached / used;
            }
        }

        static AnimationCurve GravityCurve() =>
            new(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 2f, 2f));

        // ------------------------------------------------------------------ prepare

        /// <summary>
        /// Builds every fragment up front. Yields between renderers so the cost is spread
        /// over a handful of frames instead of landing in one.
        /// </summary>
        public IEnumerator Prepare(Transform placement, Transform visualRoot)
        {
            Cleanup();

            if (placement == null || visualRoot == null)
                yield break;

            placementRoot = placement;

            Material reveal = ResolveRevealMaterial();
            if (reveal == null)
                yield break;

            // Always, not only on failure. Matching one target out of ten is just as wrong as
            // matching none, and without the inventory there is no way to tell whether the
            // profile is stale or the track never loaded.
            Debug.Log($"[TrackFracture] {DescribeVisual(visualRoot)}", this);

            activeProfile = ResolveProfile(visualRoot);
            if (activeProfile == null)
            {
                // Nothing to break is a normal outcome, not a fault: an unprofiled circuit
                // just leaves the exit as it was. Log what was actually there so the next
                // track only has to be looked at once.
                Debug.Log(
                    $"[TrackFracture] No profile has targets in this track; exit stays as it " +
                    $"was. {DescribeVisual(visualRoot)}",
                    this);
                yield break;
            }

            cells.Build(ComputeLocalBounds(), activeProfile);

            var revealTris = new List<List<int>>(cells.Count);
            for (int i = 0; i < cells.Count; i++)
                revealTris.Add(null);

            var revealVerts = new List<Vector3>();
            var revealMap = new Dictionary<long, int>();

            float startedAt = Time.realtimeSinceStartup;

            foreach (MeshRenderer target in targets)
            {
                TrackFragmentBuilder.SplitRenderer(
                    target, placementRoot, cells, built, revealTris, revealVerts, revealMap);

                yield return null;
            }

            for (int i = 0; i < cells.Count; i++)
            {
                GameObject go = TrackFragmentBuilder.BuildRevealMesh(
                    i, revealVerts, revealTris[i], placementRoot, reveal, built);
                revealObjects.Add(go);
                revealShown.Add(false);
            }

            var random = new System.Random(activeProfile.randomSeed + 977);
            foreach (TrackFragmentBuilder.Fragment fragment in built.Fragments)
            {
                fragmentLiftAxis.Add(Vector3.up);
                fragmentTumble.Add(Quaternion.Euler(
                    NextSigned(random) * maxTumbleDegrees,
                    NextSigned(random) * maxTumbleDegrees,
                    NextSigned(random) * maxTumbleDegrees));
            }

            totalWeight = cells.TotalWeight();
            isPrepared = true;

            Debug.Log(
                $"[TrackFracture][Prepare] fragments={built.Fragments.Count} " +
                $"cells={cells.UsedCount()} totalGeometryWeight={totalWeight:F4}",
                this);

            Debug.Log(
                $"[TrackFracture] prepared profile={activeProfile.profileName} " +
                $"targets={targets.Count} cells={cells.Count} usedCells={cells.UsedCount()} " +
                $"fragments={built.Fragments.Count} submeshes={built.SubMeshCount} " +
                $"revealMeshes={RevealCount} drawCallsWhileBreaking=" +
                $"{built.SubMeshCount + RevealCount} " +
                $"tris={built.TriangleCount} in {(Time.realtimeSinceStartup - startedAt) * 1000f:F0}ms",
                this);
        }

        // ------------------------------------------------------------------ run

        /// <summary>
        /// Swaps the originals out for the fragments and starts the clock. The swap has to be
        /// invisible: same vertices, same materials, same place.
        /// </summary>
        public bool BeginFracture(Transform viewer)
        {
            if (!isPrepared || isRunning)
                return false;

            Vector2 origin = Vector2.zero;
            if (viewer != null && placementRoot != null)
            {
                Vector3 point = viewer.position + viewer.forward * fractureOriginForwardOffset;
                Vector3 local = placementRoot.InverseTransformPoint(point);
                origin = new Vector2(local.x, local.z);
            }

            cells.ComputeTiming(
                origin, propagationStep, looseDuration, timingJitter,
                activeProfile != null ? activeProfile.randomSeed : 0);

            foreach (MeshRenderer target in targets)
            {
                if (target != null)
                    target.enabled = false;
            }

            foreach (TrackFragmentBuilder.Fragment fragment in built.Fragments)
            {
                if (fragment.Renderer != null)
                    fragment.Renderer.enabled = true;
            }

            elapsed = 0f;
            totalDuration = cells.LastDetachTime() + fallDuration;
            isRunning = true;

            LogDisplacementConversion();

            Debug.Log(
                $"[TrackFracture] begin originLocalXZ={origin} " +
                $"duration={totalDuration:F2}s fragments={built.Fragments.Count} " +
                $"cellSize={cells.CellSize:F4} " +
                $"liftWorld={placementRoot.TransformVector(Vector3.up * (cells.CellSize * liftInCellSizes)).magnitude:F2}m " +
                $"fallWorld={placementRoot.TransformVector(Vector3.up * (cells.CellSize * fallInCellSizes)).magnitude:F2}m " +
                $"tumble={(disableTumbleForTest ? "OFF (test)" : "on")}",
                this);

            return true;
        }

        void Update()
        {
            if (!isRunning)
                return;

            elapsed += Time.deltaTime;
            Step();
        }

        /// <summary>
        /// Moves a piece by a displacement expressed in the map's own units.
        ///
        /// Every renderer under a circuit sits at a different depth in the transform chain,
        /// and a glTF import puts thousands of model units where the map has fractions of one.
        /// Writing a map-sized number straight into a fragment's localPosition therefore means
        /// something different for every renderer - which is what made the terrain come apart
        /// in slabs, each drifting a different distance. Converting through world space makes
        /// one cell-width mean one cell-width everywhere.
        /// </summary>
        void MoveFragment(TrackFragmentBuilder.Fragment fragment, Vector3 displacementPlacementLocal)
        {
            Transform parent = fragment.Transform.parent;

            // TransformVector, not TransformDirection: direction discards scale, and scale is
            // the entire problem being corrected here.
            Vector3 world = placementRoot.TransformVector(displacementPlacementLocal);
            Vector3 parentLocal = parent != null
                ? parent.InverseTransformVector(world)
                : world;

            fragment.Transform.localPosition = fragment.InitialLocalPosition + parentLocal;
        }

        /// <summary>
        /// One full fall's worth of displacement, traced through the conversion for a few
        /// fragments off different renderers. The source-local numbers should differ wildly -
        /// that is the transform chain - while the world numbers should all agree. If they do
        /// not, the units are still mixed.
        /// </summary>
        void LogDisplacementConversion()
        {
            if (placementRoot == null || built.Fragments.Count == 0)
                return;

            Vector3 downPlacement = -placementRoot.InverseTransformDirection(Vector3.up).normalized;
            Vector3 sample = downPlacement * (cells.CellSize * fallInCellSizes);

            var seen = new List<string>();
            int reported = 0;

            foreach (TrackFragmentBuilder.Fragment fragment in built.Fragments)
            {
                Transform parent = fragment.Transform.parent;
                string owner = parent != null ? parent.name : "(none)";
                if (seen.Contains(owner))
                    continue;

                seen.Add(owner);

                Vector3 world = placementRoot.TransformVector(sample);
                Vector3 parentLocal = parent != null
                    ? parent.InverseTransformVector(world)
                    : world;

                Debug.Log(
                    $"[TrackFracture][units] '{owner}' placementLocal={sample.magnitude:F5} " +
                    $"world={world.magnitude:F4}m sourceLocal={parentLocal.magnitude:F2} " +
                    $"pivot={fragment.InitialLocalPosition}",
                    this);

                if (++reported >= 3)
                    break;
            }
        }

        void Step()
        {
            // Both taken once per frame in the map's space, then converted per fragment.
            Vector3 upPlacement = placementRoot != null
                ? placementRoot.InverseTransformDirection(Vector3.up).normalized
                : Vector3.up;
            Vector3 downPlacement = -upPlacement;

            for (int i = 0; i < built.Fragments.Count; i++)
            {
                TrackFragmentBuilder.Fragment fragment = built.Fragments[i];
                if (fragment.Transform == null)
                    continue;

                TrackFractureCells.Cell cell = cells[fragment.CellId];
                float since = elapsed - cell.CrackTime;
                if (since <= 0f)
                    continue;

                float detachAt = cell.DetachTime - cell.CrackTime;

                float lift = cells.CellSize * liftInCellSizes;
                float fall = cells.CellSize * fallInCellSizes;

                if (since < detachAt)
                {
                    // Loose: barely off the surface, just enough to read as "this let go".
                    float u = Mathf.Clamp01(since / Mathf.Max(detachAt, 0.0001f));
                    MoveFragment(fragment, upPlacement * (lift * u));
                    continue;
                }

                RevealCell(fragment.CellId);

                float progress = Mathf.Clamp01((since - detachAt) / fallDuration);
                float fallen = fallCurve != null ? fallCurve.Evaluate(progress) : progress;

                MoveFragment(fragment, upPlacement * lift + downPlacement * (fall * fallen));

                fragment.Transform.localRotation = disableTumbleForTest
                    ? Quaternion.identity
                    : Quaternion.SlerpUnclamped(Quaternion.identity, fragmentTumble[i], progress);

                if (progress >= 1f && fragment.Renderer != null && fragment.Renderer.enabled)
                    fragment.Renderer.enabled = false;
            }

            if (elapsed >= totalDuration)
                isRunning = false;
        }

        void RevealCell(int cellId)
        {
            if (cellId < 0 || cellId >= revealObjects.Count || revealShown[cellId])
                return;

            revealShown[cellId] = true;

            GameObject go = revealObjects[cellId];
            if (go == null)
                return;

            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.enabled = true;

            dust?.EmitAtCell(placementRoot, cells[cellId].Seed);
        }

        /// <summary>
        /// Called once by the exit, at the moment it decides to hide the rest behind the
        /// blink. Everything needed to judge whether the break had actually done its job by
        /// then, in one line.
        /// </summary>
        public void LogCommitState()
        {
            Debug.Log(
                $"[TrackFracture][Commit] cellProgress={Progress:F2} " +
                $"geometryCoverage={Coverage:F3} " +
                $"detachedWeight={cells.DetachedWeight(elapsed):F4} " +
                $"totalWeight={totalWeight:F4} " +
                $"remainingCells={cells.RemainingCells(elapsed)} " +
                $"remainingFragments={RemainingFragmentCount()}",
                this);
        }

        int RemainingFragmentCount()
        {
            int remaining = 0;
            foreach (TrackFragmentBuilder.Fragment fragment in built.Fragments)
            {
                if (fragment.Renderer != null && fragment.Renderer.enabled)
                    remaining++;
            }

            return remaining;
        }

        // ------------------------------------------------------------------ cleanup

        /// <summary>
        /// Puts the circuit back exactly as it was. The originals were only ever switched
        /// off, never modified, so restoring them is a flag; everything this built is thrown
        /// away so a second trip in starts from nothing.
        /// </summary>
        public void Cleanup()
        {
            int restored = 0;
            foreach (MeshRenderer target in targets)
            {
                if (target != null)
                {
                    target.enabled = true;
                    restored++;
                }
            }

            if (restored > 0 || built.OwnedObjects.Count > 0)
            {
                Debug.Log(
                    $"[TrackFracture] cleanup restored={restored} destroyedObjects=" +
                    $"{built.OwnedObjects.Count} destroyedMeshes={built.OwnedMeshes.Count}",
                    this);
            }

            targets.Clear();

            foreach (GameObject go in built.OwnedObjects)
                DestroySafely(go);

            foreach (Mesh mesh in built.OwnedMeshes)
                DestroySafely(mesh);

            built.Fragments.Clear();
            built.OwnedObjects.Clear();
            built.OwnedMeshes.Clear();
            built.TriangleCount = 0;
            built.SubMeshCount = 0;

            revealObjects.Clear();
            revealShown.Clear();
            fragmentLiftAxis.Clear();
            fragmentTumble.Clear();
            cells.Clear();

            placementRoot = null;
            activeProfile = null;
            isPrepared = false;
            isRunning = false;
            elapsed = 0f;
            totalDuration = 0f;
            totalWeight = 0f;
        }

        void OnDestroy()
        {
            Cleanup();
            DestroySafely(revealMaterial);
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// Picks a profile by whether its target renderers are actually present, not by the
        /// track's name. The name was only ever a guess at "is this the circuit I know", and
        /// it guesses wrong the moment a map instance is named something unexpected or the
        /// visual root is still empty. Whether the renderers exist is the real requirement,
        /// so it is the thing that gets tested. A name hint still breaks ties.
        /// </summary>
        TrackFractureProfile ResolveProfile(Transform visualRoot)
        {
            string trackName = visualRoot.childCount > 0
                ? visualRoot.GetChild(0).name
                : visualRoot.name;

            TrackFractureProfile best = null;
            int bestCount = 0;
            bool bestNameMatched = false;

            var candidates = new List<TrackFractureProfile>();
            if (profiles != null)
                candidates.AddRange(profiles);
            candidates.AddRange(BuiltInProfiles);

            foreach (TrackFractureProfile profile in candidates)
            {
                if (profile == null)
                    continue;

                CollectTargets(visualRoot, profile);
                int found = targets.Count;
                bool nameMatched = profile.Matches(trackName);

                Debug.Log(
                    $"[TrackFracture] profile '{profile.profileName}': {found} of " +
                    $"{(profile.targetRendererNames != null ? profile.targetRendererNames.Length : 0)} " +
                    $"target renderers present, nameMatch={nameMatched}",
                    this);

                if (found == 0)
                    continue;

                bool better = found > bestCount ||
                    (found == bestCount && nameMatched && !bestNameMatched);

                if (better)
                {
                    best = profile;
                    bestCount = found;
                    bestNameMatched = nameMatched;
                }
            }

            // CollectTargets left the list holding whichever profile was checked last.
            if (best != null)
                CollectTargets(visualRoot, best);
            else
                targets.Clear();

            return best;
        }

        /// <summary>
        /// What is actually under the visual root, for the case where nothing matched. The
        /// answer is usually either "nothing at all" - the map prefab was never instantiated
        /// because the dataset's circuit has no track asset - or a set of renderer names
        /// nobody has profiled yet.
        /// </summary>
        static string DescribeVisual(Transform visualRoot)
        {
            var renderers = visualRoot.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length == 0)
            {
                return $"visualRoot='{visualRoot.name}' children={visualRoot.childCount} " +
                    "renderers=0 (the map prefab was probably never instantiated).";
            }

            var names = new List<string>();
            foreach (MeshRenderer r in renderers)
            {
                if (names.Count >= 25)
                    break;
                names.Add(r.name);
            }

            string firstChild = visualRoot.childCount > 0 ? visualRoot.GetChild(0).name : "(none)";
            return $"visualRoot='{visualRoot.name}' firstChild='{firstChild}' " +
                $"renderers={renderers.Length} first names: {string.Join(", ", names)}";
        }

        void CollectTargets(Transform visualRoot, TrackFractureProfile profile)
        {
            targets.Clear();

            // By name, not by hierarchy path. The glTF import nests these six levels deep
            // behind Sketchfab_model/root/GLTF_SceneRootNode, and hard-coding that string
            // would break on the next re-export.
            foreach (MeshRenderer renderer in visualRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer == null || !profile.IsTarget(renderer.name))
                    continue;

                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                    continue;

                if (!filter.sharedMesh.isReadable)
                {
                    Debug.LogWarning(
                        $"[TrackFracture] '{renderer.name}' mesh is not readable; enable " +
                        "Read/Write on the model import or it cannot be broken.",
                        this);
                    continue;
                }

                targets.Add(renderer);
            }
        }

        Bounds ComputeLocalBounds()
        {
            var bounds = new Bounds();
            bool started = false;

            foreach (MeshRenderer renderer in targets)
            {
                Bounds world = renderer.bounds;
                for (int x = -1; x <= 1; x += 2)
                {
                    for (int y = -1; y <= 1; y += 2)
                    {
                        for (int z = -1; z <= 1; z += 2)
                        {
                            Vector3 corner = world.center +
                                Vector3.Scale(world.extents, new Vector3(x, y, z));
                            Vector3 local = placementRoot.InverseTransformPoint(corner);

                            if (!started)
                            {
                                bounds = new Bounds(local, Vector3.zero);
                                started = true;
                            }
                            else
                            {
                                bounds.Encapsulate(local);
                            }
                        }
                    }
                }
            }

            return bounds;
        }

        Material ResolveRevealMaterial()
        {
            if (revealMaterial != null)
                return revealMaterial;

            Shader shader = Shader.Find("F1XR/ShellPassthroughReveal");
            if (shader == null)
            {
                Debug.LogError(
                    "[TrackFracture] ShellPassthroughReveal shader missing. Without it a " +
                    "detached piece would leave the clear colour showing, not the room.",
                    this);
                return null;
            }

            revealMaterial = new Material(shader) { name = "TrackFractureReveal" };
            revealMaterial.SetFloat("_OutputAlpha", 0f);
            return revealMaterial;
        }

        static float NextSigned(System.Random random) =>
            (float)(random.NextDouble() * 2.0 - 1.0);

        static void DestroySafely(Object target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }
    }

    /// <summary>
    /// Optional hook for the existing dust emitter. Kept as a tiny separate component so the
    /// fracture does not have to know which dust system the project is using this week.
    /// </summary>
    public sealed class ShellDustEmitterBridge : MonoBehaviour
    {
        [SerializeField] Experience.Fracture.ShellDustEmitter emitter;

        public void EmitAtCell(Transform placementRoot, Vector2 localXZ)
        {
            if (emitter == null || placementRoot == null)
                return;

            emitter.EmitAt(placementRoot.TransformPoint(new Vector3(localXZ.x, 0f, localXZ.y)));
        }
    }
}
