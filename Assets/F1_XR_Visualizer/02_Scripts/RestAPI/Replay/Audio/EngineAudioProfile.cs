using System;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    [CreateAssetMenu(fileName = "EngineAudioProfile", menuName = "F1 XR/Engine Audio Profile")]
    public class EngineAudioProfile : ScriptableObject
    {
        public string profileId;
        public string displayName;
        public string[] teamAliases;

        public AudioClip lowOn;
        public AudioClip lowOff;
        public AudioClip midOn;
        public AudioClip midOff;
        public AudioClip highOn;
        public AudioClip highOff;

        public float lowOnGain = 1f;
        public float lowOffGain = 0.75f;
        public float midOnGain = 1f;
        public float midOffGain = 0.8f;
        public float highOnGain = 1f;
        public float highOffGain = 0.85f;

        public float lowOnBaseRpm = 6600f;
        public float lowOffBaseRpm = 6500f;
        public float midOnBaseRpm = 9900f;
        public float midOffBaseRpm = 10200f;
        public float highOnBaseRpm = 11800f;
        public float highOffBaseRpm = 11800f;

        public bool MatchesTeam(string teamName)
        {
            return EngineAudioTeamMatcher.MatchesAny(teamName, teamAliases);
        }

        public void ApplyTo(CarEngineSoundSettings settings)
        {
            if (settings == null)
                return;

            settings.mode = EngineAudioMode.SampleLoop;

            ApplySample(settings.lowOn, lowOn, EngineLoadType.OnLoad, lowOnBaseRpm, lowOnGain);
            ApplySample(settings.lowOff, lowOff, EngineLoadType.OffLoad, lowOffBaseRpm, lowOffGain);
            ApplySample(settings.midOn, midOn, EngineLoadType.OnLoad, midOnBaseRpm, midOnGain);
            ApplySample(settings.midOff, midOff, EngineLoadType.OffLoad, midOffBaseRpm, midOffGain);
            ApplySample(settings.highOn, highOn, EngineLoadType.OnLoad, highOnBaseRpm, highOnGain);
            ApplySample(settings.highOff, highOff, EngineLoadType.OffLoad, highOffBaseRpm, highOffGain);

            settings.lowOnLoop = lowOn;
            settings.lowOffLoop = lowOff;
            settings.midOnLoop = midOn;
            settings.midOffLoop = midOff;
            settings.highOnLoop = highOn;
            settings.highOffLoop = highOff;

            settings.idle.clip = null;
            settings.idleLoop = null;
            settings.veryHighOn.clip = null;
            settings.veryHighOff.clip = null;
            settings.veryHighOnLoop = null;
            settings.veryHighOffLoop = null;
            settings.maxRpmLoop = null;
        }

        private static void ApplySample(EngineLoopSample sample, AudioClip clip, EngineLoadType loadType, float baseRpm, float gain)
        {
            if (sample == null)
                return;

            sample.clip = clip;
            sample.baseRpm = baseRpm;
            sample.loadType = loadType;
            sample.isLoop = true;
            sample.gain = gain;
        }
    }

    public static class EngineAudioTeamMatcher
    {
        private static readonly string[] RedBullAliases =
        {
            "Red Bull Racing",
            "Red Bull",
            "Oracle Red Bull Racing"
        };

        private static readonly string[] MercedesAliases =
        {
            "Mercedes",
            "Mercedes-AMG",
            "Mercedes-AMG Petronas F1 Team"
        };

        private static readonly string[] FerrariAliases =
        {
            "Ferrari",
            "Scuderia Ferrari",
            "Scuderia Ferrari HP"
        };

        public static bool IsRedBull(string teamName)
        {
            return MatchesAny(teamName, RedBullAliases);
        }

        public static bool IsMercedes(string teamName)
        {
            return MatchesAny(teamName, MercedesAliases);
        }

        public static bool IsFerrari(string teamName)
        {
            return MatchesAny(teamName, FerrariAliases);
        }

        public static bool MatchesAny(string teamName, string[] aliases)
        {
            string normalizedTeam = Normalize(teamName);
            if (string.IsNullOrEmpty(normalizedTeam) || aliases == null)
                return false;

            foreach (string alias in aliases)
            {
                if (string.Equals(normalizedTeam, Normalize(alias), StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        public static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            Span<char> buffer = value.Length <= 128 ? stackalloc char[value.Length] : new char[value.Length];
            int count = 0;

            foreach (char c in value)
            {
                if (!char.IsLetterOrDigit(c))
                    continue;

                buffer[count] = char.ToLowerInvariant(c);
                count++;
            }

            return new string(buffer.Slice(0, count));
        }
    }
}
