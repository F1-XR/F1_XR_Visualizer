using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARSubsystems;

namespace F1XR.RestAPI.Replay.Room
{
    [DisallowMultipleComponent]
    public sealed class ShowcaseLayout : MonoBehaviour
    {
        private static readonly Color EntryColor =
            new(0.2f, 1f, 0.3f, 0.95f);
        private static readonly Color HeroColor =
            new(1f, 0.9f, 0.15f, 0.95f);
        private static readonly Color ExitColor =
            new(1f, 0.2f, 0.8f, 0.95f);

        [Header("Sources")]
        [SerializeField] private WallDiscovery wallDiscovery;
        [SerializeField] private Transform xrMainCamera;

        [Header("Entry Pose")]
        [SerializeField] private float entryHorizontalOffset;
        [SerializeField] private float entryVerticalOffset;
        [SerializeField] private float entryDepthOffset = 0.05f;
        [SerializeField] private float entryYawOffset;

        [Header("Hero Capture")]
        [SerializeField, Min(0f)] private float heroForwardDistance = 1.5f;
        [SerializeField] private float heroHorizontalOffset;
        [SerializeField] private float heroVerticalOffset = -0.3f;
        [SerializeField] private float heroYawOffset;

        [Header("Exit Pose")]
        [SerializeField] private float exitHorizontalOffset;
        [SerializeField] private float exitVerticalOffset;
        [SerializeField] private float exitDepthOffset = 0.05f;
        [SerializeField] private float exitYawOffset;

        [Header("Validation")]
        [SerializeField, Min(0f)] private float wallBoundsSafetyMargin = 0.1f;

        [Header("Development Debug")]
        [SerializeField] private bool showDebug = true;
        [SerializeField, Min(0.01f)] private float markerSize = 0.08f;
        [SerializeField, Min(0.05f)] private float arrowLength = 0.35f;
        [SerializeField, Min(0.001f)] private float debugLineWidth = 0.015f;

        private Pose entryPose = new(Vector3.zero, Quaternion.identity);
        private Pose heroPose = new(Vector3.zero, Quaternion.identity);
        private Pose exitPose = new(Vector3.zero, Quaternion.identity);
        private Vector3 entryTravelDirection;
        private Vector3 exitTravelDirection;
        private TrackableId entryWallId;
        private TrackableId exitWallId;
        private bool entryPoseValid;
        private bool heroPoseValid;
        private bool exitPoseValid;
        private Vector3 heroLocalPosition;
        private Quaternion heroLocalRotation = Quaternion.identity;
        private int observedSelectionRevision = -1;
        private bool rebuildRequested = true;
        private bool entryBoundsWarningActive;
        private bool exitBoundsWarningActive;
        private TrackableId entryBoundsWarningWallId;
        private TrackableId exitBoundsWarningWallId;
        private Vector3 lastRootPosition;
        private Quaternion lastRootRotation;
        private Vector3 lastRootScale;

        private Transform debugRoot;
        private PoseDebugView entryDebug;
        private PoseDebugView heroDebug;
        private PoseDebugView exitDebug;
        private LineRenderer entryHeroGuide;
        private LineRenderer heroExitGuide;
        private Material debugMaterial;
        private readonly Vector3[] guidePositions = new Vector3[2];

        public bool EntryPoseValid => entryPoseValid;
        public bool HeroPoseValid => heroPoseValid;
        public bool ExitPoseValid => exitPoseValid;
        public bool IsLayoutValid =>
            entryPoseValid &&
            heroPoseValid &&
            exitPoseValid &&
            entryWallId != exitWallId;
        public Pose EntryPose => entryPose;
        public Pose HeroPose => heroPose;
        public Pose ExitPose => exitPose;
        public Vector3 EntryTravelDirection => entryTravelDirection;
        public Vector3 ExitTravelDirection => exitTravelDirection;
        public TrackableId EntryWallId => entryWallId;
        public TrackableId ExitWallId => exitWallId;

        private void Reset()
        {
            ResolveReferences();
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            RememberRootPose();
            rebuildRequested = true;
            RebuildLayout();
        }

