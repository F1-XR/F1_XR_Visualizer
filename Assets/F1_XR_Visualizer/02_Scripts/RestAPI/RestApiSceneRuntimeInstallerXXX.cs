using UnityEngine.SceneManagement;
using UnityEngine;
using F1XR.RestAPI.Api;
using F1XR.RestAPI.Replay;
using F1XR.RestAPI.Utility;

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
        ChunkReplayPlayer player = runtime.AddComponent<ChunkReplayPlayer>();
        RestApiAutoReplayController controller = runtime.AddComponent<RestApiAutoReplayController>();

        player.api = api;
        player.playOnReady = true;
        player.playbackSpeed = 1f;
        player.preloadChunksAhead = 3;

        controller.api = api;
        controller.player = player;
        controller.autoStart = true;
    }
}
