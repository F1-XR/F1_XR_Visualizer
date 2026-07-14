using System;
using System.Collections.Generic;
using F1XR.AR;
using F1XR.RestAPI.Api;
using F1XR.RestAPI.Utility;
using UnityEngine;
using F1XR.RestAPI.AR;
using Object = UnityEngine.Object;

namespace F1XR.RestAPI.Replay
{
    public class ReplayCarSet
    {
        private const bool SnapCarsToTrackSurface = false;
        private const bool DebugGroundSnap = false;
        private const float GroundProbeHeight = 100f;
        private const float GroundProbeDistance = 220f;
        private const float MinGroundOffset = 0.005f;
        private const float MaxTiltDegrees = 35f;
        private const float MinTrackSurfaceNormalDot = 0.35f;
        private const float SurfaceHeightChangeSeconds = 0.3f;
        private const float SurfaceHeightChangeBodyRatio = 1f;
        private const float SurfaceHeightSpreadBodyRatio = 1f;
        private const float SurfaceProbeBodyRatio = 1.5f;
        private const float GroundOffsetBodyRatio = 0.75f;
        private const float PositionSnapLerp = 0.45f;
        private const float RotationSnapLerp = 0.35f;
        private const float EngineConfigLogInterval = 1f;
        private const float GroundSnapDebugLogInterval = 0.5f;

        private readonly GameObject carPrefab;
        private readonly Dictionary<string, GameObject> teamPrefabs = new();
        private readonly Dictionary<int, ReplayCarView> cars = new();
        private readonly Dictionary<int, GameObject> carPrefabsByDriver = new();
        
        private bool hasOrigin;
        private Vector3 origin;
        private ARPlanePlacementController placement;
        private TrackCalibration calibration;
        private TrackRevealPlacer buildPlacer;
        private bool labelsVisible = true;
        private bool leaderHighlightVisible;
        
        private readonly Dictionary<int, Quaternion> baseRotations = new();
        private readonly Dictionary<int, Color> driverColors = new();
        private readonly Dictionary<int, string> driverLabels = new();
        private readonly Dictionary<int, string> driverTeams = new();
        private readonly Dictionary<int, Vector3> snappedPositions = new();
        private readonly Dictionary<int, Quaternion> snappedRotations = new();
        private readonly HashSet<Transform> colliderReadyRoots = new();
        private readonly HashSet<Collider> trackSurfaceColliders = new();
        private readonly Dictionary<int, Collider> lastGroundSnapColliders = new();
        private readonly Dictionary<int, float> nextGroundSnapDebugLogTimes = new();
        private readonly Dictionary<int, int> groundSnapMissCounts = new();
        private readonly Dictionary<int, CarEngineSound> engineSounds = new();
        private readonly Dictionary<int, CarEngineSoundSettings> runtimeEngineSettings = new();
        private readonly Dictionary<int, EngineAudioProfile> nativeEngineProfiles = new();
        private readonly Dictionary<int, AudioSource> gridStartSources = new();
        private readonly List<CarEngineSound> soundOrder = new();
        private readonly List<int> redBullGridStartDrivers = new();
        private CarEngineSoundSettings engineSoundSettings;
        private Transform trackSurfaceRoot;
        private bool soundPlaying = true;
        private bool soundPlacementReady;
        private int selectedDriverNumber;
        private int lastGridStartSelectedDriver = -1;
        private bool loggedEngineSound;
        private bool loggedMissingSoundTeam;
        private bool loggedNoAudioListener;
        private bool loggedNoAudibleCars;
        private bool loggedWaitingForTeams;
        private bool loggedDriverTeams;
        private int engineConfigLogBurst = 8;
        private float nextEngineConfigLogTime;

        private struct GroundHit
        {
            public Vector3 point;
            public Vector3 normal;
            public Collider collider;
            public float verticalOffset;

            public GroundHit(Vector3 point, Vector3 normal, Collider collider, float verticalOffset)
            {
                this.point = point;
                this.normal = normal;
                this.collider = collider;
                this.verticalOffset = verticalOffset;
            }
        }

        public ReplayCarSet(GameObject carPrefab)
        {
            this.carPrefab = carPrefab;
        }

        public bool HasCars => cars.Count > 0;

        public void SetTeamPrefabs(TeamCarPrefab[] prefabs)
        {
            teamPrefabs.Clear();

            if (prefabs != null)
            {
                foreach (TeamCarPrefab entry in prefabs)
                    RegisterTeamPrefab(entry);
            }

            ReplaceCarsWithTeamPrefabs();
        }

        public bool TryGetCarTransform(int driverNumber, out Transform carTransform)
        {
            carTransform = null;

            if (!cars.TryGetValue(driverNumber, out ReplayCarView car) || car == null)
                return false;

            carTransform = car.transform;
            return true;
        }

        public void Show(
            Dictionary<int, List<LocationSample>> samples,
            Dictionary<int, int> indices,
            float time,
            List<PositionSampleDto> positions = null)
        {
            Dictionary<int, int> ranks = GetRanksByDriver(positions);

            foreach (KeyValuePair<int, List<LocationSample>> pair in samples)
            {
                int driver = pair.Key;
                List<LocationSample> list = pair.Value;

                if (list.Count < 2)
                    continue;

                if (!cars.TryGetValue(driver, out ReplayCarView car) || car == null)
                    car = CreateCar(driver);

                EnsureEngineSound(driver, car);
                car.SetLeaderHighlightVisible(leaderHighlightVisible);

                if (ranks.TryGetValue(driver, out int rank))
                    car.SetRank(rank);

                int index = indices[driver];
                index = Mathf.Clamp(index, 0, list.Count - 2);

                while (index > 0 && list[index].t > time)
                    index--;

                while (index < list.Count - 2 && list[index + 1].t < time)
                    index++;

                indices[driver] = index;

                MoveCar(car, list[index], list[index + 1], time);
                car.SetSelected(driver == selectedDriverNumber, SelectionColor(driver));
            }

            UpdateSoundAudibility();
        }

        private static Dictionary<int, int> GetRanksByDriver(List<PositionSampleDto> positions)
        {
            Dictionary<int, int> result = new();

            if (positions == null)
                return result;

            foreach (PositionSampleDto position in positions)
            {
                if (position == null)
                    continue;

                result[position.driverNumber] = position.position;
            }

            return result;
        }

        public void Clear()
        {
            foreach (ReplayCarView car in cars.Values)
            {
                if (car != null)
                    Object.Destroy(car.gameObject);
            }

            cars.Clear();
            carPrefabsByDriver.Clear();
            baseRotations.Clear();
            snappedPositions.Clear();
            snappedRotations.Clear();
            ClearTrackSurfaceColliderCache();
            lastGroundSnapColliders.Clear();
            nextGroundSnapDebugLogTimes.Clear();
            groundSnapMissCounts.Clear();
            StopGridStartAudio();
            gridStartSources.Clear();
            engineSounds.Clear();
            runtimeEngineSettings.Clear();
            nativeEngineProfiles.Clear();
            hasOrigin = false;
            origin = Vector3.zero;
            loggedEngineSound = false;
            loggedMissingSoundTeam = false;
            loggedNoAudioListener = false;
            loggedNoAudibleCars = false;
            loggedWaitingForTeams = false;
            loggedDriverTeams = false;
            engineConfigLogBurst = 8;
            nextEngineConfigLogTime = 0f;
        }

