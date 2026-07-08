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
        [SerializeField] GameObject frontLeftTyre;
        [SerializeField] ParticleSystem frontLeftTyrePuff;
        [SerializeField] XRSocketInteractor frontLeftTyreSocket;
        [SerializeField] float tiltDelay = 1.5f;
        [SerializeField] Vector3 tiltLocalEuler = new(3f, 0f, 4f);
        [SerializeField] float tiltDuration = 0.6f;
        [SerializeField] Ease tiltEase = Ease.OutQuad;
        [SerializeField] float riseDuration = 0.6f;
        [SerializeField] Ease riseEase = Ease.OutQuad;

        Tween moveTween;
        Tween tiltTween;
        Tween tiltDelayTween;

        void OnEnable()
        {
            if (frontLeftTyreSocket != null)
            {
                frontLeftTyreSocket.selectEntered.AddListener(OnWheelSocketed);
                frontLeftTyreSocket.selectExited.AddListener(OnWheelUnsocketed);
            }

            if (playOnEnable)
                Play();
        }

        void OnDisable()
        {
            if (frontLeftTyreSocket != null)
            {
                frontLeftTyreSocket.selectEntered.RemoveListener(OnWheelSocketed);
                frontLeftTyreSocket.selectExited.RemoveListener(OnWheelUnsocketed);
            }

            moveTween?.Kill();
            tiltTween?.Kill();
            tiltDelayTween?.Kill();
        }

        void OnWheelSocketed(SelectEnterEventArgs args) => Tilt(-tiltLocalEuler, riseDuration, riseEase);

        void OnWheelUnsocketed(SelectExitEventArgs args) => Tilt(tiltLocalEuler, tiltDuration, tiltEase);

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
            moveTween = transform.DOLocalMove(endLocalPosition, duration)
                .SetEase(Ease.InOutExpo)
                .OnComplete(() =>
                {
                    if (frontLeftTyre != null)
                        frontLeftTyre.SetActive(false);

                    if (frontLeftTyreSocket != null)
                        frontLeftTyreSocket.gameObject.SetActive(true);

                    if (frontLeftTyrePuff != null)
                    {
                        frontLeftTyrePuff.gameObject.SetActive(true);
                        frontLeftTyrePuff.Play();
                    }

                    tiltDelayTween?.Kill();
                    tiltDelayTween = DOVirtual.DelayedCall(tiltDelay, () => Tilt(tiltLocalEuler, tiltDuration, tiltEase));
                });
        }
    }
}
