using System.Collections.Generic;
using F1XR.RestAPI.Api;
using F1XR.RestAPI.Utility;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public class CarReplayView
    {
        private readonly GameObject carPrefab;
        private readonly Dictionary<int, CarAgent> cars = new();
        private readonly Dictionary<int, Quaternion> baseRotations = new();
        private readonly Dictionary<int, Color> driverColors = new();

        public CarReplayView(GameObject carPrefab)
        {
            this.carPrefab = carPrefab;
        }

        public void Show(Dictionary<int, List<LocationSample>> samples, Dictionary<int, int> indices, float time)
        {
            foreach (KeyValuePair<int, List<LocationSample>> pair in samples)
            {
                int driver = pair.Key;
                List<LocationSample> list = pair.Value;

                if (list.Count < 2)
                    continue;

                if (!cars.TryGetValue(driver, out CarAgent car))
                    car = CreateCar(driver);

                int index = indices[driver];
                index = Mathf.Clamp(index, 0, list.Count - 2);

                while (index > 0 && list[index].t > time)
                    index--;

                while (index < list.Count - 2 && list[index + 1].t < time)
                    index++;

                indices[driver] = index;

                MoveCar(driver, car, list[index], list[index + 1], time);
            }
        }

        public void Clear()
        {
            foreach (CarAgent car in cars.Values)
            {
                if (car != null)
                    Object.Destroy(car.gameObject);
            }

            cars.Clear();
            baseRotations.Clear();
        }

        private CarAgent CreateCar(int driver)
        {
            GameObject obj;

            if (carPrefab != null)
            {
                obj = Object.Instantiate(carPrefab);
            }
            else
            {
                obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obj.transform.localScale = new Vector3(0.6f, 0.3f, 1.0f);
            }

            CarAgent car = obj.GetComponent<CarAgent>();
            if (car == null)
                car = obj.AddComponent<CarAgent>();

            car.Init(driver);
            if (driverColors.TryGetValue(driver, out Color color))
                car.SetColor(color);
            baseRotations.Add(driver, obj.transform.rotation);
            cars.Add(driver, car);

            return car;
        }

        private void MoveCar(int driver, CarAgent car, LocationSample a, LocationSample b, float time)
        {
            float duration = Mathf.Max(0.001f, b.t - a.t);
            float u = Mathf.Clamp01((time - a.t) / duration);

            Vector3 posA = CoordinateUtil.ToUnity(a);
            Vector3 posB = CoordinateUtil.ToUnity(b);
            Vector3 position = Vector3.Lerp(posA, posB, u);

            car.SetPosition(position);

            Vector3 direction = posB - posA;
            if (direction.sqrMagnitude > 0.0001f)
            {
                Quaternion baseRotation = baseRotations.TryGetValue(driver, out Quaternion rotation)
                    ? rotation
                    : Quaternion.identity;

                car.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up) * baseRotation;
            }
        }
        
        public void SetDrivers(DriverInfoDto[] drivers)
        {
            driverColors.Clear();

            if (drivers == null)
                return;

            foreach (DriverInfoDto driver in drivers)
            {
                if (string.IsNullOrWhiteSpace(driver.teamColour))
                    continue;

                if (ColorUtility.TryParseHtmlString("#" + driver.teamColour, out Color color))
                    driverColors[driver.driverNumber] = color;
            }
        }
    }
}
