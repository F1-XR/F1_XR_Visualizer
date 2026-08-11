using System.Collections.Generic;
using UnityEngine;

namespace F1XR.Experience.Fracture
{
    /// <summary>
    /// Splits a convex 2D polygon into Voronoi cells. Pure geometry, no scene objects, no
    /// external package.
    ///
    /// The cells are produced by half-plane clipping rather than by building a Delaunay
    /// triangulation and taking its dual: for each seed, start with the whole boundary
    /// polygon and clip it by the perpendicular bisector against every other seed. What
    /// survives is exactly that seed's Voronoi cell. It is O(n^2) in the seed count, which
    /// costs nothing at the 20-40 seeds this is used with, and it needs one Sutherland
    /// Hodgman pass instead of a triangulation library.
    ///
    /// Every cell it returns is convex, so callers can fan triangulate them directly and
    /// never need an ear clipper.
    ///
    /// Limitation worth remembering: the boundary polygon must be convex. A rectangle or
    /// any convex outline is fine. A concave outline (an L shaped wall, say) would produce
    /// cells that spill across the concave part.
    /// </summary>
    public static class VoronoiShatter
    {
        /// <summary>
        /// Builds one cell polygon per seed. Cells with fewer than three corners are
        /// dropped, so the result can be shorter than the seed list.
        /// </summary>
        public static List<List<Vector2>> BuildCells(
            IReadOnlyList<Vector2> boundary,
            IReadOnlyList<Vector2> seeds)
        {
            var cells = new List<List<Vector2>>(seeds.Count);
            if (boundary == null || boundary.Count < 3 || seeds == null)
                return cells;

            var working = new List<Vector2>(boundary.Count + seeds.Count);
            var clipped = new List<Vector2>(working.Capacity);

            for (int i = 0; i < seeds.Count; i++)
            {
                working.Clear();
                working.AddRange(boundary);

                for (int j = 0; j < seeds.Count && working.Count >= 3; j++)
                {
                    if (i == j)
                        continue;

                    // Keep the half-plane on seed i's side of the bisector between i and j.
                    Vector2 towardsOther = seeds[j] - seeds[i];
                    if (towardsOther.sqrMagnitude < 1e-10f)
                        continue;

                    Vector2 midpoint = (seeds[i] + seeds[j]) * 0.5f;
                    ClipByHalfPlane(working, midpoint, towardsOther.normalized, clipped);

                    (working, clipped) = (clipped, working);
                }

                if (working.Count >= 3)
                    cells.Add(new List<Vector2>(working));
            }

            return cells;
        }

        /// <summary>
        /// Sutherland Hodgman clip of a polygon against a single half-plane. Points are
        /// kept when they sit on the negative side of the plane through
        /// <paramref name="planePoint"/> with normal <paramref name="planeNormal"/>.
        /// </summary>
        static void ClipByHalfPlane(
            List<Vector2> polygon,
            Vector2 planePoint,
            Vector2 planeNormal,
            List<Vector2> result)
        {
            result.Clear();
            if (polygon.Count == 0)
                return;

            Vector2 current = polygon[polygon.Count - 1];
            float currentSide = Vector2.Dot(current - planePoint, planeNormal);

            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 next = polygon[i];
                float nextSide = Vector2.Dot(next - planePoint, planeNormal);

                if (nextSide <= 0f)
                {
                    if (currentSide > 0f)
                        result.Add(Intersect(current, next, currentSide, nextSide));

                    result.Add(next);
                }
                else if (currentSide <= 0f)
                {
                    result.Add(Intersect(current, next, currentSide, nextSide));
                }

                current = next;
                currentSide = nextSide;
            }
        }

        static Vector2 Intersect(Vector2 a, Vector2 b, float sideA, float sideB)
        {
            float denominator = sideA - sideB;
            if (Mathf.Abs(denominator) < 1e-10f)
                return a;

            return Vector2.LerpUnclamped(a, b, sideA / denominator);
        }

        /// <summary>
        /// Seeds for the shatter, laid out on a jittered grid and then pulled towards the
        /// fracture origin.
        ///
        /// A plain random scatter makes long thin slivers, which read as shards of glass
        /// rather than shell. A jittered grid keeps the cells to a similar order of size
        /// while leaving every one of them a different shape.
        ///
        /// <paramref name="originBias"/> above zero warps the radius so seeds bunch up
        /// near the origin: the break point ends up finely shattered and the far edge
        /// stays in larger pieces, which is how a shell actually gives way.
        /// </summary>
        public static List<Vector2> GenerateSeeds(
            IReadOnlyList<Vector2> boundary,
            int count,
            Vector2 origin,
            float originBias,
            int randomSeed)
        {
            var seeds = new List<Vector2>(count);
            if (boundary == null || boundary.Count < 3 || count <= 0)
                return seeds;

            Rect bounds = GetBounds(boundary);
            var random = new System.Random(randomSeed);

            float aspect = bounds.height > 1e-5f ? bounds.width / bounds.height : 1f;
            int columns = Mathf.Max(1, Mathf.RoundToInt(Mathf.Sqrt(count * aspect)));
            int rows = Mathf.Max(1, Mathf.CeilToInt(count / (float)columns));

            float maxRadius = MaxDistanceToCorners(bounds, origin);

            // Spacing between grid columns, used as the yardstick for "too close". Rejecting
            // seeds nearer than a fraction of this kills the long thin slivers that come from
            // two seeds landing almost on top of each other.
            float cellSpacing = bounds.width / Mathf.Max(1, columns);

            for (int row = 0; row < rows && seeds.Count < count; row++)
            {
                for (int column = 0; column < columns && seeds.Count < count; column++)
                {
                    float u = (column + (float)random.NextDouble()) / columns;
                    float v = (row + (float)random.NextDouble()) / rows;
                    var point = new Vector2(
                        Mathf.Lerp(bounds.xMin, bounds.xMax, u),
                        Mathf.Lerp(bounds.yMin, bounds.yMax, v));

                    point = ApplyOriginBias(point, origin, maxRadius, originBias);

                    if (!IsInsidePolygon(boundary, point))
                        continue;

                    // Minimum spacing shrinks near the origin, so the break point still
                    // shatters finely while the rest keeps clean, even-sized cells.
                    float t = maxRadius > 1e-5f
                        ? Mathf.Clamp01(Vector2.Distance(point, origin) / maxRadius)
                        : 1f;
                    float minSpacing = cellSpacing * Mathf.Lerp(0.18f, 0.42f, t);

                    if (TooClose(seeds, point, minSpacing))
                        continue;

                    seeds.Add(point);
                }
            }

            return seeds;
        }

