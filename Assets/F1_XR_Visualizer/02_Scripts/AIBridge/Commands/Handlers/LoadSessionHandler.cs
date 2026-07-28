// AIBridge/Commands/Handlers/LoadSessionHandler.cs
// loadSession 명령 → 해당 세션 리플레이 로드.
//
// ⚠️ 이건 다른 명령보다 복잡하다. ReplayPlayer.LoadDataset 은 '이미 만들어진 매니페스트'를
//    받으므로, session_key 로부터 데이터셋을 먼저 생성해야 한다. 기존 흐름(AutoReplayStarter):
//      1) api.GetF1Track(...) 으로 track 확보
//      2) CreateDatasetBody{ sessionKey = ... } 구성
//      3) api.CreateDataset(body, manifest => player.LoadDataset(manifest, track, true), onError)
//    → 이 flow 를 session_key 지정 버전으로 재사용해야 한다.
//    지금은 강조/재생 제어 검증이 우선이라 TODO 로 둔다.
#if AIBRIDGE_READY
using UnityEngine;

namespace F1XR.AIBridge.Commands
{
    public class LoadSessionHandler : MonoBehaviour
    {
        public void Handle(int sessionKey)
        {
            Debug.Log($"[AIBridge] loadSession {sessionKey} (아직 미연결)");
            // TODO: AutoReplayStarter 의 CreateDataset→LoadDataset 흐름을
            //       'session_key 지정' 버전으로 호출. (api.CreateDataset + player.LoadDataset)
        }
    }
}
#endif
