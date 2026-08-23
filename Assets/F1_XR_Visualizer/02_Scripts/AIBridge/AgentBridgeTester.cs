// AIBridge/AgentBridgeTester.cs
// 테스트/발표용 팀라디오 패널: F1 라디오 스타일 음성 입력 + 텍스트 질문.
#if AIBRIDGE_READY
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.UI;
using TMPro;
using F1XR.AIBridge.Voice;

namespace F1XR.AIBridge
{
    public class AgentBridgeTester : MonoBehaviour
    {
        [Header("연결")]
        public AgentBridge bridge;
        public MicRecorder mic;

        [Header("설정")]
        public int sessionKey = 9839;
        public string defaultQuestion = "Who is in the lead?";

        [Header("스타일")]
        [Tooltip("Formula1-Bold SDF.asset")]
        public TMP_FontAsset uiFont;
        [Tooltip("한글 자막/답변용 TMP 폰트. 비우면 OS Malgun Gothic으로 런타임 생성 시도")]
        public TMP_FontAsset koreanFont;
        public Color accent = new Color(0.882f, 0.024f, 0f, 1f);
        public Color panelColor = new Color(0.012f, 0.014f, 0.017f, 0.96f);

        const float PanelWidth = 440f;
        const float HeaderHeight = 168f;

        TMP_InputField _input;
        TMP_Text _statusText;
        TMP_Text _userLabel;
        UnityEngine.UI.Text _userText;
        UnityEngine.UI.Text _agentText;
        TMP_Text _agentLabel;
        TMP_Text _processingText;
        TMP_Text _footerText;
        Image _statusDot;
        Image _recordBg;
        Image _stopBg;
        RectTransform _agentGroup;
        readonly List<WaveBarGraphic> _bars = new List<WaveBarGraphic>();
        bool _recording;
        bool _processing;
        string _lastTranscript;
        Coroutine _typeRoutine;
        TMP_FontAsset _resolvedKoreanFont;
        Font _koreanUiFont;

        void OnEnable()
        {
            if (bridge != null)
            {
                bridge.OnTranscript += HandleTranscript;
                bridge.OnAssistantText += HandleAssistantText;
            }
        }

        void OnDisable()
        {
            if (bridge != null)
            {
                bridge.OnTranscript -= HandleTranscript;
                bridge.OnAssistantText -= HandleAssistantText;
            }
        }

        void Start()
        {
            _resolvedKoreanFont = koreanFont != null ? koreanFont : CreateRuntimeKoreanFont();
            _koreanUiFont = CreateKoreanUiFont();
            BuildUI();
            StartCoroutine(FocusInputNextFrame());
        }

        void Update()
        {
            float t = Time.unscaledTime;
            for (int i = 0; i < _bars.Count; i++)
            {
                float normalized = _bars.Count <= 1 ? 0f : i / (float)(_bars.Count - 1);
                float centerBoost = 1f - Mathf.Abs(normalized * 2f - 1f);
                float liveNoise = Mathf.PerlinNoise(i * 0.37f + Mathf.Sin(t * 0.9f) * 0.18f, t * (_recording ? 6.7f : 2.9f));
                float flicker = Mathf.Abs(Mathf.Sin(t * (3.2f + i * 0.09f) + i * 0.71f));
                float amp = _recording || _processing
                    ? Mathf.Clamp01(liveNoise * 0.72f + flicker * 0.28f)
                    : 0.12f + liveNoise * 0.18f;
                amp *= Mathf.Lerp(0.42f, 1f, centerBoost);
                float height = Mathf.Lerp(7f, _recording ? 62f : 34f, amp);
                RectTransform rt = _bars[i].rectTransform;
                rt.sizeDelta = new Vector2(Mathf.Lerp(2.4f, 4.4f, amp), height);
                Color c = Color.Lerp(new Color(0.35f, 0.015f, 0.01f, 0.62f), accent, Mathf.Clamp01(amp + 0.16f));
                _bars[i].color = c;
            }
        }

        IEnumerator FocusInputNextFrame()
        {
            yield return null;
            FocusInput();
        }

