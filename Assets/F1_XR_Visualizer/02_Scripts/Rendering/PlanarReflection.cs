using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace F1XR.Rendering
{
    [ExecuteAlways]
    [RequireComponent(typeof(Renderer))]
    public sealed class PlanarReflection : MonoBehaviour
    {
        [SerializeField] Renderer floorRenderer;
        [SerializeField] Transform reflectionPlane;
        [SerializeField] LayerMask reflectionMask = ~0;
        [SerializeField] int textureSize = 2048;
        [SerializeField] int updateEveryFrames = 1;
        [SerializeField] float clipPlaneOffset = 0.04f;
        [SerializeField] bool useObliqueClipPlane;
        [SerializeField, Range(0f, 1f)] float reflectionStrength = 0.68f;
        [SerializeField] string textureProperty = "_PlanarReflectionTex";
        [SerializeField] bool logDiagnostics;
        [SerializeField] bool disableOnMobile = true;

        static readonly int ReflectionTexId = Shader.PropertyToID("_PlanarReflectionTex");
        static readonly int ReflectionViewProjectionId = Shader.PropertyToID("_ReflectionViewProjection");
        static readonly int ReflectionStrengthId = Shader.PropertyToID("_ReflectionStrength");
        static readonly int PlanarBlendId = Shader.PropertyToID("_PlanarBlend");

        Camera reflectionCamera;
        RenderTexture reflectionTexture;
        Camera lastSourceCamera;
        int lastRenderFrame = -1;
        bool isRendering;
        bool loggedFirstRender;

        void OnEnable()
        {
            if (floorRenderer == null)
                floorRenderer = GetComponent<Renderer>();

            if (reflectionPlane == null && floorRenderer != null)
                reflectionPlane = floorRenderer.transform;

            if (disableOnMobile && Application.isMobilePlatform)
            {
                // Skip the per-frame planar re-render entirely on mobile/standalone XR and let
                // the floor shader fall back to the reflection probe (see _PlanarBlend in
                // PlanarReflectiveFloorURP.shader) instead of paying for a second scene render.
                ForcePlanarBlend(0f);
                LogDiagnostic("Mobile platform detected — planar camera render disabled, using reflection probe only.");
                return;
            }

            RenderPipelineManager.beginCameraRendering += RenderBeforeCamera;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= RenderBeforeCamera;
            ReleaseResources();
        }

        void ForcePlanarBlend(float blend)
        {
            Material material = floorRenderer != null ? floorRenderer.sharedMaterial : null;
            if (material != null && material.HasProperty(PlanarBlendId))
                material.SetFloat(PlanarBlendId, blend);
        }

        void RenderBeforeCamera(ScriptableRenderContext context, Camera sourceCamera)
        {
            if (!CanRender(sourceCamera))
                return;

            // The throttle may only reuse a reflection for the camera it was rendered from. A planar
            // reflection is valid for exactly one viewpoint -- its mirror -- so handing camera A's
            // render to camera B projects the image from the wrong eye and the reflection slides
            // across the floor as that camera moves. Any second camera in the same frame (this scene
            // has a Main Camera at the origin alongside the XR camera) must get its own render.
            int interval = Mathf.Max(1, updateEveryFrames);
            if (Application.isPlaying
                && sourceCamera == lastSourceCamera
                && Time.frameCount - lastRenderFrame < interval)
                return;

            RenderReflection(context, sourceCamera);
            lastRenderFrame = Time.frameCount;
            lastSourceCamera = sourceCamera;
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
            reflectionCamera.useOcclusionCulling = false;
            // CopyFrom pulls the source camera's XR state across, so re-assert this every frame.
            // Without it URP renders the reflection camera as an XR camera and overwrites the
            // mirrored worldToCameraMatrix below with the head pose, putting the live view -- not
            // the reflection -- into the RT. Camera.stereoTargetEye does NOT work here: URP logs
            // "You can use Camera.stereoTargetEye only with the built-in renderer" and ignores it.
            reflectionCamera.GetUniversalAdditionalCameraData().allowXRRendering = false;

            // Mirror the orientation too, not just the position. URP renders from the camera
            // transform, so copying the source rotation unchanged pointed the reflection camera the
            // same way as the eye from below the floor -- not a mirror view at all -- and the RT
            // ended up holding geometry that does not match worldToCameraMatrix. The reflection then
            // slides across the floor as the eye moves instead of staying on the mirrored object.
            Vector3 reflectedPosition = reflectionMatrix.MultiplyPoint(sourceCamera.transform.position);
            Vector3 reflectedForward = Vector3.Reflect(sourceCamera.transform.forward, floorNormal);
            Vector3 reflectedUp = Vector3.Reflect(sourceCamera.transform.up, floorNormal);
            reflectionCamera.transform.SetPositionAndRotation(
                reflectedPosition, Quaternion.LookRotation(reflectedForward, reflectedUp));
            reflectionCamera.worldToCameraMatrix = sourceCamera.worldToCameraMatrix * reflectionMatrix;

            if (useObliqueClipPlane)
            {
                Vector4 cameraSpaceClipPlane = CameraSpacePlane(reflectionCamera, floorPosition, floorNormal, 1f);
                reflectionCamera.projectionMatrix = reflectionCamera.CalculateObliqueMatrix(cameraSpaceClipPlane);
            }

            Material material = floorRenderer.sharedMaterial;
            // renderIntoTexture:false on purpose. This matrix is used to *sample* the reflection, not
            // to render it, and SAMPLE_TEXTURE2D reads with a bottom-left origin. Passing true bakes
            // in the render-target Y flip and the floor then samples the reflection upside down,
            // which is what the removed ComputeScreenPos call used to paper over -- badly, since
            // _ProjectionParams.x describes whichever camera is drawing the floor, not this RT.
            Matrix4x4 gpuProjection = GL.GetGPUProjectionMatrix(reflectionCamera.projectionMatrix, false);

            // The texture is a Properties-block entry, so it is material-scoped: a global would be
            // ignored and the shader would fall back to the "black" default. The matrix is not a
            // material property, and the SRP Batcher only uploads what lives in UnityPerMaterial,
            // so Material.SetMatrix on it is silently dropped -> matrix zero -> uv 0 -> edgeFade 0
            // -> probe-only floor. Each one has to go through the path that actually reaches it.
            material.SetTexture(ReflectionTexId, reflectionTexture);
            Shader.SetGlobalMatrix(ReflectionViewProjectionId, gpuProjection * reflectionCamera.worldToCameraMatrix);
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
            int size = Mathf.Clamp(textureSize, 128, 2048);

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
