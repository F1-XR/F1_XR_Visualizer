using UnityEngine;

public class RainArea : MonoBehaviour
{
    [Header("Particle Systems")]
    public ParticleSystem rainMain;
    public ParticleSystem rainSplash;
    public ParticleSystem rainRipple;

    [Header("Intensity Mapping")]
    [Tooltip("Emission rate at full intensity")]
    public float maxEmissionRate = 80f;

    ParticleSystem.EmissionModule _emMain;
    bool _initialized;

    void Awake()
    {
        if (rainMain)
        {
            _emMain = rainMain.emission;
            _initialized = true;
        }
    }

    public void SetIntensity(float t)
    {
        if (!_initialized) return;

        bool active = t > 0.01f;

        if (rainMain)
        {
            if (active && !rainMain.isPlaying) rainMain.Play(true);
            else if (!active && rainMain.isPlaying) rainMain.Stop(true);
            _emMain.rateOverTime = t * maxEmissionRate;
        }

        if (rainSplash) rainSplash.gameObject.SetActive(active);
        if (rainRipple) rainRipple.gameObject.SetActive(active);
    }
}
