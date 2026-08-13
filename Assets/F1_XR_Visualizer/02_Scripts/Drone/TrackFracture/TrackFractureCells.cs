using System.Collections.Generic;
using UnityEngine;

namespace F1XR.Drone.TrackFracture
{
    /// <summary>
    /// The grid the whole circuit is broken along, and when each part of it lets go.
    ///
    /// Cells live in the placement root's own coordinates, so the same triangle lands in the
    /// same cell whether the map is sitting on a table or blown up a thousand times around
    /// the viewer's head. Scale must not change what breaks.
    ///
    /// Seeds are a regular grid pushed off-centre by a fixed amount. Assignment is nearest
    /// seed, not polygon clipping: a triangle goes wholly to one cell and is never cut. That
    /// keeps every fragment made of original, untouched triangles while the overall break
    /// still reads as irregular rather than as a chessboard.
    /// </summary>
    public sealed class TrackFractureCells
    {
        public struct Cell
        {
            public Vector2 Seed;        // placement-root local XZ
            public int GridX;
            public int GridY;
            public float CrackTime;
            public float DetachTime;
            public bool Used;           // any triangle landed here

            /// <summary>
            /// Surface area of the geometry in this cell, in placement-root units.
            ///
            /// Counting cells treats a cell holding two lamp posts as equal to one holding
            /// half the terrain, so "seventy-five per cent of cells gone" can still leave the
            /// view almost entirely virtual. Area is a far better stand-in for how much of
            /// the world has actually been removed.
            /// </summary>
            public float Weight;
        }

        readonly List<Cell> cells = new();
        int resolution;
        float cellSize;

        public int Count => cells.Count;
        public Cell this[int index] => cells[index];
        public int Resolution => resolution;

        /// <summary>
        /// Width of one cell in placement-root units. Motion is expressed as a fraction of
        /// this rather than as an absolute distance: the map is a thousand times bigger in VR
        /// than on the table, so a fixed local distance that looks like a nudge on the table
        /// is a hundreds-of-metres plunge once the viewer is standing inside the circuit.
        /// </summary>
        public float CellSize => cellSize;

        /// <summary>
        /// Lays the grid over the footprint of everything that is going to break.
        /// </summary>
        public void Build(Bounds localBounds, TrackFractureProfile profile)
        {
            cells.Clear();
            resolution = Mathf.Max(2, profile.gridResolution);

            var random = new System.Random(profile.randomSeed);
            float stepX = localBounds.size.x / resolution;
            float stepZ = localBounds.size.z / resolution;
            cellSize = Mathf.Max(stepX, stepZ);

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float cx = localBounds.min.x + stepX * (x + 0.5f);
                    float cz = localBounds.min.z + stepZ * (y + 0.5f);

                    cx += (float)(random.NextDouble() * 2.0 - 1.0) * profile.cellJitter * stepX;
                    cz += (float)(random.NextDouble() * 2.0 - 1.0) * profile.cellJitter * stepZ;

                    cells.Add(new Cell
                    {
                        Seed = new Vector2(cx, cz),
                        GridX = x,
                        GridY = y
                    });
                }
            }
        }

        /// <summary>Nearest seed to a point already in placement-root local XZ.</summary>
        public int CellAt(Vector2 localXZ)
        {
            int best = 0;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < cells.Count; i++)
            {
                float dx = cells[i].Seed.x - localXZ.x;
                float dz = cells[i].Seed.y - localXZ.y;
                float sqr = dx * dx + dz * dz;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = i;
                }
            }

            return best;
        }

        public void MarkUsed(int index)
        {
            Cell c = cells[index];
            c.Used = true;
            cells[index] = c;
        }

        public void AddWeight(int index, float area)
        {
            Cell c = cells[index];
            c.Weight += area;
            cells[index] = c;
        }

        public float TotalWeight()
        {
            float total = 0f;
            foreach (Cell c in cells)
                total += c.Weight;

            return total;
        }

        /// <summary>
        /// Area whose pieces have started to come away by this point in the break. This is
        /// what "how much of the world is gone" is measured against.
        /// </summary>
        public float DetachedWeight(float elapsed)
        {
            float detached = 0f;
            foreach (Cell c in cells)
            {
                if (c.Used && elapsed >= c.DetachTime)
                    detached += c.Weight;
            }

            return detached;
        }

        public int RemainingCells(float elapsed)
        {
            int remaining = 0;
            foreach (Cell c in cells)
            {
                if (c.Used && elapsed < c.DetachTime)
                    remaining++;
            }

            return remaining;
        }

        /// <summary>
        /// Walks the crack out from the cell nearest the viewer, one ring of neighbours at a
        /// time. Adjacency is taken from the original grid coordinates rather than from the
        /// jittered seed positions - the jitter is a look, not a topology, and a grid is
        /// already the right neighbour graph.
        /// </summary>
        public void ComputeTiming(
            Vector2 originLocalXZ,
            float propagationStep,
            float looseDuration,
            float jitterSeconds,
            int randomSeed)
        {
            int originCell = CellAt(originLocalXZ);
            var random = new System.Random(randomSeed + 31);

            var hops = new int[cells.Count];
            for (int i = 0; i < hops.Length; i++)
                hops[i] = -1;

            var queue = new Queue<int>();
            hops[originCell] = 0;
            queue.Enqueue(originCell);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                int cx = cells[current].GridX;
                int cy = cells[current].GridY;

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0)
                            continue;

                        int nx = cx + dx;
                        int ny = cy + dy;
                        if (nx < 0 || ny < 0 || nx >= resolution || ny >= resolution)
                            continue;

                        int neighbour = ny * resolution + nx;
                        if (hops[neighbour] >= 0)
                            continue;

                        hops[neighbour] = hops[current] + 1;
                        queue.Enqueue(neighbour);
                    }
                }
            }

            for (int i = 0; i < cells.Count; i++)
            {
                Cell c = cells[i];
                int hop = hops[i] >= 0 ? hops[i] : resolution;
                float jitter = (float)random.NextDouble() * jitterSeconds;
                c.CrackTime = hop * propagationStep + jitter;
                c.DetachTime = c.CrackTime + looseDuration;
                cells[i] = c;
            }
        }

        public float LastDetachTime()
        {
            float last = 0f;
            foreach (Cell c in cells)
            {
                if (c.Used && c.DetachTime > last)
                    last = c.DetachTime;
            }

            return last;
        }

        public int UsedCount()
        {
            int n = 0;
            foreach (Cell c in cells)
            {
                if (c.Used)
                    n++;
            }

            return n;
        }

        public void Clear() => cells.Clear();
    }
}
