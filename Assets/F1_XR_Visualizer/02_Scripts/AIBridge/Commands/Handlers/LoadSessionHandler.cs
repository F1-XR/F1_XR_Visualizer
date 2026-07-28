// AIBridge/Commands/Handlers/LoadSessionHandler.cs
// loadSession 명령 → session_key 로 데이터셋을 생성하고 리플레이를 로드.
//
// 흐름(기존 AutoReplayStarter와 동일 패턴):
//   1) ApiClient.CreateDataset({ sessionKey })  → 데이터 서버(:8000)가 매니페스트 생성
//   2) ReplayPlayer.LoadDataset(manifest, true) → 로드(재생). track 없는 오버로드라 역추적 불필요.
// ⚠️ 데이터 서버(F1_XR_Server :8000)가 켜져 있어야 동작한다.
#if AIBRIDGE_READY
using UnityEngine;
using F1XR.RestAPI.Replay;   // ReplayPlayer
using F1XR.RestAPI.Api;      // ApiClient, CreateDatasetBody, DatasetManifestDto

namespace F1XR.AIBridge.Commands
{
    public class LoadSessionHandler : MonoBehaviour
    {
        [Tooltip("비워두면 씬에서 자동으로 찾음")]
        public ReplayPlayer player;

        ReplayPlayer Player =>
            player != null ? player : (player = FindFirstObjectByType<ReplayPlayer>());

        public void Handle(int sessionKey)
        {
            var p = Player;
            if (p == null)
            {
                Debug.LogWarning("[AIBridge] ReplayPlayer 없음 — 리플레이 씬에서 테스트하세요.");
                return;
            }

            ApiClient api = p.api;
            if (api == null)
            {
                Debug.LogWarning("[AIBridge] ApiClient 없음(ReplayPlayer.api 미할당).");
                return;
            }

            // sessionKey 만 지정, 나머지(chunkMinutes=2·requestedMinutes=6 등)는 기본값 사용
            var body = new CreateDatasetBody { sessionKey = sessionKey };
            Debug.Log($"[AIBridge] loadSession {sessionKey} → CreateDataset 요청…");

            StartCoroutine(api.CreateDataset(
                body,
                manifest =>
                {
                    Debug.Log($"[AIBridge] dataset 생성됨: {manifest.datasetId} → 로드");
                    p.LoadDataset(manifest, true);   // track=null 오버로드
                },
                error => Debug.LogError($"[AIBridge] CreateDataset 실패: {error}")
            ));
        }
    }
}
#endif
