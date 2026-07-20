using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

namespace F1XR.Interaction.Input
{
    public enum InputFeedbackMode
    {
        RayOnly,
        RayAndContextualIndicators
    }

    [DisallowMultipleComponent]
    public sealed class ContextIndicatorIgnore : MonoBehaviour
    {
    }

    [DisallowMultipleComponent]
    public sealed class ContextualInputFeedback : MonoBehaviour
    {
        const int RingPointCount = 24;

        [SerializeField] InputFeedbackMode mode = InputFeedbackMode.RayOnly;
        [SerializeField, Min(0.01f)] float enterDistance = 0.035f;
        [SerializeField, Min(0.01f)] float exitDistance = 0.055f;
        [SerializeField, Min(0.01f)] float handRayHideDistance = 0.07f;
        [SerializeField, Min(0.01f)] float handRayShowDistance = 0.09f;
        [SerializeField] LayerMask targetLayers = ~0;
        [SerializeField] GameObject leftHandRoot;
        [SerializeField] GameObject rightHandRoot;
        [SerializeField] GameObject leftHandRay;
        [SerializeField] GameObject rightHandRay;
        [SerializeField] GameObject leftController;
        [SerializeField] GameObject rightController;
        [SerializeField, Min(0.01f)] float controllerEnterDistance = 0.05f;
        [SerializeField, Min(0.01f)] float controllerExitDistance = 0.08f;
        [SerializeField, Min(0.01f)] float controllerRayHideDistance = 0.12f;
        [SerializeField, Min(0.01f)] float controllerRayShowDistance = 0.16f;
        [SerializeField] Color nearbyColor = new Color(0.2f, 0.85f, 1f, 0.8f);
        [SerializeField] Color pinchAttemptColor = new Color(0.71f, 0.42f, 1f, 1f);
        [SerializeField] Color pinchColor = new Color(1f, 0.75f, 0.2f, 1f);
        [SerializeField, Min(0.001f)] float markerRadius = 0.012f;
        [SerializeField, Min(0.001f)] float pinchedRadius = 0.024f;
        [SerializeField, Min(1f)] float approachRadiusScale = 1.35f;
        [SerializeField, Min(0f)] float surfaceOffset = 0.002f;
        [SerializeField, Min(0f)] float contactDistance = 0.015f;
        [SerializeField, Min(0.01f)] float selectionPulseDuration = 0.12f;
        [SerializeField, Min(0.0001f)] float lineWidth = 0.002f;
        [SerializeField, Min(0f)] float fadeSpeed = 8f;
        [SerializeField, Min(0.001f)] float pinchStartDistance = 0.025f;
        [SerializeField, Min(0.001f)] float pinchEndDistance = 0.04f;

        readonly Collider[] overlapResults = new Collider[64];
        readonly Dictionary<Collider, bool> relevantTargets = new Dictionary<Collider, bool>();

        readonly HandMarker leftMarker = new HandMarker("Left Context Indicator");
        readonly HandMarker rightMarker = new HandMarker("Right Context Indicator");
        readonly HandMarker leftControllerMarker = new HandMarker("Left Controller Context Indicator");
        readonly HandMarker rightControllerMarker = new HandMarker("Right Controller Context Indicator");

        XRHandSubsystem handSubsystem;
        NearFarInteractor leftControllerInteractor;
        NearFarInteractor rightControllerInteractor;
        XRBaseInteractor[] leftHandInteractors = System.Array.Empty<XRBaseInteractor>();
        XRBaseInteractor[] rightHandInteractors = System.Array.Empty<XRBaseInteractor>();
        XRBaseInteractor[] leftHandRayInteractors = System.Array.Empty<XRBaseInteractor>();
        XRBaseInteractor[] rightHandRayInteractors = System.Array.Empty<XRBaseInteractor>();
        CurveVisualController[] leftHandRayVisuals = System.Array.Empty<CurveVisualController>();
        CurveVisualController[] rightHandRayVisuals = System.Array.Empty<CurveVisualController>();
        CurveVisualController[] leftControllerRayVisuals = System.Array.Empty<CurveVisualController>();
        CurveVisualController[] rightControllerRayVisuals = System.Array.Empty<CurveVisualController>();
        Transform viewTransform;
        Material lineMaterial;
        bool ownsLineMaterial;
        float nextSubsystemCheck;

