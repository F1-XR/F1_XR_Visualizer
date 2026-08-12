using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace F1XR.RestAPI.Replay
{
    [DisallowMultipleComponent]
    public sealed class CollisionTimeLensGate : MonoBehaviour
    {
        private const float MinimumRailLength = 0.001f;

        [Header("Handle")]
        [SerializeField, Min(0.05f)]
        private float handleHeightMeters = 0.75f;
        [SerializeField, Min(0.005f)]
        private float handleRadiusMeters = 0.055f;
        [SerializeField, Min(0.01f)]
        private float handleColliderHeightMeters = 0.14f;

        [Header("Interaction")]
        [SerializeField, Min(0f)]
        private float contactDetentMeters = 0.04f;
        [SerializeField, Min(0f)]
        private float endpointDetentMeters = 0.03f;
        [SerializeField, Min(0f)]
        private float movementDeadzoneMeters = 0.007f;
        [SerializeField, Min(0f)]
        private float visualSmoothTime = 0.035f;
        [SerializeField, Min(0.1f)]
        private float maximumRayPlaneDistance = 6f;

        private Transform railSpace;
        private Transform gateBody;
        private Transform handle;
        private XRSimpleInteractable interactable;
        private CapsuleCollider handleCollider;
        private XRSimpleInteractable boundInteractable;
        private Vector3[] localRailPoints;
        private Vector3[] worldRailPoints;
        private float[] worldCumulativeDistances;
        private int contactPointIndex;
        private float totalDistanceMeters;
        private float contactDistanceMeters;
        private float currentNormalized = 1f;
        private float visualNormalized = 1f;
        private float visualVelocity;
        private float grabOffsetNormalized;
        private float lastProjectedNormalized;
        private bool hasLastProjected;
        private bool configured;
        private bool available;
        private bool grabbed;
        private bool projectionModeInitialized;
        private bool useFarProjection;
        private bool hasFarProjectionPlane;
        private Plane farProjectionPlane;
        private IXRSelectInteractor activeInteractor;
        private IXRRayProvider activeRayProvider;
        private Transform activeInteractorTransform;

        /// <summary>
        /// Invoked with the current rail distance in metres and its normalized value.
        /// </summary>
        public event Action<float, float> ValueChanged;

        public event Action<bool> GrabStateChanged;

        public bool IsConfigured => configured;
        public bool IsAvailable => available;
        public bool IsGrabbed => grabbed;
        public float NormalizedValue => currentNormalized;
        public float DistanceMeters => currentNormalized * totalDistanceMeters;
        public float TotalDistanceMeters => totalDistanceMeters;
        public float ContactDistanceMeters => contactDistanceMeters;
        public float ContactNormalized => totalDistanceMeters > MinimumRailLength
            ? contactDistanceMeters / totalDistanceMeters
            : 0f;
        public Transform GateBody => gateBody;
        public Transform Handle => handle;
        public XRSimpleInteractable Interactable => interactable;

        public void SetInteractionTuning(
            float handleRadius,
            float deadzone,
            float smoothTime,
            float contactDetent,
            float endpointDetent)
        {
            handleRadiusMeters = Mathf.Max(0.005f, handleRadius);
            movementDeadzoneMeters = Mathf.Max(0f, deadzone);
            visualSmoothTime = Mathf.Max(0f, smoothTime);
            contactDetentMeters = Mathf.Max(0f, contactDetent);
            endpointDetentMeters = Mathf.Max(0f, endpointDetent);
            if (handleCollider != null)
                UpdateHandleGeometry();
        }

        /// <summary>
        /// Configures a collider-free gate body and a single selectable handle along a rail.
        /// Rail points are expressed in <paramref name="valueRailSpace"/> local space.
        /// </summary>
        public bool Configure(
            Transform valueRailSpace,
            IReadOnlyList<Vector3> railPoints,
            int valueContactPointIndex,
            Transform valueGateBody = null,
            Transform valueHandle = null,
            float initialNormalized = 1f)
        {
            if (valueRailSpace == transform ||
                (valueRailSpace != null &&
                 valueRailSpace.IsChildOf(transform)))
            {
                return false;
            }

            if (!TryCopyRail(
                    valueRailSpace,
                    railPoints,
                    valueContactPointIndex,
                    out Vector3[] copiedPoints))
            {
                return false;
            }

            SetAvailable(false);
            ForceEndGrab();

            railSpace = valueRailSpace;
            localRailPoints = copiedPoints;
            worldRailPoints = new Vector3[copiedPoints.Length];
            worldCumulativeDistances = new float[copiedPoints.Length];
            contactPointIndex = valueContactPointIndex;
            gateBody = ResolveBody(valueGateBody);
            handle = ResolveHandle(valueHandle);
            EnsureBodyIsColliderFree();
            EnsureHandleInteraction();

            configured = RefreshWorldRail();
            if (!configured)
            {
                SetInteractionActive(false);
                return false;
            }

            currentNormalized = Mathf.Clamp01(initialNormalized);
            currentNormalized = ApplyDetents(currentNormalized);
            visualNormalized = currentNormalized;
            visualVelocity = 0f;
            ApplyVisualPose(visualNormalized);
            SetInteractionActive(false);
            return true;
        }

        public void SetAvailable(bool value)
        {
            bool resolved = value && configured;
            if (available == resolved)
            {
                if (resolved)
                    ApplyVisualPose(visualNormalized);
                return;
            }

            if (!resolved)
            {
                ForceEndGrab();
                available = false;
                SetInteractionActive(false);
                return;
            }

            if (!RefreshWorldRail())
                return;

            available = true;
            visualNormalized = currentNormalized;
            visualVelocity = 0f;
            ApplyVisualPose(visualNormalized);
            SetInteractionActive(true);
        }

        public bool SetNormalized(float value, bool notify = true)
        {
            if (!configured || !RefreshWorldRail())
                return false;

            SetValue(
                ApplyDetents(Mathf.Clamp01(value)),
                notify,
                true,
                true);
            return true;
        }

        public bool SetDistanceMeters(float value, bool notify = true)
        {
            if (!configured || !RefreshWorldRail())
                return false;

            float normalized = totalDistanceMeters > MinimumRailLength
                ? value / totalDistanceMeters
                : 0f;
            return SetNormalized(normalized, notify);
        }

        public void ResetValue(
            float normalized = 1f,
            bool notify = false)
        {
            ForceEndGrab();
            SetNormalized(normalized, notify);
        }

        public void SetHandleHeightMeters(float value)
        {
            handleHeightMeters = Mathf.Max(0.05f, value);
            if (configured)
            {
                UpdateHandleGeometry();
                ApplyVisualPose(visualNormalized);
            }
        }

        private void OnEnable()
        {
            BindInteractionEvents();
        }

        private void OnDisable()
        {
            UnbindInteractionEvents();
            ForceEndGrab();
        }

        private void LateUpdate()
        {
            if (!available || !configured || !RefreshWorldRail())
                return;

            if (grabbed &&
                !projectionModeInitialized)
            {
                TryInitializeProjectionMode();
            }

            if (grabbed &&
                projectionModeInitialized &&
                TryProjectInteractor(out float projectedNormalized))
            {
                float target = ApplyDetents(Mathf.Clamp01(
                    projectedNormalized + grabOffsetNormalized));
                SetValue(target, true, false, false);
            }

            if (grabbed && visualSmoothTime > 0f)
            {
                visualNormalized = Mathf.SmoothDamp(
                    visualNormalized,
                    currentNormalized,
                    ref visualVelocity,
                    visualSmoothTime,
                    Mathf.Infinity,
                    Time.unscaledDeltaTime);
            }
            else
            {
                visualNormalized = currentNormalized;
                visualVelocity = 0f;
            }

            ApplyVisualPose(visualNormalized);
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (!available ||
                !configured ||
                !ReferenceEquals(
                    args.interactableObject,
                    interactable) ||
                activeInteractor != null)
            {
                return;
            }

            activeInteractor = args.interactorObject;
            activeInteractorTransform =
                (args.interactorObject as Component)?.transform;
            projectionModeInitialized = false;
            useFarProjection = false;
            activeRayProvider = null;
            hasFarProjectionPlane = false;
            hasLastProjected = false;
            grabOffsetNormalized = 0f;

            grabbed = true;
            GrabStateChanged?.Invoke(true);
        }

        private void OnSelectExited(SelectExitEventArgs args)
        {
            if (!ReferenceEquals(
                    args.interactorObject,
                    activeInteractor))
                return;

            if (!args.isCanceled &&
                available &&
                projectionModeInitialized &&
                TryProjectInteractor(out float projectedNormalized))
            {
                float target = ApplyDetents(Mathf.Clamp01(
                    projectedNormalized + grabOffsetNormalized));
                SetValue(target, true, true, true);
            }

            ForceEndGrab();
        }

        private bool TryInitializeProjectionMode()
        {
            if (activeInteractor == null)
                return false;

            if (activeInteractor is NearFarInteractor nearFarInteractor)
            {
                NearFarInteractor.Region region =
                    nearFarInteractor.selectionRegion.Value;
                if (region == NearFarInteractor.Region.None)
                    return false;

                useFarProjection =
                    region == NearFarInteractor.Region.Far;
            }
            else
            {
                useFarProjection =
                    activeInteractor is XRRayInteractor;
            }

            activeRayProvider = useFarProjection
                ? activeInteractor as IXRRayProvider
                : null;
            hasFarProjectionPlane =
                useFarProjection &&
                TryCreateFarProjectionPlane();
            hasLastProjected = false;
            grabOffsetNormalized = 0f;
            if (!TryProjectInteractor(out float projectedNormalized))
                return false;

            grabOffsetNormalized =
                currentNormalized - projectedNormalized;
            projectionModeInitialized = true;

            return true;
        }

        private bool TryProjectInteractor(
            out float projectedNormalized)
        {
            projectedNormalized = currentNormalized;
            if (!TryGetPointerOnRailPlane(out Vector3 pointerWorld))
                return false;

            float bestDistanceSquared = float.PositiveInfinity;
            float bestNormalized = currentNormalized;
            bool found = false;
            for (int i = 0; i < worldRailPoints.Length - 1; i++)
            {
                Vector3 start = worldRailPoints[i];
                Vector3 segment = worldRailPoints[i + 1] - start;
                float segmentLengthSquared = segment.sqrMagnitude;
                if (segmentLengthSquared <= 0.0000001f)
                    continue;

                float segmentProgress = Mathf.Clamp01(
                    Vector3.Dot(pointerWorld - start, segment) /
                    segmentLengthSquared);
                Vector3 closest = start + segment * segmentProgress;
                float distanceSquared =
                    (pointerWorld - closest).sqrMagnitude;
                float segmentLength =
                    worldCumulativeDistances[i + 1] -
                    worldCumulativeDistances[i];
                float distance =
                    worldCumulativeDistances[i] +
                    segmentLength * segmentProgress;
                float normalized = distance / totalDistanceMeters;

                bool closer = distanceSquared <
                    bestDistanceSquared - 0.0000001f;
                bool equallyClose = Mathf.Abs(
                    distanceSquared - bestDistanceSquared) <=
                    0.0000001f;
                bool preservesContinuity = equallyClose &&
                    Mathf.Abs(normalized - lastProjectedNormalized) <
                    Mathf.Abs(bestNormalized - lastProjectedNormalized);
                if (!closer &&
                    !(hasLastProjected && preservesContinuity))
                {
                    continue;
                }

                found = true;
                bestDistanceSquared = distanceSquared;
                bestNormalized = normalized;
            }

            if (!found)
                return false;

            projectedNormalized = Mathf.Clamp01(bestNormalized);
            lastProjectedNormalized = projectedNormalized;
            hasLastProjected = true;
            return true;
        }

        private bool TryGetPointerOnRailPlane(out Vector3 pointerWorld)
        {
            pointerWorld = default;
            if (railSpace == null || worldRailPoints.Length == 0)
                return false;

            Vector3 normal = railSpace.up;
            if (normal.sqrMagnitude <= 0.000001f)
                return false;
            normal.Normalize();
            if (!useFarProjection)
            {
                if (activeInteractorTransform == null)
                    return false;

                Plane plane = new(normal, worldRailPoints[0]);
                pointerWorld = plane.ClosestPointOnPlane(
                    activeInteractorTransform.position);
                return IsFinite(pointerWorld);
            }

            Transform rayOrigin =
                activeRayProvider?.GetOrCreateRayOrigin();
            if (rayOrigin == null)
                return false;

            if (!hasFarProjectionPlane &&
                !TryCreateFarProjectionPlane())
            {
                return false;
            }

            Vector3 rayDirection = rayOrigin.forward;
            if (Mathf.Abs(Vector3.Dot(
                    rayDirection,
                    farProjectionPlane.normal)) < 0.0001f)
            {
                return false;
            }

            Ray ray = new(rayOrigin.position, rayDirection);
            if (!farProjectionPlane.Raycast(ray, out float enter) ||
                enter < 0f ||
                enter > maximumRayPlaneDistance)
            {
                return false;
            }

            pointerWorld = ray.GetPoint(enter);
            return IsFinite(pointerWorld);
        }

        private bool TryCreateFarProjectionPlane()
        {
            Transform rayOrigin =
                activeRayProvider?.GetOrCreateRayOrigin();
            if (rayOrigin == null ||
                railSpace == null ||
                totalDistanceMeters <= MinimumRailLength)
            {
                hasFarProjectionPlane = false;
                return false;
            }

            EvaluateRail(
                currentNormalized * totalDistanceMeters,
                out Vector3 railPoint,
                out Vector3 railTangent);
            Vector3 up = railSpace.up;
            if (up.sqrMagnitude <= 0.000001f)
                up = Vector3.up;
            else
                up.Normalize();

            Vector3 planePoint = handle != null
                ? handle.position
                : railPoint + up * handleHeightMeters;
            Vector3 railPlaneNormal = Vector3.Cross(
                up,
                railTangent);
            if (railPlaneNormal.sqrMagnitude > 0.000001f)
                railPlaneNormal.Normalize();

            Vector3 viewNormal = Vector3.ProjectOnPlane(
                planePoint - rayOrigin.position,
                up);
            if (viewNormal.sqrMagnitude <= 0.000001f)
            {
                viewNormal = Vector3.ProjectOnPlane(
                    rayOrigin.forward,
                    up);
            }
            if (viewNormal.sqrMagnitude > 0.000001f)
                viewNormal.Normalize();

            Vector3 normal = railPlaneNormal;
            if (normal.sqrMagnitude <= 0.000001f ||
                Mathf.Abs(Vector3.Dot(
                    rayOrigin.forward,
                    normal)) < 0.08f)
            {
                normal = viewNormal;
            }
            if (normal.sqrMagnitude <= 0.000001f)
            {
                hasFarProjectionPlane = false;
                return false;
            }

            farProjectionPlane = new Plane(normal, planePoint);
            hasFarProjectionPlane = true;
            return true;
        }

        private void SetValue(
            float normalized,
            bool notify,
            bool force,
            bool snapVisual)
        {
            float resolved = Mathf.Clamp01(normalized);
            float deltaMeters = Mathf.Abs(
                resolved - currentNormalized) * totalDistanceMeters;
            if (!force && deltaMeters < movementDeadzoneMeters)
                return;

            bool changed = !Mathf.Approximately(
                resolved,
                currentNormalized);
            currentNormalized = resolved;
            if (snapVisual)
            {
                visualNormalized = currentNormalized;
                visualVelocity = 0f;
                ApplyVisualPose(visualNormalized);
            }

            if (notify && changed)
            {
                ValueChanged?.Invoke(
                    DistanceMeters,
                    currentNormalized);
            }
        }

        private float ApplyDetents(float normalized)
        {
            if (totalDistanceMeters <= MinimumRailLength)
                return Mathf.Clamp01(normalized);

            float distance = Mathf.Clamp01(normalized) *
                totalDistanceMeters;
            if (distance <= endpointDetentMeters)
                return 0f;
            if (totalDistanceMeters - distance <= endpointDetentMeters)
                return 1f;
            if (Mathf.Abs(distance - contactDistanceMeters) <=
                contactDetentMeters)
            {
                return ContactNormalized;
            }

            return distance / totalDistanceMeters;
        }

        private bool RefreshWorldRail()
        {
            if (railSpace == null ||
                localRailPoints == null ||
                localRailPoints.Length < 2)
            {
                totalDistanceMeters = 0f;
                contactDistanceMeters = 0f;
                return false;
            }

            totalDistanceMeters = 0f;
            for (int i = 0; i < localRailPoints.Length; i++)
            {
                Vector3 worldPoint = railSpace.TransformPoint(
                    localRailPoints[i]);
                if (!IsFinite(worldPoint))
                    return false;

                worldRailPoints[i] = worldPoint;
                if (i == 0)
                {
                    worldCumulativeDistances[i] = 0f;
                    continue;
                }

                totalDistanceMeters += Vector3.Distance(
                    worldRailPoints[i - 1],
                    worldPoint);
                worldCumulativeDistances[i] = totalDistanceMeters;
            }

            if (!float.IsFinite(totalDistanceMeters) ||
                totalDistanceMeters <= MinimumRailLength)
            {
                totalDistanceMeters = 0f;
                contactDistanceMeters = 0f;
                return false;
            }

            contactDistanceMeters =
                worldCumulativeDistances[contactPointIndex];
            UpdateHandleGeometry();
            return true;
        }

        private void ApplyVisualPose(float normalized)
        {
            if (!configured ||
                worldRailPoints == null ||
                totalDistanceMeters <= MinimumRailLength)
            {
                return;
            }

            EvaluateRail(
                Mathf.Clamp01(normalized) * totalDistanceMeters,
                out Vector3 position,
                out Vector3 tangent);
            Vector3 up = railSpace != null
                ? railSpace.up
                : Vector3.up;
            Vector3 forward = Vector3.ProjectOnPlane(tangent, up);
            if (forward.sqrMagnitude <= 0.000001f && railSpace != null)
                forward = Vector3.ProjectOnPlane(railSpace.forward, up);
            if (forward.sqrMagnitude <= 0.000001f)
                forward = Vector3.forward;

            transform.SetPositionAndRotation(
                position,
                Quaternion.LookRotation(forward.normalized, up));
            UpdateHandleGeometry();
        }

        private void EvaluateRail(
            float distanceMeters,
            out Vector3 position,
            out Vector3 tangent)
        {
            float clamped = Mathf.Clamp(
                distanceMeters,
                0f,
                totalDistanceMeters);
            for (int i = 0; i < worldRailPoints.Length - 1; i++)
            {
                float endDistance = worldCumulativeDistances[i + 1];
                if (clamped > endDistance &&
                    i < worldRailPoints.Length - 2)
                {
                    continue;
                }

                Vector3 segment =
                    worldRailPoints[i + 1] - worldRailPoints[i];
                float segmentLength =
                    endDistance - worldCumulativeDistances[i];
                float progress = segmentLength > 0.000001f
                    ? (clamped - worldCumulativeDistances[i]) /
                    segmentLength
                    : 0f;
                position = Vector3.LerpUnclamped(
                    worldRailPoints[i],
                    worldRailPoints[i + 1],
                    Mathf.Clamp01(progress));
                tangent = segment.sqrMagnitude > 0.000001f
                    ? segment.normalized
                    : Vector3.forward;
                return;
            }

            position = worldRailPoints[worldRailPoints.Length - 1];
            tangent = position -
                worldRailPoints[worldRailPoints.Length - 2];
            if (tangent.sqrMagnitude <= 0.000001f)
                tangent = Vector3.forward;
            else
                tangent.Normalize();
        }

        private void UpdateHandleGeometry()
        {
            if (handle == null || handleCollider == null)
                return;

            Vector3 rootScale = Abs(transform.lossyScale);
            handle.localPosition = Vector3.up *
                (handleHeightMeters / Mathf.Max(0.0001f, rootScale.y));
            handle.localRotation = Quaternion.identity;

            Vector3 handleScale = Abs(handle.lossyScale);
            float radialScale = Mathf.Max(
                0.0001f,
                Mathf.Max(handleScale.x, handleScale.z));
            float verticalScale = Mathf.Max(0.0001f, handleScale.y);
            handleCollider.radius = handleRadiusMeters / radialScale;
            handleCollider.height = Mathf.Max(
                handleCollider.radius * 2f,
                handleColliderHeightMeters / verticalScale);
            handleCollider.center = Vector3.zero;
            handleCollider.direction = 1;
        }

        private void EnsureHandleInteraction()
        {
            if (handle == null)
                return;

            handle.gameObject.layer = 0;
            handleCollider = handle.GetComponent<CapsuleCollider>();
            if (handleCollider == null)
                handleCollider = handle.gameObject.AddComponent<CapsuleCollider>();
            handleCollider.isTrigger = false;

            interactable = handle.GetComponent<XRSimpleInteractable>();
            if (interactable == null)
            {
                interactable =
                    handle.gameObject.AddComponent<XRSimpleInteractable>();
            }

            interactable.colliders.Clear();
            interactable.colliders.Add(handleCollider);
            interactable.interactionLayers =
                InteractionLayerMask.GetMask("Default");
            interactable.selectMode = InteractableSelectMode.Single;
            BindInteractionEvents();
            UpdateHandleGeometry();
        }

        private void EnsureBodyIsColliderFree()
        {
            if (gateBody == null)
                return;

            Collider[] bodyColliders =
                gateBody.GetComponents<Collider>();
            for (int i = 0; i < bodyColliders.Length; i++)
                bodyColliders[i].enabled = false;
        }

        private void SetInteractionActive(bool value)
        {
            if (!value)
            {
                if (interactable != null)
                    interactable.enabled = false;
                if (handleCollider != null)
                    handleCollider.enabled = false;
                SetChildActive(handle, false);
                SetChildActive(gateBody, false);
                return;
            }

            SetChildActive(gateBody, true);
            SetChildActive(handle, true);
            if (handleCollider != null)
                handleCollider.enabled = true;
            if (interactable != null)
                interactable.enabled = true;
        }

        private void BindInteractionEvents()
        {
            if (!isActiveAndEnabled ||
                interactable == null ||
                boundInteractable == interactable)
            {
                return;
            }

            UnbindInteractionEvents();
            boundInteractable = interactable;
            boundInteractable.selectEntered.AddListener(OnSelectEntered);
            boundInteractable.selectExited.AddListener(OnSelectExited);
        }

        private void UnbindInteractionEvents()
        {
            if (boundInteractable == null)
                return;

            boundInteractable.selectEntered.RemoveListener(OnSelectEntered);
            boundInteractable.selectExited.RemoveListener(OnSelectExited);
            boundInteractable = null;
        }

        private void ForceEndGrab()
        {
            bool wasGrabbed = grabbed;
            activeInteractor = null;
            activeRayProvider = null;
            activeInteractorTransform = null;
            projectionModeInitialized = false;
            hasFarProjectionPlane = false;
            useFarProjection = false;
            hasLastProjected = false;
            grabOffsetNormalized = 0f;
            grabbed = false;
            visualNormalized = currentNormalized;
            visualVelocity = 0f;
            if (wasGrabbed)
                GrabStateChanged?.Invoke(false);
        }

        private Transform ResolveBody(Transform value)
        {
            Transform resolved = value;
            if (resolved == null || resolved == transform)
            {
                resolved = new GameObject(
                    "CollisionTimeLensGateBody").transform;
            }

            if (resolved.parent != transform)
                resolved.SetParent(transform, false);
            return resolved;
        }

        private Transform ResolveHandle(Transform value)
        {
            Transform resolved = value;
            if (resolved == null ||
                resolved == transform ||
                resolved == gateBody)
            {
                resolved = new GameObject(
                    "CollisionTimeLensHandle").transform;
            }

            if (resolved.parent != transform)
                resolved.SetParent(transform, false);
            return resolved;
        }

        private static bool TryCopyRail(
            Transform valueRailSpace,
            IReadOnlyList<Vector3> railPoints,
            int valueContactPointIndex,
            out Vector3[] copiedPoints)
        {
            copiedPoints = null;
            if (valueRailSpace == null ||
                railPoints == null ||
                railPoints.Count < 2 ||
                valueContactPointIndex < 0 ||
                valueContactPointIndex >= railPoints.Count)
            {
                return false;
            }

            copiedPoints = new Vector3[railPoints.Count];
            float localLength = 0f;
            for (int i = 0; i < railPoints.Count; i++)
            {
                Vector3 point = railPoints[i];
                if (!IsFinite(point))
                {
                    copiedPoints = null;
                    return false;
                }

                copiedPoints[i] = point;
                if (i > 0)
                {
                    localLength += Vector3.Distance(
                        copiedPoints[i - 1],
                        point);
                }
            }

            if (!float.IsFinite(localLength) ||
                localLength <= MinimumRailLength)
            {
                copiedPoints = null;
                return false;
            }

            return true;
        }

        private static void SetChildActive(
            Transform target,
            bool value)
        {
            if (target != null && target.gameObject.activeSelf != value)
                target.gameObject.SetActive(value);
        }

        private static Vector3 Abs(Vector3 value)
        {
            return new Vector3(
                Mathf.Abs(value.x),
                Mathf.Abs(value.y),
                Mathf.Abs(value.z));
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) &&
                float.IsFinite(value.y) &&
                float.IsFinite(value.z);
        }
    }
}
