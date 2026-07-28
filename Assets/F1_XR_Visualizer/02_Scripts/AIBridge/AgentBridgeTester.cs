// AIBridge/AgentBridgeTester.cs
// 임시 테스트용: Play 중 화면 버튼으로 (1)텍스트 질문 (2)마이크 녹음→전송 을 테스트한다.
// UI·기기 없이 WS 왕복(텍스트/음성)을 검증. Input System 의존 없이 OnGUI 사용.
// 검증 끝나면 이 컴포넌트는 지워도 된다.
#if AIBRIDGE_READY
using UnityEngine;
using F1XR.AIBridge.Voice;

namespace F1XR.AIBridge
{
    public class AgentBridgeTester : MonoBehaviour
    {
        [Tooltip("연결할 AgentBridge")]
        public AgentBridge bridge;

        [Tooltip("연결할 MicRecorder(음성 테스트용). 없으면 마이크 버튼 비활성")]
        public MicRecorder mic;

        [Tooltip("텍스트 버튼으로 보낼 질문")]
        public string question = "DRS가 뭐야?";

        [Tooltip("현재 보는 경기 (0이면 서버 기본 세션)")]
        public int sessionKey = 9839;

        bool _recording;

        void OnGUI()
        {
            // ── 1) 텍스트 질문 ──
            if (GUI.Button(new Rect(20, 20, 320, 60), $"[텍스트] 질문 보내기:\n{question}"))
            {
                if (bridge == null) { Debug.LogError("[Tester] bridge 미할당"); return; }
                Debug.Log($"[Tester] 텍스트 전송 → {question}");
                bridge.SendText(question, sessionKey);
            }

            // ── 2) 마이크 녹음 → 전송 ──
            GUI.Label(new Rect(20, 92, 320, 22),
                _recording ? "● 녹음 중… (정지·전송 누르기)" : "마이크: 대기");

            if (!_recording)
            {
                if (GUI.Button(new Rect(20, 118, 320, 56), "[음성] 녹음 시작"))
                {
                    if (mic == null) { Debug.LogError("[Tester] mic 미할당"); return; }
                    mic.StartRecording();
                    _recording = true;
                    Debug.Log("[Tester] 녹음 시작");
                }
            }
            else
            {
                if (GUI.Button(new Rect(20, 118, 320, 56), "[음성] 정지 & 전송"))
                {
                    // 현재 보는 경기 맥락을 마이크 발화에도 실어 보냄
                    mic.currentSessionKey = sessionKey;
                    mic.StopAndSend();
                    _recording = false;
                    Debug.Log("[Tester] 정지 & 전송");
                }
            }
        }
    }
}
#endif
