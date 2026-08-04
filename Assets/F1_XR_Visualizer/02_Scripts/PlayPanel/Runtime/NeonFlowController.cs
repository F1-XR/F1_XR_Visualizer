using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace F1XR.PlayPanel
{
    /// <summary>
    /// Owns the "alive" behaviour of the play panel's neon element.
    ///
    /// - The colour flow (cyan -> green -> yellow -> orange -> pink -> purple) is driven entirely by the
    ///   NeonFlowRibbon shader from _Time, so it scrolls slowly on its own in both edit and play mode.
    /// - This controller only reacts to a hand / ray approaching the BUTTON: on hover the whole neon
    ///   brightens and the button group eases a hair forward (+Z). The panel and the top play symbol do
    ///   not move. On exit everything settles back.
    ///
    /// Every ribbon shares one material; per-ribbon glow and flow phase are pushed via a
    /// MaterialPropertyBlock so nothing allocates per frame.
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class NeonFlowController : MonoBehaviour
    {
        [Serializable]
        public class Ribbon
        {
            public Renderer renderer;
            [Tooltip("Flow phase offset so the gradient reads continuous across path joins.")]
            public float phaseOffset;
            [Tooltip("Gradient cycles along this ribbon; set proportional to length for a uniform flow.")]
            public float repeat = 1f;
        }

        [Header("References")]
        [SerializeField] XRSimpleInteractable interactable;
        [Tooltip("Only this group eases forward on hover (button outline + label + glyph).")]
        [SerializeField] Transform buttonGroup;
        [Tooltip("The play triangle - pops smoothly forward while the START button is pressed.")]
        [SerializeField] Transform playIcon;

        [Header("Neon ribbons")]
        [SerializeField] Ribbon[] ribbons = Array.Empty<Ribbon>();

        [Header("Glow")]
        [SerializeField, Min(0f)] float idleGlow = 1.0f;
        [SerializeField, Min(0f)] float hoverGlow = 2.3f;

        [Header("Button push")]
        [Tooltip("How far the button eases toward the user on hover (metres). The panel front faces -Z local.")]
        [SerializeField, Min(0f)] float hoverProtrude = 0.006f;

        [Header("Play triangle depth")]
        [Tooltip("Resting depth of the play triangle (local Z). More negative = further OFF the panel toward you; " +
                 "less negative / higher = flush against the panel. Adjust this to sit it right on the surface.")]
        [SerializeField] float iconLocalZ = -0.02f;
        [Tooltip("How far the play triangle pops toward the user while the button is pressed (metres).")]
        [SerializeField, Min(0f)] float iconPopDistance = 0.03f;
        [Tooltip("Ease time for the icon pop in/out (s).")]
        [SerializeField, Min(0f)] float pressSmoothTime = 0.12f;

        [Header("Smoothing (s)")]
        [SerializeField, Min(0f)] float hoverSmoothTime = 0.09f;

        static readonly int GlowId = Shader.PropertyToID("_Glow");
        static readonly int PhaseOffsetId = Shader.PropertyToID("_PhaseOffset");
        static readonly int RepeatId = Shader.PropertyToID("_Repeat");

        MaterialPropertyBlock mpb;
        bool hovered;
        float hoverAmount;
        float buttonBaseZ;
        bool pressed;
        float pressAmount;
        // buttonBaseZ is captured on the FIRST LateUpdate (the builder assigns buttonGroup after AddComponent,
        // so it is null during Awake/OnEnable). The play triangle's depth is the serialized iconLocalZ instead.
        bool basesCaptured;

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
        }

        void CaptureBases()
        {
            if (buttonGroup != null)
                buttonBaseZ = buttonGroup.localPosition.z;
            basesCaptured = true;
        }

        void OnValidate()
        {
            // Live preview: dragging iconLocalZ in the Inspector moves the triangle immediately.
            if (playIcon != null)
            {
                var p = playIcon.localPosition;
                p.z = iconLocalZ;
                playIcon.localPosition = p;
            }
        }

        void OnEnable()
        {
            if (mpb == null)
                mpb = new MaterialPropertyBlock();
            basesCaptured = false; // re-capture authored base on the next LateUpdate

            if (Application.isPlaying && interactable != null)
            {
                interactable.hoverEntered.AddListener(OnHoverEntered);
                interactable.hoverExited.AddListener(OnHoverExited);
                interactable.selectEntered.AddListener(OnSelectEntered);
                interactable.selectExited.AddListener(OnSelectExited);
            }

            ApplyGlow(idleGlow); // ensure ribbons are lit immediately (edit mode / first frame)
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

            hovered = pressed = false;
            hoverAmount = pressAmount = 0f;
            ApplyGlow(idleGlow);
            if (basesCaptured && buttonGroup != null) // only reset if we captured the authored base (avoid snapping to 0)
            {
                var p = buttonGroup.localPosition;
                p.z = buttonBaseZ;
                buttonGroup.localPosition = p;
            }
            if (playIcon != null)
            {
                var p = playIcon.localPosition;
                p.z = iconLocalZ;
                playIcon.localPosition = p;
            }
        }

        // Front faces -Z local, so "toward the user" is the -Z direction.
        void OnHoverEntered(HoverEnterEventArgs a) => hovered = true;
        void OnHoverExited(HoverExitEventArgs a) => hovered = false;
        void OnSelectEntered(SelectEnterEventArgs a) => pressed = true;
        void OnSelectExited(SelectExitEventArgs a) => pressed = false;

        void LateUpdate()
        {
            if (!basesCaptured)
                CaptureBases(); // captures the authored z before we ever offset it

            float dt = Mathf.Max(Application.isPlaying ? Time.deltaTime : 0.016f, 1e-5f);
            float s = hoverSmoothTime <= 0f ? 1f : 1f - Mathf.Exp(-dt / hoverSmoothTime);
            hoverAmount = Mathf.Lerp(hoverAmount, hovered ? 1f : 0f, s);

            ApplyGlow(Mathf.Lerp(idleGlow, hoverGlow, hoverAmount));

            if (buttonGroup != null)
            {
                var p = buttonGroup.localPosition;
                p.z = buttonBaseZ - hoverAmount * hoverProtrude; // -Z = toward the user
                buttonGroup.localPosition = p;
            }

            // Press the button -> the play triangle pops smoothly toward the user, then eases back.
            float sp = pressSmoothTime <= 0f ? 1f : 1f - Mathf.Exp(-dt / pressSmoothTime);
            pressAmount = Mathf.Lerp(pressAmount, pressed ? 1f : 0f, sp);
            if (playIcon != null)
            {
                var p = playIcon.localPosition;
                p.z = iconLocalZ - pressAmount * iconPopDistance; // rest depth (Inspector) minus the pop
                playIcon.localPosition = p;
            }
        }

        void ApplyGlow(float glow)
        {
            if (mpb == null)
                mpb = new MaterialPropertyBlock();
            if (ribbons == null)
                return;

            foreach (var r in ribbons)
            {
                if (r == null || r.renderer == null)
                    continue;
                r.renderer.GetPropertyBlock(mpb);
                mpb.SetFloat(GlowId, glow);
                mpb.SetFloat(PhaseOffsetId, r.phaseOffset);
                mpb.SetFloat(RepeatId, r.repeat);
                r.renderer.SetPropertyBlock(mpb);
            }
        }
    }
}
