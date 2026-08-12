using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace F1XR.Experience.Environment
{
    public enum VirtualSurfaceType
    {
        Floor,
        Wall,
        Ceiling
    }

    /// <summary>One built surface of the garage, and everything the fracture needs to break it.</summary>
    public sealed class VirtualGarageSurface
    {
        public VirtualSurfaceType Type;
        public Transform Space;          // local XY is the surface plane, local +Z points inwards
        public MeshRenderer Renderer;
        public Material SurfaceMaterial;
        public Vector2 Size;             // metres, local X by local Y
    }

    /// <summary>
    /// The VR world, such as it is: a closed F1 pit garage built from six flat surfaces.
    ///
    /// This exists because there was nothing to break. The VR environment was a forty metre
    /// white plane and eight marker posts, so "returning to reality" could only ever look
    /// like a couple of objects being deleted. Six surfaces around the user is the smallest
    /// thing that makes the VR side an actual space, and a space can collapse.
    ///
    /// It is ordinary environment geometry, not a transition prop. It is present the whole
    /// time the user is in VR, which is the point: when it breaks, what breaks is the room
    /// they were already standing in, rather than a proxy that appeared for the occasion.
    ///
    /// Everything is generated - meshes, materials, textures - so this needs no art. The F1
    /// read comes from a dark shell with a red accent line and lit ceiling strips, not from
    /// modelling.
    ///
    /// Surfaces are built with local XY in the plane and local +Z pointing into the room,
    /// which is exactly the convention <see cref="Fracture.ShellFractureRig"/> wants, so the
    /// existing fracture can be pointed straight at them.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VirtualGarage : MonoBehaviour
    {
        [Header("Size")]
        [Tooltip("Interior width and depth. The VR marker posts sit on a six metre radius " +
            "ring, so anything under about fourteen metres puts a wall through them.")]
        [SerializeField, Min(8f)] float interiorSize = 16f;

        [Tooltip("Interior height. The marker posts are two metres, so this is headroom over " +
            "them. Four reads as a garage; five starts to read as a hangar.")]
        [SerializeField, Range(2.5f, 8f)] float interiorHeight = 4f;

        [Header("Look")]
        [SerializeField] Color floorColor = new(0.10f, 0.11f, 0.125f, 1f);
        [SerializeField, Range(0f, 1f)] float floorSmoothness = 0.6f;
        [SerializeField] Color wallColor = new(0.078f, 0.086f, 0.102f, 1f);
        [SerializeField] Color accentColor = new(0.882f, 0.024f, 0f, 1f);

        [Tooltip("Height of the red accent line up the wall, in metres.")]
        [SerializeField, Range(0f, 3f)] float accentHeight = 1.2f;

        [SerializeField] Color ceilingColor = new(0.035f, 0.035f, 0.042f, 1f);
        [SerializeField] Color ceilingLightColor = new(1f, 0.97f, 0.92f, 1f);

        [Tooltip("Distance between ceiling light strips, in metres.")]
        [SerializeField, Range(1f, 8f)] float ceilingStripSpacing = 4f;

        [Tooltip("Build on Awake. Off leaves the garage to be built by hand for testing.")]
        [SerializeField] bool buildOnAwake = true;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");

        readonly List<VirtualGarageSurface> surfaces = new();
        readonly List<Object> owned = new();

        public IReadOnlyList<VirtualGarageSurface> Surfaces => surfaces;
        public float InteriorSize => interiorSize;
        public float InteriorHeight => interiorHeight;

        /// <summary>Roughly where a person stands: middle of the room, at eye height.</summary>
        public Vector3 RoomCentre => transform.TransformPoint(new Vector3(0f, interiorHeight * 0.5f, 0f));

        void Awake()
        {
            if (buildOnAwake)
                Build();
        }

        void OnDestroy() => Clear();

        [ContextMenu("Rebuild Garage")]
        public void Build()
        {
            Clear();

            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
            {
                Debug.LogError("[VirtualGarage] URP/Lit shader missing; no garage built.", this);
                return;
            }

            Material floorMaterial = BuildFloorMaterial(lit);
            Material wallMaterial = BuildWallMaterial(lit);
            Material ceilingMaterial = BuildCeilingMaterial(lit);

            float half = interiorSize * 0.5f;
            float height = interiorHeight;

            // Local +Z is the inward normal in every case, so the fracture rig's own
            // convention (plane in local XY, normal along +Z) needs no adapter here.
            AddSurface("Garage Floor", VirtualSurfaceType.Floor,
                new Vector3(0f, 0f, 0f), Quaternion.Euler(-90f, 0f, 0f),
                new Vector2(interiorSize, interiorSize), floorMaterial);

            AddSurface("Garage Ceiling", VirtualSurfaceType.Ceiling,
                new Vector3(0f, height, 0f), Quaternion.Euler(90f, 0f, 0f),
                new Vector2(interiorSize, interiorSize), ceilingMaterial);

            AddSurface("Garage Wall Front", VirtualSurfaceType.Wall,
                new Vector3(0f, height * 0.5f, half), Quaternion.Euler(0f, 180f, 0f),
                new Vector2(interiorSize, height), wallMaterial);

            AddSurface("Garage Wall Back", VirtualSurfaceType.Wall,
                new Vector3(0f, height * 0.5f, -half), Quaternion.identity,
                new Vector2(interiorSize, height), wallMaterial);

            AddSurface("Garage Wall Left", VirtualSurfaceType.Wall,
                new Vector3(-half, height * 0.5f, 0f), Quaternion.Euler(0f, 90f, 0f),
                new Vector2(interiorSize, height), wallMaterial);

            AddSurface("Garage Wall Right", VirtualSurfaceType.Wall,
                new Vector3(half, height * 0.5f, 0f), Quaternion.Euler(0f, -90f, 0f),
                new Vector2(interiorSize, height), wallMaterial);

            RetireLegacyFloor();

            Debug.Log(
                $"[VirtualGarage] built {surfaces.Count} surfaces, " +
                $"{interiorSize}m x {interiorSize}m x {interiorHeight}m.",
                this);
        }

        /// <summary>
        /// The old forty metre white plane is what made VR read as an empty void, and it is
        /// also the thing the garage floor replaces. Switch its renderer off rather than
        /// destroying it, so anything still referencing the object keeps working.
        /// </summary>
        void RetireLegacyFloor()
        {
            Transform legacy = transform.Find("VR Floor");
            if (legacy == null)
                return;

            var renderer = legacy.GetComponent<MeshRenderer>();
            if (renderer == null || !renderer.enabled)
                return;

            renderer.enabled = false;
            Debug.Log(
                "[VirtualGarage] legacy 'VR Floor' plane hidden; the garage floor replaces it.",
                this);
        }

        void AddSurface(
            string surfaceName,
            VirtualSurfaceType type,
            Vector3 localPosition,
            Quaternion localRotation,
            Vector2 size,
            Material material)
        {
            var host = new GameObject(surfaceName);
            host.transform.SetParent(transform, false);
            host.transform.localPosition = localPosition;
            host.transform.localRotation = localRotation;

            Mesh mesh = BuildQuad(size);
            host.AddComponent<MeshFilter>().sharedMesh = mesh;

            MeshRenderer renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;

            surfaces.Add(new VirtualGarageSurface
            {
                Type = type,
                Space = host.transform,
                Renderer = renderer,
                SurfaceMaterial = material,
                Size = size
            });
        }

        /// <summary>
        /// UVs are the vertex position in metres, not 0..1. The fracture rig bakes cell
        /// positions into fragment UVs the same way, so a fragment lines up with the surface
        /// it came out of without any extra work. Tiling on the material turns metres into
        /// texture space.
        /// </summary>
        Mesh BuildQuad(Vector2 size)
        {
            float x = size.x * 0.5f;
            float y = size.y * 0.5f;

            var mesh = new Mesh { name = "VirtualGarageSurface" };
            mesh.SetVertices(new[]
            {
                new Vector3(-x, -y, 0f),
                new Vector3(x, -y, 0f),
                new Vector3(x, y, 0f),
                new Vector3(-x, y, 0f)
            });
            mesh.SetNormals(new[]
            {
                Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward
            });
            mesh.SetUVs(0, new[]
            {
                new Vector2(-x, -y),
                new Vector2(x, -y),
                new Vector2(x, y),
                new Vector2(-x, y)
            });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0, true);

            owned.Add(mesh);
            return mesh;
        }

        Material BuildFloorMaterial(Shader lit)
        {
            var material = new Material(lit) { name = "VirtualGarageFloor" };
            material.SetColor(BaseColorId, floorColor);
            material.SetFloat(SmoothnessId, floorSmoothness);
            material.SetFloat(MetallicId, 0f);
            owned.Add(material);
            return material;
        }

        Material BuildWallMaterial(Shader lit)
        {
            // One column of pixels: dark wall with a red band at accent height. A texture
            // rather than extra geometry, so the wall stays a single quad the fracture can
            // shatter in one go.
            Texture2D band = BuildColumn(64, wallColor, accentColor,
                accentHeight / Mathf.Max(interiorHeight, 0.01f), 0.045f, TextureWrapMode.Clamp);

            var material = new Material(lit) { name = "VirtualGarageWall" };
            material.SetColor(BaseColorId, Color.white);
            material.SetTexture(BaseMapId, band);
            material.SetFloat(SmoothnessId, 0.25f);
            material.SetFloat(MetallicId, 0f);

            // Vertical only: UVs are metres, so scale height into 0..1 and shift the centred
            // quad so the bottom edge lands at v = 0.
            material.mainTextureScale = new Vector2(1f, 1f / Mathf.Max(interiorHeight, 0.01f));
            material.mainTextureOffset = new Vector2(0f, 0.5f);

            owned.Add(material);
            return material;
        }

        Material BuildCeilingMaterial(Shader lit)
        {
            Texture2D strips = BuildColumn(64, ceilingColor, ceilingLightColor,
                0.5f, 0.08f, TextureWrapMode.Repeat);

            var material = new Material(lit) { name = "VirtualGarageCeiling" };
            material.SetColor(BaseColorId, Color.white);
            material.SetTexture(BaseMapId, strips);
            material.SetFloat(SmoothnessId, 0.15f);
            material.SetFloat(MetallicId, 0f);

            // Strips glow rather than just being pale, which is most of what sells a ceiling
            // as lit rather than painted.
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            material.SetTexture(EmissionMapId, strips);
            material.SetColor(EmissionColorId, ceilingLightColor * 1.6f);

            material.mainTextureScale =
                new Vector2(1f, 1f / Mathf.Max(ceilingStripSpacing, 0.01f));

            owned.Add(material);
            return material;
        }

        /// <summary>
        /// A one pixel wide strip: <paramref name="baseColour"/> everywhere except a band of
        /// <paramref name="bandColour"/> centred at <paramref name="bandCentre"/>. Generated
        /// rather than imported, so the garage needs no art assets at all.
        /// </summary>
        static Texture2D BuildColumn(
            int height,
            Color baseColour,
            Color bandColour,
            float bandCentre,
            float bandHalfWidth,
            TextureWrapMode wrap)
        {
            var texture = new Texture2D(1, height, TextureFormat.RGBA32, false)
            {
                name = "VirtualGarageColumn",
                wrapMode = wrap,
                filterMode = FilterMode.Bilinear
            };

            for (int y = 0; y < height; y++)
            {
                float v = (y + 0.5f) / height;
                float distance = Mathf.Abs(v - bandCentre);
                if (wrap == TextureWrapMode.Repeat)
                    distance = Mathf.Min(distance, 1f - distance);

                texture.SetPixel(0, y, distance <= bandHalfWidth ? bandColour : baseColour);
            }

            texture.Apply(false, false);
            return texture;
        }

        public void Clear()
        {
            foreach (VirtualGarageSurface surface in surfaces)
            {
                if (surface.Space != null)
                    DestroySafely(surface.Space.gameObject);
            }

            surfaces.Clear();

            foreach (Object item in owned)
                DestroySafely(item);

            owned.Clear();
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
