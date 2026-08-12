using System.Collections;
using System.Collections.Generic;
using F1XR.Experience.Environment;
using UnityEngine;

namespace F1XR.Experience.Fracture
{
    /// <summary>
    /// The return to MR: the VR pit garage is what breaks.
    ///
    /// This owns the way home and nothing else. Going into VR the user is looking at their
    /// real room, so breaking the physical room shell is right, and
    /// <see cref="RoomShellFractureController"/> keeps doing it. Coming back they are looking
    /// at the garage, and cracking the outline of their real walls in front of it puts the
    /// world that is leaving and the thing that shatters in two different places.
    ///
    /// The rule the whole sequence is built around: behind a detached fragment is the real
    /// room, immediately. Not a sky, not a clear colour, not a second layer to break through
    /// afterwards. A cell lets go, its hole opens onto passthrough, and that is the only
    /// place MR appears until the next cell goes.
    ///
    /// Nothing is captured and nothing is proxied. The fragments are the garage: same meshes'
    /// material, same UVs, cut out of surfaces the user has been standing inside all along.
    /// Because they are real geometry, the crack is world-locked for free - lean left and the
    /// same hole is seen from a new angle rather than dragged along.
    ///
    /// The break itself is the existing rig, unchanged: Voronoi cells, breadth-first crack
    /// propagation, loose, lift, detach, fall, dust. Only the geometry fed into it is new.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VirtualGarageFractureController : MonoBehaviour
    {
        [SerializeField] ExperienceModeManager experienceManager;
        [SerializeField] PassthroughTransitionController passthrough;
        [SerializeField] VirtualGarage garage;

        [Tooltip("Assign the return hook on the mode manager. Turn off to hand the return " +
            "back to whatever else claims it.")]
        [SerializeField] bool driveReturnToMR = true;

        [Header("Fracture")]
        [SerializeField] ShellFractureRig.Settings fractureSettings =
            ShellFractureRig.Settings.Default;

        [Header("Where it starts")]
        [Tooltip("Metres in front of the head, at the moment the return begins, for the point " +
            "the first crack opens from. It is snapped onto the nearest garage surface and " +
            "then fixed in the VR world; it does not follow the head afterwards.")]
        [SerializeField, Range(0.5f, 12f)] float originDistance = 4f;

        [Tooltip("Height above the floor for that same point. Around chest height on a wall " +
            "reads better than a crack opening at the ceiling or under the feet.")]
        [SerializeField, Range(0f, 4f)] float originHeight = 1.5f;

        [Tooltip("How fast the crack travels from one surface to the next, in metres per " +
            "second. This is what makes the garage come apart in sequence instead of all six " +
            "surfaces letting go together.")]
        [SerializeField, Range(0.5f, 30f)] float crackSpeed = 5f;

        [Header("Per surface travel")]
        [SerializeField, Min(0f)] float wallFallDistance = 1.6f;
        [SerializeField, Min(0f)] float floorFallDistance = 0.4f;

        [Tooltip("Ceiling pieces barely move: they come loose, turn a little and are gone. " +
            "Anything more and the ceiling rains onto the user's head.")]
        [SerializeField, Min(0f)] float ceilingFallDistance = 0.12f;

        [Header("Counts")]
        [SerializeField, Range(4, 120)] int floorFragments = 30;
        [SerializeField, Range(4, 120)] int wallFragments = 24;
        [SerializeField, Range(4, 120)] int ceilingFragments = 24;

        [Header("Dust")]
        [SerializeField] ShellDustEmitter dust;
        [SerializeField] bool spawnDustIfMissing = true;

        [Tooltip("Seconds to hold after the last piece before the mode manager settles MR.")]
        [SerializeField, Min(0f)] float tailSeconds = 0.15f;

        readonly List<ShellFractureRig> rigs = new();
        readonly List<float> rigDelays = new();
        readonly List<VirtualGarageSurface> rigSurfaces = new();

        Material revealMaterial;

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

            if (passthrough == null)
                passthrough = FindAnyObjectByType<PassthroughTransitionController>();

            if (garage == null)
                garage = FindAnyObjectByType<VirtualGarage>(FindObjectsInactive.Include);
        }

        void OnEnable()
        {
            if (!driveReturnToMR || experienceManager == null)
            {
                Debug.LogWarning(
                    $"[VR2MR][garage] NOT claiming the return: driveReturnToMR=" +
                    $"{driveReturnToMR} manager={(experienceManager != null ? "found" : "NULL")}.",
                    this);
                return;
            }

            experienceManager.RebuildSequence = PlayReturnSequence;
            Debug.Log(
                "[VR2MR][garage] CLAIMED the return hook. Return MR breaks the VR pit garage.",
                this);
        }

