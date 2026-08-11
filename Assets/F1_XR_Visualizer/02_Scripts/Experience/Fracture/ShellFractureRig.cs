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

        /// <summary>VR to MR: a frozen piece of the VR view, from a snapshot texture.</summary>
        VRSnapshot
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

            [Tooltip("True for walls and floors: the piece drops. False for the ceiling: " +
                "it holds position and only fades, so nothing rains into the room.")]
            public bool fallsUnderGravity;

            [Tooltip("Fraction of the break at which a piece begins to fade out. Walls and " +
                "floors fade late (they are still falling); the ceiling fades the whole " +
                "time, so set 0 there.")]
            [Range(0f, 1f)] public float fadeStartFraction;

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
                fragmentCount = 30,
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
                fallsUnderGravity = true,
                fadeStartFraction = 0.55f,

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

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int AlphaId = Shader.PropertyToID("_Alpha");

        ShellVisualMode visualMode = ShellVisualMode.DebugGray;

        readonly List<Mesh> ownedMeshes = new();
        Transform[] fragments;
        MeshRenderer[] renderers;
        FragmentAnim[] anims;
        Settings settings;
        Vector3 localFall = Vector3.down;
        Color baseColor = Color.white;

        // One block, reused for every fragment every frame. SetPropertyBlock copies it, so
        // there is no per-fragment allocation and no material is ever cloned.
        MaterialPropertyBlock propertyBlock;

        /// <summary>Raised once per fragment, at the world position where it lets go.</summary>
        public System.Action<Vector3> FragmentReleased;

        public Transform Root { get; private set; }
        public int FragmentCount => fragments != null ? fragments.Length : 0;
        public int OwnedMeshCount => ownedMeshes.Count;

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
            ShellVisualMode mode = ShellVisualMode.DebugGray)
        {
            Dispose();
            settings = rigSettings;
            visualMode = mode;
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
            anims = new FragmentAnim[cells.Count];
            propertyBlock ??= new MaterialPropertyBlock();
            baseColor = sharedMaterial != null && sharedMaterial.HasProperty(BaseColorId)
                ? sharedMaterial.GetColor(BaseColorId)
                : Color.white;

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
                Mesh mesh = BuildCellMesh(cells[i], centroid, inset);

                var piece = new GameObject($"Fragment_{i:00}");
                piece.transform.SetParent(Root, false);
                piece.transform.localPosition = new Vector3(centroid.x, centroid.y, 0f);

                piece.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer renderer = piece.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = sharedMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;

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

        Mesh BuildCellMesh(IReadOnlyList<Vector2> polygon, Vector2 pivot, float inset)
        {
            Vector2 centre = VoronoiShatter.Centroid(polygon);

            var vertices = new Vector3[polygon.Count];
            var normals = new Vector3[polygon.Count];
            var uvs = new Vector2[polygon.Count];

            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 point = polygon[i];
                Vector2 toCentre = centre - point;
                float length = toCentre.magnitude;
                if (inset > 0f && length > inset)
                    point += toCentre / length * inset;

                Vector2 local = point - pivot;
                vertices[i] = new Vector3(local.x, local.y, 0f);
                normals[i] = Vector3.forward;
                uvs[i] = point;
            }

            // Voronoi cells are convex, so a fan is a correct triangulation.
            var triangles = new int[(polygon.Count - 2) * 3];
            for (int i = 0; i < polygon.Count - 2; i++)
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
                    FragmentReleased?.Invoke(piece.position);
                }

                ApplyPose(i, piece, anims[i], time);
            }
        }

        void ApplyPose(int index, Transform piece, FragmentAnim anim, float time)
        {
            if (time <= 0f)
                return;   // Phase 0: intact, still part of the surface.

            float detachStart = settings.liftDuration + settings.holdDuration;

            if (time < detachStart)
            {
                // Phase 1-2: cracked and loose. The piece nudges off the surface by the lift
                // amount and tilts a little, then holds there until its detach time. It does
                // not fall yet, which is what separates 'it cracked' from 'it fell'.
                float u = settings.liftCurve.Evaluate(
                    Mathf.Clamp01(time / settings.liftDuration));
                piece.localPosition = Vector3.LerpUnclamped(
                    anim.StartPosition, anim.LiftPosition, u);
                piece.localRotation = Quaternion.SlerpUnclamped(
                    Quaternion.identity, anim.EndRotation, u * 0.15f);
                piece.localScale = Vector3.one;
                SetAlpha(index, 1f);
                return;
            }

            // Phase 3: detach.
            float progress = Mathf.Clamp01(
                (time - detachStart) / settings.breakDuration);

            if (settings.fallsUnderGravity)
            {
                // Wall and floor: the piece stays where it was on the surface and drops.
                // No blast term, which is what threw pieces at the viewer before.
                float fallen = settings.breakCurve.Evaluate(progress);
                piece.localPosition = anim.LiftPosition
                    + anim.FallDirection * (settings.fallDistance * fallen)
                    + anim.LateralDirection * (settings.lateralDistance * progress)
                    + Vector3.forward * (settings.settleDistance * progress);
                piece.localRotation = Quaternion.SlerpUnclamped(
                    Quaternion.identity, anim.EndRotation, Mathf.Lerp(0.15f, 1f, progress));
            }
            else
            {
                // Ceiling: hold position, turn a little, and let the fade do the work so
                // nothing rains down into the room.
                piece.localPosition = anim.LiftPosition;
                piece.localRotation = Quaternion.SlerpUnclamped(
                    Quaternion.identity, anim.EndRotation, progress);
            }

            piece.localScale = Vector3.one * Mathf.Lerp(1f, settings.endScale, progress);
            SetAlpha(index, FadeAlpha(progress));
        }

        float FadeAlpha(float progress)
        {
            if (progress <= settings.fadeStartFraction)
                return 1f;

            float span = 1f - settings.fadeStartFraction;
            if (span <= 1e-4f)
                return 1f - progress;

            return 1f - (progress - settings.fadeStartFraction) / span;
        }

        void SetAlpha(int index, float alpha)
        {
            MeshRenderer renderer = renderers[index];
            if (renderer == null)
                return;

            float a = Mathf.Clamp01(alpha);

            if (visualMode == ShellVisualMode.MRMask)
            {
                // The mask always outputs alpha 0 so passthrough shows through it. Fading it
                // toward 1 would paint black, not reveal VR, so instead the piece is simply
                // switched off once it has faded out, and VR shows where it was.
                bool visible = a > 0.02f;
                if (renderer.enabled != visible)
                    renderer.enabled = visible;
                return;
            }

            // DebugGray and VRSnapshot: drive the material alpha. DebugGray keeps its base
            // translucency; VRSnapshot is fully opaque while present.
            propertyBlock ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(propertyBlock);

            Color color = baseColor;
            color.a = baseColor.a * a;
            propertyBlock.SetColor(BaseColorId, color);
            propertyBlock.SetColor(ColorId, color);
            propertyBlock.SetFloat(AlphaId, a);
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
