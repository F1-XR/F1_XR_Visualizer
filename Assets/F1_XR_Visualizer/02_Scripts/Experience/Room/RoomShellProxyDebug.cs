using UnityEngine;
using UnityEngine.Rendering;

namespace F1XR.Experience.Room
{
    /// <summary>
    /// Step 2 verification only: shows the generated proxies in a translucent colour per
    /// surface type and draws their inward normals, so their position, size and rotation
    /// can be checked against the real room. These are not the final materials.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RoomShellProxyDebug : MonoBehaviour
    {
        [SerializeField] RoomShellProxyGenerator generator;

        [Tooltip("On by default so the proxies can be checked against the real room as " +
            "soon as play starts. They are room sized and the viewer stands inside them, " +
            "so use the Toggle Proxy Debug button to get them out of the way before " +
            "looking at anything else.")]
        [SerializeField] bool debugVisible = true;

        [SerializeField] Color wallColor = new(0.20f, 0.55f, 1f, 0.25f);
        [SerializeField] Color floorColor = new(0.20f, 1f, 0.45f, 0.25f);
        [SerializeField] Color ceilingColor = new(1f, 0.55f, 0.15f, 0.25f);

        [Tooltip("Length of the normal ray drawn in the Scene view.")]
        [SerializeField] float normalGizmoLength = 0.5f;

        Material wallMaterial;
        Material floorMaterial;
        Material ceilingMaterial;

        public bool DebugVisible => debugVisible;

        void Awake()
        {
            if (generator == null)
                generator = FindAnyObjectByType<RoomShellProxyGenerator>();
        }

        void OnEnable()
        {
            if (generator != null)
                generator.ProxiesBuilt += ApplyVisualization;

            ApplyVisualization();
        }

        void OnDisable()
        {
            if (generator != null)
                generator.ProxiesBuilt -= ApplyVisualization;
        }

        void OnDestroy()
        {
            DestroyMaterial(ref wallMaterial);
            DestroyMaterial(ref floorMaterial);
            DestroyMaterial(ref ceilingMaterial);
        }

        public void SetDebugVisible(bool visible)
        {
            debugVisible = visible;
            ApplyVisualization();
        }

        [ContextMenu("Rebuild Room Proxies")]
        public void RebuildRoomProxies()
        {
            generator?.BuildRoomProxies();
        }

        [ContextMenu("Clear Room Proxies")]
        public void ClearRoomProxies()
        {
            generator?.ClearRoomProxies();
        }

        [ContextMenu("Toggle Proxy Debug")]
        public void ToggleProxyDebug()
        {
            SetDebugVisible(!debugVisible);
            Debug.Log($"[RoomShell] Proxy debug visualization = {debugVisible}", this);
        }

        [ContextMenu("Apply Offset 0")]
        public void ApplyOffsetZero() => ApplyOffset(0f);

        [ContextMenu("Apply Offset 0.005")]
        public void ApplyOffsetSmall() => ApplyOffset(0.005f);

        [ContextMenu("Apply Offset 0.02")]
        public void ApplyOffsetLarge() => ApplyOffset(0.02f);

        void ApplyOffset(float value)
        {
            if (generator == null)
            {
                Debug.LogWarning("[RoomShell] No generator assigned.", this);
                return;
            }

            generator.SetSurfaceOffset(value);

            foreach (RoomShellProxy proxy in generator.Proxies)
            {
                if (proxy.GameObject == null)
                    continue;

                Debug.Log(
                    $"[RoomShell][Offset {value:F3}] {proxy.GameObject.name} " +
                    $"pos={proxy.GameObject.transform.position} " +
                    $"inwardNormal={proxy.InwardNormal}",
                    proxy.GameObject);
            }
        }

        [ContextMenu("Log Room Surfaces")]
        public void LogRoomSurfaces()
        {
            if (generator == null)
            {
                Debug.LogWarning("[RoomShell] No generator assigned.", this);
                return;
            }

            Debug.Log($"[RoomShell] {generator.Proxies.Count} proxies:", this);
            foreach (RoomShellProxy proxy in generator.Proxies)
            {
                if (proxy.SourcePlane == null)
                    continue;

                Vector2 size = proxy.SourcePlane.size;
                Debug.Log(
                    $"[RoomShell] {proxy.Type} {proxy.GameObject.name} " +
                    $"Size=({size.x:F2}, {size.y:F2}) " +
                    $"Pos={proxy.GameObject.transform.position} " +
                    $"InwardNormal={proxy.InwardNormal}",
                    proxy.GameObject);
            }
        }

        void ApplyVisualization()
        {
            if (generator == null)
                return;

            foreach (RoomShellProxy proxy in generator.Proxies)
            {
                if (proxy.GameObject == null)
                    continue;

                MeshRenderer renderer = proxy.GameObject.GetComponent<MeshRenderer>();
                if (renderer == null)
                    continue;

                renderer.enabled = debugVisible;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                if (debugVisible)
                    renderer.sharedMaterial = GetMaterial(proxy.Type);
            }
        }

        Material GetMaterial(RoomSurfaceType type)
        {
            switch (type)
            {
                case RoomSurfaceType.Floor:
                    return floorMaterial ??= CreateDebugMaterial(floorColor, "RoomShellFloorDebug");
                case RoomSurfaceType.Ceiling:
                    return ceilingMaterial ??= CreateDebugMaterial(ceilingColor, "RoomShellCeilingDebug");
                default:
                    return wallMaterial ??= CreateDebugMaterial(wallColor, "RoomShellWallDebug");
            }
        }

        static Material CreateDebugMaterial(Color color, string name)
        {
            // Shader.Find only resolves shaders that made it into the build. If the URP
            // Unlit shader was stripped, fall back rather than constructing a Material
            // from a null shader, which errors out at runtime on the device.
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Sprites/Default");

            if (shader == null)
            {
                Debug.LogWarning(
                    "[RoomShell] No shader available for the debug material; proxies will " +
                    "not be visible.");
                return null;
            }

            Material material = new Material(shader) { name = name };

            // Transparent, unlit, and drawn from both sides. Double-sided matters here:
            // whether a surface's mesh faces into or out of the room depends on how the
            // device reported the plane, and this step must not bake an assumption about
            // that into the geometry.
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetColor("_BaseColor", color);
            material.color = color;
            return material;
        }

        static void DestroyMaterial(ref Material material)
        {
            if (material == null)
                return;

            if (Application.isPlaying)
                Destroy(material);
            else
                DestroyImmediate(material);

            material = null;
        }

        void OnDrawGizmosSelected()
        {
            if (generator == null)
                return;

            foreach (RoomShellProxy proxy in generator.Proxies)
            {
                if (proxy.GameObject == null)
                    continue;

                Vector3 origin = proxy.GameObject.transform.position;
                Gizmos.color = proxy.Type switch
                {
                    RoomSurfaceType.Floor => floorColor,
                    RoomSurfaceType.Ceiling => ceilingColor,
                    _ => wallColor
                };
                Gizmos.DrawLine(origin, origin + proxy.InwardNormal * normalGizmoLength);
                Gizmos.DrawSphere(origin + proxy.InwardNormal * normalGizmoLength, 0.02f);
            }
        }
    }
}
