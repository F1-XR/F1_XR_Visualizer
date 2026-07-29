using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using F1XR.Interaction.Input;
using F1XR.Interaction.World;

namespace F1XR.UI.WorldPanel
{
    public sealed class PanelBarHighlight : MonoBehaviour
    {
        [SerializeField] Transform bar;
        [SerializeField] Collider barCollider;
        [SerializeField] Image rim;
        [SerializeField] float rayDistance = 20f;
        [SerializeField] float handNearDistance = 0.08f;

        [Header("Bar Hover")]
        [SerializeField, Min(0f)] float hoverPadding = 18f;
        [SerializeField, Min(0f)] float barDepth = 12f;
        [SerializeField, Min(1f)] float hoverLength = 1.35f;
        [SerializeField, Min(1f)] float hoverThickness = 1.18f;

        [Header("Rim Hover")]
        [SerializeField, Range(0f, 1f)] float rimBrighten = 0.35f;
        [SerializeField, Min(1f)] float rimAlphaScale = 1.3f;
        [SerializeField, Min(0f)] float rimGrowPixels = 2f;
        [SerializeField, Range(0f, 1f)] float rimGrowAlpha = 0.8f;

        [Header("Hover Icons")]
        [SerializeField] Color iconColor = new(0.92f, 0.95f, 0.95f, 0.9f);
        [SerializeField, Min(1f)] float iconSize = 15f;
        [SerializeField, Min(0f)] float iconSpacing = 45f;
        [SerializeField, Range(0f, 0.9f)] float iconAppearAt = 0.35f;
        [SerializeField, Min(0f)] float iconDepthMargin = 0.5f;

        [Header("Icon Buttons")]
        // Darker than the bar underneath it, otherwise the disc vanishes into the bar's own grey.
        [SerializeField] Color iconHoverColor = new(0.32f, 0.34f, 0.38f, 0.8f);
        [SerializeField] Color iconPressColor = new(0.12f, 0.13f, 0.15f, 0.92f);
        [SerializeField, Min(1f)] float iconBackScale = 1.2f;
        [SerializeField, Min(0f)] float iconHitPadding = 8f;

        [Header("Actions")]
        [SerializeField] GameObject panelRoot;
        [SerializeField] ScaleController scaleController;
        [SerializeField, Min(0f)] float resetLerpSpeed = 12f;

        [Header("Haptics")]
        [SerializeField, Range(0f, 1f)] float hapticAmplitude = 0.25f;
        [SerializeField, Min(0f)] float hapticDuration = 0.04f;
        [Tooltip("Minimum time (s) between pulses so a ray shivering on the edge can't buzz the motor.")]
        [SerializeField, Min(0f)] float minPulseInterval = 0.15f;

        [Header("Motion")]
        [SerializeField, Min(0f)] float hoverEnterDelay = 0.3f;
        [SerializeField, Min(0f)] float hoverExitDelay = 0.3f;
        [SerializeField, Min(0.01f)] float hoverFadeTime = 0.28f;

        static readonly List<XRBaseInputInteractor> Interactors = new();
        static readonly List<InputDevice> InputDeviceBuffer = new();

        enum Axis
        {
            X,
            Y,
            Z
        }

        XRHandSubsystem handSubsystem;
        Vector3 baseScale = Vector3.one;
        Color baseRimColor = Color.white;
        Image rimGrow;
        Collider hoverArea;
        Image closeIcon;
        Image resetIcon;
        Image closeBack;
        Image resetBack;
        readonly List<Object> glyphAssets = new();
        Axis lengthAxis = Axis.Y;
        float hoverAmount;
        bool hoverTarget;
        float hoverDelayTimer;
        Image hoveredIcon;
        Image pressedIcon;
        bool iconPressed;
        bool triggerWasPressed;
        Vector3 panelBaseScale = Vector3.one;
        bool restoringScale;
        bool hasHoverRay;
        Vector3 hoverRayOrigin;
        Vector3 hoverRayDirection;
        InteractorHandedness hoverHandedness;
        XRBaseInputInteractor hoverInteractor;
        bool hapticWasHovered;
        float lastPulseTime = -999f;
        bool initialized;
        float resetBackBlend;
        float closeBackBlend;

