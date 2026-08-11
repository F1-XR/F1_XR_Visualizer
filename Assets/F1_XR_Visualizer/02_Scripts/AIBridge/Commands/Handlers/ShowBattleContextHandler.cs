// AIBridge/Commands/Handlers/ShowBattleContextHandler.cs
// showBattleContext 명령 → 두 차(subject↔target) 사이 Gap Line + 복합 배지("0.8s · Closing · DRS")를
// 메인 맵의 실제 차 위치에 잠깐 표시한다. 시간 조작 없이 실시간 위치를 매 프레임 따라간다.
//
// 재사용: PredictOvertakeRibbonHandler(차 찾기·수명·차 스케일 비례 라벨) 패턴을 그대로 따른다.
// 씬 세팅: AgentCommandDispatcher 가 붙은 오브젝트에 이 컴포넌트 추가(없으면 dispatcher가 자동 생성).
//   AI 없이 확인: 컴포넌트 우클릭 → "Test Show Battle" (Play 중, 리플레이 로드 상태에서).
#if AIBRIDGE_READY
using UnityEngine;
using TMPro;
using F1XR.RestAPI.Replay;   // ReplayPlayer, ReplayCarView

namespace F1XR.AIBridge.Commands
{
    public class ShowBattleContextHandler : MonoBehaviour
    {
        [Header("참조")]
        public ReplayPlayer player;
        ReplayPlayer Player => player != null ? player : (player = FindFirstObjectByType<ReplayPlayer>());

        [Header("표시 시간")]
        [Tooltip("Gap Line·배지를 유지하는 시간(초). 이후 자동으로 꺼진다")]
        public float holdSeconds = 6f;

        [Header("Gap Line")]
        public Color lineColor = new Color(1f, 0.82f, 0.08f, 0.9f);
        [Tooltip("선 두께(차 스케일 배수). 트랙이 작아도 비율 유지")]
        public float lineWidth = 0.06f;

        [Header("배지")]
        public float badgeFontSize = 6f;
        [Tooltip("두 차 중점에서 위로 띄우는 높이(차 스케일 배수)")]
        public float badgeHeight = 1.2f;
        [Tooltip("갭이 좁혀지는(closing) 위협 상황 색")]
        public Color closingColor = new Color(1f, 0.5f, 0.1f, 1f);
        [Tooltip("그 외(stable/opening) 색")]
        public Color neutralColor = new Color(0.85f, 0.85f, 0.9f, 1f);

        [Header("테스트(AI 없이 씬 확인용)")]
        public int testSubject = 44;
        public int testTarget = 16;

        int subjectDriver;
        int targetDriver;
        float activeUntil = -1f;
        ReplayCarView subjectView;
        ReplayCarView targetView;
        string badgeText = "";
        Color badgeColor = Color.white;

        LineRenderer _line;
        TextMeshPro _badge;

        /// <summary>showBattleContext 진입점. 두 차 사이 Gap Line + 배지를 표시한다.</summary>
        public void Handle(int subject, int target, float gapSeconds,
                           string trend, bool drs, float confidence, string reason)
        {
            if (subject <= 0 || target <= 0) return;
            subjectDriver = subject;
            targetDriver = target;
            subjectView = null;   // 매 명령마다 번호로 다시 찾음(차가 재생성됐을 수 있음)
            targetView = null;

            // 배지: "0.8s · Closing · DRS" (drs 없으면 DRS 생략, trend 없으면 생략)
            string t = string.IsNullOrEmpty(trend)
                ? "" : char.ToUpper(trend[0]) + trend.Substring(1);
            string label = $"{gapSeconds:0.0}s";
            if (!string.IsNullOrEmpty(t)) label += $"  ·  {t}";
            if (drs) label += "  ·  DRS";
            badgeText = label;
            badgeColor = (trend == "closing") ? closingColor : neutralColor;

            activeUntil = Time.time + holdSeconds;
        }

        /// <summary>즉시 끄기(경기 전환 시 디스패처가 호출).</summary>
        public void Clear()
        {
            activeUntil = -1f;
            if (_line != null) _line.gameObject.SetActive(false);
            if (_badge != null) _badge.gameObject.SetActive(false);
        }

        void Update()
        {
            if (activeUntil < 0f) return;
            ReplayPlayer p = Player;
            if (p == null || !p.HasDataset) return;
            if (Time.time >= activeUntil) { Clear(); return; }

            if (subjectView == null) subjectView = FindCarView(subjectDriver);
            if (targetView == null) targetView = FindCarView(targetDriver);
            if (subjectView == null || targetView == null) return;

            Vector3 a = subjectView.transform.position;
            Vector3 b = targetView.transform.position;
            float carScale = subjectView.transform.lossyScale.y;
            if (carScale <= 0f) carScale = 1f;

            // Gap Line
            EnsureLine();
            _line.gameObject.SetActive(true);
            _line.startColor = lineColor;
            _line.endColor = lineColor;
            _line.startWidth = lineWidth * carScale;
            _line.endWidth = lineWidth * carScale;
            _line.SetPosition(0, a);
            _line.SetPosition(1, b);

            // 배지(두 차 중점 위, 빌보드, 차 스케일 비례)
            EnsureBadge();
            _badge.gameObject.SetActive(true);
            _badge.text = badgeText;
            _badge.color = badgeColor;
            _badge.fontSize = badgeFontSize;
            _badge.transform.localScale = Vector3.one * carScale;
            _badge.transform.position = (a + b) * 0.5f + Vector3.up * (badgeHeight * carScale);
            Camera cam = Camera.main;
            if (cam != null)
                _badge.transform.rotation = Quaternion.LookRotation(
                    _badge.transform.position - cam.transform.position, Vector3.up);
        }

        void EnsureLine()
        {
            if (_line != null) return;
            var go = new GameObject("BattleGapLine");
            go.transform.SetParent(transform, false);
            _line = go.AddComponent<LineRenderer>();
            _line.material = new Material(Shader.Find("Sprites/Default"));
            _line.positionCount = 2;
            _line.numCapVertices = 4;
            _line.useWorldSpace = true;
            _line.textureMode = LineTextureMode.Stretch;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
        }

        void EnsureBadge()
        {
            if (_badge != null) return;
            var go = new GameObject("BattleBadge");
            go.transform.SetParent(transform, false);
            _badge = go.AddComponent<TextMeshPro>();
            _badge.alignment = TextAlignmentOptions.Center;
        }

        ReplayCarView FindCarView(int number)
        {
            ReplayCarView[] views = FindObjectsByType<ReplayCarView>(FindObjectsSortMode.None);
            for (int i = 0; i < views.Length; i++)
                if (views[i] != null && views[i].driverNumber == number)
                    return views[i];
            return null;
        }

        [ContextMenu("Test Show Battle")]
        void TestShowBattle()
            => Handle(testSubject, testTarget, 0.8f, "closing", true, 0.76f, "test");
    }
}
#endif
