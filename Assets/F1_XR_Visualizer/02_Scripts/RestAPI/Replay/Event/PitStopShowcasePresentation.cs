using System.Collections.Generic;
using F1XR.RestAPI.Api;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace F1XR.RestAPI.Replay
{
    public sealed class PitStopShowcasePresentation
    {
        private const float TimerUpdatesPerSecond = 20f;
        private const float StopCueDurationSeconds = 0.7f;
        private const float LaunchCueDelaySeconds = 0.08f;

        private readonly List<Material> materials = new();
        private readonly List<Mesh> meshes = new();
        private readonly List<Texture2D> textures = new();
        private readonly Dictionary<Color32, Material> materialCache = new();
        private readonly Dictionary<PrimitiveType, Mesh> primitiveMeshes =
            new();
        private readonly List<Transform> crew = new();
        private readonly List<Vector3> crewWaitingPositions = new();
        private readonly List<Vector3> crewServicePositions = new();
        private readonly List<GameObject> wheelGuns = new();
        private GameObject root;
        private TextMeshPro phaseText;
        private TextMeshPro timerText;
        private AudioSource wheelGunAudio;
        private AudioSource transitionAudio;
        private AudioClip suspensionSettleClip;
        private AudioClip serviceClunkClip;
        private AudioClip launchClip;
        private PitStopSequence sequence;
        private PitStopFirstMilestoneChoreography firstMilestoneChoreography;
        private Color teamColor;
        private float carLength;
        private float lastReplayTime = float.NaN;
        private float stopCueEndTime = float.NaN;
        private int lastTimelineRevision = int.MinValue;
        private PitStopPhase displayedPhase;
        private int displayedTimerTick = int.MinValue;
        private bool displayedReconstructed;
        private bool displayedDriveThrough;
        private bool displayedReleaseReady;
        private bool displayCacheValid;
#if UNITY_EDITOR
        private float firstMilestoneCalibrationTime = float.NaN;
#endif

        private readonly struct PitBoxSpec
        {
            public PitBoxSpec(Vector3 position, Vector3 scale)
            {
                Position = position;
                Scale = scale;
            }

            public Vector3 Position { get; }
            public Vector3 Scale { get; }
        }

        public Bounds LocalBounds { get; private set; }

        public void Build(
            Transform parent,
            ReplayCarView vehicle,
            Vector3 localFocus,
            float localVehicleLength,
            DriverInfoDto driver,
            ReplayEventDto replayEvent,
            PitStopSequence pitSequence,
            GameObject wheelGunPrefab,
            AudioClip wheelGunClip,
            PitEnvironmentProfile environmentProfile,
            PitShowcaseAssetProfile assetProfile = null)
        {
            Clear();
            if (parent == null || pitSequence == null)
                return;

            sequence = pitSequence;
            carLength = Mathf.Max(0.04f, localVehicleLength);
            teamColor = ResolveTeamColor(driver);
            wheelGunPrefab = assetProfile != null &&
                             assetProfile.WheelGunPrefab != null
                ? assetProfile.WheelGunPrefab
                : wheelGunPrefab;
            wheelGunClip = assetProfile != null &&
                           assetProfile.WheelGunLoopClip != null
                ? assetProfile.WheelGunLoopClip
                : wheelGunClip;
            suspensionSettleClip = assetProfile != null
                ? assetProfile.SuspensionSettleClip
                : null;
            serviceClunkClip = assetProfile != null
                ? assetProfile.ServiceClunkClip
                : null;
            launchClip = assetProfile != null
                ? assetProfile.LaunchClip
                : null;
            Color floor = new(0.075f, 0.08f, 0.09f, 1f);
            float vehicleGroundOffset = 0f;
            if (vehicle != null &&
                vehicle.TryGetVisualGroundOffset(
                    parent,
                    out float measuredGroundOffset))
            {
                vehicleGroundOffset = measuredGroundOffset;
            }
            Vector3 localGroundFocus =
                localFocus +
                Vector3.up * vehicleGroundOffset;

            root = new GameObject("PitStopTeamBox");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localGroundFocus;

            CreateEnvironmentModules(
                carLength,
                environmentProfile,
                floor);
            PitStopFirstMilestoneChoreography choreography = new();
            if (choreography.TryBuild(
                    root.transform,
                    vehicle,
                    carLength,
                    assetProfile))
            {
                firstMilestoneChoreography = choreography;
            }
            else
            {
                choreography.Clear();
                CreateCrew(carLength, teamColor, wheelGunPrefab);
                Debug.LogWarning(
                    "[PitChoreography] First milestone assets or FL_Tire binding are unavailable; using the existing fallback crew presentation.");
            }
            CreateSign(
                carLength,
                teamColor,
                assetProfile != null
                    ? assetProfile.DisplayFont
                    : null);

            if (wheelGunClip != null)
            {
                wheelGunAudio = root.AddComponent<AudioSource>();
                wheelGunAudio.clip = wheelGunClip;
                wheelGunAudio.loop = true;
                wheelGunAudio.playOnAwake = false;
                wheelGunAudio.spatialBlend = 1f;
                wheelGunAudio.minDistance = 0.8f;
                wheelGunAudio.maxDistance = 12f;
                wheelGunAudio.dopplerLevel = 0f;
                wheelGunAudio.volume = 0.55f;
            }

            if (suspensionSettleClip != null ||
                serviceClunkClip != null ||
                launchClip != null)
            {
                transitionAudio = root.AddComponent<AudioSource>();
                transitionAudio.playOnAwake = false;
                transitionAudio.loop = false;
                transitionAudio.spatialBlend = 0.85f;
                transitionAudio.minDistance = 2f;
                transitionAudio.maxDistance = 24f;
                transitionAudio.dopplerLevel = 0f;
                transitionAudio.volume = 1f;
                suspensionSettleClip?.LoadAudioData();
                serviceClunkClip?.LoadAudioData();
                launchClip?.LoadAudioData();
            }

            LocalBounds = new Bounds(
                localGroundFocus +
                new Vector3(
                    carLength * 0.7f,
                    carLength * 0.55f,
                    0f),
                new Vector3(
                    carLength * 3.5f,
                    carLength * 2.3f,
                    carLength * 15f));
            Apply(pitSequence.StartTime, false);
        }

        public void Apply(
            float replayTime,
            bool playing,
            int timelineRevision = 0)
        {
            if (root == null || sequence == null)
                return;

            Apply(
                sequence.GetPresentationState(replayTime),
                replayTime,
                playing,
                timelineRevision);
        }

        private void Apply(
            PitStopPresentationState state,
            float replayTime,
            bool playing,
            int timelineRevision)
        {
            if (root == null || sequence == null)
                return;

            PitStopPhase phase = state.Phase;
            bool timelineDiscontinuity =
                lastTimelineRevision != int.MinValue &&
                (timelineRevision != lastTimelineRevision ||
                 (!float.IsNaN(lastReplayTime) &&
                  replayTime < lastReplayTime));
            float crewBlend = ResolveCrewBlend(
                replayTime,
                phase);

#if UNITY_EDITOR
            if (float.IsFinite(firstMilestoneCalibrationTime))
            {
                firstMilestoneChoreography?.ApplyChoreographyTime(
                    firstMilestoneCalibrationTime);
            }
            else if (firstMilestoneStaticCalibrationPose)
            {
                firstMilestoneChoreography?
                    .ApplyStaticServiceComposition();
            }
            else
#endif
            {
                firstMilestoneChoreography?.Apply(
                    replayTime,
                    sequence);
            }
            bool releaseReady = firstMilestoneChoreography == null ||
                firstMilestoneChoreography.ReleaseReady;
            float serviceCueTime = firstMilestoneChoreography != null
                ? PitStopFirstMilestoneChoreography.ResolveReplayStart(sequence)
                : sequence.ServiceStartTime;
            float releaseCueTime = firstMilestoneChoreography != null
                ? PitStopFirstMilestoneChoreography.ResolveReplayEnd(sequence)
                : sequence.ServiceEndTime;

            for (int i = 0; i < crew.Count; i++)
            {
                if (crew[i] == null)
                    continue;

                crew[i].localPosition = Vector3.Lerp(
                    crewWaitingPositions[i],
                    crewServicePositions[i],
                    crewBlend);
                Vector3 lookDirection =
                    -crew[i].localPosition;
                lookDirection.y = 0f;
                if (lookDirection.sqrMagnitude > 0.000001f)
                {
                    crew[i].localRotation = Quaternion.LookRotation(
                        lookDirection.normalized,
                        Vector3.up);
                }
            }

            bool servicing =
                !sequence.IsDriveThrough &&
                (firstMilestoneChoreography != null
                    ? replayTime >= serviceCueTime && !releaseReady
                    : phase == PitStopPhase.Service);
            for (int i = 0; i < wheelGuns.Count; i++)
            {
                if (wheelGuns[i] != null)
                    wheelGuns[i].SetActive(servicing);
            }
            UpdateBroadcastDisplay(
                state,
                releaseReady,
                replayTime,
                firstMilestoneChoreography != null);
            UpdateTransitionAudio(
                replayTime,
                playing,
                timelineDiscontinuity,
                serviceCueTime,
                releaseCueTime);

            ApplyAudio(
                replayTime,
                playing,
                servicing,
                timelineDiscontinuity);
            lastReplayTime = replayTime;
            lastTimelineRevision = timelineRevision;
        }

        public void Clear()
        {
            firstMilestoneChoreography?.Clear();
            firstMilestoneChoreography = null;
            if (wheelGunAudio != null)
                wheelGunAudio.Stop();
            if (transitionAudio != null)
                transitionAudio.Stop();
            wheelGunAudio = null;
            transitionAudio = null;
            if (root != null)
                Object.Destroy(root);
            root = null;
            phaseText = null;
            timerText = null;
            suspensionSettleClip = null;
            serviceClunkClip = null;
            launchClip = null;
            sequence = null;
            teamColor = default;
            carLength = 0f;
            lastReplayTime = float.NaN;
            stopCueEndTime = float.NaN;
            lastTimelineRevision = int.MinValue;
            displayedTimerTick = int.MinValue;
            displayedReconstructed = false;
            displayedDriveThrough = false;
            displayedReleaseReady = false;
            displayCacheValid = false;
            crew.Clear();
            crewWaitingPositions.Clear();
            crewServicePositions.Clear();
            wheelGuns.Clear();
#if UNITY_EDITOR
            firstMilestoneCalibrationTime = float.NaN;
#endif
            for (int i = 0; i < materials.Count; i++)
            {
                if (materials[i] != null)
                    Object.Destroy(materials[i]);
            }
            materials.Clear();
            materialCache.Clear();
            for (int i = 0; i < meshes.Count; i++)
            {
                if (meshes[i] != null)
                    Object.Destroy(meshes[i]);
            }
            meshes.Clear();
            for (int i = 0; i < textures.Count; i++)
            {
                if (textures[i] != null)
                    Object.Destroy(textures[i]);
            }
            textures.Clear();
            LocalBounds = default;
        }

        private void CreateCrew(
            float carLength,
            Color teamColor,
            GameObject wheelGunPrefab)
        {
            Vector3[] waiting =
            {
                new(carLength * 1.08f, 0f, carLength * 0.96f),
                new(carLength * 1.08f, 0f, carLength * 0.32f),
                new(carLength * 1.08f, 0f, -carLength * 0.32f),
                new(carLength * 1.08f, 0f, -carLength * 0.96f),
                new(carLength * 0.72f, 0f, carLength * 1.28f),
                new(carLength * 0.72f, 0f, -carLength * 1.28f)
            };
            Vector3[] working =
            {
                new(carLength * 0.48f, 0f, carLength * 0.42f),
                new(carLength * 0.48f, 0f, -carLength * 0.42f),
                new(-carLength * 0.48f, 0f, carLength * 0.42f),
                new(-carLength * 0.48f, 0f, -carLength * 0.42f),
                new(0f, 0f, carLength * 0.78f),
                new(0f, 0f, -carLength * 0.78f)
            };

            for (int i = 0; i < waiting.Length; i++)
            {
                Transform member = new GameObject(
                    $"Crew_{i + 1}").transform;
                member.SetParent(root.transform, false);
                member.localPosition = waiting[i];
                CreateCrewSilhouette(
                    member,
                    carLength,
                    teamColor);
                crew.Add(member);
                crewWaitingPositions.Add(member.localPosition);
                crewServicePositions.Add(working[i]);

                if (i >= 4)
                {
                    GameObject jack = CreateBox(
                        i == 4 ? "FrontJack" : "RearJack",
                        member,
                        new Vector3(
                            0f,
                            carLength * 0.055f,
                            i == 4
                                ? -carLength * 0.12f
                                : carLength * 0.12f),
                        new Vector3(
                            carLength * 0.12f,
                            carLength * 0.035f,
                            carLength * 0.22f),
                        Color.Lerp(teamColor, Color.white, 0.32f));
                    jack.transform.localRotation =
                        Quaternion.Euler(0f, 0f, 0f);
                    continue;
                }

                GameObject gun;
                if (wheelGunPrefab != null)
                {
                    gun = Object.Instantiate(
                        wheelGunPrefab,
                        member);
                    gun.name = $"WheelGun_{i + 1}";
                    gun.transform.localPosition =
                        new Vector3(
                            carLength * 0.1f,
                            carLength * 0.12f,
                            carLength * 0.06f);
                    gun.transform.localRotation =
                        Quaternion.identity;
                    gun.transform.localScale *=
                        carLength * 0.08f;
                    OptimizePresentationRenderers(gun);
                }
                else
                {
                    gun = CreateBox(
                        $"WheelGun_{i + 1}",
                        member,
                        new Vector3(
                            carLength * 0.1f,
                            carLength * 0.12f,
                            carLength * 0.06f),
                        new Vector3(
                            carLength * 0.05f,
                            carLength * 0.035f,
                            carLength * 0.11f),
                        Color.gray);
                }
                gun.SetActive(false);
                wheelGuns.Add(gun);
            }
        }

        private void CreateSign(
            float carLength,
            Color teamColor,
            TMP_FontAsset displayFont)
        {
            phaseText = CreateDisplayText(
                "PitPhaseDisplay",
                new Vector3(
                    carLength * 0.93f,
                    carLength * 0.46f,
                    carLength * 0.66f),
                carLength,
                displayFont,
                TextAlignmentOptions.Center,
                teamColor);
            timerText = CreateDisplayText(
                "PitServiceTimer",
                new Vector3(
                    carLength * 0.93f,
                    carLength * 0.46f,
                    -carLength * 0.66f),
                carLength,
                displayFont,
                TextAlignmentOptions.Center,
                Color.white);
        }

        private TextMeshPro CreateDisplayText(
            string name,
            Vector3 localPosition,
            float vehicleLength,
            TMP_FontAsset displayFont,
            TextAlignmentOptions alignment,
            Color color)
        {
            GameObject display = new GameObject(
                name,
                typeof(TextMeshPro));
            display.transform.SetParent(root.transform, false);
            display.transform.localPosition = localPosition;
            display.transform.localRotation =
                Quaternion.LookRotation(Vector3.right, Vector3.up);
            display.transform.localScale =
                Vector3.one * vehicleLength * 0.045f;
            TextMeshPro text = display.GetComponent<TextMeshPro>();
            if (displayFont != null)
                text.font = displayFont;
            text.text = "READY";
            text.alignment = alignment;
            text.color = color;
            text.fontSize = 5.4f;
            text.enableAutoSizing = false;
            text.rectTransform.sizeDelta = new Vector2(14f, 2.2f);
            text.renderer.shadowCastingMode = ShadowCastingMode.Off;
            text.renderer.receiveShadows = false;
            return text;
        }

        private void UpdateBroadcastDisplay(
            PitStopPresentationState state,
            bool releaseReady,
            float replayTime,
            bool choreographyActive)
        {
            float displayedServiceTotal = choreographyActive
                ? PitStopFirstMilestoneChoreography.ReleaseReadyTime
                : state.ServiceTotalSeconds;
            float choreographyStart = choreographyActive && sequence != null
                ? PitStopFirstMilestoneChoreography.ResolveReplayStart(sequence)
                : 0f;
            float choreographyEnd = choreographyActive && sequence != null
                ? PitStopFirstMilestoneChoreography.ResolveReplayEnd(sequence)
                : 0f;
            float displayedServiceElapsed = choreographyActive &&
                                            sequence != null
                ? Mathf.Clamp(
                    (replayTime - choreographyStart) *
                    (displayedServiceTotal /
                     Mathf.Max(
                         0.05f,
                         choreographyEnd - choreographyStart)),
                    0f,
                    displayedServiceTotal)
                : state.ServiceElapsedSeconds;
            bool serviceTiming = !state.IsDriveThrough &&
                (choreographyActive && sequence != null
                    ? replayTime >= choreographyStart && !releaseReady
                    : state.Phase == PitStopPhase.Service);
            int timerTick = serviceTiming
                ? Mathf.FloorToInt(
                    displayedServiceElapsed *
                    TimerUpdatesPerSecond + 0.0001f)
                : 0;
            bool contentChanged =
                !displayCacheValid ||
                displayedPhase != state.Phase ||
                displayedTimerTick != timerTick ||
                displayedReconstructed != state.IsReconstructed ||
                displayedDriveThrough != state.IsDriveThrough ||
                displayedReleaseReady != releaseReady;

            if (contentChanged && phaseText != null)
            {
                phaseText.text = !state.IsDriveThrough &&
                                 !releaseReady &&
                                 (state.Phase == PitStopPhase.Release ||
                                  state.Phase == PitStopPhase.Exit)
                    ? "HOLD"
                    : state.IsDriveThrough
                    ? state.Phase == PitStopPhase.Exit
                        ? "PIT LANE CLEAR"
                        : "DRIVE THROUGH"
                    : state.Phase switch
                    {
                        PitStopPhase.Approach => "INBOUND",
                        PitStopPhase.Brake => "HIT YOUR MARKS",
                        PitStopPhase.Service => "SERVICE",
                        PitStopPhase.Release => "RELEASE",
                        _ => "PIT STOP COMPLETE"
                    };
                phaseText.color = releaseReady &&
                    (state.Phase == PitStopPhase.Release ||
                     state.Phase == PitStopPhase.Exit)
                    ? new Color(0.22f, 1f, 0.4f, 1f)
                    : teamColor;
            }

            if (contentChanged && timerText == null)
            {
                displayCacheValid = true;
                displayedPhase = state.Phase;
                displayedTimerTick = timerTick;
                displayedReconstructed = state.IsReconstructed;
                displayedDriveThrough = state.IsDriveThrough;
                displayedReleaseReady = releaseReady;
                return;
            }

            if (contentChanged && state.IsDriveThrough)
            {
                timerText.text = state.Phase == PitStopPhase.Exit
                    ? "COMPLETE"
                    : "NO SERVICE";
            }
            else if (contentChanged && state.Phase == PitStopPhase.Approach)
            {
                timerText.text = "BOX READY";
            }
            else if (contentChanged && state.Phase == PitStopPhase.Brake)
            {
                timerText.text = "STOP TARGET";
            }
            else if (contentChanged && serviceTiming)
            {
                timerText.SetText(
                    "{0:0.000} s",
                    timerTick / TimerUpdatesPerSecond);
            }
            else if (contentChanged &&
                     state.IsReconstructed &&
                     !choreographyActive)
            {
                timerText.SetText(
                    "~{0:0.000} s  RECONSTRUCTED",
                    displayedServiceTotal);
            }
            else if (contentChanged)
            {
                timerText.SetText(
                    "{0:0.000} s",
                    displayedServiceTotal);
            }

            if (contentChanged)
            {
                displayCacheValid = true;
                displayedPhase = state.Phase;
                displayedTimerTick = timerTick;
                displayedReconstructed = state.IsReconstructed;
                displayedDriveThrough = state.IsDriveThrough;
                displayedReleaseReady = releaseReady;
            }
        }

#if UNITY_EDITOR
        private bool firstMilestoneStaticCalibrationPose;

        public bool SetFirstMilestoneCalibrationTime(float time)
        {
            if (firstMilestoneChoreography == null)
                return false;

            firstMilestoneStaticCalibrationPose = false;
            firstMilestoneCalibrationTime = Mathf.Clamp(
                time,
                0f,
                PitStopFirstMilestoneChoreography.ReleaseReadyTime);
            return true;
        }

        public bool SetFirstMilestoneStaticCalibrationPose()
        {
            if (firstMilestoneChoreography == null)
                return false;

            firstMilestoneCalibrationTime = float.NaN;
            firstMilestoneStaticCalibrationPose = true;
            return true;
        }

        public void ClearFirstMilestoneCalibrationTime()
        {
            firstMilestoneCalibrationTime = float.NaN;
            firstMilestoneStaticCalibrationPose = false;
        }
#endif

        private void UpdateTransitionAudio(
            float replayTime,
            bool playing,
            bool timelineDiscontinuity,
            float serviceCueTime,
            float releaseCueTime)
        {
            if (transitionAudio == null ||
                sequence == null ||
                sequence.IsDriveThrough)
            {
                return;
            }

            if (!playing || timelineDiscontinuity)
            {
                transitionAudio.Stop();
                stopCueEndTime = float.NaN;
                return;
            }

            if (float.IsNaN(lastReplayTime))
                return;

            if (CrossedForward(
                    lastReplayTime,
                    replayTime,
                    serviceCueTime))
            {
                transitionAudio.Stop();
                stopCueEndTime = serviceCueTime +
                    StopCueDurationSeconds;
                PlayLoadedOneShot(
                    transitionAudio,
                    suspensionSettleClip,
                    0.48f);
            }

            if (!float.IsNaN(stopCueEndTime) &&
                CrossedForward(
                    lastReplayTime,
                    replayTime,
                    stopCueEndTime))
            {
                transitionAudio.Stop();
                stopCueEndTime = float.NaN;
            }

            if (CrossedForward(
                    lastReplayTime,
                    replayTime,
                    releaseCueTime))
            {
                transitionAudio.Stop();
                stopCueEndTime = float.NaN;
                PlayLoadedOneShot(
                    transitionAudio,
                    serviceClunkClip,
                    0.65f);
            }

            float launchDelay = Mathf.Min(
                LaunchCueDelaySeconds,
                Mathf.Max(
                    0f,
                    sequence.EndTime - releaseCueTime) * 0.5f);
            float launchTime = releaseCueTime + launchDelay;
            if (CrossedForward(
                    lastReplayTime,
                    replayTime,
                    launchTime))
            {
                PlayLoadedOneShot(
                    transitionAudio,
                    launchClip,
                    0.25f);
            }
        }

        private static bool CrossedForward(
            float previousTime,
            float replayTime,
            float boundaryTime)
        {
            return previousTime < boundaryTime &&
                   replayTime >= boundaryTime;
        }

        private static void PlayLoadedOneShot(
            AudioSource source,
            AudioClip clip,
            float volumeScale)
        {
            if (source == null ||
                clip == null ||
                clip.loadState != AudioDataLoadState.Loaded)
            {
                return;
            }

            source.PlayOneShot(
                clip,
                Mathf.Clamp01(volumeScale));
        }

        private static void DisablePresentationPhysics(
            GameObject instance)
        {
            if (instance == null)
                return;

            Collider[] colliders =
                instance.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
                Object.Destroy(colliders[i]);
            Rigidbody[] bodies =
                instance.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < bodies.Length; i++)
                Object.Destroy(bodies[i]);
        }

        private static void OptimizePresentationRenderers(
            GameObject instance)
        {
            if (instance == null)
                return;

            Renderer[] renderers =
                instance.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                ConfigurePresentationRenderer(renderers[i]);
        }

        private static void NormalizeVisualSize(
            GameObject instance,
            float targetSize)
        {
            if (instance == null)
                return;

            Renderer[] renderers =
                instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return;

            Matrix4x4 worldToParent = instance.transform.parent != null
                ? instance.transform.parent.worldToLocalMatrix
                : Matrix4x4.identity;
            Bounds bounds = TransformBounds(
                renderers[0].localBounds,
                worldToParent * renderers[0].localToWorldMatrix);
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(TransformBounds(
                    renderers[i].localBounds,
                    worldToParent * renderers[i].localToWorldMatrix));
            }
            float size = Mathf.Max(
                bounds.size.x,
                Mathf.Max(bounds.size.y, bounds.size.z));
            if (!float.IsFinite(size) || size <= 0.00001f)
                return;

            instance.transform.localScale *= targetSize / size;
        }

        private static Bounds TransformBounds(
            Bounds source,
            Matrix4x4 transform)
        {
            Vector3 center = transform.MultiplyPoint3x4(source.center);
            Vector3 sourceExtents = source.extents;
            Vector3 axisX = transform.MultiplyVector(
                new Vector3(sourceExtents.x, 0f, 0f));
            Vector3 axisY = transform.MultiplyVector(
                new Vector3(0f, sourceExtents.y, 0f));
            Vector3 axisZ = transform.MultiplyVector(
                new Vector3(0f, 0f, sourceExtents.z));
            Vector3 extents = new(
                Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
                Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
                Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));
            return new Bounds(center, extents * 2f);
        }

        private void CreateEnvironmentModules(
            float carLength,
            PitEnvironmentProfile environmentProfile,
            Color floorColor)
        {
            bool isSuzuka = environmentProfile != null &&
                environmentProfile.Matches("Suzuka");
            Transform trackModule = new GameObject(
                "PitTrackModule").transform;
            trackModule.SetParent(root.transform, false);
            if (environmentProfile != null &&
                environmentProfile.pitTrackPrefab != null)
            {
                GameObject pitTrack = InstantiateEnvironmentPrefab(
                    environmentProfile.pitTrackPrefab,
                    "PitTrack",
                    trackModule,
                    environmentProfile.pitTrackLocalPosition,
                    environmentProfile.pitTrackLocalEulerAngles,
                    environmentProfile.pitTrackLocalScale);
                if (isSuzuka)
                {
                    ExtendSuzukaPitLane(
                        environmentProfile,
                        trackModule,
                        pitTrack);
                }
            }
            else
            {
                CreateFallbackPitTrack(
                    trackModule,
                    carLength,
                    floorColor);
            }

            Transform buildingModule = new GameObject(
                "PitBuildingModule").transform;
            buildingModule.SetParent(root.transform, false);
            if (environmentProfile != null &&
                environmentProfile.pitBuildingPrefab != null)
            {
                InstantiateEnvironmentPrefab(
                    environmentProfile.pitBuildingPrefab,
                    "PitBuilding",
                    buildingModule,
                    environmentProfile.pitBuildingLocalPosition,
                    environmentProfile.pitBuildingLocalEulerAngles,
                    environmentProfile.pitBuildingLocalScale);
            }

            if (environmentProfile != null &&
                environmentProfile.backgroundPrefab != null)
            {
                InstantiateEnvironmentPrefab(
                    environmentProfile.backgroundPrefab,
                    "PitEnvironmentBackground",
                    root.transform,
                    environmentProfile.localPosition,
                    environmentProfile.localEulerAngles,
                    environmentProfile.localScale);
            }

            if (isSuzuka)
            {
                CreateContainedSuzukaBackdrop(
                    root.transform,
                    carLength);
            }
        }

        private static void ExtendSuzukaPitLane(
            PitEnvironmentProfile environmentProfile,
            Transform trackModule,
            GameObject pitTrack)
        {
            if (environmentProfile == null ||
                trackModule == null ||
                pitTrack == null)
            {
                return;
            }

            if (TryCalculateRendererBounds(
                    pitTrack,
                    pitTrack.transform,
                    out Bounds nativeBounds) &&
                float.IsFinite(nativeBounds.size.z) &&
                nativeBounds.size.z >= 90f)
            {
                return;
            }

            if (!TryCalculateRendererBounds(
                    pitTrack,
                    trackModule,
                    out Bounds bounds))
            {
                return;
            }

            Vector3 laneDirection =
                Quaternion.Euler(
                    environmentProfile.pitTrackLocalEulerAngles) *
                Vector3.forward;
            laneDirection.Normalize();
            float laneSpan =
                Mathf.Abs(laneDirection.x) * bounds.size.x +
                Mathf.Abs(laneDirection.y) * bounds.size.y +
                Mathf.Abs(laneDirection.z) * bounds.size.z;
            if (!float.IsFinite(laneSpan) || laneSpan <= 0.00001f)
                return;
            Vector3 continuationOffset =
                laneDirection * laneSpan * 0.995f;
            InstantiateEnvironmentPrefab(
                environmentProfile.pitTrackPrefab,
                "PitTrack_ApproachContinuation",
                trackModule,
                environmentProfile.pitTrackLocalPosition -
                continuationOffset,
                environmentProfile.pitTrackLocalEulerAngles,
                environmentProfile.pitTrackLocalScale);
            InstantiateEnvironmentPrefab(
                environmentProfile.pitTrackPrefab,
                "PitTrack_DepartureContinuation",
                trackModule,
                environmentProfile.pitTrackLocalPosition +
                continuationOffset,
                environmentProfile.pitTrackLocalEulerAngles,
                environmentProfile.pitTrackLocalScale);
        }

        private void CreateContainedSuzukaBackdrop(
            Transform parent,
            float carLength)
        {
            Transform backdrop = new GameObject(
                "ContainedSuzukaBackdrop").transform;
            backdrop.SetParent(parent, false);

            CreateBackdropMesh(
                "SuzukaDaylightSky",
                backdrop,
                new Vector3(0f, carLength * 3.8f, 0f),
                new Vector3(
                    carLength * 12f,
                    carLength * 6f,
                    carLength * 12f),
                CreateSkyDomeMesh(),
                CreateSuzukaSkyMaterial());
        }

        private void CreateBackdropMesh(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Mesh mesh,
            Color color)
        {
            CreateBackdropMesh(
                name,
                parent,
                localPosition,
                localScale,
                mesh,
                CreateMaterial(color));
        }

        private static void CreateBackdropMesh(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Mesh mesh,
            Material material)
        {
            if (mesh == null || material == null)
                return;

            GameObject instance = new(name);
            instance.transform.SetParent(parent, false);
            instance.transform.localPosition = localPosition;
            instance.transform.localScale = localScale;
            MeshFilter filter = instance.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = instance.AddComponent<MeshRenderer>();
            if (material.HasProperty("_Cull"))
                material.SetFloat("_Cull", (float)CullMode.Off);
            renderer.sharedMaterial = material;
            ConfigurePresentationRenderer(renderer);
        }

        private Material CreateSuzukaSkyMaterial()
        {
            const int height = 64;
            Texture2D gradient = new(
                1,
                height,
                TextureFormat.RGBA32,
                false,
                true)
            {
                name = "Runtime_SuzukaDaylightGradient",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            Color zenith = new(0.26f, 0.37f, 0.49f, 1f);
            Color upperSky = new(0.41f, 0.52f, 0.62f, 1f);
            Color horizon = new(0.65f, 0.71f, 0.75f, 1f);
            Color below = new(0.39f, 0.46f, 0.51f, 1f);
            for (int y = 0; y < height; y++)
            {
                float v = y / (float)(height - 1);
                Color color;
                if (v <= 0.34f)
                {
                    float t = Mathf.SmoothStep(0f, 1f, v / 0.34f);
                    color = Color.Lerp(zenith, upperSky, t);
                }
                else if (v <= 0.56f)
                {
                    float t = Mathf.SmoothStep(
                        0f,
                        1f,
                        (v - 0.34f) / 0.22f);
                    color = Color.Lerp(upperSky, horizon, t);
                }
                else
                {
                    float t = Mathf.SmoothStep(
                        0f,
                        1f,
                        (v - 0.56f) / 0.44f);
                    color = Color.Lerp(horizon, below, t);
                }
                gradient.SetPixel(0, y, color);
            }
            gradient.Apply(false, true);
            textures.Add(gradient);

            Shader shader = Shader.Find(
                "Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Texture");
            Material material = new(shader)
            {
                name = "Runtime_SuzukaDaylightSky",
                color = Color.white,
                mainTexture = gradient
            };
            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", gradient);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", Color.white);
            materials.Add(material);
            return material;
        }

        private Mesh CreateSkyDomeMesh()
        {
            const int segments = 24;
            const int rings = 8;
            List<Vector3> vertices = new((segments + 1) * (rings + 1));
            List<Vector2> uvs = new(vertices.Capacity);
            List<int> triangles = new(segments * rings * 6);
            for (int ring = 0; ring <= rings; ring++)
            {
                float v = ring / (float)rings;
                float latitude = Mathf.PI * v;
                float radius = Mathf.Sin(latitude);
                float y = Mathf.Cos(latitude);
                for (int segment = 0; segment <= segments; segment++)
                {
                    float u = segment / (float)segments;
                    float longitude = Mathf.PI * 2f * u;
                    vertices.Add(new Vector3(
                        Mathf.Cos(longitude) * radius,
                        y,
                        Mathf.Sin(longitude) * radius));
                    uvs.Add(new Vector2(u, v));
                }
            }

            for (int ring = 0; ring < rings; ring++)
            {
                for (int segment = 0; segment < segments; segment++)
                {
                    int current = ring * (segments + 1) + segment;
                    int next = current + segments + 1;
                    triangles.Add(current);
                    triangles.Add(next);
                    triangles.Add(current + 1);
                    triangles.Add(current + 1);
                    triangles.Add(next);
                    triangles.Add(next + 1);
                }
            }

            Mesh mesh = new()
            {
                name = "Runtime_SuzukaDaylightSky",
                vertices = vertices.ToArray(),
                uv = uvs.ToArray(),
                triangles = triangles.ToArray()
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            meshes.Add(mesh);
            return mesh;
        }

        private static bool TryCalculateRendererBounds(
            GameObject instance,
            Transform relativeTo,
            out Bounds bounds)
        {
            bounds = default;
            if (instance == null || relativeTo == null)
                return false;

            Renderer[] renderers =
                instance.GetComponentsInChildren<Renderer>(true);
            bool found = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                Bounds rendererBounds = TransformBounds(
                    renderer.localBounds,
                    relativeTo.worldToLocalMatrix *
                    renderer.localToWorldMatrix);
                if (!found)
                {
                    bounds = rendererBounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(rendererBounds);
                }
            }
            return found;
        }

        private void CreateCrewSilhouette(
            Transform parent,
            float carLength,
            Color teamColor)
        {
            Color suitColor = Color.Lerp(
                teamColor,
                Color.black,
                0.28f);
            Color helmetColor = Color.Lerp(
                teamColor,
                Color.white,
                0.2f);
            Color visorColor = new(0.03f, 0.04f, 0.055f, 1f);

            CreatePrimitive(
                PrimitiveType.Capsule,
                "LeftLeg",
                parent,
                new Vector3(
                    -carLength * 0.035f,
                    carLength * 0.065f,
                    0f),
                new Vector3(
                    carLength * 0.028f,
                    carLength * 0.06f,
                    carLength * 0.03f),
                suitColor);
            CreatePrimitive(
                PrimitiveType.Capsule,
                "RightLeg",
                parent,
                new Vector3(
                    carLength * 0.035f,
                    carLength * 0.065f,
                    0f),
                new Vector3(
                    carLength * 0.028f,
                    carLength * 0.06f,
                    carLength * 0.03f),
                suitColor);
            CreatePrimitive(
                PrimitiveType.Capsule,
                "Torso",
                parent,
                new Vector3(
                    0f,
                    carLength * 0.18f,
                    0f),
                new Vector3(
                    carLength * 0.075f,
                    carLength * 0.09f,
                    carLength * 0.055f),
                suitColor);

            GameObject leftArm = CreatePrimitive(
                PrimitiveType.Capsule,
                "LeftArm",
                parent,
                new Vector3(
                    -carLength * 0.09f,
                    carLength * 0.18f,
                    0f),
                new Vector3(
                    carLength * 0.025f,
                    carLength * 0.07f,
                    carLength * 0.025f),
                suitColor);
            leftArm.transform.localRotation =
                Quaternion.Euler(0f, 0f, -12f);
            GameObject rightArm = CreatePrimitive(
                PrimitiveType.Capsule,
                "RightArm",
                parent,
                new Vector3(
                    carLength * 0.09f,
                    carLength * 0.18f,
                    0f),
                new Vector3(
                    carLength * 0.025f,
                    carLength * 0.07f,
                    carLength * 0.025f),
                suitColor);
            rightArm.transform.localRotation =
                Quaternion.Euler(0f, 0f, 12f);

            CreatePrimitive(
                PrimitiveType.Sphere,
                "Helmet",
                parent,
                new Vector3(
                    0f,
                    carLength * 0.31f,
                    0f),
                Vector3.one * carLength * 0.065f,
                helmetColor);
            CreateBox(
                "Visor",
                parent,
                new Vector3(
                    0f,
                    carLength * 0.315f,
                    carLength * 0.058f),
                new Vector3(
                    carLength * 0.07f,
                    carLength * 0.022f,
                    carLength * 0.018f),
                visorColor);
        }

        private float ResolveCrewBlend(
            float replayTime,
            PitStopPhase phase)
        {
            if (sequence == null || sequence.IsDriveThrough)
                return 0f;

            if (phase == PitStopPhase.Brake)
            {
                return Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(
                        sequence.BrakeTime,
                        sequence.ServiceStartTime,
                        replayTime));
            }

            if (phase == PitStopPhase.Service)
                return 1f;

            if (phase == PitStopPhase.Release)
            {
                return 1f - Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(
                        sequence.ServiceEndTime,
                        sequence.ReleaseEndTime,
                        replayTime));
            }

            return 0f;
        }

        private void CreateFallbackPitTrack(
            Transform parent,
            float carLength,
            Color floorColor)
        {
            Color laneColor = Color.Lerp(
                floorColor,
                Color.white,
                0.08f);
            float trackLength = carLength * 15f;

            CreateCombinedBoxes(
                "PitTrackBase",
                parent,
                floorColor,
                new List<PitBoxSpec>
                {
                    new(
                        new Vector3(
                            carLength * 0.15f,
                            -carLength * 0.035f,
                            0f),
                        new Vector3(
                            carLength * 2.2f,
                            carLength * 0.05f,
                            trackLength))
                });
            CreateCombinedBoxes(
                "PitLaneSurface",
                parent,
                laneColor,
                new List<PitBoxSpec>
                {
                    new(
                        new Vector3(
                            -carLength * 0.12f,
                            -carLength * 0.0075f,
                            0f),
                        new Vector3(
                            carLength * 1.62f,
                            carLength * 0.015f,
                            trackLength * 0.98f))
                });

        }

        private void CreateFallbackPitBuilding(
            Transform parent,
            float carLength,
            Color darkColor,
            Color teamColor)
        {
            Color structureColor = Color.Lerp(
                darkColor,
                Color.white,
                0.18f);
            Color lightColor = new(0.7f, 0.82f, 0.92f, 1f);
            float buildingLength = carLength * 15f;

            CreateCombinedBoxes(
                "PitBuildingShell",
                parent,
                darkColor,
                new List<PitBoxSpec>
                {
                    new(
                        new Vector3(
                            carLength * 1.25f,
                            carLength * 0.72f,
                            0f),
                        new Vector3(
                            carLength * 0.12f,
                            carLength * 1.55f,
                            buildingLength))
                });

            List<PitBoxSpec> structure = new()
            {
                new(
                    new Vector3(
                        carLength * 2.35f,
                        carLength * 0.72f,
                        0f),
                    new Vector3(
                        carLength * 0.12f,
                        carLength * 1.55f,
                        buildingLength)),
                new(
                    new Vector3(
                        carLength * 1.78f,
                        carLength * 1.48f,
                        0f),
                    new Vector3(
                        carLength * 1.25f,
                        carLength * 0.1f,
                        buildingLength)),
                new(
                    new Vector3(
                        carLength * 1.8f,
                        carLength * 0.72f,
                        -buildingLength * 0.5f),
                    new Vector3(
                        carLength * 1.25f,
                        carLength * 1.55f,
                        carLength * 0.1f)),
                new(
                    new Vector3(
                        carLength * 1.8f,
                        carLength * 0.72f,
                        buildingLength * 0.5f),
                    new Vector3(
                        carLength * 1.25f,
                        carLength * 1.55f,
                        carLength * 0.1f))
            };
            List<PitBoxSpec> lights = new();

            for (int i = -7; i <= 7; i++)
            {
                float z = i * carLength;
                structure.Add(new PitBoxSpec(
                    new Vector3(
                        carLength * 1.16f,
                        carLength * 0.45f,
                        z),
                    new Vector3(
                        carLength * 0.09f,
                        carLength * 0.9f,
                        carLength * 0.08f)));
                structure.Add(new PitBoxSpec(
                    new Vector3(
                        carLength * 1.16f,
                        carLength * 0.88f,
                        z + carLength * 0.5f),
                    new Vector3(
                        carLength * 0.09f,
                        carLength * 0.08f,
                        carLength * 0.92f)));

                if (i % 2 == 0)
                {
                    lights.Add(new PitBoxSpec(
                        new Vector3(
                            carLength * 1.2f,
                            carLength * 1.07f,
                            z + carLength * 0.5f),
                        new Vector3(
                            carLength * 0.04f,
                            carLength * 0.035f,
                            carLength * 0.55f)));
                }
            }

            CreateCombinedBoxes(
                "PitBuildingStructure",
                parent,
                structureColor,
                structure);
            CreateCombinedBoxes(
                "PitBuildingFascia",
                parent,
                Color.Lerp(darkColor, teamColor, 0.18f),
                new List<PitBoxSpec>
                {
                    new(
                        new Vector3(
                            carLength * 1.17f,
                            carLength * 1.28f,
                            0f),
                        new Vector3(
                            carLength * 0.1f,
                            carLength * 0.3f,
                            buildingLength))
                });
            CreateCombinedBoxes(
                "PitBuildingLights",
                parent,
                lightColor,
                lights);
        }

        private static GameObject InstantiateEnvironmentPrefab(
            GameObject prefab,
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 localEulerAngles,
            Vector3 localScale)
        {
            GameObject instance = Object.Instantiate(prefab, parent);
            instance.name = name;
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation =
                Quaternion.Euler(localEulerAngles);
            instance.transform.localScale = localScale;
            return instance;
        }

        private void ApplyAudio(
            float replayTime,
            bool playing,
            bool servicing,
            bool timelineDiscontinuity)
        {
            if (wheelGunAudio == null ||
                wheelGunAudio.clip == null ||
                sequence == null)
            {
                return;
            }

            if (!servicing)
            {
                wheelGunAudio.Stop();
                return;
            }

            if (!playing)
            {
                if (wheelGunAudio.isPlaying)
                    wheelGunAudio.Pause();
                return;
            }

            float offset = Mathf.Repeat(
                Mathf.Max(
                    0f,
                    replayTime -
                    (firstMilestoneChoreography != null
                        ? PitStopFirstMilestoneChoreography
                            .ResolveReplayStart(sequence)
                        : sequence.ServiceStartTime)),
                wheelGunAudio.clip.length);
            if (!wheelGunAudio.isPlaying || timelineDiscontinuity)
            {
                wheelGunAudio.Stop();
                wheelGunAudio.time = Mathf.Clamp(
                    offset,
                    0f,
                    Mathf.Max(0f, wheelGunAudio.clip.length - 0.01f));
                wheelGunAudio.Play();
            }
        }

        private void CreateCombinedBoxes(
            string name,
            Transform parent,
            Color color,
            IReadOnlyList<PitBoxSpec> boxes)
        {
            if (parent == null || boxes == null || boxes.Count == 0)
                return;

            Mesh cube = ResolvePrimitiveMesh(PrimitiveType.Cube);
            if (cube == null)
                return;

            CombineInstance[] instances = new CombineInstance[boxes.Count];
            for (int i = 0; i < boxes.Count; i++)
            {
                PitBoxSpec box = boxes[i];
                instances[i] = new CombineInstance
                {
                    mesh = cube,
                    transform = Matrix4x4.TRS(
                        box.Position,
                        Quaternion.identity,
                        box.Scale)
                };
            }

            Mesh mesh = new()
            {
                name = $"{name}Mesh",
                indexFormat = cube.vertexCount * boxes.Count > 65535
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16
            };
            mesh.CombineMeshes(instances, true, true, false);
            mesh.RecalculateBounds();
            meshes.Add(mesh);

            GameObject instance = new GameObject(name);
            instance.transform.SetParent(parent, false);
            MeshFilter filter = instance.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = instance.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = CreateMaterial(color);
            ConfigurePresentationRenderer(renderer);
        }

        private GameObject CreateBox(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Color color)
        {
            return CreatePrimitive(
                PrimitiveType.Cube,
                name,
                parent,
                localPosition,
                localScale,
                color);
        }

        private GameObject CreatePrimitive(
            PrimitiveType type,
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Color color)
        {
            GameObject instance = new GameObject(name);
            instance.transform.SetParent(parent, false);
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = localScale;
            Mesh mesh = ResolvePrimitiveMesh(type);
            if (mesh != null)
            {
                MeshFilter filter = instance.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                MeshRenderer renderer = instance.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = CreateMaterial(color);
                ConfigurePresentationRenderer(renderer);
            }
            return instance;
        }

        private static void ConfigurePresentationRenderer(
            Renderer renderer)
        {
            if (renderer == null)
                return;

            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
        }

        private Mesh ResolvePrimitiveMesh(PrimitiveType type)
        {
            if (primitiveMeshes.TryGetValue(type, out Mesh cached) &&
                cached != null)
            {
                return cached;
            }

            GameObject template = GameObject.CreatePrimitive(type);
            template.SetActive(false);
            Mesh mesh = template.TryGetComponent(out MeshFilter filter)
                ? filter.sharedMesh
                : null;
            Object.Destroy(template);
            if (mesh != null)
                primitiveMeshes[type] = mesh;
            return mesh;
        }

        private Material CreateMaterial(Color color)
        {
            Color32 key = color;
            if (materialCache.TryGetValue(
                    key,
                    out Material cached) &&
                cached != null)
            {
                return cached;
            }

            Shader shader = Shader.Find(
                "Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            Material material = new(shader)
            {
                color = color
            };
            materials.Add(material);
            materialCache[key] = material;
            return material;
        }

        private static Color ResolveTeamColor(DriverInfoDto driver)
        {
            if (driver != null &&
                !string.IsNullOrWhiteSpace(driver.teamColour) &&
                ColorUtility.TryParseHtmlString(
                    "#" + driver.teamColour.TrimStart('#'),
                    out Color color))
            {
                return color;
            }

            return new Color(0.9f, 0.08f, 0.08f, 1f);
        }
    }
}
