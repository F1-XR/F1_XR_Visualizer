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

        public Transform LogicalRoot => logicalRoot != null
            ? logicalRoot
            : transform;
        public Transform VisualMotionRoot => transform;

        public void SetLogicalRoot(Transform root)
        {
            logicalRoot = root;
            visualBasePosition = transform.localPosition;
            visualBaseRotation = transform.localRotation;
        }

        public void Init(int number)
        {
            driverNumber = number;
            LogicalRoot.name = $"Car_{number}";
            name = "VisualMotionRoot";
            bodyRenderersDirty = true;
            SetLabel(number.ToString());
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

            transform.localPosition =
                visualBasePosition + LogicalRoot.InverseTransformVector(worldOffset);
            transform.localRotation =
                Quaternion.AngleAxis(localYaw, Vector3.up) * visualBaseRotation;
        }

        public void ResetVisualMotion()
        {
            if (LogicalRoot == transform)
                return;

            transform.localPosition = visualBasePosition;
            transform.localRotation = visualBaseRotation;
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

        private void LateUpdate()
        {
            if (selected || hovered)
                UpdateSelectionEffect();

            if (leaderHighlightVisible && rank == 1)
                UpdateLeaderEffect();

            if (!ShouldShowLabel() || label == null || Camera.main == null)
                return;

            labelLine ??= CreateLabelLine();
            labelBackground ??= CreateLabelBackground();
            labelTopDot ??= CreateLabelDot("DriverLabelTopDot");
            labelBottomDot ??= CreateLabelDot("DriverLabelBottomDot");

            if (labelLayoutDirty && UpdateLabelLayout())
                labelLayoutDirty = false;

            label.transform.rotation = Camera.main.transform.rotation;
        }

        private static void AddRenderer(List<Renderer> renderers, Renderer renderer)
        {
            if (renderer != null)
                renderers.Add(renderer);
        }
    }
}
