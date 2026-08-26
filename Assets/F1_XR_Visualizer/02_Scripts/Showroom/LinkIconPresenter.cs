using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace F1XR.Showroom
{
    /// <summary>
    /// Presentation for a flat link icon: keeps it turned towards the player, and on hover swells it
    /// slightly and shows a caption above it. Purely visual - the actual link lives on
    /// <see cref="OpenUrlOnTrigger"/>.
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public sealed class LinkIconPresenter : MonoBehaviour
    {
        [Header("Billboard")]
        [Tooltip("Head to turn towards. Left empty, Camera.main is used.")]
        [SerializeField] Transform target;

        [Tooltip("Keep the icon upright instead of rolling with the head.")]
        [SerializeField] bool keepUpright = true;

        [Tooltip("Flip if the icon ends up showing its back.")]
        [SerializeField] bool faceAway;

        [Header("Hover")]
        [Tooltip("Size on hover, relative to the resting size.")]
        [SerializeField, Min(1f)] float hoverScale = 1.18f;

        [Tooltip("How quickly it settles into the hovered size.")]
        [SerializeField, Min(0.01f)] float scaleSpeed = 12f;

        [Tooltip("Caption shown while hovered, e.g. the [Link] label.")]
        [SerializeField] GameObject caption;

        XRSimpleInteractable interactable;
        Vector3 baseScale;
        int hoverCount;

        void Awake()
        {
            interactable = GetComponent<XRSimpleInteractable>();
            baseScale = transform.localScale;
            if (!target && Camera.main) target = Camera.main.transform;
            if (caption) caption.SetActive(false);
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
            hoverCount = 0;
            transform.localScale = baseScale;
            if (caption) caption.SetActive(false);
        }

        void OnHoverEntered(HoverEnterEventArgs args)
        {
            hoverCount++;
            if (caption) caption.SetActive(true);
        }

        void OnHoverExited(HoverExitEventArgs args)
        {
            hoverCount = Mathf.Max(0, hoverCount - 1);
            if (hoverCount == 0 && caption) caption.SetActive(false);
        }

        void LateUpdate()
        {
            if (!target && Camera.main) target = Camera.main.transform;

            if (target)
            {
                Vector3 away = transform.position - target.position;
                if (keepUpright) away.y = 0f;
                if (away.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(faceAway ? -away : away, Vector3.up);
            }

            Vector3 wanted = hoverCount > 0 ? baseScale * hoverScale : baseScale;
            transform.localScale = Vector3.Lerp(transform.localScale, wanted, 1f - Mathf.Exp(-scaleSpeed * Time.deltaTime));
        }
    }
}
