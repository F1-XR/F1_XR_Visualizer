using System;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public sealed class ReplayAudio
    {
        private readonly ReplayCarSet cars;
        private readonly EngineSoundSettingsSnapshot settingsSnapshot = new();

        private bool wasPlacementReady;
        private float distanceScale = 1f;

        public ReplayAudio(ReplayCarSet cars)
        {
            this.cars = cars;
        }

        public void Reset(
            CarEngineSoundSettings settings,
            bool placementReady,
            Action applyDriverMetadata)
        {
            Refresh(settings, applyDriverMetadata);

            wasPlacementReady = placementReady;
            cars.SetSoundPlacementReady(placementReady);
            cars.SetSoundPlaying(false);
        }

        public void Update(
            CarEngineSoundSettings settings,
            bool placementReady,
            bool isPlaying,
            Action applyDriverMetadata)
        {
            ApplySettingsChange(
                settings,
                applyDriverMetadata);

            cars.SetSoundPlacementReady(placementReady);

            if (!wasPlacementReady && placementReady)
            {
                Refresh(
                    settings,
                    applyDriverMetadata);
            }

            wasPlacementReady = placementReady;
            cars.SetSoundPlaying(isPlaying);
        }

        public void SetPlaying(bool isPlaying)
        {
            cars.SetSoundPlaying(isPlaying);
        }

        public void SetDistanceScale(float value)
        {
            distanceScale = Mathf.Max(0.0001f, value);
        }

        public void SetDistanceScale(
            float value,
            CarEngineSoundSettings settings,
            Action applyDriverMetadata)
        {
            float nextScale = Mathf.Max(0.0001f, value);
            if (Mathf.Approximately(distanceScale, nextScale))
                return;

            distanceScale = nextScale;
            Refresh(settings, applyDriverMetadata);
        }

        public void ResetPlacement()
        {
            wasPlacementReady = false;
            cars.SetSoundPlacementReady(false);
            cars.SetSoundPlaying(false);
            cars.ResetPlacement();
        }

        public void ApplyGridStart(
            float currentTime,
            float raceStartTime,
            bool isPlaying,
            float playbackSpeed)
        {
            cars.ApplyGridStartTimeline(
                currentTime,
                raceStartTime,
                isPlaying,
                playbackSpeed);
        }

        public void Clear()
        {
            cars.SetSoundPlaying(false);
            cars.StopGridStartAudio();
        }

        private void ApplySettingsChange(
            CarEngineSoundSettings settings,
            Action applyDriverMetadata)
        {
            if (!settingsSnapshot.HasChanged(
                    settings,
                    out bool modeChanged))
                return;

            if (modeChanged)
            {
                Debug.Log(
                    settings.useTeamBasedEngineAudio
                        ? "[EngineAudio] Mode changed: TeamBased"
                        : "[EngineAudio] Mode changed: Legacy");
            }

            cars.StopGridStartAudio();

            Refresh(
                settings,
                applyDriverMetadata);
        }

        private void Refresh(
            CarEngineSoundSettings settings,
            Action applyDriverMetadata)
        {
            applyDriverMetadata?.Invoke();
            cars.SetEngineSound(GetScaledSettings(settings));
            settingsSnapshot.Capture(settings);
        }

        CarEngineSoundSettings GetScaledSettings(
            CarEngineSoundSettings settings)
        {
            if (settings == null || Mathf.Approximately(distanceScale, 1f))
                return settings;

            CarEngineSoundSettings scaled = settings.CloneForProfile(null);
            scaled.minDistance *= distanceScale;
            scaled.maxDistance *= distanceScale;
            scaled.maximumAudibleDistance *= distanceScale;
            return scaled;
        }
    }
}
