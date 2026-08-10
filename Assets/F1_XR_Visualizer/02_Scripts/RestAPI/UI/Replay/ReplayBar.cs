using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using F1XR.RestAPI.Api;
using F1XR.RestAPI.Replay;

namespace F1XR.RestAPI.UI
{
    public class ReplayBar : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        private const float OvertakeMarkerOffset = 8f;
        private static readonly Color OvertakeMarkerColor =
            new(0.16f, 0.82f, 1f, 1f);
        private static readonly Color CurrentOvertakeMarkerColor =
            new(1f, 0.18f, 0.5f, 1f);

        public ReplayPlayer player;

        public RectTransform barRect;
        public Image playedFill;
        public Image loadedFill;

        public bool allowDragSeek = true;

        private RectTransform raceStartMarker;
        private RectTransform raceEndMarker;
        private readonly List<RectTransform> yellowFlagMarkers = new();
        private readonly List<RectTransform> redFlagMarkers = new();
        private readonly List<TMP_Text> timelineGapMarkers = new();
        private readonly List<RectTransform> overtakeMarkers = new();
        private TMP_Text nextOvertakeLabel;
        private float displayedNextOvertakeTime = float.NaN;
        private bool displayedOvertakesComplete;

        private void Awake()
        {
            if (barRect == null)
                barRect = GetComponent<RectTransform>();
        }

        private void Update()
        {
            Refresh();
        }

        public void Refresh()
        {
            if (player == null || player.Duration <= 0f)
            {
                SetFill(playedFill, 0f);
                SetFill(loadedFill, 0f);
                SetMarkerVisible(false);
                return;
            }

            SetFill(playedFill, player.TimelineToNormalized(player.CurrentTime));
            SetFill(loadedFill, player.TimelineToNormalized(player.ReadyUntilTime));
            SetTimeMarker(ref raceStartMarker, "Race Start Marker", Color.black, player.RaceStartTime);
            SetTimeMarker(ref raceEndMarker, "Race End Marker", Color.black, player.RaceEndTime);
            SetRaceControlMarkers(yellowFlagMarkers, "Yellow Flag Marker", new Color(1f, 0.85f, 0f), player.YellowFlags);
            SetRaceControlMarkers(redFlagMarkers, "Red Flag Marker", Color.red, player.RedFlags);
            SetTimelineGapMarkers(player.TimelineGaps);
            SetOvertakeMarkers(player.Events);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            SeekFromPointer(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (allowDragSeek)
                SeekFromPointer(eventData);
        }

        private void SeekFromPointer(PointerEventData eventData)
        {
            if (player == null || barRect == null || player.Duration <= 0f)
                return;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    barRect,
                    eventData.position,
                    eventData.pressEventCamera,
                    out Vector2 localPoint))
            {
                return;
            }

            Rect rect = barRect.rect;
            float normalized = Mathf.InverseLerp(rect.xMin, rect.xMax, localPoint.x);
            float targetTime = player.NormalizedToTimeline(normalized);

            if (targetTime > player.ReadyUntilTime)
                return;

