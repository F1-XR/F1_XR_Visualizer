using System;
using System.Collections.Generic;
using F1XR.RestAPI.Replay.Track.Placement;
using F1XR.RestAPI.Api;
using UnityEngine;
using Unity.Profiling;
using F1XR.RestAPI.Replay.Track.Build;

namespace F1XR.RestAPI.Replay
{
    public class ReplayCarSet
    {
        private static readonly ProfilerMarker BuildFramesMarker =
            new("F1XR.Cars.BuildFrames");
        private static readonly ProfilerMarker ApplyLogicalPosesMarker =
            new("F1XR.Cars.ApplyLogicalPoses");
        private static readonly ProfilerMarker ApplyVisualsMarker =
            new("F1XR.Cars.ApplyVisuals");
        private static readonly ProfilerMarker AudioAudibilityMarker =
            new("F1XR.Cars.AudioAudibility");
        private static readonly ProfilerMarker FindCarMarker =
            new("F1XR.Cars.FindCar");
        private static readonly ProfilerMarker ResolvePoseMarker =
            new("F1XR.Cars.ResolvePose");

        private readonly CarInstances carInstances;
        private readonly ReplayCarMotion carMotion;
        private readonly ReplayGridStartAudio gridStartAudio = new();
        private readonly DriverRoster driverRoster = new();
        private readonly CarPresentation carPresentation;
        private readonly CarAudio carAudio;
        private readonly OvertakeMotion overtakeMotion = new();
        private readonly List<CarFrame> frames = new();
        private readonly Dictionary<int, ReplayCarPose> poses = new();
        private readonly Dictionary<int, float> visualWidths = new();
        private readonly Dictionary<int, float> visualLengths = new();
        private readonly Dictionary<int, int> ranks = new();
        private readonly Dictionary<int, string> debugEventByDriver = new();
        private readonly Action<int> removeCarState;
        private readonly Action<int, ReplayCarView> setupCar;
        private readonly ReplayPlayer player;
        private readonly bool allowInteraction;
        private OvertakeMotionSettings overtakeSettings = new();
        private int selectedDriverNumber;

        public ReplayCarSet(
            GameObject carPrefab,
            ReplayPlayer player,
            bool allowInteraction = true)
        {
            this.player = player;
            this.allowInteraction = allowInteraction;
            removeCarState = RemoveCarState;
            setupCar = SetupCar;
            TeamCarPrefabs teamCarPrefabs = new TeamCarPrefabs(carPrefab);
            carInstances = new CarInstances(
                teamCarPrefabs,
                driverRoster);
            carMotion = new ReplayCarMotion(carInstances);
            carPresentation = new CarPresentation(carInstances.Cars, driverRoster);
            carAudio = new CarAudio(carInstances.Cars, driverRoster.Teams);
        }

        public bool HasCars => carInstances.HasCars;

        public void SetMapScaleRatio(float ratio)
        {
            carInstances.SetMapScaleRatio(ratio);
        }

        public void SetTeamPrefabs(TeamCarPrefab[] prefabs)
        {
            carInstances.SetTeamPrefabs(
                prefabs,
                removeCarState,
                setupCar);
        }

        public bool TryGetCarTransform(int driverNumber, out Transform carTransform)
        {
            return carInstances.TryGetTransform(driverNumber, out carTransform);
        }

        public bool TryGetVisualTransform(int driverNumber, out Transform carTransform)
        {
            return carInstances.TryGetVisualTransform(driverNumber, out carTransform);
        }

        public void SetReplayEvents(ReplayEventDto[] events)
        {
            overtakeMotion.SetEvents(events);
        }

        public void SetOvertakeSettings(OvertakeMotionSettings settings)
        {
            overtakeSettings = settings ?? new OvertakeMotionSettings();
            overtakeMotion.SetSettings(overtakeSettings);
        }