        private void Update()
        {
            if (wallDiscovery != null &&
                observedSelectionRevision != wallDiscovery.SelectionRevision)
            {
                rebuildRequested = true;
            }

            if (HasRootPoseChanged())
            {
                RememberRootPose();
                rebuildRequested = true;
            }

            if (rebuildRequested)
                RebuildLayout();
        }

        private void LateUpdate()
        {
            if (!showDebug || xrMainCamera == null)
                return;

            entryDebug?.FaceLabelTowards(xrMainCamera);
            heroDebug?.FaceLabelTowards(xrMainCamera);
            exitDebug?.FaceLabelTowards(xrMainCamera);
        }

        private void OnDisable()
        {
            DestroyDebug();
        }

        private void OnValidate()
        {
            heroForwardDistance = Mathf.Max(0f, heroForwardDistance);
            wallBoundsSafetyMargin = Mathf.Max(0f, wallBoundsSafetyMargin);
            markerSize = Mathf.Max(0.01f, markerSize);
            arrowLength = Mathf.Max(0.05f, arrowLength);
            debugLineWidth = Mathf.Max(0.001f, debugLineWidth);
            rebuildRequested = true;
        }

        public bool TryGetEntryPose(out Pose pose)
        {
            pose = entryPose;
            return entryPoseValid;
        }

        public bool TryGetHeroPose(out Pose pose)
        {
            pose = heroPose;
            return heroPoseValid;
        }

        public bool TryGetExitPose(out Pose pose)
        {
            pose = exitPose;
            return exitPoseValid;
        }

        [ContextMenu("Capture Hero From Current View")]
        public void CaptureHeroFromCurrentView()
        {
            TryCaptureHeroFromCurrentView();
        }

        public bool TryCaptureHeroFromCurrentView()
        {
            ResolveReferences();
            if (xrMainCamera == null)
            {
                Debug.LogWarning(
                    "[ShowcaseLayout] XR Main Camera is required to capture the Hero Pose.",
                    this);
                return false;
            }

            var up = transform.up.normalized;
            var forward = Vector3.ProjectOnPlane(xrMainCamera.forward, up);
            if (forward.sqrMagnitude < 0.0001f)
            {
                Debug.LogWarning(
                    "[ShowcaseLayout] Hero Pose capture failed because the camera forward direction is nearly vertical.",
                    this);
                return false;
            }

            forward.Normalize();
            var right = Vector3.Cross(up, forward).normalized;
            var capturedForward =
                Quaternion.AngleAxis(heroYawOffset, up) * forward;
            var worldPosition =
                xrMainCamera.position +
                forward * heroForwardDistance +
                right * heroHorizontalOffset +
                up * heroVerticalOffset;
            var worldRotation = Quaternion.LookRotation(capturedForward, up);

            heroLocalPosition = transform.InverseTransformPoint(worldPosition);
            heroLocalRotation =
                Quaternion.Inverse(transform.rotation) * worldRotation;
            heroPoseValid = true;
            rebuildRequested = true;
            RebuildLayout();
            return true;
        }

        [ContextMenu("Clear Hero Capture")]
        public void ClearHeroCapture()
        {
            heroPoseValid = false;
            heroPose = new Pose(Vector3.zero, Quaternion.identity);
            rebuildRequested = true;
            RebuildLayout();
        }

        [ContextMenu("Rebuild Layout")]
        public void RebuildLayout()
        {
            ResolveReferences();
            rebuildRequested = false;
            observedSelectionRevision =
                wallDiscovery != null ? wallDiscovery.SelectionRevision : -1;

            RebuildEntryPose();
            RebuildHeroPose();
            RebuildExitPose();
            RefreshDebug();
        }

        public void SetDebugVisible(bool visible)
        {
            showDebug = visible;
            rebuildRequested = true;
            RebuildLayout();
        }

        private void ResolveReferences()
        {
            if (wallDiscovery == null)
                wallDiscovery = GetComponent<WallDiscovery>();

            if (xrMainCamera == null && Camera.main != null)
                xrMainCamera = Camera.main.transform;
        }

