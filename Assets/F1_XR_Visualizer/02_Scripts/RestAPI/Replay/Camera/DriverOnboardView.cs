using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace F1XR.RestAPI.Replay
{
    public class DriverOnboardView : MonoBehaviour
    {
        public int textureWidth = 512;
        public int textureHeight = 288;
        public float fieldOfView = 72f;
        public float nearClip = 0.01f;
        public float farClip = 120f;
        public float heightAboveCarHeights = 2f;
        public Vector3 localOffset = Vector3.zero;
        public float pitchDownDegrees = 10f;
        public bool smoothPose = true;
        public float positionLerpSpeed = 22f;
        public float rotationLerpSpeed = 24f;

        private Camera onboardCamera;
        private RenderTexture texture;
        private RawImage output;
        private Transform target;
        private bool hasSmoothedPose;
        private float poseScale = 1f;
        private bool hidingOverlayRenderers;
        private readonly List<ReplayCarView> onboardCars = new();
        private readonly List<Renderer> onboardHiddenRenderers = new();
        private readonly List<RendererState> hiddenRendererStates = new();

        public RenderTexture Texture => texture;

        private struct RendererState
        {
            public Renderer renderer;
            public bool enabled;
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            RestoreOnboardHiddenRenderers();
        }

        public void SetOutput(RawImage image, int width, int height)
        {
            output = image;
            textureWidth = Mathf.Max(16, width);
            textureHeight = Mathf.Max(16, height);
            EnsureTexture();

            if (output != null)
                output.texture = texture;
        }

        public void Show(Transform targetCar)
        {
            bool targetChanged = target != targetCar;
            target = targetCar;
            EnsureCamera();
            EnsureTexture();

            if (output != null)
                output.texture = texture;

            if (onboardCamera != null)
                onboardCamera.enabled = target != null;

            enabled = target != null;
            if (target != null)
                UpdateCameraPose(targetChanged);
        }

        public void Hide()
        {
            target = null;
            enabled = false;
            hasSmoothedPose = false;

            if (onboardCamera != null)
                onboardCamera.enabled = false;
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                Hide();
                return;
            }

            UpdateCameraPose(false);
        }

        private void OnDestroy()
        {
            RestoreOnboardHiddenRenderers();

            if (onboardCamera != null)
                onboardCamera.targetTexture = null;

            if (texture != null)
            {
                texture.Release();
                Destroy(texture);
            }
        }

        private void EnsureCamera()
        {
            if (onboardCamera != null)
                return;

            GameObject obj = new GameObject("DriverOnboardCamera");
            obj.transform.SetParent(transform, false);
            onboardCamera = obj.AddComponent<Camera>();
            onboardCamera.enabled = false;
            onboardCamera.clearFlags = CameraClearFlags.SolidColor;
            onboardCamera.backgroundColor = new Color(0.01f, 0.012f, 0.016f, 1f);
            onboardCamera.fieldOfView = fieldOfView;
            onboardCamera.nearClipPlane = nearClip;
            onboardCamera.farClipPlane = farClip;
            onboardCamera.allowHDR = false;
            onboardCamera.allowMSAA = false;
            onboardCamera.depth = -10f;
            onboardCamera.cullingMask = DefaultCullingMask();
            onboardCamera.stereoTargetEye = StereoTargetEyeMask.None;

            if (texture != null)
                onboardCamera.targetTexture = texture;
        }

        private void EnsureTexture()
        {
            if (texture != null && texture.width == textureWidth && texture.height == textureHeight)
                return;

            if (onboardCamera != null)
                onboardCamera.targetTexture = null;

            if (texture != null)
            {
                texture.Release();
                Destroy(texture);
            }

            texture = new RenderTexture(textureWidth, textureHeight, 16, RenderTextureFormat.ARGB32)
            {
                name = "DriverOnboardTexture",
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false,
            };
            texture.Create();

            if (onboardCamera != null)
                onboardCamera.targetTexture = texture;

            if (output != null)
                output.texture = texture;
        }

        private void UpdateCameraPose(bool snap)
        {
            EnsureCamera();

            if (target == null || onboardCamera == null)
                return;

            Transform cameraTransform = onboardCamera.transform;
            GetWorldViewPose(target, out Vector3 targetPosition, out Quaternion targetRotation, out poseScale);

            if (!smoothPose || snap || !hasSmoothedPose)
            {
                cameraTransform.SetPositionAndRotation(targetPosition, targetRotation);
                hasSmoothedPose = true;
            }
            else
            {
                float deltaTime = Time.deltaTime;
                cameraTransform.SetPositionAndRotation(
                    Vector3.Lerp(cameraTransform.position, targetPosition, GetLerpT(positionLerpSpeed, deltaTime)),
                    Quaternion.Slerp(cameraTransform.rotation, targetRotation, GetLerpT(rotationLerpSpeed, deltaTime)));
            }

            onboardCamera.fieldOfView = fieldOfView;
            onboardCamera.nearClipPlane = Mathf.Max(0.0001f, nearClip * poseScale);
            onboardCamera.farClipPlane = Mathf.Max(
                onboardCamera.nearClipPlane + 0.001f,
                Mathf.Max(10f, farClip * poseScale));
        }

        private void GetWorldViewPose(Transform car, out Vector3 position, out Quaternion rotation, out float scale)
        {
            scale = GetTransformScale(car);
            Vector3 up = car.up;

            if (TryGetLocalBodyBounds(car, out Bounds bounds))
            {
                float height = Mathf.Max(bounds.size.y, 0.0001f);
                Vector3 localPosition = new Vector3(
                    bounds.center.x,
                    bounds.max.y + height * heightAboveCarHeights,
                    bounds.center.z
                ) + localOffset;

                position = car.TransformPoint(localPosition);
            }
            else
            {
                position = car.TransformPoint(new Vector3(0f, 2f, 0f) + localOffset);
            }

            Vector3 forward = car.forward;
            float pitchRadians = Mathf.Clamp(pitchDownDegrees, -89f, 89f) * Mathf.Deg2Rad;
            Vector3 direction = forward * Mathf.Cos(pitchRadians) - up * Mathf.Sin(pitchRadians);

            if (direction.sqrMagnitude < 0.0001f)
                direction = car.forward;

            rotation = Quaternion.LookRotation(direction.normalized, up);
        }

        private static bool TryGetLocalBodyBounds(Transform root, out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);

            foreach (Renderer renderer in renderers)
            {
                if (!IsBodyRenderer(renderer))
                    continue;

                Bounds localBounds = WorldBoundsToLocal(root, renderer.bounds);
                if (!found)
                {
                    bounds = localBounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(localBounds.min);
                    bounds.Encapsulate(localBounds.max);
                }
            }

            return found;
        }

        private static bool IsBodyRenderer(Renderer renderer)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                return false;

            if (renderer is LineRenderer || renderer.GetComponent<TextMesh>() != null)
                return false;

            if (renderer.GetComponentInParent<Canvas>() != null)
                return false;

            Transform current = renderer.transform;
            while (current != null)
            {
                string objectName = current.name;
                if (objectName.StartsWith("DriverLabel") ||
                    objectName.StartsWith("SelectionFx") ||
                    objectName.StartsWith("GroundRing") ||
                    objectName.StartsWith("SelectionPulse") ||
                    objectName.StartsWith("SelectedCar"))
                    return false;

                if (current == renderer.transform.root || current.GetComponent<ReplayCarView>() != null)
                    break;

                current = current.parent;
            }

            return true;
        }

        private static Bounds WorldBoundsToLocal(Transform root, Bounds worldBounds)
        {
            Vector3 min = root.InverseTransformPoint(worldBounds.center);
            Vector3 max = min;
            Vector3 extents = worldBounds.extents;

            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 worldPoint = worldBounds.center + Vector3.Scale(extents, new Vector3(x, y, z));
                        Vector3 localPoint = root.InverseTransformPoint(worldPoint);
                        min = Vector3.Min(min, localPoint);
                        max = Vector3.Max(max, localPoint);
                    }
                }
            }

            return new Bounds((min + max) * 0.5f, max - min);
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera == onboardCamera)
                HideOnboardOverlayRenderers();
        }

        private void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera == onboardCamera)
                RestoreOnboardHiddenRenderers();
        }

        private void HideOnboardOverlayRenderers()
        {
            if (hidingOverlayRenderers || target == null)
                return;

            hidingOverlayRenderers = true;
            onboardHiddenRenderers.Clear();
            hiddenRendererStates.Clear();

            Transform root = target.parent != null ? target.parent : target;
            onboardCars.Clear();
            root.GetComponentsInChildren(true, onboardCars);

            foreach (ReplayCarView car in onboardCars)
                car.CollectOnboardHiddenRenderers(onboardHiddenRenderers);

            foreach (Renderer renderer in onboardHiddenRenderers)
            {
                if (renderer == null || !renderer.enabled)
                    continue;

                hiddenRendererStates.Add(new RendererState
                {
                    renderer = renderer,
                    enabled = renderer.enabled
                });
                renderer.enabled = false;
            }
        }

        private void RestoreOnboardHiddenRenderers()
        {
            if (!hidingOverlayRenderers)
                return;

            foreach (RendererState state in hiddenRendererStates)
            {
                if (state.renderer != null)
                    state.renderer.enabled = state.enabled;
            }

            hiddenRendererStates.Clear();
            onboardHiddenRenderers.Clear();
            hidingOverlayRenderers = false;
        }

        private static float GetTransformScale(Transform target)
        {
            Vector3 scale = target.lossyScale;
            return Mathf.Max(0.0001f, Mathf.Max(
                Mathf.Abs(scale.x),
                Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z))));
        }

        private static int DefaultCullingMask()
        {
            int uiLayer = LayerMask.NameToLayer("UI");
            return uiLayer >= 0 ? ~(1 << uiLayer) : ~0;
        }

        private static float GetLerpT(float speed, float deltaTime)
        {
            return speed <= 0f ? 1f : 1f - Mathf.Exp(-speed * deltaTime);
        }
    }
}
