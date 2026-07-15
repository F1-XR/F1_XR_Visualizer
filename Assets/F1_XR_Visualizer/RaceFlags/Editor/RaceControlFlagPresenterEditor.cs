using F1XR.RaceFlags;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using F1XR.RestAPI.Replay;

namespace F1XR.RaceFlags.Editor
{
    [InitializeOnLoad]
    internal static class RaceControlFlagPresenterPlayModeEnabler
    {
        static RaceControlFlagPresenterPlayModeEnabler()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        [MenuItem("Tools/F1 XR/Race Flags/Enable All Presenters In Active Scene")]
        public static void EnableAllPresentersInActiveScene()
        {
            int count = EnableAllPresenters(markSceneDirty: true);
            EditorUtility.DisplayDialog(
                "Race Flag Presenter",
                $"Enabled {count} RaceControlFlagPresenter component(s) in the active scene.",
                "OK");
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredPlayMode)
                EnableAllPresenters(markSceneDirty: state == PlayModeStateChange.ExitingEditMode);
        }

        private static int EnableAllPresenters(bool markSceneDirty)
        {
            RaceControlFlagPresenter[] presenters = UnityEngine.Object.FindObjectsByType<RaceControlFlagPresenter>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            bool changed = false;
            foreach (RaceControlFlagPresenter presenter in presenters)
            {
                if (presenter == null)
                    continue;

                if (!presenter.gameObject.activeSelf)
                {
                    presenter.gameObject.SetActive(true);
                    changed = true;
                }

                if (!presenter.enabled)
                {
                    presenter.enabled = true;
                    changed = true;
                }

                if (markSceneDirty)
                    EditorUtility.SetDirty(presenter);
            }

            if (changed && markSceneDirty)
            {
                Scene activeScene = SceneManager.GetActiveScene();
                if (activeScene.IsValid())
                    EditorSceneManager.MarkSceneDirty(activeScene);
            }

            return presenters.Length;
        }
    }

    [CustomEditor(typeof(RaceControlFlagPresenter))]
    public sealed class RaceControlFlagPresenterEditor : UnityEditor.Editor
    {
        private const string FlagPrefabPath = "Assets/F1_XR_Visualizer/RaceFlags/Prefabs/RaceFlagAlert.prefab";
        private const string PresenterObjectName = "RaceControlFlagPresenter_TEST";

        [MenuItem("Tools/F1 XR/Race Flags/Place Runtime Presenter in Active Test Scene")]
        public static void PlaceRuntimePresenterInActiveTestScene()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || string.IsNullOrEmpty(activeScene.path))
            {
                EditorUtility.DisplayDialog(
                    "Race Flag Presenter",
                    "The active scene must be valid and saved. Save your test scene first.",
                    "OK");
                return;
            }

            GameObject flagPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FlagPrefabPath);
            if (flagPrefab == null)
            {
                EditorUtility.DisplayDialog(
                    "Race Flag Presenter",
                    "Create the race flag prefab first with Tools > F1 XR > Race Flags > Create or Update Prefab.",
                    "OK");
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "Place Race Flag Presenter",
                "Only the currently active scene will be changed.\n\n" +
                $"Active scene: {activeScene.name}\n" +
                $"Scene path: {activeScene.path}\n" +
                $"Flag prefab path: {FlagPrefabPath}\n\n" +
                "The presenter will not save the scene automatically.",
                "Place Presenter",
                "Cancel");

            if (!confirmed)
                return;

            GameObject presenterObject = GameObject.Find(PresenterObjectName);
            bool created = presenterObject == null;
            if (created)
            {
                presenterObject = new GameObject(PresenterObjectName);
                Undo.RegisterCreatedObjectUndo(presenterObject, "Place Race Control Flag Presenter");
                SceneManager.MoveGameObjectToScene(presenterObject, activeScene);
            }

            RaceControlFlagPresenter presenter = presenterObject.GetComponent<RaceControlFlagPresenter>();
            if (presenter == null)
                presenter = presenterObject.AddComponent<RaceControlFlagPresenter>();

            presenterObject.SetActive(true);
            presenter.enabled = true;

            SerializedObject serializedPresenter = new SerializedObject(presenter);
            serializedPresenter.FindProperty("replayPlayer").objectReferenceValue = Object.FindAnyObjectByType<ReplayPlayer>();
            serializedPresenter.FindProperty("mapRootOverride").objectReferenceValue = null;
            serializedPresenter.FindProperty("raceFlagPrefab").objectReferenceValue = flagPrefab;
            serializedPresenter.FindProperty("missingEndFallbackDuration").floatValue = 5.0f;
            serializedPresenter.ApplyModifiedPropertiesWithoutUndo();

            Selection.activeGameObject = presenterObject;
            EditorSceneManager.MarkSceneDirty(activeScene);

            EditorUtility.DisplayDialog(
                "Race Flag Presenter",
                created
                    ? "RaceControlFlagPresenter_TEST was created and enabled."
                    : "Existing RaceControlFlagPresenter_TEST was selected, enabled, and updated.",
                "OK");
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Runtime Test Actions", EditorStyles.boldLabel);

            RaceControlFlagPresenter presenter = (RaceControlFlagPresenter)target;

            if (!presenter.enabled)
            {
                EditorGUILayout.HelpBox(
                    "The RaceControlFlagPresenter component is disabled. It will not read replay time or show flags.",
                    MessageType.Warning);

                if (GUILayout.Button("Enable Presenter Component"))
                {
                    Undo.RecordObject(presenter, "Enable Race Control Flag Presenter");
                    presenter.enabled = true;
                    EditorUtility.SetDirty(presenter);
                }
            }

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("Simulate Yellow Flag"))
                    presenter.SimulateYellowFlag();

                if (GUILayout.Button("Simulate Red Flag"))
                    presenter.SimulateRedFlag();

                if (GUILayout.Button("Simulate Race Finish / Game Set"))
                    presenter.SimulateRaceFinish();

                if (GUILayout.Button("Clear Test Overrides"))
                    presenter.ClearTestOverrides();

                if (GUILayout.Button("Re-evaluate Current Replay Time"))
                    presenter.ReevaluateCurrentReplayTime();
            }

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Runtime simulation requires Play Mode. These buttons do not edit server data or scene data while outside Play Mode.",
                    MessageType.Info);
            }
        }
    }
}
