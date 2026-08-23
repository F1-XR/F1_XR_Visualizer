using UnityEngine;

public class WeatherCloud : MonoBehaviour
{
    [Header("Cloud Particle Systems")]
    public ParticleSystem cloudMain;
    public ParticleSystem cloudEdge;
    public ParticleSystem cloudBottom;

    [Header("Rain")]
    public RainArea rainArea;
    public bool isRaining;
    [Range(0f, 1f)]
    public float rainIntensity = 0.7f;

    [Header("Weather Appearance")]
    [Range(0f, 0.5f)]
    public float rainDarken = 0.3f;
    public float transitionSpeed = 2f;

    [Header("Bob Animation")]
    public float bobAmplitude = 0.01f;
    public float bobFrequency = 0.2f;

    float _currentIntensity;
    Vector3 _baseLocalPos;
    MaterialPropertyBlock _mpb;
    ParticleSystemRenderer[] _renderers;

    void Start()
    {
        _baseLocalPos = transform.localPosition;
        _mpb = new MaterialPropertyBlock();
        _renderers = GetComponentsInChildren<ParticleSystemRenderer>();
    }

    void Update()
    {
        float target = isRaining ? rainIntensity : 0f;
        _currentIntensity = Mathf.MoveTowards(_currentIntensity, target,
            transitionSpeed * Time.deltaTime);

        if (rainArea) rainArea.SetIntensity(_currentIntensity);
        UpdateCloudTint();
        UpdateBob();
    }

    void UpdateCloudTint()
    {
        float d = 1f - _currentIntensity * rainDarken;
        _mpb.SetColor("_BaseColor", new Color(d, d, d, 1f));
        foreach (var rr in _renderers)
            if (rr) rr.SetPropertyBlock(_mpb);
    }

    void UpdateBob()
    {
        float bob = Mathf.Sin(Time.time * bobFrequency * Mathf.PI * 2f) * bobAmplitude;
        transform.localPosition = _baseLocalPos + Vector3.up * bob;
    }

    public void SetWeather(bool rain, float intensity = 0.7f)
    {
        isRaining = rain;
        rainIntensity = Mathf.Clamp01(intensity);
    }
}
