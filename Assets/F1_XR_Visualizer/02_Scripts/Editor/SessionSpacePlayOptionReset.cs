using System.IO;
using F1XR.Debugging;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace F1XR.EditorTools
{
    [InitializeOnLoad]
    internal static class SessionSpacePlayOptionReset
    {
        const string SceneDirectory = "Assets/F1_XR_Visualizer/01_Scenes";
        const string ScenePrefix = "SessionSpace";

        static bool sessionSpacePlayed;

        static SessionSpacePlayOptionReset()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (EditorApplication.isPlaying &&
                scene.name.StartsWith(ScenePrefix))
            {
                sessionSpacePlayed = true;
            }
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    if (SceneManager.GetSceneAt(i).name.StartsWith(ScenePrefix))
                    {
                        sessionSpacePlayed = true;
                        break;
                    }
                }

                return;
            }

            if (state != PlayModeStateChange.EnteredEditMode ||
                !sessionSpacePlayed)
            {
                return;
            }

            sessionSpacePlayed = false;
            EditorApplication.delayCall += ResetPlayOptions;
        }

        static void ResetPlayOptions()
        {
            string[] sceneGuids = AssetDatabase.FindAssets(
                "t:Scene", new[] { SceneDirectory });
            foreach (string sceneGuid in sceneGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(sceneGuid);
                if (!Path.GetFileNameWithoutExtension(path).StartsWith(
                        ScenePrefix))
                {
                    continue;
                }

                Scene scene = SceneManager.GetSceneByPath(path);
                bool openedForReset = !scene.isLoaded;
                if (openedForReset)
                    scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

                if (scene.isDirty)
                {
                    Debug.LogWarning(
                        "[SessionSpace] Skipped resetting Play options in the " +
                        "unsaved scene: " + path);
                    if (openedForReset)
                        EditorSceneManager.CloseScene(scene, true);
                    continue;
                }

                bool changed = false;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (SessionSpaceDebugger debugger in
                             root.GetComponentsInChildren<SessionSpaceDebugger>(
                                 true))
                    {
                        changed |= ResetPlayOptions(debugger);
                    }
                }

                if (changed)
                    EditorSceneManager.SaveScene(scene);

                if (openedForReset)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        static bool ResetPlayOptions(SessionSpaceDebugger debugger)
        {
            SerializedObject serializedDebugger = new(debugger);
            bool changed = SetFalse(
                serializedDebugger,
                "skipSpatialSetupOnPlay");
            changed |= SetFalse(
                serializedDebugger,
                "showTablePlacementCandidatesOnPlay");
            changed |= SetTrue(
                serializedDebugger,
                "placeTemporaryMapInFrontOfVrOrigin");
            changed |= SetFalse(
                serializedDebugger,
                "enterVrDroneOnPlay");

            if (changed)
                serializedDebugger.ApplyModifiedPropertiesWithoutUndo();

            return changed;
        }

        static bool SetFalse(SerializedObject target, string propertyName)
        {
            SerializedProperty property = target.FindProperty(propertyName);
            if (property == null || !property.boolValue)
                return false;

            property.boolValue = false;
            return true;
        }

        static bool SetTrue(SerializedObject target, string propertyName)
        {
            SerializedProperty property = target.FindProperty(propertyName);
            if (property == null || property.boolValue)
                return false;

            property.boolValue = true;
            return true;
        }
    }
}
