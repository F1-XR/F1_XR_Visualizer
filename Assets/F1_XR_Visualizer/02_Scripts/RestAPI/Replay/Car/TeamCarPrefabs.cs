using System;
using System.Collections.Generic;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    [Serializable]
    public struct TeamCarPrefab
    {
        public string teamName;
        public GameObject prefab;
    }

    public class TeamCarPrefabs
    {
        private readonly GameObject fallbackPrefab;
        private readonly Dictionary<string, GameObject> prefabs = new();

        public TeamCarPrefabs(GameObject fallbackPrefab)
        {
            this.fallbackPrefab = fallbackPrefab;
        }

        public void SetPrefabs(TeamCarPrefab[] entries)
        {
            prefabs.Clear();

            if (entries == null)
                return;

            foreach (TeamCarPrefab entry in entries)
                Register(entry);
        }

        public GameObject Resolve(string teamName)
        {
            return TryFind(teamName, out GameObject prefab)
                ? prefab
                : fallbackPrefab;
        }

        private void Register(TeamCarPrefab entry)
        {
            if (entry.prefab == null)
                return;

            string teamKey = Normalize(EntryName(entry));
            if (!string.IsNullOrEmpty(teamKey))
                prefabs[teamKey] = entry.prefab;
        }

        private bool TryFind(string teamName, out GameObject prefab)
        {
            prefab = null;

            string teamKey = Normalize(teamName);
            if (string.IsNullOrEmpty(teamKey))
                return false;

            if (prefabs.TryGetValue(teamKey, out prefab) && prefab != null)
                return true;

            foreach (KeyValuePair<string, GameObject> pair in prefabs)
            {
                if (pair.Value == null)
                    continue;

                if (KeysMatch(teamKey, pair.Key))
                {
                    prefab = pair.Value;
                    return true;
                }
            }

            return false;
        }

        private static string EntryName(TeamCarPrefab entry)
        {
            return string.IsNullOrWhiteSpace(entry.teamName)
                ? entry.prefab.name
                : entry.teamName;
        }

        private static bool KeysMatch(string driverTeamKey, string prefabTeamKey)
        {
            return driverTeamKey.Contains(prefabTeamKey) ||
                prefabTeamKey.Contains(driverTeamKey);
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            string teamName = value.Trim();

            if (teamName.StartsWith("F1_", StringComparison.OrdinalIgnoreCase))
                teamName = teamName.Substring(3);

            if (teamName.EndsWith("_Lowpoly", StringComparison.OrdinalIgnoreCase))
                teamName = teamName.Substring(0, teamName.Length - "_Lowpoly".Length);

            string result = "";
            foreach (char character in teamName)
            {
                if (char.IsLetterOrDigit(character))
                    result += char.ToLowerInvariant(character);
            }

            return result;
        }
    }
}
