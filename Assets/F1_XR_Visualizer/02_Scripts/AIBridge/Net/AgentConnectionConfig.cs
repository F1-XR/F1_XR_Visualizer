// AIBridge/Net/AgentConnectionConfig.cs
// AI 서버 연결 설정. 외부 패키지 의존 없음(항상 컴파일).
// 에셋 생성: Project 창 우클릭 → Create → AIBridge → Connection Config
using UnityEngine;

namespace F1XR.AIBridge.Net
{
    [CreateAssetMenu(fileName = "AgentConnectionConfig", menuName = "AIBridge/Connection Config")]
    public class AgentConnectionConfig : ScriptableObject
    {
        [Tooltip("AI 서버 WebSocket 주소.\n로컬: ws://localhost:8001/ws\nQuest(같은 와이파이): ws://<PC-IP>:8001/ws\nQuest(터널): wss://<tunnel-host>/ws")]
        public string url = "ws://localhost:8001/ws";

        [Tooltip("연결 끊기면 자동 재연결")]
        public bool autoReconnect = true;

        [Tooltip("재연결 대기(초)")]
        public float reconnectDelaySec = 2f;
    }
}
