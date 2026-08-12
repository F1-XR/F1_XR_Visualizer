using F1XR.RestAPI.Replay;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace F1XR.Drone
{
    [DisallowMultipleComponent]
    public sealed class VRDroneAudioDistanceScaler : MonoBehaviour
    {
        Scene hostScene;
        ReplayPlayer replayPlayer;
        bool isApplied;

        public void ConfigureHostScene(Scene scene)
        {
            Restore();
            hostScene = scene;
            replayPlayer = null;
        }

        public void Apply(float scale)
        {
            if (!TryResolveReplayPlayer())
                return;

            replayPlayer.SetEngineAudioDistanceScale(scale);
            isApplied = true;
        }

        public void Restore()
        {
            if (!isApplied || !TryResolveReplayPlayer())
                return;

            replayPlayer.SetEngineAudioDistanceScale(1f);
            isApplied = false;
        }

        void OnDestroy()
        {
            Restore();
        }

        bool TryResolveReplayPlayer()
        {
            if (replayPlayer != null)
                return true;

            if (!hostScene.isLoaded)
                return false;

            foreach (GameObject root in hostScene.GetRootGameObjects())
            {
                replayPlayer = root.GetComponentInChildren<ReplayPlayer>(true);
                if (replayPlayer != null)
                    return true;
            }

            return false;
        }
    }
}
