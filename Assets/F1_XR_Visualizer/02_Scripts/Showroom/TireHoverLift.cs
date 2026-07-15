using DG.Tweening;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace F1XR.Showroom
{
    [RequireComponent(typeof(XRBaseInteractable))]
    public sealed class TireHoverLift : MonoBehaviour
    {
        [SerializeField] Transform visualRoot;
        [SerializeField] float riseHeight = 1.5f;
        [SerializeField] float riseDuration = 0.2f;
        [SerializeField] Ease riseEase = Ease.OutQuad;
        [SerializeField] float lowerDuration = 0.2f;
        [SerializeField] Ease lowerEase = Ease.OutQuad;

        XRBaseInteractable interactable;
        Tween liftTween;
        float baseLocalY;
        bool liftDisabled;
        Collider[] visualRootColliders;

        void Awake()
        {
            interactable = GetComponent<XRBaseInteractable>();

            if (visualRoot == null)
                visualRoot = transform.Find("VisualRoot");

            if (visualRoot != null)
            {
                baseLocalY = visualRoot.localPosition.y;
                visualRootColliders = visualRoot.GetComponentsInChildren<Collider>(true);
            }
        }

        void OnEnable()
        {
            interactable.firstHoverEntered.AddListener(OnFirstHoverEntered);
            interactable.lastHoverExited.AddListener(OnLastHoverExited);
            interactable.selectEntered.AddListener(OnSelectEntered);
        }

        void OnDisable()
        {
            interactable.firstHoverEntered.RemoveListener(OnFirstHoverEntered);
            interactable.lastHoverExited.RemoveListener(OnLastHoverExited);
            interactable.selectEntered.RemoveListener(OnSelectEntered);

            liftTween?.Kill();
        }

        void OnFirstHoverEntered(HoverEnterEventArgs args)
        {
            if (liftDisabled)
                return;

            AnimateTo(baseLocalY + riseHeight, riseDuration, riseEase);
        }

        void OnLastHoverExited(HoverExitEventArgs args)
        {
            if (liftDisabled)
                return;

            AnimateTo(baseLocalY, lowerDuration, lowerEase);
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            liftDisabled = true;
            AnimateTo(baseLocalY, lowerDuration, lowerEase);
        }

        void AnimateTo(float targetY, float animDuration, Ease ease)
        {
            if (visualRoot == null)
                return;

            liftTween?.Kill();
            SetVisualRootCollidersEnabled(false);

            liftTween = visualRoot.DOLocalMoveY(targetY, animDuration)
                .SetEase(ease)
                .OnComplete(() =>
                {
                    SetVisualRootCollidersEnabled(true);
                });
        }

        void SetVisualRootCollidersEnabled(bool colliderEnabled)
        {
            if (visualRootColliders == null)
                return;

            foreach (Collider col in visualRootColliders)
            {
                if (col != null)
                    col.enabled = colliderEnabled;
            }
        }
    }
}
