using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace F1XR.Drone.TrackFracture
{
    /// <summary>
    /// Regroups a circuit's existing triangles into per-cell pieces.
    ///
    /// Nothing is cut. Every triangle is copied whole into whichever cell its centroid falls
    /// in, carrying its original position, normal, tangent and UV, and the piece is drawn
    /// with the renderer's own material. So a fragment is not a stand-in for the track - it
    /// is the track, the same vertices, just parented somewhere that can move them.
    ///
    /// That is the whole reason this exists rather than a Voronoi over a proxy plane: a
    /// generated plane would need generated UVs, and generated UVs against a circuit's
    /// texture atlas show the wrong part of the wrong texture.
    /// </summary>
    public static class TrackFragmentBuilder
    {
        public sealed class Fragment
        {
            public int CellId;
            public Transform Transform;
            public MeshRenderer Renderer;
            public Mesh Mesh;

            /// <summary>
            /// Where the piece sits when nothing has moved: the centre of its own geometry in
            /// the source renderer's space. Motion is added to this rather than replacing it.
            /// </summary>
            public Vector3 InitialLocalPosition;
        }

        public sealed class Result
        {
            public readonly List<Fragment> Fragments = new();
            public readonly List<Mesh> OwnedMeshes = new();
            public readonly List<GameObject> OwnedObjects = new();
            public int TriangleCount;

            /// <summary>Total submeshes across every fragment - the real draw call cost.</summary>
            public int SubMeshCount;
        }

        /// <summary>
        /// Splits one renderer. Triangles are binned by the cell their centroid lands in,
        /// measured in placement-root local space so the result does not depend on how big
        /// the map currently is.
        /// </summary>
        public static void SplitRenderer(
            MeshRenderer source,
            Transform placementRoot,
            TrackFractureCells cells,
            Result output,
            List<List<int>> revealTrianglesPerCell,
            List<Vector3> revealVertices,
            Dictionary<long, int> revealVertexMap)
        {
            MeshFilter filter = source.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null || !mesh.isReadable)
                return;

            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector4[] tangents = mesh.tangents;
            Vector2[] uv = mesh.uv;

            Transform sourceTransform = source.transform;
            Material[] sourceMaterials = source.sharedMaterials;

            // Submeshes are kept apart all the way through. A circuit mesh can carry hundreds
            // of them - Suzuka's road surface has 296 - and flattening them would draw the
            // whole track with whichever material happened to be first, which is exactly the
            // "every fragment has the wrong texture" failure this whole approach exists to
            // avoid.
            int subMeshCount = Mathf.Max(1, mesh.subMeshCount);
            var subTriangles = new int[subMeshCount][];
            for (int s = 0; s < subMeshCount; s++)
                subTriangles[s] = mesh.GetTriangles(s);

            // One matrix instead of two Transform calls per vertex. The binning walks a
            // quarter of a million triangles and also has to measure each one's area now, so
            // the per-call overhead of TransformPoint would show up in the prepare time.
            Matrix4x4 toPlacement =
                placementRoot.worldToLocalMatrix * sourceTransform.localToWorldMatrix;

            int added = SubdivideOversized(
                ref vertices, ref normals, ref tangents, ref uv,
                subTriangles, toPlacement, cells.CellSize);

            if (added > 0)
            {
                Debug.Log(
                    $"[TrackFracture] '{source.name}' had triangles wider than a cell; " +
                    $"subdivided into {added} extra triangles so it can break at grid scale.",
                    source);
            }

            // After subdivision, not before: the arrays have grown.
            bool hasNormals = normals != null && normals.Length == vertices.Length;
            bool hasTangents = tangents != null && tangents.Length == vertices.Length;
            bool hasUv = uv != null && uv.Length == vertices.Length;

            // [cell][submesh] -> triangle start indices within that submesh's array.
            var perCell = new List<int>[cells.Count][];

            for (int s = 0; s < subMeshCount; s++)
            {
                int[] triangles = subTriangles[s];
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    Vector3 a = toPlacement.MultiplyPoint3x4(vertices[triangles[t]]);
                    Vector3 b = toPlacement.MultiplyPoint3x4(vertices[triangles[t + 1]]);
                    Vector3 c = toPlacement.MultiplyPoint3x4(vertices[triangles[t + 2]]);

                    Vector3 inPlacement = (a + b + c) / 3f;

                    int cell = cells.CellAt(new Vector2(inPlacement.x, inPlacement.z));
                    (perCell[cell] ??= new List<int>[subMeshCount])[s] ??= new List<int>();
                    perCell[cell][s].Add(t);

                    cells.AddWeight(cell, 0.5f * Vector3.Cross(b - a, c - a).magnitude);
                }
            }

            for (int cell = 0; cell < perCell.Length; cell++)
            {
                List<int>[] bySubMesh = perCell[cell];
                if (bySubMesh == null)
                    continue;

                int cellTriangles = 0;
                foreach (List<int> list in bySubMesh)
                    cellTriangles += list != null ? list.Count : 0;

                if (cellTriangles == 0)
                    continue;

                cells.MarkUsed(cell);

                // One vertex buffer shared by every submesh in this cell, so a vertex used by
                // two materials is still only copied once.
                var remap = new Dictionary<int, int>(cellTriangles * 2);
                var newVerts = new List<Vector3>(cellTriangles * 2);
                var newNormals = hasNormals ? new List<Vector3>(cellTriangles * 2) : null;
                var newTangents = hasTangents ? new List<Vector4>(cellTriangles * 2) : null;
                var newUv = hasUv ? new List<Vector2>(cellTriangles * 2) : null;

                var keptSubMeshes = new List<int[]>();
                var keptMaterials = new List<Material>();

                for (int s = 0; s < subMeshCount; s++)
                {
                    List<int> tris = bySubMesh[s];
                    if (tris == null || tris.Count == 0)
                        continue;

                    int[] triangles = subTriangles[s];
                    var newTris = new int[tris.Count * 3];
                    int write = 0;

                    foreach (int t in tris)
                    {
                        for (int k = 0; k < 3; k++)
                        {
                            int original = triangles[t + k];
                            if (!remap.TryGetValue(original, out int mapped))
                            {
                                mapped = newVerts.Count;
                                remap[original] = mapped;
                                newVerts.Add(vertices[original]);
                                newNormals?.Add(normals[original]);
                                newTangents?.Add(tangents[original]);
                                newUv?.Add(uv[original]);
                            }

                            newTris[write++] = mapped;
                        }

                        // Reveal geometry shares one mesh per cell across every renderer and
                        // needs no materials, so submeshes collapse here and only here.
                        if (revealTrianglesPerCell != null)
                        {
                            List<int> revealTris = revealTrianglesPerCell[cell] ??= new List<int>();
                            for (int k = 0; k < 3; k++)
                            {
                                Vector3 inRoot = toPlacement.MultiplyPoint3x4(vertices[triangles[t + k]]);

                                long key = QuantiseKey(inRoot);
                                if (!revealVertexMap.TryGetValue(key, out int revealIndex))
                                {
                                    revealIndex = revealVertices.Count;
                                    revealVertexMap[key] = revealIndex;
                                    revealVertices.Add(inRoot);
                                }

                                revealTris.Add(revealIndex);
                            }
                        }
                    }

                    keptSubMeshes.Add(newTris);
                    keptMaterials.Add(s < sourceMaterials.Length ? sourceMaterials[s] : source.sharedMaterial);
                }

                // Recentre on the piece's own middle.
                //
                // Left on the source renderer's pivot, a rotation does not tilt the piece
                // where it stands - it swings the whole thing around a point that can be the
                // far side of the circuit, throwing an eighty metre slab of terrain through a
                // huge arc. Moving the vertices and the host by the same amount leaves the
                // world position identical while giving the piece a centre to turn about.
                Vector3 pivot = Vector3.zero;
                if (newVerts.Count > 0)
                {
                    var local = new Bounds(newVerts[0], Vector3.zero);
                    for (int v = 1; v < newVerts.Count; v++)
                        local.Encapsulate(newVerts[v]);

                    pivot = local.center;
                    for (int v = 0; v < newVerts.Count; v++)
                        newVerts[v] -= pivot;
                }

                var built = new Mesh { name = $"{source.name}_Cell{cell:00}" };
                if (newVerts.Count > 65000)
                    built.indexFormat = IndexFormat.UInt32;

                built.SetVertices(newVerts);
                if (newNormals != null) built.SetNormals(newNormals);
                if (newTangents != null) built.SetTangents(newTangents);
                if (newUv != null) built.SetUVs(0, newUv);

                built.subMeshCount = keptSubMeshes.Count;
                for (int s = 0; s < keptSubMeshes.Count; s++)
                    built.SetTriangles(keptSubMeshes[s], s, true);

                output.SubMeshCount += keptSubMeshes.Count;

                var host = new GameObject($"{source.name}_Cell{cell:00}");

                // Parented to the source renderer with an identity local pose, so the copied
                // vertices land on exactly the pixels the originals did. Anything else -
                // parenting to the placement root, baking a world offset - risks a visible
                // jump at the moment the swap happens.
                // host at the pivot, vertices measured from it: host + (vertex - pivot) is
                // exactly the original vertex, so the swap is still pixel-identical.
                host.transform.SetParent(sourceTransform, false);
                host.transform.localPosition = pivot;
                host.transform.localRotation = Quaternion.identity;
                host.transform.localScale = Vector3.one;

                host.AddComponent<MeshFilter>().sharedMesh = built;
                MeshRenderer renderer = host.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = keptMaterials.ToArray();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = source.receiveShadows;
                renderer.enabled = false;

                output.Fragments.Add(new Fragment
                {
                    CellId = cell,
                    Transform = host.transform,
                    Renderer = renderer,
                    Mesh = built,
                    InitialLocalPosition = pivot
                });
                output.OwnedMeshes.Add(built);
                output.OwnedObjects.Add(host);
                output.TriangleCount += cellTriangles;
            }
        }

        /// <summary>
        /// One alpha-only mesh per cell, holding every renderer's geometry for that cell.
        /// The mask needs no material of its own, so merging costs nothing and turns a
        /// hundred and fifty extra draw calls into at most thirty-six.
        /// </summary>
        public static GameObject BuildRevealMesh(
            int cellId,
            List<Vector3> vertices,
            List<int> triangles,
            Transform placementRoot,
            Material revealMaterial,
            Result output)
        {
            if (triangles == null || triangles.Count == 0)
                return null;

            var mesh = new Mesh { name = $"Reveal_Cell{cellId:00}" };
            if (vertices.Count > 65000)
                mesh.indexFormat = IndexFormat.UInt32;

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0, true);

            var host = new GameObject($"Reveal_Cell{cellId:00}");
            host.transform.SetParent(placementRoot, false);
            host.transform.localPosition = Vector3.zero;
            host.transform.localRotation = Quaternion.identity;
            host.transform.localScale = Vector3.one;

            host.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = revealMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;

            output.OwnedMeshes.Add(mesh);
            output.OwnedObjects.Add(host);
            return host;
        }

        /// <summary>
        /// Splits every triangle that is wider than one cell, at the midpoint of its longest
        /// edge, until none are left.
        ///
        /// Binning gives a whole triangle to whichever cell its centroid falls in and never
        /// cuts one, so a triangle bigger than a cell cannot be broken any finer however high
        /// the grid goes. Suzuka's ground slab is twelve triangles spanning fourteen hundred
        /// by twenty-three hundred units - all twelve are several cells across - so the ground
        /// came away as a handful of enormous plates sliding apart while everything around it
        /// crumbled normally. Raising the resolution moved the rest and left those twelve
        /// exactly as they were, which is why every grid increase changed nothing.
        ///
        /// Midpoints are shared between the two triangles either side of an edge, so no gap
        /// opens along a seam. Attributes are interpolated, exact for the flat quads this
        /// actually fires on.
        /// </summary>
        /// <returns>Triangles added. Zero means the mesh was already fine enough and none of
        /// the arrays were touched.</returns>
        public static int SubdivideOversized(
            ref Vector3[] vertices,
            ref Vector3[] normals,
            ref Vector4[] tangents,
            ref Vector2[] uv,
            int[][] subTriangles,
            Matrix4x4 toPlacement,
            float maxEdge)
        {
            if (maxEdge <= 0f)
                return 0;

            float maxSqr = maxEdge * maxEdge;

            // Checked before anything is allocated. The dense meshes are the expensive ones and
            // they never need this, so they must not pay for a copy of a quarter million
            // vertices to find that out.
            bool needed = false;
            for (int s = 0; s < subTriangles.Length && !needed; s++)
            {
                int[] tris = subTriangles[s];
                for (int t = 0; t < tris.Length; t += 3)
                {
                    Vector3 a = toPlacement.MultiplyPoint3x4(vertices[tris[t]]);
                    Vector3 b = toPlacement.MultiplyPoint3x4(vertices[tris[t + 1]]);
                    Vector3 c = toPlacement.MultiplyPoint3x4(vertices[tris[t + 2]]);

                    if ((b - a).sqrMagnitude > maxSqr ||
                        (c - b).sqrMagnitude > maxSqr ||
                        (a - c).sqrMagnitude > maxSqr)
                    {
                        needed = true;
                        break;
                    }
                }
            }

            if (!needed)
                return 0;

            var verts = new List<Vector3>(vertices);
            List<Vector3> norms = normals != null && normals.Length == vertices.Length
                ? new List<Vector3>(normals) : null;
            List<Vector4> tans = tangents != null && tangents.Length == vertices.Length
                ? new List<Vector4>(tangents) : null;
            List<Vector2> uvs = uv != null && uv.Length == vertices.Length
                ? new List<Vector2>(uv) : null;

            var midpoints = new Dictionary<long, int>();
            var stack = new Stack<TriangleWork>();
            int before = 0;
            int after = 0;

            for (int s = 0; s < subTriangles.Length; s++)
            {
                int[] tris = subTriangles[s];
                before += tris.Length / 3;

                var output = new List<int>(tris.Length);

                for (int t = 0; t < tris.Length; t += 3)
                {
                    stack.Push(new TriangleWork(tris[t], tris[t + 1], tris[t + 2], 0));

                    while (stack.Count > 0)
                    {
                        TriangleWork work = stack.Pop();

                        Vector3 p0 = toPlacement.MultiplyPoint3x4(verts[work.I0]);
                        Vector3 p1 = toPlacement.MultiplyPoint3x4(verts[work.I1]);
                        Vector3 p2 = toPlacement.MultiplyPoint3x4(verts[work.I2]);

                        float e01 = (p1 - p0).sqrMagnitude;
                        float e12 = (p2 - p1).sqrMagnitude;
                        float e20 = (p0 - p2).sqrMagnitude;

                        // Twelve levels turns one triangle into four thousand. Anything still
                        // too big by then is degenerate, and going deeper would only trade a
                        // visible seam for a frozen headset.
                        if (work.Depth >= 12 ||
                            (e01 <= maxSqr && e12 <= maxSqr && e20 <= maxSqr))
                        {
                            output.Add(work.I0);
                            output.Add(work.I1);
                            output.Add(work.I2);
                            continue;
                        }

                        int a, b, opposite;
                        if (e01 >= e12 && e01 >= e20)
                        {
                            a = work.I0; b = work.I1; opposite = work.I2;
                        }
                        else if (e12 >= e20)
                        {
                            a = work.I1; b = work.I2; opposite = work.I0;
                        }
                        else
                        {
                            a = work.I2; b = work.I0; opposite = work.I1;
                        }

                        int mid = Midpoint(a, b, verts, norms, tans, uvs, midpoints);

                        // (a, mid, opposite) and (mid, b, opposite) walk the original triangle
                        // the same way round, so the winding - and the facing - survives.
                        stack.Push(new TriangleWork(a, mid, opposite, work.Depth + 1));
                        stack.Push(new TriangleWork(mid, b, opposite, work.Depth + 1));
                    }
                }

                subTriangles[s] = output.ToArray();
                after += output.Count / 3;
            }

            vertices = verts.ToArray();
            if (norms != null) normals = norms.ToArray();
            if (tans != null) tangents = tans.ToArray();
            if (uvs != null) uv = uvs.ToArray();

            return after - before;
        }

        readonly struct TriangleWork
        {
            public readonly int I0, I1, I2, Depth;

            public TriangleWork(int i0, int i1, int i2, int depth)
            {
                I0 = i0; I1 = i1; I2 = i2; Depth = depth;
            }
        }

        /// <summary>
        /// The vertex halfway along an edge, created once and reused by both triangles that
        /// share it. Two separate midpoints at the same place would drift apart the moment the
        /// two fragments moved, opening exactly the seam this is here to close.
        /// </summary>
        static int Midpoint(
            int a,
            int b,
            List<Vector3> verts,
            List<Vector3> norms,
            List<Vector4> tans,
            List<Vector2> uvs,
            Dictionary<long, int> cache)
        {
            long low = Mathf.Min(a, b);
            long high = Mathf.Max(a, b);
            long key = (low << 32) | high;

            if (cache.TryGetValue(key, out int existing))
                return existing;

            int index = verts.Count;
            verts.Add((verts[a] + verts[b]) * 0.5f);
            norms?.Add(Vector3.Normalize(norms[a] + norms[b]));
            tans?.Add((tans[a] + tans[b]) * 0.5f);
            uvs?.Add((uvs[a] + uvs[b]) * 0.5f);

            cache[key] = index;
            return index;
        }

        static long QuantiseKey(Vector3 p)
        {
            long x = Mathf.RoundToInt(p.x * 10000f);
            long y = Mathf.RoundToInt(p.y * 10000f);
            long z = Mathf.RoundToInt(p.z * 10000f);
            return (x * 73856093L) ^ (y * 19349663L) ^ (z * 83492791L);
        }
    }
}
