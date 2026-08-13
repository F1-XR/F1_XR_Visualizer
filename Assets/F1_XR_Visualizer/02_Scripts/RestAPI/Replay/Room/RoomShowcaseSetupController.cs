using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace F1XR.RestAPI.Replay.Room
{
    public enum RoomShowcaseSetupState
    {
        WaitingForRoom,
        SelectEntry,
        SelectExit,
        CaptureHero,
        Review,
        Ready,
        TemporarilyReacquiring,
        Error
    }

    [DisallowMultipleComponent]
    public sealed class RoomShowcaseSetupController : MonoBehaviour
    {
        [Header("Sources")]
        [SerializeField] private WallDiscovery wallDiscovery;
        [SerializeField] private ShowcaseLayout showcaseLayout;
        [SerializeField] private ShowcasePathPreview showcasePath;
        [SerializeField] private RoomShowcaseSetupView setupView;

        [Header("Room Stability")]
        [SerializeField, Min(0f)] private float candidateStableDuration = 0.75f;
        [SerializeField, Min(0f)] private float reacquisitionUiDelay = 0.6f;

        [Header("Automatic Setup")]
        [SerializeField] private bool automaticSetupEnabled = true;
        [SerializeField, Min(0f)] private float containingRoomWaitDuration = 3f;

        [Header("Quest Link Recovery")]
        [SerializeField, Min(1f)] private float planeDiscoveryRetryDelay = 15f;
        [SerializeField, Min(2f)] private float planeDiscoveryTimeout = 18f;

        [Header("Runtime Performance")]
        [SerializeField] private bool suspendSceneTrackingWhenReady = true;

        private IShowcaseWallProvider wallProvider;
        private ARPlaneManager planeManager;
        private ARRaycastManager raycastManager;
        private RoomShowcaseSetupState currentSetupState;
        private RoomShowcaseSetupState stateBeforeReacquisition;
        private TrackableId? previewCandidateId;
        private int previewCandidateDisplayIndex = -1;
        private int previewCandidateUserFacingNumber = -1;
        private int observedCandidateRevision = -1;
        private int observedCandidateCount = -1;
        private float candidatesStableSince;
        private readonly List<TrackableId> candidateMembership = new();
        private readonly List<TrackableId> candidateMembershipScratch = new();
        private readonly List<WallCandidateInfo> automaticCandidates = new();
        private float automaticSetupWaitUntil;
        private float roomWaitStartedAt;
        private bool planeDiscoveryRetried;
        private float reacquisitionStartedAt = -1f;
        private bool sessionConfirmed;
        private bool initialized;
        private string lastUserMessage = string.Empty;
        private bool sceneTrackingSuspended;
        private bool planeManagerWasEnabled;
        private bool raycastManagerWasEnabled;
        private readonly List<ARPlaneMeshVisualizer>
            suspendedPlaneVisualizers = new();

        public RoomShowcaseSetupState CurrentSetupState => currentSetupState;
        public bool IsSetupReady =>
            sessionConfirmed &&
            currentSetupState == RoomShowcaseSetupState.Ready &&
            showcaseLayout != null &&
            showcaseLayout.IsLayoutValid &&
            showcasePath != null &&
            showcasePath.IsPathValid;
        public TrackableId? PreviewCandidateId => previewCandidateId;
        public int PreviewCandidateDisplayIndex => previewCandidateDisplayIndex;
        public int PreviewCandidateUserFacingNumber =>
            previewCandidateUserFacingNumber;
        public TrackableId? ConfirmedEntryId =>
            wallProvider?.EntrySelectedTrackableId;
        public TrackableId? ConfirmedExitId =>
            wallProvider?.ExitSelectedTrackableId;
        public int CandidateCount => wallProvider?.CandidateCount ?? 0;
        public bool HasPitWallCandidate =>
            CandidateCount > 0 ||
            showcaseLayout != null &&
            showcaseLayout.EntryPoseValid;
        public string LastUserMessage => lastUserMessage;
        public bool IsWaitingForReacquisition =>
            currentSetupState ==
            RoomShowcaseSetupState.TemporarilyReacquiring;

        private void Awake()
        {
            ResolveReferences();
            setupView?.Initialize(this);
        }

        private void Start()
        {
            Initialize();
        }

        private void OnDisable()
        {
            wallProvider?.ClearPreview();
            SetSceneTrackingSuspended(false);
        }

        private void Update()
        {
            if (!initialized)
                return;

            ResolveReferences();
            if (!HasRequiredReferences())
            {
                SetState(
                    RoomShowcaseSetupState.Error,
                    "Room setup is unavailable. Use Reset to try again.");
                return;
            }

            if (!sessionConfirmed ||
                showcaseLayout == null ||
                !showcaseLayout.WallFramesFrozen)
            {
                ObserveCandidates();
                if (HandleReacquisition())
                    return;
            }

            if (currentSetupState == RoomShowcaseSetupState.WaitingForRoom)
            {
                if (wallProvider.CandidateCount == 0)
                {
                    UpdateRoomLoadingState();
                    if (currentSetupState !=
                        RoomShowcaseSetupState.WaitingForRoom)
                    {
                        return;
                    }
                }

                TryLeaveWaitingState();
            }
            else if (currentSetupState == RoomShowcaseSetupState.Ready &&
                !IsSetupReady)
            {
                sessionConfirmed = false;
                SetState(
                    RoomShowcaseSetupState.Review,
                    "The room path is no longer valid. Review the setup.");
            }

        }

        public void PreviousCandidate()
        {
            CycleCandidate(-1);
        }

        public void NextCandidate()
        {
            CycleCandidate(1);
        }

        public void ConfirmEntry()
        {
            if (currentSetupState != RoomShowcaseSetupState.SelectEntry ||
                !previewCandidateId.HasValue)
            {
                return;
            }

            if (ConfirmedExitId.HasValue &&
                ConfirmedExitId.Value == previewCandidateId.Value)
            {
                SetUserMessage("Entry and Exit must use different walls.");
                return;
            }

            if (!wallProvider.TrySelectEntryById(previewCandidateId.Value))
            {
                SetUserMessage("That wall is no longer available.");
                RefreshPreviewAfterCandidateChange();
                return;
            }

            SetState(
                RoomShowcaseSetupState.SelectExit,
                "Entry confirmed. Select a different Exit wall.");
            SelectFirstAvailableCandidate(1);
        }

        public void ConfirmExit()
        {
            if (currentSetupState != RoomShowcaseSetupState.SelectExit ||
                !previewCandidateId.HasValue)
            {
                return;
            }

            if (ConfirmedEntryId.HasValue &&
                ConfirmedEntryId.Value == previewCandidateId.Value)
            {
                SetUserMessage("Entry and Exit must use different walls.");
                return;
            }

            if (!wallProvider.TrySelectExitById(previewCandidateId.Value))
            {
                SetUserMessage("That wall is no longer available.");
                RefreshPreviewAfterCandidateChange();
                return;
            }

            ClearPreview();
            SetState(
                RoomShowcaseSetupState.CaptureHero,
                "Face the intended vehicle travel direction, then capture Hero.");
        }

        public void BackToEntry()
        {
            sessionConfirmed = false;
            ClearRoomSnapshot();
            wallProvider.ClearExitSelection();
            wallProvider.ClearEntrySelection();
            showcaseLayout.ClearHeroCapture();
            SetState(
                RoomShowcaseSetupState.SelectEntry,
                "Select the Entry wall again.");
            SelectFirstAvailableCandidate(1);
        }

        public void CaptureHero()
        {
            if (currentSetupState != RoomShowcaseSetupState.CaptureHero)
                return;

            if (!showcaseLayout.TryCaptureHeroFromCurrentView())
            {
                SetUserMessage(
                    "Hero capture failed. Keep your view level and try again.");
                return;
            }

            showcasePath.RebuildPath();
            SetState(
                RoomShowcaseSetupState.Review,
                "Review the ENTRY > HERO > EXIT path.");
        }

        public void BackToExit()
        {
            sessionConfirmed = false;
            ClearRoomSnapshot();
            showcaseLayout.ClearHeroCapture();
            wallProvider.ClearExitSelection();
            SetState(
                RoomShowcaseSetupState.SelectExit,
                "Select the Exit wall again.");
            SelectFirstAvailableCandidate(1);
        }

        public void ConfirmSetup()
        {
            if (currentSetupState != RoomShowcaseSetupState.Review)
                return;

            showcaseLayout.RebuildLayout();
            showcasePath.RebuildPath();
            if (!showcaseLayout.IsLayoutValid || !showcasePath.IsPathValid)
            {
                SetUserMessage(
                    "The path is not valid yet. Check Entry, Exit, and Hero.");
                return;
            }

            if (!TryCaptureRoomSnapshot())
            {
                SetUserMessage(
                    "The confirmed room walls could not be frozen. Try setup again.");
                return;
            }

            showcasePath.RebuildPath();
            if (!showcaseLayout.IsLayoutValid || !showcasePath.IsPathValid)
            {
                ClearRoomSnapshot();
                SetUserMessage(
                    "The frozen room path is not valid. Check Entry and Exit.");
                return;
            }

            sessionConfirmed = true;
            ClearPreview();
            SetState(
                RoomShowcaseSetupState.Ready,
                "Room setup is complete. Event replay is now available.");
        }

        public void RecaptureHero()
        {
            sessionConfirmed = false;
            ClearRoomSnapshot();
            showcaseLayout.ClearHeroCapture();
            SetState(
                RoomShowcaseSetupState.CaptureHero,
                "Face the new travel direction, then capture Hero.");
        }

        public void ReselectEntry()
        {
            sessionConfirmed = false;
            ClearRoomSnapshot();
            wallProvider.ClearEntrySelection();
            SetState(
                RoomShowcaseSetupState.SelectEntry,
                "Select a new Entry wall.");
            SelectFirstAvailableCandidate(1);
        }

        public void ReselectExit()
        {
            sessionConfirmed = false;
            ClearRoomSnapshot();
            wallProvider.ClearExitSelection();
            SetState(
                RoomShowcaseSetupState.SelectExit,
                "Select a new Exit wall.");
            SelectFirstAvailableCandidate(1);
        }

        public void ResetSetup()
        {
            CloseEventReplay();
            sessionConfirmed = false;
            ClearRoomSnapshot();
            ClearPreview();
            wallProvider?.ClearEntrySelection();
            wallProvider?.ClearExitSelection();
            showcaseLayout?.ClearHeroCapture();
            wallDiscovery?.RetryMetaRoomSetup();

            if (wallProvider == null ||
                wallProvider.CandidateCount == 0)
            {
                candidatesStableSince = Time.unscaledTime;
                automaticSetupWaitUntil =
                    Time.unscaledTime + containingRoomWaitDuration;
                EnterWaitingForRoom();
                return;
            }

            SetState(
                RoomShowcaseSetupState.SelectEntry,
                "Room setup was reset. Select the Entry wall.");
            SelectFirstAvailableCandidate(1);
        }

        public void ReconfigureRoom()
        {
            CloseEventReplay();
            sessionConfirmed = false;
            ClearRoomSnapshot();
            ClearPreview();

            if (showcaseLayout != null &&
                showcasePath != null &&
                showcaseLayout.IsLayoutValid &&
                showcasePath.IsPathValid)
            {
                SetState(
                    RoomShowcaseSetupState.Review,
                    "Edit the room setup, then confirm it again.");
                return;
            }

            ReturnToFirstIncompleteStep();
        }

        public void RecenterPanel()
        {
            ResolveReferences();
            setupView?.RecenterPanel();
        }

        public void NotifyOpenBlocked()
        {
            if (IsSetupReady)
                return;

            setupView?.SetVisible(true);

            SetUserMessage(
                "Complete and confirm the room setup before opening replay.");
        }

        private void Initialize()
        {
            if (initialized)
                return;

            initialized = true;
            observedCandidateRevision =
                wallProvider?.CandidateRevision ?? -1;
            observedCandidateCount =
                wallProvider?.CandidateCount ?? 0;
            CaptureCandidateMembership(candidateMembership);
            candidatesStableSince = Time.unscaledTime;
            automaticSetupWaitUntil =
                Time.unscaledTime + containingRoomWaitDuration;
            sessionConfirmed = false;
            ClearPreview();
            EnterWaitingForRoom();
        }

        private void ResolveReferences()
        {
            if (wallDiscovery == null)
                wallDiscovery = GetComponent<WallDiscovery>();
            if (showcaseLayout == null)
                showcaseLayout = GetComponent<ShowcaseLayout>();
            if (showcasePath == null)
                showcasePath = GetComponent<ShowcasePathPreview>();
            if (setupView == null)
                setupView = GetComponent<RoomShowcaseSetupView>();

            wallProvider = wallDiscovery;
            setupView?.Initialize(this);
        }

        private bool HasRequiredReferences()
        {
            return wallProvider != null &&
                showcaseLayout != null &&
                showcasePath != null &&
                setupView != null &&
                setupView.IsConfigured;
        }

        private string ResolveRoomLoadingMessage()
        {
            if (wallDiscovery == null || !wallDiscovery.UsesMetaSceneApi)
                return "Loading room wall candidates.";

            return wallDiscovery.MetaSceneStatus switch
            {
                MetaSceneRoomStatus.WaitingForPermission =>
                    "Allow Spatial Data access to load the room.",
                MetaSceneRoomStatus.Loading =>
                    "Loading the Meta room structure.",
                MetaSceneRoomStatus.OpeningSpaceSetup =>
                    "Complete Meta Space Setup, then return to the app.",
                MetaSceneRoomStatus.PermissionDenied =>
                    "Spatial Data permission was denied. Use Reset Setup to retry.",
                MetaSceneRoomStatus.NoSceneModel =>
                    wallDiscovery.MetaSceneStatusMessage ??
                    "No saved room scan was found. Use Reset Setup to retry.",
                MetaSceneRoomStatus.Failed =>
                    wallDiscovery.MetaSceneStatusMessage ??
                    "Meta room loading failed. Use Reset Setup to retry.",
                MetaSceneRoomStatus.Ready =>
                    "The Meta room loaded, but no valid wall anchors were found.",
                _ => "Loading the Meta room structure."
            };
        }

        private void EnterWaitingForRoom()
        {
            roomWaitStartedAt = Time.unscaledTime;
            planeDiscoveryRetried = false;
            SetState(
                RoomShowcaseSetupState.WaitingForRoom,
                ResolveRoomLoadingMessage());
        }

        private void UpdateRoomLoadingState()
        {
            if (wallDiscovery == null || wallDiscovery.UsesMetaSceneApi)
            {
                SetUserMessage(ResolveRoomLoadingMessage());
                return;
            }

            float elapsed = Time.unscaledTime - roomWaitStartedAt;
            if (!planeDiscoveryRetried &&
                elapsed >= planeDiscoveryRetryDelay)
            {
                planeDiscoveryRetried = true;
                SetUserMessage("Waiting for the Quest Link room plane retry.");
                return;
            }

            if (elapsed >= Mathf.Max(
                    planeDiscoveryTimeout,
                    planeDiscoveryRetryDelay + 2f))
            {
                SetState(
                    RoomShowcaseSetupState.Error,
                    "Quest Link spatial data is unavailable. Re-enter Quest Link, then use Reset Setup.");
                return;
            }

            SetUserMessage(ResolveRoomLoadingMessage());
        }

        private void ObserveCandidates()
        {
            var revision = wallProvider.CandidateRevision;
            var count = wallProvider.CandidateCount;
            if (revision == observedCandidateRevision &&
                count == observedCandidateCount)
            {
                return;
            }

            observedCandidateRevision = revision;
            observedCandidateCount = count;
            CaptureCandidateMembership(candidateMembershipScratch);
            var membershipChanged = !HasSameCandidateMembership(
                candidateMembership,
                candidateMembershipScratch);

            if (membershipChanged)
            {
                CopyCandidateMembership(
                    candidateMembershipScratch,
                    candidateMembership);
                candidatesStableSince = Time.unscaledTime;
            }

            if (candidateMembershipScratch.Count == 0)
            {
                ClearPreview();
                if (wallProvider.EntrySelectionState ==
                        WallSelectionState.Reacquiring ||
                    wallProvider.ExitSelectionState ==
                        WallSelectionState.Reacquiring)
                {
                    return;
                }

                sessionConfirmed = false;
                if (currentSetupState !=
                    RoomShowcaseSetupState.WaitingForRoom)
                {
                    EnterWaitingForRoom();
                }

                return;
            }

            if (membershipChanged || IsWallSelectionState())
                RefreshPreviewAfterCandidateChange(membershipChanged);
        }

        private void TryLeaveWaitingState()
        {
            if (wallProvider.CandidateCount == 0)
                return;

            if (Time.unscaledTime - candidatesStableSince <
                candidateStableDuration)
            {
                return;
            }

            wallProvider.FinalizeUserFacingOrder();
            if (automaticSetupEnabled &&
                wallProvider.HasContainingRoomBoundary &&
                TryApplyAutomaticDefaultSetup())
            {
                return;
            }

            if (automaticSetupEnabled &&
                !wallProvider.HasContainingRoomBoundary &&
                Time.unscaledTime < automaticSetupWaitUntil)
            {
                return;
            }

            SetState(
                RoomShowcaseSetupState.SelectEntry,
                automaticSetupEnabled
                    ? "Automatic room setup was unavailable. Select the Entry wall."
                    : "Room walls are ready. Select the Entry wall.");
            SelectFirstAvailableCandidate(1);
        }

        private bool TryApplyAutomaticDefaultSetup()
        {
            if (!TryChooseDefaultWallPair(
                    out var entry,
                    out var exit))
            {
                return false;
            }

            wallProvider.ClearEntrySelection();
            wallProvider.ClearExitSelection();
            showcaseLayout.ClearHeroCapture();
            if (!wallProvider.TrySelectEntryById(entry.Id) ||
                !wallProvider.TrySelectExitById(exit.Id) ||
                !showcaseLayout.TryCaptureAutomaticHero(
                    ResolvePairTravel(entry, exit)))
            {
                ClearAutomaticSetup();
                return false;
            }

            showcasePath.RebuildPath();
            if (!showcaseLayout.IsLayoutValid ||
                !showcasePath.IsPathValid)
            {
                ClearAutomaticSetup();
                return false;
            }

            if (!TryCaptureRoomSnapshot())
            {
                ClearAutomaticSetup();
                return false;
            }

            showcasePath.RebuildPath();
            if (!showcaseLayout.IsLayoutValid ||
                !showcasePath.IsPathValid)
            {
                ClearAutomaticSetup();
                return false;
            }

            sessionConfirmed = true;
            ClearPreview();
            SetState(
                RoomShowcaseSetupState.Ready,
                "Room setup was completed automatically.");
            Debug.Log(
                $"[RoomAutoSetup] Entry={entry.UserFacingNumber}, " +
                $"Exit={exit.UserFacingNumber}, " +
                $"roomWalls={automaticCandidates.Count}.",
                this);
            return true;
        }

        private bool TryChooseDefaultWallPair(
            out WallCandidateInfo entry,
            out WallCandidateInfo exit)
        {
            entry = default;
            exit = default;
            CaptureAutomaticCandidates();
            if (automaticCandidates.Count < 2)
                return false;

            var bestScore = float.NegativeInfinity;
            var bestFirst = -1;
            var bestSecond = -1;
            for (var firstIndex = 0;
                 firstIndex < automaticCandidates.Count - 1;
                 firstIndex++)
            {
                var first = automaticCandidates[firstIndex];
                var firstNormal = Flat(first.InwardNormal);
                if (firstNormal.sqrMagnitude < 0.0001f)
                    continue;
                firstNormal.Normalize();

                for (var secondIndex = firstIndex + 1;
                     secondIndex < automaticCandidates.Count;
                     secondIndex++)
                {
                    var second = automaticCandidates[secondIndex];
                    var secondNormal = Flat(second.InwardNormal);
                    if (secondNormal.sqrMagnitude < 0.0001f)
                        continue;
                    secondNormal.Normalize();

                    var normalDot =
                        Vector3.Dot(firstNormal, secondNormal);
                    if (normalDot > -0.35f)
                        continue;

                    var separation = Flat(
                        second.Center - first.Center);
                    var forwardSpan =
                        (Mathf.Abs(
                             Vector3.Dot(separation, firstNormal)) +
                         Mathf.Abs(
                             Vector3.Dot(separation, secondNormal))) *
                        0.5f;
                    var lateralOffset =
                        (separation -
                         firstNormal *
                         Vector3.Dot(separation, firstNormal)).magnitude;
                    var score =
                        forwardSpan * 4f -
                        lateralOffset +
                        (-normalDot) * 2f +
                        Mathf.Min(first.Width, second.Width) * 0.25f;
                    if (score <= bestScore)
                        continue;

                    bestScore = score;
                    bestFirst = firstIndex;
                    bestSecond = secondIndex;
                }
            }

            if (bestFirst < 0 || bestSecond < 0)
                return false;

            var firstWall = automaticCandidates[bestFirst];
            var secondWall = automaticCandidates[bestSecond];
            if (ViewAlignment(firstWall.Center) >=
                ViewAlignment(secondWall.Center))
            {
                entry = firstWall;
                exit = secondWall;
            }
            else
            {
                entry = secondWall;
                exit = firstWall;
            }

            return true;
        }

        private void ClearAutomaticSetup()
        {
            sessionConfirmed = false;
            ClearRoomSnapshot();
            wallProvider.ClearEntrySelection();
            wallProvider.ClearExitSelection();
            showcaseLayout.ClearHeroCapture();
        }

        private void CaptureAutomaticCandidates()
        {
            automaticCandidates.Clear();
            for (var i = 0; i < wallProvider.CandidateCount; i++)
            {
                if (wallProvider.TryGetCandidate(i, out var candidate) &&
                    candidate.IsAvailable)
                {
                    automaticCandidates.Add(candidate);
                }
            }
        }

        private bool TryCaptureRoomSnapshot()
        {
            ClearRoomSnapshot();
            if (!ConfirmedEntryId.HasValue ||
                !ConfirmedExitId.HasValue ||
                !wallProvider.TryGetCandidateFrameById(
                    ConfirmedEntryId.Value,
                    out var entryFrame) ||
                !wallProvider.TryGetCandidateFrameById(
                    ConfirmedExitId.Value,
                    out var exitFrame))
            {
                return false;
            }

            if (!showcaseLayout.TrySetFrozenWallFrames(
                    entryFrame,
                    exitFrame))
            {
                ClearRoomSnapshot();
                return false;
            }

            return true;
        }

        private void ClearRoomSnapshot()
        {
            showcaseLayout?.ClearFrozenWallFrames();
        }

        private static Vector3 ResolvePairTravel(
            WallCandidateInfo entry,
            WallCandidateInfo exit)
        {
            var travel = Flat(entry.InwardNormal - exit.InwardNormal);
            if (travel.sqrMagnitude < 0.0001f)
                travel = Flat(exit.Center - entry.Center);
            return travel.normalized;
        }

        private static Vector3 Flat(Vector3 value)
        {
            value.y = 0f;
            return value;
        }

        private static float ViewAlignment(Vector3 worldPosition)
        {
            var camera = Camera.main != null
                ? Camera.main.transform
                : null;
            if (camera == null)
                return 0f;

            var forward = Flat(camera.forward);
            var direction = Flat(worldPosition - camera.position);
            if (forward.sqrMagnitude < 0.0001f ||
                direction.sqrMagnitude < 0.0001f)
            {
                return 0f;
            }

            return Vector3.Dot(
                forward.normalized,
                direction.normalized);
        }

        private bool HandleReacquisition()
        {
            var reacquiring =
                wallProvider.EntrySelectionState ==
                    WallSelectionState.Reacquiring ||
                wallProvider.ExitSelectionState ==
                    WallSelectionState.Reacquiring;

            if (reacquiring)
            {
                if (reacquisitionStartedAt < 0f)
                    reacquisitionStartedAt = Time.unscaledTime;

                if (Time.unscaledTime - reacquisitionStartedAt <
                    reacquisitionUiDelay)
                {
                    return true;
                }

                if (currentSetupState !=
                    RoomShowcaseSetupState.TemporarilyReacquiring)
                {
                    stateBeforeReacquisition = currentSetupState;
                    SetState(
                        RoomShowcaseSetupState.TemporarilyReacquiring,
                        "Reacquiring the selected room wall.");
                }

                return true;
            }

            reacquisitionStartedAt = -1f;
            if (currentSetupState !=
                RoomShowcaseSetupState.TemporarilyReacquiring)
            {
                return false;
            }

            if (wallProvider.EntrySelectionState == WallSelectionState.None)
            {
                sessionConfirmed = false;
                SetState(
                    RoomShowcaseSetupState.SelectEntry,
                    "Entry wall was lost. Select it again.");
                SelectFirstAvailableCandidate(1);
                return true;
            }

            if (wallProvider.ExitSelectionState == WallSelectionState.None)
            {
                sessionConfirmed = false;
                SetState(
                    RoomShowcaseSetupState.SelectExit,
                    "Exit wall was lost. Select it again.");
                SelectFirstAvailableCandidate(1);
                return true;
            }

            if (stateBeforeReacquisition ==
                RoomShowcaseSetupState.Ready)
            {
                showcaseLayout.RebuildLayout();
                showcasePath.RebuildPath();
                if (!showcaseLayout.IsLayoutValid ||
                    !showcasePath.IsPathValid)
                {
                    return true;
                }
            }

            var restoredMessage =
                stateBeforeReacquisition ==
                    RoomShowcaseSetupState.Ready
                    ? "Room setup is complete. Event replay is now available."
                    : "The selected wall was reacquired.";
            SetState(stateBeforeReacquisition, restoredMessage);
            return true;
        }

        private void RefreshPreviewAfterCandidateChange(
            bool membershipChanged = false)
        {
            if (!IsWallSelectionState())
            {
                ClearPreview();
                return;
            }

            if (previewCandidateId.HasValue &&
                wallProvider.TryGetCandidateById(
                    previewCandidateId.Value,
                    out var retained) &&
                !IsExcluded(retained.Id))
            {
                var displayIndexChanged =
                    previewCandidateDisplayIndex != retained.DisplayIndex;
                previewCandidateDisplayIndex = retained.DisplayIndex;
                previewCandidateUserFacingNumber =
                    retained.UserFacingNumber;
                wallProvider.TrySetPreviewById(retained.Id);
                if (membershipChanged || displayIndexChanged)
                    RefreshView();
                return;
            }

            var hadPreview = previewCandidateId.HasValue;
            ClearPreview();
            SelectFirstAvailableCandidate(1);
            if (hadPreview)
                SetUserMessage(
                    "The previewed wall disappeared. Choose another wall.");
        }

        private void CycleCandidate(int direction)
        {
            if (!IsWallSelectionState() || wallProvider.CandidateCount == 0)
                return;

            var startIndex = previewCandidateDisplayIndex;
            if (previewCandidateId.HasValue &&
                wallProvider.TryGetCandidateById(
                    previewCandidateId.Value,
                    out var current))
            {
                startIndex = current.DisplayIndex;
            }

            TrySelectCandidateFrom(startIndex, direction);
        }

        private void SelectFirstAvailableCandidate(int direction)
        {
            TrySelectCandidateFrom(
                direction > 0 ? -1 : wallProvider.CandidateCount,
                direction);
        }

        private bool TrySelectCandidateFrom(int startIndex, int direction)
        {
            var count = wallProvider.CandidateCount;
            for (var offset = 1; offset <= count; offset++)
            {
                var index = Wrap(startIndex + direction * offset, count);
                if (!wallProvider.TryGetCandidate(index, out var info) ||
                    !info.IsAvailable ||
                    IsExcluded(info.Id))
                {
                    continue;
                }

                previewCandidateId = info.Id;
                previewCandidateDisplayIndex = info.DisplayIndex;
                previewCandidateUserFacingNumber =
                    info.UserFacingNumber;
                wallProvider.TrySetPreviewById(info.Id);
                SetUserMessage(
                    $"Previewing wall {info.UserFacingNumber} of " +
                    $"{wallProvider.UserFacingCandidateCount}.");
                return true;
            }

            ClearPreview();
            SetUserMessage("No different wall candidate is available.");
            return false;
        }

        private bool IsExcluded(TrackableId candidateId)
        {
            if (currentSetupState == RoomShowcaseSetupState.SelectEntry)
            {
                return ConfirmedExitId.HasValue &&
                    ConfirmedExitId.Value == candidateId;
            }

            if (currentSetupState == RoomShowcaseSetupState.SelectExit)
            {
                return ConfirmedEntryId.HasValue &&
                    ConfirmedEntryId.Value == candidateId;
            }

            return false;
        }

        private void ClearPreview()
        {
            if (!previewCandidateId.HasValue &&
                previewCandidateDisplayIndex < 0 &&
                previewCandidateUserFacingNumber < 0)
            {
                wallProvider?.ClearPreview();
                return;
            }

            previewCandidateId = null;
            previewCandidateDisplayIndex = -1;
            previewCandidateUserFacingNumber = -1;
            wallProvider?.ClearPreview();
            RefreshView();
        }

        private void CaptureCandidateMembership(List<TrackableId> target)
        {
            target.Clear();
            if (wallProvider == null)
                return;

            var count = wallProvider.CandidateCount;
            for (var i = 0; i < count; i++)
            {
                if (wallProvider.TryGetCandidate(i, out var info) &&
                    info.IsAvailable)
                {
                    target.Add(info.Id);
                }
            }
        }

        private static bool HasSameCandidateMembership(
            List<TrackableId> first,
            List<TrackableId> second)
        {
            if (first.Count != second.Count)
                return false;

            for (var i = 0; i < first.Count; i++)
            {
                var found = false;
                for (var j = 0; j < second.Count; j++)
                {
                    if (first[i] != second[j])
                        continue;

                    found = true;
                    break;
                }

                if (!found)
                    return false;
            }

            return true;
        }

        private static void CopyCandidateMembership(
            List<TrackableId> source,
            List<TrackableId> destination)
        {
            destination.Clear();
            for (var i = 0; i < source.Count; i++)
                destination.Add(source[i]);
        }

        private bool IsWallSelectionState()
        {
            return currentSetupState ==
                    RoomShowcaseSetupState.SelectEntry ||
                currentSetupState ==
                    RoomShowcaseSetupState.SelectExit;
        }

        private void ReturnToFirstIncompleteStep()
        {
            if (wallProvider == null || wallProvider.CandidateCount == 0)
            {
                SetState(
                    RoomShowcaseSetupState.WaitingForRoom,
                    ResolveRoomLoadingMessage());
                return;
            }

            if (wallProvider.EntrySelectionState !=
                WallSelectionState.Selected)
            {
                SetState(
                    RoomShowcaseSetupState.SelectEntry,
                    "Select the Entry wall.");
                SelectFirstAvailableCandidate(1);
                return;
            }

            if (wallProvider.ExitSelectionState !=
                WallSelectionState.Selected)
            {
                SetState(
                    RoomShowcaseSetupState.SelectExit,
                    "Select the Exit wall.");
                SelectFirstAvailableCandidate(1);
                return;
            }

            if (!showcaseLayout.HeroPoseValid)
            {
                SetState(
                    RoomShowcaseSetupState.CaptureHero,
                    "Face the travel direction, then capture Hero.");
                return;
            }

            SetState(
                RoomShowcaseSetupState.Review,
                "Review the room path.");
        }

        private void SetState(
            RoomShowcaseSetupState state,
            string message)
        {
            if (currentSetupState == RoomShowcaseSetupState.Error &&
                state == RoomShowcaseSetupState.Error &&
                lastUserMessage == message)
            {
                return;
            }

            currentSetupState = state;
            lastUserMessage = message;
            SetSceneTrackingSuspended(
                suspendSceneTrackingWhenReady &&
                state == RoomShowcaseSetupState.Ready &&
                sessionConfirmed &&
                showcaseLayout != null &&
                showcaseLayout.WallFramesFrozen);
            ApplyDebugVisibility();
            RefreshView();
            setupView?.SetVisible(
                state != RoomShowcaseSetupState.Ready);
        }

        private void SetSceneTrackingSuspended(bool suspended)
        {
            if (sceneTrackingSuspended == suspended)
                return;

            if (suspended)
            {
                ResolveSceneManagers();
                suspendedPlaneVisualizers.Clear();
                ARPlaneMeshVisualizer[] visualizers =
                    FindObjectsByType<ARPlaneMeshVisualizer>(
                        FindObjectsInactive.Include);
                for (int i = 0; i < visualizers.Length; i++)
                {
                    ARPlaneMeshVisualizer visualizer = visualizers[i];
                    if (visualizer == null || !visualizer.enabled)
                        continue;

                    suspendedPlaneVisualizers.Add(visualizer);
                    visualizer.enabled = false;
                }

                planeManagerWasEnabled =
                    wallDiscovery != null &&
                    !wallDiscovery.UsesMetaSceneApi &&
                    planeManager != null &&
                    planeManager.enabled;
                raycastManagerWasEnabled =
                    wallDiscovery != null &&
                    !wallDiscovery.UsesMetaSceneApi &&
                    raycastManager != null &&
                    raycastManager.enabled;
                if (planeManagerWasEnabled)
                    planeManager.enabled = false;
                if (raycastManagerWasEnabled)
                    raycastManager.enabled = false;

                sceneTrackingSuspended = true;
                return;
            }

            if (planeManager != null && planeManagerWasEnabled)
                planeManager.enabled = true;
            if (raycastManager != null && raycastManagerWasEnabled)
                raycastManager.enabled = true;

            for (int i = 0;
                 i < suspendedPlaneVisualizers.Count;
                 i++)
            {
                ARPlaneMeshVisualizer visualizer =
                    suspendedPlaneVisualizers[i];
                if (visualizer != null)
                    visualizer.enabled = true;
            }

            suspendedPlaneVisualizers.Clear();
            planeManagerWasEnabled = false;
            raycastManagerWasEnabled = false;
            sceneTrackingSuspended = false;
        }

        private void ResolveSceneManagers()
        {
            if (planeManager == null)
            {
                planeManager =
                    FindAnyObjectByType<ARPlaneManager>(
                        FindObjectsInactive.Include);
            }

            if (raycastManager == null)
            {
                raycastManager =
                    FindAnyObjectByType<ARRaycastManager>(
                        FindObjectsInactive.Include);
            }
        }

        private void SetUserMessage(string message)
        {
            if (lastUserMessage == message)
                return;

            lastUserMessage = message;
            RefreshView();
        }

        private void ApplyDebugVisibility()
        {
            var selecting =
                currentSetupState == RoomShowcaseSetupState.SelectEntry ||
                currentSetupState == RoomShowcaseSetupState.SelectExit ||
                currentSetupState == RoomShowcaseSetupState.CaptureHero;
            var reviewing =
                currentSetupState == RoomShowcaseSetupState.Review ||
                currentSetupState ==
                    RoomShowcaseSetupState.TemporarilyReacquiring;

            wallDiscovery?.SetDebugVisible(selecting);
            showcaseLayout?.SetDebugVisible(selecting || reviewing);
            showcasePath?.SetDebugVisible(reviewing);
        }

        private void CloseEventReplay()
        {
            var eventReplay =
                Object.FindAnyObjectByType<EventPopoutReplay>(
                    FindObjectsInactive.Include);
            eventReplay?.Close();
        }

        private void RefreshView()
        {
            setupView?.Refresh(
                new RoomShowcaseSetupPresentation(
                    currentSetupState,
                    lastUserMessage,
                    previewCandidateUserFacingNumber,
                    wallProvider?.UserFacingCandidateCount ?? 0));
        }

        private static int Wrap(int value, int count)
        {
            var result = value % count;
            return result < 0 ? result + count : result;
        }

    }
}
