using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace F1XR.RestAPI.Replay
{
    internal enum ShowcaseOccluderKind
    {
        Unknown,
        Decorative
    }

    internal enum EventTrackSegmentSurfaceMode
    {
        Full,
        TrackContextOnly
    }

    internal readonly struct ShowcaseOcclusionHit
    {
        public ShowcaseOcclusionHit(
            Material material,
            ShowcaseOccluderKind kind)
        {
            Material = material;
            Kind = kind;
            IsOccluded = true;
        }

        public bool IsOccluded { get; }
        public Material Material { get; }
        public ShowcaseOccluderKind Kind { get; }
    }

    internal sealed class EventTrackSegment
    {
        private const int MaxPathSamples = 48;
        private const float MinimumSurfaceNormalY = 0.35f;
        private const float MinimumReliableEdgeCoverage = 0.7f;
        private const int MinimumTrackContextPrimaryTriangles = 24;

        private readonly List<Mesh> meshes = new();
        private readonly List<RoadTriangle> roadTriangles = new();
        private readonly List<RoadTriangle> drivableTriangles = new();
        private readonly List<OcclusionTriangle> occlusionTriangles = new();
        private readonly HashSet<Material> ignoredOcclusionMaterials = new();
        private Transform segmentRoot;
        private EventTrackSegmentSurfaceMode surfaceMode;
        private int trackContextPrimaryTriangles;

        public bool Build(
            Transform parent,
            Transform sourceRoot,
            IReadOnlyList<Vector3> sourcePath,
            Vector3 sourceCenter,
            Quaternion sourceToLocalRotation,
            float padding,
            out Bounds stageBounds)
        {
            return Build(
                parent,
                sourceRoot,
                sourcePath,
                sourceCenter,
                sourceToLocalRotation,
                padding,
                EventTrackSegmentSurfaceMode.Full,
                out stageBounds);
        }

        public bool Build(
            Transform parent,
            Transform sourceRoot,
            IReadOnlyList<Vector3> sourcePath,
            Vector3 sourceCenter,
            Quaternion sourceToLocalRotation,
            float padding,
            EventTrackSegmentSurfaceMode requestedSurfaceMode,
            out Bounds stageBounds)
        {
            surfaceMode = requestedSurfaceMode;
            trackContextPrimaryTriangles = 0;
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

            GameObject segmentObject = new GameObject("ActualTrackRegion");
            segmentObject.transform.SetParent(parent, false);
            segmentRoot = segmentObject.transform;

            Vector3[] localPath = BuildLocalPath(
                sourcePath,
                sourceCenter,
                sourceToLocalRotation);
            float[] nearestSurfaceY = new float[localPath.Length];
            float[] nearestPrimarySurfaceY = surfaceMode ==
                EventTrackSegmentSurfaceMode.TrackContextOnly
                    ? new float[localPath.Length]
                    : null;
            for (int i = 0; i < nearestSurfaceY.Length; i++)
            {
                nearestSurfaceY[i] = float.NegativeInfinity;
                if (nearestPrimarySurfaceY != null)
                    nearestPrimarySurfaceY[i] = float.NegativeInfinity;
            }

            int triangleCount = 0;
            foreach (MeshFilter filter in visualRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!CanCopy(filter, sourceRoot, stageBounds, sourceCenter, sourceToLocalRotation))
                    continue;

                triangleCount += CopyMesh(
                    filter,
                    segmentRoot,
                    sourceRoot,
                    sourceCenter,
                    sourceToLocalRotation,
                    stageBounds,
                    localPath,
                    padding,
                    nearestSurfaceY,
                    nearestPrimarySurfaceY);
            }

            int primaryCoverageCount = CountSurfaceCoverage(
                nearestPrimarySurfaceY);
            int surfaceCoverageCount = CountSurfaceCoverage(
                nearestSurfaceY);
            float surfaceCoverage = nearestSurfaceY.Length > 0
                ? surfaceCoverageCount / (float)nearestSurfaceY.Length
                : 0f;
            bool hasRequiredSurface = triangleCount > 0 &&
                (surfaceMode !=
                     EventTrackSegmentSurfaceMode.TrackContextOnly ||
                 trackContextPrimaryTriangles >=
                     MinimumTrackContextPrimaryTriangles);
            if (hasRequiredSurface)
            {
                float surfaceOffset = FindMedianSurfaceOffset(
                    localPath,
                    nearestSurfaceY);
                segmentRoot.localPosition = Vector3.up * surfaceOffset;

                Bounds copiedBounds = meshes[0].bounds;
                for (int i = 1; i < meshes.Count; i++)
                    copiedBounds.Encapsulate(meshes[i].bounds);

                Debug.Log(
                    $"[EventTrackSegment] triangles={triangleCount}, " +
                    $"surfaceCoverage={surfaceCoverageCount}/" +
                    $"{nearestSurfaceY.Length}, " +
                    $"primaryCoverage={primaryCoverageCount}/" +
                    $"{nearestPrimarySurfaceY?.Length ?? 0}, " +
                    $"pathCenter={stageBounds.center:F4}, pathSize={stageBounds.size:F4}, " +
                    $"meshCenter={copiedBounds.center:F4}, meshSize={copiedBounds.size:F4}, " +
                    $"centerDelta={(copiedBounds.center - stageBounds.center):F4}, " +
                    $"surfaceOffset={surfaceOffset:F4}, sourceScale={sourceRoot.lossyScale:F4}");
                return true;
            }

            if (surfaceMode ==
                EventTrackSegmentSurfaceMode.TrackContextOnly)
            {
                Debug.LogWarning(
                    "[EventTrackSegment] Track context rejected: " +
                    $"triangles={triangleCount}, " +
                    $"primaryTriangles={trackContextPrimaryTriangles}, " +
                    $"surfaceCoverage={surfaceCoverageCount}/" +
                    $"{nearestSurfaceY.Length}, " +
                    $"primaryCoverage={primaryCoverageCount}/" +
                    $"{nearestPrimarySurfaceY?.Length ?? 0}.");
            }

            Object.Destroy(segmentObject);
            Clear();
            return false;
        }

        public bool WarpToPath(
            IReadOnlyList<Vector3> sourcePath,
            IReadOnlyList<Vector3> targetPath,
            float maximumLateralDistance,
            float maximumLongitudinalDistance,
            out Bounds warpedBounds)
        {
            warpedBounds = default;
            if (segmentRoot == null ||
                meshes.Count == 0 ||
                sourcePath == null ||
                targetPath == null ||
                sourcePath.Count < 2 ||
                sourcePath.Count != targetPath.Count)
            {
                return false;
            }

            float lateralLimit = Mathf.Max(
                0.0001f,
                maximumLateralDistance);
            float longitudinalLimit = Mathf.Max(
                0.0001f,
                maximumLongitudinalDistance);
            bool hasBounds = false;
            for (int meshIndex = 0; meshIndex < meshes.Count; meshIndex++)
            {
                Mesh mesh = meshes[meshIndex];
                if (mesh == null || !mesh.isReadable)
                    return false;

                Vector3[] vertices = mesh.vertices;
                for (int vertexIndex = 0;
                     vertexIndex < vertices.Length;
                     vertexIndex++)
                {
                    vertices[vertexIndex] = WarpPoint(
                        vertices[vertexIndex],
                        sourcePath,
                        targetPath,
                        lateralLimit,
                        longitudinalLimit);
                }

                mesh.vertices = vertices;
                mesh.RecalculateNormals();
                if (mesh.uv != null &&
                    mesh.uv.Length == vertices.Length)
                {
                    mesh.RecalculateTangents();
                }
                mesh.RecalculateBounds();
                if (!hasBounds)
                {
                    warpedBounds = mesh.bounds;
                    hasBounds = true;
                }
                else
                {
                    warpedBounds.Encapsulate(mesh.bounds);
                }
            }

            return hasBounds;
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
            occlusionTriangles.Clear();
            ignoredOcclusionMaterials.Clear();
            segmentRoot = null;
            trackContextPrimaryTriangles = 0;
        }

        public void SetIgnoredOcclusionMaterials(
            IReadOnlyCollection<Material> materials)
        {
            ignoredOcclusionMaterials.Clear();
            if (materials == null)
                return;

            foreach (Material material in materials)
            {
                if (material != null)
                    ignoredOcclusionMaterials.Add(material);
            }
        }

        public bool TryCollectRemovableOccluders(
            Vector3 worldOrigin,
            Vector3 worldTarget,
            float worldTargetClearance,
            HashSet<Material> destination,
            out bool occluded,
            out bool hasNonRemovableOccluder)
        {
            occluded = false;
            hasNonRemovableOccluder = false;
            if (segmentRoot == null || occlusionTriangles.Count == 0)
                return false;

            Vector3 origin = segmentRoot.InverseTransformPoint(worldOrigin);
            Vector3 target = segmentRoot.InverseTransformPoint(worldTarget);
            Vector3 ray = target - origin;
            float distance = ray.magnitude;
            if (distance <= 0.0001f)
                return true;

            float worldScale = Mathf.Max(
                Mathf.Abs(segmentRoot.lossyScale.x),
                Mathf.Abs(segmentRoot.lossyScale.y),
                Mathf.Abs(segmentRoot.lossyScale.z));
            float targetClearance = worldScale > 0.0001f
                ? Mathf.Max(0f, worldTargetClearance) / worldScale
                : 0f;
            float maximumHitDistance = distance - targetClearance;
            if (maximumHitDistance <= 0.0001f)
                return true;

            Vector3 direction = ray / distance;
            for (int i = 0; i < occlusionTriangles.Count; i++)
            {
                OcclusionTriangle triangle = occlusionTriangles[i];
                if (ignoredOcclusionMaterials.Contains(triangle.Material) ||
                    !triangle.Intersects(
                        origin,
                        direction,
                        maximumHitDistance))
                {
                    continue;
                }

                occluded = true;
                if (triangle.Kind == ShowcaseOccluderKind.Decorative &&
                    triangle.Material != null)
                {
                    destination?.Add(triangle.Material);
                }
                else
                {
                    hasNonRemovableOccluder = true;
                }
            }

            return true;
        }

        public bool TryIsTerrainOccluded(
            Vector3 worldOrigin,
            Vector3 worldTarget,
            float worldTargetClearance,
            out bool occluded)
        {
            bool available = TryGetOcclusion(
                worldOrigin,
                worldTarget,
                worldTargetClearance,
                out ShowcaseOcclusionHit hit);
            occluded = hit.IsOccluded;
            return available;
        }

        public bool TryGetOcclusion(
            Vector3 worldOrigin,
            Vector3 worldTarget,
            float worldTargetClearance,
            out ShowcaseOcclusionHit hit)
        {
            hit = default;
            if (segmentRoot == null || occlusionTriangles.Count == 0)
                return false;

            Vector3 origin = segmentRoot.InverseTransformPoint(worldOrigin);
            Vector3 target = segmentRoot.InverseTransformPoint(worldTarget);
            Vector3 ray = target - origin;
            float distance = ray.magnitude;
            if (distance <= 0.0001f)
                return true;

            float worldScale = Mathf.Max(
                Mathf.Abs(segmentRoot.lossyScale.x),
                Mathf.Abs(segmentRoot.lossyScale.y),
                Mathf.Abs(segmentRoot.lossyScale.z));
            float targetClearance = worldScale > 0.0001f
                ? Mathf.Max(0f, worldTargetClearance) / worldScale
                : 0f;
            float maximumHitDistance = distance - targetClearance;
            if (maximumHitDistance <= 0.0001f)
                return true;

            Vector3 direction = ray / distance;
            for (int i = 0; i < occlusionTriangles.Count; i++)
            {
                if (ignoredOcclusionMaterials.Contains(
                        occlusionTriangles[i].Material))
                {
                    continue;
                }

                if (occlusionTriangles[i].Intersects(
                        origin,
                        direction,
                        maximumHitDistance))
                {
                    hit = new ShowcaseOcclusionHit(
                        occlusionTriangles[i].Material,
                        occlusionTriangles[i].Kind);
                    break;
                }
            }

            return true;
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
            float[] nearestSurfaceY,
            float[] nearestPrimarySurfaceY)
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
            bool trackContextOnly = surfaceMode ==
                EventTrackSegmentSurfaceMode.TrackContextOnly;
            Vector3 rotatedSourceCenter =
                sourceToLocalRotation * sourceCenter;

            Vector3[] positions = new Vector3[sourceVertices.Length];
            Vector3[] normals = hasNormals ? new Vector3[sourceVertices.Length] : null;
            bool[] resolvedVertices = trackContextOnly
                ? new bool[sourceVertices.Length]
                : null;
            if (!trackContextOnly)
            {
                for (int i = 0; i < sourceVertices.Length; i++)
                {
                    Vector3 world = sourceFilter.transform
                        .TransformPoint(sourceVertices[i]);
                    Vector3 sourceLocal = sourceRoot
                        .InverseTransformPoint(world);
                    positions[i] = sourceToLocalRotation *
                        (sourceLocal - sourceCenter);
                    if (hasNormals)
                    {
                        normals[i] = normalMatrix
                            .MultiplyVector(sourceNormals[i])
                            .normalized;
                    }
                }
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

                Material sourceMaterial =
                    sourceMaterials.Length > 0
                        ? sourceMaterials[
                            Mathf.Min(
                                submesh,
                                sourceMaterials.Length - 1)]
                        : null;
                if (surfaceMode ==
                        EventTrackSegmentSurfaceMode.TrackContextOnly &&
                    !IsTrackContextSurface(sourceMaterial))
                {
                    continue;
                }
                if (trackContextOnly &&
                    !SubmeshBoundsIntersect(
                        source.GetSubMesh(submesh).bounds,
                        sourceToEvent,
                        rotatedSourceCenter,
                        clipBounds))
                {
                    continue;
                }

                bool isRoadSurface =
                    IsRoadSurfaceSubmesh(
                        sourceRenderer,
                        submesh);
                bool isDrivableSurface =
                    IsDrivableSurfaceSubmesh(
                        sourceRenderer,
                        submesh);
                ShowcaseOccluderKind occluderKind =
                    IsRemovableForegroundOccluder(
                        sourceFilter,
                        sourceMaterial,
                        isRoadSurface,
                        isDrivableSurface)
                        ? ShowcaseOccluderKind.Decorative
                        : ShowcaseOccluderKind.Unknown;
                bool isPrimaryTrackContext = trackContextOnly &&
                    IsPrimaryTrackContextSurface(sourceMaterial);
                int[] indices = source.GetIndices(submesh);
                for (int index = 0; index + 2 < indices.Length; index += 3)
                {
                    int a = indices[index];
                    int b = indices[index + 1];
                    int c = indices[index + 2];
                    Vector3 pointA = ResolveVertex(a);
                    Vector3 pointB = ResolveVertex(b);
                    Vector3 pointC = ResolveVertex(c);
                    if (!TriangleIntersects(
                            pointA,
                            pointB,
                            pointC,
                            clipBounds,
                            maximumTriangleSpan,
                            localPath,
                            padding))
                        continue;

                    RecordNearestSurfaceHeights(
                        pointA,
                        pointB,
                        pointC,
                        localPath,
                        nearestSurfaceY,
                        isPrimaryTrackContext
                            ? nearestPrimarySurfaceY
                            : null);
                    if (surfaceMode == EventTrackSegmentSurfaceMode.Full)
                    {
                        occlusionTriangles.Add(
                            new OcclusionTriangle(
                                pointA,
                                pointB,
                                pointC,
                                sourceMaterial,
                                occluderKind));
                        if (isRoadSurface)
                        {
                            RecordRoadTriangle(
                                pointA,
                                pointB,
                                pointC,
                                roadTriangles);
                        }
                        if (isDrivableSurface)
                        {
                            RecordRoadTriangle(
                                pointA,
                                pointB,
                                pointC,
                                drivableTriangles);
                        }
                    }

                    triangles.Add(CopyVertex(a));
                    triangles.Add(CopyVertex(reverseWinding ? c : b));
                    triangles.Add(CopyVertex(reverseWinding ? b : c));
                    keptTriangles++;
                    if (isPrimaryTrackContext)
                    {
                        trackContextPrimaryTriangles++;
                    }
                }

                if (triangles.Count == 0)
                    continue;

                submeshes.Add(triangles);
                materials.Add(sourceMaterial);
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
            renderer.shadowCastingMode = trackContextOnly
                ? ShadowCastingMode.Off
                : sourceRenderer.shadowCastingMode;
            renderer.receiveShadows = !trackContextOnly &&
                sourceRenderer.receiveShadows;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            if (trackContextOnly)
            {
                renderer.motionVectorGenerationMode =
                    MotionVectorGenerationMode.ForceNoMotion;
            }

            return keptTriangles;

            Vector3 ResolveVertex(int sourceIndex)
            {
                if (!trackContextOnly)
                    return positions[sourceIndex];

                if (!resolvedVertices[sourceIndex])
                {
                    positions[sourceIndex] =
                        sourceToEvent.MultiplyPoint3x4(
                            sourceVertices[sourceIndex]) -
                        rotatedSourceCenter;
                    if (hasNormals)
                    {
                        normals[sourceIndex] = normalMatrix
                            .MultiplyVector(sourceNormals[sourceIndex])
                            .normalized;
                    }
                    resolvedVertices[sourceIndex] = true;
                }

                return positions[sourceIndex];
            }

            int CopyVertex(int sourceIndex)
            {
                if (remap.TryGetValue(sourceIndex, out int existing))
                    return existing;

                int copied = vertices.Count;
                remap[sourceIndex] = copied;
                vertices.Add(ResolveVertex(sourceIndex));
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

        private static bool IsRemovableForegroundOccluder(
            MeshFilter filter,
            Material material,
            bool isRoadSurface,
            bool isDrivableSurface)
        {
            if (isRoadSurface || isDrivableSurface)
                return false;

            string name =
                $"{filter?.name} {material?.name}"
                    .ToLowerInvariant();
            if (name.Contains("grass") ||
                name.Contains("ground") ||
                name.Contains("terrain") ||
                name.Contains("grvl") ||
                name.Contains("gravel") ||
                name.Contains("runoff") ||
                name.Contains("sand") ||
                name.Contains("dirt") ||
                name.Contains("earth") ||
                name.Contains("water"))
            {
                return false;
            }

            if (material == null)
            {
                return name.Contains("grandstand") ||
                    name.Contains("grand stand") ||
                    name.Contains("tribune") ||
                    name.Contains("bleacher") ||
                    name.Contains("spectator") ||
                    name.Contains("audience") ||
                    name.Contains("scaffold");
            }

            return true;
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

        private static bool IsTrackContextSurface(Material material)
        {
            if (material == null)
                return false;

            string name = material.name.ToLowerInvariant();
            if (name.Contains("pit") ||
                name.Contains("wall") ||
                name.Contains("guard") ||
                name.Contains("fence") ||
                name.Contains("tree") ||
                name.Contains("building") ||
                name.Contains("grandstand") ||
                name.Contains("gravel") ||
                name.Contains("grvl") ||
                name.Contains("terrain") ||
                name.Contains("ground"))
            {
                return false;
            }

            return name.Contains("road") ||
                name.Contains("asphalt") ||
                name.Contains("tarmac") ||
                name.Contains("curb") ||
                name.Contains("kerb") ||
                name.Contains("rumble") ||
                name.Contains("rmbl") ||
                name.Contains("rdcp") ||
                name.Contains("runoff") ||
                name.Contains("road_rk_green") ||
                name.Contains("skid") ||
                name.Contains("groove") ||
                name == "grid" ||
                name.StartsWith("line");
        }

        private static bool IsPrimaryTrackContextSurface(
            Material material)
        {
            if (material == null)
                return false;

            string name = material.name.ToLowerInvariant();
            if (name.Contains("green") ||
                name.Contains("runoff") ||
                name.Contains("curb") ||
                name.Contains("kerb") ||
                name.Contains("rumble") ||
                name.Contains("rmbl") ||
                name.Contains("rdcp") ||
                name.Contains("skid") ||
                name.Contains("groove") ||
                name.Contains("line") ||
                name == "grid")
            {
                return false;
            }

            return name.Contains("road") ||
                name.Contains("asphalt") ||
                name.Contains("tarmac");
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

        private readonly struct OcclusionTriangle
        {
            private const float IntersectionTolerance = 0.00001f;

            public OcclusionTriangle(
                Vector3 a,
                Vector3 b,
                Vector3 c,
                Material material,
                ShowcaseOccluderKind kind)
            {
                A = a;
                EdgeAB = b - a;
                EdgeAC = c - a;
                Material = material;
                Kind = kind;
            }

            public Material Material { get; }
            public ShowcaseOccluderKind Kind { get; }
            private Vector3 A { get; }
            private Vector3 EdgeAB { get; }
            private Vector3 EdgeAC { get; }

            public bool Intersects(
                Vector3 origin,
                Vector3 direction,
                float maximumDistance)
            {
                Vector3 perpendicular =
                    Vector3.Cross(direction, EdgeAC);
                float determinant =
                    Vector3.Dot(EdgeAB, perpendicular);
                if (Mathf.Abs(determinant) <= IntersectionTolerance)
                    return false;

                float inverse = 1f / determinant;
                Vector3 fromA = origin - A;
                float u =
                    Vector3.Dot(fromA, perpendicular) * inverse;
                if (u < 0f || u > 1f)
                    return false;

                Vector3 cross = Vector3.Cross(fromA, EdgeAB);
                float v =
                    Vector3.Dot(direction, cross) * inverse;
                if (v < 0f || u + v > 1f)
                    return false;

                float distance =
                    Vector3.Dot(EdgeAC, cross) * inverse;
                return distance > IntersectionTolerance &&
                    distance < maximumDistance;
            }
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

        private static Vector3 WarpPoint(
            Vector3 point,
            IReadOnlyList<Vector3> sourcePath,
            IReadOnlyList<Vector3> targetPath,
            float maximumLateralDistance,
            float maximumLongitudinalDistance)
        {
            int closestSegment = 0;
            float closestInterpolation = 0f;
            float closestDistance = float.PositiveInfinity;
            for (int index = 0;
                 index < sourcePath.Count - 1;
                 index++)
            {
                Vector3 start = sourcePath[index];
                Vector3 end = sourcePath[index + 1];
                Vector3 segment = end - start;
                segment.y = 0f;
                float lengthSquared = segment.sqrMagnitude;
                if (lengthSquared <= 0.00000001f)
                    continue;

                Vector3 relative = point - start;
                relative.y = 0f;
                float interpolation = Mathf.Clamp01(
                    Vector3.Dot(relative, segment) /
                    lengthSquared);
                Vector3 closest = Vector3.Lerp(
                    start,
                    end,
                    interpolation);
                Vector3 delta = point - closest;
                delta.y = 0f;
                float distance = delta.sqrMagnitude;
                if (distance >= closestDistance)
                    continue;

                closestDistance = distance;
                closestSegment = index;
                closestInterpolation = interpolation;
            }

            Vector3 sourceStart = sourcePath[closestSegment];
            Vector3 sourceEnd = sourcePath[closestSegment + 1];
            Vector3 sourceTangent = sourceEnd - sourceStart;
            sourceTangent.y = 0f;
            sourceTangent = sourceTangent.sqrMagnitude > 0.00000001f
                ? sourceTangent.normalized
                : Vector3.forward;
            Vector3 sourceRight = Vector3.Cross(
                Vector3.up,
                sourceTangent).normalized;
            Vector3 sourceCenter = Vector3.Lerp(
                sourceStart,
                sourceEnd,
                closestInterpolation);
            Vector3 sourceOffset = point - sourceCenter;
            float lateral = Mathf.Clamp(
                Vector3.Dot(sourceOffset, sourceRight),
                -maximumLateralDistance,
                maximumLateralDistance);
            float longitudinal = Mathf.Clamp(
                Vector3.Dot(sourceOffset, sourceTangent),
                -maximumLongitudinalDistance,
                maximumLongitudinalDistance);
            float vertical = sourceOffset.y;

            Vector3 targetStart = targetPath[closestSegment];
            Vector3 targetEnd = targetPath[closestSegment + 1];
            Vector3 targetTangent = targetEnd - targetStart;
            targetTangent.y = 0f;
            targetTangent = targetTangent.sqrMagnitude > 0.00000001f
                ? targetTangent.normalized
                : sourceTangent;
            Vector3 targetRight = Vector3.Cross(
                Vector3.up,
                targetTangent).normalized;
            Vector3 targetCenter = Vector3.Lerp(
                targetStart,
                targetEnd,
                closestInterpolation);
            return targetCenter +
                targetTangent * longitudinal +
                targetRight * lateral +
                Vector3.up * vertical;
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
            float[] nearestSurfaceY,
            float[] nearestPrimarySurfaceY = null)
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
                if (nearestPrimarySurfaceY != null &&
                    (float.IsNegativeInfinity(
                         nearestPrimarySurfaceY[i]) ||
                     Mathf.Abs(sample.y - surfaceY) <
                     Mathf.Abs(
                         sample.y - nearestPrimarySurfaceY[i])))
                {
                    nearestPrimarySurfaceY[i] = surfaceY;
                }
            }
        }

        private static int CountSurfaceCoverage(
            IReadOnlyList<float> nearestSurfaceY)
        {
            if (nearestSurfaceY == null)
                return 0;

            int covered = 0;
            for (int i = 0; i < nearestSurfaceY.Count; i++)
            {
                if (!float.IsNegativeInfinity(nearestSurfaceY[i]))
                    covered++;
            }
            return covered;
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

        private static bool SubmeshBoundsIntersect(
            Bounds sourceBounds,
            Matrix4x4 sourceToEvent,
            Vector3 rotatedSourceCenter,
            Bounds clipBounds)
        {
            Vector3 center = sourceBounds.center;
            Vector3 size = sourceBounds.size;
            if (!float.IsFinite(center.x) ||
                !float.IsFinite(center.y) ||
                !float.IsFinite(center.z) ||
                !float.IsFinite(size.x) ||
                !float.IsFinite(size.y) ||
                !float.IsFinite(size.z) ||
                size.sqrMagnitude <= 0.00000001f)
            {
                return true;
            }

            Vector3 minimum = sourceBounds.min;
            Vector3 maximum = sourceBounds.max;
            float minimumX = float.PositiveInfinity;
            float maximumX = float.NegativeInfinity;
            float minimumZ = float.PositiveInfinity;
            float maximumZ = float.NegativeInfinity;
            for (int x = 0; x < 2; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int z = 0; z < 2; z++)
                    {
                        Vector3 point = sourceToEvent.MultiplyPoint3x4(
                            new Vector3(
                                x == 0 ? minimum.x : maximum.x,
                                y == 0 ? minimum.y : maximum.y,
                                z == 0 ? minimum.z : maximum.z)) -
                            rotatedSourceCenter;
                        minimumX = Mathf.Min(minimumX, point.x);
                        maximumX = Mathf.Max(maximumX, point.x);
                        minimumZ = Mathf.Min(minimumZ, point.z);
                        maximumZ = Mathf.Max(maximumZ, point.z);
                    }
                }
            }

            return maximumX >= clipBounds.min.x &&
                minimumX <= clipBounds.max.x &&
                maximumZ >= clipBounds.min.z &&
                minimumZ <= clipBounds.max.z;
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
