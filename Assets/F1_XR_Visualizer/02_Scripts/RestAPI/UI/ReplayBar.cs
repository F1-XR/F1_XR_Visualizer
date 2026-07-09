using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using F1XR.RestAPI.Api;
using F1XR.RestAPI.Replay;

namespace F1XR.RestAPI.UI
{
    public class ReplayBar : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public ChunkReplayPlayer player;

        public RectTransform barRect;
        public Image playedFill;
        public Image loadedFill;

        public bool allowDragSeek = true;

        private RectTransform raceStartMarker;
        private RectTransform raceEndMarker;
        private readonly List<RectTransform> yellowFlagMarkers = new();
        private readonly List<RectTransform> redFlagMarkers = new();

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
        }
    }
}
