using UnityEngine;
using static F1XR.RestAPI.Replay.ReplayCarVisualUtil;

namespace F1XR.RestAPI.Replay
{
    public partial class ReplayCarView
    {
        private const float SelectionRingHeightRatio = 0.02f;
        private const float SelectionPulseHeightRatio = 0.026f;
        private const float SelectionRingOuterRatio = 0.95f;
        private const float SelectionPulseOuterRatio = 1.8f;
        private const float SelectionRingInnerRatio = 0.56f;
        private const float SelectionPulseInnerRatio = 0.72f;
        private const float SelectionRingAlpha = 0.88f;
        private const float HoverRingAlpha = 0.34f;
        private const float SelectionPulseAlpha = 0.82f;
        private const float SelectionRingRotationSpeed = 32f;
        private const float SelectionPulseDuration = 0.58f;
        private const float LeaderRingHeightRatio = 0.035f;
        private const float LeaderRingOuterRatio = 1.28f;
        private const float LeaderRingInnerRatio = 0.76f;
        private const float LeaderRingAlpha = 0.34f;
        private const float LeaderRingPulseAlpha = 0.12f;
        private const float LeaderRingRotationSpeed = -18f;
        private const int SelectionRingSegments = 96;

        private static readonly Color LeaderFxColor = new Color(1f, 0.78f, 0.12f, 1f);

        private Transform selectionRoot;
        private Transform leaderRoot;
        private MeshRenderer selectionRing;
        private MeshRenderer selectionPulse;
        private MeshRenderer leaderRing;
        private Material selectionRingMaterial;
        private Material selectionPulseMaterial;
        private Material leaderRingMaterial;
        private Mesh selectionRingMesh;
        private Mesh selectionPulseMesh;
        private Mesh leaderRingMesh;
        private Vector3[] selectionRingVertices;
        private Vector3[] selectionPulseVertices;
        private Vector3[] leaderRingVertices;
        private Color selectionColor = Color.white;
        private float leaderAge;
        private bool leaderLayoutDirty = true;
        private float selectionAge;
        private float selectionPulseAge = SelectionPulseDuration;
        private float selectionRadius;
        private Vector3 selectionLocalCenter;
        private bool selectionLayoutDirty = true;
        private bool selected;
        private bool hovered;

        public void SetHovered(bool value)
        {
            if (hovered == value)
                return;

            hovered = value;
            UpdateRenderLod(true);
            labelLayoutDirty = true;
            selectionLayoutDirty = true;
            if (!selected && !hovered)
                SetSelectionObjectsActive(false);

            SetLabelObjectsActive(ShouldShowLabel());
            ApplySelectionColor();
            RefreshRuntimeUpdateState();
        }

        public void SetSelected(bool value)
        {
            SetSelected(value, labelColor);
        }

        public void SetSelected(bool value, Color color)
        {
            if (selected == value)
            {
                SetSelectionColor(color);
                SetLabelObjectsActive(ShouldShowLabel());
                ApplyBodyHighlight();
                RefreshRuntimeUpdateState();
                return;
            }

            SetSelectionColor(color);
            selected = value;
            UpdateRenderLod(true);
            labelLayoutDirty = true;
            selectionLayoutDirty = true;

            if (selected)
                selectionPulseAge = 0f;

            SetSelectionObjectsActive(false);
            SetLabelObjectsActive(ShouldShowLabel());
            ApplyBodyHighlight();
            RefreshRuntimeUpdateState();
        }

