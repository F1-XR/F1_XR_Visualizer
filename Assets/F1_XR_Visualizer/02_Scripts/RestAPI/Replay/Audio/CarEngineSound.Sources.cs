using System;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public partial class CarEngineSound
    {
        private void EnsureSources()
        {
            EnsureAudioObject();

            EnsureSource(ref shiftSource, "ShiftOneShot", false);
            EnsureSource(ref idleSource, "EngineIdle", true);
            EnsureSource(ref lowOnSource, "LowOn", true);
            EnsureSource(ref lowOffSource, "LowOff", true);
            EnsureSource(ref midOnSource, "MidOn", true);
            EnsureSource(ref midOffSource, "MidOff", true);
            EnsureSource(ref highOnSource, "HighOn", true);
            EnsureSource(ref highOffSource, "HighOff", true);
            EnsureSource(ref veryHighOnSource, "VeryHighOn", true);
            EnsureSource(ref veryHighOffSource, "VeryHighOff", true);
            EnsureSource(ref gearboxSource, "Gearbox", true);
            EnsureSource(ref proceduralSource, "Procedural", true);
        }

        private void EnsureAudioObject()
        {
            if (audioObject != null)
                return;

            Transform existing = transform.Find(AudioRootName);
            if (existing != null)
            {
                audioObject = existing.gameObject;
                return;
            }

            audioObject = new GameObject(AudioRootName);
            audioObject.transform.SetParent(transform, false);
            UpdateAudioObjectPosition();
        }

        private void UpdateAudioObjectPosition()
        {
            if (audioObject == null)
                return;

            audioObject.transform.localPosition = Vector3.zero;
            audioObject.transform.localRotation = Quaternion.identity;
        }

        private void EnsureSource(ref AudioSource source, string sourceName, bool loop)
        {
            if (source != null)
                return;

            Transform child = audioObject.transform.Find(sourceName);
            GameObject sourceObject = child != null ? child.gameObject : new GameObject(sourceName);
            sourceObject.transform.SetParent(audioObject.transform, false);

            source = sourceObject.GetComponent<AudioSource>();
            if (source == null)
                source = sourceObject.AddComponent<AudioSource>();

            source.playOnAwake = false;
            source.loop = loop;
            source.volume = 0f;
        }

        private void ApplySourceSettings()
        {
            ApplySourceSettings(shiftSource);
            ApplySourceSettings(idleSource);
            ApplySourceSettings(lowOnSource);
            ApplySourceSettings(lowOffSource);
            ApplySourceSettings(midOnSource);
            ApplySourceSettings(midOffSource);
            ApplySourceSettings(highOnSource);
            ApplySourceSettings(highOffSource);
            ApplySourceSettings(veryHighOnSource);
            ApplySourceSettings(veryHighOffSource);
            ApplySourceSettings(gearboxSource);
            ApplySourceSettings(proceduralSource);
        }

        private void ApplySourceSettings(AudioSource source)
        {
            if (source == null)
                return;

            float maxDistance = settings.maximumAudibleDistance > 0f
                ? settings.maximumAudibleDistance
                : settings.maxDistance;

            source.spatialBlend = Mathf.Clamp01(settings.spatialBlend);
            source.minDistance = Mathf.Max(0.01f, settings.minDistance);
            source.maxDistance = Mathf.Max(source.minDistance, maxDistance);
            source.rolloffMode = AudioRolloffMode.Custom;
            source.dopplerLevel = settings.enableCustomDoppler ? 0f : 0.1f;
            source.priority = Mathf.Clamp(settings.priority, 0, 256);
            source.SetCustomCurve(
                AudioSourceCurveType.CustomRolloff,
                new AnimationCurve(
                    new Keyframe(0f, 1f),
                    new Keyframe(source.minDistance, 1f),
                    new Keyframe(source.maxDistance, 0f)
                )
            );
        }

        private AudioClip ResolveClip(AudioClip clip, Func<AudioClip> fallback)
        {
            if (clip != null)
                return clip;

            return settings.generateFallbackClips ? fallback() : null;
        }

        private static AudioClip FirstClip(params AudioClip[] clips)
        {
            foreach (AudioClip clip in clips)
            {
                if (clip != null)
                    return clip;
            }

            return null;
        }

        private static void PlayLoop(AudioSource source)
        {
            if (source == null || source.clip == null || source.isPlaying)
                return;

            source.Play();
        }

        private void EnsureConfiguredSourcesPlaying()
        {
            if (settings == null || !settings.useEngineSound || !isActiveAndEnabled)
                return;

            if (!gameObject.activeInHierarchy || audioObject == null || !audioObject.activeInHierarchy)
                return;

            if (settings.mode == EngineAudioMode.Procedural)
            {
                EnsureSourcePlaying("Procedural", proceduralSource);
                return;
            }

            EnsureSourcePlaying("EngineIdle", idleSource);
            EnsureSourcePlaying("LowOn", lowOnSource);
            EnsureSourcePlaying("LowOff", lowOffSource);
            EnsureSourcePlaying("MidOn", midOnSource);
            EnsureSourcePlaying("MidOff", midOffSource);
            EnsureSourcePlaying("HighOn", highOnSource);
            EnsureSourcePlaying("HighOff", highOffSource);
            EnsureSourcePlaying("VeryHighOn", veryHighOnSource);
            EnsureSourcePlaying("VeryHighOff", veryHighOffSource);
            EnsureSourcePlaying("Gearbox", gearboxSource);
        }

        private void EnsureSourcePlaying(string label, AudioSource source)
        {
            if (source == null)
                return;

            bool hadClip = source.clip != null;
            bool wasPlaying = source.isPlaying;
            if (hadClip && source.enabled && !wasPlaying)
            {
                LogSourceRecovery(label, source, "restart");
                PlayLoop(source);
            }
            else if (hadClip && !source.enabled)
            {
                LogSourceRecovery(label, source, "blocked");
            }
        }

        private void LogSourceRecovery(string label, AudioSource source, string action)
        {
            float now = Time.unscaledTime;
            if (sourceRecoveryLogBurst <= 0 && now < nextSourceRecoveryLogTime)
                return;

            if (sourceRecoveryLogBurst > 0)
                sourceRecoveryLogBurst--;

            nextSourceRecoveryLogTime = now + SourceRecoveryLogInterval;

            bool audioActive = audioObject != null && audioObject.activeInHierarchy;
            Debug.Log(
                $"[EngineSound] source {action} name={label}, " +
                $"gameObjectActive={gameObject.activeInHierarchy}, audioObjectActive={audioActive}, " +
                $"sourceEnabled={source.enabled}, hasClip={source.clip != null}, isPlaying={source.isPlaying}, " +
                $"playing={playing}, audible={audible}"
            );
        }

        private void StopAll()
        {
            Stop(shiftSource);
            Stop(idleSource);
            Stop(lowOnSource);
            Stop(lowOffSource);
            Stop(midOnSource);
            Stop(midOffSource);
            Stop(highOnSource);
            Stop(highOffSource);
            Stop(veryHighOnSource);
            Stop(veryHighOffSource);
            Stop(gearboxSource);
            Stop(proceduralSource);
        }

        private void MuteSampleLoops(float responseValue)
        {
            SetVolume(idleSource, 0f, responseValue);
            SetVolume(lowOnSource, 0f, responseValue);
            SetVolume(lowOffSource, 0f, responseValue);
            SetVolume(midOnSource, 0f, responseValue);
            SetVolume(midOffSource, 0f, responseValue);
            SetVolume(highOnSource, 0f, responseValue);
            SetVolume(highOffSource, 0f, responseValue);
            SetVolume(veryHighOnSource, 0f, responseValue);
            SetVolume(veryHighOffSource, 0f, responseValue);
            SetVolume(gearboxSource, 0f, responseValue);
        }

        private static void Stop(AudioSource source)
        {
            if (source != null)
                source.Stop();
        }

        private static void SetPitch(AudioSource source, float pitch, float response)
        {
            if (source == null)
                return;

            float target = Mathf.Clamp(pitch, 0.2f, 3f);
            source.pitch = Mathf.Lerp(source.pitch, target, response);
        }

        private static void SetVolume(AudioSource source, float target, float response)
        {
            if (source != null)
                source.volume = Mathf.Lerp(source.volume, Mathf.Clamp01(target), response);
        }
    }
}