        public InputFeedbackMode Mode
        {
            get => mode;
            set => mode = value;
        }

        sealed class HandMarker
        {
            public readonly string name;
            public readonly Vector3[] points = new Vector3[RingPointCount];
            public LineRenderer line;
            public bool nearby;
            public bool contacting;
            public bool pinching;
            public bool selected;
            public float alpha;
            public float radius;
            public float selectionPulse;
            public Vector3 position;
            public Collider target;
            public bool rayHidden;

            public HandMarker(string name)
            {
                this.name = name;
            }
        }

        void Awake()
        {
            Camera mainCamera = Camera.main;
            viewTransform = mainCamera != null ? mainCamera.transform : transform;
            ResolveControllerInteractors();
            ResolveHandInteractors();
            ResolveRayVisuals();
            lineMaterial = FindRayMaterial();
            if (lineMaterial == null)
            {
                lineMaterial = CreateLineMaterial();
                ownsLineMaterial = true;
            }

            CreateMarker(leftMarker);
            CreateMarker(rightMarker);
            CreateMarker(leftControllerMarker);
            CreateMarker(rightControllerMarker);
            ConfigureRayVisuals();
            ConfigureHandFarPointer(leftHandRay, leftHandRayVisuals);
            ConfigureHandFarPointer(rightHandRay, rightHandRayVisuals);
            ResolveHandSubsystem();
        }

        void OnValidate()
        {
            exitDistance = Mathf.Max(exitDistance, enterDistance);
            handRayShowDistance = Mathf.Max(handRayShowDistance, handRayHideDistance);
            controllerExitDistance = Mathf.Max(controllerExitDistance, controllerEnterDistance);
            controllerRayShowDistance = Mathf.Max(controllerRayShowDistance, controllerRayHideDistance);
            pinchEndDistance = Mathf.Max(pinchEndDistance, pinchStartDistance);
        }

        void Update()
        {
            if (handSubsystem == null || !handSubsystem.running)
            {
                if (Time.unscaledTime >= nextSubsystemCheck)
                {
                    ResolveHandSubsystem();
                    nextSubsystemCheck = Time.unscaledTime + 1f;
                }

                ResetHandMarker(leftMarker, leftHandRayVisuals);
                ResetHandMarker(rightMarker, rightHandRayVisuals);
                return;
            }

            bool showIndicators = mode == InputFeedbackMode.RayAndContextualIndicators;
            UpdateHand(leftMarker, handSubsystem.leftHand, showIndicators, leftHandRayVisuals);
            UpdateHand(rightMarker, handSubsystem.rightHand, showIndicators, rightHandRayVisuals);
        }

        void LateUpdate()
        {
            bool showIndicators = mode == InputFeedbackMode.RayAndContextualIndicators;
            UpdateController(
                leftControllerMarker,
                leftControllerInteractor,
                leftController,
                showIndicators,
                leftControllerRayVisuals);
            UpdateController(
                rightControllerMarker,
                rightControllerInteractor,
                rightController,
                showIndicators,
                rightControllerRayVisuals);
        }

        void OnDisable()
        {
            SetRayVisualsVisible(leftHandRayVisuals, true);
            SetRayVisualsVisible(rightHandRayVisuals, true);
            SetRayVisualsVisible(leftControllerRayVisuals, true);
            SetRayVisualsVisible(rightControllerRayVisuals, true);
            Hide(leftMarker);
            Hide(rightMarker);
            Hide(leftControllerMarker);
            Hide(rightControllerMarker);
        }

        void OnDestroy()
        {
            if (ownsLineMaterial && lineMaterial != null)
                Destroy(lineMaterial);
        }

        void ResolveHandSubsystem()
        {
            handSubsystem = XRHandInput.FindRunningSubsystem();
        }

