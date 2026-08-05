using UnityEngine;
using UnityEngine.Rendering;

namespace F1XR.RestAPI.Replay.Room
{
    public sealed partial class ShowcasePortalPresentation
    {
        private const int PortalRippleSegments = 36;
        private const float PortalEffectSurfaceOffset = -0.008f;
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId =
            Shader.PropertyToID("_Color");

        private OvertakePortalTransitionVfxSettings
            portalTransitionSettings;
        private Transform portalTransitionOvertaker;
        private Transform portalTransitionDefender;
        private Transform portalTransitionRoot;
        private Transform portalRippleRoot;
        private Transform portalWakeRoot;
        private Transform portalSurfaceSweepRoot;
        private MeshRenderer portalRippleGlowRenderer;
        private MeshRenderer portalRippleCoreRenderer;
        private MeshRenderer portalWakeGlowRenderer;
        private MeshRenderer portalWakeCoreRenderer;
        private MeshRenderer portalSurfaceSweepRenderer;
        private MeshRenderer portalEdgeRenderer;
        private Material portalTransitionMaterial;
        private MaterialPropertyBlock portalRippleGlowBlock;
        private MaterialPropertyBlock portalRippleCoreBlock;
        private MaterialPropertyBlock portalWakeGlowBlock;
        private MaterialPropertyBlock portalWakeCoreBlock;
        private MaterialPropertyBlock portalSurfaceSweepBlock;
        private MaterialPropertyBlock portalEdgeBlock;
        private PortalCrossingState overtakerCrossingState;
        private PortalCrossingState defenderCrossingState;
        private float portalTransitionLastReplayTime = float.NaN;
        private float portalTransitionEffectStartTime = float.NaN;
        private bool portalTransitionConfigured;
        private bool portalRippleActive;
        private bool portalWakeActive;
        private bool portalSurfaceSweepActive;
        private bool portalEdgeActive;
        private float portalSurfaceSweepCrossingX;
        private float portalSurfaceSweepCrossingY;
        private bool portalTransitionPending;
        private Vector3 pendingPortalCrossingPoint;
        private Vector3 pendingPortalTravelDirection;

        public void ConfigureOvertakePortalTransition(
            OvertakePortalTransitionVfxSettings settings,
            Transform overtakingVehicle,
            Transform defendingVehicle,
            float replayTime)
        {
            ResetOvertakePortalTransitionVfx();
            ClearPendingPortalTransition();
            portalTransitionSettings =
                settings ??
                new OvertakePortalTransitionVfxSettings();
            portalTransitionSettings.ClampValues();
            portalTransitionOvertaker = overtakingVehicle;
            portalTransitionDefender = defendingVehicle;
            portalTransitionConfigured =
                configured &&
                exitSurface != null &&
                portalTransitionOvertaker != null;
            portalTransitionLastReplayTime = replayTime;
            InitializeCrossingState(
                ref overtakerCrossingState,
                portalTransitionOvertaker);
            InitializeCrossingState(
                ref defenderCrossingState,
                portalTransitionDefender);

            if (portalTransitionConfigured &&
                portalTransitionSettings.enabled)
            {
                EnsurePortalTransitionVisuals();
            }
        }