        private bool HasRootPoseChanged()
        {
            return transform.position != lastRootPosition ||
                transform.rotation != lastRootRotation ||
                transform.lossyScale != lastRootScale;
        }

        private void RememberRootPose()
        {
            lastRootPosition = transform.position;
            lastRootRotation = transform.rotation;
            lastRootScale = transform.lossyScale;
        }

        private void RebuildEntryPose()
        {
            entryPoseValid = false;
            entryPose = new Pose(Vector3.zero, Quaternion.identity);
            entryTravelDirection = Vector3.zero;
            entryWallId = default;

            if (wallDiscovery == null ||
                !wallDiscovery.TryGetEntryWall(out var wall))
            {
                entryBoundsWarningActive = false;
                return;
            }

            entryWallId = wall.TrackableId;
            entryTravelDirection = wall.InwardNormal;
            if (!ValidateWallBounds(
                    wall,
                    entryHorizontalOffset,
                    entryVerticalOffset,
                    "Entry",
                    ref entryBoundsWarningActive,
                    ref entryBoundsWarningWallId))
            {
                return;
            }

            var position =
                wall.Center +
                wall.HorizontalAxis * entryHorizontalOffset +
                wall.VerticalAxis * entryVerticalOffset +
                wall.InwardNormal * entryDepthOffset;
            var rotation = Quaternion.LookRotation(
                entryTravelDirection,
                wall.VerticalAxis);
            rotation =
                Quaternion.AngleAxis(entryYawOffset, wall.VerticalAxis) *
                rotation;

            entryPose = new Pose(position, rotation);
            entryPoseValid = true;
        }

        private void RebuildHeroPose()
        {
            if (!heroPoseValid)
            {
                heroPose = new Pose(Vector3.zero, Quaternion.identity);
                return;
            }

            heroPose = new Pose(
                transform.TransformPoint(heroLocalPosition),
                transform.rotation * heroLocalRotation);
        }

        private void RebuildExitPose()
        {
            exitPoseValid = false;
            exitPose = new Pose(Vector3.zero, Quaternion.identity);
            exitTravelDirection = Vector3.zero;
            exitWallId = default;

            if (wallDiscovery == null ||
                !wallDiscovery.TryGetExitWall(out var wall))
            {
                exitBoundsWarningActive = false;
                return;
            }

            exitWallId = wall.TrackableId;
            exitTravelDirection = -wall.InwardNormal;
            if (!ValidateWallBounds(
                    wall,
                    exitHorizontalOffset,
                    exitVerticalOffset,
                    "Exit",
                    ref exitBoundsWarningActive,
                    ref exitBoundsWarningWallId))
            {
                return;
            }

            var position =
                wall.Center +
                wall.HorizontalAxis * exitHorizontalOffset +
                wall.VerticalAxis * exitVerticalOffset +
                wall.InwardNormal * exitDepthOffset;
            var rotation = Quaternion.LookRotation(
                exitTravelDirection,
                wall.VerticalAxis);
            rotation =
                Quaternion.AngleAxis(exitYawOffset, wall.VerticalAxis) *
                rotation;

            exitPose = new Pose(position, rotation);
            exitPoseValid = true;
        }

        private bool ValidateWallBounds(
            WallCandidate wall,
            float horizontalOffset,
            float verticalOffset,
            string poseName,
            ref bool warningActive,
            ref TrackableId warningWallId)
        {
            var usableHalfWidth =
                Mathf.Max(0f, wall.Width * 0.5f - wallBoundsSafetyMargin);
            var usableHalfHeight =
                Mathf.Max(0f, wall.Height * 0.5f - wallBoundsSafetyMargin);
            var withinBounds =
                Mathf.Abs(horizontalOffset) <= usableHalfWidth &&
                Mathf.Abs(verticalOffset) <= usableHalfHeight;

            if (withinBounds)
            {
                warningActive = false;
                return true;
            }

            if (!warningActive || warningWallId != wall.TrackableId)
            {
                Debug.LogWarning(
                    $"[ShowcaseLayout] {poseName} Pose is outside the usable wall bounds. " +
                    $"Offsets: ({horizontalOffset:0.##}, {verticalOffset:0.##}) m, " +
                    $"usable half-size: ({usableHalfWidth:0.##}, {usableHalfHeight:0.##}) m.",
                    this);
            }

            warningActive = true;
            warningWallId = wall.TrackableId;
            return false;
        }

