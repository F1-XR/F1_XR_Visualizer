using UnityEngine;

namespace F1XR.Interaction.World
{
    /// <summary>
    /// A single circular menu item. Pure visual - it carries NO collider / raycaster / interactable,
    /// because the gear lever (not a pointer) decides what is hovered or selected. This component only
    /// knows how to pose and colour itself:
    ///   * its resting slot in the front-to-back stack (captured from the transform at startup),
    ///   * how far to pop out toward its direction and toward the camera while hovered,
    ///   * how to fade in/out during the open / close reveal,
    ///   * and how to switch between the normal, hover and selected colour states.
    ///
    /// The <see cref="GearUIController"/> drives all of this every frame; the item never animates itself.
    /// Colours are pushed through a <see cref="MaterialPropertyBlock"/> so no material assets are cloned.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GearUIItem : MonoBehaviour
    {
        [Header("Binding")]
        [Tooltip("Which lever direction this item represents. Set per item in the Inspector.")]
        [SerializeField] GearDirection gearDirection = GearDirection.None;

        [Header("Hover pose (added on top of the resting slot)")]
        [Tooltip("Local offset the item slides to when fully hovered - push it OUT of the stack along " +
                 "its direction (e.g. +X for Right).")]
        [SerializeField] Vector3 hoverPositionOffset = new Vector3(0.03f, 0f, 0f);
        [Tooltip("Extra distance the item moves toward the camera when fully hovered.")]
        [SerializeField] float cameraOffset = 0.02f;
        [Tooltip("Scale multiplier at full hover (1.2 - 1.35 reads well).")]
        [SerializeField, Range(1f, 2f)] float hoverScaleMultiplier = 1.28f;

        [Header("Detent 강조 (앞뒤 4단, 슬롯 위치 고정)")]
        [Tooltip("비활성 단 카드의 투명도(흐리게).")]
        [SerializeField, Range(0f, 1f)] float gearInactiveAlpha = 0.4f;
        [Tooltip("활성 카드의 렌더 우선순위(높을수록 앞에 그려짐).")]
        [SerializeField] int activeSortingOrder = 10;

        [Header("Reveal (open / close)")]
        [Tooltip("Scale the item starts at while collapsed onto the front slot during the open animation.")]
        [SerializeField, Range(0.1f, 1f)] float revealStartScale = 0.4f;

        [Header("Renderers (all optional)")]
        [SerializeField] Renderer backgroundRenderer;
        [SerializeField] Renderer outlineRenderer;
        [SerializeField] Renderer iconRenderer;
        [SerializeField] Renderer hoverHalo;

        [Header("Colours (F1: carbon black card, red line / glow)")]
        [SerializeField] Color normalBackgroundColor = new Color(0.043f, 0.043f, 0.051f, 0.62f);
        [SerializeField] Color selectedBackgroundColor = new Color(0.882f, 0.024f, 0f, 1f);
        [SerializeField] Color normalIconColor = Color.white;
        [SerializeField] Color selectedIconColor = Color.white;
        [SerializeField] Color outlineColor = new Color(1f, 0.165f, 0.133f, 0.9f);
        [SerializeField] Color haloColor = new Color(1f, 0.118f, 0.078f, 1f);
        [Tooltip("Halo alpha at full hover.")]
        [SerializeField, Range(0f, 1f)] float haloHoverAlpha = 0.45f;
        [Tooltip("How much brighter the outline gets at full hover.")]
        [SerializeField, Range(1f, 3f)] float outlineHoverBoost = 1.6f;
        [Tooltip("Brightness of the non-hovered items while another item is hovered (0.5 - 0.7).")]
        [SerializeField, Range(0.2f, 1f)] float dimAmount = 0.55f;

        [Tooltip("Shader colour properties to drive. _BaseColor covers URP; _Color covers legacy/Built-in.")]
        [SerializeField] string baseColorProperty = "_BaseColor";
        [SerializeField] string legacyColorProperty = "_Color";

        public GearDirection Direction => gearDirection;
        public Vector3 DefaultLocalPosition => defaultLocalPosition;

        Transform tf;
        MaterialPropertyBlock mpb;
        int baseColorId;
        int legacyColorId;
        Vector3 defaultLocalPosition;
        Vector3 defaultScale = Vector3.one;
        bool selected;
        float groupAlpha = 1f;
        bool initialized;

        void Awake() => EnsureInit();

        /// <summary>
        /// Caches the transform's authored local pose as the item's resting slot. Place the item where
        /// it should sit in the stack in the Editor; this captures it once.
        /// </summary>
        void EnsureInit()
        {
            if (initialized)
                return;

            tf = transform;
            defaultLocalPosition = tf.localPosition;
            defaultScale = tf.localScale;
            baseColorId = Shader.PropertyToID(baseColorProperty);
            legacyColorId = Shader.PropertyToID(legacyColorProperty);
            mpb = new MaterialPropertyBlock();
            initialized = true;
        }

        /// <summary>Snap straight back to the resting slot, normal colours, no halo.</summary>
        public void ResetToDefault()
        {
            EnsureInit();
            groupAlpha = 1f;
            selected = false;
            tf.localPosition = defaultLocalPosition;
            tf.localScale = defaultScale;
            SetHalo(0f);
            RefreshColors(0f, false);
            SetSortingOrder(0);
        }

        /// <summary>
        /// Reveal interpolation used by the open / close stagger. <paramref name="t"/> = 0 collapses the
        /// item onto <paramref name="collapsedLocalPos"/> (the front slot) at <see cref="revealStartScale"/>
        /// with alpha 0; t = 1 is the resting slot at full alpha.
        /// </summary>
        public void SetReveal(float t, Vector3 collapsedLocalPos)
        {
            EnsureInit();
            t = Mathf.Clamp01(t);
            // 슬롯 위치는 절대 이동하지 않는다 - 열고 닫을 때도 제자리에서 크기/투명도만 변한다.
            tf.localPosition = defaultLocalPosition;
            tf.localScale = defaultScale * Mathf.Lerp(revealStartScale, 1f, t);
            groupAlpha = t;
            SetHalo(0f);
            RefreshColors(0f, false);
        }

        /// <summary>
        /// Drives the live hover state. <paramref name="progress"/> (0..1) comes straight from the lever
        /// tilt so the pop tracks the hand. The item breaks OUT of the stack toward its direction and
        /// toward the camera, scales up, and lights its halo. <paramref name="dim"/> darkens it when a
        /// different item is the hovered one.
        /// </summary>
        public void ApplyHover(float progress, Vector3 cameraDirLocal, bool dim)
        {
            EnsureInit();
            groupAlpha = 1f;
            float p = Mathf.Clamp01(progress);

            tf.localPosition = defaultLocalPosition
                               + hoverPositionOffset * p
                               + cameraDirLocal.normalized * (cameraOffset * p);
            tf.localScale = defaultScale * Mathf.Lerp(1f, hoverScaleMultiplier, p);

            SetHalo(Mathf.Lerp(0f, haloHoverAlpha, p));
            RefreshColors(p, dim);
        }

        /// <summary>
        /// Detent(앞뒤 4단) 전용 강조. 슬롯의 x/y 위치는 절대 바꾸지 않고, <paramref name="activeness"/>
        /// (0..1, 부드럽게 보간된 값)에 따라 Z 깊이(카메라 쪽으로), Scale, Alpha, 렌더 우선순위만 바꿉니다.
        /// 그래서 카드끼리 자리를 바꾸거나 통과하지 않고, 현재 단 카드만 자기 자리에서 앞으로 올라옵니다.
        /// </summary>
        public void ApplyGearHighlight(float activeness)
        {
            EnsureInit();
            float a = Mathf.Clamp01(activeness);

            // 슬롯 위치 완전 고정 - 앞으로 이동 없이 제자리에서 크기만 커짐.
            tf.localPosition = defaultLocalPosition;

            tf.localScale = defaultScale * Mathf.Lerp(1f, hoverScaleMultiplier, a);

            // 비활성일수록 흐리게(그룹 알파), 활성이면 불투명.
            groupAlpha = Mathf.Lerp(gearInactiveAlpha, 1f, a);
            SetHalo(Mathf.Lerp(0f, haloHoverAlpha, a));
            RefreshColors(a, dim: false);

            SetSortingOrder(Mathf.RoundToInt(a * activeSortingOrder));
        }

        void SetSortingOrder(int order)
        {
            if (backgroundRenderer != null) backgroundRenderer.sortingOrder = order;
            if (outlineRenderer != null) outlineRenderer.sortingOrder = order;
            if (hoverHalo != null) hoverHalo.sortingOrder = order - 1;
            if (iconRenderer != null) iconRenderer.sortingOrder = order + 1;
        }

        /// <summary>Marks the item as the selected feature (white fill kept until the menu resets).</summary>
        public void SetSelected(bool value)
        {
            EnsureInit();
            selected = value;
            RefreshColors(0f, false);
        }

        /// <summary>Directly set the item's scale - used by the selection pop animation.</summary>
        public void SetScaleMultiplier(float multiplier)
        {
            EnsureInit();
            tf.localScale = defaultScale * multiplier;
        }

        void RefreshColors(float hoverProgress, bool dim)
        {
            float dimMul = dim ? dimAmount : 1f;

            Color bg = selected ? selectedBackgroundColor : normalBackgroundColor;
            ApplyColor(backgroundRenderer, Darken(bg, dimMul));

            Color icon = selected ? selectedIconColor : normalIconColor;
            ApplyColor(iconRenderer, Darken(icon, dimMul));

            Color outline = outlineColor;
            outline.a *= Mathf.Lerp(1f, outlineHoverBoost, hoverProgress);
            ApplyColor(outlineRenderer, Darken(outline, dimMul));
        }

        void SetHalo(float alpha)
        {
            if (hoverHalo == null)
                return;

            Color c = haloColor;
            c.a = alpha;
            ApplyColor(hoverHalo, c);
        }

        // Multiplies RGB (leaves alpha) to darken an item without changing its opacity.
        static Color Darken(Color c, float mul) => new Color(c.r * mul, c.g * mul, c.b * mul, c.a);

        void ApplyColor(Renderer r, Color c)
        {
            if (r == null)
                return;

            c.a *= groupAlpha;
            // Property blocks skip the sRGB->linear conversion a material asset gets, so the picker
            // colour above would render washed out. Convert here (alpha is left alone).
            c = c.linear;
            r.GetPropertyBlock(mpb);
            mpb.SetColor(baseColorId, c);
            mpb.SetColor(legacyColorId, c);
            r.SetPropertyBlock(mpb);
        }
    }
}
