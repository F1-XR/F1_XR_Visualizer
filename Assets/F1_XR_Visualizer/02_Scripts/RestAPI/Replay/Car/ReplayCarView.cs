using System.Collections.Generic;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public partial class ReplayCarView : MonoBehaviour
    {
        public int driverNumber;
        public Vector3 rawPosition;

        private Transform logicalRoot;
        private Vector3 visualBasePosition;
        private Quaternion visualBaseRotation = Quaternion.identity;
        private Vector3 visualMotionLocalOffset;
        private Vector3 drivingPresentationLocalOffset;
        private Vector3 roomPresentationLocalScale;
        private bool roomPresentationApplied;
        private bool visualMotionApplied;
        private Camera labelCamera;
        private readonly List<Renderer>
            showcaseTransitionRenderers = new();
        private readonly List<bool>
            showcaseTransitionRendererStates = new();
        private bool showcaseTransitionHidden;

        public Transform LogicalRoot => logicalRoot != null
            ? logicalRoot
            : transform;
        public Transform VisualMotionRoot => transform;

        public void SetLogicalRoot(Transform root)
        {
            logicalRoot = root;
            visualBasePosition = transform.localPosition;
            visualBaseRotation = transform.localRotation;
            visualMotionLocalOffset = Vector3.zero;
            drivingPresentationLocalOffset = Vector3.zero;
            visualMotionApplied = false;
        }

        public void Init(int number)
        {
            driverNumber = number;
            LogicalRoot.name = $"Car_{number}";
            name = "VisualMotionRoot";
            bodyRenderersDirty = true;
            SetLabel(number.ToString());
            RefreshRuntimeUpdateState();
        }

        public void SetPosition(Vector3 position)
        {
            rawPosition = position;
            LogicalRoot.position = position;
        }

        public void SetLocalPosition(Vector3 position)
        {
            rawPosition = position;
            LogicalRoot.localPosition = position;
        }

        public void ApplyVisualMotion(Vector3 worldOffset, float localYaw)
        {
            if (LogicalRoot == transform)
                return;

            ClearRoomPresentation();
            visualMotionApplied = true;
            visualMotionLocalOffset =
                LogicalRoot.InverseTransformVector(worldOffset);
            ApplyVisualLocalPosition();
            transform.localRotation =
                Quaternion.AngleAxis(localYaw, Vector3.up) * visualBaseRotation;
        }

        public void ApplyVisualMotionFacing(
            Vector3 worldOffset,
            Vector3 desiredWorldForward)
        {
            if (LogicalRoot == transform)
                return;

            Vector3 baseForward = Vector3.ProjectOnPlane(
                visualBaseRotation * Vector3.forward,
                Vector3.up);
            Vector3 desiredForward = Vector3.ProjectOnPlane(
                LogicalRoot.InverseTransformDirection(
                    desiredWorldForward),
                Vector3.up);
            float localYaw = baseForward.sqrMagnitude > 0.000001f &&
                desiredForward.sqrMagnitude > 0.000001f
                    ? Vector3.SignedAngle(
                        baseForward,
                        desiredForward,
                        Vector3.up)
                    : 0f;
            ApplyVisualMotion(worldOffset, localYaw);
        }

        public void ResetVisualMotion()
        {
            if (LogicalRoot == transform)
                return;

            if (!visualMotionApplied &&
                visualMotionLocalOffset.sqrMagnitude <= 0.000001f)
                return;

            ClearRoomPresentation();
            visualMotionLocalOffset = Vector3.zero;
            ApplyVisualLocalPosition();
            transform.localRotation = visualBaseRotation;
            visualMotionApplied = false;
        }

        internal void SetDrivingPresentationLocalOffset(Vector3 offset)
        {
            drivingPresentationLocalOffset = offset;
            ApplyVisualLocalPosition();
        }

        private void ApplyVisualLocalPosition()
        {
            transform.localPosition =
                visualBasePosition +
                visualMotionLocalOffset +
                drivingPresentationLocalOffset;
        }

        public void ApplyRoomPresentation(
            Vector3 worldAnchor,
            float scale)
        {
            ClearRoomPresentation();

            scale = Mathf.Max(0.01f, scale);
            if (Mathf.Approximately(scale, 1f))
                return;

            roomPresentationLocalScale = transform.localScale;
            roomPresentationApplied = true;

            transform.localScale =
                roomPresentationLocalScale * scale;
            MarkVisualLayoutDirty();
        }

        public void ClearRoomPresentation()
        {
            if (!roomPresentationApplied)
                return;

            transform.localScale =
                roomPresentationLocalScale;
            roomPresentationApplied = false;
            MarkVisualLayoutDirty();
        }

        public void CollectOnboardHiddenRenderers(List<Renderer> renderers)
        {
            if (renderers == null)
                return;

            AddRenderer(renderers, labelRenderer);
            AddRenderer(renderers, labelLine);
            AddRenderer(renderers, labelBackground);
            AddRenderer(renderers, labelTopDot);
            AddRenderer(renderers, labelBottomDot);
            AddRenderer(renderers, selectionRing);
            AddRenderer(renderers, selectionPulse);
            AddRenderer(renderers, leaderRing);
        }

        internal void SetShowcaseTransitionHidden(bool hidden)
        {
            if (hidden)
            {
                if (!showcaseTransitionHidden)
                {
                    showcaseTransitionRenderers.Clear();
                    showcaseTransitionRendererStates.Clear();
                    Renderer[] renderers =
                        GetComponentsInChildren<Renderer>(true);
                    for (int i = 0; i < renderers.Length; i++)
                    {
                        Renderer renderer = renderers[i];
                        if (renderer == null)
                            continue;

                        showcaseTransitionRenderers.Add(renderer);
                        showcaseTransitionRendererStates.Add(
                            renderer.enabled);
                    }
                    showcaseTransitionHidden = true;
                }

                for (int i = 0;
                    i < showcaseTransitionRenderers.Count;
                    i++)
                {
                    Renderer renderer =
                        showcaseTransitionRenderers[i];
                    if (renderer != null)
                        renderer.enabled = false;
                }
                return;
            }

            if (!showcaseTransitionHidden)
                return;

            for (int i = 0;
                i < showcaseTransitionRenderers.Count;
                i++)
            {
                Renderer renderer = showcaseTransitionRenderers[i];
                if (renderer != null)
                {
                    renderer.enabled =
                        showcaseTransitionRendererStates[i];
                }
            }
            showcaseTransitionRenderers.Clear();
            showcaseTransitionRendererStates.Clear();
            showcaseTransitionHidden = false;
        }

        private void OnDestroy()
        {
            SetShowcaseTransitionHidden(false);
            DisposeRenderLod();
            DisposeOvertakeRibbon();
            DisposeOvertakeSideBySideVfx();
            DisposeOvertakeCompletionVfx();

            if (selectionRingMaterial != null)
                Destroy(selectionRingMaterial);

            if (selectionPulseMaterial != null)
                Destroy(selectionPulseMaterial);

            if (selectionRingMesh != null)
                Destroy(selectionRingMesh);

            if (selectionPulseMesh != null)
                Destroy(selectionPulseMesh);

            if (leaderRingMaterial != null)
                Destroy(leaderRingMaterial);

            if (leaderRingMesh != null)
                Destroy(leaderRingMesh);
        }

        private void OnEnable()
        {
            SetLabelObjectsActive(ShouldShowLabel());
            SetSelectionObjectsActive(selected || hovered);
            SetLeaderObjectsActive(leaderHighlightVisible && rank == 1);
        }

        private void LateUpdate()
        {
            // Script reloads can leave cars that were spawned before LOD setup.
            if (!renderLodConfigured)
                ConfigureRenderLod();

            UpdateRenderLod();

            if (selected || hovered)
                UpdateSelectionEffect();

            if (leaderHighlightVisible && rank == 1)
                UpdateLeaderEffect();

            if (!ShouldShowLabel() || label == null)
                return;

            if (labelCamera == null || !labelCamera.isActiveAndEnabled)
                labelCamera = Camera.main;

            if (labelCamera == null)
                return;

            bool showLabelDetails = ShouldShowLabelDetails();
            if (showLabelDetails)
            {
                labelLine ??= CreateLabelLine();
                labelBackground ??= CreateLabelBackground();
                labelTopDot ??= CreateLabelDot("DriverLabelTopDot");
                labelBottomDot ??= CreateLabelDot("DriverLabelBottomDot");
            }

            if (labelLayoutDirty && UpdateLabelLayout(showLabelDetails))
                labelLayoutDirty = false;

            label.transform.rotation = labelCamera.transform.rotation;
        }

        private void RefreshRuntimeUpdateState()
        {
            enabled =
                renderLodConfigured ||
                selected ||
                hovered ||
                (leaderHighlightVisible && rank == 1) ||
                ShouldShowLabel();
        }

        private void MarkVisualLayoutDirty()
        {
            labelLayoutDirty = true;
            selectionLayoutDirty = true;
            leaderLayoutDirty = true;
        }

        private static void AddRenderer(List<Renderer> renderers, Renderer renderer)
        {
            if (renderer != null)
                renderers.Add(renderer);
        }
    }
}