        void ResolveControllerInteractors()
        {
            if (leftController != null)
                leftControllerInteractor = leftController.GetComponentInChildren<NearFarInteractor>(true);
            if (rightController != null)
                rightControllerInteractor = rightController.GetComponentInChildren<NearFarInteractor>(true);
        }

        void ResolveHandInteractors()
        {
            if (leftHandRoot != null)
                leftHandInteractors = leftHandRoot.GetComponentsInChildren<XRBaseInteractor>(true);
            if (rightHandRoot != null)
                rightHandInteractors = rightHandRoot.GetComponentsInChildren<XRBaseInteractor>(true);
            if (leftHandRay != null)
                leftHandRayInteractors = leftHandRay.GetComponentsInChildren<XRBaseInteractor>(true);
            if (rightHandRay != null)
                rightHandRayInteractors = rightHandRay.GetComponentsInChildren<XRBaseInteractor>(true);
        }

        void ResolveRayVisuals()
        {
            leftHandRayVisuals = GetRayVisuals(leftHandRay);
            rightHandRayVisuals = GetRayVisuals(rightHandRay);
            leftControllerRayVisuals = GetRayVisuals(leftController);
            rightControllerRayVisuals = GetRayVisuals(rightController);
        }

        static CurveVisualController[] GetRayVisuals(GameObject root)
        {
            return root != null
                ? root.GetComponentsInChildren<CurveVisualController>(true)
                : System.Array.Empty<CurveVisualController>();
        }

        void ConfigureRayVisuals()
        {
            ConfigureRayVisuals(leftHandRayVisuals);
            ConfigureRayVisuals(rightHandRayVisuals);
            ConfigureRayVisuals(leftControllerRayVisuals);
            ConfigureRayVisuals(rightControllerRayVisuals);
        }

        static void ConfigureRayVisuals(CurveVisualController[] visuals)
        {
            foreach (CurveVisualController visual in visuals)
            {
                if (visual == null)
                    continue;

                SetTaperedEnd(visual.noValidHitProperties);
                SetTaperedEnd(visual.uiHitProperties);
                SetTaperedEnd(visual.uiPressHitProperties);
                SetTaperedEnd(visual.hoverHitProperties);
                SetTaperedEnd(visual.selectHitProperties);

                if (visual.lineRenderer != null)
                    visual.lineRenderer.numCapVertices = 0;
            }
        }

        static void SetTaperedEnd(LineProperties properties)
        {
            properties.endWidth = 0f;
            properties.endWidthScaleDistanceFactor = 0f;
        }

        static void ConfigureHandFarPointer(
            GameObject handRay,
            CurveVisualController[] visuals)
        {
            if (handRay != null)
            {
                CurveInteractionCaster[] casters =
                    handRay.GetComponentsInChildren<CurveInteractionCaster>(true);
                foreach (CurveInteractionCaster caster in casters)
                    caster.hitDetectionType = CurveInteractionCaster.HitDetectionType.Raycast;
            }

            foreach (CurveVisualController visual in visuals)
            {
                if (visual == null)
                    continue;

                visual.snapToSelectedAttachIfAvailable = false;
                visual.snapToSnapVolumeIfAvailable = false;
            }
        }