        public void SetFallbackOvertakeCorridor(
            IReadOnlyList<Vector3> centerline,
            float roadWidth,
            bool loop)
        {
            overtakeMotion.SetFallbackCorridor(
                centerline,
                roadWidth,
                loop);
        }

        public void SetActualOvertakeCorridor(
            IReadOnlyList<Vector3> centerline,
            IReadOnlyList<Vector3> leftBoundary,
            IReadOnlyList<Vector3> rightBoundary,
            bool loop)
        {
            overtakeMotion.SetTrackCorridor(
                centerline,
                leftBoundary,
                rightBoundary,
                loop);
        }

        public bool TryGetResolvedOvertakeSide(
            ReplayEventDto replayEvent,
            out int side)
        {
            return overtakeMotion.TryGetResolvedPassingSide(
                replayEvent,
                out side);
        }

        public void Show(
            Dictionary<int, List<LocationSample>> samples,
            Dictionary<int, int> indices,
            float time,
            List<PositionSampleDto> positions = null,
            HashSet<int> driverFilter = null)
        {
            carMotion.PrepareMappedPositions(samples);

            Dictionary<int, int> ranks = positions != null
                ? GetRanksByDriver(positions)
                : null;
            BuildFramesMarker.Begin();
            frames.Clear();
            poses.Clear();
            visualWidths.Clear();
            visualLengths.Clear();

            foreach (KeyValuePair<int, List<LocationSample>> pair in samples)
            {
                int driver = pair.Key;
                if (driverFilter != null && !driverFilter.Contains(driver))
                    continue;

                List<LocationSample> list = pair.Value;

                if (list.Count < 2)
                    continue;

                FindCarMarker.Begin();
                ReplayCarView car = carInstances.GetOrCreate(
                    driver,
                    removeCarState,
                    setupCar);
                FindCarMarker.End();

                carAudio.EnsureCar(driver, car);
                if (ranks != null && ranks.TryGetValue(driver, out int rank))
                    carPresentation.SetRank(car, rank);

                int index = indices[driver];
                index = Mathf.Clamp(index, 0, list.Count - 2);

                while (index > 0 && list[index].t > time)
                    index--;

                while (index < list.Count - 2 && list[index + 1].t < time)
                    index++;

                indices[driver] = index;

                ResolvePoseMarker.Begin();
                carMotion.ResolvePose(
                    car,
                    list[Mathf.Max(0, index - 1)],
                    list[index],
                    list[index + 1],
                    list[Mathf.Min(list.Count - 1, index + 2)],
                    time,
                    out ReplayCarPose pose,
                    out float interpolation,
                    out float duration);
                ResolvePoseMarker.End();
                frames.Add(new CarFrame(
                    driver,
                    car,
                    list[index],
                    list[index + 1],
                    pose,
                    interpolation,
                    duration));
                poses[driver] = pose;
                visualWidths[driver] = car.GetVisualWidth();
                visualLengths[driver] = car.GetVisualLength();
            }
            BuildFramesMarker.End();

            ApplyLogicalPosesMarker.Begin();
            foreach (CarFrame frame in frames)
                carMotion.ApplyLogicalPose(frame.car, frame.pose);
            ApplyLogicalPosesMarker.End();

            ApplyVisualsMarker.Begin();
            foreach (CarFrame frame in frames)
            {
                VisualMotionPose visualPose = overtakeMotion.Resolve(
                    frame.driver,
                    time,
                    poses,
                    visualWidths,
                    visualLengths);
                carMotion.ApplyVisualPose(frame.car, frame.pose, visualPose);

                DrawOvertakeDebug(frame, visualPose);

                UpdateEngineSound(
                    frame.car,
                    frame.a,
                    frame.b,
                    frame.interpolation,
                    frame.duration);
            }
            ApplyVisualsMarker.End();

            AudioAudibilityMarker.Begin();
            carAudio.UpdateAudibility();
            AudioAudibilityMarker.End();
        }

