using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace F1XR.RestAPI.Replay
{
    internal sealed class EventTrackSegment
    {
        private const int MaxPathSamples = 48;
        private const float MinimumSurfaceNormalY = 0.35f;

        private readonly List<Mesh> meshes = new();

        public bool Build(
            Transform parent,
            Transform sourceRoot,
            IReadOnlyList<Vector3> sourcePath,
            Vector3 sourceCenter,
            Quaternion sourceToLocalRotation,
            float padding,
            out Bounds stageBounds)
        {
            stageBounds = BuildStageBounds(
                sourcePath,
                sourceCenter,
                sourceToLocalRotation,
                padding);

            if (parent == null || sourceRoot == null || sourcePath == null || sourcePath.Count < 2)
                return false;

            Transform visualRoot = sourceRoot.Find("Visual");
            if (visualRoot == null)
                visualRoot = sourceRoot;

            GameObject segmentRoot = new GameObject("ActualTrackRegion");
            segmentRoot.transform.SetParent(parent, false);

            Vector3[] localPath = BuildLocalPath(
                sourcePath,
                sourceCenter,
                sourceToLocalRotation);
            float[] highestSurfaceY = new float[localPath.Length];
            for (int i = 0; i < highestSurfaceY.Length; i++)
                highestSurfaceY[i] = float.NegativeInfinity;

            int triangleCount = 0;
            foreach (MeshFilter filter in visualRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!CanCopy(filter, sourceRoot, stageBounds, sourceCenter, sourceToLocalRotation))
                    continue;

                triangleCount += CopyMesh(
                    filter,
                    segmentRoot.transform,
                    sourceRoot,
                    sourceCenter,
                    sourceToLocalRotation,
                    stageBounds,
                    localPath,
                    padding,
                    highestSurfaceY);
            }

            if (triangleCount > 0)
            {
                float surfaceOffset = FindMedianSurfaceOffset(
                    localPath,
                    highestSurfaceY);
                segmentRoot.transform.localPosition = Vector3.up * surfaceOffset;

                Bounds copiedBounds = meshes[0].bounds;
                for (int i = 1; i < meshes.Count; i++)
                    copiedBounds.Encapsulate(meshes[i].bounds);

                Debug.Log(
                    $"[EventTrackSegment] triangles={triangleCount}, " +
                    $"pathCenter={stageBounds.center:F4}, pathSize={stageBounds.size:F4}, " +
                    $"meshCenter={copiedBounds.center:F4}, meshSize={copiedBounds.size:F4}, " +
                    $"centerDelta={(copiedBounds.center - stageBounds.center):F4}, " +
                    $"surfaceOffset={surfaceOffset:F4}, sourceScale={sourceRoot.lossyScale:F4}");
                return true;
            }

            Object.Destroy(segmentRoot);
            Clear();
            return false;
        }

        public void Clear()
        {
            foreach (Mesh mesh in meshes)
            {
                if (mesh != null)
                    Object.Destroy(mesh);
            }

            meshes.Clear();
        }

        private static bool CanCopy(
            MeshFilter filter,
            Transform sourceRoot,
            Bounds stageBounds,
            Vector3 sourceCenter,
            Quaternion sourceToLocalRotation)
        {
            if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable)
                return false;

            MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                return false;
            if (filter.GetComponentInParent<ReplayCarView>() != null)
                return false;

            return RendererBoundsIntersect(
                renderer.bounds,
                sourceRoot,
                sourceCenter,
                sourceToLocalRotation,
                stageBounds);
        }

        private int CopyMesh(
            MeshFilter sourceFilter,
            Transform parent,
            Transform sourceRoot,
            Vector3 sourceCenter,
            Quaternion sourceToLocalRotation,
            Bounds clipBounds,
            IReadOnlyList<Vector3> localPath,
            float padding,
            float[] highestSurfaceY)
        {
            Mesh source = sourceFilter.sharedMesh;
            Vector3[] sourceVertices = source.vertices;
            Vector3[] sourceNormals = source.normals;
            Vector2[] sourceUv = source.uv;
            Vector2[] sourceUv2 = source.uv2;
            Color[] sourceColors = source.colors;
            bool hasNormals = sourceNormals.Length == sourceVertices.Length;
            bool hasUv = sourceUv.Length == sourceVertices.Length;
            bool hasUv2 = sourceUv2.Length == sourceVertices.Length;
            bool hasColors = sourceColors.Length == sourceVertices.Length;
            Matrix4x4 sourceToEvent = Matrix4x4.Rotate(sourceToLocalRotation) *
                sourceRoot.worldToLocalMatrix *
                sourceFilter.transform.localToWorldMatrix;
            Matrix4x4 normalMatrix = sourceToEvent.inverse.transpose;
            bool reverseWinding = sourceToEvent.determinant < 0f;

            Vector3[] positions = new Vector3[sourceVertices.Length];
            Vector3[] normals = hasNormals ? new Vector3[sourceVertices.Length] : null;
            for (int i = 0; i < sourceVertices.Length; i++)
            {
                Vector3 world = sourceFilter.transform.TransformPoint(sourceVertices[i]);
                Vector3 sourceLocal = sourceRoot.InverseTransformPoint(world);
                positions[i] = sourceToLocalRotation * (sourceLocal - sourceCenter);

                if (!hasNormals)
                    continue;

                normals[i] = normalMatrix.MultiplyVector(sourceNormals[i]).normalized;
            }

            List<Vector3> vertices = new();
            List<Vector3> copiedNormals = hasNormals ? new List<Vector3>() : null;
            List<Vector2> uv = hasUv ? new List<Vector2>() : null;
            List<Vector2> uv2 = hasUv2 ? new List<Vector2>() : null;
            List<Color> colors = hasColors ? new List<Color>() : null;
            List<List<int>> submeshes = new(source.subMeshCount);
            Dictionary<int, int> remap = new();
            int keptTriangles = 0;
            Vector2 maximumTriangleSpan = new Vector2(
                clipBounds.size.x * 2f,
                clipBounds.size.z * 2f);

            for (int submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                List<int> triangles = new();
                submeshes.Add(triangles);
                if (source.GetTopology(submesh) != MeshTopology.Triangles)
                    continue;

                int[] indices = source.GetIndices(submesh);
                for (int index = 0; index + 2 < indices.Length; index += 3)
                {
                    int a = indices[index];
                    int b = indices[index + 1];
                    int c = indices[index + 2];
                    if (!TriangleIntersects(
                            positions[a],
                            positions[b],
                            positions[c],
                            clipBounds,
                            maximumTriangleSpan,
                            localPath,
                            padding))
                        continue;

                    RecordSurfaceHeights(
                        positions[a],
                        positions[b],
                        positions[c],
                        localPath,
                        highestSurfaceY);

                    triangles.Add(CopyVertex(a));
                    triangles.Add(CopyVertex(reverseWinding ? c : b));
                    triangles.Add(CopyVertex(reverseWinding ? b : c));
                    keptTriangles++;
                }
            }

            if (keptTriangles == 0)
                return 0;

            Mesh mesh = new Mesh
            {
                name = $"EventTrack_{source.name}",
                indexFormat = vertices.Count > ushort.MaxValue
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            if (hasNormals)
                mesh.SetNormals(copiedNormals);
            if (hasUv)
                mesh.SetUVs(0, uv);
            if (hasUv2)
                mesh.SetUVs(1, uv2);
            if (hasColors)
                mesh.SetColors(colors);
            mesh.subMeshCount = submeshes.Count;
            for (int submesh = 0; submesh < submeshes.Count; submesh++)
                mesh.SetTriangles(submeshes[submesh], submesh, false);
            if (!hasNormals)
                mesh.RecalculateNormals();
            if (hasUv)
                mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            meshes.Add(mesh);

            GameObject copy = new GameObject(sourceFilter.name, typeof(MeshFilter), typeof(MeshRenderer));
            copy.transform.SetParent(parent, false);
            copy.GetComponent<MeshFilter>().sharedMesh = mesh;

            MeshRenderer sourceRenderer = sourceFilter.GetComponent<MeshRenderer>();
            MeshRenderer renderer = copy.GetComponent<MeshRenderer>();
            renderer.sharedMaterials = sourceRenderer.sharedMaterials;
            renderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
            renderer.receiveShadows = sourceRenderer.receiveShadows;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            return keptTriangles;

            int CopyVertex(int sourceIndex)
            {
                if (remap.TryGetValue(sourceIndex, out int existing))
                    return existing;

                int copied = vertices.Count;
                remap[sourceIndex] = copied;
                vertices.Add(positions[sourceIndex]);
                if (hasNormals)
                    copiedNormals.Add(normals[sourceIndex]);
                if (hasUv)
                    uv.Add(sourceUv[sourceIndex]);
                if (hasUv2)
                    uv2.Add(sourceUv2[sourceIndex]);
                if (hasColors)
                    colors.Add(sourceColors[sourceIndex]);
                return copied;
            }
        }

        private static Bounds BuildStageBounds(
            IReadOnlyList<Vector3> sourcePath,
            Vector3 sourceCenter,
            Quaternion sourceToLocalRotation,
            float padding)
        {
            if (sourcePath == null || sourcePath.Count == 0)
                return new Bounds(Vector3.zero, new Vector3(0.1f, 0.04f, 0.1f));

            Bounds bounds = new Bounds(
                sourceToLocalRotation * (sourcePath[0] - sourceCenter),
                Vector3.zero);
            for (int i = 1; i < sourcePath.Count; i++)
            {
                bounds.Encapsulate(
                    sourceToLocalRotation * (sourcePath[i] - sourceCenter));
            }

            float safePadding = Mathf.Max(0f, padding);
            bounds.Expand(new Vector3(safePadding * 2f, 0.04f, safePadding * 2f));
            return bounds;
        }

        private static Vector3[] BuildLocalPath(
            IReadOnlyList<Vector3> sourcePath,
            Vector3 sourceCenter,
            Quaternion sourceToLocalRotation)
        {
            int stride = Mathf.Max(
                1,
                Mathf.CeilToInt(sourcePath.Count / (float)MaxPathSamples));
            List<Vector3> samples = new(MaxPathSamples + 1);
            for (int i = 0; i < sourcePath.Count; i += stride)
            {
                samples.Add(sourceToLocalRotation *
                    (sourcePath[i] - sourceCenter));
            }

            int last = sourcePath.Count - 1;
            if ((sourcePath.Count - 1) % stride != 0)
            {
                samples.Add(sourceToLocalRotation *
                    (sourcePath[last] - sourceCenter));
            }

            return samples.ToArray();
        }

        private static void RecordSurfaceHeights(
            Vector3 a,
            Vector3 b,
            Vector3 c,
            IReadOnlyList<Vector3> samples,
            float[] highestSurfaceY)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude <= 0.00000001f ||
                Mathf.Abs(normal.y) < normal.magnitude * MinimumSurfaceNormalY)
                return;

            for (int i = 0; i < samples.Count; i++)
            {
                Vector3 sample = samples[i];
                if (!TryGetSurfaceY(sample, a, b, c, out float surfaceY))
                    continue;

                highestSurfaceY[i] = Mathf.Max(highestSurfaceY[i], surfaceY);
            }
        }

        private static bool TryGetSurfaceY(
            Vector3 point,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            out float y)
        {
            Vector2 v0 = new Vector2(b.x - a.x, b.z - a.z);
            Vector2 v1 = new Vector2(c.x - a.x, c.z - a.z);
            Vector2 v2 = new Vector2(point.x - a.x, point.z - a.z);
            float denominator = v0.x * v1.y - v1.x * v0.y;
            if (Mathf.Abs(denominator) <= 0.00000001f)
            {
                y = 0f;
                return false;
            }

            float bWeight = (v2.x * v1.y - v1.x * v2.y) / denominator;
            float cWeight = (v0.x * v2.y - v2.x * v0.y) / denominator;
            float aWeight = 1f - bWeight - cWeight;
            const float edgeTolerance = 0.0001f;
            if (aWeight < -edgeTolerance ||
                bWeight < -edgeTolerance ||
                cWeight < -edgeTolerance)
            {
                y = 0f;
                return false;
            }

            y = a.y * aWeight + b.y * bWeight + c.y * cWeight;
            return true;
        }

        private static float FindMedianSurfaceOffset(
            IReadOnlyList<Vector3> samples,
            IReadOnlyList<float> highestSurfaceY)
        {
            List<float> found = new(samples.Count);
            for (int i = 0; i < samples.Count; i++)
            {
                if (!float.IsNegativeInfinity(highestSurfaceY[i]))
                    found.Add(samples[i].y - highestSurfaceY[i]);
            }

            if (found.Count == 0)
                return 0f;

            found.Sort();
            int middle = found.Count / 2;
            return found.Count % 2 == 0
                ? (found[middle - 1] + found[middle]) * 0.5f
                : found[middle];
        }

        private static bool RendererBoundsIntersect(
            Bounds worldBounds,
            Transform sourceRoot,
            Vector3 sourceCenter,
            Quaternion sourceToLocalRotation,
            Bounds clipBounds)
        {
            Vector3 min = worldBounds.min;
            Vector3 max = worldBounds.max;
            Bounds localBounds = default;
            bool found = false;

            for (int x = 0; x < 2; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int z = 0; z < 2; z++)
                    {
                        Vector3 world = new Vector3(
                            x == 0 ? min.x : max.x,
                            y == 0 ? min.y : max.y,
                            z == 0 ? min.z : max.z);
                        Vector3 point = sourceToLocalRotation *
                            (sourceRoot.InverseTransformPoint(world) - sourceCenter);
                        if (!found)
                        {
                            localBounds = new Bounds(point, Vector3.zero);
                            found = true;
                        }
                        else
                        {
                            localBounds.Encapsulate(point);
                        }
                    }
                }
            }

            return localBounds.max.x >= clipBounds.min.x &&
                localBounds.min.x <= clipBounds.max.x &&
                localBounds.max.z >= clipBounds.min.z &&
                localBounds.min.z <= clipBounds.max.z;
        }

        private static bool TriangleIntersects(
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Bounds bounds,
            Vector2 maximumSpan,
            IReadOnlyList<Vector3> localPath,
            float padding)
        {
            float minX = Mathf.Min(a.x, Mathf.Min(b.x, c.x));
            float maxX = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
            float minZ = Mathf.Min(a.z, Mathf.Min(b.z, c.z));
            float maxZ = Mathf.Max(a.z, Mathf.Max(b.z, c.z));

            if (maxX < bounds.min.x || minX > bounds.max.x ||
                maxZ < bounds.min.z || minZ > bounds.max.z)
                return false;

            if (maxX - minX > maximumSpan.x ||
                maxZ - minZ > maximumSpan.y)
                return false;

            return TriangleNearPath(a, b, c, localPath, padding);
        }

        private static bool TriangleNearPath(
            Vector3 a,
            Vector3 b,
            Vector3 c,
            IReadOnlyList<Vector3> path,
            float padding)
        {
            if (path == null || path.Count == 0)
                return false;

            float paddingSquared = padding * padding;
            for (int i = 0; i < path.Count; i++)
            {
                if (PointInsideTriangleXZ(path[i], a, b, c))
                    return true;
            }

            for (int i = 1; i < path.Count; i++)
            {
                Vector3 from = path[i - 1];
                Vector3 to = path[i];
                if (DistanceToSegmentXZSquared(a, from, to) <= paddingSquared ||
                    DistanceToSegmentXZSquared(b, from, to) <= paddingSquared ||
                    DistanceToSegmentXZSquared(c, from, to) <= paddingSquared)
                    return true;
            }

            return false;
        }

        private static bool PointInsideTriangleXZ(
            Vector3 point,
            Vector3 a,
            Vector3 b,
            Vector3 c)
        {
            return TryGetSurfaceY(point, a, b, c, out _);
        }

        private static float DistanceToSegmentXZSquared(
            Vector3 point,
            Vector3 from,
            Vector3 to)
        {
            Vector2 point2 = new Vector2(point.x, point.z);
            Vector2 from2 = new Vector2(from.x, from.z);
            Vector2 segment = new Vector2(to.x - from.x, to.z - from.z);
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= 0.00000001f)
                return (point2 - from2).sqrMagnitude;

            float interpolation = Mathf.Clamp01(
                Vector2.Dot(point2 - from2, segment) / lengthSquared);
            Vector2 closest = from2 + segment * interpolation;
            return (point2 - closest).sqrMagnitude;
        }
    }
}
