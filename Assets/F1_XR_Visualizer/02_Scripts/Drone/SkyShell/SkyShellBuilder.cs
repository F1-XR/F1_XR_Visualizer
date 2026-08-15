using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace F1XR.Drone.SkyShell
{
    /// <summary>
    /// Builds the black shell the viewer stands inside: a geodesic sphere seen from within,
    /// cut into one piece per face.
    ///
    /// A sphere rather than a box because a box breaks into six walls and reads as a room
    /// coming apart, and the fiction here is open space, not a room. A geodesic sphere also
    /// gives faces of nearly equal area, so no piece is conspicuously larger than its
    /// neighbours - the failure that made the circuit break look like a map being torn into
    /// plates.
    ///
    /// Pieces are not inset from each other. Every boundary vertex is a midpoint of the same
    /// two parent vertices for both faces sharing it, so neighbouring pieces meet exactly and
    /// the closed shell is watertight. That matters more than usual here: the moment the shell
    /// closes is the moment the whole transition commits behind it, and a one pixel seam is a
    /// hole straight through to the real room.
    /// </summary>
    public static class SkyShellBuilder
    {
        public sealed class Fragment
        {
            public Transform Transform;
            public MeshRenderer Renderer;

            /// <summary>Unit direction from the shell centre to this piece.</summary>
            public Vector3 CentreDirection;

            /// <summary>Rest pose, in the shell root's space. Motion is added to this.</summary>
            public Vector3 InitialLocalPosition;

            /// <summary>Faces sharing an edge with this one. The crack walks these.</summary>
            public int[] Neighbours;
        }

        public sealed class Result
        {
            public readonly List<Fragment> Fragments = new();
            public readonly List<Mesh> OwnedMeshes = new();
            public int TriangleCount;
        }

        /// <summary>
        /// One piece per face of an icosphere at the given subdivision: 20, 80 or 320.
        /// </summary>
        public static Result Build(
            Transform root,
            float radius,
            float thickness,
            int shellSubdivision,
            int patchDetail,
            Material material)
        {
            var result = new Result();

            var verts = new List<Vector3>();
            var faces = new List<int>();
            BuildIcosahedron(verts, faces);

            for (int i = 0; i < Mathf.Clamp(shellSubdivision, 0, 3); i++)
                Subdivide(verts, faces);

            int faceCount = faces.Count / 3;
            int[][] adjacency = BuildAdjacency(faces, faceCount);

            for (int f = 0; f < faceCount; f++)
            {
                Vector3 a = verts[faces[f * 3]];
                Vector3 b = verts[faces[f * 3 + 1]];
                Vector3 c = verts[faces[f * 3 + 2]];

                Vector3 centreDirection = ((a + b + c) / 3f).normalized;
                Vector3 pivot = centreDirection * radius;

                Mesh mesh = BuildPatch(a, b, c, radius, thickness, patchDetail, pivot);

                var host = new GameObject($"SkyShard_{f:000}");
                host.transform.SetParent(root, false);
                host.transform.localPosition = pivot;
                host.transform.localRotation = Quaternion.identity;
                host.transform.localScale = Vector3.one;

                host.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer renderer = host.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.enabled = false;

                result.Fragments.Add(new Fragment
                {
                    Transform = host.transform,
                    Renderer = renderer,
                    CentreDirection = centreDirection,
                    InitialLocalPosition = pivot,
                    Neighbours = adjacency[f]
                });

                result.OwnedMeshes.Add(mesh);
                result.TriangleCount += mesh.triangles.Length / 3;
            }

            return result;
        }

        /// <summary>
        /// One piece: the inner face the viewer actually looks at, an outer face behind it,
        /// and a band of side quads joining the two.
        ///
        /// The thickness is the whole point of the outer face and the band. A single sheet of
        /// triangles turning edge-on vanishes to nothing for a frame and reads as black paper;
        /// with a rim to catch the light the same piece reads as a slab of the sky peeling
        /// away. It costs a few dozen triangles per piece, which at eighty pieces is still
        /// less than a single road segment of the circuit.
        /// </summary>
        static Mesh BuildPatch(
            Vector3 a,
            Vector3 b,
            Vector3 c,
            float radius,
            float thickness,
            int patchDetail,
            Vector3 pivot)
        {
            var unit = new List<Vector3> { a, b, c };
            var tris = new List<int> { 0, 1, 2 };

            for (int i = 0; i < Mathf.Clamp(patchDetail, 0, 3); i++)
                Subdivide(unit, tris);

            int surfaceVertexCount = unit.Count;

            var vertices = new List<Vector3>(surfaceVertexCount * 2);
            var normals = new List<Vector3>(surfaceVertexCount * 2);
            var triangles = new List<int>(tris.Count * 2);

            // Inner surface. Normals point at the viewer standing in the middle, and the
            // winding is reversed from the icosphere's outward-facing original so the face is
            // front-facing from inside and survives normal back-face culling.
            for (int v = 0; v < surfaceVertexCount; v++)
            {
                vertices.Add(unit[v] * radius - pivot);
                normals.Add(-unit[v]);
            }

            for (int t = 0; t < tris.Count; t += 3)
            {
                triangles.Add(tris[t]);
                triangles.Add(tris[t + 2]);
                triangles.Add(tris[t + 1]);
            }

            // Outer surface, one thickness further out, wound the original way.
            int outerStart = vertices.Count;
            for (int v = 0; v < surfaceVertexCount; v++)
            {
                vertices.Add(unit[v] * (radius + thickness) - pivot);
                normals.Add(unit[v]);
            }

            for (int t = 0; t < tris.Count; t += 3)
            {
                triangles.Add(outerStart + tris[t]);
                triangles.Add(outerStart + tris[t + 1]);
                triangles.Add(outerStart + tris[t + 2]);
            }

            // Side band along the patch's own outline. Both windings are emitted rather than
            // working out which way each quad faces: the rim is a couple of centimetres of
            // near-black, so a correct silhouette is worth far more than a correct normal, and
            // this way there is no orientation to get wrong.
            foreach ((int i, int j) in BoundaryEdges(tris))
            {
                int i0 = i, i1 = j, o0 = outerStart + i, o1 = outerStart + j;

                triangles.Add(i0); triangles.Add(i1); triangles.Add(o1);
                triangles.Add(i0); triangles.Add(o1); triangles.Add(o0);

                triangles.Add(i0); triangles.Add(o1); triangles.Add(i1);
                triangles.Add(i0); triangles.Add(o0); triangles.Add(o1);
            }

            var mesh = new Mesh { name = "SkyShard" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0, true);
            return mesh;
        }

        /// <summary>Edges used by exactly one triangle: the outline of the patch.</summary>
        static IEnumerable<(int, int)> BoundaryEdges(List<int> tris)
        {
            var counts = new Dictionary<long, int>();
            var order = new List<(int, int)>();

            for (int t = 0; t < tris.Count; t += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    int i = tris[t + k];
                    int j = tris[t + (k + 1) % 3];
                    long key = EdgeKey(i, j);

                    if (counts.TryGetValue(key, out int n))
                    {
                        counts[key] = n + 1;
                    }
                    else
                    {
                        counts[key] = 1;
                        order.Add((i, j));
                    }
                }
            }

            foreach ((int i, int j) in order)
            {
                if (counts[EdgeKey(i, j)] == 1)
                    yield return (i, j);
            }
        }

        static int[][] BuildAdjacency(List<int> faces, int faceCount)
        {
            var byEdge = new Dictionary<long, List<int>>();

            for (int f = 0; f < faceCount; f++)
            {
                for (int k = 0; k < 3; k++)
                {
                    long key = EdgeKey(faces[f * 3 + k], faces[f * 3 + (k + 1) % 3]);
                    if (!byEdge.TryGetValue(key, out List<int> list))
                        byEdge[key] = list = new List<int>(2);

                    list.Add(f);
                }
            }

            var neighbours = new List<int>[faceCount];
            for (int f = 0; f < faceCount; f++)
                neighbours[f] = new List<int>(3);

            foreach (List<int> shared in byEdge.Values)
            {
                if (shared.Count != 2)
                    continue;

                neighbours[shared[0]].Add(shared[1]);
                neighbours[shared[1]].Add(shared[0]);
            }

            var result = new int[faceCount][];
            for (int f = 0; f < faceCount; f++)
                result[f] = neighbours[f].ToArray();

            return result;
        }

        static long EdgeKey(int a, int b)
        {
            long low = Mathf.Min(a, b);
            long high = Mathf.Max(a, b);
            return (low << 32) | high;
        }

        static void BuildIcosahedron(List<Vector3> verts, List<int> faces)
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;

            verts.AddRange(new[]
            {
                new Vector3(-1f, t, 0f), new Vector3(1f, t, 0f),
                new Vector3(-1f, -t, 0f), new Vector3(1f, -t, 0f),
                new Vector3(0f, -1f, t), new Vector3(0f, 1f, t),
                new Vector3(0f, -1f, -t), new Vector3(0f, 1f, -t),
                new Vector3(t, 0f, -1f), new Vector3(t, 0f, 1f),
                new Vector3(-t, 0f, -1f), new Vector3(-t, 0f, 1f)
            });

            for (int i = 0; i < verts.Count; i++)
                verts[i] = verts[i].normalized;

            faces.AddRange(new[]
            {
                0, 11, 5,  0, 5, 1,   0, 1, 7,   0, 7, 10,  0, 10, 11,
                1, 5, 9,   5, 11, 4,  11, 10, 2, 10, 7, 6,  7, 1, 8,
                3, 9, 4,   3, 4, 2,   3, 2, 6,   3, 6, 8,   3, 8, 9,
                4, 9, 5,   2, 4, 11,  6, 2, 10,  8, 6, 7,   9, 8, 1
            });
        }

        /// <summary>
        /// One round of midpoint subdivision on the unit sphere. Midpoints are cached by
        /// vertex pair, so a vertex on a shared edge is created once and both faces reference
        /// the same one - which is what keeps the shell watertight.
        /// </summary>
        static void Subdivide(List<Vector3> verts, List<int> faces)
        {
            var cache = new Dictionary<long, int>();
            var next = new List<int>(faces.Count * 4);

            for (int t = 0; t < faces.Count; t += 3)
            {
                int a = faces[t], b = faces[t + 1], c = faces[t + 2];
                int ab = Midpoint(a, b, verts, cache);
                int bc = Midpoint(b, c, verts, cache);
                int ca = Midpoint(c, a, verts, cache);

                next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }

            faces.Clear();
            faces.AddRange(next);
        }

        static int Midpoint(int a, int b, List<Vector3> verts, Dictionary<long, int> cache)
        {
            long key = EdgeKey(a, b);
            if (cache.TryGetValue(key, out int existing))
                return existing;

            int index = verts.Count;
            verts.Add(((verts[a] + verts[b]) * 0.5f).normalized);
            cache[key] = index;
            return index;
        }
    }
}
