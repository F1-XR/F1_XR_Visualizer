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

        [Header("예측형 능동 안내(서버 watcher)")]
        [Tooltip("켜면 리플레이 상태(현재 시각)를 주기 전송 → 서버가 '곧 추월' 예측 안내. " +
                 "서버 predict_watcher_enabled 와 함께 켠다. 켤 때 Unity PointOutWatcher는 끈다(안내 겹침 방지).")]
        public bool sendReplayState = false;
        public float replayStateInterval = 0.7f;   // heartbeat 주기(초)
        float _hbTimer;

        void OnEnable() { if (client != null) client.OnMessage += Route; }
        void OnDisable() { if (client != null) client.OnMessage -= Route; }

        // 리플레이 상태 heartbeat — 발화가 없어도 서버가 현재 시각을 알게 주기 전송.
        // (예측형 능동 안내 watcher가 '지금 몇 분인지'를 알아야 스스로 안내할 수 있음)
        void Update()
        {
            if (!sendReplayState || client == null) return;
            ReplayPlayer p = Player;
            if (p == null || !p.HasDataset) return;
            _hbTimer += Time.unscaledDeltaTime;
            if (_hbTimer < replayStateInterval) return;
            _hbTimer = 0f;

            var hb = new
            {
                type = "replay_state",
                session_key = ResolveSessionKey(0),
                at_time = CurrentAtTime(),
                is_playing = true,
                selected_driver = p.SelectedDriverNumber,
            };
            try { client.Send(JsonConvert.SerializeObject(hb, SendSettings)); }
            catch (System.Exception e) { Debug.LogWarning($"[hb] replay_state 전송 실패: {e.Message}"); }
        }

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
                    ttsPlayer?.Play((string)o["data"]);          // 답변: 최신 우선(이전 끊음)
                    break;
                case "tts_announce":
                    ttsPlayer?.Play((string)o["data"], false);   // 능동 안내: 재생 중이면 건너뜀
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
                interaction_context = BuildInteractionContext(),   // "이 선수" = 지금 선택된 차
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

        /// <summary>능동 안내용: 짧은 문장을 음성만 빠르게 합성 요청(에이전트 안 거침).
        /// 서버가 tts_audio 로 응답 → Route 에서 재생.</summary>
        public void SendSpeak(string text)
        {
            if (string.IsNullOrEmpty(text) || client == null) return;
            string json = JsonConvert.SerializeObject(new { type = "speak", text = text });
            client.Send(json);
        }

        /// <summary>현재 리플레이 시각을 ISO 절대시각으로. 리플레이 없으면 null.</summary>
        public string CurrentAtTime()
        {
            ReplayPlayer p = Player;
            if (p == null || !p.HasDataset) return null;
            return ReplayTimeMap.RelativeToIso(p, p.CurrentTime);
        }

        /// <summary>지금 화면에서 선택(클릭·XR Ray)된 차량을 '지목 맥락'으로 만든다.
        /// 선택이 없으면(0) null → 발화에서 통째로 생략된다(서버는 지시어를 되묻게 됨).
        /// 이 번호를 채우면 서버가 "이 선수/이 차/얘"를 이 번호로 해석한다.</summary>
        public InteractionContext BuildInteractionContext()
        {
            ReplayPlayer p = Player;
            int sel = (p != null) ? p.SelectedDriverNumber : 0;
            if (sel <= 0) return null;
            return new InteractionContext
            {
                target_type = "driver",
                driver_number = sel,
                input_modality = "click",   // 데스크톱 클릭 선택. Quest에선 "controller_ray"로 교체
            };
        }
    }
}
#endif
