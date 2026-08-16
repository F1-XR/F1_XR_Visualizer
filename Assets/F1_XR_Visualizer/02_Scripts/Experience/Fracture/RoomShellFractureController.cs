using System.Collections;
using System.Collections.Generic;
using F1XR.Experience.Room;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace F1XR.Experience.Fracture
{
    /// <summary>
    /// Breaks the room shell apart during the MR to VR change, and puts it back on the way
    /// home.
    ///
    /// This is the piece Step 1 left room for. It does not modify
    /// <see cref="ExperienceModeManager"/>: that component exposes BreakSequence and
    /// RebuildSequence as coroutine hooks precisely so the destruction could be dropped in
    /// later, and this simply assigns them. The manager still owns the order of the mode
    /// change; this only owns what breaking looks like.
    ///
    /// It also does not modify the room proxies. It reads each proxy's mesh to recover the
    /// polygon it was built from, then makes its own fragments alongside.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RoomShellFractureController : MonoBehaviour
    {
        [SerializeField] ExperienceModeManager experienceManager;
        [SerializeField] RoomShellProxyGenerator proxyGenerator;

        [Tooltip("Assign the hooks on the mode manager so the break plays during the " +
            "transition. Turn off to leave the mode change untouched.")]
        [SerializeField] bool driveModeTransition = true;

        [Tooltip("Turn off once the world-space VR fracture takes over the return to MR. " +
            "The physical room shell is the right thing to break on the way into VR, where " +
            "the user is looking at the real room, and the wrong thing on the way back, " +
            "where they are looking at the VR world. Leaving this on keeps the existing " +
            "behaviour; nothing here is deleted until the new route is proven on device.")]
        [SerializeField] bool driveReturnToMR = true;

        [Header("Fracture")]
        [SerializeField] ShellFractureRig.Settings fractureSettings =
            ShellFractureRig.Settings.Default;

        [Header("Per surface travel")]
        [Tooltip("Walls come away and drop the full height of the room.")]
        [SerializeField, Min(0f)] float wallFallDistance = 1.4f;

        [Tooltip("Ceiling pieces drop further; they start high up.")]
        [SerializeField, Min(0f)] float ceilingFallDistance = 1.8f;

        [Tooltip("The floor has nowhere to fall, so its pieces only sink away.")]
        [SerializeField, Min(0f)] float floorFallDistance = 0.35f;

        [Tooltip("Ceiling pieces must not drift into the room, so their normal drift is " +
            "forced to zero regardless of the shared setting.")]
        [SerializeField] bool zeroCeilingNormalDrift = true;

        [Header("Dust")]
        [SerializeField] ShellDustEmitter dust;
        [SerializeField] bool spawnDustIfMissing = true;

        [Header("Passthrough reveal")]
        [SerializeField] PassthroughTransitionController passthrough;

        [Tooltip("Seconds to wait after the last piece before handing back to the mode " +
            "manager, so the fade does not start on top of the break.")]
        [SerializeField, Min(0f)] float tailSeconds = 0.15f;

        [Header("Visual mode")]
        [Tooltip("Force the flat debug colour even during real transitions. Off means MR to " +
            "VR uses the passthrough mask and VR to MR uses spatial alpha reveal.")]
        [SerializeField] bool forceDebugGray;

        [Header("Surface stagger")]
        [Tooltip("Seconds between each surface starting its fracture. Surfaces are ordered " +
            "by distance from the camera: nearest cracks first, farthest last.")]
        [SerializeField, Min(0f)] float surfaceStaggerStep = 0.2f;

        [Header("Look")]
        [SerializeField] Color shellColor = new(0.78f, 0.76f, 0.72f, 1f);

        readonly List<ShellFractureRig> rigs = new();
        readonly List<float> surfaceDelays = new();
        readonly List<Transform> planeSpaces = new();
        Material sharedShellMaterial;   // debug gray
        Material maskMaterial;          // MR->VR passthrough mask
        Material revealMaterial;        // VR->MR spatial alpha holes
        Material shardMaterial;         // solid debris, both directions, after detach
        Coroutine activeRoutine;

        public int RigCount => rigs.Count;

        /// <summary>
        /// Fraction of the room that has come away. Counted by pieces released rather than
        /// pieces finished falling, because what matters to anything waiting on this is how
        /// much of the real room has stopped covering the incoming world.
        /// </summary>
        public float BreakCoverage
        {
            get
            {
                int total = TotalFragments;
                return total == 0 ? 0f : (float)ReleasedFragmentCount() / total;
            }
        }

        /// <summary>
        /// Whether there is any real surface to break, checked before a transition commits to
        /// the room being its carrier. A room that was never scanned reports nothing here, and
        /// the caller is expected to fall back rather than play a break with no pieces in it.
        /// </summary>
        public bool HasBreakableSurfaces
        {
            get
            {
                EnsureProxiesExist();
                return proxyGenerator != null && proxyGenerator.Proxies.Count > 0;
            }
        }

        /// <summary>
        /// Throws away the fragments and leaves the proxies hidden, which is the correct end
        /// state going into VR - the real room is not supposed to come back.
        /// </summary>
        public void ClearShellFragments() => ClearRigs();

        public int TotalFragments
        {
            get
            {
                int total = 0;
                foreach (ShellFractureRig rig in rigs)
                    total += rig.FragmentCount;

                return total;
            }
        }

        void Awake()
        {
            if (experienceManager == null)
                experienceManager = FindAnyObjectByType<ExperienceModeManager>();

            if (proxyGenerator == null)
                proxyGenerator = FindAnyObjectByType<RoomShellProxyGenerator>();
        }

        void OnEnable()
        {
            if (!driveModeTransition || experienceManager == null)
                return;

            experienceManager.BreakSequence = PlayBreakSequence;

            // Stand down from the return whenever the world-space fracture is present, so
            // which one runs never depends on component order. The old route is kept, not
            // deleted: it is one missing component away from coming back.
            bool worldFracturePresent =
                FindAnyObjectByType<VirtualGarageFractureController>() != null ||
                FindAnyObjectByType<VRWorldFractureController>() != null;
            if (driveReturnToMR && !worldFracturePresent)
            {
                experienceManager.RebuildSequence = PlayRebuildSequence;
                Debug.Log(
                    "[VR2MR][shell] Room shell OWNS the return. No VRWorldFractureController " +
                    "in the scene, so Return MR breaks the physical room shell as before.",
                    this);
            }
            else
            {
                Debug.Log(
                    $"[VR2MR][shell] standing down from the return " +
                    $"(driveReturnToMR={driveReturnToMR} " +
                    $"worldFracture={worldFracturePresent}).",
                    this);
            }
        }

        void OnDisable()
        {
            if (experienceManager == null)
                return;

            // Only clear the hooks if they are still ours.
            if (experienceManager.BreakSequence == PlayBreakSequence)
                experienceManager.BreakSequence = null;

            if (experienceManager.RebuildSequence == PlayRebuildSequence)
                experienceManager.RebuildSequence = null;
        }

        void OnDestroy()
        {
            ClearRigs();
            DestroySafely(sharedShellMaterial);
            DestroySafely(maskMaterial);
            DestroySafely(revealMaterial);
            DestroySafely(shardMaterial);
        }

        // ---------------------------------------------------------------- hooks

        /// <summary>Hook for the manager on the way into VR. Breaks the outgoing MR shell.</summary>
        public IEnumerator PlayBreakSequence()
        {
            return RunBreak(towardVR: true);
        }

        /// <summary>
        /// Hook for the manager on the way back to MR. Breaks the outgoing VR shell, using
        /// the same proxy geometry. It used to only clear the fragments and restore the
        /// proxies, which is why VR to MR showed no fracture at all.
        /// </summary>
        public IEnumerator PlayRebuildSequence()
        {
            return RunBreak(towardVR: false);
        }

        /// <summary>
        /// One break, both directions. The only per-direction differences are which way
        /// passthrough fades and whether the proxy renderers come back at the end, so the
        /// two transitions share this and cannot drift apart.
        /// </summary>
        /// <summary>
        /// VR to MR debris samples the camera opaque texture, which the mobile pipeline asset
        /// keeps off because it costs a full colour copy every frame. Turn it on for the
        /// length of the break only, and put it back however the break ends.
        /// </summary>
        IEnumerator RunBreak(bool towardVR)
        {
            UniversalRenderPipelineAsset pipeline =
                UniversalRenderPipeline.asset;
            bool restoreOpaqueTexture =
                !towardVR &&
                !forceDebugGray &&
                pipeline != null &&
                !pipeline.supportsCameraOpaqueTexture;

            if (restoreOpaqueTexture)
                pipeline.supportsCameraOpaqueTexture = true;

            try
            {
                yield return RunBreakCore(towardVR);
            }
            finally
            {
                if (restoreOpaqueTexture)
                    pipeline.supportsCameraOpaqueTexture = false;
            }
        }

        IEnumerator RunBreakCore(bool towardVR)
        {
            string tag = towardVR ? "MR2VR" : "VR2MR";
            Debug.Log($"[{tag}][build] break requested, clearing any previous rigs.", this);

            ClearRigs();

            // MR to VR masks passthrough. VR to MR leaves live VR rendering in place and
            // writes alpha holes at the world-space cells that have detached.
            ShellVisualMode mode = forceDebugGray
                ? ShellVisualMode.DebugGray
                : (towardVR ? ShellVisualMode.MRMask : ShellVisualMode.SpatialReveal);

            EnsureProxiesExist();

            Material material = MaterialFor(mode);
            if (material == null)
            {
                Debug.LogError(
                    $"[{tag}][FATAL VISUAL] Material unavailable for {mode}; fracture refused.",
                    this);
                yield break;
            }

            // Both directions need solid debris: a piece that keeps writing only alpha after
            // it detaches is a moving window onto the incoming world, not a chunk of the
            // outgoing one.
            Material shard = ShardMaterial();
            if (shard == null)
            {
                Debug.LogError(
                    $"[{tag}][FATAL VISUAL] Shard material unavailable; fracture refused.",
                    this);
                yield break;
            }

            if (!BuildRigs(mode, material, shard))
            {
                Debug.LogWarning(
                    $"[{tag}][build] Nothing to break for visual mode {mode}. " +
                    "Transition continues without the effect.",
                    this);
                yield break;
            }

            Debug.Log($"[{tag}][visual] mode={mode} material={(material != null ? material.name : "NULL")}.", this);

            SetProxyRenderersVisible(false);

            float total = 0f;
            for (int i = 0; i < rigs.Count; i++)
                total = Mathf.Max(total, rigs[i].TotalSeconds + surfaceDelays[i]);

            Debug.Log(
                $"[{tag}][build] {rigs.Count} surfaces, {TotalFragments} fragments, " +
                $"{total:F2}s (stagger {surfaceDelays[surfaceDelays.Count - 1]:F2}s).",
                this);

            // Neither direction fades globally. MR to VR lets the mask fragments fall away
            // so VR is revealed piece by piece; VR to MR lets detached cells overwrite only
            // their own framebuffer alpha. A global fade made the incoming world look like it
            // was waiting behind the break instead of being uncovered by it.
            if (!towardVR)
            {
                Debug.Log("[VR2MR] FractureProfile=Common", this);
                Debug.Log("[VR2MR][4] VR fracture started", this);
            }

            bool loggedFirstCrack = false;
            bool loggedFirstLoose = false;
            bool loggedFirstDetach = false;
            bool loggedHalfDetached = false;
            float firstLooseTime = rigs[0].MinDelay + fractureSettings.liftDuration;
            float firstDetachTime = rigs[0].MinDelay +
                fractureSettings.liftDuration + fractureSettings.holdDuration;

            float elapsed = 0f;
            while (elapsed < total)
            {
                elapsed += Time.deltaTime;
                for (int i = 0; i < rigs.Count; i++)
                    rigs[i].Step(elapsed - surfaceDelays[i]);

                if (!loggedFirstCrack && elapsed >= rigs[0].MinDelay)
                {
                    loggedFirstCrack = true;
                    Debug.Log($"[{tag}][crack] first fragment cracked at {elapsed:F2}s.", this);
                    if (!towardVR)
                        Debug.Log("[VR2MR] CrackStarted", this);
                }

                if (!towardVR && !loggedFirstLoose && elapsed >= firstLooseTime)
                {
                    loggedFirstLoose = true;
                    Debug.Log("[VR2MR] FirstLoose", this);
                }

                if (!loggedFirstDetach && elapsed >= firstDetachTime)
                {
                    loggedFirstDetach = true;
                    Debug.Log($"[{tag}][detach] first fragment detached at {elapsed:F2}s.", this);

                    if (!towardVR)
                    {
                        Debug.Log("[VR2MR][5] first fragment removed", this);
                        Debug.Log("[VR2MR] FirstDetach", this);

                        // Keep the Passthrough Layer completely off through Crack and Loose.
                        // Step() has already activated the fixed alpha hole for this detached
                        // cell, so enabling the underlay now cannot reveal MR before a hole
                        // actually exists.
                        if (passthrough == null || !passthrough.PrepareMRIncoming())
                        {
                            Debug.LogError(
                                "[VR2MR][FATAL VISUAL] Spatial Passthrough reveal could not " +
                                "be prepared at first detach.",
                                this);
                            ClearRigs();
                            SetProxyRenderersVisible(true);
                            yield break;
                        }

                        Debug.Log(
                            "[VR2MR] Incoming MR prepared=True " +
                            "Incoming MR globally visible=False",
                            this);
                        Debug.Log("[VR2MR][6] MR first reveal", this);
                        Debug.Log("[VR2MR] FirstMRHoleRevealed", this);
                    }
                }

                if (!towardVR && !loggedHalfDetached && ReleasedFragmentCount() * 2 >= TotalFragments)
                {
                    loggedHalfDetached = true;
                    Debug.Log("[VR2MR][7] fracture 50%", this);
                    Debug.Log("[VR2MR] 50PercentDetached", this);
                }

                yield return null;
            }

            Debug.Log($"[{tag}][done] fracture finished.", this);
            if (!towardVR)
            {
                Debug.Log("[VR2MR][8] fracture complete", this);
                Debug.Log("[VR2MR] FractureComplete", this);
            }

            if (tailSeconds > 0f)
                yield return new WaitForSeconds(tailSeconds);

            // Going back to MR, the proxies represent the real room, so restore them. Going
            // to VR they stay hidden.
            if (!towardVR)
            {
                ClearRigs();
                SetProxyRenderersVisible(true);
                Debug.Log($"[{tag}][cleanup] fragments cleared, proxies restored.", this);
            }
        }

        [ContextMenu("Test Break Now")]
        public void TestBreakNow()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[ShellFracture] Enter play mode first.", this);
                return;
            }

            if (activeRoutine != null)
                StopCoroutine(activeRoutine);

            activeRoutine = StartCoroutine(PlayBreakSequence());
        }

        [ContextMenu("Test Restore Now")]
        public void TestRestoreNow()
        {
            if (activeRoutine != null)
            {
                StopCoroutine(activeRoutine);
                activeRoutine = null;
            }

            ClearRigs();
            SetProxyRenderersVisible(true);
        }

        // ---------------------------------------------------------------- build

        /// <summary>
        /// AR planes stream in a second or two after the scene starts, so pressing Enter VR
        /// Game early used to find no proxies and skip the break entirely. Build them here
        /// instead of giving up, using whatever surfaces the provider has by now.
        /// </summary>
        void EnsureProxiesExist()
        {
            if (proxyGenerator == null || proxyGenerator.Proxies.Count > 0)
                return;

            var provider = FindAnyObjectByType<RoomSurfaceProvider>();
            if (provider == null)
                return;

            provider.Refresh();
            if (!provider.HasRoom)
            {
                Debug.LogWarning(
                    "[ShellFracture] The device has not reported any room surfaces yet, " +
                    "so there is nothing to break.",
                    this);
                return;
            }

            Debug.Log(
                "[ShellFracture] Proxies were not built yet; building them now " +
                $"(walls={provider.Walls.Count} floor={provider.Floor != null} " +
                $"ceiling={provider.Ceiling != null}).",
                this);
            proxyGenerator.BuildRoomProxies();
        }

        bool BuildRigs(
            ShellVisualMode mode,
            Material material,
            Material detachedShardMaterial)
        {
            if (proxyGenerator == null || proxyGenerator.Proxies.Count == 0)
                return false;

            foreach (RoomShellProxy proxy in proxyGenerator.Proxies)
            {
                if (proxy.GameObject == null)
                    continue;

                MeshFilter filter = proxy.GameObject.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                    continue;

                List<Vector2> boundary = ReadBoundary(filter.sharedMesh);
                if (boundary.Count < 3)
                    continue;

                // The proxy mesh lies in its local XZ plane with the normal along +Y, while
                // the rig builds in XY with the normal along +Z. This spacer carries that
                // rotation so neither side has to bake it into geometry.
                var space = new GameObject(proxy.GameObject.name + "_FractureSpace");
                space.transform.SetParent(proxy.GameObject.transform, false);
                space.transform.localRotation =
                    Quaternion.FromToRotation(Vector3.forward, Vector3.up);
                planeSpaces.Add(space.transform);

                var rig = new ShellFractureRig();
                Vector2 origin = VoronoiShatter.Centroid(boundary);
                ShellDustEmitter emitter = GetDust();
                if (emitter != null)
                    rig.FragmentReleased += emitter.EmitAt;

                if (rig.Build(boundary, origin, space.transform, material,
                        SettingsFor(proxy.Type), proxy.GameObject.name + "_Fragments", mode,
                        detachedShardMaterial))
                {
                    rigs.Add(rig);
                }
                else
                {
                    DestroySafely(space);
                }
            }

            ComputeSurfaceDelays();
            return rigs.Count > 0;
        }

        void ComputeSurfaceDelays()
        {
            surfaceDelays.Clear();

            if (rigs.Count == 0 || surfaceStaggerStep <= 0f)
            {
                for (int i = 0; i < rigs.Count; i++)
                    surfaceDelays.Add(0f);
                return;
            }

            Camera camera = Camera.main;
            Vector3 camPos = camera != null
                ? camera.transform.position
                : Vector3.zero;

            // Distance from camera per rig, paired with index.
            var distances = new List<(float dist, int index)>(rigs.Count);
            for (int i = 0; i < planeSpaces.Count && i < rigs.Count; i++)
            {
                float dist = Vector3.Distance(camPos, planeSpaces[i].position);
                distances.Add((dist, i));
            }

            distances.Sort((a, b) => a.dist.CompareTo(b.dist));

            // Assign delay by rank: nearest = 0, next = step, next = 2*step, ...
            float[] delays = new float[rigs.Count];
            for (int rank = 0; rank < distances.Count; rank++)
                delays[distances[rank].index] = rank * surfaceStaggerStep;

            for (int i = 0; i < delays.Length; i++)
                surfaceDelays.Add(delays[i]);

            Debug.Log(
                $"[MR2VR][stagger] {rigs.Count} surfaces, step={surfaceStaggerStep:F2}s, " +
                $"maxDelay={surfaceDelays[surfaceDelays.Count - 1]:F2}s.",
                this);
        }

        int ReleasedFragmentCount()
        {
            int count = 0;
            for (int i = 0; i < rigs.Count; i++)
                count += rigs[i].ReleasedCount;

            return count;
        }

        /// <summary>
        /// The shared settings, adjusted for what this surface is. A wall drops the height
        /// of the room, the ceiling drops further because it starts higher, and the floor
        /// has nowhere to go so it only sinks.
        /// </summary>
        ShellFractureRig.Settings SettingsFor(RoomSurfaceType type)
        {
            ShellFractureRig.Settings settings = fractureSettings;

            switch (type)
            {
                case RoomSurfaceType.Ceiling:
                    // Ceiling pieces drop like every other surface. They used to hold
                    // position and fade out, which was the last fade left in the break.
                    settings.fallDistance = ceilingFallDistance;
                    settings.settleDistance = 0f;
                    settings.liftDistance = 0.003f;
                    break;

                case RoomSurfaceType.Floor:
                    settings.fallDistance = floorFallDistance;
                    settings.settleDistance = 0f;
                    settings.lateralDistance = Mathf.Min(settings.lateralDistance, 0.04f);
                    break;

                default:
                    settings.fallDistance = wallFallDistance;
                    if (zeroCeilingNormalDrift)
                        settings.settleDistance = Mathf.Min(settings.settleDistance, 0.01f);
                    break;
            }

            return settings;
        }

        ShellDustEmitter GetDust()
        {
            if (dust != null || !spawnDustIfMissing)
                return dust;

            var host = new GameObject("ShellDust");
            host.transform.SetParent(transform, false);
            dust = host.AddComponent<ShellDustEmitter>();
            return dust;
        }

        /// <summary>
        /// Recovers the polygon a proxy was built from. RoomShellProxyGenerator fans the
        /// AR plane boundary directly, so the mesh vertices are that boundary in order and
        /// nothing needs to be exposed on the generator.
        /// </summary>
        static List<Vector2> ReadBoundary(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            var boundary = new List<Vector2>(vertices.Length);
            foreach (Vector3 vertex in vertices)
                boundary.Add(new Vector2(vertex.x, vertex.z));

            return boundary;
        }

        void SetProxyRenderersVisible(bool visible)
        {
            if (proxyGenerator == null)
                return;

            foreach (RoomShellProxy proxy in proxyGenerator.Proxies)
            {
                if (proxy.GameObject == null)
                    continue;

                MeshRenderer renderer = proxy.GameObject.GetComponent<MeshRenderer>();
                if (renderer != null)
                    renderer.enabled = visible;
            }
        }

        void ClearRigs()
        {
            foreach (ShellFractureRig rig in rigs)
                rig.Dispose();

            rigs.Clear();
            surfaceDelays.Clear();

            foreach (Transform space in planeSpaces)
            {
                if (space != null)
                    DestroySafely(space.gameObject);
            }

            planeSpaces.Clear();
        }

        Material GetSharedMaterial()
        {
            if (sharedShellMaterial != null)
                return sharedShellMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Sprites/Default");

            if (shader == null)
            {
                Debug.LogWarning("[ShellFracture] No shader available for the shell.", this);
                return null;
            }

            sharedShellMaterial = FractureMaterial.Create(shader, shellColor, "RoomShellFracture");
            return sharedShellMaterial;
        }

        Material MaterialFor(ShellVisualMode mode)
        {
            switch (mode)
            {
                case ShellVisualMode.MRMask:
                    if (maskMaterial == null)
                    {
                        Shader s = Shader.Find("F1XR/ShellPassthroughMask");
                        if (s == null)
                        {
                            Debug.LogError("[ShellFracture] ShellPassthroughMask shader missing.", this);
                            return GetSharedMaterial();
                        }
                        maskMaterial = new Material(s) { name = "ShellPassthroughMask" };
                    }
                    return maskMaterial;

                case ShellVisualMode.SpatialReveal:
                    if (revealMaterial == null)
                    {
                        Shader s = Shader.Find("F1XR/ShellPassthroughReveal");
                        if (s == null)
                        {
                            Debug.LogError(
                                "[VR2MR][FATAL VISUAL] ShellPassthroughReveal shader missing.",
                                this);
                            return null;
                        }
                        revealMaterial = new Material(s) { name = "ShellPassthroughReveal" };
                    }
                    return revealMaterial;

                default:
                    return GetSharedMaterial();
            }
        }

        Material ShardMaterial()
        {
            if (shardMaterial != null)
                return shardMaterial;

            Shader shader = Shader.Find("F1XR/ShellFragmentShard");
            if (shader == null)
            {
                Debug.LogError("[ShellFracture] ShellFragmentShard shader missing.", this);
                return null;
            }

            shardMaterial = new Material(shader) { name = "ShellFragmentShard" };
            return shardMaterial;
        }

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
}
