using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using F1XR.Interaction.Input;
using F1XR.Interaction.World;

namespace F1XR.UI.WorldPanel
{
    /// <summary>
    /// One-handed resize: aim at a corner mark and pull the trigger, then drag. The grab still happens
    /// through XRI (so controllers, hand pinch and the simulator all work); it is just converted from a
    /// move into a scale for as long as the corner is the thing that was grabbed. The two-handed pinch
    /// scale in <see cref="ScaleController"/> is untouched.
    /// </summary>
    public sealed class PanelCornerResize : MonoBehaviour
    {
        [SerializeField] XRGrabInteractable grab;
        [SerializeField] ScaleController scaleController;
        [SerializeField] Collider panelCollider;
        [SerializeField] PanelResizeCorners cornerVisual;

        [Header("Handles")]
        [SerializeField] float cornerSize = 70f;
        [SerializeField] float cornerDepth = 24f;
        // The corner marks are drawn outside the panel rect, so the handle has to reach past it too.
        [SerializeField] float cornerOutset = 20f;
        [SerializeField] float rayDistance = 20f;
        [SerializeField] float handNearDistance = 0.08f;
        // Center keeps the panel growing outward evenly; off pins the opposite corner instead.
        [SerializeField] bool resizeAroundCenter = true;

        static readonly List<XRBaseInputInteractor> Interactors = new();
        static readonly List<InputDevice> InputDevices = new();

        // 0 = top left, 1 = top right, 2 = bottom left, 3 = bottom right (matches PanelResizeCorners).
        static readonly Vector2[] CornerSigns =
        {
            new(-1f, 1f),
            new(1f, 1f),
            new(-1f, -1f),
            new(1f, -1f)
        };

        readonly BoxCollider[] cornerColliders = new BoxCollider[4];

        int hoveredCorner = -1;
        bool dragging;
        Plane dragPlane;
        IXRSelectInteractor dragInteractor;

        bool trackPositionWas;
        bool trackRotationWas;
        bool throwOnDetachWas;
        bool cornerCollidersRegistered;
        bool directTriggerDrag;
        bool leftTriggerWasPressed;
        bool rightTriggerWasPressed;

        public bool IsResizing => dragging;
        public bool IsCornerHovered => hoveredCorner >= 0;

        void Awake()
        {
            if (grab == null)
                grab = GetComponent<XRGrabInteractable>();

            if (scaleController == null)
                scaleController = GetComponent<ScaleController>();

            if (panelCollider == null)
                panelCollider = GetComponentInChildren<Collider>();

            if (cornerVisual == null)
                cornerVisual = GetComponentInChildren<PanelResizeCorners>(true);

            CreateCornerColliders();
        }

        void Start()
        {
            EnsureCornerCollidersRegistered();
        }

        void OnEnable()
        {
            if (grab == null)
                return;

            grab.selectEntered.AddListener(OnSelectEntered);
            grab.selectExited.AddListener(OnSelectExited);
        }

        void OnDisable()
        {
            if (grab != null)
            {
                grab.selectEntered.RemoveListener(OnSelectEntered);
                grab.selectExited.RemoveListener(OnSelectExited);
            }

            EndDrag();
            SetHover(-1);
        }

        void Update()
        {
            EnsureCornerCollidersRegistered();
            TryBeginTriggerResize();

            if (dragging)
            {
                UpdateDrag();
                return;
            }

            UpdateHover();
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (dragging || scaleController == null || scaleController.IsScaling)
                return;

            // Resize is a trigger-only gesture. PanelEdgeGrab's grip-grab-anywhere drives its select
            // through StartManualInteraction, so that flag is what separates a grip grab from a trigger
            // one; grip keeps meaning "move the panel", even over a corner.
            if (args.interactorObject is XRBaseInteractor baseInteractor &&
                baseInteractor.isPerformingManualInteraction)
                return;

            if (!TryGetCorner(args.interactorObject, out var corner, out var point))
                return;

            BeginDrag(args.interactorObject, corner, point);
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            if (dragging && ReferenceEquals(args.interactorObject, dragInteractor))
                EndDrag();
        }

