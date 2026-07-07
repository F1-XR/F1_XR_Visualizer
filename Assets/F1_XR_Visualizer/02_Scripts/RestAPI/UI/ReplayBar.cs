using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
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
                return;
            }

            SetFill(playedFill, player.CurrentTime / player.Duration);
            SetFill(loadedFill, player.ReadyUntilTime / player.Duration);
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
            float targetTime = normalized * player.Duration;

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
    }
}
