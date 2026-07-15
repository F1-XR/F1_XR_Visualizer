using F1XR.RestAPI.Replay.Playback;
using F1XR.RestAPI.Replay.Track.Placement;
using F1XR.RestAPI.Replay.Track.Build;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public sealed class ReplayStartingLights
    {
        private StartingLightSequence sequence;
        private GameObject prefab;
        private bool hasRotationOffset;
        private Quaternion rotationOffset = Quaternion.identity;

        public ReplayStartingLights(
            StartingLightSequence sequence,
            GameObject prefab)
        {
            Reset(sequence, prefab);
        }

        public void Reset(
            StartingLightSequence source,
            GameObject sourcePrefab)
        {
            sequence = source;
            prefab = sourcePrefab;
            hasRotationOffset = false;
            rotationOffset = Quaternion.identity;
        }

        public StartingLightSequence ApplyTimeline(
            bool enabled,
            bool trackPlaced,
            ARPlanePlacementController placement,
            TrackRevealPlacer buildPlacer,
            float currentTime,
            float timelineStartTime,
            float raceStartTime,
            bool isPlaying,
            float playbackSpeed,
            float leadSeconds,
            float firstDelay,
            float interval,
            float hideDelay)
        {
            if (!enabled || !trackPlaced)
                return sequence;

            float showTime = Mathf.Max(
                timelineStartTime,
                raceStartTime - Mathf.Max(0f, leadSeconds));

            float hideTime = raceStartTime + Mathf.Max(0f, hideDelay);
            bool inWindow = currentTime >= showTime && currentTime < hideTime;

            if (sequence == null && !inWindow)
                return null;

            sequence = Resolve();
            if (sequence == null)
                return null;

            Position(sequence.transform, placement, buildPlacer);

            sequence.ApplyTimeline(
                currentTime,
                raceStartTime,
                isPlaying,
                playbackSpeed,
                leadSeconds,
                firstDelay,
                interval,
                hideDelay);

            return sequence;
        }

        private StartingLightSequence Resolve()
        {
            if (sequence != null)
                return sequence;

            StartingLightSequence[] candidates =
                Object.FindObjectsByType<StartingLightSequence>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);

            if (candidates != null && candidates.Length > 0)
            {
                sequence = candidates[0];
                return sequence;
            }

            if (prefab == null)
                return null;

            GameObject instance = Object.Instantiate(prefab);
            instance.name = prefab.name;
            sequence = instance.GetComponent<StartingLightSequence>();

            return sequence;
        }

        private void Position(
            Transform target,
            ARPlanePlacementController placement,
            TrackRevealPlacer buildPlacer)
        {
            if (target == null)
                return;

            Transform mapTransform =
                buildPlacer != null && buildPlacer.HasPlacement
                    ? buildPlacer.PlacementTransform
                    : placement != null && placement.HasPlacement
                        ? placement.PlacementTransform
                        : null;

            if (mapTransform == null)
                return;

            Vector3 position = mapTransform.position + Vector3.up * 0.5f;
            target.position = position;

            Camera camera = Camera.main;
            if (camera == null)
                return;

            Vector3 toCamera = camera.transform.position - position;
            toCamera.y = 0f;

            if (toCamera.sqrMagnitude <= 0.0001f)
                return;

            RememberRotation(target);

            Quaternion lookRotation =
                Quaternion.LookRotation(toCamera.normalized, Vector3.up);

            target.rotation = lookRotation * rotationOffset;
        }

        private void RememberRotation(Transform target)
        {
            if (hasRotationOffset || target == null)
                return;

            rotationOffset = target.rotation;
            hasRotationOffset = true;
        }
    }
}
