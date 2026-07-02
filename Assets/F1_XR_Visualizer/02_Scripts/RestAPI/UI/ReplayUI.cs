using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using F1XR.RestAPI.Replay;

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

        private void Awake()
        {
            if (bar != null && bar.player == null)
                bar.player = player;
        }
        
        private void Start()
        {
            if (speedDropdown == null)
                return;

            speedDropdown.ClearOptions();

            List<string> labels = new();
            foreach (float speed in speedValues)
                labels.Add($"{speed:0.##}x");

            speedDropdown.AddOptions(labels);
            speedDropdown.value = 1;
            speedDropdown.RefreshShownValue();

            SetSpeedIndex(speedDropdown.value);
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
    }
}