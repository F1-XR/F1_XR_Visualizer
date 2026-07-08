using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace F1XR
{
    [ExecuteAlways]
    [RequireComponent(typeof(Renderer))]
    public sealed class PlanarReflection : MonoBehaviour
    {
        [SerializeField] Renderer floorRenderer;
        [SerializeField] Transform reflectionPlane;
        [SerializeField] LayerMask reflectionMask = ~0;
        [SerializeField] int textureSize = 512;
        [SerializeField] int updateEveryFrames = 1;
        [SerializeField] float clipPlaneOffset = 0.04f;
        [SerializeField] bool useObliqueClipPlane;
        [SerializeField, Range(0f, 1f)] float reflectionStrength = 0.68f;
        [SerializeField] string textureProperty = "_PlanarReflectionTex";
        [SerializeField] bool logDiagnostics;

        static readonly int ReflectionTexId = Shader.PropertyToID("_PlanarReflectionTex");
        static readonly int ReflectionViewProjectionId = Shader.PropertyToID("_ReflectionViewProjection");
        static readonly int ReflectionStrengthId = Shader.PropertyToID("_ReflectionStrength");

        Camera reflectionCamera;
        RenderTexture reflectionTexture;
        int lastRenderFrame = -1;
        bool isRendering;
        bool loggedFirstRender;

        void OnEnable()
        {
            if (floorRenderer == null)
                floorRenderer = GetComponent<Renderer>();

            if (reflectionPlane == null && floorRenderer != null)
                reflectionPlane = floorRenderer.transform;

            RenderPipelineManager.beginCameraRendering += RenderBeforeCamera;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= RenderBeforeCamera;
            ReleaseResources();
        }

        void RenderBeforeCamera(ScriptableRenderContext context, Camera sourceCamera)
        {
            if (!CanRender(sourceCamera))
                return;

            int interval = Mathf.Max(1, updateEveryFrames);
            if (Application.isPlaying && Time.frameCount - lastRenderFrame < interval)
                return;

            RenderReflection(context, sourceCamera);
            lastRenderFrame = Time.frameCount;
        }

        bool CanRender(Camera sourceCamera)
        {
            if (!enabled || isRendering || sourceCamera == null || sourceCamera == reflectionCamera)
                return false;

            if (sourceCamera.cameraType == CameraType.Preview || sourceCamera.cameraType == CameraType.Reflection)
                return false;

            if (Application.isPlaying
                && sourceCamera.cameraType != CameraType.Game
                && sourceCamera.cameraType != CameraType.VR)
                return false;

            if (floorRenderer == null)
            {
                LogDiagnostic("Floor renderer is missing.");
                return false;
            }

            Material material = floorRenderer.sharedMaterial;
            if (material == null)
            {
                LogDiagnostic("Floor material is missing.");
                return false;
            }

            if (!material.HasProperty(textureProperty))
            {
                LogDiagnostic($"Floor material '{material.name}' does not have '{textureProperty}'.");
                return false;
            }

            return true;
        }

        void RenderReflection(ScriptableRenderContext context, Camera sourceCamera)
        {
            EnsureResources(sourceCamera);

            Transform planeTransform = reflectionPlane != null ? reflectionPlane : floorRenderer.transform;
            Vector3 floorPosition = planeTransform.position;
            Vector3 floorNormal = planeTransform.up.normalized;
            float planeOffset = -Vector3.Dot(floorNormal, floorPosition);
            Vector4 planeEquation = new Vector4(floorNormal.x, floorNormal.y, floorNormal.z, planeOffset);

            Matrix4x4 reflectionMatrix = Matrix4x4.identity;
            CalculateReflectionMatrix(ref reflectionMatrix, planeEquation);

            reflectionCamera.CopyFrom(sourceCamera);
            reflectionCamera.enabled = false;
            reflectionCamera.clearFlags = CameraClearFlags.SolidColor;
            reflectionCamera.backgroundColor = Color.black;
            reflectionCamera.targetTexture = reflectionTexture;
            reflectionCamera.cullingMask = reflectionMask;
            reflectionCamera.stereoTargetEye = StereoTargetEyeMask.None;
            reflectionCamera.useOcclusionCulling = false;

            Vector3 reflectedPosition = reflectionMatrix.MultiplyPoint(sourceCamera.transform.position);
            reflectionCamera.transform.position = reflectedPosition;
            reflectionCamera.transform.rotation = sourceCamera.transform.rotation;
            reflectionCamera.worldToCameraMatrix = sourceCamera.worldToCameraMatrix * reflectionMatrix;

            if (useObliqueClipPlane)
            {
                Vector4 cameraSpaceClipPlane = CameraSpacePlane(reflectionCamera, floorPosition, floorNormal, 1f);
                reflectionCamera.projectionMatrix = reflectionCamera.CalculateObliqueMatrix(cameraSpaceClipPlane);
            }

            Material material = floorRenderer.sharedMaterial;
            material.SetTexture(ReflectionTexId, reflectionTexture);
            Matrix4x4 gpuProjection = GL.GetGPUProjectionMatrix(reflectionCamera.projectionMatrix, true);
            material.SetMatrix(ReflectionViewProjectionId, gpuProjection * reflectionCamera.worldToCameraMatrix);
            material.SetFloat(ReflectionStrengthId, reflectionStrength);
            LogFirstRender(sourceCamera, material);

            bool floorWasEnabled = floorRenderer.enabled;
            isRendering = true;

            try
            {
                floorRenderer.enabled = false;
                GL.invertCulling = true;
                UniversalRenderPipeline.RenderSingleCamera(context, reflectionCamera);
            }
            finally
            {
                GL.invertCulling = false;
                floorRenderer.enabled = floorWasEnabled;
                isRendering = false;
            }
        }

        void EnsureResources(Camera sourceCamera)
        {
            int size = Mathf.Clamp(textureSize, 128, 1024);

            if (reflectionTexture == null || reflectionTexture.width != size || reflectionTexture.height != size)
            {
                if (reflectionTexture != null)
                    ReleaseTexture();

                reflectionTexture = new RenderTexture(size, size, 16, RenderTextureFormat.ARGB32)
                {
                    name = "Planar Floor Reflection",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    useMipMap = false,
                    autoGenerateMips = false
                };
                reflectionTexture.Create();
                LogDiagnostic($"Created reflection texture {size}x{size}.");
            }

            if (reflectionCamera != null)
                return;

            GameObject cameraObject = new GameObject("Planar Reflection Camera")
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            reflectionCamera = cameraObject.AddComponent<Camera>();
            reflectionCamera.CopyFrom(sourceCamera);
            reflectionCamera.enabled = false;
            LogDiagnostic($"Created reflection camera from '{sourceCamera.name}'.");
        }

        void LogFirstRender(Camera sourceCamera, Material material)
        {
            if (loggedFirstRender)
                return;

            loggedFirstRender = true;
            LogDiagnostic(
                $"Rendering '{sourceCamera.name}' into '{reflectionTexture.name}' and assigning it to '{material.name}.{textureProperty}'.");
        }

        void LogDiagnostic(string message)
        {
            if (logDiagnostics)
                Debug.Log($"[PlanarReflection] {message}", this);
        }

        Vector4 CameraSpacePlane(Camera camera, Vector3 position, Vector3 normal, float sideSign)
        {
            Vector3 offsetPosition = position + normal * clipPlaneOffset;
            Matrix4x4 worldToCamera = camera.worldToCameraMatrix;
            Vector3 cameraPosition = worldToCamera.MultiplyPoint(offsetPosition);
            Vector3 cameraNormal = worldToCamera.MultiplyVector(normal).normalized * sideSign;
            return new Vector4(cameraNormal.x, cameraNormal.y, cameraNormal.z, -Vector3.Dot(cameraPosition, cameraNormal));
        }

        static void CalculateReflectionMatrix(ref Matrix4x4 matrix, Vector4 plane)
        {
            matrix.m00 = 1f - 2f * plane[0] * plane[0];
            matrix.m01 = -2f * plane[0] * plane[1];
            matrix.m02 = -2f * plane[0] * plane[2];
            matrix.m03 = -2f * plane[3] * plane[0];

            matrix.m10 = -2f * plane[1] * plane[0];
            matrix.m11 = 1f - 2f * plane[1] * plane[1];
            matrix.m12 = -2f * plane[1] * plane[2];
            matrix.m13 = -2f * plane[3] * plane[1];

            matrix.m20 = -2f * plane[2] * plane[0];
            matrix.m21 = -2f * plane[2] * plane[1];
            matrix.m22 = 1f - 2f * plane[2] * plane[2];
            matrix.m23 = -2f * plane[3] * plane[2];

            matrix.m30 = 0f;
            matrix.m31 = 0f;
            matrix.m32 = 0f;
            matrix.m33 = 1f;
        }

        void ReleaseResources()
        {
            ReleaseTexture();

            if (reflectionCamera == null)
                return;

            if (Application.isPlaying)
                Destroy(reflectionCamera.gameObject);
            else
                DestroyImmediate(reflectionCamera.gameObject);

            reflectionCamera = null;
        }

        void ReleaseTexture()
        {
            if (reflectionTexture == null)
                return;

            reflectionTexture.Release();

            if (Application.isPlaying)
                Destroy(reflectionTexture);
            else
                DestroyImmediate(reflectionTexture);

            reflectionTexture = null;
        }
    }
}