        void FocusInput()
        {
            if (_input == null) return;
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(_input.gameObject);
            _input.ActivateInputField();
            _input.Select();
        }

        void SendCurrent()
        {
            if (bridge == null) { Debug.LogError("[Tester] bridge 미할당"); return; }
            string q = _input != null && !string.IsNullOrWhiteSpace(_input.text) ? _input.text : defaultQuestion;
            ShowUserText(q, false);
            SetProcessing(true);
            bridge.SendText(q, sessionKey);
        }

        void ToggleRecord()
        {
            if (mic == null) { Debug.LogError("[Tester] mic 미할당"); return; }
            if (!_recording)
            {
                mic.StartRecording();
                _recording = true;
                SetProcessing(false);
                _lastTranscript = "";
                ShowUserText("", false);
                SetUserVisible(true);
                SetAgentVisible(false);
                _statusText.text = "VOICE INPUT";
                if (_statusDot != null) _statusDot.color = accent;
                _footerText.text = "RaumDeuter team radio - listening";
                _recordBg.color = accent;
                _stopBg.color = new Color(1f, 1f, 1f, 0.08f);
                Debug.Log("[Tester] 녹음 시작");
            }
            else
            {
                mic.currentSessionKey = sessionKey;
                mic.StopAndSend();
                _recording = false;
                SetProcessing(true);
                _statusText.text = "STT LIVE";
                if (_statusDot != null) _statusDot.color = accent;
                _footerText.text = "voice captured - agent processing";
                _recordBg.color = new Color(1f, 1f, 1f, 0.06f);
                _stopBg.color = accent;
                Debug.Log("[Tester] 정지 & 전송");
            }
        }

        void HandleTranscript(string text)
        {
            _lastTranscript = text ?? "";
            SetUserVisible(true);
            ShowUserText(_lastTranscript, true);
            SetAgentVisible(false);
            _statusText.text = "STT LIVE";
            if (_statusDot != null) _statusDot.color = accent;
        }

        void HandleAssistantText(string text)
        {
            SetProcessing(false);
            _statusText.text = "AGENT READY";
            if (_statusDot != null) _statusDot.color = new Color(1f, 1f, 1f, 0.28f);
            _footerText.text = "RaumDeuter team radio - response";
            SetAgentVisible(true);
            _agentText.text = string.IsNullOrWhiteSpace(text) ? "--" : text;
        }

        void ShowUserText(string text, bool type)
        {
            if (_typeRoutine != null) StopCoroutine(_typeRoutine);
            if (string.IsNullOrWhiteSpace(text))
            {
                _userText.text = "";
                return;
            }
            if (type)
                _typeRoutine = StartCoroutine(TypeText(_userText, text));
            else
                _userText.text = text;
        }

        IEnumerator TypeText(UnityEngine.UI.Text target, string text)
        {
            target.text = "";
            for (int i = 0; i < text.Length; i++)
            {
                target.text += text[i];
                yield return new WaitForSecondsRealtime(0.025f);
            }
        }

        void SetProcessing(bool processing)
        {
            _processing = processing;
            if (processing)
            {
                SetUserVisible(false);
                _agentLabel.text = "AI Agent";
                _agentText.text = "";
                SetAgentVisible(false);
            }
        }

        void SetUserVisible(bool visible)
        {
            if (_userLabel != null) _userLabel.gameObject.SetActive(visible);
            if (_userText != null) _userText.gameObject.SetActive(visible);
            if (_processingText != null) _processingText.gameObject.SetActive(false);
        }

        void SetAgentVisible(bool visible)
        {
            if (_agentGroup != null)
                _agentGroup.gameObject.SetActive(visible);
        }