        void UpdateHand(
            HandMarker marker,
            XRHand hand,
            bool showIndicators,
            CurveVisualController[] rayVisuals)
        {
            if (!showIndicators || !hand.isTracked ||
                !XRHandInput.TryGetJointPoint(hand, XRHandJointID.IndexTip, out Vector3 localFingertip))
            {
                SetRayVisualsVisible(rayVisuals, true);
                marker.nearby = false;
                marker.contacting = false;
                marker.pinching = false;
                marker.selected = false;
                marker.target = null;
                marker.rayHidden = false;
                FadeOut(marker);
                return;
            }

            Vector3 fingertip = transform.TransformPoint(localFingertip);
            UpdateRayHandoff(
                marker,
                fingertip,
                handRayHideDistance,
                handRayShowDistance,
                rayVisuals);
            float threshold = marker.nearby ? exitDistance : enterDistance;
            marker.nearby = TryFindProjectedTarget(
                fingertip,
                threshold,
                marker.target,
                out Collider target,
                out Vector3 markerPosition,
                out float distance);
            marker.pinching = XRHandInput.IsPinching(
                hand,
                marker.pinching,
                pinchStartDistance,
                pinchEndDistance);
            bool selected = hand.handedness == Handedness.Left
                ? HasNearSelection(leftHandInteractors) || HasNearSelection(leftHandRayInteractors)
                : HasNearSelection(rightHandInteractors) || HasNearSelection(rightHandRayInteractors);
            UpdateSelection(marker, selected);

            if (!marker.nearby)
            {
                marker.contacting = false;
                marker.target = null;
                FadeOut(marker);
                return;
            }

            SetRayVisualsVisible(rayVisuals, false);
            marker.target = target;
            marker.position = markerPosition;
            marker.contacting = distance <= contactDistance;
            float proximity = 1f - Mathf.Clamp01(distance / threshold);
            float targetAlpha = marker.contacting || marker.pinching || marker.selected
                ? 1f
                : Mathf.Lerp(nearbyColor.a * 0.35f, nearbyColor.a, proximity);
            marker.alpha = Mathf.MoveTowards(marker.alpha, targetAlpha, fadeSpeed * Time.unscaledDeltaTime);
            float radius = marker.selected
                ? Mathf.Lerp(markerRadius, pinchedRadius, marker.selectionPulse)
                : Mathf.Lerp(markerRadius * approachRadiusScale, markerRadius, proximity);
            DrawMarker(marker, radius);
        }

        static bool HasNearSelection(XRBaseInteractor[] interactors)
        {
            foreach (XRBaseInteractor interactor in interactors)
            {
                if (interactor == null || !interactor.hasSelection)
                    continue;

                if (interactor is NearFarInteractor nearFarInteractor &&
                    nearFarInteractor.selectionRegion.Value != NearFarInteractor.Region.Near)
                    continue;

                return true;
            }

            return false;
        }

        void UpdateController(
            HandMarker marker,
            NearFarInteractor interactor,
            GameObject controller,
            bool showIndicators,
            CurveVisualController[] rayVisuals)
        {
            if (!showIndicators || controller == null || interactor == null || !interactor.isActiveAndEnabled)
            {
                SetRayVisualsVisible(rayVisuals, true);
                marker.nearby = false;
                marker.contacting = false;
                marker.pinching = false;
                marker.selected = false;
                marker.target = null;
                marker.rayHidden = false;
                FadeOut(marker);
                return;
            }

            if (interactor.hasSelection &&
                interactor.selectionRegion.Value == NearFarInteractor.Region.Far)
            {
                SetRayVisualsVisible(rayVisuals, true);
                marker.nearby = false;
                marker.contacting = false;
                marker.pinching = false;
                marker.selected = false;
                marker.target = null;
                marker.rayHidden = false;
                Hide(marker);
                return;
            }

            Vector3 controllerPoint = controller.transform.position;
            UpdateRayHandoff(
                marker,
                controllerPoint,
                controllerRayHideDistance,
                controllerRayShowDistance,
                rayVisuals);
            float threshold = marker.nearby ? controllerExitDistance : controllerEnterDistance;
            marker.nearby = TryFindProjectedTarget(
                controllerPoint,
                threshold,
                marker.target,
                out Collider target,
                out Vector3 markerPosition,
                out float distance);
            bool selected = interactor.hasSelection &&
                interactor.selectionRegion.Value == NearFarInteractor.Region.Near;
            UpdateSelection(marker, selected);
            marker.pinching = marker.selected ||
                interactor.selectInput.ReadIsPerformed() ||
                interactor.uiPressInput.ReadIsPerformed();

            if (!marker.nearby)
            {
                marker.contacting = false;
                marker.target = null;
                FadeOut(marker);
                return;
            }

            marker.target = target;
            marker.position = markerPosition;
            marker.contacting = distance <= contactDistance;
            float proximity = 1f - Mathf.Clamp01(distance / threshold);
            float targetAlpha = marker.contacting || marker.pinching || marker.selected
                ? 1f
                : Mathf.Lerp(nearbyColor.a * 0.35f, nearbyColor.a, proximity);
            marker.alpha = Mathf.MoveTowards(marker.alpha, targetAlpha, fadeSpeed * Time.unscaledDeltaTime);
            float radius = marker.selected
                ? Mathf.Lerp(markerRadius, pinchedRadius, marker.selectionPulse)
                : Mathf.Lerp(markerRadius * approachRadiusScale, markerRadius, proximity);
            DrawMarker(marker, radius);
        }

