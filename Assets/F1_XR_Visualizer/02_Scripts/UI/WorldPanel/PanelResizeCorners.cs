using UnityEngine;
using UnityEngine.UI;
using F1XR.Interaction.World;

namespace F1XR.UI.WorldPanel
{
    public sealed class PanelResizeCorners : MonoBehaviour
    {
        [SerializeField] ScaleController scaleController;
        [SerializeField] Color color = Color.white;
        [SerializeField] Color highlightColor = Color.white;
        [SerializeField] float length = 24f;
        [SerializeField] float thickness = 2f;
        [SerializeField] float padding = 8f;
        [SerializeField, Min(0f)] float cornerRadius = 7f;
        [SerializeField, Min(1f)] float highlightThicknessScale = 1.6f;

        const int TextureSize = 128;

        // Each corner is the same drawing turned about its elbow: TL, TR, BL, BR.
        static readonly float[] CornerRotations = { 0f, -90f, 90f, 180f };

        RectTransform rect;
        RectTransform[] corners;
        Image[] cornerImages;
        Texture2D normalTexture;
        Texture2D highlightTexture;
        Sprite normalSprite;
        Sprite highlightSprite;

        bool externalVisible;
        int highlightedCorner = -1;

        /// <summary>Keeps the marks on while a one-handed corner drag owns the panel.</summary>
        public void SetExternalVisible(bool visible)
        {
            externalVisible = visible;
        }

        /// <summary>Shows the marks on hover and emphasises the targeted corner. -1 clears it.</summary>
        public void SetHighlightedCorner(int index)
        {
            if (highlightedCorner == index)
                return;

            highlightedCorner = index;
            ApplyHighlight();
        }

        void Awake()
        {
            rect = GetComponent<RectTransform>();

            if (scaleController == null)
                scaleController = GetComponentInParent<ScaleController>();

            BuildSprites();
            CreateCorners();
            SetVisible(false);
        }

        void OnDestroy()
        {
            if (cornerImages != null)
            {
                foreach (var image in cornerImages)
                {
                    if (image != null)
                        image.sprite = null;
                }
            }

            ReleaseSprites();
        }

        void OnValidate()
        {
            length = Mathf.Max(1f, length);
            thickness = Mathf.Max(0.1f, thickness);
            cornerRadius = Mathf.Max(0f, cornerRadius);
            highlightThicknessScale = Mathf.Max(1f, highlightThicknessScale);

            if (corners == null || !Application.isPlaying)
                return;

            BuildSprites();
            ApplyHighlight();
        }

        void Update()
        {
            var visible = externalVisible ||
                          highlightedCorner >= 0 ||
                          (scaleController != null && scaleController.IsScaling);

            SetVisible(visible);

            if (visible)
                LayoutCorners();
        }

        void SetVisible(bool visible)
        {
            if (corners == null)
                return;

            // A single corner showing means one hand is driving that corner, so the other three would
            // only be noise. The two-hand scale leaves the index at -1 and gets all four.
            for (var i = 0; i < corners.Length; i++)
                corners[i].gameObject.SetActive(visible && (highlightedCorner < 0 || highlightedCorner == i));
        }

        void CreateCorners()
        {
            corners = new RectTransform[4];
            cornerImages = new Image[4];

            for (var i = 0; i < corners.Length; i++)
            {
                var corner = new GameObject($"ResizeCorner{i + 1}", typeof(RectTransform), typeof(Image));
                corner.transform.SetParent(transform, false);

                var cornerRect = corner.GetComponent<RectTransform>();
                cornerRect.anchorMin = new Vector2(0.5f, 0.5f);
                cornerRect.anchorMax = new Vector2(0.5f, 0.5f);
                // Pivot on the elbow, so rotating the drawing leaves the elbow pinned to the panel corner.
                cornerRect.pivot = new Vector2(0f, 1f);
                corners[i] = cornerRect;

                var image = corner.GetComponent<Image>();
                image.raycastTarget = false;
                image.type = Image.Type.Simple;
                image.preserveAspect = false;
                cornerImages[i] = image;

                SetCorner(i, Vector2.zero);
            }

            ApplyHighlight();
        }

