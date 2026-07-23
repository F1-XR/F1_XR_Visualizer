using TMPro;
using UnityEngine;
using UnityEngine.UI;
using F1XR.RestAPI.Replay;

namespace F1XR.RestAPI.UI
{
    public partial class ReplayUI
    {
        private RectTransform eventControls;
        private TMP_Text eventStatus;
        private Button eventOpenButton;
        private Button eventPlayButton;
        private Button eventRestartButton;
        private Button eventCloseButton;
        private Slider eventSlider;
        private bool refreshingEventSlider;

        private void EnsureEventControls()
        {
            if (eventControls != null)
                return;

            eventControls = new GameObject(
                "EventReplayControls",
                typeof(RectTransform),
                typeof(Image))
                .GetComponent<RectTransform>();
            eventControls.SetParent(transform, false);
            eventControls.anchorMin = new Vector2(0.5f, 1f);
            eventControls.anchorMax = new Vector2(0.5f, 1f);
            eventControls.pivot = new Vector2(0.5f, 1f);
            eventControls.anchoredPosition = new Vector2(0f, 372f);
            eventControls.sizeDelta = new Vector2(300f, 142f);
            eventControls.GetComponent<Image>().color = new Color(0.015f, 0.018f, 0.026f, 0.92f);

            eventStatus = CreateText(
                "Status",
                eventControls,
                14,
                FontStyles.Bold,
                TextAlignmentOptions.Center);
            SetRect(eventStatus.rectTransform, 8f, -8f, 284f, 26f);

            eventOpenButton = CreateEventButton("Open", 8f, -42f, 284f, OpenTestEvent);
            eventPlayButton = CreateEventButton("Play", 8f, -42f, 86f, ToggleEventPlay);
            eventRestartButton = CreateEventButton("Restart", 104f, -42f, 86f, RestartEvent);
            eventCloseButton = CreateEventButton("Close", 200f, -42f, 92f, CloseEvent);
            eventSlider = CreateEventSlider();
            eventSlider.onValueChanged.AddListener(SeekEvent);
            RefreshEventControls();
        }

        private Button CreateEventButton(
            string name,
            float x,
            float y,
            float width,
            UnityEngine.Events.UnityAction action)
        {
            Button button = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Image),
                typeof(Button))
                .GetComponent<Button>();
            button.transform.SetParent(eventControls, false);
            SetRect(button.GetComponent<RectTransform>(), x, y, width, 36f);
            button.targetGraphic = button.GetComponent<Image>();
            button.onClick.AddListener(action);

            TMP_Text label = CreateText(
                "Label",
                button.transform,
                14,
                FontStyles.Bold,
                TextAlignmentOptions.Center);
            label.text = name;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
            StyleButton(button);
            return button;
        }

        private Slider CreateEventSlider()
        {
            Slider slider = new GameObject(
                "EventScrub",
                typeof(RectTransform),
                typeof(Slider))
                .GetComponent<Slider>();
            slider.transform.SetParent(eventControls, false);
            SetRect(slider.GetComponent<RectTransform>(), 12f, -92f, 276f, 30f);
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.direction = Slider.Direction.LeftToRight;

            Image background = CreateSliderImage(
                "Background",
                slider.transform,
                new Color(0.11f, 0.12f, 0.15f, 1f));
            FillRect(background.rectTransform, 0f, 0.35f, 1f, 0.65f);

            RectTransform fillArea = new GameObject("FillArea", typeof(RectTransform)).GetComponent<RectTransform>();
            fillArea.SetParent(slider.transform, false);
            FillRect(fillArea, 0f, 0.35f, 1f, 0.65f);
            fillArea.offsetMin = new Vector2(6f, 0f);
            fillArea.offsetMax = new Vector2(-6f, 0f);

            Image fill = CreateSliderImage(
                "Fill",
                fillArea,
                new Color(0.95f, 0.08f, 0.08f, 1f));
            FillRect(fill.rectTransform, 0f, 0f, 1f, 1f);

            RectTransform handleArea = new GameObject("HandleArea", typeof(RectTransform)).GetComponent<RectTransform>();
            handleArea.SetParent(slider.transform, false);
            FillRect(handleArea, 0f, 0f, 1f, 1f);
            handleArea.offsetMin = new Vector2(6f, 0f);
            handleArea.offsetMax = new Vector2(-6f, 0f);

            Image handle = CreateSliderImage("Handle", handleArea, Color.white);
            RectTransform handleRect = handle.rectTransform;
            handleRect.anchorMin = new Vector2(0f, 0.5f);
            handleRect.anchorMax = new Vector2(0f, 0.5f);
            handleRect.pivot = new Vector2(0.5f, 0.5f);
            handleRect.sizeDelta = new Vector2(14f, 24f);

            slider.fillRect = fill.rectTransform;
            slider.handleRect = handleRect;
            slider.targetGraphic = handle;
            return slider;
        }

        private static Image CreateSliderImage(
            string name,
            Transform parent,
            Color color)
        {
            Image image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.color = color;
            return image;
        }

        private static void FillRect(
            RectTransform rect,
            float minX,
            float minY,
            float maxX,
            float maxY)
        {
            rect.anchorMin = new Vector2(minX, minY);
            rect.anchorMax = new Vector2(maxX, maxY);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private void RefreshEventControls()
        {
            if (eventControls == null || player == null)
                return;

            EventPopoutReplay eventReplay = player.EventReplay;
            bool loading = eventReplay != null && eventReplay.IsLoading;
            bool active = eventReplay != null && eventReplay.IsActive;

            eventOpenButton.gameObject.SetActive(!active && !loading);
            eventPlayButton.gameObject.SetActive(active);
            eventRestartButton.gameObject.SetActive(active);
            eventCloseButton.gameObject.SetActive(active || loading);
            eventSlider.gameObject.SetActive(active);

            if (loading)
            {
                eventStatus.text = "LOADING EVENT REPLAY";
                return;
            }

            if (!active)
            {
                eventStatus.text = player.HasDataset
                    ? "OVERTAKE EVENT"
                    : "EVENT DATA NOT READY";
                eventOpenButton.interactable = player.HasDataset;
                return;
            }

            string title = eventReplay.CurrentEvent != null &&
                !string.IsNullOrWhiteSpace(eventReplay.CurrentEvent.displayTitle)
                    ? eventReplay.CurrentEvent.displayTitle
                    : "Overtake Event";
            eventStatus.text = $"{title}  {FormatTime(eventReplay.CurrentTime)}";
            SetButton(eventPlayButton, eventReplay.IsPlaying ? "Pause" : "Play", true);

            refreshingEventSlider = true;
            eventSlider.SetValueWithoutNotify(eventReplay.NormalizedTime);
            refreshingEventSlider = false;
        }

        private void OpenTestEvent()
        {
            player?.EventReplay?.OpenTestOvertake();
            RefreshEventControls();
        }

        private void ToggleEventPlay()
        {
            player?.EventReplay?.TogglePlay();
            RefreshEventControls();
        }

        private void RestartEvent()
        {
            player?.EventReplay?.Restart();
            RefreshEventControls();
        }

        private void CloseEvent()
        {
            player?.EventReplay?.Close();
            RefreshEventControls();
        }

        private void SeekEvent(float normalized)
        {
            if (refreshingEventSlider)
                return;

            player?.EventReplay?.SeekNormalized(normalized);
            RefreshEventControls();
        }
    }
}
