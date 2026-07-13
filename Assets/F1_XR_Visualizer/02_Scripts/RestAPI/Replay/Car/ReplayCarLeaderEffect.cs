using System.Collections.Generic;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public sealed class ReplayCarLeaderEffect
    {
        private const float RingHeightRatio = 0.035f;
        private const float RingOuterRatio = 1.28f;
        private const float RingInnerRatio = 0.76f;
        private const float RingAlpha = 0.34f;
        private const float RingPulseAlpha = 0.12f;
        private const float RingRotationSpeed = -18f;
        private const int RingSegments = 96;

        private static readonly Color FxColor = new Color(1f, 0.78f, 0.12f, 1f);
        private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
        private static readonly int CullId = Shader.PropertyToID("_Cull");

        private readonly Transform owner;

        private Transform root;
        private MeshRenderer ring;
        private Material ringMaterial;
        private Mesh ringMesh;
        private Vector3[] ringVertices;
        private float age;

        public ReplayCarLeaderEffect(Transform owner)
        {
            this.owner = owner;
        }

        public void Update(Bounds bounds)
        {
            EnsureCreated();
            SetActive(true);

            age += Time.deltaTime;

            float radius = Mathf.Max(bounds.size.x, bounds.size.z) * RingOuterRatio;
            float alpha = RingAlpha + Mathf.Sin(age * Mathf.PI * 2f) * RingPulseAlpha;
            Vector3 worldCenter = new Vector3(
                bounds.center.x,
                bounds.min.y + Mathf.Max(radius * RingHeightRatio, 0.0012f),
                bounds.center.z
            );
            Vector3 localCenter = owner.InverseTransformPoint(worldCenter);

            SetMaterialColor(ringMaterial, WithAlpha(FxColor, alpha));
            UpdateRingMesh(ringMesh, ringVertices, localCenter, radius, RingInnerRatio, age * RingRotationSpeed);
        }

        public void Hide()
        {
            SetActive(false);
        }

        public void CollectRenderers(List<Renderer> renderers)
        {
            if (renderers == null)
                return;

            if (ring != null)
                renderers.Add(ring);
        }

        public bool Owns(Renderer renderer)
        {
            return renderer != null &&
                root != null &&
                renderer.transform.IsChildOf(root);
        }

        public void Dispose()
        {
            DestroyObject(ringMaterial);
            DestroyObject(ringMesh);
        }

        private void EnsureCreated()
        {
            bool created = false;
            ringMaterial ??= CreateMaterial(WithAlpha(FxColor, RingAlpha));

            if (root == null)
            {
                GameObject rootObject = new GameObject("RaceLeaderFx");
                rootObject.transform.SetParent(owner, false);
                root = rootObject.transform;
                created = true;
            }

            if (ring == null)
            {
                GameObject ringObject = new GameObject("LeaderGroundRing");
                ringObject.transform.SetParent(root, false);
                MeshFilter ringFilter = ringObject.AddComponent<MeshFilter>();
                ring = ringObject.AddComponent<MeshRenderer>();
                ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ring.receiveShadows = false;
                ring.material = ringMaterial;
                ringMesh = CreateRingMesh("RaceLeaderGroundRing", out ringVertices);
                ringFilter.sharedMesh = ringMesh;
                created = true;
            }

            if (created)
                SetActive(false);
        }

        private void SetActive(bool active)
        {
            if (root != null)
                root.gameObject.SetActive(active);

            if (ring != null)
                ring.gameObject.SetActive(active);
        }

        private static Mesh CreateRingMesh(string meshName, out Vector3[] vertices)
        {
            Mesh mesh = new Mesh { name = meshName };
            vertices = new Vector3[RingSegments * 2];
            Vector2[] uvs = new Vector2[vertices.Length];
            Color[] colors = new Color[vertices.Length];
            int[] triangles = new int[RingSegments * 6];

            for (int i = 0; i < RingSegments; i++)
            {
                float angle = i / (float)RingSegments * Mathf.PI * 2f;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);
                int inner = i * 2;
                int outer = inner + 1;

                vertices[inner] = new Vector3(cos * 0.72f, 0f, sin * 0.72f);
                vertices[outer] = new Vector3(cos, 0f, sin);

                uvs[inner] = new Vector2(0f, i / (float)RingSegments);
                uvs[outer] = new Vector2(1f, i / (float)RingSegments);

                colors[inner] = new Color(1f, 1f, 1f, 0.24f);
                colors[outer] = Color.white;

                int nextInner = (i + 1) % RingSegments * 2;
                int nextOuter = nextInner + 1;
                int triangle = i * 6;

                triangles[triangle] = inner;
                triangles[triangle + 1] = outer;
                triangles[triangle + 2] = nextOuter;
                triangles[triangle + 3] = inner;
                triangles[triangle + 4] = nextOuter;
                triangles[triangle + 5] = nextInner;
            }

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void UpdateRingMesh(Mesh mesh, Vector3[] vertices, Vector3 localCenter, float worldOuterRadius, float innerRatio, float yawDegrees)
        {
            if (mesh == null || vertices == null)
                return;

            float outerX = worldOuterRadius;
            float outerZ = worldOuterRadius;
            float innerX = outerX * innerRatio;
            float innerZ = outerZ * innerRatio;
            float yaw = yawDegrees * Mathf.Deg2Rad;

            for (int i = 0; i < RingSegments; i++)
            {
                float angle = i / (float)RingSegments * Mathf.PI * 2f + yaw;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);
                int inner = i * 2;
                int outer = inner + 1;

                vertices[inner] = localCenter + new Vector3(cos * innerX, 0f, sin * innerZ);
                vertices[outer] = localCenter + new Vector3(cos * outerX, 0f, sin * outerZ);
            }

            mesh.vertices = vertices;
            mesh.RecalculateBounds();
        }

        private static Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            Material material = new Material(shader);
            material.name = "Runtime_RaceLeaderFx";
            SetMaterialColor(material, color);

            if (material.HasProperty(SurfaceId))
                material.SetFloat(SurfaceId, 1f);

            if (material.HasProperty(SrcBlendId))
                material.SetFloat(SrcBlendId, (float)UnityEngine.Rendering.BlendMode.SrcAlpha);

            if (material.HasProperty(DstBlendId))
                material.SetFloat(DstBlendId, (float)UnityEngine.Rendering.BlendMode.One);

            if (material.HasProperty(ZWriteId))
                material.SetFloat(ZWriteId, 0f);

            if (material.HasProperty(CullId))
                material.SetFloat(CullId, (float)UnityEngine.Rendering.CullMode.Off);

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = 3100;
            return material;
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material == null)
                return;

            material.color = color;

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);

            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private static void DestroyObject(Object target)
        {
            if (target != null)
                Object.Destroy(target);
        }
    }
}