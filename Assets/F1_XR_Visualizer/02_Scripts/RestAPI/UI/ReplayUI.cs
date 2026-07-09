using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using F1XR.RestAPI.Replay;
using F1XR.RestAPI.Api;

namespace F1XR.RestAPI.UI
{
    public partial class ReplayUI : MonoBehaviour
    {
        public ChunkReplayPlayer player;
        [FormerlySerializedAs("progressBar")]
        [FormerlySerializedAs("_bar")]
        public ReplayBar bar;

        public Button playPauseButton;
        public TMP_Text playPauseLabel;

        public TMP_Dropdown speedDropdown;
        public float[] speedValues = { 0.5f, 1f, 2f, 4f, 6f, 8f, 16f };

        public TMP_Text timeLabel;
        public TMP_Text rankText;
        public Vector2 standingsSize = new Vector2(196f, 470f);
        public Vector2 driverDetailSize = new Vector2(180f, 150f);
        public Vector2 driverOnboardSize = new Vector2(260f, 176f);
        public Vector2 driverOnboardTextureSize = new Vector2(512f, 288f);
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
        private RectTransform driverOnboardRoot;
        private RawImage driverOnboardImage;
        private TMP_Text driverOnboardTitle;
        private DriverOnboardView driverOnboardView;
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
                speedDropdown.value = DefaultSpeedIndex();
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

        private int DefaultSpeedIndex()
        {
            if (speedValues == null || speedValues.Length == 0)
                return 0;

            float speed = player != null ? player.playbackSpeed : 6f;
            int bestIndex = 0;
            float bestDistance = Mathf.Abs(speedValues[0] - speed);

            for (int i = 1; i < speedValues.Length; i++)
            {
                float distance = Mathf.Abs(speedValues[i] - speed);
                if (distance >= bestDistance)
                    continue;

                bestIndex = i;
                bestDistance = distance;
            }

            return bestIndex;
        }

    }
}
