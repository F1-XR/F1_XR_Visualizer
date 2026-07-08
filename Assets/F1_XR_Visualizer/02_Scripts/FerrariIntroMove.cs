using DG.Tweening;
using UnityEngine;

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
        [SerializeField] float tiltDelay = 1.5f;
        [SerializeField] Vector3 tiltLocalEuler = new(3f, 0f, 4f);
        [SerializeField] float tiltDuration = 0.6f;
        [SerializeField] Ease tiltEase = Ease.OutQuad;

        Tween moveTween;
        Tween tiltTween;
        Tween tiltDelayTween;

        void OnEnable()
        {
            if (playOnEnable)
                Play();
        }

        void OnDisable()
        {
            moveTween?.Kill();
            tiltTween?.Kill();
            tiltDelayTween?.Kill();
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

                    if (frontLeftTyrePuff != null)
                    {
                        frontLeftTyrePuff.gameObject.SetActive(true);
                        frontLeftTyrePuff.Play();
                    }

                    tiltDelayTween?.Kill();
                    tiltDelayTween = DOVirtual.DelayedCall(tiltDelay, () =>
                    {
                        tiltTween?.Kill();
                        tiltTween = transform.DOLocalRotate(tiltLocalEuler, tiltDuration, RotateMode.LocalAxisAdd)
                            .SetEase(tiltEase);
                    });
                });
        }
    }
}