        void BuildUI()
        {
            var canvasGO = new GameObject("AIBridgeTesterCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.GetComponent<Canvas>();
            ConfigureCanvas(canvas, canvasGO);
            canvas.sortingOrder = 1000;
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            var panel = NewRect("RaumDeuterRadioPanel", canvasGO.transform);
            panel.anchorMin = panel.anchorMax = new Vector2(0, 1);
            panel.pivot = new Vector2(0, 1);
            panel.anchoredPosition = new Vector2(32, -32);
            panel.sizeDelta = new Vector2(PanelWidth, 350);
            Bg(panel.gameObject, panelColor);
            panel.gameObject.AddComponent<RectMask2D>();

            var vlg = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(18, 18, 16, 16);
            vlg.spacing = 10;
            vlg.childForceExpandWidth = true;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            BuildHeader(panel);
            BuildTranscript(panel);
            BuildInput(panel);
            BuildFooter(panel);
            BuildPanelChrome(panel);
        }

        void ConfigureCanvas(Canvas canvas, GameObject canvasGO)
        {
            Camera viewCamera = Camera.main;
            if (!HasRunningXrDisplay() || viewCamera == null)
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                return;
            }

            // Screen-space camera preserves the Editor HUD anchors while also
            // rendering the canvas through the Quest stereo camera.
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = viewCamera;
            canvas.planeDistance = 1.25f;
            canvasGO.AddComponent<TrackedDeviceGraphicRaycaster>();
        }

        static bool HasRunningXrDisplay()
        {
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            for (int i = 0; i < displays.Count; i++)
            {
                if (displays[i] != null && displays[i].running)
                    return true;
            }

            return Application.platform == RuntimePlatform.Android;
        }

        void BuildHeader(Transform parent)
        {
            var header = NewRect("RadioHeader", parent);
            Bg(header.gameObject, new Color(1f, 1f, 1f, 0.025f));
            SetHeight(header.gameObject, HeaderHeight);

            // Keep the two-part radio wordmark on one shared right edge.  Using
            // individual left offsets made the second line drift as the font or
            // resolution changed.
            var name = Text("RaumDeuter", header, "RaumDeuter", 31, FontStyles.Italic | FontStyles.Bold, TextAlignmentOptions.Right);
            name.color = accent;
            name.enableWordWrapping = false;
            name.overflowMode = TextOverflowModes.Ellipsis;
            Place(name.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-16, -5), new Vector2(360, 40));

            var radio = Text("Radio", header, "RADIO", 33, FontStyles.Italic | FontStyles.Bold, TextAlignmentOptions.Right);
            radio.color = Color.white;
            radio.enableWordWrapping = false;
            radio.overflowMode = TextOverflowModes.Ellipsis;
            Place(radio.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-16, -44), new Vector2(360, 38));

