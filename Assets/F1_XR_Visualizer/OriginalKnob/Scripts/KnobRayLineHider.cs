using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

namespace F1XR.OriginalKnob
{
    /// <summary>
    /// Keeps an interactor's ray from locking onto the knob centre.
    /// - While HOVERING the knob: the line stays visible but its endpoint snap is turned off, so the ray
    ///   lands on the knob surface (aim anywhere) instead of sucking into the centre.
    /// - While SELECTING (grabbing) the knob: the line is hidden entirely, so the attach anchor at the knob
    ///   centre is never shown. Rotation is driven by sweeping the controller, so the ray isn't needed then.
    /// Everything is restored to its original state once the interactor stops hovering and selecting the
    /// knob. Runtime-only, scoped to this knob - it never edits the shared rig asset.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KnobRayLineHider : MonoBehaviour
    {
        [SerializeField] XRSimpleInteractable interactable;

        class VisualEntry
        {
            public XRInteractorLineVisual visual;
            public bool origSnap;
            public bool origEnabled;
        }

        class LineEntry
        {
            public LineRenderer line;
            public bool origEnabled;
        }

        class Captured
        {
            public readonly List<VisualEntry> visuals = new List<VisualEntry>();
            public readonly List<LineEntry> lines = new List<LineEntry>();
        }

        readonly Dictionary<Transform, Captured> map = new Dictionary<Transform, Captured>();

        void Awake()
        {
            if (interactable == null)
                interactable = GetComponentInChildren<XRSimpleInteractable>();
        }

        void OnEnable()
        {
            if (interactable == null)
                return;
            interactable.hoverEntered.AddListener(OnHoverEntered);
            interactable.hoverExited.AddListener(OnHoverExited);
            interactable.selectEntered.AddListener(OnSelectEntered);
            interactable.selectExited.AddListener(OnSelectExited);
        }

        void OnDisable()
        {
            if (interactable != null)
            {
                interactable.hoverEntered.RemoveListener(OnHoverEntered);
                interactable.hoverExited.RemoveListener(OnHoverExited);
                interactable.selectEntered.RemoveListener(OnSelectEntered);
                interactable.selectExited.RemoveListener(OnSelectExited);
            }
            RestoreAll();
        }

        void OnHoverEntered(HoverEnterEventArgs a) => ApplyState(a.interactorObject?.transform);
        void OnHoverExited(HoverExitEventArgs a) => ApplyState(a.interactorObject?.transform);
        void OnSelectEntered(SelectEnterEventArgs a) => ApplyState(a.interactorObject?.transform);
        void OnSelectExited(SelectExitEventArgs a) => ApplyState(a.interactorObject?.transform);

        void ApplyState(Transform interactorTransform)
        {
            if (interactorTransform == null)
                return;

            bool selected = IsSelecting(interactorTransform);
            bool hovered = IsHovering(interactorTransform);

            if (!selected && !hovered)
            {
                Restore(interactorTransform);
                return;
            }

            if (!map.TryGetValue(interactorTransform, out var cap))
            {
                cap = Capture(interactorTransform);
                map[interactorTransform] = cap;
            }

            // Hovering -> visible, no snap. Selecting -> hidden.
            foreach (var ve in cap.visuals)
            {
                if (ve.visual == null)
                    continue;
                ve.visual.snapEndpointIfAvailable = false;
                ve.visual.enabled = !selected;
            }
            foreach (var le in cap.lines)
            {
                if (le.line != null)
                    le.line.enabled = !selected;
            }
        }

        Captured Capture(Transform interactorTransform)
        {
            var cap = new Captured();
            foreach (var v in interactorTransform.GetComponentsInChildren<XRInteractorLineVisual>(true))
            {
                if (v != null)
                    cap.visuals.Add(new VisualEntry { visual = v, origSnap = v.snapEndpointIfAvailable, origEnabled = v.enabled });
            }
            foreach (var lr in interactorTransform.GetComponentsInChildren<LineRenderer>(true))
            {
                if (lr != null)
                    cap.lines.Add(new LineEntry { line = lr, origEnabled = lr.enabled });
            }
            return cap;
        }

        bool IsSelecting(Transform interactorTransform)
        {
            if (interactable == null)
                return false;
            foreach (var s in interactable.interactorsSelecting)
                if (s != null && s.transform == interactorTransform)
                    return true;
            return false;
        }

        bool IsHovering(Transform interactorTransform)
        {
            if (interactable == null)
                return false;
            foreach (var h in interactable.interactorsHovering)
                if (h != null && h.transform == interactorTransform)
                    return true;
            return false;
        }

        void Restore(Transform interactorTransform)
        {
            if (interactorTransform == null || !map.TryGetValue(interactorTransform, out var cap))
                return;

            foreach (var ve in cap.visuals)
            {
                if (ve.visual == null)
                    continue;
                ve.visual.snapEndpointIfAvailable = ve.origSnap;
                ve.visual.enabled = ve.origEnabled;
            }
            foreach (var le in cap.lines)
            {
                if (le.line != null)
                    le.line.enabled = le.origEnabled;
            }

            map.Remove(interactorTransform);
        }

        void RestoreAll()
        {
            foreach (var kv in map)
            {
                foreach (var ve in kv.Value.visuals)
                {
                    if (ve.visual == null)
                        continue;
                    ve.visual.snapEndpointIfAvailable = ve.origSnap;
                    ve.visual.enabled = ve.origEnabled;
                }
                foreach (var le in kv.Value.lines)
                {
                    if (le.line != null)
                        le.line.enabled = le.origEnabled;
                }
            }
            map.Clear();
        }
    }
}
