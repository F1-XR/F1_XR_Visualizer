// AIBridge/Net/AgentWebSocketClient.cs
// AI 서버(/ws) WebSocket 연결·JSON 송수신·재연결.
// 필요 패키지: NativeWebSocket. 준비되면 Scripting Define 에 AIBRIDGE_READY 추가.
#if AIBRIDGE_READY
using System;
using System.Threading.Tasks;
using UnityEngine;
using NativeWebSocket;

namespace F1XR.AIBridge.Net
{
    public class AgentWebSocketClient : MonoBehaviour
    {
        public AgentConnectionConfig config;

        public event Action<string> OnMessage;   // 수신 원문 JSON
        public event Action OnConnected;

        WebSocket _ws;
        bool _destroyed;   // Play 정지·오브젝트 파괴 후 재연결 예약을 막기 위한 플래그
        bool _connecting;  // 동시 Connect 재진입 방지(연결 중복 생성 차단)
        bool _reconnectScheduled;

        async void Start() { await Connect(); }

        public async Task Connect()
        {
            if (config == null) { Debug.LogError("[AIBridge] AgentConnectionConfig 미할당"); return; }
            if (_connecting) return;          // 이미 연결 시도 중이면 중복 생성 안 함
            _connecting = true;
            CancelInvoke(nameof(Reconnect));  // 예약된 재연결 정리(중복 예약 제거)
            _reconnectScheduled = false;

            // 이전 소켓이 남아 있으면 먼저 닫는다(연결·핸들러 누적 방지).
            // _ws를 먼저 null로 만들어, 옛 소켓의 OnClose가 재연결을 예약하지 못하게 한다(_ws==old 아님).
            var old = _ws;
            _ws = null;
            if (old != null) { try { await old.Close(); } catch { } }

            var ws = new WebSocket(config.url);
            ws.OnOpen += () => { Debug.Log($"[AIBridge] 연결됨: {config.url}"); OnConnected?.Invoke(); };
            ws.OnMessage += bytes => OnMessage?.Invoke(System.Text.Encoding.UTF8.GetString(bytes));
            ws.OnError += e => Debug.LogError($"[AIBridge] WS 에러: {e}");
            ws.OnClose += c =>
            {
                Debug.Log($"[AIBridge] 닫힘: {c}");
                // 현재 소켓(_ws==ws)이 끊겼고, 파괴/비활성이 아니며 autoReconnect일 때만 1회 재연결 예약.
                // (교체된 옛 소켓은 _ws!=ws 라 여기서 재연결을 예약하지 못한다 → 연결 쌓임 방지)
                if (!_destroyed && isActiveAndEnabled && config.autoReconnect && _ws == ws)
                {
                    ScheduleReconnect();
                }
            };
            _ws = ws;
            _connecting = false;
            await ws.Connect();
        }

        async void Reconnect()
        {
            _reconnectScheduled = false;
            if (!_destroyed) await Connect();
        }

        public bool Send(string json)
        {
            if (_ws != null && _ws.State == WebSocketState.Open)
            {
                _ws.SendText(json);
                return true;
            }
            Debug.LogWarning("[AIBridge] 미연결 상태 — 전송 무시");
            return false;
        }

        void Update()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            _ws?.DispatchMessageQueue();   // NativeWebSocket: 수신 콜백을 메인스레드에서 처리
#endif
            if (!_destroyed && isActiveAndEnabled && config != null && config.autoReconnect && !_connecting)
            {
                if (_ws == null || _ws.State == WebSocketState.Closed)
                    ScheduleReconnect();
            }
        }

        void ScheduleReconnect()
        {
            if (_reconnectScheduled) return;
            _reconnectScheduled = true;
            CancelInvoke(nameof(Reconnect));
            Invoke(nameof(Reconnect), Mathf.Max(0.2f, config.reconnectDelaySec));
        }

        async void OnDestroy()
        {
            _destroyed = true;
            CancelInvoke();                       // 예약된 재연결 취소
            if (_ws != null) await _ws.Close();
        }
    }
}
#endif
