using System;
using F1XR.RestAPI.Replay.Track.Placement;
using F1XR.RestAPI.Api;
using F1XR.RestAPI.Replay.Track.Build;
using F1XR.RestAPI.Replay;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace F1XR.RaceFlags
{
    [DisallowMultipleComponent]
    public sealed class RaceControlFlagPresenter : MonoBehaviour
    {
        private const string AnchorName = "RaceFlagAnchor";

        [Header("References")]
        [SerializeField] private ReplayPlayer replayPlayer;
        [SerializeField] private TrackRevealPlacer buildPlacer;
        [SerializeField] private ARPlanePlacementController placement;
        [SerializeField] private Transform mapRootOverride;
        [SerializeField] private Transform raceFlagAnchor;
        [SerializeField] private RaceFlagAlert raceFlagAlert;
        [SerializeField] private GameObject raceFlagPrefab;

        [Header("Status Logos")]
        [SerializeField] private GameObject yellowFlagLogoPrefab;
        [SerializeField] private GameObject redFlagLogoPrefab;
        [SerializeField] private Vector3 logoPivotLocalPosition = new Vector3(0.0f, -0.35f, 0.0f);
        [SerializeField] private Vector3 yellowFlagLogoLocalPosition = new Vector3(0.381f, 0.0f, 0.0f);
        [SerializeField] private Vector3 redFlagLogoLocalPosition = new Vector3(0.257f, 0.0f, 0.0f);
        [SerializeField] private Vector3 logoLocalScale = new Vector3(0.1f, 0.1f, 0.1f);
        [Min(0.0f)] [SerializeField] private float logoRotationDegreesPerSecond = 12.0f;

        [Header("Placement")]
        [SerializeField] private Vector3 anchorLocalPosition = new Vector3(0.0f, 0.5f, 0.0f);
        [SerializeField] private Vector3 flagLocalPosition = new Vector3(0.0f, -0.45f, 0.0f);

        [Header("Timing")]
        [SerializeField] private float checkeredDisplayDuration = 5.0f;
        [SerializeField] private float fallbackRaceEndTimeForTesting = 0.0f;
        [SerializeField] private float missingEndFallbackDuration = 5.0f;

        [Header("Celebration Events")]
        [SerializeField] private UnityEvent checkeredFlagShown;

        [Header("Development Test")]
        [SerializeField] private bool enableDevelopmentControls = false;
        [SerializeField] private bool forceRaceFinishedForTesting = false;
        [SerializeField] private bool forceYellowForTesting = false;
        [SerializeField] private bool forceRedForTesting = false;

        [Header("Runtime Debug")]
        [SerializeField] private bool showRuntimeDebug = true;
        [SerializeField] private bool hasMapRoot;
        [SerializeField] private bool hasAnchor;
        [SerializeField] private bool hasFlagInstance;
        [SerializeField] private float currentReplayTime;
        [SerializeField] private float effectiveReplayTime;
        [SerializeField] private float debugRaceEndTime;
        [SerializeField] private int yellowFlagCount;
        [SerializeField] private int redFlagCount;
        [SerializeField] private RaceFlagRuntimeState evaluatedState = RaceFlagRuntimeState.Hidden;
        [SerializeField] private float activeEventStartT;
        [SerializeField] private float activeEventEndT;

        private RaceFlagRuntimeState previousState = RaceFlagRuntimeState.Hidden;
        private Transform lastMapRoot;
        private bool hasRaceControlStartGate;
        private float raceControlStartGateT;
        private bool warnedMissingPlayer;
        private bool warnedMissingFlag;
        private bool incidentYellowOverride;
        private Transform incidentPresentationRoot;
        private GameObject yellowFlagLogoInstance;
        private GameObject redFlagLogoInstance;
        private Transform yellowFlagLogoPivot;
        private Transform redFlagLogoPivot;

        public event Action CheckeredFlagShown;
        public bool IncidentYellowOverrideActive =>
            incidentYellowOverride;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnablePresentersAfterSceneLoad()
        {
            EnablePresentersInScene(SceneManager.GetActiveScene());

            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnablePresentersInScene(scene);
        }

        private static void EnablePresentersInScene(Scene scene)
        {
            if (!scene.IsValid())
                return;

            RaceControlFlagPresenter[] presenters = UnityEngine.Object.FindObjectsByType<RaceControlFlagPresenter>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < presenters.Length; i++)
            {
                RaceControlFlagPresenter presenter = presenters[i];
                if (presenter == null || presenter.gameObject.scene != scene)
                    continue;

                if (!presenter.gameObject.activeSelf)
                    presenter.gameObject.SetActive(true);

                if (!presenter.enabled)
                    presenter.enabled = true;
            }
        }

        private void Awake()
        {
            if (missingEndFallbackDuration <= 0.0f)
                missingEndFallbackDuration = 5.0f;

            ResolveInitialReferences();
            EnsureAnchorAndFlag();
            ApplyStateTransition(RaceFlagRuntimeState.Hidden, RaceFlagRuntimeState.Hidden);
        }

        private void OnEnable()
        {
            previousState = RaceFlagRuntimeState.Hidden;
            EnsureAnchorAndFlag();
            EvaluateAndApply();
        }

        private void OnDisable()
        {
            if (raceFlagAlert != null)
                raceFlagAlert.HideImmediately();

            SetStatusLogoVisible(yellowFlagLogoInstance, yellowFlagLogoPivot, false);
            SetStatusLogoVisible(redFlagLogoInstance, redFlagLogoPivot, false);

            incidentYellowOverride = false;
            incidentPresentationRoot = null;
            previousState = RaceFlagRuntimeState.Hidden;
        }

        private void Update()
        {
            EnsureAnchorAndFlag();
            EvaluateAndApply();
            RotateVisibleStatusLogos();
        }

        public void SimulateYellowFlag()
        {
            if (!Application.isPlaying)
                return;

            enableDevelopmentControls = true;
            forceRaceFinishedForTesting = false;
            forceRedForTesting = false;
            forceYellowForTesting = true;
            EvaluateAndApply(forceTransition: true);
        }

        public void SimulateRedFlag()
        {
            if (!Application.isPlaying)
                return;

            enableDevelopmentControls = true;
            forceRaceFinishedForTesting = false;
            forceYellowForTesting = false;
            forceRedForTesting = true;
            EvaluateAndApply(forceTransition: true);
        }

        public void SimulateRaceFinish()
        {
            if (!Application.isPlaying)
                return;

            enableDevelopmentControls = true;
            forceYellowForTesting = false;
            forceRedForTesting = false;
            forceRaceFinishedForTesting = true;
            EvaluateAndApply(forceTransition: true);
        }

        public void ClearTestOverrides()
        {
            forceRaceFinishedForTesting = false;
            forceYellowForTesting = false;
            forceRedForTesting = false;
            EvaluateAndApply(forceTransition: true);
        }

        public void SetIncidentYellowOverride(bool active)
        {
            if (incidentYellowOverride == active)
                return;

            incidentYellowOverride = active;
            EvaluateAndApply(forceTransition: true);
        }

        public void SetIncidentPresentationRoot(Transform root)
        {
            if (incidentPresentationRoot == root)
                return;

            incidentPresentationRoot = root;
            EnsureAnchorAndFlag();
            EvaluateAndApply(forceTransition: true);
        }

        public void ReevaluateCurrentReplayTime()
        {
            EvaluateAndApply(forceTransition: true);
        }

        private void ResolveInitialReferences()
        {
            if (replayPlayer == null)
                replayPlayer = FindAnyObjectByType<ReplayPlayer>();

            if (replayPlayer != null)
            {
                if (buildPlacer == null)
                    buildPlacer = replayPlayer.buildPlacer;

                if (placement == null)
                    placement = replayPlayer.placement;
            }

            if (buildPlacer == null)
                buildPlacer = FindAnyObjectByType<TrackRevealPlacer>();

            if (placement == null)
                placement = FindAnyObjectByType<ARPlanePlacementController>();
        }

        private void EnsureAnchorAndFlag()
        {
            bool hadFlagInstance = raceFlagAlert != null;
            Transform mapRoot = ResolveMapRoot();
            hasMapRoot = mapRoot != null;
            if (mapRoot == null)
                return;

            if (raceFlagAnchor == null || raceFlagAnchor.parent != mapRoot || lastMapRoot != mapRoot)
            {
                raceFlagAnchor = FindOrCreateAnchor(mapRoot);
                lastMapRoot = mapRoot;
            }

            raceFlagAnchor.localPosition = anchorLocalPosition;
            raceFlagAnchor.localRotation = Quaternion.identity;
            raceFlagAnchor.localScale = Vector3.one;
            hasAnchor = raceFlagAnchor != null;

            if (raceFlagAlert == null)
                raceFlagAlert = raceFlagAnchor.GetComponentInChildren<RaceFlagAlert>(true);

            if (raceFlagAlert == null && raceFlagPrefab != null)
            {
                GameObject instance = Instantiate(raceFlagPrefab, raceFlagAnchor);
                instance.name = raceFlagPrefab.name;
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;
                raceFlagAlert = instance.GetComponent<RaceFlagAlert>();
            }

            if (raceFlagAlert != null && raceFlagAlert.transform.parent != raceFlagAnchor)
            {
                raceFlagAlert.transform.SetParent(raceFlagAnchor, worldPositionStays: false);
            }

            if (raceFlagAlert != null)
            {
                raceFlagAlert.transform.localPosition = flagLocalPosition;
                raceFlagAlert.transform.localRotation = Quaternion.identity;
                raceFlagAlert.transform.localScale = Vector3.one;
            }

            EnsureStatusLogo(ref yellowFlagLogoInstance, ref yellowFlagLogoPivot, yellowFlagLogoPrefab, yellowFlagLogoLocalPosition);
            EnsureStatusLogo(ref redFlagLogoInstance, ref redFlagLogoPivot, redFlagLogoPrefab, redFlagLogoLocalPosition);

            hasFlagInstance = raceFlagAlert != null;

            if (!hadFlagInstance && hasFlagInstance)
            {
                raceControlStartGateT = replayPlayer != null ? replayPlayer.CurrentTime : 0.0f;
                hasRaceControlStartGate = true;
                previousState = RaceFlagRuntimeState.Hidden;
                raceFlagAlert.HideImmediately();
            }
        }

        private Transform ResolveMapRoot()
        {
            if (incidentPresentationRoot != null)
                return incidentPresentationRoot;

            if (mapRootOverride != null && mapRootOverride != transform && !IsFlagTransform(mapRootOverride))
                return mapRootOverride;

            if (buildPlacer != null && buildPlacer.HasPlacement)
                return buildPlacer.PlacementTransform;

            if (placement != null && placement.HasPlacement)
                return placement.PlacementTransform;

            return null;
        }

        private bool IsFlagTransform(Transform candidate)
        {
            if (candidate == null)
                return false;

            if (raceFlagAlert != null && (candidate == raceFlagAlert.transform || candidate.IsChildOf(raceFlagAlert.transform)))
                return true;

            return candidate.GetComponentInParent<RaceFlagAlert>() != null;
        }

        private Transform FindOrCreateAnchor(Transform mapRoot)
        {
            Transform existing = mapRoot.Find(AnchorName);
            if (existing != null)
                return existing;

            GameObject anchorObject = new GameObject(AnchorName);
            anchorObject.transform.SetParent(mapRoot, worldPositionStays: false);
            return anchorObject.transform;
        }

        private void EnsureStatusLogo(
            ref GameObject instance,
            ref Transform pivot,
            GameObject prefab,
            Vector3 localPosition)
        {
            if (prefab == null || raceFlagAnchor == null)
                return;

            if (pivot == null)
            {
                GameObject pivotObject = new GameObject(prefab.name + "Pivot");
                pivotObject.transform.SetParent(raceFlagAnchor, worldPositionStays: false);
                pivot = pivotObject.transform;
            }
            else if (pivot.parent != raceFlagAnchor)
            {
                pivot.SetParent(raceFlagAnchor, worldPositionStays: false);
            }

            pivot.localPosition = logoPivotLocalPosition;

            if (instance == null)
            {
                instance = Instantiate(prefab, pivot);
                instance.name = prefab.name;
                instance.SetActive(false);
            }
            else if (instance.transform.parent != pivot)
            {
                instance.transform.SetParent(pivot, worldPositionStays: false);
            }

            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = logoLocalScale;
        }

        private void RotateVisibleStatusLogos()
        {
            if (logoRotationDegreesPerSecond <= 0.0f)
                return;

            RotateStatusLogo(yellowFlagLogoInstance, yellowFlagLogoPivot);
            RotateStatusLogo(redFlagLogoInstance, redFlagLogoPivot);
        }

        private void RotateStatusLogo(GameObject logo, Transform pivot)
        {
            if (logo == null || !logo.activeSelf || pivot == null)
                return;

            pivot.Rotate(0.0f, logoRotationDegreesPerSecond * Time.deltaTime, 0.0f, Space.Self);
        }

        private void EvaluateAndApply(bool forceTransition = false)
        {
            if (replayPlayer == null)
            {
                WarnOnce(ref warnedMissingPlayer, "RaceControlFlagPresenter needs a ApiClient reference.");
                return;
            }

            if (raceFlagAlert == null)
            {
                WarnOnce(ref warnedMissingFlag, "RaceControlFlagPresenter needs a RaceFlagAlert or raceFlagPrefab reference.");
                return;
            }

            float raceEndTime = replayPlayer.RaceEndTime;
            bool hasValidRaceEndTime = raceEndTime > 0.0f;
            currentReplayTime = replayPlayer.CurrentTime;
            float effectiveT = enableDevelopmentControls && forceRaceFinishedForTesting
                ? hasValidRaceEndTime ? raceEndTime : fallbackRaceEndTimeForTesting
                : currentReplayTime;

            if (hasRaceControlStartGate && effectiveT < raceControlStartGateT - 0.05f)
                hasRaceControlStartGate = false;

            effectiveReplayTime = effectiveT;
            debugRaceEndTime = raceEndTime;
            yellowFlagCount = replayPlayer.YellowFlags != null ? replayPlayer.YellowFlags.Length : 0;
            redFlagCount = replayPlayer.RedFlags != null ? replayPlayer.RedFlags.Length : 0;
            activeEventStartT = 0.0f;
            activeEventEndT = 0.0f;

            RaceFlagRuntimeState newState = EvaluateState(effectiveT, raceEndTime, hasValidRaceEndTime);
            evaluatedState = newState;
            if (!forceTransition && newState == previousState)
                return;

            RaceFlagRuntimeState oldState = previousState;
            previousState = newState;
            ApplyStateTransition(oldState, newState);
        }

        private RaceFlagRuntimeState EvaluateState(float effectiveT, float raceEndTime, bool hasValidRaceEndTime)
        {
            if (enableDevelopmentControls && forceRaceFinishedForTesting)
                return RaceFlagRuntimeState.Checkered;

            if (enableDevelopmentControls && forceRedForTesting)
                return RaceFlagRuntimeState.Red;

            if (enableDevelopmentControls && forceYellowForTesting)
                return RaceFlagRuntimeState.Yellow;

            if (hasValidRaceEndTime && effectiveT >= raceEndTime)
                return RaceFlagRuntimeState.Checkered;

            if (IsRaceControlActive(replayPlayer.RedFlags, effectiveT, raceEndTime, hasValidRaceEndTime))
                return RaceFlagRuntimeState.Red;

            if (incidentYellowOverride)
                return RaceFlagRuntimeState.Yellow;

            if (IsRaceControlActive(replayPlayer.YellowFlags, effectiveT, raceEndTime, hasValidRaceEndTime))
                return RaceFlagRuntimeState.Yellow;

            return RaceFlagRuntimeState.Hidden;
        }

        private bool IsRaceControlActive(RaceControlEventDto[] events, float effectiveT, float raceEndTime, bool hasValidRaceEndTime)
        {
            if (events == null)
                return false;

            for (int i = 0; i < events.Length; i++)
            {
                RaceControlEventDto raceEvent = events[i];
                if (raceEvent == null)
                    continue;

                float startT = raceEvent.startT > 0.0f ? raceEvent.startT : raceEvent.t;
                if (startT <= 0.0f)
                    continue;

                if (hasRaceControlStartGate && startT <= raceControlStartGateT)
                    continue;

                float endT = raceEvent.endT;
                if (endT <= startT)
                {
                    float fallbackDuration = missingEndFallbackDuration > 0.0f ? missingEndFallbackDuration : 5.0f;
                    if (fallbackDuration > 0.0f)
                        endT = startT + fallbackDuration;
                    else if (hasValidRaceEndTime)
                        endT = raceEndTime;
                    else
                        endT = float.PositiveInfinity;
                }

                if (startT <= effectiveT && effectiveT < endT)
                {
                    activeEventStartT = startT;
                    activeEventEndT = endT;
                    return true;
                }
            }

            return false;
        }

        private void ApplyStateTransition(RaceFlagRuntimeState oldState, RaceFlagRuntimeState newState)
        {
            if (raceFlagAlert == null)
                return;

            SetStatusLogoVisible(yellowFlagLogoInstance, yellowFlagLogoPivot, newState == RaceFlagRuntimeState.Yellow);
            SetStatusLogoVisible(redFlagLogoInstance, redFlagLogoPivot, newState == RaceFlagRuntimeState.Red);

            switch (newState)
            {
                case RaceFlagRuntimeState.Checkered:
                    HandleRaceFinished();
                    break;
                case RaceFlagRuntimeState.Red:
                    raceFlagAlert.ShowPersistent(RaceFlagType.Red);
                    break;
                case RaceFlagRuntimeState.Yellow:
                    raceFlagAlert.ShowPersistent(RaceFlagType.Yellow);
                    break;
                default:
                    if (oldState == RaceFlagRuntimeState.Hidden)
                        raceFlagAlert.HideImmediately();
                    else
                        raceFlagAlert.HideWithExit();
                    break;
            }
        }

        private static void SetStatusLogoVisible(GameObject logo, Transform pivot, bool visible)
        {
            if (logo == null || logo.activeSelf == visible)
                return;

            if (visible && pivot != null)
                pivot.localRotation = Quaternion.identity;

            logo.SetActive(visible);
        }

        private void HandleRaceFinished()
        {
            raceFlagAlert.ShowTimed(RaceFlagType.Checkered, checkeredDisplayDuration);
            CheckeredFlagShown?.Invoke();
            checkeredFlagShown?.Invoke();
        }

        private static void WarnOnce(ref bool warned, string message)
        {
            if (warned)
                return;

            Debug.LogWarning(message);
            warned = true;
        }
    }
}