            player.Seek(targetTime);
            Refresh();
        }

        private static void SetFill(Image image, float value)
        {
            if (image != null)
                image.fillAmount = Mathf.Clamp01(value);
        }

        private void SetTimeMarker(ref RectTransform marker, string name, Color color, float time)
        {
            if (barRect == null)
                return;

            if (time <= 0f)
            {
                if (marker != null)
                    marker.gameObject.SetActive(false);

                return;
            }

            EnsureMarker(ref marker, name, color);
            ApplyMarker(marker, player.TimelineToNormalized(time));
        }

        private void SetRaceControlMarkers(
            List<RectTransform> markers,
            string name,
            Color color,
            RaceControlEventDto[] events)
        {
            int activeCount = 0;

            if (events != null)
            {
                foreach (RaceControlEventDto raceEvent in events)
                {
                    if (raceEvent == null || raceEvent.t <= 0f)
                        continue;

                    RectTransform marker = EnsureMarker(markers, activeCount, name, color);
                    ApplyMarker(marker, player.TimelineToNormalized(raceEvent.t));
                    activeCount++;
                }
            }

            for (int i = activeCount; i < markers.Count; i++)
                markers[i].gameObject.SetActive(false);
        }

        private void ApplyMarker(RectTransform marker, float normalized)
        {
            if (marker == null)
                return;

            Rect rect = barRect.rect;
            float clamped = Mathf.Clamp01(normalized);
            marker.anchorMin = new Vector2(clamped, 0.5f);
            marker.anchorMax = new Vector2(clamped, 0.5f);
            marker.anchoredPosition = Vector2.zero;
            marker.sizeDelta = new Vector2(3f, rect.height + 6f);
            marker.SetAsLastSibling();
            marker.gameObject.SetActive(true);
        }

        private void SetTimelineGapMarkers(
            IReadOnlyList<ReplayTimelineGap> gaps)
        {
            int activeCount = gaps != null ? gaps.Count : 0;
            for (int i = 0; i < activeCount; i++)
            {
                TMP_Text marker = EnsureTimelineGapMarker(i);
                float normalized = player.TimelineToNormalized(
                    gaps[i].SourceStart);
                ApplyTimelineGapMarker(marker, normalized);
            }

            for (int i = activeCount; i < timelineGapMarkers.Count; i++)
                timelineGapMarkers[i].gameObject.SetActive(false);
        }

        private TMP_Text EnsureTimelineGapMarker(int index)
        {
            while (timelineGapMarkers.Count <= index)
            {
                GameObject markerObject = new(
                    $"Timeline Gap {timelineGapMarkers.Count + 1}",
                    typeof(RectTransform),
                    typeof(TextMeshProUGUI));
                markerObject.layer = barRect.gameObject.layer;
                markerObject.transform.SetParent(
                    barRect,
                    worldPositionStays: false);

                TMP_Text marker =
                    markerObject.GetComponent<TextMeshProUGUI>();
                marker.text = "~~~";
                marker.alignment = TextAlignmentOptions.Center;
                marker.fontSize = 18f;
                marker.fontStyle = FontStyles.Bold;
                marker.color = new Color(1f, 0.78f, 0.18f, 1f);
                marker.raycastTarget = false;
                timelineGapMarkers.Add(marker);
            }

            return timelineGapMarkers[index];
        }

        private void ApplyTimelineGapMarker(
            TMP_Text marker,
            float normalized)
        {
            RectTransform rect = marker.rectTransform;
            float clamped = Mathf.Clamp01(normalized);
            rect.anchorMin = new Vector2(clamped, 0.5f);
            rect.anchorMax = new Vector2(clamped, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(42f, barRect.rect.height + 12f);
            rect.SetAsLastSibling();
            marker.gameObject.SetActive(true);
        }

        private void SetOvertakeMarkers(ReplayEventDto[] events)
        {
            int activeCount = 0;
            ReplayEventDto nextOvertake = null;
            RectTransform currentMarker = null;
            float timelineStart = player.TimelineStartTime;
            float timelineEnd = player.TimelineEndTime;
            EventPopoutReplay eventReplay = player.EventReplay;
            ReplayEventDto currentOvertake =
                eventReplay != null &&
                (eventReplay.IsActive || eventReplay.IsLoading)
                    ? eventReplay.CurrentEvent
                    : null;

            if (events != null)
            {
                foreach (ReplayEventDto replayEvent in events)
                {
                    if (replayEvent == null ||
                        !string.Equals(
                            replayEvent.eventType,
                            "Overtake",
                            StringComparison.OrdinalIgnoreCase) ||
                        replayEvent.anchorTime < timelineStart ||
                        replayEvent.anchorTime > timelineEnd)
                    {
                        continue;
                    }

                    RectTransform marker =
                        EnsureOvertakeMarker(activeCount);
                    ApplyOvertakeMarker(
                        marker,
                        player.TimelineToNormalized(
                            replayEvent.anchorTime));
                    bool isCurrent = IsSameEvent(
                        replayEvent,
                        currentOvertake);
                    marker.GetComponent<Image>().color = isCurrent
                        ? CurrentOvertakeMarkerColor
                        : OvertakeMarkerColor;
                    if (isCurrent)
                        currentMarker = marker;
                    activeCount++;

                    if (replayEvent.anchorTime + 0.001f <
                            player.CurrentTime ||
                        nextOvertake != null &&
                        replayEvent.anchorTime >=
                            nextOvertake.anchorTime)
                    {
                        continue;
                    }

                    nextOvertake = replayEvent;
                }
            }

            for (int i = activeCount;
                 i < overtakeMarkers.Count;
                 i++)
            {
                overtakeMarkers[i].gameObject.SetActive(false);
            }

            currentMarker?.SetAsLastSibling();

            RefreshNextOvertakeLabel(
                activeCount,
                nextOvertake);
        }

        private static bool IsSameEvent(
            ReplayEventDto left,
            ReplayEventDto right)
        {
            if (left == null || right == null)
                return false;

            if (!string.IsNullOrEmpty(left.eventId) &&
                !string.IsNullOrEmpty(right.eventId))
            {
                return string.Equals(
                    left.eventId,
                    right.eventId,
                    StringComparison.Ordinal);
            }

            return Mathf.Approximately(
                left.anchorTime,
                right.anchorTime);
        }

        private RectTransform EnsureOvertakeMarker(int index)
        {
            while (overtakeMarkers.Count <= index)
            {
                GameObject markerObject = new(
                    $"Overtake Marker {overtakeMarkers.Count + 1}",
                    typeof(RectTransform),
                    typeof(Image));
                markerObject.layer = barRect.gameObject.layer;
                markerObject.transform.SetParent(
                    barRect,
                    worldPositionStays: false);

                RectTransform marker =
                    markerObject.GetComponent<RectTransform>();
                marker.pivot = new Vector2(0.5f, 0.5f);
                marker.localRotation =
                    Quaternion.Euler(0f, 0f, 45f);

                Image image = markerObject.GetComponent<Image>();
                image.color = OvertakeMarkerColor;
                image.raycastTarget = false;
                overtakeMarkers.Add(marker);
            }

            return overtakeMarkers[index];
        }

        private static void ApplyOvertakeMarker(
            RectTransform marker,
            float normalized)
        {
            float clamped = Mathf.Clamp01(normalized);
            marker.anchorMin = new Vector2(clamped, 1f);
            marker.anchorMax = new Vector2(clamped, 1f);
            marker.anchoredPosition =
                new Vector2(0f, OvertakeMarkerOffset);
            marker.sizeDelta = new Vector2(8f, 8f);
            marker.SetAsLastSibling();
            marker.gameObject.SetActive(true);
        }

        private void RefreshNextOvertakeLabel(
            int overtakeCount,
            ReplayEventDto nextOvertake)
        {
            if (overtakeCount <= 0)
            {
                if (nextOvertakeLabel != null)
                    nextOvertakeLabel.gameObject.SetActive(false);

                displayedNextOvertakeTime = float.NaN;
                displayedOvertakesComplete = false;
                return;
            }

            EnsureNextOvertakeLabel();
            nextOvertakeLabel.gameObject.SetActive(true);

            if (nextOvertake == null)
            {
                if (!displayedOvertakesComplete)
                    nextOvertakeLabel.text = "OVERTAKES COMPLETE";

                displayedNextOvertakeTime = float.NaN;
                displayedOvertakesComplete = true;
                return;
            }

            if (!displayedOvertakesComplete &&
                Mathf.Approximately(
                    displayedNextOvertakeTime,
                    nextOvertake.anchorTime))
            {
                return;
            }

            float playbackTime = player.TimelineToPlaybackTime(
                nextOvertake.anchorTime);
            nextOvertakeLabel.text =
                $"NEXT OVERTAKE {FormatTime(playbackTime)}";
            displayedNextOvertakeTime = nextOvertake.anchorTime;
            displayedOvertakesComplete = false;
        }

        private void EnsureNextOvertakeLabel()
        {
            if (nextOvertakeLabel != null)
                return;

            GameObject labelObject = new(
                "Next Overtake Label",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            labelObject.layer = barRect.gameObject.layer;
            labelObject.transform.SetParent(
                barRect,
                worldPositionStays: false);

            RectTransform rect =
                labelObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.one;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(0f, 17f);
            rect.sizeDelta = new Vector2(240f, 20f);

            nextOvertakeLabel =
                labelObject.GetComponent<TextMeshProUGUI>();
            nextOvertakeLabel.alignment =
                TextAlignmentOptions.BottomRight;
            nextOvertakeLabel.fontSize = 14f;
            nextOvertakeLabel.fontStyle = FontStyles.Bold;
            nextOvertakeLabel.color = OvertakeMarkerColor;
            nextOvertakeLabel.raycastTarget = false;
        }

        private static string FormatTime(float seconds)
        {
            int totalSeconds =
                Mathf.Max(0, Mathf.FloorToInt(seconds));
            int minutes = totalSeconds / 60;
            int remainingSeconds = totalSeconds % 60;
            return $"{minutes:00}:{remainingSeconds:00}";
        }

        private void EnsureMarker(ref RectTransform marker, string name, Color color)
        {
            if (marker != null)
                return;

            marker = CreateMarker(name, color);
        }

        private RectTransform EnsureMarker(List<RectTransform> markers, int index, string name, Color color)
        {
            while (markers.Count <= index)
                markers.Add(CreateMarker($"{name} {markers.Count + 1}", color));

            return markers[index];
        }

        private RectTransform CreateMarker(string name, Color color)
        {
            GameObject marker = new GameObject(name, typeof(RectTransform), typeof(Image));
            marker.transform.SetParent(barRect, worldPositionStays: false);

            RectTransform rectTransform = marker.GetComponent<RectTransform>();
            rectTransform.pivot = new Vector2(0.5f, 0.5f);

            Image image = marker.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;

            return rectTransform;
        }

        private void SetMarkerVisible(bool visible)
        {
            if (raceStartMarker != null)
                raceStartMarker.gameObject.SetActive(visible);

            if (raceEndMarker != null)
                raceEndMarker.gameObject.SetActive(visible);

            foreach (RectTransform marker in yellowFlagMarkers)
                marker.gameObject.SetActive(visible);

            foreach (RectTransform marker in redFlagMarkers)
                marker.gameObject.SetActive(visible);

            foreach (TMP_Text marker in timelineGapMarkers)
                marker.gameObject.SetActive(visible);

            foreach (RectTransform marker in overtakeMarkers)
                marker.gameObject.SetActive(visible);

            if (nextOvertakeLabel != null)
                nextOvertakeLabel.gameObject.SetActive(visible);
        }
    }
}
