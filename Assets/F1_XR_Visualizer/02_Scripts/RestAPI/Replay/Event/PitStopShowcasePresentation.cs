using System.Collections.Generic;
using F1XR.RestAPI.Api;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace F1XR.RestAPI.Replay
{
    public sealed class PitStopShowcasePresentation
    {
        private readonly List<Material> materials = new();
        private readonly Dictionary<Color32, Material> materialCache = new();
        private readonly List<Transform> crew = new();
        private readonly List<Vector3> crewWaitingPositions = new();
        private readonly List<Vector3> crewServicePositions = new();
        private readonly List<GameObject> wheelGuns = new();

        private GameObject root;
        private GameObject serviceBeacon;
        private AudioSource wheelGunAudio;
        private PitStopSequence sequence;
        private float lastReplayTime = float.NaN;

        public Bounds LocalBounds { get; private set; }

        public void Build(
            Transform parent,
            ReplayCarView vehicle,
            Vector3 localFocus,
            float localVehicleLength,
            DriverInfoDto driver,
            ReplayEventDto replayEvent,
            PitStopSequence pitSequence,
            GameObject wheelGunPrefab,
            AudioClip wheelGunClip,
            PitEnvironmentProfile environmentProfile)
        {
            Clear();
            if (parent == null || pitSequence == null)
                return;

            sequence = pitSequence;
            float carLength = Mathf.Max(0.04f, localVehicleLength);
            Color teamColor = ResolveTeamColor(driver);
            Color dark = new(0.025f, 0.03f, 0.04f, 1f);
            Color floor = new(0.075f, 0.08f, 0.09f, 1f);
            float vehicleGroundOffset = 0f;
            if (vehicle != null &&
                vehicle.TryGetVisualGroundOffset(
                    parent,
                    out float measuredGroundOffset))
            {
                vehicleGroundOffset = measuredGroundOffset;
            }
            Vector3 localGroundFocus =
                localFocus +
                Vector3.up * vehicleGroundOffset;

            root = new GameObject("PitStopTeamBox");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localGroundFocus;

            CreateEnvironmentModules(
                carLength,
                environmentProfile,
                floor,
                dark,
                teamColor);
            CreateBox(
                "TeamHeader",
                root.transform,
                new Vector3(carLength * 0.99f, carLength * 0.78f, 0f),
                new Vector3(carLength * 0.04f, carLength * 0.18f, carLength * 3.8f),
                teamColor);

            float line = carLength * 0.025f;
            CreateBox(
                "BoxLeftLine",
                root.transform,
                new Vector3(-carLength * 0.48f, line, 0f),
                new Vector3(line, line, carLength * 1.55f),
                teamColor);
            CreateBox(
                "BoxRightLine",
                root.transform,
                new Vector3(carLength * 0.48f, line, 0f),
                new Vector3(line, line, carLength * 1.55f),
                teamColor);
            CreateBox(
                "BoxFrontLine",
                root.transform,
                new Vector3(0f, line, carLength * 0.76f),
                new Vector3(carLength, line, line),
                teamColor);
            CreateBox(
                "BoxRearLine",
                root.transform,
                new Vector3(0f, line, -carLength * 0.76f),
                new Vector3(carLength, line, line),
                teamColor);

            CreateCrew(carLength, teamColor, wheelGunPrefab);
            CreateTireStacks(carLength, teamColor);
            CreateSign(carLength, driver, replayEvent, teamColor);

            serviceBeacon = CreatePrimitive(
                PrimitiveType.Sphere,
                "ServiceBeacon",
                root.transform,
                new Vector3(
                    carLength * 0.88f,
                    carLength * 0.72f,
                    carLength * 1.45f),
                Vector3.one * carLength * 0.13f,
                teamColor);
            serviceBeacon.SetActive(false);

            if (wheelGunClip != null)
            {
                wheelGunAudio = root.AddComponent<AudioSource>();
                wheelGunAudio.clip = wheelGunClip;
                wheelGunAudio.loop = true;
                wheelGunAudio.playOnAwake = false;
                wheelGunAudio.spatialBlend = 1f;
                wheelGunAudio.minDistance = 0.8f;
                wheelGunAudio.maxDistance = 12f;
                wheelGunAudio.volume = 0.8f;
            }

            LocalBounds = new Bounds(
                localGroundFocus +
                new Vector3(
                    carLength * 0.7f,
                    carLength * 0.55f,
                    0f),
                new Vector3(
                    carLength * 3.5f,
                    carLength * 2.3f,
                    carLength * 15f));
            Apply(pitSequence.StartTime, false);
        }

        public void Apply(float replayTime, bool playing)
        {
            if (root == null || sequence == null)
                return;

            PitStopPhase phase = sequence.GetPhase(replayTime);
            float crewBlend = ResolveCrewBlend(
                replayTime,
                phase);

            for (int i = 0; i < crew.Count; i++)
            {
                if (crew[i] == null)
                    continue;

                crew[i].localPosition = Vector3.Lerp(
                    crewWaitingPositions[i],
                    crewServicePositions[i],
                    crewBlend);
                Vector3 lookDirection =
                    -crew[i].localPosition;
                lookDirection.y = 0f;
                if (lookDirection.sqrMagnitude > 0.000001f)
                {
                    crew[i].localRotation = Quaternion.LookRotation(
                        lookDirection.normalized,
                        Vector3.up);
                }
            }

            bool servicing =
                !sequence.IsDriveThrough &&
                phase == PitStopPhase.Service;
            for (int i = 0; i < wheelGuns.Count; i++)
            {
                if (wheelGuns[i] != null)
                    wheelGuns[i].SetActive(servicing);
            }
            if (serviceBeacon != null)
                serviceBeacon.SetActive(servicing);

            ApplyAudio(replayTime, playing && servicing);
            lastReplayTime = replayTime;
        }

        public void Clear()
        {
            if (wheelGunAudio != null)
                wheelGunAudio.Stop();
            wheelGunAudio = null;
            if (root != null)
                Object.Destroy(root);
            root = null;
            serviceBeacon = null;
            sequence = null;
            lastReplayTime = float.NaN;
            crew.Clear();
            crewWaitingPositions.Clear();
            crewServicePositions.Clear();
            wheelGuns.Clear();
            for (int i = 0; i < materials.Count; i++)
            {
                if (materials[i] != null)
                    Object.Destroy(materials[i]);
            }
            materials.Clear();
            materialCache.Clear();
            LocalBounds = default;
        }

        private void CreateCrew(
            float carLength,
            Color teamColor,
            GameObject wheelGunPrefab)
        {
            Vector3[] waiting =
            {
                new(carLength * 1.08f, 0f, carLength * 0.96f),
                new(carLength * 1.08f, 0f, carLength * 0.32f),
                new(carLength * 1.08f, 0f, -carLength * 0.32f),
                new(carLength * 1.08f, 0f, -carLength * 0.96f)
            };
            Vector3[] working =
            {
                new(carLength * 0.48f, 0f, carLength * 0.42f),
                new(carLength * 0.48f, 0f, -carLength * 0.42f),
                new(-carLength * 0.48f, 0f, carLength * 0.42f),
                new(-carLength * 0.48f, 0f, -carLength * 0.42f)
            };

            for (int i = 0; i < waiting.Length; i++)
            {
                Transform member = new GameObject(
                    $"Crew_{i + 1}").transform;
                member.SetParent(root.transform, false);
                member.localPosition = waiting[i];
                CreateCrewSilhouette(
                    member,
                    carLength,
                    teamColor);
                crew.Add(member);
                crewWaitingPositions.Add(member.localPosition);
                crewServicePositions.Add(working[i]);

                GameObject gun;
                if (wheelGunPrefab != null)
                {
                    gun = Object.Instantiate(
                        wheelGunPrefab,
                        member);
                    gun.name = $"WheelGun_{i + 1}";
                    gun.transform.localPosition =
                        new Vector3(
                            carLength * 0.1f,
                            carLength * 0.12f,
                            carLength * 0.06f);
                    gun.transform.localRotation =
                        Quaternion.identity;
                    gun.transform.localScale *=
                        carLength * 0.08f;
                }
                else
                {
                    gun = CreateBox(
                        $"WheelGun_{i + 1}",
                        member,
                        new Vector3(
                            carLength * 0.1f,
                            carLength * 0.12f,
                            carLength * 0.06f),
                        new Vector3(
                            carLength * 0.05f,
                            carLength * 0.035f,
                            carLength * 0.11f),
                        Color.gray);
                }
                gun.SetActive(false);
                wheelGuns.Add(gun);
            }
        }

        private void CreateTireStacks(
            float carLength,
            Color teamColor)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                for (int longitudinal = -1;
                     longitudinal <= 1;
                     longitudinal += 2)
                {
                    CreatePrimitive(
                        PrimitiveType.Cylinder,
                        "TireStack",
                        root.transform,
                        new Vector3(
                            carLength * 0.92f,
                            carLength * 0.08f,
                            longitudinal * carLength * 1.35f +
                            side * carLength * 0.09f),
                        new Vector3(
                            carLength * 0.18f,
                            carLength * 0.07f,
                            carLength * 0.18f),
                        Color.Lerp(Color.black, teamColor, 0.12f));
                }
            }
        }

        private void CreateSign(
            float carLength,
            DriverInfoDto driver,
            ReplayEventDto replayEvent,
            Color teamColor)
        {
            GameObject sign = new GameObject(
                "PitBoxSign",
                typeof(TextMeshPro));
            sign.transform.SetParent(root.transform, false);
            sign.transform.localPosition = new Vector3(
                carLength * 0.94f,
                carLength * 0.48f,
                0f);
            sign.transform.localRotation =
                Quaternion.LookRotation(Vector3.right, Vector3.up);
            sign.transform.localScale = Vector3.one * carLength * 0.08f;
            TextMeshPro text = sign.GetComponent<TextMeshPro>();
            string team = driver != null &&
                          !string.IsNullOrWhiteSpace(driver.teamName)
                ? driver.teamName
                : "PIT BOX";
            string name = driver != null &&
                          !string.IsNullOrWhiteSpace(driver.nameAcronym)
                ? driver.nameAcronym
                : replayEvent.driverNumbers[0].ToString();
            text.text = $"{team}\n{name}  LAP {replayEvent.lapNumber}";
            text.isRightToLeftText = false;
            text.alignment = TextAlignmentOptions.Center;
            text.color = teamColor;
            text.fontSize = 5f;
            text.enableAutoSizing = false;
            text.rectTransform.sizeDelta = new Vector2(12f, 4f);
            text.renderer.shadowCastingMode = ShadowCastingMode.Off;
            text.renderer.receiveShadows = false;
        }

        private void CreateEnvironmentModules(
            float carLength,
            PitEnvironmentProfile environmentProfile,
            Color floorColor,
            Color darkColor,
            Color teamColor)
        {
            Transform trackModule = new GameObject(
                "PitTrackModule").transform;
            trackModule.SetParent(root.transform, false);
            if (environmentProfile != null &&
                environmentProfile.pitTrackPrefab != null)
            {
                InstantiateEnvironmentPrefab(
                    environmentProfile.pitTrackPrefab,
                    "PitTrack",
                    trackModule,
                    environmentProfile.pitTrackLocalPosition,
                    environmentProfile.pitTrackLocalEulerAngles,
                    environmentProfile.pitTrackLocalScale);
            }
            else
            {
                CreateFallbackPitTrack(
                    trackModule,
                    carLength,
                    floorColor);
            }

            Transform buildingModule = new GameObject(
                "PitBuildingModule").transform;
            buildingModule.SetParent(root.transform, false);
            if (environmentProfile != null &&
                environmentProfile.pitBuildingPrefab != null)
            {
                InstantiateEnvironmentPrefab(
                    environmentProfile.pitBuildingPrefab,
                    "PitBuilding",
                    buildingModule,
                    environmentProfile.pitBuildingLocalPosition,
                    environmentProfile.pitBuildingLocalEulerAngles,
                    environmentProfile.pitBuildingLocalScale);
            }
            else
            {
                CreateFallbackPitBuilding(
                    buildingModule,
                    carLength,
                    darkColor,
                    teamColor);
            }

            if (environmentProfile != null &&
                environmentProfile.backgroundPrefab != null)
            {
                InstantiateEnvironmentPrefab(
                    environmentProfile.backgroundPrefab,
                    "PitEnvironmentBackground",
                    root.transform,
                    environmentProfile.localPosition,
                    environmentProfile.localEulerAngles,
                    environmentProfile.localScale);
            }
        }

        private void CreateCrewSilhouette(
            Transform parent,
            float carLength,
            Color teamColor)
        {
            Color suitColor = Color.Lerp(
                teamColor,
                Color.black,
                0.28f);
            Color helmetColor = Color.Lerp(
                teamColor,
                Color.white,
                0.2f);
            Color visorColor = new(0.03f, 0.04f, 0.055f, 1f);

            CreatePrimitive(
                PrimitiveType.Capsule,
                "LeftLeg",
                parent,
                new Vector3(
                    -carLength * 0.035f,
                    carLength * 0.065f,
                    0f),
                new Vector3(
                    carLength * 0.028f,
                    carLength * 0.06f,
                    carLength * 0.03f),
                suitColor);
            CreatePrimitive(
                PrimitiveType.Capsule,
                "RightLeg",
                parent,
                new Vector3(
                    carLength * 0.035f,
                    carLength * 0.065f,
                    0f),
                new Vector3(
                    carLength * 0.028f,
                    carLength * 0.06f,
                    carLength * 0.03f),
                suitColor);
            CreatePrimitive(
                PrimitiveType.Capsule,
                "Torso",
                parent,
                new Vector3(
                    0f,
                    carLength * 0.18f,
                    0f),
                new Vector3(
                    carLength * 0.075f,
                    carLength * 0.09f,
                    carLength * 0.055f),
                suitColor);

            GameObject leftArm = CreatePrimitive(
                PrimitiveType.Capsule,
                "LeftArm",
                parent,
                new Vector3(
                    -carLength * 0.09f,
                    carLength * 0.18f,
                    0f),
                new Vector3(
                    carLength * 0.025f,
                    carLength * 0.07f,
                    carLength * 0.025f),
                suitColor);
            leftArm.transform.localRotation =
                Quaternion.Euler(0f, 0f, -12f);
            GameObject rightArm = CreatePrimitive(
                PrimitiveType.Capsule,
                "RightArm",
                parent,
                new Vector3(
                    carLength * 0.09f,
                    carLength * 0.18f,
                    0f),
                new Vector3(
                    carLength * 0.025f,
                    carLength * 0.07f,
                    carLength * 0.025f),
                suitColor);
            rightArm.transform.localRotation =
                Quaternion.Euler(0f, 0f, 12f);

            CreatePrimitive(
                PrimitiveType.Sphere,
                "Helmet",
                parent,
                new Vector3(
                    0f,
                    carLength * 0.31f,
                    0f),
                Vector3.one * carLength * 0.065f,
                helmetColor);
            CreateBox(
                "Visor",
                parent,
                new Vector3(
                    0f,
                    carLength * 0.315f,
                    carLength * 0.058f),
                new Vector3(
                    carLength * 0.07f,
                    carLength * 0.022f,
                    carLength * 0.018f),
                visorColor);
        }

        private float ResolveCrewBlend(
            float replayTime,
            PitStopPhase phase)
        {
            if (sequence == null || sequence.IsDriveThrough)
                return 0f;

            if (phase == PitStopPhase.Brake)
            {
                return Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(
                        sequence.BrakeTime,
                        sequence.ServiceStartTime,
                        replayTime));
            }

            if (phase == PitStopPhase.Service)
                return 1f;

            if (phase == PitStopPhase.Release)
            {
                return 1f - Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(
                        sequence.ServiceEndTime,
                        sequence.ReleaseEndTime,
                        replayTime));
            }

            return 0f;
        }

        private void CreateFallbackPitTrack(
            Transform parent,
            float carLength,
            Color floorColor)
        {
            Color laneColor = Color.Lerp(
                floorColor,
                Color.white,
                0.08f);
            Color markingColor = new(0.72f, 0.75f, 0.78f, 1f);
            Color barrierColor = new(0.035f, 0.045f, 0.06f, 1f);
            float trackLength = carLength * 15f;

            CreateBox(
                "PitTrackPlaceholder",
                parent,
                new Vector3(
                    carLength * 0.15f,
                    -carLength * 0.035f,
                    0f),
                new Vector3(
                    carLength * 2.2f,
                    carLength * 0.05f,
                    trackLength),
                floorColor);
            CreateBox(
                "PitLaneSurface",
                parent,
                new Vector3(
                    -carLength * 0.12f,
                    -carLength * 0.0075f,
                    0f),
                new Vector3(
                    carLength * 1.62f,
                    carLength * 0.015f,
                    trackLength * 0.98f),
                laneColor);
            CreateBox(
                "PitWallBarrier",
                parent,
                new Vector3(
                    -carLength * 0.98f,
                    carLength * 0.09f,
                    0f),
                new Vector3(
                    carLength * 0.08f,
                    carLength * 0.18f,
                    trackLength),
                barrierColor);

            float dashLength = carLength * 0.58f;
            for (int i = -6; i <= 6; i++)
            {
                CreateBox(
                    $"PitLaneDash_{i + 7}",
                    parent,
                    new Vector3(
                        -carLength * 0.78f,
                        carLength * 0.012f,
                        i * carLength * 1.05f),
                    new Vector3(
                        carLength * 0.025f,
                        carLength * 0.012f,
                        dashLength),
                    markingColor);
            }

            CreateBox(
                "PitTrackEndCapNear",
                parent,
                new Vector3(
                    carLength * 0.15f,
                    carLength * 0.7f,
                    -trackLength * 0.5f),
                new Vector3(
                    carLength * 2.2f,
                    carLength * 1.5f,
                    carLength * 0.08f),
                barrierColor);
            CreateBox(
                "PitTrackEndCapFar",
                parent,
                new Vector3(
                    carLength * 0.15f,
                    carLength * 0.7f,
                    trackLength * 0.5f),
                new Vector3(
                    carLength * 2.2f,
                    carLength * 1.5f,
                    carLength * 0.08f),
                barrierColor);
        }

        private void CreateFallbackPitBuilding(
            Transform parent,
            float carLength,
            Color darkColor,
            Color teamColor)
        {
            Color structureColor = Color.Lerp(
                darkColor,
                Color.white,
                0.18f);
            Color lightColor = new(0.7f, 0.82f, 0.92f, 1f);
            float buildingLength = carLength * 15f;

            CreateBox(
                "PitBuildingPlaceholder",
                parent,
                new Vector3(
                    carLength * 1.25f,
                    carLength * 0.72f,
                    0f),
                new Vector3(
                    carLength * 0.12f,
                    carLength * 1.55f,
                    buildingLength),
                darkColor);
            CreateBox(
                "PitBuildingBackWall",
                parent,
                new Vector3(
                    carLength * 2.35f,
                    carLength * 0.72f,
                    0f),
                new Vector3(
                    carLength * 0.12f,
                    carLength * 1.55f,
                    buildingLength),
                structureColor);
            CreateBox(
                "PitBuildingCanopy",
                parent,
                new Vector3(
                    carLength * 1.78f,
                    carLength * 1.48f,
                    0f),
                new Vector3(
                    carLength * 1.25f,
                    carLength * 0.1f,
                    buildingLength),
                structureColor);
            CreateBox(
                "PitBuildingFascia",
                parent,
                new Vector3(
                    carLength * 1.17f,
                    carLength * 1.28f,
                    0f),
                new Vector3(
                    carLength * 0.1f,
                    carLength * 0.3f,
                    buildingLength),
                Color.Lerp(darkColor, teamColor, 0.18f));

            for (int i = -7; i <= 7; i++)
            {
                float z = i * carLength;
                CreateBox(
                    $"PitGaragePost_{i + 8}",
                    parent,
                    new Vector3(
                        carLength * 1.16f,
                        carLength * 0.45f,
                        z),
                    new Vector3(
                        carLength * 0.09f,
                        carLength * 0.9f,
                        carLength * 0.08f),
                    structureColor);
                CreateBox(
                    $"PitGarageLintel_{i + 8}",
                    parent,
                    new Vector3(
                        carLength * 1.16f,
                        carLength * 0.88f,
                        z + carLength * 0.5f),
                    new Vector3(
                        carLength * 0.09f,
                        carLength * 0.08f,
                        carLength * 0.92f),
                    structureColor);

                if (i % 2 == 0)
                {
                    CreateBox(
                        $"PitBuildingLight_{i + 8}",
                        parent,
                        new Vector3(
                            carLength * 1.2f,
                            carLength * 1.07f,
                            z + carLength * 0.5f),
                        new Vector3(
                            carLength * 0.04f,
                            carLength * 0.035f,
                            carLength * 0.55f),
                        lightColor);
                }
            }

            CreateBox(
                "PitBuildingEndCapNear",
                parent,
                new Vector3(
                    carLength * 1.8f,
                    carLength * 0.72f,
                    -buildingLength * 0.5f),
                new Vector3(
                    carLength * 1.25f,
                    carLength * 1.55f,
                    carLength * 0.1f),
                structureColor);
            CreateBox(
                "PitBuildingEndCapFar",
                parent,
                new Vector3(
                    carLength * 1.8f,
                    carLength * 0.72f,
                    buildingLength * 0.5f),
                new Vector3(
                    carLength * 1.25f,
                    carLength * 1.55f,
                    carLength * 0.1f),
                structureColor);
        }

        private static GameObject InstantiateEnvironmentPrefab(
            GameObject prefab,
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 localEulerAngles,
            Vector3 localScale)
        {
            GameObject instance = Object.Instantiate(prefab, parent);
            instance.name = name;
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation =
                Quaternion.Euler(localEulerAngles);
            instance.transform.localScale = localScale;
            return instance;
        }

        private void ApplyAudio(float replayTime, bool shouldPlay)
        {
            if (wheelGunAudio == null ||
                wheelGunAudio.clip == null ||
                sequence == null)
            {
                return;
            }

            if (!shouldPlay)
            {
                if (wheelGunAudio.isPlaying)
                    wheelGunAudio.Pause();
                return;
            }

            float offset = Mathf.Repeat(
                Mathf.Max(
                    0f,
                    replayTime - sequence.ServiceStartTime),
                wheelGunAudio.clip.length);
            bool seeked = !float.IsNaN(lastReplayTime) &&
                Mathf.Abs(replayTime - lastReplayTime) > 0.35f;
            if (!wheelGunAudio.isPlaying || seeked)
            {
                wheelGunAudio.time = Mathf.Clamp(
                    offset,
                    0f,
                    Mathf.Max(0f, wheelGunAudio.clip.length - 0.01f));
                wheelGunAudio.Play();
            }
        }

        private GameObject CreateBox(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Color color)
        {
            return CreatePrimitive(
                PrimitiveType.Cube,
                name,
                parent,
                localPosition,
                localScale,
                color);
        }

        private GameObject CreatePrimitive(
            PrimitiveType type,
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Color color)
        {
            GameObject instance = GameObject.CreatePrimitive(type);
            instance.name = name;
            instance.transform.SetParent(parent, false);
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = localScale;
            if (instance.TryGetComponent(out Collider collider))
                Object.Destroy(collider);
            if (instance.TryGetComponent(out Renderer renderer))
            {
                renderer.sharedMaterial = CreateMaterial(color);
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            return instance;
        }

        private Material CreateMaterial(Color color)
        {
            Color32 key = color;
            if (materialCache.TryGetValue(
                    key,
                    out Material cached) &&
                cached != null)
            {
                return cached;
            }

            Shader shader = Shader.Find(
                "Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            Material material = new(shader)
            {
                color = color
            };
            materials.Add(material);
            materialCache[key] = material;
            return material;
        }

        private static Color ResolveTeamColor(DriverInfoDto driver)
        {
            if (driver != null &&
                !string.IsNullOrWhiteSpace(driver.teamColour) &&
                ColorUtility.TryParseHtmlString(
                    "#" + driver.teamColour.TrimStart('#'),
                    out Color color))
            {
                return color;
            }

            return new Color(0.9f, 0.08f, 0.08f, 1f);
        }
    }
}