        public void UpdateOvertakePortalTransition(
            float replayTime,
            bool isPlaying,
            bool overtakeCompletionConfirmed)
        {
            if (!portalTransitionConfigured ||
                portalTransitionSettings == null ||
                portalTransitionOvertaker == null ||
                exitSurface == null)
            {
                ResetOvertakePortalTransitionVfx();
                ClearPendingPortalTransition();
                return;
            }

            if (!portalTransitionSettings.enabled)
            {
                ResetOvertakePortalTransitionVfx();
                ClearPendingPortalTransition();
                portalTransitionLastReplayTime =
                    float.NaN;
                overtakerCrossingState = default;
                defenderCrossingState = default;
                return;
            }

            if (!isPlaying)
                return;

            EnsurePortalTransitionVisuals();
            if (portalTransitionPending &&
                overtakeCompletionConfirmed &&
                exitPortalVisible)
            {
                TriggerPortalTransition(
                    replayTime,
                    pendingPortalCrossingPoint,
                    pendingPortalTravelDirection);
                ClearPendingPortalTransition();
            }

            if (float.IsNaN(
                    portalTransitionLastReplayTime))
            {
                portalTransitionLastReplayTime =
                    replayTime;
                InitializeCrossingState(
                    ref overtakerCrossingState,
                    portalTransitionOvertaker);
                InitializeCrossingState(
                    ref defenderCrossingState,
                    portalTransitionDefender);
                return;
            }

            float replayDelta =
                replayTime -
                portalTransitionLastReplayTime;
            if (replayDelta < -0.0001f)
            {
                ResetOvertakePortalTransitionVfx();
                ClearPendingPortalTransition();
                InitializeCrossingState(
                    ref overtakerCrossingState,
                    portalTransitionOvertaker);
                InitializeCrossingState(
                    ref defenderCrossingState,
                    portalTransitionDefender);
                portalTransitionLastReplayTime =
                    replayTime;
                return;
            }

            if (replayDelta >
                portalTransitionSettings
                    .largeForwardSeekThresholdSeconds)
            {
                ResetOvertakePortalTransitionVfx();
                ClearPendingPortalTransition();
                bool crossed =
                    TryUpdateCrossingState(
                        ref overtakerCrossingState,
                        portalTransitionOvertaker,
                        out _,
                        out _);
                if (!portalTransitionSettings
                        .overtakingCarOnly)
                {
                    crossed |=
                        TryUpdateCrossingState(
                            ref defenderCrossingState,
                            portalTransitionDefender,
                            out _,
                            out _);
                }

                if (crossed &&
                    overtakeCompletionConfirmed &&
                    portalTransitionSettings
                        .largeForwardSeekPolicy ==
                    OvertakePortalLargeForwardSeekPolicy
                        .PortalEdgePulseOnly)
                {
                    TriggerPortalEdgePulse(replayTime);
                }

                portalTransitionLastReplayTime =
                    replayTime;
                UpdatePortalTransitionEffects(
                    replayTime);
                return;
            }

            if (replayDelta >
                portalTransitionSettings
                    .seekResetThresholdSeconds)
            {
                ResetOvertakePortalTransitionVfx();
                ClearPendingPortalTransition();
                InitializeCrossingState(
                    ref overtakerCrossingState,
                    portalTransitionOvertaker);
                InitializeCrossingState(
                    ref defenderCrossingState,
                    portalTransitionDefender);
                portalTransitionLastReplayTime =
                    replayTime;
                return;
            }

            if (TryUpdateCrossingState(
                    ref overtakerCrossingState,
                    portalTransitionOvertaker,
                    out Vector3 crossingPoint,
                    out Vector3 travelDirection))
            {
                if (overtakeCompletionConfirmed &&
                    exitPortalVisible)
                {
                    TriggerPortalTransition(
                        replayTime,
                        crossingPoint,
                        travelDirection);
                }
                else
                {
                    RememberPendingPortalTransition(
                        crossingPoint,
                        travelDirection);
                }
            }
            else if (
                !portalTransitionSettings
                    .overtakingCarOnly &&
                TryUpdateCrossingState(
                    ref defenderCrossingState,
                    portalTransitionDefender,
                    out crossingPoint,
                    out travelDirection))
            {
                if (overtakeCompletionConfirmed &&
                    exitPortalVisible)
                {
                    TriggerPortalTransition(
                        replayTime,
                        crossingPoint,
                        travelDirection);
                }
                else
                {
                    RememberPendingPortalTransition(
                        crossingPoint,
                        travelDirection);
                }
            }

            portalTransitionLastReplayTime =
                replayTime;
            UpdatePortalTransitionEffects(
                replayTime);
        }

        private void ClearOvertakePortalTransition()
        {
            ResetOvertakePortalTransitionVfx();
            ClearPendingPortalTransition();
            if (portalTransitionRoot != null)
                portalTransitionRoot.gameObject.SetActive(false);

            portalTransitionSettings = null;
            portalTransitionOvertaker = null;
            portalTransitionDefender = null;
            portalTransitionRoot = null;
            portalRippleRoot = null;
            portalWakeRoot = null;
            portalSurfaceSweepRoot = null;
            portalRippleGlowRenderer = null;
            portalRippleCoreRenderer = null;
            portalWakeGlowRenderer = null;
            portalWakeCoreRenderer = null;
            portalSurfaceSweepRenderer = null;
            portalEdgeRenderer = null;
            portalTransitionMaterial = null;
            portalRippleGlowBlock = null;
            portalRippleCoreBlock = null;
            portalWakeGlowBlock = null;
            portalWakeCoreBlock = null;
            portalSurfaceSweepBlock = null;
            portalEdgeBlock = null;
            overtakerCrossingState = default;
            defenderCrossingState = default;
            portalTransitionLastReplayTime =
                float.NaN;
            portalTransitionEffectStartTime =
                float.NaN;
            portalTransitionConfigured = false;
        }

