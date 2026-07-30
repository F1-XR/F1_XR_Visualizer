// AIBridge/PointOutWatcher.cs
// 능동 안내(pointOut): 리플레이를 지켜보다 중요한 순간(추월·사고)에 AI가 스스로 강조·안내한다.
//
// 방식: 리플레이라 데이터가 이미 정해져 있으므로, 매니페스트에서 '중요 순간'을 미리 뽑아
//       목록으로 만들어 두고, 재생 시각이 그 순간에 도달하면 발동한다(무거운 폴링 아님).
//   중요 순간 = ① 추월(events, confidence 필터)  ② 옐로/레드 플래그(항상)
//
// 기준(인스펙터에서 튜닝):
//   minConfidence : 추월 최소 확신도
//   cooldownSec   : 연속 안내 최소 간격(시끄러움 방지)
//   leadSec       : 순간보다 몇 초 미리 알릴지(볼 시간 확보)
#if AIBRIDGE_READY
using System;
using System.Collections.Generic;
using UnityEngine;
using F1XR.RestAPI.Replay;   // ReplayPlayer
using F1XR.RestAPI.Api;      // ReplayEventDto, RaceControlEventDto
using F1XR.AIBridge.Commands;

namespace F1XR.AIBridge
{
    public class PointOutWatcher : MonoBehaviour
    {
        [Tooltip("비우면 씬에서 자동 탐색")]
        public ReplayPlayer player;

        [Tooltip("강조 재사용(비우면 자동 탐색)")]
        public HighlightDriverHandler highlight;

        [Tooltip("음성 안내용 AgentBridge(비우면 자동 탐색). 없으면 강조만")]
        public AgentBridge bridge;

        [Header("기준 (튜닝)")]
        [Range(0f, 1f)] public float minConfidence = 0.6f;
        public float cooldownSec = 18f;
        public float leadSec = 2f;

        /// <summary>안내 발동 시 (driverNumber, 메시지). 자막 UI·TTS 트리거가 구독한다.</summary>
        public event Action<int, string> OnPointOut;

        struct Moment { public float t; public int driver; public int priority; public string message; }

        readonly List<Moment> _moments = new();
        int _next;
        float _lastFireT = -9999f;
        bool _built;
        string _datasetId;

        ReplayPlayer Player =>
            player != null ? player : (player = FindFirstObjectByType<ReplayPlayer>());
        HighlightDriverHandler Highlight =>
            highlight != null ? highlight : (highlight = FindFirstObjectByType<HighlightDriverHandler>());
        AgentBridge Bridge =>
            bridge != null ? bridge : (bridge = FindFirstObjectByType<AgentBridge>());

        void Update()
        {
            ReplayPlayer p = Player;
            if (p == null || !p.HasDataset) { _built = false; return; }

            // 경기(데이터셋) 바뀌면 순간 목록 재구성
            if (!_built || p.Manifest == null || p.Manifest.datasetId != _datasetId)
            {
                Build(p);
                return;
            }

            float now = p.CurrentTime;

            // 되감기·점프로 시간이 뒤로 갔으면 인덱스 재정렬
            if (_next > _moments.Count || (_next > 0 && _moments[_next - 1].t > now + leadSec))
                Reindex(now);

            // 도달한 순간들 처리
            while (_next < _moments.Count && _moments[_next].t - leadSec <= now)
            {
                Moment m = _moments[_next];
                _next++;
                if (m.t < now - 1f) continue;              // 너무 늦게 지난 건 스킵
                if (now - _lastFireT < cooldownSec) continue; // 쿨다운
                Fire(m, now);
            }
        }

        void Fire(Moment m, float now)
        {
            _lastFireT = now;
            Debug.Log($"[PointOut] {m.message} (t={m.t:0.0}s, driver={m.driver})");
            if (m.driver > 0) Highlight?.Handle(m.driver);   // 그 선수 강조
            Bridge?.SendSpeak(m.message);                     // 음성 안내(TTS 전용, 빠름)
            OnPointOut?.Invoke(m.driver, m.message);          // 자막 등 추가 훅
        }

        void Build(ReplayPlayer p)
        {
            _moments.Clear();
            DatasetManifestDto man = p.Manifest;
            if (man == null) return;

            // ① 추월 등 이벤트 (confidence 낮은 것만 제외)
            if (man.events != null)
            {
                foreach (ReplayEventDto e in man.events)
                {
                    if (e == null) continue;
                    if (e.confidence >= 0f && e.confidence < minConfidence) continue;
                    int drv = (e.driverNumbers != null && e.driverNumbers.Length > 0) ? e.driverNumbers[0] : 0;
                    // 시간에 민감한 안내라 아주 짧게(캔드). displayTitle은 길어서 안 씀.
                    string msg = drv > 0 ? $"{drv}번, 추월 나와요!" : "추월 나와요!";
                    _moments.Add(new Moment { t = e.anchorTime, driver = drv, priority = 1, message = msg });
                }
            }

            // ② 사고/깃발 (항상, 우선순위 높음)
            AddFlags(p.YellowFlags, 2, "옐로 플래그!");
            AddFlags(p.RedFlags, 3, "레드 플래그!");

            _moments.Sort((a, b) => a.t.CompareTo(b.t));
            _datasetId = man.datasetId;
            _built = true;
            _lastFireT = -9999f;
            Reindex(p.CurrentTime);
            Debug.Log($"[PointOut] 중요 순간 {_moments.Count}개 준비 (추월+깃발)");
        }

        void AddFlags(RaceControlEventDto[] evs, int prio, string msg)
        {
            if (evs == null) return;
            foreach (RaceControlEventDto e in evs)
                if (e != null)
                    _moments.Add(new Moment { t = e.t, driver = 0, priority = prio, message = msg });
        }

        void Reindex(float now)
        {
            _next = 0;
            while (_next < _moments.Count && _moments[_next].t - leadSec < now) _next++;
        }
    }
}
#endif
