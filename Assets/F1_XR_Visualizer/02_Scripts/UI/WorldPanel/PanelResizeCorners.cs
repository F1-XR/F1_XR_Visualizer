using UnityEngine;
using UnityEngine.UI;
using F1XR.Interaction.World;

namespace F1XR.UI.WorldPanel
{
    public sealed class PanelResizeCorners : MonoBehaviour
    {
        [SerializeField] ScaleController scaleController;
        [SerializeField] Color color = Color.white;
        [SerializeField] float length = 24f;
        [SerializeField] float thickness = 2f;
        [SerializeField] float padding = 8f;

        RectTransform rect;
        RectTransform[] corners;

        void Awake()
        {
            rect = GetComponent<RectTransform>();

            if (scaleController == null)
                scaleController = GetComponentInParent<ScaleController>();

            CreateCorners();
            SetVisible(false);
        }

        void Update()
        {
            var visible = scaleController != null && scaleController.IsScaling;
            SetVisible(visible);

            if (visible)
                LayoutCorners();
        }

        void CreateCorners()
        {
            corners = new RectTransform[4];

            for (var i = 0; i < corners.Length; i++)
            {
                var corner = new GameObject($"ResizeCorner{i + 1}", typeof(RectTransform));
                corner.transform.SetParent(transform, false);

                var cornerRect = corner.GetComponent<RectTransform>();
                cornerRect.sizeDelta = Vector2.zero;
                corners[i] = cornerRect;

                CreateLine(cornerRect, true);
                CreateLine(cornerRect, false);
            }
        }

        void CreateLine(RectTransform parent, bool horizontal)
        {
            var line = new GameObject(horizontal ? "Horizontal" : "Vertical", typeof(RectTransform), typeof(Image));
            line.transform.SetParent(parent, false);

            var lineRect = line.GetComponent<RectTransform>();
            lineRect.pivot = new Vector2(0.5f, 0.5f);
            lineRect.sizeDelta = horizontal
                ? new Vector2(length, thickness)
                : new Vector2(thickness, length);

            var image = line.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        void LayoutCorners()
        {
            if (rect == null)
                return;

            var halfSize = rect.rect.size * 0.5f;
            var positions = new[]
            {
                new Vector2(-halfSize.x - padding, halfSize.y + padding),
                new Vector2(halfSize.x + padding, halfSize.y + padding),
                new Vector2(-halfSize.x - padding, -halfSize.y - padding),
                new Vector2(halfSize.x + padding, -halfSize.y - padding)
            };

            for (var i = 0; i < corners.Length; i++)
            {
                corners[i].anchoredPosition = positions[i];
                LayoutCorner(corners[i], i);
            }
        }

        void LayoutCorner(RectTransform corner, int index)
        {
            var horizontalSign = index % 2 == 0 ? 1f : -1f;
            var verticalSign = index < 2 ? -1f : 1f;
            var horizontal = (RectTransform)corner.GetChild(0);
            var vertical = (RectTransform)corner.GetChild(1);

            horizontal.anchoredPosition = new Vector2(horizontalSign * length * 0.5f, 0f);
            vertical.anchoredPosition = new Vector2(0f, verticalSign * length * 0.5f);
        }

        void SetVisible(bool visible)
        {
            if (corners == null)
                return;

            foreach (var corner in corners)
                corner.gameObject.SetActive(visible);
        }
    }
}
