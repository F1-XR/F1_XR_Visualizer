using System.Collections;
using F1XR.RestAPI.Replay;
using TMPro;
using UnityEngine;

namespace F1XR.Experience
{
    /// <summary>
    /// Temporary way to drive the mode switch without touching the gear system.
    /// Every entry point is a context menu item on this component's inspector header.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ExperienceModeDebugTrigger : MonoBehaviour
    {
        [SerializeField] ExperienceModeManager manager;
        [SerializeField] ReplayPlayer player;

        [Tooltip("Driver number used by Test Select Vehicle. No vehicle is ever chosen " +
            "automatically outside this test path.")]
        [SerializeField] int testDriverNumber = 16;

        [Tooltip("Optional label inside the VR environment, used to confirm the vehicle " +
            "selected in MR survived the transition.")]
        [SerializeField] TMP_Text vrDriverLabel;

        void Awake()
        {
            if (manager == null)
                manager = FindAnyObjectByType<ExperienceModeManager>();

            if (player == null)
                player = FindAnyObjectByType<ReplayPlayer>();
        }

        void OnEnable()
        {
            if (player != null)
                player.SelectedDriverChanged += OnSelectedDriverChanged;

            if (manager == null)
                return;

            manager.VRGameEntered += OnVRGameEntered;
            manager.MRReplayRestored += OnMRReplayRestored;
        }

        void OnDisable()
        {
            if (player != null)
                player.SelectedDriverChanged -= OnSelectedDriverChanged;

            if (manager == null)
                return;

            manager.VRGameEntered -= OnVRGameEntered;
            manager.MRReplayRestored -= OnMRReplayRestored;
        }

        /// <summary>
        /// Every change to the selection is reported with a stack trace, so whatever
        /// resets it back to 0 identifies itself instead of having to be guessed at.
        /// ReplayPlayer.ClearReplay and ResetTrackPlacement both call SetSelectedDriver(0),
        /// and ClearReplay runs from LoadDataset, which the auto starter triggers
        /// asynchronously.
        /// </summary>
        void OnSelectedDriverChanged(int driverNumber)
        {
            Debug.Log(
                $"[Select][EVENT] SelectedDriverNumber -> {driverNumber}\n" +
                System.Environment.StackTrace,
                this);
        }

        [ContextMenu("Test Select Vehicle")]
        public void TestSelectVehicle()
        {
            if (player == null)
            {
                Debug.LogWarning("[ExperienceDebug] No ReplayPlayer found.", this);
                return;
            }

            // The replay has to be ready before a selection means anything: the car set is
            // only populated once the dataset has loaded and the track has been placed.
            // Selecting before then writes a number that no car answers to.
            ReplayReadiness readiness = GetReadiness();
            if (!readiness.IsReady)
            {
                LastSelectResult =
                    $"SELECT FAILED\nCars={readiness.CarCount} {readiness.Blocker}";
                Debug.LogWarning(
                    $"[Select][Set] REFUSED, replay not ready: {readiness.Blocker} " +
                    $"(dataset={readiness.HasDataset} track={readiness.TrackPlaced} " +
                    $"cars={readiness.CarCount} testCar={readiness.HasTestCar}). " +
                    "Selection left unchanged.",
                    this);
                return;
            }

            int before = player.SelectedDriverNumber;
            player.SetSelectedDriver(testDriverNumber);
            int after = player.SelectedDriverNumber;

            Debug.Log(
                $"[Select][Set] requested={testDriverNumber} result={after} " +
                $"before={before} player='{player.name}' id={player.GetInstanceID()} " +
                $"{(after == testDriverNumber ? "OK" : "MISMATCH")}",
                this);

            LastSelectResult = after == testDriverNumber
                ? $"SELECT OK\n{Describe(after)}"
                : $"SELECT FAILED\nresult={after}";

            // A late LoadDataset would call ClearReplay, which resets the selection to 0.
            // Re-read over the next few frames instead of watching every frame forever.
            if (isActiveAndEnabled)
                StartCoroutine(TrackSelectionSettling(testDriverNumber));
        }

        IEnumerator TrackSelectionSettling(int expected)
        {
            yield return null;
            int nextFrame = player != null ? player.SelectedDriverNumber : -1;
            Debug.Log(
                $"[Select][NextFrame] selected={nextFrame}" +
                (nextFrame == expected ? "" : "  <-- OVERWRITTEN"),
                this);

            yield return new WaitForSeconds(1.5f);
            int stable = player != null ? player.SelectedDriverNumber : -1;
            bool hasCar = player != null && player.TryGetCarTransform(expected, out _);
            Debug.Log(
                $"[Select][Stable] selected={stable} carExists={hasCar}" +
                (stable == expected ? "" : "  <-- OVERWRITTEN"),
                this);

            if (stable != expected)
            {
                LastSelectResult = $"SELECT LOST\nwas {expected}, now {stable}";
                Debug.LogWarning(
                    "[Select][Stable] The selection was reset after being set. Check the " +
                    "[Select][EVENT] stack trace above to see which call did it.",
                    this);
            }
        }

        /// <summary>Snapshot of whether the replay can support a vehicle selection.</summary>
        public readonly struct ReplayReadiness
        {
            public readonly bool HasPlayer;
            public readonly bool HasDataset;
            public readonly bool TrackPlaced;
            public readonly bool HasTestCar;
            public readonly int CarCount;
            public readonly int Selected;

            public ReplayReadiness(
                bool hasPlayer, bool hasDataset, bool trackPlaced,
                bool hasTestCar, int carCount, int selected)
            {
                HasPlayer = hasPlayer;
                HasDataset = hasDataset;
                TrackPlaced = trackPlaced;
                HasTestCar = hasTestCar;
                CarCount = carCount;
                Selected = selected;
            }

            public bool IsReady =>
                HasPlayer && HasDataset && TrackPlaced && CarCount > 0 && HasTestCar;

            /// <summary>First unmet condition, for display.</summary>
            public string Blocker =>
                !HasPlayer ? "no ReplayPlayer"
                : !HasDataset ? "dataset not loaded"
                : !TrackPlaced ? "track not placed"
                : CarCount == 0 ? "no cars spawned"
                : !HasTestCar ? "test car not in this replay"
                : "ready";
        }

        /// <summary>
        /// Reads readiness from the existing ReplayPlayer API only. No new replay state
        /// system is introduced.
        /// </summary>
        public ReplayReadiness GetReadiness()
        {
            if (player == null)
                return new ReplayReadiness(false, false, false, false, 0, 0);

            int carCount = 0;
            for (int driver = 1; driver <= 99; driver++)
            {
                if (player.TryGetCarTransform(driver, out _))
                    carCount++;
            }

            return new ReplayReadiness(
                true,
                player.HasDataset,
                player.IsTrackPlaced,
                player.TryGetCarTransform(testDriverNumber, out _),
                carCount,
                player.SelectedDriverNumber);
        }

        /// <summary>Result of the most recent select attempt, for the debug panel.</summary>
        public string LastSelectResult { get; private set; } = "";

        public int TestDriverNumber => testDriverNumber;

        /// <summary>True when EnterVRGame would pass the manager's vehicle check.</summary>
        public bool IsVehicleReadyForVR()
        {
            if (player == null)
                return false;

            int selected = player.SelectedDriverNumber;
            return selected > 0 && player.TryGetCarTransform(selected, out _);
        }

        public string DescribeSelected()
        {
            if (player == null)
                return "no player";

            int selected = player.SelectedDriverNumber;
            return selected > 0 ? Describe(selected) : "none";
        }

        /// <summary>
        /// Lists which driver numbers currently have a car object. Answers "does any car
        /// exist at all" without inventing one.
        /// </summary>
        [ContextMenu("Log Existing Cars")]
        public void LogExistingCars()
        {
            if (player == null)
            {
                Debug.LogWarning("[ExperienceDebug] No ReplayPlayer found.", this);
                return;
            }

            var present = new System.Text.StringBuilder();
            int count = 0;
            for (int driver = 1; driver <= 99; driver++)
            {
                if (!player.TryGetCarTransform(driver, out _))
                    continue;

                count++;
                if (present.Length > 0)
                    present.Append(", ");
                present.Append(driver);
            }

            Debug.Log(
                $"[Select][4] cars currently in the replay = {count} " +
                $"[{(count > 0 ? present.ToString() : "none")}]",
                this);
        }

        // Clearing the selection used to be exposed as a panel button and a context menu
        // item. Device logs caught it twice wiping a good selection right before Enter VR
        // Game, once from the button next door and once from the Inspector menu, and each
        // time the VR attempt then failed validation. It only ever existed to exercise the
        // refusal path, which the readiness gate already covers, so it is gone. Deselect by
        // calling ReplayPlayer.SetSelectedDriver(0) directly if it is ever needed again.

        [ContextMenu("Test Enter VR Game")]
        public void TestEnterVRGame()
        {
            manager?.EnterVRGame();
            LogMode("EnterVRGame requested");
        }

        [ContextMenu("Test Return MR")]
        public void TestReturnMR()
        {
            manager?.ReturnToMRReplay();
            LogMode("ReturnToMRReplay requested");
        }

        [ContextMenu("Log Current State")]
        public void LogCurrentState()
        {
            LogMode("state");
        }

        void OnVRGameEntered(int driverNumber)
        {
            string text = $"VR GAME\n{Describe(driverNumber)}";
            if (vrDriverLabel != null)
                vrDriverLabel.text = text;

            Debug.Log($"[ExperienceDebug] VRGame entered with {Describe(driverNumber)}.", this);
        }

        void OnMRReplayRestored()
        {
            Debug.Log($"[ExperienceDebug] MRReplay restored: {DescribeReplay()}", this);
        }

        void LogMode(string reason)
        {
            Debug.Log(
                $"[ExperienceDebug] {reason}: mode={manager?.Mode}, {DescribeReplay()}",
                this);
        }

        string DescribeReplay()
        {
            if (player == null)
                return "no ReplayPlayer";

            return $"selected={player.SelectedDriverNumber}, " +
                $"replayTime={player.CurrentTime:F2}, playing={player.IsPlaying}";
        }

        string Describe(int driverNumber)
        {
            string label = player != null ? player.GetDriverLabel(driverNumber) : null;
            return string.IsNullOrWhiteSpace(label)
                ? $"#{driverNumber}"
                : $"#{driverNumber} {label}";
        }
    }
}