        private void EnsureSelectionEffect()
        {
            bool created = false;
            selectionRingMaterial ??= CreateSelectionMaterial(WithAlpha(CurrentSelectionFxColor(), SelectionRingAlpha));
            selectionPulseMaterial ??= CreateSelectionMaterial(WithAlpha(CurrentSelectionFxColor(), SelectionPulseAlpha));

            if (selectionRoot == null)
            {
                GameObject root = new GameObject("SelectionFx");
                root.transform.SetParent(transform, false);
                selectionRoot = root.transform;
                created = true;
            }

            if (selectionRing == null)
            {
                GameObject ring = new GameObject("GroundRing");
                ring.transform.SetParent(selectionRoot, false);
                MeshFilter ringFilter = ring.AddComponent<MeshFilter>();
                selectionRing = ring.AddComponent<MeshRenderer>();
                selectionRing.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                selectionRing.receiveShadows = false;
                selectionRing.material = selectionRingMaterial;
                selectionRingMesh = CreateRingMesh("SelectedCarGroundRing", SelectionRingSegments, out selectionRingVertices);
                ringFilter.sharedMesh = selectionRingMesh;
                created = true;
            }

            if (selectionPulse == null)
            {
                GameObject pulse = new GameObject("SelectionPulse");
                pulse.transform.SetParent(selectionRoot, false);
                MeshFilter pulseFilter = pulse.AddComponent<MeshFilter>();
                selectionPulse = pulse.AddComponent<MeshRenderer>();
                selectionPulse.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                selectionPulse.receiveShadows = false;
                selectionPulse.material = selectionPulseMaterial;
                selectionPulseMesh = CreateRingMesh("SelectedCarPulse", SelectionRingSegments, out selectionPulseVertices);
                pulseFilter.sharedMesh = selectionPulseMesh;
                pulse.SetActive(false);
                created = true;
            }

            if (created)
            {
                bodyRenderersDirty = true;
                selectionLayoutDirty = true;
            }

            ApplySelectionColor();
        }

        private void EnsureLeaderEffect()
        {
            bool created = false;
            leaderRingMaterial ??= CreateSelectionMaterial(WithAlpha(LeaderFxColor, LeaderRingAlpha));

            if (leaderRoot == null)
            {
                GameObject root = new GameObject("RaceLeaderFx");
                root.transform.SetParent(transform, false);
                leaderRoot = root.transform;
                created = true;
            }

            if (leaderRing == null)
            {
                GameObject ring = new GameObject("LeaderGroundRing");
                ring.transform.SetParent(leaderRoot, false);
                MeshFilter ringFilter = ring.AddComponent<MeshFilter>();
                leaderRing = ring.AddComponent<MeshRenderer>();
                leaderRing.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                leaderRing.receiveShadows = false;
                leaderRing.material = leaderRingMaterial;
                leaderRingMesh = CreateRingMesh("RaceLeaderGroundRing", SelectionRingSegments, out leaderRingVertices);
                ringFilter.sharedMesh = leaderRingMesh;
                created = true;
            }

            if (created)
                bodyRenderersDirty = true;
        }

        private void UpdateLeaderEffect()
        {
            EnsureLeaderEffect();
            SetLeaderObjectsActive(true);

            leaderAge += Time.deltaTime;
            float alpha = LeaderRingAlpha + Mathf.Sin(leaderAge * Mathf.PI * 2f) * LeaderRingPulseAlpha;

            SetMaterialColor(leaderRingMaterial, WithAlpha(LeaderFxColor, alpha));
            leaderRing.transform.localRotation =
                Quaternion.Euler(0f, leaderAge * LeaderRingRotationSpeed, 0f);

            if (!leaderLayoutDirty)
                return;

            if (!TryGetCarBounds(out Bounds bounds))
                return;

            float radius = Mathf.Max(bounds.size.x, bounds.size.z) * LeaderRingOuterRatio;
            Vector3 worldCenter = new Vector3(
                bounds.center.x,
                bounds.min.y + Mathf.Max(radius * LeaderRingHeightRatio, 0.0012f),
                bounds.center.z
            );
            leaderRing.transform.position = worldCenter;
            UpdateRingMesh(
                transform,
                leaderRingMesh,
                leaderRingVertices,
                SelectionRingSegments,
                Vector3.zero,
                radius,
                LeaderRingInnerRatio,
                0f
            );
            leaderLayoutDirty = false;
        }

