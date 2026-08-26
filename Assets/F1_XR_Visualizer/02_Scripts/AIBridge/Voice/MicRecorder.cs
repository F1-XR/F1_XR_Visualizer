// AIBridge/Voice/MicRecorder.cs
// 마이크 캡처 → wav → audio_utterance 전송. push-to-talk 방식.
// 필요 패키지: (전송에 client 사용) NativeWebSocket. AIBRIDGE_READY 로 관리.
#if AIBRIDGE_READY
using System;
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
#if UNITY_ANDROID && !UNITY_EDITOR
        UnityEngine.Android.PermissionCallbacks _permissionCallbacks;
#endif

        /// <summary>버튼 누를 때: 녹음 시작.</summary>
        public void StartRecording(Action<bool> onComplete = null)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                    UnityEngine.Android.Permission.Microphone))
            {
                _permissionCallbacks = new UnityEngine.Android.PermissionCallbacks();
                _permissionCallbacks.PermissionGranted += _ =>
                {
                    _permissionCallbacks = null;
                    onComplete?.Invoke(BeginRecording());
                };
                _permissionCallbacks.PermissionDenied += _ =>
                {
                    _permissionCallbacks = null;
                    Debug.LogError("[AIBridge] 마이크 권한이 거부됨");
                    onComplete?.Invoke(false);
                };
                _permissionCallbacks.PermissionDeniedAndDontAskAgain += _ =>
                {
                    _permissionCallbacks = null;
                    Debug.LogError("[AIBridge] 마이크 권한이 영구 거부됨. 앱 설정에서 마이크 권한을 허용하세요.");
                    onComplete?.Invoke(false);
                };
                UnityEngine.Android.Permission.RequestUserPermission(
                    UnityEngine.Android.Permission.Microphone, _permissionCallbacks);
                return;
            }
#endif
            onComplete?.Invoke(BeginRecording());
        }

        bool BeginRecording()
        {
            if (Microphone.devices.Length == 0)
            {
                Debug.LogError("[AIBridge] 사용 가능한 마이크가 없음");
                return false;
            }
            _device = Microphone.devices[0];
            _clip = Microphone.Start(_device, false, maxSeconds, sampleRate);
            if (_clip == null)
            {
                Debug.LogError($"[AIBridge] 마이크 녹음 시작 실패: {_device}");
                return false;
            }
            _recording = true;
            Debug.Log($"[AIBridge] 마이크 녹음 시작: {_device}, {sampleRate}Hz");
            return true;
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

            // 매 발화마다 현재 리플레이 시각을 새로 읽는다. 이전 구현은 currentAtTime이
            // 한 번 채워지면 이후 발화에서도 재사용해, 재생바를 옮겨도 같은 시점의
            // ML 피처/확률이 반복되는 문제가 있었다. 필드값은 ReplayPlayer가 없을 때만 폴백.
            string utteranceAtTime = (p != null && p.HasDataset)
                ? ReplayTimeMap.RelativeToIso(p, p.CurrentTime)
                : currentAtTime;

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
                    input_modality = InputModality.Current,   // XR 활성 시 controller_ray
                };

            byte[] wav = WavUtil.FromAudioClip(_clip, pos);
            string b64 = System.Convert.ToBase64String(wav);
            var msg = new AudioUtteranceMsg
            {
                data = b64,
                session_key = sessionKey,
                at_time = utteranceAtTime,
                interaction_context = ictx,
            };
            // 검증용: 음성 발화에 실려 올라가는 경기 번호·시각 확인(base64 data는 커서 제외)
            string skLog = sessionKey.HasValue ? sessionKey.Value.ToString() : "null";
            string atLog = utteranceAtTime ?? "null";
            string driverLog = ictx != null && ictx.driver_number.HasValue
                ? ictx.driver_number.Value.ToString()
                : "null";
            Debug.Log($"[AIBridge→AI] audio_utterance session_key={skLog} at_time={atLog} " +
                      $"selected_driver={driverLog} wavBytes={wav.Length}");
            client.Send(JsonConvert.SerializeObject(
                msg, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));
        }
    }
}
#endif
