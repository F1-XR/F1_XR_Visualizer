// AIBridge/Commands/Handlers/PredictOvertakeRibbonHandler.cs
// predictOvertake 명령 → 예측 차에 '노란 접근 리본 + 확률 라벨 + 방향 효과음'을 잠깐 표시.
// 예측(불확실)을 팝아웃의 '확정' 리본과 구분하려고 색=노랑 + 확률%를 위에 띄운다.
// 전부 이 핸들러 안에서 런타임 생성 → 팀원 코드(ReplayCarView/EventPopout/공유 리본설정) 안 건드림.
#if AIBRIDGE_READY
using UnityEngine;
using TMPro;
using F1XR.RestAPI.Replay;   // ReplayPlayer, ReplayCarView, OvertakeApproachRibbonSettings

namespace F1XR.AIBridge.Commands
{
    public class PredictOvertakeRibbonHandler : MonoBehaviour
    {
        [Tooltip("비우면 씬에서 자동 탐색")]
        public ReplayPlayer player;

        [Header("표시 시간/강도")]
        [Min(0.5f)] public float holdSeconds = 4f;
        [Min(0.1f)] public float intensityScale = 1.4f;
        [Range(0f, 2f)] public float minIntensity = 0.35f;

        [Header("예측 리본(노랑) — 팝아웃 확정 리본과 구분")]
        [Tooltip("예측 전용 색. 공유설정과 별개라 여기만 바꿔도 팝아웃엔 영향 없음")]
        public Color predictionGlowColor = new Color(1f, 0.82f, 0.08f, 0.85f);

        [Header("확률 라벨")]
        public bool showLabel = false;   // HUD가 정보를 다 보여주므로 큰 월드 텍스트는 기본 끔(인스펙터에서 켤 수 있음)
        [Tooltip("{0}=확률%. 한글 쓰면 폰트 필요 → 기본은 숫자만")]
        public string labelFormat = "Overtake Risk High";
        [Tooltip("차 위로 띄우는 높이(차 스케일 배수). 트랙이 작아도 비율 유지 — 너무 높으면 낮추기")]
        public float labelHeight = 1.5f;
        [Tooltip("라벨 글자 크기(차 스케일에 비례해 자동 축소). 여전히 크면 더 낮추기")]
        public float labelFontSize = 6f;

        [Header("방향 효과음 큐(선택)")]
        [Tooltip("짧은 핑/휙 사운드. 비우면 소리 없음(에러 아님)")]
        public AudioClip pingClip;
        [Range(0f, 1f)] public float pingVolume = 0.9f;

        ReplayPlayer Player => player != null ? player : (player = FindFirstObjectByType<ReplayPlayer>());
        // 씬에 직접 배치한 인스턴스가 있으면 그걸 우선 사용(인스펙터로 위치 조절 가능) → 없으면 자동 생성.
        OvertakeGaugeHud gaugeHud => _gaugeHud != null ? _gaugeHud : (_gaugeHud = FindFirstObjectByType<OvertakeGaugeHud>() ?? GetComponent<OvertakeGaugeHud>() ?? gameObject.AddComponent<OvertakeGaugeHud>());

        int targetDriver;
        float baseIntensity;
        float lastProbability;
        string riskLabel;
        float activeUntil = -1f;
        ReplayCarView targetView;

        OvertakeApproachRibbonSettings _ribbon;   // 예측 전용(노랑). 공유설정 안 씀
        TextMeshPro _label;
        AudioSource _audio;
        OvertakeGaugeHud _gaugeHud;
        float _diagTimer;

        void Awake()
        {
            _ribbon = new OvertakeApproachRibbonSettings();     // 기본값 복제
            _ribbon.overtakerGlowColor = predictionGlowColor;  // 색만 노랑으로
        }

