using System;
using System.Collections;
using F1XR.RestAPI.Replay;
using F1XR.RestAPI.Replay.Track.Build;
using UnityEngine;

namespace F1XR.Experience
{
    public enum ExperienceMode
    {
        MRReplay,
        TransitionToVR,
        VRGame,
        TransitionToMR
    }

    /// <summary>
    /// Owns the MR Replay to VR Game mode state and the order of the steps between
    /// them. It contains no Passthrough details (that is
    /// <see cref="PassthroughTransitionController"/>), no vehicle driving, and no room
    /// destruction.
    ///
    /// The selected vehicle and the replay position are not duplicated into a new data
    /// store: <see cref="ReplayPlayer"/> already owns both, it is not a UI object, and
    /// it survives the MR visuals being switched off. Only a snapshot of the driver
    /// number is held here, so the value stays readable while MR is hidden.
    ///
    /// Reversal safety: transitions are never torn down with StopCoroutine. A single
    /// loop drives the current mode towards <see cref="targetMode"/>, each step checks
    /// whether the target changed, and the loop simply runs the opposite direction if
    /// it did. Whatever happens, the loop ends in <see cref="ApplyMRState"/> or
    /// <see cref="ApplyVRState"/>, which set every piece of state unconditionally.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ExperienceModeManager : MonoBehaviour
    {
        [SerializeField] ReplayPlayer player;
        [SerializeField] PassthroughTransitionController passthrough;

        [Tooltip("Root of the VR game environment. Kept inactive in MR and activated " +
            "before the Passthrough fade so no black frame is ever shown.")]
        [SerializeField] GameObject vrEnvironmentRoot;

        [Tooltip("MR-only scene roots to hide in VR. The placed track is resolved at " +
            "runtime and does not belong in this list.")]
        [SerializeField] GameObject[] mrVisualRoots;

        [SerializeField, Min(0f)] float transitionDuration = 2f;

        [Tooltip("Traces every stage of the mode change so a device run shows exactly " +
            "where the chain stops. Diagnostic only; turn off once the flow is trusted.")]
        [SerializeField] bool verboseLogging = true;

        /// <summary>
        /// Extension point for the future RoomShellFractureController. Assign a
        /// coroutine factory and it runs after the VR environment is ready and before
        /// the Passthrough fade, so the room can break apart while the real world is
        /// still visible. Not implemented in this step.
        /// </summary>
        public Func<IEnumerator> BreakSequence;

        /// <summary>
        /// Mirror of <see cref="BreakSequence"/> for the return trip: runs after the MR
        /// visuals are restored and before Passthrough fades back in.
        /// </summary>
        public Func<IEnumerator> RebuildSequence;

        ExperienceMode mode = ExperienceMode.MRReplay;
        ExperienceMode settledMode = ExperienceMode.MRReplay;
        ExperienceMode targetMode = ExperienceMode.MRReplay;
        Coroutine transitionRoutine;
        TrackRevealPlacer trackPlacer;

        int selectedDriverNumber;
        bool wasPlaying;
        bool holdsReplayPause;

        public ExperienceMode Mode => mode;

        /// <summary>Driver number captured when the VR transition was requested.</summary>
        public int SelectedDriverNumber => selectedDriverNumber;

        public event Action<int> VRGameEntered;
        public event Action MRReplayRestored;

        void Awake()
        {
            if (player == null)
                player = FindAnyObjectByType<ReplayPlayer>();

            if (passthrough == null)
                passthrough = FindAnyObjectByType<PassthroughTransitionController>();

            if (vrEnvironmentRoot != null && vrEnvironmentRoot.activeSelf)
                vrEnvironmentRoot.SetActive(false);
        }

        /// <summary>
        /// Requests the VR game. Rejected, with no state change at all, when no valid
        /// vehicle is selected.
        /// </summary>
        public void EnterVRGame()
        {
            Trace("2 EnterVRGame",
                $"mode={mode} settled={settledMode} target={targetMode} " +
                $"player={(player != null ? player.name : "NULL")} " +
                $"selected={(player != null ? player.SelectedDriverNumber : -1)}");

            if (targetMode == ExperienceMode.VRGame)
            {
                Trace("2 EnterVRGame", "IGNORED: already targeting VRGame.");
                return;
            }

            if (!TryGetSelectedDriver(out int driverNumber))
            {
                Trace("3 Validation", "FAILED, see the warning above. No state changed.");
                return;
            }

            Trace("3 Validation", $"PASSED driver={driverNumber}");
            selectedDriverNumber = driverNumber;

            // Pause at the moment of the request, not when the fade finishes. A fade
            // lasts seconds, and letting the replay run through it would break the
            // continuity between what the user last saw in MR and what VR starts from.
            if (!holdsReplayPause && player != null)
            {
                wasPlaying = player.IsPlaying;
                player.Pause();
                holdsReplayPause = true;
                Trace("4 ReplayPause",
                    $"wasPlaying={wasPlaying} time={player.CurrentTime:F2}");
            }
            else
            {
                Trace("4 ReplayPause",
                    $"skipped (holdsReplayPause={holdsReplayPause}, player={(player != null)})");
            }

            targetMode = ExperienceMode.VRGame;
            EnsureTransitionRunning();
        }

        /// <summary>Requests the MR replay. Always allowed.</summary>
        public void ReturnToMRReplay()
        {
            Trace("R2 ReturnToMRReplay",
                $"mode={mode} settled={settledMode} target={targetMode}");

            if (targetMode == ExperienceMode.MRReplay)
            {
                Trace("R2 ReturnToMRReplay", "IGNORED: already targeting MRReplay.");
                return;
            }

            Debug.Log("[VR2MR][1] Return requested", this);
            targetMode = ExperienceMode.MRReplay;
            EnsureTransitionRunning();
        }

        void Trace(string stage, string detail)
        {
            if (verboseLogging)
                Debug.Log($"[Step1][{stage}] {detail}", this);
        }

        void EnsureTransitionRunning()
        {
            if (!isActiveAndEnabled)
            {
                Debug.LogError(
                    "[Step1] Transition requested but ExperienceModeManager is not active " +
                    "and enabled, so no coroutine can run. Mode will not change.",
                    this);
                return;
            }

            if (transitionRoutine != null)
            {
                Trace("5 TransitionLoop", "already running, retargeted to " + targetMode);
                return;
            }

            transitionRoutine = StartCoroutine(TransitionLoop());
        }

        IEnumerator TransitionLoop()
        {
            while (settledMode != targetMode)
            {
                ExperienceMode goal = targetMode;
                mode = goal == ExperienceMode.VRGame
                    ? ExperienceMode.TransitionToVR
                    : ExperienceMode.TransitionToMR;

                Trace("5 Mode", $"{mode} (goal={goal})");

                yield return StartCoroutine(goal == ExperienceMode.VRGame
                    ? TransitionToVRRoutine()
                    : TransitionToMRRoutine());

                // The target flipped while we were mid-way. Loop again and run the
                // other direction from wherever things currently are.
                if (targetMode != goal)
                    continue;

                settledMode = goal;
            }

            if (settledMode == ExperienceMode.VRGame)
                ApplyVRState();
            else
                ApplyMRState();

            transitionRoutine = null;
        }

        IEnumerator TransitionToVRRoutine()
        {
            // Prepared first so the VR world already exists behind the real one.
            SetVREnvironmentActive(true);
            Trace("6 VREnvironment",
                $"SetActive(true) root={(vrEnvironmentRoot != null ? vrEnvironmentRoot.name : "NULL")} " +
                $"activeInHierarchy={(vrEnvironmentRoot != null && vrEnvironmentRoot.activeInHierarchy)}");

            if (BreakSequence != null)
                yield return StartCoroutine(BreakSequence());

            if (targetMode != ExperienceMode.VRGame)
            {
                Trace("6 VREnvironment", "reversed before the fade started.");
                yield break;
            }

            if (passthrough == null)
                Debug.LogError("[Step1][7] passthrough reference is NULL.", this);

            // The break itself already uncovered VR piece by piece, so this only settles the
            // end state. Fading here instead would replay the whole reveal a second time and
            // make VR look like it had been waiting behind the real room.
            Trace("7 Passthrough", $"ApplyVRImmediate state={passthrough?.State}");
            passthrough?.ApplyVRImmediate();

            SetMRVisualsActive(false);
            Trace("9 MRVisuals", "hidden");
        }

        IEnumerator TransitionToMRRoutine()
        {
            Debug.Log("[VR2MR][2] Outgoing VR retained", this);
            Debug.Log("[VR2MR] Outgoing=VR Incoming=MR", this);
            Debug.Log(
                $"[VR2MR] VR Environment active=" +
                $"{(vrEnvironmentRoot != null && vrEnvironmentRoot.activeInHierarchy)}",
                this);

            // Keep MR-only panels hidden during the fracture. Screen-space UI cannot sit
            // behind the transition shell, so enabling it here would reveal MR globally
            // before any fragment opens. Passthrough itself is prepared by the fracture
            // controller and becomes visible only through its holes.
            Debug.Log("[VR2MR][3] Incoming MR deferred until first detach", this);

            if (RebuildSequence != null)
                yield return StartCoroutine(RebuildSequence());

            if (targetMode != ExperienceMode.MRReplay)
            {
                Trace("R6 MRVisuals", "reversed before the fade started.");
                yield break;
            }

            // The fracture sequence reveals Passthrough through fixed alpha holes. Only
            // snap to the settled MR state after the outgoing VR fracture has finished.
            Debug.Log("[VR2MR][9] Finalize MR", this);
            Debug.Log("[VR2MR] FinalizeMR", this);
            passthrough?.ApplyMRImmediate();
            SetMRVisualsActive(true);
            Trace("R9 MRVisuals", "restored");
            SetVREnvironmentActive(false);
            Trace("R9 VREnvironment", "hidden");
            Debug.Log("[VR2MR][10] VR Environment disabled", this);
            Debug.Log("[VR2MR] VR Environment disabled", this);
        }

        /// <summary>
        /// Puts every piece of state into a consistent MR configuration. Idempotent,
        /// and never contains transition effects.
        /// </summary>
        void ApplyMRState()
        {
            SetMRVisualsActive(true);
            SetVREnvironmentActive(false);
            passthrough?.ApplyMRImmediate();
            mode = ExperienceMode.MRReplay;

            if (holdsReplayPause)
            {
                // Only resume what was actually running before the transition began.
                if (wasPlaying)
                    player?.Play();

                Trace("R10 ReplayResume",
                    $"wasPlaying={wasPlaying} time={(player != null ? player.CurrentTime : -1f):F2}");
                wasPlaying = false;
                holdsReplayPause = false;
            }

            Trace("R11 Mode", "MRReplay");
            MRReplayRestored?.Invoke();
        }

        /// <summary>
        /// Puts every piece of state into a consistent VR configuration. Idempotent,
        /// and never contains transition effects.
        /// </summary>
        void ApplyVRState()
        {
            SetVREnvironmentActive(true);
            SetMRVisualsActive(false);
            passthrough?.ApplyVRImmediate();
            mode = ExperienceMode.VRGame;

            Trace("10 ApplyVRState", $"passthrough={passthrough?.State}");
            Trace("11 Mode", $"VRGame driver={selectedDriverNumber}");
            VRGameEntered?.Invoke(selectedDriverNumber);
        }

        void SetMRVisualsActive(bool active)
        {
            if (mrVisualRoots != null)
            {
                foreach (GameObject root in mrVisualRoots)
                {
                    if (root != null && root.activeSelf != active)
                        root.SetActive(active);
                }
            }

            Transform track = ResolveTrackRoot();
            if (track != null && track.gameObject.activeSelf != active)
                track.gameObject.SetActive(active);
        }

        void SetVREnvironmentActive(bool active)
        {
            if (vrEnvironmentRoot != null && vrEnvironmentRoot.activeSelf != active)
                vrEnvironmentRoot.SetActive(active);
        }

        Transform ResolveTrackRoot()
        {
            // The track is spawned at runtime by TrackRevealPlacer, so it cannot be a
            // serialized reference.
            trackPlacer ??= player != null ? player.buildPlacer : null;
            return trackPlacer != null ? trackPlacer.PlacementTransform : null;
        }

        bool TryGetSelectedDriver(out int driverNumber)
        {
            driverNumber = 0;

            if (player == null)
            {
                Debug.LogWarning(
                    "[Experience] EnterVRGame ignored: no ReplayPlayer in the scene.",
                    this);
                return false;
            }

            // ReplayPlayer treats 0 as "nothing selected": SetSelectedDriver clamps with
            // Mathf.Max(0, ...), deselection goes through SetSelectedDriver(0), and the
            // audio and presentation layers gate on driver > 0. Same rule is used here
            // rather than inventing a sentinel.
            int candidate = player.SelectedDriverNumber;
            if (candidate <= 0)
            {
                Debug.LogWarning(
                    "[Experience] EnterVRGame ignored: no vehicle is selected. Select a " +
                    "car in MR first.",
                    this);
                return false;
            }

            if (!player.TryGetCarTransform(candidate, out _))
            {
                // Cars only exist once a dataset has loaded and the track has been placed,
                // because ReplayCarSet parents them under the placed track. Spell that out
                // rather than just saying the car is missing.
                Debug.LogWarning(
                    $"[Experience] EnterVRGame ignored: driver #{candidate} is selected " +
                    "but no car exists for it yet. " +
                    $"hasDataset={player.HasDataset} " +
                    $"isTrackPlaced={player.IsTrackPlaced} " +
                    $"isPlacementActive={player.IsTrackPlacementActive} " +
                    $"replayTime={player.CurrentTime:F2}. " +
                    "Cars appear after the dataset loads AND the track is placed; place " +
                    "the track first, then select a car.",
                    this);
                return false;
            }

            driverNumber = candidate;
            return true;
        }
    }
}
