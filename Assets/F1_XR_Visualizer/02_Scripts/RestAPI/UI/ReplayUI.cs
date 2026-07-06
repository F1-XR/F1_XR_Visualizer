using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using F1XR.RestAPI.Replay;
using F1XR.RestAPI.Api;

namespace F1XR.RestAPI.UI
{
    public class ReplayUI : MonoBehaviour
    {
        public ChunkReplayPlayer player;
        [FormerlySerializedAs("progressBar")]
        [FormerlySerializedAs("_bar")]
        public ReplayBar bar;

        public Button playPauseButton;
        public TMP_Text playPauseLabel;

        public TMP_Dropdown speedDropdown;
        public float[] speedValues = { 0.5f, 1f, 2f, 4f, 8f, 16f };

        public TMP_Text timeLabel;
        public TMP_Text rankText;
        public Vector2 standingsSize = new Vector2(142f, 470f);
        public Vector2 driverDetailSize = new Vector2(180f, 150f);
        public float standingsRowHeight = 19f;
        public float positionChangeFlashSeconds = 1.2f;
        public float standingsMoveSpeed = 10f;

        private RectTransform standingsRoot;
        private RectTransform driverDetailRoot;
        private Image driverDetailTeamBar;
        private TMP_Text driverDetailName;
        private TMP_Text driverDetailNumber;
        private TMP_Text driverDetailTeam;
        private TMP_Text driverDetailPosition;
        private TMP_Text driverDetailTire;
        private TMP_Text lapHeader;
        private TMP_Text tireHeader;
        private readonly List<StandingRow> standingRows = new();
        private readonly Dictionary<int, StandingRow> rowsByDriver = new();
        private readonly Dictionary<int, int> lastPositions = new();
        private readonly Dictionary<int, PositionChange> positionChanges = new();
        private float lastStandingsTime = -1f;
        private int selectedDriverNumber;
        private bool controlsStyled;

        private void Awake()
        {
            if (bar != null && bar.player == null)
                bar.player = player;
        }
        
        private void Start()
        {
            if (speedDropdown != null)
            {
                speedDropdown.ClearOptions();

                List<string> labels = new();
                foreach (float speed in speedValues)
                    labels.Add($"{speed:0.##}x");

                speedDropdown.AddOptions(labels);
                speedDropdown.value = 1;
                speedDropdown.RefreshShownValue();

                SetSpeedIndex(speedDropdown.value);
            }

            StyleControls();
        }

        private void OnEnable()
        {
            if (playPauseButton != null)
                playPauseButton.onClick.AddListener(TogglePlayPause);

            if (speedDropdown != null)
                speedDropdown.onValueChanged.AddListener(SetSpeedIndex);
        }

        private void OnDisable()
        {
            if (playPauseButton != null)
                playPauseButton.onClick.RemoveListener(TogglePlayPause);

            if (speedDropdown != null)
                speedDropdown.onValueChanged.RemoveListener(SetSpeedIndex);
        }

        private void Update()
        {
            Refresh();
        }

        public void TogglePlayPause()
        {
            if (player == null)
                return;

            player.TogglePlay();
            Refresh();
        }

        public void SetSpeed(float speed)
        {
            if (player == null)
                return;

            player.SetSpeed(speed);
            Refresh();
        }
        
        private static string TireSymbol(string compound)
        {
            if (string.IsNullOrWhiteSpace(compound))
                return "-";

            string value = compound.ToUpperInvariant();

            if (value.StartsWith("SOFT"))
                return "S";

            if (value.StartsWith("MEDIUM"))
                return "M";

            if (value.StartsWith("HARD"))
                return "H";

            if (value.StartsWith("INTER"))
                return "I";

            if (value.StartsWith("WET"))
                return "W";

            return value.Length > 0 ? value.Substring(0, 1) : "-";
        }

