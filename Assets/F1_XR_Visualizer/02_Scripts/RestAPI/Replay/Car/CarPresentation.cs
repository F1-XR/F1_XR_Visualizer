using System.Collections.Generic;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public class CarPresentation
    {
        private static readonly Color DefaultSelectionColor =
            new Color(0.25f, 0.28f, 0.34f);

        private readonly IReadOnlyDictionary<int, ReplayCarView> cars;
        private readonly DriverRoster driverRoster;

        private bool labelsVisible = true;
        private bool leaderHighlightVisible;
        private int selectedDriver;

        public CarPresentation(
            IReadOnlyDictionary<int, ReplayCarView> cars,
            DriverRoster driverRoster)
        {
            this.cars = cars;
            this.driverRoster = driverRoster;
        }

        public void SetupCar(int driver, ReplayCarView car)
        {
            if (car == null)
                return;

            car.SetLabelVisible(labelsVisible);
            car.SetLeaderHighlightVisible(leaderHighlightVisible);
            ApplyDriverAppearance(driver, car);
            ApplySelection(driver, car);
        }

        public void SetRank(ReplayCarView car, int rank)
        {
            if (car != null)
                car.SetRank(rank);
        }

        public void SetSelectedDriver(int driver)
        {
            selectedDriver = driver;

            foreach (KeyValuePair<int, ReplayCarView> pair in cars)
                ApplySelection(pair.Key, pair.Value);
        }

        public void SetLabelsVisible(bool visible)
        {
            if (labelsVisible == visible)
                return;

            labelsVisible = visible;

            foreach (ReplayCarView car in cars.Values)
            {
                if (car != null)
                    car.SetLabelVisible(labelsVisible);
            }
        }

        public void SetLeaderHighlightVisible(bool visible)
        {
            if (leaderHighlightVisible == visible)
                return;

            leaderHighlightVisible = visible;

            foreach (ReplayCarView car in cars.Values)
            {
                if (car != null)
                    car.SetLeaderHighlightVisible(leaderHighlightVisible);
            }
        }

        public void RefreshDriverAppearance()
        {
            foreach (KeyValuePair<int, ReplayCarView> pair in cars)
                ApplyDriverAppearance(pair.Key, pair.Value);
        }

        private void ApplyDriverAppearance(int driver, ReplayCarView car)
        {
            if (car == null)
                return;

            if (driverRoster.TryGetLabel(driver, out string label))
                car.SetLabel(label);

            if (driverRoster.TryGetColor(driver, out Color color))
                car.SetColor(color);
        }

        private void ApplySelection(int driver, ReplayCarView car)
        {
            if (car == null)
                return;

            Color color = driverRoster.TryGetColor(driver, out Color driverColor)
                ? driverColor
                : DefaultSelectionColor;
            car.SetSelected(driver == selectedDriver, color);
        }
    }
}
