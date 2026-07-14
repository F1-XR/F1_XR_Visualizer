using System.Collections.Generic;
using F1XR.AR;
using F1XR.RestAPI.Api;
using UnityEngine;
using F1XR.RestAPI.AR;

namespace F1XR.RestAPI.Replay
{
    public class ReplayCarSet
    {
        private readonly CarInstances carInstances;
        private readonly ReplayCarMotion carMotion;
        private readonly ReplayGridStartAudio gridStartAudio = new();
        private readonly DriverRoster driverRoster = new();
        private readonly CarPresentation carPresentation;
        private readonly CarAudio carAudio;
        private int selectedDriverNumber;

        public ReplayCarSet(GameObject carPrefab)
        {
            TeamCarPrefabs teamCarPrefabs = new TeamCarPrefabs(carPrefab);
            carInstances = new CarInstances(teamCarPrefabs, driverRoster);
            carMotion = new ReplayCarMotion(carInstances);
            carPresentation = new CarPresentation(carInstances.Cars, driverRoster);
            carAudio = new CarAudio(carInstances.Cars, driverRoster.Teams);
        }

        public bool HasCars => carInstances.HasCars;

        public void SetTeamPrefabs(TeamCarPrefab[] prefabs)
        {
            carInstances.SetTeamPrefabs(
                prefabs,
                RemoveCarState,
                SetupCar);
        }

        public bool TryGetCarTransform(int driverNumber, out Transform carTransform)
        {
            return carInstances.TryGetTransform(driverNumber, out carTransform);
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

                ReplayCarView car = carInstances.GetOrCreate(driver, SetupCar);

                carAudio.EnsureCar(driver, car);
                if (ranks.TryGetValue(driver, out int rank))
                    carPresentation.SetRank(car, rank);

                int index = indices[driver];
                index = Mathf.Clamp(index, 0, list.Count - 2);

                while (index > 0 && list[index].t > time)
                    index--;

                while (index < list.Count - 2 && list[index + 1].t < time)
                    index++;

                indices[driver] = index;

                carMotion.Move(
                    car,
                    list[index],
                    list[index + 1],
                    time,
                    out float interpolation,
                    out float duration);
                UpdateEngineSound(
                    car,
                    list[index],
                    list[index + 1],
                    interpolation,
                    duration);
                carPresentation.UpdateCar(driver, car);
            }

            carAudio.UpdateAudibility();
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

        public void SetCalibration(TrackCalibration source)
        {
            carMotion.SetCalibration(source);
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
            carInstances.RefreshPrefabs(RemoveCarState, SetupCar);
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
    }
}
