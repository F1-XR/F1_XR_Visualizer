// AIBridge/Voice/MicRecorder.cs
// 마이크 캡처 → wav → audio_utterance 전송. push-to-talk 방식.
// 필요 패키지: (전송에 client 사용) NativeWebSocket. AIBRIDGE_READY 로 관리.
#if AIBRIDGE_READY
using UnityEngine;
using Newtonsoft.Json;
using F1XR.AIBridge.Net;
using F1XR.AIBridge.Protocol;
using F1XR.RestAPI.Replay;   // ReplayPlayer (현재 재생 시각·로드된 세션)

namespace F1XR.AIBridge.Voice
{
    public class MicRecorder : MonoBehaviour
    {
        public AgentWebSocketClient client;

        [Tooltip("STT엔 16kHz mono 권장")]
        public int sampleRate = 16000;
        public int maxSeconds = 15;

        [Header("현재 관람 맥락 (다른 시스템이 매 발화 전에 채워줌)")]
        public int currentSessionKey;
        public string currentAtTime;   // ISO, 없으면 null

        AudioClip _clip;
        string _device;
        bool _recording;

        /// <summary>버튼 누를 때: 녹음 시작.</summary>
        public void StartRecording()
        {
            if (Microphone.devices.Length == 0) { Debug.LogError("[AIBridge] 마이크 없음"); return; }
            _device = Microphone.devices[0];
            _clip = Microphone.Start(_device, false, maxSeconds, sampleRate);
            _recording = true;
        }

        /// <summary>버튼 뗄 때: 녹음 종료 후 서버로 전송.</summary>
        public void StopAndSend()
        {
            if (!_recording) return;
            int pos = Microphone.GetPosition(_device);
            Microphone.End(_device);
            _recording = false;
            if (pos <= 0) { Debug.LogWarning("[AIBridge] 녹음 비어있음"); return; }

            // 현재 관람 맥락(at_time·session_key)을 리플레이에서 보강한다.
            ReplayPlayer p = FindFirstObjectByType<ReplayPlayer>();

            // at_time 이 비어있으면 현재 리플레이 시각으로 채운다(스포일러 방지).
            if (string.IsNullOrEmpty(currentAtTime) && p != null && p.HasDataset)
                currentAtTime = ReplayTimeMap.RelativeToIso(p, p.CurrentTime);

            // session_key: 로드된 리플레이가 있으면 그 세션을 우선(음성으로 경기 바꾼 뒤에도 정확).
            // 없으면 필드값, 그래도 유효하지 않으면 null → 서버 기본 세션 폴백.
            int? sessionKey = (p != null && p.HasDataset && p.Manifest != null && p.Manifest.sessionKey > 0)
                ? p.Manifest.sessionKey
                : (currentSessionKey > 0 ? currentSessionKey : (int?)null);

            // 공간 맥락: 지금 선택된 차량이 있으면 "이 선수"의 대상으로 실어 보낸다(없으면 생략).
            InteractionContext ictx = null;
            if (p != null && p.SelectedDriverNumber > 0)
                ictx = new InteractionContext
                {
                    target_type = "driver",
                    driver_number = p.SelectedDriverNumber,
                    input_modality = "click",
                };

            byte[] wav = WavUtil.FromAudioClip(_clip, pos);
            string b64 = System.Convert.ToBase64String(wav);
            var msg = new AudioUtteranceMsg
            {
                data = b64,
                session_key = sessionKey,
                at_time = currentAtTime,
                interaction_context = ictx,
            };
            // 검증용: 음성 발화에 실려 올라가는 경기 번호·시각 확인(base64 data는 커서 제외)
            string skLog = sessionKey.HasValue ? sessionKey.Value.ToString() : "null";
            string atLog = currentAtTime ?? "null";
            Debug.Log($"[AIBridge→AI] audio_utterance session_key={skLog} at_time={atLog} wavBytes={wav.Length}");
            client.Send(JsonConvert.SerializeObject(
                msg, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));
        }
    }
}
#endif
