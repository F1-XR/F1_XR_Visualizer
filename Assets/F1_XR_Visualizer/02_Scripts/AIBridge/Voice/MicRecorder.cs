// AIBridge/Voice/MicRecorder.cs
// 마이크 캡처 → wav → audio_utterance 전송. push-to-talk 방식.
// 필요 패키지: (전송에 client 사용) NativeWebSocket. AIBRIDGE_READY 로 관리.
#if AIBRIDGE_READY
using UnityEngine;
using F1XR.AIBridge.Net;
using F1XR.AIBridge.Protocol;

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

            byte[] wav = WavUtil.FromAudioClip(_clip, pos);
            string b64 = System.Convert.ToBase64String(wav);
            var msg = new AudioUtteranceMsg
            {
                data = b64,
                session_key = currentSessionKey,
                at_time = currentAtTime,
            };
            client.Send(JsonUtility.ToJson(msg));
        }
    }
}
#endif