        public bool IsBarHovered { get; private set; }

        void Awake()
        {
            if (bar == null)
                bar = transform;

            if (barCollider == null)
                barCollider = bar.GetComponent<Collider>();

            baseScale = bar.localScale;

            // A capsule states its own direction; a UI bar is simply longer than it is tall.
            if (barCollider is CapsuleCollider capsule)
                lengthAxis = (Axis)capsule.direction;
            else if (bar is RectTransform rect)
                lengthAxis = rect.rect.width >= rect.rect.height ? Axis.X : Axis.Y;

            if (panelRoot == null)
                panelRoot = bar.root.gameObject;

            if (scaleController == null)
                scaleController = panelRoot.GetComponent<ScaleController>();

            // Captured before anything can scale the panel, so reset always has the authored size.
            panelBaseScale = panelRoot.transform.localScale;

            hoverArea = CreateHoverArea();

            // Each background is built before its glyph so the lower sibling index puts it behind.
            var backSprite = CreateBackgroundSprite();
            resetBack = CreateIconLayer("Bar Reset Background", backSprite, -1f, iconSize * iconBackScale);
            resetIcon = CreateIconLayer("Bar Reset Icon", CreateResetSprite(), -1f, iconSize);
            closeBack = CreateIconLayer("Bar Close Background", backSprite, 1f, iconSize * iconBackScale);
            closeIcon = CreateIconLayer("Bar Close Icon", CreateCloseSprite(), 1f, iconSize);

            if (rim == null)
                rim = FindSiblingRim();

            if (rim != null)
            {
                baseRimColor = rim.color;
                rimGrow = CreateRimGrowLayer();
            }

            initialized = true;
        }

        // The bar is only ~13 units thick, so aiming at it directly is fiddly. This padded box sits
        // as a sibling rather than a child, so it keeps a fixed size while the bar itself grows.
        Collider CreateHoverArea()
        {
            if (bar.parent == null || hoverPadding <= 0f)
                return null;

            var center = barCollider is CapsuleCollider capsule ? Vector3.Scale(capsule.center, baseScale) : Vector3.zero;

            var area = new GameObject("Bar Hover Area", typeof(BoxCollider));
            area.transform.SetParent(bar.parent, false);
            area.transform.localPosition = bar.localPosition + bar.localRotation * center;
            area.transform.localRotation = bar.localRotation;
            area.transform.localScale = Vector3.one;
            area.AddComponent<ContextIndicatorIgnore>();

            var box = area.GetComponent<BoxCollider>();
            box.size = GetBarSize() + Vector3.one * (hoverPadding * 2f);
            box.center = Vector3.zero;
            return box;
        }

        // The bar's extents in its parent's units, so a capsule mesh and a UI rect can both drive the
        // hover box and the icon depth.
        Vector3 GetBarSize()
        {
            var length = (int)lengthAxis;

            if (barCollider is CapsuleCollider capsule)
            {
                var thicknessScale = Mathf.Max(Mathf.Abs(baseScale[(length + 1) % 3]), Mathf.Abs(baseScale[(length + 2) % 3]));
                var size = Vector3.one * (capsule.radius * 2f * thicknessScale);
                size[length] = capsule.height * Mathf.Abs(baseScale[length]);
                return size;
            }

            if (bar is RectTransform rect)
                return new Vector3(rect.rect.width * Mathf.Abs(baseScale.x), rect.rect.height * Mathf.Abs(baseScale.y), barDepth);

            return Vector3.one * barDepth;
        }

