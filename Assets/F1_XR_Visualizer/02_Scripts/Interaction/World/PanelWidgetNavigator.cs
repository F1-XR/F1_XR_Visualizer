using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR;
using TMPro;

namespace F1XR.Interaction.World
{
    /// <summary>
    /// 기어로 고른 판넬 안의 요소들을 컨트롤러 썸스틱으로 옮겨 다니고 트리거로 실행합니다.
    /// 레이(광선)로 가리키지 않습니다.
    ///
    /// 유니티 기본 EventSystem 내비게이션은 <see cref="Selectable"/>(Button/Toggle/Dropdown ...) 만
    /// 다루고, XRI 기본 액션의 Navigate 는 Gamepad/Keyboard 에만 묶여 있어서 컨트롤러로는 동작하지
    /// 않습니다. 그래서 여기서 직접:
    ///   * 판넬 안에서 고를 수 있는 것들을 모으고(버튼뿐 아니라 글자/이미지 행까지),
    ///   * 썸스틱을 민 방향에서 가장 가까운 것으로 포커스를 옮기고,
    ///   * 트리거로 pointerClick / submit 이벤트를 쏩니다.
    /// 포커스 표시는 대상 위에 얇은 테두리 Image 하나를 씌워서 보여줍니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PanelWidgetNavigator : MonoBehaviour
    {
        [Header("Refs")]
        [Tooltip("비우면 같은 오브젝트 -> 씬에서 찾습니다.")]
        [SerializeField] GearPanelSelection panelSelection;
        [Tooltip("포커스 테두리에 쓸 셰이더. 비우면 F1XR/PanelSelectBorderUI 를 찾습니다.")]
        [SerializeField] Shader focusShader;

        [Header("입력")]
        [Tooltip("오른손 컨트롤러로 조작. 끄면 왼손.")]
        [SerializeField] bool useRightHand = true;
        [Tooltip("이 값을 넘겨야 한 칸 이동. 스틱을 중앙으로 되돌려야 다음 이동이 먹습니다.")]
        [SerializeField, Range(0.2f, 0.95f)] float moveThreshold = 0.6f;
        [Tooltip("스틱을 계속 밀고 있을 때 연속 이동 간격(초). 0 이면 반복 없음.")]
        [SerializeField, Min(0f)] float repeatInterval = 0.25f;

        [Header("고를 대상")]
        [Tooltip("Button/Toggle/Dropdown 같은 Selectable 포함.")]
        [SerializeField] bool includeSelectables = true;
        [Tooltip("클릭 핸들러가 붙은 요소 포함.")]
        [SerializeField] bool includeClickHandlers = true;
        [Tooltip("글자(TMP/Text)도 고를 수 있게. 리더보드 행처럼 버튼이 아닌 것도 선택하려면 켭니다.")]
        [SerializeField] bool includeTexts = true;
        [Tooltip("이 넓이(rect 단위 면적) 미만인 요소는 후보에서 제외. 잔글씨 걸러냄.")]
        [SerializeField, Min(0f)] float minArea = 200f;
        [Tooltip("대상 목록을 다시 훑는 간격(초). 드롭다운 항목처럼 런타임에 생기는 것들을 잡아줍니다.")]
        [SerializeField, Min(0.05f)] float rescanInterval = 0.3f;

        [Header("포커스 표시")]
        [SerializeField] Color focusColor = new Color(1f, 0.85f, 0.1f, 1f);
        [SerializeField, Min(0f)] float focusIntensity = 2.5f;
        [Tooltip("대상 짧은 변 대비 테두리 두께.")]
        [SerializeField, Range(0.01f, 0.3f)] float focusThicknessFraction = 0.06f;

        /// <summary>포커스가 옮겨갈 때 발생. 인수는 새 대상(없으면 null).</summary>
        public event System.Action<GameObject> FocusChanged;
        /// <summary>트리거로 실행했을 때 발생.</summary>
        public event System.Action<GameObject> Activated;

        public GameObject Focused => focused != null ? focused.gameObject : null;

        static readonly int SizeId = Shader.PropertyToID("_Size");
        static readonly int ThicknessId = Shader.PropertyToID("_Thickness");
        static readonly int RadiusId = Shader.PropertyToID("_Radius");
        static readonly int AmountId = Shader.PropertyToID("_Amount");
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int SpeedId = Shader.PropertyToID("_Speed");
        static readonly int TailId = Shader.PropertyToID("_Tail");

        readonly List<RectTransform> targets = new List<RectTransform>();
        readonly List<InputDevice> devices = new List<InputDevice>();

        Transform trackedPanel;
        RectTransform focused;
        RectTransform focusRect;
        Material focusMaterial;
        bool stickArmed = true;
        float nextRepeat;
        float nextRescan;
        bool triggerWasDown;

        void Awake()
        {
            if (panelSelection == null)
                panelSelection = GetComponent<GearPanelSelection>() ?? FindFirstObjectByType<GearPanelSelection>();
            if (focusShader == null)
                focusShader = Shader.Find("F1XR/PanelSelectBorderUI");
        }

        void Update()
        {
            var panel = panelSelection != null ? panelSelection.SelectedPanel : null;

            if (panel != trackedPanel)
            {
                trackedPanel = panel;
                nextRescan = 0f;
                Collect(panel);
                Focus(targets.Count > 0 ? targets[0] : null);
            }
            else if (panel != null && Time.time >= nextRescan)
            {
                // 드롭다운 목록처럼 나중에 생기는 요소를 잡는다. 포커스는 살아 있으면 유지.
                nextRescan = Time.time + rescanInterval;
                int before = targets.Count;
                var keep = focused;
                Collect(panel, quiet: true);
                if (targets.Count != before)
                    Debug.Log($"[PanelWidgetNavigator] 대상 {before} -> {targets.Count}개", panel);

                if (keep != null && targets.Contains(keep))
                    focused = keep;
                else
                    Focus(targets.Count > 0 ? targets[0] : null);
            }

            if (focused == null || targets.Count == 0)
                return;

            HandleStick();
            HandleTrigger();
            UpdateFocusVisual();
        }

        // ---------- 대상 수집 ----------

        void Collect(Transform panel, bool quiet = false)
        {
            targets.Clear();
            if (panel == null)
                return;

            var seen = new HashSet<RectTransform>();

            if (includeSelectables)
            {
                foreach (var s in panel.GetComponentsInChildren<Selectable>(false))
                    if (s.IsInteractable() && s.transform is RectTransform rt && Area(rt) >= minArea)
                        if (seen.Add(rt)) targets.Add(rt);
            }

            if (includeClickHandlers)
            {
                foreach (var h in panel.GetComponentsInChildren<MonoBehaviour>(false))
                    if (h is IPointerClickHandler && h.transform is RectTransform rt && Area(rt) >= minArea)
                        if (seen.Add(rt)) targets.Add(rt);
            }

            if (includeTexts)
            {
                foreach (var t in panel.GetComponentsInChildren<TMP_Text>(false))
                    AddIfStandalone(t.rectTransform, seen);
                foreach (var t in panel.GetComponentsInChildren<Text>(false))
                    AddIfStandalone(t.transform as RectTransform, seen);
            }

            if (!quiet)
                Debug.Log($"[PanelWidgetNavigator] {panel.name}: 대상 {targets.Count}개", panel);
        }

        // 버튼 안의 라벨은 버튼과 중복이므로 제외한다.
        void AddIfStandalone(RectTransform rt, HashSet<RectTransform> seen)
        {
            if (rt == null || rt.GetComponentInParent<Selectable>() != null || Area(rt) < minArea)
                return;
            if (seen.Add(rt))
                targets.Add(rt);
        }

        static float Area(RectTransform rt) => Mathf.Abs(rt.rect.width * rt.rect.height);

        // ---------- 이동 ----------

        void HandleStick()
        {
            Vector2 stick = ReadStick();

            if (stick.magnitude < moveThreshold * 0.5f)
            {
                stickArmed = true;
                return;
            }
            if (stick.magnitude < moveThreshold)
                return;

            bool repeat = !stickArmed && repeatInterval > 0f && Time.time >= nextRepeat;
            if (!stickArmed && !repeat)
                return;

            stickArmed = false;
            nextRepeat = Time.time + repeatInterval;

            var next = FindNeighbour(stick.normalized);
            if (next != null)
                Focus(next);
        }

        /// <summary>민 방향에 있는 것 중, 방향에서 벗어난 정도로 벌점을 준 거리 최솟값.</summary>
        RectTransform FindNeighbour(Vector2 dir)
        {
            Vector2 from = LocalCenter(focused);
            RectTransform best = null;
            float bestScore = float.MaxValue;

            foreach (var rt in targets)
            {
                if (rt == null || rt == focused)
                    continue;

                Vector2 delta = LocalCenter(rt) - from;
                float dist = delta.magnitude;
                if (dist < 1e-3f)
                    continue;

                float along = Vector2.Dot(delta / dist, dir);
                if (along < 0.5f)             // 대략 ±60도 안쪽만 후보
                    continue;

                float score = dist / Mathf.Max(along, 0.01f);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = rt;
                }
            }
            return best;
        }

