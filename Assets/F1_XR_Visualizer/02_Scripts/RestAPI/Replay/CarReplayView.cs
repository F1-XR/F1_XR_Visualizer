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
    public class CarReplayView
    {
        private const bool SnapCarsToTrackSurface = true;
        private const float GroundProbeHeight = 100f;
        private const float GroundProbeDistance = 220f;
        private const float MinGroundOffset = 0.005f;
        private const float MaxSnapDelta = 0.08f;
        private const float MaxTiltDegrees = 35f;
        private const float PositionSnapLerp = 0.45f;
        private const float RotationSnapLerp = 0.35f;

        private readonly GameObject carPrefab;
        private readonly Dictionary<int, CarAgent> cars = new();
        
        private bool hasOrigin;
        private Vector3 origin;
        private ARPlanePlacementController placement;
        private TrackCalibration calibration;
        private ARBuildRevealPlacer buildPlacer;
        private bool labelsVisible = true;
        
        private readonly Dictionary<int, Quaternion> baseRotations = new();
        private readonly Dictionary<int, Color> driverColors = new();
        private readonly Dictionary<int, string> driverLabels = new();
        private readonly Dictionary<int, string> driverTeams = new();
        private readonly Dictionary<int, Vector3> snappedPositions = new();
        private readonly Dictionary<int, Quaternion> snappedRotations = new();
        private readonly HashSet<Transform> colliderReadyRoots = new();
        private readonly Dictionary<int, CarEngineSound> engineSounds = new();
        private readonly List<CarEngineSound> soundOrder = new();
        private CarEngineSoundSettings engineSoundSettings;
        private bool soundPlaying = true;
        private bool soundPlacementReady;
        private int selectedDriverNumber;
        private bool loggedEngineSound;
        private bool loggedMissingSoundTeam;
        private bool loggedNoAudioListener;
        private bool loggedNoAudibleCars;
        private bool loggedWaitingForTeams;
        private bool loggedDriverTeams;

        public CarReplayView(GameObject carPrefab)
        {
            this.carPrefab = carPrefab;
        }

        public bool HasCars => cars.Count > 0;

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

                if (!cars.TryGetValue(driver, out CarAgent car) || car == null)
                    car = CreateCar(driver);

                EnsureEngineSound(driver, car);

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
            foreach (CarAgent car in cars.Values)
            {
                if (car != null)
                    Object.Destroy(car.gameObject);
            }

            cars.Clear();
            baseRotations.Clear();
            snappedPositions.Clear();
            snappedRotations.Clear();
            engineSounds.Clear();
            hasOrigin = false;
            origin = Vector3.zero;
            loggedEngineSound = false;
            loggedMissingSoundTeam = false;
            loggedNoAudioListener = false;
            loggedNoAudibleCars = false;
            loggedWaitingForTeams = false;
            loggedDriverTeams = false;
        }

        public void SetSelectedDriver(int driverNumber)
        {
            selectedDriverNumber = driverNumber;

            foreach (KeyValuePair<int, CarAgent> pair in cars)
            {
                if (pair.Value != null)
                    pair.Value.SetSelected(pair.Key == selectedDriverNumber, SelectionColor(pair.Key));
            }
        }

        private CarAgent CreateCar(int driver)
        {
            GameObject obj;

            if (carPrefab != null)
            {
                obj = Object.Instantiate(carPrefab);
            }
            else
            {
                obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obj.transform.localScale = new Vector3(0.6f, 0.3f, 1.0f);
            }

            CarAgent car = obj.GetComponent<CarAgent>();
            if (car == null)
                car = obj.AddComponent<CarAgent>();

            car.Init(driver);
            car.SetLabelVisible(labelsVisible);

            if (driverLabels.TryGetValue(driver, out string label))
                car.SetLabel(label);

            if (driverColors.TryGetValue(driver, out Color color))
                car.SetColor(color);

            car.SetSelected(driver == selectedDriverNumber, SelectionColor(driver));

            baseRotations.Add(driver, obj.transform.rotation);
            cars.Add(driver, car);
            ConfigureEngineSound(driver, car);

            return car;
        }

        private Color SelectionColor(int driver)
        {
            return driverColors.TryGetValue(driver, out Color color)
                ? color
                : new Color(0.25f, 0.28f, 0.34f);
        }

        public void SetPlacement(ARPlanePlacementController source)
        {
            placement = source;
        }

        public void SetBuildPlacer(ARBuildRevealPlacer source)
        {
            buildPlacer = source;
        }

        public void SetLabelsVisible(bool visible)
        {
            labelsVisible = visible;

            foreach (CarAgent car in cars.Values)
            {
                if (car != null)
                    car.SetLabelVisible(visible);
            }
        }

        public void SetEngineSound(CarEngineSoundSettings settings)
        {
            engineSoundSettings = settings ?? new CarEngineSoundSettings();

            foreach (KeyValuePair<int, CarAgent> pair in cars)
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
        }

        public void SetCalibration(TrackCalibration source)
        {
            calibration = source;
            if (calibration != null)
                calibration.ResetRuntimeHeightOrigin();

            hasOrigin = false;
            origin = Vector3.zero;
        }

        private void MoveCar(CarAgent car, LocationSample a, LocationSample b, float time)
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

            if (hasDirection && SnapCarsToTrackSurface && TrySnapToTrackSurface(car, worldPosition, worldTrackRotation, baseRotation, out var snappedPosition, out var snappedRotation))
            {
                SmoothSnap(car.driverNumber, snappedPosition, snappedRotation, worldPosition, worldRotation, out worldPosition, out worldRotation);
            }
            else
            {
                snappedPositions.Remove(car.driverNumber);
                snappedRotations.Remove(car.driverNumber);
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

        private void ConfigureEngineSound(int driver, CarAgent car)
        {
            if (car == null || engineSoundSettings == null)
                return;

            CarEngineSound sound = car.GetComponent<CarEngineSound>();

            if (engineSoundSettings.useEngineSound && UsesEngineSound(driver))
            {
                if (sound == null)
                    sound = car.gameObject.AddComponent<CarEngineSound>();

                sound.SetVariation(EnginePitchVariation(driver), EngineVolumeVariation(driver));
                sound.Configure(engineSoundSettings);
                sound.SetPlaying(soundPlaying && soundPlacementReady);
                engineSounds[driver] = sound;

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
                if (engineSoundSettings.useEngineSound && !HasDriverTeams())
                {
                    if (!loggedWaitingForTeams)
                    {
                        Debug.Log("[EngineSound] waiting for driver team data before applying team filter.");
                        loggedWaitingForTeams = true;
                    }

                    return;
                }

                if (engineSoundSettings.useEngineSound && !loggedMissingSoundTeam)
                {
                    string team = driverTeams.TryGetValue(driver, out string value) ? value : "";
                    Debug.Log($"[EngineSound] skipped driver={driver}, team='{team}', filter='{engineSoundSettings.teamNameFilter}'");
                    loggedMissingSoundTeam = true;
                }

                if (sound != null)
                    Object.Destroy(sound);

                engineSounds.Remove(driver);
            }
        }

        private void EnsureEngineSound(int driver, CarAgent car)
        {
            if (car == null || engineSoundSettings == null || !engineSoundSettings.useEngineSound)
                return;

            if (engineSounds.ContainsKey(driver))
                return;

            if (!UsesEngineSound(driver))
                return;

            ConfigureEngineSound(driver, car);
        }

        private bool UsesEngineSound(int driver)
        {
            if (engineSoundSettings == null || !engineSoundSettings.redBullOnly)
                return true;

            if (string.IsNullOrWhiteSpace(engineSoundSettings.teamNameFilter))
                return true;

            return driverTeams.TryGetValue(driver, out string team)
                && !string.IsNullOrWhiteSpace(team)
                && team.IndexOf(engineSoundSettings.teamNameFilter, StringComparison.OrdinalIgnoreCase) >= 0;
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

        private void UpdateEngineSound(CarAgent car, LocationSample a, LocationSample b, float u, float duration)
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
            if (engineSoundSettings == null || engineSoundSettings.maxActiveCars <= 0)
                return;

            Vector3 listenerPosition;
            AudioListener listener = Object.FindAnyObjectByType<AudioListener>();
            if (listener != null)
                listenerPosition = listener.transform.position;
            else if (Camera.main != null)
                listenerPosition = Camera.main.transform.position;
            else
            {
                if (!loggedNoAudioListener)
                {
                    Debug.LogWarning("[EngineSound] no AudioListener or MainCamera found; audio LOD cannot enable cars.");
                    loggedNoAudioListener = true;
                }

                return;
            }

            float maxDistance = engineSoundSettings.maximumAudibleDistance > 0f
                ? engineSoundSettings.maximumAudibleDistance
                : engineSoundSettings.maxDistance;
            float maxDistanceSqr = maxDistance > 0f ? maxDistance * maxDistance : 0f;
            soundOrder.Clear();

            foreach (CarEngineSound sound in engineSounds.Values)
            {
                if (sound != null)
                    soundOrder.Add(sound);
            }

            soundOrder.Sort((a, b) =>
            {
                float distanceA = Vector3.SqrMagnitude(a.transform.position - listenerPosition);
                float distanceB = Vector3.SqrMagnitude(b.transform.position - listenerPosition);
                return distanceA.CompareTo(distanceB);
            });

            int fullCars = Mathf.Max(0, engineSoundSettings.maxActiveCars);
            int fadeCars = Mathf.Max(0, engineSoundSettings.fadeOutCars);
            float fadeVolume = Mathf.Clamp01(engineSoundSettings.fadeOutVolume);

            for (int i = 0; i < soundOrder.Count; i++)
            {
                bool inRange = maxDistanceSqr <= 0f
                    || Vector3.SqrMagnitude(soundOrder[i].transform.position - listenerPosition) <= maxDistanceSqr;
                soundOrder[i].SetAudibility(inRange ? AudibilityForRank(i, fullCars, fadeCars, fadeVolume) : 0f);
            }

            if (!loggedNoAudibleCars && soundOrder.Count > 0)
            {
                int audibleCount = 0;

                for (int i = 0; i < soundOrder.Count; i++)
                {
                    bool inRange = maxDistanceSqr <= 0f
                        || Vector3.SqrMagnitude(soundOrder[i].transform.position - listenerPosition) <= maxDistanceSqr;
                    if (inRange && AudibilityForRank(i, fullCars, fadeCars, fadeVolume) > 0f)
                        audibleCount++;
                }

                if (audibleCount == 0)
                {
                    float nearest = Vector3.Distance(soundOrder[0].transform.position, listenerPosition);
                    Debug.LogWarning($"[EngineSound] no audible cars. nearest={nearest:0.00}m, maxDistance={maxDistance:0.00}m, maxActiveCars={engineSoundSettings.maxActiveCars}");
                    loggedNoAudibleCars = true;
                }
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
            Vector3 fallbackPosition,
            Quaternion fallbackRotation,
            out Vector3 position,
            out Quaternion rotation
        )
        {
            if (snappedPositions.TryGetValue(driverNumber, out Vector3 previousPosition))
            {
                float snapDelta = Vector3.Distance(previousPosition, snappedPosition);
                if (snapDelta > MaxSnapDelta)
                {
                    position = Vector3.Lerp(previousPosition, fallbackPosition, PositionSnapLerp);
                    rotation = snappedRotations.TryGetValue(driverNumber, out Quaternion previousRotation)
                        ? Quaternion.Slerp(previousRotation, fallbackRotation, RotationSnapLerp)
                        : fallbackRotation;

                    snappedPositions[driverNumber] = position;
                    snappedRotations[driverNumber] = rotation;
                    return;
                }

                position = Vector3.Lerp(previousPosition, snappedPosition, PositionSnapLerp);
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

        private bool TrySnapToTrackSurface(CarAgent car, Vector3 worldPosition, Quaternion trackRotation, Quaternion baseRotation, out Vector3 snappedPosition, out Quaternion snappedRotation)
        {
            snappedPosition = worldPosition;
            snappedRotation = trackRotation * baseRotation;

            Vector3 up = trackRotation * Vector3.up;
            Vector3 forward = Vector3.ProjectOnPlane(trackRotation * Vector3.forward, up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(trackRotation * Vector3.right, up).normalized;

            if (forward.sqrMagnitude <= 0.000001f || right.sqrMagnitude <= 0.000001f)
                return false;

            GetCarFootprint(car, forward, right, out float halfLength, out float halfWidth, out float groundOffset);

            Vector3 frontLeft = worldPosition + forward * halfLength - right * halfWidth;
            Vector3 frontRight = worldPosition + forward * halfLength + right * halfWidth;
            Vector3 rearLeft = worldPosition - forward * halfLength - right * halfWidth;
            Vector3 rearRight = worldPosition - forward * halfLength + right * halfWidth;

            if (!TryRaycastTrack(car, frontLeft, up, out Vector3 hitFrontLeft))
                return false;
            if (!TryRaycastTrack(car, frontRight, up, out Vector3 hitFrontRight))
                return false;
            if (!TryRaycastTrack(car, rearLeft, up, out Vector3 hitRearLeft))
                return false;
            if (!TryRaycastTrack(car, rearRight, up, out Vector3 hitRearRight))
                return false;

            Vector3 frontCenter = (hitFrontLeft + hitFrontRight) * 0.5f;
            Vector3 rearCenter = (hitRearLeft + hitRearRight) * 0.5f;
            Vector3 rightCenter = (hitFrontRight + hitRearRight) * 0.5f;
            Vector3 leftCenter = (hitFrontLeft + hitRearLeft) * 0.5f;
            Vector3 surfaceForward = frontCenter - rearCenter;
            Vector3 surfaceRight = rightCenter - leftCenter;
            Vector3 normal = Vector3.Cross(surfaceForward, surfaceRight);

            if (normal.sqrMagnitude <= 0.000001f)
                return false;

            normal.Normalize();
            if (Vector3.Dot(normal, up) < 0f)
                normal = -normal;
            if (Vector3.Angle(up, normal) > MaxTiltDegrees)
                return false;

            Vector3 projectedForward = Vector3.ProjectOnPlane(forward, normal);
            if (projectedForward.sqrMagnitude <= 0.000001f)
                projectedForward = Vector3.ProjectOnPlane(surfaceForward, normal);
            if (projectedForward.sqrMagnitude <= 0.000001f)
                return false;

            Vector3 hitCenter = (hitFrontLeft + hitFrontRight + hitRearLeft + hitRearRight) * 0.25f;
            snappedPosition = hitCenter + normal * Mathf.Max(MinGroundOffset, groundOffset);
            snappedRotation = Quaternion.LookRotation(projectedForward.normalized, normal) * baseRotation;
            return true;
        }

        private bool TryRaycastTrack(CarAgent car, Vector3 origin, Vector3 up, out Vector3 hitPoint)
        {
            hitPoint = default;

            Vector3 rayOrigin = origin + up * GroundProbeHeight;
            RaycastHit[] hits = Physics.RaycastAll(rayOrigin, -up, GroundProbeDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float bestMeshDistance = float.MaxValue;
            float bestFallbackDistance = float.MaxValue;
            RaycastHit? bestMeshHit = null;
            RaycastHit? bestFallbackHit = null;

            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null || IsIgnoredGroundHit(car, hit.collider))
                    continue;

                if (hit.collider is MeshCollider)
                {
                    if (hit.distance < bestMeshDistance)
                    {
                        bestMeshDistance = hit.distance;
                        bestMeshHit = hit;
                    }
                }
                else if (hit.distance < bestFallbackDistance)
                {
                    bestFallbackDistance = hit.distance;
                    bestFallbackHit = hit;
                }
            }

            RaycastHit? bestHit = bestMeshHit ?? bestFallbackHit;
            if (!bestHit.HasValue)
                return false;

            hitPoint = bestHit.Value.point;
            return true;
        }

        private static bool IsIgnoredGroundHit(CarAgent car, Collider collider)
        {
            if (collider.transform.IsChildOf(car.transform))
                return true;

            CarAgent hitCar = collider.GetComponentInParent<CarAgent>();
            return hitCar != null;
        }

        private void GetCarFootprint(CarAgent car, Vector3 forward, Vector3 right, out float halfLength, out float halfWidth, out float groundOffset)
        {
            halfLength = 0.02f;
            halfWidth = 0.01f;
            groundOffset = MinGroundOffset;

            Renderer[] renderers = car.GetComponentsInChildren<Renderer>();
            bool found = false;
            float minForward = 0f;
            float maxForward = 0f;
            float minRight = 0f;
            float maxRight = 0f;
            float minUp = 0f;
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
                        minUp = upValue;
                        found = true;
                    }
                    else
                    {
                        minForward = Mathf.Min(minForward, forwardValue);
                        maxForward = Mathf.Max(maxForward, forwardValue);
                        minRight = Mathf.Min(minRight, rightValue);
                        maxRight = Mathf.Max(maxRight, rightValue);
                        minUp = Mathf.Min(minUp, upValue);
                    }
                }
            }

            if (!found)
                return;

            halfLength = Mathf.Max(halfLength, (maxForward - minForward) * 0.35f);
            halfWidth = Mathf.Max(halfWidth, (maxRight - minRight) * 0.35f);
            groundOffset = Mathf.Max(MinGroundOffset, -minUp + MinGroundOffset);
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
            return renderer != null
                && renderer.enabled
                && !renderer.name.StartsWith("DriverLabel")
                && !renderer.name.StartsWith("SelectedCar");
        }

        private void EnsureTrackSurfaceColliders(Transform root)
        {
            if (root == null || colliderReadyRoots.Contains(root))
                return;

            MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
            foreach (MeshFilter meshFilter in meshFilters)
            {
                if (meshFilter.sharedMesh == null || meshFilter.GetComponent<MeshCollider>() != null)
                    continue;

                if (meshFilter.GetComponentInParent<CarAgent>() != null)
                    continue;

                MeshRenderer renderer = meshFilter.GetComponent<MeshRenderer>();
                if (renderer == null || !renderer.enabled)
                    continue;

                MeshCollider collider = meshFilter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = meshFilter.sharedMesh;
            }

            colliderReadyRoots.Add(root);
        }

        private static void SetCarParent(CarAgent car, Transform parent)
        {
            if (car.transform.parent == parent)
                return;

            car.transform.SetParent(parent, worldPositionStays: false);
        }
        
        public void SetDrivers(DriverInfoDto[] drivers)
        {
            if (drivers == null)
                return;

            driverColors.Clear();
            driverLabels.Clear();
            driverTeams.Clear();

            foreach (DriverInfoDto driver in drivers)
            {
                driverLabels[driver.driverNumber] = string.IsNullOrWhiteSpace(driver.nameAcronym)
                    ? driver.driverNumber.ToString()
                    : driver.nameAcronym;
                driverTeams[driver.driverNumber] = driver.teamName;

                if (string.IsNullOrWhiteSpace(driver.teamColour))
                    continue;

                if (ColorUtility.TryParseHtmlString("#" + driver.teamColour, out Color color))
                    driverColors[driver.driverNumber] = color;
            }

            if (!loggedDriverTeams)
            {
                int matched = 0;
                foreach (KeyValuePair<int, string> pair in driverTeams)
                {
                    if (!string.IsNullOrWhiteSpace(pair.Value) &&
                        engineSoundSettings != null &&
                        !string.IsNullOrWhiteSpace(engineSoundSettings.teamNameFilter) &&
                        pair.Value.IndexOf(engineSoundSettings.teamNameFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        matched++;
                    }
                }

                Debug.Log($"[EngineSound] driver teams loaded. count={driverTeams.Count}, filter='{engineSoundSettings?.teamNameFilter}', matches={matched}");
                loggedDriverTeams = true;
            }

            foreach (KeyValuePair<int, CarAgent> pair in cars)
                ConfigureEngineSound(pair.Key, pair.Value);
        }
    }
}
