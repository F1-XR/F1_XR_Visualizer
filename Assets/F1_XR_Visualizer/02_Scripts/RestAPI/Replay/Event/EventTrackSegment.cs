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
        private const string AccidentRoadShaderName =
            "F1XR/Accident Suzuka Road Clip";
        private const string SuzukaRoadRendererName = "suzuka_2001";
        private const string SuzukaRoadMaterialName = "ROAD01";
        private const int SuzukaRoadExpectedSubmesh = 242;
        private const int SuzukaClipBoxCount = 5;

        private readonly List<Mesh> meshes = new();
        private readonly List<Material> runtimeMaterials = new();
        private readonly Dictionary<Material, Material> convertedMaterials =
            new();
        private readonly List<string> sourceRendererNames = new();
        private readonly List<RoadTriangle> roadTriangles = new();
        private readonly List<RoadTriangle> drivableTriangles = new();
        private readonly List<OcclusionTriangle> occlusionTriangles = new();
        private readonly HashSet<Material> ignoredOcclusionMaterials = new();
        private Transform segmentRoot;
        private EventTrackSegmentSurfaceMode surfaceMode;
        private int trackContextPrimaryTriangles;
        private int trackContextConnectedComponents;
        private int trackContextSelectedSubmeshes;
        private int unsupportedSourceMaterialCount;
        private float maximumTrackTriangleEdge;
        private bool usesIntactSuzukaRoad;
        private int sourceRoadSubmesh = -1;
        private int sourceRoadIndexCount;
        private int sourceRoadVertexCount;
        private readonly List<Vector3> clipBoxCenters = new();
        private readonly List<Vector3> clipBoxSizes = new();
        private readonly List<float> clipBoxYawDegrees = new();

        public int ConvertedMaterialCount => convertedMaterials.Count;
        public int UnsupportedSourceMaterialCount =>
            unsupportedSourceMaterialCount;
        public IReadOnlyList<string> SourceRendererNames =>
            sourceRendererNames;
        public float MaximumTrackTriangleEdge => maximumTrackTriangleEdge;
        public int TrackContextConnectedComponents =>
            trackContextConnectedComponents;
        public int TrackContextSelectedSubmeshes =>
            trackContextSelectedSubmeshes;
        public int SourceRoadSubmesh => sourceRoadSubmesh;
        public int SourceRoadIndexCount => sourceRoadIndexCount;
        public int SourceRoadVertexCount => sourceRoadVertexCount;
        public IReadOnlyList<Vector3> ClipBoxCenters => clipBoxCenters;
        public IReadOnlyList<Vector3> ClipBoxSizes => clipBoxSizes;
        public IReadOnlyList<float> ClipBoxYawDegrees =>
            clipBoxYawDegrees;

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
            return Build(
                parent,
                sourceRoot,
                sourcePath,
                sourceCenter,
                sourceToLocalRotation,
                padding,
                padding,
                requestedSurfaceMode,
                out stageBounds);
        }

        public bool Build(
            Transform parent,
            Transform sourceRoot,
            IReadOnlyList<Vector3> sourcePath,
            Vector3 sourceCenter,
            Quaternion sourceToLocalRotation,
            float lateralPadding,
            float longitudinalPadding,
            EventTrackSegmentSurfaceMode requestedSurfaceMode,
            out Bounds stageBounds)
        {
            surfaceMode = requestedSurfaceMode;
            trackContextPrimaryTriangles = 0;
            trackContextConnectedComponents = 0;
            trackContextSelectedSubmeshes = 0;
            unsupportedSourceMaterialCount = 0;
            maximumTrackTriangleEdge = 0f;
            usesIntactSuzukaRoad = false;
            sourceRoadSubmesh = -1;
            sourceRoadIndexCount = 0;
            sourceRoadVertexCount = 0;
            sourceRendererNames.Clear();
            clipBoxCenters.Clear();
            clipBoxSizes.Clear();
            clipBoxYawDegrees.Clear();
            stageBounds = BuildStageBounds(
                sourcePath,
                sourceCenter,
                sourceToLocalRotation,
                lateralPadding,
                longitudinalPadding);

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
            if (surfaceMode ==
                EventTrackSegmentSurfaceMode.TrackContextOnly)
            {
                if (BuildIntactSuzukaRoad(
                        visualRoot,
                        sourceRoot,
                        sourcePath,
                        sourceCenter,
                        sourceToLocalRotation,
                        lateralPadding,
                        longitudinalPadding,
                        out Bounds intactBounds))
                {
                    stageBounds = intactBounds;
                    return true;
                }

                Object.Destroy(segmentObject);
                Clear();
                return false;
            }

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
                    lateralPadding,
                    longitudinalPadding,
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
                if (surfaceMode ==
                    EventTrackSegmentSurfaceMode.TrackContextOnly)
                {
                    Debug.Log(
                        $"[EventTrackSegment] actualSourceRenderers=" +
                        $"{string.Join(",", sourceRendererNames)}, " +
                        $"selectedSubmeshes=" +
                        $"{TrackContextSelectedSubmeshes}, " +
                        $"connectedComponents=" +
                        $"{TrackContextConnectedComponents}, " +
                        $"convertedMaterials={ConvertedMaterialCount}, " +
                        $"unsupportedSourceMaterials=" +
                        $"{UnsupportedSourceMaterialCount}.");
                }
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

        public bool ApplyUniformScale(
            float scale,
            Vector3 pivot,
            out Bounds scaledBounds)
        {
            scaledBounds = default;
            if (segmentRoot == null || meshes.Count == 0)
                return false;

            float safeScale = Mathf.Max(0.0001f, scale);
            if (usesIntactSuzukaRoad)
            {
                segmentRoot.localPosition = pivot +
                    (segmentRoot.localPosition - pivot) * safeScale;
                segmentRoot.localScale *= safeScale;
                return TryCalculateSegmentBounds(out scaledBounds);
            }

            Vector3 rootPosition = segmentRoot.localPosition;
            Vector3 meshPivot = pivot - rootPosition;
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
                    vertices[vertexIndex] = meshPivot +
                        (vertices[vertexIndex] - meshPivot) * safeScale;
                }

                mesh.vertices = vertices;
                mesh.RecalculateNormals();
                if (mesh.uv != null && mesh.uv.Length == vertices.Length)
                    mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                if (!hasBounds)
                {
                    scaledBounds = mesh.bounds;
                    hasBounds = true;
                }
                else
                {
                    scaledBounds.Encapsulate(mesh.bounds);
                }
            }

            segmentRoot.localPosition = pivot +
                (rootPosition - pivot) * safeScale;
            return hasBounds;
        }

        public void Clear()
        {
            foreach (Mesh mesh in meshes)
            {
                if (mesh != null)
                    Object.Destroy(mesh);
            }
            foreach (Material material in runtimeMaterials)
            {
                if (material != null)
                    Object.Destroy(material);
            }

            meshes.Clear();
            runtimeMaterials.Clear();
            convertedMaterials.Clear();
            sourceRendererNames.Clear();
            roadTriangles.Clear();
            drivableTriangles.Clear();
            occlusionTriangles.Clear();
            ignoredOcclusionMaterials.Clear();
            segmentRoot = null;
            trackContextPrimaryTriangles = 0;
            trackContextConnectedComponents = 0;
            trackContextSelectedSubmeshes = 0;
            unsupportedSourceMaterialCount = 0;
            maximumTrackTriangleEdge = 0f;
            usesIntactSuzukaRoad = false;
            sourceRoadSubmesh = -1;
            sourceRoadIndexCount = 0;
            sourceRoadVertexCount = 0;
            clipBoxCenters.Clear();
            clipBoxSizes.Clear();
            clipBoxYawDegrees.Clear();
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

        private bool BuildIntactSuzukaRoad(
            Transform visualRoot,
            Transform sourceRoot,
            IReadOnlyList<Vector3> sourcePath,
            Vector3 sourceCenter,
            Quaternion sourceToLocalRotation,
            float lateralPadding,
            float longitudinalPadding,
            out Bounds roadBounds)
        {
            roadBounds = default;
            MeshFilter roadFilter = null;
            MeshRenderer roadRenderer = null;
            Material roadSourceMaterial = null;
            int roadSubmesh = -1;
            MeshFilter[] filters = visualRoot
                .GetComponentsInChildren<MeshFilter>(true);
            for (int filterIndex = 0;
                 filterIndex < filters.Length;
                 filterIndex++)
            {
                MeshFilter candidate = filters[filterIndex];
                if (candidate == null ||
                    candidate.sharedMesh == null ||
                    !candidate.sharedMesh.isReadable ||
                    !string.Equals(
                        candidate.name,
                        SuzukaRoadRendererName,
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                MeshRenderer candidateRenderer =
                    candidate.GetComponent<MeshRenderer>();
                if (candidateRenderer == null)
                    continue;

                Material[] materials = candidateRenderer.sharedMaterials;
                int submeshCount = candidate.sharedMesh.subMeshCount;
                for (int submesh = 0;
                     submesh < submeshCount;
                     submesh++)
                {
                    Material material = materials.Length > 0
                        ? materials[Mathf.Min(
                            submesh,
                            materials.Length - 1)]
                        : null;
                    if (!IsExactSuzukaRoadMaterial(material))
                        continue;

                    roadFilter = candidate;
                    roadRenderer = candidateRenderer;
                    roadSourceMaterial = material;
                    roadSubmesh = submesh;
                    break;
                }

                if (roadSubmesh >= 0)
                    break;
            }

            if (roadFilter == null ||
                roadRenderer == null ||
                roadSubmesh < 0)
            {
                Debug.LogError(
                    "[EventTrackSegment] Intact Suzuka ROAD01 was not " +
                    "found on renderer suzuka_2001.");
                return false;
            }

            Shader shader = Shader.Find(AccidentRoadShaderName);
            if (shader == null || !shader.isSupported)
            {
                Debug.LogError(
                    "[EventTrackSegment] Quest-safe Suzuka road clip " +
                    $"shader is unavailable: {AccidentRoadShaderName}.");
                return false;
            }

            Mesh source = roadFilter.sharedMesh;
            int[] completeRoadIndices = source.GetIndices(roadSubmesh);
            if (completeRoadIndices.Length < 3)
                return false;

            Mesh roadMesh = new()
            {
                name = $"EventTrack_{source.name}_ROAD01_Intact",
                indexFormat = source.indexFormat
            };
            CopyCompleteVertexAttributes(source, roadMesh);
            roadMesh.subMeshCount = 1;
            roadMesh.SetIndices(
                completeRoadIndices,
                MeshTopology.Triangles,
                0,
                false);
            roadMesh.bounds = source.GetSubMesh(roadSubmesh).bounds;
            meshes.Add(roadMesh);

            GameObject roadObject = new(
                "ActualSuzukaRoad01",
                typeof(MeshFilter),
                typeof(MeshRenderer));
            roadObject.transform.SetParent(segmentRoot, false);
            Matrix4x4 sourceToEvent =
                Matrix4x4.Translate(
                    -(sourceToLocalRotation * sourceCenter)) *
                Matrix4x4.Rotate(sourceToLocalRotation) *
                sourceRoot.worldToLocalMatrix *
                roadFilter.transform.localToWorldMatrix;
            ApplyLocalMatrix(roadObject.transform, sourceToEvent);
            roadObject.GetComponent<MeshFilter>().sharedMesh = roadMesh;

            Material roadMaterial = new(shader)
            {
                name = "Runtime_AccidentSuzuka_ROAD01_Clip"
            };
            runtimeMaterials.Add(roadMaterial);
            if (roadMaterial.HasProperty("_BaseColor"))
            {
                roadMaterial.SetColor(
                    "_BaseColor",
                    new Color(0.29f, 0.30f, 0.32f, 1f));
            }
            Texture baseTexture = ResolveTrackContextBaseTexture(
                roadSourceMaterial);
            if (baseTexture != null &&
                roadMaterial.HasProperty("_BaseMap"))
            {
                roadMaterial.SetTexture("_BaseMap", baseTexture);
                CopyTextureTransform(
                    roadSourceMaterial,
                    roadMaterial);
            }

            Matrix4x4[] clipMatrices = BuildSuzukaClipMatrices(
                roadFilter,
                sourceRoot,
                sourcePath,
                sourceCenter,
                sourceToLocalRotation,
                lateralPadding,
                longitudinalPadding,
                out Bounds visibleObjectBounds);
            roadMesh.bounds = visibleObjectBounds;
            roadMaterial.SetInt("_ClipBoxCount", clipMatrices.Length);
            roadMaterial.SetMatrixArray(
                "_ClipBoxInverse",
                clipMatrices);

            MeshRenderer runtimeRenderer =
                roadObject.GetComponent<MeshRenderer>();
            runtimeRenderer.sharedMaterial = roadMaterial;
            runtimeRenderer.shadowCastingMode = ShadowCastingMode.Off;
            runtimeRenderer.receiveShadows = false;
            runtimeRenderer.lightProbeUsage = LightProbeUsage.Off;
            runtimeRenderer.reflectionProbeUsage =
                ReflectionProbeUsage.Off;
            runtimeRenderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;

            usesIntactSuzukaRoad = true;
            sourceRoadSubmesh = roadSubmesh;
            sourceRoadIndexCount = completeRoadIndices.Length;
            sourceRoadVertexCount = source.vertexCount;
            trackContextPrimaryTriangles =
                completeRoadIndices.Length / 3;
            trackContextSelectedSubmeshes = 1;
            sourceRendererNames.Add(roadFilter.name);
            UpdateMaximumTriangleEdge(
                source.vertices,
                completeRoadIndices);
            convertedMaterials[roadSourceMaterial] = roadMaterial;

            if (!TryCalculateSegmentBounds(out roadBounds))
                return false;

            Debug.Log(
                "[EventTrackSegment] Intact ROAD01 GPU clip active: " +
                $"renderer={roadFilter.name}, " +
                $"submesh={roadSubmesh}, " +
                $"expectedSubmesh={SuzukaRoadExpectedSubmesh}, " +
                $"vertices={sourceRoadVertexCount}, " +
                $"indices={sourceRoadIndexCount}, " +
                $"triangles={sourceRoadIndexCount / 3}, " +
                $"clipBoxes={clipMatrices.Length}, " +
                "cpuTriangleCropping=False, grooveEnabled=False, " +
                $"shader={shader.name}.");
            if (roadSubmesh != SuzukaRoadExpectedSubmesh)
            {
                Debug.LogWarning(
                    "[EventTrackSegment] ROAD01 material resolved to " +
                    $"submesh {roadSubmesh}; GLB source expectation is " +
                    $"{SuzukaRoadExpectedSubmesh}.");
            }
            return true;
        }

        private static void CopyCompleteVertexAttributes(
            Mesh source,
            Mesh destination)
        {
            int vertexCount = source.vertexCount;
            destination.vertices = source.vertices;
            Vector3[] normals = source.normals;
            if (normals.Length == vertexCount)
                destination.normals = normals;
            Vector4[] tangents = source.tangents;
            if (tangents.Length == vertexCount)
                destination.tangents = tangents;
            Color32[] colors = source.colors32;
            if (colors.Length == vertexCount)
                destination.colors32 = colors;

            for (int channel = 0; channel < 8; channel++)
            {
                List<Vector4> uv = new(vertexCount);
                source.GetUVs(channel, uv);
                if (uv.Count == vertexCount)
                    destination.SetUVs(channel, uv);
            }
        }

        private Matrix4x4[] BuildSuzukaClipMatrices(
            MeshFilter roadFilter,
            Transform sourceRoot,
            IReadOnlyList<Vector3> sourcePath,
            Vector3 sourceCenter,
            Quaternion sourceToLocalRotation,
            float lateralPadding,
            float longitudinalPadding,
            out Bounds visibleObjectBounds)
        {
            visibleObjectBounds = default;
            bool hasVisibleBounds = false;
            float[] progress = BuildPathProgress(sourcePath);
            float pathLength = progress.Length > 0
                ? progress[progress.Length - 1]
                : 0f;
            float segmentLength = Mathf.Max(
                0.0001f,
                pathLength / SuzukaClipBoxCount);
            float overlap = Mathf.Max(
                longitudinalPadding,
                segmentLength * 0.18f);
            float lateralSize = Mathf.Max(
                lateralPadding * 3f,
                segmentLength * 0.8f);
            float verticalSize = Mathf.Max(
                lateralSize * 0.75f,
                0.012f);
            Matrix4x4 sourceToRoadObject =
                roadFilter.transform.worldToLocalMatrix *
                sourceRoot.localToWorldMatrix;
            Matrix4x4[] matrices =
                new Matrix4x4[SuzukaClipBoxCount];
            for (int box = 0;
                 box < SuzukaClipBoxCount;
                 box++)
            {
                float start = segmentLength * box - overlap;
                float end = segmentLength * (box + 1) + overlap;
                if (box == 0)
                    start -= longitudinalPadding;
                if (box == SuzukaClipBoxCount - 1)
                    end += longitudinalPadding;
                float middle = (start + end) * 0.5f;
                Vector3 center = EvaluatePathAtDistance(
                    sourcePath,
                    progress,
                    middle);
                Vector3 before = EvaluatePathAtDistance(
                    sourcePath,
                    progress,
                    middle - segmentLength * 0.25f);
                Vector3 after = EvaluatePathAtDistance(
                    sourcePath,
                    progress,
                    middle + segmentLength * 0.25f);
                Vector3 tangent = after - before;
                tangent.y = 0f;
                if (tangent.sqrMagnitude <= 0.00000001f)
                    tangent = Vector3.forward;
                else
                    tangent.Normalize();
                Quaternion rotation = Quaternion.LookRotation(
                    tangent,
                    Vector3.up);
                Vector3 size = new(
                    lateralSize,
                    verticalSize,
                    Mathf.Max(0.0001f, end - start));
                Matrix4x4 boxToRoadObject = sourceToRoadObject *
                    Matrix4x4.TRS(center, rotation, size);
                matrices[box] = boxToRoadObject.inverse;
                EncapsulateUnitBox(
                    boxToRoadObject,
                    ref visibleObjectBounds,
                    ref hasVisibleBounds);

                Vector3 eventCenter = sourceToLocalRotation *
                    (center - sourceCenter);
                Vector3 eventTangent = sourceToLocalRotation * tangent;
                eventTangent.y = 0f;
                clipBoxCenters.Add(eventCenter);
                clipBoxSizes.Add(size);
                clipBoxYawDegrees.Add(
                    Vector3.SignedAngle(
                        Vector3.forward,
                        eventTangent,
                        Vector3.up));
            }
            return matrices;
        }

        private static void EncapsulateUnitBox(
            Matrix4x4 boxToDestination,
            ref Bounds bounds,
            ref bool hasBounds)
        {
            for (int x = 0; x < 2; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int z = 0; z < 2; z++)
                    {
                        Vector3 point = boxToDestination.MultiplyPoint3x4(
                            new Vector3(
                                x == 0 ? -0.5f : 0.5f,
                                y == 0 ? -0.5f : 0.5f,
                                z == 0 ? -0.5f : 0.5f));
                        if (!hasBounds)
                        {
                            bounds = new Bounds(point, Vector3.zero);
                            hasBounds = true;
                        }
                        else
                        {
                            bounds.Encapsulate(point);
                        }
                    }
                }
            }
        }

        private static Vector3 EvaluatePathAtDistance(
            IReadOnlyList<Vector3> path,
            IReadOnlyList<float> progress,
            float distance)
        {
            if (path == null || path.Count == 0)
                return Vector3.zero;
            if (path.Count == 1)
                return path[0];

            float total = progress[progress.Count - 1];
            if (distance <= 0f)
            {
                Vector3 tangent = path[1] - path[0];
                return path[0] +
                    tangent.normalized * distance;
            }
            if (distance >= total)
            {
                Vector3 tangent =
                    path[path.Count - 1] - path[path.Count - 2];
                return path[path.Count - 1] +
                    tangent.normalized * (distance - total);
            }

            for (int index = 0;
                 index + 1 < path.Count;
                 index++)
            {
                if (distance > progress[index + 1])
                    continue;
                float length = Mathf.Max(
                    0.00000001f,
                    progress[index + 1] - progress[index]);
                return Vector3.LerpUnclamped(
                    path[index],
                    path[index + 1],
                    (distance - progress[index]) / length);
            }
            return path[path.Count - 1];
        }

        private static bool IsExactSuzukaRoadMaterial(
            Material material)
        {
            if (material == null)
                return false;
            string name = material.name;
            int suffix = name.IndexOf(
                " (",
                System.StringComparison.Ordinal);
            if (suffix >= 0)
                name = name.Substring(0, suffix);
            return string.Equals(
                name,
                SuzukaRoadMaterialName,
                System.StringComparison.OrdinalIgnoreCase);
        }

        private static void CopyTextureTransform(
            Material source,
            Material destination)
        {
            if (source == null || destination == null)
                return;
            string property = source.HasProperty("_BaseMap")
                ? "_BaseMap"
                : source.HasProperty("_MainTex")
                    ? "_MainTex"
                    : null;
            if (property == null)
                return;
            destination.SetTextureScale(
                "_BaseMap",
                source.GetTextureScale(property));
            destination.SetTextureOffset(
                "_BaseMap",
                source.GetTextureOffset(property));
        }

        private static void ApplyLocalMatrix(
            Transform target,
            Matrix4x4 matrix)
        {
            Vector3 forward = matrix.GetColumn(2);
            Vector3 up = matrix.GetColumn(1);
            float scaleX = matrix.GetColumn(0).magnitude;
            float scaleY = up.magnitude;
            float scaleZ = forward.magnitude;
            if (matrix.determinant < 0f)
                scaleX = -scaleX;
            target.localPosition = matrix.GetColumn(3);
            target.localRotation = Quaternion.LookRotation(
                forward.normalized,
                up.normalized);
            target.localScale = new Vector3(
                scaleX,
                scaleY,
                scaleZ);
        }

        private bool TryCalculateSegmentBounds(
            out Bounds bounds)
        {
            bounds = default;
            if (segmentRoot == null || segmentRoot.parent == null)
                return false;
            MeshFilter[] filters = segmentRoot
                .GetComponentsInChildren<MeshFilter>(true);
            bool found = false;
            Transform destination = segmentRoot.parent;
            for (int filterIndex = 0;
                 filterIndex < filters.Length;
                 filterIndex++)
            {
                MeshFilter filter = filters[filterIndex];
                if (filter == null || filter.sharedMesh == null)
                    continue;
                Bounds meshBounds = filter.sharedMesh.bounds;
                Vector3 minimum = meshBounds.min;
                Vector3 maximum = meshBounds.max;
                for (int x = 0; x < 2; x++)
                {
                    for (int y = 0; y < 2; y++)
                    {
                        for (int z = 0; z < 2; z++)
                        {
                            Vector3 point = destination.InverseTransformPoint(
                                filter.transform.TransformPoint(
                                    new Vector3(
                                        x == 0 ? minimum.x : maximum.x,
                                        y == 0 ? minimum.y : maximum.y,
                                        z == 0 ? minimum.z : maximum.z)));
                            if (!found)
                            {
                                bounds = new Bounds(point, Vector3.zero);
                                found = true;
                            }
                            else
                            {
                                bounds.Encapsulate(point);
                            }
                        }
                    }
                }
            }
            return found;
        }

        private void UpdateMaximumTriangleEdge(
            IReadOnlyList<Vector3> vertices,
            IReadOnlyList<int> indices)
        {
            for (int index = 0;
                 index + 2 < indices.Count;
                 index += 3)
            {
                Vector3 a = vertices[indices[index]];
                Vector3 b = vertices[indices[index + 1]];
                Vector3 c = vertices[indices[index + 2]];
                maximumTrackTriangleEdge = Mathf.Max(
                    maximumTrackTriangleEdge,
                    Vector3.Distance(a, b),
                    Vector3.Distance(b, c),
                    Vector3.Distance(c, a));
            }
        }

        private int CopyMesh(
            MeshFilter sourceFilter,
            Transform parent,
            Transform sourceRoot,
            Vector3 sourceCenter,
            Quaternion sourceToLocalRotation,
            Bounds clipBounds,
            IReadOnlyList<Vector3> localPath,
            float lateralPadding,
            float longitudinalPadding,
            float[] nearestSurfaceY,
            float[] nearestPrimarySurfaceY)
        {
            Mesh source = sourceFilter.sharedMesh;
            Vector3[] sourceVertices = source.vertices;
            Vector3[] sourceNormals = source.normals;
            Vector4[] sourceTangents = source.tangents;
            Vector2[] sourceUv = source.uv;
            Vector2[] sourceUv2 = source.uv2;
            Color[] sourceColors = source.colors;
            bool hasNormals = sourceNormals.Length == sourceVertices.Length;
            bool hasTangents = sourceTangents.Length == sourceVertices.Length;
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
            Vector4[] tangents = hasTangents ? new Vector4[sourceVertices.Length] : null;
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
                    if (hasTangents)
                    {
                        Vector3 direction = sourceToEvent.MultiplyVector(
                            new Vector3(
                                sourceTangents[i].x,
                                sourceTangents[i].y,
                                sourceTangents[i].z)).normalized;
                        tangents[i] = new Vector4(
                            direction.x,
                            direction.y,
                            direction.z,
                            sourceTangents[i].w *
                            (reverseWinding ? -1f : 1f));
                    }
                }
            }

            List<Vector3> vertices = new();
            List<Vector3> copiedNormals = hasNormals ? new List<Vector3>() : null;
            List<Vector4> copiedTangents = hasTangents ? new List<Vector4>() : null;
            List<Vector2> uv = hasUv ? new List<Vector2>() : null;
            List<Vector2> uv2 = hasUv2 ? new List<Vector2>() : null;
            List<Color> colors = hasColors ? new List<Color>() : null;
            List<List<int>> submeshes = new(source.subMeshCount);
            List<Material> copiedMaterials = new(source.subMeshCount);
            Material[] sourceMaterials = sourceRenderer.sharedMaterials;
            Dictionary<int, int> remap = new();
            int keptTriangles = 0;
            float[] pathProgress = trackContextOnly
                ? BuildPathProgress(localPath)
                : null;
            Vector2 maximumTriangleSpan = trackContextOnly
                ? new Vector2(float.PositiveInfinity, float.PositiveInfinity)
                : new Vector2(
                    clipBounds.size.x * 2f,
                    clipBounds.size.z * 2f);

            for (int submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                List<int> triangles = new();
                List<int> candidateSourceIndices = trackContextOnly
                    ? new List<int>()
                    : null;
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
                Dictionary<int, int> sourceComponentRoots = trackContextOnly
                    ? BuildTriangleComponentRoots(indices)
                    : null;
                for (int index = 0; index + 2 < indices.Length; index += 3)
                {
                    int a = indices[index];
                    int b = indices[index + 1];
                    int c = indices[index + 2];
                    Vector3 pointA = ResolveVertex(a);
                    Vector3 pointB = ResolveVertex(b);
                    Vector3 pointC = ResolveVertex(c);
                    if (trackContextOnly)
                    {
                        Vector3 center =
                            (pointA + pointB + pointC) / 3f;
                        if (!TryProjectToPathWindow(
                                center,
                                localPath,
                                pathProgress,
                                longitudinalPadding,
                                lateralPadding,
                                out _,
                                out float verticalDistance) ||
                            verticalDistance > Mathf.Max(
                                0.0015f,
                                lateralPadding * 0.3f))
                        {
                            continue;
                        }
                        candidateSourceIndices.Add(a);
                        candidateSourceIndices.Add(b);
                        candidateSourceIndices.Add(c);
                        continue;
                    }

                    if (!TriangleIntersects(
                            pointA,
                            pointB,
                            pointC,
                            clipBounds,
                            maximumTriangleSpan,
                            localPath,
                            lateralPadding))
                    {
                        continue;
                    }

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

                if (trackContextOnly)
                {
                    for (int triangleIndex = 0;
                         triangleIndex + 2 < candidateSourceIndices.Count;
                         triangleIndex += 3)
                    {
                        int a = candidateSourceIndices[triangleIndex];
                        int b = candidateSourceIndices[triangleIndex + 1];
                        int c = candidateSourceIndices[triangleIndex + 2];
                        Vector3 pointA = ResolveVertex(a);
                        Vector3 pointB = ResolveVertex(b);
                        Vector3 pointC = ResolveVertex(c);
                        maximumTrackTriangleEdge = Mathf.Max(
                            maximumTrackTriangleEdge,
                            Vector3.Distance(pointA, pointB),
                            Vector3.Distance(pointB, pointC),
                            Vector3.Distance(pointC, pointA));
                        RecordNearestSurfaceHeights(
                            pointA,
                            pointB,
                            pointC,
                            localPath,
                            nearestSurfaceY,
                            isPrimaryTrackContext
                                ? nearestPrimarySurfaceY
                                : null);
                        triangles.Add(CopyVertex(a));
                        triangles.Add(CopyVertex(
                            reverseWinding ? c : b));
                        triangles.Add(CopyVertex(
                            reverseWinding ? b : c));
                        keptTriangles++;
                        if (isPrimaryTrackContext)
                            trackContextPrimaryTriangles++;
                    }
                }

                if (triangles.Count == 0)
                    continue;

                if (trackContextOnly)
                {
                    trackContextSelectedSubmeshes++;
                    trackContextConnectedComponents +=
                        CountSelectedComponentRoots(
                            candidateSourceIndices,
                            sourceComponentRoots);
                }

                submeshes.Add(triangles);
                copiedMaterials.Add(trackContextOnly
                    ? ResolveTrackContextMaterial(sourceMaterial)
                    : sourceMaterial);
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
            if (hasTangents)
                mesh.SetTangents(copiedTangents);
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
            renderer.sharedMaterials = copiedMaterials.ToArray();
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
                if (!sourceRendererNames.Contains(sourceFilter.name))
                    sourceRendererNames.Add(sourceFilter.name);
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
                    if (hasTangents)
                    {
                        Vector4 sourceTangent = sourceTangents[sourceIndex];
                        Vector3 direction = sourceToEvent.MultiplyVector(
                            new Vector3(
                                sourceTangent.x,
                                sourceTangent.y,
                                sourceTangent.z)).normalized;
                        tangents[sourceIndex] = new Vector4(
                            direction.x,
                            direction.y,
                            direction.z,
                            sourceTangent.w *
                            (reverseWinding ? -1f : 1f));
                    }
                    resolvedVertices[sourceIndex] = true;
                }

                return positions[sourceIndex];
            }

            TrackClipVertex CreateClipVertex(int sourceIndex)
            {
                ResolveVertex(sourceIndex);
                return new TrackClipVertex(
                    positions[sourceIndex],
                    hasNormals ? normals[sourceIndex] : Vector3.up,
                    hasTangents
                        ? tangents[sourceIndex]
                        : new Vector4(1f, 0f, 0f, 1f),
                    hasUv ? sourceUv[sourceIndex] : Vector2.zero,
                    hasUv2 ? sourceUv2[sourceIndex] : Vector2.zero,
                    hasColors ? sourceColors[sourceIndex] : Color.white);
            }

            int CopyClippedVertex(TrackClipVertex vertex)
            {
                int copied = vertices.Count;
                vertices.Add(vertex.Position);
                if (hasNormals)
                    copiedNormals.Add(vertex.Normal);
                if (hasTangents)
                    copiedTangents.Add(vertex.Tangent);
                if (hasUv)
                    uv.Add(vertex.Uv);
                if (hasUv2)
                    uv2.Add(vertex.Uv2);
                if (hasColors)
                    colors.Add(vertex.Color);
                return copied;
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
                if (hasTangents)
                    copiedTangents.Add(tangents[sourceIndex]);
                if (hasUv)
                    uv.Add(sourceUv[sourceIndex]);
                if (hasUv2)
                    uv2.Add(sourceUv2[sourceIndex]);
                if (hasColors)
                    colors.Add(sourceColors[sourceIndex]);
                return copied;
            }
        }

        private static float[] BuildPathProgress(
            IReadOnlyList<Vector3> path)
        {
            int count = path?.Count ?? 0;
            float[] progress = new float[count];
            for (int index = 1; index < count; index++)
            {
                progress[index] = progress[index - 1] +
                    Vector2.Distance(
                        new Vector2(
                            path[index - 1].x,
                            path[index - 1].z),
                        new Vector2(
                            path[index].x,
                            path[index].z));
            }
            return progress;
        }

        private static bool TryProjectToPathWindow(
            Vector3 point,
            IReadOnlyList<Vector3> path,
            IReadOnlyList<float> progress,
            float longitudinalPadding,
            float lateralWidth,
            out float alongTrackProgress,
            out float verticalDistance)
        {
            alongTrackProgress = 0f;
            verticalDistance = float.PositiveInfinity;
            int count = path?.Count ?? 0;
            if (count < 2 || progress == null || progress.Count != count)
                return false;

            Vector2 candidate = new(point.x, point.z);
            float bestDistanceSquared = float.PositiveInfinity;
            float bestProgress = 0f;
            float bestPathHeight = 0f;
            for (int index = 0; index + 1 < count; index++)
            {
                Vector2 start = new(path[index].x, path[index].z);
                Vector2 end = new(path[index + 1].x, path[index + 1].z);
                Vector2 segment = end - start;
                float lengthSquared = segment.sqrMagnitude;
                if (lengthSquared <= 0.00000001f)
                    continue;

                float length = Mathf.Sqrt(lengthSquared);
                float interpolation = Vector2.Dot(
                    candidate - start,
                    segment) / lengthSquared;
                float minimum = index == 0
                    ? -longitudinalPadding / length
                    : 0f;
                float maximum = index + 2 == count
                    ? 1f + longitudinalPadding / length
                    : 1f;
                interpolation = Mathf.Clamp(
                    interpolation,
                    minimum,
                    maximum);
                Vector2 nearest = start + segment * interpolation;
                float distanceSquared =
                    (candidate - nearest).sqrMagnitude;
                if (distanceSquared >= bestDistanceSquared)
                    continue;

                bestDistanceSquared = distanceSquared;
                bestProgress = progress[index] +
                    length * interpolation;
                bestPathHeight = Mathf.LerpUnclamped(
                    path[index].y,
                    path[index + 1].y,
                    interpolation);
            }

            alongTrackProgress = bestProgress;
            verticalDistance = Mathf.Abs(point.y - bestPathHeight);
            float totalLength = progress[count - 1];
            return bestDistanceSquared <= lateralWidth * lateralWidth &&
                bestProgress >= -longitudinalPadding &&
                bestProgress <= totalLength + longitudinalPadding;
        }

        private static int CountTriangleComponents(
            IReadOnlyList<int> triangleIndices)
        {
            if (triangleIndices == null || triangleIndices.Count < 3)
                return 0;

            Dictionary<int, int> parents = new();
            for (int index = 0;
                 index + 2 < triangleIndices.Count;
                 index += 3)
            {
                int a = triangleIndices[index];
                int b = triangleIndices[index + 1];
                int c = triangleIndices[index + 2];
                Add(a);
                Add(b);
                Add(c);
                Union(a, b);
                Union(b, c);
            }

            HashSet<int> components = new();
            List<int> vertices = new(parents.Keys);
            foreach (int vertex in vertices)
                components.Add(Find(vertex));
            return components.Count;

            void Add(int vertex)
            {
                if (!parents.ContainsKey(vertex))
                    parents[vertex] = vertex;
            }

            int Find(int vertex)
            {
                int parent = parents[vertex];
                if (parent == vertex)
                    return vertex;
                int root = Find(parent);
                parents[vertex] = root;
                return root;
            }

            void Union(int left, int right)
            {
                int leftRoot = Find(left);
                int rightRoot = Find(right);
                if (leftRoot != rightRoot)
                    parents[rightRoot] = leftRoot;
            }
        }

        private static Dictionary<int, int> BuildTriangleComponentRoots(
            IReadOnlyList<int> triangleIndices)
        {
            Dictionary<int, int> parents = new();
            for (int index = 0;
                 index + 2 < triangleIndices.Count;
                 index += 3)
            {
                int a = triangleIndices[index];
                int b = triangleIndices[index + 1];
                int c = triangleIndices[index + 2];
                Add(a);
                Add(b);
                Add(c);
                Union(a, b);
                Union(b, c);
            }

            Dictionary<int, int> roots = new(parents.Count);
            foreach (int vertex in new List<int>(parents.Keys))
                roots[vertex] = Find(vertex);
            return roots;

            void Add(int vertex)
            {
                if (!parents.ContainsKey(vertex))
                    parents[vertex] = vertex;
            }

            int Find(int vertex)
            {
                int parent = parents[vertex];
                if (parent == vertex)
                    return vertex;
                int root = Find(parent);
                parents[vertex] = root;
                return root;
            }

            void Union(int left, int right)
            {
                int leftRoot = Find(left);
                int rightRoot = Find(right);
                if (leftRoot != rightRoot)
                    parents[rightRoot] = leftRoot;
            }
        }

        private static int CountSelectedComponentRoots(
            IReadOnlyList<int> candidateTriangleIndices,
            IReadOnlyDictionary<int, int> componentRoots)
        {
            if (candidateTriangleIndices == null ||
                candidateTriangleIndices.Count < 3 ||
                componentRoots == null)
            {
                return 0;
            }

            HashSet<int> selectedRoots = new();
            for (int index = 0;
                 index + 2 < candidateTriangleIndices.Count;
                 index += 3)
            {
                int vertex = candidateTriangleIndices[index];
                if (!componentRoots.TryGetValue(vertex, out int root))
                    continue;
                selectedRoots.Add(root);
            }
            return selectedRoots.Count;
        }

        private readonly struct TrackClipVertex
        {
            public TrackClipVertex(
                Vector3 position,
                Vector3 normal,
                Vector4 tangent,
                Vector2 uv,
                Vector2 uv2,
                Color color)
            {
                Position = position;
                Normal = normal;
                Tangent = tangent;
                Uv = uv;
                Uv2 = uv2;
                Color = color;
            }

            public Vector3 Position { get; }
            public Vector3 Normal { get; }
            public Vector4 Tangent { get; }
            public Vector2 Uv { get; }
            public Vector2 Uv2 { get; }
            public Color Color { get; }

            public static TrackClipVertex Lerp(
                TrackClipVertex from,
                TrackClipVertex to,
                float interpolation)
            {
                float t = Mathf.Clamp01(interpolation);
                Vector3 normal = Vector3.LerpUnclamped(
                    from.Normal,
                    to.Normal,
                    t).normalized;
                Vector4 tangent = Vector4.LerpUnclamped(
                    from.Tangent,
                    to.Tangent,
                    t);
                Vector3 tangentDirection = new(
                    tangent.x,
                    tangent.y,
                    tangent.z);
                if (tangentDirection.sqrMagnitude > 0.000001f)
                    tangentDirection.Normalize();
                tangent = new Vector4(
                    tangentDirection.x,
                    tangentDirection.y,
                    tangentDirection.z,
                    tangent.w >= 0f ? 1f : -1f);
                return new TrackClipVertex(
                    Vector3.LerpUnclamped(
                        from.Position,
                        to.Position,
                        t),
                    normal,
                    tangent,
                    Vector2.LerpUnclamped(from.Uv, to.Uv, t),
                    Vector2.LerpUnclamped(from.Uv2, to.Uv2, t),
                    Color.LerpUnclamped(from.Color, to.Color, t));
            }
        }

        private static List<TrackClipVertex> ClipTrackTriangle(
            TrackClipVertex a,
            TrackClipVertex b,
            TrackClipVertex c,
            Bounds bounds)
        {
            List<TrackClipVertex> polygon = new(7) { a, b, c };
            polygon = ClipTrackPolygon(polygon, 0, bounds.min.x, true);
            polygon = ClipTrackPolygon(polygon, 0, bounds.max.x, false);
            polygon = ClipTrackPolygon(polygon, 2, bounds.min.z, true);
            return ClipTrackPolygon(polygon, 2, bounds.max.z, false);
        }

        private static List<TrackClipVertex> ClipTrackPolygon(
            IReadOnlyList<TrackClipVertex> input,
            int axis,
            float limit,
            bool keepGreater)
        {
            List<TrackClipVertex> output = new(input.Count + 1);
            if (input.Count == 0)
                return output;

            TrackClipVertex previous = input[input.Count - 1];
            float previousCoordinate = ResolveClipCoordinate(
                previous.Position,
                axis);
            bool previousInside = keepGreater
                ? previousCoordinate >= limit
                : previousCoordinate <= limit;
            for (int index = 0; index < input.Count; index++)
            {
                TrackClipVertex current = input[index];
                float currentCoordinate = ResolveClipCoordinate(
                    current.Position,
                    axis);
                bool currentInside = keepGreater
                    ? currentCoordinate >= limit
                    : currentCoordinate <= limit;
                if (currentInside != previousInside)
                {
                    float denominator =
                        currentCoordinate - previousCoordinate;
                    float interpolation = Mathf.Abs(denominator) >
                        0.00000001f
                            ? (limit - previousCoordinate) / denominator
                            : 0f;
                    output.Add(TrackClipVertex.Lerp(
                        previous,
                        current,
                        interpolation));
                }
                if (currentInside)
                    output.Add(current);

                previous = current;
                previousCoordinate = currentCoordinate;
                previousInside = currentInside;
            }
            return output;
        }

        private static float ResolveClipCoordinate(
            Vector3 point,
            int axis)
        {
            return axis == 0 ? point.x : point.z;
        }

        private Material ResolveTrackContextMaterial(Material source)
        {
            if (source != null &&
                convertedMaterials.TryGetValue(
                    source,
                    out Material cached))
            {
                return cached;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ??
                Shader.Find("Universal Render Pipeline/Unlit") ??
                Shader.Find("Unlit/Color");
            if (shader == null)
                return source;

            bool unsupported = source == null ||
                source.shader == null ||
                !source.shader.isSupported;
            if (unsupported)
                unsupportedSourceMaterialCount++;

            string sourceName = source != null
                ? source.name
                : "MissingTrackMaterial";
            string lowerName = sourceName.ToLowerInvariant();
            Color baseColor = ResolveTrackContextBaseColor(
                source,
                lowerName);
            Material converted = new(shader)
            {
                name = $"AccidentSuzuka_{sourceName}"
            };
            if (converted.HasProperty("_BaseColor"))
                converted.SetColor("_BaseColor", baseColor);
            if (converted.HasProperty("_Color"))
                converted.SetColor("_Color", baseColor);
            if (converted.HasProperty("_Metallic"))
                converted.SetFloat("_Metallic", 0f);
            if (converted.HasProperty("_Smoothness"))
                converted.SetFloat("_Smoothness", 0.16f);

            Texture baseTexture = ResolveTrackContextBaseTexture(source);
            if (baseTexture != null)
            {
                if (converted.HasProperty("_BaseMap"))
                    converted.SetTexture("_BaseMap", baseTexture);
                if (converted.HasProperty("_MainTex"))
                    converted.SetTexture("_MainTex", baseTexture);
                string sourceTextureProperty = source != null &&
                    source.HasProperty("_BaseMap")
                        ? "_BaseMap"
                        : "_MainTex";
                if (source != null &&
                    source.HasProperty(sourceTextureProperty))
                {
                    Vector2 scale = source.GetTextureScale(
                        sourceTextureProperty);
                    Vector2 offset = source.GetTextureOffset(
                        sourceTextureProperty);
                    if (converted.HasProperty("_BaseMap"))
                    {
                        converted.SetTextureScale("_BaseMap", scale);
                        converted.SetTextureOffset("_BaseMap", offset);
                    }
                }
            }

            runtimeMaterials.Add(converted);
            if (source != null)
                convertedMaterials[source] = converted;
            Debug.Log(
                $"[EventTrackSegment] material source={sourceName}, " +
                $"shader={(source != null && source.shader != null ? source.shader.name : "missing")}, " +
                $"supported={!unsupported}, converted={shader.name}.");
            return converted;
        }

        private static Texture ResolveTrackContextBaseTexture(
            Material source)
        {
            if (source == null)
                return null;
            if (source.HasProperty("_BaseMap"))
                return source.GetTexture("_BaseMap");
            if (source.HasProperty("_MainTex"))
                return source.GetTexture("_MainTex");
            return null;
        }

        private static Color ResolveTrackContextBaseColor(
            Material source,
            string lowerName)
        {
            Color color = Color.white;
            if (source != null)
            {
                if (source.HasProperty("_BaseColor"))
                    color = source.GetColor("_BaseColor");
                else if (source.HasProperty("_Color"))
                    color = source.GetColor("_Color");
            }

            if (lowerName.Contains("road") ||
                lowerName.Contains("asphalt") ||
                lowerName.Contains("tarmac"))
            {
                color *= new Color(0.34f, 0.35f, 0.37f, 1f);
            }
            else if (lowerName.Contains("green") ||
                     lowerName.Contains("runoff") ||
                     lowerName.Contains("grass") ||
                     lowerName.Contains("ground") ||
                     lowerName.Contains("terrain"))
            {
                color *= new Color(0.30f, 0.48f, 0.24f, 1f);
            }
            else if (lowerName.Contains("gravel") ||
                     lowerName.Contains("grvl"))
            {
                color *= new Color(0.48f, 0.39f, 0.27f, 1f);
            }
            else if (lowerName.Contains("skid") ||
                     lowerName.Contains("groove"))
            {
                color *= new Color(0.15f, 0.16f, 0.17f, 1f);
            }
            else
            {
                color *= new Color(0.42f, 0.43f, 0.45f, 1f);
            }
            color.a = 1f;
            return color;
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
                name.Contains("terrain") ||
                name.Contains("ground"))
            {
                return false;
            }

            return (name.Contains("road") &&
                    !name.Contains("road_rk_green")) ||
                name.Contains("asphalt") ||
                name.Contains("tarmac");
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
            float lateralPadding,
            float longitudinalPadding)
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

            float safeLateralPadding = Mathf.Max(0f, lateralPadding);
            float safeLongitudinalPadding = Mathf.Max(
                0f,
                longitudinalPadding);
            bounds.Expand(new Vector3(
                safeLateralPadding * 2f,
                0.04f,
                safeLongitudinalPadding * 2f));
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