        public void Handle(int driverNumber, float probability, string label = null,
                           float? gapSeconds = null, float? gapTrend = null, float? windowSeconds = null)
        {
            if (driverNumber <= 0) return;
            bool newBattle = (driverNumber != targetDriver) || (Time.time >= activeUntil);   // 같은 배틀 이어짐 판별
            if (targetView != null && targetDriver != driverNumber)
                targetView.ClearOvertakeApproachRibbon();

            targetDriver = driverNumber;
            lastProbability = Mathf.Clamp01(probability);
            riskLabel = string.IsNullOrWhiteSpace(label) ? labelFormat : label;
            baseIntensity = Mathf.Max(minIntensity, lastProbability * intensityScale);
            activeUntil = Time.time + Mathf.Max(0.5f, holdSeconds);
            targetView = null;

            Debug.Log($"[AIBridge] predictOvertake handle driver={targetDriver} probability={lastProbability:0.00} gap={(gapSeconds.HasValue ? gapSeconds.Value.ToString("0.00") : "--")} hold={holdSeconds:0.##}s");
            // gap이 오면 그 드라이버의 실제 gap을 바로 표시(gapTrend=closing 슬롯, windowSeconds=추월까지 남은 시간).
            gaugeHud.Show(targetDriver, null, null, gapSeconds, gapTrend, windowSeconds, lastProbability);
            if (newBattle) PlayPing();   // 새 배틀일 때만 방향 효과음(같은 배틀 반복 재생 X → 덜 거슬림)
        }

        /// <summary>배틀이 이어지는 동안 스트림이 호출 — 같은 드라이버면 표시 시간만 갱신(팝인/핑 없이 유지).</summary>
        public void KeepAlive(int driverNumber)
        {
            if (activeUntil < 0f) return;                                   // 안 떠 있으면 무시
            if (driverNumber > 0 && driverNumber != targetDriver) return;  // 다른 드라이버면 무시
            activeUntil = Time.time + Mathf.Max(0.5f, holdSeconds);
        }

        public void Clear()
        {
            if (targetView != null) targetView.ClearOvertakeApproachRibbon();
            if (_label != null) _label.gameObject.SetActive(false);
            if (_gaugeHud != null) _gaugeHud.Clear();
            targetView = null;
            targetDriver = 0;
            riskLabel = null;
            activeUntil = -1f;
        }

        void Update()
        {
            if (activeUntil < 0f) return;
            ReplayPlayer p = Player;
            if (p == null)
            {
                LogBlocked("ReplayPlayer not found");
                return;
            }
            if (!p.HasDataset)
            {
                LogBlocked($"dataset not loaded player={p.name}#{p.GetInstanceID()}");
                return;
            }
            if (Time.time >= activeUntil) { Clear(); return; }

            if (targetView == null) targetView = FindCarView(targetDriver);
            if (targetView == null)
            {
                LogBlocked($"driver car view not found driver={targetDriver}");
                return;
            }

            float fade = Mathf.Clamp01((activeUntil - Time.time) / Mathf.Max(0.01f, holdSeconds));
            float intensity = baseIntensity * Mathf.Lerp(0.5f, 1f, fade);

            _ribbon.overtakerGlowColor = predictionGlowColor;   // 인스펙터 변경 반영
            targetView.SetOvertakeApproachRibbon(
                _ribbon, overtaker: true, intensity: intensity, replayTime: p.CurrentTime);

            UpdateLabel();
        }

        void LogBlocked(string reason)
        {
            _diagTimer += Time.unscaledDeltaTime;
            if (_diagTimer < 1f) return;
            _diagTimer = 0f;
            Debug.LogWarning($"[AIBridge] predictOvertake blocked: {reason}");
        }

        void PlayPing()
        {
            if (pingClip == null) return;
            if (_audio == null)
            {
                var go = new GameObject("PredictPingAudio");
                go.transform.SetParent(transform, false);
                _audio = go.AddComponent<AudioSource>();
                _audio.spatialBlend = 1f;                 // 3D(방향감)
                _audio.rolloffMode = AudioRolloffMode.Linear;
                _audio.minDistance = 1f;
                _audio.maxDistance = 100000f;             // 멀어도 거의 안 작아지게(방향만)
                _audio.playOnAwake = false;
            }
            if (targetView != null) _audio.transform.position = targetView.transform.position;
            _audio.PlayOneShot(pingClip, pingVolume);
        }

        void UpdateLabel()
        {
            // 큰 월드 텍스트('Overtake Risk High')는 제거 — HUD가 정보를 다 보여주므로 시야만 가림.
            // showLabel(인스펙터 저장값)과 무관하게 항상 끔. 방향 사운드 위치만 갱신.
            if (_label != null) _label.gameObject.SetActive(false);
            if (_audio != null && targetView != null)
                _audio.transform.position = targetView.transform.position;
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