        void UpdateRayHandoff(
            HandMarker marker,
            Vector3 sourcePoint,
            float hideDistance,
            float showDistance,
            CurveVisualController[] rayVisuals)
        {
            float threshold = marker.rayHidden ? showDistance : hideDistance;
            marker.rayHidden = TryFindProjectedTarget(
                sourcePoint,
                threshold,
                marker.target,
                out _,
                out _,
                out _);
            SetRayVisualsVisible(rayVisuals, !marker.rayHidden);
        }

        static void ResetHandMarker(
            HandMarker marker,
            CurveVisualController[] rayVisuals)
        {
            marker.nearby = false;
            marker.contacting = false;
            marker.pinching = false;
            marker.selected = false;
            marker.target = null;
            marker.rayHidden = false;
            SetRayVisualsVisible(rayVisuals, true);
            Hide(marker);
        }

        static void SetRayVisualsVisible(CurveVisualController[] visuals, bool visible)
        {
            foreach (CurveVisualController visual in visuals)
            {
                if (visual == null)
                    continue;

                if (visual.enabled != visible)
                    visual.enabled = visible;

                if (!visible && visual.lineRenderer != null)
                    visual.lineRenderer.enabled = false;
            }
        }

        void UpdateSelection(HandMarker marker, bool selected)
        {
            if (selected && !marker.selected)
                marker.selectionPulse = 1f;

            marker.selected = selected;
            marker.selectionPulse = Mathf.MoveTowards(
                marker.selectionPulse,
                0f,
                Time.unscaledDeltaTime / selectionPulseDuration);
        }

        bool TryFindProjectedTarget(
            Vector3 point,
            float radius,
            Collider preferredTarget,
            out Collider target,
            out Vector3 markerPosition,
            out float distance)
        {
            if (TryGetProjectedCandidate(
                    preferredTarget,
                    point,
                    radius,
                    out markerPosition,
                    out distance))
            {
                target = preferredTarget;
                return true;
            }

            int count = Physics.OverlapSphereNonAlloc(
                point,
                radius,
                overlapResults,
                targetLayers,
                QueryTriggerInteraction.Collide);

            target = null;
            markerPosition = default;
            distance = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                Collider candidate = overlapResults[i];
                if (candidate == preferredTarget ||
                    !TryGetProjectedCandidate(
                        candidate,
                        point,
                        radius,
                        out Vector3 candidatePosition,
                        out float candidateDistance) ||
                    candidateDistance >= distance)
                    continue;

                target = candidate;
                markerPosition = candidatePosition;
                distance = candidateDistance;
            }

            return target != null;
        }

        bool TryGetProjectedCandidate(
            Collider candidate,
            Vector3 point,
            float radius,
            out Vector3 markerPosition,
            out float distance)
        {
            markerPosition = default;
            distance = float.MaxValue;

            if (candidate == null || !candidate.enabled ||
                !candidate.gameObject.activeInHierarchy || !IsRelevantTarget(candidate))
                return false;

            Vector3 closestPoint = candidate.ClosestPoint(point);
            distance = Vector3.Distance(point, closestPoint);
            return distance <= radius &&
                TryGetProjectedSurfaceMarkerPosition(point, candidate, radius, out markerPosition);
        }

