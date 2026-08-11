// AIBridge/InputModality.cs
// 지목(interaction_context)에 쓰는 input_modality 값을 실행 환경에 맞춰 결정.
// XR 헤드셋이 활성이면 컨트롤러 Ray 선택("controller_ray"), 아니면 데스크톱 클릭("click").
// (가드 없이 항상 컴파일 — AgentBridge/MicRecorder 양쪽에서 공용으로 참조)
using UnityEngine.XR;

namespace F1XR.AIBridge
{
    public static class InputModality
    {
        public const string Click = "click";
        public const string ControllerRay = "controller_ray";

        /// <summary>현재 환경 기준 modality. XR 디바이스 활성 시 controller_ray.</summary>
        public static string Current =>
            XRSettings.isDeviceActive ? ControllerRay : Click;
    }
}