        private Dictionary<int, int> GetRanksByDriver(List<PositionSampleDto> positions)
        {
            ranks.Clear();

            if (positions == null)
                return ranks;

            foreach (PositionSampleDto position in positions)
            {
                if (position == null)
                    continue;

                ranks[position.driverNumber] = position.position;
            }

            return ranks;
        }

        public void Clear()
        {
            selectedDriverNumber = 0;
            debugEventByDriver.Clear();
            carPresentation.SetSelectedDriver(0);
            ResetPlacement();
        }

        public void ResetPlacement()
        {
            carInstances.Clear();
            carMotion.Clear();
            gridStartAudio.Clear(selectedDriverNumber);
            carAudio.Clear();
        }

        public void SetSelectedDriver(int driverNumber)
        {
            selectedDriverNumber = driverNumber;

            carPresentation.SetSelectedDriver(driverNumber);
            carAudio.SetSelectedDriver(driverNumber);
        }

        private void SetupCar(int driver, ReplayCarView car)
        {
            carPresentation.SetupCar(driver, car);
            carAudio.ConfigureCar(driver, car);

            ReplayCarInteractable interaction = car.GetComponent<ReplayCarInteractable>();
            if (interaction == null && allowInteraction)
                interaction = car.gameObject.AddComponent<ReplayCarInteractable>();

            if (interaction != null)
            {
                interaction.enabled = allowInteraction;
                if (allowInteraction)
                {
                    interaction.Configure(car, player);
                    interaction.enabled = player == null || !player.IsTrackEditMode;
                }
            }
        }

        private void RemoveCarState(int driver)
        {
            carMotion.RemoveCar(driver);
            carAudio.RemoveCar(driver);
            gridStartAudio.RemoveCar(driver);
        }

        public void SetPlacement(ARPlanePlacementController source)
        {
            carMotion.SetPlacement(source);
        }

        public void SetBuildPlacer(TrackRevealPlacer source)
        {
            carMotion.SetBuildPlacer(source);
        }

        public void SetLabelsVisible(bool visible)
        {
            carPresentation.SetLabelsVisible(visible);
        }

        public void SetLeaderHighlightVisible(bool visible)
        {
            carPresentation.SetLeaderHighlightVisible(visible);
        }

        public void SetEngineSound(CarEngineSoundSettings settings)
        {
            carAudio.SetSettings(settings);
            gridStartAudio.SetDrivers(
                driverRoster.Teams,
                carAudio.Settings.useTeamBasedEngineAudio);
            StopGridStartAudio();
            carAudio.ConfigureCars();
        }

        public void SetSoundPlaying(bool playing)
        {
            carAudio.SetPlaying(playing);

            if (!carAudio.IsActive)
                gridStartAudio.Pause();
        }

        public void SetSoundPlacementReady(bool ready)
        {
            carAudio.SetPlacementReady(ready);

            if (!carAudio.IsActive)
                gridStartAudio.Pause();
        }

        public void SetCalibration(
            TrackCalibration source,
            bool resetRuntimeState = true)
        {
            carMotion.SetCalibration(source, resetRuntimeState);
        }

        public void SetCustomSpace(
            Transform parent,
            Vector3 sourceOrigin,
            Quaternion sourceToLocalRotation)
        {
            carMotion.SetCustomSpace(
                parent,
                sourceOrigin,
                sourceToLocalRotation);
        }

        public bool TryGetMappedPosition(
            LocationSample sample,
            out Vector3 position)
        {
            return carMotion.TryGetMappedPosition(sample, out position);
        }

