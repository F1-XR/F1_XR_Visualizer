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
            if (predictOvertake == null)
                predictOvertake = GetComponent<PredictOvertakeRibbonHandler>()
                    ?? gameObject.AddComponent<PredictOvertakeRibbonHandler>();
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
            Debug.Log($"[AIBridge] command received: {name}");

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
                    if (predictOvertake == null)
                    {
                        Debug.LogWarning("[AIBridge] predictOvertake handler missing");
                    }
                    else
                    {
                        predictOvertake.Handle(
                            (int)args["driver_number"],
                            args["probability"] != null ? (float)args["probability"] : 0f,
                            args["risk_label"] != null ? (string)args["risk_label"] : null,
                            (args["gap_seconds"] != null && args["gap_seconds"].Type != JTokenType.Null)
                                ? (float?)(float)args["gap_seconds"] : null,
                            (args["gap_trend"] != null && args["gap_trend"].Type != JTokenType.Null)
                                ? (float?)(float)args["gap_trend"] : null,
                            (args["window_seconds"] != null && args["window_seconds"].Type != JTokenType.Null)
                                ? (float?)(float)args["window_seconds"] : null);
                    }
                    break;
                case "droneView":
                    // 드론(공중) 시점 켜기/끄기. on 없으면 켜기로 간주.
                    droneView?.Handle(args["on"] == null || (bool)args["on"]);
                    break;
                case "controlReplay":
                    controlReplay?.Handle((string)args["action"], args["value"]);
                    break;
                case "showBattleContext":
                    // 두 차 사이 Gap Line + 복합 배지("0.8s → 0.4s(3s) · Closing · DRS") + 예측 화살표를 잠깐 표시.
                    // predicted_gap_seconds(3초 뒤 예측 갭)가 없거나 null이면 -1 전달 → 화살표 생략.
                    showBattleContext?.Handle(
                        (int)args["subject_driver"],
                        (int)args["target_driver"],
                        args["gap_seconds"] != null ? (float)args["gap_seconds"] : 0f,
                        (args["predicted_gap_seconds"] != null
                            && args["predicted_gap_seconds"].Type != JTokenType.Null)
                            ? (float)args["predicted_gap_seconds"] : -1f,
                        args["predict_horizon_sec"] != null ? (float)args["predict_horizon_sec"] : 3f,
                        (string)args["trend"],
                        args["drs"] != null && (bool)args["drs"],
                        args["confidence"] != null ? (float)args["confidence"] : 0f,
                        (string)args["reason"],
                        // 예측 불확실성 ±σ(초). 없거나 null이면 -1 전달 → 브래킷 생략.
                        (args["predicted_gap_std_seconds"] != null
                            && args["predicted_gap_std_seconds"].Type != JTokenType.Null)
                            ? (float)args["predicted_gap_std_seconds"] : -1f,
                        args["driver_name"] != null ? (string)args["driver_name"] : null,
                        args["team_name"] != null ? (string)args["team_name"] : null,
                        (args["air_temperature"] != null
                            && args["air_temperature"].Type != JTokenType.Null)
                            ? (float?)((float)args["air_temperature"]) : null,
                        (args["track_temperature"] != null
                            && args["track_temperature"].Type != JTokenType.Null)
                            ? (float?)((float)args["track_temperature"]) : null);
                    break;
                case "updateOvertakeGauge":
                {
                    // 서버가 하트비트마다 스트리밍하는 실시간 gap → 게이지 표시만(계산 X).
                    var hud = GetComponent<OvertakeGaugeHud>() ?? FindFirstObjectByType<OvertakeGaugeHud>();
                    if (hud != null)
                        hud.UpdateLiveForDriver(
                            args["driver_number"] != null ? (int)args["driver_number"] : -1,
                            (args["gap_seconds"] != null && args["gap_seconds"].Type != JTokenType.Null)
                                ? (float?)(float)args["gap_seconds"] : null,
                            (args["gap_trend"] != null && args["gap_trend"].Type != JTokenType.Null)
                                ? (float?)(float)args["gap_trend"] : null,
                            (args["window_seconds"] != null && args["window_seconds"].Type != JTokenType.Null)
                                ? (float?)(float)args["window_seconds"] : null);
                    // 같은 배틀이 이어지는 동안 predict 핸들러 표시 시간도 갱신 → HUD가 안 꺼지고 유지(팝인/핑 없이).
                    predictOvertake?.KeepAlive(args["driver_number"] != null ? (int)args["driver_number"] : -1);
                    break;
                }
                default:
                    Debug.LogWarning($"[AIBridge] 미지원 명령: {name}");
                    break;
            }
        }
    }
}
#endif