        bool TryGetProjectedSurfaceMarkerPosition(
            Vector3 sourcePoint,
            Collider target,
            float searchDistance,
            out Vector3 position)
        {
            Vector3 viewToSource = sourcePoint - viewTransform.position;
            float viewDistance = viewToSource.magnitude;
            if (viewDistance > Mathf.Epsilon)
            {
                Ray viewRay = new Ray(viewTransform.position, viewToSource / viewDistance);
                if (target.Raycast(viewRay, out RaycastHit hit, viewDistance + searchDistance))
                {
                    position = hit.point + hit.normal * surfaceOffset;
                    return true;
                }
            }

            position = default;
            return false;
        }

        bool IsRelevantTarget(Collider candidate)
        {
            if (relevantTargets.TryGetValue(candidate, out bool relevant))
                return relevant;

            relevant = candidate.GetComponent<ContextIndicatorIgnore>() == null &&
                candidate.GetComponentInParent<XRBaseInteractable>() != null;
            relevantTargets.Add(candidate, relevant);
            return relevant;
        }

        void CreateMarker(HandMarker marker)
        {
            GameObject markerObject = new GameObject(marker.name);
            markerObject.transform.SetParent(transform, false);
            marker.line = markerObject.AddComponent<LineRenderer>();
            marker.line.useWorldSpace = true;
            marker.line.loop = true;
            marker.line.positionCount = RingPointCount;
            marker.line.sharedMaterial = lineMaterial;
            marker.line.numCornerVertices = 2;
            marker.line.numCapVertices = 2;
            marker.line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            marker.line.receiveShadows = false;
            marker.line.sortingOrder = 30006;
            marker.line.enabled = false;
        }

        Material FindRayMaterial()
        {
            CurveVisualController[] visuals = GetComponentsInChildren<CurveVisualController>(true);
            foreach (CurveVisualController visual in visuals)
            {
                if (visual.baseLineMaterial != null)
                    return visual.baseLineMaterial;

                if (visual.lineRenderer != null && visual.lineRenderer.sharedMaterial != null)
                    return visual.lineRenderer.sharedMaterial;
            }

            return null;
        }

        void DrawMarker(HandMarker marker, float radius)
        {
            marker.radius = radius;
            Vector3 right = viewTransform.right * radius;
            Vector3 up = viewTransform.up * radius;
            for (int i = 0; i < RingPointCount; i++)
            {
                float angle = i * Mathf.PI * 2f / RingPointCount;
                marker.points[i] = marker.position + right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
            }

            Color color = marker.selected
                ? pinchColor
                : marker.pinching
                    ? pinchAttemptColor
                    : nearbyColor;
            color.a = marker.alpha;
            marker.line.startColor = color;
            marker.line.endColor = color;
            float width = marker.selected
                ? lineWidth * 1.5f
                : marker.contacting
                    ? lineWidth * 1.25f
                    : lineWidth;
            marker.line.startWidth = width;
            marker.line.endWidth = width;
            marker.line.SetPositions(marker.points);
            marker.line.enabled = marker.alpha > 0.001f;
        }

        void FadeOut(HandMarker marker)
        {
            marker.alpha = Mathf.MoveTowards(marker.alpha, 0f, fadeSpeed * Time.unscaledDeltaTime);
            if (marker.alpha <= 0.001f)
            {
                marker.line.enabled = false;
                return;
            }

            DrawMarker(marker, marker.radius > 0f ? marker.radius : markerRadius);
        }

        static void Hide(HandMarker marker)
        {
            marker.alpha = 0f;
            marker.contacting = false;
            marker.selected = false;
            marker.selectionPulse = 0f;
            marker.target = null;
            marker.rayHidden = false;
            if (marker.line != null)
                marker.line.enabled = false;
        }

        Material CreateLineMaterial()
        {
            Shader shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            Material material = new Material(shader)
            {
                name = "Contextual Input Indicator (Runtime)",
                renderQueue = 3000,
                hideFlags = HideFlags.DontSave
            };

            material.SetOverrideTag("RenderType", "Transparent");
            return material;
        }
    }
}
