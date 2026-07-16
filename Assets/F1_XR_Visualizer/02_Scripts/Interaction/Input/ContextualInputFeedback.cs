using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

namespace F1XR.Interaction.Input
{
    public enum InputFeedbackMode
    {
        RayOnly,
        RayAndContextualIndicators
    }

    [DisallowMultipleComponent]
    public sealed class ContextualInputFeedback : MonoBehaviour
    {
        const int RingPointCount = 24;

        [SerializeField] InputFeedbackMode mode = InputFeedbackMode.RayOnly;
        [SerializeField, Min(0.01f)] float enterDistance = 0.11f;
        [SerializeField, Min(0.01f)] float exitDistance = 0.17f;
        [SerializeField] LayerMask targetLayers = ~0;
        [SerializeField] GameObject leftHandRoot;
        [SerializeField] GameObject rightHandRoot;
        [SerializeField] GameObject leftHandRay;
        [SerializeField] GameObject rightHandRay;
        [SerializeField] GameObject leftController;
        [SerializeField] GameObject rightController;
        [SerializeField, Min(0.01f)] float controllerEnterDistance = 0.2f;
        [SerializeField, Min(0.01f)] float controllerExitDistance = 0.26f;
        [SerializeField, Min(0f)] float controllerViewOffset = 0.04f;
        [SerializeField] Color nearbyColor = new Color(0.2f, 0.85f, 1f, 0.8f);
        [SerializeField] Color pinchAttemptColor = new Color(0.71f, 0.42f, 1f, 1f);
        [SerializeField] Color pinchColor = new Color(1f, 0.75f, 0.2f, 1f);
        [SerializeField, Min(0.001f)] float markerRadius = 0.012f;
        [SerializeField, Min(0.001f)] float pinchedRadius = 0.024f;
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
        Transform viewTransform;
        Material lineMaterial;
        bool ownsLineMaterial;
        float nextSubsystemCheck;

        sealed class HandMarker
        {
            public readonly string name;
            public readonly Vector3[] points = new Vector3[RingPointCount];
            public LineRenderer line;
            public bool nearby;
            public bool pinching;
            public bool selected;
            public float alpha;
            public Vector3 position;

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
            ResolveHandSubsystem();
        }

        void OnValidate()
        {
            exitDistance = Mathf.Max(exitDistance, enterDistance);
            controllerExitDistance = Mathf.Max(controllerExitDistance, controllerEnterDistance);
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

                FadeOut(leftMarker);
                FadeOut(rightMarker);
                return;
            }

            bool showIndicators = mode == InputFeedbackMode.RayAndContextualIndicators;
            UpdateHand(leftMarker, handSubsystem.leftHand, showIndicators);
            UpdateHand(rightMarker, handSubsystem.rightHand, showIndicators);
        }

        void LateUpdate()
        {
            bool showIndicators = mode == InputFeedbackMode.RayAndContextualIndicators;
            UpdateController(leftControllerMarker, leftControllerInteractor, leftController, showIndicators);
            UpdateController(rightControllerMarker, rightControllerInteractor, rightController, showIndicators);
        }

