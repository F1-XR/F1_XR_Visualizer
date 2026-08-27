using System;
using F1XR.RestAPI.Replay.Track.Build;
using UnityEngine;

public class WeatherController : MonoBehaviour
{
    public WeatherCloud[] clouds;

    [SerializeField] TrackCloudPlacer trackCloudPlacer;
    [SerializeField] bool isRaining;
    [SerializeField, Range(0f, 1f)] float rainIntensity = 0.7f;

    public bool IsRaining => isRaining;
    public event Action<bool> RainChanged;

    void Awake()
    {
        trackCloudPlacer ??= GetComponent<TrackCloudPlacer>();
        ApplyWeather();
    }

    public void SetAllWeather(bool rain, float intensity = 0.7f)
    {
        SetRaining(rain, intensity);
    }

    public void SetRaining(bool rain, float intensity = 0.7f)
    {
        bool changed = isRaining != rain;
        isRaining = rain;
        rainIntensity = Mathf.Clamp01(intensity);
        ApplyWeather();

        if (changed)
            RainChanged?.Invoke(isRaining);
    }

    void ApplyWeather()
    {
        if (clouds != null)
            foreach (var c in clouds)
                if (c) c.SetWeather(isRaining, rainIntensity);

        trackCloudPlacer?.SetRainEnabled(isRaining);
    }

    public void SetCloudWeather(int index, bool rain, float intensity = 0.7f)
    {
        if (index >= 0 && index < clouds.Length && clouds[index])
            clouds[index].SetWeather(rain, intensity);
    }

    public void RainOn()  => SetRaining(true);
    public void RainOff() => SetRaining(false);
}