        private static Color TireColor(string compound)
        {
            if (string.IsNullOrWhiteSpace(compound))
                return new Color(0.55f, 0.6f, 0.68f);

            string value = compound.ToUpperInvariant();

            if (value.StartsWith("SOFT"))
                return new Color(0.95f, 0.08f, 0.08f);

            if (value.StartsWith("MEDIUM"))
                return new Color(1f, 0.84f, 0.08f);

            if (value.StartsWith("HARD"))
                return new Color(0.95f, 0.95f, 0.95f);

            if (value.StartsWith("INTER"))
                return new Color(0.1f, 0.78f, 0.28f);

            if (value.StartsWith("WET"))
                return new Color(0.1f, 0.42f, 1f);

            return new Color(0.55f, 0.6f, 0.68f);
        }

        public void Refresh()
        {
            if (player == null)
                return;

            if (playPauseLabel != null)
                playPauseLabel.text = player.IsPlaying ? "Pause" : "Play";

            if (timeLabel != null)
                timeLabel.text = $"{FormatTime(player.CurrentTime)} / {FormatTime(player.Duration)}";

            if (bar != null)
                bar.Refresh();

            StyleControls();
            RefreshStandings(player.GetPositions());
        }

        private static string FormatTime(float seconds)
        {
            seconds = Mathf.Max(0f, seconds);
            int totalSeconds = Mathf.FloorToInt(seconds);
            int minutes = totalSeconds / 60;
            int secs = totalSeconds % 60;

            return $"{minutes:00}:{secs:00}";
        }
        
        private void SetSpeedIndex(int index)
        {
            if (speedValues == null || index < 0 || index >= speedValues.Length)
                return;

            SetSpeed(speedValues[index]);
        }

        private void StyleControls()
        {
            if (controlsStyled)
                return;

            controlsStyled = true;

            StyleControlPanel();
            StyleButton(playPauseButton);
            StyleDropdown(speedDropdown);
            StyleReplayBar();

            if (timeLabel != null)
            {
                timeLabel.fontSize = 28f;
                timeLabel.fontStyle = FontStyles.Bold;
                timeLabel.color = new Color(0.92f, 0.94f, 0.98f);
                timeLabel.alignment = TextAlignmentOptions.Center;
            }
        }

        private void StyleControlPanel()
        {
            Transform parent = playPauseButton != null
                ? playPauseButton.transform.parent
                : transform;

            if (parent == null)
                return;

            Image panel = parent.GetComponent<Image>();
            if (panel == null)
                return;

            panel.color = new Color(0.015f, 0.018f, 0.026f, 0.82f);
        }

        private static void StyleButton(Button button)
        {
            if (button == null)
                return;

            Image image = button.GetComponent<Image>();
            if (image != null)
            {
                image.color = new Color(0.09f, 0.1f, 0.13f, 0.95f);
                button.targetGraphic = image;
            }

            ColorBlock colors = button.colors;
            colors.normalColor = new Color(0.09f, 0.1f, 0.13f, 0.95f);
            colors.highlightedColor = new Color(0.16f, 0.18f, 0.24f, 0.98f);
            colors.pressedColor = new Color(0.62f, 0.04f, 0.07f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.05f, 0.055f, 0.07f, 0.65f);
            button.colors = colors;

            TMP_Text label = button.GetComponentInChildren<TMP_Text>();
            if (label == null)
                return;

            label.fontSize = 20f;
            label.fontStyle = FontStyles.Bold;
            label.color = new Color(0.92f, 0.94f, 0.98f);
            label.alignment = TextAlignmentOptions.Center;
        }