        public void SetSelectedDriver(int driverNumber)
        {
            selectedDriverNumber = driverNumber;

            RefreshCarSelectionStates();
            RefreshEngineSoundSelection();
            UpdateSoundAudibility();
        }

        private ReplayCarView CreateCar(int driver)
        {
            GameObject prefab = ResolvePrefabForDriver(driver);
            GameObject obj;

            if (prefab != null)
            {
                obj = Object.Instantiate(prefab);
            }
            else
            {
                obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obj.transform.localScale = new Vector3(0.6f, 0.3f, 1.0f);
            }

            ReplayCarView car = obj.GetComponent<ReplayCarView>();
            if (car == null)
                car = obj.AddComponent<ReplayCarView>();

            car.Init(driver);
            ApplyCarPresentation(driver, car);

            carPrefabsByDriver[driver] = prefab;
            baseRotations.Add(driver, obj.transform.rotation);
            cars.Add(driver, car);
            ConfigureEngineSound(driver, car);

            return car;
        }

        private void ReplaceCarsWithTeamPrefabs()
        {
            if (cars.Count == 0)
                return;

            List<int> driversToReplace = null;

            foreach (KeyValuePair<int, ReplayCarView> pair in cars)
            {
                GameObject expectedPrefab = ResolvePrefabForDriver(pair.Key);
                carPrefabsByDriver.TryGetValue(pair.Key, out GameObject currentPrefab);

                if (expectedPrefab == currentPrefab)
                    continue;

                driversToReplace ??= new List<int>();
                driversToReplace.Add(pair.Key);
            }

            if (driversToReplace == null)
                return;

            foreach (int driver in driversToReplace)
                ReplaceCar(driver);
        }

        private void ReplaceCar(int driver)
        {
            if (!cars.TryGetValue(driver, out ReplayCarView oldCar) || oldCar == null)
                return;

            Transform oldTransform = oldCar.transform;
            Transform parent = oldTransform.parent;
            Vector3 position = oldTransform.position;
            Quaternion rotation = oldTransform.rotation;
            Vector3 rawPosition = oldCar.rawPosition;

            cars.Remove(driver);
            carPrefabsByDriver.Remove(driver);
            baseRotations.Remove(driver);
            snappedPositions.Remove(driver);
            snappedRotations.Remove(driver);
            lastGroundSnapColliders.Remove(driver);
            nextGroundSnapDebugLogTimes.Remove(driver);
            groundSnapMissCounts.Remove(driver);
            engineSounds.Remove(driver);
            runtimeEngineSettings.Remove(driver);
            nativeEngineProfiles.Remove(driver);
            StopGridStartAudio(driver);

            ReplayCarView newCar = CreateCar(driver);
            newCar.rawPosition = rawPosition;
            newCar.transform.SetParent(parent, worldPositionStays: false);
            newCar.transform.position = position;
            newCar.transform.rotation = rotation;

            Object.Destroy(oldCar.gameObject);
        }

        private void RegisterTeamPrefab(TeamCarPrefab entry)
        {
            if (entry.prefab == null)
                return;

            string teamKey = NormalizeTeamName(TeamPrefabName(entry));
            if (!string.IsNullOrEmpty(teamKey))
                teamPrefabs[teamKey] = entry.prefab;
        }

        private static string TeamPrefabName(TeamCarPrefab entry)
        {
            return string.IsNullOrWhiteSpace(entry.teamName)
                ? entry.prefab.name
                : entry.teamName;
        }

        private GameObject ResolvePrefabForDriver(int driver)
        {
            if (driverTeams.TryGetValue(driver, out string teamName) &&
                TryFindTeamPrefab(teamName, out GameObject prefab))
            {
                return prefab;
            }

            return carPrefab;
        }

        private bool TryFindTeamPrefab(string teamName, out GameObject prefab)
        {
            prefab = null;

            string teamKey = NormalizeTeamName(teamName);
            if (string.IsNullOrEmpty(teamKey))
                return false;

            if (teamPrefabs.TryGetValue(teamKey, out prefab) && prefab != null)
                return true;

            foreach (KeyValuePair<string, GameObject> pair in teamPrefabs)
            {
                if (pair.Value == null)
                    continue;

                if (TeamKeysMatch(teamKey, pair.Key))
                {
                    prefab = pair.Value;
                    return true;
                }
            }

            return false;
        }

        private static bool TeamKeysMatch(string driverTeamKey, string prefabTeamKey)
        {
            return driverTeamKey.Contains(prefabTeamKey) || prefabTeamKey.Contains(driverTeamKey);
        }

        private static string NormalizeTeamName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            string teamName = value.Trim();

            if (teamName.StartsWith("F1_", StringComparison.OrdinalIgnoreCase))
                teamName = teamName.Substring(3);

            if (teamName.EndsWith("_Lowpoly", StringComparison.OrdinalIgnoreCase))
                teamName = teamName.Substring(0, teamName.Length - "_Lowpoly".Length);

            string result = "";
            foreach (char c in teamName)
            {
                if (char.IsLetterOrDigit(c))
                    result += char.ToLowerInvariant(c);
            }

            return result;
        }

        private Color SelectionColor(int driver)
        {
            return driverColors.TryGetValue(driver, out Color color)
                ? color
                : new Color(0.25f, 0.28f, 0.34f);
        }

        public void SetPlacement(ARPlanePlacementController source)
        {
            if (placement != source)
                ClearTrackSurfaceColliderCache();

            placement = source;
        }

        public void SetBuildPlacer(TrackRevealPlacer source)
        {
            if (buildPlacer != source)
                ClearTrackSurfaceColliderCache();

            buildPlacer = source;
        }

        public void SetLabelsVisible(bool visible)
        {
            labelsVisible = visible;
            RefreshCarLabelVisibility();
        }

        public void SetLeaderHighlightVisible(bool visible)
        {
            leaderHighlightVisible = visible;
            RefreshCarLeaderHighlightVisibility();
        }

        private void ApplyCarPresentation(int driver, ReplayCarView car)
        {
            if (car == null)
                return;

            car.SetLabelVisible(labelsVisible);
            car.SetLeaderHighlightVisible(leaderHighlightVisible);
            ApplyDriverAppearance(driver, car);
            ApplySelectionState(driver, car);
        }

        private void RefreshCarSelectionStates()
        {
            foreach (KeyValuePair<int, ReplayCarView> pair in cars)
                ApplySelectionState(pair.Key, pair.Value);
        }

        private void ApplySelectionState(int driver, ReplayCarView car)
        {
            if (car != null)
                car.SetSelected(driver == selectedDriverNumber, SelectionColor(driver));
        }

        private void RefreshCarLabelVisibility()
        {
            foreach (ReplayCarView car in cars.Values)
            {
                if (car != null)
                    car.SetLabelVisible(labelsVisible);
            }
        }

        private void RefreshCarLeaderHighlightVisibility()
        {
            foreach (ReplayCarView car in cars.Values)
            {
                if (car != null)
                    car.SetLeaderHighlightVisible(leaderHighlightVisible);
            }
        }

        public void SetEngineSound(CarEngineSoundSettings settings)
        {
            engineSoundSettings = settings ?? new CarEngineSoundSettings();
            CacheNativeEngineProfiles();
            StopGridStartAudio();

            foreach (KeyValuePair<int, ReplayCarView> pair in cars)
                ConfigureEngineSound(pair.Key, pair.Value);
        }