        private void UpdateEngineSound(ReplayCarView car, LocationSample a, LocationSample b, float u, float duration)
        {
            float rpm = Mathf.Lerp(a.rpm, b.rpm, u);
            float throttle = Mathf.Lerp(a.throttle, b.throttle, u);
            float speed = Mathf.Lerp(a.speed, b.speed, u);
            int gear = u < 0.5f ? Gear(a) : Gear(b);
            int brake = u < 0.5f ? a.brake : b.brake;
            int drs = u < 0.5f ? a.drs : b.drs;

            if (speed <= 0.01f)
                speed = EstimateSpeed(a, b, duration);

            carAudio.UpdateTelemetry(
                car.driverNumber,
                rpm,
                throttle,
                speed,
                gear,
                brake,
                drs);
        }

        public void ApplyGridStartTimeline(float currentReplayTime, float raceStartTime, bool isPlaying, float playbackSpeed)
        {
            gridStartAudio.Apply(
                carAudio.Settings,
                carInstances.Cars,
                selectedDriverNumber,
                carAudio.PlacementReady,
                currentReplayTime,
                raceStartTime,
                isPlaying,
                playbackSpeed);
        }

        public void StopGridStartAudio()
        {
            gridStartAudio.Stop(selectedDriverNumber);
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

        public void SetDrivers(DriverInfoDto[] drivers)
        {
            if (drivers == null || drivers.Length == 0)
                return;

            driverRoster.SetDrivers(drivers);

            carAudio.RefreshDriverData();
            gridStartAudio.SetDrivers(
                driverRoster.Teams,
                carAudio.Settings != null && carAudio.Settings.useTeamBasedEngineAudio);
            carInstances.RefreshPrefabs(removeCarState, setupCar);
            carPresentation.RefreshDriverAppearance();

            foreach (KeyValuePair<int, ReplayCarView> pair in carInstances.Cars)
                carAudio.ConfigureCar(pair.Key, pair.Value);
        }

        public string GetDriverLabel(int driverNumber)
        {
            return driverRoster.GetLabel(driverNumber);
        }

        public DriverInfoDto GetDriverInfo(int driverNumber)
        {
            return driverRoster.GetInfo(driverNumber);
        }

        public Color GetDriverColor(int driverNumber)
        {
            return driverRoster.GetColor(driverNumber);
        }

        private void DrawOvertakeDebug(
            CarFrame frame,
            VisualMotionPose visualPose)
        {
            if (!overtakeSettings.debugOvertakeVisuals)
                return;

            if (!visualPose.active)
            {
                debugEventByDriver.Remove(frame.driver);
                return;
            }

            Debug.DrawLine(
                frame.pose.worldPosition,
                frame.car.VisualMotionRoot.position,
                visualPose.lateralOffset >= 0f ? Color.cyan : Color.magenta);

            if (debugEventByDriver.TryGetValue(frame.driver, out string previous) &&
                previous == visualPose.sourceEventId)
                return;

            debugEventByDriver[frame.driver] = visualPose.sourceEventId;
            Debug.Log(
                $"[OvertakeMotion] driver={frame.driver}, role={visualPose.role}, " +
                $"event={visualPose.sourceEventId}, side={visualPose.passingSide}, " +
                $"source={visualPose.sideSource}, confidence={visualPose.confidence:0.00}, " +
                $"offset={visualPose.lateralOffset:0.000}, yaw={visualPose.localYaw:0.0}, " +
                $"logical={frame.pose.worldPosition}, visual={frame.car.VisualMotionRoot.position}");
        }

        private readonly struct CarFrame
        {
            public readonly int driver;
            public readonly ReplayCarView car;
            public readonly LocationSample a;
            public readonly LocationSample b;
            public readonly ReplayCarPose pose;
            public readonly float interpolation;
            public readonly float duration;

            public CarFrame(
                int driver,
                ReplayCarView car,
                LocationSample a,
                LocationSample b,
                ReplayCarPose pose,
                float interpolation,
                float duration)
            {
                this.driver = driver;
                this.car = car;
                this.a = a;
                this.b = b;
                this.pose = pose;
                this.interpolation = interpolation;
                this.duration = duration;
            }
        }
    }
}
