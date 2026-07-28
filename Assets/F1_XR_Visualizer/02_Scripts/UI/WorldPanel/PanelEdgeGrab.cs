using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using F1XR.Interaction.Input;
using F1XR.Interaction.World;

namespace F1XR.UI.WorldPanel
{
    public sealed class PanelEdgeGrab : MonoBehaviour
    {
        [SerializeField] XRGrabInteractable grab;
        [SerializeField] ScaleController scaleController;
        [SerializeField] Collider panelCollider;
        [SerializeField] RectTransform panelRect;
        [SerializeField] float edgeWidth = 35f;
        [SerializeField] float edgeDepth = 12f;
        [SerializeField] float rayDistance = 20f;
        [SerializeField] float handNearDistance = 0.08f;

        [Header("Line Visual")]
        [SerializeField] Color edgeColor = new(0.92f, 0.95f, 0.95f, 1f);
        [SerializeField, Min(1f)] float highlightLength = 180f;
        [SerializeField, Min(0.1f)] float lineThickness = 8f;
        [SerializeField, Range(0.25f, 1f)] float lineEndRoundness = 0.68f;
        [SerializeField, Range(0.08f, 0.45f)] float lineFadeLength = 0.3f;
        [SerializeField] float lineForwardOffset = -0.01f;

        [Header("Line Motion")]
        [SerializeField, Min(0f)] float visualSmoothTime = 0.06f;
        [SerializeField, Min(0f)] float visualDeadZone = 3f;
        [SerializeField, Min(0.01f)] float visualFadeTime = 0.08f;

        static readonly List<XRBaseInputInteractor> Interactors = new();

        enum EdgeSide
        {
            Top,
            Bottom,
            Left,
            Right
        }

        readonly List<Collider> edgeColliders = new();
        readonly List<EdgeSide> edgeSides = new();
        RectTransform edgeLine;
        Image edgeImage;
        Texture2D edgeTexture;
        Sprite edgeSprite;
        XRHandSubsystem handSubsystem;
        Vector2 lastLocalPoint;
        EdgeSide lastSide;
        Vector3 targetLinePosition;
        Vector3 smoothLinePosition;
        Vector3 smoothLineVelocity;
        Quaternion targetLineRotation = Quaternion.identity;
        EdgeSide targetLineSide;
        float lineAlpha;
        bool hasLineTarget;
        bool wasSelected;
        bool hasLockedEdge;
        EdgeSide lockedEdge;

        void Awake()
        {
            if (grab == null)
                grab = GetComponent<XRGrabInteractable>();

            if (scaleController == null)
                scaleController = GetComponent<ScaleController>();

            if (panelCollider == null)
                panelCollider = GetComponentInChildren<Collider>();

            if (panelRect == null)
                panelRect = GetComponentInChildren<Canvas>()?.GetComponent<RectTransform>();

            CreateEdgeColliders();
            CreateEdgeLine();
            SetEdgeVisible(false);
        }

        void OnEnable()
        {
            handSubsystem = XRHandInput.FindRunningSubsystem();
        }

        void OnDestroy()
        {
            if (edgeImage != null)
                edgeImage.sprite = null;

            if (edgeSprite != null)
                Destroy(edgeSprite);

            if (edgeTexture != null)
                Destroy(edgeTexture);
        }

        void OnValidate()
        {
            highlightLength = Mathf.Max(1f, highlightLength);
            lineThickness = Mathf.Max(0.1f, lineThickness);
            visualSmoothTime = Mathf.Max(0f, visualSmoothTime);
            visualDeadZone = Mathf.Max(0f, visualDeadZone);
            visualFadeTime = Mathf.Max(0.01f, visualFadeTime);

            if (edgeLine == null)
                return;

            UpdateLineVisual();
        }

        void Update()
        {
            var selected = grab != null && grab.isSelected;
            if (!selected)
            {
                if (wasSelected)
                    hasLockedEdge = false;

                wasSelected = false;
                if (lineAlpha > 0.001f ||
                    edgeLine != null &&
                    edgeLine.gameObject.activeSelf)
                {
                    UpdateHighlightVisual(false);
                }
                return;
            }

            var hasPoint = TryGetPointerEdgePoint(out var localPoint, out var side);

            if (!wasSelected)
            {
                hasLockedEdge = hasPoint;
                if (hasLockedEdge)
                    lockedEdge = side;
            }

            wasSelected = true;

            if (hasLockedEdge)
                side = lockedEdge;

            if (hasPoint)
            {
                lastLocalPoint = localPoint;
                lastSide = side;
            }

            var visible = hasLockedEdge;

            if (scaleController != null && scaleController.IsScaling)
                visible = false;

            if (visible)
                SetHighlightTarget(lastLocalPoint, lastSide);

            UpdateHighlightVisual(visible);
        }