        /// <summary>Lets PanelEdgeGrab admit a grab that starts on a corner handle.</summary>
        public bool IsRayOnCorner(XRBaseInputInteractor interactor)
        {
            return TryGetCorner(interactor, out _, out _);
        }

        public void Configure(
            XRGrabInteractable panelGrab,
            ScaleController panelScale,
            Collider bodyCollider,
            PanelResizeCorners visual)
        {
            grab = panelGrab;
            scaleController = panelScale;
            panelCollider = bodyCollider;
            cornerVisual = visual;
            CreateCornerColliders();
            EnsureCornerCollidersRegistered();
        }

        void TryBeginTriggerResize()
        {
            var leftPressed =
                IsTriggerPressed(InteractorHandedness.Left);
            var rightPressed =
                IsTriggerPressed(InteractorHandedness.Right);
            var leftDown = leftPressed && !leftTriggerWasPressed;
            var rightDown = rightPressed && !rightTriggerWasPressed;
            leftTriggerWasPressed = leftPressed;
            rightTriggerWasPressed = rightPressed;

            if (dragging || (!leftDown && !rightDown))
                return;

            Interactors.Clear();
            Interactors.AddRange(
                FindObjectsByType<XRBaseInputInteractor>(
                    FindObjectsInactive.Exclude));

            foreach (var interactor in Interactors)
            {
                var pressedThisFrame = interactor.handedness switch
                {
                    InteractorHandedness.Left => leftDown,
                    InteractorHandedness.Right => rightDown,
                    _ => false
                };

                if (!pressedThisFrame ||
                    !TryGetCorner(
                        interactor,
                        out var corner,
                        out var point))
                {
                    continue;
                }

                BeginDrag(
                    interactor,
                    corner,
                    point,
                    isDirectTrigger: true);
                return;
            }
        }

        void BeginDrag(IXRSelectInteractor interactor, int corner, Vector3 grabPoint)
        {
            BeginDrag(
                interactor,
                corner,
                grabPoint,
                isDirectTrigger: false);
        }

        void BeginDrag(
            IXRSelectInteractor interactor,
            int corner,
            Vector3 grabPoint,
            bool isDirectTrigger)
        {
            if (panelCollider == null)
                return;

            // Freeze the plane so the panel growing underneath cannot feed back into the input point.
            dragPlane = new Plane(panelCollider.transform.forward, panelCollider.transform.position);

            var pivot = resizeAroundCenter ? GetPanelCenterWorld() : GetOppositeCornerWorld(corner);

            if (!scaleController.BeginHandleScale(ProjectOnDragPlane(grabPoint), ProjectOnDragPlane(pivot)))
                return;

            dragging = true;
            dragInteractor = interactor;
            directTriggerDrag = isDirectTrigger;

            // Keep the select alive (it is what tells us when the trigger is released) but stop it from
            // dragging the panel around, otherwise move and resize fight over the transform.
            trackPositionWas = grab.trackPosition;
            trackRotationWas = grab.trackRotation;
            throwOnDetachWas = grab.throwOnDetach;
            grab.trackPosition = false;
            grab.trackRotation = false;
            grab.throwOnDetach = false;

            SetHover(corner);

            if (cornerVisual != null)
                cornerVisual.SetExternalVisible(true);
        }

        void UpdateDrag()
        {
            var inputHeld = dragInteractor != null &&
                (directTriggerDrag
                    ? IsTriggerPressed(
                        ((XRBaseInputInteractor)dragInteractor)
                            .handedness)
                    : dragInteractor.IsSelecting(grab));
            if (!inputHeld || !TryGetDragPoint(out var point))
            {
                EndDrag();
                return;
            }

            scaleController.UpdateHandleScale(point);
        }