        private void RememberPendingPortalTransition(
            Vector3 crossingPoint,
            Vector3 travelDirection)
        {
            portalTransitionPending = true;
            pendingPortalCrossingPoint = crossingPoint;
            pendingPortalTravelDirection = travelDirection;
        }

        private void ClearPendingPortalTransition()
        {
            portalTransitionPending = false;
            pendingPortalCrossingPoint = Vector3.zero;
            pendingPortalTravelDirection = Vector3.zero;
        }

        private void InitializeCrossingState(
            ref PortalCrossingState state,
            Transform vehicle)
        {
            state = default;
            if (vehicle == null ||
                portalTransitionSettings == null)
            {
                return;
            }

            Vector3 position = vehicle.position;
            float distance =
                GetDirectionalPortalDistance(position);
            state.Initialized = true;
            state.Armed =
                distance >=
                portalTransitionSettings
                    .crossingHysteresis;
            state.PreviousDistance = distance;
            if (distance >= 0f)
            {
                state.StartSideDistance = distance;
                state.StartSidePosition = position;
            }
        }

        private bool TryUpdateCrossingState(
            ref PortalCrossingState state,
            Transform vehicle,
            out Vector3 crossingPoint,
            out Vector3 travelDirection)
        {
            crossingPoint = Vector3.zero;
            travelDirection = Vector3.zero;
            if (vehicle == null ||
                portalTransitionSettings == null)
            {
                return false;
            }

            Vector3 position = vehicle.position;
            float distance =
                GetDirectionalPortalDistance(position);
            if (!state.Initialized)
            {
                InitializeCrossingState(
                    ref state,
                    vehicle);
                return false;
            }

            float margin =
                portalTransitionSettings
                    .crossingHysteresis;
            if (!state.Triggered &&
                distance >= margin)
            {
                state.Armed = true;
            }

            if (!state.Triggered &&
                distance >= 0f)
            {
                state.StartSideDistance = distance;
                state.StartSidePosition = position;
            }

            bool movingTowardPortalOutside =
                distance <
                state.PreviousDistance -
                0.00001f;
            bool crossed =
                !state.Triggered &&
                state.Armed &&
                movingTowardPortalOutside &&
                distance <= -margin;
            if (crossed)
            {
                float startDistance =
                    Mathf.Max(
                        0f,
                        state.StartSideDistance);
                float denominator =
                    startDistance -
                    distance;
                float interpolation =
                    denominator > 0.000001f
                        ? Mathf.Clamp01(
                            startDistance /
                            denominator)
                        : 1f;
                crossingPoint =
                    Vector3.Lerp(
                        state.StartSidePosition,
                        position,
                        interpolation);
                travelDirection =
                    position -
                    state.StartSidePosition;
                if (travelDirection.sqrMagnitude <=
                    0.000001f)
                {
                    travelDirection =
                        -exitInward *
                        GetCrossingDirectionSign();
                }
                else
                {
                    travelDirection.Normalize();
                }

                state.Triggered = true;
                state.Armed = false;
            }

            state.PreviousDistance = distance;
            return crossed;
        }

        private float GetDirectionalPortalDistance(
            Vector3 position)
        {
            float signedDistance =
                Vector3.Dot(
                    position - exitPosition,
                    exitInward);
            return signedDistance *
                GetCrossingDirectionSign();
        }

        private float GetCrossingDirectionSign()
        {
            return portalTransitionSettings != null &&
                portalTransitionSettings
                    .crossingDirection ==
                OvertakePortalCrossingDirection
                    .PortalOutsideToRoomInside
                ? -1f
                : 1f;
        }

