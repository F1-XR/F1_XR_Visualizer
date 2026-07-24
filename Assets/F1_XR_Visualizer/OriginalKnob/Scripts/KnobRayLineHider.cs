using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace F1XR.OriginalKnob
{
    /// <summary>
    /// While the knob is selected, hides the grabbing controller's ray line so it does not draw over
    /// the knob and glow ring. It disables the interactor's line visual behaviour and its LineRenderer
    /// for the duration of the grab, then restores exactly what it disabled on release. Scoped to this
    /// knob only - it touches the interactor at runtime and never edits the shared rig asset.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KnobRayLineHider : MonoBehaviour
    {
        [SerializeField] XRSimpleInteractable interactable;

        readonly List<Behaviour> disabledBehaviours = new List<Behaviour>();
        readonly List<LineRenderer> disabledLines = new List<LineRenderer>();
        IXRSelectInteractor activeInteractor;

        void Awake()
        {
            if (interactable == null)
                interactable = GetComponentInChildren<XRSimpleInteractable>();
        }

        void OnEnable()
        {
            if (interactable == null)
                return;
            interactable.selectEntered.AddListener(OnSelectEntered);
            interactable.selectExited.AddListener(OnSelectExited);
        }

        void OnDisable()
        {
            if (interactable != null)
            {
                interactable.selectEntered.RemoveListener(OnSelectEntered);
                interactable.selectExited.RemoveListener(OnSelectExited);
            }
            Restore();
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (activeInteractor != null)
                return;

            activeInteractor = args.interactorObject;
            var interactorTransform = activeInteractor.transform;
            if (interactorTransform == null)
                return;

            // Line visual behaviours (e.g. XRInteractorLineVisual) drive the LineRenderer, so disable
            // both: the behaviour to stop it re-enabling the line, and the LineRenderer itself.
            foreach (var b in interactorTransform.GetComponentsInChildren<Behaviour>(true))
            {
                if (b != null && b.enabled && b.GetType().Name.Contains("LineVisual"))
                {
                    b.enabled = false;
                    disabledBehaviours.Add(b);
                }
            }

            foreach (var lr in interactorTransform.GetComponentsInChildren<LineRenderer>(true))
            {
                if (lr != null && lr.enabled)
                {
                    lr.enabled = false;
                    disabledLines.Add(lr);
                }
            }
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            if (args.interactorObject == activeInteractor)
                Restore();
        }

        void Restore()
        {
            foreach (var b in disabledBehaviours)
                if (b != null)
                    b.enabled = true;
            foreach (var lr in disabledLines)
                if (lr != null)
                    lr.enabled = true;

            disabledBehaviours.Clear();
            disabledLines.Clear();
            activeInteractor = null;
        }
    }
}
