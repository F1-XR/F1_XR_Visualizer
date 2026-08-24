using UnityEngine;

public class RainArea : MonoBehaviour
{
    [Header("Particle Systems")]
    public ParticleSystem rainMain;
    public ParticleSystem rainFine;
    public ParticleSystem rainSplash;
    public ParticleSystem rainRipple;

    [Header("Intensity Mapping")]
    [Tooltip("Emission rate at full intensity")]
    public float maxEmissionRate = 80f;
    [Tooltip("Fine-drizzle emission rate at full intensity")]
    public float maxFineEmissionRate = 160f;

    ParticleSystem.EmissionModule _emMain;
    ParticleSystem.EmissionModule _emFine;
    bool _initialized;

    void Awake() => Init();

    void Init()
    {
        if (_initialized || rainMain == null) return;
        _emMain = rainMain.emission;
        if (rainFine) _emFine = rainFine.emission;
        _initialized = true;
    }

    public void SetIntensity(float t)
    {
        Init();
        if (!_initialized) return;

        bool active = t > 0.01f;

        Drive(rainMain, ref _emMain, active, t * maxEmissionRate);
        if (rainFine) Drive(rainFine, ref _emFine, active, t * maxFineEmissionRate);

        if (rainSplash) rainSplash.gameObject.SetActive(active);
        if (rainRipple) rainRipple.gameObject.SetActive(active);
    }

    static void Drive(ParticleSystem ps, ref ParticleSystem.EmissionModule em, bool active, float rate)
    {
        if (ps == null) return;
        if (active && !ps.isPlaying) ps.Play(true);
        else if (!active && ps.isPlaying) ps.Stop(true);
        em.rateOverTime = rate;
    }
}
