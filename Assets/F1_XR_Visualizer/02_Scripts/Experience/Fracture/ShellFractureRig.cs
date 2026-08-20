using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace F1XR.Experience.Fracture
{
    /// <summary>
    /// The eggshell break itself, with no opinion about what is being broken. Give it a
    /// convex polygon and it builds the fragments; call <see cref="Step"/> each frame to
    /// animate them; call <see cref="Dispose"/> to release everything it made.
    ///
    /// Plain class rather than a MonoBehaviour: one of these exists per surface, and a
    /// wall, a floor and a ceiling all want their own without three more components in the
    /// scene.
    ///
    /// The polygon is taken in 2D and built in local XY with the normal along local +Z.
    /// Callers that need another orientation rotate the parent instead of the geometry.
    /// </summary>
    /// <summary>
    /// How a fragment is drawn. The geometry and motion are identical for all three; only
    /// the material and how alpha behaves differ.
    /// </summary>
    public enum ShellVisualMode
    {
        /// <summary>Flat colour. Editor and diagnostics only; never a real transition.</summary>
        DebugGray,

        /// <summary>MR to VR: a passthrough mask. Present = real room shows, gone = VR shows.</summary>
        MRMask,

        /// <summary>VR to MR: detached cells write spatial holes into framebuffer alpha.</summary>
        SpatialReveal,

        /// <summary>Legacy frozen VR snapshot mode, retained for the standalone prototype.</summary>
        VRSnapshot,

        /// <summary>
        /// VR to MR against real virtual geometry. The fragment is drawn with the surface's
        /// own material from the first frame, because it is that surface: the piece the user
        /// was already looking at, now coming loose. A fixed hole at the cell it left behind
        /// writes alpha 0, so the real room shows through the gap and nothing else does.
        /// </summary>
        VirtualSurface
    }

    public sealed class ShellFractureRig
    {
        [System.Serializable]
        public struct Settings
        {
            [Range(4, 120)] public int fragmentCount;
            public int randomSeed;
            [Range(0f, 2f)] public float originBias;
            [Range(0f, 10f)] public float crackWidthMillimetres;

            [Range(0f, 0.2f)] public float liftDistance;
            [Min(0.01f)] public float liftDuration;
            public AnimationCurve liftCurve;

            [Tooltip("How far a piece falls. This is the main travel: gravity, not blast.")]
            [Min(0f)] public float fallDistance;

            [Tooltip("Sideways slip while falling. Keep small.")]
            [Range(0f, 0.3f)] public float lateralDistance;

            [Tooltip("Extra drift along the surface normal after the lift. Near zero, or " +
                "pieces launch at the viewer.")]
            [Range(0f, 0.2f)] public float settleDistance;

            [Min(0.01f)] public float breakDuration;

            [Tooltip("Fall shape over time. Ease-in reads as gravity taking hold.")]
            public AnimationCurve breakCurve;

            [Tooltip("Crack time added for each step the crack walks out from the origin " +
                "piece, following shared edges. This, not radius, is what makes the break " +
                "spread piece to piece instead of a whole ring letting go at once.")]
            [Min(0f)] public float propagationStep;

            [Tooltip("Gap between a piece cracking (loose nudge) and actually detaching. " +
                "Separates 'it cracked' from 'it fell'.")]
            [Min(0f)] public float holdDuration;

            [Min(0f)] public float delayJitter;

            [Range(0f, 3f)] public float rotationStrength;
            public Vector3 rotationRange;
            [Range(0.5f, 1f)] public float endScale;

            public static Settings Default => new()
            {
                fragmentCount = 48,
                randomSeed = 1234,
                originBias = 0.8f,

                // Almost closed at build time. The gap that reads as a crack should open
                // because the pieces move, not because they were spawned apart.
                crackWidthMillimetres = 0.4f,

                liftDistance = 0.015f,
                liftDuration = 0.22f,
                liftCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f),

                fallDistance = 0.6f,
                lateralDistance = 0.04f,
                settleDistance = 0.008f,
                breakDuration = 0.7f,
                breakCurve = GravityCurve(),

                propagationStep = 0.09f,
                holdDuration = 0.12f,
                delayJitter = 0.05f,
                rotationStrength = 1f,
                rotationRange = new Vector3(10f, 10f, 8f),
                endScale = 0.95f
            };

            /// <summary>
            /// A rough upper bound on the whole break, used only to size the driving loop.
            /// The rig reports its real length from the computed crack times.
            /// </summary>
            public float TotalSeconds() =>
                propagationStep * 12f + holdDuration + liftDuration + breakDuration + delayJitter;

            /// <summary>Slow to start, then accelerating. Falling, not launching.</summary>
            public static AnimationCurve GravityCurve()
            {
                var curve = new AnimationCurve(
                    new Keyframe(0f, 0f, 0f, 0f),
                    new Keyframe(1f, 1f, 2f, 2f));
                return curve;
            }
        }

        struct FragmentAnim
        {
            public Vector3 StartPosition;
            public Vector3 LiftPosition;
            public Vector3 FallDirection;
            public Vector3 LateralDirection;
            public Quaternion EndRotation;

            // The crack reaches this piece at CrackTime. It nudges loose over liftDuration,
            // waits holdDuration, then detaches over breakDuration.
            public float CrackTime;
            public bool Released;
        }

        static readonly int OutputAlphaId = Shader.PropertyToID("_OutputAlpha");
        static readonly int OriginalObjectToWorldId =
            Shader.PropertyToID("_OriginalObjectToWorld");

        ShellVisualMode visualMode = ShellVisualMode.DebugGray;

        readonly List<Mesh> ownedMeshes = new();
        Transform[] fragments;
        MeshRenderer[] renderers;
        MeshRenderer[] revealRenderers;
        FragmentAnim[] anims;
        Settings settings;
        Vector3 localFall = Vector3.down;

        // What a piece switches to the instant it lets go. Until then a piece has no colour
        // of its own so the outgoing world shows on the intact shell; afterwards it is a
        // solid chunk, and the incoming world is seen only through the hole it left.
        Material shardMaterial;

        // One block, reused for every fragment every frame. SetPropertyBlock copies it, so
        // there is no per-fragment allocation and no material is ever cloned.
        MaterialPropertyBlock propertyBlock;

        /// <summary>Raised once per fragment, at the world position where it lets go.</summary>
        public System.Action<Vector3> FragmentReleased;

        /// <summary>Raised once per fragment during reverse, when it snaps back to intact.</summary>
        public System.Action<Vector3> FragmentReattached;

        public Transform Root { get; private set; }
        public int FragmentCount => fragments != null ? fragments.Length : 0;
        public int OwnedMeshCount => ownedMeshes.Count;
        public bool IsDormant { get; private set; }

        public int ReleasedCount
        {
            get
            {
                if (anims == null)
                    return 0;

                int count = 0;
                for (int i = 0; i < anims.Length; i++)
                {
                    if (anims[i].Released)
                        count++;
                }

                return count;
            }
        }

        float maxCrackTime;

        public float TotalSeconds =>
            maxCrackTime + settings.liftDuration + settings.holdDuration +
            settings.breakDuration;

        /// <summary>
        /// Builds every fragment once. Nothing is allocated after this until Dispose.
        /// Returns false when the polygon could not be shattered.
        /// </summary>
        public bool Build(
            IReadOnlyList<Vector2> boundary,
            Vector2 fractureOrigin,
            Transform parent,
            Material sharedMaterial,
            Settings rigSettings,
            string rootName = "ShellFragments",
            ShellVisualMode mode = ShellVisualMode.DebugGray,
            Material detachedShardMaterial = null,
            Material revealMaterial = null,
            float thickness = 0f)
        {
            Dispose();
            settings = rigSettings;
            visualMode = mode;
            shardMaterial = detachedShardMaterial;
            settings.liftCurve ??= AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            settings.breakCurve ??= AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

            List<Vector2> seeds = VoronoiShatter.GenerateSeeds(
                boundary, settings.fragmentCount, fractureOrigin,
                settings.originBias, settings.randomSeed);
            List<List<Vector2>> cells = VoronoiShatter.BuildCells(boundary, seeds);

            if (cells.Count == 0)
                return false;

            var rootObject = new GameObject(rootName);
            rootObject.transform.SetParent(parent, false);
            Root = rootObject.transform;

            // World down expressed in the rig's own space, so the same fall formula works
            // for a wall, a floor and a ceiling without any of them knowing their orientation.
            localFall = Root.InverseTransformDirection(Vector3.down).normalized;

            fragments = new Transform[cells.Count];
            renderers = new MeshRenderer[cells.Count];
            bool leavesHoles = mode == ShellVisualMode.SpatialReveal ||
                mode == ShellVisualMode.VirtualSurface;
            revealRenderers = leavesHoles ? new MeshRenderer[cells.Count] : null;
            anims = new FragmentAnim[cells.Count];
            propertyBlock ??= new MaterialPropertyBlock();

            float inset = settings.crackWidthMillimetres * 0.001f;
            var centroids = new Vector2[cells.Count];
            for (int i = 0; i < cells.Count; i++)
                centroids[i] = VoronoiShatter.Centroid(cells[i]);

            // The crack walks the shared-edge graph out from the origin piece, so it spreads
            // neighbour to neighbour rather than every piece at a radius going at once.
            List<int>[] adjacency = VoronoiShatter.BuildAdjacency(cells);
            int originCell = VoronoiShatter.NearestCell(cells, fractureOrigin);
            var random = new System.Random(settings.randomSeed + 7);
            float[] crackTimes = ComputeCrackTimes(adjacency, originCell, random);

            maxCrackTime = 0f;
            for (int i = 0; i < crackTimes.Length; i++)
                maxCrackTime = Mathf.Max(maxCrackTime, crackTimes[i]);

            for (int i = 0; i < cells.Count; i++)
            {
                Vector2 centroid = centroids[i];
                Mesh fragmentMesh = BuildCellMesh(cells[i], centroid, inset, thickness);

                var piece = new GameObject($"Fragment_{i:00}");
                piece.transform.SetParent(Root, false);
                piece.transform.localPosition = new Vector3(centroid.x, centroid.y, 0f);

                piece.AddComponent<MeshFilter>().sharedMesh = fragmentMesh;
                MeshRenderer renderer = piece.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                if (mode == ShellVisualMode.SpatialReveal)
                {
                    renderer.sharedMaterial = detachedShardMaterial;
                    renderer.enabled = false;
                }
                else
                {
                    renderer.sharedMaterial = sharedMaterial;
                }

                if (leavesHoles)
                {
                    Mesh revealMesh = thickness > 0f
                        ? BuildCellMesh(cells[i], centroid, inset, 0f)
                        : fragmentMesh;

                    var reveal = new GameObject($"Reveal_{i:00}");
                    reveal.transform.SetParent(Root, false);
                    reveal.transform.localPosition = piece.transform.localPosition;
                    reveal.AddComponent<MeshFilter>().sharedMesh = revealMesh;

                    MeshRenderer revealRenderer = reveal.AddComponent<MeshRenderer>();
                    revealRenderer.sharedMaterial = mode == ShellVisualMode.VirtualSurface
                        ? revealMaterial
                        : sharedMaterial;
                    revealRenderer.shadowCastingMode = ShadowCastingMode.Off;
                    revealRenderer.receiveShadows = false;
                    revealRenderer.enabled = false;
                    revealRenderers[i] = revealRenderer;
                }

                fragments[i] = piece.transform;
                renderers[i] = renderer;
                anims[i] = BuildAnim(piece.transform.localPosition, crackTimes[i], random);
            }

            return true;
        }

        /// <summary>
        /// Breadth-first from the origin cell. Each hop along the adjacency graph adds one
        /// propagation step plus a little jitter, so pieces at the same graph depth still do
        /// not all move on the same frame.
        /// </summary>
        float[] ComputeCrackTimes(List<int>[] adjacency, int originCell, System.Random random)
        {
            int n = adjacency.Length;
            var times = new float[n];
            for (int i = 0; i < n; i++)
                times[i] = -1f;

            var queue = new Queue<int>();
            times[originCell] = 0f;
            queue.Enqueue(originCell);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                foreach (int next in adjacency[current])
                {
                    if (times[next] >= 0f)
                        continue;

                    float jitter = (float)random.NextDouble() * settings.delayJitter;
                    times[next] = times[current] + settings.propagationStep + jitter;
                    queue.Enqueue(next);
                }
            }

            // A cell the crack never reaches (an island, if the graph is disconnected) still
            // has to go; give it a late time based on how far it sits from the origin.
            for (int i = 0; i < n; i++)
            {
                if (times[i] < 0f)
                    times[i] = maxKnown(times) + settings.propagationStep;
            }

            return times;

            static float maxKnown(float[] t)
            {
                float m = 0f;
                foreach (float v in t)
                    m = Mathf.Max(m, v);
                return m;
            }
        }

        FragmentAnim BuildAnim(Vector3 start, float crackTime, System.Random random)
        {
            // Sideways slip stays in the plane of the surface and is randomised per piece,
            // so the shell crumbles instead of sliding as one sheet.
            Vector3 lateral = Vector3.ProjectOnPlane(
                new Vector3(NextSigned(random), NextSigned(random), 0f), localFall);
            lateral = lateral.sqrMagnitude > 1e-8f ? lateral.normalized : Vector3.zero;

            var euler = new Vector3(
                NextSigned(random) * settings.rotationRange.x * settings.rotationStrength,
                NextSigned(random) * settings.rotationRange.y * settings.rotationStrength,
                NextSigned(random) * settings.rotationRange.z * settings.rotationStrength);

            return new FragmentAnim
            {
                StartPosition = start,
                LiftPosition = start + Vector3.forward * settings.liftDistance,
                FallDirection = localFall,
                LateralDirection = lateral,
                EndRotation = Quaternion.Euler(euler),
                CrackTime = crackTime
            };
        }

        static float NextSigned(System.Random random) =>
            (float)(random.NextDouble() * 2.0 - 1.0);

        Mesh BuildCellMesh(IReadOnlyList<Vector2> polygon, Vector2 pivot, float inset,
            float thickness)
        {
            Vector2 centre = VoronoiShatter.Centroid(polygon);
            int n = polygon.Count;

            var insetLocal = new Vector2[n];
            var insetWorld = new Vector2[n];

            for (int i = 0; i < n; i++)
            {
                Vector2 point = polygon[i];
                Vector2 toCentre = centre - point;
                float length = toCentre.magnitude;
                if (inset > 0f && length > inset)
                    point += toCentre / length * inset;
                insetWorld[i] = point;
                insetLocal[i] = point - pivot;
            }

            if (thickness <= 0f)
            {
                var vertices = new Vector3[n];
                var normals = new Vector3[n];
                var uvs = new Vector2[n];
                for (int i = 0; i < n; i++)
                {
                    vertices[i] = new Vector3(insetLocal[i].x, insetLocal[i].y, 0f);
                    normals[i] = Vector3.forward;
                    uvs[i] = insetWorld[i];
                }

                var triangles = new int[(n - 2) * 3];
                for (int i = 0; i < n - 2; i++)
                {
                    triangles[i * 3] = 0;
                    triangles[i * 3 + 1] = i + 1;
                    triangles[i * 3 + 2] = i + 2;
                }

                var mesh = new Mesh { name = "ShellFragment" };
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(triangles, 0, true);
                ownedMeshes.Add(mesh);
                return mesh;
            }

            // Extruded mesh: front face at z=0, back face at z=-thickness, side quads.
            int frontBase = 0;
            int backBase = n;
            int sideBase = n * 2;
            int vertCount = n * 2 + n * 4;
            int triIndexCount = ((n - 2) * 2 + n * 2) * 3;

            var verts = new Vector3[vertCount];
            var norms = new Vector3[vertCount];
            var uv = new Vector2[vertCount];

            for (int i = 0; i < n; i++)
            {
                verts[frontBase + i] = new Vector3(insetLocal[i].x, insetLocal[i].y, 0f);
                norms[frontBase + i] = Vector3.forward;
                uv[frontBase + i] = insetWorld[i];

                verts[backBase + i] = new Vector3(insetLocal[i].x, insetLocal[i].y, -thickness);
                norms[backBase + i] = Vector3.back;
                uv[backBase + i] = insetWorld[i];
            }

            for (int i = 0; i < n; i++)
            {
                int next = (i + 1) % n;
                int vi = sideBase + i * 4;

                verts[vi]     = new Vector3(insetLocal[i].x,    insetLocal[i].y,    0f);
                verts[vi + 1] = new Vector3(insetLocal[next].x, insetLocal[next].y, 0f);
                verts[vi + 2] = new Vector3(insetLocal[next].x, insetLocal[next].y, -thickness);
                verts[vi + 3] = new Vector3(insetLocal[i].x,    insetLocal[i].y,    -thickness);

                Vector2 edge = insetLocal[next] - insetLocal[i];
                Vector3 sideNormal = new Vector3(edge.y, -edge.x, 0f).normalized;
                Vector2 mid = (insetLocal[i] + insetLocal[next]) * 0.5f;
                if (Vector2.Dot(new Vector2(sideNormal.x, sideNormal.y), mid) < 0f)
                    sideNormal = -sideNormal;

                norms[vi] = norms[vi + 1] = norms[vi + 2] = norms[vi + 3] = sideNormal;
                uv[vi]     = insetWorld[i];
                uv[vi + 1] = insetWorld[next];
                uv[vi + 2] = insetWorld[next];
                uv[vi + 3] = insetWorld[i];
            }

            var tris = new int[triIndexCount];
            int ti = 0;

            for (int i = 0; i < n - 2; i++)
            {
                tris[ti++] = frontBase;
                tris[ti++] = frontBase + i + 1;
                tris[ti++] = frontBase + i + 2;
            }

            for (int i = 0; i < n - 2; i++)
            {
                tris[ti++] = backBase;
                tris[ti++] = backBase + i + 2;
                tris[ti++] = backBase + i + 1;
            }

            for (int i = 0; i < n; i++)
            {
                int vi = sideBase + i * 4;
                tris[ti++] = vi;
                tris[ti++] = vi + 1;
                tris[ti++] = vi + 2;
                tris[ti++] = vi;
                tris[ti++] = vi + 2;
                tris[ti++] = vi + 3;
            }

            var extruded = new Mesh { name = "ShellFragment" };
            extruded.SetVertices(verts);
            extruded.SetNormals(norms);
            extruded.SetUVs(0, uv);
            extruded.SetTriangles(tris, 0, true);
            ownedMeshes.Add(extruded);
            return extruded;
        }

        /// <summary>Places every fragment for the given time since the break began.</summary>
        public void Step(float elapsed)
        {
            if (fragments == null)
                return;

            for (int i = 0; i < fragments.Length; i++)
            {
                Transform piece = fragments[i];
                if (piece == null)
                    continue;

                // Time since the crack reached this piece.
                float time = elapsed - anims[i].CrackTime;
                float detachStart = settings.liftDuration + settings.holdDuration;

                // Dust is dropped once, at the instant the piece actually detaches.
                if (!anims[i].Released && time >= detachStart)
                {
                    anims[i].Released = true;
                    BecomeShard(i);
                    FragmentReleased?.Invoke(piece.position);
                }

                ApplyPose(i, piece, anims[i], time);
            }
        }

        public int ReattachedCount
        {
            get
            {
                if (anims == null) return 0;
                int count = 0;
                for (int i = 0; i < anims.Length; i++)
                    if (!anims[i].Released) count++;
                return count;
            }
        }

        /// <summary>
        /// All renderers off, data preserved. The rig costs nothing per frame in this
        /// state and can be woken for the reverse playback later.
        /// </summary>
        public void SetDormant()
        {
            IsDormant = true;
            if (fragments == null) return;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null) renderers[i].enabled = false;
                if (revealRenderers != null && i < revealRenderers.Length &&
                    revealRenderers[i] != null)
                    revealRenderers[i].enabled = false;
            }
        }

        /// <summary>
        /// Prepares all fragments for reverse playback: each starts at its final fallen
        /// pose with renderer enabled, using the mask material so reality shows through.
        /// </summary>
        public void WakeForReverse(Material maskMaterial)
        {
            IsDormant = false;
            if (fragments == null) return;

            for (int i = 0; i < fragments.Length; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].sharedMaterial = maskMaterial;
                    renderers[i].enabled = true;
                }

                // Position at full-fall end state so first frame is seamless.
                float endTime = TotalSeconds - anims[i].CrackTime;
                ApplyPoseOnly(i, fragments[i], anims[i], endTime);

                // Mark released so ReattachedCount starts at 0.
                anims[i].Released = true;
            }
        }

        /// <summary>
        /// Drives fragments backwards. reverseElapsed goes from 0 (fully broken) to
        /// TotalSeconds (fully intact). Uses the exact same pose math as forward.
        /// </summary>
        public void StepReverse(float reverseElapsed)
        {
            if (fragments == null) return;

            for (int i = 0; i < fragments.Length; i++)
            {
                Transform piece = fragments[i];
                if (piece == null) continue;

                float forwardEquivalent = TotalSeconds - reverseElapsed;
                float time = forwardEquivalent - anims[i].CrackTime;
                float detachStart = settings.liftDuration + settings.holdDuration;

                // Reattach: was released, now back in lift/hold or intact phase.
                if (anims[i].Released && time < detachStart)
                {
                    anims[i].Released = false;
                    if (renderers[i] != null) renderers[i].enabled = true;
                    FragmentReattached?.Invoke(piece.position);
                }

                // In forward, renderer disables at progress>=1. In reverse, re-enable
                // when progress drops below 1.
                float progress = (time - detachStart) / settings.breakDuration;
                if (progress < 1f && renderers[i] != null && !renderers[i].enabled)
                    renderers[i].enabled = true;

                ApplyPoseOnly(i, piece, anims[i], time);

                // Close reveal holes as fragments return.
                if (revealRenderers != null && i < revealRenderers.Length)
                {
                    bool shouldReveal = time >= detachStart;
                    if (revealRenderers[i] != null)
                        revealRenderers[i].enabled = shouldReveal;
                }
            }
        }

        void ApplyPoseOnly(int index, Transform piece, FragmentAnim anim, float time)
        {
            if (time <= 0f)
            {
                piece.localPosition = anim.StartPosition;
                piece.localRotation = Quaternion.identity;
                piece.localScale = Vector3.one;
                return;
            }

            float detachStart = settings.liftDuration + settings.holdDuration;

            if (time < detachStart)
            {
                float u = settings.liftCurve.Evaluate(
                    Mathf.Clamp01(time / settings.liftDuration));
                piece.localPosition = Vector3.LerpUnclamped(
                    anim.StartPosition, anim.LiftPosition, u);
                piece.localRotation = Quaternion.SlerpUnclamped(
                    Quaternion.identity, anim.EndRotation, u * 0.15f);
                piece.localScale = Vector3.one;
                return;
            }

            float progress = Mathf.Clamp01(
                (time - detachStart) / settings.breakDuration);
            float fallen = settings.breakCurve.Evaluate(progress);
            piece.localPosition = anim.LiftPosition
                + anim.FallDirection * (settings.fallDistance * fallen)
                + anim.LateralDirection * (settings.lateralDistance * progress)
                + Vector3.forward * (settings.settleDistance * progress);
            piece.localRotation = Quaternion.SlerpUnclamped(
                Quaternion.identity, anim.EndRotation, Mathf.Lerp(0.15f, 1f, progress));
            piece.localScale = Vector3.one * Mathf.Lerp(1f, settings.endScale, progress);
        }

        void ApplyPose(int index, Transform piece, FragmentAnim anim, float time)
        {
            if (time <= 0f)
                return;   // Phase 0: intact, still part of the surface.

            ApplyPoseOnly(index, piece, anim, time);

            float detachStart = settings.liftDuration + settings.holdDuration;
            float progress = Mathf.Clamp01(
                (time - detachStart) / settings.breakDuration);

            if (time >= detachStart && progress >= 1f)
                DisableMovingRenderer(index);

            if (visualMode == ShellVisualMode.SpatialReveal ||
                visualMode == ShellVisualMode.VirtualSurface)
            {
                SetReveal(index, 1f);
            }
        }

        /// <summary>
        /// VR to MR only: the piece starts drawing at the instant it detaches, carrying the
        /// VR image it was cut from. Its pose right now is recorded so the shard shader can
        /// keep sampling that original screen position however far the piece then tumbles.
        ///
        /// MR to VR does not do this. Its pieces are chunks of the real room, and the real
        /// room lives in the compositor where no shader can sample it, so those stay
        /// passthrough masks the whole way down.
        /// </summary>
        void BecomeShard(int index)
        {
            if (shardMaterial == null || visualMode != ShellVisualMode.SpatialReveal)
                return;

            MeshRenderer renderer = renderers[index];
            Transform piece = fragments[index];
            if (renderer == null || piece == null)
                return;

            renderer.sharedMaterial = shardMaterial;

            propertyBlock ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetMatrix(OriginalObjectToWorldId, piece.localToWorldMatrix);
            renderer.SetPropertyBlock(propertyBlock);

            renderer.enabled = true;
        }

        void DisableMovingRenderer(int index)
        {
            MeshRenderer renderer = renderers[index];
            if (renderer != null)
                renderer.enabled = false;
        }

        void SetReveal(int index, float amount)
        {
            if (revealRenderers == null || index < 0 || index >= revealRenderers.Length)
                return;

            MeshRenderer renderer = revealRenderers[index];
            if (renderer == null)
                return;

            float reveal = Mathf.Clamp01(amount);
            renderer.enabled = reveal > 0.001f;
            if (!renderer.enabled)
                return;

            propertyBlock ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat(OutputAlphaId, 1f - reveal);
            renderer.SetPropertyBlock(propertyBlock);
        }

        /// <summary>
        /// Freezes each fragment's mesh UVs to the screen position it occupied when this was
        /// called, so a snapshot shader shows the exact VR pixels that were there. Call once,
        /// right after Build and before the break moves anything. VRSnapshot only.
        /// </summary>
        public void BakeSnapshotUVs(Camera camera)
        {
            if (fragments == null || camera == null)
                return;

            for (int i = 0; i < fragments.Length; i++)
            {
                Transform piece = fragments[i];
                if (piece == null)
                    continue;

                MeshFilter filter = piece.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null)
                    continue;

                Vector3[] verts = mesh.vertices;
                var uvs = new Vector2[verts.Length];
                for (int v = 0; v < verts.Length; v++)
                {
                    Vector3 world = piece.TransformPoint(verts[v]);
                    Vector3 screen = camera.WorldToViewportPoint(world);
                    uvs[v] = new Vector2(screen.x, screen.y);
                }

                mesh.SetUVs(0, uvs);
            }
        }

        public float MinDelay
        {
            get
            {
                float min = float.MaxValue;
                if (anims != null)
                    foreach (FragmentAnim a in anims)
                        min = Mathf.Min(min, a.CrackTime);

                return min == float.MaxValue ? 0f : min;
            }
        }

        public float MaxDelay => maxCrackTime;

        /// <summary>
        /// Releases the fragment objects and, crucially, the meshes. Destroying a
        /// GameObject leaves its Mesh alive, which is exactly how Step 2 leaked.
        /// </summary>
        public void Dispose()
        {
            if (Root != null)
            {
                DestroySafely(Root.gameObject);
                Root = null;
            }

            foreach (Mesh mesh in ownedMeshes)
                DestroySafely(mesh);

            ownedMeshes.Clear();
            fragments = null;
            renderers = null;
            revealRenderers = null;
            anims = null;
        }

        static void DestroySafely(Object target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                Object.Destroy(target);
            else
                Object.DestroyImmediate(target);
        }
    }
}