        private void TriggerPortalTransition(
            float replayTime,
            Vector3 crossingPoint,
            Vector3 travelDirection)
        {
            EnsurePortalTransitionVisuals();
            if (portalTransitionRoot == null ||
                exitSurface == null)
            {
                return;
            }

            Vector3 projectedPoint =
                crossingPoint -
                exitInward *
                Vector3.Dot(
                    crossingPoint - exitPosition,
                    exitInward);
            Vector3 localPoint =
                exitSurface.InverseTransformPoint(
                    projectedPoint);
            float halfWidth =
                exitPortalSize.x * 0.5f;
            float halfHeight =
                exitPortalSize.y * 0.5f;
            float radius =
                portalTransitionSettings
                    .rippleEndRadius;
            localPoint.x =
                Mathf.Clamp(
                    localPoint.x,
                    -halfWidth + radius * 0.25f,
                    halfWidth - radius * 0.25f);
            localPoint.y =
                Mathf.Clamp(
                    localPoint.y,
                    -halfHeight + radius * 0.2f,
                    halfHeight - radius * 0.2f);
            localPoint.z =
                PortalEffectSurfaceOffset;
            portalRippleRoot.localPosition =
                localPoint;

            Vector3 localTravel =
                exitSurface.InverseTransformDirection(
                    travelDirection);
            localTravel.y = 0f;
            if (localTravel.sqrMagnitude <=
                0.000001f)
            {
                localTravel = Vector3.forward;
            }
            else
            {
                localTravel.Normalize();
            }

            portalWakeRoot.localPosition =
                new Vector3(
                    localPoint.x,
                    localPoint.y +
                    portalTransitionSettings
                        .wakeWidth * 0.12f,
                    PortalEffectSurfaceOffset -
                    0.004f);
            portalWakeRoot.localRotation =
                Quaternion.LookRotation(
                    localTravel,
                    Vector3.up);
            portalSurfaceSweepCrossingX =
                localPoint.x;
            portalSurfaceSweepCrossingY =
                localPoint.y;

            portalTransitionEffectStartTime =
                replayTime;
            portalRippleActive = true;
            portalWakeActive = true;
            portalSurfaceSweepActive = true;
            portalEdgeActive = true;
            portalRippleGlowRenderer.enabled = true;
            portalRippleCoreRenderer.enabled = true;
            portalWakeGlowRenderer.enabled = true;
            portalWakeCoreRenderer.enabled = true;
            portalSurfaceSweepRenderer.enabled = true;
            portalEdgeRenderer.enabled = true;
            UpdatePortalTransitionEffects(
                replayTime);
        }

        private void TriggerPortalEdgePulse(
            float replayTime)
        {
            EnsurePortalTransitionVisuals();
            if (portalEdgeRenderer == null)
                return;

            portalTransitionEffectStartTime =
                replayTime;
            portalRippleActive = false;
            portalWakeActive = false;
            portalSurfaceSweepActive = false;
            portalEdgeActive = true;
            if (portalRippleGlowRenderer != null)
                portalRippleGlowRenderer.enabled = false;
            if (portalRippleCoreRenderer != null)
                portalRippleCoreRenderer.enabled = false;
            if (portalWakeGlowRenderer != null)
                portalWakeGlowRenderer.enabled = false;
            if (portalWakeCoreRenderer != null)
                portalWakeCoreRenderer.enabled = false;
            if (portalSurfaceSweepRenderer != null)
                portalSurfaceSweepRenderer.enabled = false;
            portalEdgeRenderer.enabled = true;
            UpdatePortalTransitionEffects(
                replayTime);
        }

        private void UpdatePortalTransitionEffects(
            float replayTime)
        {
            if (portalTransitionSettings == null ||
                float.IsNaN(
                    portalTransitionEffectStartTime))
            {
                return;
            }

            float elapsed =
                Mathf.Max(
                    0f,
                    replayTime -
                    portalTransitionEffectStartTime);
            UpdatePortalRipple(elapsed);
            UpdatePortalWake(elapsed);
            UpdatePortalSurfaceSweep(elapsed);
            UpdatePortalEdgePulse(elapsed);
            if (!portalRippleActive &&
                !portalWakeActive &&
                !portalSurfaceSweepActive &&
                !portalEdgeActive)
            {
                portalTransitionEffectStartTime =
                    float.NaN;
            }
        }

