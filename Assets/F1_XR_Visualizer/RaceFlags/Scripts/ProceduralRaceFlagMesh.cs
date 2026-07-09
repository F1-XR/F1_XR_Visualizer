using UnityEngine;

namespace F1XR.RaceFlags
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class ProceduralRaceFlagMesh : MonoBehaviour
    {
        private const string GeneratedMeshName = "F1XR Generated Race Flag Mesh";
        private const float DeformationBoundsPaddingZ = 0.08f;

        [SerializeField, Min(0.01f)] private float width = 0.42f;
        [SerializeField, Min(0.01f)] private float height = 0.30f;
        [SerializeField, Range(1, 64)] private int horizontalSubdivisions = 12;
        [SerializeField, Range(1, 32)] private int verticalSubdivisions = 6;

        private MeshFilter meshFilter;
        private Mesh generatedMesh;
        private int lastBuildHash;

        private void Awake()
        {
            EnsureMeshFilter();

            if (HasValidMesh(meshFilter.sharedMesh))
            {
                lastBuildHash = GetSettingsHash();
                return;
            }

            RebuildMesh();
        }

        private void OnDestroy()
        {
            DestroyGeneratedMesh();
        }

        private void OnValidate()
        {
            width = Mathf.Max(0.01f, width);
            height = Mathf.Max(0.01f, height);
            horizontalSubdivisions = Mathf.Clamp(horizontalSubdivisions, 1, 64);
            verticalSubdivisions = Mathf.Clamp(verticalSubdivisions, 1, 32);

            EnsureMeshFilter();

            int settingsHash = GetSettingsHash();
            if (settingsHash == lastBuildHash && HasValidMesh(meshFilter.sharedMesh))
                return;

            RebuildMesh();
        }

        public void RebuildMesh()
        {
            EnsureMeshFilter();
            DestroyGeneratedMesh();

            generatedMesh = BuildMesh();
            meshFilter.sharedMesh = generatedMesh;
            lastBuildHash = GetSettingsHash();
        }

        private void EnsureMeshFilter()
        {
            if (meshFilter == null)
                meshFilter = GetComponent<MeshFilter>();
        }

        private Mesh BuildMesh()
        {
            int columns = horizontalSubdivisions + 1;
            int rows = verticalSubdivisions + 1;
            int vertexCount = columns * rows;
            int triangleIndexCount = horizontalSubdivisions * verticalSubdivisions * 6;

            Vector3[] vertices = new Vector3[vertexCount];
            Vector3[] normals = new Vector3[vertexCount];
            Vector4[] tangents = new Vector4[vertexCount];
            Vector2[] uvs = new Vector2[vertexCount];
            int[] triangles = new int[triangleIndexCount];

            for (int y = 0; y < rows; y++)
            {
                float v = y / (float)verticalSubdivisions;
                float localY = v * height;

                for (int x = 0; x < columns; x++)
                {
                    float u = x / (float)horizontalSubdivisions;
                    int index = y * columns + x;

                    vertices[index] = new Vector3(u * width, localY, 0.0f);
                    normals[index] = Vector3.forward;
                    tangents[index] = new Vector4(1.0f, 0.0f, 0.0f, 1.0f);
                    uvs[index] = new Vector2(u, v);
                }
            }

            int triangle = 0;
            for (int y = 0; y < verticalSubdivisions; y++)
            {
                for (int x = 0; x < horizontalSubdivisions; x++)
                {
                    int lowerLeft = y * columns + x;
                    int lowerRight = lowerLeft + 1;
                    int upperLeft = lowerLeft + columns;
                    int upperRight = upperLeft + 1;

                    triangles[triangle++] = lowerLeft;
                    triangles[triangle++] = lowerRight;
                    triangles[triangle++] = upperLeft;
                    triangles[triangle++] = lowerRight;
                    triangles[triangle++] = upperRight;
                    triangles[triangle++] = upperLeft;
                }
            }

            Mesh mesh = new Mesh
            {
                name = $"{GeneratedMeshName} {horizontalSubdivisions}x{verticalSubdivisions}"
            };

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0, true);
            mesh.bounds = new Bounds(
                new Vector3(width * 0.5f, height * 0.5f, 0.0f),
                new Vector3(width, height + 0.04f, DeformationBoundsPaddingZ * 2.0f)
            );

            return mesh;
        }

        private bool HasValidMesh(Mesh mesh)
        {
            if (mesh == null)
                return false;

            int expectedVertexCount = (horizontalSubdivisions + 1) * (verticalSubdivisions + 1);
            return mesh.vertexCount == expectedVertexCount;
        }

        private void DestroyGeneratedMesh()
        {
            if (generatedMesh == null)
                return;

            if (meshFilter != null && meshFilter.sharedMesh == generatedMesh)
                meshFilter.sharedMesh = null;

            if (Application.isPlaying)
                Destroy(generatedMesh);
            else
                DestroyImmediate(generatedMesh);

            generatedMesh = null;
        }

        private int GetSettingsHash()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + width.GetHashCode();
                hash = hash * 31 + height.GetHashCode();
                hash = hash * 31 + horizontalSubdivisions;
                hash = hash * 31 + verticalSubdivisions;
                return hash;
            }
        }
    }
}
