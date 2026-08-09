using TMPro;
using UnityEngine;
using UnityEngine.UI;
using F1XR.RestAPI.Api;
using F1XR.RestAPI.Replay;
using F1XR.RestAPI.Replay.Room;

namespace F1XR.RestAPI.UI
{
    public partial class ReplayUI
    {
        private RectTransform eventControls;
        private TMP_Text eventStatus;
        private Button eventOpenButton;
        private Button eventOpenPitButton;
        private Button eventPlayButton;
        private Button eventRestartButton;
        private Button eventNextButton;
        private Button eventCloseButton;
        private Button eventPitWallButton;
        private Slider eventSlider;
        private bool refreshingEventSlider;
        private RoomShowcaseSetupController roomSetup;
        private PitWallShowcasePresenter pitWallPresenter;

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
            eventStatus.richText = true;
            SetRect(eventStatus.rectTransform, 8f, -8f, 284f, 26f);

            eventOpenButton = CreateEventButton(
                "Open Overtake",
                8f,
                -42f,
                284f,
                OpenTestEvent);
            eventOpenPitButton = CreateEventButton(
                "Open Pit Stop",
                8f,
                -84f,
                284f,
                OpenPitStop);
            eventPlayButton = CreateEventButton("Play", 8f, -42f, 86f, ToggleEventPlay);
            eventRestartButton = CreateEventButton("Restart", 104f, -42f, 86f, RestartEvent);
            eventCloseButton = CreateEventButton("Close", 200f, -42f, 92f, CloseEvent);
            eventNextButton = CreateEventButton(
                "Next Overtake",
                8f,
                -84f,
                284f,
                OpenNextEvent);
            eventPitWallButton = CreateEventButton(
                "Change Wall",
                200f,
                -84f,
                92f,
                SelectNextPitWall);
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
            SetRect(slider.GetComponent<RectTransform>(), 12f, -134f, 276f, 30f);
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

            ResolveRoomSetup();
            EventPopoutReplay eventReplay = player.EventReplay;
            bool loading = eventReplay != null && eventReplay.IsLoading;
            bool active = eventReplay != null && eventReplay.IsActive;
            bool roomReady = roomSetup == null || roomSetup.IsSetupReady;

            eventOpenButton.gameObject.SetActive(!active && !loading);
            eventOpenPitButton.gameObject.SetActive(!active && !loading);
            eventPlayButton.gameObject.SetActive(active);
            eventRestartButton.gameObject.SetActive(active);
            eventNextButton.gameObject.SetActive(active);
            bool pitActive = active && eventReplay.IsPitStopActive;
            eventPitWallButton.gameObject.SetActive(pitActive);
            eventCloseButton.gameObject.SetActive(active || loading);
            eventSlider.gameObject.SetActive(active);
            eventControls.sizeDelta = new Vector2(
                300f,
                active ? 184f : 184f);

            if (loading)
            {
                eventStatus.text = "LOADING EVENT REPLAY";
                return;
            }

            if (!active)
            {
                bool hasPitStop =
                    eventReplay != null &&
                    eventReplay.HasPitStop;
                bool pitWallReady =
                    roomSetup == null ||
                    roomSetup.HasPitWallCandidate;
                eventStatus.text = !player.HasDataset
                    ? "EVENT DATA NOT READY"
                    : !roomReady && !pitWallReady
                        ? "ROOM SETUP REQUIRED"
                        : "MANUAL SHOWCASE EVENT";
                eventOpenButton.interactable =
                    player.HasDataset && roomReady;
                SetButton(
                    eventOpenPitButton,
                    hasPitStop
                        ? "Open Pit Stop"
                        : "No Pit Stops In Loaded Range",
                    player.HasDataset &&
                    hasPitStop &&
                    pitWallReady);
                return;
            }

            string title = FormatEventTitle(
                eventReplay.CurrentEvent);
            eventStatus.text = FormatActiveEventStatus(
                eventReplay,
                title);
            SetButton(eventPlayButton, eventReplay.IsPlaying ? "Pause" : "Play", true);
            bool hasNext = pitActive
                ? eventReplay.HasNextPitStop
                : eventReplay.HasNextOvertake;
            SetRect(
                eventNextButton.GetComponent<RectTransform>(),
                8f,
                -84f,
                pitActive ? 182f : 284f,
                36f);
            SetButton(
                eventNextButton,
                hasNext
                    ? pitActive
                        ? "Next Pit Stop"
                        : "Next Overtake"
                    : pitActive
                        ? "No More Pit Stops"
                        : "No More Overtakes",
                hasNext);

            refreshingEventSlider = true;
            eventSlider.SetValueWithoutNotify(eventReplay.NormalizedTime);
            refreshingEventSlider = false;
        }