        private void UpdatePortalRipple(float elapsed)
        {
            if (!portalRippleActive)
                return;

            float duration =
                Mathf.Max(
                    0.01f,
                    portalTransitionSettings
                        .rippleDurationReplaySeconds);
            float progress =
                Mathf.Clamp01(
                    elapsed / duration);
            if (progress >= 1f)
            {
                portalRippleActive = false;
                portalRippleGlowRenderer.enabled =
                    false;
                portalRippleCoreRenderer.enabled =
                    false;
                return;
            }

            float eased =
                Mathf.SmoothStep(
                    0f,
                    1f,
                    progress);
            float radius =
                Mathf.Lerp(
                    portalTransitionSettings
                        .rippleStartRadius,
                    portalTransitionSettings
                        .rippleEndRadius,
                    eased);
            portalRippleGlowRenderer.transform
                .localScale =
                    new Vector3(
                        radius,
                        radius,
                        1f);
            portalRippleCoreRenderer.transform
                .localScale =
                    new Vector3(
                        radius * 0.985f,
                        radius * 0.985f,
                        1f);

            float pulse =
                Mathf.Sin(
                    Mathf.PI * progress);
            ApplyPortalTransitionColor(
                portalRippleGlowRenderer,
                portalRippleGlowBlock,
                portalTransitionSettings
                    .rippleGlowColor,
                pulse,
                portalTransitionSettings
                    .rippleIntensity);
            ApplyPortalTransitionColor(
                portalRippleCoreRenderer,
                portalRippleCoreBlock,
                portalTransitionSettings
                    .rippleCoreColor,
                pulse,
                portalTransitionSettings
                    .rippleIntensity);
        }

        private void UpdatePortalWake(float elapsed)
        {
            if (!portalWakeActive)
                return;

            float duration =
                Mathf.Max(
                    0.01f,
                    portalTransitionSettings
                        .wakeDurationReplaySeconds);
            float progress =
                Mathf.Clamp01(
                    elapsed / duration);
            if (progress >= 1f)
            {
                portalWakeActive = false;
                portalWakeGlowRenderer.enabled = false;
                portalWakeCoreRenderer.enabled = false;
                return;
            }

            float contraction =
                1f -
                Mathf.SmoothStep(
                    0f,
                    1f,
                    progress);
            portalWakeRoot.localScale =
                new Vector3(
                    portalTransitionSettings
                        .wakeWidth,
                    1f,
                    Mathf.Max(
                        0.001f,
                        portalTransitionSettings
                            .wakeLength *
                        contraction));
            float fade =
                Mathf.Clamp01(
                    portalTransitionSettings
                        .wakeFadeCurve.Evaluate(
                            progress));
            ApplyPortalTransitionColor(
                portalWakeGlowRenderer,
                portalWakeGlowBlock,
                portalTransitionSettings
                    .rippleGlowColor,
                fade,
                portalTransitionSettings
                    .wakeIntensity);
            ApplyPortalTransitionColor(
                portalWakeCoreRenderer,
                portalWakeCoreBlock,
                portalTransitionSettings
                    .rippleCoreColor,
                fade,
                portalTransitionSettings
                    .wakeIntensity * 1.35f);
        }

        private void UpdatePortalSurfaceSweep(float elapsed)
        {
            if (!portalSurfaceSweepActive)
                return;

            float duration =
                Mathf.Max(
                    0.01f,
                    portalTransitionSettings
                        .surfaceSweepDurationReplaySeconds);
            float progress =
                Mathf.Clamp01(
                    elapsed / duration);
            if (progress >= 1f)
            {
                portalSurfaceSweepActive = false;
                portalSurfaceSweepRenderer.enabled =
                    false;
                return;
            }

            float expansion =
                Mathf.SmoothStep(
                    0f,
                    1f,
                    progress);
            float halfWidth =
                exitPortalSize.x * 0.5f;
            float left =
                Mathf.Lerp(
                    portalSurfaceSweepCrossingX,
                    -halfWidth,
                    expansion);
            float right =
                Mathf.Lerp(
                    portalSurfaceSweepCrossingX,
                    halfWidth,
                    expansion);
            portalSurfaceSweepRoot.localPosition =
                new Vector3(
                    (left + right) * 0.5f,
                    portalSurfaceSweepCrossingY,
                    PortalEffectSurfaceOffset -
                    0.005f);
            portalSurfaceSweepRoot.localScale =
                new Vector3(
                    Mathf.Max(
                        0.02f,
                        right - left),
                    portalTransitionSettings
                        .surfaceSweepWidth *
                    Mathf.Lerp(
                        1f,
                        0.45f,
                        progress),
                    1f);

            float pulse =
                Mathf.Sin(
                    Mathf.PI * progress) *
                (1f - progress * 0.35f);
            ApplyPortalTransitionColor(
                portalSurfaceSweepRenderer,
                portalSurfaceSweepBlock,
                portalTransitionSettings
                    .rippleCoreColor,
                pulse,
                portalTransitionSettings
                    .surfaceSweepIntensity);
        }