        void EndDrag()
        {
            if (!dragging)
                return;

            dragging = false;
            dragInteractor = null;
            directTriggerDrag = false;

            if (grab != null)
            {
                grab.trackPosition = trackPositionWas;
                grab.trackRotation = trackRotationWas;
                grab.throwOnDetach = throwOnDetachWas;
            }

            if (scaleController != null)
                scaleController.EndHandleScale();

            if (cornerVisual != null)
                cornerVisual.SetExternalVisible(false);

            SetHover(-1);
        }

        static bool IsTriggerPressed(
            InteractorHandedness handedness)
        {
            var node = handedness switch
            {
                InteractorHandedness.Left => XRNode.LeftHand,
                InteractorHandedness.Right => XRNode.RightHand,
                _ => XRNode.HardwareTracker
            };
            if (node == XRNode.HardwareTracker)
                return false;

            InputDevices.Clear();
            UnityEngine.XR.InputDevices.GetDevicesAtXRNode(
                node,
                InputDevices);
            foreach (var device in InputDevices)
            {
                if (device.TryGetFeatureValue(
                        CommonUsages.triggerButton,
                        out var pressed) &&
                    pressed)
                {
                    return true;
                }
            }

            return false;
        }

        bool TryGetDragPoint(out Vector3 point)
        {
            point = default;

            if (dragInteractor is IXRRayProvider rayProvider)
            {
                var origin = rayProvider.GetOrCreateRayOrigin();
                if (origin != null)
                {
                    var ray = new Ray(origin.position, origin.forward);
                    if (!dragPlane.Raycast(ray, out var distance) || distance > rayDistance)
                        return false;

                    point = ray.GetPoint(distance);
                    return true;
                }
            }

            var attach = dragInteractor.GetAttachTransform(grab);
            if (attach == null)
                return false;

            point = ProjectOnDragPlane(attach.position);
            return true;
        }

        void CreateCornerColliders()
        {
            var box = panelCollider as BoxCollider;
            if (box == null)
                return;

            cornerCollidersRegistered = false;
            var size = box.size;
            var parent = panelCollider.transform;

            for (var i = 0; i < CornerSigns.Length; i++)
            {
                var sign = CornerSigns[i];
                var center = new Vector3(
                    sign.x * (size.x * 0.5f + cornerOutset - cornerSize * 0.5f),
                    sign.y * (size.y * 0.5f + cornerOutset - cornerSize * 0.5f),
                    0f);

                cornerColliders[i] = CreateCornerCollider(parent, $"Resize Corner {i + 1}", center);
            }
        }

        void EnsureCornerCollidersRegistered()
        {
            if (cornerCollidersRegistered || grab == null)
                return;

            foreach (var corner in cornerColliders)
            {
                if (corner != null && !grab.colliders.Contains(corner))
                    grab.colliders.Add(corner);
            }

            if (grab.interactionManager == null)
                return;

            grab.interactionManager.UnregisterInteractable(
                (IXRInteractable)grab);
            grab.interactionManager.RegisterInteractable(
                (IXRInteractable)grab);
            cornerCollidersRegistered = true;
        }

        BoxCollider CreateCornerCollider(Transform parent, string objectName, Vector3 center)
        {
            var existing = parent.Find(objectName);
            var corner = existing != null ? existing.gameObject : new GameObject(objectName);
            corner.transform.SetParent(parent, false);
            corner.transform.localPosition = center;
            corner.transform.localRotation = Quaternion.identity;
            corner.transform.localScale = Vector3.one;

            var collider = corner.GetComponent<BoxCollider>();
            if (collider == null)
                collider = corner.AddComponent<BoxCollider>();

            // Deeper than the edge colliders so the corner wins the ray where the two overlap.
            collider.size = new Vector3(cornerSize, cornerSize, cornerDepth);
            collider.center = Vector3.zero;

            if (corner.GetComponent<ContextIndicatorIgnore>() == null)
                corner.AddComponent<ContextIndicatorIgnore>();

            return collider;
        }