        private string FormatEventTitle(
            ReplayEventDto replayEvent)
        {
            string title = replayEvent != null &&
                           !string.IsNullOrWhiteSpace(
                               replayEvent.displayTitle)
                ? replayEvent.displayTitle
                : "Overtake Event";
            int[] drivers = replayEvent != null
                ? replayEvent.driverNumbers
                : null;
            if (drivers == null || drivers.Length == 0)
                return title;

            bool colored = false;
            int count = Mathf.Min(2, drivers.Length);
            for (int i = 0; i < count; i++)
            {
                int driver = drivers[i];
                string color = ColorUtility.ToHtmlStringRGB(
                    player.GetDriverColor(driver));
                var info = player.GetDriverInfo(driver);
                if (info != null &&
                    !string.IsNullOrWhiteSpace(info.fullName))
                {
                    title = ColorizeTitleToken(
                        title,
                        info.fullName,
                        color,
                        ref colored);
                }

                title = ColorizeTitleToken(
                    title,
                    player.GetDriverLabel(driver),
                    color,
                    ref colored);
            }

            if (colored || count < 2)
                return title;

            return $"{ColorizeDriverLabel(drivers[0])}  VS  " +
                   ColorizeDriverLabel(drivers[1]);
        }

        private string FormatActiveEventStatus(
            EventPopoutReplay eventReplay,
            string title)
        {
            if (eventReplay == null)
                return title;
            if (!eventReplay.IsPitStopActive)
            {
                return $"{title}  {FormatTime(eventReplay.CurrentTime)}";
            }

            ReplayEventDto replayEvent = eventReplay.CurrentEvent;
            int driver = replayEvent.driverNumbers != null &&
                         replayEvent.driverNumbers.Length > 0
                ? replayEvent.driverNumbers[0]
                : 0;
            DriverInfoDto info = player.GetDriverInfo(driver);
            string team = info != null &&
                          !string.IsNullOrWhiteSpace(info.teamName)
                ? info.teamName
                : "TEAM";
            string timing = eventReplay.PitStopDriveThrough
                ? "DRIVE THROUGH"
                : eventReplay.PitStopReconstructed
                    ? "RECONSTRUCTED STOP"
                    : eventReplay.CurrentPitStopPhase
                        .ToString()
                        .ToUpperInvariant();
            return $"{title} | {team} | L{replayEvent.lapNumber} | " +
                   $"{timing}  {FormatTime(eventReplay.CurrentTime)}";
        }

        private string ColorizeDriverLabel(int driver)
        {
            string color = ColorUtility.ToHtmlStringRGB(
                player.GetDriverColor(driver));
            return $"<color=#{color}>" +
                   $"{player.GetDriverLabel(driver)}</color>";
        }

        private static string ColorizeTitleToken(
            string title,
            string token,
            string color,
            ref bool colored)
        {
            if (string.IsNullOrWhiteSpace(title) ||
                string.IsNullOrWhiteSpace(token))
            {
                return title;
            }

            int index = title.IndexOf(
                token,
                System.StringComparison.OrdinalIgnoreCase);
            while (index >= 0)
            {
                string visibleText = title.Substring(
                    index,
                    token.Length);
                string replacement =
                    $"<color=#{color}>{visibleText}</color>";
                title = title.Substring(0, index) +
                        replacement +
                        title.Substring(index + token.Length);
                colored = true;
                index = title.IndexOf(
                    token,
                    index + replacement.Length,
                    System.StringComparison.OrdinalIgnoreCase);
            }

            return title;
        }

        private void OpenTestEvent()
        {
            ResolveRoomSetup();
            if (roomSetup != null && !roomSetup.IsSetupReady)
            {
                roomSetup.NotifyOpenBlocked();
                RefreshEventControls();
                return;
            }

            player?.EventReplay?.OpenTestOvertake();
            RefreshEventControls();
        }

        private void ResolveRoomSetup()
        {
            if (roomSetup == null)
            {
                roomSetup =
                    Object.FindAnyObjectByType<RoomShowcaseSetupController>(
                        FindObjectsInactive.Include);
            }
            if (pitWallPresenter == null)
            {
                pitWallPresenter =
                    Object.FindAnyObjectByType<PitWallShowcasePresenter>(
                        FindObjectsInactive.Include);
            }
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

        private void OpenPitStop()
        {
            ResolveRoomSetup();
            if (roomSetup != null &&
                !roomSetup.HasPitWallCandidate)
            {
                roomSetup.NotifyOpenBlocked();
                RefreshEventControls();
                return;
            }

            player?.EventReplay?.OpenTestPitStop();
            RefreshEventControls();
        }

        private void OpenNextEvent()
        {
            EventPopoutReplay eventReplay = player?.EventReplay;
            if (eventReplay != null && eventReplay.IsPitStopActive)
                eventReplay.OpenNextPitStop();
            else
                eventReplay?.OpenNextOvertake();
            RefreshEventControls();
        }

        private void SelectNextPitWall()
        {
            ResolveRoomSetup();
            pitWallPresenter?.SelectNextPitWall();
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
