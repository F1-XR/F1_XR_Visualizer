using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace F1XR.Interaction.World
{
    /// <summary>
    /// 기어봉 4단(디텐트)과 판넬을 1:1로 묶습니다. 기어를 그 단으로 넣으면
    /// (<see cref="GearShiftController.GearChanged"/>) 그 단에 묶인 판넬만 살짝 커지고,
    /// 테두리에 빨간 emission 이 한 바퀴씩 돕니다. 다른 단으로 가면 그쪽으로 넘어갑니다.
    /// 단 번호는 <see cref="GearUIController"/> 의 items 순서와 같습니다(0 = items[0] 카드).
    ///
    /// 테두리는 판넬 Canvas 를 꽉 채우는 Image 하나를 런타임에 만들어 F1XR/PanelSelectBorderUI 로
    /// 그립니다(판넬 프리팹은 건드리지 않음). 판넬 스케일은 다른 스크립트(등장 연출 등)가 쓰는 값을
    /// 기준으로 곱하기만 하므로, 판넬이 0 스케일로 숨어 있어도 억지로 띄우지 않습니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GearPanelSelection : MonoBehaviour
    {
        [Serializable]
        public class Binding
        {
            [Tooltip("이 판넬에 연결된 기어 UI 카드(UI_Item_*). 단 번호는 여기서 자동으로 뽑습니다.")]
            public GearUIItem uiItem;
            [Tooltip("커지고 테두리가 도는 판넬 루트.")]
            public Transform panel;
            [Tooltip("비우면 판넬의 배경/림 Image 를 찾아 거기에 딱 맞춰 붙입니다.")]
            public RectTransform borderParent;
            [Tooltip("모서리 반경을 직접 지정(rect 단위). 음수면 판넬 스프라이트에서 자동 계산.")]
            public float cornerRadiusOverride = -1f;
            [Tooltip("단을 골랐을 때 처음 선택될 위젯. 비우면 판넬 안 첫 Selectable 을 자동으로 찾습니다.")]
            public Selectable firstWidget;

            [NonSerialized] public float autoRadius = -1f;
            [NonSerialized] public int gear = -1;
            [NonSerialized] public Vector3 baseScale = Vector3.one;
            [NonSerialized] public Vector3 lastApplied;
            [NonSerialized] public RectTransform borderRect;
            [NonSerialized] public Material borderMaterial;
            [NonSerialized] public float amount;
        }

        [Header("Bindings (단 하나에 판넬 하나)")]
        [SerializeField] List<Binding> bindings = new List<Binding>();

        [Header("Refs")]
        [Tooltip("보통 비워 둡니다. 기어봉은 A 버튼을 누를 때 ControllerShiftMorph 가 런타임에 만들기 때문에, " +
                 "생길 때까지 기다렸다가 자동으로 잡습니다.")]
        [SerializeField] GearShiftController gearShift;
        [Tooltip("F1XR/PanelSelectBorderUI 셰이더. 비우면 이름으로 찾습니다.")]
        [SerializeField] Shader borderShader;

        [Header("Select 연출")]
        [Tooltip("선택된 판넬의 크기 배수.")]
        [SerializeField, Range(1f, 1.5f)] float selectedScale = 1.06f;
        [Tooltip("커지고 작아지는 부드러움(초). 작을수록 즉각.")]
        [SerializeField, Min(0f)] float smoothTime = 0.12f;

        [Header("테두리")]
        [SerializeField] Color borderColor = new Color(1f, 0.06f, 0.03f, 1f);
        [Tooltip("판넬 짧은 변 대비 테두리 두께 비율.")]
        [SerializeField, Range(0.002f, 0.1f)] float thicknessFraction = 0.03f;
        [Tooltip("판넬 스프라이트에서 반경을 못 뽑았을 때만 쓰는 대비책(짧은 변 대비 비율).")]
        [SerializeField, Range(0f, 0.5f)] float cornerRadiusFraction = 0.08f;
        [Tooltip("초당 몇 바퀴 도는지.")]
        [SerializeField, Min(0f)] float chaseSpeed = 0.6f;
        [Tooltip("꼬리 길이(둘레 대비).")]
        [SerializeField, Range(0.02f, 1f)] float tailLength = 0.18f;
        [Tooltip("emission 세기. Bloom 이 켜져 있으면 1 이상에서 번집니다.")]
        [SerializeField, Min(0f)] float intensity = 3f;

        [Header("판넬 내부 위젯 조작 (썸스틱 + Submit)")]
        [Tooltip("단을 고르면 그 판넬의 첫 위젯을 EventSystem 에 선택시킵니다. 그 뒤 썸스틱(XRI UI/Navigate)으로 " +
                 "버튼 사이를 옮기고 Submit 으로 누릅니다. 레이로 가리킬 필요 없음.")]
        [SerializeField] bool focusWidgetOnSelect = true;

        static readonly int SizeId = Shader.PropertyToID("_Size");
        static readonly int ThicknessId = Shader.PropertyToID("_Thickness");
        static readonly int RadiusId = Shader.PropertyToID("_Radius");
        static readonly int SpeedId = Shader.PropertyToID("_Speed");
        static readonly int TailId = Shader.PropertyToID("_Tail");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly int AmountId = Shader.PropertyToID("_Amount");

        int selected = -1; // 아직 아무 단도 안 골림
        float nextLeverSearch;

        /// <summary>현재 기어로 고른 판넬. 아무것도 안 골랐으면 null.</summary>
        public Transform SelectedPanel
        {
            get
            {
                if (selected < 0)
                    return null;
                foreach (var b in bindings)
                    if (b != null && b.gear == selected)
                        return b.panel;
                return null;
            }
        }

        void Awake()
        {
            if (borderShader == null)
                borderShader = Shader.Find("F1XR/PanelSelectBorderUI");

            foreach (var b in bindings)
            {
                if (b == null || b.panel == null)
                    continue;

                b.baseScale = b.panel.localScale;
                b.lastApplied = b.baseScale;
                BuildBorder(b);
            }
        }

        void OnEnable()
        {
            if (gearShift != null)
                Bind(gearShift);
        }

        void OnDisable()
        {
            if (gearShift != null)
                gearShift.GearChanged -= OnGearChanged;
        }

        /// <summary>
        /// 기어봉은 A 버튼 토글 때 생기므로, 없으면 0.5초마다 한 번씩만 찾아본다.
        /// 카드 참조는 프리팹 원본을 가리키니 인스턴스 쪽 단 번호는 이름으로 맞춘다.
        /// </summary>
        void FindLever()
        {
            var found = FindFirstObjectByType<GearShiftController>(FindObjectsInactive.Include);
            if (found != null)
                Bind(found);
        }

        void Bind(GearShiftController lever)
        {
            if (gearShift != null)
                gearShift.GearChanged -= OnGearChanged;

            gearShift = lever;
            gearShift.GearChanged += OnGearChanged;

            var ui = gearShift.UI;
            foreach (var b in bindings)
            {
                if (b == null || b.panel == null)
                    continue;

                b.gear = ui != null && b.uiItem != null ? ui.IndexOfName(b.uiItem.name) : -1;
                if (b.gear < 0)
                    Debug.LogWarning($"[GearPanelSelection] {b.panel.name} 의 UI Item 을 기어봉 items 에서 못 찾음.", this);
                Debug.Log($"[GearPanelSelection] bound: gear {b.gear} <- {(b.uiItem != null ? b.uiItem.name : "?")} -> {b.panel.name}", b.panel);
            }
        }

        void OnGearChanged(int gear)
        {
            selected = gear;
            FocusWidget(gear);
            Debug.Log($"[GearPanelSelection] gear changed -> {gear}", this);
        }

        /// <summary>
        /// 고른 단의 판넬에서 첫 위젯을 EventSystem 에 선택시킨다. 선택된 위젯이 있어야 썸스틱
        /// (XRI UI/Navigate)으로 이동하고 Submit 으로 누를 수 있다 - 레이로 가리킬 필요가 없어진다.
        /// </summary>
        void FocusWidget(int gear)
        {
            if (!focusWidgetOnSelect)
                return;

            var events = EventSystem.current;
            if (events == null)
                return;

            foreach (var b in bindings)
            {
                if (b == null || b.gear != gear || b.panel == null)
                    continue;

                var target = b.firstWidget != null ? b.firstWidget : FindFirstWidget(b.panel);
                events.SetSelectedGameObject(target != null ? target.gameObject : null);
                if (target == null)
                    Debug.LogWarning($"[GearPanelSelection] {b.panel.name} 안에 선택 가능한 위젯이 없습니다.", b.panel);
                return;
            }
        }

        static Selectable FindFirstWidget(Transform panel)
        {
            foreach (var s in panel.GetComponentsInChildren<Selectable>(false))
                if (s.IsInteractable() && s.navigation.mode != Navigation.Mode.None)
                    return s;
            return null;
        }

        void OnDestroy()
        {
            foreach (var b in bindings)
                if (b != null && b.borderMaterial != null)
                    Destroy(b.borderMaterial);
        }

        /// <summary>선택 해제(모든 판넬 원래대로, 위젯 포커스도 해제).</summary>
        public void ClearSelection()
        {
            selected = -1;
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
        }

        void Update()
        {
            if (gearShift == null && Time.time >= nextLeverSearch)
            {
                nextLeverSearch = Time.time + 0.5f;
                FindLever();
            }

            float t = smoothTime <= 0f ? 1f : 1f - Mathf.Exp(-Time.deltaTime / smoothTime);

            foreach (var b in bindings)
            {
                if (b == null || b.panel == null)
                    continue;

                // selected 와 gear 둘 다 -1(미선택 / 미배선)일 수 있으므로 반드시 0 이상일 때만 매칭한다.
                bool on = selected >= 0 && b.gear == selected;
                b.amount = Mathf.Lerp(b.amount, on ? 1f : 0f, t);

                // 판넬 등장 연출 등 다른 스크립트가 스케일을 쓰면 그 값을 새 기준으로 삼는다.
                // (판넬들은 0 스케일로 숨어 있다가 나타나므로 시작 스케일을 캐싱하면 안 된다.)
                Vector3 current = b.panel.localScale;
                if ((current - b.lastApplied).sqrMagnitude > 1e-10f)
                    b.baseScale = current;

                Vector3 target = b.baseScale * Mathf.Lerp(1f, selectedScale, b.amount);
                b.panel.localScale = target;
                b.lastApplied = target;

                if (b.borderMaterial == null || b.borderRect == null)
                    continue;

                // 판넬은 코너 드래그로 크기가 바뀌므로 매 프레임 실제 rect 를 넣어 준다.
                Rect r = b.borderRect.rect;
                float shortSide = Mathf.Min(r.width, r.height);
                b.borderMaterial.SetVector(SizeId, new Vector4(r.width, r.height, 0f, 0f));
                b.borderMaterial.SetFloat(ThicknessId, shortSide * thicknessFraction);
                // 반경: 직접 지정 > 판넬 스프라이트에서 계산 > 비율 대비책.
                float radius = b.cornerRadiusOverride >= 0f ? b.cornerRadiusOverride
                             : b.autoRadius >= 0f ? b.autoRadius
                             : shortSide * cornerRadiusFraction;
                b.borderMaterial.SetFloat(RadiusId, radius);
                b.borderMaterial.SetFloat(AmountId, b.amount);
            }
        }

        void BuildBorder(Binding b)
        {
            if (borderShader == null)
                return;

            RectTransform host = b.borderParent;
            if (host == null)
            {
                // 판넬마다 배경/림 모양이 다르므로, 그 Image 에 직접 붙여 rect 와 모서리 반경을 그대로 따라간다.
                var bg = FindPanelBackground(b.panel);
                if (bg != null)
                {
                    host = (RectTransform)bg.transform;
                    b.autoRadius = SpriteCornerRadius(bg);
                }
            }
            if (host == null)
            {
                // 배경 Image 를 못 찾으면 판넬 자신 -> 아래 첫 Canvas 순으로 떨어진다(반경은 비율 대비책).
                host = b.panel as RectTransform;
                if (host == null)
                {
                    var canvas = b.panel.GetComponentInChildren<Canvas>(true);
                    host = canvas != null ? canvas.transform as RectTransform : null;
                }
            }
            if (host == null)
            {
                Debug.LogWarning($"[GearPanelSelection] {b.panel.name} 아래 Canvas 가 없어 테두리를 못 만듭니다.", b.panel);
                return;
            }

            var go = new GameObject("SelectBorder", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(host, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.SetAsLastSibling();

            var image = go.GetComponent<Image>();
            image.raycastTarget = false;

            b.borderMaterial = new Material(borderShader) { hideFlags = HideFlags.DontSave };
            b.borderMaterial.SetColor(EmissionId, borderColor);
            b.borderMaterial.SetFloat(SpeedId, chaseSpeed);
            b.borderMaterial.SetFloat(TailId, tailLength);
            b.borderMaterial.SetFloat(IntensityId, intensity);
            b.borderMaterial.SetFloat(AmountId, 0f);
            image.material = b.borderMaterial;

            b.borderRect = rect;
            Debug.Log($"[GearPanelSelection] border on {b.panel.name}/{host.name} rect={rect.rect.size} autoRadius={b.autoRadius:F1}", b.panel);
        }

        /// <summary>
        /// 테두리를 그릴 바탕 Image. 9-slice(둥근) 스프라이트를 쓰는 것 중 가장 큰 것 = 판넬 외곽.
        /// 없으면 그냥 가장 큰 Image.
        /// </summary>
        static Image FindPanelBackground(Transform panel)
        {
            Image best = null, bestAny = null;
            float bestArea = -1f, bestAnyArea = -1f;

            foreach (var img in panel.GetComponentsInChildren<Image>(true))
            {
                var rt = img.transform as RectTransform;
                // 꺼져 있는 Image(드롭다운 Template 등)는 판넬 외곽이 아니다.
                if (rt == null || !img.gameObject.activeInHierarchy)
                    continue;

                float area = rt.rect.width * rt.rect.height;
                if (area > bestAnyArea) { bestAnyArea = area; bestAny = img; }

                if (img.sprite != null && img.sprite.border != Vector4.zero && area > bestArea)
                {
                    bestArea = area;
                    best = img;
                }
            }
            return best != null ? best : bestAny;
        }

        /// <summary>
        /// 9-slice 스프라이트의 모서리 반경을 rect 단위로 환산. sliced Image 는 border(스프라이트 px)를
        /// spritePPU / canvasRefPPU * pixelsPerUnitMultiplier 로 나눠 그리므로 같은 식을 쓴다.
        /// 계산할 수 없으면 -1.
        /// </summary>
        static float SpriteCornerRadius(Image img)
        {
            if (img == null || img.sprite == null)
                return -1f;

            Vector4 border = img.sprite.border;
            float maxBorder = Mathf.Max(Mathf.Max(border.x, border.y), Mathf.Max(border.z, border.w));
            if (maxBorder <= 0f)
                return -1f;

            var canvas = img.canvas;
            float referencePPU = canvas != null ? canvas.referencePixelsPerUnit : 100f;
            float scale = img.sprite.pixelsPerUnit / Mathf.Max(1e-4f, referencePPU)
                          * Mathf.Max(0.01f, img.pixelsPerUnitMultiplier);
            return maxBorder / Mathf.Max(1e-4f, scale);
        }
    }
}