        public void SetSoundPlaying(bool playing)
        {
            soundPlaying = playing;
            ApplySoundState();
        }

        public void SetSoundPlacementReady(bool ready)
        {
            soundPlacementReady = ready;
            ApplySoundState();
        }

        private void ApplySoundState()
        {
            bool active = soundPlaying && soundPlacementReady;

            foreach (CarEngineSound sound in engineSounds.Values)
            {
                if (sound != null)
                    sound.SetPlaying(active);
            }

            if (!active)
                PauseGridStartAudio();
        }

        private void RefreshEngineSoundSelection()
        {
            if (engineSoundSettings == null || !engineSoundSettings.useEngineSound)
                return;

            foreach (KeyValuePair<int, ReplayCarView> pair in cars)
                ConfigureEngineSound(pair.Key, pair.Value);
        }

        public void SetCalibration(TrackCalibration source)
        {
            calibration = source;
            if (calibration != null)
                calibration.ResetRuntimeHeightOrigin();

            hasOrigin = false;
            origin = Vector3.zero;
            ClearTrackSurfaceColliderCache();
            lastGroundSnapColliders.Clear();
            nextGroundSnapDebugLogTimes.Clear();
            groundSnapMissCounts.Clear();
        }

        private void MoveCar(ReplayCarView car, LocationSample a, LocationSample b, float time)
        {
            float duration = Mathf.Max(0.001f, b.t - a.t);
            float u = Mathf.Clamp01((time - a.t) / duration);

            Vector3 posA = default;
            Vector3 posB = default;
            bool useCalibration = false;

            if (calibration != null)
            {
                bool mappedA = calibration.TryMap(a, out posA);
                bool mappedB = calibration.TryMap(b, out posB);
                useCalibration = mappedA && mappedB;
            }

            if (!useCalibration)
            {
                posA = ReplayCoordinate.ToUnity(a);
                posB = ReplayCoordinate.ToUnity(b);

                if (!hasOrigin)
                {
                    origin = posA;
                    hasOrigin = true;
                }

                posA -= origin;
                posB -= origin;
            }

            Vector3 position = Vector3.Lerp(posA, posB, u);
            Transform placementTransform = buildPlacer != null && buildPlacer.HasPlacement
                ? buildPlacer.PlacementTransform
                : placement != null && placement.HasPlacement
                    ? placement.PlacementTransform
                    : null;
            Transform carParent = buildPlacer != null && buildPlacer.HasPlacement
                ? buildPlacer.CarsTransform
                : placementTransform;

            SetCarParent(car, carParent);
            if (SnapCarsToTrackSurface)
                EnsureTrackSurfaceColliders(placementTransform);

            Vector3 direction = posB - posA;
            direction.y = 0f;
            bool hasDirection = direction.sqrMagnitude > 0.000001f;
            Quaternion baseRotation = baseRotations.TryGetValue(car.driverNumber, out Quaternion rotation)
                ? rotation
                : Quaternion.identity;
            Quaternion trackRotation = hasDirection
                ? Quaternion.LookRotation(direction.normalized, Vector3.up)
                : Quaternion.identity;
            Quaternion worldTrackRotation = placementTransform != null
                ? placementTransform.rotation * trackRotation
                : trackRotation;
            Quaternion worldRotation = placementTransform != null
                ? worldTrackRotation * baseRotation
                : trackRotation * baseRotation;
            Vector3 worldPosition = placementTransform != null
                ? placementTransform.TransformPoint(position)
                : position;

            Vector3 worldUp = worldTrackRotation * Vector3.up;
            if (hasDirection && SnapCarsToTrackSurface && TrySnapToTrackSurface(car, worldPosition, worldTrackRotation, baseRotation, out var snappedPosition, out var snappedRotation, out float bodyHeight))
            {
                groundSnapMissCounts.Remove(car.driverNumber);
                SmoothSnap(car.driverNumber, snappedPosition, snappedRotation, worldUp, bodyHeight, out worldPosition, out worldRotation);
            }
            else if (hasDirection && SnapCarsToTrackSurface && TryHoldPreviousSnap(car.driverNumber, worldPosition, worldRotation, worldUp, out var heldPosition, out var heldRotation))
            {
                worldPosition = heldPosition;
                worldRotation = heldRotation;
            }
            else
            {
                snappedPositions.Remove(car.driverNumber);
                snappedRotations.Remove(car.driverNumber);
                groundSnapMissCounts.Remove(car.driverNumber);
            }

            if (placementTransform != null)
            {
                car.rawPosition = position;
                car.transform.position = worldPosition;
            }
            else
            {
                car.rawPosition = position;
                car.transform.position = worldPosition;
            }

            if (hasDirection)
                car.transform.rotation = worldRotation;

            UpdateEngineSound(car, a, b, u, duration);
        }

        private void ConfigureEngineSound(int driver, ReplayCarView car)
        {
            if (car == null || engineSoundSettings == null)
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

            bool useEngineSound = engineSoundSettings.useEngineSound && UsesEngineSound(driver);
            EngineAudioProfile profile = ResolveProfileForDriver(driver);
            CarEngineSoundSettings runtimeSettings = null;

            if (useEngineSound && engineSoundSettings.useTeamBasedEngineAudio && profile == null)
                useEngineSound = false;

            if (useEngineSound)
            {
                if (sound == null)
                    sound = car.gameObject.AddComponent<CarEngineSound>();

                runtimeSettings = engineSoundSettings.useTeamBasedEngineAudio
                    ? engineSoundSettings.CloneForProfile(profile)
                    : engineSoundSettings;
                sound.SetVariation(EnginePitchVariation(driver), EngineVolumeVariation(driver));
                sound.Configure(runtimeSettings);
                sound.SetPlaying(soundPlaying && soundPlacementReady);
                engineSounds[driver] = sound;
                runtimeEngineSettings[driver] = runtimeSettings;
                LogEngineSoundConfig(driver, "enabled");

                if (!loggedEngineSound)
                {
                    Debug.Log(
                        $"[EngineSound] enabled fallback={engineSoundSettings.generateFallbackClips}, " +
                        $"volume={engineSoundSettings.masterVolume}, spatialBlend={engineSoundSettings.spatialBlend}"
                    );
                    loggedEngineSound = true;
                }
            }
            else
            {
                if (engineSoundSettings.useEngineSound && !loggedMissingSoundTeam)
                {
                    string team = driverTeams.TryGetValue(driver, out string value) ? value : "";
                    Debug.Log($"[EngineSound] skipped driver={driver}, team='{team}', filter='{engineSoundSettings.teamNameFilter}'");
                    loggedMissingSoundTeam = true;
                }

                if (sound != null)
                {
                    sound.StopAudioNow();
                    Object.Destroy(sound);
                }

                engineSounds.Remove(driver);
                runtimeEngineSettings.Remove(driver);
                LogEngineSoundConfig(driver, "removed");
            }
        }

        private void LogEngineSoundConfig(int driver, string action)
        {
            float now = Time.unscaledTime;
            if (engineConfigLogBurst <= 0 && now < nextEngineConfigLogTime)
                return;

            if (engineConfigLogBurst > 0)
                engineConfigLogBurst--;

            nextEngineConfigLogTime = now + EngineConfigLogInterval;

            string team = driverTeams.TryGetValue(driver, out string value) ? value : "";
            bool redBullOnly = engineSoundSettings != null && engineSoundSettings.redBullOnly;
            bool teamBased = engineSoundSettings != null && engineSoundSettings.useTeamBasedEngineAudio;
            Debug.Log(
                $"[EngineSound] driver={driver}, team='{team}', redBullOnly={redBullOnly}, teamBased={teamBased}, " +
                $"action={action}, cars={cars.Count}, configured={engineSounds.Count}"
            );
        }

