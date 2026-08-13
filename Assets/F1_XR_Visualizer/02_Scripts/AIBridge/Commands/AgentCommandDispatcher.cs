// AIBridge/Commands/AgentCommandDispatcher.cs
// command JSON → name별 Handler 호출.
// 필요 패키지: Newtonsoft Json.NET(동적 args 파싱). AIBRIDGE_READY 로 관리.
#if AIBRIDGE_READY
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace F1XR.AIBridge.Commands
{
    public class AgentCommandDispatcher : MonoBehaviour
    {
        public LoadSessionHandler loadSession;
        public HighlightDriverHandler highlightDriver;
        public ControlReplayHandler controlReplay;
        public PredictOvertakeRibbonHandler predictOvertake;
        public DroneViewHandler droneView;
        public ShowBattleContextHandler showBattleContext;

        // 인스펙터에서 안 붙인 핸들러는 같은 오브젝트에서 자동 확보(없으면 생성).
        // → 프리팹 수작업 없이도 명령이 각 핸들러로 전달된다.
        //   (droneView의 onEnter/onExit ↔ VRDroneCoordinator 연결은 BootstrapLoader가 런타임에 수행)
        void Awake()
        {
            if (droneView == null)
                droneView = GetComponent<DroneViewHandler>()
                    ?? gameObject.AddComponent<DroneViewHandler>();
            if (showBattleContext == null)
                showBattleContext = GetComponent<ShowBattleContextHandler>()
                    ?? gameObject.AddComponent<ShowBattleContextHandler>();
        }

        /// <summary>command 메시지 원문(JSON)을 받아 name별로 분기.</summary>
        public void Dispatch(string commandJson)
        {
            var o = JObject.Parse(commandJson);
            string name = (string)o["name"];
            var args = o["args"] as JObject;

            switch (name)
            {
                case "loadSession":
                    loadSession?.Handle((int)args["session_key"]);
                    // 규칙: 경기 바뀌면 강조·예측 리본·배틀 표시 자동 해제
                    highlightDriver?.Clear();
                    predictOvertake?.Clear();
                    showBattleContext?.Clear();
                    break;
                case "highlightDriver":
                    highlightDriver?.Handle((int)args["driver_number"]);
                    break;
                case "predictOvertake":
                    // 능동 안내(예측): 그 차에 접근 리본을 잠깐 표시. probability 없으면 0.
                    predictOvertake?.Handle(
                        (int)args["driver_number"],
                        args["probability"] != null ? (float)args["probability"] : 0f,
                        args["risk_label"] != null ? (string)args["risk_label"] : null);
                    break;
                case "droneView":
                    // 드론(공중) 시점 켜기/끄기. on 없으면 켜기로 간주.
                    droneView?.Handle(args["on"] == null || (bool)args["on"]);
                    break;
                case "controlReplay":
                    controlReplay?.Handle((string)args["action"], args["value"]);
                    break;
                case "showBattleContext":
                    // 두 차 사이 Gap Line + 복합 배지("0.8s · Closing · DRS")를 잠깐 표시.
                    showBattleContext?.Handle(
                        (int)args["subject_driver"],
                        (int)args["target_driver"],
                        args["gap_seconds"] != null ? (float)args["gap_seconds"] : 0f,
                        (string)args["trend"],
                        args["drs"] != null && (bool)args["drs"],
                        args["confidence"] != null ? (float)args["confidence"] : 0f,
                        (string)args["reason"]);
                    break;
                default:
                    Debug.LogWarning($"[AIBridge] 미지원 명령: {name}");
                    break;
            }
        }
    }
}
#endif
