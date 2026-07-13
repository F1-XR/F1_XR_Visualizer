using UnityEngine.SceneManagement;
using UnityEngine;
using F1XR.RestAPI.Api;
using F1XR.RestAPI.Replay;

public static class RestApiSceneRuntimeInstaller
{
    private const string SceneName = "RestAPI";
    private const string RuntimeObjectName = "RestAPI Replay Runtime";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallForInitialScene()
    {
        InstallIfRestApiScene(SceneManager.GetActiveScene());
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        InstallIfRestApiScene(scene);
    }

    private static void InstallIfRestApiScene(Scene scene)
    {
        if (scene.name != SceneName)
            return;

        if (GameObject.Find(RuntimeObjectName) != null)
            return;

        GameObject runtime = new GameObject(RuntimeObjectName);

        ApiClient api = runtime.AddComponent<ApiClient>();
        ReplayPlayer player = runtime.AddComponent<ReplayPlayer>();
        AutoReplayStarter controller = runtime.AddComponent<AutoReplayStarter>();

        player.api = api;
        player.playOnReady = true;
        player.playbackSpeed = 6f;
        player.preloadChunksAhead = 3;

        controller.api = api;
        controller.player = player;
        controller.autoStart = true;
    }
}
