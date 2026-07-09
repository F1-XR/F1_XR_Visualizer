using DG.Tweening;
using UnityEngine;
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

        Tween moveTween;
        Tween tiltTween;
        Tween tiltDelayTween;
        Tween exitDelayTween;
        Tween exitMoveTween;
        Tween startDelayTween;
        GameObject socketedWheel;

        void OnEnable()
        {
            if (frontLeftTyreSocket != null)
            {
                frontLeftTyreSocket.selectEntered.AddListener(OnWheelSocketed);
                frontLeftTyreSocket.selectExited.AddListener(OnWheelUnsocketed);
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
            }

            startDelayTween?.Kill();
            moveTween?.Kill();
            tiltTween?.Kill();
            tiltDelayTween?.Kill();
            exitDelayTween?.Kill();
            exitMoveTween?.Kill();
        }

        void OnWheelSocketed(SelectEnterEventArgs args)
        {
            socketedWheel = args.interactableObject?.transform.gameObject;

            if (wheelMountedAudio != null)
                wheelMountedAudio.Play();

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

            Tilt(tiltLocalEuler, tiltDuration, tiltEase);
        }

        void Tilt(Vector3 localEulerDelta, float animDuration, Ease ease)
        {
            tiltTween?.Kill();
            tiltTween = transform.DOLocalRotate(localEulerDelta, animDuration, RotateMode.LocalAxisAdd)
                .SetEase(ease);
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
