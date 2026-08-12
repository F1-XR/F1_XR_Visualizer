using System;
using System.Collections.Generic;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public class CarPresentation
    {
        private static readonly Color DefaultSelectionColor =
            new Color(0.25f, 0.28f, 0.34f);
        private static readonly Color RedBullOvertakeColor =
            new Color(1.12f, 0.025f, 0.035f, 1f);

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

            bool hasColor = driverRoster.TryGetColor(
                driver,
                out Color color);
            if (hasColor)
                car.SetColor(color);

            Color overtakeColor = hasColor
                ? color
                : DefaultSelectionColor;
            Color overtakeCoreColor = Color.white;
            if (driverRoster.TryGetTeam(
                    driver,
                    out string team) &&
                IsRedBullTeam(team))
            {
                overtakeColor = RedBullOvertakeColor;
                overtakeCoreColor = RedBullOvertakeColor;
            }

            car.SetOvertakeEffectPalette(
                overtakeColor,
                overtakeCoreColor);
        }

        private static bool IsRedBullTeam(string team)
        {
            return !string.IsNullOrWhiteSpace(team) &&
                team.IndexOf(
                    "Red Bull",
                    StringComparison.OrdinalIgnoreCase) >= 0;
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
