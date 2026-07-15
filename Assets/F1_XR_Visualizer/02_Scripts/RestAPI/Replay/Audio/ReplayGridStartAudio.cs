using System.Collections.Generic;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public class ReplayGridStartAudio
    {
        private readonly Dictionary<int, AudioSource> sources = new();
        private readonly List<int> redBullDrivers = new();
        private int lastSelectedDriver = -1;

        public void SetDrivers(IReadOnlyDictionary<int, string> driverTeams, bool useTeamBasedAudio)
        {
            redBullDrivers.Clear();

            if (!useTeamBasedAudio || driverTeams == null)
                return;

            foreach (KeyValuePair<int, string> pair in driverTeams)
            {
                if (EngineAudioTeamMatcher.IsRedBull(pair.Value))
                    redBullDrivers.Add(pair.Key);
            }

            redBullDrivers.Sort();
        }

        public void Apply(
            CarEngineSoundSettings settings,
            IReadOnlyDictionary<int, ReplayCarView> cars,
            int selectedDriver,
            bool placementReady,
            float currentReplayTime,
            float raceStartTime,
            bool isPlaying,
            float playbackSpeed)
        {
            if (settings == null ||
                !settings.useTeamBasedEngineAudio ||
                !settings.enableNewGridStartAudio ||
                settings.redBullGridStartClip == null ||
                !placementReady)
            {
                Stop(selectedDriver);
                return;
            }

            AudioClip clip = settings.redBullGridStartClip;
            float clipStartTime = raceStartTime - Mathf.Max(0f, settings.gridStartLaunchOffsetSeconds);
            float clipLocalTime = currentReplayTime - clipStartTime;

            if (clipLocalTime < 0f || clipLocalTime >= clip.length)
            {
                Stop(selectedDriver);
                return;
            }

            if (lastSelectedDriver != selectedDriver)
                Stop(selectedDriver);

            if (selectedDriver > 0)
            {
                StopSourcesExcept(selectedDriver, 0);

                if (cars.TryGetValue(selectedDriver, out ReplayCarView selectedCar) && selectedCar != null)
                {
                    AudioSource source = GetOrCreateSource(selectedDriver, selectedCar, settings);
                    SyncSource(
                        source,
                        clip,
                        clipLocalTime,
                        0f,
                        Mathf.Clamp01(settings.selectedStartGain),
                        isPlaying,
                        playbackSpeed);
                }

                return;
            }

            int firstDriver = redBullDrivers.Count > 0 ? redBullDrivers[0] : 0;
            int secondDriver = redBullDrivers.Count > 1 ? redBullDrivers[1] : 0;
            StopSourcesExcept(firstDriver, secondDriver);

            if (firstDriver > 0 && cars.TryGetValue(firstDriver, out ReplayCarView firstCar) && firstCar != null)
            {
                SyncSource(
                    GetOrCreateSource(firstDriver, firstCar, settings),
                    clip,
                    clipLocalTime,
                    0f,
                    Mathf.Clamp01(settings.redBullStartGainA),
                    isPlaying,
                    playbackSpeed);
            }

            if (secondDriver > 0 && cars.TryGetValue(secondDriver, out ReplayCarView secondCar) && secondCar != null)
            {
                SyncSource(
                    GetOrCreateSource(secondDriver, secondCar, settings),
                    clip,
                    clipLocalTime,
                    Mathf.Clamp(settings.redBullStartSecondDelay, 0f, 0.1f),
                    Mathf.Clamp01(settings.redBullStartGainB),
                    isPlaying,
                    playbackSpeed);
            }
        }

        public void Stop(int selectedDriver)
        {
            foreach (AudioSource source in sources.Values)
            {
                if (source != null)
                    source.Stop();
            }

            lastSelectedDriver = selectedDriver;
        }

        public void Pause()
        {
            foreach (AudioSource source in sources.Values)
            {
                if (source != null && source.isPlaying)
                    source.Pause();
            }
        }

        public void RemoveCar(int driver)
        {
            if (!sources.TryGetValue(driver, out AudioSource source))
                return;

            if (source != null)
                source.Stop();

            sources.Remove(driver);
        }

        public void Clear(int selectedDriver)
        {
            Stop(selectedDriver);
            sources.Clear();
        }

        private void StopSourcesExcept(int firstDriver, int secondDriver)
        {
            foreach (KeyValuePair<int, AudioSource> pair in sources)
            {
                if (pair.Key == firstDriver || pair.Key == secondDriver)
                    continue;

                if (pair.Value != null)
                    pair.Value.Stop();
            }
        }

        private AudioSource GetOrCreateSource(
            int driver,
            ReplayCarView car,
            CarEngineSoundSettings settings)
        {
            if (sources.TryGetValue(driver, out AudioSource source) && source != null)
                return source;

            Transform audioRoot = car.transform.Find("Audio");
            if (audioRoot == null)
            {
                GameObject audioObject = new GameObject("Audio");
                audioObject.transform.SetParent(car.transform, false);
                audioRoot = audioObject.transform;
            }

            Transform sourceTransform = audioRoot.Find("GridStart");
            GameObject sourceObject = sourceTransform != null
                ? sourceTransform.gameObject
                : new GameObject("GridStart");
            sourceObject.transform.SetParent(audioRoot, false);

            source = sourceObject.GetComponent<AudioSource>();
            if (source == null)
                source = sourceObject.AddComponent<AudioSource>();

            source.playOnAwake = false;
            source.loop = false;
            source.volume = 0f;
            ApplySourceSettings(source, settings);
            sources[driver] = source;
            return source;
        }

        private static void ApplySourceSettings(AudioSource source, CarEngineSoundSettings settings)
        {
            if (source == null || settings == null)
                return;

            float maxDistance = settings.maximumAudibleDistance > 0f
                ? settings.maximumAudibleDistance
                : settings.maxDistance;

            source.spatialBlend = Mathf.Clamp01(settings.spatialBlend);
            source.minDistance = Mathf.Max(0.01f, settings.minDistance);
            source.maxDistance = Mathf.Max(source.minDistance, maxDistance);
            source.rolloffMode = AudioRolloffMode.Custom;
            source.dopplerLevel = 0f;
            source.priority = Mathf.Clamp(settings.priority, 0, 256);
            source.SetCustomCurve(
                AudioSourceCurveType.CustomRolloff,
                new AnimationCurve(
                    new Keyframe(0f, 1f),
                    new Keyframe(source.minDistance, 1f),
                    new Keyframe(source.maxDistance, 0f)
                )
            );
        }

        private static void SyncSource(
            AudioSource source,
            AudioClip clip,
            float clipLocalTime,
            float sourceDelay,
            float gain,
            bool isPlaying,
            float playbackSpeed)
        {
            if (source == null || clip == null)
                return;

            float sourceLocalTime = clipLocalTime - sourceDelay;
            if (sourceLocalTime < 0f || sourceLocalTime >= clip.length)
            {
                source.Stop();
                return;
            }

            source.clip = clip;
            source.volume = gain;
            source.pitch = Mathf.Clamp(playbackSpeed, 0.1f, 3f);

            int targetSamples = Mathf.Clamp(
                Mathf.RoundToInt(sourceLocalTime * clip.frequency),
                0,
                Mathf.Max(0, clip.samples - 1));
            int driftSamples = Mathf.Abs(source.timeSamples - targetSamples);
            int maxDriftSamples = Mathf.Max(1, Mathf.RoundToInt(clip.frequency * 0.08f));

            if (!source.isPlaying || driftSamples > maxDriftSamples)
                source.timeSamples = targetSamples;

            if (isPlaying)
            {
                if (!source.isPlaying)
                    source.Play();
            }
            else if (source.isPlaying)
            {
                source.Pause();
            }
        }
    }
}
