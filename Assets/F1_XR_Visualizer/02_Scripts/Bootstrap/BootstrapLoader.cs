using System.Collections;
using F1XR.Drone;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace F1XR.Bootstrap
{
    [DisallowMultipleComponent]
    public sealed class BootstrapLoader : MonoBehaviour
    {
        [SerializeField] string initialSceneName = "HomeSpace";
        [SerializeField] string vrDroneSceneName = "VRDroneSpace";
        [SerializeField] string[] droneHostSceneNames =
            { "SessionSpace", "SessionSpace0803" };

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
                    BindDroneViewCommand(coordinator);
                }
            }

            isLoadingDroneScene = false;
        }

        // AIBridge droneView 명령 ↔ VRDroneCoordinator 런타임 연결.
        // (AIBridge는 세션 씬에 직접 배치. 배치돼 있지 않으면 handler를 못 찾아 조용히 스킵)
        // VRDroneCoordinator 는 additive로 나중에 뜨는 VRDroneSpace 인스턴스라
        // 인스펙터(UnityEvent)로는 참조가 안 되므로, 여기서 코드로 이어준다.
        // 핸들러가 씬에 없으면(=AIBridge 미배치) 조용히 스킵.
        static void BindDroneViewCommand(VRDroneCoordinator coordinator)
        {
#if AIBRIDGE_READY
            var handler = Object.FindFirstObjectByType<
                F1XR.AIBridge.Commands.DroneViewHandler>(FindObjectsInactive.Include);
            if (handler == null)
                return;

            if (handler.onEnterDrone == null)
                handler.onEnterDrone = new UnityEngine.Events.UnityEvent();
            if (handler.onExitDrone == null)
                handler.onExitDrone = new UnityEngine.Events.UnityEvent();

            // 재진입(씬 재로드) 시 중복 등록 방지 후 재연결.
            handler.onEnterDrone.RemoveListener(coordinator.EnterVrFromCommand);
            handler.onEnterDrone.AddListener(coordinator.EnterVrFromCommand);
            handler.onExitDrone.RemoveListener(coordinator.ExitVr);
            handler.onExitDrone.AddListener(coordinator.ExitVr);
#endif
        }

        bool IsDroneHostScene(string sceneName)
        {
            foreach (string hostSceneName in droneHostSceneNames)
            {
                if (hostSceneName == sceneName)
                    return true;
            }

            return false;
        }

        static bool IsDirectPlayHostScene(string sceneName)
        {
            return sceneName == "SessionSpace" ||
                sceneName == "SessionSpace0803";
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
