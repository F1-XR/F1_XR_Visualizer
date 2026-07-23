using System;
using F1XR.RestAPI.Replay.Track.Placement;
using F1XR.RestAPI.Api;
using F1XR.RestAPI.Replay.Track.Build;
using UnityEngine;
using UnityEngine.Serialization;

namespace F1XR.RestAPI.Replay
{
    public static class TrackAssets
    {
        public static void Apply(
            TrackAsset[] assets,
            TrackOption track,
            DatasetManifestDto manifest,
            GameObject defaultVisualizerPrefab,
            ref ARPlanePlacementController placement,
            ref TrackRevealPlacer buildPlacer,
            ref TrackCalibration calibration)
        {
            if (placement == null)
            {
                placement =
                    UnityEngine.Object
                        .FindAnyObjectByType<ARPlanePlacementController>();
            }

            if (!TryFind(assets, track, manifest, out TrackAsset asset))
            {
                Debug.LogWarning(
                    $"Track asset not found. " +
                    $"circuit={manifest?.circuit}, " +
                    $"circuitKey={track?.circuitKey}");

                return;
            }

            GameObject visualizerPrefab = asset.visualizerPrefab != null
                ? asset.visualizerPrefab
                : defaultVisualizerPrefab;

            if (visualizerPrefab == null && placement != null)
                visualizerPrefab = placement.PlacementPrefab;

            if (asset.calibration != null)
                calibration = asset.calibration;

            bool fitMapToBounds =
                asset.TryGetMapTargetXZSize(out Vector2 mapTargetXZSize);

            if (visualizerPrefab == null)
                return;

            if (placement != null)
            {
                placement.SetPlacementPrefab(
                    visualizerPrefab,
                    asset.mapPrefab,
                    asset.MapScale,
                    fitMapToBounds,
                    mapTargetXZSize);
            }

            if (buildPlacer == null)
            {
                buildPlacer =
                    UnityEngine.Object
                        .FindAnyObjectByType<TrackRevealPlacer>();
            }

            if (buildPlacer != null)
            {
                buildPlacer.SetPlacementPrefab(
                    visualizerPrefab,
                    asset.mapPrefab,
                    asset.MapScale,
                    fitMapToBounds,
                    mapTargetXZSize);
            }
        }

        private static bool TryFind(
            TrackAsset[] assets,
            TrackOption track,
            DatasetManifestDto manifest,
            out TrackAsset result)
        {
            result = default;

            if (assets == null)
                return false;

            foreach (TrackAsset asset in assets)
            {
                if (!asset.Matches(track, manifest))
                    continue;

                result = asset;
                return true;
            }

            return false;
        }
    }

    [Serializable]
    public struct TrackAsset
    {
        public int circuitKey;
        public string circuitName;
        public string circuitShortName;

        [FormerlySerializedAs("prefab")]
        public GameObject visualizerPrefab;

        public GameObject mapPrefab;
        public float mapScale;
        public bool fitMapToCalibrationBounds;
        public float mapFitScaleMultiplier;
        public TrackCalibration calibration;

        public float MapScale =>
            mapScale > 0f ? mapScale : 1f;

        public float MapFitScaleMultiplier =>
            mapFitScaleMultiplier > 0f
                ? mapFitScaleMultiplier
                : 1f;

        public bool TryGetMapTargetXZSize(out Vector2 size)
        {
            size = default;

            if (!fitMapToCalibrationBounds ||
                calibration == null ||
                calibration.points == null)
                return false;

            bool found = false;
            Vector2 min = default;
            Vector2 max = default;

            foreach (TrackCalibration.Point point in calibration.points)
            {
                if (string.IsNullOrWhiteSpace(point.name))
                    continue;

                Vector2 position = new(
                    point.targetLocalPosition.x,
                    point.targetLocalPosition.z);

                if (!found)
                {
                    min = position;
                    max = position;
                    found = true;
                    continue;
                }

                min = Vector2.Min(min, position);
                max = Vector2.Max(max, position);
            }

            if (!found)
                return false;

            size =
                (max - min) *
                calibration.OutputScale *
                MapFitScaleMultiplier;

            return size.x > 0.0001f &&
                size.y > 0.0001f;
        }

        public bool Matches(
            TrackOption track,
            DatasetManifestDto manifest)
        {
            if (track != null)
            {
                if (circuitKey > 0 &&
                    circuitKey == track.circuitKey)
                    return true;

                if (MatchesName(
                        circuitShortName,
                        track.circuitShortName) ||
                    MatchesName(
                        circuitName,
                        track.meetingName) ||
                    MatchesName(
                        circuitName,
                        track.location))
                {
                    return true;
                }
            }

            return manifest != null &&
                (MatchesName(circuitName, manifest.circuit) ||
                 MatchesName(circuitShortName, manifest.circuit));
        }

        private static bool MatchesName(string a, string b)
        {
            return !string.IsNullOrWhiteSpace(a) &&
                !string.IsNullOrWhiteSpace(b) &&
                string.Equals(
                    a.Trim(),
                    b.Trim(),
                    StringComparison.OrdinalIgnoreCase);
        }
    }
}
