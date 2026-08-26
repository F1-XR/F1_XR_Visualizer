using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace F1XR.Drone
{
    [DisallowMultipleComponent]
    public sealed class VRDroneHud : MonoBehaviour
    {
        enum SpeedIndicatorStyle
        {
            [InspectorName("Tick Version")] Tick,
            [InspectorName("Bar Version")] Bar
        }

        const float DistanceFromCamera = 1.2f;
        const int SpeedMarkerCount = 81;
        const int WhiteTickCount = 17;
        const int SpeedOutlineSegmentCount = 73;
        const float SpeedArcDegrees = 288f;
        const float SpeedArcStartDegrees = -54f;
        const float SpeedometerScale = 0.49f;
        const int SpeedWarningKph = 300;
        const int HighSpeedUpdateThresholdKph = 100;
        static readonly Vector2 CanvasSize = new(2200f, 1238f);

        [Header("Speedometer")]
        [SerializeField] TMP_FontAsset f1NumberFont;
        [SerializeField, Min(0.0001f)] float hudScale = 0.0014f;
        [SerializeField, Min(1f)] float maxDisplaySpeedKph = 342f;
        [SerializeField, Min(0.02f)] float highSpeedUpdateInterval = 0.05f;
        [SerializeField, Min(1f)] float speedValueFontSize = 110f;
        [SerializeField] SpeedIndicatorStyle speedIndicatorStyle =
            SpeedIndicatorStyle.Tick;

        [Header("Debug")]
        [SerializeField] bool showFlightInputDiagnostic = true;

        Canvas canvas;
        Camera xrCamera;
        Image exitProgress;
        Image[] speedMarkers;
        Image[] whiteTicks;
        TextMeshProUGUI speedValue;
        TextMeshProUGUI flightInputDiagnostic;
        RectTransform targetOverlay;
        readonly List<TargetBox> targetBoxes = new();
        TargetCard targetCard;
        float targetSpeedKph;
        float displayedSpeedKph;
        float nextSpeedUpdateTime;
        int displayedSpeed = -1;
        int activeSpeedMarkers = -1;
        bool isVisible;

        public TMP_FontAsset NumberFont => f1NumberFont;

        public void Configure(Transform environmentTransform)
        {
            if (canvas != null || environmentTransform == null)
                return;

            if (f1NumberFont == null)
            {
                Debug.LogError(
                    "[VRDrone] VRDroneHud requires the Formula1-Bold SDF font asset.",
                    this);
                return;
            }

            canvas = CreateCanvas(environmentTransform);
            canvas.gameObject.SetActive(false);
        }

        public void Show(Camera camera)
        {
            if (camera == null || canvas == null)
                return;

            xrCamera = camera;
            canvas.gameObject.SetActive(true);
            SetExitHoldProgress(0f);
            ResetSpeedometer();
            isVisible = true;
            UpdatePose();
        }

        public void Hide()
        {
            isVisible = false;
            xrCamera = null;
            ResetSpeedometer();
            if (canvas != null)
                canvas.gameObject.SetActive(false);
        }

        public void SetExitHoldProgress(float normalizedProgress)
        {
            if (exitProgress != null)
                exitProgress.fillAmount = Mathf.Clamp01(normalizedProgress);
        }

        public void SetSpeedKph(float speedKph)
        {
            targetSpeedKph = Mathf.Max(0f, speedKph);
        }

        public void SetFlightInputDiagnostic(
            Vector2 leftStick,
            bool hasLeftThumbstick,
            float throttle)
        {
            if (flightInputDiagnostic == null)
                return;

            flightInputDiagnostic.text =
                $"L STICK  {leftStick.x:+0.00;-0.00;0.00}  " +
                $"{leftStick.y:+0.00;-0.00;0.00}\n" +
                $"L INPUT  {(hasLeftThumbstick ? "OK" : "NOT FOUND")}  " +
                $"THR  {throttle:0.00}";
        }

        void LateUpdate()
        {
            if (isVisible)
            {
                UpdatePose();
                UpdateSpeedometer();
            }
        }

        void UpdatePose()
        {
            if (canvas == null || xrCamera == null)
                return;

            Transform cameraTransform = xrCamera.transform;
            Transform hudTransform = canvas.transform;
            hudTransform.position = cameraTransform.position +
                cameraTransform.forward * DistanceFromCamera;
            hudTransform.rotation = Quaternion.LookRotation(
                hudTransform.position - cameraTransform.position,
                cameraTransform.up);
        }

        Canvas CreateCanvas(Transform parent)
        {
            GameObject canvasObject = new(
                "VR Drone HUD",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler));
            canvasObject.transform.SetParent(parent, false);

            Canvas result = canvasObject.GetComponent<Canvas>();
            result.renderMode = RenderMode.WorldSpace;
            RectTransform rect = canvasObject.GetComponent<RectTransform>();
            rect.sizeDelta = CanvasSize;
            rect.localScale = Vector3.one * hudScale;

            CreateFrame(rect);
            CreateHeader(rect);
            CreateCrosshair(rect);
            CreateExitHint(rect);
            CreateSpeedometer(rect);
            CreateFlightInputDiagnostic(rect);
            targetOverlay = CreateRect("Vehicle Targets", rect);
            targetOverlay.anchorMin = Vector2.zero;
            targetOverlay.anchorMax = Vector2.one;
            targetOverlay.offsetMin = Vector2.zero;
            targetOverlay.offsetMax = Vector2.zero;
            return result;
        }

        void CreateFlightInputDiagnostic(RectTransform parent)
        {
            if (!showFlightInputDiagnostic)
                return;

            flightInputDiagnostic = CreateText(
                "Flight Input Diagnostic",
                parent,
                "L STICK  0.00  0.00\nL INPUT  WAITING  THR  0.00",
                24f);
            flightInputDiagnostic.font = f1NumberFont;
            flightInputDiagnostic.color = new Color(0.55f, 0.9f, 1f, 0.9f);
            flightInputDiagnostic.alignment = TextAlignmentOptions.BottomLeft;
            RectTransform rect = flightInputDiagnostic.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(56f, 98f);
            rect.sizeDelta = new Vector2(620f, 68f);
        }

        public void SetVehicleTargets(IReadOnlyList<DroneVehicleTarget> targets)
        {
            int count = targets != null ? targets.Count : 0;
            EnsureTargetBoxCount(count);

            for (int i = 0; i < targetBoxes.Count; i++)
            {
                bool active = i < count;
                targetBoxes[i].SetActive(active);
                if (active)
                    targetBoxes[i].SetTarget(targets[i]);
            }

            if (count == 0)
            {
                targetCard?.SetActive(false);
                return;
            }

            targetCard ??= CreateTargetCard(targetOverlay);
            targetCard.SetTarget(targets[0]);
        }

        void EnsureTargetBoxCount(int count)
        {
            while (targetBoxes.Count < count)
                targetBoxes.Add(CreateTargetBox(targetOverlay));
        }

        TargetBox CreateTargetBox(Transform parent)
        {
            RectTransform root = CreateRect("Vehicle Target Box", parent);
            Image top = CreateImage("Top", root, Color.cyan);
            Image bottom = CreateImage("Bottom", root, Color.cyan);
            Image left = CreateImage("Left", root, Color.cyan);
            Image right = CreateImage("Right", root, Color.cyan);

            top.rectTransform.anchorMin = new Vector2(0f, 1f);
            top.rectTransform.anchorMax = Vector2.one;
            top.rectTransform.pivot = new Vector2(0.5f, 1f);
            top.rectTransform.sizeDelta = new Vector2(0f, 4f);
            bottom.rectTransform.anchorMin = Vector2.zero;
            bottom.rectTransform.anchorMax = new Vector2(1f, 0f);
            bottom.rectTransform.pivot = Vector2.zero;
            bottom.rectTransform.sizeDelta = new Vector2(0f, 4f);
            left.rectTransform.anchorMin = Vector2.zero;
            left.rectTransform.anchorMax = new Vector2(0f, 1f);
            left.rectTransform.pivot = Vector2.zero;
            left.rectTransform.sizeDelta = new Vector2(4f, 0f);
            right.rectTransform.anchorMin = new Vector2(1f, 0f);
            right.rectTransform.anchorMax = Vector2.one;
            right.rectTransform.pivot = new Vector2(1f, 0f);
            right.rectTransform.sizeDelta = new Vector2(4f, 0f);

            root.gameObject.SetActive(false);
            return new TargetBox(root, top, bottom, left, right);
        }

        TargetCard CreateTargetCard(Transform parent)
        {
            RectTransform root = CreateRect("Vehicle Target Card", parent);
            root.sizeDelta = new Vector2(300f, 240f);
            Image background = CreateImage("Background", root,
                new Color(0.02f, 0.05f, 0.08f, 0.9f));
            background.rectTransform.anchorMin = Vector2.zero;
            background.rectTransform.anchorMax = Vector2.one;
            background.rectTransform.offsetMin = Vector2.zero;
            background.rectTransform.offsetMax = Vector2.zero;
            Image accent = CreateImage("Accent", root, Color.cyan);
            accent.rectTransform.anchorMin = Vector2.zero;
            accent.rectTransform.anchorMax = new Vector2(0f, 1f);
            accent.rectTransform.sizeDelta = new Vector2(8f, 0f);
            TextMeshProUGUI number = CreateText("Number", root, "", 30f);
            TextMeshProUGUI driver = CreateText("Driver", root, "", 38f);
            TextMeshProUGUI team = CreateText("Team", root, "", 23f);
            TextMeshProUGUI rank = CreateText("Rank", root, "", 28f);
            TextMeshProUGUI speed = CreateText("Speed", root, "", 26f);
            Image brakeBadge = CreateImage("Brake Badge", root,
                new Color(0.8f, 0.04f, 0.02f, 0.95f));
            TextMeshProUGUI brake = CreateText("Brake", brakeBadge.transform,
                "BRAKE", 20f);
            SetCardTextRect(number.rectTransform, new Vector2(26f, -26f), new Vector2(240f, 34f));
            SetCardTextRect(driver.rectTransform, new Vector2(26f, -64f), new Vector2(250f, 46f));
            SetCardTextRect(team.rectTransform, new Vector2(26f, -108f), new Vector2(250f, 30f));
            SetCardTextRect(rank.rectTransform, new Vector2(26f, -140f), new Vector2(180f, 28f));
            SetCardTextRect(speed.rectTransform, new Vector2(26f, -172f), new Vector2(220f, 28f));
            SetCardTextRect(brakeBadge.rectTransform, new Vector2(26f, -206f), new Vector2(115f, 26f));
            brake.rectTransform.anchorMin = Vector2.zero;
            brake.rectTransform.anchorMax = Vector2.one;
            brake.rectTransform.offsetMin = Vector2.zero;
            brake.rectTransform.offsetMax = Vector2.zero;
            brake.alignment = TextAlignmentOptions.Center;
            number.font = f1NumberFont;
            driver.font = f1NumberFont;
            rank.font = f1NumberFont;
            speed.font = f1NumberFont;
            brake.font = f1NumberFont;
            root.gameObject.SetActive(false);
            return new TargetCard(root, accent, number, driver, team, rank, speed,
                brakeBadge);
        }

        static void SetCardTextRect(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        void CreateFrame(RectTransform parent)
        {
            CreateCorner(parent, "Top Left", new Vector2(0f, 1f),
                new Vector2(1f, 0f));
            CreateCorner(parent, "Top Right", new Vector2(1f, 1f),
                new Vector2(-1f, 0f));
            CreateCorner(parent, "Bottom Left", new Vector2(0f, 0f),
                new Vector2(1f, 0f));
            CreateCorner(parent, "Bottom Right", new Vector2(1f, 0f),
                new Vector2(-1f, 0f));
        }

        void CreateCorner(RectTransform parent, string name, Vector2 anchor,
            Vector2 horizontalDirection)
        {
            RectTransform corner = CreateRect(name, parent);
            corner.anchorMin = anchor;
            corner.anchorMax = anchor;
            corner.pivot = anchor;
            corner.anchoredPosition = new Vector2(
                anchor.x == 0f ? 34f : -34f,
                anchor.y == 0f ? 34f : -34f);

            Image horizontal = CreateImage("Horizontal", corner, Color.white);
            RectTransform horizontalRect = horizontal.rectTransform;
            horizontalRect.anchorMin = anchor.y == 0f
                ? Vector2.zero
                : new Vector2(0f, 1f);
            horizontalRect.anchorMax = horizontalRect.anchorMin;
            horizontalRect.pivot = new Vector2(
                horizontalDirection.x > 0f ? 0f : 1f,
                anchor.y == 0f ? 0f : 1f);
            horizontalRect.sizeDelta = new Vector2(62f, 4f);

            Image vertical = CreateImage("Vertical", corner, Color.white);
            RectTransform verticalRect = vertical.rectTransform;
            verticalRect.anchorMin = horizontalRect.anchorMin;
            verticalRect.anchorMax = horizontalRect.anchorMin;
            verticalRect.pivot = new Vector2(
                anchor.x == 0f ? 0f : 1f,
                anchor.y == 0f ? 0f : 1f);
            verticalRect.sizeDelta = new Vector2(4f, 62f);
        }

        void CreateHeader(RectTransform parent)
        {
            TextMeshProUGUI label = CreateText("Status", parent, "DRONE CAM", 30f);
            RectTransform rect = label.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -38f);
            rect.sizeDelta = new Vector2(300f, 48f);
            label.alignment = TextAlignmentOptions.Center;
        }

        void CreateCrosshair(RectTransform parent)
        {
            RectTransform crosshair = CreateRect("Crosshair", parent);
            crosshair.anchorMin = new Vector2(0.5f, 0.5f);
            crosshair.anchorMax = new Vector2(0.5f, 0.5f);
            crosshair.pivot = new Vector2(0.5f, 0.5f);
            crosshair.sizeDelta = new Vector2(14f, 14f);

            Image horizontal = CreateImage("Horizontal", crosshair, Color.white);
            horizontal.rectTransform.sizeDelta = new Vector2(14f, 2f);
            Image vertical = CreateImage("Vertical", crosshair, Color.white);
            vertical.rectTransform.sizeDelta = new Vector2(2f, 14f);
        }

        void CreateExitHint(RectTransform parent)
        {
            RectTransform group = CreateRect("Exit Hold Hint", parent);
            group.anchorMin = new Vector2(0.5f, 0f);
            group.anchorMax = new Vector2(0.5f, 0f);
            group.pivot = new Vector2(0.5f, 0f);
            group.anchoredPosition = new Vector2(0f, 38f);
            group.sizeDelta = new Vector2(420f, 54f);

            TextMeshProUGUI label = CreateText("Label", group,
                "HOLD B TO EXIT", 26f);
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 0.5f);
            labelRect.anchorMax = new Vector2(0f, 0.5f);
            labelRect.pivot = new Vector2(0f, 0.5f);
            labelRect.sizeDelta = new Vector2(250f, 44f);

            Image background = CreateImage("Progress Background", group,
                new Color(1f, 1f, 1f, 0.25f));
            RectTransform backgroundRect = background.rectTransform;
            backgroundRect.anchorMin = new Vector2(1f, 0.5f);
            backgroundRect.anchorMax = new Vector2(1f, 0.5f);
            backgroundRect.pivot = new Vector2(1f, 0.5f);
            backgroundRect.sizeDelta = new Vector2(140f, 8f);

            exitProgress = CreateImage("Progress", background.transform,
                new Color(0.95f, 0.15f, 0.18f, 1f));
            exitProgress.type = Image.Type.Filled;
            exitProgress.fillMethod = Image.FillMethod.Horizontal;
            exitProgress.fillOrigin = (int)Image.OriginHorizontal.Left;
            exitProgress.rectTransform.anchorMin = Vector2.zero;
            exitProgress.rectTransform.anchorMax = Vector2.one;
            exitProgress.rectTransform.offsetMin = Vector2.zero;
            exitProgress.rectTransform.offsetMax = Vector2.zero;
        }

        void CreateSpeedometer(RectTransform parent)
        {
            RectTransform group = CreateRect("Speedometer", parent);
            group.anchorMin = new Vector2(0.5f, 0f);
            group.anchorMax = new Vector2(0.5f, 0f);
            group.pivot = new Vector2(0.5f, 0.5f);
            group.anchoredPosition = new Vector2(0f, 200f);
            group.sizeDelta = new Vector2(470f, 470f);
            group.localScale = Vector3.one * SpeedometerScale;

            const float outlineRadius = 205f;
            const float innerOutlineRadius = 150f;
            const float gaugeRadius = 177f;
            const float outlineThickness = 6f;
            const float tickInnerOverlap = 18f;
            float outlineStep = SpeedArcDegrees / (SpeedOutlineSegmentCount - 1);
            for (int i = 0; i < SpeedOutlineSegmentCount; i++)
            {
                float angle = SpeedArcStartDegrees + outlineStep * i;
                Image segment = CreateImage("Outline", group,
                    new Color(1f, 1f, 1f, 0.72f));
                RectTransform segmentRect = segment.rectTransform;
                segmentRect.anchoredPosition = Direction(angle) * outlineRadius;
                segmentRect.sizeDelta = new Vector2(
                    outlineRadius * Mathf.Deg2Rad * outlineStep + 1f,
                    outlineThickness);
                segmentRect.localRotation = Quaternion.Euler(0f, 0f, angle + 90f);
            }

            for (int i = 0; i < SpeedOutlineSegmentCount; i++)
            {
                float angle = SpeedArcStartDegrees + outlineStep * i;
                Image segment = CreateImage("Inner Outline", group,
                    new Color(1f, 1f, 1f, 0.55f));
                RectTransform segmentRect = segment.rectTransform;
                segmentRect.anchoredPosition =
                    Direction(angle) * innerOutlineRadius;
                segmentRect.sizeDelta = new Vector2(
                    innerOutlineRadius * Mathf.Deg2Rad * outlineStep + 1f,
                    outlineThickness);
                segmentRect.localRotation = Quaternion.Euler(0f, 0f, angle + 90f);
            }

            speedMarkers = new Image[SpeedMarkerCount];
            float markerStep = SpeedArcDegrees / (SpeedMarkerCount - 1);
            float markerWidth = speedIndicatorStyle == SpeedIndicatorStyle.Bar
                ? gaugeRadius * Mathf.Deg2Rad * markerStep + 1f
                : 6f;
            for (int i = 0; i < SpeedMarkerCount; i++)
            {
                float angle = SpeedArcStartDegrees + SpeedArcDegrees -
                    markerStep * i;
                Image marker = CreateImage("Speed Marker", group,
                    new Color(0.95f, 0.12f, 0.16f, 1f));
                RectTransform markerRect = marker.rectTransform;
                markerRect.anchoredPosition = Direction(angle) * gaugeRadius;
                markerRect.sizeDelta = new Vector2(markerWidth, 43f);
                markerRect.localRotation = Quaternion.Euler(0f, 0f, angle - 90f);
                marker.enabled = false;
                speedMarkers[i] = marker;
            }

            whiteTicks = new Image[WhiteTickCount];
            float tickStep = SpeedArcDegrees / (WhiteTickCount - 1);
            for (int i = 0; i < WhiteTickCount; i++)
            {
                float angle = SpeedArcStartDegrees + SpeedArcDegrees - tickStep * i;
                Image tick = CreateImage("Speed Tick", group,
                    new Color(1f, 1f, 1f, 0.72f));
                RectTransform tickRect = tick.rectTransform;
                float tickLength = i % 4 == 0 ? 73f : 60f;
                float tickRadius = innerOutlineRadius - tickInnerOverlap +
                    tickLength * 0.5f;
                tickRect.anchoredPosition = Direction(angle) * tickRadius;
                tickRect.sizeDelta = new Vector2(3f, tickLength);
                tickRect.localRotation = Quaternion.Euler(0f, 0f, angle - 90f);
                whiteTicks[i] = tick;
            }

            foreach (Image marker in speedMarkers)
                marker.transform.SetAsLastSibling();

            speedValue = CreateText("Speed Value", group, "0", speedValueFontSize);
            speedValue.font = f1NumberFont;
            speedValue.alignment = TextAlignmentOptions.Center;
            RectTransform valueRect = speedValue.rectTransform;
            valueRect.anchorMin = new Vector2(0.5f, 0.5f);
            valueRect.anchorMax = new Vector2(0.5f, 0.5f);
            valueRect.pivot = new Vector2(0.5f, 0.5f);
            valueRect.anchoredPosition = new Vector2(0f, -5f);
            valueRect.sizeDelta = new Vector2(300f, 145f);

            TextMeshProUGUI unit = CreateText("Speed Unit", group, "KMH", 28f);
            unit.font = f1NumberFont;
            unit.alignment = TextAlignmentOptions.Center;
            unit.color = new Color(1f, 1f, 1f, 0.72f);
            unit.characterSpacing = 6f;
            RectTransform unitRect = unit.rectTransform;
            unitRect.anchorMin = new Vector2(0.5f, 0.5f);
            unitRect.anchorMax = new Vector2(0.5f, 0.5f);
            unitRect.pivot = new Vector2(0.5f, 0.5f);
            unitRect.anchoredPosition = new Vector2(0f, -96f);
            unitRect.sizeDelta = new Vector2(200f, 40f);
        }

        void UpdateSpeedometer()
        {
            bool isHighSpeed = targetSpeedKph >= HighSpeedUpdateThresholdKph;
            if (isHighSpeed && Time.time < nextSpeedUpdateTime)
                return;

            nextSpeedUpdateTime = isHighSpeed
                ? Time.time + highSpeedUpdateInterval
                : 0f;
            displayedSpeedKph = targetSpeedKph;

            int roundedSpeed = Mathf.RoundToInt(displayedSpeedKph);
            if (roundedSpeed != displayedSpeed)
            {
                displayedSpeed = roundedSpeed;
                speedValue.text = roundedSpeed.ToString();
                speedValue.color = roundedSpeed > SpeedWarningKph
                    ? new Color(0.95f, 0.12f, 0.16f, 1f)
                    : Color.white;
            }

            float normalizedSpeed = Mathf.Clamp01(
                displayedSpeedKph / maxDisplaySpeedKph);
            int markerCount = normalizedSpeed <= 0f
                ? 0
                : Mathf.Clamp(
                    Mathf.RoundToInt(normalizedSpeed *
                        (SpeedMarkerCount - 1)) + 1,
                    1,
                    SpeedMarkerCount);
            if (markerCount == activeSpeedMarkers)
                return;

            activeSpeedMarkers = markerCount;
            for (int i = 0; i < speedMarkers.Length; i++)
                speedMarkers[i].enabled = i < markerCount;
        }

        void ResetSpeedometer()
        {
            targetSpeedKph = 0f;
            displayedSpeedKph = 0f;
            nextSpeedUpdateTime = 0f;
            displayedSpeed = -1;
            activeSpeedMarkers = -1;

            if (speedValue != null)
            {
                speedValue.text = "0";
                speedValue.color = Color.white;
            }

            if (speedMarkers != null)
            {
                foreach (Image marker in speedMarkers)
                    marker.enabled = false;
            }

            if (whiteTicks == null)
                return;

            foreach (Image tick in whiteTicks)
                tick.enabled = true;
        }

        sealed class TargetBox
        {
            readonly RectTransform root;
            readonly Image[] borders;

            public TargetBox(RectTransform root, params Image[] borders)
            {
                this.root = root;
                this.borders = borders;
            }

            public void SetActive(bool active)
            {
                root.gameObject.SetActive(active);
            }

            public void SetTarget(DroneVehicleTarget target)
            {
                root.anchorMin = target.viewportRect.min;
                root.anchorMax = target.viewportRect.max;
                root.offsetMin = Vector2.zero;
                root.offsetMax = Vector2.zero;
                foreach (Image border in borders)
                    border.color = target.teamColor;
            }
        }

        sealed class TargetCard
        {
            readonly RectTransform root;
            readonly Image accent;
            readonly TextMeshProUGUI number;
            readonly TextMeshProUGUI driver;
            readonly TextMeshProUGUI team;
            readonly TextMeshProUGUI rank;
            readonly TextMeshProUGUI speed;
            readonly Image brakeBadge;

            public TargetCard(
                RectTransform root,
                Image accent,
                TextMeshProUGUI number,
                TextMeshProUGUI driver,
                TextMeshProUGUI team,
                TextMeshProUGUI rank,
                TextMeshProUGUI speed,
                Image brakeBadge)
            {
                this.root = root;
                this.accent = accent;
                this.number = number;
                this.driver = driver;
                this.team = team;
                this.rank = rank;
                this.speed = speed;
                this.brakeBadge = brakeBadge;
            }

            public void SetActive(bool active)
            {
                root.gameObject.SetActive(active);
            }

            public void SetTarget(DroneVehicleTarget target)
            {
                float x = target.viewportRect.xMax * CanvasSize.x + 26f;
                bool placeLeft = x + root.sizeDelta.x > CanvasSize.x - 24f;
                if (placeLeft)
                    x = target.viewportRect.xMin * CanvasSize.x -
                        root.sizeDelta.x - 26f;

                root.anchorMin = Vector2.zero;
                root.anchorMax = Vector2.zero;
                root.pivot = Vector2.zero;
                root.anchoredPosition = new Vector2(
                    Mathf.Clamp(x, 24f, CanvasSize.x - root.sizeDelta.x - 24f),
                    Mathf.Clamp(
                        target.viewportRect.yMin * CanvasSize.y,
                        24f,
                        CanvasSize.y - root.sizeDelta.y - 24f));
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
        }

        static Vector2 Direction(float angle)
        {
            float radians = angle * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }

        static RectTransform CreateRect(string name, Transform parent)
        {
            GameObject gameObject = new(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);
            return gameObject.GetComponent<RectTransform>();
        }

        static Image CreateImage(string name, Transform parent, Color color)
        {
            GameObject gameObject = new(name, typeof(RectTransform), typeof(Image));
            gameObject.transform.SetParent(parent, false);
            Image image = gameObject.GetComponent<Image>();
            image.color = color;
            return image;
        }

        static TextMeshProUGUI CreateText(string name, Transform parent,
            string content, float fontSize)
        {
            GameObject gameObject = new(name, typeof(RectTransform),
                typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            gameObject.transform.SetParent(parent, false);
            TextMeshProUGUI text = gameObject.GetComponent<TextMeshProUGUI>();
            text.font = TMP_Settings.defaultFontAsset;
            text.text = content;
            text.fontSize = fontSize;
            text.color = Color.white;
            return text;
        }
    }
}
