using System;
using System.Collections;
using System.Collections.Generic;
using F1XR.Experience;
using F1XR.RestAPI.Replay.Track.Build;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace F1XR.Drone
{
    [DisallowMultipleComponent]
    public sealed class VRDroneCoordinator : MonoBehaviour
    {
        const string EnvironmentName = "VRDroneEnvironment";

        [SerializeField, Min(1f)] float vrScaleMultiplier = 1000f;

        [Header("Transition")]
        [Tooltip("How much of the growth the viewer actually watches. The rest happens behind " +
            "the blink. Showing all thousand times of it is what made the transition " +
            "sickening: the whole field of view expands for over a second while the inner ear " +
            "reports sitting perfectly still.")]
        [SerializeField, Min(1f)] float visibleScaleMultiplier = 10f;

        [Tooltip("Seconds spent on the part of the growth the viewer sees, going in.")]
        [SerializeField, Min(0f)] float visibleScaleDurationEnter = 0.7f;

        [Tooltip("Seconds spent shrinking the map back down on the table, coming out. The " +
            "viewer is already back in MR by then and only the map moves.")]
        [SerializeField, Min(0f)] float visibleScaleDurationExit = 0.7f;

        [Tooltip("Seconds to close the blink. A blink, not a fade to black: long enough to " +
            "hide a jump, short enough that it never reads as a loading screen.")]
        [SerializeField, Min(0f)] float occlusionInDuration = 0.1f;

        [SerializeField, Min(0f)] float occlusionOutDuration = 0.1f;

        [Tooltip("How much of the circuit's surface area has to be gone before the blink takes " +
            "over. Measured by area, not by counting cells: cells vary enormously in how much " +
            "geometry they hold, and a cell count can read three-quarters done while the view " +
            "is still almost entirely virtual - which leaves the blink doing the work the " +
            "break was supposed to do.")]
        [SerializeField, Range(0.1f, 1f)] float hiddenCommitCoverage = 0.88f;

        [Tooltip("Blink used for the final commit only. Short: this is meant to read as a " +
            "single eye-blink during the collapse, not as the transition itself.")]
        [SerializeField, Min(0f)] float commitOcclusionInDuration = 0.07f;

        [SerializeField, Min(0f)] float commitOcclusionOutDuration = 0.07f;

        [Tooltip("Shape of the visible growth. Linear on purpose. The scale itself is already " +
            "exponential, so linear progress means a constant ratio per second and therefore " +
            "a constant rate of visual flow; an ease on top of that spikes the apparent zoom " +
            "speed halfway through, which is the most nauseating moment of all.")]
        [SerializeField] AnimationCurve scaleCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Tooltip("Unused by the blink-based transition; kept so the previous continuous " +
            "version can be restored without re-authoring anything.")]
        [SerializeField, Min(0f)] float enterDuration = 1.2f;

        [Tooltip("Unused by the blink-based transition. See enterDuration.")]
        [SerializeField, Min(0f)] float exitDuration = 1f;

        [Tooltip("Unused by the blink-based transition. See enterDuration.")]
        [SerializeField] AnimationCurve cameraCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Sky Shell Exit")]
        [Tooltip("Breaks the circuit itself on the way out. Off. The map is what the user " +
            "came to look at, and tearing it apart to change scene destroys the subject to " +
            "change the frame. Kept as a switch rather than deleted so the previous behaviour " +
            "can be compared against the shell without restoring anything.")]
        [SerializeField] bool useTrackFracture;

        [Tooltip("Hands the exit to the sky shell break. Off during Phase A: the shell is " +
            "being proven as a background and as a hole onto the real room first, and until " +
            "that is confirmed on the headset the exit keeps the blink it already had. " +
            "Turning this on before the hole test passes just hides which half is wrong.")]
        [SerializeField] bool useSkyShellExit;

        [Tooltip("Seconds of nothing at all once the shell is gone. The viewer has just been " +
            "somewhere else at a thousand times this size; dropping the map in front of them " +
            "the same instant gives them no chance to register that they are back in their " +
            "own room.")]
        [SerializeField, Min(0f)] float mapRevealDelay = 1f;

        [Tooltip("How much of the sky has to be open onto the real room before the exit " +
            "commits. High on purpose: the commit hides the circuit and snaps the viewer's " +
            "origin back, and the only thing that makes those invisible is that almost " +
            "everything they could be seen against is already the real room.")]
        [SerializeField, Range(0.5f, 1f)] float skyRevealCommitThreshold = 0.98f;

        [Tooltip("Seconds after the break starts, after which the exit commits whatever the " +
            "coverage says. A bug that stops the coverage rising must not be able to leave " +
            "someone stuck inside a circuit with no way out.")]
        [SerializeField, Min(0.5f)] float skyFractureCommitTimeout = 4f;

        [SerializeField, Min(0f)] float mapRevealDuration = 0.7f;

        [Tooltip("Size the map appears at before it grows into place, as a fraction of its " +
            "tabletop size.")]
        [SerializeField, Range(0.01f, 1f)] float mapRevealStartScaleMultiplier = 0.1f;

        [Tooltip("Must be monotonic. A bounce or an elastic overshoot on an object sitting " +
            "on a real table reads as the table being unreal.")]
        [SerializeField] AnimationCurve mapRevealCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Debug")]
        [SerializeField, FormerlySerializedAs("showGrabVolumeVisual")]
        bool showGrabRange;

        TrackRevealPlacer trackPlacer;
        XROrigin xrOrigin;
        Camera xrCamera;
        GameObject passthroughLayer;

        // Optional. Where the host scene has one, it owns the MR and VR screen endpoints -
        // the passthrough layer and the camera's clear settings - and this coordinator stops
        // writing them. Two components setting the same three values from different
        // directions is how the passthrough state ends up wedged after a few transitions.
        // Host scenes without one keep the original direct control, so nothing regresses.
        PassthroughTransitionController passthrough;

        GameObject environment;
        DroneViewCubeSpawner cubeSpawner;
        VRDroneFlightController flightController;
        VRDroneAudioDistanceScaler audioDistanceScaler;
        Transform placementRoot;
        Transform visualRoot;
        Transform hiddenCube;
        GameObject ground;
        VRDroneHud droneHud;
        readonly List<XRBaseInteractable> disabledInteractables = new();

        // Host-scene UI switched off for the flight. Only the ones this turned off are
        // recorded, so a panel the user had already closed stays closed on the way back.
        readonly List<Canvas> hiddenCanvases = new();

        Vector3 savedOriginPosition;
        Quaternion savedOriginRotation;
        Vector3 savedPlacementLocalScale;

        // The map's authored local position, taken before the entry anchoring nudges it.
        // Restored before the shrink so that stretch starts on the table rather than wherever
        // the anchor correction left the pivot.
        Vector3 savedPlacementLocalPositionAtEnter;
        Vector3 savedVisualLocalPosition;
        Quaternion savedVisualLocalRotation;
        Vector3 savedVisualLocalScale;
        CameraClearFlags savedClearFlags;
        Color savedBackgroundColor;
        bool savedPassthroughActive;
        bool savedTrackEditMode;
        bool appliedShowGrabVolumeVisual;
        bool isVrActive;

        // Enter and Exit are still synchronous, so this is only ever true inside one of them.
        // It exists now because the scale-up and the camera move are about to become timed
        // sequences, and a second CubeReleased arriving halfway through would otherwise save
        // the transition's own state as the MR state to return to.
        bool isTransitioning;
        Coroutine transitionRoutine;
        VRDroneTransitionOccluder occluder;
        TrackAnchorStabilizer anchorStabilizer;
        TrackFracture.VRTrackFractureController trackFracture;
        SkyShell.VRSkyShellFractureController skyShell;

        // The chosen spot in world space, held from the moment the cube is dropped until the
        // map is back on the table. Both the growth and the collapse are centred on it.
        Vector3 entryWorldAnchor;

        // The chosen spot in the map's own coordinates. Held for the length of a transition
        // because the map is being scaled underneath it: the world position it maps to is
        // different every frame, and only this stays fixed.
        Vector3 entryPointLocal;
        Scene hostScene;
        bool hasHostScene;

        public bool IsVrActive => isVrActive;
        public Transform VrCameraTransform =>
            xrCamera != null ? xrCamera.transform : null;

        public void ConfigureHostScene(Scene scene)
        {
            if (!scene.isLoaded)
            {
                Debug.LogError(
                    "[VRDrone] The configured host scene is not loaded.",
                    this);
                return;
            }

            hostScene = scene;
            hasHostScene = true;
        }

        void Start()
        {
            StartCoroutine(Initialize());
        }

        void Update()
        {
            if (showGrabRange == appliedShowGrabVolumeVisual)
                return;

            appliedShowGrabVolumeVisual = showGrabRange;
            cubeSpawner?.SetGrabVolumeVisual(showGrabRange);
        }

        void OnDestroy()
        {
            if (cubeSpawner != null)
                cubeSpawner.CubeReleased -= EnterVr;
        }

        IEnumerator Initialize()
        {
            while (!TryResolveReferences())
                yield return null;

            cubeSpawner = trackPlacer.GetComponent<DroneViewCubeSpawner>();
            if (cubeSpawner == null)
                cubeSpawner = trackPlacer.gameObject.AddComponent<DroneViewCubeSpawner>();

            cubeSpawner.Configure(
                trackPlacer,
                xrCamera.transform,
                showGrabRange);
            appliedShowGrabVolumeVisual = showGrabRange;
            cubeSpawner.CubeReleased -= EnterVr;
            cubeSpawner.CubeReleased += EnterVr;

            audioDistanceScaler = GetComponent<VRDroneAudioDistanceScaler>();
            if (audioDistanceScaler == null)
                audioDistanceScaler = gameObject.AddComponent<VRDroneAudioDistanceScaler>();
            audioDistanceScaler.ConfigureHostScene(hostScene);

            flightController = GetComponent<VRDroneFlightController>();
            if (flightController == null)
            {
                Debug.LogError(
                    "[VRDrone] VRDroneFlightController is missing on VRDroneRuntime.",
                    this);
            }
            else
            {
                flightController.Configure(this);
            }

            environment.SetActive(false);
            EnsureEnvironment();
            droneHud = GetComponent<VRDroneHud>();
            if (droneHud == null)
                droneHud = gameObject.AddComponent<VRDroneHud>();
            droneHud.Configure(environment.transform);

        }

        bool TryResolveReferences()
        {
            Scene vrScene = gameObject.scene;
            if (!hasHostScene || !hostScene.isLoaded || !vrScene.isLoaded)
                return false;

            trackPlacer ??= FindInScene<TrackRevealPlacer>(hostScene);
            xrOrigin ??= FindInScene<XROrigin>(hostScene);
            environment ??= FindRoot(vrScene, EnvironmentName);
            passthroughLayer ??= FindInScene(hostScene, "Passthrough Layer");
            passthrough ??= FindInScene<PassthroughTransitionController>(hostScene);

            // Optional. Absent means the exit keeps working exactly as it did, with the blink
            // and no fracture.
            trackFracture ??= GetComponent<TrackFracture.VRTrackFractureController>();
            skyShell ??= GetComponent<SkyShell.VRSkyShellFractureController>();
            xrCamera ??= xrOrigin != null ? xrOrigin.Camera : Camera.main;

            return trackPlacer != null &&
                xrOrigin != null &&
                xrCamera != null &&
                environment != null &&
                passthroughLayer != null;
        }

        void EnterVr(Transform cubeTransform)
        {
            // Releasing the cube is the only way in, and a release fires every time the user
            // lets go - including the small adjusting drops while they position it. Anything
            // already in flight has to win.
            if (isVrActive || isTransitioning || cubeTransform == null ||
                trackPlacer == null || !trackPlacer.HasPlacement)
            {
                return;
            }

            Transform placement = trackPlacer.PlacementTransform;
            placementRoot = placement;
            visualRoot = placementRoot != null
                ? placementRoot.Find("Visual") ?? placementRoot
                : null;
            if (visualRoot == null)
                return;

            isTransitioning = true;
            transitionRoutine = StartCoroutine(EnterVrRoutine(cubeTransform));
        }

        IEnumerator EnterVrRoutine(Transform cubeTransform)
        {
            // The chosen spot, in the map's own coordinates. Held for the whole transition:
            // the map is being scaled underneath it, so the world position it corresponds to
            // changes, and only the local one stays meaningful.
            entryPointLocal = placementRoot.InverseTransformPoint(cubeTransform.position);

            SaveMrState();
            LockTrackInteraction();

            hiddenCube = cubeTransform;
            hiddenCube.gameObject.SetActive(false);

            Debug.Log(
                $"[DroneTransition][EnterStart] entryLocal={entryPointLocal} " +
                $"startScale={savedPlacementLocalScale} " +
                $"visible=×{visibleScaleMultiplier} hidden=×{vrScaleMultiplier}",
                this);

            // The chosen spot, pinned in the world for the whole round trip. The map grows
            // away from it going in and collapses back into it coming out, so both halves are
            // centred on the place the user actually picked. Captured while the map is still
            // at its MR scale, which is why restoring that scale later puts the map back
            // exactly where it started with no correction left over.
            entryWorldAnchor = placementRoot.TransformPoint(entryPointLocal);

            // Position is this component's for the duration; give it back on the way out.
            SetAnchorStabilizerPaused(true);

            // ---- Phase A: the part the viewer watches -------------------------------------
            // The viewer does not move at all. One moving thing: the map growing out of the
            // spot they chose, with the real room steady around it.
            float elapsed = 0f;
            while (visibleScaleDurationEnter > 0f && elapsed < visibleScaleDurationEnter)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / visibleScaleDurationEnter);
                ApplyScaleAnchored(
                    Mathf.Pow(visibleScaleMultiplier, Evaluate(scaleCurve, t)),
                    entryWorldAnchor);
                yield return null;
            }

            ApplyScaleAnchored(visibleScaleMultiplier, entryWorldAnchor);

            // ---- Phase B: everything expensive, behind the blink --------------------------
            yield return Occlude(occlusionInDuration);
            PerformEnterHiddenJump();

            // ---- Phase C: open the eyes, already inside -----------------------------------
            yield return Reveal(occlusionOutDuration);

            // Both fractures are built now, while the viewer is settling in and has a whole
            // trip ahead of them, rather than at the moment they ask to leave. Building the
            // meshes on the exit press would stall the frame exactly when the motion is most
            // visible.
            // isActiveAndEnabled, not just non-null: unticking the component in the inspector
            // is then a one-click A/B between a fracture exit and the plain blink exit, which
            // is the only way to tell which of the two owns a visual problem.
            if (useTrackFracture && trackFracture != null && trackFracture.isActiveAndEnabled)
                yield return trackFracture.Prepare(placementRoot, visualRoot);

            RaiseSkyShell();

            Debug.Log(
                $"[DroneTransition][EnterComplete] scale={placementRoot.localScale} " +
                $"cameraWorld={xrCamera.transform.position} " +
                $"origin={xrOrigin.transform.position}",
                this);

            transitionRoutine = null;
            isTransitioning = false;
        }

        /// <summary>
        /// The three things that actually cause the nausea - the rest of the scale jump, the
        /// long teleport, and the swap from real room to VR - all done in the frames nobody
        /// can see. Everything here is a snap; there is nothing to animate when the view is
        /// covered.
        /// </summary>
        void PerformEnterHiddenJump()
        {
            // Same anchor as the visible phase, so the viewer ends up standing exactly where
            // the cube was sitting on the real table.
            ApplyScaleAnchored(vrScaleMultiplier, entryWorldAnchor);

            // Recomputed at the final scale, so the viewer still arrives at the spot they
            // put the cube on rather than wherever that point used to be.
            Vector3 entryWorldFinal = placementRoot.TransformPoint(entryPointLocal);
            xrOrigin.MoveCameraToWorldLocation(entryWorldFinal);

            audioDistanceScaler?.Apply(vrScaleMultiplier);

            // Switched on here rather than before Phase A: its ground plane is a kilometre
            // across and would otherwise be sitting in the middle of the real room while the
            // map is still on the table.
            environment.SetActive(true);
            ApplyVrScreenEndpoint();
            PlaceGround(placementRoot.up);
            HideHostUi();

            isVrActive = true;
            droneHud?.Show(xrCamera);
            flightController?.ResetFlight();
        }

        /// <summary>
        /// Scale is interpolated as a power, not a straight line. The travel is one to a
        /// thousand, and lerping that puts the map at five hundred times by halfway - the
        /// whole growth would be over in the first fraction of a second and the rest of the
        /// transition would look like nothing happening. Raising the multiplier to the
        /// progress makes each equal slice of time a constant ratio instead: a quarter of the
        /// way in is around six times, halfway is around thirty.
        /// </summary>
        void ApplyScaleMultiplier(float multiplier)
        {
            // Always from the saved MR scale, never from the current one: multiplying the
            // live scale each pass would compound across repeated entries.
            placementRoot.localScale = Vector3.Scale(
                savedPlacementLocalScale,
                Vector3.one * Mathf.Max(multiplier, 0.0001f));
        }

        /// <summary>
        /// Scales the map about the chosen spot instead of about its own origin.
        ///
        /// The pivot sits at the middle of the track, so scaling alone grows the middle while
        /// the spot the user picked slides away from it: put the cube near an edge and the
        /// growth visibly happens somewhere else. The pivot cannot move without rebuilding the
        /// mesh, so the map is nudged back by however far the spot drifted, holding that one
        /// point still while everything else expands away from it.
        ///
        /// Only safe with the anchor stabiliser paused. It writes placementRoot's position
        /// every LateUpdate, and left running it fights this frame by frame - the map shakes,
        /// and at a thousand times the correction is large enough that it drags the map out
        /// from under an already-teleported viewer, who ends up beneath the ground looking at
        /// its back faces.
        /// </summary>
        void ApplyScaleAnchored(float multiplier, Vector3 anchorWorld)
        {
            ApplyScaleMultiplier(multiplier);
            Vector3 anchorNow = placementRoot.TransformPoint(entryPointLocal);
            placementRoot.position += anchorWorld - anchorNow;
        }

        /// <summary>
        /// Hands placementRoot's position over for the length of a transition, and hands it
        /// back afterwards. Resuming re-reads the current pose as the new anchor-relative
        /// offset, so the map is never yanked back to where it used to be.
        /// </summary>
        void SetAnchorStabilizerPaused(bool paused)
        {
            if (placementRoot == null)
                return;

            if (anchorStabilizer == null)
                anchorStabilizer = placementRoot.GetComponent<TrackAnchorStabilizer>();

            anchorStabilizer?.SetPaused(paused);
        }

        static float Evaluate(AnimationCurve curve, float t) =>
            curve != null && curve.length > 0 ? curve.Evaluate(t) : t;

        IEnumerator Occlude(float duration)
        {
            VRDroneTransitionOccluder blink = EnsureOccluder();
            if (blink == null)
            {
                // No blink means the jump would be visible, which is worse than a slow one.
                // Nothing to do but continue; the log says why it looked bad.
                Debug.LogWarning(
                    "[DroneTransition] No occluder available; the scale jump will be visible.",
                    this);
                yield break;
            }

            yield return blink.Cover(duration);
        }

        IEnumerator Reveal(float duration)
        {
            if (occluder == null)
                yield break;

            yield return occluder.Reveal(duration);
        }

        VRDroneTransitionOccluder EnsureOccluder()
        {
            if (occluder != null)
                return occluder;

            if (xrCamera == null)
                return null;

            occluder = GetComponent<VRDroneTransitionOccluder>();
            if (occluder == null)
                occluder = gameObject.AddComponent<VRDroneTransitionOccluder>();

            occluder.Configure(xrCamera);
            return occluder;
        }

        /// <summary>
        /// Puts the black sky up. Geometry only: the passthrough state is left exactly as
        /// <see cref="PerformEnterHiddenJump"/> set it, which is layer off and camera alpha 1.
        ///
        /// This used to move passthrough behind the shell here, on the theory that the shell
        /// covers every pixel with alpha 1 and so nothing could show through. The theory was
        /// wrong. The circuit's three hundred glTF materials write their own texture alpha
        /// straight into the framebuffer - the depth fix sets the alpha blend to One/Zero, so
        /// whatever the base map holds lands in the alpha channel - and any texel between the
        /// cutoff and one therefore let that fraction of the real room in. The result was the
        /// room's walls showing through the track and the whole circuit looking washed out,
        /// because the compositor was blending the two.
        ///
        /// Making a hole in the sky show the real room needs the alpha channel taken away from
        /// the track first. Until that exists, normal VR keeps the state that has always been
        /// correct: no underlay at all, so nothing in the alpha channel can matter.
        /// </summary>
        void RaiseSkyShell()
        {
            if (skyShell == null || !skyShell.isActiveAndEnabled || xrCamera == null)
                return;

            // The controller is handed the passthrough owner rather than reaching for it: the
            // compositing test has to bring the underlay up and put it back down again, and
            // nothing else in the drone scene is allowed to touch that state.
            skyShell.Prepare(xrCamera.transform, passthrough);

            if (!skyShell.IsVisible)
            {
                Debug.LogWarning(
                    "[VRDrone] The sky shell did not build; the exit keeps its blink.",
                    this);
            }
        }

        /// <summary>
        /// Hands the screen over to VR. With a transition controller present that is one call
        /// and this component never touches the layer or the camera; without one it falls back
        /// to the original direct writes so host scenes lacking the controller still work.
        /// </summary>
        void ApplyVrScreenEndpoint()
        {
            if (passthrough != null)
            {
                passthrough.ApplyVRImmediate();
                return;
            }

            passthroughLayer.SetActive(false);
            xrCamera.clearFlags = CameraClearFlags.Skybox;
            xrCamera.backgroundColor = new Color(0.015f, 0.02f, 0.04f, 1f);
        }

        void ApplyMrScreenEndpoint()
        {
            if (passthrough != null)
            {
                passthrough.ApplyMRImmediate();
                return;
            }

            xrCamera.clearFlags = savedClearFlags;
            xrCamera.backgroundColor = savedBackgroundColor;
            passthroughLayer.SetActive(savedPassthroughActive);
        }

        public void ExitVr()
        {
            if (!isVrActive || isTransitioning)
                return;

            Debug.Log($"[SkyExit][BHoldAccepted] time={Time.realtimeSinceStartup:F3}", this);

            isTransitioning = true;
            transitionRoutine = StartCoroutine(ExitVrRoutine());
        }

        IEnumerator ExitVrRoutine()
        {
            // Drone input first: the flight controller reads IsVrActive, and stopping it
            // before the transforms move keeps a frame of motion from being applied to an
            // origin that is on its way back to where it started.
            flightController?.ResetFlight();
            bool fractureDroveThisExit = false;

            Debug.Log(
                $"[DroneTransition][ExitStart] scale={(placementRoot != null ? placementRoot.localScale : Vector3.zero)} " +
                $"origin={xrOrigin.transform.position}",
                this);

            // The shell owns the exit when it is there. It closes over the whole view, the
            // transition commits behind it, and it then breaks apart onto the real room - so
            // nothing else in this method runs, including the blink.
            if (useSkyShellExit &&
                skyShell != null && skyShell.isActiveAndEnabled && skyShell.IsPrepared &&
                skyShell.BeginFracture(xrCamera != null ? xrCamera.transform : null))
            {
                yield return SkyShellExitRoutine();

                Debug.Log(
                    $"[DroneTransition][ExitComplete] via=skyShell " +
                    $"scale={(placementRoot != null ? placementRoot.localScale : Vector3.zero)} " +
                    $"origin={xrOrigin.transform.position}",
                    this);

                transitionRoutine = null;
                isTransitioning = false;
                yield break;
            }

            // ---- Phase 0: break the circuit, in the open ----------------------------------
            // The one part of leaving that is worth watching. The track the viewer has been
            // flying around comes apart where they are standing, and the real room shows
            // through the gaps. Only once enough of it has gone does the blink take over for
            // the scale jump, which is still the part that makes people ill.
            if (useTrackFracture && trackFracture != null && trackFracture.isActiveAndEnabled &&
                trackFracture.IsPrepared &&
                trackFracture.BeginFracture(xrCamera != null ? xrCamera.transform : null))
            {
                Debug.Log(
                    $"[DroneTransition][ExitFracture] fragments={trackFracture.FragmentCount} " +
                    $"revealMeshes={trackFracture.RevealCount}",
                    this);

                // Waits on area gone, not cells gone, so by the time the blink closes the real
                // room is already most of what the viewer is looking at.
                while (trackFracture.IsRunning &&
                    trackFracture.Coverage < hiddenCommitCoverage)
                {
                    yield return null;
                }

                trackFracture.LogCommitState();
                fractureDroveThisExit = true;
            }

            // ---- Phase A: close the blink ------------------------------------------------
            // Short when the break did the work, longer when it did not: with the circuit
            // already gone this only has to cover a transform commit, but a plain exit still
            // needs to hide the whole collapse from a thousand times down.
            yield return Occlude(fractureDroveThisExit
                ? commitOcclusionInDuration
                : occlusionInDuration);

            // ---- Phase B: the jump, unseen -----------------------------------------------
            PerformExitHiddenJump();

            // ---- Phase C: open the eyes, already home -------------------------------------
            yield return Reveal(fractureDroveThisExit
                ? commitOcclusionOutDuration
                : occlusionOutDuration);

            // Only the map moves from here. The viewer is standing in their real room at
            // their real pose, watching one virtual object settle back onto the table - no
            // self-motion, so nothing to be sick about.
            LogShrinkStartState();

            shrinkSampleIndex = 0;
            float elapsed = 0f;
            while (visibleScaleDurationExit > 0f && elapsed < visibleScaleDurationExit &&
                placementRoot != null)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / visibleScaleDurationExit);
                float wanted = Mathf.Pow(visibleScaleMultiplier, 1f - Evaluate(scaleCurve, t));
                ApplyScaleMultiplier(wanted);
                SampleShrinkFrame(t, wanted);
                yield return null;
            }

            ExitVrFinalize();

            Debug.Log(
                $"[DroneTransition][ExitComplete] scale={(placementRoot != null ? placementRoot.localScale : Vector3.zero)} " +
                $"origin={xrOrigin.transform.position}",
                this);

            transitionRoutine = null;
            isTransitioning = false;
        }

        /// <summary>
        /// The exit as the shell plays it.
        ///
        /// Nothing here is hidden by a blink. The closed shell is a real surface covering every
        /// pixel, so the commit happens behind geometry rather than behind a black quad, and
        /// the same surface then breaks apart to uncover the room. One thing does the work that
        /// previously took a curtain and a fracture that had to fight it.
        /// </summary>
        IEnumerator SkyShellExitRoutine()
        {
            // Hiding the map is the one step here that is invisible when it goes wrong. If
            // anything between the commit and the reveal throws, or the routine is stopped,
            // the map stays switched off and the user is left standing in an empty room with
            // no way to get it back - and no error that points at why. The finally runs on
            // disposal as well as on completion, so there is no path out of this method that
            // leaves the map hidden.
            try
            {
                float beganAt = Time.realtimeSinceStartup;
                Debug.Log($"[SkyExit][FractureBegin] time={beganAt:F3}", this);

                // Nothing at all is touched while the sky comes apart. The viewer stays exactly
                // where they were flying, at a thousand times scale, with the circuit intact
                // around them, and watches the black break open onto their own room. Every
                // previous attempt moved something here and every one of them read as the world
                // being switched rather than being uncovered.
                float waited = 0f;
                while (skyShell.RevealCoverage < skyRevealCommitThreshold &&
                    waited < skyFractureCommitTimeout)
                {
                    waited += Time.deltaTime;
                    yield return null;
                }

                if (skyShell.RevealCoverage < skyRevealCommitThreshold)
                {
                    Debug.LogWarning(
                        $"[SkyExit] Committing on the timeout at {waited:F2}s with only " +
                        $"{skyShell.RevealCoverage:P0} of the sky open. The commit will be " +
                        $"more visible than it should be, but being stuck in VR is worse.",
                        this);
                }

                CommitExitBehindShell();

                // Straight after the commit, not after the last piece has finished falling.
                // Those pieces are still tumbling out of frame behind a view that is already
                // entirely the real room, and waiting on them is what turned a one second pause
                // into nearly three.
                skyShell.Cleanup(restorePassthrough: false);

                float mrCompleteAt = Time.realtimeSinceStartup;
                Debug.Log(
                    $"[SkyExit][MRComplete] time={mrCompleteAt:F3} " +
                    $"elapsedSinceBegin={mrCompleteAt - beganAt:F2}s",
                    this);

                // The real room, alone, with nothing moving in it. Deliberately dead time: the
                // viewer has just come back from standing inside a circuit a thousand times
                // this size, and needs a moment to be somewhere before something is put in
                // front of them.
                float held = 0f;
                while (held < mapRevealDelay)
                {
                    held += Time.deltaTime;
                    yield return null;
                }

                Debug.Log(
                    $"[SkyExit][MapRevealStart] time={Time.realtimeSinceStartup:F3} " +
                    $"elapsedSinceMRComplete={Time.realtimeSinceStartup - mrCompleteAt:F2}s",
                    this);

                yield return RevealTabletopMap();

                Debug.Log(
                    $"[SkyExit][MapRevealComplete] time={Time.realtimeSinceStartup:F3}", this);

                ExitVrFinalize();
            }
            finally
            {
                SetMapVisible(true);
                RestoreHostUi();
            }
        }

        /// <summary>
        /// Everything that would be unbearable to watch, done while the shell is closed: the
        /// map back to tabletop size, the viewer back to the pose they left from, the drone
        /// world switched off, and the screen handed to passthrough.
        ///
        /// The map goes all the way back to its saved size here rather than being left
        /// oversized to shrink afterwards. Watching a circuit contract onto a table is the
        /// wrong last impression - it says the big thing was the real one and this is what is
        /// left of it. The map is hidden instead, and returns at the end by growing.
        /// </summary>
        void CommitExitBehindShell()
        {
            float coverage = skyShell != null ? skyShell.RevealCoverage : 0f;

            // Order matters and only in this direction.
            //
            // The circuit stops being drawn first. It is a thousand times its tabletop size and
            // the viewer is standing inside it, so restoring the origin while it is still
            // visible drags a kilometre of track across the whole field of view in one frame.
            // With it already gone, the same restore moves nothing anybody can see.
            SetMapVisible(false);
            droneHud?.Hide();
            environment.SetActive(false);

            if (visualRoot != null)
            {
                visualRoot.localPosition = savedVisualLocalPosition;
                visualRoot.localRotation = savedVisualLocalRotation;
                visualRoot.localScale = savedVisualLocalScale;
            }

            if (placementRoot != null)
            {
                placementRoot.localScale = savedPlacementLocalScale;
                placementRoot.localPosition = savedPlacementLocalPositionAtEnter;
            }

            audioDistanceScaler?.Restore();

            // The one and only origin move of the whole exit, and it happens here rather than
            // at the start precisely because by now there is almost nothing virtual left for it
            // to move relative to.
            xrOrigin.transform.SetPositionAndRotation(savedOriginPosition, savedOriginRotation);

            // Full MR endpoint before anything is torn down. The alpha seal and the masks are
            // removed straight after this, and if the screen were not already committed to
            // passthrough at that moment it would black out as they go.
            ApplyMrScreenEndpoint();

            isVrActive = false;

            Debug.Log(
                $"[SkyExit][Commit] time={Time.realtimeSinceStartup:F3} " +
                $"revealCoverage={coverage:P0} " +
                $"scale={(placementRoot != null ? placementRoot.localScale : Vector3.zero)} " +
                $"origin={xrOrigin.transform.position}",
                this);
        }

        /// <summary>
        /// Brings the map back the way the viewer should remember it: small, then growing into
        /// place on the table. Position and rotation never move - only scale - so it settles
        /// where it belongs instead of flying in.
        /// </summary>
        IEnumerator RevealTabletopMap()
        {
            RestoreHostUi();

            if (placementRoot == null)
                yield break;

            placementRoot.localPosition = savedPlacementLocalPositionAtEnter;
            ApplyScaleMultiplier(mapRevealStartScaleMultiplier);
            SetMapVisible(true);

            float elapsed = 0f;
            while (elapsed < mapRevealDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / mapRevealDuration);

                // A straight interpolation, not the exponential the entry uses. That one exists
                // because a thousandfold journey lerps to five hundred times by halfway; a
                // tenth to one is a factor of ten and has no such runaway to correct for.
                ApplyScaleMultiplier(Mathf.Lerp(
                    mapRevealStartScaleMultiplier, 1f, Evaluate(mapRevealCurve, t)));

                yield return null;
            }

            ApplyScaleMultiplier(1f);
        }

        void SetMapVisible(bool visible)
        {
            if (visualRoot == null)
                return;

            // Only ever the Visual child. If the lookup fell back to the placement root then
            // switching it off would take this coordinator's own references and the anchor
            // stabiliser down with it, and a map that pops into view is a far smaller problem
            // than a transition that cannot finish.
            if (visualRoot == placementRoot)
            {
                Debug.LogWarning(
                    "[DroneTransition] No 'Visual' child under the placement root, so the map " +
                    "cannot be hidden during the shell break; it will show through the first " +
                    "hole that opens.",
                    this);
                return;
            }

            if (visualRoot.gameObject.activeSelf == visible)
                return;

            visualRoot.gameObject.SetActive(visible);
            Debug.Log($"[DroneTransition] map visible={visible} ('{visualRoot.name}')", this);
        }

        /// <summary>
        /// Behind the blink: back to the size the viewer is about to watch shrink, and back
        /// to the real room at the exact pose they left it from. The map is deliberately left
        /// oversized - that last stretch is the only part worth showing.
        /// </summary>
        /// <summary>
        /// What the viewer is about to watch shrink. Two guesses at why that stretch looked
        /// chaotic were wrong, so this reports the actual state instead: anything still
        /// carrying a fracture name, and any target renderer still switched off, is a piece
        /// that should have been put back while the blink was closed.
        /// </summary>
        void LogShrinkStartState()
        {
            if (placementRoot == null)
                return;

            int fractureObjects = 0;
            int disabledRenderers = 0;
            int enabledRenderers = 0;

            foreach (Transform t in placementRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.Contains("_Cell") || t.name.StartsWith("Reveal_Cell"))
                    fractureObjects++;
            }

            foreach (MeshRenderer r in placementRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.enabled)
                    enabledRenderers++;
                else
                    disabledRenderers++;
            }

            Debug.Log(
                $"[DroneTransition][ShrinkStart] leftoverFractureObjects={fractureObjects} " +
                $"renderersEnabled={enabledRenderers} renderersDisabled={disabledRenderers} " +
                $"placementScale={placementRoot.localScale}",
                this);
        }

        int shrinkSampleIndex;

        /// <summary>
        /// Three snapshots across the shrink, comparing what this component asked for against
        /// what the transforms actually hold. Something makes the map come apart during this
        /// stretch and three guesses at the mechanism have all been wrong, so this measures
        /// rather than assumes: a scale that does not match the requested one means another
        /// component is writing it, and a child whose local pose moves means the child is
        /// being driven in world space while its parent shrinks underneath it.
        /// </summary>
        void SampleShrinkFrame(float t, float wantedMultiplier)
        {
            // Driven off the sample counter, not off t reaching zero. The loop advances
            // elapsed before computing t, so t is already past 0.01 on the very first frame
            // and a "t <= 0.001" test for the opening sample can never fire - which is why
            // this logged nothing at all for two runs.
            bool due = shrinkSampleIndex == 0 ||
                shrinkSampleIndex == 1 && t >= 0.5f ||
                shrinkSampleIndex == 2 && t >= 0.999f;

            if (!due)
                return;

            shrinkSampleIndex++;

            if (placementRoot == null)
                return;

            Vector3 wanted = Vector3.Scale(savedPlacementLocalScale, Vector3.one * wantedMultiplier);
            Vector3 actual = placementRoot.localScale;

            string visualInfo = "visual=none";
            if (visualRoot != null && visualRoot != placementRoot)
            {
                visualInfo = $"visualLocalPos={visualRoot.localPosition} " +
                    $"visualLocalScale={visualRoot.localScale}";
            }

            // First non-track child gives a read on whether replay-driven objects keep up.
            string childInfo = "child=none";
            foreach (Transform child in placementRoot.GetComponentsInChildren<Transform>(true))
            {
                if (child == placementRoot || child == visualRoot)
                    continue;

                childInfo = $"child='{child.name}' localPos={child.localPosition} " +
                    $"worldPos={child.position}";
                break;
            }

            Debug.Log(
                $"[DroneTransition][Shrink t={t:F2}] wantedScale={wanted} actualScale={actual} " +
                $"match={(actual - wanted).sqrMagnitude < 1e-6f} rootPos={placementRoot.position} " +
                $"{visualInfo} {childInfo}",
                this);
        }

        void PerformExitHiddenJump()
        {
            Vector3 originBeforeSnap = xrOrigin.transform.position;

            // Put the circuit back together here, behind the blink, not after the shrink.
            // The pieces are still lying where they fell and the originals are still switched
            // off, so leaving this any later means the viewer watches a shattered, debris
            // strewn map contract onto the table.
            trackFracture?.Cleanup();

            // The sky goes with it. Left up, an opaque near-black sphere would still be
            // standing around the viewer's head once they are back in their own room.
            skyShell?.Cleanup();

            if (visualRoot != null)
            {
                visualRoot.localPosition = savedVisualLocalPosition;
                visualRoot.localRotation = savedVisualLocalRotation;
                visualRoot.localScale = savedVisualLocalScale;
            }

            if (placementRoot != null)
            {
                // Scale only, about the map's own pivot, and position left exactly where the
                // anchor put it.
                //
                // Anchoring this on the chosen spot is what made the map appear to jump: the
                // correction is zero only at scale one, so at ten times the pivot has to sit
                // nine entry-offsets away from its authored pose. The blink opened on a map
                // that had visibly moved off the table and was clipping through the real
                // room. Centring the shrink on the drone's spot is not worth landing the map
                // somewhere it does not belong.
                ApplyScaleMultiplier(visibleScaleMultiplier);
                placementRoot.localPosition = savedPlacementLocalPositionAtEnter;
            }

            audioDistanceScaler?.Restore();

            xrOrigin.transform.SetPositionAndRotation(
                savedOriginPosition,
                savedOriginRotation);
            ApplyMrScreenEndpoint();

            droneHud?.Hide();
            environment.SetActive(false);
            RestoreHostUi();

            // Already in MR by every measure that matters, so the drone is no longer flying.
            isVrActive = false;

            // The origin is snapped back to where the viewer stood when they went in. If they
            // walked while flying, that snap moves the whole virtual world sideways relative
            // to the real room, and the blink is the only thing hiding it.
            Debug.Log(
                $"[DroneTransition][HiddenJump] originBefore={originBeforeSnap} " +
                $"originAfter={xrOrigin.transform.position} " +
                $"snapDistance={Vector3.Distance(originBeforeSnap, savedOriginPosition):F3}m " +
                $"cameraWorld={xrCamera.transform.position}",
                this);
        }

        void ExitVrFinalize()
        {
            if (placementRoot != null)
            {
                placementRoot.localPosition = savedPlacementLocalPositionAtEnter;
                placementRoot.localScale = savedPlacementLocalScale;
            }

            // Unconditionally, even though the reveal already did it. Hiding the map is the
            // one step of this transition that is invisible when it goes wrong: if anything
            // between the commit and the reveal throws, the coroutine dies with the map
            // switched off and the user is left in an empty room with no way to get it back.
            // Putting it back here as well costs a bool write and removes that failure mode.
            SetMapVisible(true);
            RestoreHostUi();

            // Originals switched back on, fragments and their meshes thrown away, so the next
            // trip in prepares from nothing and no state carries across.
            trackFracture?.Cleanup();

            // Position belongs to the stabiliser again from here.
            SetAnchorStabilizerPaused(false);

            // Held back until the map is its own size again: the cube is parented to the map,
            // so bringing it back any earlier shows it ten times too big.
            if (hiddenCube != null)
                hiddenCube.gameObject.SetActive(true);

            RestoreTrackInteraction();
            isVrActive = false;
        }

        public void ApplyDroneMotion(Vector3 movement, float yaw)
        {
            if (!isVrActive || isTransitioning || xrOrigin == null || xrCamera == null)
                return;

            if (Mathf.Abs(yaw) > Mathf.Epsilon)
            {
                xrOrigin.transform.RotateAround(
                    xrCamera.transform.position,
                    Vector3.up,
                    yaw);
            }

            xrOrigin.transform.position += movement;
        }

        public void SetExitHoldProgress(float normalizedProgress)
        {
            droneHud?.SetExitHoldProgress(normalizedProgress);
        }

        public void SetDroneSpeed(float speedKph)
        {
            droneHud?.SetSpeedKph(speedKph);
        }

        void SaveMrState()
        {
            savedOriginPosition = xrOrigin.transform.position;
            savedOriginRotation = xrOrigin.transform.rotation;
            savedPlacementLocalScale = placementRoot.localScale;
            savedPlacementLocalPositionAtEnter = placementRoot.localPosition;
            savedVisualLocalPosition = visualRoot.localPosition;
            savedVisualLocalRotation = visualRoot.localRotation;
            savedVisualLocalScale = visualRoot.localScale;
            savedClearFlags = xrCamera.clearFlags;
            savedBackgroundColor = xrCamera.backgroundColor;
            savedPassthroughActive = passthroughLayer.activeSelf;
            savedTrackEditMode = trackPlacer.IsEditMode;
        }

        float GetTrackBaseLocalY()
        {
            return GetTrackLocalY(highest: false);
        }

        float GetTrackLocalY(bool highest)
        {
            Transform cars = visualRoot.Find("Cars");
            bool hasRenderer = false;
            float result = 0f;

            foreach (Renderer renderer in
                visualRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null ||
                    cars != null && renderer.transform.IsChildOf(cars))
                {
                    continue;
                }

                Bounds bounds = renderer.bounds;
                for (int x = -1; x <= 1; x += 2)
                {
                    for (int y = -1; y <= 1; y += 2)
                    {
                        for (int z = -1; z <= 1; z += 2)
                        {
                            Vector3 worldPoint = bounds.center +
                                Vector3.Scale(bounds.extents,
                                    new Vector3(x, y, z));
                            float localY = placementRoot
                                .InverseTransformPoint(worldPoint).y;
                            bool isMoreExtreme = highest
                                ? localY > result
                                : localY < result;
                            if (!hasRenderer || isMoreExtreme)
                            {
                                result = localY;
                            }
                            hasRenderer = true;
                        }
                    }
                }
            }

            return hasRenderer ? result : 0f;
        }

        /// <summary>
        /// Switches off the replay's own panels for the length of the flight.
        ///
        /// They are authored for a tabletop map at arm's length. Once the circuit is a
        /// thousand times bigger and the viewer is inside it, those panels are still pinned
        /// where they were - which puts a leaderboard the size of a building through the
        /// middle of the track.
        ///
        /// Only the host scene is touched, so the drone's own HUD - which lives in the drone
        /// scene - is left alone without needing to be named here. Canvas.enabled rather than
        /// SetActive: the panels keep ticking and keep their state, they just stop drawing.
        /// </summary>
        void HideHostUi()
        {
            RestoreHostUi();

            if (!hasHostScene || !hostScene.isLoaded)
                return;

            foreach (GameObject root in hostScene.GetRootGameObjects())
            {
                foreach (Canvas canvas in root.GetComponentsInChildren<Canvas>(true))
                {
                    if (canvas == null || !canvas.enabled)
                        continue;

                    canvas.enabled = false;
                    hiddenCanvases.Add(canvas);
                }
            }

            Debug.Log($"[VRDrone] hid {hiddenCanvases.Count} host UI canvases for the flight.", this);
        }

        void RestoreHostUi()
        {
            foreach (Canvas canvas in hiddenCanvases)
            {
                if (canvas != null)
                    canvas.enabled = true;
            }

            hiddenCanvases.Clear();
        }

        void LockTrackInteraction()
        {
            if (savedTrackEditMode)
                trackPlacer.ToggleEditMode();

            disabledInteractables.Clear();
            foreach (XRBaseInteractable interactable in
                placementRoot.GetComponentsInChildren<XRBaseInteractable>(true))
            {
                if (interactable == null || !interactable.enabled)
                    continue;

                interactable.enabled = false;
                disabledInteractables.Add(interactable);
            }
        }

        void RestoreTrackInteraction()
        {
            foreach (XRBaseInteractable interactable in disabledInteractables)
            {
                if (interactable != null)
                    interactable.enabled = true;
            }
            disabledInteractables.Clear();

            if (savedTrackEditMode && !trackPlacer.IsEditMode)
                trackPlacer.ToggleEditMode();
        }

        void EnsureEnvironment()
        {
            if (ground == null)
            {
                ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = "VR Drone Ground";
                ground.transform.SetParent(environment.transform, false);
                ground.transform.localScale = Vector3.one * 1000f;
                Renderer renderer = ground.GetComponent<Renderer>();
                if (renderer != null)
                    renderer.material.color = new Color(0.025f, 0.035f, 0.06f, 1f);
            }

        }

        void PlaceGround(Vector3 up)
        {
            float localY = GetTrackBaseLocalY() - 1f;
            ground.transform.position = placementRoot.TransformPoint(
                new Vector3(0f, localY, 0f));
            ground.transform.rotation = Quaternion.FromToRotation(
                Vector3.up,
                up);
        }

        static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T found = root.GetComponentInChildren<T>(true);
                if (found != null)
                    return found;
            }

            return null;
        }

        static GameObject FindInScene(Scene scene, string objectName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform found = FindChild(root.transform, objectName);
                if (found != null)
                    return found.gameObject;
            }

            return null;
        }

        static GameObject FindRoot(Scene scene, string objectName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == objectName)
                    return root;
            }

            return null;
        }

        static Transform FindChild(Transform root, string objectName)
        {
            if (root.name == objectName)
                return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindChild(root.GetChild(i), objectName);
                if (found != null)
                    return found;
            }

            return null;
        }

    }
}
