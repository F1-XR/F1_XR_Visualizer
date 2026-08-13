using System;
using UnityEngine;

namespace F1XR.Drone.TrackFracture
{
    /// <summary>
    /// Which parts of a circuit are worth breaking, and how finely.
    ///
    /// Kept as data rather than as lookups in code because the answer is different for every
    /// track and cannot be derived: the glTF exporter merges everything sharing a material
    /// into one mesh and names them Object_NN, so neither the name nor the bounds says what a
    /// renderer actually is. The Bahrain list below came from measuring how much of the view
    /// each renderer occupies from inside the circuit - ten of the ninety-eight cover
    /// essentially the whole visible world, and the rest are invisible detail that would cost
    /// draw calls for nothing.
    /// </summary>
    [Serializable]
    public sealed class TrackFractureProfile
    {
        [Tooltip("Shown in logs only.")]
        public string profileName = "Bahrain";

        [Tooltip("Matched against the map prefab's root name, case-insensitive and by " +
            "substring. Leave empty to accept any track.")]
        public string mapNameContains = "Bahrain";

        [Tooltip("GameObject names of the renderers to break. Measured by screen coverage " +
            "from inside the circuit; these ten cover about 97% of everything that is " +
            "actually drawn.")]
        public string[] targetRendererNames =
        {
            "Object_12",   // main terrain, 21.5% of the view on its own
            "Object_82",   //  8.2%
            "Cube",        //  5.3%  ground slab, Ground.mat
            "Object_98",   //  4.9%
            "Object_78",   //  2.6%
            "Object_11",   //  2.5%
            "TrackBase",   //  1.6%  base plate, TrackBaseMat
            "Object_79",   //  1.5%
            "Object_103",  //  1.1%
            "Object_97"    //  0.7%
        };

        [Tooltip("Cells per axis. Six by six is thirty-six cells, which lands around a " +
            "hundred and fifty fragments once empty cells are dropped.")]
        [Range(2, 12)] public int gridResolution = 6;

        [Tooltip("How far each cell centre is nudged off the regular grid, as a fraction of " +
            "cell size. Zero draws a chessboard; this is what makes the break look like a " +
            "fracture rather than a tiling.")]
        [Range(0f, 0.45f)] public float cellJitter = 0.28f;

        [Tooltip("Fixed so the same track always breaks the same way.")]
        public int randomSeed = 20260813;

        public bool Matches(string trackName)
        {
            if (string.IsNullOrWhiteSpace(mapNameContains))
                return true;

            return !string.IsNullOrWhiteSpace(trackName) &&
                trackName.IndexOf(mapNameContains, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public bool IsTarget(string rendererName)
        {
            if (targetRendererNames == null)
                return false;

            foreach (string name in targetRendererNames)
            {
                if (string.Equals(name, rendererName, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }
}
