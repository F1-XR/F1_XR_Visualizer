using System.Collections;
using System.Collections.Generic;
using F1XR.Experience.Room;
using UnityEngine;
using UnityEngine.Rendering;

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

        [Tooltip("Fade the real world out over the course of the break, instead of after " +
            "it, so the growing holes reveal the VR world behind the shell rather than more " +
            "real room. The mode manager's own fade afterwards just confirms the end state.")]
        [SerializeField] bool revealDuringBreak = true;

        [Tooltip("The passthrough fade finishes at this fraction of the break, so the VR " +
            "world is fully shown before the last fragment lands.")]
        [SerializeField, Range(0.3f, 1f)] float revealFinishFraction = 0.85f;

        [Tooltip("Seconds to wait after the last piece before handing back to the mode " +
            "manager, so the fade does not start on top of the break.")]
        [SerializeField, Min(0f)] float tailSeconds = 0.15f;

        [Header("Visual mode")]
        [Tooltip("Force the flat debug colour even during real transitions. Off means MR to " +
            "VR uses the passthrough mask and VR to MR uses the VR snapshot.")]
        [SerializeField] bool forceDebugGray;

        [Tooltip("Camera whose view is frozen for the VR->MR snapshot. Falls back to " +
            "Camera.main.")]
        [SerializeField] Camera vrCamera;

        [Header("Look")]
        [SerializeField] Color shellColor = new(0.78f, 0.76f, 0.72f, 1f);

        readonly List<ShellFractureRig> rigs = new();
        readonly List<Transform> planeSpaces = new();
        Material sharedShellMaterial;   // debug gray
        Material maskMaterial;          // MR->VR passthrough mask
        Material snapshotMaterial;      // VR->MR VR snapshot
        RenderTexture snapshotTexture;
        Coroutine activeRoutine;

        public int RigCount => rigs.Count;

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
            experienceManager.RebuildSequence = PlayRebuildSequence;
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
            DestroySafely(snapshotMaterial);
            if (snapshotTexture != null)
            {
                snapshotTexture.Release();
                DestroySafely(snapshotTexture);
            }
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
        IEnumerator RunBreak(bool towardVR)
        {
            string tag = towardVR ? "MR2VR" : "VR2MR";
            Debug.Log($"[{tag}][build] break requested, clearing any previous rigs.", this);

            ClearRigs();
            EnsureProxiesExist();

            // Pick how the fragments are drawn. MR to VR masks passthrough; VR to MR shows a
            // frozen VR snapshot. DebugGray is editor only.
            ShellVisualMode mode = forceDebugGray
                ? ShellVisualMode.DebugGray
                : (towardVR ? ShellVisualMode.MRMask : ShellVisualMode.VRSnapshot);

            // The VR snapshot must be captured before anything moves, and it must never fall
            // back to gray: if it fails, the transition should not pretend to work.
            if (mode == ShellVisualMode.VRSnapshot && !CaptureVRSnapshot())
            {
                Debug.LogError(
                    "[VR2MR][FATAL VISUAL] Snapshot unavailable; not starting the fracture. " +
                    "The mode change continues without the effect rather than showing gray.",
                    this);
                yield break;
            }

            Material material = MaterialFor(mode);

            if (!BuildRigs(mode, material))
            {
                Debug.LogWarning(
                    $"[{tag}][build] Nothing to break: proxies=" +
                    $"{(proxyGenerator != null ? proxyGenerator.Proxies.Count : -1)}. " +
                    "Transition continues without the effect.",
                    this);
                yield break;
            }

            // Freeze the VR view onto the fragments before they move.
            if (mode == ShellVisualMode.VRSnapshot)
            {
                Camera cam = ResolveVRCamera();
                foreach (ShellFractureRig rig in rigs)
                    rig.BakeSnapshotUVs(cam);

                Debug.Log($"[{tag}][snapshot] UV baked onto {TotalFragments} fragments.", this);
            }

            Debug.Log($"[{tag}][visual] mode={mode} material={(material != null ? material.name : "NULL")}.", this);

            SetProxyRenderersVisible(false);

            float total = 0f;
            foreach (ShellFractureRig rig in rigs)
                total = Mathf.Max(total, rig.TotalSeconds);

            Debug.Log(
                $"[{tag}][build] {rigs.Count} surfaces, {TotalFragments} fragments, {total:F2}s.",
                this);

            // Passthrough fades in parallel, in the direction of travel. Going to VR the real
            // world fades out so the holes reveal VR; going to MR it fades in so the holes
            // reveal the real room. The manager's own fade afterwards is a harmless confirm.
            if (revealDuringBreak && passthrough != null)
            {
                if (towardVR)
                    passthrough.EnterVR(total * revealFinishFraction);
                else
                    passthrough.EnterMR(total * revealFinishFraction);

                Debug.Log($"[{tag}][passthrough] fade started over {total * revealFinishFraction:F2}s.", this);
            }

            bool loggedFirstCrack = false;
            bool loggedFirstDetach = false;
            float firstDetachTime = rigs[0].MinDelay +
                fractureSettings.liftDuration + fractureSettings.holdDuration;

            float elapsed = 0f;
            while (elapsed < total)
            {
                elapsed += Time.deltaTime;
                for (int i = 0; i < rigs.Count; i++)
                    rigs[i].Step(elapsed);

                if (!loggedFirstCrack && elapsed >= rigs[0].MinDelay)
                {
                    loggedFirstCrack = true;
                    Debug.Log($"[{tag}][crack] first fragment cracked at {elapsed:F2}s.", this);
                }

                if (!loggedFirstDetach && elapsed >= firstDetachTime)
                {
                    loggedFirstDetach = true;
                    Debug.Log($"[{tag}][detach] first fragment detached at {elapsed:F2}s.", this);
                }

                yield return null;
            }

            Debug.Log($"[{tag}][done] fracture finished.", this);

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

        bool BuildRigs(ShellVisualMode mode, Material material)
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
                        SettingsFor(proxy.Type), proxy.GameObject.name + "_Fragments", mode))
                {
                    rigs.Add(rig);
                }
                else
                {
                    DestroySafely(space);
                }
            }

            return rigs.Count > 0;
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
                    // Holds position and fades; ceiling pieces must never drop into the room.
                    settings.fallsUnderGravity = false;
                    settings.fadeStartFraction = 0f;
                    settings.fallDistance = 0f;
                    settings.settleDistance = 0f;
                    settings.liftDistance = 0.003f;
                    break;

                case RoomSurfaceType.Floor:
                    settings.fallsUnderGravity = true;
                    settings.fadeStartFraction = 0.55f;
                    settings.fallDistance = floorFallDistance;
                    settings.settleDistance = 0f;
                    settings.lateralDistance = Mathf.Min(settings.lateralDistance, 0.04f);
                    break;

                default:
                    settings.fallsUnderGravity = true;
                    settings.fadeStartFraction = 0.55f;
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

                case ShellVisualMode.VRSnapshot:
                    if (snapshotMaterial == null)
                    {
                        Shader s = Shader.Find("F1XR/ShellSnapshot");
                        if (s == null)
                        {
                            Debug.LogError("[ShellFracture] ShellSnapshot shader missing.", this);
                            return GetSharedMaterial();
                        }
                        snapshotMaterial = new Material(s) { name = "ShellSnapshot" };
                    }
                    if (snapshotTexture != null)
                        snapshotMaterial.SetTexture("_SnapshotTex", snapshotTexture);
                    return snapshotMaterial;

                default:
                    return GetSharedMaterial();
            }
        }

        Camera ResolveVRCamera()
        {
            return vrCamera != null ? vrCamera : Camera.main;
        }

        /// <summary>
        /// Renders the VR view once into a RenderTexture. Returns false on any failure so the
        /// caller can refuse to start rather than fall back to gray.
        /// </summary>
        bool CaptureVRSnapshot()
        {
            Camera cam = ResolveVRCamera();
            if (cam == null)
            {
                Debug.LogError("[VR2MR][snapshot] No camera to capture.", this);
                return false;
            }

            int w = Mathf.Max(2, cam.pixelWidth);
            int h = Mathf.Max(2, cam.pixelHeight);

            if (snapshotTexture == null || snapshotTexture.width != w || snapshotTexture.height != h)
            {
                if (snapshotTexture != null)
                    snapshotTexture.Release();

                snapshotTexture = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32)
                {
                    name = "VRTransitionRT"
                };
                snapshotTexture.Create();
            }

            // Render the current VR view into the texture without disturbing the live camera.
            RenderTexture previous = cam.targetTexture;
            cam.targetTexture = snapshotTexture;
            cam.Render();
            cam.targetTexture = previous;

            Debug.Log($"[VR2MR][snapshot] captured {w}x{h} valid={snapshotTexture.IsCreated()}.", this);
            return snapshotTexture.IsCreated();
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