        private void SetSelectionColor(Color color)
        {
            selectionColor = color;
            ApplySelectionColor();
        }

        private void ApplySelectionColor()
        {
            float ringAlpha = selected ? SelectionRingAlpha : HoverRingAlpha;
            SetMaterialColor(selectionRingMaterial, WithAlpha(CurrentSelectionFxColor(), ringAlpha));
            SetMaterialColor(selectionPulseMaterial, WithAlpha(CurrentSelectionFxColor(), SelectionPulseAlpha));
        }

        private void UpdateSelectionEffect()
        {
            EnsureSelectionEffect();
            SetSelectionObjectsActive(true);
            if (!selected && selectionPulse != null)
                selectionPulse.gameObject.SetActive(false);
            ApplyBodyHighlight();

            selectionAge += Time.deltaTime;
            selectionRing.transform.localRotation =
                Quaternion.Euler(0f, selectionAge * SelectionRingRotationSpeed, 0f);

            if (selectionLayoutDirty)
            {
                if (!TryGetCarBounds(out Bounds bounds))
                    return;

                selectionRadius =
                    Mathf.Max(bounds.size.x, bounds.size.z) *
                    SelectionRingOuterRatio;
                Vector3 worldCenter = new Vector3(
                    bounds.center.x,
                    bounds.min.y + Mathf.Max(selectionRadius * SelectionRingHeightRatio, 0.001f),
                    bounds.center.z
                );
                selectionLocalCenter = transform.InverseTransformPoint(worldCenter);
                selectionRing.transform.position = worldCenter;

                UpdateRingMesh(
                    transform,
                    selectionRingMesh,
                    selectionRingVertices,
                    SelectionRingSegments,
                    Vector3.zero,
                    selectionRadius,
                    SelectionRingInnerRatio,
                    0f
                );
                selectionLayoutDirty = false;
            }

            UpdateSelectionPulse(selectionLocalCenter, selectionRadius);
        }

        private void UpdateSelectionPulse(Vector3 localCenter, float radius)
        {
            if (selectionPulse == null)
                return;

            if (selectionPulseAge >= SelectionPulseDuration)
            {
                selectionPulse.gameObject.SetActive(false);
                return;
            }

            selectionPulse.gameObject.SetActive(true);

            float t = Mathf.Clamp01(selectionPulseAge / SelectionPulseDuration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            float pulseRadius = radius * Mathf.Lerp(0.78f, SelectionPulseOuterRatio, eased);
            float pulseAlpha = Mathf.Lerp(SelectionPulseAlpha, 0f, t);
            Color color = CurrentSelectionFxColor();
            SetMaterialColor(selectionPulseMaterial, WithAlpha(color, pulseAlpha));
            Vector3 pulseCenter = localCenter + Vector3.up * LocalDistance(Mathf.Max(radius * SelectionPulseHeightRatio, 0.0008f));
            UpdateRingMesh(
                transform,
                selectionPulseMesh,
                selectionPulseVertices,
                SelectionRingSegments,
                pulseCenter,
                pulseRadius,
                SelectionPulseInnerRatio,
                -selectionAge * SelectionRingRotationSpeed * 0.65f);
            selectionPulseAge += Time.deltaTime;
        }

        private float LocalDistance(float worldDistance)
        {
            return worldDistance / Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.y));
        }

        private Color CurrentSelectionFxColor()
        {
            return selectionColor;
        }

        private void SetSelectionObjectsActive(bool active)
        {
            if (selectionRoot != null)
                selectionRoot.gameObject.SetActive(active);

            if (selectionRing != null)
                selectionRing.gameObject.SetActive(active);

            if (selectionPulse != null)
                selectionPulse.gameObject.SetActive(active && selectionPulseAge < SelectionPulseDuration);
        }

        private void SetLeaderObjectsActive(bool active)
        {
            if (leaderRoot != null)
                leaderRoot.gameObject.SetActive(active);

            if (leaderRing != null)
                leaderRing.gameObject.SetActive(active);
        }
    }
}
