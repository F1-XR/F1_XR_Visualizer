using System.Collections.Generic;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public sealed class ReplayCarSelectionEffect
    {
        private const float RingHeightRatio = 0.02f;
        private const float PulseHeightRatio = 0.026f;
        private const float RingOuterRatio = 0.95f;
        private const float PulseOuterRatio = 1.8f;
        private const float RingInnerRatio = 0.56f;
        private const float PulseInnerRatio = 0.72f;
        private const float RingAlpha = 0.88f;
        private const float PulseAlpha = 0.82f;
        private const float RingRotationSpeed = 32f;
        private const float PulseDuration = 0.58f;
        private const int RingSegments = 96;

        private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
        private static readonly int CullId = Shader.PropertyToID("_Cull");

        private readonly Transform owner;

        private Transform root;
        private MeshRenderer ring;
        private MeshRenderer pulse;
        private Material ringMaterial;
        private Material pulseMaterial;
        private Mesh ringMesh;
        private Mesh pulseMesh;
        private Vector3[] ringVertices;
        private Vector3[] pulseVertices;

        private Color color = Color.white;
        private float age;
        private float pulseAge = PulseDuration;

        public ReplayCarSelectionEffect(Transform owner)
        {
            this.owner = owner;
        }

        public Color Color => color;

        public void SetColor(Color value)
        {
            color = value;
            ApplyColor();
        }

        public void Show(bool restartPulse)
        {
            if (restartPulse)
                pulseAge = 0f;

            SetActive(false);
        }

        public void Hide()
        {
            SetActive(false);
        }

        public void Update(Bounds bounds)
        {
            EnsureCreated();
            SetActive(true);

            age += Time.deltaTime;

            float radius = Mathf.Max(bounds.size.x, bounds.size.z) * RingOuterRatio;
            Vector3 localCenter = owner.InverseTransformPoint(new Vector3(
                bounds.center.x,
                bounds.min.y + Mathf.Max(radius * RingHeightRatio, 0.001f),
                bounds.center.z
            ));

            UpdateRingMesh(
                ringMesh,
                ringVertices,
                localCenter,
                radius,
                RingInnerRatio,
                age * RingRotationSpeed
            );

            UpdatePulse(localCenter, radius);
        }

        public void CollectRenderers(List<Renderer> renderers)
        {
            if (renderers == null)
                return;

            AddRenderer(renderers, ring);
            AddRenderer(renderers, pulse);
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
            DestroyObject(pulseMaterial);
            DestroyObject(ringMesh);
            DestroyObject(pulseMesh);
        }

        private void EnsureCreated()
        {
            bool created = false;
            ringMaterial ??= CreateMaterial(WithAlpha(color, RingAlpha));
            pulseMaterial ??= CreateMaterial(WithAlpha(color, PulseAlpha));

            if (root == null)
            {
                GameObject rootObject = new GameObject("SelectionFx");
                rootObject.transform.SetParent(owner, false);
                root = rootObject.transform;
                created = true;
            }

            if (ring == null)
            {
                GameObject ringObject = new GameObject("GroundRing");
                ringObject.transform.SetParent(root, false);
                MeshFilter ringFilter = ringObject.AddComponent<MeshFilter>();
                ring = ringObject.AddComponent<MeshRenderer>();
                ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ring.receiveShadows = false;
                ring.material = ringMaterial;
                ringMesh = CreateRingMesh("SelectedCarGroundRing", out ringVertices);
                ringFilter.sharedMesh = ringMesh;
                created = true;
            }

            if (pulse == null)
            {
                GameObject pulseObject = new GameObject("SelectionPulse");
                pulseObject.transform.SetParent(root, false);
                MeshFilter pulseFilter = pulseObject.AddComponent<MeshFilter>();
                pulse = pulseObject.AddComponent<MeshRenderer>();
                pulse.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                pulse.receiveShadows = false;
                pulse.material = pulseMaterial;
                pulseMesh = CreateRingMesh("SelectedCarPulse", out pulseVertices);
                pulseFilter.sharedMesh = pulseMesh;
                created = true;
            }

            if (created)
                SetActive(false);

            ApplyColor();
        }

        private void ApplyColor()
        {
            SetMaterialColor(ringMaterial, WithAlpha(color, RingAlpha));
            SetMaterialColor(pulseMaterial, WithAlpha(color, PulseAlpha));
        }

        private void UpdatePulse(Vector3 localCenter, float radius)
        {
            if (pulse == null)
                return;

            if (pulseAge >= PulseDuration)
            {
                pulse.gameObject.SetActive(false);
                return;
            }

            pulse.gameObject.SetActive(true);

            float t = Mathf.Clamp01(pulseAge / PulseDuration);
            float eased = 1f - Mathf.Pow(1f - t, 2f);
            float pulseRadius = radius * Mathf.Lerp(0.78f, PulseOuterRatio, eased);
            float pulseAlpha = Mathf.Lerp(PulseAlpha, 0f, t);

            SetMaterialColor(pulseMaterial, WithAlpha(color, pulseAlpha));

            Vector3 pulseCenter = localCenter + Vector3.up * LocalDistance(Mathf.Max(radius * PulseHeightRatio, 0.0008f));
            UpdateRingMesh(pulseMesh, pulseVertices, pulseCenter, pulseRadius, PulseInnerRatio, -age * RingRotationSpeed * 0.65f);

            pulseAge += Time.deltaTime;
        }

        private float LocalDistance(float worldDistance)
        {
            return worldDistance / Mathf.Max(0.0001f, Mathf.Abs(owner.lossyScale.y));
        }

        private void SetActive(bool active)
        {
            if (root != null)
                root.gameObject.SetActive(active);

            if (ring != null)
                ring.gameObject.SetActive(active);

            if (pulse != null)
                pulse.gameObject.SetActive(active && pulseAge < PulseDuration);
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

                vertices[inner] = new Vector3(cos, 0f, sin);
                vertices[outer] = new Vector3(cos, 0f, sin);

                uvs[inner] = new Vector2(0f, i / (float)RingSegments);
                uvs[outer] = new Vector2(1f, i / (float)RingSegments);

                colors[inner] = Color.white;
                colors[outer] = Color.white;

                int nextInner = (i + 1) % RingSegments * 2;
                int nextOuter = nextInner + 1;
                int triangle = i * 6;

                triangles[triangle] = inner;
                triangles[triangle + 1] = nextInner;
                triangles[triangle + 2] = outer;
                triangles[triangle + 3] = outer;
                triangles[triangle + 4] = nextInner;
                triangles[triangle + 5] = nextOuter;
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
            material.name = "Runtime_SelectedCarFx";
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

        private static void SetMaterialColor(Material material, Color value)
        {
            if (material == null)
                return;

            material.color = value;

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", value);

            if (material.HasProperty("_Color"))
                material.SetColor("_Color", value);
        }

        private static void AddRenderer(List<Renderer> renderers, Renderer renderer)
        {
            if (renderer != null)
                renderers.Add(renderer);
        }

        private static Color WithAlpha(Color value, float alpha)
        {
            value.a = alpha;
            return value;
        }

        private static void DestroyObject(Object target)
        {
            if (target != null)
                Object.Destroy(target);
        }
    }
}