            _statusText = Text("Status", header, "AGENT READY", 10, FontStyles.Bold, TextAlignmentOptions.Right);
            _statusText.color = new Color(1f, 1f, 1f, 0.72f);
            Place(_statusText.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-8, -84), new Vector2(112, 18));

            var dot = NewRect("StatusDot", header);
            _statusDot = Bg(dot.gameObject, new Color(1f, 1f, 1f, 0.28f));
            Place(dot, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-126, -88), new Vector2(8, 8));

            BuildWaveform(header);
            BuildTransport(header);
        }

        void BuildTransport(RectTransform parent)
        {
            var group = NewRect("TransportControls", parent);
            Place(group, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 56), new Vector2(76, 30));
            var hlg = group.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8;
            hlg.childControlWidth = hlg.childControlHeight = true;
            hlg.childAlignment = TextAnchor.MiddleCenter;

            var rec = MakeIconButton(group, true, out _recordBg);
            rec.onClick.AddListener(ToggleRecord);
            var play = NewGraphicRect("PlayIcon", rec.transform);
            Stretch(play);
            var playGraphic = play.gameObject.AddComponent<PlayIconGraphic>();
            playGraphic.color = Color.white;
            playGraphic.raycastTarget = false;

            var stop = MakeIconButton(group, false, out _stopBg);
            stop.onClick.AddListener(ToggleRecord);
            var pause = NewGraphicRect("PauseIcon", stop.transform);
            Stretch(pause);
            var pauseGraphic = pause.gameObject.AddComponent<PauseIconGraphic>();
            pauseGraphic.color = Color.white;
            pauseGraphic.raycastTarget = false;
        }

        void BuildWaveform(RectTransform parent)
        {
            var rail = NewRect("VoiceWaveform", parent);
            Place(rail, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 12), new Vector2(0, 72));
            Bg(rail.gameObject, new Color(0.7f, 0f, 0f, 0.05f));
            rail.gameObject.AddComponent<RectMask2D>();
            BuildWaveformChrome(rail);

            var line = NewRect("CenterLine", rail);
            Bg(line.gameObject, new Color(accent.r, accent.g, accent.b, 0.42f));
            Place(line, new Vector2(0, 0.5f), new Vector2(1, 0.5f), Vector2.zero, new Vector2(0, 2));
            IgnoreLayout(line.gameObject);

            var bars = NewRect("Bars", rail);
            Stretch(bars);
            var hlg = bars.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(18, 18, 6, 8);
            hlg.spacing = 3;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childForceExpandWidth = false;
            hlg.childControlWidth = false;
            hlg.childControlHeight = false;

            for (int i = 0; i < 58; i++)
            {
                var bar = NewGraphicRect("WaveBar", bars);
                bar.sizeDelta = new Vector2(3.2f, 18);
                var img = bar.gameObject.AddComponent<WaveBarGraphic>();
                img.color = accent;
                img.raycastTarget = false;
                var le = bar.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = 3.2f;
                le.preferredHeight = 18;
                _bars.Add(img);
            }

            var chrome = rail.Find("WaveformChrome");
            if (chrome != null) chrome.SetAsLastSibling();
        }

        void BuildPanelChrome(RectTransform panel)
        {
            var chrome = NewRect("PanelChrome", panel);
            Stretch(chrome);
            IgnoreLayout(chrome.gameObject);

            AddEdge(chrome, "TopHairline", new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, new Vector2(0, 1.2f), new Color(1f, 1f, 1f, 0.11f));
            AddEdge(chrome, "LeftHairline", new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, new Vector2(1.2f, 0), new Color(1f, 1f, 1f, 0.06f));
            AddEdge(chrome, "RightHairline", new Vector2(1, 0), new Vector2(1, 1), Vector2.zero, new Vector2(1.2f, 0), new Color(1f, 1f, 1f, 0.08f));

            var bottom = NewGraphicRect("BottomGradientBorder", chrome);
            Place(bottom, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1.5f), new Vector2(0, 3));
            var gradient = bottom.gameObject.AddComponent<GradientLineGraphic>();
            gradient.Left = new Color(0.24f, 0f, 0f, 0.45f);
            gradient.Middle = accent;
            gradient.Right = new Color(0.55f, 0f, 0f, 0.6f);
            gradient.raycastTarget = false;

            AddCornerCut(chrome, "TopRightCut", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-11, -11), true);
            AddCornerCut(chrome, "BottomLeftCut", new Vector2(0, 0), new Vector2(0, 0), new Vector2(11, 11), false);
        }

        void BuildWaveformChrome(RectTransform rail)
        {
            var chrome = NewRect("WaveformChrome", rail);
            Stretch(chrome);
            IgnoreLayout(chrome.gameObject);

            AddEdge(chrome, "RailTop", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -1), new Vector2(0, 1), new Color(1f, 1f, 1f, 0.08f));
            AddEdge(chrome, "RailBottomGlow", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 2), new Vector2(0, 2), new Color(accent.r, accent.g, accent.b, 0.28f));
            AddEdge(chrome, "RailLeft", new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 0), new Vector2(1, 0), new Color(accent.r, accent.g, accent.b, 0.18f));
            AddEdge(chrome, "RailRight", new Vector2(1, 0), new Vector2(1, 1), new Vector2(-1, 0), new Vector2(1, 0), new Color(accent.r, accent.g, accent.b, 0.18f));

            var glow = NewRect("WaveformRedWash", rail);
            Place(glow, new Vector2(0, 0.5f), new Vector2(1, 0.5f), Vector2.zero, new Vector2(0, 30));
            Bg(glow.gameObject, new Color(accent.r, accent.g, accent.b, 0.055f));
            IgnoreLayout(glow.gameObject);
        }

        void BuildTranscript(Transform parent)
        {
            _userLabel = Text("UserLabel", parent, "Driver / User", 9, FontStyles.Bold, TextAlignmentOptions.Left);
            _userLabel.color = new Color(1f, 1f, 1f, 0.52f);
            SetHeight(_userLabel.gameObject, 14);

            _userText = KoreanText("UserTranscript", parent, "", 18, TextAnchor.UpperLeft);
            _userText.color = Color.white;
            SetHeight(_userText.gameObject, 50);

            _processingText = Text("Processing", parent, "", 1, FontStyles.Bold, TextAlignmentOptions.Left);
            _processingText.color = new Color(accent.r, accent.g, accent.b, 0f);
            SetHeight(_processingText.gameObject, 1);

            _agentGroup = NewRect("AgentAnswerGroup", parent);
            var vlg = _agentGroup.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 6;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;

            _agentLabel = Text("AgentLabel", _agentGroup, "AI Agent", 9, FontStyles.Bold, TextAlignmentOptions.Left);
            _agentLabel.color = new Color(1f, 1f, 1f, 0.52f);
            SetHeight(_agentLabel.gameObject, 14);

            _agentText = KoreanText("AgentAnswer", _agentGroup, "", 16, TextAnchor.UpperLeft);
            _agentText.color = Color.white;
            SetHeight(_agentText.gameObject, 68);
            SetAgentVisible(false);
        }

        void BuildInput(Transform parent)
        {
            _input = MakeInput(parent, defaultQuestion);
            var sendBtn = MakeTextButton(parent, "TEXT COMMAND", new Color(0f, 0f, 0f, 0.18f), Color.white, 30);
            sendBtn.onClick.AddListener(SendCurrent);
        }

        void BuildFooter(Transform parent)
        {
            _footerText = Text("Footer", parent, "RaumDeuter team radio - idle", 8, FontStyles.Bold, TextAlignmentOptions.Left);
            _footerText.color = new Color(1f, 1f, 1f, 0.42f);
            SetHeight(_footerText.gameObject, 12);
        }

        Button MakeIconButton(Transform parent, bool primary, out Image bg)
        {
            var go = new GameObject(primary ? "StartVoice" : "StopVoice", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(30, 30);
            bg = Bg(go, primary ? accent : new Color(1f, 1f, 1f, 0.08f));
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 30;
            le.preferredHeight = 30;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.transition = Selectable.Transition.None;
            return btn;
        }

        Button MakeTextButton(Transform parent, string label, Color bg, Color fg, float height)
        {
            var go = new GameObject("Button", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = Bg(go, bg);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;
            SetHeight(go, height);
            var lbl = Text("Label", go.transform, label, 13, FontStyles.Bold, TextAlignmentOptions.Center);
            lbl.color = fg;
            Stretch(lbl.rectTransform);
            return btn;
        }

        TMP_InputField MakeInput(Transform parent, string value)
        {
            var go = new GameObject("Input", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Bg(go, new Color(0f, 0f, 0f, 0.18f));
            var input = go.AddComponent<TMP_InputField>();
            SetHeight(go, 34);

            var area = NewRect("TextArea", go.transform);
            area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one;
            area.offsetMin = new Vector2(10, 5); area.offsetMax = new Vector2(-10, -5);
            area.gameObject.AddComponent<RectMask2D>();

            var placeholder = Text("Placeholder", area, "Type a question or use radio", 12, FontStyles.Italic, TextAlignmentOptions.MidlineLeft);
            placeholder.color = new Color(1, 1, 1, 0.35f);
            Stretch(placeholder.rectTransform);

            var text = Text("Text", area, "", 13, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            Stretch(text.rectTransform);

            input.textViewport = area;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.text = "";
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.onSubmit.AddListener(_ =>
            {
                if (string.IsNullOrWhiteSpace(input.text))
                    return;
                SendCurrent();
                input.text = "";
                StartCoroutine(FocusInputNextFrame());
            });
            return input;
        }

        void AddEdge(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size, Color color)
        {
            var edge = NewRect(name, parent);
            Bg(edge.gameObject, color).raycastTarget = false;
            Place(edge, anchorMin, anchorMax, pos, size);
            IgnoreLayout(edge.gameObject);
        }

        void AddCornerCut(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, bool topRight)
        {
            var cut = NewGraphicRect(name, parent);
            Place(cut, anchorMin, anchorMax, pos, new Vector2(22, 22));
            var graphic = cut.gameObject.AddComponent<CornerCutGraphic>();
            graphic.TopRight = topRight;
            graphic.color = Color.black;
            graphic.raycastTarget = false;
            IgnoreLayout(cut.gameObject);
        }

        void IgnoreLayout(GameObject go)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
        }

        RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        RectTransform NewGraphicRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        Image Bg(GameObject go, Color c)
        {
            var img = go.GetComponent<Image>() ?? go.AddComponent<Image>();
            img.color = c;
            return img;
        }

        TMP_Text Text(string name, Transform parent, string text, int size, FontStyles style, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = align;
            t.color = Color.white;
            if (uiFont != null) t.font = uiFont;
            return t;
        }

        UnityEngine.UI.Text KoreanText(string name, Transform parent, string text, int size, TextAnchor align)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<UnityEngine.UI.Text>();
            t.text = text;
            t.font = _koreanUiFont != null ? _koreanUiFont : Font.CreateDynamicFontFromOSFont("Malgun Gothic", size);
            t.fontSize = size;
            t.fontStyle = FontStyle.Normal;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.lineSpacing = 1.12f;
            t.raycastTarget = false;
            return t;
        }

        void SetHeight(GameObject go, float h)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.preferredHeight = h;
        }

        void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(
                Mathf.Approximately(anchorMin.x, anchorMax.x) ? anchorMin.x : 0.5f,
                Mathf.Approximately(anchorMin.y, anchorMax.y) ? anchorMin.y : 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        TMP_FontAsset CreateRuntimeKoreanFont()
        {
            try
            {
                Font font = Font.CreateDynamicFontFromOSFont(
                    new[] { "Malgun Gothic", "맑은 고딕", "Noto Sans CJK KR", "Arial" }, 20);
                TMP_FontAsset asset = font != null ? TMP_FontAsset.CreateFontAsset(font) : null;
                if (asset != null) asset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                return asset;
            }
            catch
            {
                return null;
            }
        }

        Font CreateKoreanUiFont()
        {
            try
            {
                return Font.CreateDynamicFontFromOSFont(
                    new[] { "Malgun Gothic", "맑은 고딕", "Noto Sans CJK KR", "Arial" }, 18);
            }
            catch
            {
                return null;
            }
        }
    }

    public sealed class WaveBarGraphic : Graphic
    {
        const int Segments = 8;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            float radius = Mathf.Min(r.width * 0.5f, r.height * 0.5f);
            if (r.width <= 0f || r.height <= 0f) return;

            AddQuad(vh, new Rect(r.xMin, r.yMin + radius, r.width, Mathf.Max(0f, r.height - radius * 2f)));
            AddCap(vh, new Vector2(r.center.x, r.yMax - radius), radius, 0f, Mathf.PI);
            AddCap(vh, new Vector2(r.center.x, r.yMin + radius), radius, Mathf.PI, Mathf.PI * 2f);
        }

        void AddCap(VertexHelper vh, Vector2 center, float radius, float startAngle, float endAngle)
        {
            int start = vh.currentVertCount;
            vh.AddVert(center, color, Vector2.zero);
            for (int i = 0; i <= Segments; i++)
            {
                float t = Mathf.Lerp(startAngle, endAngle, i / (float)Segments);
                vh.AddVert(center + new Vector2(Mathf.Cos(t) * radius, Mathf.Sin(t) * radius), color, Vector2.zero);
            }

            for (int i = 1; i <= Segments; i++)
                vh.AddTriangle(start, start + i, start + i + 1);
        }

        void AddQuad(VertexHelper vh, Rect r)
        {
            int start = vh.currentVertCount;
            vh.AddVert(new Vector2(r.xMin, r.yMin), color, Vector2.zero);
            vh.AddVert(new Vector2(r.xMin, r.yMax), color, Vector2.zero);
            vh.AddVert(new Vector2(r.xMax, r.yMax), color, Vector2.zero);
            vh.AddVert(new Vector2(r.xMax, r.yMin), color, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }

    public sealed class GradientLineGraphic : Graphic
    {
        public Color Left = Color.red;
        public Color Middle = Color.red;
        public Color Right = Color.red;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            float mid = r.xMin + r.width * 0.55f;

            int a = vh.currentVertCount;
            vh.AddVert(new Vector2(r.xMin, r.yMin), Left, Vector2.zero);
            vh.AddVert(new Vector2(r.xMin, r.yMax), Left, Vector2.zero);
            vh.AddVert(new Vector2(mid, r.yMax), Middle, Vector2.zero);
            vh.AddVert(new Vector2(mid, r.yMin), Middle, Vector2.zero);
            vh.AddTriangle(a, a + 1, a + 2);
            vh.AddTriangle(a, a + 2, a + 3);

            int b = vh.currentVertCount;
            vh.AddVert(new Vector2(mid, r.yMin), Middle, Vector2.zero);
            vh.AddVert(new Vector2(mid, r.yMax), Middle, Vector2.zero);
            vh.AddVert(new Vector2(r.xMax, r.yMax), Right, Vector2.zero);
            vh.AddVert(new Vector2(r.xMax, r.yMin), Right, Vector2.zero);
            vh.AddTriangle(b, b + 1, b + 2);
            vh.AddTriangle(b, b + 2, b + 3);
        }
    }

    public sealed class CornerCutGraphic : Graphic
    {
        public bool TopRight = true;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            int start = vh.currentVertCount;
            if (TopRight)
            {
                vh.AddVert(new Vector2(r.xMax, r.yMax), color, Vector2.zero);
                vh.AddVert(new Vector2(r.xMin, r.yMax), color, Vector2.zero);
                vh.AddVert(new Vector2(r.xMax, r.yMin), color, Vector2.zero);
            }
            else
            {
                vh.AddVert(new Vector2(r.xMin, r.yMin), color, Vector2.zero);
                vh.AddVert(new Vector2(r.xMin, r.yMax), color, Vector2.zero);
                vh.AddVert(new Vector2(r.xMax, r.yMin), color, Vector2.zero);
            }
            vh.AddTriangle(start, start + 1, start + 2);
        }
    }

    public sealed class PlayIconGraphic : Graphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            Vector2 a = new Vector2(r.xMin + r.width * 0.38f, r.yMin + r.height * 0.30f);
            Vector2 b = new Vector2(r.xMin + r.width * 0.38f, r.yMax - r.height * 0.30f);
            Vector2 c = new Vector2(r.xMax - r.width * 0.30f, r.center.y);
            vh.AddVert(a, color, Vector2.zero);
            vh.AddVert(b, color, Vector2.zero);
            vh.AddVert(c, color, Vector2.zero);
            vh.AddTriangle(0, 1, 2);
        }
    }

    public sealed class PauseIconGraphic : Graphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            AddBar(vh, new Rect(r.xMin + r.width * 0.33f, r.yMin + r.height * 0.30f, r.width * 0.11f, r.height * 0.40f));
            AddBar(vh, new Rect(r.xMax - r.width * 0.44f, r.yMin + r.height * 0.30f, r.width * 0.11f, r.height * 0.40f));
        }

        void AddBar(VertexHelper vh, Rect r)
        {
            int start = vh.currentVertCount;
            vh.AddVert(new Vector2(r.xMin, r.yMin), color, Vector2.zero);
            vh.AddVert(new Vector2(r.xMin, r.yMax), color, Vector2.zero);
            vh.AddVert(new Vector2(r.xMax, r.yMax), color, Vector2.zero);
            vh.AddVert(new Vector2(r.xMax, r.yMin), color, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
#endif
