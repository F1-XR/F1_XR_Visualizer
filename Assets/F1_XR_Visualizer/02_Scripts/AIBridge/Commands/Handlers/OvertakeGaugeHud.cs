// AIBridge/Commands/Handlers/OvertakeGaugeHud.cs
// Runtime-created HUD for overtake analysis. Matches the verified HTML mockup:
//   left = CLOSING (blue), center = GAP (red, big), right = WINDOW (green).
// Gauges open at the bottom and sweep over the top:
//   f=0 at bottom-right (-55deg), f=1 at bottom-left (235deg) [mirror flips it].
//
// Rendering: everything is drawn with UnityEngine.UI.Image primitives (not custom
// MaskableGraphic meshes), because custom OnPopulateMesh graphics were not rendering
// in this project's canvas. Images render reliably.
//
// Live motion: the needle + active arc animate every frame toward a target value
// (smooth sweep). Feed live telemetry with UpdateLive(...) each frame and the
// needles track it; Show(...) sets the initial snapshot.
//
// Works both as a flat Screen-Space-Overlay HUD and a camera-facing world-space XR panel.
#if AIBRIDGE_READY
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using F1XR.RestAPI.Replay;

namespace F1XR.AIBridge.Commands
{
    public sealed class OvertakeGaugeHud : MonoBehaviour
    {
        const float DistanceFromCamera = 1.25f;
        static readonly Vector2 CanvasSize = new Vector2(1040f, 480f);

        [Header("XR size")]
        [Tooltip("World-space scale. 0.0009 with 1040px canvas is about 0.94m wide.")]
        [SerializeField, Min(0.0002f)] float hudScale = 0.0009f;
        [SerializeField, Min(0.5f)] float holdSeconds = 4.5f;

        [Header("XR 배치 (WorldSpace일 때)")]
        [Tooltip("카메라(머리) 앞 거리(m)")]
        [SerializeField, Min(0.3f)] float dockDistance = 1.35f;
        [Tooltip("눈높이 대비 상하 오프셋(m). 음수=아래로 내려 시야 중앙/액션을 안 가림")]
        [SerializeField] float dockHeightOffset = -0.35f;
        [Tooltip("따라오는 부드러움. 낮을수록 느긋한 바디락(멀미↓), 높을수록 빠르게 따라붙음")]
        [SerializeField, Min(0.5f)] float dockFollow = 5f;
        [Tooltip("여기에 Transform 지정 시 그 자리에 고정(재생바/패널 옆 등, 앵커 따라감). 비우면 시야를 부드럽게 따라감. ※ 지정하려면 이 컴포넌트를 씬에 직접 추가")]
        public Transform dockAnchor;
        [Tooltip("Dock Anchor 기준 위치 오프셋(m)")]
        public Vector3 dockLocalOffset = Vector3.zero;
        [Tooltip("Dock Anchor 기준 회전 오프셋(도)")]
        public Vector3 dockLocalEuler = Vector3.zero;
        [Tooltip("체크 시: 이 컴포넌트가 붙은 GameObject 위치에 그대로 고정(에디터에서 눈으로 배치). 시야추적·앵커 무시.")]
        public bool useThisTransform = false;
        bool _posed;

        [Header("Font")]
        [Tooltip("비우면 Formula1-Bold SDF 또는 ReplayPlayer.carLabelFont를 자동 탐색")]
        public TMP_FontAsset formulaFont;

        [Header("Colors")]
        public Color red = new Color(0.882f, 0.024f, 0f, 0.95f);
        public Color blue = new Color(0.17f, 0.78f, 1f, 0.92f);
        public Color green = new Color(0.28f, 0.95f, 0.61f, 0.92f);

        public enum HudMode { Auto, ScreenOverlay, WorldSpaceXR }

        [Header("Display")]
        [Tooltip("기본 WorldSpaceXR = 재생바/순위표처럼 월드공간 UI → 헤드셋·평면 모니터 둘 다 보임. 순수 평면 오버레이만 원하면 ScreenOverlay.")]
        public HudMode displayMode = HudMode.WorldSpaceXR;
        [Tooltip("화면 오버레이일 때 HUD 배율")]
        [SerializeField, Min(0.1f)] float overlayScale = 1.1f;
        [Tooltip("화면 오버레이일 때 화면 중앙 기준 오프셋(px). 음수 y=아래로. 기본은 하단 배치로 시야 중앙 안 가림")]
        [SerializeField] Vector2 overlayPosition = new Vector2(0f, -200f);

        [Header("Animation")]
        [Tooltip("바늘이 목표치로 수렴하는 속도(클수록 빠릿함)")]
        [SerializeField, Min(0.5f)] float needleResponse = 9f;

        bool useWorldSpace;