        void CreateEdgeColliders()
        {
            if (grab == null || panelCollider == null)
                return;

            var box = panelCollider as BoxCollider;
            if (box == null)
                return;

            var size = box.size;
            var parent = panelCollider.transform;
            var colliders = grab.colliders;
            colliders.Clear();
            edgeColliders.Clear();
            edgeSides.Clear();

            AddEdgeCollider(parent, "Move Edge Top", EdgeSide.Top, new Vector3(0f, size.y * 0.5f - edgeWidth * 0.5f, 0f), new Vector3(size.x, edgeWidth, edgeDepth), colliders);
            AddEdgeCollider(parent, "Move Edge Bottom", EdgeSide.Bottom, new Vector3(0f, -size.y * 0.5f + edgeWidth * 0.5f, 0f), new Vector3(size.x, edgeWidth, edgeDepth), colliders);
            AddEdgeCollider(parent, "Move Edge Left", EdgeSide.Left, new Vector3(-size.x * 0.5f + edgeWidth * 0.5f, 0f, 0f), new Vector3(edgeWidth, size.y, edgeDepth), colliders);
            AddEdgeCollider(parent, "Move Edge Right", EdgeSide.Right, new Vector3(size.x * 0.5f - edgeWidth * 0.5f, 0f, 0f), new Vector3(edgeWidth, size.y, edgeDepth), colliders);
        }

        void AddEdgeCollider(Transform parent, string objectName, EdgeSide side, Vector3 center, Vector3 size, System.Collections.Generic.List<Collider> colliders)
        {
            var existing = parent.Find(objectName);
            var edge = existing != null ? existing.gameObject : new GameObject(objectName);
            edge.transform.SetParent(parent, false);
            edge.transform.localPosition = center;
            edge.transform.localRotation = Quaternion.identity;
            edge.transform.localScale = Vector3.one;

            var collider = edge.GetComponent<BoxCollider>();
            if (collider == null)
                collider = edge.AddComponent<BoxCollider>();

            collider.size = size;
            collider.center = Vector3.zero;
            if (edge.GetComponent<ContextIndicatorIgnore>() == null)
                edge.AddComponent<ContextIndicatorIgnore>();

            colliders.Add(collider);
            edgeColliders.Add(collider);
            edgeSides.Add(side);
        }