        void OnDisable()
        {
            if (experienceManager != null &&
                experienceManager.RebuildSequence == PlayReturnSequence)
            {
                experienceManager.RebuildSequence = null;
            }

            ClearRigs();
        }

        void OnDestroy()
        {
            ClearRigs();
            DestroySafely(revealMaterial);
        }

        public IEnumerator PlayReturnSequence() => RunFracture();

        IEnumerator RunFracture()
        {
            Debug.Log("[VR2MR][garage][1] return requested", this);
            ClearRigs();

            if (garage == null || garage.Surfaces.Count == 0)
            {
                Debug.LogError(
                    "[VR2MR][garage][FATAL] No VirtualGarage surfaces to break. The return " +
                    "continues without a fracture.",
                    this);
                yield break;
            }

            Material reveal = RevealMaterial();
            if (reveal == null)
                yield break;

            Vector3 origin = ChooseOrigin();

            if (!BuildRigs(origin, reveal))
            {
                Debug.LogWarning("[VR2MR][garage] nothing could be shattered.", this);
                yield break;
            }

            // The intact surfaces hand over to their fragments in the same frame, so there is
            // no moment where both are drawn and no moment where neither is.
            SetSurfaceRenderersVisible(false);

            float total = 0f;
            for (int i = 0; i < rigs.Count; i++)
                total = Mathf.Max(total, rigDelays[i] + rigs[i].TotalSeconds);

            Debug.Log(
                $"[VR2MR][garage][2] {rigs.Count} surfaces, {TotalFragments} fragments, " +
                $"{total:F2}s, origin={origin}.",
                this);

            float elapsed = 0f;
            bool preparedMR = false;
            bool loggedHalf = false;

            while (elapsed < total)
            {
                elapsed += Time.deltaTime;

                for (int i = 0; i < rigs.Count; i++)
                {
                    // A negative time leaves that surface intact: the rig treats anything at
                    // or before zero as "the crack has not reached me yet". That is the whole
                    // cross-surface propagation, with no change to the rig itself.
                    rigs[i].Step(elapsed - rigDelays[i]);
                }

                // Passthrough is switched on at the first detach and not one frame earlier.
                // The layer alone reveals nothing - the camera stays opaque - but preparing it
                // only once a hole exists means a mistake here can never flash the whole real
                // room before the garage has started to come apart.
                if (!preparedMR && ReleasedFragmentCount() > 0)
                {
                    preparedMR = true;

                    if (passthrough == null || !passthrough.PrepareMRIncoming())
                    {
                        Debug.LogError(
                            "[VR2MR][garage][FATAL] Passthrough could not be prepared at the " +
                            "first detach; the holes would open onto nothing.",
                            this);
                        ClearRigs();
                        SetSurfaceRenderersVisible(true);
                        yield break;
                    }

                    Debug.Log("[VR2MR][garage][3] FirstDetach, first MR hole open", this);
                }

                if (!loggedHalf && ReleasedFragmentCount() * 2 >= TotalFragments)
                {
                    loggedHalf = true;
                    Debug.Log("[VR2MR][garage][4] 50PercentDetached", this);
                }

                yield return null;
            }

            Debug.Log("[VR2MR][garage][5] FractureComplete", this);

            if (tailSeconds > 0f)
                yield return new WaitForSeconds(tailSeconds);

            // The manager settles MR and switches the VR environment off after this returns,
            // so the garage stays live right up to the handover.
            ClearRigs();
            SetSurfaceRenderersVisible(true);
            Debug.Log("[VR2MR][garage][6] fragments cleared, garage restored for next time", this);
        }

        /// <summary>
        /// A point on the garage surface the user is facing, fixed in the VR world for the
        /// rest of the break. Read from the head once: if it kept tracking, the crack would
        /// follow the eyes, which is the exact failure this route exists to avoid.
        /// </summary>
        Vector3 ChooseOrigin()
        {
            Camera head = Camera.main;
            if (head == null)
                return garage.RoomCentre;

            Vector3 forward = Vector3.ProjectOnPlane(head.transform.forward, Vector3.up);
            forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;

            Vector3 aim = head.transform.position
                + forward * originDistance
                + Vector3.up * (originHeight - head.transform.position.y);

            // Snap onto whichever surface is nearest, so the crack starts on the garage and
            // not in mid air next to it.
            Vector3 best = aim;
            float bestDistance = float.MaxValue;

            foreach (VirtualGarageSurface surface in garage.Surfaces)
            {
                Vector3 onSurface = ClampToSurface(surface, aim);
                float distance = (onSurface - aim).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = onSurface;
                }
            }