        private void RefreshDebug()
        {
            if (!showDebug || !isActiveAndEnabled)
            {
                DestroyDebug();
                return;
            }

            EnsureDebug();
            if (debugRoot == null)
                return;

            entryDebug.Refresh(
                entryPoseValid,
                entryPose,
                entryTravelDirection,
                markerSize,
                arrowLength,
                debugLineWidth);
            heroDebug.Refresh(
                heroPoseValid,
                heroPose,
                heroPose.rotation * Vector3.forward,
                markerSize,
                arrowLength,
                debugLineWidth);
            exitDebug.Refresh(
                exitPoseValid,
                exitPose,
                exitTravelDirection,
                markerSize,
                arrowLength,
                debugLineWidth);

            RefreshGuide(
                entryHeroGuide,
                entryPoseValid && heroPoseValid,
                entryPose.position,
                heroPose.position);
            RefreshGuide(
                heroExitGuide,
                heroPoseValid && exitPoseValid,
                heroPose.position,
                exitPose.position);
        }

        private void EnsureDebug()
        {
            if (debugRoot != null)
                return;

            var rootObject = new GameObject("ShowcaseLayoutDebug");
            rootObject.layer = 2;
            debugRoot = rootObject.transform;
            debugRoot.SetParent(transform, false);

            var shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                Debug.LogWarning(
                    "[ShowcaseLayout] No debug-compatible shader was found.",
                    this);
                showDebug = false;
                DestroyDebug();
                return;
            }

            debugMaterial = new Material(shader)
            {
                name = "Showcase Layout Debug Material",
                hideFlags = HideFlags.HideAndDontSave
            };

            entryDebug = new PoseDebugView(
                debugRoot,
                "Entry",
                "ENTRY",
                EntryColor,
                debugMaterial);
            heroDebug = new PoseDebugView(
                debugRoot,
                "Hero",
                "HERO",
                HeroColor,
                debugMaterial);
            exitDebug = new PoseDebugView(
                debugRoot,
                "Exit",
                "EXIT",
                ExitColor,
                debugMaterial);
            entryHeroGuide = CreateLine("Entry to Hero Guide", EntryColor);
            heroExitGuide = CreateLine("Hero to Exit Guide", ExitColor);
        }

