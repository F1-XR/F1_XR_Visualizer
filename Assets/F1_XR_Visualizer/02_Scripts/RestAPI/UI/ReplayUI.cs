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

        public Button[] speedButtons;
        public float[] speedValues = { 0.5f, 1f, 2f, 4f };
        public TMP_Text speedLabel;

        public TMP_Text timeLabel;

        private void Awake()
        {
            if (bar != null && bar.player == null)
                bar.player = player;
        }

        private void OnEnable()
        {
            if (playPauseButton != null)
                playPauseButton.onClick.AddListener(TogglePlayPause);

            for (int i = 0; i < speedButtons.Length && i < speedValues.Length; i++)
            {
                int index = i;
                if (speedButtons[index] != null)
                    speedButtons[index].onClick.AddListener(() => SetSpeed(speedValues[index]));
            }
        }

        private void OnDisable()
        {
            if (playPauseButton != null)
                playPauseButton.onClick.RemoveListener(TogglePlayPause);

            for (int i = 0; i < speedButtons.Length; i++)
            {
                if (speedButtons[i] != null)
                    speedButtons[i].onClick.RemoveAllListeners();
            }
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

            if (speedLabel != null)
                speedLabel.text = $"{player.playbackSpeed:0.##}x";

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
    }
}