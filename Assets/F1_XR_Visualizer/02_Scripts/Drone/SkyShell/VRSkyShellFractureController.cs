using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace F1XR.Drone.SkyShell
{
    /// <summary>
    /// Breaks the black shell of virtual space, not the circuit inside it.
    ///
    /// The previous attempt broke the track itself, and it was wrong for a reason no amount of
    /// tuning could fix: the F1 map is the thing the user came to look at, and tearing it into
    /// pieces to get back to the room destroys the subject to change the scene. What should
    /// come apart is the black nothing wrapped around it.
    ///
    /// Unity has no geometry for that nothing - a camera background is a clear colour, not a
    /// surface - so this builds one. A dark sphere seen from inside, indistinguishable from the
    /// background until it cracks, is the only way the emptiness can become something that
    /// falls.
    ///
    /// While the shell is closed it covers every pixel. That single property is what lets the
    /// whole exit commit behind it - the thousand-times scale reset, the origin snap, the
    /// switch to passthrough - with no black blink anywhere. The cover is the effect.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VRSkyShellFractureController : MonoBehaviour
    {
        [Header("Shell")]
        [Tooltip("Metres. Far enough to read as distance rather than as a ceiling, close " +
            "enough that one piece is a recognisable object instead of a wall of black.")]
        [SerializeField, Min(1f)] float skyShellRadius = 20f;

        [Tooltip("0 = 20 pieces, 1 = 80, 2 = 320. Eighty reads as a shell coming apart; " +
            "twenty reads as plates, and three hundred as gravel.")]
        [SerializeField, Range(0, 2)] int shellSubdivision = 1;

        [Tooltip("Triangles within one piece. Only curvature - the piece never splits here.")]
        [SerializeField, Range(0, 2)] int patchDetail = 1;

        [Tooltip("Metres of rim. Enough to catch the light when a piece turns edge-on, which " +
            "is the difference between a shard and a sheet of black paper.")]
        [SerializeField, Min(0f)] float shellThickness = 0.3f;

        [Tooltip("Exactly the VR background colour. Intact, the shell has to be the " +
            "background, not a large dark object in front of it - and it is drawn unlit for " +
            "the same reason, so no light direction or ambient term can give it away.")]
        [SerializeField] Color shellColor = new(0.015f, 0.02f, 0.04f, 1f);

        [Header("Fracture test")]
        [Tooltip("Breaks the shell by itself a few seconds after arriving, with no input at " +
            "all. Only ever for bringing up the render path on a headset where no context menu " +
            "exists; it makes the sky collapse while the user is still flying, which is a bug " +
            "in every other situation. The exit is the one thing that starts the break.")]
        [SerializeField] bool runFractureTestOnEntry;

        [Tooltip("Seconds in the compositing state before the automatic test breaks the shell. " +
            "Unused unless the automatic test above is on.")]
        [SerializeField, Min(0f)] float compositingTestRevealDelay = 3f;

        [Header("Timing")]
        [Tooltip("Seconds added per step as the crack walks out across neighbouring faces. " +
            "Short on purpose: until the first pieces go, the closed shell is the only thing " +
            "on screen, and that is the one moment of this transition that reads as a cut.")]
        [SerializeField, Min(0f)] float propagationStep = 0.045f;

        [Tooltip("Gap between a piece cracking and letting go.")]
        [SerializeField, Min(0f)] float looseDuration = 0.12f;

        [SerializeField, Min(0f)] float timingJitter = 0.04f;

        [SerializeField, Min(0.05f)] float fallDuration = 1.2f;

        [Header("Motion")]
        [Tooltip("How far a piece eases off the shell before it drops, in metres.")]
        [SerializeField, Min(0f)] float looseLift = 0.2f;

        [Tooltip("Fall distance as a multiple of the shell radius. Enough to leave the field " +
            "of view entirely; a piece that stops while still visible reads as scenery.")]
        [SerializeField, Range(0.5f, 5f)] float fallInRadii = 2.5f;

        [Tooltip("Sideways drift per unit of fall. Not a blast - it exists so pieces from " +
            "overhead pass wide of the viewer instead of straight through their head.")]
        [SerializeField, Range(0f, 2f)] float lateralSpread = 0.8f;

        [SerializeField, Range(0f, 180f)] float maxTumbleDegrees = 70f;

        [Tooltip("Fall shape. Ease-in reads as gravity taking hold; any overshoot reads as a " +
            "bounce, and a bounce reads as motion sickness.")]
        [SerializeField] AnimationCurve fallCurve = GravityCurve();

        [SerializeField] int randomSeed = 20260814;

        readonly List<float> crackTimes = new();
        readonly List<Vector3> fallDirections = new();
        readonly List<Quaternion> tumbles = new();

        SkyShellBuilder.Result built;
        Transform root;
        Transform viewer;
        Material intactMaterial;
        Light shellLight;

        F1XR.Experience.PassthroughTransitionController passthrough;
        MeshRenderer alphaSeal;
        Material alphaSealMaterial;
        Mesh alphaSealMesh;
        Material revealMaterial;
        Material detachedMaterial;
        readonly List<GameObject> revealMasks = new();
        readonly List<bool> detached = new();
        bool compositingActive;
        Coroutine testRoutine;
        int revealedCount;

        bool isPrepared;
        bool isRunning;
        bool followViewer;
        float elapsed;
        float totalDuration;

        public bool IsPrepared => isPrepared;
        public bool IsRunning => isRunning;
        public int FragmentCount => built != null ? built.Fragments.Count : 0;
        public float ShellRadius => skyShellRadius;

        /// <summary>True once the shell is built and drawing as the VR background.</summary>
        public bool IsVisible => isPrepared && root != null;

        /// <summary>
        /// How much of the sky has already been opened onto the real room.
        ///
        /// Counted by pieces that have let go, not by pieces that have finished falling. Those
        /// are very different numbers: a piece opens its hole the instant it detaches and then
        /// spends more than a second tumbling out of sight, and waiting for that animation is
        /// what made the exit sit on a finished MR view for nearly two seconds with nothing
        /// happening. What matters is how much of the background is real, and that is decided
        /// at the moment of detachment.
        ///
        /// A plain count is honest here because every face of a geodesic sphere covers very
        /// nearly the same solid angle.
        /// </summary>
        public float RevealCoverage
        {
            get
            {
                int total = built != null ? built.Fragments.Count : 0;
                return total == 0 ? 0f : (float)revealedCount / total;
            }
        }

        /// <summary>
        /// Fraction of the shell that has let go. Counting pieces is honest here in a way it
        /// was not for the circuit: every face of a geodesic sphere covers very nearly the
        /// same solid angle, so one piece gone really is one piece worth of sky opened.
        /// </summary>
        public float Coverage
        {
            get
            {
                if (!isRunning || built == null || built.Fragments.Count == 0)
                    return 0f;

                int detached = 0;
                foreach (float crack in crackTimes)
                {
                    if (elapsed >= crack + looseDuration)
                        detached++;
                }

                return (float)detached / built.Fragments.Count;
            }
        }

        static AnimationCurve GravityCurve() =>
            new(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 2f, 2f));

        // ------------------------------------------------------------------ prepare

        /// <summary>
        /// Builds the shell and shows it immediately, as the VR background.
        ///
        /// It is on for the whole flight, not switched on when the exit is pressed. A shell
        /// that appears at the moment it breaks is a curtain being drawn, and no amount of
        /// motion afterwards makes it read as the sky coming apart - the viewer saw it arrive.
        /// Intact it is an unlit near-black surface in the background queue, which is to say
        /// it is indistinguishable from the flat background it replaces.
        /// </summary>
        public void Prepare(
            Transform viewerTransform,
            F1XR.Experience.PassthroughTransitionController passthroughController = null)
        {
            Cleanup();

            passthrough = passthroughController;

            Material material = ResolveIntactMaterial();
            if (material == null)
                return;

            viewer = viewerTransform;

            float startedAt = Time.realtimeSinceStartup;

            // World space, parented to nothing, position tracked in LateUpdate instead.
            // Parenting to the camera would carry the shell's rotation as well, and a hole in
            // the sky that turns with the head is the one thing that gives away that the sky
            // is not a place.
            var rootObject = new GameObject("VR Sky Shell");
            root = rootObject.transform;
            root.rotation = Quaternion.identity;
            if (viewer != null)
                root.position = viewer.position;

            followViewer = true;

            built = SkyShellBuilder.Build(
                root, skyShellRadius, shellThickness, shellSubdivision, patchDetail, material);

            Material reveal = ResolveRevealMaterial();

            var random = new System.Random(randomSeed);
            foreach (SkyShellBuilder.Fragment fragment in built.Fragments)
            {
                if (fragment.Renderer != null)
                    fragment.Renderer.enabled = true;

                fallDirections.Add(ResolveFallDirection(fragment.CentreDirection, random));
                tumbles.Add(Quaternion.Euler(
                    NextSigned(random) * maxTumbleDegrees,
                    NextSigned(random) * maxTumbleDegrees,
                    NextSigned(random) * maxTumbleDegrees));
                crackTimes.Add(0f);
                detached.Add(false);
                revealMasks.Add(BuildRevealMask(fragment, reveal));
            }

            isPrepared = true;

            Debug.Log(
                $"[SkyShell][Prepare] fragments={built.Fragments.Count} " +
                $"tris={built.TriangleCount} buildMs=" +
                $"{(Time.realtimeSinceStartup - startedAt) * 1000f:F0} " +
                $"radius={skyShellRadius}m thickness={shellThickness}m " +
                $"anglePerFragment={AngularDiameterDegrees():F1}deg " +
                $"renderers={built.Fragments.Count * 2} visibleFromStart=true",
                this);

            LogState("StateB");

            // Prepared and then nothing. The shell stands there intact for as long as the
            // flight lasts; no timer runs, no coroutine is waiting, and the only thing that can
            // start the break is the exit. Time passing must never be enough.
            if (runFractureTestOnEntry)
            {
                Debug.LogWarning(
                    "[SkyShell] The automatic fracture test is on: the sky will collapse a few " +
                    "seconds from now without any input. Switch runFractureTestOnEntry off " +
                    "unless the render path is being brought up.",
                    this);

                testRoutine = StartCoroutine(FractureTestRoutine());
            }
        }

        /// <summary>
        /// The hole this fragment will leave, built now and left switched off.
        ///
        /// It shares the fragment's own mesh rather than copying it - same vertices, same
        /// place, no second mesh to allocate or free - and simply stays behind when the
        /// fragment moves. That sharing is what guarantees the requirement that the hole and
        /// the piece line up exactly: they are the same geometry, so they cannot drift apart.
        /// </summary>
        GameObject BuildRevealMask(SkyShellBuilder.Fragment fragment, Material reveal)
        {
            if (reveal == null)
                return null;

            MeshFilter filter = fragment.Transform.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
                return null;

            var host = new GameObject($"{fragment.Transform.name}_Reveal");
            host.transform.SetParent(fragment.Transform.parent, false);
            host.transform.localPosition = fragment.InitialLocalPosition;
            host.transform.localRotation = Quaternion.identity;
            host.transform.localScale = Vector3.one;

            host.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            MeshRenderer renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = reveal;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;

            return host;
        }

        float AngularDiameterDegrees()
        {
            int n = built != null ? built.Fragments.Count : 0;
            if (n == 0)
                return 0f;

            float steradians = 4f * Mathf.PI / n;
            return 2f * Mathf.Acos(1f - steradians / (2f * Mathf.PI)) * Mathf.Rad2Deg;
        }

        // ------------------------------------------------------------- compositing test

        IEnumerator FractureTestRoutine()
        {
            EnterCompositingTest();

            // A pause with the underlay live and nothing showing. Short, but it is the only
            // window in which the alpha seal can be seen to be doing its job rather than
            // assumed to be.
            float waited = 0f;
            while (waited < compositingTestRevealDelay)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            BeginFracture(viewer);
            testRoutine = null;
        }

        [ContextMenu("Step C: begin sky fracture")]
        public void TestBeginSkyFracture()
        {
            EnterCompositingTest();
            BeginFracture(viewer);
        }

        [ContextMenu("Step C: reset sky fracture")]
        public void TestResetSkyFracture() => ResetCompositingTest();

        /// <summary>
        /// Brings the underlay up and immediately takes the alpha channel away from the scene,
        /// so the real room is live behind everything and nothing shows.
        ///
        /// This is the half of the test that proves a negative, and it is the half that
        /// matters: the previous attempt switched the underlay on and the room bled through
        /// the circuit everywhere a texture happened to carry alpha below one. Entering this
        /// state must be completely invisible.
        /// </summary>
        [ContextMenu("Step B: enter compositing test state")]
        public void EnterCompositingTest()
        {
            if (compositingActive)
                return;

            if (!EnsureAlphaSeal())
                return;

            // The seal goes up first. The underlay coming live one frame before the alpha is
            // sealed is one frame of the real room across the whole circuit.
            alphaSeal.enabled = true;

            // Layer live, camera background still fully opaque. That combination already
            // exists on the controller and needs no change to it: the alpha now comes from the
            // seal, not from the clear.
            if (passthrough != null)
                passthrough.PrepareMRIncoming();

            compositingActive = true;
            LogState("StateC");
        }

        /// <summary>
        /// A face above the horizon and in front of the viewer, for the crack to start at.
        ///
        /// Above the horizon on purpose: down there the circuit is in the way, and a hole that
        /// stays shut because the track is correctly in front of it looks exactly like a hole
        /// that stays shut because the compositing is broken.
        /// </summary>
        int PickSkyFaceInFront()
        {
            Vector3 forward = viewer != null ? viewer.forward : Vector3.forward;

            int best = -1;
            float bestDot = float.MinValue;

            for (int i = 0; i < built.Fragments.Count; i++)
            {
                Vector3 direction = built.Fragments[i].CentreDirection;
                if (direction.y < 0.2f)
                    continue;

                float dot = Vector3.Dot(direction, forward);
                if (dot <= bestDot)
                    continue;

                bestDot = dot;
                best = i;
            }

            return best;
        }

        /// <summary>
        /// Everything back exactly as normal VR left it: no mask, no seal, no underlay.
        /// </summary>
        [ContextMenu("Step B: reset to normal VR")]
        public void ResetCompositingTest() => ResetCompositingTest(true);

        /// <param name="restorePassthrough">
        /// False once the exit has already committed to MR. Putting the VR endpoint back at
        /// that point would switch the underlay off and the camera background opaque again -
        /// the real room the user is now standing in would black out on the frame this tidies
        /// up after itself.
        /// </param>
        public void ResetCompositingTest(bool restorePassthrough)
        {
            if (testRoutine != null)
            {
                StopCoroutine(testRoutine);
                testRoutine = null;
            }

            isRunning = false;
            elapsed = 0f;
            revealedCount = 0;

            // Every piece back where it started, drawing the intact sky material again, with
            // its hole shut. The masks are kept rather than destroyed - they are needed again
            // the next time this runs, and rebuilding three hundred of them is the sort of
            // cost that shows up as a hitch exactly when the test is being repeated.
            for (int i = 0; built != null && i < built.Fragments.Count; i++)
            {
                SkyShellBuilder.Fragment fragment = built.Fragments[i];

                if (fragment.Transform != null)
                {
                    fragment.Transform.localPosition = fragment.InitialLocalPosition;
                    fragment.Transform.localRotation = Quaternion.identity;
                }

                if (fragment.Renderer != null)
                {
                    fragment.Renderer.sharedMaterial = intactMaterial;
                    fragment.Renderer.enabled = true;
                }

                if (i < revealMasks.Count && revealMasks[i] != null)
                {
                    MeshRenderer mask = revealMasks[i].GetComponent<MeshRenderer>();
                    if (mask != null)
                        mask.enabled = false;
                }

                if (i < detached.Count)
                    detached[i] = false;
            }

            if (shellLight != null)
                shellLight.enabled = false;

            if (alphaSeal != null)
                alphaSeal.enabled = false;

            if (compositingActive && restorePassthrough && passthrough != null)
                passthrough.ApplyVRImmediate();

            compositingActive = false;
            LogState(restorePassthrough ? "Reset" : "ResetKeepingMR");
        }

        void LogState(string label)
        {
            Camera camera = Camera.main;
            Debug.Log(
                $"[SkyShellTest][{label}] passthroughState=" +
                $"{(passthrough != null ? passthrough.State.ToString() : "none")} " +
                $"alphaSeal={(alphaSeal != null && alphaSeal.enabled)} " +
                $"cameraAlpha={(camera != null ? camera.backgroundColor.a.ToString("F2") : "?")} " +
                $"revealedFaces={revealedCount}",
                this);
        }

        /// <summary>
        /// Down, pushed outwards. Straight down alone would drop the pieces overhead through
        /// the viewer's head; a purely radial push would be an explosion, with pieces from
        /// below rising. Down plus a horizontal shove away from the centre is the only
        /// combination that both collapses and clears.
        /// </summary>
        Vector3 ResolveFallDirection(Vector3 centreDirection, System.Random random)
        {
            Vector3 horizontal = Vector3.ProjectOnPlane(centreDirection, Vector3.up);

            // Directly overhead and directly underfoot have no horizontal direction of their
            // own, so they get an arbitrary but fixed one rather than none at all.
            if (horizontal.sqrMagnitude < 1e-4f)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                horizontal = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            }

            return (Vector3.down + horizontal.normalized * lateralSpread).normalized;
        }

        // ------------------------------------------------------------------ run

        /// <summary>
        /// Closes the shell around the viewer and starts the crack from whatever they are
        /// looking at. Returns false if there is no shell to run, in which case the caller
        /// keeps its old exit and nobody is trapped in VR by a fracture that failed to build.
        /// </summary>
        public bool BeginFracture(Transform viewerTransform)
        {
            if (!isPrepared || isRunning || built == null || built.Fragments.Count == 0)
                return false;

            // Whoever starts the break gets the compositing with it. The underlay has to be
            // live and the alpha sealed before the first hole opens, and leaving that to the
            // caller means a caller that forgets breaks the sky onto nothing.
            EnterCompositingTest();

            // Frozen exactly where it already is. Nothing is moved, re-parented or re-centred:
            // the shell has been standing around the viewer for the whole flight, and the only
            // change is that it stops following them. Every fragment's world position across
            // this call is identical, which is what keeps the first frame of the break
            // indistinguishable from the last frame before it.
            followViewer = false;

            // Above the horizon and in front. Starting on a face the circuit is standing in
            // front of would open a hole nobody can see and make the break look like it began
            // somewhere behind them.
            int origin = PickSkyFaceInFront();
            if (origin < 0)
                origin = ResolveOriginFragment(viewerTransform);

            ComputeCrackTimes(origin);
            BuildLight();

            if (shellLight != null)
                shellLight.enabled = true;

            elapsed = 0f;
            revealedCount = 0;
            totalDuration = MaxCrackTime() + looseDuration + fallDuration;
            isRunning = true;

            Debug.Log(
                $"[SkyShell][Begin] origin={origin} fragments={built.Fragments.Count} " +
                $"maxDepth={MaxHopDepth(origin)} propagationStep={propagationStep} " +
                $"duration={totalDuration:F2}s fall={skyShellRadius * fallInRadii:F1}m",
                this);

            return true;
        }

        /// <summary>Hops from the origin to the furthest face, for the log only.</summary>
        int MaxHopDepth(int origin)
        {
            float max = 0f;
            foreach (float t in crackTimes)
                max = Mathf.Max(max, t);

            return propagationStep > 0f ? Mathf.RoundToInt(max / propagationStep) : 0;
        }

        /// <summary>Keeps the shell around the head without inheriting how the head is turned.</summary>
        void LateUpdate()
        {
            if (!followViewer || root == null || viewer == null)
                return;

            root.position = viewer.position;
        }

        /// <summary>The piece the viewer is looking straight at, so the break starts in view.</summary>
        int ResolveOriginFragment(Transform viewerTransform)
        {
            if (viewerTransform == null)
                return 0;

            Vector3 forward = viewerTransform.forward;
            int best = 0;
            float bestDot = float.MinValue;

            for (int i = 0; i < built.Fragments.Count; i++)
            {
                float dot = Vector3.Dot(built.Fragments[i].CentreDirection, forward);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>
        /// Breadth-first across shared edges from the origin piece. Every hop costs one
        /// propagation step, so the crack spreads outward face by face instead of the whole
        /// shell letting go at once - the difference between something breaking and something
        /// being switched off.
        /// </summary>
        void ComputeCrackTimes(int origin)
        {
            var random = new System.Random(randomSeed + 31);
            int count = built.Fragments.Count;

            for (int i = 0; i < count; i++)
                crackTimes[i] = -1f;

            var queue = new Queue<int>();
            crackTimes[origin] = 0f;
            queue.Enqueue(origin);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                foreach (int next in built.Fragments[current].Neighbours)
                {
                    if (crackTimes[next] >= 0f)
                        continue;

                    crackTimes[next] = crackTimes[current] + propagationStep +
                        (float)random.NextDouble() * timingJitter;
                    queue.Enqueue(next);
                }
            }

            // An unreachable piece would otherwise hang in the air forever, holding a patch of
            // black across the room that never leaves.
            float last = MaxCrackTime();
            for (int i = 0; i < count; i++)
            {
                if (crackTimes[i] < 0f)
                    crackTimes[i] = last + propagationStep;
            }
        }

        float MaxCrackTime()
        {
            float max = 0f;
            foreach (float t in crackTimes)
                max = Mathf.Max(max, t);

            return max;
        }

        void Update()
        {
            if (!isRunning)
                return;

            elapsed += Time.deltaTime;

            float fallDistance = skyShellRadius * fallInRadii;

            for (int i = 0; i < built.Fragments.Count; i++)
            {
                SkyShellBuilder.Fragment fragment = built.Fragments[i];
                if (fragment.Transform == null)
                    continue;

                float since = elapsed - crackTimes[i];
                if (since <= 0f)
                    continue;

                Vector3 outward = fragment.CentreDirection;

                if (since < looseDuration)
                {
                    // Cracked, not gone: the piece eases off the surface and tips slightly, so
                    // the shell visibly loses its structure before any of it falls.
                    float u = Mathf.Clamp01(since / Mathf.Max(looseDuration, 0.0001f));
                    fragment.Transform.localPosition =
                        fragment.InitialLocalPosition + outward * (looseLift * u);
                    fragment.Transform.localRotation = Quaternion.SlerpUnclamped(
                        Quaternion.identity, tumbles[i], u * 0.12f);
                    continue;
                }

                // The instant it lets go: the hole it leaves opens, and it stops being sky and
                // starts being an object. Both happen once, here, so the piece and its hole can
                // never disagree about whether it has gone.
                Detach(i, fragment);

                float progress = Mathf.Clamp01((since - looseDuration) / fallDuration);
                float fallen = fallCurve != null ? fallCurve.Evaluate(progress) : progress;

                fragment.Transform.localPosition = fragment.InitialLocalPosition +
                    outward * looseLift +
                    fallDirections[i] * (fallDistance * fallen);

                fragment.Transform.localRotation = Quaternion.SlerpUnclamped(
                    Quaternion.identity, tumbles[i], Mathf.Lerp(0.12f, 1f, progress));

                if (progress >= 1f && fragment.Renderer != null && fragment.Renderer.enabled)
                    fragment.Renderer.enabled = false;
            }

            if (elapsed >= totalDuration)
            {
                isRunning = false;
                Debug.Log(
                    $"[SkyShell][FractureComplete] duration={elapsed:F2}s " +
                    $"revealedFragments={revealedCount} of {built.Fragments.Count}",
                    this);
            }
        }

        /// <summary>
        /// One piece leaves the sky: its hole opens and it takes on a material that can be lit.
        ///
        /// The material swap assigns a second shared material to this renderer; it never edits
        /// the intact one. Editing that would change every other face at once, and the sky
        /// would brighten as a whole the moment the first piece moved.
        /// </summary>
        void Detach(int index, SkyShellBuilder.Fragment fragment)
        {
            if (index >= detached.Count || detached[index])
                return;

            detached[index] = true;
            revealedCount++;

            if (index < revealMasks.Count && revealMasks[index] != null)
            {
                MeshRenderer mask = revealMasks[index].GetComponent<MeshRenderer>();
                if (mask != null)
                    mask.enabled = true;
            }

            Material shard = ResolveDetachedMaterial();
            if (shard != null && fragment.Renderer != null)
                fragment.Renderer.sharedMaterial = shard;
        }

        // ------------------------------------------------------------------ cleanup

        public void Cleanup() => Cleanup(true);

        /// <param name="restorePassthrough">
        /// False when the caller has already moved the screen to MR and intends to keep it
        /// there. See <see cref="ResetCompositingTest(bool)"/>.
        /// </param>
        public void Cleanup(bool restorePassthrough)
        {
            // Before the shell goes, so the underlay and the seal are put back in the order
            // they were raised and normal VR is restored even if this is a hard teardown.
            ResetCompositingTest(restorePassthrough);

            if (alphaSeal != null)
            {
                DestroySafely(alphaSeal.gameObject);
                alphaSeal = null;
            }

            DestroySafely(alphaSealMesh);
            alphaSealMesh = null;

            if (root != null)
                DestroySafely(root.gameObject);

            if (built != null)
            {
                foreach (Mesh mesh in built.OwnedMeshes)
                    DestroySafely(mesh);

                built = null;
            }

            root = null;
            viewer = null;
            passthrough = null;
            shellLight = null;
            crackTimes.Clear();
            fallDirections.Clear();
            tumbles.Clear();
            revealMasks.Clear();
            detached.Clear();
            revealedCount = 0;

            isPrepared = false;
            isRunning = false;
            followViewer = false;
            elapsed = 0f;
            totalDuration = 0f;
        }

        void OnDestroy()
        {
            Cleanup();
            DestroySafely(intactMaterial);
            DestroySafely(detachedMaterial);
            DestroySafely(alphaSealMaterial);
            DestroySafely(revealMaterial);
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// One shared material for every intact face. Unlit, background queue, no depth
        /// write - the shell is the sky, not an object in it.
        /// </summary>
        Material ResolveIntactMaterial()
        {
            if (intactMaterial != null)
                return intactMaterial;

            Shader shader = Shader.Find("F1XR/SkyShellIntact");
            if (shader == null)
            {
                Debug.LogError(
                    "[SkyShell] F1XR/SkyShellIntact is missing. Without it the shell would be " +
                    "ordinary opaque geometry: a twenty metre sphere cutting through a " +
                    "kilometre-wide circuit, which reads as a curtain, not as the sky.",
                    this);
                return null;
            }

            intactMaterial = new Material(shader) { name = "SkyShell Intact (Runtime)" };
            intactMaterial.SetColor("_BaseColor", shellColor);
            return intactMaterial;
        }

        /// <summary>
        /// Builds the alpha seal: a quad whose vertex shader ignores every matrix and emits the
        /// clip-space corners, so it covers each eye's whole viewport by construction.
        /// </summary>
        bool EnsureAlphaSeal()
        {
            if (alphaSeal != null)
                return true;

            if (viewer == null)
            {
                Debug.LogError("[SkyShellTest] No viewer transform to hang the alpha seal on.", this);
                return false;
            }

            Shader shader = Shader.Find("F1XR/SkyShellAlphaSeal");
            if (shader == null)
            {
                Debug.LogError(
                    "[SkyShellTest] F1XR/SkyShellAlphaSeal is missing. Without it the underlay " +
                    "would show through every track pixel whose texture alpha is under one.",
                    this);
                return false;
            }

            alphaSealMaterial = new Material(shader) { name = "SkyShell Alpha Seal (Runtime)" };

            alphaSealMesh = new Mesh { name = "SkyShellAlphaSeal" };
            alphaSealMesh.SetVertices(new List<Vector3>
            {
                new(-1f, -1f, 0f), new(1f, -1f, 0f), new(-1f, 1f, 0f), new(1f, 1f, 0f)
            });
            alphaSealMesh.SetTriangles(new[] { 0, 2, 1, 2, 3, 1 }, 0, true);

            // Culling works off these, and the real vertices never go anywhere near them.
            // Without a bounds big enough to always be considered visible, the seal vanishes
            // the moment the camera turns away from wherever Unity thinks it is.
            alphaSealMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);

            var host = new GameObject("Sky Shell Alpha Seal");
            host.transform.SetParent(viewer, false);
            host.transform.localPosition = Vector3.zero;
            host.transform.localRotation = Quaternion.identity;
            host.transform.localScale = Vector3.one;

            host.AddComponent<MeshFilter>().sharedMesh = alphaSealMesh;
            alphaSeal = host.AddComponent<MeshRenderer>();
            alphaSeal.sharedMaterial = alphaSealMaterial;
            alphaSeal.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            alphaSeal.receiveShadows = false;
            alphaSeal.enabled = false;

            Debug.Log(
                $"[SkyShellTest] alpha seal built, queue={alphaSealMaterial.renderQueue}.", this);

            return true;
        }

        /// <summary>
        /// The existing spatial reveal shader, unmodified, with its output alpha at zero.
        ///
        /// Its depth test is LEqual rather than Always, which is exactly what is wanted here:
        /// the mask sits out at the shell radius, so a piece of circuit standing closer wins
        /// the depth test and keeps its pixels. The room opens where the sky was, not over the
        /// top of the world.
        /// </summary>
        Material ResolveRevealMaterial()
        {
            if (revealMaterial != null)
                return revealMaterial;

            Shader shader = Shader.Find("F1XR/ShellPassthroughReveal");
            if (shader == null)
            {
                Debug.LogError("[SkyShellTest] F1XR/ShellPassthroughReveal is missing.", this);
                return null;
            }

            revealMaterial = new Material(shader) { name = "SkyShell Reveal (Runtime)" };
            revealMaterial.SetFloat("_OutputAlpha", 0f);
            return revealMaterial;
        }

        /// <summary>
        /// What a piece is made of once it has come away: the same near-black, but lit, so the
        /// rim and the turn are readable.
        ///
        /// Ordinary opaque geometry, which puts it before the alpha seal - a falling shard
        /// therefore has its alpha sealed to one like everything else and stays solid, and the
        /// hole it left is only opened afterwards by the mask. A shard drifting across its own
        /// hole is depth tested against it and correctly stays opaque.
        /// </summary>
        Material ResolveDetachedMaterial()
        {
            if (detachedMaterial != null)
                return detachedMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("[SkyShell] URP/Lit is missing; shards cannot be shaded.", this);
                return null;
            }

            detachedMaterial = new Material(shader) { name = "SkyShell Shard (Runtime)" };
            detachedMaterial.SetColor("_BaseColor", shellColor);
            if (detachedMaterial.HasProperty("_Smoothness"))
                detachedMaterial.SetFloat("_Smoothness", 0.1f);
            if (detachedMaterial.HasProperty("_Metallic"))
                detachedMaterial.SetFloat("_Metallic", 0f);

            return detachedMaterial;
        }

        /// <summary>
        /// The light the falling pieces are shaded by, created only when the break starts.
        ///
        /// Deliberately not present during the flight: it is a directional light, so it would
        /// fall on the circuit as well and quietly change how the track itself looks for the
        /// whole trip. The intact shell needs no light at all - it is unlit by design.
        /// </summary>
        void BuildLight()
        {
            if (shellLight != null)
                return;

            var lightObject = new GameObject("Sky Shell Light");
            lightObject.transform.SetParent(root, false);
            lightObject.transform.localRotation = Quaternion.Euler(55f, -35f, 0f);

            shellLight = lightObject.AddComponent<Light>();
            shellLight.type = LightType.Directional;
            shellLight.color = new Color(0.72f, 0.79f, 1f);
            shellLight.intensity = 0.3f;
            shellLight.shadows = LightShadows.None;
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
}
