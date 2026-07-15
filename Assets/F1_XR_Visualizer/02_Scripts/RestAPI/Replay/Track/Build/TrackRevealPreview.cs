using System.Collections.Generic;
using UnityEngine;

namespace F1XR.RestAPI.Replay.Track.Build
{
    public sealed partial class TrackRevealPlacer
    {
        readonly Dictionary<Renderer, Material[]> previewOriginalMaterials = new();
        readonly List<Behaviour> previewDisabledBehaviours = new();
        readonly List<Collider> previewDisabledColliders = new();
        Vector3 previewVelocity;
        float previewSurfaceYOffset;
        bool previewPoseInitialized;

        void UpdatePreview()
        {
            if (!hasCurrentHit || placementPrefab == null)
            {
                HidePreview();
                return;
            }

            if (previewInstance == null)
                CreatePreview();

            if (previewInstance == null)
                return;

            Quaternion rotation = CalculatePlacementRotation(previewInstance);
            Vector3 scale = CalculatePlacementScale(previewInstance);

            if (!previewPoseInitialized || previewPositionSmoothTime <= 0f)
            {
                previewInstance.transform.localScale = scale;
                previewSurfaceYOffset = CalculateSurfaceYOffset(previewInstance);
                Vector3 position = CalculatePlacementCenter() + Vector3.up * previewSurfaceYOffset;
                previewInstance.transform.SetPositionAndRotation(position, rotation);
                previewPoseInitialized = true;
            }
            else
            {
                float scaleT = previewScaleSpeed <= 0f
                    ? 1f
                    : 1f - Mathf.Exp(-previewScaleSpeed * Time.deltaTime);
                previewInstance.transform.localScale = Vector3.Lerp(
                    previewInstance.transform.localScale,
                    scale,
                    scaleT);
                previewSurfaceYOffset = CalculateSurfaceYOffset(previewInstance);
                Vector3 position = CalculatePlacementCenter() + Vector3.up * previewSurfaceYOffset;
                previewInstance.transform.position = Vector3.SmoothDamp(
                    previewInstance.transform.position,
                    position,
                    ref previewVelocity,
                    previewPositionSmoothTime);
                float t = previewRotationSpeed <= 0f
                    ? 1f
                    : 1f - Mathf.Exp(-previewRotationSpeed * Time.deltaTime);
                previewInstance.transform.rotation = Quaternion.Slerp(
                    previewInstance.transform.rotation,
                    rotation,
                    t);
            }

            if (!previewInstance.activeSelf)
                previewInstance.SetActive(true);
        }

        void CreatePreview()
        {
            ClearPreviewCaches();

            previewInstance = Instantiate(placementPrefab);
            previewInstance.name = placementPrefab.name + " Preview";

            ApplyTrackMap(previewInstance);
            DisablePreviewBehaviours(previewInstance);
            ApplyPreviewMaterial(previewInstance);

            if (previewInstance.GetComponentsInChildren<Renderer>(includeInactive: true).Length == 0)
                Debug.LogWarning("[TrackRevealPlacer] Preview renderer를 찾지 못했습니다.", this);

            previewInstance.SetActive(false);
        }

        float CalculateSurfaceYOffset(GameObject target)
        {
            if (target == null)
                return surfaceOffset;

            Vector3 position = target.transform.position;
            Quaternion rotation = target.transform.rotation;
            target.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            bool hasBounds = false;
            Bounds bounds = default;
            foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                if (renderer == null || !renderer.enabled)
                    continue;

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            target.transform.SetPositionAndRotation(position, rotation);
            return hasBounds ? surfaceOffset - bounds.min.y : surfaceOffset;
        }

        void DisablePreviewBehaviours(GameObject target)
        {
            foreach (Collider col in target.GetComponentsInChildren<Collider>(includeInactive: true))
            {
                if (!col.enabled)
                    continue;

                previewDisabledColliders.Add(col);
                col.enabled = false;
            }

            foreach (Animator animator in target.GetComponentsInChildren<Animator>(includeInactive: true))
            {
                if (!animator.enabled)
                    continue;

                previewDisabledBehaviours.Add(animator);
                animator.enabled = false;
            }

            foreach (MonoBehaviour behaviour in target.GetComponentsInChildren<MonoBehaviour>(includeInactive: true))
            {
                if (!behaviour.enabled)
                    continue;

                if (behaviour is F1XR.RestAPI.Replay.Track.TrackMapView || behaviour is TrackRevealView)
                    continue;

                previewDisabledBehaviours.Add(behaviour);
                behaviour.enabled = false;
            }

            ConfigurePhysics(target);
        }

        void RestorePreviewForRealUse(GameObject target)
        {
            RestorePreviewMaterials(target);

            foreach (Collider col in previewDisabledColliders)
            {
                if (col != null)
                    col.enabled = true;
            }

            foreach (Behaviour behaviour in previewDisabledBehaviours)
            {
                if (behaviour != null)
                    behaviour.enabled = true;
            }

            previewDisabledColliders.Clear();
            previewDisabledBehaviours.Clear();

            ConfigurePhysics(target);
        }

        void ApplyPreviewMaterial(GameObject target)
        {
            Material material = previewMaterial != null ? previewMaterial : GetOrCreatePreviewMaterial();

            if (material == null)
                return;

            foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                if (!previewOriginalMaterials.ContainsKey(renderer))
                    previewOriginalMaterials.Add(renderer, renderer.sharedMaterials);

                int materialCount = Mathf.Max(1, renderer.sharedMaterials.Length);
                Material[] materials = new Material[materialCount];

                for (int i = 0; i < materials.Length; i++)
                    materials[i] = material;

                renderer.sharedMaterials = materials;
            }
        }

        void RestorePreviewMaterials(GameObject target)
        {
            foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                if (renderer != null &&
                    previewOriginalMaterials.TryGetValue(renderer, out Material[] originalMaterials))
                {
                    renderer.sharedMaterials = originalMaterials;
                }
            }

            previewOriginalMaterials.Clear();
        }

        Material GetOrCreatePreviewMaterial()
        {
            if (runtimePreviewMaterial != null)
                return runtimePreviewMaterial;

            Shader shader = Shader.Find("F1XR/PreviewTransparentURP");

            if (shader == null)
            {
                Debug.LogError("[TrackRevealPlacer] Shader 'F1XR/PreviewTransparentURP'를 찾지 못했습니다.", this);
                return null;
            }

            runtimePreviewMaterial = new Material(shader);
            runtimePreviewMaterial.SetColor("_BaseColor", previewColor);
            runtimePreviewMaterial.renderQueue = 3000;

            return runtimePreviewMaterial;
        }

        void HidePreview()
        {
            if (previewInstance != null && previewInstance.activeSelf)
                previewInstance.SetActive(false);
        }

        void ClearPreview()
        {
            if (previewInstance != null)
            {
                Destroy(previewInstance);
                previewInstance = null;
            }

            previewPoseInitialized = false;
            previewVelocity = Vector3.zero;
            ResetPlacementRotation();
            ClearPreviewCaches();
        }

        void ClearPreviewCaches()
        {
            previewOriginalMaterials.Clear();
            previewDisabledBehaviours.Clear();
            previewDisabledColliders.Clear();
        }
    }
}