            return best;
        }

        static Vector3 ClampToSurface(VirtualGarageSurface surface, Vector3 worldPoint)
        {
            Vector3 local = surface.Space.InverseTransformPoint(worldPoint);
            local.x = Mathf.Clamp(local.x, -surface.Size.x * 0.5f, surface.Size.x * 0.5f);
            local.y = Mathf.Clamp(local.y, -surface.Size.y * 0.5f, surface.Size.y * 0.5f);
            local.z = 0f;
            return surface.Space.TransformPoint(local);
        }

        bool BuildRigs(Vector3 origin, Material reveal)
        {
            ShellDustEmitter emitter = GetDust();

            foreach (VirtualGarageSurface surface in garage.Surfaces)
            {
                if (surface.Space == null || surface.Renderer == null)
                    continue;

                float halfX = surface.Size.x * 0.5f;
                float halfY = surface.Size.y * 0.5f;
                var boundary = new List<Vector2>
                {
                    new(-halfX, -halfY),
                    new(halfX, -halfY),
                    new(halfX, halfY),
                    new(-halfX, halfY)
                };

                // Where the global origin lands on this surface, in the surface's own plane.
                // Each surface therefore starts cracking at the point closest to where the
                // crack arrived, rather than at its own middle.
                Vector3 localOrigin = surface.Space.InverseTransformPoint(
                    ClampToSurface(surface, origin));
                var seed = new Vector2(localOrigin.x, localOrigin.y);

                var rig = new ShellFractureRig();
                if (emitter != null)
                    rig.FragmentReleased += emitter.EmitAt;

                if (!rig.Build(boundary, seed, surface.Space, surface.SurfaceMaterial,
                        SettingsFor(surface.Type), surface.Space.name + "_Fragments",
                        ShellVisualMode.VirtualSurface, null, reveal))
                {
                    continue;
                }

                float travel = Vector3.Distance(ClampToSurface(surface, origin), origin);

                rigs.Add(rig);
                rigSurfaces.Add(surface);
                rigDelays.Add(travel / Mathf.Max(crackSpeed, 0.01f));
            }

            return rigs.Count > 0;
        }

        ShellFractureRig.Settings SettingsFor(VirtualSurfaceType type)
        {
            ShellFractureRig.Settings settings = fractureSettings;

            switch (type)
            {
                case VirtualSurfaceType.Floor:
                    settings.fragmentCount = floorFragments;
                    settings.fallDistance = floorFallDistance;
                    settings.settleDistance = 0f;
                    settings.lateralDistance = Mathf.Min(settings.lateralDistance, 0.04f);
                    break;

                case VirtualSurfaceType.Ceiling:
                    settings.fragmentCount = ceilingFragments;
                    settings.fallDistance = ceilingFallDistance;
                    settings.settleDistance = 0f;
                    settings.lateralDistance = 0f;
                    settings.liftDistance = 0.01f;
                    break;

                default:
                    settings.fragmentCount = wallFragments;
                    settings.fallDistance = wallFallDistance;
                    settings.settleDistance = Mathf.Min(settings.settleDistance, 0.01f);
                    break;
            }

            return settings;
        }

        int ReleasedFragmentCount()
        {
            int count = 0;
            for (int i = 0; i < rigs.Count; i++)
                count += rigs[i].ReleasedCount;

            return count;
        }

        void SetSurfaceRenderersVisible(bool visible)
        {
            if (garage == null)
                return;

            foreach (VirtualGarageSurface surface in garage.Surfaces)
            {
                if (surface.Renderer != null)
                    surface.Renderer.enabled = visible;
            }
        }

        Material RevealMaterial()
        {
            if (revealMaterial != null)
                return revealMaterial;

            Shader shader = Shader.Find("F1XR/ShellPassthroughReveal");
            if (shader == null)
            {
                Debug.LogError(
                    "[VR2MR][garage][FATAL] ShellPassthroughReveal shader missing; without it " +
                    "a detached cell would leave the clear colour showing instead of the room.",
                    this);
                return null;
            }

            revealMaterial = new Material(shader) { name = "VirtualGarageReveal" };
            return revealMaterial;
        }

        ShellDustEmitter GetDust()
        {
            if (dust != null || !spawnDustIfMissing)
                return dust;

            var host = new GameObject("VirtualGarageDust");
            host.transform.SetParent(transform, false);
            dust = host.AddComponent<ShellDustEmitter>();
            return dust;
        }

        void ClearRigs()
        {
            foreach (ShellFractureRig rig in rigs)
                rig.Dispose();

            rigs.Clear();
            rigDelays.Clear();
            rigSurfaces.Clear();
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