        void CreateEdgeLine()
        {
            if (panelRect == null)
                return;

            edgeLine = new GameObject("Move Edge Highlight", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            edgeLine.SetParent(panelRect, false);
            edgeLine.anchorMin = new Vector2(0.5f, 0.5f);
            edgeLine.anchorMax = new Vector2(0.5f, 0.5f);
            edgeLine.pivot = new Vector2(0.5f, 0.5f);
            edgeLine.SetAsLastSibling();

            edgeImage = edgeLine.GetComponent<Image>();
            edgeImage.raycastTarget = false;
            UpdateLineVisual();
            SetLineAlpha(0f);
        }

        void UpdateLineVisual()
        {
            if (edgeImage == null)
                return;

            edgeImage.color = edgeColor;
            edgeImage.sprite = CreateLineSprite();
            edgeImage.type = Image.Type.Simple;
            edgeImage.preserveAspect = false;
            SetLineAlpha(lineAlpha);
        }

        void SetHighlightTarget(Vector2 localPoint, EdgeSide side)
        {
            if (panelRect == null || edgeLine == null)
                return;

            var rect = panelRect.rect;
            var z = lineForwardOffset;

            if (side == EdgeSide.Left || side == EdgeSide.Right)
            {
                var x = side == EdgeSide.Left ? rect.xMin : rect.xMax;
                var y = Mathf.Clamp(localPoint.y, rect.yMin + highlightLength * 0.5f, rect.yMax - highlightLength * 0.5f);
                SetLineTarget(new Vector3(x, y, z), Quaternion.identity, side);
                return;
            }

            var yEdge = side == EdgeSide.Top ? rect.yMax : rect.yMin;
            var xEdge = Mathf.Clamp(localPoint.x, rect.xMin + highlightLength * 0.5f, rect.xMax - highlightLength * 0.5f);
            SetLineTarget(new Vector3(xEdge, yEdge, z), Quaternion.Euler(0f, 0f, 90f), side);
        }

        void SetLineTarget(Vector3 localPosition, Quaternion localRotation, EdgeSide side)
        {
            if (edgeLine == null)
                return;

            if (!hasLineTarget)
            {
                targetLinePosition = localPosition;
                smoothLinePosition = localPosition;
                smoothLineVelocity = Vector3.zero;
                targetLineRotation = localRotation;
                targetLineSide = side;
                hasLineTarget = true;
                edgeLine.localPosition = localPosition;
            }
            else if (side != targetLineSide)
            {
                targetLinePosition = localPosition;
                smoothLinePosition = localPosition;
                smoothLineVelocity = Vector3.zero;
                targetLineRotation = localRotation;
                targetLineSide = side;
                edgeLine.localPosition = localPosition;
                edgeLine.localRotation = localRotation;
            }
            else if (Vector3.Distance(targetLinePosition, localPosition) >= visualDeadZone)
            {
                targetLinePosition = localPosition;
                targetLineRotation = localRotation;
            }

            edgeLine.sizeDelta = new Vector2(lineThickness, highlightLength);
        }

        void UpdateHighlightVisual(bool visible)
        {
            if (edgeLine == null)
                return;

            var nextAlpha = Mathf.MoveTowards(lineAlpha, visible ? 1f : 0f, Time.deltaTime / visualFadeTime);
            lineAlpha = nextAlpha;

            if (visible && !edgeLine.gameObject.activeSelf)
                edgeLine.gameObject.SetActive(true);

            if (hasLineTarget)
            {
                smoothLinePosition = visualSmoothTime <= 0f
                    ? targetLinePosition
                    : Vector3.SmoothDamp(smoothLinePosition, targetLinePosition, ref smoothLineVelocity, visualSmoothTime);
                edgeLine.localPosition = smoothLinePosition;
                edgeLine.localRotation = targetLineRotation;
                edgeLine.sizeDelta = new Vector2(lineThickness, highlightLength);
            }

            SetLineAlpha(lineAlpha);

            if (!visible && lineAlpha <= 0.001f && edgeLine.gameObject.activeSelf)
                edgeLine.gameObject.SetActive(false);
        }

        void SetLineAlpha(float alpha)
        {
            if (edgeImage == null)
                return;

            edgeImage.color = new Color(edgeColor.r, edgeColor.g, edgeColor.b, edgeColor.a * alpha);
        }

        Sprite CreateLineSprite()
        {
            const int width = 32;
            const int height = 256;

            if (edgeTexture == null)
            {
                edgeTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
                {
                    name = "Move Edge Highlight Texture",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
            }

            var fade = Mathf.Clamp(lineFadeLength, 0.08f, 0.45f);
            var roundness = Mathf.Clamp01(lineEndRoundness);
            var pixels = new Color32[width * height];

            for (var y = 0; y < height; y++)
            {
                var v = y / (float)(height - 1);
                var endFade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Min(v, 1f - v) / fade));

                for (var x = 0; x < width; x++)
                {
                    var u = Mathf.Abs(x / (float)(width - 1) - 0.5f) * 2f;
                    var sideFade = Mathf.SmoothStep(1f, 0f, Mathf.Clamp01((u - 0.74f) / 0.26f));
                    var roundedEnd = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Min(v, 1f - v) / Mathf.Lerp(0.02f, 0.08f, roundness)));
                    var alpha = endFade * sideFade * roundedEnd;
                    pixels[y * width + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            edgeTexture.SetPixels32(pixels);
            edgeTexture.Apply(false, false);

            if (edgeSprite == null)
                edgeSprite = Sprite.Create(edgeTexture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), 100f);

            return edgeSprite;
        }

        void SetEdgeVisible(bool visible)
        {
            if (edgeLine != null && edgeLine.gameObject.activeSelf != visible)
                edgeLine.gameObject.SetActive(visible);
        }

        bool TryGetPointerEdgePoint(out Vector2 localPoint, out EdgeSide side)
        {
            return TryGetInteractorEdgePoint(out localPoint, out side) || TryGetHandEdgePoint(out localPoint, out side);
        }

        bool TryGetInteractorEdgePoint(out Vector2 localPoint, out EdgeSide side)
        {
            Interactors.Clear();
            Interactors.AddRange(FindObjectsByType<XRBaseInputInteractor>(FindObjectsInactive.Exclude));

            foreach (var interactor in Interactors)
            {
                if (interactor is not IXRRayProvider rayProvider)
                    continue;

                var origin = rayProvider.GetOrCreateRayOrigin();
                if (origin != null && TryRayEdgePoint(origin.position, origin.forward, out localPoint, out side))
                    return true;

                var end = rayProvider.rayEndTransform;
                if (end != null && TryClosestEdgePoint(end.position, handNearDistance, out localPoint, out side))
                    return true;
            }

            localPoint = default;
            side = default;
            return false;
        }

