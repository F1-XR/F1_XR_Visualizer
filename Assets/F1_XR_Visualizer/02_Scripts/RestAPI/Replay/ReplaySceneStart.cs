using UnityEngine;
using F1XR.RestAPI.Api;

namespace F1XR.RestAPI.Replay
{
    public class ReplaySceneStart : MonoBehaviour
    {
        public ApiClient api;
        public ChunkReplayPlayer player;

        private void Awake()
        {
            Debug.Log("ReplaySceneStart Awake");
        }

        private void Start()
        {
            if (api == null)
                api = FindFirstObjectByType<ApiClient>();

            if (player == null)
                player = FindFirstObjectByType<ChunkReplayPlayer>();

            Debug.Log($"ReplaySceneStart Start manifest={ReplayLoad.Manifest != null}, api={api != null}, player={player != null}");

            if (ReplayLoad.Manifest == null || api == null || player == null)
                return;

            player.api = api;
            player.LoadDataset(ReplayLoad.Manifest, true);

            Debug.Log($"ReplaySceneStart LoadDataset: {ReplayLoad.Manifest.datasetId}");

            ReplayLoad.Clear();
        }
    }
}