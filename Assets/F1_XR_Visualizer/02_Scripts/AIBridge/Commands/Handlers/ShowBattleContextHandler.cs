// AIBridge/Commands/Handlers/ShowBattleContextHandler.cs
// showBattleContext 명령 → 두 차(subject↔target) 사이에 "Battle Lens" 오버레이를 표시한다.
//   ▸ 앰버 그라데이션 Gap Line + 소프트 글로우 언더레이 + 좁혀지는 방향으로 흐르는 점선
//   ▸ 주황 예측 화살표(subject→target, 길이 = 3초 뒤 좁혀지는 비율) + 예측 갭 위치 고스트 마커
//   ▸ 근사 글래스 HUD 배지: 라운드 반투명 패널 + 앰버 림 + F1 폰트 텍스트 + OVERTAKE PRESSURE 게이지
// 전부 메인 맵의 '실제 차 위치'에 월드공간으로 뜨고, Camera.main(=XR 헤드셋/데스크톱 카메라)을
// 바라보게 빌보드된다. 시간 조작 없이 매 프레임 실시간 위치를 따라간다.
//
// 디자인 언어(기존 유지): 앰버 rgb(255,209,20)=예측/접근 시그니처(추월 리본과 동일),
//   주황 rgb(255,128,26)=closing/위협. 폰트=프로젝트 F1 폰트(ReplayPlayer.carLabelFont 자동 사용).
//
// AI측(app/agent/tools.py show_battle_context)이 predicted_gap_seconds / predict_horizon_sec /
//   trend / drs / confidence 를 실어 보낸다.
//
// 전부 이 핸들러 안에서 런타임 생성 → 팀원 프리팹/공유설정 안 건드림(드롭인 1파일).
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

        [Tooltip("배지 폰트. 비우면 ReplayPlayer.carLabelFont → TMP 기본 순으로 자동 사용(프로젝트 F1 폰트)")]
        public TMP_FontAsset badgeFont;

        [Header("표시 시간")]
        [Tooltip("오버레이 유지 시간(초). 이후 자동으로 꺼진다")]
        public float holdSeconds = 6f;

        [Header("색 (기존 디자인 언어 유지)")]
        [Tooltip("앰버 = 예측/접근 시그니처 rgb(255,209,20)")]
        public Color amberColor = new Color(1f, 0.82f, 0.08f, 0.95f);
        [Tooltip("주황 = closing/위협 rgb(255,128,26)")]
        public Color closingColor = new Color(1f, 0.5f, 0.1f, 1f);
        [Tooltip("그 외(stable/opening) 배지·텍스트 색")]
        public Color neutralColor = new Color(0.85f, 0.85f, 0.9f, 1f);

        [Header("Gap Line")]
        [Tooltip("선 두께(차 스케일 배수). 트랙이 작아도 비율 유지")]
        public float lineWidth = 0.06f;
        [Tooltip("글로우 언더레이 두께 배수(선 두께 대비). 소프트한 발광 느낌")]
        public float glowWidthMul = 3.2f;
        [Tooltip("흐르는 점선 스크롤 속도(초당 UV). 좁혀지는 방향으로 흐른다")]
        public float dashScrollSpeed = 1.6f;

        [Header("예측 화살표")]
        [Tooltip("3초 뒤 예측 갭 화살표를 Gap Line 위로 띄우는 높이(차 스케일 배수)")]
        public float arrowHeight = 0.4f;
        [Tooltip("이 값(초)보다 적게 좁혀지면 화살표를 그리지 않음(잡음 방지)")]
        public float arrowMinClosing = 0.03f;

        [Header("배지(근사 글래스)")]
        public float badgeFontSize = 6f;
        [Tooltip("두 차 중점에서 위로 띄우는 높이(차 스케일 배수)")]
        public float badgeHeight = 1.25f;
        [Tooltip("패널 반투명 다크 색(글래스 근사)")]
        public Color panelColor = new Color(0.05f, 0.08f, 0.11f, 0.72f);
        [Tooltip("OVERTAKE PRESSURE 게이지 표시 여부")]
        public bool showConfidenceBar = true;

        [Header("테스트(AI 없이 씬 확인용)")]
        public int testSubject = 44;
        public int testTarget = 16;

        // ── 명령 상태 ──
        int subjectDriver, targetDriver;
        float activeUntil = -1f;
        ReplayCarView subjectView, targetView;
        float curGap, predGap = -1f, predGapStd = -1f, horizon = 3f, confidence;
        bool isClosing, hasDrs;
        string badgeText = "";
        Color accentColor = Color.white;   // closing=주황 / 그 외=앰버

        // ── 런타임 생성 리소스 ──
        LineRenderer _line, _glow, _dash, _arrow, _bracket;   // _bracket = 예측 불확실성 브래킷(±σ)
        Transform _badgeRoot;
        TextMeshPro _badge;
        SpriteRenderer _panel, _rim, _confBg, _confFill, _ghost;
        static Sprite _whiteSprite, _roundedSprite;
        static Texture2D _dashTex;
        float _dashOffset;
        OvertakeGaugeHud _gaugeHud;

        /// <summary>showBattleContext 진입점. (dispatcher가 호출하는 시그니처)</summary>
        /// <param name="predictedGap">3초 뒤 예측 갭(초). 없으면 음수(-1) 전달.</param>
        /// <param name="horizonSec">예측 지평(초, 기본 3).</param>
        /// <param name="predictedGapStd">예측 불확실성 ±σ(초). 없으면 음수(-1) → 브래킷 생략.</param>
        public void Handle(int subject, int target, float gapSeconds, float predictedGap, float horizonSec,
                           string trend, bool drs, float confidence, string reason,
                           float predictedGapStd = -1f,
                           string driverName = null,
                           string teamName = null,
                           float? airTempC = null,
                           float? trackTempC = null)
        {
            if (subject <= 0 || target <= 0) return;
            subjectDriver = subject;
            targetDriver = target;
            subjectView = null;   // 매 명령마다 번호로 다시 찾음(차가 재생성됐을 수 있음)
            targetView = null;

            curGap = gapSeconds;
            predGap = predictedGap;
            predGapStd = predictedGapStd;
            horizon = horizonSec > 0f ? horizonSec : 3f;
            this.confidence = Mathf.Clamp01(confidence);
            hasDrs = drs;
            isClosing = (trend == "closing");
            accentColor = isClosing ? closingColor : amberColor;

            // 배지: "0.8s → 0.4±0.1s (3s) · Closing · DRS"  (±σ는 있을 때만)
            string t = string.IsNullOrEmpty(trend) ? "" : char.ToUpper(trend[0]) + trend.Substring(1);
            string label = $"{gapSeconds:0.0}s";
            if (predictedGap >= 0f)
            {
                label += $"  →  {predictedGap:0.0}";
                if (predictedGapStd > 0f) label += $"±{predictedGapStd:0.1}";
                label += $"s ({horizon:0}s)";
            }
            if (!string.IsNullOrEmpty(t)) label += $"  ·  {t}";
            if (drs) label += "  ·  DRS";
            badgeText = label;

            float? closingRate = null;
            if (predictedGap >= 0f && horizon > 0.001f)
                closingRate = (predictedGap - gapSeconds) / horizon;

            if (_gaugeHud == null)
                _gaugeHud = FindFirstObjectByType<OvertakeGaugeHud>() ?? GetComponent<OvertakeGaugeHud>() ?? gameObject.AddComponent<OvertakeGaugeHud>();
            _gaugeHud.Show(subject, driverName, teamName, gapSeconds, closingRate, horizon, confidence, airTempC, trackTempC);

            activeUntil = Time.time + holdSeconds;
        }

        /// <summary>즉시 끄기(경기 전환 시 디스패처가 호출).</summary>
        public void Clear()
        {
            activeUntil = -1f;
            SetActive(_line, false); SetActive(_glow, false); SetActive(_dash, false); SetActive(_arrow, false);
            SetActive(_bracket, false);
            if (_ghost) _ghost.gameObject.SetActive(false);
            if (_badgeRoot) _badgeRoot.gameObject.SetActive(false);
            if (_gaugeHud != null) _gaugeHud.Clear();
        }

        void SetActive(Component c, bool on) { if (c) c.gameObject.SetActive(on); }

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

            UpdateGapLine(a, b, carScale);
            UpdateArrow(a, b, carScale);
            UpdateBadge(a, b, carScale);
        }

        // ── Gap Line: 글로우 언더레이 + 앰버→주황 그라데이션 + 흐르는 점선 ──
        void UpdateGapLine(Vector3 a, Vector3 b, float carScale)
        {
            EnsureLines();
            float w = lineWidth * carScale;

            // 1) 소프트 글로우(넓고 투명) — 파이프라인 독립적 발광 근사
            Color glow = accentColor; glow.a *= 0.28f;
            _glow.gameObject.SetActive(true);
            _glow.startWidth = _glow.endWidth = w * glowWidthMul;
            _glow.startColor = _glow.endColor = glow;
            _glow.SetPosition(0, a); _glow.SetPosition(1, b);

            // 2) 메인 라인(앰버 subject → 주황 target 그라데이션)
            _line.gameObject.SetActive(true);
            _line.startWidth = _line.endWidth = w;
            _line.startColor = amberColor;
            _line.endColor = isClosing ? closingColor : amberColor;
            _line.SetPosition(0, a); _line.SetPosition(1, b);

            // 3) 흐르는 점선(좁혀지는 방향 = subject→target 으로 스크롤)
            _dash.gameObject.SetActive(true);
            _dash.startWidth = _dash.endWidth = w * 0.6f;
            Color dc = Color.white; dc.a = 0.85f;
            _dash.startColor = _dash.endColor = dc;
            _dash.SetPosition(0, a); _dash.SetPosition(1, b);
            float len = Vector3.Distance(a, b);
            _dashOffset -= dashScrollSpeed * Time.deltaTime;   // 음수 = subject→target 방향
            if (_dash.material != null)
            {
                _dash.material.mainTextureScale = new Vector2(1f, Mathf.Max(1f, len / Mathf.Max(0.001f, carScale) * 2f));
                _dash.material.mainTextureOffset = new Vector2(0f, _dashOffset);
            }
        }

        // ── 예측 화살표: predGap 유효하고 '의미 있게' 좁혀질 때만. 길이 = 좁혀지는 비율 ──
        void UpdateArrow(Vector3 a, Vector3 b, float carScale)
        {
            EnsureArrow();
            float closing = curGap - predGap;   // +면 좁혀짐
            if (predGap < 0f || closing < arrowMinClosing || curGap <= 0.001f)
            {
                _arrow.gameObject.SetActive(false);
                if (_ghost) _ghost.gameObject.SetActive(false);
                return;
            }

            Vector3 up = Vector3.up * (arrowHeight * carScale);
            Vector3 pa = a + up, pb = b + up;
            Vector3 dir = pb - pa;
            float dist = dir.magnitude;
            if (dist < 1e-4f) { _arrow.gameObject.SetActive(false); return; }
            dir /= dist;

            float frac = Mathf.Clamp(closing / curGap, 0.15f, 0.9f);
            Vector3 tip = pa + dir * (dist * frac);
            Vector3 side = Vector3.Cross(dir, Vector3.up).normalized;
            float hw = Mathf.Max(lineWidth * carScale * 4f, dist * 0.045f);   // 화살촉 크기
            Vector3 back = tip - dir * hw;
            Vector3 h1 = back + side * hw, h2 = back - side * hw;

            // 은은한 펄스(굵기)로 '예측 압박' 강조
            float pulse = 1f + 0.12f * Mathf.Sin(Time.time * 6f);
            _arrow.gameObject.SetActive(true);
            _arrow.startColor = _arrow.endColor = closingColor;
            _arrow.startWidth = _arrow.endWidth = lineWidth * carScale * 1.1f * pulse;
            _arrow.positionCount = 5;
            _arrow.SetPosition(0, pa);
            _arrow.SetPosition(1, tip);
            _arrow.SetPosition(2, h1);
            _arrow.SetPosition(3, tip);
            _arrow.SetPosition(4, h2);

            // 고스트 마커: 3초 뒤 예측 갭 '지점'(target 기준 predGap 비율만큼 앞) 표시
            EnsureGhost();
            _ghost.gameObject.SetActive(true);
            _ghost.color = new Color(closingColor.r, closingColor.g, closingColor.b, 0.8f);
            _ghost.transform.position = tip;
            _ghost.transform.localScale = Vector3.one * (hw * 0.7f);
            BillboardTo(_ghost.transform);

            UpdateBracket(pa, dir, dist, side, frac, hw, carScale);
        }

        // ── 예측 불확실성 브래킷(⊣ ⊢): 예측 갭 ±σ 범위를 갭 라인 위에 에러바로 표시 ──
        //   갭 값 g → 라인 위 위치 비율 f(g) = (curGap - g)/curGap (subject=0, target=1).
        //   좁을수록(σ 작음) 확신, 넓을수록 불확실. predGapStd<=0 이면 생략.
        void UpdateBracket(Vector3 pa, Vector3 dir, float dist, Vector3 side,
                           float frac, float hw, float carScale)
        {
            EnsureBracket();
            if (predGapStd <= 0f || curGap <= 0.001f)
            {
                _bracket.gameObject.SetActive(false);
                return;
            }
            float sigmaFrac = predGapStd / curGap;                 // ±σ 를 라인 비율로 환산
            float fHi = Mathf.Clamp01(frac - sigmaFrac);           // predGap+σ (subject 쪽)
            float fLo = Mathf.Clamp01(frac + sigmaFrac);           // predGap−σ (target 쪽)
            if (fLo - fHi < 1e-3f) { _bracket.gameObject.SetActive(false); return; }

            Vector3 pHi = pa + dir * (dist * fHi);
            Vector3 pLo = pa + dir * (dist * fLo);
            float capHalf = hw * 0.8f;

            _bracket.gameObject.SetActive(true);
            Color bc = closingColor; bc.a = 0.85f;
            _bracket.startColor = _bracket.endColor = bc;
            _bracket.startWidth = _bracket.endWidth = lineWidth * carScale * 0.6f;
            // I-빔 폴리라인: 위 캡 → 위스커 → 아래 캡
            _bracket.positionCount = 6;
            _bracket.SetPosition(0, pHi + side * capHalf);
            _bracket.SetPosition(1, pHi - side * capHalf);
            _bracket.SetPosition(2, pHi);
            _bracket.SetPosition(3, pLo);
            _bracket.SetPosition(4, pLo + side * capHalf);
            _bracket.SetPosition(5, pLo - side * capHalf);
        }

        // ── 근사 글래스 배지: 라운드 패널 + 앰버 림 + F1 폰트 텍스트 + 게이지 ──
        void UpdateBadge(Vector3 a, Vector3 b, float carScale)
        {
            EnsureBadge();
            _badgeRoot.gameObject.SetActive(true);
            _badgeRoot.position = (a + b) * 0.5f + Vector3.up * (badgeHeight * carScale);
            _badgeRoot.localScale = Vector3.one * carScale;
            BillboardTo(_badgeRoot);

            _badge.font = ResolveFont();
            _badge.text = badgeText;
            _badge.fontSize = badgeFontSize;
            _badge.color = isClosing ? closingColor : neutralColor;

            // 텍스트 실제 폭에 맞춰 패널 크기 조절(가로만 대략 추정)
            Vector2 pref = _badge.GetPreferredValues(badgeText);
            float pw = pref.x + badgeFontSize * 0.9f;     // 좌우 패딩
            float ph = pref.y + badgeFontSize * (showConfidenceBar ? 1.5f : 0.7f);

            _panel.transform.localScale = new Vector3(pw, ph, 1f);
            _rim.transform.localScale = new Vector3(pw + badgeFontSize * 0.18f, ph + badgeFontSize * 0.18f, 1f);
            _panel.color = panelColor;
            _rim.color = new Color(accentColor.r, accentColor.g, accentColor.b, 0.55f);

            // OVERTAKE PRESSURE 게이지(= confidence)
            if (showConfidenceBar)
            {
                _confBg.gameObject.SetActive(true);
                _confFill.gameObject.SetActive(true);
                float barW = pw * 0.82f, barH = badgeFontSize * 0.28f;
                float barY = -ph * 0.5f + barH * 1.4f;
                _confBg.transform.localPosition = new Vector3(0f, barY, -0.02f);
                _confBg.transform.localScale = new Vector3(barW, barH, 1f);
                _confBg.color = new Color(1f, 1f, 1f, 0.14f);

                float fillW = barW * Mathf.Clamp01(confidence);
                _confFill.transform.localPosition = new Vector3(-(barW - fillW) * 0.5f, barY, -0.03f);
                _confFill.transform.localScale = new Vector3(Mathf.Max(0.0001f, fillW), barH, 1f);
                _confFill.color = accentColor;
                _badge.rectTransform.localPosition = new Vector3(0f, badgeFontSize * 0.35f, -0.04f);
            }
            else
            {
                _confBg.gameObject.SetActive(false);
                _confFill.gameObject.SetActive(false);
                _badge.rectTransform.localPosition = new Vector3(0f, 0f, -0.04f);
            }
        }

        // ───────────────────────── helpers ─────────────────────────

        void BillboardTo(Transform t)
        {
            Camera cam = Camera.main;
            if (cam != null)   // 기존 라벨과 동일한 빌보드(헤드셋/데스크톱 공통)
                t.rotation = Quaternion.LookRotation(t.position - cam.transform.position, Vector3.up);
        }

        TMP_FontAsset ResolveFont()
        {
            if (badgeFont != null) return badgeFont;
            ReplayPlayer p = Player;
            if (p != null && p.carLabelFont != null) return p.carLabelFont;   // 프로젝트 F1 폰트
            return TMP_Settings.defaultFontAsset;
        }

        void EnsureLines()
        {
            if (_glow == null) _glow = MakeLine("BattleGapGlow");
            if (_line == null) _line = MakeLine("BattleGapLine");
            if (_dash == null)
            {
                _dash = MakeLine("BattleGapDash");
                _dash.textureMode = LineTextureMode.Tile;
                if (_dash.material != null) _dash.material.mainTexture = DashTexture();
            }
        }

        void EnsureArrow() { if (_arrow == null) { _arrow = MakeLine("BattlePredictArrow"); _arrow.positionCount = 5; } }

        void EnsureBracket() { if (_bracket == null) { _bracket = MakeLine("BattleUncertaintyBracket"); _bracket.positionCount = 6; } }

        LineRenderer MakeLine(string goName)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.material = new Material(Shader.Find("Sprites/Default"));   // unlit·투명·URP 호환·핑크 없음
            lr.positionCount = 2;
            lr.numCapVertices = 6;
            lr.numCornerVertices = 4;
            lr.useWorldSpace = true;
            lr.textureMode = LineTextureMode.Stretch;
            lr.alignment = LineAlignment.View;   // 항상 카메라를 향한 리본(XR/데스크톱 공통)
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        void EnsureGhost() { if (_ghost == null) _ghost = MakeSprite("BattlePredictGhost", RoundedSprite(), transform); }

        void EnsureBadge()
        {
            if (_badgeRoot != null) return;
            _badgeRoot = new GameObject("BattleBadge").transform;
            _badgeRoot.SetParent(transform, false);

            _rim = MakeSprite("Rim", RoundedSprite(), _badgeRoot);
            _rim.transform.localPosition = new Vector3(0f, 0f, 0.02f);
            _panel = MakeSprite("Panel", RoundedSprite(), _badgeRoot);
            _panel.transform.localPosition = new Vector3(0f, 0f, 0.01f);
            _confBg = MakeSprite("ConfBg", WhiteSprite(), _badgeRoot);
            _confFill = MakeSprite("ConfFill", WhiteSprite(), _badgeRoot);

            var go = new GameObject("Text");
            go.transform.SetParent(_badgeRoot, false);
            _badge = go.AddComponent<TextMeshPro>();
            _badge.alignment = TextAlignmentOptions.Center;
            _badge.enableWordWrapping = false;
            _badge.rectTransform.sizeDelta = new Vector2(40f, 8f);
        }

        SpriteRenderer MakeSprite(string goName, Sprite sprite, Transform parent)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            // drawMode=Simple + transform.localScale 로 크기 제어(9-slice 미사용 → 에셋/사이즈 의존 최소).
            // 배지 절대 크기는 badgeFontSize 로 인스펙터에서 미세 조정 가능.
            return sr;
        }

        ReplayCarView FindCarView(int number)
        {
            ReplayCarView[] views = FindObjectsByType<ReplayCarView>(FindObjectsSortMode.None);
            for (int i = 0; i < views.Length; i++)
                if (views[i] != null && views[i].driverNumber == number)
                    return views[i];
            return null;
        }

        // ── 런타임 생성 스프라이트/텍스처(에셋 의존 0) ──
        static Sprite WhiteSprite()
        {
            if (_whiteSprite != null) return _whiteSprite;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color32[16];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px); tex.Apply();
            _whiteSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f,
                                         0, SpriteMeshType.FullRect, new Vector4(1, 1, 1, 1));
            return _whiteSprite;
        }

        static Sprite RoundedSprite()
        {
            if (_roundedSprite != null) return _roundedSprite;
            int W = 64, H = 32, r = 12;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    bool inside = true;
                    int cx = -1, cy = -1;
                    if (x < r && y < r) { cx = r; cy = r; }
                    else if (x >= W - r && y < r) { cx = W - r - 1; cy = r; }
                    else if (x < r && y >= H - r) { cx = r; cy = H - r - 1; }
                    else if (x >= W - r && y >= H - r) { cx = W - r - 1; cy = H - r - 1; }
                    if (cx >= 0)
                    {
                        float dx = x - cx, dy = y - cy;
                        inside = (dx * dx + dy * dy) <= (r * r);
                    }
                    px[y * W + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
                }
            tex.SetPixels32(px); tex.Apply();
            // 9-slice border(r) → 스케일해도 코너 반경 유지
            _roundedSprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 32f,
                                           0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
            return _roundedSprite;
        }

        static Texture2D DashTexture()
        {
            if (_dashTex != null) return _dashTex;
            int H = 16;
            _dashTex = new Texture2D(1, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat };
            var px = new Color32[H];
            for (int y = 0; y < H; y++)
            {
                byte a = (byte)(y % H < H * 0.45f ? 255 : 0);   // 대시 45% / 갭 55%
                px[y] = new Color32(255, 255, 255, a);
            }
            _dashTex.SetPixels32(px); _dashTex.Apply();
            return _dashTex;
        }

        [ContextMenu("Test Show Battle")]
        void TestShowBattle()
            => Handle(testSubject, testTarget, 0.8f, 0.4f, 3f, "closing", true, 0.76f, "test", 0.15f);
    }
}
#endif
