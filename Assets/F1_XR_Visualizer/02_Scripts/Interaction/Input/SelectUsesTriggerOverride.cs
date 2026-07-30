using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace F1XR.Interaction.Input
{
    /// <summary>
    /// Scene-local override that makes the ray interactors' Select fire on the TRIGGER instead of the
    /// grip. The shared XR Origin prefab binds Select to the grip so world objects (e.g. tyres) are
    /// grabbed with grip, but this scene wants the trigger to drive OriginalUI's edge-move and
    /// corner-resize, which are select-driven (see PanelEdgeGrab / PanelCornerResize).
    ///
    /// Rather than fight the shared prefab, each interactor's <c>selectInput</c> is pointed at its own
    /// <c>activateInput</c> reference, which is already bound to the trigger. This is a per-hand-correct
    /// revert to the pre-grip behaviour and leaves the grip free for PanelEdgeGrab's grip-grab-anywhere.
    /// Only the controller interactors are touched; the hand-rig interactors keep their grasp/pinch
    /// select so hand tracking still works.
    /// </summary>
    public sealed class SelectUsesTriggerOverride : MonoBehaviour
    {
        [Tooltip("Interactors to retarget. Leave empty to auto-find the controller ray interactors.")]
        [SerializeField] List<XRBaseInputInteractor> interactors = new();
        [Tooltip("When no interactors are assigned, auto-find the ray interactors in the scene.")]
        [SerializeField] bool autoFind = true;
        [Tooltip("Hand-tracking interactors are skipped by default so their grasp/pinch select keeps working.")]
        [SerializeField] bool includeHandInteractors;
        [Tooltip("Name of the hand-rig root used to recognise (and skip) hand-tracking interactors.")]
        [SerializeField] string handRigRootName = "HandVisualizer";

        void OnEnable()
        {
            Apply();
        }

        void Start()
        {
            // Re-apply once every interactor has finished its own OnEnable, in case script execution
            // order ran this component first.
            Apply();
        }

        void Apply()
        {
            if (autoFind && interactors.Count == 0)
                CollectInteractors();

            foreach (var interactor in interactors)
            {
                if (interactor == null)
                    continue;

                var select = interactor.selectInput;
                var activate = interactor.activateInput;
                if (select == null || activate == null)
                    continue;

                select.inputActionReferencePerformed = activate.inputActionReferencePerformed;
                select.inputActionReferenceValue = activate.inputActionReferenceValue;
            }
        }

        void CollectInteractors()
        {
            var found = FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var interactor in found)
            {
                if (!includeHandInteractors && IsUnderHandRig(interactor.transform))
                    continue;

                if (!interactors.Contains(interactor))
                    interactors.Add(interactor);
            }
        }

        bool IsUnderHandRig(Transform node)
        {
            for (var current = node; current != null; current = current.parent)
            {
                if (current.name == handRigRootName)
                    return true;
            }

            return false;
        }
    }
}