        // pixelsPerUnitMultiplier scales the whole 9-slice border, corner arc included, so animating
        // it would swell the rounded corners out of step with the panel behind it. A second copy of
        // the same outline at the same multiplier keeps the exact corner shape and only pushes the
        // stroke outward, which reads as a thicker rim.
        Image CreateRimGrowLayer()
        {
            var layer = new GameObject("Thin Rim Grow", typeof(RectTransform), typeof(Image));
            var rect = layer.GetComponent<RectTransform>();
            var rimRect = rim.rectTransform;
            rect.SetParent(rimRect.parent, false);
            rect.SetSiblingIndex(rimRect.GetSiblingIndex());
            rect.anchorMin = rimRect.anchorMin;
            rect.anchorMax = rimRect.anchorMax;
            rect.pivot = rimRect.pivot;
            rect.anchoredPosition3D = rimRect.anchoredPosition3D;
            rect.localRotation = rimRect.localRotation;
            rect.localScale = rimRect.localScale;
            rect.sizeDelta = rimRect.sizeDelta + new Vector2(rimGrowPixels, rimGrowPixels) * 2f;

            var image = layer.GetComponent<Image>();
            image.sprite = rim.sprite;
            image.material = rim.material;
            image.type = rim.type;
            image.fillCenter = rim.fillCenter;
            image.pixelsPerUnitMultiplier = rim.pixelsPerUnitMultiplier;
            image.preserveAspect = rim.preserveAspect;
            image.raycastTarget = false;
            image.color = new Color(baseRimColor.r, baseRimColor.g, baseRimColor.b, 0f);
            return image;
        }

        // Siblings again, so the bar's hover scaling doesn't stretch the glyphs. The canvas faces the
        // user from its -z side, so they have to sit in front of the bar's fattest hover radius.
        // side is -1 for the left end of the bar, +1 for the right.
        Image CreateIconLayer(string objectName, Sprite sprite, float side, float size)
        {
            if (bar.parent == null)
                return null;

            var alongBar = bar.localRotation * AxisDirection(lengthAxis);

            var layer = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            var rect = layer.GetComponent<RectTransform>();
            rect.SetParent(bar.parent, false);
            rect.SetAsLastSibling();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);
            rect.localRotation = Quaternion.identity;
            rect.localPosition = bar.localPosition - new Vector3(0f, 0f, IconDepth()) + alongBar * (iconSpacing * side);

            var image = layer.GetComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.raycastTarget = false;
            image.color = new Color(iconColor.r, iconColor.g, iconColor.b, 0f);
            return image;
        }

        static Vector3 AxisDirection(Axis axis)
        {
            return axis switch
            {
                Axis.X => Vector3.right,
                Axis.Z => Vector3.forward,
                _ => Vector3.up
            };
        }

        // A UI bar shares the canvas draw order with the icons, so sibling order already puts them on
        // top and only the margin is needed. A mesh bar has to be cleared in depth instead.
        float IconDepth()
        {
            if (bar is RectTransform)
                return iconDepthMargin;

            var size = GetBarSize();
            var length = (int)lengthAxis;
            var thickness = Mathf.Max(size[(length + 1) % 3], size[(length + 2) % 3]);
            return thickness * 0.5f * hoverThickness + iconDepthMargin;
        }

        // A bare cross with round caps. The arms stop short of the reset ring's radius so the two
        // glyphs read as the same weight side by side.
        Sprite CreateCloseSprite()
        {
            const float armLength = 0.55f;
            const float stroke = 0.085f;

            return CreateGlyphSprite("Bar Close Icon", point => Mathf.Min(
                DistanceToSegment(point, new Vector2(-armLength, -armLength), new Vector2(armLength, armLength)),
                DistanceToSegment(point, new Vector2(-armLength, armLength), new Vector2(armLength, -armLength))) - stroke);
        }

        // An open ring broken at the top: the arc sweeps up the right side and ends in a leftward
        // arrowhead, and its other end gets a round cap, which is the usual reset glyph.
        Sprite CreateResetSprite()
        {
            const float ringRadius = 0.72f;
            const float stroke = 0.075f;
            const float arrowAngle = 95f;
            const float capAngle = 155f;
            const float arrowLength = 0.3f;
            const float arrowWidth = 0.17f;

            var head = arrowAngle * Mathf.Deg2Rad;
            var anchor = new Vector2(Mathf.Cos(head), Mathf.Sin(head)) * ringRadius;
            var tangent = new Vector2(-Mathf.Sin(head), Mathf.Cos(head));
            var normal = new Vector2(-tangent.y, tangent.x);
            var back = anchor - tangent * (arrowLength * 0.25f);
            var tip = anchor + tangent * arrowLength;
            var left = back + normal * arrowWidth;
            var right = back - normal * arrowWidth;
            var cap = new Vector2(Mathf.Cos(capAngle * Mathf.Deg2Rad), Mathf.Sin(capAngle * Mathf.Deg2Rad)) * ringRadius;

            return CreateGlyphSprite("Bar Reset Icon", point =>
            {
                var angle = Mathf.Repeat(Mathf.Atan2(point.y, point.x) * Mathf.Rad2Deg, 360f);
                var inGap = angle > arrowAngle && angle < capAngle;
                var arc = inGap ? float.MaxValue : Mathf.Abs(point.magnitude - ringRadius) - stroke;
                var roundCap = Vector2.Distance(point, cap) - stroke;
                return Mathf.Min(arc, Mathf.Min(roundCap, DistanceToTriangle(point, tip, left, right)));
            });
        }

