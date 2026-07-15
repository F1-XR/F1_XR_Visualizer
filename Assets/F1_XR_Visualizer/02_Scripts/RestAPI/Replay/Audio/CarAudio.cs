using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace F1XR.RestAPI.Replay
{
    public class CarAudio
    {
        private const float ConfigLogInterval = 1f;

        private readonly IReadOnlyDictionary<int, ReplayCarView> cars;
        private readonly IReadOnlyDictionary<int, string> driverTeams;
        private readonly Dictionary<int, CarEngineSound> engineSounds = new();
        private readonly Dictionary<int, CarEngineSoundSettings> runtimeSettings = new();
        private readonly Dictionary<int, EngineAudioProfile> nativeProfiles = new();
        private readonly List<CarEngineSound> soundOrder = new();

        private CarEngineSoundSettings settings;
        private bool playing = true;
        private bool placementReady;
        private int selectedDriver;
        private bool loggedEngineSound;
        private bool loggedMissingSoundTeam;
        private bool loggedNoAudioListener;
        private bool loggedNoAudibleCars;
        private bool loggedWaitingForTeams;
        private bool loggedDriverTeams;
        private int configLogBurst = 8;
        private float nextConfigLogTime;

        public CarAudio(
            IReadOnlyDictionary<int, ReplayCarView> cars,
            IReadOnlyDictionary<int, string> driverTeams)
        {
            this.cars = cars;
            this.driverTeams = driverTeams;
        }

        public CarEngineSoundSettings Settings => settings;
        public bool PlacementReady => placementReady;
        public bool IsActive => playing && placementReady;

        public void SetSettings(CarEngineSoundSettings value)
        {
            settings = value ?? new CarEngineSoundSettings();
            CacheNativeProfiles();
        }

        public void RefreshDriverData()
        {
            CacheNativeProfiles();

            if (loggedDriverTeams)
                return;

            int matched = CountTeamMatches();
            Debug.Log($"[EngineSound] driver teams loaded. count={driverTeams.Count}, filter='{settings?.teamNameFilter}', matches={matched}");
            loggedDriverTeams = true;
        }

        public void SetSelectedDriver(int driver)
        {
            selectedDriver = driver;

            if (settings != null && settings.useEngineSound)
                ConfigureCars();

            UpdateAudibility();
        }

        public void SetPlaying(bool value)
        {
            playing = value;
            ApplyPlayingState();
        }

        public void SetPlacementReady(bool value)
        {
            placementReady = value;
            ApplyPlayingState();
        }

        public void ConfigureCars()
        {
            foreach (KeyValuePair<int, ReplayCarView> pair in cars)
                ConfigureCar(pair.Key, pair.Value);
        }

        public void ConfigureCar(int driver, ReplayCarView car)
        {
            if (car == null || settings == null)
                return;

            CarEngineSound sound = car.GetComponent<CarEngineSound>();

            if (NeedsDriverTeams())
            {
                if (!loggedWaitingForTeams)
                {
                    Debug.Log("[EngineSound] waiting for driver team data before applying team filter.");
                    loggedWaitingForTeams = true;
                }

                return;
            }

            bool useEngineSound = settings.useEngineSound && UsesEngineSound(driver);
            EngineAudioProfile profile = ResolveProfile(driver);
            CarEngineSoundSettings appliedSettings = null;

            if (useEngineSound && settings.useTeamBasedEngineAudio && profile == null)
                useEngineSound = false;

            if (useEngineSound)
            {
                if (sound == null)
                    sound = car.gameObject.AddComponent<CarEngineSound>();

                appliedSettings = settings.useTeamBasedEngineAudio
                    ? settings.CloneForProfile(profile)
                    : settings;
                sound.SetVariation(PitchVariation(driver), VolumeVariation(driver));
                sound.Configure(appliedSettings);
                sound.SetPlaying(IsActive);
                engineSounds[driver] = sound;
                runtimeSettings[driver] = appliedSettings;
                LogConfig(driver, "enabled");

                if (!loggedEngineSound)
                {
                    Debug.Log(
                        $"[EngineSound] enabled fallback={settings.generateFallbackClips}, " +
                        $"volume={settings.masterVolume}, spatialBlend={settings.spatialBlend}"
                    );
                    loggedEngineSound = true;
                }
            }
            else
            {
                if (settings.useEngineSound && !loggedMissingSoundTeam)
                {
                    string team = driverTeams.TryGetValue(driver, out string value) ? value : "";
                    Debug.Log($"[EngineSound] skipped driver={driver}, team='{team}', filter='{settings.teamNameFilter}'");
                    loggedMissingSoundTeam = true;
                }

                if (sound != null)
                {
                    sound.StopAudioNow();
                    Object.Destroy(sound);
                }

                engineSounds.Remove(driver);
                runtimeSettings.Remove(driver);
                LogConfig(driver, "removed");
            }
        }

        public void EnsureCar(int driver, ReplayCarView car)
        {
            if (car == null || settings == null || !settings.useEngineSound)
                return;

            if (engineSounds.ContainsKey(driver))
                return;

            if (NeedsDriverTeams() || !UsesEngineSound(driver))
                return;

            ConfigureCar(driver, car);
        }

        public void RemoveCar(int driver)
        {
            engineSounds.Remove(driver);
            runtimeSettings.Remove(driver);
        }

        public void UpdateTelemetry(
            int driver,
            float rpm,
            float throttle,
            float speed,
            int gear,
            int brake,
            int drs)
        {
            if (engineSounds.TryGetValue(driver, out CarEngineSound sound) && sound != null)
                sound.UpdateTelemetry(rpm, throttle, speed, gear, brake, drs);
        }

        public void UpdateAudibility()
        {
            if (settings == null)
                return;

            CollectSounds();

            if (selectedDriver > 0)
            {
                ApplySelectedAudibility();
                return;
            }

            if (settings.useTeamBasedEngineAudio)
            {
                SetAllAudible();
                return;
            }

            if (settings.maxActiveCars <= 0)
                return;

            if (!TryGetListenerPosition(out Vector3 listenerPosition))
                return;

            float maxDistance = MaximumAudibleDistance();
            float maxDistanceSqr = maxDistance > 0f ? maxDistance * maxDistance : 0f;

            SortByDistance(listenerPosition);

            int fullCars = Mathf.Max(0, settings.maxActiveCars);
            int fadeCars = Mathf.Max(0, settings.fadeOutCars);
            float fadeVolume = Mathf.Clamp01(settings.fadeOutVolume);

            ApplyDistanceAudibility(listenerPosition, maxDistanceSqr, fullCars, fadeCars, fadeVolume);
            LogIfNoAudibleCars(listenerPosition, maxDistance, maxDistanceSqr, fullCars, fadeCars, fadeVolume);
        }

        public void Clear()
        {
            engineSounds.Clear();
            runtimeSettings.Clear();
            nativeProfiles.Clear();
            soundOrder.Clear();
            loggedEngineSound = false;
            loggedMissingSoundTeam = false;
            loggedNoAudioListener = false;
            loggedNoAudibleCars = false;
            loggedWaitingForTeams = false;
            loggedDriverTeams = false;
            configLogBurst = 8;
            nextConfigLogTime = 0f;
        }

        private void ApplyPlayingState()
        {
            foreach (CarEngineSound sound in engineSounds.Values)
            {
                if (sound != null)
                    sound.SetPlaying(IsActive);
            }
        }

        private void LogConfig(int driver, string action)
        {
            float now = Time.unscaledTime;
            if (configLogBurst <= 0 && now < nextConfigLogTime)
                return;

            if (configLogBurst > 0)
                configLogBurst--;

            nextConfigLogTime = now + ConfigLogInterval;

            string team = driverTeams.TryGetValue(driver, out string value) ? value : "";
            bool redBullOnly = settings != null && settings.redBullOnly;
            bool teamBased = settings != null && settings.useTeamBasedEngineAudio;
            Debug.Log(
                $"[EngineSound] driver={driver}, team='{team}', redBullOnly={redBullOnly}, teamBased={teamBased}, " +
                $"action={action}, cars={cars.Count}, configured={engineSounds.Count}"
            );
        }

        private bool NeedsDriverTeams()
        {
            if (selectedDriver > 0)
                return false;

            return settings != null &&
                settings.useEngineSound &&
                (settings.useTeamBasedEngineAudio ||
                (settings.redBullOnly && !string.IsNullOrWhiteSpace(settings.teamNameFilter))) &&
                driverTeams.Count == 0;
        }

        private bool UsesEngineSound(int driver)
        {
            if (selectedDriver > 0)
                return driver == selectedDriver;

            if (settings != null && settings.useTeamBasedEngineAudio)
                return IsSupportedTeam(driver);

            if (settings == null || !settings.redBullOnly)
                return true;

            if (string.IsNullOrWhiteSpace(settings.teamNameFilter))
                return true;

            return driverTeams.TryGetValue(driver, out string team) &&
                !string.IsNullOrWhiteSpace(team) &&
                team.IndexOf(settings.teamNameFilter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsSupportedTeam(int driver)
        {
            return driverTeams.TryGetValue(driver, out string team) &&
                (EngineAudioTeamMatcher.IsRedBull(team) ||
                EngineAudioTeamMatcher.IsMercedes(team) ||
                EngineAudioTeamMatcher.IsFerrari(team));
        }

        private EngineAudioProfile ResolveProfile(int driver)
        {
            if (settings == null || !settings.useTeamBasedEngineAudio)
                return null;

            if (nativeProfiles.TryGetValue(driver, out EngineAudioProfile profile) && profile != null)
                return profile;

            return selectedDriver == driver ? settings.redBullProfile : null;
        }

        private void CacheNativeProfiles()
        {
            nativeProfiles.Clear();

            if (settings == null || !settings.useTeamBasedEngineAudio)
                return;

            foreach (KeyValuePair<int, string> pair in driverTeams)
            {
                EngineAudioProfile profile = ResolveNativeProfile(pair.Value);
                if (profile != null)
                    nativeProfiles[pair.Key] = profile;
            }
        }

        private EngineAudioProfile ResolveNativeProfile(string teamName)
        {
            if (settings == null)
                return null;

            if (EngineAudioTeamMatcher.IsRedBull(teamName))
                return settings.redBullProfile;

            if (EngineAudioTeamMatcher.IsMercedes(teamName))
                return settings.mercedesProfile;

            if (EngineAudioTeamMatcher.IsFerrari(teamName))
                return settings.ferrariProfile;

            return null;
        }

        private static float PitchVariation(int driver)
        {
            return Mathf.Lerp(0.965f, 1.035f, Stable01(driver, 17));
        }

        private static float VolumeVariation(int driver)
        {
            return Mathf.Lerp(0.85f, 1.1f, Stable01(driver, 43));
        }

        private static float Stable01(int driver, int salt)
        {
            unchecked
            {
                int hash = driver * 73856093 ^ salt * 19349663;
                hash ^= hash >> 13;
                hash *= 1274126177;
                return (hash & 0x7fffffff) / (float)int.MaxValue;
            }
        }

        private void CollectSounds()
        {
            soundOrder.Clear();

            foreach (CarEngineSound sound in engineSounds.Values)
            {
                if (sound != null)
                    soundOrder.Add(sound);
            }
        }

        private void ApplySelectedAudibility()
        {
            foreach (CarEngineSound sound in soundOrder)
                sound.SetAudibility(IsSelectedSound(sound) ? 1f : 0f);
        }

        private void SetAllAudible()
        {
            foreach (CarEngineSound sound in soundOrder)
                sound.SetAudibility(1f);
        }

        private bool TryGetListenerPosition(out Vector3 listenerPosition)
        {
            AudioListener listener = Object.FindAnyObjectByType<AudioListener>();
            if (listener != null)
            {
                listenerPosition = listener.transform.position;
                return true;
            }

            if (Camera.main != null)
            {
                listenerPosition = Camera.main.transform.position;
                return true;
            }

            listenerPosition = default;
            if (!loggedNoAudioListener)
            {
                Debug.LogWarning("[EngineSound] no AudioListener or MainCamera found; audio LOD cannot enable cars.");
                loggedNoAudioListener = true;
            }

            return false;
        }

        private float MaximumAudibleDistance()
        {
            return settings.maximumAudibleDistance > 0f
                ? settings.maximumAudibleDistance
                : settings.maxDistance;
        }

        private void SortByDistance(Vector3 listenerPosition)
        {
            soundOrder.Sort((a, b) =>
            {
                float distanceA = Vector3.SqrMagnitude(a.transform.position - listenerPosition);
                float distanceB = Vector3.SqrMagnitude(b.transform.position - listenerPosition);
                return distanceA.CompareTo(distanceB);
            });
        }

        private void ApplyDistanceAudibility(
            Vector3 listenerPosition,
            float maxDistanceSqr,
            int fullCars,
            int fadeCars,
            float fadeVolume)
        {
            for (int i = 0; i < soundOrder.Count; i++)
            {
                bool inRange = IsSoundInRange(soundOrder[i], listenerPosition, maxDistanceSqr);
                soundOrder[i].SetAudibility(inRange ? AudibilityForRank(i, fullCars, fadeCars, fadeVolume) : 0f);
            }
        }

        private void LogIfNoAudibleCars(
            Vector3 listenerPosition,
            float maxDistance,
            float maxDistanceSqr,
            int fullCars,
            int fadeCars,
            float fadeVolume)
        {
            if (loggedNoAudibleCars || soundOrder.Count <= 0)
                return;

            int audibleCount = 0;

            for (int i = 0; i < soundOrder.Count; i++)
            {
                if (IsSoundInRange(soundOrder[i], listenerPosition, maxDistanceSqr) &&
                    AudibilityForRank(i, fullCars, fadeCars, fadeVolume) > 0f)
                    audibleCount++;
            }

            if (audibleCount == 0)
            {
                float nearest = Vector3.Distance(soundOrder[0].transform.position, listenerPosition);
                Debug.LogWarning($"[EngineSound] no audible cars. nearest={nearest:0.00}m, maxDistance={maxDistance:0.00}m, maxActiveCars={settings.maxActiveCars}");
                loggedNoAudibleCars = true;
            }
        }

        private static bool IsSoundInRange(CarEngineSound sound, Vector3 listenerPosition, float maxDistanceSqr)
        {
            return maxDistanceSqr <= 0f ||
                Vector3.SqrMagnitude(sound.transform.position - listenerPosition) <= maxDistanceSqr;
        }

        private bool IsSelectedSound(CarEngineSound sound)
        {
            return sound != null &&
                cars.TryGetValue(selectedDriver, out ReplayCarView selectedCar) &&
                selectedCar != null &&
                sound.transform == selectedCar.transform;
        }

        private static float AudibilityForRank(int rank, int fullCars, int fadeCars, float fadeVolume)
        {
            if (rank < fullCars)
                return 1f;

            if (fadeCars <= 0 || rank >= fullCars + fadeCars)
                return 0f;

            int fadeRank = rank - fullCars;
            float fade01 = 1f - (fadeRank + 1f) / (fadeCars + 1f);
            return fadeVolume * fade01;
        }

        private int CountTeamMatches()
        {
            int matched = 0;

            foreach (KeyValuePair<int, string> pair in driverTeams)
            {
                if (settings != null && settings.useTeamBasedEngineAudio)
                {
                    if (IsSupportedTeam(pair.Key))
                        matched++;
                }
                else if (!string.IsNullOrWhiteSpace(pair.Value) &&
                    settings != null &&
                    !string.IsNullOrWhiteSpace(settings.teamNameFilter) &&
                    pair.Value.IndexOf(settings.teamNameFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    matched++;
                }
            }

            return matched;
        }
    }
}
