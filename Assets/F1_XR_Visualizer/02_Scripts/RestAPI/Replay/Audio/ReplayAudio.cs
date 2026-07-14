using System;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public sealed class ReplayAudio
    {
        private readonly ReplayCarSet cars;
        private readonly EngineSoundSettingsSnapshot settingsSnapshot = new();

        private bool wasPlacementReady;

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
            cars.SetEngineSound(settings);
            settingsSnapshot.Capture(settings);
        }
    }
}