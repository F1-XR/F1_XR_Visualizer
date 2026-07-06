using System.Collections.Generic;
using F1XR.AR;
using F1XR.RestAPI.Api;
using F1XR.RestAPI.Utility;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public class CarReplayView
    {
        private readonly GameObject carPrefab;
        private readonly Dictionary<int, CarAgent> cars = new();
        
        private bool hasOrigin;
        private Vector3 origin;
        private ARPlanePlacementController placement;
        private TrackCalibration calibration;
        
        private readonly Dictionary<int, Quaternion> baseRotations = new();
        private readonly Dictionary<int, Color> driverColors = new();
        private readonly Dictionary<int, string> driverLabels = new();

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

                if (!cars.TryGetValue(driver, out CarAgent car) || car == null)
                    car = CreateCar(driver);

                int index = indices[driver];
                index = Mathf.Clamp(index, 0, list.Count - 2);

                while (index > 0 && list[index].t > time)
                    index--;

                while (index < list.Count - 2 && list[index + 1].t < time)
                    index++;

                indices[driver] = index;

                MoveCar(car, list[index], list[index + 1], time);
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
            hasOrigin = false;
            origin = Vector3.zero;
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
            if (driverLabels.TryGetValue(driver, out string label))
                car.SetLabel(label);

            if (driverColors.TryGetValue(driver, out Color color))
                car.SetColor(color);

            baseRotations.Add(driver, obj.transform.rotation);
            cars.Add(driver, car);

            return car;
        }
        
        public void SetPlacement(ARPlanePlacementController source)
        {
            placement = source;
        }

        public void SetCalibration(TrackCalibration source)
        {
            calibration = source;
            hasOrigin = false;
            origin = Vector3.zero;
        }

        private void MoveCar(CarAgent car, LocationSample a, LocationSample b, float time)
        {
            float duration = Mathf.Max(0.001f, b.t - a.t);
            float u = Mathf.Clamp01((time - a.t) / duration);

            Vector3 posA = default;
            Vector3 posB = default;
            bool useCalibration = false;

            if (calibration != null)
            {
                bool mappedA = calibration.TryMap(a, out posA);
                bool mappedB = calibration.TryMap(b, out posB);
                useCalibration = mappedA && mappedB;
            }

            if (!useCalibration)
            {
                posA = CoordinateUtil.ToUnity(a);
                posB = CoordinateUtil.ToUnity(b);

                if (!hasOrigin)
                {
                    origin = posA;
                    hasOrigin = true;
                }

                posA -= origin;
                posB -= origin;
            }

            Vector3 position = Vector3.Lerp(posA, posB, u);
            Transform placementTransform = placement != null && placement.HasPlacement
                ? placement.PlacementTransform
                : null;

            SetCarParent(car, placementTransform);
            if (placementTransform != null)
                car.SetLocalPosition(position);
            else
                car.SetPosition(position);

            Vector3 direction = posB - posA;
            direction.y = 0f;

            if (direction.sqrMagnitude > 0.000001f)
            {
                Quaternion baseRotation = baseRotations.TryGetValue(car.driverNumber, out Quaternion rotation)
                    ? rotation
                    : Quaternion.identity;

                Quaternion carRotation = Quaternion.LookRotation(direction.normalized, Vector3.up) * baseRotation;
                if (placementTransform != null)
                    car.transform.localRotation = carRotation;
                else
                    car.transform.rotation = carRotation;
            }
        }

        private static void SetCarParent(CarAgent car, Transform parent)
        {
            if (car.transform.parent == parent)
                return;

            car.transform.SetParent(parent, worldPositionStays: false);
        }
        
        public void SetDrivers(DriverInfoDto[] drivers)
        {
            driverColors.Clear();
            driverLabels.Clear();

            if (drivers == null)
                return;

            foreach (DriverInfoDto driver in drivers)
            {
                driverLabels[driver.driverNumber] = string.IsNullOrWhiteSpace(driver.nameAcronym)
                    ? driver.driverNumber.ToString()
                    : driver.nameAcronym;

                if (string.IsNullOrWhiteSpace(driver.teamColour))
                    continue;

                if (ColorUtility.TryParseHtmlString("#" + driver.teamColour, out Color color))
                    driverColors[driver.driverNumber] = color;
            }
        }
    }
}
