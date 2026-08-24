using System;
using System.Collections;
using F1XR.Drone;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace F1XR.Bootstrap
{
    [DisallowMultipleComponent]
    public sealed class BootstrapLoader : MonoBehaviour
    {
        const string DroneHostScenePrefix = "SessionSpace";
        const string FitinSceneName = "SessionSpace_fitin";

        [SerializeField] string initialSceneName = "HomeSpace";
        [SerializeField] string vrDroneSceneName = "VRDroneSpace";

        static BootstrapLoader instance;
        bool isLoadingDroneScene;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void CreateForDirectHostScenePlay()
        {
            if (instance != null)
                return;

            Scene activeScene = SceneManager.GetActiveScene();
            if (!IsDirectPlayHostScene(activeScene.name))
                return;

            new GameObject(nameof(BootstrapLoader))
                .AddComponent<BootstrapLoader>();
        }

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void Start()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (IsDroneHostScene(activeScene.name))
            {
                StartCoroutine(LoadDroneScene(activeScene));
                return;
            }

            if (!SceneManager.GetSceneByName(initialSceneName).isLoaded)
                SceneManager.LoadSceneAsync(initialSceneName, LoadSceneMode.Single);
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!IsDroneHostScene(scene.name) || isLoadingDroneScene)
                return;

            StartCoroutine(LoadDroneScene(scene));
        }

        IEnumerator LoadDroneScene(Scene hostScene)
        {
            isLoadingDroneScene = true;

            Scene droneScene = SceneManager.GetSceneByName(vrDroneSceneName);
            if (!droneScene.isLoaded)
            {
                AsyncOperation operation = SceneManager.LoadSceneAsync(
                    vrDroneSceneName,
                    LoadSceneMode.Additive);
                if (operation == null)
                {
                    Debug.LogError(
                        $"[Bootstrap] Unable to load '{vrDroneSceneName}'.",
                        this);
                    isLoadingDroneScene = false;
                    yield break;
                }

                yield return operation;
                droneScene = SceneManager.GetSceneByName(vrDroneSceneName);
            }

            if (hostScene.isLoaded)
            {
                VRDroneCoordinator coordinator = FindInScene<VRDroneCoordinator>(
                    droneScene);
                if (coordinator == null)
                {
                    Debug.LogError(
                        "[Bootstrap] VRDroneCoordinator is missing from VRDroneSpace.",
                        this);
                }
                else
                {
                    coordinator.ConfigureHostScene(hostScene);
                }
            }

            isLoadingDroneScene = false;
        }

        bool IsDroneHostScene(string sceneName)
        {
            return !string.Equals(
                    sceneName,
                    FitinSceneName,
                    StringComparison.Ordinal) &&
                sceneName.StartsWith(
                DroneHostScenePrefix,
                StringComparison.Ordinal);
        }

        static bool IsDirectPlayHostScene(string sceneName)
        {
            return !string.Equals(
                    sceneName,
                    FitinSceneName,
                    StringComparison.Ordinal) &&
                sceneName.StartsWith(
                DroneHostScenePrefix,
                StringComparison.Ordinal);
        }

        static T FindInScene<T>(Scene scene) where T : Component
        {
            if (!scene.isLoaded)
                return null;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T found = root.GetComponentInChildren<T>(true);
                if (found != null)
                    return found;
            }

            return null;
        }
    }
}