        bool TryGetHandEdgePoint(out Vector2 localPoint, out EdgeSide side)
        {
            if (handSubsystem == null || !handSubsystem.running)
                handSubsystem = XRHandInput.FindRunningSubsystem();

            if (handSubsystem != null &&
                (TryHandEdgePoint(handSubsystem.leftHand, out localPoint, out side) ||
                TryHandEdgePoint(handSubsystem.rightHand, out localPoint, out side)))
                return true;

            localPoint = default;
            side = default;
            return false;
        }

        bool TryHandEdgePoint(XRHand hand, out Vector2 localPoint, out EdgeSide side)
        {
            if (XRHandInput.TryGetJointPoint(hand, XRHandJointID.IndexTip, out var point) &&
                TryClosestEdgePoint(point, handNearDistance, out localPoint, out side))
                return true;

            localPoint = default;
            side = default;
            return false;
        }

        bool TryRayEdgePoint(Vector3 origin, Vector3 direction, out Vector2 localPoint, out EdgeSide side)
        {
            var hits = Physics.RaycastAll(origin, direction, rayDistance, ~0, QueryTriggerInteraction.Ignore);
            var bestDistance = float.MaxValue;
            localPoint = default;
            side = default;

            foreach (var hit in hits)
            {
                if (!TryGetEdgeSide(hit.collider, out var hitSide) || hit.distance >= bestDistance)
                    continue;

                bestDistance = hit.distance;
                localPoint = ToPanelLocalPoint(hit.point);
                side = hitSide;
            }

            if (bestDistance >= float.MaxValue)
                return TryPanelRayEdgePoint(hits, out localPoint, out side);

            return bestDistance < float.MaxValue;
        }

        bool TryPanelRayEdgePoint(RaycastHit[] hits, out Vector2 localPoint, out EdgeSide side)
        {
            var bestDistance = float.MaxValue;
            localPoint = default;
            side = default;

            foreach (var hit in hits)
            {
                if (hit.collider != panelCollider || hit.distance >= bestDistance)
                    continue;

                var point = ToPanelLocalPoint(hit.point);
                if (!TryGetPanelEdge(point, out var hitSide))
                    continue;

                bestDistance = hit.distance;
                localPoint = point;
                side = hitSide;
            }

            return bestDistance < float.MaxValue;
        }

        bool TryGetEdgeSide(Collider collider, out EdgeSide side)
        {
            for (var i = 0; i < edgeColliders.Count; i++)
            {
                if (edgeColliders[i] != collider)
                    continue;

                side = edgeSides[i];
                return true;
            }

            side = default;
            return false;
        }

        bool TryClosestEdgePoint(Vector3 worldPoint, float maxDistance, out Vector2 localPoint, out EdgeSide side)
        {
            var bestDistance = float.MaxValue;
            localPoint = default;
            side = default;

            for (var i = 0; i < edgeColliders.Count; i++)
            {
                var closest = edgeColliders[i].ClosestPoint(worldPoint);
                var distance = Vector3.Distance(worldPoint, closest);
                if (distance >= bestDistance || distance > maxDistance)
                    continue;

                bestDistance = distance;
                localPoint = ToPanelLocalPoint(closest);
                side = edgeSides[i];
            }

            if (bestDistance < float.MaxValue)
                return true;

            return false;
        }

        Vector2 ToPanelLocalPoint(Vector3 worldPoint)
        {
            if (panelCollider == null)
                return panelRect != null ? panelRect.InverseTransformPoint(worldPoint) : Vector2.zero;

            var local = panelCollider.transform.InverseTransformPoint(worldPoint);
            return new Vector2(local.x, local.y);
        }

        bool TryGetPanelEdge(Vector2 localPoint, out EdgeSide side)
        {
            side = default;

            var rect = panelRect != null ? panelRect.rect : new Rect(-225f, -267.5f, 450f, 535f);
            if (localPoint.x < rect.xMin - edgeWidth || localPoint.x > rect.xMax + edgeWidth ||
                localPoint.y < rect.yMin - edgeWidth || localPoint.y > rect.yMax + edgeWidth)
                return false;

            var left = Mathf.Abs(localPoint.x - rect.xMin);
            var right = Mathf.Abs(localPoint.x - rect.xMax);
            var top = Mathf.Abs(localPoint.y - rect.yMax);
            var bottom = Mathf.Abs(localPoint.y - rect.yMin);
            var min = Mathf.Min(left, right, top, bottom);

            if (min > edgeWidth)
                return false;

            if (min == right)
                side = EdgeSide.Right;
            else if (min == left)
                side = EdgeSide.Left;
            else if (min == top)
                side = EdgeSide.Top;
            else
                side = EdgeSide.Bottom;

            return true;
        }

    }

}