        void UpdateHover()
        {
            var corner = -1;

            Interactors.Clear();
            Interactors.AddRange(FindObjectsByType<XRBaseInputInteractor>(FindObjectsInactive.Exclude));

            foreach (var interactor in Interactors)
            {
                if (TryGetCorner(interactor, out var hitCorner, out _))
                {
                    corner = hitCorner;
                    break;
                }
            }

            SetHover(corner);
        }

        void SetHover(int corner)
        {
            if (hoveredCorner == corner)
                return;

            hoveredCorner = corner;

            if (cornerVisual != null)
                cornerVisual.SetHighlightedCorner(corner);
        }

        bool TryGetCorner(object interactor, out int corner, out Vector3 point)
        {
            corner = -1;
            point = default;

            if (interactor is IXRRayProvider rayProvider)
            {
                var origin = rayProvider.GetOrCreateRayOrigin();
                if (origin != null && TryRayCorner(origin.position, origin.forward, out corner, out point))
                    return true;

                var end = rayProvider.rayEndTransform;
                if (end != null && TryNearCorner(end.position, out corner, out point))
                    return true;
            }

            if (interactor is IXRInteractor baseInteractor)
            {
                var attach = baseInteractor.GetAttachTransform(grab);
                if (attach != null && TryNearCorner(attach.position, out corner, out point))
                    return true;
            }

            return false;
        }

        bool TryRayCorner(Vector3 origin, Vector3 direction, out int corner, out Vector3 point)
        {
            corner = -1;
            point = default;

            var hits = Physics.RaycastAll(origin, direction, rayDistance, ~0, QueryTriggerInteraction.Ignore);
            var bestDistance = float.MaxValue;

            foreach (var hit in hits)
            {
                if (hit.distance >= bestDistance)
                    continue;

                var hitCorner = IndexOfCorner(hit.collider);
                if (hitCorner < 0)
                    continue;

                bestDistance = hit.distance;
                corner = hitCorner;
                point = hit.point;
            }

            return corner >= 0;
        }

        bool TryNearCorner(Vector3 worldPoint, out int corner, out Vector3 point)
        {
            corner = -1;
            point = default;
            var bestDistance = float.MaxValue;

            for (var i = 0; i < cornerColliders.Length; i++)
            {
                if (cornerColliders[i] == null)
                    continue;

                var closest = cornerColliders[i].ClosestPoint(worldPoint);
                var distance = Vector3.Distance(worldPoint, closest);
                if (distance > handNearDistance || distance >= bestDistance)
                    continue;

                bestDistance = distance;
                corner = i;
                point = worldPoint;
            }

            return corner >= 0;
        }

        Vector3 ProjectOnDragPlane(Vector3 worldPoint)
        {
            return worldPoint - dragPlane.normal * dragPlane.GetDistanceToPoint(worldPoint);
        }

        Vector3 GetPanelCenterWorld()
        {
            return panelCollider is BoxCollider box
                ? panelCollider.transform.TransformPoint(box.center)
                : panelCollider.transform.position;
        }

        Vector3 GetOppositeCornerWorld(int corner)
        {
            var box = panelCollider as BoxCollider;
            var sign = CornerSigns[corner];

            if (box == null)
                return panelCollider.transform.position;

            var size = box.size;
            return panelCollider.transform.TransformPoint(
                new Vector3(-sign.x * size.x * 0.5f, -sign.y * size.y * 0.5f, 0f));
        }

        int IndexOfCorner(Collider collider)
        {
            for (var i = 0; i < cornerColliders.Length; i++)
            {
                if (cornerColliders[i] == collider)
                    return i;
            }

            return -1;
        }
    }
}
