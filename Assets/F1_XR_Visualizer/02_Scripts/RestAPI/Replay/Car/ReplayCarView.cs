using System.Collections.Generic;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public partial class ReplayCarView : MonoBehaviour
    {
        public int driverNumber;
        public Vector3 rawPosition;

        public void Init(int number)
        {
            driverNumber = number;
            name = $"Car_{number}";
            bodyRenderersDirty = true;
            SetLabel(number.ToString());
        }

        public void SetPosition(Vector3 position)
        {
            rawPosition = position;
            transform.position = position;
        }

        public void SetLocalPosition(Vector3 position)
        {
            rawPosition = position;
            transform.localPosition = position;
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
            if (labelTextMaterial != null)
                Destroy(labelTextMaterial);

            if (labelLineMaterial != null)
                Destroy(labelLineMaterial);

            if (labelBackgroundMaterial != null)
                Destroy(labelBackgroundMaterial);

            if (labelDotMaterial != null)
                Destroy(labelDotMaterial);

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
            if (selected)
                UpdateSelectionEffect();

            if (leaderHighlightVisible && rank == 1)
                UpdateLeaderEffect();

            if (!ShouldShowLabel() || label == null || Camera.main == null)
                return;

            labelLine ??= CreateLabelLine();
            labelBackground ??= CreateLabelBackground();
            labelTopDot ??= CreateLabelDot("DriverLabelTopDot");
            labelBottomDot ??= CreateLabelDot("DriverLabelBottomDot");

            UpdateLabelLayout();
            label.transform.rotation = Camera.main.transform.rotation;
        }

        private static void AddRenderer(List<Renderer> renderers, Renderer renderer)
        {
            if (renderer != null)
                renderers.Add(renderer);
        }
    }
}
