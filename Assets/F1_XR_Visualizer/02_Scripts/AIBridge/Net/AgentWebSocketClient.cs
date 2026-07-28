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

        async void Start() { await Connect(); }

        public async Task Connect()
        {
            if (config == null) { Debug.LogError("[AIBridge] AgentConnectionConfig 미할당"); return; }
            _ws = new WebSocket(config.url);
            _ws.OnOpen += () => { Debug.Log($"[AIBridge] 연결됨: {config.url}"); OnConnected?.Invoke(); };
            _ws.OnMessage += bytes => OnMessage?.Invoke(System.Text.Encoding.UTF8.GetString(bytes));
            _ws.OnError += e => Debug.LogError($"[AIBridge] WS 에러: {e}");
            _ws.OnClose += c =>
            {
                Debug.Log($"[AIBridge] 닫힘: {c}");
                // 파괴됐거나(Play 정지) 컴포넌트가 꺼졌으면 재연결하지 않는다.
                if (!_destroyed && isActiveAndEnabled && config.autoReconnect)
                    Invoke(nameof(Reconnect), config.reconnectDelaySec);
            };
            await _ws.Connect();
        }

        async void Reconnect()
        {
            if (!_destroyed) await Connect();
        }

        public void Send(string json)
        {
            if (_ws != null && _ws.State == WebSocketState.Open) _ws.SendText(json);
            else Debug.LogWarning("[AIBridge] 미연결 상태 — 전송 무시");
        }

        void Update()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            _ws?.DispatchMessageQueue();   // NativeWebSocket: 수신 콜백을 메인스레드에서 처리
#endif
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