        Sprite CreateBackgroundSprite()
        {
            return CreateGlyphSprite("Bar Icon Background", point => point.magnitude - 0.94f);
        }

        Sprite CreateGlyphSprite(string name, System.Func<Vector2, float> signedDistance)
        {
            const int size = 128;

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name + " Texture",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color32[size * size];
            var half = (size - 1) * 0.5f;
            var feather = 1.6f / half;

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var point = new Vector2((x - half) / half, (y - half) / half);
                    var alpha = Mathf.SmoothStep(1f, 0f, Mathf.Clamp01(signedDistance(point) / feather));
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            var sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = name + " Sprite";
            glyphAssets.Add(texture);
            glyphAssets.Add(sprite);
            return sprite;
        }

        static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
        {
            var line = end - start;
            var t = Mathf.Clamp01(Vector2.Dot(point - start, line) / Vector2.Dot(line, line));
            return Vector2.Distance(point, start + line * t);
        }

        // Negative inside the triangle; the winding is normalised so the caller can pass the corners
        // in either order.
        static float DistanceToTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
        {
            if (Cross(b - a, c - a) < 0f)
                (b, c) = (c, b);

            return Mathf.Max(EdgeDistance(point, a, b), Mathf.Max(EdgeDistance(point, b, c), EdgeDistance(point, c, a)));
        }

        static float EdgeDistance(Vector2 point, Vector2 start, Vector2 end)
        {
            var edge = (end - start).normalized;
            return Vector2.Dot(point - start, new Vector2(edge.y, -edge.x));
        }