        // 판넬 기준 평면 좌표. 캔버스가 월드에 떠 있어도 같은 평면에서 비교된다.
        Vector2 LocalCenter(RectTransform rt)
        {
            if (rt == null || trackedPanel == null)
                return Vector2.zero;
            Vector3 local = trackedPanel.InverseTransformPoint(rt.TransformPoint(rt.rect.center));
            return new Vector2(local.x, local.y);
        }

        // ---------- 실행 ----------

        void HandleTrigger()
        {
            bool down = ReadTrigger();
            if (down && !triggerWasDown)
                Activate();
            triggerWasDown = down;
        }

        void Activate()
        {
            if (focused == null)
                return;

            var go = focused.gameObject;
            var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };

            // 버튼/토글/드롭다운은 pointerClick 으로 다 처리된다. 그 외 요소는 submit 으로 한 번 더 시도.
            bool handled = ExecuteEvents.ExecuteHierarchy(go, data, ExecuteEvents.pointerClickHandler) != null;
            if (!handled)
                ExecuteEvents.ExecuteHierarchy(go, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);

            Debug.Log($"[PanelWidgetNavigator] activate {go.name} (handled={handled})", go);
            Activated?.Invoke(go);
        }

        // ---------- 포커스 표시 ----------

        void Focus(RectTransform next)
        {
            focused = next;

            if (focused == null)
            {
                if (focusRect != null)
                    focusRect.gameObject.SetActive(false);
                FocusChanged?.Invoke(null);
                return;
            }

            EnsureFocusRect();
            focusRect.gameObject.SetActive(true);
            focusRect.SetParent(focused, false);
            focusRect.anchorMin = Vector2.zero;
            focusRect.anchorMax = Vector2.one;
            focusRect.offsetMin = Vector2.zero;
            focusRect.offsetMax = Vector2.zero;
            focusRect.SetAsLastSibling();

            FocusChanged?.Invoke(focused.gameObject);
        }

        void EnsureFocusRect()
        {
            if (focusRect != null)
                return;

            var go = new GameObject("WidgetFocus", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            focusRect = (RectTransform)go.transform;

            var image = go.GetComponent<Image>();
            image.raycastTarget = false;

            focusMaterial = new Material(focusShader) { hideFlags = HideFlags.DontSave };
            focusMaterial.SetColor(EmissionId, focusColor);
            focusMaterial.SetFloat(IntensityId, focusIntensity);
            focusMaterial.SetFloat(SpeedId, 0f);   // 흐르지 않고 가만히 있는 테두리
            focusMaterial.SetFloat(TailId, 1f);
            focusMaterial.SetFloat(AmountId, 1f);
            image.material = focusMaterial;
        }

        void UpdateFocusVisual()
        {
            if (focusRect == null || focusMaterial == null)
                return;

            Rect r = focusRect.rect;
            float shortSide = Mathf.Min(r.width, r.height);
            focusMaterial.SetVector(SizeId, new Vector4(r.width, r.height, 0f, 0f));
            focusMaterial.SetFloat(ThicknessId, shortSide * focusThicknessFraction);
            focusMaterial.SetFloat(RadiusId, shortSide * 0.12f);
        }

        void OnDestroy()
        {
            if (focusMaterial != null)
                Destroy(focusMaterial);
        }

        // ---------- 컨트롤러 입력 ----------

        Vector2 ReadStick()
        {
            var characteristics = InputDeviceCharacteristics.Controller |
                (useRightHand ? InputDeviceCharacteristics.Right : InputDeviceCharacteristics.Left);
            devices.Clear();
            InputDevices.GetDevicesWithCharacteristics(characteristics, devices);
            foreach (var d in devices)
                if (d.isValid && d.TryGetFeatureValue(CommonUsages.primary2DAxis, out var axis) && axis.sqrMagnitude > 0f)
                    return axis;

            // 시뮬레이터/에디터에서는 레거시 XR 입력이 비어 있으므로 Input System 으로 대체.
            var control = FindControl("thumbstick") ?? FindControl("primary2DAxis");
            return control is UnityEngine.InputSystem.Controls.Vector2Control v ? v.ReadValue() : Vector2.zero;
        }

        bool ReadTrigger()
        {
            var characteristics = InputDeviceCharacteristics.Controller |
                (useRightHand ? InputDeviceCharacteristics.Right : InputDeviceCharacteristics.Left);
            devices.Clear();
            InputDevices.GetDevicesWithCharacteristics(characteristics, devices);
            foreach (var d in devices)
                if (d.isValid && d.TryGetFeatureValue(CommonUsages.triggerButton, out var pressed) && pressed)
                    return true;

            var control = FindControl("triggerButton") ?? FindControl("trigger");
            return control is UnityEngine.InputSystem.Controls.ButtonControl b && b.isPressed;
        }

        UnityEngine.InputSystem.InputControl FindControl(string controlName)
        {
            var want = useRightHand
                ? UnityEngine.InputSystem.CommonUsages.RightHand
                : UnityEngine.InputSystem.CommonUsages.LeftHand;

            foreach (var device in UnityEngine.InputSystem.InputSystem.devices)
            {
                if (!(device is UnityEngine.InputSystem.XR.XRController controller))
                    continue;

                bool match = false;
                foreach (var u in controller.usages)
                    if (u == want) { match = true; break; }
                if (!match)
                    continue;

                foreach (var child in controller.children)
                    if (child.name == controlName)
                        return child;
            }
            return null;
        }
    }
}
