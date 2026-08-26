using F1XR.Interaction.World;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace F1XR.RestAPI.Replay.Room
{
    [DefaultExecutionOrder(1075)]
    [DisallowMultipleComponent]
    public sealed class PitWallPortalEditState : MonoBehaviour
    {
        private const float MinimumPortalScale = 0.55f;
        private const float MaximumPortalScale = 1.1f;
        private const int EditInteractionLayer = 0;
        private const float ThumbstickDeadzone = 0.15f;

        private static readonly System.Collections.Generic.List<InputDevice>
            InputDevices = new();

        private ShowcasePortalPresentation owner;
        private ShowcaseWallFrame wall;
        private XRGrabInteractable grab;
        private ScaleController scaleController;
        private WorldGrabPolicy grabPolicy;
        private BoxCollider editCollider;
        private Vector2 baseSize;
        private Vector3 basePosition;
        private Quaternion baseRotation;
        private Vector3 presentationLocalScale = Vector3.one;
        private Vector3 constrainedRight;
        private Vector3 constrainedUp;
        private float minimumHorizontal;
        private float maximumHorizontal;
        private float minimumVertical;
        private float maximumVertical;
        private float maximumScale = 1f;
        private int presentationLayer;
        private Snapshot undoSnapshot;
        private bool hasUndo;
        private bool eventsBound;
        private bool freePlacement;

        public bool IsEditMode { get; private set; }
        public bool CanUndo => hasUndo;
        public bool IsManipulating =>
            (grab != null && grab.isSelected) ||
            (scaleController != null && scaleController.IsScaling);

        internal void Configure(
            ShowcasePortalPresentation presentation,
            ShowcaseWallFrame wallFrame,
            Vector2 portalSize,
            XRGrabInteractable portalGrab,
            ScaleController portalScale,
            WorldGrabPolicy portalGrabPolicy,
            BoxCollider portalCollider,
            bool allowFreePlacement)
        {
            owner = presentation;
            wall = wallFrame;
            baseSize = portalSize;
            basePosition = transform.position;
            baseRotation = transform.rotation;
            presentationLocalScale = transform.localScale;
            grab = portalGrab;
            scaleController = portalScale;
            grabPolicy = portalGrabPolicy;
            editCollider = portalCollider;
            freePlacement = allowFreePlacement;
            presentationLayer = gameObject.layer;

            Vector3 wallRight = wall.HorizontalAxis.normalized;
            Vector3 wallUp = wall.VerticalAxis.normalized;
            constrainedRight = transform.right.normalized;
            constrainedUp = transform.up.normalized;
            minimumHorizontal = wall.MinHorizontal;
            maximumHorizontal = wall.MaxHorizontal;
            minimumVertical = wall.MinVertical;
            maximumVertical = wall.MaxVertical;

            if (Vector3.Dot(transform.right, wallRight) < 0f)
            {
                float previousMinimum = minimumHorizontal;
                minimumHorizontal = -maximumHorizontal;
                maximumHorizontal = -previousMinimum;
            }

            if (Vector3.Dot(transform.up, wallUp) < 0f)
            {
                float previousMinimum = minimumVertical;
                minimumVertical = -maximumVertical;
                maximumVertical = -previousMinimum;
            }

            float availableWidth =
                maximumHorizontal - minimumHorizontal;
            float availableHeight =
                maximumVertical - minimumVertical;
            maximumScale = freePlacement
                ? MaximumPortalScale
                : Mathf.Clamp(
                    Mathf.Min(
                        availableWidth / Mathf.Max(0.001f, baseSize.x),
                        availableHeight / Mathf.Max(0.001f, baseSize.y)),
                    MinimumPortalScale,
                    MaximumPortalScale);

            BindEvents();
            hasUndo = false;
            SetEditMode(false);
            ApplyEditedTransform();
        }

        public void SetEditMode(bool enabled)
        {
            if (owner == null)
                enabled = false;

            IsEditMode = enabled;
            owner?.SetPitWallEditOutlineVisible(enabled);
            if (enabled)
            {
                gameObject.layer = EditInteractionLayer;
                if (editCollider != null)
                    editCollider.enabled = true;
                if (grab != null)
                    grab.enabled = true;
                if (grabPolicy != null)
                    grabPolicy.enabled = true;
                if (scaleController != null)
                    scaleController.enabled = true;
            }
            else
            {
                if (scaleController != null)
                    scaleController.enabled = false;
                if (grabPolicy != null)
                    grabPolicy.enabled = false;
                if (grab != null)
                    grab.enabled = false;
                if (editCollider != null)
                    editCollider.enabled = false;
                gameObject.layer = presentationLayer;
            }
            ApplyEditedTransform();
        }

        public void Undo()
        {
            if (!hasUndo)
                return;

            transform.position = undoSnapshot.Position;
            transform.rotation = freePlacement
                ? undoSnapshot.Rotation
                : baseRotation;
            transform.localScale =
                presentationLocalScale * undoSnapshot.Scale;
            hasUndo = false;
            ApplyEditedTransform();
        }

        public void ResetToAutomatic()
        {
            CaptureUndo();
            transform.position = basePosition;
            transform.rotation = baseRotation;
            transform.localScale = presentationLocalScale;
            ApplyEditedTransform();
        }

        internal void Release()
        {
            SetEditMode(false);
            UnbindEvents();
            owner = null;
            enabled = false;
        }

        private void LateUpdate()
        {
            if (owner != null)
                ApplyEditedTransform();
        }

        private void Update()
        {
            if (!IsEditMode || owner == null)
                return;

            Vector2 axis = ReadLeftThumbstick();
            if (axis.sqrMagnitude <
                ThumbstickDeadzone * ThumbstickDeadzone)
            {
                return;
            }

            owner.AdjustPitWallImmersiveReference(
                axis,
                Time.deltaTime);
        }

        private static Vector2 ReadLeftThumbstick()
        {
            InputDevices.Clear();
            UnityEngine.XR.InputDevices.GetDevicesAtXRNode(
                XRNode.LeftHand,
                InputDevices);
            foreach (InputDevice device in InputDevices)
            {
                if (device.TryGetFeatureValue(
                        CommonUsages.primary2DAxis,
                        out Vector2 axis))
                {
                    return axis;
                }
            }

            return Vector2.zero;
        }

        private void BindEvents()
        {
            if (eventsBound)
                return;

            if (grab != null)
                grab.selectEntered.AddListener(OnSelectEntered);
            if (scaleController != null)
                scaleController.ScaleStarted += OnScaleStarted;
            eventsBound = true;
        }

        private void UnbindEvents()
        {
            if (!eventsBound)
                return;

            if (grab != null)
                grab.selectEntered.RemoveListener(OnSelectEntered);
            if (scaleController != null)
                scaleController.ScaleStarted -= OnScaleStarted;
            eventsBound = false;
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (IsEditMode)
                CaptureUndo();
        }

        private void OnScaleStarted()
        {
            if (IsEditMode)
                CaptureUndo();
        }

        private void CaptureUndo()
        {
            if (owner == null)
                return;

            undoSnapshot = new Snapshot(
                transform.position,
                transform.rotation,
                ResolveUniformScale());
            hasUndo = true;
        }

        private void ApplyEditedTransform()
        {
            if (owner == null)
                return;

            float uniformScale = Mathf.Clamp(
                ResolveUniformScale(),
                MinimumPortalScale,
                maximumScale);
            if (freePlacement)
            {
                transform.localScale =
                    presentationLocalScale * uniformScale;
                UpdateColliderGeometry(uniformScale);
                owner.UpdatePitWallEditedPortal(
                    transform.position,
                    transform.rotation,
                    baseSize * uniformScale);
                return;
            }

            if (!wall.IsValid)
                return;

            float halfWidth = baseSize.x * uniformScale * 0.5f;
            float halfHeight = baseSize.y * uniformScale * 0.5f;
            Vector3 requestedOffset =
                transform.position - wall.Center;
            float horizontal = ClampCenterCoordinate(
                Vector3.Dot(requestedOffset, constrainedRight),
                minimumHorizontal,
                maximumHorizontal,
                halfWidth);
            float vertical = ClampCenterCoordinate(
                Vector3.Dot(requestedOffset, constrainedUp),
                minimumVertical,
                maximumVertical,
                halfHeight);

            transform.SetPositionAndRotation(
                wall.Center +
                constrainedRight * horizontal +
                constrainedUp * vertical,
                baseRotation);
            transform.localScale =
                presentationLocalScale * uniformScale;
            UpdateColliderGeometry(uniformScale);
            owner.UpdatePitWallEditedPortal(
                transform.position,
                baseRotation,
                baseSize * uniformScale);
        }

        private float ResolveUniformScale()
        {
            Vector3 current = transform.localScale;
            float x = Mathf.Abs(presentationLocalScale.x) > 0.0001f
                ? Mathf.Abs(current.x / presentationLocalScale.x)
                : 1f;
            float y = Mathf.Abs(presentationLocalScale.y) > 0.0001f
                ? Mathf.Abs(current.y / presentationLocalScale.y)
                : 1f;
            float z = Mathf.Abs(presentationLocalScale.z) > 0.0001f
                ? Mathf.Abs(current.z / presentationLocalScale.z)
                : 1f;
            return Mathf.Max(x, y, z);
        }

        private void UpdateColliderGeometry(float scale)
        {
            if (editCollider == null || scale <= 0.0001f)
                return;

            float handleWorldHeight = Mathf.Min(
                0.18f,
                baseSize.y * scale * 0.18f);
            float inverseScale = 1f / scale;
            editCollider.center = new Vector3(
                0f,
                baseSize.y * 0.5f -
                handleWorldHeight * inverseScale * 0.5f,
                0f);
            editCollider.size = new Vector3(
                baseSize.x,
                handleWorldHeight * inverseScale,
                0.08f * inverseScale);
        }

        private static float ClampCenterCoordinate(
            float value,
            float minimum,
            float maximum,
            float halfExtent)
        {
            float centerMinimum = minimum + halfExtent;
            float centerMaximum = maximum - halfExtent;
            return centerMinimum <= centerMaximum
                ? Mathf.Clamp(value, centerMinimum, centerMaximum)
                : (minimum + maximum) * 0.5f;
        }

        private void OnDestroy()
        {
            UnbindEvents();
        }

        private readonly struct Snapshot
        {
            public Snapshot(
                Vector3 position,
                Quaternion rotation,
                float scale)
            {
                Position = position;
                Rotation = rotation;
                Scale = scale;
            }

            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
            public float Scale { get; }
        }
    }
}