        Canvas canvas;
        RectTransform root;
        GaugeUI closingGauge;
        GaugeUI gapGauge;
        GaugeUI windowGauge;
        TextMeshProUGUI airText;
        TextMeshProUGUI trackText;
        TextMeshProUGUI sessionText;
        TextMeshProUGUI driverNoText;
        TextMeshProUGUI driverNameText;
        TextMeshProUGUI teamText;
        TextMeshProUGUI footerLeftText;
        TextMeshProUGUI footerMidText;
        TextMeshProUGUI footerRightText;
        Camera xrCamera;
        float visibleUntil = -1f;
        ReplayPlayer player;
        int _currentDriver = -1;   // 현재 표시 중 드라이버(서버 스트리밍 gap 매칭용)

        public void Show(
            int driverNumber,
            string driverName,
            string teamName,
            float? gapSeconds,
            float? closingSecondsPerSecond,
            float? windowSeconds,
            float? probability,
            float? airTempC = null,
            float? trackTempC = null,
            int? lapNumber = null,
            string circuitName = "SUZUKA")
        {
            EnsureCanvas();
            ApplyFont(ResolveFont());
            _currentDriver = driverNumber;

            driverNoText.text = driverNumber > 0 ? driverNumber.ToString() : "--";
            driverNameText.text = string.IsNullOrWhiteSpace(driverName)
                ? DriverName(driverNumber)
                : driverName.ToUpperInvariant();
            teamText.text = string.IsNullOrWhiteSpace(teamName)
                ? DriverTeam(driverNumber)
                : teamName.ToUpperInvariant();
            gapGauge.SetCarNumber(driverNumber > 0 ? driverNumber : 0);

            airText.text = $"AIR {FormatTemp(airTempC)}";
            trackText.text = $"TRACK {FormatTemp(trackTempC)}";
            string circuit = string.IsNullOrWhiteSpace(circuitName) ? "SUZUKA" : circuitName.ToUpperInvariant();
            sessionText.text = lapNumber.HasValue
                ? $"{circuit} · LAP {lapNumber.Value} · AI ANALYSIS"
                : $"{circuit} · AI ANALYSIS";

            gapGauge.SetDisplay("GAP", gapSeconds.HasValue ? gapSeconds.Value.ToString("0.00") : "--", "seconds", GapNorm(gapSeconds, probability));
            closingGauge.SetDisplay("CLOSING", closingSecondsPerSecond.HasValue ? closingSecondsPerSecond.Value.ToString("0.00") : "--", "sec/s", ClosingNorm(closingSecondsPerSecond));
            windowGauge.SetDisplay("WINDOW", windowSeconds.HasValue ? "+" + Mathf.RoundToInt(windowSeconds.Value) : "--", "seconds", WindowNorm(windowSeconds, probability));

            footerLeftText.text = "OVERTAKE WINDOW";
            footerMidText.text = probability.HasValue ? $"ML {probability.Value:0.00}" : "ML --";
            footerRightText.text = gapSeconds.HasValue ? "DRS RANGE" : "PREDICT";

            ApplyAlwaysOnTop();   // 지형/오브젝트에 안 가리게(HUD는 항상 위)
            _topApplyFrames = 8;  // 이후 몇 프레임 더 재적용(게이지 니들/아크 등 늦생성 커버)
            canvas.gameObject.SetActive(true);
            visibleUntil = Time.unscaledTime + holdSeconds;
            if (useWorldSpace) UpdatePose();
        }

        /// <summary>실시간 갱신. 매 프레임 넘긴 값만 갱신하고 바늘이 부드럽게 따라감. 넘긴 항목만 반영.</summary>
        public void UpdateLive(
            float? gapSeconds = null,
            float? closingSecondsPerSecond = null,
            float? windowSeconds = null,
            float? probability = null,
            float? airTempC = null,
            float? trackTempC = null,
            bool keepAlive = true)
        {
            if (canvas == null || !canvas.gameObject.activeSelf) return;   // 떠 있을 때만 갱신

            if (airTempC.HasValue) airText.text = $"AIR {FormatTemp(airTempC)}";
            if (trackTempC.HasValue) trackText.text = $"TRACK {FormatTemp(trackTempC)}";

            if (gapSeconds.HasValue)
                gapGauge.SetDisplay("GAP", gapSeconds.Value.ToString("0.00"), "seconds", GapNorm(gapSeconds, probability));
            if (closingSecondsPerSecond.HasValue)
                closingGauge.SetDisplay("CLOSING", closingSecondsPerSecond.Value.ToString("0.00"), "sec/s", ClosingNorm(closingSecondsPerSecond));
            if (windowSeconds.HasValue)
                windowGauge.SetDisplay("WINDOW", "+" + Mathf.RoundToInt(windowSeconds.Value), "seconds", WindowNorm(windowSeconds, probability));
            if (probability.HasValue)
                footerMidText.text = $"ML {probability.Value:0.00}";

            if (keepAlive) visibleUntil = Time.unscaledTime + holdSeconds;
        }

        public void Clear()
        {
            visibleUntil = -1f;
            _currentDriver = -1;
            _posed = false;
            if (canvas != null)
                canvas.gameObject.SetActive(false);
        }