        static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        Image FindSiblingRim()
        {
            var parent = bar.parent;
            if (parent == null)
                return null;

            foreach (var image in parent.GetComponentsInChildren<Image>(true))
            {
                if (image.transform != bar && image.name.IndexOf("Rim", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return image;
            }

            return null;
        }

        void OnEnable()
        {
            handSubsystem = XRHandInput.FindRunningSubsystem();
        }

        void OnDisable()
        {
            hoverAmount = 0f;
            IsBarHovered = false;
            hoverTarget = false;
            hoverDelayTimer = 0f;
            hapticWasHovered = false;
            hoverInteractor = null;
            hoveredIcon = null;
            pressedIcon = null;
            iconPressed = false;
            triggerWasPressed = false;
            resetBackBlend = 0f;
            closeBackBlend = 0f;
            ApplyHover(0f);
        }

        void OnDestroy()
        {
            if (rimGrow != null)
                Destroy(rimGrow.gameObject);

            if (hoverArea != null)
                Destroy(hoverArea.gameObject);

            DestroyIcon(resetBack);
            DestroyIcon(resetIcon);
            DestroyIcon(closeBack);
            DestroyIcon(closeIcon);

            foreach (var asset in glyphAssets)
            {
                if (asset != null)
                    Destroy(asset);
            }

            glyphAssets.Clear();
        }

        static void DestroyIcon(Image image)
        {
            if (image == null)
                return;

            image.sprite = null;
            Destroy(image.gameObject);
        }

        void OnValidate()
        {
            hoverFadeTime = Mathf.Max(0.01f, hoverFadeTime);

            if (!Application.isPlaying || bar == null)
                return;

            ApplyHover(hoverAmount);
        }

        void Update()
        {
            IsBarHovered = IsPointerOnBar();

            UpdateHoverHaptics();
            UpdateHoverDelay();
            hoverAmount = Mathf.MoveTowards(hoverAmount, hoverTarget ? 1f : 0f, Time.deltaTime / hoverFadeTime);
            UpdateIconButtons();
            UpdateIconClick();
            UpdateScaleRestore();
            ApplyHover(EaseOutQuint(hoverAmount));
        }

        // The hover has to hold for the full delay before the bar commits, so a ray sweeping across on
        // its way somewhere else never sets the animation off, and a flick back within the delay
        // simply cancels the pending change.
        void UpdateHoverDelay()
        {
            if (IsBarHovered == hoverTarget)
            {
                hoverDelayTimer = 0f;
                return;
            }

            hoverDelayTimer += Time.deltaTime;

            if (hoverDelayTimer < (IsBarHovered ? hoverEnterDelay : hoverExitDelay))
                return;

            hoverTarget = IsBarHovered;
            hoverDelayTimer = 0f;
        }

        // The bar should shoot out and settle rather than creep, which is the easeOutQuint curve
        // tweening libraries reach for on this kind of reveal.
        static float EaseOutQuint(float t)
        {
            var remaining = 1f - Mathf.Clamp01(t);
            return 1f - remaining * remaining * remaining * remaining * remaining;
        }

        void UpdateIconButtons()
        {
            hoveredIcon = null;
            iconPressed = false;

            if (!hasHoverRay || hoverAmount <= iconAppearAt || bar.parent == null)
                return;

            var localOrigin = bar.parent.InverseTransformPoint(hoverRayOrigin);
            var localDirection = bar.parent.InverseTransformDirection(hoverRayDirection);
            var bestDistance = float.MaxValue;

            if (TryIconHit(resetIcon, localOrigin, localDirection, out var resetDistance))
            {
                bestDistance = resetDistance;
                hoveredIcon = resetIcon;
            }

            if (TryIconHit(closeIcon, localOrigin, localDirection, out var closeDistance) && closeDistance < bestDistance)
                hoveredIcon = closeIcon;

            if (hoveredIcon != null)
                iconPressed = IsTriggerPressed(hoverHandedness);
        }

        // Fires on release rather than press, so sliding off an icon while held cancels the click the
        // way a normal button does.
        void UpdateIconClick()
        {
            if (iconPressed && !triggerWasPressed)
            {
                pressedIcon = hoveredIcon;
            }
            else if (!iconPressed && triggerWasPressed)
            {
                if (pressedIcon != null && pressedIcon == hoveredIcon)
                    InvokeIcon(pressedIcon);

                pressedIcon = null;
            }

            triggerWasPressed = iconPressed;
        }

        void InvokeIcon(Image icon)
        {
            if (icon == closeIcon)
                ClosePanel();
            else if (icon == resetIcon)
                ResetPanelScale();
        }

        public void ClosePanel()
        {
            if (panelRoot != null)
                panelRoot.SetActive(false);
        }

        public void ResetPanelScale()
        {
            restoringScale = panelRoot != null;
        }

        void UpdateScaleRestore()
        {
            if (!restoringScale || panelRoot == null)
                return;

            // A scale started while the panel is easing back wins; fighting the user's hands would
            // feel like the panel is stuck.
            if (scaleController != null && scaleController.IsScaling)
            {
                restoringScale = false;
                return;
            }

            var panel = panelRoot.transform;
            var lerp = resetLerpSpeed <= 0f ? 1f : 1f - Mathf.Exp(-resetLerpSpeed * Time.deltaTime);
            panel.localScale = Vector3.Lerp(panel.localScale, panelBaseScale, lerp);

            if (Vector3.Distance(panel.localScale, panelBaseScale) > panelBaseScale.magnitude * 0.001f)
                return;

            panel.localScale = panelBaseScale;
            restoringScale = false;
        }

        // The icons have no colliders of their own: they sit inside the padded hover box, so a
        // physics ray would always report the box first. Intersecting their plane directly is both
        // cheaper and immune to that ordering.
        bool TryIconHit(Image icon, Vector3 localOrigin, Vector3 localDirection, out float distance)
        {
            distance = float.MaxValue;

            if (icon == null || Mathf.Abs(localDirection.z) < 0.0001f)
                return false;

            var center = icon.rectTransform.localPosition;
            var travel = (center.z - localOrigin.z) / localDirection.z;
            if (travel <= 0f)
                return false;

            var point = localOrigin + localDirection * travel;
            distance = Vector2.Distance(new Vector2(point.x, point.y), new Vector2(center.x, center.y));
            return distance <= iconSize * 0.5f * iconBackScale + iconHitPadding;
        }

        static bool IsTriggerPressed(InteractorHandedness handedness)
        {
            var node = ToNode(handedness);

            if (node == XRNode.Head)
                return false;

            InputDeviceBuffer.Clear();
            UnityEngine.XR.InputDevices.GetDevicesAtXRNode(node, InputDeviceBuffer);

            foreach (var device in InputDeviceBuffer)
            {
                if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out var pressed) && pressed)
                    return true;
            }

            return false;
        }

