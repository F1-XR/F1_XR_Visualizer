// AIBridge/Commands/Handlers/PredictOvertakeRibbonHandler.cs
// predictOvertake 명령 → 그 차에 '접근 리본(overtakeApproachRibbon)' VFX를 메인 맵에서 잠깐 켠다.
//
// 왜 별도 핸들러인가:
//   리본은 TrailRenderer라 '매 프레임' SetOvertakeApproachRibbon을 호출해야 꼬리가 그려진다.
//   그래서 명령을 한 번 받으면 holdSeconds 동안 Update에서 계속 갱신하고, 끝에 페이드아웃 후 끈다.
//   (팝아웃 스테이지가 아니라 메인 맵의 실제 차에 직접 켠다 — 시간 조작 없음, 실시간 표시.)
//
// [씬 세팅]
//   1) AgentCommandDispatcher 가 붙은 오브젝트(또는 아무 곳)에 이 컴포넌트 추가.
//   2) AgentCommandDispatcher 의 predictOvertake 필드에 드래그.
//   3) player 는 비우면 자동 탐색. ReplayPlayer.overtakeApproachRibbon.enabled = true 여야 보인다.
#if AIBRIDGE_READY
using UnityEngine;
using F1XR.RestAPI.Replay;   // ReplayPlayer, ReplayCarView

namespace F1XR.AIBridge.Commands
{
    public class PredictOvertakeRibbonHandler : MonoBehaviour
    {
        [Tooltip("비우면 씬에서 자동 탐색")]
        public ReplayPlayer player;

        [Header("표시")]
        [Tooltip("리본을 켜 두는 시간(초). 이후 자동으로 꺼진다")]
        [Min(0.5f)] public float holdSeconds = 4f;
        [Tooltip("확률(0~1)을 리본 강도로 바꿀 때 곱하는 값. 클수록 두껍고 밝다")]
        [Min(0.1f)] public float intensityScale = 1.4f;
        [Tooltip("확률이 낮아도 최소 이만큼은 보이게")]
        [Range(0f, 2f)] public float minIntensity = 0.35f;

        ReplayPlayer Player =>
            player != null ? player : (player = FindFirstObjectByType<ReplayPlayer>());

        int targetDriver;
        float baseIntensity;
        float activeUntil = -1f;
        ReplayCarView targetView;

        /// <summary>predictOvertake 명령 진입점. driverNumber 차에 확률 기반 리본을 켠다.</summary>
        public void Handle(int driverNumber, float probability)
        {
            if (driverNumber <= 0)
                return;

            // 항상 1대만 표시 — 다른 차로 바뀌면 이전 리본을 끈다.
            if (targetView != null && targetDriver != driverNumber)
                targetView.ClearOvertakeApproachRibbon();

            targetDriver = driverNumber;
            baseIntensity = Mathf.Max(minIntensity, Mathf.Clamp01(probability) * intensityScale);
            activeUntil = Time.time + Mathf.Max(0.5f, holdSeconds);
            targetView = null;   // 다음 Update에서 번호로 다시 찾는다(차가 재생성됐을 수 있음)
        }

        /// <summary>즉시 끄기(경기 전환 시 디스패처가 호출).</summary>
        public void Clear()
        {
            if (targetView != null)
                targetView.ClearOvertakeApproachRibbon();
            targetView = null;
            targetDriver = 0;
            activeUntil = -1f;
        }

        void Update()
        {
            if (activeUntil < 0f)
                return;

            ReplayPlayer p = Player;
            if (p == null || !p.HasDataset)
                return;

            if (Time.time >= activeUntil)   // 만료 → 끄기
            {
                Clear();
                return;
            }

            if (targetView == null)
                targetView = FindCarView(targetDriver);
            if (targetView == null)
                return;

            // 끝으로 갈수록 서서히 약해지게(페이드아웃). 시작 1 → 종료 0.
            float fade = Mathf.Clamp01((activeUntil - Time.time) / Mathf.Max(0.01f, holdSeconds));
            float intensity = baseIntensity * Mathf.Lerp(0.5f, 1f, fade);

            // 예측 대상 = '추월하는 쪽(overtaker)'으로 표시. 매 프레임 갱신해야 트레일이 이어진다.
            targetView.SetOvertakeApproachRibbon(
                p.overtakeApproachRibbon,
                overtaker: true,
                intensity: intensity,
                replayTime: p.CurrentTime);
        }

        ReplayCarView FindCarView(int number)
        {
            ReplayCarView[] views = FindObjectsByType<ReplayCarView>(FindObjectsSortMode.None);
            for (int i = 0; i < views.Length; i++)
                if (views[i] != null && views[i].driverNumber == number)
                    return views[i];
            return null;
        }
    }
}
#endif
