using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace F1XR.AR
{
    public sealed class FerrariIntroMove : MonoBehaviour
    {
        [SerializeField] Vector3 startLocalPosition = new(0f, -0.5f, 50f);
        [SerializeField] Vector3 endLocalPosition = new(0f, -0.5f, 9f);
        [SerializeField] float duration = 2f;
        [SerializeField] bool playOnEnable = true;
        [SerializeField] float startDelay = 2f;
        [SerializeField] GameObject frontLeftTyre;
        [SerializeField, Range(0f, 1f)] float frontLeftTyreGhostAlpha = 0.35f;
        [SerializeField] float frontLeftTyreGhostFadeDuration = 0.4f;
        [SerializeField] ParticleSystem frontLeftTyrePuff;
        [SerializeField] XRSocketInteractor frontLeftTyreSocket;
        [SerializeField] float tiltDelay = 1.5f;
        [SerializeField] Vector3 tiltLocalEuler = new(3f, 0f, 4f);
        [SerializeField] float tiltDuration = 0.6f;
        [SerializeField] Ease tiltEase = Ease.OutQuad;
        [SerializeField] float riseDuration = 0.6f;
        [SerializeField] Ease riseEase = Ease.OutQuad;
        [SerializeField] float exitDelay = 2f;
        [SerializeField] Vector3 exitStartLocalPosition = new(0f, -0.55f, 9f);
        [SerializeField] Vector3 exitEndLocalPosition = new(0f, -0.55f, -20f);
        [SerializeField] float exitDuration = 2f;
        [SerializeField] AudioSource frontLeftTyrePuffAudio;
        [SerializeField] AudioSource introMoveAudio;
        [SerializeField] AudioSource wheelMountedAudio;
        [SerializeField] AudioSource exitMoveAudio;
        [SerializeField] string sessionSceneName = "SessionSpace";

        Tween moveTween;
        Tween tiltTween;
        Tween tiltDelayTween;
        Tween exitDelayTween;
        Tween exitMoveTween;
        Tween startDelayTween;
        Tween ghostFadeTween;
        GameObject socketedWheel;
        Material[] frontLeftTyreGhostMaterials;

        void OnEnable()
        {
            if (frontLeftTyreSocket != null)
            {
                frontLeftTyreSocket.selectEntered.AddListener(OnWheelSocketed);
                frontLeftTyreSocket.selectExited.AddListener(OnWheelUnsocketed);
                frontLeftTyreSocket.hoverEntered.AddListener(OnSocketHoverEntered);
                frontLeftTyreSocket.hoverExited.AddListener(OnSocketHoverExited);
            }

            if (playOnEnable)
            {
                startDelayTween?.Kill();
                startDelayTween = DOVirtual.DelayedCall(startDelay, Play, false);
            }
        }

        void OnDisable()
        {
            if (frontLeftTyreSocket != null)
            {
                frontLeftTyreSocket.selectEntered.RemoveListener(OnWheelSocketed);
                frontLeftTyreSocket.selectExited.RemoveListener(OnWheelUnsocketed);
                frontLeftTyreSocket.hoverEntered.RemoveListener(OnSocketHoverEntered);
                frontLeftTyreSocket.hoverExited.RemoveListener(OnSocketHoverExited);
            }

            startDelayTween?.Kill();
            moveTween?.Kill();
            tiltTween?.Kill();
            tiltDelayTween?.Kill();
            exitDelayTween?.Kill();
            exitMoveTween?.Kill();
            ghostFadeTween?.Kill();
        }

        void OnWheelSocketed(SelectEnterEventArgs args)
        {
            socketedWheel = args.interactableObject?.transform.gameObject;

            if (wheelMountedAudio != null)
                wheelMountedAudio.Play();

            SetFrontLeftTyreGhostAlpha(0f);
            Tilt(-tiltLocalEuler, riseDuration, riseEase);

            exitDelayTween?.Kill();
            exitDelayTween = DOVirtual.DelayedCall(exitDelay, () =>
            {
                transform.localPosition = exitStartLocalPosition;

                if (exitMoveAudio != null)
                    exitMoveAudio.Play();

                exitMoveTween?.Kill();
                exitMoveTween = transform.DOLocalMove(exitEndLocalPosition, exitDuration)
                    .SetEase(Ease.InOutQuart)
                    .OnComplete(() =>
                    {
                        if (exitMoveAudio != null)
                            exitMoveAudio.Stop();

                        if (socketedWheel != null)
                            socketedWheel.SetActive(false);

                        gameObject.SetActive(false);

                        SceneManager.LoadScene(sessionSceneName);
                    });
            }, false);
        }

        void OnWheelUnsocketed(SelectExitEventArgs args)
        {
            socketedWheel = null;

            exitDelayTween?.Kill();
            exitMoveTween?.Kill();

            if (exitMoveAudio != null)
                exitMoveAudio.Stop();

            SetFrontLeftTyreGhostAlpha(0f);
            Tilt(tiltLocalEuler, tiltDuration, tiltEase);
        }

        void OnSocketHoverEntered(HoverEnterEventArgs args)
        {
            if (frontLeftTyreSocket != null && frontLeftTyreSocket.hasSelection)
                return;

            SetFrontLeftTyreGhostAlpha(frontLeftTyreGhostAlpha);
        }

        void OnSocketHoverExited(HoverExitEventArgs args)
        {
            if (frontLeftTyreSocket != null && frontLeftTyreSocket.hasSelection)
                return;

            SetFrontLeftTyreGhostAlpha(0f);
        }

        void Tilt(Vector3 localEulerDelta, float animDuration, Ease ease)
        {
            tiltTween?.Kill();
            tiltTween = transform.DOLocalRotate(localEulerDelta, animDuration, RotateMode.LocalAxisAdd)
                .SetEase(ease);
        }

        void EnsureFrontLeftTyreGhostMaterials()
        {
            if (frontLeftTyreGhostMaterials != null || frontLeftTyre == null)
                return;

            var renderers = frontLeftTyre.GetComponentsInChildren<Renderer>(true);
            var materials = new System.Collections.Generic.List<Material>();

            foreach (var renderer in renderers)
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;

                foreach (var material in renderer.materials)
                {
                    if (!material.HasProperty("baseColorFactor"))
                        continue;

                    material.SetFloat("_Surface", 1f);
                    material.SetFloat("_Blend", 0f);
                    material.SetOverrideTag("RenderType", "Transparent");
                    material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                    material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                    material.SetInt("_ZWrite", 0);
                    material.renderQueue = (int)RenderQueue.Transparent;
                    material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    material.DisableKeyword("_ALPHATEST_ON");
                    material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    material.DisableKeyword("_ALPHAMODULATE_ON");

                    var color = material.GetColor("baseColorFactor");
                    color.a = 0f;
                    material.SetColor("baseColorFactor", color);

                    materials.Add(material);
                }
            }

            frontLeftTyreGhostMaterials = materials.ToArray();
        }

        void SetFrontLeftTyreGhostAlpha(float targetAlpha)
        {
            if (frontLeftTyre == null)
                return;

            EnsureFrontLeftTyreGhostMaterials();

            ghostFadeTween?.Kill();

            if (frontLeftTyreGhostMaterials == null || frontLeftTyreGhostMaterials.Length == 0)
            {
                frontLeftTyre.SetActive(targetAlpha > 0f);
                return;
            }

            frontLeftTyre.SetActive(true);
            var startAlpha = frontLeftTyreGhostMaterials[0].GetColor("baseColorFactor").a;

            ghostFadeTween = DOVirtual.Float(startAlpha, targetAlpha, frontLeftTyreGhostFadeDuration, alpha =>
            {
                foreach (var material in frontLeftTyreGhostMaterials)
                {
                    var color = material.GetColor("baseColorFactor");
                    color.a = alpha;
                    material.SetColor("baseColorFactor", color);
                }
            }).OnComplete(() =>
            {
                if (targetAlpha <= 0f)
                    frontLeftTyre.SetActive(false);
            });
        }

        public void Play()
        {
            moveTween?.Kill();
            transform.localPosition = startLocalPosition;

            if (introMoveAudio != null)
                introMoveAudio.Play();

            moveTween = transform.DOLocalMove(endLocalPosition, duration)
                .SetEase(Ease.InOutExpo)
                .OnComplete(() =>
                {
                    if (introMoveAudio != null)
                        introMoveAudio.Stop();

                    EnsureFrontLeftTyreGhostMaterials();

                    if (frontLeftTyre != null)
                        frontLeftTyre.SetActive(false);

                    if (frontLeftTyreSocket != null)
                        frontLeftTyreSocket.gameObject.SetActive(true);

                    if (frontLeftTyrePuff != null)
                    {
                        frontLeftTyrePuff.gameObject.SetActive(true);
                        frontLeftTyrePuff.Play();
                    }

                    if (frontLeftTyrePuffAudio != null)
                        frontLeftTyrePuffAudio.Play();

                    tiltDelayTween?.Kill();
                    tiltDelayTween = DOVirtual.DelayedCall(tiltDelay, () => Tilt(tiltLocalEuler, tiltDuration, tiltEase), false);
                });
        }
    }
}
