using System;
using System.IO;
using F1XR.RestAPI.Replay;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace F1XR.EditorTools
{
    [InitializeOnLoad]
    internal static class FirstPitStopGameViewRecorder
    {
        private const string MenuPath =
            "F1 XR/Capture/Record First Ferrari Pit Stop MP4";
        private const string HeroMenuPath =
            "F1 XR/Capture/Record First Ferrari Pit Stop - Hero 45 MP4";
        private const string PortalHeroMenuPath =
            "F1 XR/Capture/Record First Ferrari Pit Stop - Portal Hero MP4";
        private const string ElevatedFourCornerMenuPath =
            "F1 XR/Capture/Record First Ferrari Pit Stop - ElevatedFourCorner MP4";
        private const string ScenePath =
            "Assets/F1_XR_Visualizer/01_Scenes/SessionSpace_fitin.unity";
        private const string ExpectedEventId = "pit_9496_55_15";
        private const string StatePrefix =
            "F1XR.FirstPitStopGameViewRecorder.";
        private const string PendingKey = StatePrefix + "Pending";
        private const string RecordingKey = StatePrefix + "Recording";
        private const string SucceededKey = StatePrefix + "Succeeded";
        private const string OutputBaseKey = StatePrefix + "OutputBase";
        private const string StartedTicksKey = StatePrefix + "StartedTicks";
        private const string HeroKey = StatePrefix + "Hero45";
        private const string PortalHeroKey = StatePrefix + "PortalHero";
        private const string ElevatedFourCornerKey =
            StatePrefix + "ElevatedFourCorner";
        private const string HeroCameraTag = "GameController";
        private const string PortalCameraName = "PitStopPortalCamera";
        private const string PortalSurfaceName = "PitStopPortalSurface";
        private const string PitPortalControlsName =
            "EventReplayControls";
        private const string ChoreographyOriginName =
            "PitChoreographyOrigin";
        private const string FlHubName = "FL_Hub";
        private const string FrHubName = "FR_Hub";
        private const string RlHubName = "RL_Hub";
        private const string RrHubName = "RR_Hub";
        private const string FlTyreName = "FL_Tire";
        private const string SuzukaContextSurfaceName = "ContextSurface";
        private const string SuzukaContextMeshName =
            "SuzukaPitLaneContextMesh";
        private const int ElevatedOccluderSubMeshIndex = 1;
        private const string ElevatedOccluderMaterialName = "WALL1";
        private const float HeroFieldOfView = 50f;
        private const float HeroTargetBlend = 0.35f;
        private const float HeroTargetHeightInTyres = 0.58f;
        private const float HeroDistanceInTyres = 8.75f;
        private const float HeroDownwardAim = 0.30f;
        private const float ElevatedFourCornerFieldOfView = 48f;
        private const double ReadyTimeoutSeconds = 180d;
        private const double RecordingTimeoutSeconds = 90d;
        private const double TailSeconds = 0.5d;

        private static RecorderController recorder;
        private static RecorderControllerSettings controllerSettings;
        private static MovieRecorderSettings movieSettings;
        private static EventPopoutReplay replay;
        private static Camera heroCamera;
        private static Camera portalHeroCamera;
        private static Camera elevatedFourCornerCamera;
        private static GameObject suppressedPortalHeroUi;
        private static MeshFilter suppressedElevatedContextFilter;
        private static Mesh suppressedElevatedContextSourceMesh;
        private static Mesh elevatedValidationContextMesh;
        private static float replayEndTime;
        private static bool observedPlayback;
        private static double recordingStartedAt;
        private static double stopAfterTime = -1d;

        static FirstPitStopGameViewRecorder()
        {
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.delayCall += RecoverStaleEditModeState;
        }

        [MenuItem(MenuPath, false, 2000)]
        private static void Record()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                SessionState.GetBool(PendingKey, false))
            {
                Debug.LogWarning(
                    "[PitRecorder] A Play Mode transition or recording is already active.");
                return;
            }

            if (!EnsureTargetScene())
                return;

            string outputBase = BuildOutputBasePath();
            Directory.CreateDirectory(Path.GetDirectoryName(outputBase));

            SessionState.SetBool(PendingKey, true);
            SessionState.SetBool(RecordingKey, false);
            SessionState.SetBool(SucceededKey, false);
            SessionState.SetBool(HeroKey, false);
            SessionState.SetBool(PortalHeroKey, false);
            SessionState.SetBool(ElevatedFourCornerKey, false);
            SessionState.SetString(OutputBaseKey, outputBase);
            SessionState.SetString(
                StartedTicksKey,
                DateTime.UtcNow.Ticks.ToString());

            Debug.Log(
                "[PitRecorder] Waiting for the validated Ferrari pit stop. " +
                "Output: " + outputBase + ".mp4");
            EditorApplication.isPlaying = true;
        }

        [MenuItem(HeroMenuPath, false, 2001)]
        private static void RecordHero45()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                SessionState.GetBool(PendingKey, false))
            {
                Debug.LogWarning(
                    "[PitRecorder] A Play Mode transition or recording is already active.");
                return;
            }

            if (!EnsureTargetScene())
                return;

            string outputBase = BuildHeroOutputBasePath();
            Directory.CreateDirectory(Path.GetDirectoryName(outputBase));

            SessionState.SetBool(PendingKey, true);
            SessionState.SetBool(RecordingKey, false);
            SessionState.SetBool(SucceededKey, false);
            SessionState.SetBool(HeroKey, true);
            SessionState.SetBool(PortalHeroKey, false);
            SessionState.SetBool(ElevatedFourCornerKey, false);
            SessionState.SetString(OutputBaseKey, outputBase);
            SessionState.SetString(
                StartedTicksKey,
                DateTime.UtcNow.Ticks.ToString());

            Debug.Log(
                "[PitRecorder] Waiting for the validated Ferrari pit stop " +
                "Hero 45 view. Output: " + outputBase + ".mp4");
            EditorApplication.isPlaying = true;
        }

        [MenuItem(PortalHeroMenuPath, false, 2002)]
        private static void RecordPortalHero()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                SessionState.GetBool(PendingKey, false))
            {
                Debug.LogWarning(
                    "[PitRecorder] A Play Mode transition or recording is already active.");
                return;
            }

            if (!EnsureTargetScene())
                return;

            string outputBase = BuildPortalHeroOutputBasePath();
            Directory.CreateDirectory(Path.GetDirectoryName(outputBase));

            SessionState.SetBool(PendingKey, true);
            SessionState.SetBool(RecordingKey, false);
            SessionState.SetBool(SucceededKey, false);
            SessionState.SetBool(HeroKey, false);
            SessionState.SetBool(PortalHeroKey, true);
            SessionState.SetBool(ElevatedFourCornerKey, false);
            SessionState.SetString(OutputBaseKey, outputBase);
            SessionState.SetString(
                StartedTicksKey,
                DateTime.UtcNow.Ticks.ToString());

            Debug.Log(
                "[PitRecorder] Waiting for the validated Ferrari pit stop " +
                "Portal Hero view. Output: " + outputBase + ".mp4");
            EditorApplication.isPlaying = true;
        }

        [MenuItem(ElevatedFourCornerMenuPath, false, 2003)]
        private static void RecordElevatedFourCorner()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                SessionState.GetBool(PendingKey, false))
            {
                Debug.LogWarning(
                    "[PitRecorder] A Play Mode transition or recording is already active.");
                return;
            }

            if (!EnsureTargetScene())
                return;

            string outputBase = BuildElevatedFourCornerOutputBasePath();
            Directory.CreateDirectory(Path.GetDirectoryName(outputBase));

            SessionState.SetBool(PendingKey, true);
            SessionState.SetBool(RecordingKey, false);
            SessionState.SetBool(SucceededKey, false);
            SessionState.SetBool(HeroKey, false);
            SessionState.SetBool(PortalHeroKey, false);
            SessionState.SetBool(ElevatedFourCornerKey, true);
            SessionState.SetString(OutputBaseKey, outputBase);
            SessionState.SetString(
                StartedTicksKey,
                DateTime.UtcNow.Ticks.ToString());

            Debug.Log(
                "[PitRecorder] Waiting for the validated Ferrari pit stop " +
                "ElevatedFourCorner view. Output: " + outputBase + ".mp4");
            EditorApplication.isPlaying = true;
        }

        [MenuItem(MenuPath, true)]
        private static bool CanRecord()
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode &&
                SessionState.GetBool(PendingKey, false) &&
                !SessionState.GetBool(RecordingKey, false) &&
                SessionState.GetBool(SucceededKey, false))
            {
                VerifyOutput();
            }

            return !EditorApplication.isPlayingOrWillChangePlaymode &&
                !SessionState.GetBool(PendingKey, false);
        }

        [MenuItem(HeroMenuPath, true)]
        private static bool CanRecordHero45()
        {
            return CanRecord();
        }

        [MenuItem(PortalHeroMenuPath, true)]
        private static bool CanRecordPortalHero()
        {
            return CanRecord();
        }

        [MenuItem(ElevatedFourCornerMenuPath, true)]
        private static bool CanRecordElevatedFourCorner()
        {
            return CanRecord();
        }

        private static bool EnsureTargetScene()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.path == ScenePath)
                return true;

            if (!File.Exists(ScenePath))
            {
                Debug.LogError(
                    "[PitRecorder] Required scene is missing: " + ScenePath);
                return false;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return false;

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            return SceneManager.GetActiveScene().path == ScenePath;
        }

        private static string BuildOutputBasePath()
        {
            string projectRoot =
                Directory.GetParent(Application.dataPath).FullName;
            string fileName =
                "Ferrari_Suzuka_L15_GameView_" +
                DateTime.Now.ToString("yyyyMMdd_HHmmss");
            return Path.Combine(
                projectRoot,
                "Temp",
                "PitValidation",
                "Recorder",
                fileName);
        }

        private static string BuildHeroOutputBasePath()
        {
            string projectRoot =
                Directory.GetParent(Application.dataPath).FullName;
            string fileName =
                "Ferrari_Suzuka_L15_Hero45_" +
                DateTime.Now.ToString("yyyyMMdd_HHmmss");
            return Path.Combine(
                projectRoot,
                "Temp",
                "PitValidation",
                "Recorder",
                fileName);
        }

        private static string BuildPortalHeroOutputBasePath()
        {
            string projectRoot =
                Directory.GetParent(Application.dataPath).FullName;
            string fileName =
                "Ferrari_Suzuka_L15_PortalHero_" +
                DateTime.Now.ToString("yyyyMMdd_HHmmss");
            return Path.Combine(
                projectRoot,
                "Temp",
                "PitValidation",
                "Recorder",
                fileName);
        }

        private static string BuildElevatedFourCornerOutputBasePath()
        {
            string projectRoot =
                Directory.GetParent(Application.dataPath).FullName;
            string fileName =
                "Ferrari_Suzuka_L15_ElevatedFourCorner_" +
                DateTime.Now.ToString("yyyyMMdd_HHmmss");
            return Path.Combine(
                projectRoot,
                "Temp",
                "PitValidation",
                "Recorder",
                fileName);
        }

        private static void Update()
        {
            if (!SessionState.GetBool(PendingKey, false) ||
                !EditorApplication.isPlaying)
            {
                return;
            }

            if (SessionState.GetBool(RecordingKey, false))
            {
                UpdateRecording();
                return;
            }

            if (HasTimedOut(ReadyTimeoutSeconds))
            {
                Fail(
                    "Timed out waiting for the first Ferrari pit-stop stage.");
                return;
            }

            replay = UnityEngine.Object
                .FindFirstObjectByType<EventPopoutReplay>();
            if (replay == null ||
                replay.IsLoading ||
                !replay.IsPitStopActive)
            {
                return;
            }

            if (replay.CurrentEvent == null ||
                replay.CurrentEvent.eventId != ExpectedEventId)
            {
                Fail(
                    "Expected " + ExpectedEventId +
                    " but the active event is " +
                    (replay.CurrentEvent == null
                        ? "<none>"
                        : replay.CurrentEvent.eventId) +
                    ". Event selection was not changed.");
                return;
            }

            StartRecording();
        }

        private static void StartRecording()
        {
            if (!replay.TryPauseAtPitStopReplayTime(replay.StartTime))
            {
                Fail("Could not rewind the pit-stop replay to its arrival start.");
                return;
            }

            string outputBase =
                SessionState.GetString(OutputBaseKey, "");
            if (string.IsNullOrEmpty(outputBase))
            {
                Fail("The recording output path is missing.");
                return;
            }

            bool recordHero = SessionState.GetBool(HeroKey, false);
            bool recordPortalHero =
                SessionState.GetBool(PortalHeroKey, false);
            bool recordElevatedFourCorner =
                SessionState.GetBool(ElevatedFourCornerKey, false);
            if (recordPortalHero &&
                !TryCreatePortalHeroCamera(out string cameraFailure))
            {
                Fail(cameraFailure);
                return;
            }
            if (recordElevatedFourCorner &&
                !TryCreateElevatedFourCornerCamera(out cameraFailure))
            {
                Fail(cameraFailure);
                return;
            }
            if (recordHero && !TryCreateHeroCamera(out cameraFailure))
            {
                Fail(cameraFailure);
                return;
            }

            controllerSettings =
                ScriptableObject.CreateInstance<RecorderControllerSettings>();
            controllerSettings.name = "F1 XR Pit Validation Recorder";
            controllerSettings.SetRecordModeToManual();
            controllerSettings.FrameRate = 30f;
            controllerSettings.CapFrameRate = true;
            controllerSettings.ExitPlayMode = false;

            movieSettings =
                ScriptableObject.CreateInstance<MovieRecorderSettings>();
            movieSettings.name = recordElevatedFourCorner
                ? "ElevatedFourCorner MP4"
                : recordPortalHero
                    ? "Portal Hero MP4"
                    : recordHero
                        ? "Hero 45 MP4"
                        : "Current Game View MP4";
            movieSettings.Enabled = true;
            movieSettings.OutputFile = outputBase;
            movieSettings.CaptureAudio = false;
            movieSettings.CaptureAlpha = false;
            movieSettings.ImageInputSettings =
                recordHero || recordPortalHero || recordElevatedFourCorner
                    ? CreateHeroInputSettings()
                    : new GameViewInputSettings
                    {
                        OutputWidth = 1920,
                        OutputHeight = 1080
                    };
            movieSettings.EncoderSettings =
                new CoreEncoderSettings
                {
                    Codec = CoreEncoderSettings.OutputCodec.MP4,
                    EncodingProfile =
                        CoreEncoderSettings.H264EncodingProfile.High,
                    EncodingQuality =
                        CoreEncoderSettings.VideoEncodingQuality.High
                };

            controllerSettings.AddRecorderSettings(movieSettings);
            recorder = new RecorderController(controllerSettings);
            recorder.PrepareRecording();

            if (!recorder.StartRecording())
            {
                Fail(
                    "Unity Recorder failed to start. Check the Console for " +
                    "the package error.");
                return;
            }

            replayEndTime = replay.EndTime;
            observedPlayback = false;
            recordingStartedAt = EditorApplication.timeSinceStartup;
            stopAfterTime = -1d;
            SessionState.SetBool(RecordingKey, true);

            EditorApplication.isPaused = false;
            replay.Play();

            if (recordElevatedFourCorner)
            {
                Debug.Log(
                    "[PitRecorder] Recording ElevatedFourCorner at 1920x1080, " +
                    "30 fps, H.264 MP4. " +
                    DescribeElevatedFourCornerCamera());
            }
            else if (recordPortalHero)
            {
                Debug.Log(
                    "[PitRecorder] Recording Portal Hero at 1920x1080, " +
                    "30 fps, H.264 MP4. " +
                    DescribePortalHeroCamera());
            }
            else if (recordHero)
            {
                Debug.Log(
                    "[PitRecorder] Recording Hero 45 at 1920x1080, " +
                    "30 fps, H.264 MP4. " +
                    DescribeHeroCamera());
            }
            else
            {
                Debug.Log(
                    "[PitRecorder] Recording current Game View at 1920x1080, " +
                    "30 fps, H.264 MP4.");
            }
        }

        private static CameraInputSettings CreateHeroInputSettings()
        {
            return new CameraInputSettings
            {
                Source = ImageSource.TaggedCamera,
                CameraTag = HeroCameraTag,
                CaptureUI = false,
                OutputWidth = 1920,
                OutputHeight = 1080
            };
        }

        private static bool TryCreateHeroCamera(out string failure)
        {
            failure = null;
            Transform stage = replay.PresentationRoot;
            if (stage == null)
            {
                failure = "The active replay presentation root is missing.";
                return false;
            }

            Transform[] transforms =
                stage.GetComponentsInChildren<Transform>(true);
            Transform origin = FindTransform(
                transforms,
                ChoreographyOriginName);
            Transform hub = FindTransform(transforms, FlHubName);
            Transform tyre = FindTransform(transforms, FlTyreName);
            Camera portalCamera = FindCamera(PortalCameraName);
            if (origin == null || hub == null || tyre == null)
            {
                failure =
                    "The active pit presentation is missing its Hero camera " +
                    "origin, FL hub, or FL tyre.";
                return false;
            }
            if (portalCamera == null || portalCamera.cullingMask == 0)
            {
                failure =
                    "The pit portal camera layer mask is unavailable.";
                return false;
            }
            float tyreDiameter = ResolveVisualDiameter(tyre);
            Vector3 vehicleUp = origin.up;
            Vector3 localHub = origin.InverseTransformPoint(hub.position);
            float side = Mathf.Abs(localHub.x) > 0.0001f
                ? Mathf.Sign(localHub.x)
                : 1f;
            Vector3 outward = origin.TransformDirection(
                Vector3.right * side);
            Vector3 target = Vector3.Lerp(
                    origin.position,
                    hub.position,
                    HeroTargetBlend) +
                vehicleUp * tyreDiameter * HeroTargetHeightInTyres;
            Vector3 forward = (
                -outward -
                origin.forward -
                vehicleUp * HeroDownwardAim).normalized;
            Vector3 position = target -
                forward * tyreDiameter * HeroDistanceInTyres;

            GameObject cameraObject = new(
                "PitRecorder_Hero45",
                typeof(Camera));
            cameraObject.hideFlags = HideFlags.HideAndDontSave;
            cameraObject.tag = HeroCameraTag;
            heroCamera = cameraObject.GetComponent<Camera>();
            heroCamera.enabled = true;
            heroCamera.clearFlags = CameraClearFlags.SolidColor;
            heroCamera.backgroundColor = Color.black;
            heroCamera.cullingMask = portalCamera.cullingMask;
            heroCamera.fieldOfView = HeroFieldOfView;
            heroCamera.nearClipPlane = 0.01f;
            heroCamera.farClipPlane = 1000f;
            heroCamera.aspect = 16f / 9f;
            heroCamera.usePhysicalProperties = false;
            heroCamera.rect = new Rect(0f, 0f, 1f, 1f);
            heroCamera.depth = portalCamera.depth + 1f;
            heroCamera.GetUniversalAdditionalCameraData()
                .allowXRRendering = false;
            heroCamera.transform.SetPositionAndRotation(
                position,
                Quaternion.LookRotation(forward, vehicleUp));
            return true;
        }

        private static bool TrySuppressElevatedOccluder(
            Transform stage,
            out string failure)
        {
            failure = null;
            MeshFilter[] filters =
                stage.GetComponentsInChildren<MeshFilter>(true);
            MeshFilter contextFilter = null;
            for (int i = 0; i < filters.Length; i++)
            {
                MeshFilter candidate = filters[i];
                if (candidate.gameObject.name ==
                        SuzukaContextSurfaceName &&
                    candidate.sharedMesh != null &&
                    candidate.sharedMesh.name == SuzukaContextMeshName)
                {
                    contextFilter = candidate;
                    break;
                }
            }

            if (contextFilter == null)
            {
                failure =
                    "The runtime Suzuka pit context surface is missing.";
                return false;
            }

            MeshRenderer contextRenderer =
                contextFilter.GetComponent<MeshRenderer>();
            Mesh sourceMesh = contextFilter.sharedMesh;
            Material[] materials = contextRenderer != null
                ? contextRenderer.sharedMaterials
                : null;
            if (contextRenderer == null ||
                sourceMesh.subMeshCount <= ElevatedOccluderSubMeshIndex ||
                materials == null ||
                materials.Length <= ElevatedOccluderSubMeshIndex ||
                materials[ElevatedOccluderSubMeshIndex] == null ||
                materials[ElevatedOccluderSubMeshIndex].name !=
                    ElevatedOccluderMaterialName)
            {
                failure =
                    "The validated Suzuka WALL1 occluder submesh mapping " +
                    "is unavailable.";
                return false;
            }

            Mesh validationMesh = UnityEngine.Object.Instantiate(sourceMesh);
            validationMesh.name =
                sourceMesh.name + "_ElevatedValidation";
            validationMesh.hideFlags = HideFlags.HideAndDontSave;
            validationMesh.SetIndices(
                new int[0],
                sourceMesh.GetTopology(ElevatedOccluderSubMeshIndex),
                ElevatedOccluderSubMeshIndex,
                false);

            suppressedElevatedContextFilter = contextFilter;
            suppressedElevatedContextSourceMesh = sourceMesh;
            elevatedValidationContextMesh = validationMesh;
            contextFilter.sharedMesh = validationMesh;

            Debug.Log(
                "[PitRecorder] Elevated validation suppressed " +
                "PitTrack/ContextSurface submesh 1 (WALL1) only. " +
                "Original renderer bounds: " + contextRenderer.bounds + ".");
            return true;
        }

        private static bool TryCreatePortalHeroCamera(
            out string failure)
        {
            failure = null;
            Camera viewer = Camera.main;
            Renderer portalSurface = FindRenderer(PortalSurfaceName);
            if (viewer == null)
            {
                failure =
                    "The production viewer camera is unavailable for Portal Hero framing.";
                return false;
            }
            if (portalSurface == null)
            {
                failure =
                    "The active pit portal surface is unavailable for Portal Hero framing.";
                return false;
            }

            Transform surface = portalSurface.transform;
            Bounds localBounds = portalSurface.localBounds;
            Vector3 scale = surface.lossyScale;
            float width = Mathf.Abs(localBounds.size.x * scale.x);
            float height = Mathf.Abs(localBounds.size.y * scale.y);
            if (width <= 0.01f || height <= 0.01f)
            {
                failure = "The active pit portal has invalid visual bounds.";
                return false;
            }

            float distance = Mathf.Max(width * 0.82f, height * 1.35f);
            Vector3 roomDirection =
                Vector3.Dot(
                    viewer.transform.position - surface.position,
                    surface.forward) >= 0f
                    ? surface.forward
                    : -surface.forward;
            Vector3 target =
                surface.position - roomDirection * width * 0.12f;
            Vector3 position =
                surface.position + roomDirection * distance +
                surface.right * width * 0.1f +
                surface.up * height * 0.04f;

            GameObject cameraObject = new(
                "PitRecorder_PortalHero",
                typeof(Camera));
            cameraObject.hideFlags = HideFlags.HideAndDontSave;
            cameraObject.tag = HeroCameraTag;
            portalHeroCamera = cameraObject.GetComponent<Camera>();
            portalHeroCamera.enabled = true;
            portalHeroCamera.clearFlags = CameraClearFlags.SolidColor;
            portalHeroCamera.backgroundColor =
                new Color(0.12f, 0.13f, 0.15f, 1f);
            portalHeroCamera.cullingMask =
                (viewer.cullingMask | (1 << 2)) & ~(1 << 30);
            portalHeroCamera.fieldOfView = 54f;
            portalHeroCamera.nearClipPlane = 0.03f;
            portalHeroCamera.farClipPlane = viewer.farClipPlane;
            portalHeroCamera.aspect = 16f / 9f;
            portalHeroCamera.usePhysicalProperties = false;
            portalHeroCamera.rect = new Rect(0f, 0f, 1f, 1f);
            portalHeroCamera.depth = viewer.depth + 1f;
            portalHeroCamera.GetUniversalAdditionalCameraData()
                .allowXRRendering = false;
            portalHeroCamera.transform.SetPositionAndRotation(
                position,
                Quaternion.LookRotation(
                    (target - position).normalized,
                    surface.up));
            suppressedPortalHeroUi =
                GameObject.Find(PitPortalControlsName);
            if (suppressedPortalHeroUi != null)
                suppressedPortalHeroUi.SetActive(false);
            return true;
        }

        private static bool TryCreateElevatedFourCornerCamera(
            out string failure)
        {
            failure = null;
            Transform stage = replay.PresentationRoot;
            if (stage == null)
            {
                failure = "The active replay presentation root is missing.";
                return false;
            }

            Transform[] transforms =
                stage.GetComponentsInChildren<Transform>(true);
            Transform origin = FindTransform(
                transforms,
                ChoreographyOriginName);
            Transform flHub = FindTransform(transforms, FlHubName);
            Transform frHub = FindTransform(transforms, FrHubName);
            Transform rlHub = FindTransform(transforms, RlHubName);
            Transform rrHub = FindTransform(transforms, RrHubName);
            Transform flTyre = FindTransform(transforms, FlTyreName);
            Camera portalCamera = FindCamera(PortalCameraName);
            if (origin == null || flHub == null || frHub == null ||
                rlHub == null || rrHub == null || flTyre == null)
            {
                failure =
                    "The active pit presentation is missing its choreography " +
                    "origin, a wheel hub, or the FL tyre reference.";
                return false;
            }
            if (portalCamera == null || portalCamera.cullingMask == 0)
            {
                failure =
                    "The pit portal camera layer mask is unavailable.";
                return false;
            }
            if (!TrySuppressElevatedOccluder(stage, out failure))
                return false;

            Bounds framingBounds = new(origin.position, Vector3.zero);
            framingBounds.Encapsulate(flHub.position);
            framingBounds.Encapsulate(frHub.position);
            framingBounds.Encapsulate(rlHub.position);
            framingBounds.Encapsulate(rrHub.position);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform candidate = transforms[i];
                if (candidate != null &&
                    (IsWheelServiceActorRoot(candidate.name) ||
                     IsLooseTyreRoot(candidate.name)))
                {
                    framingBounds.Encapsulate(candidate.position);
                }
            }

            float tyreDiameter = ResolveVisualDiameter(flTyre);
            framingBounds.Expand(new Vector3(
                tyreDiameter * 2.25f,
                tyreDiameter * 3.5f,
                tyreDiameter * 2.25f));
            Vector3 vehicleUp = origin.up;
            Vector3 target = framingBounds.center +
                vehicleUp * tyreDiameter * 0.12f;
            Vector3 viewDirection = (
                vehicleUp * 1.9f -
                origin.forward * 0.45f -
                origin.right * 0.75f).normalized;
            float radius = Mathf.Max(
                framingBounds.extents.magnitude,
                tyreDiameter * 3f);
            float halfFov =
                ElevatedFourCornerFieldOfView * Mathf.Deg2Rad * 0.5f;
            float distance = radius / Mathf.Sin(halfFov) * 1.05f;
            Vector3 position = target + viewDirection * distance;
            Vector3 forward = (target - position).normalized;

            GameObject cameraObject = new(
                "PitRecorder_ElevatedFourCorner",
                typeof(Camera));
            cameraObject.hideFlags = HideFlags.HideAndDontSave;
            cameraObject.tag = HeroCameraTag;
            elevatedFourCornerCamera = cameraObject.GetComponent<Camera>();
            elevatedFourCornerCamera.enabled = true;
            elevatedFourCornerCamera.clearFlags =
                CameraClearFlags.SolidColor;
            elevatedFourCornerCamera.backgroundColor = Color.black;
            elevatedFourCornerCamera.cullingMask = portalCamera.cullingMask;
            elevatedFourCornerCamera.fieldOfView =
                ElevatedFourCornerFieldOfView;
            elevatedFourCornerCamera.nearClipPlane = 0.01f;
            elevatedFourCornerCamera.farClipPlane = 1000f;
            elevatedFourCornerCamera.aspect = 16f / 9f;
            elevatedFourCornerCamera.usePhysicalProperties = false;
            elevatedFourCornerCamera.rect = new Rect(0f, 0f, 1f, 1f);
            elevatedFourCornerCamera.depth = portalCamera.depth + 1f;
            elevatedFourCornerCamera.GetUniversalAdditionalCameraData()
                .allowXRRendering = false;
            elevatedFourCornerCamera.transform.SetPositionAndRotation(
                position,
                Quaternion.LookRotation(forward, vehicleUp));
            return true;
        }

        private static bool IsWheelServiceActorRoot(string name)
        {
            return name == "FL_WheelGunner" ||
                name == "FR_WheelGunner" ||
                name == "RL_WheelGunner" ||
                name == "RR_WheelGunner" ||
                name.StartsWith("FL_WheelOff_") ||
                name.StartsWith("FR_WheelOff_") ||
                name.StartsWith("RL_WheelOff_") ||
                name.StartsWith("RR_WheelOff_") ||
                name.StartsWith("FL_WheelOn_") ||
                name.StartsWith("FR_WheelOn_") ||
                name.StartsWith("RL_WheelOn_") ||
                name.StartsWith("RR_WheelOn_");
        }

        private static bool IsLooseTyreRoot(string name)
        {
            return name.EndsWith("_OldLooseTyre") ||
                name.EndsWith("_NewLooseTyre");
        }

        private static Transform FindTransform(
            Transform[] transforms,
            string name)
        {
            for (int i = 0; i < transforms.Length; i++)
            {
                if (transforms[i].name == name)
                    return transforms[i];
            }

            return null;
        }

        private static Camera FindCamera(string name)
        {
            Camera[] cameras = UnityEngine.Object.FindObjectsByType<Camera>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i].name == name)
                    return cameras[i];
            }

            return null;
        }

        private static Renderer FindRenderer(string name)
        {
            Renderer[] renderers =
                UnityEngine.Object.FindObjectsByType<Renderer>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i].name == name)
                    return renderers[i];
            }

            return null;
        }

        private static float ResolveVisualDiameter(Transform target)
        {
            Renderer[] renderers =
                target.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            Bounds bounds = default;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return hasBounds
                ? Mathf.Max(
                    bounds.size.x,
                    Mathf.Max(bounds.size.y, bounds.size.z))
                : 0.72f;
        }

        private static string DescribeHeroCamera()
        {
            return heroCamera == null
                ? "Hero camera unavailable."
                : "Position=" + heroCamera.transform.position.ToString("F3") +
                  ", Rotation=" +
                  heroCamera.transform.rotation.eulerAngles.ToString("F2") +
                  ", FOV=" + heroCamera.fieldOfView.ToString("F1") + ".";
        }

        private static string DescribePortalHeroCamera()
        {
            return portalHeroCamera == null
                ? "Portal Hero camera unavailable."
                : "Position=" +
                  portalHeroCamera.transform.position.ToString("F3") +
                  ", Rotation=" +
                  portalHeroCamera.transform.rotation.eulerAngles
                      .ToString("F2") +
                  ", FOV=" +
                  portalHeroCamera.fieldOfView.ToString("F1") +
                  ", Mask=" + portalHeroCamera.cullingMask + ".";
        }

        private static string DescribeElevatedFourCornerCamera()
        {
            return elevatedFourCornerCamera == null
                ? "ElevatedFourCorner camera unavailable."
                : "Position=" +
                  elevatedFourCornerCamera.transform.position.ToString("F3") +
                  ", Rotation=" +
                  elevatedFourCornerCamera.transform.rotation.eulerAngles
                      .ToString("F2") +
                  ", FOV=" +
                  elevatedFourCornerCamera.fieldOfView.ToString("F1") +
                  ", Mask=" + elevatedFourCornerCamera.cullingMask + ".";
        }

        private static void RecoverStaleEditModeState()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                !SessionState.GetBool(PendingKey, false))
            {
                return;
            }

            if (SessionState.GetBool(RecordingKey, false))
            {
                Debug.LogError(
                    "[PitRecorder] Cleared stale recording state after an " +
                    "Edit Mode domain reload.");
                CleanupRecorderObjects();
                ClearSessionState();
                return;
            }

            VerifyOutput();
        }

        private static void UpdateRecording()
        {
            if (recorder == null)
            {
                Fail(
                    "Recorder state was lost during a domain reload. " +
                    "The capture was stopped without retrying Play Mode.");
                return;
            }

            if (EditorApplication.timeSinceStartup - recordingStartedAt >
                RecordingTimeoutSeconds)
            {
                Fail("Recording exceeded the 90 second safety timeout.");
                return;
            }

            if (replay == null)
            {
                replay = UnityEngine.Object
                    .FindFirstObjectByType<EventPopoutReplay>();
            }

            if (replay == null)
            {
                Fail("The active pit-stop replay disappeared.");
                return;
            }

            if (replay.IsPlaying)
                observedPlayback = true;

            bool departureComplete =
                observedPlayback &&
                (!replay.IsPlaying ||
                 replay.CurrentTime >= replayEndTime - 0.02f);
            if (!departureComplete)
            {
                stopAfterTime = -1d;
                return;
            }

            if (stopAfterTime < 0d)
            {
                stopAfterTime =
                    EditorApplication.timeSinceStartup + TailSeconds;
                return;
            }

            if (EditorApplication.timeSinceStartup < stopAfterTime)
                return;

            StopRecording(true, null);
        }

        private static bool HasTimedOut(double seconds)
        {
            string ticksText =
                SessionState.GetString(StartedTicksKey, "");
            long ticks;
            if (!long.TryParse(ticksText, out ticks))
                return false;

            return DateTime.UtcNow -
                new DateTime(ticks, DateTimeKind.Utc) >
                TimeSpan.FromSeconds(seconds);
        }

        private static void Fail(string message)
        {
            StopRecording(false, message);
        }

        private static void StopRecording(
            bool succeeded,
            string failure)
        {
            if (recorder != null && recorder.IsRecording())
                recorder.StopRecording();

            SessionState.SetBool(RecordingKey, false);
            SessionState.SetBool(SucceededKey, succeeded);

            if (!string.IsNullOrEmpty(failure))
                Debug.LogError("[PitRecorder] " + failure);

            CleanupRecorderObjects();

            if (EditorApplication.isPlaying)
                EditorApplication.isPlaying = false;
        }

        private static void OnPlayModeStateChanged(
            PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                RestoreElevatedOccluder();
                return;
            }

            if (state != PlayModeStateChange.EnteredEditMode ||
                !SessionState.GetBool(PendingKey, false))
            {
                return;
            }

            EditorApplication.delayCall += VerifyOutput;
        }

        private static void VerifyOutput()
        {
            string outputPath =
                SessionState.GetString(OutputBaseKey, "") + ".mp4";
            bool expectedSuccess =
                SessionState.GetBool(SucceededKey, false);
            FileInfo file = File.Exists(outputPath)
                ? new FileInfo(outputPath)
                : null;

            if (expectedSuccess &&
                file != null &&
                file.Length > 0)
            {
                Debug.Log(
                    "[PitRecorder] MP4 created: " +
                    outputPath +
                    " (" + file.Length + " bytes)");
            }
            else
            {
                Debug.LogError(
                    "[PitRecorder] Recording did not produce a non-empty MP4: " +
                    outputPath);
            }

            ClearSessionState();
        }

        private static void CleanupRecorderObjects()
        {
            recorder = null;

            RestoreElevatedOccluder();

            if (suppressedPortalHeroUi != null)
                suppressedPortalHeroUi.SetActive(true);

            if (heroCamera != null)
            {
                UnityEngine.Object.DestroyImmediate(
                    heroCamera.gameObject);
            }
            if (portalHeroCamera != null)
            {
                UnityEngine.Object.DestroyImmediate(
                    portalHeroCamera.gameObject);
            }
            if (elevatedFourCornerCamera != null)
            {
                UnityEngine.Object.DestroyImmediate(
                    elevatedFourCornerCamera.gameObject);
            }

            if (movieSettings != null)
                UnityEngine.Object.DestroyImmediate(movieSettings);
            if (controllerSettings != null)
                UnityEngine.Object.DestroyImmediate(controllerSettings);

            movieSettings = null;
            controllerSettings = null;
            replay = null;
            heroCamera = null;
            portalHeroCamera = null;
            elevatedFourCornerCamera = null;
            suppressedPortalHeroUi = null;
            observedPlayback = false;
            stopAfterTime = -1d;
        }

        private static void RestoreElevatedOccluder()
        {
            bool restored = false;
            if (suppressedElevatedContextFilter != null &&
                suppressedElevatedContextSourceMesh != null)
            {
                suppressedElevatedContextFilter.sharedMesh =
                    suppressedElevatedContextSourceMesh;
                restored = true;
            }

            if (elevatedValidationContextMesh != null)
            {
                UnityEngine.Object.DestroyImmediate(
                    elevatedValidationContextMesh);
            }

            suppressedElevatedContextFilter = null;
            suppressedElevatedContextSourceMesh = null;
            elevatedValidationContextMesh = null;

            if (restored)
            {
                Debug.Log(
                    "[PitRecorder] Restored the Suzuka WALL1 context " +
                    "submesh after Elevated validation.");
            }
        }

        private static void ClearSessionState()
        {
            SessionState.EraseBool(PendingKey);
            SessionState.EraseBool(RecordingKey);
            SessionState.EraseBool(SucceededKey);
            SessionState.EraseString(OutputBaseKey);
            SessionState.EraseString(StartedTicksKey);
            SessionState.EraseBool(HeroKey);
            SessionState.EraseBool(PortalHeroKey);
            SessionState.EraseBool(ElevatedFourCornerKey);
        }
    }

}