        private static void StyleDropdown(TMP_Dropdown dropdown)
        {
            if (dropdown == null)
                return;

            if (dropdown.targetGraphic is Image target)
                target.color = new Color(0.09f, 0.1f, 0.13f, 0.95f);

            ColorBlock colors = dropdown.colors;
            colors.normalColor = new Color(0.09f, 0.1f, 0.13f, 0.95f);
            colors.highlightedColor = new Color(0.16f, 0.18f, 0.24f, 0.98f);
            colors.pressedColor = new Color(0.62f, 0.04f, 0.07f, 1f);
            colors.selectedColor = colors.highlightedColor;
            dropdown.colors = colors;

            if (dropdown.captionText != null)
            {
                dropdown.captionText.fontSize = 16f;
                dropdown.captionText.fontStyle = FontStyles.Bold;
                dropdown.captionText.color = new Color(0.92f, 0.94f, 0.98f);
            }

            if (dropdown.itemText != null)
            {
                dropdown.itemText.fontSize = 15f;
                dropdown.itemText.color = new Color(0.92f, 0.94f, 0.98f);
            }

            StyleDropdownTemplate(dropdown);
        }

        private static void StyleDropdownTemplate(TMP_Dropdown dropdown)
        {
            if (dropdown.template == null)
                return;

            foreach (Image image in dropdown.template.GetComponentsInChildren<Image>(true))
            {
                image.color = image.gameObject.name.Contains("Checkmark")
                    ? new Color(0.92f, 0.94f, 0.98f, 1f)
                    : new Color(0.05f, 0.055f, 0.075f, 0.98f);
            }

            foreach (TMP_Text text in dropdown.template.GetComponentsInChildren<TMP_Text>(true))
            {
                text.fontSize = 15f;
                text.color = new Color(0.92f, 0.94f, 0.98f);
            }
        }

        private void StyleReplayBar()
        {
            if (bar == null)
                return;

            Image background = bar.barRect != null
                ? bar.barRect.GetComponent<Image>()
                : bar.GetComponent<Image>();

            if (background != null)
                background.color = new Color(0.11f, 0.12f, 0.15f, 0.92f);

            if (bar.loadedFill != null)
                bar.loadedFill.color = new Color(0.62f, 0.64f, 0.68f, 0.88f);

            if (bar.playedFill != null)
                bar.playedFill.color = new Color(0.95f, 0.08f, 0.08f, 0.96f);
        }

        private void RefreshStandings(List<PositionSampleDto> positions)
        {
            EnsureStandings();

            if (standingsRoot == null)
                return;

            if (lapHeader != null)
                lapHeader.text = $"LAP  {FormatTime(player.CurrentTime)}";

            if (lastStandingsTime >= 0f && Mathf.Abs(player.CurrentTime - lastStandingsTime) > 2f)
            {
                lastPositions.Clear();
                positionChanges.Clear();
            }

            lastStandingsTime = player.CurrentTime;

            HashSet<int> visibleDrivers = new();

            if (positions != null)
            {
                for (int i = 0; i < positions.Count; i++)
                {
                    PositionSampleDto position = positions[i];
                    TireSampleDto tire = player.GetTire(position.driverNumber);
                    string compound = tire != null ? tire.compound : "";
                    PositionChange change = UpdatePositionChange(position.driverNumber, position.position);
                    StandingRow row = GetStandingRow(position.driverNumber);

                    if (row == null)
                        continue;

                    visibleDrivers.Add(position.driverNumber);
                    row.Root.SetActive(true);
                    row.SetSlot(i, standingsRowHeight, standingsMoveSpeed);
                    row.Set(
                        position.position,
                        player.GetDriverLabel(position.driverNumber),
                        player.GetDriverColor(position.driverNumber),
                        TireSymbol(compound),
                        TireColor(compound),
                        tire != null && tire.tireAge > 0 ? $"{tire.tireAge} L" : "",
                        change
                    );
                }
            }

            foreach (StandingRow row in standingRows)
            {
                if (row.DriverNumber == 0 || visibleDrivers.Contains(row.DriverNumber))
                    continue;

                row.Root.SetActive(false);
            }

            RefreshDriverDetail(positions);
        }

        private StandingRow GetStandingRow(int driverNumber)
        {
            if (rowsByDriver.TryGetValue(driverNumber, out StandingRow row))
                return row;

            foreach (StandingRow candidate in standingRows)
            {
                if (candidate.DriverNumber != 0)
                    continue;

                candidate.DriverNumber = driverNumber;
                rowsByDriver.Add(driverNumber, candidate);
                return candidate;
            }

            return null;
        }

