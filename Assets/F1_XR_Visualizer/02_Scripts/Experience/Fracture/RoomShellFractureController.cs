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

        [Header("Look")]
        [SerializeField] Color shellColor = new(0.78f, 0.76f, 0.72f, 1f);

        readonly List<ShellFractureRig> rigs = new();
        readonly List<Transform> planeSpaces = new();
        Material sharedShellMaterial;
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
        }

        // ---------------------------------------------------------------- hooks

        /// <summary>Runs on the way into VR: the shell comes apart.</summary>
        public IEnumerator PlayBreakSequence()
        {
            ClearRigs();
            EnsureProxiesExist();

            if (!BuildRigs())
            {
                Debug.LogWarning(
                    "[ShellFracture] Nothing to break: proxies=" +
                    $"{(proxyGenerator != null ? proxyGenerator.Proxies.Count : -1)}. " +
                    "The transition continues without the effect.",
                    this);
                yield break;
            }

            SetProxyRenderersVisible(false);

            float total = 0f;
            foreach (ShellFractureRig rig in rigs)
                total = Mathf.Max(total, rig.TotalSeconds);

            // Fade the real world out in parallel with the break. The VR environment is
            // already active behind the shell (the mode manager turns it on before this
            // runs), so as passthrough drops and the holes open, VR is what shows through.
            // The manager fades again after this returns, which is a harmless confirmation
            // because passthrough is already at VR by then.
            if (revealDuringBreak && passthrough != null)
                passthrough.EnterVR(total * revealFinishFraction);

            Debug.Log(
                $"[ShellFracture] Breaking {rigs.Count} surfaces, " +
                $"{TotalFragments} fragments, {total:F2}s, reveal={revealDuringBreak}.",
                this);

            float elapsed = 0f;
            while (elapsed < total)
            {
                elapsed += Time.deltaTime;
                for (int i = 0; i < rigs.Count; i++)
                    rigs[i].Step(elapsed);

                yield return null;
            }

            if (tailSeconds > 0f)
                yield return new WaitForSeconds(tailSeconds);
        }

        /// <summary>Runs on the way back to MR: the fragments are cleared and the proxies return.</summary>
        public IEnumerator PlayRebuildSequence()
        {
            ClearRigs();
            SetProxyRenderersVisible(true);
            Debug.Log("[ShellFracture] Shell restored.", this);
            yield break;
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

        bool BuildRigs()
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

                if (rig.Build(boundary, origin, space.transform, GetSharedMaterial(),
                        SettingsFor(proxy.Type), proxy.GameObject.name + "_Fragments"))
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
