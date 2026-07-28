// AIBridge/Commands/Handlers/ControlReplayHandler.cs
// controlReplay 명령 → ReplayPlayer 재생 제어(Play/Pause/SetSpeed/Seek).
// value 다형: speed→숫자, seek→숫자(상대초) 또는 ISO 문자열(절대, jump_to_event).
#if AIBRIDGE_READY
using UnityEngine;
using Newtonsoft.Json.Linq;
using F1XR.RestAPI.Replay;   // ReplayPlayer

namespace F1XR.AIBridge.Commands
{
    public class ControlReplayHandler : MonoBehaviour
    {
        [Tooltip("비워두면 씬에서 자동으로 찾음")]
        public ReplayPlayer player;

        ReplayPlayer Player =>
            player != null ? player : (player = FindFirstObjectByType<ReplayPlayer>());

        public void Handle(string action, JToken value)
        {
            var p = Player;
            if (p == null)
            {
                Debug.LogWarning("[AIBridge] ReplayPlayer 없음 — 리플레이 씬에서 테스트하세요.");
                return;
            }

            switch (action)
            {
                case "play":
                    p.Play();
                    break;
                case "pause":
                    p.Pause();
                    break;
                case "speed":
                    p.SetSpeed(value != null ? value.Value<float>() : 1f);
                    break;
                case "seek":
                    if (value != null && value.Type == JTokenType.String)
                    {
                        // ISO 절대시각(jump_to_event) → 리플레이 상대초로 변환해야 함.
                        // ReplayPlayer.Seek 는 상대초(float)를 받으므로, 세션 시작 절대시각 기준
                        // 매핑이 필요. 그 기준값 확보 후 구현 예정.
                        Debug.LogWarning("[AIBridge] ISO 절대시각 seek 미지원(상대초 변환 필요). 수치 seek만 동작.");
                    }
                    else
                    {
                        p.Seek(value != null ? value.Value<float>() : 0f);
                    }
                    break;
                default:
                    Debug.LogWarning($"[AIBridge] 미지원 action: {action}");
                    break;
            }
        }
    }
}
#endif
