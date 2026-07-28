// AIBridge/AgentBridgeTester.cs
// 임시 테스트용: Play 중 화면 버튼을 누르면 텍스트 질문을 서버로 보낸다.
// (UI·마이크 없이 WS 왕복만 먼저 확인. Input System 의존 없이 OnGUI 사용.)
// 검증 끝나면 이 컴포넌트는 지워도 된다.
#if AIBRIDGE_READY
using UnityEngine;

namespace F1XR.AIBridge
{
    public class AgentBridgeTester : MonoBehaviour
    {
        [Tooltip("연결할 AgentBridge")]
        public AgentBridge bridge;

        [Tooltip("보낼 질문")]
        public string question = "DRS가 뭐야?";

        [Tooltip("현재 보는 경기 (0이면 서버 기본 세션)")]
        public int sessionKey = 9839;

        void OnGUI()
        {
            if (GUI.Button(new Rect(20, 20, 320, 70), $"질문 보내기:\n{question}"))
            {
                if (bridge == null) { Debug.LogError("[Tester] bridge 미할당"); return; }
                Debug.Log($"[Tester] 전송 → {question}");
                bridge.SendText(question, sessionKey);
            }
        }
    }
}
#endif
