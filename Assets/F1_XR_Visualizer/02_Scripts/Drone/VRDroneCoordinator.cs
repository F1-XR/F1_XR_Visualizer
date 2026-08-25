using System;
using System.Collections;
using System.Collections.Generic;
using F1XR.Experience;
using F1XR.RestAPI.Replay;
using F1XR.RestAPI.Replay.Track.Build;
using F1XR.RestAPI.Replay.Track.Placement;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace F1XR.Drone
{
    [DisallowMultipleComponent]
    public sealed class VRDroneCoordinator : MonoBehaviour
    {
        const string EnvironmentName = "VRDroneEnvironment";
        const string GroundTextureResourcePath = "Drone/SuzukaAerial";

        [SerializeField, Min(1f)] float vrScaleMultiplier = 1000f;

        [Header("Drone Camera")]
        [Tooltip("Far clip while flying. The aerial ground is 11.3 km across, so the rig's " +
            "default of a kilometre would cut most of it off mid-air.")]
        [SerializeField, Min(1f)] float droneFarClipPlane = 5000f;

        [Header("Aerial Ground")]
        [Tooltip("Textures the drone ground with the aerial photo, placed in the " +
            "track's own metric space so it rides the placement scale. Off falls back " +
            "to the plain dark slab, so the previous look is one checkbox away.")]
        [SerializeField] bool useAerialGround = true;

        [Tooltip("Photo centre in the circuit's own metres, read off the Blender scene. " +
            "glTFast negates X on import (NodeExtension.GetTransform), so Blender " +
            "(x, y, z) arrives as Unity (-x, z, y).")]
        [SerializeField] Vector3 aerialLocalPosition = new Vector3(-64.9576f, 26.56f, 40.0006f);

        [Tooltip("Yaw of the photo in the same space. Blender's -39 about +Z arrives as " +
            "+39 about Unity's +Y (the same X-flip negates the quaternion's y).")]
        [SerializeField] float aerialLocalYaw = 39f;

        [Tooltip("Ground footprint of the photo in metres. 11304.42 x 7118.60 is 11.3 km " +
            "of Suzuka countryside with the circuit punched out of the middle.")]
        [SerializeField] Vector2 aerialLocalSize = new Vector2(11304.42f, 7118.60f);

        [Tooltip("The placeholder plate and walls that stand in for the world under the " +
            "tabletop map. They stay for MR, but in VR the aerial photo takes over that " +
            "job, so their renderers are hidden for the trip and restored on the way " +
            "out. Renderers only - colliders and grab targets keep working. Matched by " +
            "name because the glTF import nests them behind generated parents.")]
        [SerializeField] string[] plateRendererNames =
        {
            "suzuka_rect_fill",
            "suzuka_rect_walls",
            "Cube",
        };

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

        [Header("Room Shell")]
        [Tooltip("Enters VR by breaking the real room instead of blinking. Falls back to the " +
            "blink on its own whenever the room was never scanned, so leaving this on cannot " +
            "stop anyone getting into the drone.")]
        [SerializeField] bool useRoomShellEnter = true;

        [Tooltip("Exits VR by reversing the room fracture — same fragments fly back to their " +
            "intact positions, reality reappears through each piece. Requires a successful " +
            "room shell enter to have dormant fragments. Falls back to legacy blink exit.")]
        [SerializeField] bool useRoomShellExit = true;

        [Header("Room reverse — Phase A (time-based rapid collapse)")]
        [Tooltip("Seconds for Phase A. Map goes from immersive to earlyShrinkTargetMultiplier " +
            "on a fixed timer, independent of reassembly coverage.")]
        [SerializeField, Min(0.1f)] float earlyShrinkDuration = 0.5f;

        [Tooltip("Map multiplier at the end of Phase A. 1000 → this value.")]
        [SerializeField, Min(1f)] float earlyShrinkTargetMultiplier = 5f;

        [Header("Room reverse — Phase B (coverage-based tabletop settle)")]
        [Tooltip("Coverage at which Phase B ends and the map reaches 1×.")]
        [SerializeField, Range(0.3f, 1f)] float tabletopShrinkEndCoverage = 0.85f;

        [Tooltip("Seconds the break gets entirely to itself before the circuit is allowed to " +
            "move at all. Coverage alone is not enough of a gate: the first pieces come away " +
            "fast, so a coverage threshold can be met before the viewer has registered that " +
            "anything is happening, and then everything starts at once.")]
        [SerializeField, Min(0f)] float minFractureLeadTime = 0.65f;

        [Tooltip("How much of the room has to be gone before the circuit starts growing. Both " +
            "this and the lead time above must be satisfied - time makes the break readable, " +
            "coverage makes sure there is actually a hole for the circuit to grow through.")]
        [SerializeField, Range(0f, 1f)] float mapGrowStartBreakCoverage = 1f;

        [Tooltip("Seconds for the circuit to go from tabletop to immersive scale. Long enough " +
            "that the growth is still going while the last of the room falls, so the second " +
            "half of the transition belongs to the circuit rather than to the break.")]
        [SerializeField, Min(0.1f)] float mapGrowDuration = 1.6f;

        [Tooltip("Map scale at which the drone's ground plane is placed and switched on. The " +
            "ground is ten kilometres across and sits about half a metre under the real floor, " +
            "so bringing it up early puts it in a depth fight with the room across the whole " +
            "view. By this scale it is nowhere near anything real.")]
        [SerializeField, Range(10f, 300f)] float groundEnableScaleMultiplier = 75f;

        [Tooltip("Seconds after which the entry finishes regardless of how much room is left. " +
            "Nobody may be stranded halfway into VR by a break that stalled.")]
        [SerializeField, Min(0.5f)] float roomBreakTimeout = 6f;

        [Tooltip("How much of the room has to be gone before VR is committed to.")]
        [SerializeField, Range(0.5f, 1f)] float roomBreakCommitCoverage = 0.95f;

        [Tooltip("How much of the sky has to be open before the circuit starts shrinking. " +
            "Deliberately not near one: the point is that the shell is still visibly coming " +
            "apart while the world inside it contracts, so the two read as one event. Wait " +
            "until the break is over and it becomes 'VR ended, then a map appeared'.")]
        [SerializeField, Range(0.5f, 1f)] float mapShrinkStartRevealCoverage = 0.75f;

        [Tooltip("Seconds for the circuit to go from immersive scale to tabletop.")]
        [SerializeField, Min(0.1f)] float mapShrinkDuration = 1f;

        [Tooltip("Seconds after the break starts, after which the shrink begins whatever the " +
            "coverage says. A bug that stops the coverage rising must not be able to leave " +
            "someone stuck inside a circuit with no way out.")]
        [SerializeField, Min(0.5f)] float skyFractureCommitTimeout = 4f;

        [Header("Debug")]
        [SerializeField, FormerlySerializedAs("showGrabVolumeVisual")]
        bool showGrabRange;

        [Tooltip("Skips manual track and drone-cube placement. The map is created at the " +
            "TrackRevealPlacer fixed pose, then Drone mode starts automatically.")]
        [SerializeField] bool debugSkipPlacementAndEnterDrone;

        [Tooltip("Drone entry point in the track's local space when debug auto-entry is on. " +
            "(0, 0, 0) uses the map origin.")]
        [SerializeField] Vector3 debugDroneEntryLocalPoint;

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
        Material groundMaterial;
        readonly List<Renderer> plateRenderers = new();
        bool plateRenderersScanned;
        bool warnedMissingPlates;
        VRDroneHud droneHud;
        DroneVehicleTargeting vehicleTargeting;
        DroneVehicleWorldTargetPresenter worldTargetPresenter;
        ReplayPlayer replayPlayer;
        readonly List<XRBaseInteractable> disabledInteractables = new();
        readonly List<ARPlaneMeshVisualizer> hiddenPlaneVisualizers = new();
        readonly List<ARPlaneManager> subscribedPlaneManagers = new();
        readonly List<AutomaticTableCandidatePreview>
            hiddenCandidatePreviews = new();

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
        float savedFarClipPlane;
        bool savedPassthroughActive;
        bool savedTrackEditMode;
        bool appliedShowGrabVolumeVisual;
        bool isVrActive;

        // Enter and Exit are still synchronous, so this is only ever true inside one of them.
        // It exists now because the scale-up and the camera move are about to become timed
        // sequences, and a second CubeReleased arriving halfway through would otherwise save
        // the transition's own state as the MR state to return to.
        bool isTransitioning;
        bool debugEntryRequested;
        bool initializationComplete;
        Coroutine transitionRoutine;
        VRDroneTransitionOccluder occluder;
        TrackAnchorStabilizer anchorStabilizer;
        TrackFracture.VRTrackFractureController trackFracture;
        SkyShell.VRSkyShellFractureController skyShell;
        Experience.Fracture.RoomShellFractureController roomShell;

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

            RestorePlaneVisualizers();
        }

        IEnumerator Initialize()
        {
            while (!TryResolveReferences())
                yield return null;

            if (!debugSkipPlacementAndEnterDrone)
            {
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
            }

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

            SetDroneWorldActive(false);
            EnsureEnvironment();
            droneHud = GetComponent<VRDroneHud>();
            if (droneHud == null)
                droneHud = gameObject.AddComponent<VRDroneHud>();
            droneHud.Configure(environment.transform);

            replayPlayer = FindInScene<ReplayPlayer>(hostScene);
            worldTargetPresenter =
                GetComponent<DroneVehicleWorldTargetPresenter>();
            if (worldTargetPresenter == null)
            {
                worldTargetPresenter =
                    gameObject.AddComponent<DroneVehicleWorldTargetPresenter>();
            }

            worldTargetPresenter.Configure(xrCamera, droneHud.NumberFont);
            vehicleTargeting = GetComponent<DroneVehicleTargeting>();
            if (vehicleTargeting == null)
                vehicleTargeting = gameObject.AddComponent<DroneVehicleTargeting>();
            vehicleTargeting.Configure(
                replayPlayer,
                worldTargetPresenter,
                xrCamera);

            if (debugSkipPlacementAndEnterDrone)
                StartCoroutine(EnterDroneDebugWhenReady());

            initializationComplete = true;
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

            // Optional. Without it the entry is the blink it has always been.
            roomShell ??= FindInScene<Experience.Fracture.RoomShellFractureController>(hostScene);

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
            if (placement == null)
                return;

            BeginEnterVr(
                placement.InverseTransformPoint(cubeTransform.position),
                cubeTransform);
        }

        IEnumerator EnterDroneDebugWhenReady()
        {
            trackPlacer.SetPlacementMode(TrackPlacementMode.Fixed);

            while (!trackPlacer.HasPlacement)
            {
                trackPlacer.TryPlaceFixed();
                yield return null;
            }

            BeginEnterVr(debugDroneEntryLocalPoint, null);
        }

        public void BeginDebugEntryWithoutPlacement()
        {
            if (debugEntryRequested || isVrActive || isTransitioning)
                return;

            debugEntryRequested = true;
            StartCoroutine(EnterDroneDebugAfterReady());
        }

        IEnumerator EnterDroneDebugAfterReady()
        {
            while (!TryResolveReferences())
                yield return null;

            yield return EnterDroneDebugWhenReady();
            debugEntryRequested = false;
        }

        public void BeginDebugEntryFromExistingPlacement()
        {
            if (debugEntryRequested || isVrActive || isTransitioning)
                return;

            debugEntryRequested = true;
            StartCoroutine(EnterDroneFromExistingPlacementAfterReady());
        }

        IEnumerator EnterDroneFromExistingPlacementAfterReady()
        {
            while (!TryResolveReferences() || !initializationComplete)
                yield return null;

            if (!trackPlacer.HasPlacement)
            {
                Debug.LogError(
                    "[VRDrone] Debug VR entry needs an existing track placement.",
                    this);
                debugEntryRequested = false;
                yield break;
            }

            BeginEnterVr(debugDroneEntryLocalPoint, null, true);
            debugEntryRequested = false;
        }

        void BeginEnterVr(
            Vector3 entryLocal,
            Transform sourceCube,
            bool skipRoomShellForEntry = false)
        {
            if (isVrActive || isTransitioning || trackPlacer == null ||
                !trackPlacer.HasPlacement)
            {
                return;
            }

            Transform placement = trackPlacer.PlacementTransform;
            placementRoot = placement;
            visualRoot = placementRoot != null
                ? placementRoot.Find("Visual") ?? placementRoot
                : null;
            plateRenderersScanned = false;
            if (visualRoot == null)
                return;

            entryPointLocal = entryLocal;
            isTransitioning = true;
            transitionRoutine = StartCoroutine(
                EnterVrRoutine(sourceCube, skipRoomShellForEntry));
        }

        IEnumerator EnterVrRoutine(
            Transform cubeTransform,
            bool skipRoomShellForEntry)
        {
            SaveMrState();
            SuspendPlaneVisualizers();
            LockTrackInteraction();

            hiddenCube = cubeTransform;
            if (hiddenCube != null)
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

            // The room is the way in whenever there is a room to break. Everything below this
            // - the visible tenfold growth, the blink, the scale jump, the origin teleport -
            // exists only because there was previously nothing to uncover VR through.
            if (!skipRoomShellForEntry && useRoomShellEnter &&
                roomShell != null && roomShell.isActiveAndEnabled &&
                roomShell.HasBreakableSurfaces)
            {
                yield return RoomShellEnterRoutine();
                transitionRoutine = null;
                isTransitioning = false;
                yield break;
            }

            if (!skipRoomShellForEntry && useRoomShellEnter)
            {
                Debug.LogWarning(
                    "[MR2VR] No breakable room surfaces, so the entry falls back to the blink. " +
                    "Complete Space Setup on the device for the room to be the way in.",
                    this);
            }

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
        /// Enters VR by taking the real room apart, with the circuit growing through the gaps.
        ///
        /// Nothing is switched from one world to the other. The room is drawn as passthrough
        /// masks that occlude the VR behind them, so while a mask stands the viewer sees their
        /// own room and while it is gone they see the drone world - and the same tabletop map
        /// they were looking at grows from a model on the table into the circuit around them
        /// without its renderers ever being touched.
        ///
        /// No blink, no scale jump, and no origin move. The last of those is the important one:
        /// at a thousand times scale the entry point is within a metre of where the head
        /// already is, so teleporting the viewer to it buys nothing and costs the one thing the
        /// transition cannot afford, which is the world lurching sideways.
        /// </summary>
        IEnumerator RoomShellEnterRoutine()
        {
            float beganAt = Time.realtimeSinceStartup;
            Debug.Log(
                $"[MR2VR][EnterBegin] time={beganAt:F3} anchor={entryWorldAnchor} " +
                $"entryLocal={entryPointLocal}",
                this);

            // The sky first, behind the room rather than in front of everything. Its usual
            // background queue would paint the whole view opaque before the masks ran, and the
            // masks have no way to undo that - they write depth, not alpha.
            if (skyShell != null && skyShell.isActiveAndEnabled)
            {
                skyShell.SetTransitionDepthMode(true);
                skyShell.Prepare(xrCamera.transform, passthrough);
            }

            // The drone world comes up now, while the room still covers it completely, so that
            // the first hole has something waiting behind it instead of opening onto a frame of
            // nothing. The ground is held back - it is ten kilometres wide and would be fighting
            // the real floor for the depth buffer across the entire room.
            SetDroneWorldActive(true);
            if (ground != null)
                ground.SetActive(false);

            HideDroneHud();

            // One frame with everything up and nothing yet broken. Shaders compile and the
            // renderers register here rather than in the middle of the break.
            yield return null;

            Coroutine breaking = roomShell.StartCoroutine(roomShell.PlayBreakSequence());

            // The break gets the opening to itself. Both gates have to open: the lead time so
            // that "the room is coming apart" has time to land as its own event, and the
            // coverage so that there is really a hole for the circuit to grow through rather
            // than just a crack. Coverage alone let the growth start inside half a second,
            // which put both effects on screen at once and left neither of them legible.
            float waited = 0f;
            while (waited < roomBreakTimeout &&
                (waited < minFractureLeadTime ||
                    roomShell.BreakCoverage < mapGrowStartBreakCoverage))
            {
                waited += Time.deltaTime;
                yield return null;
            }

            Debug.Log(
                $"[MR2VR][GrowStart] time={Time.realtimeSinceStartup:F3} " +
                $"fractureElapsed={waited:F2}s (lead {minFractureLeadTime:F2}s) " +
                $"breakCoverage={roomShell.BreakCoverage:P0} (gate {mapGrowStartBreakCoverage:P0}) " +
                $"fragments={roomShell.TotalFragments}",
                this);

            yield return MapGrowRoutine();

            // The rest of the room, on its own clock. The circuit is already at full size by
            // now, so nothing anybody is looking at is waiting on this.
            float settle = 0f;
            while (roomShell.BreakCoverage < roomBreakCommitCoverage && settle < roomBreakTimeout)
            {
                settle += Time.deltaTime;
                yield return null;
            }

            if (breaking != null)
                roomShell.StopCoroutine(breaking);

            // VR endpoint only now, with the room essentially gone. Doing it earlier would
            // switch the underlay off and take the remaining real surfaces away in one frame,
            // which is the whole failure this replaces.
            ApplyVrScreenEndpoint();
            // Fragments stay dormant for reverse playback on exit.
            skyShell?.SetTransitionDepthMode(false);

            audioDistanceScaler?.Apply(vrScaleMultiplier);
            HideHostUi();

            isVrActive = true;
            ShowDroneHud();
            flightController?.ResetFlight();

            Debug.Log(
                $"[MR2VR][EnterComplete] time={Time.realtimeSinceStartup:F3} " +
                $"total={Time.realtimeSinceStartup - beganAt:F2}s " +
                $"scale={placementRoot.localScale} " +
                $"breakCoverage={roomShell.BreakCoverage:P0} " +
                $"originMoved=0m camera={xrCamera.transform.position}",
                this);
        }

        /// <summary>
        /// The tabletop circuit growing into the one the viewer ends up standing in, anchored on
        /// the spot they put the cube down.
        /// </summary>
        IEnumerator MapGrowRoutine()
        {
            bool groundUp = false;
            float elapsed = 0f;

            while (elapsed < mapGrowDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / mapGrowDuration);

                // Exponential. One to a thousand interpolated straight is already at five
                // hundred times by halfway, so almost the whole growth would happen in the
                // first instant and the rest would look like nothing moving.
                float multiplier = Mathf.Pow(vrScaleMultiplier, Evaluate(scaleCurve, t));
                ApplyScaleAnchored(multiplier, entryWorldAnchor);

                // Placed first, shown second. The ground's height is read off the circuit's
                // bounds, and switching it on before that is computed puts it at last frame's
                // position for one frame - a ten kilometre plane in the wrong place.
                if (!groundUp && multiplier >= groundEnableScaleMultiplier && ground != null)
                {
                    groundUp = true;
                    PlaceGround(placementRoot.up);
                    ground.SetActive(true);

                    Debug.Log(
                        $"[MR2VR][GroundUp] multiplier={multiplier:F0} " +
                        $"groundY={ground.transform.position.y:F3}",
                        this);
                }

                yield return null;
            }

            ApplyScaleAnchored(vrScaleMultiplier, entryWorldAnchor);

            if (ground != null)
            {
                // Once more at the final scale: the bounds this is derived from have grown by
                // the whole thousandfold since it was first placed.
                PlaceGround(placementRoot.up);
                ground.SetActive(true);
            }

            Debug.Log(
                $"[MR2VR][GrowComplete] scale={placementRoot.localScale} " +
                $"placementPos={placementRoot.position}",
                this);
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
            SetDroneWorldActive(true);
            ApplyVrScreenEndpoint();
            PlaceGround(placementRoot.up);
            HideHostUi();

            isVrActive = true;
            ShowDroneHud();
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
            xrCamera.farClipPlane = droneFarClipPlane;

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
            xrCamera.farClipPlane = savedFarClipPlane;

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

            // RoomShell reverse: same fragments fly back, reality reappears.
            if (useRoomShellExit && roomShell != null && roomShell.HasDormantFragments)
            {
                yield return RoomShellExitRoutine();

                Debug.Log(
                    $"[DroneTransition][ExitComplete] via=roomShellReverse " +
                    $"scale={(placementRoot != null ? placementRoot.localScale : Vector3.zero)} " +
                    $"origin={xrOrigin.transform.position}",
                    this);

                transitionRoutine = null;
                isTransitioning = false;
                yield break;
            }

            // Legacy sky shell exit — kept as fallback.
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
        /// VR→MR via room shell reverse. The forward fracture's fragments fly back to their
        /// intact positions while the map shrinks from immersive to tabletop. Same geometry,
        /// same paths, reversed time. Reality appears through each returning piece.
        /// </summary>
        IEnumerator RoomShellExitRoutine()
        {
            float beganAt = Time.realtimeSinceStartup;

            HideDroneHud();
            SetAnchorStabilizerPaused(true);

            // SkyShell to transition depth mode so mask fragments (queue 1999) can
            // depth-block the sky (queue 2050). Without this the sky at Background queue
            // paints alpha 1 over the whole view before the masks run.
            skyShell?.SetTransitionDepthMode(true);

            // Passthrough compositing: layer ON, camera alpha 0. The VR environment +
            // SkyShell still cover every pixel so the viewer sees no change yet. As mask
            // fragments return they depth-block VR at their position, and the camera's
            // alpha-0 background lets passthrough show through those patches.
            if (passthrough != null)
                passthrough.ApplyVRBehindOpaqueCover();

            Debug.Log(
                $"[RoomReverse][Begin] time={beganAt:F3} " +
                $"dormantFragments={roomShell.TotalFragments} " +
                $"passthroughActive={(passthrough != null)} " +
                $"cameraAlpha={(xrCamera != null ? xrCamera.backgroundColor.a : -1f):F2} " +
                $"earlyDuration={earlyShrinkDuration:F2}s earlyTarget={earlyShrinkTargetMultiplier:F0}× " +
                $"tabletopEnd={tabletopShrinkEndCoverage:F2} vrScale={vrScaleMultiplier:F0}",
                this);

            // Start reassembly on its own coroutine. The map shrink is driven by
            // reassembly coverage below, not by a separate coroutine.
            Coroutine reassembling = roomShell.StartCoroutine(roomShell.PlayReverseSequence());

            // Shrink anchor: same math as MapShrinkRoutine, accounting for origin drift.
            Matrix4x4 originAtEnter = Matrix4x4.TRS(
                savedOriginPosition, savedOriginRotation, Vector3.one);
            Matrix4x4 originNow = Matrix4x4.TRS(
                xrOrigin.transform.position, xrOrigin.transform.rotation, Vector3.one);
            Vector3 shrinkAnchor =
                (originNow * originAtEnter.inverse).MultiplyPoint3x4(entryWorldAnchor);

            bool groundOff = false;

            // ── Phase A: time-based rapid collapse ──
            // Shrinks the map on a fixed timer, independent of how many fragments
            // have returned. By the time MR becomes prominent the track is already
            // small enough that it doesn't plaster over the real room.
            float phaseAElapsed = 0f;
            float logStartA = Mathf.Log(vrScaleMultiplier);
            float logEndA = Mathf.Log(earlyShrinkTargetMultiplier);

            Debug.Log(
                $"[RoomReverse][PhaseA-Start] duration={earlyShrinkDuration:F2}s " +
                $"target={earlyShrinkTargetMultiplier:F0}×", this);

            while (phaseAElapsed < earlyShrinkDuration &&
                roomShell.State == Experience.Fracture.RoomShellState.ReassemblingToMR)
            {
                phaseAElapsed += Time.deltaTime;
                float t = Mathf.Clamp01(phaseAElapsed / earlyShrinkDuration);
                float multiplier = Mathf.Exp(Mathf.Lerp(logStartA, logEndA, t));
                ApplyScaleAnchored(multiplier, shrinkAnchor);

                if (!groundOff && multiplier < groundEnableScaleMultiplier && ground != null)
                {
                    ground.SetActive(false);
                    groundOff = true;
                }

                yield return null;
            }

            // Snap to Phase A endpoint.
            ApplyScaleAnchored(earlyShrinkTargetMultiplier, shrinkAnchor);

            Debug.Log(
                $"[RoomReverse][PhaseA-Done] elapsed={phaseAElapsed:F2}s " +
                $"coverage={roomShell.ReassemblyCoverage:P0} " +
                $"multiplier={earlyShrinkTargetMultiplier:F0}×", this);

            // ── Phase B: coverage-based tabletop settle ──
            // From earlyShrinkTargetMultiplier → 1×, driven by reassembly coverage.
            bool loggedPhaseB = false;
            bool loggedShrinkComplete = false;
            float phaseBStartCoverage = roomShell.ReassemblyCoverage;
            float logStartB = Mathf.Log(earlyShrinkTargetMultiplier);

            float settle = 0f;
            while (roomShell.State == Experience.Fracture.RoomShellState.ReassemblingToMR &&
                settle < roomBreakTimeout)
            {
                settle += Time.deltaTime;
                float coverage = roomShell.ReassemblyCoverage;

                float t = Mathf.InverseLerp(
                    phaseBStartCoverage, tabletopShrinkEndCoverage, coverage);
                t = Mathf.Clamp01(t);
                float multiplier = Mathf.Exp(Mathf.Lerp(logStartB, 0f, t));
                ApplyScaleAnchored(multiplier, shrinkAnchor);

                if (!loggedPhaseB && t > 0f)
                {
                    loggedPhaseB = true;
                    Debug.Log(
                        $"[RoomReverse][PhaseB] coverage={coverage:P0} " +
                        $"multiplier={multiplier:F1}", this);
                }

                if (!groundOff && multiplier < groundEnableScaleMultiplier && ground != null)
                {
                    ground.SetActive(false);
                    groundOff = true;
                }

                if (!loggedShrinkComplete && t >= 1f)
                {
                    loggedShrinkComplete = true;
                    Debug.Log(
                        $"[RoomReverse][MapShrinkComplete] coverage={coverage:P0}", this);
                }

                yield return null;
            }

            // Final snap to tabletop scale.
            ApplyScaleAnchored(1f, shrinkAnchor);
            if (ground != null) ground.SetActive(false);

            if (reassembling != null)
                roomShell.StopCoroutine(reassembling);

            SetDroneWorldActive(false);

            Debug.Log(
                $"[RoomReverse][PreFinalize] " +
                $"placementRoot={(placementRoot != null ? placementRoot.position.ToString("F3") : "null")} " +
                $"placementScale={(placementRoot != null ? placementRoot.localScale.ToString("F3") : "null")} " +
                $"visualRoot={(visualRoot != null ? visualRoot.gameObject.activeSelf : false)} " +
                $"visualScale={(visualRoot != null ? visualRoot.localScale.ToString("F3") : "null")} " +
                $"cameraAlpha={(xrCamera != null ? xrCamera.backgroundColor.a : -1f):F2}", this);

            RestoreExitBookkeeping();
            ApplyMrScreenEndpoint();

            skyShell?.SetTransitionDepthMode(false);
            skyShell?.Cleanup(restorePassthrough: false);

            roomShell.ClearShellFragments();
            ExitVrFinalize();

            Debug.Log(
                $"[RoomReverse][Complete] total={Time.realtimeSinceStartup - beganAt:F2}s " +
                $"origin={xrOrigin.transform.position} " +
                $"placementPos={(placementRoot != null ? placementRoot.position.ToString("F3") : "null")} " +
                $"placementScale={(placementRoot != null ? placementRoot.localScale.ToString("F3") : "null")} " +
                $"visualActive={(visualRoot != null ? visualRoot.gameObject.activeSelf : false)} " +
                $"visualActiveInHierarchy={(visualRoot != null ? visualRoot.gameObject.activeInHierarchy : false)} " +
                $"cameraAlpha={(xrCamera != null ? xrCamera.backgroundColor.a : -1f):F2}", this);
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
                // where they were flying, at a thousand times scale, with the circuit around
                // them, and watches the black break open onto their own room.
                float waited = 0f;
                while (skyShell.RevealCoverage < mapShrinkStartRevealCoverage &&
                    waited < skyFractureCommitTimeout)
                {
                    waited += Time.deltaTime;
                    yield return null;
                }

                if (skyShell.RevealCoverage < mapShrinkStartRevealCoverage)
                {
                    Debug.LogWarning(
                        $"[SkyExit] Starting the shrink on the timeout at {waited:F2}s with " +
                        $"only {skyShell.RevealCoverage:P0} of the sky open. Being stuck in VR " +
                        $"is worse than an early shrink.",
                        this);
                }

                // Only the drone's own furniture goes. The circuit stays exactly as it is - it
                // is the thing that is about to shrink, and switching it off for even a frame
                // turns the whole transition back into "VR ended, then a map appeared".
                HideDroneHud();
                SetDroneWorldActive(false);

                yield return MapShrinkRoutine();

                // The last pieces of sky, finishing on their own. By now the circuit is already
                // sitting on the table at its proper size, so this costs nothing anybody is
                // waiting on.
                float settle = 0f;
                while (skyShell.IsRunning && settle < skyFractureCommitTimeout)
                {
                    settle += Time.deltaTime;
                    yield return null;
                }

                RestoreExitBookkeeping();

                // Full MR only now. Doing it at the shrink would have thrown the whole room up
                // at once and thrown away the piece-by-piece reveal the shell exists for.
                ApplyMrScreenEndpoint();

                // Seal and masks last, and only once the screen is already committed to MR.
                skyShell.Cleanup(restorePassthrough: false);

                ExitVrFinalize();
            }
            finally
            {
                SetMapVisible(true);
                RestoreHostUi();
            }
        }

        /// <summary>
        /// The circuit the viewer has been flying around contracts onto the real table, without
        /// ever stopping being drawn.
        ///
        /// This is the whole point of the transition. Hiding the huge circuit, waiting, and then
        /// growing a small one back reads as one scene ending and another starting; the same
        /// geometry shrinking in place reads as the world the viewer was standing in becoming
        /// the model on their table. Same renderers, same hierarchy, no swap, no gap.
        /// </summary>
        IEnumerator MapShrinkRoutine()
        {
            if (placementRoot == null)
                yield break;

            // Where the circuit has to end up, in world terms, right now.
            //
            // The anchor was captured when the origin was where the viewer left it, and flying
            // the drone has moved that origin since. The real table has not moved - it is the
            // virtual world that has slid relative to it - so the anchor is carried through the
            // origin's change to find where that same real spot is at this moment. Shrinking
            // onto the stale anchor would put the circuit wherever the table used to be
            // relative to the world, which is not where the viewer can see it.
            Matrix4x4 originAtEnter = Matrix4x4.TRS(
                savedOriginPosition, savedOriginRotation, Vector3.one);
            Matrix4x4 originNow = Matrix4x4.TRS(
                xrOrigin.transform.position, xrOrigin.transform.rotation, Vector3.one);

            Vector3 shrinkAnchor =
                (originNow * originAtEnter.inverse).MultiplyPoint3x4(entryWorldAnchor);

            float startedAt = Time.realtimeSinceStartup;

            Debug.Log(
                $"[DroneTransition][ShrinkStart] " +
                $"skyCoverage={(skyShell != null ? skyShell.RevealCoverage.ToString("P0") : "n/a")} " +
                $"roomState={(roomShell != null ? roomShell.State.ToString() : "n/a")} " +
                $"multiplier={vrScaleMultiplier} anchor={shrinkAnchor} " +
                $"origin={xrOrigin.transform.position}",
                this);

            float elapsed = 0f;
            while (elapsed < mapShrinkDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / mapShrinkDuration);

                // Exponential, the entry's growth run backwards. A straight interpolation from
                // a thousand to one is still at five hundred times by halfway, so the circuit
                // would look unchanged for most of the shrink and then vanish at the end.
                // Raising the multiplier to the remaining progress takes a constant proportion
                // off every second instead, which is what reads as a steady contraction.
                ApplyScaleAnchored(
                    Mathf.Pow(vrScaleMultiplier, 1f - Evaluate(scaleCurve, t)), shrinkAnchor);

                yield return null;
            }

            ApplyScaleAnchored(1f, shrinkAnchor);

            Debug.Log(
                $"[SkyExit][ShrinkComplete] duration={Time.realtimeSinceStartup - startedAt:F2}s " +
                $"placementScale={placementRoot.localScale} " +
                $"placementPos={placementRoot.position} " +
                $"originBeforeBookkeeping={xrOrigin.transform.position}",
                this);
        }

        /// <summary>
        /// Puts the origin back where the rest of the application expects it, and moves the map
        /// by exactly the amount that cancels out.
        ///
        /// The origin holds the camera, so restoring it moves the viewer, not the world. On its
        /// own that is a teleport - the thing every previous attempt at this transition was
        /// wrecked by. Done in the same frame as the matching correction to the map, the two
        /// changes leave the map's pose relative to the camera algebraically identical, and
        /// nothing on screen moves at all.
        /// </summary>
        void RestoreExitBookkeeping()
        {
            Vector3 originBefore = xrOrigin.transform.position;
            Vector3 mapBefore = placementRoot != null ? placementRoot.position : Vector3.zero;
            Vector3 cameraBefore = xrCamera.transform.position;
            Vector3 relativeBefore = mapBefore - cameraBefore;

            xrOrigin.transform.SetPositionAndRotation(savedOriginPosition, savedOriginRotation);

            if (placementRoot != null)
            {
                placementRoot.localPosition = savedPlacementLocalPositionAtEnter;
                placementRoot.localScale = savedPlacementLocalScale;
            }

            if (visualRoot != null)
            {
                visualRoot.localPosition = savedVisualLocalPosition;
                visualRoot.localRotation = savedVisualLocalRotation;
                visualRoot.localScale = savedVisualLocalScale;
            }

            audioDistanceScaler?.Restore();
            isVrActive = false;

            Vector3 relativeAfter = placementRoot != null
                ? placementRoot.position - xrCamera.transform.position
                : Vector3.zero;

            Debug.Log(
                $"[SkyExit][BookkeepingComplete] origin={xrOrigin.transform.position} " +
                $"placementPos={(placementRoot != null ? placementRoot.position : Vector3.zero)} " +
                $"visualDeltaBeforeAfter={(relativeAfter - relativeBefore).magnitude:F4}m " +
                $"originMoved={Vector3.Distance(originBefore, savedOriginPosition):F3}m",
                this);
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

            HideDroneHud();
            SetDroneWorldActive(false);
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
            RestorePlaneVisualizers();
            isVrActive = false;
        }

        void SuspendPlaneVisualizers()
        {
            RestorePlaneVisualizers();

            ARPlaneMeshVisualizer[] visualizers =
                FindObjectsByType<ARPlaneMeshVisualizer>(
                    FindObjectsInactive.Include);
            foreach (ARPlaneMeshVisualizer visualizer in visualizers)
                HidePlaneVisualizer(visualizer);

            AutomaticTableCandidatePreview[] candidatePreviews =
                FindObjectsByType<AutomaticTableCandidatePreview>(
                    FindObjectsInactive.Include);
            foreach (AutomaticTableCandidatePreview candidatePreview in
                candidatePreviews)
            {
                if (candidatePreview == null)
                    continue;

                candidatePreview.SetRuntimeVisible(false);
                hiddenCandidatePreviews.Add(candidatePreview);
            }

            ARPlaneManager[] planeManagers =
                FindObjectsByType<ARPlaneManager>(
                    FindObjectsInactive.Include);
            foreach (ARPlaneManager planeManager in planeManagers)
            {
                if (planeManager == null || !planeManager.isActiveAndEnabled)
                    continue;

                planeManager.trackablesChanged.AddListener(
                    OnPlaneTrackablesChanged);
                subscribedPlaneManagers.Add(planeManager);
            }
        }

        void OnPlaneTrackablesChanged(
            ARTrackablesChangedEventArgs<ARPlane> changes)
        {
            foreach (ARPlane plane in changes.added)
                HidePlaneVisualizer(plane?.GetComponent<ARPlaneMeshVisualizer>());

            foreach (ARPlane plane in changes.updated)
                HidePlaneVisualizer(plane?.GetComponent<ARPlaneMeshVisualizer>());
        }

        void HidePlaneVisualizer(ARPlaneMeshVisualizer visualizer)
        {
            if (visualizer == null || !visualizer.enabled)
                return;

            hiddenPlaneVisualizers.Add(visualizer);
            visualizer.enabled = false;
        }

        void RestorePlaneVisualizers()
        {
            foreach (ARPlaneManager planeManager in subscribedPlaneManagers)
            {
                if (planeManager != null)
                {
                    planeManager.trackablesChanged.RemoveListener(
                        OnPlaneTrackablesChanged);
                }
            }

            subscribedPlaneManagers.Clear();

            foreach (AutomaticTableCandidatePreview candidatePreview in
                hiddenCandidatePreviews)
            {
                candidatePreview?.SetRuntimeVisible(true);
            }

            hiddenCandidatePreviews.Clear();

            foreach (ARPlaneMeshVisualizer visualizer in hiddenPlaneVisualizers)
            {
                if (visualizer != null)
                    visualizer.enabled = true;
            }

            hiddenPlaneVisualizers.Clear();
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

        void ShowDroneHud()
        {
            droneHud?.Show(xrCamera);
            vehicleTargeting?.Show(xrCamera);
        }

        void HideDroneHud()
        {
            vehicleTargeting?.Hide();
            droneHud?.Hide();
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
            savedFarClipPlane = xrCamera.farClipPlane;
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
                Renderer renderer = ground.GetComponent<Renderer>();

                Texture2D terrainTexture = useAerialGround
                    ? Resources.Load<Texture2D>(GroundTextureResourcePath)
                    : null;
                Shader unlit = Shader.Find("Universal Render Pipeline/Unlit") ??
                    Shader.Find("Unlit/Texture");

                if (terrainTexture == null || unlit == null)
                {
                    // The plain dark slab: either aerial mode is off, or the photo or
                    // shader is missing and a solid plate beats a magenta one.
                    if (useAerialGround)
                    {
                        Debug.LogWarning(
                            "[VRDrone] Aerial ground fell back to the plain slab " +
                            $"(texture at Resources/{GroundTextureResourcePath}: " +
                            $"{terrainTexture != null}, unlit shader: {unlit != null}).",
                            this);
                    }

                    ground.transform.localScale = Vector3.one * 1000f;
                    if (renderer != null)
                        renderer.material.color = new Color(0.025f, 0.035f, 0.06f, 1f);
                    return;
                }

                // No scale here: in aerial mode PlaceGround owns it. It is
                // track-relative and rewritten on every call, including mid-growth.
                groundMaterial = new Material(unlit)
                {
                    name = "VR Drone Ground Terrain"
                };
                groundMaterial.mainTexture = terrainTexture;
                if (groundMaterial.HasProperty("_BaseMap"))
                    groundMaterial.SetTexture("_BaseMap", terrainTexture);
                if (groundMaterial.HasProperty("_BaseColor"))
                    groundMaterial.SetColor("_BaseColor", Color.white);
                MakeTransparent(groundMaterial);
                renderer.sharedMaterial = groundMaterial;
            }

        }

        void PlaceGround(Vector3 up)
        {
            if (!useAerialGround || groundMaterial == null)
            {
                float localY = GetTrackBaseLocalY() - 1f;
                ground.transform.position = placementRoot.TransformPoint(
                    new Vector3(0f, localY, 0f));
                ground.transform.rotation = Quaternion.FromToRotation(
                    Vector3.up,
                    up);
                return;
            }

            // Everything below is expressed in the circuit's own metres. The anchor is
            // the instantiated map itself, not placementRoot: the circuit arrives through
            // TrackMapView with its own shrink factor (the prefab is authored at 0.001,
            // and Show() then overwrites the instance scale), so a metre of circuit is
            // nowhere near one placementRoot unit. Reading the map's own transform folds
            // the table fit, that shrink factor and the x1000 growth into one number, and
            // PlaceGround already reruns mid-growth and at the end, which is exactly the
            // resync this needs.
            Transform anchor = FindAerialAnchor();
            float metresToWorld = anchor.lossyScale.x;

            ground.transform.position = anchor.TransformPoint(aerialLocalPosition);

            // The anchor already carries the surface orientation; the photo only adds
            // its own yaw. FromToRotation would throw that yaw away.
            ground.transform.rotation =
                anchor.rotation * Quaternion.Euler(0f, aerialLocalYaw, 0f);

            // Unity's Plane primitive is 10 x 10 units at scale 1.
            ground.transform.localScale = new Vector3(
                aerialLocalSize.x * metresToWorld / 10f,
                1f,
                aerialLocalSize.y * metresToWorld / 10f);

            Debug.Log(
                "[VRDrone][AerialGround] anchor=" + anchor.name +
                " metresToWorld=" + metresToWorld.ToString("F6") +
                " placementScale=" + placementRoot.lossyScale.x.ToString("F6") +
                " groundWidth=" + (aerialLocalSize.x * metresToWorld).ToString("F1"),
                this);
        }

        /// <summary>
        /// The transform whose local space is the circuit's own metres: the instantiated
        /// map root sitting directly under visualRoot. Found by climbing up from a known
        /// circuit renderer, because TrackMapView keeps its instance private and the
        /// prefab name is data, not something to hard-code. Falls back to placementRoot,
        /// which is wrong by the map's own shrink factor - but that branch is only
        /// reachable when no circuit renderer exists, i.e. when there is nothing on
        /// screen to be misaligned with anyway.
        /// </summary>
        Transform FindAerialAnchor()
        {
            if (visualRoot == null)
                return placementRoot;

            ScanTrackPlateRenderers();

            foreach (Renderer plate in plateRenderers)
            {
                if (plate == null)
                    continue;

                Transform node = plate.transform;
                while (node.parent != null && node.parent != visualRoot)
                    node = node.parent;

                if (node.parent == visualRoot)
                    return node;
            }

            return placementRoot;
        }

        /// <summary>
        /// The photo has the circuit punched out of it, so the plate has to actually
        /// respect alpha. URP/Unlit ships opaque, and an opaque plate would paint the
        /// hole straight back in.
        /// </summary>
        static void MakeTransparent(Material material)
        {
            material.SetOverrideTag("RenderType", "Transparent");
            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend"))
                material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_ZWrite"))
                material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_SrcBlend"))
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend"))
                material.SetInt(
                    "_DstBlend",
                    (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        /// <summary>
        /// The aerial world and the tabletop plate always move together - the photo
        /// replaces the plate, so one appearing is the other leaving. Kept in one place
        /// so a new call site cannot toggle half of it.
        /// </summary>
        void SetDroneWorldActive(bool active)
        {
            environment.SetActive(active);

            if (useAerialGround)
                SetTrackPlateVisible(!active);
        }

        /// <summary>
        /// Renderers only, never SetActive: colliders and grab targets on the plate keep
        /// working, and the placement bounds are computed with includeInactive anyway, so
        /// hiding this way changes nothing about how the map sits on the table.
        /// </summary>
        void SetTrackPlateVisible(bool visible)
        {
            ScanTrackPlateRenderers();

            foreach (Renderer plate in plateRenderers)
            {
                if (plate != null)
                    plate.enabled = visible;
            }
        }

        void ScanTrackPlateRenderers()
        {
            if (visualRoot == null)
                return;

            // A successful scan sticks; an empty one is retried, because the map prefab
            // is instantiated under visualRoot at some point after ConfigureHostScene and
            // there is no callback that says when.
            if (plateRenderersScanned && plateRenderers.Count > 0)
                return;

            plateRenderers.Clear();
            foreach (Renderer child in visualRoot.GetComponentsInChildren<Renderer>(true))
            {
                foreach (string plateName in plateRendererNames)
                {
                    if (!string.Equals(child.name, plateName, StringComparison.Ordinal))
                        continue;

                    plateRenderers.Add(child);
                    break;
                }
            }

            plateRenderersScanned = true;

            if (plateRenderers.Count == 0 && !warnedMissingPlates)
            {
                warnedMissingPlates = true;
                Debug.LogWarning(
                    "[VRDrone] No plate renderers matched " +
                    "[" + string.Join(", ", plateRendererNames) + "] under " +
                    "'" + visualRoot.name + "'. The placeholder plate will stay " +
                    "visible under the aerial photo.",
                    this);
            }
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
