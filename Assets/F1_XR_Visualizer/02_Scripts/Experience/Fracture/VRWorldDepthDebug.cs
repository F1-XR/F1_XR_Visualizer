using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace F1XR.Experience.Fracture
{
    /// <summary>
    /// Step 1 of the VR to MR world-space fracture, and only step 1: it proves that a world
    /// position can be recovered for every VR pixel, on the headset, in both eyes.
    ///
    /// Nothing is fractured and no framebuffer alpha is written. Turn it on while standing in
    /// VR and the world is repainted as a metre grid derived from reconstructed positions.
    /// The question it answers is whether that grid stays welded to the geometry when the
    /// head moves. If it does, a fracture field written in world space will be world-locked
    /// for free, which is the whole reason for this route. If it swims, nothing built on top
    /// of it can be trusted and there is no point writing the fracture shader.
    ///
    /// The depth texture is turned on per camera rather than on the pipeline asset, so no
    /// project-wide setting is edited and the exact previous value goes back afterwards.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VRWorldDepthDebug : MonoBehaviour
    {
        [SerializeField] Camera xrCamera;

        [Tooltip("Size of one colour cell of the world grid, in metres. Smaller reveals " +
            "small reconstruction errors; larger is easier to read at a glance.")]
        [SerializeField, Range(0.05f, 5f)] float gridMetres = 1f;

        static readonly int GridMetresId = Shader.PropertyToID("_GridMetres");

        Material material;
        Mesh mesh;
        GameObject overlay;

        UniversalAdditionalCameraData cameraData;
        bool previousRequiresDepthTexture;
        bool depthTextureForced;

        public bool IsActive => overlay != null;

        public void Toggle() => SetActive(!IsActive);

        public void SetActive(bool active)
        {
            if (active == IsActive)
                return;

            if (active)
                Begin();
            else
                End();
        }

        [ContextMenu("Toggle Depth Debug")]
        void ToggleFromMenu() => Toggle();

        void OnDisable() => End();

        void OnDestroy() => End();

        void Begin()
        {
            if (!ResolveCamera())
                return;

            Shader shader = Shader.Find("F1XR/VRWorldDepthDebug");
            if (shader == null)
            {
                Debug.LogError(
                    "[VRDepthDebug] F1XR/VRWorldDepthDebug shader missing. It has to be in " +
                    "a build for the headset, so check it is not stripped.",
                    this);
                return;
            }

            // Per camera, not the pipeline asset: the asset is shared by every camera and by
            // the editor, and a debug toggle has no business editing it.
            previousRequiresDepthTexture = cameraData.requiresDepthTexture;
            cameraData.requiresDepthTexture = true;
            depthTextureForced = true;

            material = new Material(shader) { name = "VRWorldDepthDebug" };
            material.SetFloat(GridMetresId, gridMetres);

            mesh = BuildClipSpaceTriangle();

            overlay = new GameObject("VRWorldDepthDebugOverlay");
            overlay.transform.SetParent(transform, false);
            overlay.AddComponent<MeshFilter>().sharedMesh = mesh;

            MeshRenderer renderer = overlay.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            Debug.Log(
                $"[VRDepthDebug] ON camera={xrCamera.name} " +
                $"depthTexture was={previousRequiresDepthTexture} now=True " +
                $"grid={gridMetres}m",
                this);
        }

        void End()
        {
            if (depthTextureForced && cameraData != null)
            {
                cameraData.requiresDepthTexture = previousRequiresDepthTexture;
                Debug.Log(
                    $"[VRDepthDebug] OFF depthTexture restored to " +
                    $"{previousRequiresDepthTexture}.",
                    this);
            }

            depthTextureForced = false;

            DestroySafely(overlay);
            DestroySafely(material);
            DestroySafely(mesh);

            overlay = null;
            material = null;
            mesh = null;
        }

        bool ResolveCamera()
        {
            if (xrCamera == null)
                xrCamera = Camera.main;

            if (xrCamera == null)
            {
                Debug.LogError(
                    "[VRDepthDebug] No camera assigned and Camera.main is missing.", this);
                return false;
            }

            cameraData = xrCamera.GetUniversalAdditionalCameraData();
            if (cameraData == null)
            {
                Debug.LogError(
                    $"[VRDepthDebug] {xrCamera.name} has no URP camera data.", this);
                return false;
            }

            return true;
        }

        /// <summary>
        /// A triangle whose vertex positions are already clip-space coordinates; the shader
        /// uses them untransformed. Bounds are made enormous so frustum culling, which has no
        /// idea this thing ignores its transform, never removes it.
        /// </summary>
        static Mesh BuildClipSpaceTriangle()
        {
            var built = new Mesh { name = "VRWorldDepthDebugTriangle" };
            built.SetVertices(new[]
            {
                new Vector3(-1f, -1f, 0f),
                new Vector3(3f, -1f, 0f),
                new Vector3(-1f, 3f, 0f)
            });
            built.SetTriangles(new[] { 0, 1, 2 }, 0, false);
            built.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);
            return built;
        }

        static void DestroySafely(Object target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }
    }
}
