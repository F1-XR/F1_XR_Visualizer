using System;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public enum EngineAudioMode
    {
        Procedural,
        SampleLoop
    }

    public enum EngineLoadType
    {
        Idle,
        OnLoad,
        OffLoad
    }

    public enum EngineAudioPerspective
    {
        Exterior,
        Onboard
    }

    [Serializable]
    public class EngineLoopSample
    {
        public AudioClip clip;
        public float baseRpm = 10000f;
        public EngineLoadType loadType = EngineLoadType.OnLoad;
        public bool isLoop = true;
        public float minimumPitch = 0.82f;
        public float maximumPitch = 1.15f;
        public float gain = 1f;
        public EngineAudioPerspective perspective = EngineAudioPerspective.Exterior;

        public EngineLoopSample()
        {
        }

        public EngineLoopSample(float baseRpm, EngineLoadType loadType, float gain = 1f)
        {
            this.baseRpm = baseRpm;
            this.loadType = loadType;
            this.gain = gain;
        }
    }
}