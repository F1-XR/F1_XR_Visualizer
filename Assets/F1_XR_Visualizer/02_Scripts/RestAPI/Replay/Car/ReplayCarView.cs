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
        private Vector3 roomPresentationLocalScale;
        private bool roomPresentationApplied;
        private bool visualMotionApplied;
        private Camera labelCamera;

        public Transform LogicalRoot => logicalRoot != null
            ? logicalRoot
            : transform;
        public Transform VisualMotionRoot => transform;

        public void SetLogicalRoot(Transform root)
        {
            logicalRoot = root;
            visualBasePosition = transform.localPosition;
            visualBaseRotation = transform.localRotation;
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
            transform.localPosition =
                visualBasePosition + LogicalRoot.InverseTransformVector(worldOffset);
            transform.localRotation =
                Quaternion.AngleAxis(localYaw, Vector3.up) * visualBaseRotation;
        }

        public void ResetVisualMotion()
        {
            if (LogicalRoot == transform)
                return;

            if (!visualMotionApplied)
                return;

            ClearRoomPresentation();
            transform.localPosition = visualBasePosition;
            transform.localRotation = visualBaseRotation;
            visualMotionApplied = false;
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

        private void OnDestroy()
        {
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
