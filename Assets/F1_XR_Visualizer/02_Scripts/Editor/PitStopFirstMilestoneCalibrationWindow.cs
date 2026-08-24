using System.IO;
using System.Collections.Generic;
using System.Text;
using F1XR.RestAPI.Replay;
using UnityEditor;
using UnityEngine;

namespace F1XR.Editor
{
    public sealed class PitStopFirstMilestoneCalibrationWindow : EditorWindow
    {
        private const float ServiceDuration = 2.8f;

        private static readonly CalibrationFrame[] Frames =
        {
            new("Ready", 0f),
            new("Gun / jack contact", 0.7f),
            new("Wheel Off takes old tyre", 1.1f),
            new("Old tyre clears hub", 1.5f),
            new("Wheel On reaches hub", 1.68f),
            new("Gunner tighten", 1.96f),
            new("Crew clear", ServiceDuration),
        };

        private static readonly string[] RoleNames =
        {
            "FL_WheelGunner",
            "FL_WheelOff_L",
            "FL_WheelOn_L",
            "FrontJack",
            "RearJack_R",
            "PitSignal_R",
        };

        private EventPopoutReplay replay;
        private float choreographyTime;

        [MenuItem("F1XR/Pit Stop/FL First Milestone Calibration")]
        private static void Open()
        {
            GetWindow<PitStopFirstMilestoneCalibrationWindow>(
                "FL Pit Calibration");
        }

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            TryFindReplay();
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            if (EditorApplication.isPlaying && replay != null)
                replay.ClearPitStopCalibrationTime();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField(
                "FL First Milestone",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Play Mode에서 Pit Stop 이벤트를 연 뒤 사용하세요. " +
                "차량은 실제 정지 중심에 고정되고 승무원/타이어만 선택한 " +
                "접촉 프레임으로 샘플링됩니다.",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.ObjectField(
                    "Event Replay",
                    replay,
                    typeof(EventPopoutReplay),
                    true);
                if (GUILayout.Button("Find", GUILayout.Width(54f)))
                    TryFindReplay();
            }

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Unity Play Mode가 필요합니다.",
                    MessageType.Warning);
                return;
            }

            EditorGUI.BeginChangeCheck();
            choreographyTime = EditorGUILayout.Slider(
                "Choreography Time",
                choreographyTime,
                0f,
                ServiceDuration);
            if (EditorGUI.EndChangeCheck())
                ApplyFrame(choreographyTime);

            for (int i = 0; i < Frames.Length; i++)
            {
                CalibrationFrame frame = Frames[i];
                if (GUILayout.Button(
                        $"{frame.Label}  ({frame.Time:0.00}s)"))
                {
                    choreographyTime = frame.Time;
                    ApplyFrame(frame.Time);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Scene View inspection",
                EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("45° FL cluster"))
                    FrameFlCluster();
                if (GUILayout.Button("Stop calibration"))
                    StopCalibration();
            }

            if (GUILayout.Button("Run forward / backward seek probe"))
                RunSeekProbe();

            for (int i = 0; i < RoleNames.Length; i += 2)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    FocusRole(RoleNames[i]);
                    if (i + 1 < RoleNames.Length)
                        FocusRole(RoleNames[i + 1]);
                }
            }
        }

        private void ApplyFrame(float time)
        {
            if (replay == null)
                TryFindReplay();

            if (replay == null ||
                !replay.TryPauseAtPitStopCalibrationTime(time))
            {
                Debug.LogWarning(
                    "[PitChoreography] Active Pit Stop first milestone " +
                    "was not found. Open the Pit Stop event in Play Mode first.");
                return;
            }

            SceneView.RepaintAll();
            Repaint();
        }

        private void StopCalibration()
        {
            replay?.ClearPitStopCalibrationTime();
            SceneView.RepaintAll();
        }

        private void RunSeekProbe()
        {
            if (replay == null)
                TryFindReplay();
            if (replay == null)
                return;

            RunSeekProbeForReplay(replay);
        }

        internal static bool RunSeekProbeForReplay(EventPopoutReplay replay)
        {
            if (replay == null)
                return false;

            const float targetTime = 1.68f;
            for (int i = 0; i <= 16; i++)
                replay.TryPauseAtPitStopCalibrationTime(i * 0.1f);
            replay.TryPauseAtPitStopCalibrationTime(targetTime);
            Dictionary<string, TransformState> forward =
                CaptureChoreographyState();

            replay.TryPauseAtPitStopCalibrationTime(0.4f);
            replay.TryPauseAtPitStopCalibrationTime(targetTime);
            Dictionary<string, TransformState> backward =
                CaptureChoreographyState();

            if (StatesMatch(forward, backward, out string mismatch))
            {
                Debug.Log(
                    "[PitChoreography] Forward/backward seek probe passed " +
                    $"for {forward.Count} transforms at {targetTime:0.00}s.");
                return true;
            }

            Debug.LogError(
                "[PitChoreography] Forward/backward seek probe failed: " +
                mismatch);
            return false;
        }

        private void TryFindReplay()
        {
            replay = Object.FindAnyObjectByType<EventPopoutReplay>(
                FindObjectsInactive.Include);
            Repaint();
        }

        internal static void FrameFlCluster()
        {
            Transform hub = FindRuntimeTransform("FL_Hub");
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (hub == null || sceneView == null)
                return;

            float size = ResolveFlClusterViewSize(hub);
            Transform origin = FindRuntimeTransform(
                "PitChoreographyOrigin");
            Quaternion vehicleAlignedRotation =
                origin != null ? origin.rotation : Quaternion.identity;
            Vector3 cameraForward =
                vehicleAlignedRotation *
                new Vector3(-1f, -0.32f, -1f).normalized;
            sceneView.LookAt(
                hub.position,
                Quaternion.LookRotation(
                    cameraForward,
                    vehicleAlignedRotation * Vector3.up),
                size,
                false,
                true);
            sceneView.Repaint();
        }

        private static float ResolveFlClusterViewSize(Transform hub)
        {
            Transform tyre = FindRuntimeTransform("FL_Tire");
            if (tyre == null)
                return Mathf.Max(1f, hub.lossyScale.x * 4f);

            Renderer[] renderers =
                tyre.GetComponentsInChildren<Renderer>(true);
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

            if (!hasBounds)
                return Mathf.Max(1f, hub.lossyScale.x * 4f);

            float tyreDiameter = Mathf.Max(
                bounds.size.x,
                Mathf.Max(bounds.size.y, bounds.size.z));
            return Mathf.Max(1f, tyreDiameter * 3.2f);
        }

        private static void FocusRole(string roleName)
        {
            if (!GUILayout.Button(roleName))
                return;

            Transform role = FindRuntimeTransform(roleName);
            if (role == null)
                return;

            Selection.activeGameObject = role.gameObject;
            SceneView.lastActiveSceneView?.FrameSelected();
        }

        internal static Transform FindRuntimeTransform(string name)
        {
            Transform[] transforms = Resources.FindObjectsOfTypeAll<Transform>();
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform candidate = transforms[i];
                if (candidate != null &&
                    candidate.gameObject.scene.IsValid() &&
                    candidate.name == name)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static Dictionary<string, TransformState>
            CaptureChoreographyState()
        {
            Dictionary<string, TransformState> result = new();
            Transform root = FindRuntimeTransform("PitChoreographyOrigin");
            if (root == null)
                return result;

            CaptureTransformRecursive(root, root.name, result);
            return result;
        }

        private static void CaptureTransformRecursive(
            Transform current,
            string path,
            Dictionary<string, TransformState> destination)
        {
            destination[path] = new TransformState(current);
            for (int i = 0; i < current.childCount; i++)
            {
                Transform child = current.GetChild(i);
                CaptureTransformRecursive(
                    child,
                    $"{path}/{child.name}[{i}]",
                    destination);
            }
        }

        private static bool StatesMatch(
            Dictionary<string, TransformState> first,
            Dictionary<string, TransformState> second,
            out string mismatch)
        {
            mismatch = string.Empty;
            if (first.Count != second.Count)
            {
                mismatch = $"transform count {first.Count} != {second.Count}";
                return false;
            }

            foreach (KeyValuePair<string, TransformState> pair in first)
            {
                if (!second.TryGetValue(
                        pair.Key,
                        out TransformState candidate))
                {
                    mismatch = $"missing {pair.Key}";
                    return false;
                }

                if (!pair.Value.NearlyEquals(candidate))
                {
                    mismatch = pair.Key;
                    return false;
                }
            }

            return true;
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
                TryFindReplay();
            else if (state == PlayModeStateChange.ExitingPlayMode)
                replay = null;
        }

        private readonly struct CalibrationFrame
        {
            public CalibrationFrame(string label, float time)
            {
                Label = label;
                Time = time;
            }

            public string Label { get; }
            public float Time { get; }
        }

        private readonly struct TransformState
        {
            private readonly Vector3 localPosition;
            private readonly Quaternion localRotation;
            private readonly Vector3 localScale;
            private readonly bool active;

            public TransformState(Transform source)
            {
                localPosition = source.localPosition;
                localRotation = source.localRotation;
                localScale = source.localScale;
                active = source.gameObject.activeSelf;
            }

            public bool NearlyEquals(TransformState other)
            {
                return active == other.active &&
                    Vector3.Distance(
                        localPosition,
                        other.localPosition) <= 0.0001f &&
                    Quaternion.Angle(
                        localRotation,
                        other.localRotation) <= 0.02f &&
                    Vector3.Distance(
                        localScale,
                        other.localScale) <= 0.0001f;
            }
        }
    }

    [InitializeOnLoad]
    internal static class PitStopFirstMilestoneCalibrationAutomation
    {
        private const string RequestFileName = "run-audit.request";
        private const string PathRequestFileName = "run-path-audit.request";

        private static readonly (string Label, float Time)[] AuditFrames =
        {
            ("00_ready", 0f),
            ("01_contact", 0.7f),
            ("02_old_tyre_take", 1.1f),
            ("03_old_tyre_clear", 1.5f),
            ("04_new_tyre_hub", 1.68f),
            ("05_tighten", 1.96f),
            ("06_crew_clear", 2.8f),
        };

        private static EventPopoutReplay auditReplay;
        private static SceneView auditSceneView;
        private static int auditFrameIndex = -1;
        private static bool waitingForRender;
        private static double nextAuditStepTime;

        static PitStopFirstMilestoneCalibrationAutomation()
        {
            EditorApplication.update += PollForAuditRequest;
        }

        private static void PollForAuditRequest()
        {
            if (auditFrameIndex >= 0)
            {
                AdvanceAudit();
                return;
            }

            string pathRequestPath = GetOutputPath(PathRequestFileName);
            if (File.Exists(pathRequestPath))
            {
                if (!EditorApplication.isPlaying)
                    return;

                EventPopoutReplay pathReplay =
                    Object.FindAnyObjectByType<EventPopoutReplay>(
                        FindObjectsInactive.Include);
                if (pathReplay == null || !RunPathAudit(pathReplay))
                    return;

                File.Delete(pathRequestPath);
                return;
            }

            string requestPath = GetOutputPath(RequestFileName);
            if (!File.Exists(requestPath))
                return;

            if (!EditorApplication.isPlaying)
                return;

            auditReplay =
                Object.FindAnyObjectByType<EventPopoutReplay>(
                    FindObjectsInactive.Include);
            auditSceneView = SceneView.lastActiveSceneView;
            if (auditReplay == null || auditSceneView == null)
                return;

            if (!auditReplay.TryPauseAtPitStopCalibrationTime(0f))
                return;

            File.Delete(requestPath);
            Directory.CreateDirectory(GetOutputPath(string.Empty));
            for (int i = 0; i < AuditFrames.Length; i++)
            {
                string label = AuditFrames[i].Label;
                DeleteIfPresent(GetOutputPath($"{label}.png"));
                DeleteIfPresent(GetOutputPath($"{label}.txt"));
            }

            auditFrameIndex = 0;
            waitingForRender = false;
            nextAuditStepTime = EditorApplication.timeSinceStartup;
        }

        private static void AdvanceAudit()
        {
            if (!EditorApplication.isPlaying ||
                auditReplay == null ||
                auditSceneView == null)
            {
                AbortAudit("Play Mode or audit target was lost.");
                return;
            }

            if (EditorApplication.timeSinceStartup < nextAuditStepTime)
                return;

            if (auditFrameIndex >= AuditFrames.Length)
            {
                bool seekPassed =
                    PitStopFirstMilestoneCalibrationWindow
                        .RunSeekProbeForReplay(auditReplay);
                Debug.Log(
                    "[PitChoreography] Automated audit captured " +
                    $"{AuditFrames.Length} frames. Seek probe passed: " +
                    seekPassed);
                auditReplay = null;
                auditSceneView = null;
                auditFrameIndex = -1;
                waitingForRender = false;
                return;
            }

            (string label, float time) = AuditFrames[auditFrameIndex];
            if (!waitingForRender)
            {
                if (!auditReplay.TryPauseAtPitStopCalibrationTime(time))
                {
                    AbortAudit($"Could not apply {time:0.00}s.");
                    return;
                }

                PitStopFirstMilestoneCalibrationWindow.FrameFlCluster();
                SceneView.RepaintAll();
                EditorApplication.QueuePlayerLoopUpdate();
                waitingForRender = true;
                nextAuditStepTime =
                    EditorApplication.timeSinceStartup + 0.12d;
                return;
            }

            CaptureSceneView(
                auditSceneView,
                GetOutputPath($"{label}.png"));
            WriteFrameSnapshot(
                GetOutputPath($"{label}.txt"),
                time);
            auditFrameIndex++;
            waitingForRender = false;
            nextAuditStepTime =
                EditorApplication.timeSinceStartup + 0.04d;
        }

        private static void AbortAudit(string reason)
        {
            Debug.LogError(
                "[PitChoreography] Automated audit aborted: " + reason);
            auditReplay = null;
            auditSceneView = null;
            auditFrameIndex = -1;
            waitingForRender = false;
        }

        private static void CaptureSceneView(
            SceneView sceneView,
            string path)
        {
            const int width = 1440;
            const int height = 900;
            Camera camera = sceneView.camera;
            RenderTexture target = new(
                width,
                height,
                24,
                RenderTextureFormat.ARGB32);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;

            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;

                Texture2D image = new(
                    width,
                    height,
                    TextureFormat.RGB24,
                    false);
                image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
                Object.DestroyImmediate(image);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        private static string GetOutputPath(string fileName)
        {
            return Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "Temp",
                "PitValidation",
                "Automated",
                fileName));
        }

        private static void WriteFrameSnapshot(string path, float time)
        {
            StringBuilder text = new();
            text.AppendLine($"time={time:0.000}");
            string[] names =
            {
                "FL_Hub",
                "FL_Tire",
                "FL_OldLooseTyre",
                "FL_NewLooseTyre",
                "FL_WheelGun",
                "FL_WheelGunner",
                "FL_WheelOff_L",
                "FL_WheelOn_L",
                "FrontJack",
                "RearJack_R",
                "PitSignal_R",
            };

            for (int i = 0; i < names.Length; i++)
                AppendObjectSnapshot(text, names[i]);

            File.WriteAllText(path, text.ToString());
        }

        private static bool RunPathAudit(EventPopoutReplay replay)
        {
            if (!replay.TryGetPitStopCalibrationRange(
                    out float serviceStart,
                    out float focus,
                    out float serviceEnd,
                    out float pitStopDuration,
                    out float pitLaneDuration))
            {
                return false;
            }

            Transform tyre =
                PitStopFirstMilestoneCalibrationWindow
                    .FindRuntimeTransform("FL_Tire");
            if (tyre == null)
                return false;

            const int sampleCount = 41;
            StringBuilder text = new();
            text.AppendLine(
                $"serviceStart={serviceStart:0.000}" +
                $"|focus={focus:0.000}" +
                $"|serviceEnd={serviceEnd:0.000}" +
                $"|serviceDuration={serviceEnd - serviceStart:0.000}" +
                $"|pitStopDuration={pitStopDuration:0.000}" +
                $"|pitLaneDuration={pitLaneDuration:0.000}");
            Vector3 firstCenter = default;
            for (int i = 0; i < sampleCount; i++)
            {
                float normalized = i / (sampleCount - 1f);
                float replayTime = Mathf.Lerp(
                    serviceStart,
                    serviceEnd,
                    normalized);
                if (!replay.TryPauseAtPitStopReplayTime(replayTime) ||
                    !TryGetVisualCenter(tyre, out Vector3 center))
                {
                    return false;
                }

                if (i == 0)
                    firstCenter = center;
                text.AppendLine(
                    $"time={replayTime:0.000}" +
                    $"|normalized={normalized:0.000}" +
                    $"|center=({center.x:0.0000}," +
                    $"{center.y:0.0000},{center.z:0.0000})" +
                    $"|fromStart={Vector3.Distance(firstCenter, center):0.0000}");
            }

            replay.TryPauseAtPitStopReplayTime(focus);
            string outputPath = GetOutputPath("service-path.txt");
            File.WriteAllText(outputPath, text.ToString());
            Debug.Log(
                "[PitChoreography] Service path audit written to " +
                outputPath);
            return true;
        }

        private static bool TryGetVisualCenter(
            Transform target,
            out Vector3 center)
        {
            center = target != null ? target.position : Vector3.zero;
            if (target == null)
                return false;

            Renderer[] renderers =
                target.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            Bounds bounds = default;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled)
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

            if (hasBounds)
                center = bounds.center;
            return true;
        }

        private static void AppendObjectSnapshot(
            StringBuilder destination,
            string name)
        {
            Transform target =
                PitStopFirstMilestoneCalibrationWindow
                    .FindRuntimeTransform(name);
            if (target == null)
            {
                destination.AppendLine($"{name}|missing");
                return;
            }

            Vector3 position = target.position;
            destination.Append(
                $"{name}|active={target.gameObject.activeInHierarchy}" +
                $"|position=({position.x:0.0000}," +
                $"{position.y:0.0000},{position.z:0.0000})");

            Renderer[] renderers =
                target.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            Bounds bounds = default;
            int enabledRendererCount = 0;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled)
                    continue;

                enabledRendererCount++;
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

            destination.Append($"|enabledRenderers={enabledRendererCount}");
            if (hasBounds)
            {
                Vector3 center = bounds.center;
                destination.Append(
                    $"|visualCenter=({center.x:0.0000}," +
                    $"{center.y:0.0000},{center.z:0.0000})");
            }
            destination.AppendLine();
        }

        private static void DeleteIfPresent(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