        /// <summary>서버가 스트리밍하는 실시간 gap을 표시(계산은 서버 담당, HUD는 표시만).
        /// HUD가 떠 있고 driver가 일치할 때만 적용. gapTrend&lt;0 = 좁혀짐(closing).</summary>
        public void UpdateLiveForDriver(int driverNumber, float? gapSeconds, float? gapTrend, float? windowSeconds)
        {
            if (canvas == null || !canvas.gameObject.activeSelf) return;
            if (driverNumber > 0 && _currentDriver > 0 && driverNumber != _currentDriver) return;
            visibleUntil = Time.unscaledTime + holdSeconds;   // 배틀 스트림이 살아있는 동안 HUD 유지(깜박임 방지)
            if (gapSeconds.HasValue)
                gapGauge.SetDisplay("GAP", gapSeconds.Value.ToString("0.00"), "seconds", GapNorm(gapSeconds, null));
            if (gapTrend.HasValue)
                closingGauge.SetDisplay("CLOSING", gapTrend.Value.ToString("0.00"), "sec/s", ClosingNorm(gapTrend.Value));
            if (windowSeconds.HasValue)
                windowGauge.SetDisplay("WINDOW", "+" + Mathf.RoundToInt(windowSeconds.Value), "seconds", WindowNorm(windowSeconds, null));
        }

        void LateUpdate()
        {
            if (canvas == null || !canvas.gameObject.activeSelf)
                return;

            if (visibleUntil > 0f && Time.unscaledTime >= visibleUntil)
            {
                Clear();
                return;
            }

            if (_topApplyFrames > 0) { _topApplyFrames--; ApplyAlwaysOnTop(); }

            if (useWorldSpace) UpdatePose();
        }

        void EnsureCanvas()
        {
            if (canvas != null)
                return;

            useWorldSpace = displayMode == HudMode.WorldSpaceXR
                || (displayMode == HudMode.Auto && UnityEngine.XR.XRSettings.isDeviceActive);

            GameObject canvasObject = new GameObject("Overtake Gauge HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.sortingOrder = 80;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();

            if (useWorldSpace)
            {
                canvas.renderMode = RenderMode.WorldSpace;
                canvasRect.sizeDelta = CanvasSize;
                canvasRect.localScale = Vector3.one * hudScale;

                root = CreateRect("Root", canvasRect);
                root.anchorMin = Vector2.zero;
                root.anchorMax = Vector2.one;
                root.offsetMin = Vector2.zero;
                root.offsetMax = Vector2.zero;
            }
            else
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;

                root = CreateRect("Root", canvasRect);
                root.anchorMin = new Vector2(0.5f, 0.5f);
                root.anchorMax = new Vector2(0.5f, 0.5f);
                root.pivot = new Vector2(0.5f, 0.5f);
                root.sizeDelta = CanvasSize;
                root.anchoredPosition = overlayPosition;
                root.localScale = Vector3.one * Mathf.Max(0.1f, overlayScale);
            }

            BuildBackplateAndFrame(root);
            BuildHeader(root);
            BuildGauges(root);
            BuildFooter(root);
            canvas.gameObject.SetActive(false);
        }

        void BuildBackplateAndFrame(RectTransform parent)
        {
            Image backplate = CreateImage("Backplate", parent, new Color(0f, 0f, 0f, 0.72f));
            Place(backplate.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            backplate.rectTransform.offsetMin = new Vector2(30f, 22f);
            backplate.rectTransform.offsetMax = new Vector2(-30f, -22f);

            float w = CanvasSize.x, h = CanvasSize.y;
            float x0 = -w * 0.5f, x1 = w * 0.5f, y0 = -h * 0.5f, y1 = h * 0.5f;
            Vector2[] poly =
            {
                new Vector2(x0 + w * 0.05f, y1),
                new Vector2(x0 + w * 0.30f, y1),
                new Vector2(x0 + w * 0.335f, y1 - h * 0.06f),
                new Vector2(x0 + w * 0.665f, y1 - h * 0.06f),
                new Vector2(x0 + w * 0.70f, y1),
                new Vector2(x0 + w * 0.95f, y1),
                new Vector2(x1, y1 - h * 0.12f),
                new Vector2(x1, y0 + h * 0.20f),
                new Vector2(x0 + w * 0.955f, y0),
                new Vector2(x0 + w * 0.70f, y0),
                new Vector2(x0 + w * 0.665f, y0 + h * 0.06f),
                new Vector2(x0 + w * 0.335f, y0 + h * 0.06f),
                new Vector2(x0 + w * 0.30f, y0),
                new Vector2(x0 + w * 0.05f, y0),
                new Vector2(x0, y0 + h * 0.16f),
                new Vector2(x0, y1 - h * 0.14f)
            };
            Color line = new Color(red.r, red.g, red.b, 0.9f);
            for (int i = 0; i < poly.Length; i++)
                HudDraw.Line(parent, poly[i], poly[(i + 1) % poly.Length], 4f, line);

            Color soft = new Color(red.r, red.g, red.b, 0.55f);
            HudDraw.Quad(parent, new Vector2(x0 + 120f, y1 - 34f), new Vector2(120f, 3f), 0f, soft);
            HudDraw.Quad(parent, new Vector2(x1 - 120f, y1 - 34f), new Vector2(120f, 3f), 0f, soft);
            HudDraw.Quad(parent, new Vector2(x0 + 30f, 0f), new Vector2(4f, 250f), 0f, soft);
            HudDraw.Quad(parent, new Vector2(x1 - 30f, 0f), new Vector2(4f, 250f), 0f, soft);
        }

        void BuildHeader(RectTransform parent)
        {
            airText = CreateText("Air Temp", parent, "AIR --", 18f, TextAlignmentOptions.Left);
            Place(airText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(92f, -60f), new Vector2(160f, 30f));

            trackText = CreateText("Track Temp", parent, "TRACK --", 18f, TextAlignmentOptions.Left);
            Place(trackText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(250f, -60f), new Vector2(190f, 30f));

            sessionText = CreateText("Session", parent, "SUZUKA · LAP -- · AI ANALYSIS", 14f, TextAlignmentOptions.Center);
            sessionText.color = new Color(1f, 1f, 1f, 0.6f);
            Place(sessionText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(420f, 26f));

            driverNoText = CreateText("Driver No", parent, "44", 38f, TextAlignmentOptions.Right);
            driverNoText.color = red;
            Place(driverNoText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-92f, -50f), new Vector2(120f, 48f));

            driverNameText = CreateText("Driver Name", parent, "HAMILTON", 18f, TextAlignmentOptions.Right);
            Place(driverNameText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-92f, -92f), new Vector2(240f, 26f));