        // Deliberately keyed off the raw touch rather than the delayed growth: the buzz is what
        // confirms the ray landed, so waiting out hoverEnterDelay would read as a lagging rumble.
        void UpdateHoverHaptics()
        {
            if (IsBarHovered && !hapticWasHovered)
                Pulse(hapticAmplitude, hapticDuration);

            hapticWasHovered = IsBarHovered;
        }

        void Pulse(float amplitude, float duration)
        {
            if (amplitude <= 0f || duration <= 0f)
                return;

            if (Time.time - lastPulseTime < minPulseInterval)
                return;

            lastPulseTime = Time.time;

            // XRI routes SendHapticImpulse through a HapticImpulsePlayer, and these interactors have
            // none wired up, so the impulse goes straight to the device. The interactor stays as a
            // fallback for rigs that do have the player set up.
            if (!SendDeviceImpulse(hoverHandedness, amplitude, duration) && hoverInteractor != null)
                hoverInteractor.SendHapticImpulse(Mathf.Clamp01(amplitude), duration);
        }

        static bool SendDeviceImpulse(InteractorHandedness handedness, float amplitude, float duration)
        {
            var node = ToNode(handedness);
            if (node == XRNode.Head)
                return false;

            InputDeviceBuffer.Clear();
            UnityEngine.XR.InputDevices.GetDevicesAtXRNode(node, InputDeviceBuffer);

            var sent = false;

            foreach (var device in InputDeviceBuffer)
            {
                if (device.TryGetHapticCapabilities(out var capabilities) && capabilities.supportsImpulse)
                    sent |= device.SendHapticImpulse(0u, Mathf.Clamp01(amplitude), duration);
            }

            return sent;
        }

        static XRNode ToNode(InteractorHandedness handedness)
        {
            return handedness switch
            {
                InteractorHandedness.Left => XRNode.LeftHand,
                InteractorHandedness.Right => XRNode.RightHand,
                _ => XRNode.Head
            };
        }

        void ApplyHover(float amount)
        {
            // Awake never runs in edit mode, so without this an assembly reload would fire OnDisable
            // with baseRimColor still at its Color.white initialiser and repaint the authored rim.
            if (!initialized)
                return;

            if (bar != null)
            {
                var length = Mathf.Lerp(1f, hoverLength, amount);
                var thickness = Mathf.Lerp(1f, hoverThickness, amount);
                bar.localScale = lengthAxis switch
                {
                    Axis.X => new Vector3(baseScale.x * length, baseScale.y * thickness, baseScale.z * thickness),
                    Axis.Z => new Vector3(baseScale.x * thickness, baseScale.y * thickness, baseScale.z * length),
                    _ => new Vector3(baseScale.x * thickness, baseScale.y * length, baseScale.z * thickness)
                };
            }

            // Held back until the bar has actually opened up, so the glyphs never flash on a grazing
            // hover.
            var reveal = Mathf.InverseLerp(iconAppearAt, 1f, amount);
            ApplyIcon(resetIcon, reveal);
            ApplyIcon(closeIcon, reveal);
            ApplyIconBackground(resetBack, resetIcon, ref resetBackBlend, reveal);
            ApplyIconBackground(closeBack, closeIcon, ref closeBackBlend, reveal);

            if (rim == null)
                return;

            var color = Color.Lerp(baseRimColor, Color.white, rimBrighten * amount);
            color.a = Mathf.Clamp01(baseRimColor.a * Mathf.Lerp(1f, rimAlphaScale, amount));
            rim.color = color;

            if (rimGrow == null)
                return;

            rimGrow.rectTransform.sizeDelta = rim.rectTransform.sizeDelta +
                new Vector2(rimGrowPixels, rimGrowPixels) * (2f * amount);
            rimGrow.color = new Color(color.r, color.g, color.b, color.a * rimGrowAlpha * amount);
        }

