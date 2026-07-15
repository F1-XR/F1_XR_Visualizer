using System.Collections.Generic;
using F1XR.RestAPI.Api;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public class DriverRoster
    {
        private readonly Dictionary<int, Color> colors = new();
        private readonly Dictionary<int, string> labels = new();
        private readonly Dictionary<int, string> teams = new();
        private readonly Dictionary<int, DriverInfoDto> drivers = new();

        public IReadOnlyDictionary<int, string> Teams => teams;

        public void SetDrivers(DriverInfoDto[] drivers)
        {
            colors.Clear();
            labels.Clear();
            teams.Clear();
            this.drivers.Clear();

            foreach (DriverInfoDto driver in drivers)
                AddDriver(driver);
        }

        public bool TryGetColor(int driver, out Color color)
        {
            return colors.TryGetValue(driver, out color);
        }

        public bool TryGetLabel(int driver, out string label)
        {
            return labels.TryGetValue(driver, out label);
        }

        public bool TryGetTeam(int driver, out string team)
        {
            return teams.TryGetValue(driver, out team);
        }

        public string GetLabel(int driver)
        {
            if (!drivers.TryGetValue(driver, out DriverInfoDto info) ||
                string.IsNullOrWhiteSpace(info.nameAcronym))
            {
                return $"#{driver}";
            }

            return info.nameAcronym;
        }

        public DriverInfoDto GetInfo(int driver)
        {
            drivers.TryGetValue(driver, out DriverInfoDto info);
            return info;
        }

        public Color GetColor(int driver)
        {
            return colors.TryGetValue(driver, out Color color)
                ? color
                : new Color(0.25f, 0.28f, 0.34f);
        }

        private void AddDriver(DriverInfoDto driver)
        {
            if (driver == null)
                return;

            drivers[driver.driverNumber] = driver;
            labels[driver.driverNumber] = string.IsNullOrWhiteSpace(driver.nameAcronym)
                ? driver.driverNumber.ToString()
                : driver.nameAcronym;
            teams[driver.driverNumber] = driver.teamName;

            if (string.IsNullOrWhiteSpace(driver.teamColour))
                return;

            if (ColorUtility.TryParseHtmlString("#" + driver.teamColour, out Color color))
                colors[driver.driverNumber] = color;
        }
    }
}