        private void UpdatePortalEdgePulse(float elapsed)
        {
            if (!portalEdgeActive)
                return;

            float duration =
                Mathf.Max(
                    0.01f,
                    portalTransitionSettings
                        .edgePulseDurationReplaySeconds);
            float progress =
                Mathf.Clamp01(
                    elapsed / duration);
            if (progress >= 1f)
            {
                portalEdgeActive = false;
                portalEdgeRenderer.enabled = false;
                return;
            }

            float pulse =
                Mathf.Sin(
                    Mathf.PI * progress);
            ApplyPortalTransitionColor(
                portalEdgeRenderer,
                portalEdgeBlock,
                portalTransitionSettings
                    .edgePulseColor,
                pulse,
                portalTransitionSettings
                    .edgePulseIntensity);
        }

        private void ResetOvertakePortalTransitionVfx()
        {
            portalRippleActive = false;
            portalWakeActive = false;
            portalSurfaceSweepActive = false;
            portalEdgeActive = false;
            portalTransitionEffectStartTime =
                float.NaN;
            if (portalRippleGlowRenderer != null)
                portalRippleGlowRenderer.enabled = false;
            if (portalRippleCoreRenderer != null)
                portalRippleCoreRenderer.enabled = false;
            if (portalWakeGlowRenderer != null)
                portalWakeGlowRenderer.enabled = false;
            if (portalWakeCoreRenderer != null)
                portalWakeCoreRenderer.enabled = false;
            if (portalSurfaceSweepRenderer != null)
                portalSurfaceSweepRenderer.enabled = false;
            if (portalEdgeRenderer != null)
                portalEdgeRenderer.enabled = false;
        }

        private void EnsurePortalTransitionVisuals()
        {
            if (portalTransitionRoot != null ||
                exitSurface == null ||
                portalTransitionSettings == null)
            {
                return;
            }

            portalTransitionMaterial =
                CreatePortalTransitionMaterial();
            if (portalTransitionMaterial == null)
                return;

            portalTransitionRoot =
                new GameObject(
                    "OvertakeExitPortalTransitionVfx")
                    .transform;
            portalTransitionRoot.gameObject.layer =
                PortalSurfaceLayer;
            portalTransitionRoot.SetParent(
                exitSurface,
                false);

            portalRippleRoot =
                new GameObject(
                    "LocalizedPortalRipple")
                    .transform;
            portalRippleRoot.gameObject.layer =
                PortalSurfaceLayer;
            portalRippleRoot.SetParent(
                portalTransitionRoot,
                false);

            Mesh glowMesh =
                CreatePortalAnnulusMesh(
                    "Runtime_ExitPortalRippleGlow",
                    portalTransitionSettings
                        .rippleEndRadius,
                    portalTransitionSettings
                        .rippleWidth * 2.2f);
            Mesh coreMesh =
                CreatePortalAnnulusMesh(
                    "Runtime_ExitPortalRippleCore",
                    portalTransitionSettings
                        .rippleEndRadius,
                    portalTransitionSettings
                        .rippleWidth);
            portalRippleGlowRenderer =
                CreatePortalTransitionRenderer(
                    "RippleGlow",
                    glowMesh,
                    portalRippleRoot,
                    31);
            portalRippleCoreRenderer =
                CreatePortalTransitionRenderer(
                    "RippleCore",
                    coreMesh,
                    portalRippleRoot,
                    32);
            portalRippleGlowRenderer.transform
                .localPosition =
                    new Vector3(
                        0f,
                        0f,
                        -0.001f);
            portalRippleCoreRenderer.transform
                .localPosition =
                    new Vector3(
                        0f,
                        0f,
                        -0.002f);

            portalWakeRoot =
                new GameObject(
                    "PortalWake")
                    .transform;
            portalWakeRoot.gameObject.layer =
                PortalSurfaceLayer;
            portalWakeRoot.SetParent(
                portalTransitionRoot,
                false);
            Mesh wakeMesh =
                CreatePortalWakeMesh();
            portalWakeGlowRenderer =
                CreatePortalTransitionRenderer(
                    "PortalWakeGlow",
                    wakeMesh,
                    portalWakeRoot,
                    30);
            portalWakeCoreRenderer =
                CreatePortalTransitionRenderer(
                    "PortalWakeCore",
                    wakeMesh,
                    portalWakeRoot,
                    31);
            portalWakeCoreRenderer.transform
                .localScale =
                    new Vector3(
                        0.34f,
                        1f,
                        0.82f);

            portalSurfaceSweepRenderer =
                CreatePortalTransitionRenderer(
                    "PortalSurfaceSweep",
                    CreatePortalUnitQuadMesh(),
                    portalTransitionRoot,
                    30);
            portalSurfaceSweepRoot =
                portalSurfaceSweepRenderer.transform;

            float edgeWidth =
                Mathf.Max(
                    0.015f,
                    portalTransitionSettings
                        .rippleWidth * 0.45f);
            portalEdgeRenderer =
                CreatePortalTransitionRenderer(
                    "PortalEdgePulse",
                    CreatePortalFrameMesh(
                        exitPortalSize,
                        edgeWidth),
                    portalTransitionRoot,
                    29);
            portalEdgeRenderer.transform
                .localPosition =
                    new Vector3(
                        0f,
                        0f,
                        PortalEffectSurfaceOffset -
                        0.006f);

            portalRippleGlowBlock =
                new MaterialPropertyBlock();
            portalRippleCoreBlock =
                new MaterialPropertyBlock();
            portalWakeGlowBlock =
                new MaterialPropertyBlock();
            portalWakeCoreBlock =
                new MaterialPropertyBlock();
            portalSurfaceSweepBlock =
                new MaterialPropertyBlock();
            portalEdgeBlock =
                new MaterialPropertyBlock();
            ResetOvertakePortalTransitionVfx();
        }

