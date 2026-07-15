using System;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public sealed class EngineSoundSettingsSnapshot
    {
        private bool redBullOnly;
        private bool useTeamBasedEngineAudio;
        private bool enableNewGridStartAudio;
        private string teamNameFilter;
        private EngineAudioProfile redBullProfile;
        private EngineAudioProfile mercedesProfile;
        private EngineAudioProfile ferrariProfile;
        private AudioClip redBullGridStartClip;

        public bool HasChanged(
            CarEngineSoundSettings settings,
            out bool modeChanged)
        {
            modeChanged =
                settings != null &&
                useTeamBasedEngineAudio !=
                settings.useTeamBasedEngineAudio;

            if (settings == null)
                return false;

            return redBullOnly != settings.redBullOnly ||
                useTeamBasedEngineAudio !=
                    settings.useTeamBasedEngineAudio ||
                enableNewGridStartAudio !=
                    settings.enableNewGridStartAudio ||
                redBullProfile != settings.redBullProfile ||
                mercedesProfile != settings.mercedesProfile ||
                ferrariProfile != settings.ferrariProfile ||
                redBullGridStartClip !=
                    settings.redBullGridStartClip ||
                !string.Equals(
                    teamNameFilter,
                    settings.teamNameFilter,
                    StringComparison.Ordinal);
        }

        public void Capture(CarEngineSoundSettings settings)
        {
            redBullOnly =
                settings != null && settings.redBullOnly;

            useTeamBasedEngineAudio =
                settings != null &&
                settings.useTeamBasedEngineAudio;

            enableNewGridStartAudio =
                settings != null &&
                settings.enableNewGridStartAudio;

            teamNameFilter =
                settings != null
                    ? settings.teamNameFilter
                    : null;

            redBullProfile =
                settings != null
                    ? settings.redBullProfile
                    : null;

            mercedesProfile =
                settings != null
                    ? settings.mercedesProfile
                    : null;

            ferrariProfile =
                settings != null
                    ? settings.ferrariProfile
                    : null;

            redBullGridStartClip =
                settings != null
                    ? settings.redBullGridStartClip
                    : null;
        }
    }
}