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

            root = new GameObject("PitStopTeamBox");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localFocus;

            CreateBox(
                "PitLaneFloor",
                root.transform,
                new Vector3(carLength * 0.15f, -carLength * 0.035f, 0f),
                new Vector3(carLength * 2.2f, carLength * 0.05f, carLength * 4.8f),
                floor);
            CreateBox(
                "GarageBackdrop",
                root.transform,
                new Vector3(carLength * 1.05f, carLength * 0.42f, 0f),
                new Vector3(carLength * 0.08f, carLength * 0.9f, carLength * 4.1f),
                dark);
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

            if (environmentProfile != null &&
                environmentProfile.backgroundPrefab != null)
            {
                GameObject background = Object.Instantiate(
                    environmentProfile.backgroundPrefab,
                    root.transform);
                background.name = "PitEnvironmentBackground";
                background.transform.localPosition =
                    environmentProfile.localPosition;
                background.transform.localRotation =
                    Quaternion.Euler(
                        environmentProfile.localEulerAngles);
                background.transform.localScale =
                    environmentProfile.localScale;
            }

            LocalBounds = new Bounds(
                localFocus +
                new Vector3(
                    carLength * 0.3f,
                    carLength * 0.4f,
                    0f),
                new Vector3(
                    carLength * 2.8f,
                    carLength,
                    carLength * 4.9f));
            Apply(pitSequence.StartTime, false);
        }

        public void Apply(float replayTime, bool playing)
        {
            if (root == null || sequence == null)
                return;

            PitStopPhase phase = sequence.GetPhase(replayTime);
            float crewBlend = phase switch
            {
                PitStopPhase.Brake => 0.35f,
                PitStopPhase.Service => 1f,
                PitStopPhase.Release => 0.45f,
                _ => 0f
            };
            if (sequence.IsDriveThrough)
                crewBlend = 0f;

            for (int i = 0; i < crew.Count; i++)
            {
                if (crew[i] == null)
                    continue;

                crew[i].localPosition = Vector3.Lerp(
                    crewWaitingPositions[i],
                    crewServicePositions[i],
                    crewBlend);
                crew[i].localRotation = Quaternion.LookRotation(
                    -crew[i].localPosition.normalized,
                    Vector3.up);
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
            LocalBounds = default;
        }

        private void CreateCrew(
            float carLength,
            Color teamColor,
            GameObject wheelGunPrefab)
        {
            Vector3[] waiting =
            {
                new(carLength * 0.82f, 0f, carLength * 0.62f),
                new(carLength * 0.82f, 0f, -carLength * 0.62f),
                new(-carLength * 0.82f, 0f, carLength * 0.62f),
                new(-carLength * 0.82f, 0f, -carLength * 0.62f)
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
                GameObject member = CreatePrimitive(
                    PrimitiveType.Capsule,
                    $"Crew_{i + 1}",
                    root.transform,
                    waiting[i] + Vector3.up * carLength * 0.16f,
                    new Vector3(
                        carLength * 0.12f,
                        carLength * 0.18f,
                        carLength * 0.12f),
                    Color.Lerp(teamColor, Color.black, 0.35f));
                crew.Add(member.transform);
                crewWaitingPositions.Add(member.transform.localPosition);
                crewServicePositions.Add(
                    working[i] + Vector3.up * carLength * 0.16f);

                GameObject gun;
                if (wheelGunPrefab != null)
                {
                    gun = Object.Instantiate(
                        wheelGunPrefab,
                        member.transform);
                    gun.name = $"WheelGun_{i + 1}";
                    gun.transform.localPosition =
                        new Vector3(0f, -0.3f, -0.25f);
                    gun.transform.localRotation =
                        Quaternion.identity;
                }
                else
                {
                    gun = CreateBox(
                        $"WheelGun_{i + 1}",
                        member.transform,
                        new Vector3(0f, -0.25f, -0.22f),
                        new Vector3(0.16f, 0.12f, 0.35f),
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
                Quaternion.LookRotation(Vector3.left, Vector3.up);
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
            text.alignment = TextAlignmentOptions.Center;
            text.color = teamColor;
            text.fontSize = 5f;
            text.enableAutoSizing = false;
            text.rectTransform.sizeDelta = new Vector2(12f, 4f);
            text.renderer.shadowCastingMode = ShadowCastingMode.Off;
            text.renderer.receiveShadows = false;
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
            Shader shader = Shader.Find(
                "Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            Material material = new(shader)
            {
                color = color
            };
            materials.Add(material);
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
