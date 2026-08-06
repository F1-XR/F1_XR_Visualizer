// AIBridge/AgentBridgeTester.cs
// 테스트/발표용 패널: 텍스트 질문 + 마이크 녹음/전송.
// uGUI + TextMeshPro 로 구성해 프로젝트 폰트와 통일. 런타임에 캔버스를 코드로 생성한다.
// 검증 끝나면 이 컴포넌트(가 붙은 오브젝트)는 지워도 된다.
#if AIBRIDGE_READY
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
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
        [Tooltip("보낼 질문. 라틴 폰트라 UI엔 영어로. AI 답변은 한국어 음성.")]
        public string defaultQuestion = "Who is in the lead?";

        [Header("스타일")]
        [Tooltip("프로젝트 UI 폰트(TMP). 비우면 TMP 기본")]
        public TMP_FontAsset uiFont;
        [Tooltip("둥근 모서리 스프라이트(선택). 비우면 각진 사각형")]
        public Sprite roundedSprite;
        public Color accent = new Color(0.882f, 0.024f, 0f, 1f);   // F1 red
        public Color panelColor = new Color(0.07f, 0.08f, 0.10f, 0.94f);

        TMP_InputField _input;
        TMP_Text _micStatus, _micLabel;
        Image _micBg;
        bool _recording;

        void Start() { BuildUI(); StartCoroutine(FocusInputNextFrame()); }

        // 이 씬의 EventSystem은 XR용(XRUIInputModule)이라 데스크톱에서 마우스로
        // 입력창을 눌러 포커스 잡기가 잘 안 된다. 그래서 시작할 때 코드로 포커스를 준다.
        // (UI가 다 만들어진 뒤 1프레임 기다렸다 선택해야 안정적)
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
            Debug.Log($"[Tester] 텍스트 전송 → {q}");
            bridge.SendText(q, sessionKey);
        }

        // ─────────────────────────── UI 생성 ───────────────────────────
        void BuildUI()
        {
            // Canvas
            var canvasGO = new GameObject("AIBridgeTesterCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            // Panel (좌상단, 세로 스택)
            var panel = NewRect("Panel", canvasGO.transform);
            panel.anchorMin = panel.anchorMax = new Vector2(0, 1);
            panel.pivot = new Vector2(0, 1);
            panel.anchoredPosition = new Vector2(28, -28);
            panel.sizeDelta = new Vector2(560, 10);
            Bg(panel.gameObject, panelColor);
            var vlg = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(22, 22, 22, 22);
            vlg.spacing = 14;
            vlg.childForceExpandWidth = true;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // 타이틀
            var title = Text("Title", panel, "F1 Tutorial AI", 26, FontStyles.Bold, TextAlignmentOptions.Left);
            title.color = new Color(1, 1, 1, 0.85f);
            SetHeight(title.gameObject, 34);

            // 입력창
            _input = MakeInput(panel, defaultQuestion);

            // 질문 보내기 버튼 (Enter로도 전송됨 — MakeInput의 onSubmit 참고)
            var sendBtn = MakeButton(panel, "Send  (Enter)", accent, Color.white, out _, out _, 66);
            sendBtn.onClick.AddListener(SendCurrent);

            // 구분 여백
            var gap = NewRect("Gap", panel);
            SetHeight(gap.gameObject, 6);

            // 마이크 상태
            _micStatus = Text("MicStatus", panel, "Mic: idle", 22, FontStyles.Bold, TextAlignmentOptions.Left);
            _micStatus.color = new Color(1, 1, 1, 0.7f);
            SetHeight(_micStatus.gameObject, 30);

            // 마이크 버튼
            var micBtn = MakeButton(panel, "Record voice", new Color(1, 1, 1, 0.10f), Color.white,
                                    out _micBg, out _micLabel, 72);
            micBtn.onClick.AddListener(ToggleRecord);
        }

        void ToggleRecord()
        {
            if (mic == null) { Debug.LogError("[Tester] mic 미할당"); return; }
            if (!_recording)
            {
                mic.StartRecording();
                _recording = true;
                _micStatus.text = "●  Recording…";
                _micStatus.color = accent;
                _micLabel.text = "■  Stop & send";
                _micBg.color = accent;
                Debug.Log("[Tester] 녹음 시작");
            }
            else
            {
                mic.currentSessionKey = sessionKey;
                mic.StopAndSend();
                _recording = false;
                _micStatus.text = "Mic: idle";
                _micStatus.color = new Color(1, 1, 1, 0.7f);
                _micLabel.text = "Record voice";
                _micBg.color = new Color(1, 1, 1, 0.10f);
                Debug.Log("[Tester] 정지 & 전송");
            }
        }

        // ─────────────────────────── 헬퍼 ───────────────────────────
        RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        Image Bg(GameObject go, Color c)
        {
            var img = go.GetComponent<Image>() ?? go.AddComponent<Image>();
            img.color = c;
            if (roundedSprite != null) { img.sprite = roundedSprite; img.type = Image.Type.Sliced; }
            return img;
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

        TMP_Text Text(string name, Transform parent, string text, int size, FontStyles style, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text; t.fontSize = size; t.fontStyle = style; t.alignment = align;
            t.color = Color.white;
            if (uiFont != null) t.font = uiFont;
            return t;
        }

        Button MakeButton(Transform parent, string label, Color bg, Color fg,
                          out Image bgImg, out TMP_Text lbl, float height)
        {
            var go = new GameObject("Button", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            bgImg = Bg(go, bg);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = bgImg;
            SetHeight(go, height);
            lbl = Text("Label", go.transform, label, 28, FontStyles.Bold, TextAlignmentOptions.Center);
            lbl.color = fg;
            Stretch(lbl.rectTransform);
            return btn;
        }

        TMP_InputField MakeInput(Transform parent, string value)
        {
            var go = new GameObject("Input", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Bg(go, new Color(1, 1, 1, 0.07f));
            var input = go.AddComponent<TMP_InputField>();
            SetHeight(go, 58);

            var area = NewRect("TextArea", go.transform);
            area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one;
            area.offsetMin = new Vector2(16, 8); area.offsetMax = new Vector2(-16, -8);
            area.gameObject.AddComponent<RectMask2D>();

            var placeholder = Text("Placeholder", area, "Type a question or use voice", 26, FontStyles.Italic, TextAlignmentOptions.MidlineLeft);
            placeholder.color = new Color(1, 1, 1, 0.4f);
            Stretch(placeholder.rectTransform);

            var text = Text("Text", area, "", 26, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            Stretch(text.rectTransform);

            input.textViewport = area;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.text = value;
            input.lineType = TMP_InputField.LineType.SingleLine;   // Enter=제출(줄바꿈 아님)
            // Enter를 누르면 전송하고, 다음 질문을 위해 입력창을 비우고 다시 포커스한다.
            input.onSubmit.AddListener(_ =>
            {
                SendCurrent();
                input.text = "";
                FocusInput();
            });
            return input;
        }
    }
}
#endif
