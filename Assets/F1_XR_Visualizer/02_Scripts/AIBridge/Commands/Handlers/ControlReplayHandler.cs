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
                    if (value == null)
                        break;
                    if (value.Type == JTokenType.Integer || value.Type == JTokenType.Float)
                    {
                        // 숫자 = 상대초 그대로 Seek
                        p.Seek(value.Value<float>());
                    }
                    else
                    {
                        // ISO 절대시각(jump_to_event). ⚠️ Newtonsoft는 ISO 문자열을 Date 토큰으로
                        // 자동 파싱하므로 String 뿐 아니라 Date 도 처리해야 한다(둘 다 절대시각).
                        string iso = value.Type == JTokenType.Date
                            ? value.Value<System.DateTime>().ToUniversalTime().ToString("o")
                            : value.Value<string>();
                        if (ReplayTimeMap.IsoToRelative(p, iso, out float rel))
                        {
                            Debug.Log($"[AIBridge] seek ISO {iso} → 상대 {rel:0.0}s");
                            p.Seek(rel);
                        }
                        else
                        {
                            Debug.LogWarning($"[AIBridge] seek 시각 변환 실패(앵커 없음): {iso}");
                        }
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
