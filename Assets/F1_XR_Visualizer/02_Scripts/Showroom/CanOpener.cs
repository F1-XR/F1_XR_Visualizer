using DG.Tweening;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace F1XR.Showroom
{
    /// <summary>
    /// Grab/select the pull-tab collider to "pop" a beer can open:
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

        XRSimpleInteractable interactable;
        Tween tween;
        Quaternion baseRot;
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
            SetRevealed(false);
        }

        void OnEnable() => interactable.selectEntered.AddListener(OnSelect);

        void OnDisable()
        {
            interactable.selectEntered.RemoveListener(OnSelect);
            tween?.Kill();
        }

        void OnSelect(SelectEnterEventArgs _)
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

            if (open)
                SetRevealed(true);

            Quaternion target = open
                ? Quaternion.Euler(0f, 0f, -openAngle) * baseRot
                : baseRot;
            tween = hinge.DOLocalRotateQuaternion(target, open ? popDuration : closeDuration)
                    .SetEase(open ? popEase : Ease.OutQuad);

            if (open)
            {
                if (mist != null) mist.Play();
                if (hiss != null) hiss.Play();
            }
            else
            {
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
