using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace F1XR.Showroom
{
    /// <summary>
    /// Opens a web page when the player pulls the trigger while pointing at this object.
    ///
    /// Select is bound to the grip scene-wide (that is what grabs the world), so the trigger has to be
    /// read straight off the hovering interactor rather than through selectEntered - the same approach
    /// <see cref="CanOpener"/> uses for the pull-tab.
    ///
    /// On Quest the link surfaces in the system browser on top of the running app, so treat following
    /// it as leaving the scene.
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public sealed class OpenUrlOnTrigger : MonoBehaviour
    {
        [Tooltip("Page to open. Must start with http:// or https://.")]
        [SerializeField, TextArea(3, 6)] string url;

        [Tooltip("Ignores repeat presses for this long, so one pull cannot open several tabs.")]
        [SerializeField, Min(0f)] float retriggerDelay = 1.5f;

        [Tooltip("Log hover and open events while dialling this in.")]
        [SerializeField] bool logEvents = true;

        readonly List<IXRHoverInteractor> hovering = new();
        XRSimpleInteractable interactable;
        float nextAllowedTime;

        void Awake() => interactable = GetComponent<XRSimpleInteractable>();

        void OnEnable()
        {
            interactable.hoverEntered.AddListener(OnHoverEntered);
            interactable.hoverExited.AddListener(OnHoverExited);
            // Select is on the grip, but keep it working as a fallback for hand tracking,
            // whose pinch has no separate trigger to read.
            interactable.selectEntered.AddListener(OnSelectEntered);
        }

        void OnDisable()
        {
            interactable.hoverEntered.RemoveListener(OnHoverEntered);
            interactable.hoverExited.RemoveListener(OnHoverExited);
            interactable.selectEntered.RemoveListener(OnSelectEntered);
            hovering.Clear();
        }

        void OnHoverEntered(HoverEnterEventArgs args)
        {
            if (!hovering.Contains(args.interactorObject))
                hovering.Add(args.interactorObject);
            if (logEvents) Debug.Log("[OpenUrl] Hover entered", this);
        }

        void OnHoverExited(HoverExitEventArgs args) => hovering.Remove(args.interactorObject);

        void OnSelectEntered(SelectEnterEventArgs args) => TryOpen();

        void Update()
        {
            if (hovering.Count == 0)
                return;

            for (int i = 0; i < hovering.Count; i++)
            {
                // Hand-tracking interactors carry no button reader; they come through selectEntered.
                if (hovering[i] is XRBaseInputInteractor input &&
                    input.activateInput != null &&
                    input.activateInput.ReadWasPerformedThisFrame())
                {
                    TryOpen();
                    return;
                }
            }
        }

        void TryOpen()
        {
            if (Time.unscaledTime < nextAllowedTime)
                return;

            if (string.IsNullOrWhiteSpace(url))
            {
                Debug.LogWarning("[OpenUrl] No URL set.", this);
                return;
            }

            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            {
                Debug.LogWarning($"[OpenUrl] Refusing to open '{url}': not an http(s) address.", this);
                return;
            }

            nextAllowedTime = Time.unscaledTime + retriggerDelay;
            if (logEvents) Debug.Log("[OpenUrl] Opening link", this);
            Application.OpenURL(url);
        }
    }
}
