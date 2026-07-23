using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace F1XR.RestAPI.Replay
{
    public class CarInstances
    {
        private readonly TeamCarPrefabs teamCarPrefabs;
        private readonly DriverRoster driverRoster;
        private readonly Dictionary<int, ReplayCarView> cars = new();
        private readonly Dictionary<int, GameObject> prefabsByDriver = new();
        private readonly Dictionary<int, Quaternion> baseRotations = new();
        private readonly Dictionary<int, Vector3> authoredVisualScales = new();
        private float mapScaleRatio = 1f;

        public CarInstances(
            TeamCarPrefabs teamCarPrefabs,
            DriverRoster driverRoster)
        {
            this.teamCarPrefabs = teamCarPrefabs;
            this.driverRoster = driverRoster;
        }

        public IReadOnlyDictionary<int, ReplayCarView> Cars => cars;
        public bool HasCars => cars.Count > 0;

        public ReplayCarView GetOrCreate(
            int driver,
            Action<int> onRemoving,
            Action<int, ReplayCarView> onCreated)
        {
            if (cars.TryGetValue(driver, out ReplayCarView car))
            {
                if (car != null)
                    return car;

                onRemoving?.Invoke(driver);
                cars.Remove(driver);
                prefabsByDriver.Remove(driver);
                baseRotations.Remove(driver);
                authoredVisualScales.Remove(driver);
            }

            return Create(driver, onCreated);
        }

        public void SetMapScaleRatio(float ratio)
        {
            mapScaleRatio = CarVisualScale.NormalizeRatio(ratio);

            foreach (KeyValuePair<int, ReplayCarView> pair in cars)
            {
                if (pair.Value == null ||
                    !authoredVisualScales.TryGetValue(
                        pair.Key,
                        out Vector3 authoredScale))
                {
                    continue;
                }

                CarVisualScale.Apply(
                    pair.Value.VisualMotionRoot,
                    authoredScale,
                    mapScaleRatio);
            }
        }

        public void SetTeamPrefabs(
            TeamCarPrefab[] prefabs,
            Action<int> onRemoving,
            Action<int, ReplayCarView> onCreated)
        {
            teamCarPrefabs.SetPrefabs(prefabs);
            RefreshPrefabs(onRemoving, onCreated);
        }

        public void RefreshPrefabs(
            Action<int> onRemoving,
            Action<int, ReplayCarView> onCreated)
        {
            if (cars.Count == 0)
                return;

            List<int> driversToReplace = null;

            foreach (KeyValuePair<int, ReplayCarView> pair in cars)
            {
                GameObject expectedPrefab = ResolvePrefab(pair.Key);
                prefabsByDriver.TryGetValue(pair.Key, out GameObject currentPrefab);

                if (expectedPrefab == currentPrefab)
                    continue;

                driversToReplace ??= new List<int>();
                driversToReplace.Add(pair.Key);
            }

            if (driversToReplace == null)
                return;

            foreach (int driver in driversToReplace)
                Replace(driver, onRemoving, onCreated);
        }

        public bool TryGetTransform(int driver, out Transform carTransform)
        {
            carTransform = null;

            if (!cars.TryGetValue(driver, out ReplayCarView car) || car == null)
                return false;

            carTransform = car.VisualMotionRoot;
            return true;
        }

        public bool TryGetVisualTransform(int driver, out Transform carTransform)
        {
            carTransform = null;

            if (!cars.TryGetValue(driver, out ReplayCarView car) || car == null)
                return false;

            carTransform = car.VisualMotionRoot;
            return true;
        }

        public Quaternion GetBaseRotation(int driver)
        {
            return baseRotations.TryGetValue(driver, out Quaternion rotation)
                ? rotation
                : Quaternion.identity;
        }

        public void Clear()
        {
            foreach (ReplayCarView car in cars.Values)
            {
                if (car != null)
                    Object.Destroy(car.LogicalRoot.gameObject);
            }

            cars.Clear();
            prefabsByDriver.Clear();
            baseRotations.Clear();
            authoredVisualScales.Clear();
        }

        private ReplayCarView Create(
            int driver,
            Action<int, ReplayCarView> onCreated)
        {
            GameObject prefab = ResolvePrefab(driver);
            GameObject carObject;

            if (prefab != null)
            {
                carObject = Object.Instantiate(prefab);
            }
            else
            {
                carObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                carObject.transform.localScale = new Vector3(0.6f, 0.3f, 1f);
            }

            Quaternion baseRotation = carObject.transform.rotation;
            Vector3 authoredVisualScale = carObject.transform.localScale;
            GameObject logicalObject = new GameObject($"Car_{driver}");
            carObject.transform.SetParent(logicalObject.transform, false);
            CarVisualScale.Apply(
                carObject.transform,
                authoredVisualScale,
                mapScaleRatio);

            ReplayCarView car = carObject.GetComponent<ReplayCarView>();
            if (car == null)
                car = carObject.AddComponent<ReplayCarView>();

            car.SetLogicalRoot(logicalObject.transform);
            car.Init(driver);

            prefabsByDriver[driver] = prefab;
            baseRotations[driver] = baseRotation;
            authoredVisualScales[driver] = authoredVisualScale;
            cars.Add(driver, car);
            onCreated?.Invoke(driver, car);

            return car;
        }

        private void Replace(
            int driver,
            Action<int> onRemoving,
            Action<int, ReplayCarView> onCreated)
        {
            if (!cars.TryGetValue(driver, out ReplayCarView oldCar) || oldCar == null)
                return;

            Transform oldTransform = oldCar.LogicalRoot;
            Transform parent = oldTransform.parent;
            Vector3 position = oldTransform.position;
            Quaternion rotation = oldTransform.rotation;
            Vector3 scale = oldTransform.localScale;
            Vector3 rawPosition = oldCar.rawPosition;

            onRemoving?.Invoke(driver);

            cars.Remove(driver);
            prefabsByDriver.Remove(driver);
            baseRotations.Remove(driver);
            authoredVisualScales.Remove(driver);

            ReplayCarView newCar = Create(driver, onCreated);
            newCar.rawPosition = rawPosition;
            newCar.LogicalRoot.SetParent(parent, worldPositionStays: false);
            newCar.LogicalRoot.SetPositionAndRotation(position, rotation);
            newCar.LogicalRoot.localScale = scale;

            Object.Destroy(oldTransform.gameObject);
        }

        private GameObject ResolvePrefab(int driver)
        {
            string teamName = driverRoster.TryGetTeam(driver, out string team)
                ? team
                : null;
            return teamCarPrefabs.Resolve(teamName);
        }
    }
}
