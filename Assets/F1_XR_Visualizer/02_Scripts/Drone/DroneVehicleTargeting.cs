using System.Collections.Generic;
using F1XR.RestAPI.Api;
using F1XR.RestAPI.Replay;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace F1XR.Drone
{
    public readonly struct DroneVehicleTarget
    {
        public readonly Rect viewportRect;
        public readonly Bounds worldBounds;
        public readonly int driverNumber;
        public readonly int rank;
        public readonly string driverLabel;
        public readonly string teamName;
        public readonly Color teamColor;
        public readonly bool hasTelemetry;
        public readonly float speedKph;
        public readonly bool isBraking;

        public DroneVehicleTarget(
            Rect viewportRect,
            Bounds worldBounds,
            int driverNumber,
            int rank,
            string driverLabel,
            string teamName,
            Color teamColor,
            bool hasTelemetry,
            float speedKph,
            bool isBraking)
        {
            this.viewportRect = viewportRect;
            this.worldBounds = worldBounds;
            this.driverNumber = driverNumber;
            this.rank = rank;
            this.driverLabel = driverLabel;
            this.teamName = teamName;
            this.teamColor = teamColor;
            this.hasTelemetry = hasTelemetry;
            this.speedKph = speedKph;
            this.isBraking = isBraking;
        }
    }

    [DisallowMultipleComponent]
    public sealed class DroneVehicleTargeting : MonoBehaviour
    {
        [Header("Detection Frame")]
        [SerializeField] Rect viewportFrame = new(0.15f, 0.16f, 0.7f, 0.68f);
        [SerializeField, Min(0.001f)] float minimumViewportSize = 0.012f;
        [SerializeField, Min(1)] int maximumVisibleTargets = 8;

        readonly List<DroneVehicleTarget> visibleTargets = new();
        readonly List<Renderer> renderers = new();

        ReplayPlayer replayPlayer;
        DroneVehicleWorldTargetPresenter presenter;
        Camera camera;
        bool isVisible;

        public void Configure(
            ReplayPlayer source,
            DroneVehicleWorldTargetPresenter targetPresenter,
            Camera targetCamera)
        {
            replayPlayer = source;
            presenter = targetPresenter;
            camera = targetCamera;
        }

        public void Show(Camera targetCamera)
        {
            camera = targetCamera;
            isVisible = true;
            presenter?.Show(camera);
            RefreshTargets();
        }

        public void Hide()
        {
            isVisible = false;
            visibleTargets.Clear();
            presenter?.Hide();
        }

        void LateUpdate()
        {
            if (isVisible)
                RefreshTargets();
        }

        void RefreshTargets()
        {
            visibleTargets.Clear();

            if (replayPlayer == null || presenter == null || camera == null)
            {
                presenter?.SetTargets(visibleTargets);
                return;
            }

            List<PositionSampleDto> positions = replayPlayer.GetPositions();
            if (positions == null)
            {
                presenter.SetTargets(visibleTargets);
                return;
            }

            foreach (PositionSampleDto position in positions)
            {
                if (position == null ||
                    !replayPlayer.TryGetVisualCarTransform(
                        position.driverNumber,
                        out Transform carTransform) ||
                    !TryGetViewportRect(
                        carTransform,
                        out Rect rect,
                        out Bounds worldBounds) ||
                    !viewportFrame.Overlaps(rect) ||
                    !viewportFrame.Contains(rect.center))
                {
                    continue;
                }

                DriverInfoDto driver = replayPlayer.GetDriverInfo(
                    position.driverNumber);
                string label = !string.IsNullOrWhiteSpace(driver?.nameAcronym)
                    ? driver.nameAcronym
                    : replayPlayer.GetDriverLabel(position.driverNumber);
                string team = driver?.teamName ?? string.Empty;
                bool hasTelemetry = replayPlayer.TryGetDrivingTelemetry(
                    position.driverNumber,
                    out float speedKph,
                    out int brake);
                visibleTargets.Add(new DroneVehicleTarget(
                    rect,
                    worldBounds,
                    position.driverNumber,
                    position.position,
                    label,
                    team,
                    replayPlayer.GetDriverColor(position.driverNumber),
                    hasTelemetry,
                    speedKph,
                    hasTelemetry && IsBraking(brake)));
            }

            visibleTargets.Sort(CompareTargets);
            if (visibleTargets.Count > maximumVisibleTargets)
                visibleTargets.RemoveRange(
                    maximumVisibleTargets,
                    visibleTargets.Count - maximumVisibleTargets);

            presenter.SetTargets(visibleTargets);
        }

        static bool IsBraking(int brake)
        {
            return brake > 0;
        }

        bool TryGetViewportRect(
            Transform carTransform,
            out Rect rect,
            out Bounds worldBounds)
        {
            rect = default;
            worldBounds = default;
            if (carTransform == null)
                return false;

            renderers.Clear();
            carTransform.GetComponentsInChildren(renderers);
            if (renderers.Count == 0)
                return false;

            bool hasBounds = false;
            Bounds bounds = default;
            foreach (Renderer renderer in renderers)
            {
                if (!IsVehicleRenderer(renderer))
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

            if (!hasBounds)
                return false;

            worldBounds = bounds;

            Vector3 center = camera.WorldToViewportPoint(bounds.center);
            if (center.z <= 0.01f)
                return false;

            Vector3 extents = bounds.extents;
            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            int visibleCornerCount = 0;

            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 point = bounds.center + Vector3.Scale(
                    extents,
                    new Vector3(x, y, z));
                Vector3 viewportPoint = camera.WorldToViewportPoint(point);
                if (viewportPoint.z <= 0.01f)
                    continue;

                visibleCornerCount++;
                minX = Mathf.Min(minX, viewportPoint.x);
                minY = Mathf.Min(minY, viewportPoint.y);
                maxX = Mathf.Max(maxX, viewportPoint.x);
                maxY = Mathf.Max(maxY, viewportPoint.y);
            }

            if (visibleCornerCount == 0)
                return false;

            rect = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return rect.width >= minimumViewportSize &&
                rect.height >= minimumViewportSize;
        }

        static bool IsVehicleRenderer(Renderer renderer)
        {
            if (renderer == null || !renderer.enabled ||
                !renderer.gameObject.activeInHierarchy ||
                renderer is LineRenderer ||
                renderer.GetComponent<TextMesh>() != null ||
                renderer.GetComponent<TextMeshPro>() != null ||
                renderer.GetComponentInParent<Canvas>() != null)
            {
                return false;
            }

            Transform current = renderer.transform;
            while (current != null)
            {
                string objectName = current.name;
                if (objectName.StartsWith("DriverLabel") ||
                    objectName.StartsWith("SelectionFx") ||
                    objectName.StartsWith("GroundRing") ||
                    objectName.StartsWith("SelectionPulse") ||
                    objectName.StartsWith("SelectedCar"))
                {
                    return false;
                }

                if (current.GetComponent<ReplayCarView>() != null)
                    break;

                current = current.parent;
            }

            return true;
        }

        static int CompareTargets(
            DroneVehicleTarget first,
            DroneVehicleTarget second)
        {
            float firstDistance = (first.viewportRect.center -
                new Vector2(0.5f, 0.5f)).sqrMagnitude;
            float secondDistance = (second.viewportRect.center -
                new Vector2(0.5f, 0.5f)).sqrMagnitude;
            return firstDistance.CompareTo(secondDistance);
        }
    }

    [DisallowMultipleComponent]
    public sealed class DroneVehicleWorldTargetPresenter : MonoBehaviour
    {
        const float FrameMinimumAngularWidth = 0.007f;
        const float CardCanvasWidth = 430f;
        const float CardCanvasHeight = 290f;

        [Header("Depth Offset")]
        [SerializeField, Min(0f)] float minimumDepthOffset = 0.3f;
        [SerializeField, Min(0f)] float maximumDepthOffset = 12f;
        [SerializeField, Min(0f)] float depthOffsetPerMeter = 0.02f;

        readonly List<WorldTargetFrame> frames = new();
        Camera xrCamera;
        TMP_FontAsset font;
        GameObject root;
        Material lineMaterial;
        Material leaderMaterial;
        Material uiMaterial;
        Material textMaterial;
        WorldTargetCard card;
        LineRenderer leaderLine;
        bool isVisible;

        public void Configure(Camera camera, TMP_FontAsset targetFont)
        {
            xrCamera = camera;
            font = targetFont != null
                ? targetFont
                : TMP_Settings.defaultFontAsset;
            EnsureRoot();
        }

        public void Show(Camera camera)
        {
            xrCamera = camera;
            EnsureRoot();
            isVisible = xrCamera != null;
            root.SetActive(isVisible);
        }

        public void Hide()
        {
            isVisible = false;
            if (root != null)
                root.SetActive(false);
        }

        public void SetTargets(IReadOnlyList<DroneVehicleTarget> targets)
        {
            if (!isVisible || xrCamera == null || root == null)
                return;

            int count = targets != null ? targets.Count : 0;
            while (frames.Count < count)
                frames.Add(new WorldTargetFrame(root.transform, lineMaterial));

            for (int i = 0; i < frames.Count; i++)
            {
                bool active = i < count;
                frames[i].SetActive(active);
                if (active)
                {
                    frames[i].SetTarget(
                        targets[i],
                        xrCamera,
                        ResolveDepthOffset(targets[i].worldBounds.center));
                }
            }

            if (count == 0)
            {
                card?.SetActive(false);
                leaderLine.enabled = false;
                return;
            }

            SetPrimaryCard(targets[0], frames[0]);
        }

        float ResolveDepthOffset(Vector3 vehicleCenter)
        {
            float distance = Vector3.Distance(
                xrCamera.transform.position,
                vehicleCenter);
            return Mathf.Clamp(
                distance * depthOffsetPerMeter,
                minimumDepthOffset,
                maximumDepthOffset);
        }

        void EnsureRoot()
        {
            if (root != null)
                return;

            root = new GameObject("Drone Vehicle World Targets");
            root.transform.SetParent(transform, false);
            root.SetActive(false);
            lineMaterial = CreateLineMaterial();
            leaderMaterial = lineMaterial != null
                ? new Material(lineMaterial)
                : null;
            uiMaterial = CreateUiMaterial();
            textMaterial = CreateTextMaterial(font);
            leaderLine = CreateLine(
                "Vehicle Target Leader",
                root.transform,
                leaderMaterial);
            leaderLine.useWorldSpace = true;
            leaderLine.positionCount = 2;
            leaderLine.enabled = false;
        }

        void SetPrimaryCard(
            DroneVehicleTarget target,
            WorldTargetFrame frame)
        {
            card ??= new WorldTargetCard(
                root.transform,
                font,
                uiMaterial,
                textMaterial);

            float distance = Vector3.Distance(
                xrCamera.transform.position,
                frame.Center);
            float cardWidth = Mathf.Clamp(distance * 0.27f, 2.4f, 39f);
            float side = target.viewportRect.center.x > 0.5f ? -1f : 1f;
            Vector3 cardPosition = frame.Center +
                xrCamera.transform.right * side *
                    (frame.Width * 0.5f + cardWidth * 0.85f) +
                xrCamera.transform.up * frame.Height * 0.2f;
            Quaternion rotation = Quaternion.LookRotation(
                cardPosition - xrCamera.transform.position,
                xrCamera.transform.up);
            card.SetTarget(target, cardPosition, rotation, cardWidth);

            SetLineColor(leaderMaterial, target.teamColor);
            leaderLine.startColor = Color.white;
            leaderLine.endColor = Color.white;
            leaderLine.widthMultiplier = Mathf.Clamp(
                distance * 0.0024f,
                0.035f,
                0.22f);
            leaderLine.SetPosition(0, frame.GetSidePoint(side));
            leaderLine.SetPosition(1, card.GetSidePoint(-side));
            leaderLine.enabled = true;
        }

        void OnDestroy()
        {
            if (lineMaterial != null)
                Destroy(lineMaterial);
            if (leaderMaterial != null)
                Destroy(leaderMaterial);
            foreach (WorldTargetFrame frame in frames)
                frame.Dispose();
            if (uiMaterial != null)
                Destroy(uiMaterial);
            if (textMaterial != null)
                Destroy(textMaterial);
        }

        static Material CreateLineMaterial()
        {
            Shader shader = Shader.Find("F1XR/DroneTargetOverlayURP") ??
                Shader.Find("Universal Render Pipeline/Unlit") ??
                Shader.Find("Sprites/Default") ??
                Shader.Find("Unlit/Color");
            if (shader == null)
                return null;

            Material material = new Material(shader)
            {
                renderQueue = 4000
            };
            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_SrcBlend"))
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend"))
            {
                material.SetFloat(
                    "_DstBlend",
                    (float)BlendMode.OneMinusSrcAlpha);
            }
            if (material.HasProperty("_ZWrite"))
                material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_ZTest"))
                material.SetFloat("_ZTest", (float)CompareFunction.Always);
            if (material.HasProperty("_Cull"))
                material.SetFloat("_Cull", (float)CullMode.Off);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            return material;
        }

        static Material CreateUiMaterial()
        {
            Shader shader = Shader.Find("UI/NoZTest") ??
                Shader.Find("UI/Default");
            if (shader == null)
                return null;

            Material material = new Material(shader)
            {
                renderQueue = 4000
            };
            if (material.HasProperty("_ZTestMode"))
                material.SetFloat("_ZTestMode", (float)CompareFunction.Always);
            return material;
        }

        static void SetLineColor(Material material, Color color)
        {
            if (material == null)
                return;

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
        }

        static Material CreateTextMaterial(TMP_FontAsset font)
        {
            if (font == null || font.material == null)
                return null;

            Material material = new Material(font.material);
            Shader overlayShader = Shader.Find(
                "TextMeshPro/Mobile/Distance Field Overlay") ??
                Shader.Find("TextMeshPro/Distance Field Overlay");
            if (overlayShader != null)
                material.shader = overlayShader;
            material.renderQueue = 4000;
            return material;
        }

        static LineRenderer CreateLine(
            string name,
            Transform parent,
            Material material)
        {
            GameObject lineObject = new(name, typeof(LineRenderer));
            lineObject.transform.SetParent(parent, false);
            LineRenderer line = lineObject.GetComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.numCapVertices = 4;
            line.numCornerVertices = 3;
            line.sortingOrder = 30000;
            return line;
        }

        sealed class WorldTargetFrame
        {
            readonly Transform root;
            readonly LineRenderer line;
            readonly Material material;
            readonly Vector3[] positions = new Vector3[5];

            public Vector3 Center { get; private set; }
            public float Width { get; private set; }
            public float Height { get; private set; }

            public WorldTargetFrame(Transform parent, Material material)
            {
                GameObject frameObject = new(
                    "Vehicle Target Frame",
                    typeof(LineRenderer));
                frameObject.transform.SetParent(parent, false);
                root = frameObject.transform;
                line = frameObject.GetComponent<LineRenderer>();
                this.material = material != null
                    ? new Material(material)
                    : null;
                line.sharedMaterial = this.material;
                line.useWorldSpace = false;
                line.positionCount = positions.Length;
                line.numCornerVertices = 3;
                line.numCapVertices = 4;
                line.sortingOrder = 30000;
                root.gameObject.SetActive(false);
            }

            public void SetActive(bool active)
            {
                root.gameObject.SetActive(active);
            }

            public void SetTarget(
                DroneVehicleTarget target,
                Camera camera,
                float depthOffset)
            {
                Vector3 toCamera = camera.transform.position -
                    target.worldBounds.center;
                Center = target.worldBounds.center +
                    (toCamera.sqrMagnitude > Mathf.Epsilon
                        ? toCamera.normalized * depthOffset
                        : Vector3.zero);
                float distance = Vector3.Distance(
                    camera.transform.position,
                    Center);
                float carWidth = Mathf.Max(
                    target.worldBounds.size.x,
                    target.worldBounds.size.z);
                Width = Mathf.Clamp(
                    Mathf.Max(carWidth * 1.05f,
                        distance * FrameMinimumAngularWidth),
                    0.3f,
                    20f);
                Height = Mathf.Clamp(
                    Mathf.Max(target.worldBounds.size.y * 1.8f,
                        Width * 0.58f),
                    0.18f,
                    18f);

                root.SetPositionAndRotation(
                    Center,
                    Quaternion.LookRotation(
                        Center - camera.transform.position,
                        camera.transform.up));
                float halfWidth = Width * 0.5f;
                float halfHeight = Height * 0.5f;
                line.widthMultiplier = Mathf.Clamp(
                    distance * 0.0036f,
                    0.0525f,
                    0.33f);
                DroneVehicleWorldTargetPresenter.SetLineColor(
                    this.material,
                    target.teamColor);
                line.startColor = Color.white;
                line.endColor = Color.white;
                positions[0] = new Vector3(-halfWidth, halfHeight, 0f);
                positions[1] = new Vector3(halfWidth, halfHeight, 0f);
                positions[2] = new Vector3(halfWidth, -halfHeight, 0f);
                positions[3] = new Vector3(-halfWidth, -halfHeight, 0f);
                positions[4] = positions[0];
                line.SetPositions(positions);
            }

            public void Dispose()
            {
                if (material != null)
                    UnityEngine.Object.Destroy(material);
            }

            public Vector3 GetSidePoint(float side)
            {
                return root.TransformPoint(new Vector3(
                    Mathf.Sign(side) * Width * 0.5f,
                    0f,
                    0f));
            }
        }

        sealed class WorldTargetCard
        {
            readonly RectTransform root;
            readonly Image accent;
            readonly TextMeshProUGUI number;
            readonly TextMeshProUGUI driver;
            readonly TextMeshProUGUI team;
            readonly TextMeshProUGUI rank;
            readonly TextMeshProUGUI speed;
            readonly Image brakeBadge;

            public WorldTargetCard(
                Transform parent,
                TMP_FontAsset font,
                Material uiMaterial,
                Material textMaterial)
            {
                GameObject cardObject = new(
                    "Vehicle Target Info",
                    typeof(RectTransform),
                    typeof(Canvas),
                    typeof(CanvasScaler));
                cardObject.transform.SetParent(parent, false);
                Canvas canvas = cardObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.overrideSorting = true;
                canvas.sortingOrder = 30000;
                root = cardObject.GetComponent<RectTransform>();
                root.sizeDelta = new Vector2(CardCanvasWidth, CardCanvasHeight);

                Image background = CreateImage("Background", root,
                    new Color(0.02f, 0.05f, 0.08f, 0.9f));
                background.material = uiMaterial;
                Stretch(background.rectTransform);
                accent = CreateImage("Accent", root, Color.cyan);
                accent.material = uiMaterial;
                accent.rectTransform.anchorMin = Vector2.zero;
                accent.rectTransform.anchorMax = new Vector2(0f, 1f);
                accent.rectTransform.sizeDelta = new Vector2(10f, 0f);
                number = CreateText("Number", root, 34f, font, textMaterial);
                driver = CreateText("Driver", root, 46f, font, textMaterial);
                team = CreateText("Team", root, 26f, font, textMaterial);
                rank = CreateText("Rank", root, 32f, font, textMaterial);
                speed = CreateText("Speed", root, 30f, font, textMaterial);
                brakeBadge = CreateImage("Brake Badge", root,
                    new Color(0.8f, 0.04f, 0.02f, 0.95f));
                brakeBadge.material = uiMaterial;
                TextMeshProUGUI brake = CreateText(
                    "Brake", brakeBadge.transform, 22f, font, textMaterial);
                SetRect(number.rectTransform, new Vector2(34f, -30f), new Vector2(340f, 38f));
                SetRect(driver.rectTransform, new Vector2(34f, -76f), new Vector2(360f, 52f));
                SetRect(team.rectTransform, new Vector2(34f, -126f), new Vector2(360f, 32f));
                SetRect(rank.rectTransform, new Vector2(34f, -162f), new Vector2(260f, 34f));
                SetRect(speed.rectTransform, new Vector2(34f, -200f), new Vector2(300f, 34f));
                SetRect(brakeBadge.rectTransform, new Vector2(34f, -238f), new Vector2(130f, 28f));
                Stretch(brake.rectTransform);
                brake.alignment = TextAlignmentOptions.Center;
                brake.text = "BRAKE";
                root.gameObject.SetActive(false);
            }

            public void SetActive(bool active)
            {
                root.gameObject.SetActive(active);
            }

            public void SetTarget(
                DroneVehicleTarget target,
                Vector3 position,
                Quaternion rotation,
                float worldWidth)
            {
                root.SetPositionAndRotation(position, rotation);
                root.localScale = Vector3.one * (worldWidth / CardCanvasWidth);
                accent.color = target.teamColor;
                number.text = $"#{target.driverNumber}";
                driver.text = target.driverLabel;
                team.text = target.teamName;
                rank.text = target.rank > 0 ? $"P{target.rank}" : string.Empty;
                speed.text = target.hasTelemetry
                    ? $"{Mathf.RoundToInt(target.speedKph)} KM/H"
                    : "-- KM/H";
                brakeBadge.gameObject.SetActive(target.isBraking);
                root.gameObject.SetActive(true);
            }

            public Vector3 GetSidePoint(float side)
            {
                return root.TransformPoint(new Vector3(
                    Mathf.Sign(side) * CardCanvasWidth * 0.5f,
                    0f,
                    0f));
            }

            static void Stretch(RectTransform rect)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }

            static void SetRect(
                RectTransform rect,
                Vector2 position,
                Vector2 size)
            {
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = position;
                rect.sizeDelta = size;
            }

            static Image CreateImage(string name, Transform parent, Color color)
            {
                GameObject imageObject = new(
                    name,
                    typeof(RectTransform),
                    typeof(Image));
                imageObject.transform.SetParent(parent, false);
                Image image = imageObject.GetComponent<Image>();
                image.color = color;
                return image;
            }

            static TextMeshProUGUI CreateText(
                string name,
                Transform parent,
                float fontSize,
                TMP_FontAsset font,
                Material material)
            {
                GameObject textObject = new(
                    name,
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(TextMeshProUGUI));
                textObject.transform.SetParent(parent, false);
                TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
                text.font = font;
                if (material != null)
                    text.fontSharedMaterial = material;
                text.fontSize = fontSize;
                text.color = Color.white;
                return text;
            }
        }
    }
}
