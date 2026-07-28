// AIBridge/Commands/Handlers/HighlightDriverHandler.cs
// highlightDriver 명령 → ReplayPlayer.SetSelectedDriver 로 해당 차 강조.
// ReplayPlayer.SetSelectedDriver 가 내부에서 '이전 강조 대체(1명)'와 실제 차 강조를 처리한다.
#if AIBRIDGE_READY
using UnityEngine;
using F1XR.RestAPI.Replay;   // ReplayPlayer

namespace F1XR.AIBridge.Commands
{
    public class HighlightDriverHandler : MonoBehaviour
    {
        [Tooltip("비워두면 씬에서 자동으로 찾음")]
        public ReplayPlayer player;

        // 인스펙터에 안 넣었으면 씬에서 1회 탐색해 캐시
        ReplayPlayer Player =>
            player != null ? player : (player = FindFirstObjectByType<ReplayPlayer>());

        public void Handle(int driverNumber)
        {
            var p = Player;
            if (p == null)
            {
                Debug.LogWarning("[AIBridge] ReplayPlayer 없음 — 리플레이가 로드된 씬에서 테스트하세요.");
                return;
            }
            // 내부에서 이전 선택 해제 + 새 차 강조(항상 1명). 같은 번호면 무시.
            p.SetSelectedDriver(driverNumber);
        }

        public void Clear()
        {
            // 0 = 선택 해제 (loadSession 시 디스패처가 호출)
            Player?.SetSelectedDriver(0);
        }
    }
}
#endif
