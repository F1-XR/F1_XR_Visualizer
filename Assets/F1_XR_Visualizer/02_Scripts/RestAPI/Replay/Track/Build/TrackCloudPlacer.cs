using UnityEngine;

namespace F1XR.RestAPI.Replay.Track.Build
{
    /// <summary>
    /// Drops a miniature weather cloud above the placed track, sized from the map bounds.
    /// </summary>
    public sealed class TrackCloudPlacer : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] TrackRevealPlacer trackPlacer;
        [SerializeField] GameObject cloudPrefab;

        [Header("Placement")]
        [Tooltip("Gap in metres between the map surface and the underside of the cloud.")]
        [SerializeField] float cloudHeightOffset = 0.15f;
        [SerializeField, Range(0.1f, 1.2f)] float cloudSizeRatio = 0.85f;
        [SerializeField] Vector3 cloudPositionOffset;

        [Header("Rain - MR (tabletop)")]
        [Tooltip("Width of a raindrop streak, in millimetres as seen in the room.")]
        [SerializeField, Range(0.5f, 12f)] float rainDropWidthMm = 4f;
        [Tooltip("Length of a streak, in millimetres. Independent of width.")]
        [SerializeField, Range(5f, 200f)] float rainStreakLengthMm = 80f;
        [Tooltip("Drops emitted per second at full intensity.")]
        [SerializeField, Range(20f, 1200f)] float rainAmount = 380f;
        [Tooltip("Width of a splash on the map surface, in millimetres.")]
        [SerializeField, Range(0.5f, 20f)] float rainSplashSizeMm = 4f;

        [Header("Rain - VR drone (map blown up)")]
        [Tooltip("Drone mode scales the whole track up, so the rain needs its own numbers. " +
            "These are also millimetres, but read at the blown-up world scale.")]
        // At a thousand times scale the viewer is tens of metres from the rain, so a
        // millimetre-sized drop is sub-pixel. These have to be metre-class numbers.
        [SerializeField, Range(1f, 2000f)] float vrRainDropWidthMm = 250f;
        [SerializeField, Range(10f, 40000f)] float vrRainStreakLengthMm = 7000f;
        [SerializeField, Range(20f, 4000f)] float vrRainAmount = 3000f;
        [Tooltip("Use this, not width, to make drone rain look finer. Below about 150mm a " +
            "streak is under one pixel at flight distance and breaks into specks, so " +
            "thinning is done with opacity instead.")]
        [SerializeField, Range(0.05f, 1f)] float vrRainOpacity = 0.5f;
        [Tooltip("Ground splashes read as scattered snowflakes from the air. Off by default " +
            "in drone mode; the falling streaks carry the effect on their own.")]
        [SerializeField] bool vrRainSplash;
        [SerializeField, Range(0.5f, 2000f)] float vrRainSplashSizeMm = 350f;

        [Tooltip("Cloud is treated as drone-mode once the track has been scaled up by " +
            "more than this multiple of its tabletop size.")]
        [SerializeField, Range(1.5f, 20f)] float vrScaleThreshold = 3f;

        /// <summary>How much bigger the cloud is now than when it was placed.</summary>
        public float ScaleRatio { get; private set; }

        /// <summary>True while the VR drone profile is the one being applied.</summary>
        public bool DroneProfileActive { get; private set; }

        // T_RainStreak only paints a line across the middle ~1/8 of the quad,
        // so the quad has to be this much wider than the streak you want to see.
        const float StreakQuadRatio = 0.125f;

        // T_RainSplash covers about half of its quad.
        const float SplashQuadRatio = 0.5f;

        // Measured renderer-bounds width of MiniWeatherCloud.prefab at scale 1,
        // so cloudSizeRatio reads directly as "fraction of map width".
        const float NativeCloudSpan = 13.21f;

        // How far the puff cluster hangs below the prefab origin, in the same units.
        const float NativeCloudBottom = 2.48f;

        GameObject cloudInstance;
        float spawnedScale;
        Vector4 appliedRain = new Vector4(float.NaN, float.NaN, float.NaN, float.NaN);
        bool appliedDrone;
        bool appliedSplashOn = true;
        float appliedScale;

        void Awake()
        {
            if (trackPlacer == null)
                trackPlacer = GetComponent<TrackRevealPlacer>();
        }

        void OnEnable()
        {
            if (trackPlacer == null)
                return;

            trackPlacer.PlacementRevealed += OnPlacementRevealed;

            if (trackPlacer.HasPlacement && cloudInstance == null)
                OnPlacementRevealed();
        }

        void OnDisable()
        {
            if (trackPlacer != null)
                trackPlacer.PlacementRevealed -= OnPlacementRevealed;
        }

        void OnPlacementRevealed()
        {
            if (cloudInstance != null)
            {
                Destroy(cloudInstance);
                cloudInstance = null;
            }

            if (cloudPrefab == null)
                return;

            Transform placementRoot = trackPlacer != null ? trackPlacer.PlacementTransform : null;
            if (placementRoot == null)
                return;

            if (!TryComputeMapBounds(placementRoot, out Bounds mapBounds))
                return;

            float mapWidth = Mathf.Max(mapBounds.size.x, mapBounds.size.z);
            float scaleFactor = mapWidth * cloudSizeRatio / NativeCloudSpan;

            // Offset is measured to the cloud's underside, so lift the origin by the overhang.
            float originAboveMap = cloudHeightOffset + NativeCloudBottom * scaleFactor;

            cloudInstance = Instantiate(cloudPrefab);
            cloudInstance.transform.localScale = Vector3.one * scaleFactor;
            cloudInstance.transform.position = new Vector3(
                mapBounds.center.x + cloudPositionOffset.x,
                mapBounds.max.y + originAboveMap + cloudPositionOffset.y,
                mapBounds.center.z + cloudPositionOffset.z);
            cloudInstance.transform.SetParent(placementRoot, worldPositionStays: true);

            // Splash/mist have to sit on the map, however high the cloud floats.
            Transform ground = cloudInstance.transform.Find("RainGround");
            if (ground != null && scaleFactor > 0f)
                ground.localPosition = new Vector3(0f, -originAboveMap / scaleFactor, 0f);

            spawnedScale = scaleFactor;
            ApplyRainSettings(cloudInstance);

            // Puffs burst once at t=0, so emit only after the transform is final.
            foreach (var ps in cloudInstance.GetComponentsInChildren<ParticleSystem>())
            {
                ps.Clear(true);
                if (!ps.main.loop)
                    ps.Play(true);
            }
        }

        /// <summary>
        /// Re-applies the Rain sliders whenever they change, so the knobs work no matter
        /// when they are touched rather than only at the moment the cloud is spawned.
        /// </summary>
        void Update()
        {
            if (cloudInstance == null || spawnedScale <= 0f)
                return;

            bool drone = IsDroneScale;
            ScaleRatio = cloudInstance.transform.lossyScale.x / spawnedScale;
            DroneProfileActive = drone;

            var wanted = drone
                ? new Vector4(vrRainDropWidthMm, vrRainStreakLengthMm, vrRainAmount, vrRainOpacity)
                : new Vector4(rainDropWidthMm, rainStreakLengthMm, rainAmount, rainSplashSizeMm);

            // Drone mode grows the track over a couple of seconds. Sizes are derived from
            // the live scale, so they have to be recomputed while it is still moving -
            // otherwise they stay frozen at the scale of the frame the profile switched on
            // and the transform then stretches them by the rest of the growth.
            float scaleNow = cloudInstance.transform.lossyScale.x;
            bool scaleMoved = appliedScale <= 0f ||
                Mathf.Abs(scaleNow / appliedScale - 1f) > 0.01f;

            bool splashOn = !drone || vrRainSplash;
            bool settingsChanged = wanted != appliedRain || drone != appliedDrone ||
                                   splashOn != appliedSplashOn;

            if (!settingsChanged && !scaleMoved)
                return;

            ApplyRainSettings(cloudInstance);

            // Live drops keep the size they were born with, so a settings change has to
            // flush them. Growth alone must not: the transition would then wipe the rain
            // every frame while the track expands.
            if (settingsChanged)
                foreach (var ps in cloudInstance.GetComponentsInChildren<ParticleSystem>(true))
                    if (ps.name.StartsWith("Rain"))
                        ps.Clear(true);
        }

        /// <summary>
        /// Drone mode does not hide the track, it scales it up, so the cloud grows with
        /// it. The blown-up scale is what tells the two modes apart.
        /// </summary>
        bool IsDroneScale =>
            cloudInstance != null &&
            spawnedScale > 0f &&
            cloudInstance.transform.lossyScale.x > spawnedScale * vrScaleThreshold;

        /// <summary>
        /// Drives streak width/length/amount from the Inspector. Editing the prefab's own
        /// Start Size has no visible effect once this runs, so tune the rain here.
        /// </summary>
        void ApplyRainSettings(GameObject cloud)
        {
            // Read the live scale, not the spawn scale, so millimetres stay honest after
            // drone mode has grown the track underneath the cloud.
            float scaleFactor = cloud.transform.lossyScale.x;
            if (scaleFactor <= 0f)
                scaleFactor = spawnedScale;
            if (scaleFactor <= 0f)
                return;

            Transform emitter = cloud.transform.Find("RainEmitter");
            if (emitter == null)
                return;

            bool drone = IsDroneScale;
            float widthMm = drone ? vrRainDropWidthMm : rainDropWidthMm;
            float lengthMm = drone ? vrRainStreakLengthMm : rainStreakLengthMm;
            float amount = drone ? vrRainAmount : rainAmount;
            float splashMm = drone ? vrRainSplashSizeMm : rainSplashSizeMm;

            // metres wanted on screen -> prefab-local quad width
            float quad = (widthMm * 0.001f) / scaleFactor / StreakQuadRatio;

            float lengthLocal = (lengthMm * 0.001f) / scaleFactor;

            foreach (var ps in emitter.GetComponentsInChildren<ParticleSystem>(true))
            {
                bool isMain = ps.name == "RainSystem_Main";
                float w = isMain ? quad : quad * 0.7f;
                float l = isMain ? lengthLocal : lengthLocal * 0.75f;

                // Stretched Billboard ties length to speed and ignores it when a drop is
                // slow, which renders the quads square. A 3D-sized billboard is exact.
                var m = ps.main;
                m.startSize3D = true;
                m.startSizeX = new ParticleSystem.MinMaxCurve(w * 0.85f, w * 1.15f);
                m.startSizeY = new ParticleSystem.MinMaxCurve(l * 0.85f, l * 1.15f);
                m.startSizeZ = new ParticleSystem.MinMaxCurve(w);

                float alpha = (isMain ? 1f : 0.6f) * (drone ? vrRainOpacity : 1f);
                m.startColor = isMain
                    ? new Color(0.88f, 0.94f, 1f, alpha)
                    : new Color(0.82f, 0.91f, 1f, alpha);

                // Vertical Billboard pins the quad's up to world up and only spins it about
                // Y to face the viewer. A plain Billboard is aligned to the camera plane, so
                // its up tips with the head and the rain leans with it.
                var rr = ps.GetComponent<ParticleSystemRenderer>();
                rr.renderMode = ParticleSystemRenderMode.VerticalBillboard;
                rr.minParticleSize = 0f;

                // A screen-size floor scales the quad uniformly, so a long thin streak
                // snaps into a square dot and the rain reads as snow. Never clamp.
                rr.minParticleSize = 0f;
            }

            var area = emitter.GetComponent<RainArea>();
            if (area != null)
            {
                area.maxEmissionRate = amount;
                area.maxFineEmissionRate = amount * 1.4f;
            }

            Transform splash = cloud.transform.Find("RainGround/RainSplash");
            if (splash != null)
            {
                bool splashOn = !drone || vrRainSplash;
                if (splash.gameObject.activeSelf != splashOn)
                    splash.gameObject.SetActive(splashOn);

                if (splashOn)
                {
                    float s = (splashMm * 0.001f) / scaleFactor / SplashQuadRatio;
                    var sm = splash.GetComponent<ParticleSystem>().main;
                    sm.startSize3D = false;
                    sm.startSize = new ParticleSystem.MinMaxCurve(s * 0.6f, s * 1.4f);
                }
            }

            appliedRain = drone
                ? new Vector4(widthMm, lengthMm, amount, vrRainOpacity)
                : new Vector4(widthMm, lengthMm, amount, splashMm);
            appliedDrone = drone;
            appliedSplashOn = !drone || vrRainSplash;
            appliedScale = scaleFactor;
        }

        static bool TryComputeMapBounds(Transform placementRoot, out Bounds bounds)
        {
            Transform visual = placementRoot.Find("Visual");
            Transform root = visual != null ? visual : placementRoot;
            Transform cars = root.Find("Cars");

            bounds = default;
            bool hasBounds = false;

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
            {
                if (renderer == null)
                    continue;
                if (cars != null && renderer.transform.IsChildOf(cars))
                    continue;

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return hasBounds;
        }
    }
}