        private LineRenderer CreateLine(string name, Color color)
        {
            var lineObject = new GameObject(name);
            lineObject.layer = 2;
            lineObject.transform.SetParent(debugRoot, false);
            var line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.sharedMaterial = debugMaterial;
            line.startColor = color;
            line.endColor = color;
            line.startWidth = debugLineWidth;
            line.endWidth = debugLineWidth;
            line.alignment = LineAlignment.View;
            line.numCornerVertices = 2;
            line.numCapVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        private void RefreshGuide(
            LineRenderer line,
            bool visible,
            Vector3 start,
            Vector3 end)
        {
            line.gameObject.SetActive(visible);
            if (!visible)
                return;

            line.startWidth = debugLineWidth;
            line.endWidth = debugLineWidth;
            guidePositions[0] = debugRoot.InverseTransformPoint(start);
            guidePositions[1] = debugRoot.InverseTransformPoint(end);
            line.positionCount = guidePositions.Length;
            line.SetPositions(guidePositions);
        }

        private void DestroyDebug()
        {
            entryDebug = null;
            heroDebug = null;
            exitDebug = null;
            entryHeroGuide = null;
            heroExitGuide = null;

            if (debugRoot != null)
            {
                Destroy(debugRoot.gameObject);
                debugRoot = null;
            }

            if (debugMaterial != null)
            {
                Destroy(debugMaterial);
                debugMaterial = null;
            }
        }

        private sealed class PoseDebugView
        {
            private readonly Transform parent;
            private readonly GameObject root;
            private readonly Transform marker;
            private readonly MeshRenderer markerRenderer;
            private readonly LineRenderer arrow;
            private readonly TextMesh label;
            private readonly Color color;
            private readonly MaterialPropertyBlock colorProperties = new();
            private readonly Vector3[] arrowPositions = new Vector3[5];

            public PoseDebugView(
                Transform parent,
                string name,
                string labelText,
                Color color,
                Material material)
            {
                this.parent = parent;
                this.color = color;
                root = new GameObject(name);
                root.layer = 2;
                root.transform.SetParent(parent, false);

                var markerObject =
                    GameObject.CreatePrimitive(PrimitiveType.Sphere);
                markerObject.name = "Marker";
                markerObject.layer = 2;
                markerObject.transform.SetParent(root.transform, false);
                var collider = markerObject.GetComponent<Collider>();
                if (collider != null)
                {
                    collider.enabled = false;
                    Destroy(collider);
                }

                marker = markerObject.transform;
                markerRenderer = markerObject.GetComponent<MeshRenderer>();
                markerRenderer.sharedMaterial = material;
                markerRenderer.shadowCastingMode = ShadowCastingMode.Off;
                markerRenderer.receiveShadows = false;

                var arrowObject = new GameObject("Direction");
                arrowObject.layer = 2;
                arrowObject.transform.SetParent(root.transform, false);
                arrow = arrowObject.AddComponent<LineRenderer>();
                arrow.useWorldSpace = false;
                arrow.sharedMaterial = material;
                arrow.startColor = color;
                arrow.endColor = color;
                arrow.alignment = LineAlignment.View;
                arrow.numCornerVertices = 2;
                arrow.numCapVertices = 2;
                arrow.shadowCastingMode = ShadowCastingMode.Off;
                arrow.receiveShadows = false;

                var labelObject = new GameObject("Label");
                labelObject.layer = 2;
                labelObject.transform.SetParent(root.transform, false);
                label = labelObject.AddComponent<TextMesh>();
                label.text = labelText;
                label.color = color;
                label.anchor = TextAnchor.MiddleCenter;
                label.alignment = TextAlignment.Center;
                label.characterSize = 0.025f;
                label.fontSize = 36;
            }

            public void Refresh(
                bool visible,
                Pose pose,
                Vector3 direction,
                float markerScale,
                float directionLength,
                float lineWidth)
            {
                root.SetActive(visible);
                if (!visible)
                    return;

                root.transform.SetPositionAndRotation(
                    pose.position,
                    pose.rotation);
                marker.localPosition = Vector3.zero;
                marker.localScale = Vector3.one * markerScale;
                colorProperties.SetColor("_Color", color);
                colorProperties.SetColor("_BaseColor", color);
                markerRenderer.SetPropertyBlock(colorProperties);

                direction.Normalize();
                var tip = pose.position + direction * directionLength;
                var arrowBack = tip - direction * directionLength * 0.25f;
                var up = pose.rotation * Vector3.up;
                var arrowRight = Vector3.Cross(up, direction).normalized;
                var arrowHalfWidth = directionLength * 0.12f;
                arrowPositions[0] =
                    root.transform.InverseTransformPoint(pose.position);
                arrowPositions[1] =
                    root.transform.InverseTransformPoint(tip);
                arrowPositions[2] = root.transform.InverseTransformPoint(
                    arrowBack + arrowRight * arrowHalfWidth);
                arrowPositions[3] =
                    root.transform.InverseTransformPoint(tip);
                arrowPositions[4] = root.transform.InverseTransformPoint(
                    arrowBack - arrowRight * arrowHalfWidth);
                arrow.positionCount = arrowPositions.Length;
                arrow.startWidth = lineWidth;
                arrow.endWidth = lineWidth;
                arrow.SetPositions(arrowPositions);

                label.transform.position =
                    pose.position + parent.parent.up * (markerScale + 0.08f);
            }

            public void FaceLabelTowards(Transform cameraTransform)
            {
                if (!root.activeSelf)
                    return;

                var direction =
                    label.transform.position - cameraTransform.position;
                if (direction.sqrMagnitude < 0.001f)
                    return;

                label.transform.rotation =
                    Quaternion.LookRotation(direction.normalized, Vector3.up);
            }
        }
    }
}