        void ApplyHighlight()
        {
            if (cornerImages == null)
                return;

            for (var i = 0; i < cornerImages.Length; i++)
            {
                if (cornerImages[i] == null)
                    continue;

                var active = i == highlightedCorner;
                cornerImages[i].sprite = active ? highlightSprite : normalSprite;
                cornerImages[i].color = active ? highlightColor : color;
            }
        }

        void LayoutCorners()
        {
            if (rect == null || corners == null)
                return;

            var halfSize = rect.rect.size * 0.5f;
            var x = halfSize.x + padding;
            var y = halfSize.y + padding;

            SetCorner(0, new Vector2(-x, y));
            SetCorner(1, new Vector2(x, y));
            SetCorner(2, new Vector2(-x, -y));
            SetCorner(3, new Vector2(x, -y));
        }

        void SetCorner(int index, Vector2 anchoredPosition)
        {
            var corner = corners[index];
            corner.anchoredPosition = anchoredPosition;
            corner.sizeDelta = new Vector2(length, length);
            corner.localRotation = Quaternion.Euler(0f, 0f, CornerRotations[index]);
        }

        void BuildSprites()
        {
            ReleaseSprites();

            normalTexture = BuildCornerTexture(thickness);
            highlightTexture = BuildCornerTexture(thickness * highlightThicknessScale);
            normalSprite = CreateSprite(normalTexture);
            highlightSprite = CreateSprite(highlightTexture);
        }

        void ReleaseSprites()
        {
            DestroyAsset(normalSprite);
            DestroyAsset(highlightSprite);
            DestroyAsset(normalTexture);
            DestroyAsset(highlightTexture);

            normalSprite = null;
            highlightSprite = null;
            normalTexture = null;
            highlightTexture = null;
        }

        static void DestroyAsset(Object asset)
        {
            if (asset == null)
                return;

            if (Application.isPlaying)
                Destroy(asset);
            else
                DestroyImmediate(asset);
        }

        static Sprite CreateSprite(Texture2D texture)
        {
            return Sprite.Create(texture, new Rect(0f, 0f, TextureSize, TextureSize), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>
        /// Draws the elbow as a rounded stroke: a quarter arc joining the two arms, with round caps that
        /// fall out of the clamped segment distance. Measured in the corner's own units (0..length).
        /// </summary>
        Texture2D BuildCornerTexture(float strokeThickness)
        {
            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
            {
                name = "Resize Corner Mark",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var halfStroke = strokeThickness * 0.5f;
            var radius = Mathf.Clamp(cornerRadius, 0f, Mathf.Max(0f, length - halfStroke * 2f));
            var elbow = new Vector2(halfStroke, halfStroke);
            var arcCenter = new Vector2(elbow.x + radius, elbow.y + radius);
            var armEnd = Mathf.Max(arcCenter.x, length - halfStroke);

            var horizontalA = new Vector2(arcCenter.x, elbow.y);
            var horizontalB = new Vector2(armEnd, elbow.y);
            var verticalA = new Vector2(elbow.x, arcCenter.y);
            var verticalB = new Vector2(elbow.x, armEnd);

            var unitsPerPixel = length / (TextureSize - 1);
            var edge = unitsPerPixel * 1.2f;
            var pixels = new Color32[TextureSize * TextureSize];

            for (var y = 0; y < TextureSize; y++)
            {
                // Texture v runs bottom-up; the drawing measures downward from the elbow.
                var down = (1f - y / (float)(TextureSize - 1)) * length;

                for (var x = 0; x < TextureSize; x++)
                {
                    var right = x / (float)(TextureSize - 1) * length;
                    var point = new Vector2(right, down);

                    var distance = right < arcCenter.x && down < arcCenter.y
                        ? Mathf.Abs(Vector2.Distance(point, arcCenter) - radius)
                        : Mathf.Min(
                            SegmentDistance(point, horizontalA, horizontalB),
                            SegmentDistance(point, verticalA, verticalB));

                    var alpha = 1f - Mathf.SmoothStep(0f, 1f,
                        Mathf.Clamp01((distance - (halfStroke - edge)) / Mathf.Max(edge * 2f, 1e-4f)));

                    pixels[y * TextureSize + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        static float SegmentDistance(Vector2 point, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var lengthSqr = Vector2.Dot(ab, ab);
            if (lengthSqr <= Mathf.Epsilon)
                return Vector2.Distance(point, a);

            var t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSqr);
            return Vector2.Distance(point, a + ab * t);
        }
    }
}
