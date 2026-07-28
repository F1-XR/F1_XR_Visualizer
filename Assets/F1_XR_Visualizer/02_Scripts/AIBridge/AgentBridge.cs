// AIBridge/AgentBridge.cs
// 브릿지 진입점: 씬에 하나 두고 나머지 컴포넌트를 연결한다.
// 수신 JSON을 type별로 라우팅하고, 텍스트/음성 발화 전송을 노출.
#if AIBRIDGE_READY
using UnityEngine;
using Newtonsoft.Json.Linq;
using F1XR.AIBridge.Net;
using F1XR.AIBridge.Voice;
using F1XR.AIBridge.Commands;
using F1XR.AIBridge.Protocol;

namespace F1XR.AIBridge
{
    public class AgentBridge : MonoBehaviour
    {
        [Header("연결")]
        public AgentWebSocketClient client;
        public AgentCommandDispatcher dispatcher;
        public TtsAudioPlayer ttsPlayer;

        void OnEnable() { if (client != null) client.OnMessage += Route; }
        void OnDisable() { if (client != null) client.OnMessage -= Route; }

        /// <summary>수신 JSON을 type별로 라우팅.</summary>
        void Route(string json)
        {
            var o = JObject.Parse(json);
            switch ((string)o["type"])
            {
                case "transcript":
                    Debug.Log($"[STT] {o["text"]}");
                    // TODO: 사용자 발화 자막 UI
                    break;
                case "assistant_text":
                    Debug.Log($"[답변] {o["text"]}");
                    // TODO: 답변 자막 UI
                    break;
                case "tts_audio":
                    ttsPlayer?.Play((string)o["data"]);
                    break;
                case "command":
                    dispatcher?.Dispatch(json);
                    break;
            }
        }

        /// <summary>텍스트 발화 전송(디버그·키보드 입력용).</summary>
        public void SendText(string text, int sessionKey, string atTime = null)
        {
            var msg = new UtteranceMsg { text = text, session_key = sessionKey, at_time = atTime };
            client.Send(JsonUtility.ToJson(msg));
        }
    }
}
#endif
