// AIBridge/Commands/AgentCommandDispatcher.cs
// command JSON → name별 Handler 호출.
// 필요 패키지: Newtonsoft Json.NET(동적 args 파싱). AIBRIDGE_READY 로 관리.
#if AIBRIDGE_READY
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace F1XR.AIBridge.Commands
{
    public class AgentCommandDispatcher : MonoBehaviour
    {
        public LoadSessionHandler loadSession;
        public HighlightDriverHandler highlightDriver;
        public ControlReplayHandler controlReplay;

        /// <summary>command 메시지 원문(JSON)을 받아 name별로 분기.</summary>
        public void Dispatch(string commandJson)
        {
            var o = JObject.Parse(commandJson);
            string name = (string)o["name"];
            var args = o["args"] as JObject;

            switch (name)
            {
                case "loadSession":
                    loadSession?.Handle((int)args["session_key"]);
                    // 규칙: 경기 바뀌면 강조 자동 해제
                    highlightDriver?.Clear();
                    break;
                case "highlightDriver":
                    highlightDriver?.Handle((int)args["driver_number"]);
                    break;
                case "controlReplay":
                    controlReplay?.Handle((string)args["action"], args["value"]);
                    break;
                default:
                    Debug.LogWarning($"[AIBridge] 미지원 명령: {name}");
                    break;
            }
        }
    }
}
#endif
