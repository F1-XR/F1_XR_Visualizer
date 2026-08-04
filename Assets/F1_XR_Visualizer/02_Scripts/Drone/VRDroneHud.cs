using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace F1XR.Drone
{
    [DisallowMultipleComponent]
    public sealed class VRDroneHud : MonoBehaviour
    {
        const float DistanceFromCamera = 1.2f;
        static readonly Vector2 CanvasSize = new(2200f, 1238f);

        Canvas canvas;
        Camera xrCamera;
        Image exitProgress;
        bool isVisible;

        public void Configure(Transform environmentTransform)
        {
            if (canvas != null || environmentTransform == null)
                return;

            canvas = CreateCanvas(environmentTransform);
            canvas.gameObject.SetActive(false);
        }

        public void Show(Camera camera)
        {
            if (camera == null)
                return;

            xrCamera = camera;
            canvas.gameObject.SetActive(true);
            SetExitHoldProgress(0f);
            isVisible = true;
            UpdatePose();
        }

        public void Hide()
        {
            isVisible = false;
            xrCamera = null;
            if (canvas != null)
                canvas.gameObject.SetActive(false);
        }

        public void SetExitHoldProgress(float normalizedProgress)
        {
            if (exitProgress != null)
                exitProgress.fillAmount = Mathf.Clamp01(normalizedProgress);
        }

        void LateUpdate()
        {
            if (isVisible)
                UpdatePose();
        }

        void UpdatePose()
        {
            if (canvas == null || xrCamera == null)
                return;

            Transform cameraTransform = xrCamera.transform;
            Transform hudTransform = canvas.transform;
            hudTransform.position = cameraTransform.position +
                cameraTransform.forward * DistanceFromCamera;
            hudTransform.rotation = Quaternion.LookRotation(
                hudTransform.position - cameraTransform.position,
                cameraTransform.up);
        }

        Canvas CreateCanvas(Transform parent)
        {
            GameObject canvasObject = new(
                "VR Drone HUD",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler));
            canvasObject.transform.SetParent(parent, false);

            Canvas result = canvasObject.GetComponent<Canvas>();
            result.renderMode = RenderMode.WorldSpace;
            RectTransform rect = canvasObject.GetComponent<RectTransform>();
            rect.sizeDelta = CanvasSize;
            rect.localScale = Vector3.one * 0.001f;

            CreateFrame(rect);
            CreateHeader(rect);
            CreateCrosshair(rect);
            CreateExitHint(rect);
            return result;
        }

        void CreateFrame(RectTransform parent)
        {
            CreateCorner(parent, "Top Left", new Vector2(0f, 1f),
                new Vector2(1f, 0f));
            CreateCorner(parent, "Top Right", new Vector2(1f, 1f),
                new Vector2(-1f, 0f));
            CreateCorner(parent, "Bottom Left", new Vector2(0f, 0f),
                new Vector2(1f, 0f));
            CreateCorner(parent, "Bottom Right", new Vector2(1f, 0f),
                new Vector2(-1f, 0f));
        }

        void CreateCorner(RectTransform parent, string name, Vector2 anchor,
            Vector2 horizontalDirection)
        {
            RectTransform corner = CreateRect(name, parent);
            corner.anchorMin = anchor;
            corner.anchorMax = anchor;
            corner.pivot = anchor;
            corner.anchoredPosition = new Vector2(
                anchor.x == 0f ? 34f : -34f,
                anchor.y == 0f ? 34f : -34f);

            Image horizontal = CreateImage("Horizontal", corner, Color.white);
            RectTransform horizontalRect = horizontal.rectTransform;
            horizontalRect.anchorMin = anchor.y == 0f
                ? Vector2.zero
                : new Vector2(0f, 1f);
            horizontalRect.anchorMax = horizontalRect.anchorMin;
            horizontalRect.pivot = new Vector2(
                horizontalDirection.x > 0f ? 0f : 1f,
                anchor.y == 0f ? 0f : 1f);
            horizontalRect.sizeDelta = new Vector2(62f, 4f);

            Image vertical = CreateImage("Vertical", corner, Color.white);
            RectTransform verticalRect = vertical.rectTransform;
            verticalRect.anchorMin = horizontalRect.anchorMin;
            verticalRect.anchorMax = horizontalRect.anchorMin;
            verticalRect.pivot = new Vector2(
                anchor.x == 0f ? 0f : 1f,
                anchor.y == 0f ? 0f : 1f);
            verticalRect.sizeDelta = new Vector2(4f, 62f);
        }

        void CreateHeader(RectTransform parent)
        {
            TextMeshProUGUI label = CreateText("Status", parent, "DRONE CAM", 30f);
            RectTransform rect = label.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -38f);
            rect.sizeDelta = new Vector2(300f, 48f);
            label.alignment = TextAlignmentOptions.Center;
        }

        void CreateCrosshair(RectTransform parent)
        {
            RectTransform crosshair = CreateRect("Crosshair", parent);
            crosshair.anchorMin = new Vector2(0.5f, 0.5f);
            crosshair.anchorMax = new Vector2(0.5f, 0.5f);
            crosshair.pivot = new Vector2(0.5f, 0.5f);
            crosshair.sizeDelta = new Vector2(14f, 14f);

            Image horizontal = CreateImage("Horizontal", crosshair, Color.white);
            horizontal.rectTransform.sizeDelta = new Vector2(14f, 2f);
            Image vertical = CreateImage("Vertical", crosshair, Color.white);
            vertical.rectTransform.sizeDelta = new Vector2(2f, 14f);
        }

        void CreateExitHint(RectTransform parent)
        {
            RectTransform group = CreateRect("Exit Hold Hint", parent);
            group.anchorMin = new Vector2(0.5f, 0f);
            group.anchorMax = new Vector2(0.5f, 0f);
            group.pivot = new Vector2(0.5f, 0f);
            group.anchoredPosition = new Vector2(0f, 38f);
            group.sizeDelta = new Vector2(420f, 54f);

            TextMeshProUGUI label = CreateText("Label", group,
                "HOLD B TO EXIT", 26f);
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 0.5f);
            labelRect.anchorMax = new Vector2(0f, 0.5f);
            labelRect.pivot = new Vector2(0f, 0.5f);
            labelRect.sizeDelta = new Vector2(250f, 44f);

            Image background = CreateImage("Progress Background", group,
                new Color(1f, 1f, 1f, 0.25f));
            RectTransform backgroundRect = background.rectTransform;
            backgroundRect.anchorMin = new Vector2(1f, 0.5f);
            backgroundRect.anchorMax = new Vector2(1f, 0.5f);
            backgroundRect.pivot = new Vector2(1f, 0.5f);
            backgroundRect.sizeDelta = new Vector2(140f, 8f);

            exitProgress = CreateImage("Progress", background.transform,
                new Color(0.95f, 0.15f, 0.18f, 1f));
            exitProgress.type = Image.Type.Filled;
            exitProgress.fillMethod = Image.FillMethod.Horizontal;
            exitProgress.fillOrigin = (int)Image.OriginHorizontal.Left;
            exitProgress.rectTransform.anchorMin = Vector2.zero;
            exitProgress.rectTransform.anchorMax = Vector2.one;
            exitProgress.rectTransform.offsetMin = Vector2.zero;
            exitProgress.rectTransform.offsetMax = Vector2.zero;
        }

        static RectTransform CreateRect(string name, Transform parent)
        {
            GameObject gameObject = new(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);
            return gameObject.GetComponent<RectTransform>();
        }

        static Image CreateImage(string name, Transform parent, Color color)
        {
            GameObject gameObject = new(name, typeof(RectTransform), typeof(Image));
            gameObject.transform.SetParent(parent, false);
            Image image = gameObject.GetComponent<Image>();
            image.color = color;
            return image;
        }

        static TextMeshProUGUI CreateText(string name, Transform parent,
            string content, float fontSize)
        {
            GameObject gameObject = new(name, typeof(RectTransform),
                typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            gameObject.transform.SetParent(parent, false);
            TextMeshProUGUI text = gameObject.GetComponent<TextMeshProUGUI>();
            text.font = TMP_Settings.defaultFontAsset;
            text.text = content;
            text.fontSize = fontSize;
            text.color = Color.white;
            return text;
        }
    }
}
