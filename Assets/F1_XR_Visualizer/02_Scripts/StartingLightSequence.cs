using System.Collections.Generic;
using UnityEngine;

namespace F1XR
{
    public sealed class StartingLightSequence : MonoBehaviour
    {
        [SerializeField] private Material offMaterial;
        [SerializeField] private Material onMaterial;
        [SerializeField] private AudioClip stepSound;
        [SerializeField] private float volume = 1f;

        private readonly List<LightStep> steps = new List<LightStep>();
        private AudioSource audioSource;
        private float lastTime = float.NaN;
        private const int StartLightCount = 5;
        private static readonly string[] LightRendererNames =
        {
            "Redt11",
            "Redt21",
            "Redt31",
            "Redt41",
            "Redt51"
        };

        private void Awake()
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
                audioSource = gameObject.AddComponent<AudioSource>();

            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 0f;

            BuildSteps();
            SetOnCount(0);
            gameObject.SetActive(false);
        }

        public void ApplyTimeline(
            float currentTime,
            float raceStartTime,
            bool isPlaying,
            float playbackSpeed,
            float leadSeconds,
            float firstLightDelay,
            float lightInterval,
            float hideDelay)
        {
            BuildSteps();

            float showTime = Mathf.Max(0f, raceStartTime - Mathf.Max(0f, leadSeconds));
            float hideTime = raceStartTime + Mathf.Max(0f, hideDelay);
            bool visible = currentTime >= showTime && currentTime < hideTime;

            if (gameObject.activeSelf != visible)
                gameObject.SetActive(visible);

            if (!visible)
            {
                SetOnCount(0);
                lastTime = currentTime;
                return;
            }

            int onCount = LightCountAt(currentTime, showTime, raceStartTime, firstLightDelay, lightInterval);
            SetOnCount(onCount);
            PlayStepSounds(currentTime, showTime, raceStartTime, firstLightDelay, lightInterval, isPlaying, playbackSpeed);
            lastTime = currentTime;
        }

        private int LightCountAt(float currentTime, float showTime, float raceStartTime, float firstLightDelay, float lightInterval)
        {
            if (currentTime >= raceStartTime)
                return 0;

            float firstLightTime = showTime + Mathf.Max(0f, firstLightDelay);
            float interval = Mathf.Max(0.01f, lightInterval);
            return Mathf.Clamp(Mathf.FloorToInt((currentTime - firstLightTime) / interval) + 1, 0, steps.Count);
        }

        private void PlayStepSounds(
            float currentTime,
            float showTime,
            float raceStartTime,
            float firstLightDelay,
            float lightInterval,
            bool isPlaying,
            float playbackSpeed)
        {
            if (!isPlaying || stepSound == null || float.IsNaN(lastTime) || currentTime < lastTime)
                return;

            audioSource.pitch = Mathf.Max(0.01f, playbackSpeed);
            float firstLightTime = showTime + Mathf.Max(0f, firstLightDelay);
            float interval = Mathf.Max(0.01f, lightInterval);

            for (int i = 0; i < StartLightCount; i++)
            {
                float stepTime = firstLightTime + interval * i;
                if (stepTime >= raceStartTime)
                    break;

                if (lastTime < stepTime && currentTime >= stepTime)
                    audioSource.PlayOneShot(stepSound, Mathf.Clamp01(volume));
            }
        }

        private void BuildSteps()
        {
            if (steps.Count > 0)
                return;

            if (offMaterial == null || onMaterial == null)
                return;

            for (int i = 0; i < StartLightCount; i++)
            {
                LightStep step = new LightStep();
                AddRendererSlots(step, LightRendererNames[i]);
                AddLight(step, $"Red Glow Light {i + 1}");
                steps.Add(step);
            }
        }

        private void AddRendererSlots(LightStep step, string objectName)
        {
            Transform target = FindChild(objectName);
            if (target == null)
            {
                Debug.LogWarning($"{name}: starting light renderer not found. object={objectName}");
                return;
            }

            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
                AddMaterialSlots(step, renderer);
        }

        private void AddMaterialSlots(LightStep step, Renderer renderer)
        {
            Material[] materials = renderer.sharedMaterials;
            bool added = false;

            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] != offMaterial && materials[i] != onMaterial)
                    continue;

                step.renderers.Add(new RendererSlot(renderer, i));
                added = true;
            }

            if (!added && materials.Length > 0)
                step.renderers.Add(new RendererSlot(renderer, 0));
        }

        private void AddLight(LightStep step, string objectName)
        {
            Transform target = FindChild(objectName);
            if (target == null)
                return;

            Light light = target.GetComponent<Light>();
            if (light != null)
                step.lights.Add(light);
        }

        private Transform FindChild(string objectName)
        {
            Transform[] children = GetComponentsInChildren<Transform>(true);
            foreach (Transform child in children)
            {
                if (child.name == objectName)
                    return child;
            }

            return null;
        }

        private void SetOnCount(int count)
        {
            for (int i = 0; i < steps.Count; i++)
            {
                if (i < count)
                    steps[i].SetOn(onMaterial);
                else
                    steps[i].SetOff(offMaterial);
            }
        }

        private sealed class LightStep
        {
            public readonly List<RendererSlot> renderers = new List<RendererSlot>();
            public readonly List<Light> lights = new List<Light>();

            public void SetOff(Material material)
            {
                SetMaterial(material);
                SetLights(false);
            }

            public void SetOn(Material material)
            {
                SetMaterial(material);
                SetLights(true);
            }

            private void SetMaterial(Material material)
            {
                for (int i = 0; i < renderers.Count; i++)
                    renderers[i].SetMaterial(material);
            }

            private void SetLights(bool enabled)
            {
                for (int i = 0; i < lights.Count; i++)
                    lights[i].enabled = enabled;
            }
        }

        private struct RendererSlot
        {
            private readonly Renderer renderer;
            private readonly int materialIndex;

            public RendererSlot(Renderer renderer, int materialIndex)
            {
                this.renderer = renderer;
                this.materialIndex = materialIndex;
            }

            public void SetMaterial(Material material)
            {
                Material[] materials = renderer.sharedMaterials;
                if (materialIndex >= materials.Length)
                    return;

                materials[materialIndex] = material;
                renderer.sharedMaterials = materials;
            }
        }
    }
}