            teamText = CreateText("Driver Team", parent, "FERRARI", 11f, TextAlignmentOptions.Right);
            teamText.color = new Color(1f, 1f, 1f, 0.5f);
            Place(teamText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-92f, -116f), new Vector2(240f, 20f));
        }

        void BuildGauges(RectTransform parent)
        {
            closingGauge = CreateGauge("Closing Gauge", parent, blue, false, false, false, false);
            Place(closingGauge.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(185f, -8f), new Vector2(240f, 208f));

            gapGauge = CreateGauge("Gap Gauge", parent, red, true, false, true, true);
            Place(gapGauge.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -4f), new Vector2(380f, 302f));

            windowGauge = CreateGauge("Window Gauge", parent, green, false, true, false, false);
            Place(windowGauge.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-185f, -8f), new Vector2(240f, 208f));
        }

        void BuildFooter(RectTransform parent)
        {
            footerLeftText = CreateText("Footer Left", parent, "OVERTAKE WINDOW", 15f, TextAlignmentOptions.Left);
            footerLeftText.color = red;
            Place(footerLeftText.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(140f, 58f), new Vector2(300f, 30f));

            footerMidText = CreateText("Footer Mid", parent, "ML --", 14f, TextAlignmentOptions.Center);
            footerMidText.color = new Color(1f, 1f, 1f, 0.62f);
            Place(footerMidText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 58f), new Vector2(200f, 30f));

            footerRightText = CreateText("Footer Right", parent, "DRS RANGE", 14f, TextAlignmentOptions.Right);
            footerRightText.color = new Color(1f, 1f, 1f, 0.62f);
            Place(footerRightText.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-150f, 58f), new Vector2(250f, 30f));
        }

        GaugeUI CreateGauge(string name, Transform parent, Color accent, bool large, bool mirror, bool labels, bool callout)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            GaugeUI gauge = go.AddComponent<GaugeUI>();
            gauge.Configure(accent, large, mirror, labels, callout, needleResponse);
            return gauge;
        }

        TextMeshProUGUI CreateText(string name, Transform parent, string content, float size, TextAlignmentOptions align)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = size;
            text.alignment = align;
            text.color = Color.white;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            return text;
        }

        // ── 월드공간 HUD가 지형/씬 지오메트리에 가려지지 않도록 UI/텍스트를 ZTest Always 로.
        //    ScreenOverlay 모드에선 이미 항상 위라 불필요 → 스킵.
        Material _uiTopMat;
        static readonly int _zTestId = Shader.PropertyToID("_ZTestMode");
        static readonly int _guiZTestId = Shader.PropertyToID("unity_GUIZTestMode");
        int _topApplyFrames;   // Show 직후 몇 프레임 재적용(늦게 생기는 게이지 요소 커버)
        void ApplyAlwaysOnTop()
        {
            if (canvas == null || !useWorldSpace) return;
            int always = (int)UnityEngine.Rendering.CompareFunction.Always;   // 8

            if (_uiTopMat == null)
            {
                Shader sh = Shader.Find("UI/Default");
                if (sh != null)
                {
                    _uiTopMat = new Material(sh) { name = "HUD_UI_AlwaysOnTop" };
                    _uiTopMat.SetInt("unity_GUIZTestMode", always);
                }
            }

            var graphics = canvas.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic g = graphics[i];
                if (g is TMP_Text tmp)
                {
                    // TMP SDF 셰이더는 ZTest 를 [unity_GUIZTestMode] 로 읽음(_ZTestMode 아님!).
                    // + Overlay 셰이더 교체로 확실히 항상 위. fontMaterial=인스턴스라 타 텍스트 영향 X.
                    Material fm = tmp.fontMaterial;
                    if (fm != null)
                    {
                        fm.SetInt(_guiZTestId, always);
                        fm.SetFloat(_zTestId, always);   // 일부 버전 호환
                        if (fm.shader != null && !fm.shader.name.Contains("Overlay"))
                        {
                            Shader ov = Shader.Find(fm.shader.name.Contains("Mobile")
                                ? "TextMeshPro/Mobile/Distance Field Overlay"
                                : "TextMeshPro/Distance Field Overlay");
                            if (ov != null) fm.shader = ov;
                        }
                    }
                }
                else if (g is Image img && _uiTopMat != null)
                {
                    img.material = _uiTopMat;
                }
            }
        }

        static Image CreateImage(string name, Transform parent, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static RectTransform CreateRect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        void ApplyFont(TMP_FontAsset font)
        {
            if (font == null || root == null)
                return;

            TextMeshProUGUI[] texts = root.GetComponentsInChildren<TextMeshProUGUI>(true);
            for (int i = 0; i < texts.Length; i++)
                texts[i].font = font;
            closingGauge?.SetFont(font);
            gapGauge?.SetFont(font);
            windowGauge?.SetFont(font);
        }

        TMP_FontAsset ResolveFont()
        {
            if (formulaFont != null)
                return formulaFont;

            TMP_FontAsset[] fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            for (int i = 0; i < fonts.Length; i++)
            {
                if (fonts[i] != null && fonts[i].name.Contains("Formula1-Bold"))
                {
                    formulaFont = fonts[i];
                    return formulaFont;
                }
            }

            if (player == null)
                player = FindFirstObjectByType<ReplayPlayer>();
            if (player != null && player.carLabelFont != null)
                return player.carLabelFont;

            return TMP_Settings.defaultFontAsset;
        }

        void UpdatePose()
        {
            if (canvas == null)
                return;

            if (useThisTransform)   // 이 GameObject 위치에 그대로 고정(캔버스가 자식이라 자동 따라감)
                return;

            Transform hud = canvas.transform;

            // 앵커 지정 시: 재생바/패널 옆 등 월드 고정(앵커가 움직이면 같이 따라감)
            if (dockAnchor != null)
            {
                hud.position = dockAnchor.position + dockAnchor.rotation * dockLocalOffset;
                hud.rotation = dockAnchor.rotation * Quaternion.Euler(dockLocalEuler);
                return;
            }

            xrCamera = Camera.main;
            if (xrCamera == null)
                return;

            Transform cam = xrCamera.transform;

            // 수평 전방만 사용(고개를 위/아래로 숙여도 HUD가 흔들리지 않음)
            Vector3 fwd = cam.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward; else fwd.Normalize();

            // 시야 중앙보다 아래·앞쪽(대시보드처럼 흘끗 내려다보는 위치)
            Vector3 target = cam.position + fwd * dockDistance + Vector3.up * dockHeightOffset;
            Quaternion targetRot = Quaternion.LookRotation(target - cam.position, Vector3.up);

            if (!_posed)   // 처음 뜰 때는 스냅(월드 원점에서 미끄러져 오는 현상 방지)
            {
                hud.position = target;
                hud.rotation = targetRot;
                _posed = true;
                return;
            }

            // 레이지 팔로우: 얼굴에 딱 붙지 않고 부드럽게 따라옴(멀미↓, 편안)
            float k = 1f - Mathf.Exp(-dockFollow * Mathf.Max(0.0001f, Time.unscaledDeltaTime));
            hud.position = Vector3.Lerp(hud.position, target, k);
            hud.rotation = Quaternion.Slerp(hud.rotation, targetRot, k);
        }

        static float GapNorm(float? gap, float? prob)
            => gap.HasValue ? Mathf.Clamp01(1f - Mathf.Clamp(gap.Value, 0f, 1.2f) / 1.2f) : Mathf.Clamp01((prob ?? 0f) * 2.2f);
        static float ClosingNorm(float? c)
            => c.HasValue ? Mathf.Clamp01(Mathf.Abs(Mathf.Min(0f, c.Value)) / 0.12f) : 0f;
        static float WindowNorm(float? w, float? prob)
            => w.HasValue ? Mathf.Clamp01(1f - Mathf.Clamp(w.Value, 0f, 45f) / 45f) : Mathf.Clamp01((prob ?? 0f) * 2f);

        static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(
                Mathf.Approximately(anchorMin.x, anchorMax.x) ? anchorMin.x : 0.5f,
                Mathf.Approximately(anchorMin.y, anchorMax.y) ? anchorMin.y : 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
        }

        static string FormatTemp(float? value)
        {
            return value.HasValue ? Mathf.RoundToInt(value.Value) + "°C" : "--";
        }

        static string DriverName(int driverNumber)
        {
            switch (driverNumber)
            {
                case 44: return "HAMILTON";
                case 16: return "LECLERC";
                case 55: return "SAINZ";
                case 63: return "RUSSELL";
                case 1: return "VERSTAPPEN";
                case 4: return "NORRIS";
                case 81: return "PIASTRI";
                case 5: return "BORTOLETO";
                case 6: return "HADJAR";
                default: return "DRIVER";
            }
        }

        static string DriverTeam(int driverNumber)
        {
            switch (driverNumber)
            {
                case 44:
                case 16:
                    return "FERRARI";
                case 63:
                    return "MERCEDES";
                case 1:
                    return "RED BULL";
                case 4:
                case 81:
                    return "MCLAREN";
                case 55:
                    return "WILLIAMS";
                case 5:
                    return "SAUBER";
                case 6:
                    return "RACING BULLS";
                default:
                    return "F1";
            }
        }

        [ContextMenu("Test Gauge HUD")]
        void TestGaugeHud()
        {
            Show(44, "HAMILTON", "FERRARI", 0.44f, -0.06f, 20f, 0.13f, 24f, 32f, 11);
        }

        [ContextMenu("Test Gauge HUD (Animated)")]
        void TestGaugeHudAnimated()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[OvertakeGaugeHud] 애니메이션 테스트는 Play 모드에서 실행하세요.");
                return;
            }
            Show(44, "HAMILTON", "FERRARI", 1.0f, 0f, 45f, 0.1f, 24f, 32f, 11);
            StopAllCoroutines();
            StartCoroutine(AnimDemo());
        }

        IEnumerator AnimDemo()
        {
            float t = 0f;
            while (t < 10f)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.PingPong(t * 0.35f, 1f);
                float gap = Mathf.Lerp(1.0f, 0.15f, k);
                float closing = -Mathf.Lerp(0f, 0.11f, k);
                float window = Mathf.Lerp(45f, 4f, k);
                float prob = Mathf.Lerp(0.1f, 0.9f, k);
                UpdateLive(gap, closing, window, prob, 24f, 32f);
                yield return null;
            }
        }
    }

    // ---- Shared Image-primitive drawing helpers (reliable rendering) ----
    static class HudDraw
    {
        static Sprite _circle;

        public static Sprite Circle
        {
            get
            {
                if (_circle != null) return _circle;
                const int S = 64;
                Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                Color32[] px = new Color32[S * S];
                float r = S * 0.5f;
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        float dx = x + 0.5f - r, dy = y + 0.5f - r;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01((r - d) / 1.5f);
                        px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                    }
                tex.SetPixels32(px);
                tex.Apply();
                _circle = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
                return _circle;
            }
        }

        static Image Raw(Transform parent, string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            Image img = go.GetComponent<Image>();
            img.raycastTarget = false;
            RectTransform rt = img.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            return img;
        }

        public static Image Line(Transform parent, Vector2 a, Vector2 b, float width, Color c)
        {
            Image img = Raw(parent, "Line");
            img.color = c;
            SetLine(img, a, b, width);
            return img;
        }

        public static void SetLine(Image img, Vector2 a, Vector2 b, float width)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            RectTransform rt = img.rectTransform;
            rt.sizeDelta = new Vector2(len + width * 0.5f, width);
            rt.anchoredPosition = (a + b) * 0.5f;
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            rt.localRotation = Quaternion.Euler(0f, 0f, ang);
        }

        public static Image Quad(Transform parent, Vector2 center, Vector2 size, float rotationDeg, Color c)
        {
            Image img = Raw(parent, "Quad");
            img.color = c;
            RectTransform rt = img.rectTransform;
            rt.sizeDelta = size;
            rt.anchoredPosition = center;
            rt.localRotation = Quaternion.Euler(0f, 0f, rotationDeg);
            return img;
        }

        public static Image Disc(Transform parent, Vector2 center, float radius, Color c)
        {
            Image img = Raw(parent, "Disc");
            img.sprite = Circle;
            img.color = c;
            RectTransform rt = img.rectTransform;
            rt.sizeDelta = new Vector2(radius * 2f, radius * 2f);
            rt.anchoredPosition = center;
            return img;
        }

        public static Vector2 Dir(float deg)
        {
            float rad = deg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        }

        public static void Arc(Transform parent, float r, float aStart, float aEnd, float width, Color c, float stepDeg)
        {
            float span = Mathf.Abs(aEnd - aStart);
            if (span < 0.01f) return;
            int n = Mathf.Max(1, Mathf.CeilToInt(span / Mathf.Max(1f, stepDeg)));
            Vector2 prev = Dir(aStart) * r;
            for (int i = 1; i <= n; i++)
            {
                float a = Mathf.Lerp(aStart, aEnd, i / (float)n);
                Vector2 cur = Dir(a) * r;
                Line(parent, prev, cur, width, c);
                prev = cur;
            }
        }
    }

    // ---- One animated gauge (arc + ticks + needle + center text), all Images ----
    public sealed class GaugeUI : MonoBehaviour
    {
        const float ALo = -55f;
        const float ASweep = 290f;
        static readonly float[] ScaleStops = { 0.2f, 0.4f, 0.6f, 0.8f, 1.0f };

        Color accent = Color.red;
        Color dim;
        bool large;
        bool mirror;
        bool showLabels;
        bool showCallout;
        int carNumber = 44;
        float response = 9f;

        float targetNorm = 0.5f;
        float currentNorm = 1f;
        bool built;
        bool firstValue = true;
        TMP_FontAsset font;

        TextMeshProUGUI label, value, unit, calloutText;

        // persistent animated parts
        Image[] activeSegs;
        float[] activeSegMidF;
        Image needleCore, needleGlow;
        RectTransform calloutRoot;
        Image calloutConnector;
        float radiusCache;
        float needleOutR;

        public RectTransform rectTransform => (RectTransform)transform;

        public void Configure(Color accentColor, bool isLarge, bool isMirror, bool labels, bool callout, float needleResponse)
        {
            accent = accentColor;
            large = isLarge;
            mirror = isMirror;
            showLabels = labels;
            showCallout = callout;
            response = Mathf.Max(0.5f, needleResponse);
            dim = large
                ? new Color(0.30f, 0.065f, 0.05f, 0.95f)
                : new Color(0.62f, 0.64f, 0.70f, 0.22f);
        }

        public void SetCarNumber(int number)
        {
            carNumber = number;
            if (calloutText != null)
                calloutText.text = number > 0 ? "CAR " + number : "CAR";
        }

        public void SetFont(TMP_FontAsset f)
        {
            font = f;
            if (f == null) return;
            if (label != null) label.font = f;
            if (value != null) value.font = f;
            if (unit != null) unit.font = f;
            if (calloutText != null) calloutText.font = f;
        }

        /// <summary>텍스트를 갱신하고 목표 바늘 위치를 설정. Update()에서 부드럽게 수렴.</summary>
        public void SetDisplay(string labelText, string valueText, string unitText, float target)
        {
            if (!built) BuildStatic();
            label.text = labelText;
            value.text = valueText;
            unit.text = unitText;
            targetNorm = Mathf.Clamp01(target);
            if (firstValue)
            {
                firstValue = false;
                currentNorm = 1f;   // 1.0(닫힌 끝)에서 채워 올라오는 인트로 스윕
            }
            ApplyNeedle();
        }

        void Update()
        {
            if (!built) return;
            if (Mathf.Abs(currentNorm - targetNorm) > 0.0004f)
            {
                currentNorm = Mathf.Lerp(currentNorm, targetNorm, 1f - Mathf.Exp(-response * Time.unscaledDeltaTime));
                if (Mathf.Abs(currentNorm - targetNorm) <= 0.0004f) currentNorm = targetNorm;
                ApplyNeedle();
            }
        }

        float Ang(float f) => mirror ? (ALo + ASweep - ASweep * f) : (ALo + ASweep * f);

        void BuildStatic()
        {
            built = true;
            Rect rr = rectTransform.rect;
            float radius = Mathf.Min(rr.width, rr.height) * (large ? 0.44f : 0.42f);
            radiusCache = radius;
            float w = large ? 15f : 11f;

            int segN = large ? 84 : 60;
            float aFull0 = Ang(0f), aFull1 = Ang(1f);

            // dim + active segment pairs
            activeSegs = new Image[segN];
            activeSegMidF = new float[segN];
            for (int i = 0; i < segN; i++)
            {
                float f0 = i / (float)segN;
                float f1 = (i + 1) / (float)segN;
                Vector2 p0 = HudDraw.Dir(Mathf.Lerp(aFull0, aFull1, f0)) * radius;
                Vector2 p1 = HudDraw.Dir(Mathf.Lerp(aFull0, aFull1, f1)) * radius;
                HudDraw.Line(transform, p0, p1, w, dim);
                Image act = HudDraw.Line(transform, p0, p1, w * 0.62f, accent);
                act.enabled = false;
                activeSegs[i] = act;
                activeSegMidF[i] = (i + 0.5f) / segN;
            }

            // inner guide ring
            HudDraw.Arc(transform, radius * 0.72f, aFull0, aFull1, 1.6f, new Color(1f, 1f, 1f, 0.22f), 5f);

            // ticks
            int nTicks = large ? 20 : 16;
            int majorEvery = large ? 2 : 4;
            for (int i = 0; i <= nTicks; i++)
            {
                float f = i / (float)nTicks;
                float a = Ang(f);
                bool major = (i % majorEvery) == 0;
                float len = major ? (large ? 18f : 12f) : (large ? 10f : 7f);
                Vector2 dir = HudDraw.Dir(a);
                HudDraw.Line(transform, dir * (radius - len), dir * radius, major ? 2.4f : 1.5f, new Color(1f, 1f, 1f, 0.78f));
            }

            // needle (glow + core), pivot at gauge center, rotated each frame
            needleOutR = radius * (large ? 0.93f : 0.9f);
            needleGlow = MakeNeedle(needleOutR, large ? 8f : 5f, new Color(accent.r, accent.g, accent.b, 0.28f));
            needleCore = MakeNeedle(needleOutR, large ? 3.4f : 2.2f, accent);

            // inner disc + thin ring (covers needle base)
            float discR = large ? 78f : 52f;
            HudDraw.Disc(transform, Vector2.zero, discR, new Color(0.024f, 0.024f, 0.031f, 0.92f));
            HudDraw.Arc(transform, discR, 0f, 359f, 1.4f, new Color(accent.r, accent.g, accent.b, 0.5f), 12f);

            // scale labels
            if (showLabels)
            {
                for (int i = 0; i < ScaleStops.Length; i++)
                {
                    float a = Ang(ScaleStops[i]);
                    Vector2 pos = HudDraw.Dir(a) * (radius - (large ? 42f : 30f));
                    TextMeshProUGUI t = CreateChildText("Scale " + ScaleStops[i].ToString("0.0"), 18f, new Color(1f, 1f, 1f, 0.9f), TextAlignmentOptions.Center);
                    t.text = ScaleStops[i].ToString("0.0");
                    PlaceLocal(t.rectTransform, pos, new Vector2(48f, 24f));
                }
            }

            // center text
            label = CreateChildText("Label", large ? 18f : 12f, new Color(1f, 1f, 1f, 0.64f), TextAlignmentOptions.Center);
            value = CreateChildText("Value", large ? 56f : 30f, Color.white, TextAlignmentOptions.Center);
            unit = CreateChildText("Unit", large ? 15f : 10f, new Color(1f, 1f, 1f, 0.55f), TextAlignmentOptions.Center);
            PlaceLocal(label.rectTransform, new Vector2(0f, large ? 34f : 22f), new Vector2(200f, 26f));
            PlaceLocal(value.rectTransform, new Vector2(0f, large ? -4f : -2f), new Vector2(240f, large ? 72f : 42f));
            PlaceLocal(unit.rectTransform, new Vector2(0f, large ? -44f : -26f), new Vector2(160f, 22f));

            // callout (large gauge only) — connector + box + text, repositioned each frame
            if (showCallout)
            {
                calloutConnector = HudDraw.Line(transform, Vector2.zero, Vector2.zero, 2f, accent);

                calloutRoot = new GameObject("Callout", typeof(RectTransform)).GetComponent<RectTransform>();
                calloutRoot.SetParent(transform, false);
                calloutRoot.anchorMin = calloutRoot.anchorMax = new Vector2(0.5f, 0.5f);
                calloutRoot.pivot = new Vector2(0.5f, 0.5f);
                calloutRoot.sizeDelta = new Vector2(92f, 30f);

                HudDraw.Quad(calloutRoot, Vector2.zero, new Vector2(92f, 30f), 0f, new Color(0.04f, 0.04f, 0.05f, 0.92f));
                Vector2 hb = new Vector2(46f, 15f);
                HudDraw.Line(calloutRoot, new Vector2(-hb.x, -hb.y), new Vector2(-hb.x, hb.y), 1.6f, accent);
                HudDraw.Line(calloutRoot, new Vector2(-hb.x, hb.y), new Vector2(hb.x, hb.y), 1.6f, accent);
                HudDraw.Line(calloutRoot, new Vector2(hb.x, hb.y), new Vector2(hb.x, -hb.y), 1.6f, accent);
                HudDraw.Line(calloutRoot, new Vector2(hb.x, -hb.y), new Vector2(-hb.x, -hb.y), 1.6f, accent);

                calloutText = CreateChildText("Callout Text", 16f, Color.white, TextAlignmentOptions.Center);
                calloutText.fontStyle = FontStyles.Bold;
                calloutText.text = carNumber > 0 ? "CAR " + carNumber : "CAR";
                calloutText.transform.SetParent(calloutRoot, false);
                PlaceLocal(calloutText.rectTransform, Vector2.zero, new Vector2(92f, 30f));
            }
        }

        Image MakeNeedle(float length, float width, Color c)
        {
            GameObject go = new GameObject("Needle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(transform, false);
            Image img = go.GetComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            RectTransform rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0f);         // base at gauge center
            rt.sizeDelta = new Vector2(width, length);
            rt.anchoredPosition = Vector2.zero;
            return img;
        }

        void ApplyNeedle()
        {
            float aN = Ang(currentNorm);

            if (needleCore != null)
            {
                Quaternion q = Quaternion.Euler(0f, 0f, aN - 90f);
                needleCore.rectTransform.localRotation = q;
                if (needleGlow != null) needleGlow.rectTransform.localRotation = q;
            }

            if (activeSegs != null)
                for (int i = 0; i < activeSegs.Length; i++)
                    activeSegs[i].enabled = activeSegMidF[i] >= currentNorm;

            if (showCallout && calloutRoot != null)
            {
                Vector2 tip = HudDraw.Dir(aN) * needleOutR;
                Vector2 box = HudDraw.Dir(aN) * (needleOutR + 34f);
                calloutRoot.anchoredPosition = box;
                if (calloutConnector != null) HudDraw.SetLine(calloutConnector, tip, box, 2f);
            }
        }

        TextMeshProUGUI CreateChildText(string name, float size, Color textColor, TextAlignmentOptions align)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(transform, false);
            TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
            text.text = "";
            text.fontSize = size;
            text.alignment = align;
            text.color = textColor;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            if (font != null) text.font = font;
            return text;
        }

        static void PlaceLocal(RectTransform rect, Vector2 anchoredPosition, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
        }
    }
}
#endif
