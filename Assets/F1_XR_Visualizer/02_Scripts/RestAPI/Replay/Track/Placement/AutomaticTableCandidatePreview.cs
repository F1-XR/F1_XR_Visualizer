using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace F1XR.RestAPI.Replay.Track.Placement
{
    [DefaultExecutionOrder(-900)]
    public sealed class AutomaticTableCandidatePreview : MonoBehaviour
    {
        [SerializeField] private ARPlanePlacementController placementController;
        [SerializeField] private bool showCandidates;
        [SerializeField] private Color candidateColor =
            new(0.25f, 0.85f, 1f, 0.28f);
        [SerializeField, Min(0.05f)] private float refreshInterval = 0.25f;
        [SerializeField, Min(0f)] private float heightOffset = 0.01f;

        private readonly List<ARPlane> candidates = new();
        private readonly Dictionary<TrackableId, GameObject> previews = new();
        private readonly HashSet<TrackableId> activePreviewIds = new();
        private readonly List<TrackableId> previewIdsToRemove = new();
        private Material material;
        private float nextRefreshTime;
        private bool runtimeVisible = true;

        public void SetRuntimeVisible(bool visible)
        {
            runtimeVisible = visible;
            if (!runtimeVisible)
                ClearPreviews();
        }

        public void SetShowCandidates(bool visible)
        {
            showCandidates = visible;
            if (!showCandidates)
                ClearPreviews();
        }

        private void Reset()
        {
            placementController = GetComponent<ARPlanePlacementController>();
        }

        private void Awake()
        {
            if (placementController == null)
                placementController = GetComponent<ARPlanePlacementController>();
        }

        private void OnDisable()
        {
            ClearPreviews();
        }

        private void OnDestroy()
        {
            ClearPreviews();
            if (material != null)
                Destroy(material);
        }

        private void Update()
        {
            if (!runtimeVisible || !showCandidates || placementController == null ||
                placementController.PlaneManager == null)
            {
                ClearPreviews();
                return;
            }

            if (Time.unscaledTime < nextRefreshTime)
                return;

            nextRefreshTime = Time.unscaledTime + refreshInterval;
            candidates.Clear();
            foreach (ARPlane plane in placementController.PlaneManager.trackables)
            {
                if (placementController.IsAutomaticTableCandidate(plane))
                    candidates.Add(plane);
            }

            UpdatePreviews();
        }

        private void UpdatePreviews()
        {
            activePreviewIds.Clear();
            foreach (ARPlane plane in candidates)
            {
                TrackableId id = plane.trackableId;
                activePreviewIds.Add(id);
                UpdatePreview(plane);
            }

            previewIdsToRemove.Clear();
            foreach (TrackableId id in previews.Keys)
            {
                if (!activePreviewIds.Contains(id))
                    previewIdsToRemove.Add(id);
            }

            foreach (TrackableId id in previewIdsToRemove)
            {
                GameObject preview = previews[id];
                if (preview != null)
                    Destroy(preview);
                previews.Remove(id);
            }
        }

        private void UpdatePreview(ARPlane plane)
        {
            TrackableId id = plane.trackableId;
            if (!previews.TryGetValue(id, out GameObject preview) || preview == null)
            {
                preview = GameObject.CreatePrimitive(PrimitiveType.Quad);
                preview.name = "Automatic Table Candidate Preview";
                Destroy(preview.GetComponent<Collider>());

                var renderer = preview.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = GetMaterial();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                previews[id] = preview;
            }

            preview.transform.SetPositionAndRotation(
                plane.center + plane.transform.up * heightOffset,
                plane.transform.rotation * Quaternion.Euler(-90f, 0f, 0f));
            preview.transform.localScale = new Vector3(
                Mathf.Max(plane.size.x, 0.01f),
                Mathf.Max(plane.size.y, 0.01f),
                1f);
        }

        private Material GetMaterial()
        {
            if (material != null)
                return material;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Standard");

            material = new Material(shader) { color = candidateColor };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", candidateColor);
            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Cull"))
                material.SetFloat("_Cull", 0f);

            material.SetInt(
                "_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt(
                "_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.renderQueue =
                (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return material;
        }

        private void ClearPreviews()
        {
            foreach (GameObject preview in previews.Values)
            {
                if (preview != null)
                    Destroy(preview);
            }

            previews.Clear();
            activePreviewIds.Clear();
            previewIdsToRemove.Clear();
        }
    }
}