        void OnDisable()
        {
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

        void UpdateHand(HandMarker marker, XRHand hand, bool showIndicators)
        {
            if (!showIndicators || !hand.isTracked ||
                !XRHandInput.TryGetJointPoint(hand, XRHandJointID.IndexTip, out Vector3 localFingertip))
            {
                marker.nearby = false;
                marker.pinching = false;
                marker.selected = false;
                FadeOut(marker);
                return;
            }

            Vector3 fingertip = transform.TransformPoint(localFingertip);
            float threshold = marker.nearby ? exitDistance : enterDistance;
            marker.nearby = TryFindClosestTarget(fingertip, threshold, out _);
            marker.pinching = XRHandInput.IsPinching(
                hand,
                marker.pinching,
                pinchStartDistance,
                pinchEndDistance);
            marker.selected = hand.handedness == Handedness.Left
                ? HasNearSelection(leftHandInteractors) || HasNearSelection(leftHandRayInteractors)
                : HasNearSelection(rightHandInteractors) || HasNearSelection(rightHandRayInteractors);

            if (!marker.nearby)
            {
                FadeOut(marker);
                return;
            }

            Vector3 towardView = viewTransform.position - fingertip;
            marker.position = fingertip + towardView.normalized * 0.015f;
            float targetAlpha = marker.pinching || marker.selected ? 1f : nearbyColor.a;
            marker.alpha = Mathf.MoveTowards(marker.alpha, targetAlpha, fadeSpeed * Time.unscaledDeltaTime);
            DrawMarker(marker, marker.selected ? pinchedRadius : markerRadius);
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
            bool showIndicators)
        {
            if (!showIndicators || controller == null || interactor == null || !interactor.isActiveAndEnabled)
            {
                marker.nearby = false;
                marker.pinching = false;
                marker.selected = false;
                FadeOut(marker);
                return;
            }

            if (interactor.hasSelection &&
                interactor.selectionRegion.Value == NearFarInteractor.Region.Far)
            {
                marker.nearby = false;
                marker.pinching = false;
                marker.selected = false;
                Hide(marker);
                return;
            }

            Transform origin = interactor.curveOrigin;
            if (origin == null)
            {
                marker.nearby = false;
                marker.pinching = false;
                marker.selected = false;
                FadeOut(marker);
                return;
            }

            Vector3 controllerPoint = controller.transform.position;
            Vector3 rayOriginPoint = origin.position;
            float threshold = marker.nearby ? controllerExitDistance : controllerEnterDistance;
            marker.nearby = TryFindClosestTarget(controllerPoint, threshold, out _) ||
                TryFindClosestTarget(rayOriginPoint, threshold, out _);
            marker.selected = interactor.hasSelection &&
                interactor.selectionRegion.Value == NearFarInteractor.Region.Near;
            marker.pinching = marker.selected ||
                interactor.selectInput.ReadIsPerformed() ||
                interactor.uiPressInput.ReadIsPerformed();

            if (!marker.nearby)
            {
                FadeOut(marker);
                return;
            }

            Vector3 towardView = viewTransform.position - controllerPoint;
            marker.position = controllerPoint + towardView.normalized * controllerViewOffset;
            float targetAlpha = marker.pinching || marker.selected ? 1f : nearbyColor.a;
            marker.alpha = Mathf.MoveTowards(marker.alpha, targetAlpha, fadeSpeed * Time.unscaledDeltaTime);
            DrawMarker(marker, marker.selected ? pinchedRadius : markerRadius);
        }

        bool TryFindClosestTarget(Vector3 point, float radius, out Vector3 closestPoint)
        {
            int count = Physics.OverlapSphereNonAlloc(
                point,
                radius,
                overlapResults,
                targetLayers,
                QueryTriggerInteraction.Collide);

            float closestDistance = float.MaxValue;
            closestPoint = default;

            for (int i = 0; i < count; i++)
            {
                Collider candidate = overlapResults[i];
                if (candidate == null || !IsRelevantTarget(candidate))
                    continue;

                Vector3 candidatePoint = candidate.ClosestPoint(point);
                float distance = Vector3.Distance(point, candidatePoint);
                if (distance > radius || distance >= closestDistance)
                    continue;

                closestDistance = distance;
                closestPoint = candidatePoint;
            }

            return closestDistance < float.MaxValue;
        }

        bool IsRelevantTarget(Collider candidate)
        {
            if (relevantTargets.TryGetValue(candidate, out bool relevant))
                return relevant;

            relevant = candidate.GetComponentInParent<XRBaseInteractable>() != null;
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
            float width = marker.selected ? lineWidth * 1.5f : lineWidth;
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

            DrawMarker(marker, marker.selected ? pinchedRadius : markerRadius);
        }

        static void Hide(HandMarker marker)
        {
            marker.alpha = 0f;
            marker.selected = false;
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