        private Material CreatePortalTransitionMaterial()
        {
            Shader shader =
                Shader.Find(
                    "Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null)
                return null;

            Material material =
                new(shader)
                {
                    name =
                        "Runtime_ExitPortalTransitionVfx"
                };
            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_SrcBlend"))
            {
                material.SetFloat(
                    "_SrcBlend",
                    (float)BlendMode.SrcAlpha);
            }
            if (material.HasProperty("_DstBlend"))
            {
                material.SetFloat(
                    "_DstBlend",
                    (float)BlendMode.One);
            }
            if (material.HasProperty("_ZWrite"))
                material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_Cull"))
            {
                material.SetFloat(
                    "_Cull",
                    (float)CullMode.Off);
            }
            if (material.HasProperty(BaseColorId))
                material.SetColor(BaseColorId, Color.white);
            if (material.HasProperty(ColorId))
                material.SetColor(ColorId, Color.white);
            material.EnableKeyword(
                "_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = 3150;
            runtimeMaterials.Add(material);
            return material;
        }

        private MeshRenderer CreatePortalTransitionRenderer(
            string name,
            Mesh mesh,
            Transform parent,
            int sortingOrder)
        {
            GameObject obj = new(name);
            obj.layer = PortalSurfaceLayer;
            obj.transform.SetParent(parent, false);
            MeshFilter filter =
                obj.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer =
                obj.AddComponent<MeshRenderer>();
            renderer.sharedMaterial =
                portalTransitionMaterial;
            renderer.shadowCastingMode =
                ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage =
                LightProbeUsage.Off;
            renderer.reflectionProbeUsage =
                ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            renderer.sortingOrder = sortingOrder;
            renderer.enabled = false;
            return renderer;
        }

        private Mesh CreatePortalAnnulusMesh(
            string name,
            float endRadius,
            float width)
        {
            float safeRadius =
                Mathf.Max(
                    0.01f,
                    endRadius);
            float innerRadius =
                Mathf.Clamp01(
                    1f -
                    width /
                    safeRadius);
            Vector3[] vertices =
                new Vector3[
                    (PortalRippleSegments + 1) * 2];
            int[] triangles =
                new int[
                    PortalRippleSegments * 6];
            for (int i = 0;
                 i <= PortalRippleSegments;
                 i++)
            {
                float angle =
                    Mathf.PI * 2f * i /
                    PortalRippleSegments;
                float x = Mathf.Cos(angle);
                float y = Mathf.Sin(angle);
                int vertex = i * 2;
                vertices[vertex] =
                    new Vector3(
                        x,
                        y,
                        0f);
                vertices[vertex + 1] =
                    new Vector3(
                        x * innerRadius,
                        y * innerRadius,
                        0f);

                if (i >= PortalRippleSegments)
                    continue;

                int triangle = i * 6;
                triangles[triangle] = vertex;
                triangles[triangle + 1] =
                    vertex + 2;
                triangles[triangle + 2] =
                    vertex + 1;
                triangles[triangle + 3] =
                    vertex + 1;
                triangles[triangle + 4] =
                    vertex + 2;
                triangles[triangle + 5] =
                    vertex + 3;
            }

            Mesh mesh =
                new()
                {
                    name = name,
                    vertices = vertices,
                    triangles = triangles
                };
            mesh.RecalculateBounds();
            runtimeMeshes.Add(mesh);
            return mesh;
        }

        private Mesh CreatePortalWakeMesh()
        {
            Mesh mesh =
                new()
                {
                    name =
                        "Runtime_ExitPortalWake",
                    vertices =
                        new[]
                        {
                            new Vector3(-0.5f, 0f, 0f),
                            new Vector3(0.5f, 0f, 0f),
                            new Vector3(-0.12f, 0f, -1f),
                            new Vector3(0.12f, 0f, -1f)
                        },
                    triangles =
                        new[]
                        {
                            0, 2, 1,
                            1, 2, 3
                        }
                };
            mesh.RecalculateBounds();
            runtimeMeshes.Add(mesh);
            return mesh;
        }

        private Mesh CreatePortalUnitQuadMesh()
        {
            Mesh mesh =
                new()
                {
                    name =
                        "Runtime_ExitPortalSurfaceSweep",
                    vertices =
                        new[]
                        {
                            new Vector3(-0.5f, -0.5f, 0f),
                            new Vector3(0.5f, -0.5f, 0f),
                            new Vector3(-0.5f, 0.5f, 0f),
                            new Vector3(0.5f, 0.5f, 0f)
                        },
                    triangles =
                        new[]
                        {
                            0, 2, 1,
                            1, 2, 3
                        }
                };
            mesh.RecalculateBounds();
            runtimeMeshes.Add(mesh);
            return mesh;
        }

        private Mesh CreatePortalFrameMesh(
            Vector2 size,
            float width)
        {
            float halfWidth = size.x * 0.5f;
            float halfHeight = size.y * 0.5f;
            float innerHalfWidth =
                Mathf.Max(
                    0f,
                    halfWidth - width);
            float innerHalfHeight =
                Mathf.Max(
                    0f,
                    halfHeight - width);
            Vector3[] vertices =
                {
                    new(-halfWidth, -halfHeight, 0f),
                    new(halfWidth, -halfHeight, 0f),
                    new(-innerHalfWidth, -innerHalfHeight, 0f),
                    new(innerHalfWidth, -innerHalfHeight, 0f),
                    new(-halfWidth, halfHeight, 0f),
                    new(halfWidth, halfHeight, 0f),
                    new(-innerHalfWidth, innerHalfHeight, 0f),
                    new(innerHalfWidth, innerHalfHeight, 0f),
                    new(-halfWidth, -innerHalfHeight, 0f),
                    new(-innerHalfWidth, -innerHalfHeight, 0f),
                    new(-halfWidth, innerHalfHeight, 0f),
                    new(-innerHalfWidth, innerHalfHeight, 0f),
                    new(innerHalfWidth, -innerHalfHeight, 0f),
                    new(halfWidth, -innerHalfHeight, 0f),
                    new(innerHalfWidth, innerHalfHeight, 0f),
                    new(halfWidth, innerHalfHeight, 0f)
                };
            int[] triangles =
                {
                    0, 2, 1, 1, 2, 3,
                    6, 4, 7, 7, 4, 5,
                    8, 10, 9, 9, 10, 11,
                    12, 14, 13, 13, 14, 15
                };
            Mesh mesh =
                new()
                {
                    name =
                        "Runtime_ExitPortalEdgePulse",
                    vertices = vertices,
                    triangles = triangles
                };
            mesh.RecalculateBounds();
            runtimeMeshes.Add(mesh);
            return mesh;
        }

        private static void ApplyPortalTransitionColor(
            MeshRenderer renderer,
            MaterialPropertyBlock block,
            Color source,
            float alpha,
            float intensity)
        {
            if (renderer == null ||
                block == null)
            {
                return;
            }

            Color color = new(
                source.r * intensity,
                source.g * intensity,
                source.b * intensity,
                source.a *
                Mathf.Clamp01(alpha));
            block.Clear();
            block.SetColor(
                BaseColorId,
                color);
            block.SetColor(
                ColorId,
                color);
            renderer.SetPropertyBlock(block);
        }

        private struct PortalCrossingState
        {
            public bool Initialized;
            public bool Armed;
            public bool Triggered;
            public float PreviousDistance;
            public float StartSideDistance;
            public Vector3 StartSidePosition;
        }
    }
}