        static bool TooClose(List<Vector2> seeds, Vector2 point, float minSpacing)
        {
            float minSqr = minSpacing * minSpacing;
            for (int i = 0; i < seeds.Count; i++)
            {
                if ((seeds[i] - point).sqrMagnitude < minSqr)
                    return true;
            }

            return false;
        }

        static Vector2 ApplyOriginBias(
            Vector2 point, Vector2 origin, float maxRadius, float originBias)
        {
            if (originBias <= 0f || maxRadius <= 1e-5f)
                return point;

            Vector2 offset = point - origin;
            float radius = offset.magnitude;
            if (radius < 1e-5f)
                return point;

            float normalized = Mathf.Clamp01(radius / maxRadius);
            float warped = Mathf.Pow(normalized, 1f + originBias);
            return origin + offset * (warped * maxRadius / radius);
        }

        static float MaxDistanceToCorners(Rect bounds, Vector2 origin)
        {
            float maxDistance = 0f;
            maxDistance = Mathf.Max(maxDistance, Vector2.Distance(origin, new Vector2(bounds.xMin, bounds.yMin)));
            maxDistance = Mathf.Max(maxDistance, Vector2.Distance(origin, new Vector2(bounds.xMax, bounds.yMin)));
            maxDistance = Mathf.Max(maxDistance, Vector2.Distance(origin, new Vector2(bounds.xMin, bounds.yMax)));
            maxDistance = Mathf.Max(maxDistance, Vector2.Distance(origin, new Vector2(bounds.xMax, bounds.yMax)));
            return maxDistance;
        }

        public static Rect GetBounds(IReadOnlyList<Vector2> polygon)
        {
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            for (int i = 0; i < polygon.Count; i++)
            {
                minX = Mathf.Min(minX, polygon[i].x);
                minY = Mathf.Min(minY, polygon[i].y);
                maxX = Mathf.Max(maxX, polygon[i].x);
                maxY = Mathf.Max(maxY, polygon[i].y);
            }

            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        public static Vector2 Centroid(IReadOnlyList<Vector2> polygon)
        {
            var sum = Vector2.zero;
            for (int i = 0; i < polygon.Count; i++)
                sum += polygon[i];

            return sum / polygon.Count;
        }

        /// <summary>
        /// Adjacency between cells: two cells are neighbours when they share an edge, which
        /// for Voronoi cells means they own the same two corner points. That is enough for a
        /// crack to walk from one piece to the next instead of every piece at a given radius
        /// letting go together.
        ///
        /// Returned as one neighbour list per cell, index-aligned with <paramref name="cells"/>.
        /// </summary>
        public static List<int>[] BuildAdjacency(
            IReadOnlyList<List<Vector2>> cells, float weldEpsilon = 0.004f)
        {
            int count = cells.Count;
            var adjacency = new List<int>[count];
            for (int i = 0; i < count; i++)
                adjacency[i] = new List<int>();

            float epsilonSqr = weldEpsilon * weldEpsilon;

            for (int i = 0; i < count; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    if (ShareAnEdge(cells[i], cells[j], epsilonSqr))
                    {
                        adjacency[i].Add(j);
                        adjacency[j].Add(i);
                    }
                }
            }

            return adjacency;
        }

        static bool ShareAnEdge(
            List<Vector2> a, List<Vector2> b, float epsilonSqr)
        {
            // Count corners the two cells have in common. Two shared corners means a shared
            // edge. Voronoi neighbours share exactly one edge, so two is the threshold.
            int shared = 0;
            for (int ai = 0; ai < a.Count; ai++)
            {
                for (int bi = 0; bi < b.Count; bi++)
                {
                    if ((a[ai] - b[bi]).sqrMagnitude <= epsilonSqr)
                    {
                        shared++;
                        if (shared >= 2)
                            return true;

                        break;
                    }
                }
            }

            return false;
        }

        /// <summary>Index of the cell whose centroid is nearest a point.</summary>
        public static int NearestCell(IReadOnlyList<List<Vector2>> cells, Vector2 point)
        {
            int best = 0;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < cells.Count; i++)
            {
                float sqr = (Centroid(cells[i]) - point).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = i;
                }
            }

            return best;
        }

        static bool IsInsidePolygon(IReadOnlyList<Vector2> polygon, Vector2 point)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[j];
                if (a.y > point.y != b.y > point.y &&
                    point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                {
                    inside = !inside;
                }
            }

            return inside;
        }
    }
}