        private void EnsureEngineSound(int driver, ReplayCarView car)
        {
            if (car == null || engineSoundSettings == null || !engineSoundSettings.useEngineSound)
                return;

            if (engineSounds.ContainsKey(driver))
                return;

            if (NeedsDriverTeams())
                return;

            if (!UsesEngineSound(driver))
                return;

            ConfigureEngineSound(driver, car);
        }

        private bool NeedsDriverTeams()
        {
            if (selectedDriverNumber > 0)
                return false;

            return engineSoundSettings != null &&
                engineSoundSettings.useEngineSound &&
                ((engineSoundSettings.useTeamBasedEngineAudio) ||
                (engineSoundSettings.redBullOnly && !string.IsNullOrWhiteSpace(engineSoundSettings.teamNameFilter))) &&
                !HasDriverTeams();
        }

        private bool UsesEngineSound(int driver)
        {
            if (selectedDriverNumber > 0)
                return driver == selectedDriverNumber;

            if (engineSoundSettings != null && engineSoundSettings.useTeamBasedEngineAudio)
                return IsSupportedTeam(driver);

            if (engineSoundSettings == null || !engineSoundSettings.redBullOnly)
                return true;

            if (string.IsNullOrWhiteSpace(engineSoundSettings.teamNameFilter))
                return true;

            return driverTeams.TryGetValue(driver, out string team)
                && !string.IsNullOrWhiteSpace(team)
                && team.IndexOf(engineSoundSettings.teamNameFilter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsSupportedTeam(int driver)
        {
            return driverTeams.TryGetValue(driver, out string team) &&
                (EngineAudioTeamMatcher.IsRedBull(team) ||
                EngineAudioTeamMatcher.IsMercedes(team) ||
                EngineAudioTeamMatcher.IsFerrari(team));
        }

        private EngineAudioProfile ResolveProfileForDriver(int driver)
        {
            if (engineSoundSettings == null || !engineSoundSettings.useTeamBasedEngineAudio)
                return null;

            if (nativeEngineProfiles.TryGetValue(driver, out EngineAudioProfile profile) && profile != null)
                return profile;

            return selectedDriverNumber == driver ? engineSoundSettings.redBullProfile : null;
        }

        private bool HasDriverTeams()
        {
            return driverTeams.Count > 0;
        }

        private static float EnginePitchVariation(int driver)
        {
            return Mathf.Lerp(0.965f, 1.035f, Stable01(driver, 17));
        }

        private static float EngineVolumeVariation(int driver)
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

        private void UpdateEngineSound(ReplayCarView car, LocationSample a, LocationSample b, float u, float duration)
        {
            if (!engineSounds.TryGetValue(car.driverNumber, out CarEngineSound sound) || sound == null)
                return;

            float rpm = Mathf.Lerp(a.rpm, b.rpm, u);
            float throttle = Mathf.Lerp(a.throttle, b.throttle, u);
            float speed = Mathf.Lerp(a.speed, b.speed, u);
            int gear = u < 0.5f ? Gear(a) : Gear(b);
            int brake = u < 0.5f ? a.brake : b.brake;
            int drs = u < 0.5f ? a.drs : b.drs;

            if (speed <= 0.01f)
                speed = EstimateSpeed(a, b, duration);

            sound.UpdateTelemetry(rpm, throttle, speed, gear, brake, drs);
        }

        private void UpdateSoundAudibility()
        {
            if (engineSoundSettings == null)
                return;

            CollectEngineSounds();

            if (selectedDriverNumber > 0)
            {
                ApplySelectedSoundAudibility();
                return;
            }

            if (engineSoundSettings.useTeamBasedEngineAudio)
            {
                SetAllEngineSoundsAudible();
                return;
            }

            if (engineSoundSettings.maxActiveCars <= 0)
                return;

            if (!TryGetAudioListenerPosition(out Vector3 listenerPosition))
                return;

            float maxDistance = ResolveMaximumAudibleDistance();
            float maxDistanceSqr = maxDistance > 0f ? maxDistance * maxDistance : 0f;

            SortEngineSoundsByDistance(listenerPosition);

            int fullCars = Mathf.Max(0, engineSoundSettings.maxActiveCars);
            int fadeCars = Mathf.Max(0, engineSoundSettings.fadeOutCars);
            float fadeVolume = Mathf.Clamp01(engineSoundSettings.fadeOutVolume);

            ApplyDistanceSoundAudibility(listenerPosition, maxDistanceSqr, fullCars, fadeCars, fadeVolume);
            LogIfNoAudibleCars(listenerPosition, maxDistance, maxDistanceSqr, fullCars, fadeCars, fadeVolume);
        }

        private void CollectEngineSounds()
        {
            soundOrder.Clear();

            foreach (CarEngineSound sound in engineSounds.Values)
            {
                if (sound != null)
                    soundOrder.Add(sound);
            }
        }

        private void ApplySelectedSoundAudibility()
        {
            foreach (CarEngineSound sound in soundOrder)
                sound.SetAudibility(IsSelectedSound(sound) ? 1f : 0f);
        }

        private void SetAllEngineSoundsAudible()
        {
            foreach (CarEngineSound sound in soundOrder)
                sound.SetAudibility(1f);
        }

        private bool TryGetAudioListenerPosition(out Vector3 listenerPosition)
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

        private float ResolveMaximumAudibleDistance()
        {
            return engineSoundSettings.maximumAudibleDistance > 0f
                ? engineSoundSettings.maximumAudibleDistance
                : engineSoundSettings.maxDistance;
        }

        private void SortEngineSoundsByDistance(Vector3 listenerPosition)
        {
            soundOrder.Sort((a, b) =>
            {
                float distanceA = Vector3.SqrMagnitude(a.transform.position - listenerPosition);
                float distanceB = Vector3.SqrMagnitude(b.transform.position - listenerPosition);
                return distanceA.CompareTo(distanceB);
            });
        }

        private void ApplyDistanceSoundAudibility(
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
                Debug.LogWarning($"[EngineSound] no audible cars. nearest={nearest:0.00}m, maxDistance={maxDistance:0.00}m, maxActiveCars={engineSoundSettings.maxActiveCars}");
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
                cars.TryGetValue(selectedDriverNumber, out ReplayCarView selectedCar) &&
                selectedCar != null &&
                sound.transform == selectedCar.transform;
        }

        private void CacheNativeEngineProfiles()
        {
            nativeEngineProfiles.Clear();
            redBullGridStartDrivers.Clear();

            if (engineSoundSettings == null || !engineSoundSettings.useTeamBasedEngineAudio)
                return;

            foreach (KeyValuePair<int, string> pair in driverTeams)
            {
                if (EngineAudioTeamMatcher.IsRedBull(pair.Value))
                    redBullGridStartDrivers.Add(pair.Key);

                EngineAudioProfile profile = ResolveNativeProfile(pair.Value);
                if (profile != null)
                    nativeEngineProfiles[pair.Key] = profile;
            }

            redBullGridStartDrivers.Sort();
        }

        private EngineAudioProfile ResolveNativeProfile(string teamName)
        {
            if (engineSoundSettings == null)
                return null;

            if (EngineAudioTeamMatcher.IsRedBull(teamName))
                return engineSoundSettings.redBullProfile;

            if (EngineAudioTeamMatcher.IsMercedes(teamName))
                return engineSoundSettings.mercedesProfile;

            if (EngineAudioTeamMatcher.IsFerrari(teamName))
                return engineSoundSettings.ferrariProfile;

            return null;
        }

        public void ApplyGridStartTimeline(float currentReplayTime, float raceStartTime, bool isPlaying, float playbackSpeed)
        {
            if (engineSoundSettings == null ||
                !engineSoundSettings.useTeamBasedEngineAudio ||
                !engineSoundSettings.enableNewGridStartAudio ||
                engineSoundSettings.redBullGridStartClip == null ||
                !soundPlacementReady)
            {
                StopGridStartAudio();
                return;
            }

            AudioClip clip = engineSoundSettings.redBullGridStartClip;
            float clipStartTime = raceStartTime - Mathf.Max(0f, engineSoundSettings.gridStartLaunchOffsetSeconds);
            float clipLocalTime = currentReplayTime - clipStartTime;

            if (clipLocalTime < 0f || clipLocalTime >= clip.length)
            {
                StopGridStartAudio();
                return;
            }

            if (lastGridStartSelectedDriver != selectedDriverNumber)
            {
                StopGridStartAudio();
                lastGridStartSelectedDriver = selectedDriverNumber;
            }

            if (selectedDriverNumber > 0)
            {
                StopGridStartSourcesExcept(selectedDriverNumber, 0);

                if (cars.TryGetValue(selectedDriverNumber, out ReplayCarView selectedCar) && selectedCar != null)
                {
                    AudioSource source = EnsureGridStartSource(selectedDriverNumber, selectedCar);
                    SyncGridStartSource(
                        source,
                        clip,
                        clipLocalTime,
                        0f,
                        Mathf.Clamp01(engineSoundSettings.selectedStartGain),
                        isPlaying,
                        playbackSpeed);
                }

                return;
            }

            int firstDriver = redBullGridStartDrivers.Count > 0 ? redBullGridStartDrivers[0] : 0;
            int secondDriver = redBullGridStartDrivers.Count > 1 ? redBullGridStartDrivers[1] : 0;
            StopGridStartSourcesExcept(firstDriver, secondDriver);

            if (firstDriver > 0 && cars.TryGetValue(firstDriver, out ReplayCarView firstCar) && firstCar != null)
            {
                SyncGridStartSource(
                    EnsureGridStartSource(firstDriver, firstCar),
                    clip,
                    clipLocalTime,
                    0f,
                    Mathf.Clamp01(engineSoundSettings.redBullStartGainA),
                    isPlaying,
                    playbackSpeed);
            }

            if (secondDriver > 0 && cars.TryGetValue(secondDriver, out ReplayCarView secondCar) && secondCar != null)
            {
                SyncGridStartSource(
                    EnsureGridStartSource(secondDriver, secondCar),
                    clip,
                    clipLocalTime,
                    Mathf.Clamp(engineSoundSettings.redBullStartSecondDelay, 0f, 0.1f),
                    Mathf.Clamp01(engineSoundSettings.redBullStartGainB),
                    isPlaying,
                    playbackSpeed);
            }
        }

        public void StopGridStartAudio()
        {
            foreach (AudioSource source in gridStartSources.Values)
            {
                if (source != null)
                    source.Stop();
            }

            lastGridStartSelectedDriver = selectedDriverNumber;
        }

        private void StopGridStartAudio(int driver)
        {
            if (!gridStartSources.TryGetValue(driver, out AudioSource source))
                return;

            if (source != null)
                source.Stop();

            gridStartSources.Remove(driver);
        }

        private void PauseGridStartAudio()
        {
            foreach (AudioSource source in gridStartSources.Values)
            {
                if (source != null && source.isPlaying)
                    source.Pause();
            }
        }

        private void StopGridStartSourcesExcept(int firstDriver, int secondDriver)
        {
            foreach (KeyValuePair<int, AudioSource> pair in gridStartSources)
            {
                if (pair.Key == firstDriver || pair.Key == secondDriver)
                    continue;

                if (pair.Value != null)
                    pair.Value.Stop();
            }
        }

        private AudioSource EnsureGridStartSource(int driver, ReplayCarView car)
        {
            if (gridStartSources.TryGetValue(driver, out AudioSource source) && source != null)
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
            ApplyGridStartSourceSettings(source);
            gridStartSources[driver] = source;
            return source;
        }

        private void ApplyGridStartSourceSettings(AudioSource source)
        {
            if (source == null || engineSoundSettings == null)
                return;

            float maxDistance = engineSoundSettings.maximumAudibleDistance > 0f
                ? engineSoundSettings.maximumAudibleDistance
                : engineSoundSettings.maxDistance;

            source.spatialBlend = Mathf.Clamp01(engineSoundSettings.spatialBlend);
            source.minDistance = Mathf.Max(0.01f, engineSoundSettings.minDistance);
            source.maxDistance = Mathf.Max(source.minDistance, maxDistance);
            source.rolloffMode = AudioRolloffMode.Custom;
            source.dopplerLevel = 0f;
            source.priority = Mathf.Clamp(engineSoundSettings.priority, 0, 256);
            source.SetCustomCurve(
                AudioSourceCurveType.CustomRolloff,
                new AnimationCurve(
                    new Keyframe(0f, 1f),
                    new Keyframe(source.minDistance, 1f),
                    new Keyframe(source.maxDistance, 0f)
                )
            );
        }

        private static void SyncGridStartSource(
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

        private static int Gear(LocationSample sample)
        {
            return sample.nGear > 0 ? sample.nGear : sample.n_gear;
        }

        private static float EstimateSpeed(LocationSample a, LocationSample b, float duration)
        {
            Vector3 positionA = new Vector3(a.x, a.y, a.z);
            Vector3 positionB = new Vector3(b.x, b.y, b.z);
            float metersPerSecond = Vector3.Distance(positionA, positionB) / Mathf.Max(0.001f, duration);
            return Mathf.Clamp(metersPerSecond * 3.6f, 0f, 340f);
        }

        private void SmoothSnap(
            int driverNumber,
            Vector3 snappedPosition,
            Quaternion snappedRotation,
            Vector3 up,
            float bodyHeight,
            out Vector3 position,
            out Quaternion rotation
        )
        {
            if (snappedPositions.TryGetValue(driverNumber, out Vector3 previousPosition))
            {
                position = Vector3.Lerp(previousPosition, snappedPosition, PositionSnapLerp);
                position = ClampSurfaceHeightChange(previousPosition, position, up, bodyHeight);
                rotation = snappedRotations.TryGetValue(driverNumber, out Quaternion oldRotation)
                    ? Quaternion.Slerp(oldRotation, snappedRotation, RotationSnapLerp)
                    : snappedRotation;
            }
            else
            {
                position = snappedPosition;
                rotation = snappedRotation;
            }

            snappedPositions[driverNumber] = position;
            snappedRotations[driverNumber] = rotation;
        }

        private static Vector3 ClampSurfaceHeightChange(Vector3 previousPosition, Vector3 targetPosition, Vector3 up, float bodyHeight)
        {
            float heightDelta = Vector3.Dot(targetPosition - previousPosition, up);
            float maxStep = GetMaxSurfaceHeightStep(bodyHeight);
            float clampedDelta = Mathf.Clamp(heightDelta, -maxStep, maxStep);
            return targetPosition + up * (clampedDelta - heightDelta);
        }

        private static float GetMaxSurfaceHeightStep(float bodyHeight)
        {
            float deltaTime = Time.deltaTime > 0f ? Time.deltaTime : Time.unscaledDeltaTime;
            float height = Mathf.Max(MinGroundOffset, bodyHeight * SurfaceHeightChangeBodyRatio);
            return height * Mathf.Clamp01(deltaTime / SurfaceHeightChangeSeconds);
        }

        private static float GetMaxSurfaceHeightSpread(float bodyHeight)
        {
            return Mathf.Max(MinGroundOffset, bodyHeight * SurfaceHeightSpreadBodyRatio);
        }

        private static float GetMaxSurfaceProbeOffset(float bodyHeight)
        {
            return Mathf.Max(MinGroundOffset * 2f, bodyHeight * SurfaceProbeBodyRatio);
        }

        private bool TryHoldPreviousSnap(
            int driverNumber,
            Vector3 fallbackPosition,
            Quaternion fallbackRotation,
            Vector3 up,
            out Vector3 position,
            out Quaternion rotation
        )
        {
            position = fallbackPosition;
            rotation = fallbackRotation;

            if (!snappedPositions.TryGetValue(driverNumber, out Vector3 previousPosition))
                return false;

            int misses = groundSnapMissCounts.TryGetValue(driverNumber, out int oldMisses)
                ? oldMisses + 1
                : 1;
            groundSnapMissCounts[driverNumber] = misses;

            float heightDelta = Vector3.Dot(previousPosition - fallbackPosition, up);
            position = fallbackPosition + up * heightDelta;
            rotation = snappedRotations.TryGetValue(driverNumber, out Quaternion previousRotation)
                ? Quaternion.Slerp(previousRotation, fallbackRotation, RotationSnapLerp)
                : fallbackRotation;

            snappedPositions[driverNumber] = position;
            snappedRotations[driverNumber] = rotation;
            return true;
        }

        private bool TrySnapToTrackSurface(
            ReplayCarView car,
            Vector3 worldPosition,
            Quaternion trackRotation,
            Quaternion baseRotation,
            out Vector3 snappedPosition,
            out Quaternion snappedRotation,
            out float bodyHeight)
        {
            snappedPosition = worldPosition;
            snappedRotation = trackRotation * baseRotation;
            bodyHeight = MinGroundOffset;

            Vector3 up = trackRotation * Vector3.up;
            Vector3 forward = Vector3.ProjectOnPlane(trackRotation * Vector3.forward, up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(trackRotation * Vector3.right, up).normalized;

            if (forward.sqrMagnitude <= 0.000001f || right.sqrMagnitude <= 0.000001f)
                return false;

            GetCarFootprint(car, forward, right, out float halfLength, out float halfWidth, out float groundOffset, out bodyHeight);

            Vector3 frontLeft = worldPosition + forward * halfLength - right * halfWidth;
            Vector3 frontRight = worldPosition + forward * halfLength + right * halfWidth;
            Vector3 rearLeft = worldPosition - forward * halfLength - right * halfWidth;
            Vector3 rearRight = worldPosition - forward * halfLength + right * halfWidth;
            float maxSurfaceOffset = GetMaxSurfaceProbeOffset(bodyHeight);

            bool hasFrontLeft = TryRaycastTrack(car, car.driverNumber, frontLeft, up, maxSurfaceOffset, out GroundHit hitFrontLeft);
            bool hasFrontRight = TryRaycastTrack(car, car.driverNumber, frontRight, up, maxSurfaceOffset, out GroundHit hitFrontRight);
            bool hasRearLeft = TryRaycastTrack(car, car.driverNumber, rearLeft, up, maxSurfaceOffset, out GroundHit hitRearLeft);
            bool hasRearRight = TryRaycastTrack(car, car.driverNumber, rearRight, up, maxSurfaceOffset, out GroundHit hitRearRight);
            int hitCount = CountGroundHits(hasFrontLeft, hasFrontRight, hasRearLeft, hasRearRight);

            if (hitCount < 2)
            {
                LogGroundSnapReject(car.driverNumber, "too few surface hits", null, 0f);
                return false;
            }

            if (GetHitHeightSpread(up, hasFrontLeft, hitFrontLeft, hasFrontRight, hitFrontRight, hasRearLeft, hitRearLeft, hasRearRight, hitRearRight) > GetMaxSurfaceHeightSpread(bodyHeight))
            {
                LogGroundSnapReject(car.driverNumber, "mixed surface heights", FirstGroundHitCollider(hasFrontLeft, hitFrontLeft, hasFrontRight, hitFrontRight, hasRearLeft, hitRearLeft, hasRearRight, hitRearRight), 0f);
                return false;
            }

            if (!TryGetSurfaceNormal(up, hasFrontLeft, hitFrontLeft, hasFrontRight, hitFrontRight, hasRearLeft, hitRearLeft, hasRearRight, hitRearRight, out Vector3 normal))
            {
                normal = up;
            }
            else if (Vector3.Angle(up, normal) > MaxTiltDegrees)
            {
                return false;
            }

            Vector3 projectedForward = Vector3.ProjectOnPlane(forward, normal);
            if (projectedForward.sqrMagnitude <= 0.000001f)
                return false;

            Vector3 hitCenter = AverageGroundHitPoint(hasFrontLeft, hitFrontLeft, hasFrontRight, hitFrontRight, hasRearLeft, hitRearLeft, hasRearRight, hitRearRight, hitCount);
            snappedPosition = hitCenter + normal * Mathf.Max(MinGroundOffset, groundOffset);
            snappedRotation = Quaternion.LookRotation(projectedForward.normalized, normal) * baseRotation;
            LogGroundSnapAccepted(car.driverNumber, FirstGroundHit(hasFrontLeft, hitFrontLeft, hasFrontRight, hitFrontRight, hasRearLeft, hitRearLeft, hasRearRight, hitRearRight), hitCount);
            return true;
        }

        private bool TryRaycastTrack(ReplayCarView car, int driverNumber, Vector3 origin, Vector3 up, float maxSurfaceOffset, out GroundHit groundHit)
        {
            groundHit = default;

            Vector3 rayOrigin = origin + up * GroundProbeHeight;
            RaycastHit[] hits = Physics.RaycastAll(rayOrigin, -up, GroundProbeDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float bestOffset = float.MaxValue;
            RaycastHit? bestHit = null;

            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null || IsIgnoredGroundHit(car, hit.collider))
                    continue;

                if (!(hit.collider is MeshCollider))
                    continue;

                if (!IsTrackSurfaceCollider(hit.collider))
                {
                    DrawGroundSnapRay(origin, hit.point, Color.red);
                    LogGroundSnapReject(driverNumber, "unregistered collider", hit.collider, 0f);
                    continue;
                }

                if (Vector3.Dot(hit.normal, up) < MinTrackSurfaceNormalDot)
                {
                    DrawGroundSnapRay(origin, hit.point, Color.red);
                    LogGroundSnapReject(driverNumber, "surface normal rejected", hit.collider, Vector3.Dot(hit.normal, up));
                    continue;
                }

                float offset = Mathf.Abs(Vector3.Dot(hit.point - origin, up));
                if (offset > maxSurfaceOffset)
                {
                    DrawGroundSnapRay(origin, hit.point, Color.red);
                    LogGroundSnapReject(driverNumber, "surface offset rejected", hit.collider, offset);
                    continue;
                }

                if (offset < bestOffset)
                {
                    bestOffset = offset;
                    bestHit = hit;
                }
            }

            if (!bestHit.HasValue)
            {
                DrawGroundSnapRay(origin, origin - up * maxSurfaceOffset, Color.red);
                LogGroundSnapReject(driverNumber, "no valid surface", null, 0f);
                return false;
            }

            RaycastHit selected = bestHit.Value;
            groundHit = new GroundHit(selected.point, selected.normal, selected.collider, bestOffset);
            DrawGroundSnapRay(origin, selected.point, Color.green);
            return true;
        }

        private static int CountGroundHits(bool a, bool b, bool c, bool d)
        {
            int count = 0;
            if (a)
                count++;
            if (b)
                count++;
            if (c)
                count++;
            if (d)
                count++;
            return count;
        }

        private static float GetHitHeightSpread(
            Vector3 up,
            bool hasA,
            GroundHit a,
            bool hasB,
            GroundHit b,
            bool hasC,
            GroundHit c,
            bool hasD,
            GroundHit d)
        {
            float min = 0f;
            float max = 0f;
            bool found = false;

            IncludeGroundHitHeight(up, hasA, a, ref min, ref max, ref found);
            IncludeGroundHitHeight(up, hasB, b, ref min, ref max, ref found);
            IncludeGroundHitHeight(up, hasC, c, ref min, ref max, ref found);
            IncludeGroundHitHeight(up, hasD, d, ref min, ref max, ref found);

            return found ? max - min : 0f;
        }

        private static void IncludeGroundHitHeight(Vector3 up, bool hasHit, GroundHit hit, ref float min, ref float max, ref bool found)
        {
            if (!hasHit)
                return;

            float height = Vector3.Dot(hit.point, up);
            if (!found)
            {
                min = max = height;
                found = true;
                return;
            }

            min = Mathf.Min(min, height);
            max = Mathf.Max(max, height);
        }

        private static Vector3 AverageGroundHitPoint(
            bool hasA,
            GroundHit a,
            bool hasB,
            GroundHit b,
            bool hasC,
            GroundHit c,
            bool hasD,
            GroundHit d,
            int hitCount)
        {
            Vector3 sum = Vector3.zero;

            if (hasA)
                sum += a.point;
            if (hasB)
                sum += b.point;
            if (hasC)
                sum += c.point;
            if (hasD)
                sum += d.point;

            return sum / Mathf.Max(1, hitCount);
        }

        private static GroundHit FirstGroundHit(
            bool hasA,
            GroundHit a,
            bool hasB,
            GroundHit b,
            bool hasC,
            GroundHit c,
            bool hasD,
            GroundHit d)
        {
            if (hasA)
                return a;
            if (hasB)
                return b;
            if (hasC)
                return c;
            return d;
        }

        private static Collider FirstGroundHitCollider(
            bool hasA,
            GroundHit a,
            bool hasB,
            GroundHit b,
            bool hasC,
            GroundHit c,
            bool hasD,
            GroundHit d)
        {
            return FirstGroundHit(hasA, a, hasB, b, hasC, c, hasD, d).collider;
        }

        private static bool TryGetSurfaceNormal(
            Vector3 up,
            bool hasA,
            GroundHit a,
            bool hasB,
            GroundHit b,
            bool hasC,
            GroundHit c,
            bool hasD,
            GroundHit d,
            out Vector3 normal)
        {
            normal = up;

            if (hasA && hasB && hasC && TryBuildSurfaceNormal(up, a.point, b.point, c.point, out normal))
                return true;
            if (hasA && hasB && hasD && TryBuildSurfaceNormal(up, a.point, b.point, d.point, out normal))
                return true;
            if (hasA && hasC && hasD && TryBuildSurfaceNormal(up, a.point, c.point, d.point, out normal))
                return true;
            if (hasB && hasC && hasD && TryBuildSurfaceNormal(up, b.point, c.point, d.point, out normal))
                return true;

            return false;
        }

        private static bool TryBuildSurfaceNormal(Vector3 up, Vector3 a, Vector3 b, Vector3 c, out Vector3 normal)
        {
            normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude <= 0.000001f)
                return false;

            normal.Normalize();
            if (Vector3.Dot(normal, up) < 0f)
                normal = -normal;

            return true;
        }

        private static bool IsIgnoredGroundHit(ReplayCarView car, Collider collider)
        {
            if (collider.transform.IsChildOf(car.transform))
                return true;

            ReplayCarView hitCar = collider.GetComponentInParent<ReplayCarView>();
            return hitCar != null;
        }

        private void GetCarFootprint(ReplayCarView car, Vector3 forward, Vector3 right, out float halfLength, out float halfWidth, out float groundOffset, out float bodyHeight)
        {
            halfLength = 0.02f;
            halfWidth = 0.01f;
            groundOffset = MinGroundOffset;
            bodyHeight = MinGroundOffset;

            Renderer[] renderers = car.GetComponentsInChildren<Renderer>();
            bool found = false;
            float minForward = 0f;
            float maxForward = 0f;
            float minRight = 0f;
            float maxRight = 0f;
            float minUp = 0f;
            float maxUp = 0f;
            Vector3 up = Vector3.Cross(forward, right).normalized;
            float originUp = Vector3.Dot(car.transform.position, up);

            foreach (Renderer item in renderers)
            {
                if (!IsCarBodyRenderer(item))
                    continue;

                Bounds bounds = item.bounds;
                Vector3[] corners = GetBoundsCorners(bounds);
                foreach (Vector3 corner in corners)
                {
                    Vector3 offset = corner - car.transform.position;
                    float forwardValue = Vector3.Dot(offset, forward);
                    float rightValue = Vector3.Dot(offset, right);
                    float upValue = Vector3.Dot(corner, up) - originUp;

                    if (!found)
                    {
                        minForward = maxForward = forwardValue;
                        minRight = maxRight = rightValue;
                        minUp = maxUp = upValue;
                        found = true;
                    }
                    else
                    {
                        minForward = Mathf.Min(minForward, forwardValue);
                        maxForward = Mathf.Max(maxForward, forwardValue);
                        minRight = Mathf.Min(minRight, rightValue);
                        maxRight = Mathf.Max(maxRight, rightValue);
                        minUp = Mathf.Min(minUp, upValue);
                        maxUp = Mathf.Max(maxUp, upValue);
                    }
                }
            }

            if (!found)
                return;

            halfLength = Mathf.Max(halfLength, (maxForward - minForward) * 0.35f);
            halfWidth = Mathf.Max(halfWidth, (maxRight - minRight) * 0.35f);
            bodyHeight = Mathf.Max(MinGroundOffset, maxUp - minUp);
            groundOffset = Mathf.Clamp(
                -minUp + MinGroundOffset,
                MinGroundOffset,
                Mathf.Max(MinGroundOffset, bodyHeight * GroundOffsetBodyRatio)
            );
        }

        private static Vector3[] GetBoundsCorners(Bounds bounds)
        {
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            return new[]
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(min.x, min.y, max.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(min.x, max.y, max.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(max.x, max.y, max.z)
            };
        }

        private static bool IsCarBodyRenderer(Renderer renderer)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                return false;

            if (renderer is LineRenderer || renderer.GetComponent<TextMesh>() != null)
                return false;

            if (renderer.GetComponent<MeshFilter>() == null)
                return false;

            Transform current = renderer.transform;
            while (current != null)
            {
                string objectName = current.name;
                if (objectName.StartsWith("DriverLabel") ||
                    objectName.StartsWith("SelectionFx") ||
                    objectName.StartsWith("GroundRing") ||
                    objectName.StartsWith("SelectionPulse") ||
                    objectName.StartsWith("SelectedCar"))
                {
                    return false;
                }

                if (current.GetComponent<ReplayCarView>() != null)
                    break;

                current = current.parent;
            }

            return true;
        }

        private void EnsureTrackSurfaceColliders(Transform root)
        {
            if (root == null)
            {
                ClearTrackSurfaceColliderCache();
                return;
            }

            if (trackSurfaceRoot != root)
            {
                ClearTrackSurfaceColliderCache();
                trackSurfaceRoot = root;
            }

            PruneTrackSurfaceColliders(root);

            if (colliderReadyRoots.Contains(root))
                return;

            MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
            foreach (MeshFilter meshFilter in meshFilters)
            {
                if (meshFilter.sharedMesh == null)
                    continue;

                if (meshFilter.GetComponentInParent<ReplayCarView>() != null)
                    continue;

                MeshRenderer renderer = meshFilter.GetComponent<MeshRenderer>();
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;

                MeshCollider existingMeshCollider = meshFilter.GetComponent<MeshCollider>();
                if (existingMeshCollider != null)
                {
                    if (existingMeshCollider.enabled)
                        trackSurfaceColliders.Add(existingMeshCollider);

                    continue;
                }

                if (meshFilter.GetComponent<Collider>() != null)
                    continue;

                MeshCollider collider = meshFilter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = meshFilter.sharedMesh;
                trackSurfaceColliders.Add(collider);
            }

            colliderReadyRoots.Add(root);
        }

        private void ClearTrackSurfaceColliderCache()
        {
            colliderReadyRoots.Clear();
            trackSurfaceColliders.Clear();
            trackSurfaceRoot = null;
        }

        private void PruneTrackSurfaceColliders(Transform root)
        {
            trackSurfaceColliders.RemoveWhere(collider =>
                collider == null ||
                root == null ||
                !collider.transform.IsChildOf(root));
        }

        private bool IsTrackSurfaceCollider(Collider collider)
        {
            if (collider == null)
                return false;

            if (trackSurfaceRoot == null)
                return true;

            if (!collider.transform.IsChildOf(trackSurfaceRoot))
                return false;

            if (!trackSurfaceColliders.Contains(collider))
                return false;

            return true;
        }

        private void DrawGroundSnapRay(Vector3 origin, Vector3 hitPoint, Color color)
        {
            if (!DebugGroundSnap)
                return;

            Debug.DrawLine(origin, hitPoint, color, 0f, false);
        }

        private void LogGroundSnapAccepted(int driverNumber, GroundHit hit, int hitCount)
        {
            if (!DebugGroundSnap)
                return;

            bool changedCollider = !lastGroundSnapColliders.TryGetValue(driverNumber, out Collider previous) || previous != hit.collider;
            if (!changedCollider && !ShouldLogGroundSnap(driverNumber))
                return;

            lastGroundSnapColliders[driverNumber] = hit.collider;
            Debug.Log(
                $"[GroundSnap] driver={driverNumber}, hits={hitCount}, collider={ColliderName(hit.collider)}, " +
                $"height={hit.point.y:0.000}, offset={hit.verticalOffset:0.000}, changedCollider={changedCollider}"
            );
        }

        private void LogGroundSnapReject(int driverNumber, string reason, Collider collider, float value)
        {
            if (!DebugGroundSnap || !ShouldLogGroundSnap(driverNumber))
                return;

            Debug.Log(
                $"[GroundSnap] reject driver={driverNumber}, reason={reason}, " +
                $"collider={ColliderName(collider)}, value={value:0.000}"
            );
        }

        private bool ShouldLogGroundSnap(int driverNumber)
        {
            float now = Time.unscaledTime;
            if (nextGroundSnapDebugLogTimes.TryGetValue(driverNumber, out float nextTime) && now < nextTime)
                return false;

            nextGroundSnapDebugLogTimes[driverNumber] = now + GroundSnapDebugLogInterval;
            return true;
        }

        private static string ColliderName(Collider collider)
        {
            return collider != null ? collider.name : "<none>";
        }

        private static void SetCarParent(ReplayCarView car, Transform parent)
        {
            if (car.transform.parent == parent)
                return;

            car.transform.SetParent(parent, worldPositionStays: false);
        }
        
        public void SetDrivers(DriverInfoDto[] drivers)
        {
            if (drivers == null || drivers.Length == 0)
                return;

            driverColors.Clear();
            driverLabels.Clear();
            driverTeams.Clear();

            foreach (DriverInfoDto driver in drivers)
                CacheDriverInfo(driver);

            CacheNativeEngineProfiles();
            ReplaceCarsWithTeamPrefabs();

            if (!loggedDriverTeams)
            {
                int matched = CountEngineSoundTeamMatches();
                Debug.Log($"[EngineSound] driver teams loaded. count={driverTeams.Count}, filter='{engineSoundSettings?.teamNameFilter}', matches={matched}");
                loggedDriverTeams = true;
            }

            foreach (KeyValuePair<int, ReplayCarView> pair in cars)
            {
                ApplyDriverAppearance(pair.Key, pair.Value);
                ConfigureEngineSound(pair.Key, pair.Value);
            }
        }

        private void CacheDriverInfo(DriverInfoDto driver)
        {
            if (driver == null)
                return;

            driverLabels[driver.driverNumber] = string.IsNullOrWhiteSpace(driver.nameAcronym)
                ? driver.driverNumber.ToString()
                : driver.nameAcronym;
            driverTeams[driver.driverNumber] = driver.teamName;

            if (string.IsNullOrWhiteSpace(driver.teamColour))
                return;

            if (ColorUtility.TryParseHtmlString("#" + driver.teamColour, out Color color))
                driverColors[driver.driverNumber] = color;
        }

        private void ApplyDriverAppearance(int driver, ReplayCarView car)
        {
            if (car == null)
                return;

            if (driverLabels.TryGetValue(driver, out string label))
                car.SetLabel(label);

            if (driverColors.TryGetValue(driver, out Color color))
                car.SetColor(color);
        }

        private int CountEngineSoundTeamMatches()
        {
            int matched = 0;

            foreach (KeyValuePair<int, string> pair in driverTeams)
            {
                if (engineSoundSettings != null && engineSoundSettings.useTeamBasedEngineAudio)
                {
                    if (IsSupportedTeam(pair.Key))
                        matched++;
                }
                else if (!string.IsNullOrWhiteSpace(pair.Value) &&
                    engineSoundSettings != null &&
                    !string.IsNullOrWhiteSpace(engineSoundSettings.teamNameFilter) &&
                    pair.Value.IndexOf(engineSoundSettings.teamNameFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    matched++;
                }
            }

            return matched;
        }
    }
}
