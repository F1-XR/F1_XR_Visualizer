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
        public bool showLabel = true;
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

        int targetDriver;
        float baseIntensity;
        float lastProbability;
        string riskLabel;
        float activeUntil = -1f;
        ReplayCarView targetView;

        OvertakeApproachRibbonSettings _ribbon;   // 예측 전용(노랑). 공유설정 안 씀
        TextMeshPro _label;
        AudioSource _audio;
        float _diagTimer;

        void Awake()
        {
            _ribbon = new OvertakeApproachRibbonSettings();     // 기본값 복제
            _ribbon.overtakerGlowColor = predictionGlowColor;  // 색만 노랑으로
        }

        public void Handle(int driverNumber, float probability, string label = null)
        {
            if (driverNumber <= 0) return;
            if (targetView != null && targetDriver != driverNumber)
                targetView.ClearOvertakeApproachRibbon();

            targetDriver = driverNumber;
            lastProbability = Mathf.Clamp01(probability);
            riskLabel = string.IsNullOrWhiteSpace(label) ? labelFormat : label;
            baseIntensity = Mathf.Max(minIntensity, lastProbability * intensityScale);
            activeUntil = Time.time + Mathf.Max(0.5f, holdSeconds);
            targetView = null;

            Debug.Log($"[AIBridge] predictOvertake handle driver={targetDriver} probability={lastProbability:0.00} hold={holdSeconds:0.##}s");
            PlayPing();   // 명령 도착 즉시 방향 효과음
        }

        public void Clear()
        {
            if (targetView != null) targetView.ClearOvertakeApproachRibbon();
            if (_label != null) _label.gameObject.SetActive(false);
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
            if (!showLabel) { if (_label != null) _label.gameObject.SetActive(false); return; }
            if (_label == null)
            {
                var go = new GameObject("PredictProbabilityLabel");
                _label = go.AddComponent<TextMeshPro>();
                _label.alignment = TextAlignmentOptions.Center;
            }
            _label.gameObject.SetActive(true);
            _label.color = predictionGlowColor;
            _label.fontSize = labelFontSize;
            _label.text = string.IsNullOrWhiteSpace(riskLabel)
                ? "Overtake Risk High"
                : riskLabel;

            // 라벨을 대상 차의 월드 스케일에 맞춘다 → 테이블탑/실물 어느 배율에서도 비율 일정.
            // (전엔 절대 크기라 작은 트랙에서 글자가 거대해지고 너무 높이(천장) 떴다.)
            float carScale = targetView.transform.lossyScale.y;
            if (carScale <= 0f) carScale = 1f;
            _label.transform.localScale = Vector3.one * carScale;
            _label.transform.position =
                targetView.transform.position + Vector3.up * (labelHeight * carScale);
            Camera cam = Camera.main;
            if (cam != null)
                _label.transform.rotation = Quaternion.LookRotation(
                    _label.transform.position - cam.transform.position, Vector3.up);   // 빌보드

            if (_audio != null) _audio.transform.position = targetView.transform.position;
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
