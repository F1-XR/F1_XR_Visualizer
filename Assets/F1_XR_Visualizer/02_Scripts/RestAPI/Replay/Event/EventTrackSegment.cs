using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace F1XR.RestAPI.Replay
{
    internal sealed class EventTrackSegment
    {
        private const int MaxPathSamples = 48;
        private const float MinimumSurfaceNormalY = 0.35f;
        private const float MinimumReliableEdgeCoverage = 0.7f;

        private readonly List<Mesh> meshes = new();
        private readonly List<RoadTriangle> roadTriangles = new();
        private readonly List<RoadTriangle> drivableTriangles = new();

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
            float[] nearestSurfaceY = new float[localPath.Length];
            for (int i = 0; i < nearestSurfaceY.Length; i++)
                nearestSurfaceY[i] = float.NegativeInfinity;

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
                    nearestSurfaceY);
            }

            if (triangleCount > 0)
            {
                float surfaceOffset = FindMedianSurfaceOffset(
                    localPath,
                    nearestSurfaceY);
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
            roadTriangles.Clear();
            drivableTriangles.Clear();
        }

        public bool TryBuildSafetyApronEdges(
            IReadOnlyList<Vector3> path,
            float vehicleWidth,
            List<Vector3> leftEdges,
            List<Vector3> rightEdges)
        {
            return TryBuildEdges(
                path,
                vehicleWidth,
                roadTriangles,
                "Safety apron",
                false,
                leftEdges,
                rightEdges);
        }

        public bool TryBuildDrivableEdges(
            IReadOnlyList<Vector3> path,
            float vehicleWidth,
            List<Vector3> leftEdges,
            List<Vector3> rightEdges)
        {
            return TryBuildEdges(
                path,
                vehicleWidth,
                drivableTriangles,
                "Drivable",
                true,
                leftEdges,
                rightEdges);
        }

        public bool TryBuildReliableRoadEdges(
            IReadOnlyList<Vector3> path,
            float vehicleWidth,
            List<Vector3> leftEdges,
            List<Vector3> rightEdges)
        {
            return TryBuildEdges(
                path,
                vehicleWidth,
                roadTriangles,
                "Reliable road surface",
                true,
                leftEdges,
                rightEdges);
        }

        private bool TryBuildEdges(
            IReadOnlyList<Vector3> path,
            float vehicleWidth,
            IReadOnlyList<RoadTriangle> triangles,
            string label,
            bool requireCompleteCoverage,
            List<Vector3> leftEdges,
            List<Vector3> rightEdges)
        {
            leftEdges.Clear();
            rightEdges.Clear();
            if (path == null ||
                path.Count < 2 ||
                vehicleWidth <= 0f ||
                triangles == null ||
                triangles.Count == 0)
            {
                return false;
            }

            int detected = 0;
            float[] minimumOffsets =
                new float[path.Count];
            float[] maximumOffsets =
                new float[path.Count];
            for (int i = 0; i < path.Count; i++)
            {
                if (TryFindRoadSpan(
                        path[i],
                        GetPathSide(path, i),
                        vehicleWidth,
                        triangles,
                        out float roadMinimum,
                        out float roadMaximum))
                {
                    minimumOffsets[i] = roadMinimum;
                    maximumOffsets[i] = roadMaximum;
                    detected++;
                }
                else
                {
                    minimumOffsets[i] = float.NaN;
                    maximumOffsets[i] = float.NaN;
                }
            }

            Debug.Log(
                $"[EventTrackSegment] {label} road-edge samples=" +
                $"{detected}/{path.Count}.");
            float coverage =
                detected /
                (float)path.Count;
            if (detected == 0 ||
                requireCompleteCoverage &&
                coverage < MinimumReliableEdgeCoverage)
            {
                leftEdges.Clear();
                rightEdges.Clear();
                return false;
            }

            FillMissingOffsets(minimumOffsets);
            FillMissingOffsets(maximumOffsets);
            for (int i = 0; i < path.Count; i++)
            {
                Vector3 side =
                    GetPathSide(path, i);
                leftEdges.Add(
                    path[i] +
                    side * minimumOffsets[i]);
                rightEdges.Add(
                    path[i] +
                    side * maximumOffsets[i]);
            }

            return true;
        }

        private static Vector3 GetPathSide(
            IReadOnlyList<Vector3> path,
            int index)
        {
            Vector3 before =
                path[Mathf.Max(0, index - 1)];
            Vector3 after =
                path[Mathf.Min(path.Count - 1, index + 1)];
            Vector3 tangent = after - before;
            tangent.y = 0f;
            if (tangent.sqrMagnitude <= 0.000001f)
                tangent = Vector3.forward;

            return Vector3.Cross(
                Vector3.up,
                tangent.normalized);
        }

        private static void FillMissingOffsets(
            float[] offsets)
        {
            int first = -1;
            for (int i = 0; i < offsets.Length; i++)
            {
                if (!float.IsNaN(offsets[i]))
                {
                    first = i;
                    break;
                }
            }

            if (first < 0)
                return;

            for (int i = 0; i < first; i++)
                offsets[i] = offsets[first];

            int previous = first;
            for (int i = first + 1; i < offsets.Length; i++)
            {
                if (float.IsNaN(offsets[i]))
                    continue;

                int gap = i - previous;
                for (int missing = previous + 1;
                     missing < i;
                     missing++)
                {
                    offsets[missing] = Mathf.Lerp(
                        offsets[previous],
                        offsets[i],
                        (missing - previous) /
                        (float)gap);
                }

                previous = i;
            }

            for (int i = previous + 1;
                 i < offsets.Length;
                 i++)
            {
                offsets[i] = offsets[previous];
            }
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
            float[] nearestSurfaceY)
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
            MeshRenderer sourceRenderer =
                sourceFilter.GetComponent<MeshRenderer>();

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
            List<Material> materials = new(source.subMeshCount);
            Material[] sourceMaterials = sourceRenderer.sharedMaterials;
            Dictionary<int, int> remap = new();
            int keptTriangles = 0;
            Vector2 maximumTriangleSpan = new Vector2(
                clipBounds.size.x * 2f,
                clipBounds.size.z * 2f);

            for (int submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                List<int> triangles = new();
                if (source.GetTopology(submesh) != MeshTopology.Triangles)
                    continue;

                bool isRoadSurface =
                    IsRoadSurfaceSubmesh(
                        sourceRenderer,
                        submesh);
                bool isDrivableSurface =
                    IsDrivableSurfaceSubmesh(
                        sourceRenderer,
                        submesh);
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

                    RecordNearestSurfaceHeights(
                        positions[a],
                        positions[b],
                        positions[c],
                        localPath,
                        nearestSurfaceY);
                    if (isRoadSurface)
                    {
                        RecordRoadTriangle(
                            positions[a],
                            positions[b],
                            positions[c],
                            roadTriangles);
                    }
                    if (isDrivableSurface)
                    {
                        RecordRoadTriangle(
                            positions[a],
                            positions[b],
                            positions[c],
                            drivableTriangles);
                    }

                    triangles.Add(CopyVertex(a));
                    triangles.Add(CopyVertex(reverseWinding ? c : b));
                    triangles.Add(CopyVertex(reverseWinding ? b : c));
                    keptTriangles++;
                }

                if (triangles.Count == 0)
                    continue;

                submeshes.Add(triangles);
                materials.Add(
                    sourceMaterials.Length > 0
                        ? sourceMaterials[
                            Mathf.Min(
                                submesh,
                                sourceMaterials.Length - 1)]
                        : null);
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

            MeshRenderer renderer = copy.GetComponent<MeshRenderer>();
            renderer.sharedMaterials = materials.ToArray();
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

        private bool TryFindRoadSpan(
            Vector3 point,
            Vector3 side,
            float maximumCenterDistance,
            IReadOnlyList<RoadTriangle> triangles,
            out float minimum,
            out float maximum)
        {
            List<RoadInterval> intervals = new();
            for (int i = 0; i < triangles.Count; i++)
            {
                if (TryIntersectCrossSection(
                        point,
                        side,
                        triangles[i],
                        out RoadInterval interval))
                {
                    intervals.Add(interval);
                }
            }

            minimum = 0f;
            maximum = 0f;
            if (intervals.Count == 0)
                return false;

            intervals.Sort(
                (left, right) =>
                    left.Minimum.CompareTo(
                        right.Minimum));
            List<RoadInterval> merged = new();
            RoadInterval current = intervals[0];
            for (int i = 1; i < intervals.Count; i++)
            {
                RoadInterval next = intervals[i];
                if (next.Minimum <=
                    current.Maximum + 0.00001f)
                {
                    current.Maximum = Mathf.Max(
                        current.Maximum,
                        next.Maximum);
                    continue;
                }

                merged.Add(current);
                current = next;
            }
            merged.Add(current);

            float closestDistance =
                float.PositiveInfinity;
            bool found = false;
            for (int i = 0; i < merged.Count; i++)
            {
                RoadInterval interval = merged[i];
                float distance =
                    interval.Minimum > 0f
                        ? interval.Minimum
                        : interval.Maximum < 0f
                            ? -interval.Maximum
                            : 0f;
                if (distance >= closestDistance)
                    continue;

                closestDistance = distance;
                minimum = interval.Minimum;
                maximum = interval.Maximum;
                found = true;
            }

            return found &&
                closestDistance <= maximumCenterDistance;
        }

        private static bool TryIntersectCrossSection(
            Vector3 point,
            Vector3 side,
            RoadTriangle triangle,
            out RoadInterval interval)
        {
            Vector2 lateral =
                new Vector2(side.x, side.z);
            Vector2 longitudinal =
                new Vector2(-side.z, side.x);
            Vector2 origin =
                new Vector2(point.x, point.z);
            Vector2[] vertices =
            {
                new Vector2(
                    triangle.A.x,
                    triangle.A.z),
                new Vector2(
                    triangle.B.x,
                    triangle.B.z),
                new Vector2(
                    triangle.C.x,
                    triangle.C.z)
            };
            float[] along = new float[3];
            float[] across = new float[3];
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector2 relative =
                    vertices[i] - origin;
                along[i] =
                    Vector2.Dot(
                        relative,
                        longitudinal);
                across[i] =
                    Vector2.Dot(
                        relative,
                        lateral);
            }

            List<float> intersections = new(4);
            for (int i = 0; i < 3; i++)
            {
                int next = (i + 1) % 3;
                if (Mathf.Abs(along[i]) <= 0.000001f)
                    intersections.Add(across[i]);

                if ((along[i] < 0f &&
                     along[next] > 0f) ||
                    (along[i] > 0f &&
                     along[next] < 0f))
                {
                    float interpolation =
                        along[i] /
                        (along[i] - along[next]);
                    intersections.Add(
                        Mathf.Lerp(
                            across[i],
                            across[next],
                            interpolation));
                }
            }

            if (intersections.Count < 2)
            {
                interval = default;
                return false;
            }

            float minimum = intersections[0];
            float maximum = intersections[0];
            for (int i = 1;
                 i < intersections.Count;
                 i++)
            {
                minimum = Mathf.Min(
                    minimum,
                    intersections[i]);
                maximum = Mathf.Max(
                    maximum,
                    intersections[i]);
            }

            interval = new RoadInterval(
                minimum,
                maximum);
            return maximum - minimum > 0.000001f;
        }

        private void RecordRoadTriangle(
            Vector3 a,
            Vector3 b,
            Vector3 c,
            List<RoadTriangle> destination)
        {
            Vector3 normal =
                Vector3.Cross(b - a, c - a);
            float normalMagnitude = normal.magnitude;
            if (normalMagnitude <= 0f ||
                Mathf.Abs(normal.y) <
                normalMagnitude *
                MinimumSurfaceNormalY)
            {
                return;
            }

            destination.Add(
                new RoadTriangle(a, b, c));
        }

        private static bool IsRoadSurfaceSubmesh(
            MeshRenderer renderer,
            int submesh)
        {
            if (renderer == null)
                return false;

            Material[] materials =
                renderer.sharedMaterials;
            if (materials == null ||
                materials.Length == 0)
            {
                return false;
            }

            Material material =
                materials[Mathf.Min(
                    submesh,
                    materials.Length - 1)];
            if (material == null)
                return false;

            string name =
                material.name.ToLowerInvariant();
            if (name.Contains("pit") ||
                name.Contains("grass") ||
                name.Contains("ground") ||
                name.Contains("grvl") ||
                name.Contains("gravel") ||
                name.Contains("terrain") ||
                name.Contains("tree") ||
                name.Contains("forest"))
            {
                return false;
            }

            return name.Contains("road") ||
                name.Contains("asphalt") ||
                name.Contains("tarmac") ||
                name.Contains("track") ||
                name.Contains("curb") ||
                name.Contains("kerb") ||
                name.Contains("rumble") ||
                name.Contains("rmbl") ||
                name.Contains("rdcp") ||
                name.Contains("skid") ||
                name.Contains("groove") ||
                name.Contains("runoff") ||
                name.Contains("pitexitline") ||
                name == "grid" ||
                name.StartsWith("line");
        }

        private static bool IsDrivableSurfaceSubmesh(
            MeshRenderer renderer,
            int submesh)
        {
            if (renderer == null)
                return false;

            Material[] materials =
                renderer.sharedMaterials;
            if (materials == null ||
                materials.Length == 0)
            {
                return false;
            }

            Material material =
                materials[Mathf.Min(
                    submesh,
                    materials.Length - 1)];
            if (material == null)
                return false;

            string name =
                material.name.ToLowerInvariant();
            if (name.Contains("pit") ||
                name.Contains("grass") ||
                name.Contains("ground") ||
                name.Contains("grvl") ||
                name.Contains("gravel") ||
                name.Contains("terrain") ||
                name.Contains("tree") ||
                name.Contains("forest") ||
                name.Contains("curb") ||
                name.Contains("kerb") ||
                name.Contains("rumble") ||
                name.Contains("rmbl") ||
                name.Contains("rdcp") ||
                name.Contains("runoff") ||
                name.Contains("green") ||
                name.Contains("skid") ||
                name.Contains("groove") ||
                name.Contains("line") ||
                name == "grid")
            {
                return false;
            }

            return name.Contains("road") ||
                name.Contains("asphalt") ||
                name.Contains("tarmac") ||
                name.Contains("track");
        }

        private readonly struct RoadTriangle
        {
            public RoadTriangle(
                Vector3 a,
                Vector3 b,
                Vector3 c)
            {
                A = a;
                B = b;
                C = c;
            }

            public Vector3 A { get; }
            public Vector3 B { get; }
            public Vector3 C { get; }
        }

        private struct RoadInterval
        {
            public RoadInterval(
                float minimum,
                float maximum)
            {
                Minimum = minimum;
                Maximum = maximum;
            }

            public float Minimum;
            public float Maximum;
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

        private static void RecordNearestSurfaceHeights(
            Vector3 a,
            Vector3 b,
            Vector3 c,
            IReadOnlyList<Vector3> samples,
            float[] nearestSurfaceY)
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

                if (float.IsNegativeInfinity(nearestSurfaceY[i]) ||
                    Mathf.Abs(sample.y - surfaceY) <
                    Mathf.Abs(sample.y - nearestSurfaceY[i]))
                {
                    nearestSurfaceY[i] = surfaceY;
                }
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
            IReadOnlyList<float> nearestSurfaceY)
        {
            List<float> found = new(samples.Count);
            for (int i = 0; i < samples.Count; i++)
            {
                if (!float.IsNegativeInfinity(nearestSurfaceY[i]))
                    found.Add(samples[i].y - nearestSurfaceY[i]);
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
