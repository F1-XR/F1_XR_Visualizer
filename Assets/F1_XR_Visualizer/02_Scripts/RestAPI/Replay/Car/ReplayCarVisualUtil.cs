using UnityEngine;
using TMPro;

namespace F1XR.RestAPI.Replay
{
    internal static class ReplayCarVisualUtil
    {
        private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
        private static readonly int CullId = Shader.PropertyToID("_Cull");
        private static Material sharedLabelTextMaterial;
        private static Material sharedLabelLineMaterial;
        private static Material sharedLabelBackgroundMaterial;
        private static Material sharedLabelDotMaterial;

        public static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        public static void SetMaterialColor(Material material, Color color)
        {
            if (material == null)
                return;

            material.color = color;

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);

            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
        }

        public static Material CreateUnlitMaterial(Color color)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");

            Material material = new Material(shader);
            SetMaterialColor(material, color);

            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);

            if (material.HasProperty("_SrcBlend"))
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);

            if (material.HasProperty("_DstBlend"))
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

            if (material.HasProperty("_ZWrite"))
                material.SetFloat("_ZWrite", 0f);

            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 0f);

            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0f);

            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness", 0f);

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = 3000;
            return material;
        }

        public static Material CreateSelectionMaterial(Color color)
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

        public static Material CreateTextMaterial(TextMesh text, Color color)
        {
            Shader shader = Shader.Find("GUI/Text Shader");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            Material material = new Material(shader);
            if (text.font != null && text.font.material != null)
                material.mainTexture = text.font.material.mainTexture;

            SetMaterialColor(material, color);
            material.renderQueue = 3000;
            return material;
        }

        public static Material GetLabelTextMaterial(TextMesh text)
        {
            if (sharedLabelTextMaterial == null)
            {
                sharedLabelTextMaterial = CreateTextMaterial(text, Color.white);
                sharedLabelTextMaterial.name = "Shared_DriverLabelText";
            }

            return sharedLabelTextMaterial;
        }

        public static Material GetLabelLineMaterial()
        {
            if (sharedLabelLineMaterial == null)
            {
                sharedLabelLineMaterial =
                    CreateUnlitMaterial(new Color(1f, 1f, 1f, 0.72f));
                sharedLabelLineMaterial.name = "Shared_DriverLabelLine";
            }

            return sharedLabelLineMaterial;
        }

        public static Material GetLabelBackgroundMaterial()
        {
            if (sharedLabelBackgroundMaterial == null)
            {
                sharedLabelBackgroundMaterial =
                    CreateUnlitMaterial(new Color(0f, 0f, 0f, 0.34f));
                sharedLabelBackgroundMaterial.name = "Shared_DriverLabelBackground";
                sharedLabelBackgroundMaterial.renderQueue = 2990;
            }

            return sharedLabelBackgroundMaterial;
        }

        public static Material GetLabelDotMaterial()
        {
            if (sharedLabelDotMaterial == null)
            {
                sharedLabelDotMaterial = CreateUnlitMaterial(Color.white);
                sharedLabelDotMaterial.name = "Shared_DriverLabelDot";
            }

            return sharedLabelDotMaterial;
        }

        public static Mesh CreateRingMesh(string meshName, int segments, out Vector3[] vertices)
        {
            Mesh mesh = new Mesh { name = meshName };
            vertices = new Vector3[segments * 2];
            Vector2[] uvs = new Vector2[vertices.Length];
            Color[] colors = new Color[vertices.Length];
            int[] triangles = new int[segments * 6];

            for (int i = 0; i < segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);
                int inner = i * 2;
                int outer = inner + 1;

                vertices[inner] = new Vector3(cos * 0.72f, 0f, sin * 0.72f);
                vertices[outer] = new Vector3(cos, 0f, sin);
                uvs[inner] = new Vector2(0f, i / (float)segments);
                uvs[outer] = new Vector2(1f, i / (float)segments);
                colors[inner] = new Color(1f, 1f, 1f, 0.24f);
                colors[outer] = Color.white;

                int nextInner = (i + 1) % segments * 2;
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
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        public static void UpdateRingMesh(
            Transform owner,
            Mesh mesh,
            Vector3[] vertices,
            int segments,
            Vector3 localCenter,
            float worldOuterRadius,
            float innerRatio,
            float yawDegrees)
        {
            if (owner == null || mesh == null || vertices == null)
                return;

            Vector3 scale = owner.lossyScale;
            float outerX = worldOuterRadius / Mathf.Max(0.0001f, Mathf.Abs(scale.x));
            float outerZ = worldOuterRadius / Mathf.Max(0.0001f, Mathf.Abs(scale.z));
            float innerX = outerX * innerRatio;
            float innerZ = outerZ * innerRatio;
            float yaw = yawDegrees * Mathf.Deg2Rad;

            for (int i = 0; i < segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f + yaw;
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

        public static float MaxAbsComponent(Vector3 value)
        {
            return Mathf.Max(
                0.0001f,
                Mathf.Max(
                    Mathf.Abs(value.x),
                    Mathf.Max(Mathf.Abs(value.y), Mathf.Abs(value.z))
                )
            );
        }

        public static void GetTextBackgroundTransform(
            TextMesh text,
            MeshRenderer textRenderer,
            float depthRatio,
            out Vector3 localPosition,
            out Vector3 localScale)
        {
            Bounds textBounds = textRenderer != null ? textRenderer.localBounds : default;
            float fallbackHeight = text != null
                ? Mathf.Max(0.0001f, text.characterSize * text.fontSize)
                : 0.0001f;
            string content = text != null ? text.text : string.Empty;
            float textWidth = textBounds.size.x > 0f
                ? textBounds.size.x
                : fallbackHeight * Mathf.Max(1.2f, content.Length * 0.42f);
            float textLocalHeight = textBounds.size.y > 0f
                ? textBounds.size.y
                : fallbackHeight * 0.82f;
            float horizontalPadding = textLocalHeight * 0.16f;
            float verticalPadding = textLocalHeight * 0.12f;

            localPosition = new Vector3(
                textBounds.center.x,
                textBounds.center.y,
                -textLocalHeight * depthRatio
            );
            localScale = new Vector3(
                textWidth + horizontalPadding * 2f,
                textLocalHeight + verticalPadding * 2f,
                1f
            );
        }

        public static void GetTextBackgroundTransform(
            TMP_Text text,
            MeshRenderer textRenderer,
            float depthRatio,
            out Vector3 localPosition,
            out Vector3 localScale)
        {
            Bounds textBounds = textRenderer != null
                ? textRenderer.localBounds
                : default;
            float fallbackHeight = text != null
                ? Mathf.Max(0.0001f, text.fontSize)
                : 0.0001f;
            string content = text != null ? text.text : string.Empty;
            float textWidth = textBounds.size.x > 0f
                ? textBounds.size.x
                : fallbackHeight * Mathf.Max(1.2f, content.Length * 0.42f);
            float textLocalHeight = textBounds.size.y > 0f
                ? textBounds.size.y
                : fallbackHeight * 0.82f;
            float horizontalPadding = textLocalHeight * 0.16f;
            float verticalPadding = textLocalHeight * 0.12f;

            localPosition = new Vector3(
                textBounds.center.x,
                textBounds.center.y,
                -textLocalHeight * depthRatio
            );
            localScale = new Vector3(
                textWidth + horizontalPadding * 2f,
                textLocalHeight + verticalPadding * 2f,
                1f
            );
        }

        public static Vector3 ToLocalScale(Transform target, float worldSize)
        {
            Transform parent = target.parent;
            if (parent == null)
                return Vector3.one * worldSize;

            Vector3 scale = parent.lossyScale;
            return new Vector3(
                worldSize / Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
                worldSize / Mathf.Max(0.0001f, Mathf.Abs(scale.y)),
                worldSize / Mathf.Max(0.0001f, Mathf.Abs(scale.z))
            );
        }
    }
}
