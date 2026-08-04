using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace F1XR.OriginalKnob
{
    /// <summary>
    /// Drives the white glow ring as a set of arcs that are DRAWN by turning the knob.
    ///
    /// - Idle / not grabbed: the bright ring is NOT active - only a faint recessed groove is (barely) visible.
    /// - While grabbed and turning: an arc is progressively drawn, winding from zero up to a full circle over
    ///   one full turn of the knob (so the line follows the knob instead of a fixed half-arc spinning).
    /// - Each further full turn starts an additional concentric arc at a different radius (2nd after 1 turn,
    ///   3rd after 2 turns) - they never all appear at once.
    /// - On release everything fades out; grabbing again redraws to the current accumulated turn count.
    ///
    /// Every arc shares one material and is fed through a MaterialPropertyBlock - no per-frame meshes or
    /// material copies. Arc geometry (radius / width / colour) is per-arc and Inspector-tunable.
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class RotaryRingVisualController : MonoBehaviour
    {
        [Serializable]
        public class ArcSettings
        {
            public Renderer renderer;
            [Tooltip("Ring radius in normalised quad units (1 = quad mid-edge).")]
            public float radius = 0.79f;
            [Tooltip("Base start angle (deg) - unused for the drawn arcs, kept for tuning.")]
            public float startAngle = 90f;
            [Tooltip("Unused for drawn arcs (length is driven by turn progress).")]
            [Range(0f, 360f)] public float arcLength = 360f;
            [Tooltip("Line half-width in normalised units.")]
            public float lineWidth = 0.015f;
            public float intensity = 1f;
            [Range(0f, 1f)] public float alpha = 1f;
            [ColorUsage(true, true)] public Color color = Color.white;
        }

        [Header("References")]
        [SerializeField] RotaryKnobController knob;
        [SerializeField] XRSimpleInteractable interactable;

        [Header("Arcs (concentric, drawn by turns)")]
        // Same diameter and thickness for every ring - they are separated by DEPTH (Z), not radius, to
        // form the 3D stacked-coil look. The per-ring depth offset lives on the ring quads (see builder).
        [SerializeField] ArcSettings baseRing = new ArcSettings
        { radius = 0.72f, lineWidth = 0.012f, intensity = 0.10f, alpha = 1f, color = new Color(0.5f, 0.5f, 0.58f) };
        [SerializeField] ArcSettings mainGlowArc = new ArcSettings
        { radius = 0.72f, lineWidth = 0.018f, intensity = 2.2f, alpha = 1f, color = Color.white };
        [SerializeField] ArcSettings trailArc01 = new ArcSettings
        { radius = 0.72f, lineWidth = 0.018f, intensity = 2.2f, alpha = 1f, color = Color.white };
        [SerializeField] ArcSettings trailArc02 = new ArcSettings
        { radius = 0.72f, lineWidth = 0.018f, intensity = 2.2f, alpha = 1f, color = Color.white };

        [Header("Drawing")]
        [Tooltip("Angle (deg) where each arc starts drawing from. 90 = top.")]
        [SerializeField] float drawStartAngle = 90f;
        [Tooltip("Flip the draw/wind direction if it runs opposite to the knob/marker.")]
        [SerializeField] bool invertDraw = false;
        [Tooltip("Turns needed to fully draw one ring before the next begins.")]
        [SerializeField, Min(0.1f)] float turnsPerRing = 1f;

        [Header("Base groove")]
        [SerializeField, Range(0f, 1f)] float grooveIdle = 0.35f;
        [SerializeField, Min(0f)] float grooveHoverBoost = 0.4f;

        [Header("Smoothing (s)")]
        [Tooltip("Fade-in time when grabbing (ring activates).")]
        [SerializeField, Min(0f)] float activateTime = 0.05f;
        [Tooltip("Fade-out time when releasing (ring deactivates).")]
        [SerializeField, Min(0f)] float releaseTime = 0.2f;
        [SerializeField, Min(0f)] float stateSmoothTime = 0.08f;

        static readonly int RingColorId = Shader.PropertyToID("_RingColor");
        static readonly int RadiusId = Shader.PropertyToID("_Radius");
        static readonly int RadiusOffsetId = Shader.PropertyToID("_RadiusOffset");
        static readonly int LineWidthId = Shader.PropertyToID("_LineWidth");
        static readonly int StartAngleId = Shader.PropertyToID("_StartAngle");
        static readonly int RotationOffsetId = Shader.PropertyToID("_RotationOffset");
        static readonly int ArcLengthId = Shader.PropertyToID("_ArcLength");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int AlphaId = Shader.PropertyToID("_Alpha");

        MaterialPropertyBlock mpb;
        bool hovered;
        bool grabbed;
        float hoverAmount;
        float activeAmount; // 0 idle -> 1 grabbed; gates the whole bright ring

        void Awake()
        {
            if (knob == null)
                knob = GetComponentInParent<RotaryKnobController>();
            mpb = new MaterialPropertyBlock();
        }

        void OnEnable()
        {
            if (mpb == null)
                mpb = new MaterialPropertyBlock();

            if (Application.isPlaying && interactable != null)
            {
                interactable.hoverEntered.AddListener(OnHoverEntered);
                interactable.hoverExited.AddListener(OnHoverExited);
                interactable.selectEntered.AddListener(OnSelectEntered);
                interactable.selectExited.AddListener(OnSelectExited);
            }
        }

        void OnDisable()
        {
            if (Application.isPlaying && interactable != null)
            {
                interactable.hoverEntered.RemoveListener(OnHoverEntered);
                interactable.hoverExited.RemoveListener(OnHoverExited);
                interactable.selectEntered.RemoveListener(OnSelectEntered);
                interactable.selectExited.RemoveListener(OnSelectExited);
            }

            hovered = grabbed = false;
            hoverAmount = activeAmount = 0f;
            ApplyAll();
        }

        void OnHoverEntered(HoverEnterEventArgs a) => hovered = true;
        void OnHoverExited(HoverExitEventArgs a) => hovered = false;
        void OnSelectEntered(SelectEnterEventArgs a) => grabbed = true;
        void OnSelectExited(SelectExitEventArgs a) => grabbed = false;

        void LateUpdate()
        {
            if (mpb == null)
                mpb = new MaterialPropertyBlock();

            float dt = Mathf.Max(Application.isPlaying ? Time.deltaTime : 0.016f, 1e-5f);
            float sState = stateSmoothTime <= 0f ? 1f : 1f - Mathf.Exp(-dt / stateSmoothTime);
            hoverAmount = Mathf.Lerp(hoverAmount, hovered ? 1f : 0f, sState);

            // Ring activates while grabbed, deactivates on release.
            float aTime = grabbed ? activateTime : releaseTime;
            float sActive = aTime <= 0f ? 1f : 1f - Mathf.Exp(-dt / aTime);
            activeAmount = Mathf.Lerp(activeAmount, grabbed ? 1f : 0f, sActive);

            ApplyAll();
        }

        void ApplyAll()
        {
            // Faint recessed groove - not the "active" bright ring; brightens slightly on hover.
            float grooveIntensity = baseRing.intensity * (grooveIdle + hoverAmount * grooveHoverBoost);
            SetArc(baseRing, baseRing.startAngle, 0f, 360f, baseRing.lineWidth, grooveIntensity, baseRing.alpha);

            float per = Mathf.Max(turnsPerRing, 0.1f);
            // Signed visual rotation: magnitude fills the rings, sign sets the draw direction so the line
            // winds the SAME way the knob (and marker) turns. The panel is mirrored, so +visualTotal reads
            // as clockwise on screen; drawing toward decreasing shader angle keeps the line clockwise too.
            float signed = knob != null ? knob.VisualAngle : 0f;
            float turns = Mathf.Abs(signed) / (360f * per);
            float dirSign = signed >= 0f ? 1f : -1f;
            if (invertDraw) dirSign = -dirSign;

            DrawRing(mainGlowArc, Mathf.Clamp01(turns), dirSign);
            DrawRing(trailArc01, Mathf.Clamp01(turns - 1f), dirSign);
            DrawRing(trailArc02, Mathf.Clamp01(turns - 2f), dirSign);
        }

        void DrawRing(ArcSettings arc, float progress, float dirSign)
        {
            float len = progress * 360f;
            float vis = activeAmount * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.02f, progress));

            // Anchor fixed at drawStartAngle; grow in the knob's turn direction. dirSign > 0 grows toward
            // decreasing shader angle (screen-clockwise), so the arc occupies [anchor-len, anchor].
            float start = dirSign > 0f ? drawStartAngle - len : drawStartAngle;

            SetArc(arc, start, 0f, len, arc.lineWidth, arc.intensity * vis, arc.alpha * vis);
        }

        void SetArc(ArcSettings arc, float startAngle, float rotationOffset, float arcLength, float lineWidth, float intensity, float alpha)
        {
            if (arc == null || arc.renderer == null)
                return;

            arc.renderer.GetPropertyBlock(mpb);
            mpb.SetColor(RingColorId, arc.color);
            mpb.SetFloat(RadiusId, arc.radius);
            mpb.SetFloat(RadiusOffsetId, 0f);
            mpb.SetFloat(LineWidthId, Mathf.Max(0.0005f, lineWidth));
            mpb.SetFloat(StartAngleId, startAngle);
            mpb.SetFloat(RotationOffsetId, rotationOffset);
            mpb.SetFloat(ArcLengthId, Mathf.Clamp(arcLength, 0f, 360f));
            mpb.SetFloat(IntensityId, Mathf.Max(0f, intensity));
            mpb.SetFloat(AlphaId, Mathf.Clamp01(alpha));
            arc.renderer.SetPropertyBlock(mpb);
        }
    }
}