        private PositionChange UpdatePositionChange(int driverNumber, int currentPosition)
        {
            PositionChange change = null;

            if (positionChanges.TryGetValue(driverNumber, out PositionChange activeChange))
            {
                if (Time.time <= activeChange.endTime)
                    change = activeChange;
                else
                    positionChanges.Remove(driverNumber);
            }

            if (lastPositions.TryGetValue(driverNumber, out int previousPosition) &&
                previousPosition != currentPosition)
            {
                change = new PositionChange
                {
                    improved = currentPosition < previousPosition,
                    places = Mathf.Abs(previousPosition - currentPosition),
                    endTime = Time.time + positionChangeFlashSeconds,
                };

                positionChanges[driverNumber] = change;
            }

            lastPositions[driverNumber] = currentPosition;
            return change;
        }

        private void EnsureStandings()
        {
            if (standingsRoot != null || rankText == null)
                return;

            RectTransform source = rankText.rectTransform;
            rankText.gameObject.SetActive(false);

            standingsRoot = new GameObject("StandingsPanel", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            standingsRoot.SetParent(source.parent, false);
            standingsRoot.anchorMin = source.anchorMin;
            standingsRoot.anchorMax = source.anchorMax;
            standingsRoot.anchoredPosition = source.anchoredPosition;
            standingsRoot.sizeDelta = standingsSize;
            standingsRoot.pivot = source.pivot;
            standingsRoot.localRotation = source.localRotation;
            standingsRoot.localScale = Vector3.one;
            standingsRoot.SetSiblingIndex(source.GetSiblingIndex());

            Image panel = standingsRoot.GetComponent<Image>();
            panel.color = new Color(0.015f, 0.018f, 0.026f, 0.82f);

            lapHeader = CreateText("Header", standingsRoot, 18, FontStyles.Bold, TextAlignmentOptions.Center);
            lapHeader.color = new Color(0.82f, 0.86f, 0.92f);
            SetRect(lapHeader.rectTransform, 8f, -8f, standingsSize.x - 16f, 28f);

            tireHeader = CreateText("TireHeader", standingsRoot, 8, FontStyles.Bold, TextAlignmentOptions.Right);
            tireHeader.text = "CURRENT TYRE AGE (LAPS)";
            tireHeader.color = new Color(1f, 0.2f, 0.12f);
            SetRect(tireHeader.rectTransform, 8f, -36f, standingsSize.x - 16f, 12f);

            for (int i = 0; i < 20; i++)
            {
                StandingRow row = CreateStandingRow(standingsRoot, i, standingsSize.x - 16f, standingsRowHeight);
                row.Root.SetActive(false);
                standingRows.Add(row);
            }

            EnsureDriverDetailPanel();
        }

        private StandingRow CreateStandingRow(Transform parent, int index, float rowWidth, float rowHeight)
        {
            RectTransform root = new GameObject($"StandingRow_{index + 1}", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement)).GetComponent<RectTransform>();
            root.SetParent(parent, false);
            SetRect(root, 8f, RowY(index, rowHeight), rowWidth, rowHeight);

            Image background = root.GetComponent<Image>();
            background.color = index % 2 == 0
                ? new Color(0.08f, 0.09f, 0.12f, 0.92f)
                : new Color(0.04f, 0.045f, 0.065f, 0.92f);

            Button button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(0.16f, 0.18f, 0.24f, 0.96f);
            colors.pressedColor = new Color(0.22f, 0.24f, 0.32f, 0.98f);
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;

            HorizontalLayoutGroup layout = root.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(5, 6, 1, 1);
            layout.spacing = 5f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            root.GetComponent<LayoutElement>().preferredHeight = rowHeight;

            TMP_Text position = CreateText("Position", root, 14, FontStyles.Bold, TextAlignmentOptions.Center);
            position.gameObject.AddComponent<LayoutElement>().preferredWidth = 24f;

            Image teamBar = new GameObject("TeamColor", typeof(RectTransform), typeof(Image), typeof(LayoutElement)).GetComponent<Image>();
            teamBar.transform.SetParent(root, false);
            LayoutElement teamBarLayout = teamBar.GetComponent<LayoutElement>();
            teamBarLayout.preferredWidth = 3f;

            TMP_Text driver = CreateText("Driver", root, 15, FontStyles.Bold, TextAlignmentOptions.Left);
            driver.gameObject.AddComponent<LayoutElement>().preferredWidth = 38f;

            Image tireDot = new GameObject("TireDot", typeof(RectTransform), typeof(Image), typeof(LayoutElement)).GetComponent<Image>();
            tireDot.transform.SetParent(root, false);
            LayoutElement tireDotLayout = tireDot.GetComponent<LayoutElement>();
            tireDotLayout.preferredWidth = 14f;

            TMP_Text tire = CreateText("Tire", tireDot.transform, 12, FontStyles.Bold, TextAlignmentOptions.Center);
            RectTransform tireRect = tire.rectTransform;
            tireRect.anchorMin = Vector2.zero;
            tireRect.anchorMax = Vector2.one;
            tireRect.offsetMin = Vector2.zero;
            tireRect.offsetMax = Vector2.zero;
            tire.color = Color.black;

            TMP_Text age = CreateText("Age", root, 14, FontStyles.Normal, TextAlignmentOptions.Right);
            age.gameObject.AddComponent<LayoutElement>().preferredWidth = 32f;

            TMP_Text change = CreateText("Change", root, 12, FontStyles.Bold, TextAlignmentOptions.Center);
            change.gameObject.AddComponent<LayoutElement>().preferredWidth = 16f;

            StandingRow row = null;
            button.onClick.AddListener(() =>
            {
                if (row != null)
                    ShowDriverDetail(row.DriverNumber);
            });

            row = new StandingRow(root.gameObject, root, background, background.color, position, teamBar, driver, tireDot, tire, age, change);
            return row;
        }

