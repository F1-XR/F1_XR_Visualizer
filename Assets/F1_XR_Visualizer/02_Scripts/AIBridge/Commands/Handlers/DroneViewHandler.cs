// AIBridge/Commands/Handlers/DroneViewHandler.cs
// droneView 명령 → 드론(공중) 시점 켜기/끄기.
//
// 왜 UnityEvent인가:
//   드론 '진입'은 큐브 없이 부를 public 메서드(VRDroneCoordinator.EnterVrFromCommand 등)가
//   아직 없을 수 있다(팀 작업 ②). 여기서 그 메서드를 직접 호출하면 그게 생기기 전엔 컴파일이 안 된다.
//   그래서 UnityEvent로 빼서, 씬 인스펙터에서 연결한다 → ② 없이도 컴파일되고, 준비되면 드래그로 연결.
//
// [씬 세팅]
//   1) AgentCommandDispatcher 가 있는 오브젝트에 이 컴포넌트 추가.
//   2) AgentCommandDispatcher 의 droneView 칸에 드래그.
//   3) On Exit Drone  → VRDroneCoordinator.ExitVr() 연결 (지금 가능 — public).
//   4) On Enter Drone → VRDroneCoordinator.EnterVrFromCommand() 연결 (②가 열리면).
//      (②가 아직이면 On Enter Drone은 비워둬도 됨 — 종료만 먼저 동작)
#if AIBRIDGE_READY
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace F1XR.AIBridge.Commands
{
    public class DroneViewHandler : MonoBehaviour
    {
        [Tooltip("드론 진입 — 보통 VRDroneCoordinator.EnterVrFromCommand() 연결(② 완성 후)")]
        public UnityEvent onEnterDrone;

        [Tooltip("드론 종료 — VRDroneCoordinator.ExitVr() 연결(지금 가능)")]
        public UnityEvent onExitDrone;

        /// <summary>droneView 명령 진입점. on=true 진입 / false 종료.</summary>
        public void Handle(bool on)
        {
            if (on)
            {
                onEnterDrone?.Invoke();
                TryDesktopFallback(true);
            }
            else
            {
                onExitDrone?.Invoke();
                TryDesktopFallback(false);
            }
        }

        void TryDesktopFallback(bool on)
        {
            Scene scene = gameObject.scene;
            if (!scene.IsValid() || scene.name != "AICommandTest")
                return;

            bool hasSceneWiredEvent = on
                ? onEnterDrone != null && onEnterDrone.GetPersistentEventCount() > 0
                : onExitDrone != null && onExitDrone.GetPersistentEventCount() > 0;
            if (hasSceneWiredEvent)
                return;

            DesktopDroneViewFallback fallback =
                FindObjectOfType<DesktopDroneViewFallback>(true);
            if (fallback == null)
            {
                Camera camera = Camera.main;
                if (camera == null)
                {
                    Debug.LogWarning(
                        "[AIBridge] AICommandTest desktop drone fallback skipped: no Main Camera.");
                    return;
                }

                fallback = camera.gameObject.AddComponent<DesktopDroneViewFallback>();
            }

            fallback.SetDroneView(on);
        }
    }
}
#endif
