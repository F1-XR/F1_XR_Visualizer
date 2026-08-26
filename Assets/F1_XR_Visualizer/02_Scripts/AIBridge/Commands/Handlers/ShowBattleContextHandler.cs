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

        [Header("화살표 밴드")]
        [Tooltip("밴드 두께(차 크기 배수). 클수록 두껍게")]
        public float bandWidth = 0.18f;
        [Tooltip("채도 낮은 옅은 형광노랑(뒤쪽) — 여기서 시작")]
        public Color bandColorStart = new Color(0.96f, 1f, 0.72f, 0.95f);
        [Tooltip("채도 높은 쨍한 형광노랑(앞/팁) — 여기로 갈수록 진해짐")]
        public Color bandColorEnd = new Color(0.82f, 1f, 0.02f, 1f);
        [Tooltip("화살촉 길이(밴드 두께 배수)")]
        public float arrowHeadMul = 2.6f;
        [Tooltip("셰브론(>) 사이 간격(차 크기 배수). 클수록 듬성듬성")]
        public float chevronSpacing = 1.15f;
        [Tooltip("셰브론 획 두께(셰브론 크기 배수)")]
        public float chevronBarRatio = 0.5f;
        [Tooltip("셰브론 최대 개수")]
        public int maxChevrons = 24;
        [Tooltip("밴드를 맵 위로 띄우는 높이(차 크기 배수). 맵에 안 가릴 정도로만")]
        public float bandLift = 0.18f;

        [Header("예측 화살표")]
        [Tooltip("3초 뒤 예측 갭 화살표를 Gap Line 위로 띄우는 높이(차 스케일 배수)")]
        public float arrowHeight = 0.4f;
        [Tooltip("이 값(초)보다 적게 좁혀지면 화살표를 그리지 않음(잡음 방지)")]
        public float arrowMinClosing = 0.03f;

        [Header("배지(근사 글래스)")]
        [Tooltip("갭 텍스트 높이 = 실제 차 크기 × 이 값(1.6~2.4 권장). 값 키우면 글씨 커짐")]
        public float badgeTextCarHeights = 2.1f;
        [Tooltip("갭 텍스트 색 — 쨍한 코발트 블루")]
        public Color gapTextColor = new Color(0.04f, 0.28f, 1f, 1f);
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
        readonly System.Collections.Generic.List<LineRenderer> _chevrons = new System.Collections.Generic.List<LineRenderer>();
        Transform _badgeRoot;
        TextMeshPro _badge;
        MeshRenderer _badgeRenderer;
        Material _badgeTopMaterial;
        TMP_FontAsset _badgeTopMaterialFont;
        SpriteRenderer _panel, _rim, _confBg, _confFill, _ghost;
        static Sprite _whiteSprite, _roundedSprite;
        static Texture2D _dashTex;
        float _dashOffset;
        OvertakeGaugeHud _gaugeHud;
        static readonly int _zTestId = Shader.PropertyToID("_ZTest");
        static readonly int _zTestModeId = Shader.PropertyToID("_ZTestMode");
        static readonly int _guiZTestId = Shader.PropertyToID("unity_GUIZTestMode");

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

            // 갭차이만 크게: "0.95s". 방향/접근은 화살표 밴드가 표현.
            badgeText = $"{gapSeconds:0.00}s";

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
            DeactivateChevronsFrom(0);
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
            // 단위 = 실제 차 월드 크기(라벨과 동일 기준). 미니맵/실물 공통 비율.
            float carScale = Mathf.Max(subjectView.GetVisualLength(), subjectView.GetVisualWidth());
            if (carScale <= 0f) carScale = subjectView.transform.lossyScale.y;
            if (carScale <= 0f) carScale = 1f;

            // 이제 안 쓰는 요소(글로우/점선/브래킷/고스트/패널)는 꺼둔다.
            SetActive(_glow, false); SetActive(_dash, false); SetActive(_bracket, false);
            if (_ghost) _ghost.gameObject.SetActive(false);
            if (_panel) _panel.gameObject.SetActive(false);
            if (_rim) _rim.gameObject.SetActive(false);
            if (_confBg) _confBg.gameObject.SetActive(false);
            if (_confFill) _confFill.gameObject.SetActive(false);

            UpdateArrowBand(a, b, carScale);
            UpdateGapText(a, b, carScale);
        }

        // ── 화살표 밴드: 뒤차→앞차 두꺼운 그라데이션 리본(코발트블루→형광노랑) + 화살촉 ──
        void UpdateArrowBand(Vector3 a, Vector3 b, float carScale)
        {
            EnsureBand();
            Vector3 up = Vector3.up * (bandLift * carScale);   // 맵 위로 살짝 띄워 안 가리게
            Vector3 pa = a + up, pb = b + up;
            Vector3 dir = pb - pa;
            float dist = dir.magnitude;
            if (dist < 1e-4f) { SetActive(_arrow, false); DeactivateChevronsFrom(0); return; }
            dir /= dist;
            Vector3 side = Vector3.Cross(dir, Vector3.up).normalized;

            float chevW = bandWidth * carScale;                       // 셰브론 반크기(가로=세로)
            float bt = Mathf.Max(0.001f, chevW * chevronBarRatio);    // 획 두께
            float headLen = Mathf.Min(dist * 0.45f, chevW * arrowHeadMul);
            float usable = Mathf.Max(0f, dist - headLen);             // 화살촉 앞 구간은 비움

            float spacing = Mathf.Max(0.001f, carScale * chevronSpacing);
            int n = Mathf.Clamp(Mathf.RoundToInt(usable / spacing), 1, Mathf.Max(1, maxChevrons));

            float pulse = 1f + 0.06f * Mathf.Sin(Time.time * 4f);
            // 개별 셰브론(>) 나열 — 뒤(옅은 저채도) → 앞(쨍한 고채도)
            for (int i = 0; i < n; i++)
            {
                float f = (i + 0.5f) / n;
                Vector3 c = pa + dir * (usable * f);
                float t = (n <= 1) ? 1f : i / (float)(n - 1);
                Color col = Color.Lerp(bandColorStart, bandColorEnd, t);
                col.a = Mathf.Lerp(bandColorStart.a * 0.45f, bandColorEnd.a, t);   // 뒤로 갈수록 옅게

                LineRenderer lr = GetChevron(i);
                lr.gameObject.SetActive(true);
                lr.startWidth = lr.endWidth = bt * pulse;
                lr.startColor = lr.endColor = col;
                Vector3 tip = c + dir * chevW;
                Vector3 top = c - dir * chevW + side * chevW;
                Vector3 bot = c - dir * chevW - side * chevW;
                lr.positionCount = 3;
                lr.SetPosition(0, top);
                lr.SetPosition(1, tip);
                lr.SetPosition(2, bot);
            }
            DeactivateChevronsFrom(n);

            // 맨 앞 솔리드 화살촉(가장 쨍한 형광노랑)
            Vector3 neck = pb - dir * headLen;
            float hw = chevW * 1.7f;
            Vector3 h1 = neck + side * hw, h2 = neck - side * hw;
            _arrow.gameObject.SetActive(true);
            _arrow.startColor = _arrow.endColor = bandColorEnd;
            _arrow.startWidth = _arrow.endWidth = chevW * 1.1f;
            _arrow.numCapVertices = 4;
            _arrow.positionCount = 3;
            _arrow.SetPosition(0, h1);
            _arrow.SetPosition(1, pb);
            _arrow.SetPosition(2, h2);
        }

        // ── 갭 텍스트: 패널 없이 밴드 위에 떠 있는 큰 텍스트(프로젝트 폰트) ──
        void UpdateGapText(Vector3 a, Vector3 b, float carScale)
        {
            EnsureText();
            _badgeRoot.gameObject.SetActive(true);
            float textLift = Mathf.Max(badgeHeight, bandLift + bandWidth * 0.75f);
            _badgeRoot.position = (a + b) * 0.5f + Vector3.up * (textLift * carScale);
            _badge.fontSize = badgeFontSize;
            float rootScale = (carScale * badgeTextCarHeights) / Mathf.Max(0.001f, badgeFontSize);
            _badgeRoot.localScale = Vector3.one * rootScale;
            BillboardTo(_badgeRoot);

            TMP_FontAsset f = ResolveFont();
            if (f != null && _badge.font != f) _badge.font = f;
            ApplyGapTextMaterial(_badge.font);
            ConfigureGapTextRenderer();
            _badge.text = badgeText;
            _badge.color = gapTextColor;
            _badge.rectTransform.localPosition = Vector3.zero;
            _badge.ForceMeshUpdate();
        }

        // ───────────────────────── helpers ─────────────────────────

        void EnsureBand()
        {
            if (_arrow == null) _arrow = MakeLine("BattleArrowHead");
        }

        LineRenderer GetChevron(int i)
        {
            while (_chevrons.Count <= i)
            {
                LineRenderer lr = MakeLine("BattleChevron" + _chevrons.Count);
                lr.numCapVertices = 4;      // 둥근 끝
                lr.numCornerVertices = 4;   // 둥근 꼭짓점(참고 이미지처럼)
                lr.positionCount = 3;
                _chevrons.Add(lr);
            }
            return _chevrons[i];
        }

        void DeactivateChevronsFrom(int start)
        {
            for (int i = start; i < _chevrons.Count; i++)
                if (_chevrons[i]) _chevrons[i].gameObject.SetActive(false);
        }

        void EnsureText()
        {
            if (_badgeRoot != null) return;
            _badgeRoot = new GameObject("BattleGapText").transform;
            _badgeRoot.SetParent(transform, false);
            var go = new GameObject("Text");
            go.transform.SetParent(_badgeRoot, false);
            _badge = go.AddComponent<TextMeshPro>();
            _badge.alignment = TextAlignmentOptions.Center;
            _badge.enableWordWrapping = false;
            _badge.fontStyle = FontStyles.Bold;
            _badge.enableAutoSizing = false;
            _badge.overflowMode = TextOverflowModes.Overflow;
            _badge.rectTransform.sizeDelta = new Vector2(40f, 8f);
            _badgeRenderer = go.GetComponent<MeshRenderer>();
            ConfigureGapTextRenderer();
        }

        void ConfigureGapTextRenderer()
        {
            if (_badge == null) return;
            if (_badgeRenderer == null)
                _badgeRenderer = _badge.GetComponent<MeshRenderer>();
            if (_badgeRenderer == null) return;

            _badgeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _badgeRenderer.receiveShadows = false;
            _badgeRenderer.sortingOrder = 30001;
        }

        void ApplyGapTextMaterial(TMP_FontAsset font)
        {
            if (_badge == null) return;
            if (font == null) font = _badge.font;
            if (font == null || font.material == null) return;

            if (_badgeTopMaterial == null || _badgeTopMaterialFont != font)
            {
                if (_badgeTopMaterial != null)
                    Destroy(_badgeTopMaterial);

                _badgeTopMaterialFont = font;
                _badgeTopMaterial = new Material(font.material)
                {
                    name = "BattleGapText_AlwaysOnTop",
                    renderQueue = 4000
                };

                Shader overlay = Shader.Find("TextMeshPro/Mobile/Distance Field Overlay") ??
                                 Shader.Find("TextMeshPro/Distance Field Overlay");
                if (overlay != null)
                    _badgeTopMaterial.shader = overlay;

                int always = (int)UnityEngine.Rendering.CompareFunction.Always;
                SetMaterialInt(_badgeTopMaterial, _guiZTestId, always);
                SetMaterialFloat(_badgeTopMaterial, _zTestId, always);
                SetMaterialFloat(_badgeTopMaterial, _zTestModeId, always);
            }

            _badge.fontSharedMaterial = _badgeTopMaterial;
        }

        static void SetMaterialInt(Material material, int propertyId, int value)
        {
            if (material != null && material.HasProperty(propertyId))
                material.SetInt(propertyId, value);
        }

        static void SetMaterialFloat(Material material, int propertyId, float value)
        {
            if (material != null && material.HasProperty(propertyId))
                material.SetFloat(propertyId, value);
        }

        void BillboardTo(Transform t)
        {
            Camera cam = Camera.main;
            if (cam != null)   // 기존 라벨과 동일한 빌보드(헤드셋/데스크톱 공통)
                t.rotation = Quaternion.LookRotation(t.position - cam.transform.position, Vector3.up);
        }

        static TMP_FontAsset _f1Font;
        TMP_FontAsset ResolveFont()
        {
            if (badgeFont != null) return badgeFont;
            // 씬에 연결돼 이미 로드된 프로젝트 F1 폰트(carLabelFont = Formula1-Bold SDF) 우선 → 확실히 렌더됨.
            ReplayPlayer p = Player;
            if (p != null && p.carLabelFont != null) return p.carLabelFont;
            if (_f1Font != null) return _f1Font;
            // (carLabelFont 미연결 씬 폴백) 로드된 Formula1-Bold 탐색
            TMP_FontAsset[] fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            for (int i = 0; i < fonts.Length; i++)
                if (fonts[i] != null && fonts[i].name.Contains("Formula1-Bold"))
                { _f1Font = fonts[i]; return _f1Font; }
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

        void EnsureGhost() { if (_ghost == null) _ghost = MakeSprite("BattlePredictGhost", CircleSprite(), transform); }

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
        // ── 셰브론(>>>) 타일 텍스처: 부드러운(페더) 가장자리로 형태가 자연스럽게 이어짐 ──
        static Texture2D _chevronTex;
        static Texture2D ChevronTexture()
        {
            if (_chevronTex != null) return _chevronTex;
            int W = 128, H = 64; float th = 0.20f, feather = 0.17f;
            _chevronTex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            {
                float v = y / (float)(H - 1);
                float uc = 1f - Mathf.Abs(v - 0.5f) * 2f;   // 셰브론 중심선(팁 v=0.5,u=1 → +길이방향)
                for (int x = 0; x < W; x++)
                {
                    float u = x / (float)(W - 1);
                    float d = Mathf.Min(Mathf.Abs(u - uc),
                              Mathf.Min(Mathf.Abs(u - (uc - 1f)), Mathf.Abs(u - (uc + 1f))));
                    float a = 1f - Mathf.SmoothStep(th, th + feather, d);
                    px[y * W + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                }
            }
            _chevronTex.SetPixels32(px); _chevronTex.Apply();
            return _chevronTex;
        }

        static Sprite _circleSprite;
        static Sprite CircleSprite()
        {
            if (_circleSprite != null) return _circleSprite;
            int D = 48; float r = D * 0.5f - 1f, c = (D - 1) * 0.5f;
            var tex = new Texture2D(D, D, TextureFormat.RGBA32, false);
            var px = new Color32[D * D];
            for (int y = 0; y < D; y++)
                for (int x = 0; x < D; x++)
                {
                    float dx = x - c, dy = y - c, d = Mathf.Sqrt(dx * dx + dy * dy);
                    // 속 빈 링(고스트 마커): 바깥 테두리만 채움 → 차 위 '이상한 blob' 대신 깔끔한 원
                    byte a = (byte)(d <= r && d >= r - D * 0.22f ? 255 : 0);
                    px[y * D + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px); tex.Apply();
            _circleSprite = Sprite.Create(tex, new Rect(0, 0, D, D), new Vector2(0.5f, 0.5f), D);
            return _circleSprite;
        }

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

        void OnDestroy()
        {
            if (_badgeTopMaterial != null)
                Destroy(_badgeTopMaterial);
        }
    }
}
#endif
