using UnityEngine;

public class WeatherController : MonoBehaviour
{
    public WeatherCloud[] clouds;

    public void SetAllWeather(bool rain, float intensity = 0.7f)
    {
        foreach (var c in clouds)
            if (c) c.SetWeather(rain, intensity);
    }

    public void SetCloudWeather(int index, bool rain, float intensity = 0.7f)
    {
        if (index >= 0 && index < clouds.Length && clouds[index])
            clouds[index].SetWeather(rain, intensity);
    }

    public void RainOn()  => SetAllWeather(true);
    public void RainOff() => SetAllWeather(false);
}
