using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace F1XR.Showroom
{
    /// <summary>
    /// Aim at the pull-tab collider and pull the trigger to "pop" a beer can open:
    /// the tab flips up at its hinge, a CO2 mist sprays, and a hiss plays.
    /// Lives on the CanTab child (its transform origin is the tab hinge).
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public sealed class CanOpener : MonoBehaviour
    {
        [Tooltip("Transform actually rotated. Leave empty to rotate this object.")]
        [SerializeField] Transform hinge;

        [Header("Tab flip (rotates about local Z at the hinge)")]
        [SerializeField] float openAngle = 20f;
        [SerializeField] float popDuration = 0.18f;
        [SerializeField] Ease popEase = Ease.OutBack;
        [SerializeField] float closeDuration = 0.15f;

        [Header("FX")]
        [SerializeField] ParticleSystem mist;
        [SerializeField] AudioSource hiss;
        [SerializeField] Transform foamCluster;
        [SerializeField] float foamGrowDuration = 0.9f;

        [Header("Reveal")]
        [Tooltip("Hidden while closed: the can body still carries its own tab, so showing both would z-fight.")]
        [SerializeField] Renderer tabRenderer;
        [Tooltip("The dark drink opening, shown once the tab pops.")]
        [SerializeField] GameObject mouth;

        [Header("Lid swap")]
        [Tooltip("The can body. Its lid mesh is swapped so the tab bump flattens and the mouth opens.")]
        [SerializeField] MeshFilter bodyFilter;
        [SerializeField] UnityEngine.Mesh closedLidMesh;
        [SerializeField] UnityEngine.Mesh openLidMesh;

        [Tooltip("If true, selecting again re-closes the tab. Off = opens once.")]
        [SerializeField] bool toggle = true;

        [Header("Input")]
        [Tooltip("Pop on the trigger while the tab is hovered. Select stays on the grip scene-wide (world " +
                 "grabs), so the trigger is read straight off the hovering interactor; grip must not pop " +
                 "the can or grabbing it by the body would open it by accident.")]
        [SerializeField] bool triggerWhileHovering = true;

        readonly List<IXRHoverInteractor> hovering = new();
        XRSimpleInteractable interactable;
        Tween tween;
        Tween foamTween;
        Quaternion baseRot;
        Vector3 foamScale;
        bool opened;

        void Awake()
        {
            interactable = GetComponent<XRSimpleInteractable>();
            if (tabRenderer == null)
                tabRenderer = GetComponent<Renderer>();
            if (hiss != null && hiss.clip == null)
                hiss.clip = BuildHiss();

            if (hinge == null)
                hinge = transform;
            // the FBX axis conversion leaves a base rotation on the piece: lift on top of it
            baseRot = hinge.localRotation;
            if (foamCluster != null)
            {
                foamScale = foamCluster.localScale;
                foamCluster.localScale = Vector3.zero;
                foamCluster.gameObject.SetActive(false);
            }
            SetRevealed(false);
        }

        void OnEnable()
        {
            interactable.hoverEntered.AddListener(OnHoverEntered);
            interactable.hoverExited.AddListener(OnHoverExited);
        }

        void OnDisable()
        {
            interactable.hoverEntered.RemoveListener(OnHoverEntered);
            interactable.hoverExited.RemoveListener(OnHoverExited);
            hovering.Clear();
            tween?.Kill();
            foamTween?.Kill();
            if (mist != null)
                mist.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (foamCluster != null)
            {
                foamCluster.localScale = Vector3.zero;
                foamCluster.gameObject.SetActive(false);
            }
        }

        void Update()
        {
            if (!triggerWhileHovering || hovering.Count == 0)
                return;

            for (int i = 0; i < hovering.Count; i++)
            {
                // Hand-tracking interactors carry no button reader: they keep their pinch-select.
                if (hovering[i] is XRBaseInputInteractor input &&
                    input.activateInput != null &&
                    input.activateInput.ReadWasPerformedThisFrame())
                {
                    Toggle();
                    return;
                }
            }
        }

        void OnHoverEntered(HoverEnterEventArgs args)
        {
            if (!hovering.Contains(args.interactorObject))
                hovering.Add(args.interactorObject);
        }

        void OnHoverExited(HoverExitEventArgs args) => hovering.Remove(args.interactorObject);

        void Toggle()
        {
            if (opened && !toggle)
                return;
            Set(!opened);
        }

        public void Open() => Set(true);
        public void Close() => Set(false);

        void Set(bool open)
        {
            opened = open;
            tween?.Kill();
            foamTween?.Kill();

            if (open)
                SetRevealed(true);

            Quaternion target = open
                ? Quaternion.Euler(0f, 0f, -openAngle) * baseRot
                : baseRot;
            tween = hinge.DOLocalRotateQuaternion(target, open ? popDuration : closeDuration)
                    .SetEase(open ? popEase : Ease.OutQuad);

            if (open)
            {
                if (foamCluster != null)
                {
                    foamCluster.gameObject.SetActive(true);
                    foamCluster.localScale = Vector3.zero;
                    foamTween = foamCluster.DOScale(foamScale, foamGrowDuration)
                            .SetEase(Ease.OutCubic);
                }
                if (mist != null) mist.Play(true);
                if (hiss != null) hiss.Play();
            }
            else
            {
                if (foamCluster != null)
                {
                    foamTween = foamCluster.DOScale(Vector3.zero, closeDuration)
                            .SetEase(Ease.InQuad)
                            .OnComplete(() => foamCluster.gameObject.SetActive(false));
                }
                if (mist != null)
                    mist.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                tween.OnComplete(() => SetRevealed(false));
            }
        }

        void SetRevealed(bool revealed)
        {
            if (tabRenderer != null) tabRenderer.enabled = revealed;
            if (mouth != null) mouth.SetActive(revealed);

            if (bodyFilter != null)
            {
                var lid = revealed ? openLidMesh : closedLidMesh;
                if (lid != null) bodyFilter.sharedMesh = lid;
            }
        }

        // Procedural "psshht": fast-attack, decaying low-passed noise. No audio asset needed.
        static AudioClip BuildHiss()
        {
            const int sr = 44100;
            const float dur = 0.6f;
            int n = (int)(sr * dur);
            var data = new float[n];
            float last = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float env = Mathf.Exp(-t * 4f) * Mathf.Min(1f, t * 60f);
                float white = Random.value * 2f - 1f;
                last = Mathf.Lerp(last, white, 0.35f);
                data[i] = (white * 0.6f + last * 0.4f) * env * 0.5f;
            }
            var clip = AudioClip.Create("CanHiss", n, 1, sr, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