        void ApplyIcon(Image image, float reveal)
        {
            if (image == null)
                return;

            image.color = new Color(iconColor.r, iconColor.g, iconColor.b, iconColor.a * reveal);
            image.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, reveal);

            var visible = reveal > 0f;
            if (image.gameObject.activeSelf != visible)
                image.gameObject.SetActive(visible);
        }

        void ApplyIconBackground(Image back, Image icon, ref float blend, float reveal)
        {
            if (back == null)
                return;

            var hovered = icon != null && hoveredIcon == icon;
            blend = Mathf.MoveTowards(blend, hovered ? 1f : 0f, Time.deltaTime / hoverFadeTime);

            // The press state snaps rather than fades, so the button feels like it answers the pull.
            var color = hovered && iconPressed ? iconPressColor : iconHoverColor;
            back.color = new Color(color.r, color.g, color.b, color.a * blend * reveal);
            back.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, reveal);

            var visible = blend > 0f && reveal > 0f;
            if (back.gameObject.activeSelf != visible)
                back.gameObject.SetActive(visible);
        }

        bool IsPointerOnBar()
        {
            hasHoverRay = false;
            hoverInteractor = null;

            if ((barCollider == null && hoverArea == null) || !isActiveAndEnabled)
                return false;

            Interactors.Clear();
            Interactors.AddRange(FindObjectsByType<XRBaseInputInteractor>(FindObjectsInactive.Exclude));

            foreach (var interactor in Interactors)
            {
                if (interactor is not IXRRayProvider rayProvider)
                    continue;

                var origin = rayProvider.GetOrCreateRayOrigin();
                if (origin != null && IsRayOnBar(origin.position, origin.forward))
                {
                    // Kept so the icon buttons and the haptic pulse follow the very hand that is
                    // pointing at the bar.
                    hasHoverRay = true;
                    hoverRayOrigin = origin.position;
                    hoverRayDirection = origin.forward;
                    hoverHandedness = interactor.handedness;
                    hoverInteractor = interactor;
                    return true;
                }

                var end = rayProvider.rayEndTransform;
                if (end != null && IsNearBar(end.position))
                {
                    hoverHandedness = interactor.handedness;
                    hoverInteractor = interactor;
                    return true;
                }
            }

            return IsHandNearBar();
        }

        bool IsRayOnBar(Vector3 origin, Vector3 direction)
        {
            var hits = Physics.RaycastAll(origin, direction, rayDistance, ~0, QueryTriggerInteraction.Ignore);
            var bestDistance = float.MaxValue;
            var onBar = false;

            // Only the nearest hit counts, otherwise the bar lights up through the panel.
            foreach (var hit in hits)
            {
                if (hit.distance >= bestDistance)
                    continue;

                bestDistance = hit.distance;
                onBar = hit.collider == barCollider || hit.collider == hoverArea;
            }

            return onBar;
        }

        bool IsHandNearBar()
        {
            if (handSubsystem == null || !handSubsystem.running)
                handSubsystem = XRHandInput.FindRunningSubsystem();

            if (handSubsystem == null)
                return false;

            return IsHandTipNearBar(handSubsystem.leftHand) || IsHandTipNearBar(handSubsystem.rightHand);
        }

        bool IsHandTipNearBar(XRHand hand)
        {
            return XRHandInput.TryGetJointPoint(hand, XRHandJointID.IndexTip, out var point) && IsNearBar(point);
        }

        bool IsNearBar(Vector3 worldPoint)
        {
            var target = hoverArea != null ? hoverArea : barCollider;
            if (target == null)
                return false;

            var closest = target.ClosestPoint(worldPoint);
            return Vector3.Distance(worldPoint, closest) <= handNearDistance;
        }
    }
}