        private void EnsureDriverDetailPanel()
        {
            if (driverDetailRoot != null || standingsRoot == null)
                return;

            driverDetailRoot = new GameObject("DriverDetailPanel", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            driverDetailRoot.SetParent(standingsRoot.parent, false);
            driverDetailRoot.anchorMin = standingsRoot.anchorMin;
            driverDetailRoot.anchorMax = standingsRoot.anchorMax;
            driverDetailRoot.pivot = standingsRoot.pivot;
            driverDetailRoot.anchoredPosition = standingsRoot.anchoredPosition + new Vector2(standingsSize.x + 8f, 0f);
            driverDetailRoot.sizeDelta = driverDetailSize;
            driverDetailRoot.localRotation = standingsRoot.localRotation;
            driverDetailRoot.localScale = Vector3.one;
            driverDetailRoot.SetSiblingIndex(standingsRoot.GetSiblingIndex() + 1);

            Image panel = driverDetailRoot.GetComponent<Image>();
            panel.color = new Color(0.015f, 0.018f, 0.026f, 0.9f);

            driverDetailTeamBar = new GameObject("TeamColor", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            driverDetailTeamBar.transform.SetParent(driverDetailRoot, false);
            SetRect(driverDetailTeamBar.rectTransform, 0f, 0f, 5f, driverDetailSize.y);

            driverDetailName = CreateText("DriverName", driverDetailRoot, 18, FontStyles.Bold, TextAlignmentOptions.Left);
            SetRect(driverDetailName.rectTransform, 14f, -10f, driverDetailSize.x - 26f, 24f);

            driverDetailNumber = CreateText("DriverNumber", driverDetailRoot, 13, FontStyles.Bold, TextAlignmentOptions.Left);
            driverDetailNumber.color = new Color(0.82f, 0.86f, 0.92f);
            SetRect(driverDetailNumber.rectTransform, 14f, -36f, driverDetailSize.x - 26f, 18f);

            driverDetailTeam = CreateText("Team", driverDetailRoot, 12, FontStyles.Normal, TextAlignmentOptions.Left);
            driverDetailTeam.color = new Color(0.72f, 0.76f, 0.82f);
            SetRect(driverDetailTeam.rectTransform, 14f, -58f, driverDetailSize.x - 26f, 18f);

            driverDetailPosition = CreateText("Position", driverDetailRoot, 14, FontStyles.Bold, TextAlignmentOptions.Left);
            SetRect(driverDetailPosition.rectTransform, 14f, -88f, driverDetailSize.x - 26f, 20f);

            driverDetailTire = CreateText("Tire", driverDetailRoot, 14, FontStyles.Bold, TextAlignmentOptions.Left);
            SetRect(driverDetailTire.rectTransform, 14f, -114f, driverDetailSize.x - 26f, 20f);

            Button close = new GameObject("Close", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Button>();
            close.transform.SetParent(driverDetailRoot, false);
            SetRect(close.GetComponent<RectTransform>(), driverDetailSize.x - 26f, -6f, 20f, 20f);
            close.targetGraphic = close.GetComponent<Image>();
            close.GetComponent<Image>().color = new Color(0.14f, 0.15f, 0.2f, 0.9f);
            close.onClick.AddListener(HideDriverDetail);

            TMP_Text closeText = CreateText("Label", close.transform, 12, FontStyles.Bold, TextAlignmentOptions.Center);
            closeText.text = "X";
            closeText.rectTransform.anchorMin = Vector2.zero;
            closeText.rectTransform.anchorMax = Vector2.one;
            closeText.rectTransform.offsetMin = Vector2.zero;
            closeText.rectTransform.offsetMax = Vector2.zero;

            driverDetailRoot.gameObject.SetActive(false);
        }

        private void ShowDriverDetail(int driverNumber)
        {
            if (driverNumber <= 0)
                return;

            selectedDriverNumber = driverNumber;
            EnsureDriverDetailPanel();

            if (driverDetailRoot != null)
                driverDetailRoot.gameObject.SetActive(true);

            RefreshDriverDetail(player != null ? player.GetPositions() : null);
        }

        private void HideDriverDetail()
        {
            selectedDriverNumber = 0;

            if (driverDetailRoot != null)
                driverDetailRoot.gameObject.SetActive(false);

            foreach (StandingRow row in standingRows)
                row.SetSelected(false);
        }

        private void RefreshDriverDetail(List<PositionSampleDto> positions)
        {
            if (selectedDriverNumber <= 0 || player == null || driverDetailRoot == null)
                return;

            DriverInfoDto driver = player.GetDriverInfo(selectedDriverNumber);
            TireSampleDto tire = player.GetTire(selectedDriverNumber);
            PositionSampleDto position = FindPosition(positions, selectedDriverNumber);

            driverDetailTeamBar.color = player.GetDriverColor(selectedDriverNumber);
            driverDetailName.text = DriverName(driver, selectedDriverNumber);
            driverDetailNumber.text = $"#{selectedDriverNumber}  {player.GetDriverLabel(selectedDriverNumber)}";
            driverDetailTeam.text = driver != null && !string.IsNullOrWhiteSpace(driver.teamName)
                ? driver.teamName
                : "Team -";
            driverDetailPosition.text = position != null
                ? $"P{position.position}"
                : "P-";

            if (tire != null)
            {
                string compound = string.IsNullOrWhiteSpace(tire.compound) ? "-" : tire.compound.ToUpperInvariant();
                string age = tire.tireAge > 0 ? $"{tire.tireAge} L" : "-";
                driverDetailTire.text = $"{compound}  {age}";
                driverDetailTire.color = TireColor(tire.compound);
            }
            else
            {
                driverDetailTire.text = "TYRE -";
                driverDetailTire.color = new Color(0.72f, 0.76f, 0.82f);
            }

            foreach (StandingRow row in standingRows)
                row.SetSelected(row.DriverNumber == selectedDriverNumber);
        }

        private static PositionSampleDto FindPosition(List<PositionSampleDto> positions, int driverNumber)
        {
            if (positions == null)
                return null;

            foreach (PositionSampleDto position in positions)
            {
                if (position.driverNumber == driverNumber)
                    return position;
            }

            return null;
        }

        private static string DriverName(DriverInfoDto driver, int driverNumber)
        {
            if (driver == null)
                return $"Driver #{driverNumber}";

            if (!string.IsNullOrWhiteSpace(driver.fullName))
                return driver.fullName;

            if (!string.IsNullOrWhiteSpace(driver.nameAcronym))
                return driver.nameAcronym;

            return $"Driver #{driverNumber}";
        }

        private static float RowY(int slotIndex, float rowHeight)
        {
            return -50f - slotIndex * (rowHeight + 2f);
        }

        private static void SetRect(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static TMP_Text CreateText(string name, Transform parent, int size, FontStyles style, TextAlignmentOptions alignment)
        {
            TMP_Text text = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
            text.transform.SetParent(parent, false);
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = Color.white;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Truncate;
            return text;
        }

        private class StandingRow
        {
            public readonly GameObject Root;
            public int DriverNumber;

            private readonly RectTransform rect;
            private readonly Image background;
            private readonly Color defaultBackground;
            private readonly TMP_Text position;
            private readonly Image teamBar;
            private readonly TMP_Text driver;
            private readonly Image tireDot;
            private readonly TMP_Text tire;
            private readonly TMP_Text age;
            private readonly TMP_Text change;
            private bool hasSlot;
            private bool selected;

            public StandingRow(GameObject root, RectTransform rect, Image background, Color defaultBackground, TMP_Text position, Image teamBar, TMP_Text driver, Image tireDot, TMP_Text tire, TMP_Text age, TMP_Text change)
            {
                Root = root;
                this.rect = rect;
                this.background = background;
                this.defaultBackground = defaultBackground;
                this.position = position;
                this.teamBar = teamBar;
                this.driver = driver;
                this.tireDot = tireDot;
                this.tire = tire;
                this.age = age;
                this.change = change;
            }

            public void SetSelected(bool value)
            {
                selected = value;

                if (selected)
                    background.color = new Color(0.16f, 0.18f, 0.24f, 0.96f);
            }

            public void SetSlot(int slotIndex, float rowHeight, float moveSpeed)
            {
                Vector2 target = new Vector2(8f, RowY(slotIndex, rowHeight));

                if (!hasSlot)
                {
                    rect.anchoredPosition = target;
                    hasSlot = true;
                    return;
                }

                float t = 1f - Mathf.Exp(-moveSpeed * Time.deltaTime);
                rect.anchoredPosition = Vector2.Lerp(rect.anchoredPosition, target, t);
            }

            public void Set(int rank, string driverLabel, Color teamColor, string tireSymbol, Color tireColor, string tireAge, PositionChange positionChange)
            {
                position.text = rank.ToString();
                driver.text = driverLabel;
                teamBar.color = teamColor;
                tireDot.color = tireColor;
                tire.text = string.IsNullOrWhiteSpace(tireSymbol) ? "-" : tireSymbol;
                age.text = tireAge;

                if (positionChange != null)
                {
                    bool improved = positionChange.improved;
                    background.color = improved
                        ? new Color(0.02f, 0.28f, 0.13f, 0.96f)
                        : new Color(0.42f, 0.04f, 0.06f, 0.96f);
                    change.text = improved ? $"+{positionChange.places}" : $"-{positionChange.places}";
                    change.color = improved ? new Color(0.25f, 1f, 0.45f) : new Color(1f, 0.22f, 0.18f);
                    return;
                }

                change.text = "";
                background.color = rank == 1
                    ? new Color(0.18f, 0.02f, 0.035f, 0.95f)
                    : selected
                        ? new Color(0.16f, 0.18f, 0.24f, 0.96f)
                        : defaultBackground;
            }
        }

        private class PositionChange
        {
            public bool improved;
            public int places;
            public float endTime;
        }
    }
}
