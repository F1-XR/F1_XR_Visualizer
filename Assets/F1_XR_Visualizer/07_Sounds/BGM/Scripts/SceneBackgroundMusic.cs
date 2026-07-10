using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace F1XR
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class SceneBackgroundMusic : MonoBehaviour
    {
        private const string DefaultMusicClipPath = "Assets/F1_XR_Visualizer/07_Sounds/BGM/LoseMyMind.wav";

        [SerializeField] private AudioClip musicClip;
        [SerializeField, Range(0f, 1f)] private float targetVolume = 0.1f;
        [SerializeField, Min(0f)] private float fadeInDuration = 2f;
        [SerializeField, Min(0f)] private float fadeOutDuration = 1f;
        [SerializeField] private bool loop = false;
        [SerializeField] private bool playOnStart = true;

        private static readonly Dictionary<string, SceneBackgroundMusic> ActiveByScene = new Dictionary<string, SceneBackgroundMusic>();

        private AudioSource source;
        private Coroutine fadeRoutine;
        private string sceneName;
        private bool isDuplicate;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (musicClip == null)
            {
                AssetDatabase.ImportAsset(DefaultMusicClipPath);
                musicClip = AssetDatabase.LoadAssetAtPath<AudioClip>(DefaultMusicClipPath);
                EditorUtility.SetDirty(this);
            }

            AudioSource audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
                return;

            audioSource.playOnAwake = false;
            audioSource.clip = musicClip;
            audioSource.loop = loop;
            audioSource.volume = 0f;
            audioSource.spatialBlend = 0f;
            audioSource.dopplerLevel = 0f;
            audioSource.spatialize = false;
            audioSource.spatializePostEffects = false;
            audioSource.panStereo = 0f;
            audioSource.spread = 0f;
            audioSource.reverbZoneMix = 0f;

            EditorUtility.SetDirty(audioSource);
        }
#endif

        private void Awake()
        {
            sceneName = gameObject.scene.name;
            source = GetComponent<AudioSource>();
            ConfigureSource();

            if (ActiveByScene.TryGetValue(sceneName, out SceneBackgroundMusic active) && active != null && active != this)
            {
                isDuplicate = true;
                enabled = false;
                return;
            }

            ActiveByScene[sceneName] = this;
        }

        private void Start()
        {
            if (playOnStart)
                PlayFromBeginning();
        }

        private void OnDisable()
        {
            if (isDuplicate)
                return;

            StopNow();
            Unregister();
        }

        private void OnDestroy()
        {
            if (isDuplicate)
                return;

            StopNow();
            Unregister();
        }

        public void PlayFromBeginning()
        {
            if (isDuplicate || musicClip == null)
                return;

            if (fadeRoutine != null)
                StopCoroutine(fadeRoutine);

            ConfigureSource();
            source.volume = 0f;
            source.time = 0f;
            source.Play();

            fadeRoutine = StartCoroutine(FadeVolume(targetVolume, fadeInDuration, false));
        }

        public void StopWithFade()
        {
            if (source == null || !source.isPlaying)
                return;

            if (fadeRoutine != null)
                StopCoroutine(fadeRoutine);

            fadeRoutine = StartCoroutine(FadeVolume(0f, fadeOutDuration, true));
        }

        private void ConfigureSource()
        {
            source.playOnAwake = false;
            source.clip = musicClip;
            source.loop = loop;
            source.volume = 0f;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.spatialize = false;
            source.spatializePostEffects = false;
            source.panStereo = 0f;
            source.spread = 0f;
            source.reverbZoneMix = 0f;
        }

        private IEnumerator FadeVolume(float volume, float duration, bool stopWhenDone)
        {
            float startVolume = source.volume;
            float elapsed = 0f;

            if (duration <= 0f)
            {
                source.volume = volume;
                if (stopWhenDone)
                    source.Stop();
                yield break;
            }

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                source.volume = Mathf.Lerp(startVolume, volume, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            source.volume = volume;
            if (stopWhenDone)
                source.Stop();
        }

        private void StopNow()
        {
            if (fadeRoutine != null)
            {
                StopCoroutine(fadeRoutine);
                fadeRoutine = null;
            }

            if (source == null)
                return;

            source.volume = 0f;
            source.Stop();
        }

        private void Unregister()
        {
            if (string.IsNullOrEmpty(sceneName))
                return;

            if (ActiveByScene.TryGetValue(sceneName, out SceneBackgroundMusic active) && active == this)
                ActiveByScene.Remove(sceneName);
        }
    }
}
