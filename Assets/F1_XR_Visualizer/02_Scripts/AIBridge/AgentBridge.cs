// AIBridge/AgentBridge.cs
// 브릿지 진입점: 씬에 하나 두고 나머지 컴포넌트를 연결한다.
// 수신 JSON을 type별로 라우팅하고, 텍스트/음성 발화 전송을 노출.
#if AIBRIDGE_READY
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using F1XR.AIBridge.Net;
using F1XR.AIBridge.Voice;
using F1XR.AIBridge.Commands;
using F1XR.AIBridge.Protocol;
using F1XR.RestAPI.Replay;   // ReplayPlayer (현재 재생 시각·로드된 세션)

namespace F1XR.AIBridge
{
    public class AgentBridge : MonoBehaviour
    {
        [Header("연결")]
        public AgentWebSocketClient client;
        public AgentCommandDispatcher dispatcher;
        public TtsAudioPlayer ttsPlayer;

        [Tooltip("현재 재생 시각(at_time)을 뽑을 ReplayPlayer. 비우면 자동 탐색")]
        public ReplayPlayer player;
        ReplayPlayer Player =>
            player != null ? player : (player = FindFirstObjectByType<ReplayPlayer>());

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

        // null 필드(session_key·at_time)를 생략해 protocol의 int|null·string|null 과 맞춘다.
        static readonly JsonSerializerSettings SendSettings =
            new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };

        /// <summary>텍스트 발화 전송(디버그·키보드 입력용).</summary>
        public void SendText(string text, int sessionKey, string atTime = null)
        {
            // at_time 을 안 주면 현재 리플레이 시각으로 채운다(스포일러 방지).
            if (string.IsNullOrEmpty(atTime)) atTime = CurrentAtTime();
            var msg = new UtteranceMsg
            {
                text = text,
                session_key = ResolveSessionKey(sessionKey),
                at_time = atTime,
            };
            string json = JsonConvert.SerializeObject(msg, SendSettings);
            // 검증용: 발화에 실려 올라가는 경기 번호·시각 확인(화면과 일치하는지)
            string skLog = msg.session_key.HasValue ? msg.session_key.Value.ToString() : "null";
            string atLog = msg.at_time ?? "null";
            Debug.Log($"[AIBridge→AI] utterance session_key={skLog} at_time={atLog} | {json}");
            client.Send(json);
        }

        /// <summary>
        /// 발화에 실을 session_key 결정. 로드된 리플레이가 있으면 그 세션을 우선한다
        /// (음성으로 경기를 바꾼 뒤에도 다음 발화가 올바른 세션을 가리키도록). 없으면 fallback,
        /// 그래도 유효하지 않으면 null → 서버가 기본 세션으로 폴백.
        /// </summary>
        public int? ResolveSessionKey(int fallback)
        {
            ReplayPlayer p = Player;
            if (p != null && p.HasDataset && p.Manifest != null && p.Manifest.sessionKey > 0)
                return p.Manifest.sessionKey;
            return fallback > 0 ? fallback : (int?)null;
        }

        /// <summary>현재 리플레이 시각을 ISO 절대시각으로. 리플레이 없으면 null.</summary>
        public string CurrentAtTime()
        {
            ReplayPlayer p = Player;
            if (p == null || !p.HasDataset) return null;
            return ReplayTimeMap.RelativeToIso(p, p.CurrentTime);
        }
    }
}
#endif
