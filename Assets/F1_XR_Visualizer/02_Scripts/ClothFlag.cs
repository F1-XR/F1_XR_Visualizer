using UnityEngine;

namespace F1XR.AR
{
    [RequireComponent(typeof(SkinnedMeshRenderer))]
    [RequireComponent(typeof(Cloth))]
    public sealed class ClothFlag : MonoBehaviour
    {
        [SerializeField] float width = 0.45f;
        [SerializeField] float height = 0.28f;
        [SerializeField] int columns = 18;
        [SerializeField] int rows = 10;
        [SerializeField] Material material;
        [SerializeField] Vector3 wind = new(0.35f, 0.02f, 0.08f);
        [SerializeField] Vector3 windNoise = new(0.12f, 0.04f, 0.12f);
        [SerializeField] float maxDistance = 0.06f;
        [SerializeField] float solverFrequency = 90f;
        [SerializeField] bool buildOnAwake = true;

        SkinnedMeshRenderer meshRenderer;
        Cloth cloth;
        Mesh mesh;

        void Awake()
        {
            if (buildOnAwake)
                Build();
        }

        void OnValidate()
        {
            columns = Mathf.Max(2, columns);
            rows = Mathf.Max(2, rows);
            width = Mathf.Max(0.01f, width);
            height = Mathf.Max(0.01f, height);
            maxDistance = Mathf.Max(0.001f, maxDistance);
            solverFrequency = Mathf.Max(1f, solverFrequency);
        }

        [ContextMenu("Build Flag")]
        public void Build()
        {
            meshRenderer = GetComponent<SkinnedMeshRenderer>();
            cloth = GetComponent<Cloth>();
            cloth.enabled = false;

            mesh = CreateMesh();
            meshRenderer.sharedMesh = mesh;
            meshRenderer.bones = new[] { transform };
            meshRenderer.rootBone = transform;
            meshRenderer.localBounds = mesh.bounds;

            if (material != null)
                meshRenderer.sharedMaterial = material;

            ApplyCloth();
            cloth.ClearTransformMotion();
            cloth.enabled = true;
        }

        Mesh CreateMesh()
        {
            var vertexCount = columns * rows;
            var vertices = new Vector3[vertexCount];
            var normals = new Vector3[vertexCount];
            var uvs = new Vector2[vertexCount];
            var boneWeights = new BoneWeight[vertexCount];
            var triangles = new int[(columns - 1) * (rows - 1) * 12];

            for (var y = 0; y < rows; y++)
            {
                var v = rows == 1 ? 0f : y / (float)(rows - 1);
                for (var x = 0; x < columns; x++)
                {
                    var u = columns == 1 ? 0f : x / (float)(columns - 1);
                    var index = y * columns + x;

                    vertices[index] = new Vector3(u * width, (v - 0.5f) * height, 0f);
                    normals[index] = Vector3.back;
                    uvs[index] = new Vector2(u, v);
                    boneWeights[index] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
                }
            }

            var triangleIndex = 0;
            for (var y = 0; y < rows - 1; y++)
            {
                for (var x = 0; x < columns - 1; x++)
                {
                    var lowerLeft = y * columns + x;
                    var lowerRight = lowerLeft + 1;
                    var upperLeft = lowerLeft + columns;
                    var upperRight = upperLeft + 1;

                    triangles[triangleIndex++] = lowerLeft;
                    triangles[triangleIndex++] = upperLeft;
                    triangles[triangleIndex++] = lowerRight;
                    triangles[triangleIndex++] = lowerRight;
                    triangles[triangleIndex++] = upperLeft;
                    triangles[triangleIndex++] = upperRight;

                    triangles[triangleIndex++] = lowerLeft;
                    triangles[triangleIndex++] = lowerRight;
                    triangles[triangleIndex++] = upperLeft;
                    triangles[triangleIndex++] = lowerRight;
                    triangles[triangleIndex++] = upperRight;
                    triangles[triangleIndex++] = upperLeft;
                }
            }

            var flagMesh = new Mesh
            {
                name = "ClothFlagMesh",
                vertices = vertices,
                normals = normals,
                uv = uvs,
                boneWeights = boneWeights,
                bindposes = new[] { Matrix4x4.identity },
                triangles = triangles
            };

            flagMesh.RecalculateBounds();
            return flagMesh;
        }

        void ApplyCloth()
        {
            var coefficients = new ClothSkinningCoefficient[mesh.vertexCount];

            for (var y = 0; y < rows; y++)
            {
                for (var x = 0; x < columns; x++)
                {
                    var index = y * columns + x;
                    var t = x / (float)(columns - 1);

                    var pinned = x <= 1;
                    coefficients[index] = new ClothSkinningCoefficient
                    {
                        maxDistance = pinned ? 0f : Mathf.Lerp(maxDistance * 0.25f, maxDistance, t),
                        collisionSphereDistance = 0f
                    };
                }
            }

            cloth.coefficients = coefficients;
            cloth.useGravity = false;
            cloth.externalAcceleration = wind;
            cloth.randomAcceleration = windNoise;
            cloth.damping = 0.35f;
            cloth.bendingStiffness = 0.35f;
            cloth.stretchingStiffness = 0.8f;
            cloth.worldVelocityScale = 0f;
            cloth.worldAccelerationScale = 0f;
            cloth.clothSolverFrequency = solverFrequency;
        }
    }
}
