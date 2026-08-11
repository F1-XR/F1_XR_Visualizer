using System;
using System.Collections;
using System.Collections.Generic;
using F1XR.RaceFlags;
using F1XR.RestAPI.Api;
using F1XR.RestAPI.Replay.Room;
using F1XR.RestAPI.Utility;
using UnityEngine;
using UnityEngine.Rendering;

namespace F1XR.RestAPI.Replay
{
    public enum CollisionPresentationPhase
    {
        Preparing,
        IslandReveal,
        PreImpact,
        Impact,
        PostImpact,
        ForensicHold,
        ImpactReplay
    }

    [Serializable]
    public sealed class CollisionShowcaseVfxSettings
    {
        public bool enabled = true;

        [Header("Hero Presentation")]
        [Min(0.1f)] public float targetVehicleLengthMeters = 0.42f;
        [Range(1f, 2.8f)] public float maximumIslandSpanMeters = 2.8f;
        [Min(0.1f)] public float minimumStageScale = 8f;
        [Min(0.1f)] public float maximumStageScale = 120f;

        [Header("Viewer Placement")]
        [Min(0.5f)] public float focusForwardDistanceMeters = 1.6f;
        [Min(0f)] public float focusBelowEyeMeters = 0.75f;
        [Min(0.2f)] public float interactionWidthMeters = 0.75f;
        [Min(0.05f)] public float interactionHeightMeters = 0.12f;

        [Header("Room Trajectory Corridor")]
        [Min(2.4f)] public float preferredCorridorLengthMeters = 5f;
        [Min(2.4f)] public float minimumRoomCorridorLengthMeters = 4f;
        [Min(4f)] public float maximumRoomCorridorLengthMeters = 6f;
        [Min(1.5f)] public float minimumCompactCorridorLengthMeters = 2.4f;
        [Min(2.4f)] public float maximumCompactCorridorLengthMeters = 3.8f;
        [Min(0.2f)] public float roomVehicleLengthMeters = 0.7f;
        [Min(0.2f)] public float compactVehicleLengthMeters = 0.46f;
        [Min(0f)] public float floorSurfaceOffsetMeters = 0.02f;
        [Min(0f)] public float selectedWallMarginMeters = 0.35f;
        [Min(0f)] public float otherWallMarginMeters = 0.25f;

        [Header("Forensic Track Slice")]
        public bool enableForensicTrack = true;
        [Min(1f)] public float forensicRoadWidthInCarWidths = 5.4f;
        [Min(0.05f)] public float forensicKerbWidthInCarWidths = 0.42f;
        [Min(0.05f)] public float forensicRunoffWidthInCarWidths = 0.75f;

        [Header("Trajectory Evidence")]
        [Min(0.25f)] public float observedLeadSeconds = 0.9f;
        [Min(0.1f)] public float observedTailSeconds = 0.45f;
        [Range(10, 60)] public int trajectorySamplesPerSecond = 20;
        [Min(0.05f)] public float temporalTailSeconds = 0.15f;
        [Min(0.05f)] public float evidenceEchoDelaySeconds = 0.12f;
        [Min(0.05f)] public float evidenceTickSeconds = 0.25f;

        [Header("Time Lens")]
        public bool enableTimeLens = true;
        [Min(0.2f)] public float timeLensHandleHeightMeters = 0.75f;
        [Min(0.01f)] public float timeLensHandleRadiusMeters = 0.06f;
        [Min(0.01f)] public float timeLensDeadzoneMeters = 0.007f;
        [Min(0.001f)] public float timeLensVisualSmoothSeconds = 0.035f;
        [Min(0f)] public float timeLensContactDetentMeters = 0.04f;
        [Min(0f)] public float timeLensEndpointDetentMeters = 0.03f;

        [Header("Playback")]
        [Min(0f)] public float leadSeconds = 3f;
        [Min(0f)] public float tailSeconds = 4.5f;
        [Min(0f)] public float forensicHoldSeconds = 2f;
        [Range(0.1f, 1f)] public float slowMotionSpeed = 0.78f;
        [Min(0f)] public float slowMotionLeadSeconds = 0.03f;
        [Min(0f)] public float slowMotionTailSeconds = 0.65f;
        [Min(0.01f)] public float slowMotionBlendSeconds = 0.08f;

        [Header("Crash Motion")]
        [Range(0f, 0.2f)] public float impactHoldSeconds = 0.09f;
        [Min(0.1f)] public float victimSlideDuration = 1.45f;
        [Min(0f)] public float victimForwardSlideInRoadWidths = 1.8f;
        [Min(0f)] public float victimOutwardSlideInRoadWidths = 1.25f;
        [Range(0f, 90f)] public float victimYawDegrees = 32f;
        [Min(0.05f)] public float otherJoltDuration = 0.42f;
        [Min(0f)] public float otherJoltInRoadWidths = 0.14f;
        [Range(0f, 30f)] public float otherYawDegrees = 7f;

        [Header("Contact Sparks")]
        [Range(1, 24)] public int sparkBurstPerCar = 24;
        [Range(0.05f, 0.8f)] public float sparkLifetime = 0.55f;
        [Range(0.005f, 0.12f)]
        public float sparkSizeInCarWidths = 0.07f;
        [Range(0.1f, 4f)]
        public float sparkSpeedInCarLengthsPerSecond = 3f;
        public Color sparkColor =
            new(1.45f, 0.55f, 0.08f, 0.96f);

        [Header("Carbon Debris")]
        [Range(0, 16)] public int debrisCount = 16;
        [Min(0.05f)] public float debrisLifetime = 1.35f;
        [Min(0f)] public float debrisHorizontalSpeedInRoadWidths = 3.2f;
        [Min(0f)] public float debrisVerticalSpeedInRoadWidths = 3.5f;
        [Min(0f)] public float debrisGravityInRoadWidths = 7.5f;
        [Min(0.001f)] public float debrisSizeInRoadWidths = 0.075f;
        public Color debrisColor =
            new(0.045f, 0.05f, 0.06f, 1f);

        [Header("Skid And Smoke")]
        [Min(0.001f)] public float skidWidthInRoadWidths = 0.035f;
        public Color skidColor =
            new(0.025f, 0.025f, 0.028f, 0.92f);
        [Min(0.1f)] public float smokeDuration = 2.2f;
        [Range(1, 40)] public int smokeParticlesPerSecond = 18;
        [Min(0.01f)] public float smokeSizeInRoadWidths = 0.22f;
        public Color smokeColor =
            new(0.22f, 0.23f, 0.25f, 0.72f);

        [Header("Race Control")]
        [Min(0f)] public float incidentYellowSeconds = 4f;

        [Header("Impact Audio")]
        public bool playImpactAudio = true;
        public AudioClip authoredImpactClip;
        [Range(0f, 1f)] public float impactVolume = 0.85f;
        [Range(0f, 1f)] public float impactSpatialBlend = 0.92f;
        [Min(0.05f)] public float impactMinDistance = 0.12f;
        [Min(0.1f)] public float impactMaxDistance = 6f;

        [Header("MR Impact Feedback")]
        public bool playImpactHaptics = true;
        [Range(0f, 1f)] public float primaryHapticAmplitude = 0.55f;
        [Min(0.01f)] public float primaryHapticDuration = 0.08f;
        [Range(0f, 1f)] public float secondaryHapticAmplitude = 0.2f;
        [Min(0.01f)] public float secondaryHapticDuration = 0.05f;
        [Min(0f)] public float secondaryHapticDelaySeconds = 0.09f;
        [Range(0f, 2f)] public float warningWaveIntensity = 1f;
        [Min(0.1f)] public float warningWaveDurationSeconds = 0.62f;

        [Header("Capture")]
        public bool captureProfile = true;

        [Header("Reset")]
        [Min(0.05f)] public float seekResetThresholdSeconds = 0.5f;
    }

    public sealed partial class EventPopoutReplay
    {
        private const int CollisionSkidPointCount = 18;

        [Header("Collision Showcase")]
        public CollisionShowcaseVfxSettings collisionShowcase = new();

        private Transform collisionVfxRoot;
        private Mesh collisionDebrisMesh;
        private Material collisionDebrisMaterial;
        private Transform[] collisionDebris;
        private Vector3[] collisionDebrisVelocities;
        private Vector3[] collisionDebrisSpins;
        private AudioSource collisionAudio;
        private AudioClip collisionImpactClip;
        private LineRenderer[] collisionSkidLines;
        private Material collisionSkidMaterial;
        private ParticleSystem collisionSmoke;
        private Material collisionSmokeMaterial;
        private OvertakeSideBySideVfxSettings collisionSparkSettings;
        private float lastCollisionVfxReplayTime = float.NaN;
        private float collisionResolvedStageScale = 1f;
        private bool collisionPresentationFitted;
        private bool collisionReconstructionResolved;
        private int collisionVictimDriver;
        private int collisionOtherDriver;
        private Vector3 collisionVictimAnchorLocal;
        private Vector3 collisionOtherAnchorLocal;
        private Vector3 collisionForwardLocal = Vector3.forward;
        private Vector3 collisionOutwardLocal = Vector3.right;
        private float collisionVictimHalfWidth;
        private float collisionHitStopRemaining;
        private RaceControlFlagPresenter collisionFlagPresenter;
        private Transform collisionFlagPlacementRoot;
        private bool collisionYellowFlagActive;
        private Coroutine collisionPreloadRoutine;
        private string collisionPreloadKey;
        private bool collisionPreloadReady;
        private string collisionPreparationFailure;
        private float collisionPresentationContactTime;
        private CollisionTrajectoryAnalysis collisionTrajectoryAnalysis;
        private readonly CollisionRoomPlacementResolver
            collisionRoomPlacementResolver = new();
        private CollisionRoomPlacementResult collisionRoomPlacement;
        private ShowcaseLayout collisionShowcaseLayout;
        private bool collisionShowcaseLayoutResolved;
        private Vector3[] collisionVictimMappedForensicPath;
        private Vector3[] collisionOtherMappedForensicPath;
        private const float CollisionFootprintLongitudinalPadding = 0.75f;
        private const float CollisionFootprintLateralPadding = 3.1f;

        private float CollisionPresentationContactTime =>
            collisionPresentationContactTime > 0f
                ? collisionPresentationContactTime
                : currentEvent != null
                    ? currentEvent.anchorTime
                    : 0f;

        public bool HasCollision =>
            FindClosestCollision(
                player != null ? player.Events : null,
                player != null ? player.CurrentTime : 0f,
                player != null
                    ? player.TimelineStartTime
                    : float.NegativeInfinity,
                player != null
                    ? player.ReadyUntilTime
                    : float.PositiveInfinity) != null;

        public bool HasNextCollision =>
            TryFindNextCollision(out _);

        public bool IsCurrentCollision =>
            IsCollisionEvent(currentEvent);

        public bool IsCollisionPreloading =>
            collisionPreloadRoutine != null;

        public bool IsCollisionPreloaded =>
            IsCollisionPrepared;

        public string CollisionPreparationFailure =>
            collisionPreparationFailure;

        public bool UseCollisionCaptureProfile =>
            collisionShowcase == null ||
            collisionShowcase.captureProfile;

        private float CollisionLeadSeconds =>
            collisionShowcase != null
                ? collisionShowcase.leadSeconds
                : eventLeadSeconds;

        private float CollisionTailSeconds =>
            collisionShowcase != null
                ? collisionShowcase.tailSeconds
                : eventTailSeconds;

        public void OpenTestCollision()
        {
            if (player == null || !player.HasDataset)
            {
                Debug.LogWarning(
                    "[EventReplay] Cannot open a collision before the replay dataset is ready.",
                    this);
                return;
            }

            if (FindTestCollisionDefinition() == null)
            {
                Debug.LogWarning(
                    "[EventReplay] No collision event is available for this session.",
                    this);
                return;
            }

            OpenPreparedCollision();
        }

        public void PreloadTestCollision()
        {
            PrepareTestCollision();
        }

        private ReplayEventDto FindTestCollisionDefinition()
        {
            if (player == null || !player.HasDataset)
                return null;

            ReplayEventDto definition =
                FindClosestCollision(
                    player.Events,
                    player.CurrentTime,
                    player.TimelineStartTime,
                    player.ReadyUntilTime);
            return definition ?? FindClosestCollision(
                ReplayEventFixtures.Load(
                    player.Manifest),
                player.CurrentTime,
                player.TimelineStartTime,
                player.ReadyUntilTime);
        }

        private IEnumerator PreloadCollisionRoutine(
            ReplayEventDto definition,
            string key)
        {
            float startedAt =
                Time.realtimeSinceStartup;
            float loadStart = Mathf.Max(
                player.TimelineStartTime,
                definition.startTime - 0.6f);
            float loadEnd = Mathf.Min(
                player.ReadyUntilTime,
                definition.endTime + 0.6f);
            bool loaded = false;
            yield return player.LoadEventRange(
                loadStart,
                loadEnd,
                value => loaded = value);

            bool sameDataset = string.Equals(
                key,
                CreateCollisionPreloadKey(
                    definition),
                StringComparison.Ordinal);
            collisionPreloadReady = loaded &&
                sameDataset;
            collisionPreloadRoutine = null;
            Debug.Log(
                $"[EventReplay] Collision preload " +
                $"ready={collisionPreloadReady}, " +
                $"range={loadStart:0.00}-{loadEnd:0.00}, " +
                $"elapsed={Time.realtimeSinceStartup - startedAt:0.00}s.",
                this);
        }

        private string CreateCollisionPreloadKey(
            ReplayEventDto definition)
        {
            string datasetId = player?.Manifest != null
                ? player.Manifest.datasetId
                : string.Empty;
            int layoutRevision = ResolveCollisionShowcaseLayout() != null
                ? collisionShowcaseLayout.LayoutRevision
                : -1;
            return $"{datasetId}|" +
                $"{definition?.eventId}|" +
                $"{definition?.anchorTime:0.000}|" +
                $"layout:{layoutRevision}";
        }

        private ShowcaseLayout ResolveCollisionShowcaseLayout()
        {
            if (collisionShowcaseLayoutResolved)
                return collisionShowcaseLayout;

            collisionShowcaseLayoutResolved = true;
            collisionShowcaseLayout =
                GetComponent<ShowcaseLayout>() ??
                FindAnyObjectByType<ShowcaseLayout>(
                    FindObjectsInactive.Include);
            return collisionShowcaseLayout;
        }

        public void OpenNextCollision()
        {
            if (isLoading ||
                player == null ||
                !player.HasDataset)
            {
                return;
            }

            if (TryFindNextCollision(
                    out ReplayEventDto definition))
            {
                Open(definition);
            }
        }

        private void ActivateCollisionPresentationStage()
        {
            if (stageRoot == null ||
                !IsCollisionEvent(currentEvent))
            {
                return;
            }

            if (collisionRoomPlacement.IsValid)
            {
                TryApplyRoomStagePlacement(
                    collisionRoomPlacement.StagePose.position,
                    collisionRoomPlacement.StagePose.rotation,
                    collisionResolvedStageScale,
                    ResolveCollisionContactPosition(),
                    0.12f);
                stageRoot.SetActive(true);
                return;
            }

            ResolveStagePose(
                out Vector3 position,
                out Quaternion rotation);
            if (stageAnchor == null && player != null)
            {
                Transform track =
                    player.GetTrackPlacementTransform();
                if (track != null)
                {
                    position = track.position +
                        Vector3.up * stageHeightOffset;
                }
            }

            rotation = Quaternion.Euler(
                0f,
                rotation.eulerAngles.y,
                0f);
            collisionResolvedStageScale =
                collisionPresentationFitted
                    ? Mathf.Max(
                        0.0001f,
                        PresentationRoot.localScale.x)
                    : ResolveInitialCollisionStageScale();
            TrySetPresentationPose(
                position,
                rotation,
                collisionResolvedStageScale);
            Vector3 parentLossyScale =
                PresentationRoot.parent != null
                    ? PresentationRoot.parent.lossyScale
                    : Vector3.one;
            float parentWorldScale = Mathf.Max(
                0.0001f,
                Mathf.Max(
                    Mathf.Abs(parentLossyScale.x),
                    Mathf.Abs(parentLossyScale.z)));
            PlaceCollisionStageForViewer(
                collisionResolvedStageScale * parentWorldScale);
            SetStageInteractionEnabled(false);
            stageRoot.SetActive(true);
        }

        private bool TryRefreshCollisionPlacementForActivation()
        {
            if (!collisionRoomPlacement.IsValid ||
                collisionPreparedDefinition == null)
            {
                return false;
            }

            string currentKey = CreateCollisionPreloadKey(
                collisionPreparedDefinition);
            if (!string.Equals(
                    collisionPreloadKey,
                    currentKey,
                    StringComparison.Ordinal))
            {
                return false;
            }

            CollisionRoomPlacementResult refreshed;
            if (collisionRoomPlacement.Mode ==
                CollisionRoomPlacementMode.ViewerCompact)
            {
                if (!collisionRoomPlacementResolver
                        .TryRefreshViewerCompact(
                            Camera.main,
                            out refreshed))
                {
                    return false;
                }
            }
            else if (!collisionRoomPlacementResolver.TryGetCached(
                         collisionShowcaseLayout,
                         sourceGeometryRevision,
                         out refreshed))
            {
                return false;
            }

            collisionRoomPlacement = refreshed;
            Vector3 parentScale = PresentationRoot != null &&
                                  PresentationRoot.parent != null
                ? PresentationRoot.parent.lossyScale
                : Vector3.one;
            float parentUniformScale = Mathf.Max(
                0.0001f,
                Mathf.Max(
                    Mathf.Abs(parentScale.x),
                    Mathf.Max(
                        Mathf.Abs(parentScale.y),
                        Mathf.Abs(parentScale.z))));
            collisionResolvedStageScale =
                collisionRoomPlacement.UniformScale /
                parentUniformScale;
            return float.IsFinite(collisionResolvedStageScale) &&
                collisionResolvedStageScale > 0f;
        }

        private float ResolveInitialCollisionStageScale()
        {
            collisionShowcase ??=
                new CollisionShowcaseVfxSettings();
            float minimum = Mathf.Max(
                0.1f,
                collisionShowcase.minimumStageScale);
            float maximum = Mathf.Max(
                minimum,
                collisionShowcase.maximumStageScale);
            return Mathf.Clamp(stageScale, minimum, maximum);
        }

        private void FitCollisionPresentationStage()
        {
            if (collisionPresentationFitted ||
                eventCars == null ||
                PresentationRoot == null ||
                currentEvent?.driverNumbers == null)
            {
                return;
            }

            float sourceVehicleLength = 0f;
            float sourceVehicleWidth = 0f;
            foreach (int driver in currentEvent.driverNumbers)
            {
                if (eventCars.TryGetVisualTransform(
                        driver,
                        out Transform visualTransform) &&
                    visualTransform != null &&
                    visualTransform.TryGetComponent(
                        out ReplayCarView car))
                {
                    float visualLength =
                        car.GetVisualLength();
                    if (visualLength > 0.0001f)
                    {
                        sourceVehicleLength =
                            visualLength;
                        sourceVehicleWidth = Mathf.Max(
                            0.0001f,
                            car.GetVisualWidth());
                        break;
                    }
                }
            }

            if (sourceVehicleLength <= 0.0001f)
                return;

            collisionShowcase ??=
                new CollisionShowcaseVfxSettings();
            Vector3 contact = ResolveCollisionContactPosition();
            Vector3 forward = collisionForwardLocal;
            forward.y = 0f;
            if (forward.sqrMagnitude <= 0.000001f)
                forward = Vector3.forward;
            else
                forward.Normalize();
            Vector3[] footprint = BuildCollisionRoomFootprint(
                contact,
                forward,
                sourceVehicleLength,
                sourceVehicleWidth);
            CollisionRoomPlacementContent content = new(
                contact,
                forward,
                footprint,
                sourceVehicleLength);
            CollisionRoomPlacementSettings settings =
                CollisionRoomPlacementSettings.Default;
            settings.FullMinimumLengthMeters = Mathf.Max(
                4f,
                collisionShowcase.minimumRoomCorridorLengthMeters);
            settings.FullPreferredLengthMeters = Mathf.Clamp(
                collisionShowcase.preferredCorridorLengthMeters,
                settings.FullMinimumLengthMeters,
                Mathf.Max(
                    settings.FullMinimumLengthMeters,
                    collisionShowcase.maximumRoomCorridorLengthMeters));
            settings.FullMaximumLengthMeters = Mathf.Max(
                settings.FullPreferredLengthMeters,
                collisionShowcase.maximumRoomCorridorLengthMeters);
            settings.FullMinimumVehicleLengthMeters = 0.55f;
            settings.FullMaximumVehicleLengthMeters = Mathf.Clamp(
                collisionShowcase.roomVehicleLengthMeters,
                0.55f,
                0.7f);
            settings.CompactMinimumLengthMeters = Mathf.Max(
                2.4f,
                collisionShowcase.minimumCompactCorridorLengthMeters);
            settings.CompactMaximumLengthMeters = Mathf.Max(
                settings.CompactMinimumLengthMeters,
                collisionShowcase.maximumCompactCorridorLengthMeters);
            settings.CompactPreferredLengthMeters = Mathf.Clamp(
                3.2f,
                settings.CompactMinimumLengthMeters,
                settings.CompactMaximumLengthMeters);
            settings.CompactMaximumVehicleLengthMeters = Mathf.Clamp(
                collisionShowcase.compactVehicleLengthMeters,
                0.36f,
                0.5f);
            settings.ViewerPreferredVehicleLengthMeters = Mathf.Clamp(
                collisionShowcase.targetVehicleLengthMeters,
                0.42f,
                0.46f);
            settings.ViewerMinimumVehicleLengthMeters = 0.42f;
            settings.ViewerMaximumVehicleLengthMeters = 0.46f;
            settings.ViewerMaximumSpanMeters = Mathf.Min(
                2.8f,
                Mathf.Max(
                    1.2f,
                    collisionShowcase.maximumIslandSpanMeters));
            settings.ViewerForwardDistanceMeters = Mathf.Max(
                0.5f,
                collisionShowcase.focusForwardDistanceMeters);
            settings.ViewerBelowEyeMeters = Mathf.Max(
                0f,
                collisionShowcase.focusBelowEyeMeters);
            settings.FloorOffsetMeters = Mathf.Max(
                0f,
                collisionShowcase.floorSurfaceOffsetMeters);
            settings.EntryExitWallMarginMeters = Mathf.Max(
                0f,
                collisionShowcase.selectedWallMarginMeters);
            settings.SideWallMarginMeters = Mathf.Max(
                0f,
                collisionShowcase.otherWallMarginMeters);

            bool placementReady = collisionRoomPlacementResolver.Prepare(
                ResolveCollisionShowcaseLayout(),
                Camera.main,
                sourceGeometryRevision,
                content,
                settings,
                out collisionRoomPlacement);
            if (!placementReady || !collisionRoomPlacement.IsValid)
            {
                Debug.LogWarning(
                    "[CollisionForensics] No safe room or viewer placement was available.",
                    this);
                return;
            }

            Vector3 parentScale = PresentationRoot.parent != null
                ? PresentationRoot.parent.lossyScale
                : Vector3.one;
            float parentUniformScale = Mathf.Max(
                0.0001f,
                Mathf.Max(
                    Mathf.Abs(parentScale.x),
                    Mathf.Max(
                        Mathf.Abs(parentScale.y),
                        Mathf.Abs(parentScale.z))));
            collisionResolvedStageScale =
                collisionRoomPlacement.UniformScale /
                parentUniformScale;
            if (!TryApplyRoomStagePlacement(
                    collisionRoomPlacement.StagePose.position,
                    collisionRoomPlacement.StagePose.rotation,
                    collisionResolvedStageScale,
                    contact,
                    0.12f))
            {
                collisionRoomPlacement = default;
                return;
            }

            Debug.Log(
                $"[CollisionForensics] placement=" +
                $"{collisionRoomPlacement.Mode}, " +
                $"corridor={collisionRoomPlacement.PhysicalLengthMeters:0.###}m, " +
                $"vehicle={collisionRoomPlacement.TargetVehicleLengthMeters:0.###}m, " +
                $"stageScale={collisionResolvedStageScale:0.###}, " +
                $"fallback={collisionRoomPlacement.RoomFallbackReason}.",
                this);
            collisionPresentationFitted = true;
        }

        private Vector3[] BuildCollisionRoomFootprint(
            Vector3 contact,
            Vector3 forward,
            float sourceVehicleLength,
            float sourceVehicleWidth)
        {
            float targetVehicleLength = Mathf.Clamp(
                collisionShowcase != null
                    ? collisionShowcase.roomVehicleLengthMeters
                    : 0.7f,
                0.55f,
                0.7f);
            float preferredLength = Mathf.Clamp(
                collisionShowcase != null
                    ? collisionShowcase.preferredCorridorLengthMeters
                    : 5f,
                4f,
                6f);
            float localLength = sourceVehicleLength *
                preferredLength / targetVehicleLength;
            Vector3 right = Vector3.Cross(
                Vector3.up,
                forward).normalized;
            float rawMinimumLongitudinal = float.PositiveInfinity;
            float rawMaximumLongitudinal = float.NegativeInfinity;
            AccumulateCollisionForensicBounds(
                collisionVictimMappedForensicPath,
                contact,
                forward,
                right,
                1f,
                ref rawMinimumLongitudinal,
                ref rawMaximumLongitudinal,
                out _,
                out _);
            AccumulateCollisionForensicBounds(
                collisionOtherMappedForensicPath,
                contact,
                forward,
                right,
                1f,
                ref rawMinimumLongitudinal,
                ref rawMaximumLongitudinal,
                out _,
                out _);
            float rawSpan = rawMaximumLongitudinal -
                rawMinimumLongitudinal;
            float railLength = Mathf.Max(
                sourceVehicleLength * 2f,
                localLength -
                sourceVehicleLength *
                CollisionFootprintLongitudinalPadding * 2f);
            float compression = float.IsFinite(rawSpan) &&
                                rawSpan > 0.0001f
                ? railLength / rawSpan
                : 1f;

            float minimumLongitudinal = float.PositiveInfinity;
            float maximumLongitudinal = float.NegativeInfinity;
            float minimumLateral = float.PositiveInfinity;
            float maximumLateral = float.NegativeInfinity;
            AccumulateCollisionForensicBounds(
                collisionVictimMappedForensicPath,
                contact,
                forward,
                right,
                compression,
                ref minimumLongitudinal,
                ref maximumLongitudinal,
                out float victimMinimumLateral,
                out float victimMaximumLateral);
            minimumLateral = Mathf.Min(
                minimumLateral,
                victimMinimumLateral);
            maximumLateral = Mathf.Max(
                maximumLateral,
                victimMaximumLateral);
            AccumulateCollisionForensicBounds(
                collisionOtherMappedForensicPath,
                contact,
                forward,
                right,
                compression,
                ref minimumLongitudinal,
                ref maximumLongitudinal,
                out float otherMinimumLateral,
                out float otherMaximumLateral);
            minimumLateral = Mathf.Min(
                minimumLateral,
                otherMinimumLateral);
            maximumLateral = Mathf.Max(
                maximumLateral,
                otherMaximumLateral);

            if (collisionTrajectoryAnalysis != null &&
                collisionTrajectoryAnalysis.Tier ==
                    CollisionEvidenceTier
                        .ObservedContactRequiresReconstruction)
            {
                AccumulateCollisionReconstructedVehicleBounds(
                    collisionVictimMappedForensicPath,
                    contact,
                    forward,
                    right,
                    collisionOutwardLocal,
                    compression,
                    sourceVehicleLength,
                    sourceVehicleWidth,
                    1f,
                    0.7f,
                    28f,
                    ref minimumLongitudinal,
                    ref maximumLongitudinal,
                    ref minimumLateral,
                    ref maximumLateral);
                AccumulateCollisionReconstructedVehicleBounds(
                    collisionOtherMappedForensicPath,
                    contact,
                    forward,
                    right,
                    -collisionOutwardLocal,
                    compression,
                    sourceVehicleLength,
                    sourceVehicleWidth,
                    0f,
                    0.18f,
                    5f,
                    ref minimumLongitudinal,
                    ref maximumLongitudinal,
                    ref minimumLateral,
                    ref maximumLateral);
            }

            if (!float.IsFinite(minimumLongitudinal) ||
                !float.IsFinite(maximumLongitudinal) ||
                !float.IsFinite(minimumLateral) ||
                !float.IsFinite(maximumLateral))
            {
                minimumLongitudinal = -railLength * (2f / 3f);
                maximumLongitudinal = railLength * (1f / 3f);
                minimumLateral = -sourceVehicleWidth;
                maximumLateral = sourceVehicleWidth;
            }

            minimumLongitudinal -= sourceVehicleLength *
                CollisionFootprintLongitudinalPadding;
            maximumLongitudinal += sourceVehicleLength *
                CollisionFootprintLongitudinalPadding;
            float trackHalfWidth = 0f;
            bool missingSerializedTrackDefaults =
                collisionShowcase != null &&
                collisionShowcase.forensicRoadWidthInCarWidths <= 0f &&
                collisionShowcase.forensicKerbWidthInCarWidths <= 0f &&
                collisionShowcase.forensicRunoffWidthInCarWidths <= 0f;
            if (collisionShowcase == null ||
                collisionShowcase.enableForensicTrack ||
                missingSerializedTrackDefaults)
            {
                float configuredRoadWidth = collisionShowcase != null
                    ? collisionShowcase.forensicRoadWidthInCarWidths
                    : 5.4f;
                float configuredKerbWidth = collisionShowcase != null
                    ? collisionShowcase.forensicKerbWidthInCarWidths
                    : 0.42f;
                float configuredRunoffWidth = collisionShowcase != null
                    ? collisionShowcase.forensicRunoffWidthInCarWidths
                    : 0.75f;
                float roadHalfWidth = (configuredRoadWidth > 0f
                    ? configuredRoadWidth
                    : 5.4f) * 0.5f;
                float kerbWidth = configuredKerbWidth > 0f
                    ? configuredKerbWidth
                    : 0.42f;
                float runoffWidth = configuredRunoffWidth > 0f
                    ? configuredRunoffWidth
                    : 0.75f;
                trackHalfWidth = sourceVehicleWidth *
                    (roadHalfWidth + kerbWidth + runoffWidth);
            }
            float lateralPadding = sourceVehicleWidth *
                CollisionFootprintLateralPadding;
            minimumLateral -= Mathf.Max(lateralPadding, trackHalfWidth);
            maximumLateral += Mathf.Max(lateralPadding, trackHalfWidth);
            return new[]
            {
                contact + forward * minimumLongitudinal +
                    right * minimumLateral,
                contact + forward * minimumLongitudinal +
                    right * maximumLateral,
                contact + forward * maximumLongitudinal +
                    right * maximumLateral,
                contact + forward * maximumLongitudinal +
                    right * minimumLateral
            };
        }

        private static void AccumulateCollisionReconstructedVehicleBounds(
            IReadOnlyList<Vector3> observedPath,
            Vector3 contact,
            Vector3 forward,
            Vector3 right,
            Vector3 lateralDirection,
            float compression,
            float vehicleLength,
            float vehicleWidth,
            float forwardInVehicleLengths,
            float lateralInVehicleWidths,
            float yawDegrees,
            ref float minimumLongitudinal,
            ref float maximumLongitudinal,
            ref float minimumLateral,
            ref float maximumLateral)
        {
            if (observedPath == null || observedPath.Count == 0)
                return;

            Vector3 relative = observedPath[observedPath.Count - 1] -
                contact;
            relative.y = 0f;
            Vector3 flatLateral = lateralDirection;
            flatLateral.y = 0f;
            if (flatLateral.sqrMagnitude <= 0.000001f)
                flatLateral = right;
            else
                flatLateral.Normalize();

            float centerLongitudinal =
                Vector3.Dot(relative, forward) * compression +
                vehicleLength * forwardInVehicleLengths;
            float centerLateral =
                Vector3.Dot(relative, right) * compression +
                Vector3.Dot(flatLateral, right) *
                vehicleWidth * lateralInVehicleWidths;
            float radians = Mathf.Abs(yawDegrees) * Mathf.Deg2Rad;
            float cosine = Mathf.Abs(Mathf.Cos(radians));
            float sine = Mathf.Abs(Mathf.Sin(radians));
            float longitudinalExtent =
                cosine * vehicleLength * 0.5f +
                sine * vehicleWidth * 0.5f;
            float lateralExtent =
                sine * vehicleLength * 0.5f +
                cosine * vehicleWidth * 0.5f;

            minimumLongitudinal = Mathf.Min(
                minimumLongitudinal,
                centerLongitudinal - longitudinalExtent);
            maximumLongitudinal = Mathf.Max(
                maximumLongitudinal,
                centerLongitudinal + longitudinalExtent);
            minimumLateral = Mathf.Min(
                minimumLateral,
                centerLateral - lateralExtent);
            maximumLateral = Mathf.Max(
                maximumLateral,
                centerLateral + lateralExtent);
        }

        private static void AccumulateCollisionForensicBounds(
            IReadOnlyList<Vector3> path,
            Vector3 contact,
            Vector3 forward,
            Vector3 right,
            float scale,
            ref float minimumLongitudinal,
            ref float maximumLongitudinal,
            out float minimumLateral,
            out float maximumLateral)
        {
            minimumLateral = float.PositiveInfinity;
            maximumLateral = float.NegativeInfinity;
            if (path == null)
                return;

            for (int index = 0; index < path.Count; index++)
            {
                Vector3 relative = path[index] - contact;
                relative.y = 0f;
                float longitudinal = Vector3.Dot(
                    relative,
                    forward) * scale;
                float lateral = Vector3.Dot(
                    relative,
                    right) * scale;
                minimumLongitudinal = Mathf.Min(
                    minimumLongitudinal,
                    longitudinal);
                maximumLongitudinal = Mathf.Max(
                    maximumLongitudinal,
                    longitudinal);
                minimumLateral = Mathf.Min(
                    minimumLateral,
                    lateral);
                maximumLateral = Mathf.Max(
                    maximumLateral,
                    lateral);
            }
        }

        private void PlaceCollisionStageForViewer(
            float stageWorldScale)
        {
            if (PresentationRoot == null ||
                stageInteractionCollider == null)
            {
                return;
            }

            Vector3 focusLocal =
                ResolveCollisionContactPosition();
            if (stageAnchor == null && Camera.main != null)
            {
                Transform viewer = Camera.main.transform;
                Vector3 flatForward = Vector3.ProjectOnPlane(
                    viewer.forward,
                    Vector3.up);
                if (flatForward.sqrMagnitude < 0.001f)
                    flatForward = Vector3.forward;
                else
                    flatForward.Normalize();

                Vector3 desiredFocus =
                    viewer.position +
                    flatForward * Mathf.Max(
                        0.5f,
                        collisionShowcase
                            .focusForwardDistanceMeters) -
                    Vector3.up * Mathf.Max(
                        0f,
                        collisionShowcase
                            .focusBelowEyeMeters);
                PresentationRoot.position +=
                    desiredFocus -
                    PresentationRoot.TransformPoint(
                        focusLocal);
            }

            float safeWorldScale = Mathf.Max(
                0.0001f,
                stageWorldScale);
            float localWidth = Mathf.Max(
                    0.2f,
                    collisionShowcase
                        .interactionWidthMeters) /
                safeWorldScale;
            float localHeight = Mathf.Max(
                    0.05f,
                    collisionShowcase
                        .interactionHeightMeters) /
                safeWorldScale;
            stageInteractionCollider.center =
                focusLocal +
                Vector3.up * localHeight * 0.5f;
            stageInteractionCollider.size = new Vector3(
                localWidth,
                localHeight,
                localWidth);
        }

        private void EnsureCollisionShowcase()
        {
            if (!isActive ||
                !IsCollisionEvent(currentEvent) ||
                collisionVfxRoot != null)
            {
                return;
            }

            collisionShowcase ??=
                new CollisionShowcaseVfxSettings();
            if (!collisionShowcase.enabled ||
                PresentationRoot == null ||
                eventCars == null)
            {
                return;
            }

            Vector3 contactPosition =
                ResolveCollisionContactPosition();
            GameObject root =
                new("CollisionShowcaseVfx");
            root.transform.SetParent(
                PresentationRoot,
                false);
            root.transform.localPosition =
                contactPosition +
                Vector3.up * roadWidth * 0.08f;
            root.transform.localRotation =
                Quaternion.identity;
            collisionVfxRoot = root.transform;

            ResolveCollisionReconstruction();
            CreateCollisionSkids();
            CreateCollisionSmoke();
            CreateCollisionDebris();
            CreateCollisionAudio();
            collisionFlagPresenter =
                FindAnyObjectByType<RaceControlFlagPresenter>();
            CreateCollisionRaceControlPlacement();
            collisionSparkSettings =
                CreateCollisionSparkSettings();
            ResetCollisionShowcasePlayback(
                timeline.CurrentTime);
        }

        private Vector3 ResolveCollisionContactPosition()
        {
            float contactTime = CollisionPresentationContactTime;
            int[] drivers = currentEvent != null
                ? currentEvent.driverNumbers
                : null;
            if (drivers != null &&
                drivers.Length >= 2 &&
                TryGetEventLocalVehiclePosition(
                    drivers[0],
                    contactTime,
                    out Vector3 first) &&
                TryGetEventLocalVehiclePosition(
                    drivers[1],
                    contactTime,
                    out Vector3 second))
            {
                return (first + second) * 0.5f;
            }

            return TryGetEventLocalPathPosition(
                    contactTime,
                    out Vector3 pathPosition)
                ? pathPosition
                : Vector3.zero;
        }

        private bool ResolveCollisionReconstruction()
        {
            if (collisionReconstructionResolved)
                return true;

            int[] drivers = currentEvent != null
                ? currentEvent.driverNumbers
                : null;
            float contactTime = CollisionPresentationContactTime;
            if (drivers == null ||
                drivers.Length < 2 ||
                !TryGetEventLocalVehiclePosition(
                    drivers[0],
                    contactTime,
                    out Vector3 first) ||
                !TryGetEventLocalVehiclePosition(
                    drivers[1],
                    contactTime,
                    out Vector3 second))
            {
                return false;
            }

            float sampleOffset = 0.35f;
            Vector3 before = Vector3.zero;
            Vector3 center = Vector3.zero;
            Vector3 after = Vector3.zero;
            bool hasPath =
                TryGetEventLocalPathPosition(
                    contactTime - sampleOffset,
                    out before) &&
                TryGetEventLocalPathPosition(
                    contactTime,
                    out center) &&
                TryGetEventLocalPathPosition(
                    contactTime + sampleOffset,
                    out after);
            if (!hasPath)
            {
                center = (first + second) * 0.5f;
                before = center - Vector3.forward;
                after = center + Vector3.forward;
            }

            Vector3 incoming = center - before;
            Vector3 outgoing = after - center;
            incoming.y = 0f;
            outgoing.y = 0f;
            Vector3 forward = after - before;
            forward.y = 0f;
            collisionForwardLocal =
                forward.sqrMagnitude > 0.000001f
                    ? forward.normalized
                    : Vector3.forward;
            Vector3 right = Vector3.Cross(
                Vector3.up,
                collisionForwardLocal).normalized;
            float turn =
                incoming.sqrMagnitude > 0.000001f &&
                outgoing.sqrMagnitude > 0.000001f
                    ? Vector3.Cross(
                        incoming.normalized,
                        outgoing.normalized).y
                    : 0f;
            if (Mathf.Abs(turn) > 0.0001f)
            {
                collisionOutwardLocal =
                    right * -Mathf.Sign(turn);
            }
            else
            {
                float firstSide = Vector3.Dot(
                    first - center,
                    right);
                float secondSide = Vector3.Dot(
                    second - center,
                    right);
                float widestSide =
                    Mathf.Abs(firstSide) >=
                    Mathf.Abs(secondSide)
                        ? firstSide
                        : secondSide;
                collisionOutwardLocal = right *
                    (Mathf.Abs(widestSide) > 0.0001f
                        ? Mathf.Sign(widestSide)
                        : 1f);
            }

            bool firstIsVictim = Vector3.Dot(
                    first - center,
                    collisionOutwardLocal) >=
                Vector3.Dot(
                    second - center,
                    collisionOutwardLocal);
            collisionVictimDriver = firstIsVictim
                ? drivers[0]
                : drivers[1];
            collisionOtherDriver = firstIsVictim
                ? drivers[1]
                : drivers[0];
            collisionVictimAnchorLocal = firstIsVictim
                ? first
                : second;
            collisionOtherAnchorLocal = firstIsVictim
                ? second
                : first;
            collisionVictimHalfWidth =
                roadWidth * 0.12f;
            if (TryGetCollisionCar(
                    collisionVictimDriver,
                    out ReplayCarView victim))
            {
                collisionVictimHalfWidth = Mathf.Max(
                    collisionVictimHalfWidth,
                    victim.GetVisualWidth() * 0.32f);
            }

            collisionReconstructionResolved = true;
            Debug.Log(
                $"[EventReplay] Collision reconstruction " +
                $"victim={collisionVictimDriver}, " +
                $"other={collisionOtherDriver}, " +
                $"outward={collisionOutwardLocal:F3}.",
                this);
            return true;
        }

        private bool TryGetCollisionCar(
            int driver,
            out ReplayCarView car)
        {
            car = null;
            return eventCars != null &&
                eventCars.TryGetVisualTransform(
                    driver,
                    out Transform visualTransform) &&
                visualTransform != null &&
                visualTransform.TryGetComponent(out car);
        }

        private void CreateCollisionSkids()
        {
            if (!collisionReconstructionResolved ||
                PresentationRoot == null)
            {
                return;
            }

            collisionSkidMaterial =
                ReplayCarVisualUtil.CreateUnlitMaterial(
                    collisionShowcase.skidColor);
            collisionSkidMaterial.name =
                "Runtime_CollisionSkid";
            collisionSkidLines = new LineRenderer[2];
            for (int i = 0; i < 2; i++)
            {
                GameObject skid = new(
                    i == 0
                        ? "LeftTireSkid"
                        : "RightTireSkid");
                skid.transform.SetParent(
                    PresentationRoot,
                    false);
                LineRenderer line =
                    skid.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.alignment = LineAlignment.View;
                line.textureMode = LineTextureMode.Stretch;
                line.numCapVertices = 2;
                line.numCornerVertices = 2;
                line.widthMultiplier = roadWidth *
                    collisionShowcase
                        .skidWidthInRoadWidths;
                line.sharedMaterial =
                    collisionSkidMaterial;
                line.shadowCastingMode =
                    ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.enabled = false;
                collisionSkidLines[i] = line;
            }
        }

        private void CreateCollisionSmoke()
        {
            if (!collisionReconstructionResolved ||
                collisionVfxRoot == null)
            {
                return;
            }

            GameObject smokeObject =
                new("TireSmoke");
            smokeObject.transform.SetParent(
                collisionVfxRoot,
                false);
            smokeObject.transform.localRotation =
                Quaternion.Euler(-90f, 0f, 0f);
            collisionSmoke =
                smokeObject.AddComponent<ParticleSystem>();
            collisionSmoke.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main =
                collisionSmoke.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = Mathf.Max(
                0.1f,
                collisionShowcase.smokeDuration);
            main.startLifetime =
                new ParticleSystem.MinMaxCurve(
                    0.65f,
                    1.25f);
            float smokeSize = roadWidth *
                collisionShowcase
                    .smokeSizeInRoadWidths;
            main.startSize =
                new ParticleSystem.MinMaxCurve(
                    smokeSize * 0.55f,
                    smokeSize);
            main.startSpeed =
                new ParticleSystem.MinMaxCurve(
                    roadWidth * 0.12f,
                    roadWidth * 0.32f);
            main.startColor =
                collisionShowcase.smokeColor;
            main.simulationSpace =
                ParticleSystemSimulationSpace.Local;
            main.scalingMode =
                ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 80;

            ParticleSystem.EmissionModule emission =
                collisionSmoke.emission;
            emission.enabled = true;
            emission.rateOverTime = Mathf.Clamp(
                collisionShowcase
                    .smokeParticlesPerSecond,
                1,
                40);
            ParticleSystem.ShapeModule shape =
                collisionSmoke.shape;
            shape.enabled = true;
            shape.shapeType =
                ParticleSystemShapeType.Cone;
            shape.angle = 18f;
            shape.radius = roadWidth * 0.08f;
            ParticleSystem.NoiseModule noise =
                collisionSmoke.noise;
            noise.enabled = true;
            noise.strength = roadWidth * 0.08f;
            noise.frequency = 0.45f;

            ParticleSystem.ColorOverLifetimeModule color =
                collisionSmoke.colorOverLifetime;
            color.enabled = true;
            Gradient fade = new();
            fade.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.82f, 0.18f),
                    new GradientAlphaKey(0f, 1f)
                });
            color.color = fade;

            ParticleSystemRenderer renderer =
                smokeObject.GetComponent<
                    ParticleSystemRenderer>();
            renderer.renderMode =
                ParticleSystemRenderMode.Billboard;
            renderer.alignment =
                ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode =
                ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            Shader shader = Shader.Find(
                    "Universal Render Pipeline/Particles/Unlit") ??
                Shader.Find("Particles/Standard Unlit") ??
                Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null)
            {
                collisionSmokeMaterial =
                    new Material(shader)
                    {
                        name = "Runtime_CollisionTireSmoke",
                        renderQueue = 3000
                    };
                if (collisionSmokeMaterial.HasProperty(
                        "_BaseColor"))
                {
                    collisionSmokeMaterial.SetColor(
                        "_BaseColor",
                        collisionShowcase.smokeColor);
                }
                if (collisionSmokeMaterial.HasProperty(
                        "_Color"))
                {
                    collisionSmokeMaterial.SetColor(
                        "_Color",
                        collisionShowcase.smokeColor);
                }
                renderer.sharedMaterial =
                    collisionSmokeMaterial;
            }

            collisionSmoke.Stop(
                true,
                ParticleSystemStopBehavior
                    .StopEmittingAndClear);
        }

        private void CreateCollisionRaceControlPlacement()
        {
            if (collisionFlagPresenter == null ||
                !collisionReconstructionResolved ||
                PresentationRoot == null)
            {
                return;
            }

            GameObject placementObject =
                new("CollisionRaceControlRoot");
            Transform placement =
                placementObject.transform;
            placement.SetParent(
                PresentationRoot,
                false);
            placement.localPosition =
                collisionVictimAnchorLocal +
                collisionOutwardLocal *
                roadWidth * 1.75f -
                collisionForwardLocal *
                roadWidth * 0.35f;
            placement.localRotation =
                Quaternion.LookRotation(
                    -collisionOutwardLocal,
                    Vector3.up);
            float presentationScale =
                Mathf.Max(
                    0.0001f,
                    PresentationRoot.lossyScale.x);
            placement.localScale =
                Vector3.one / presentationScale;
            collisionFlagPlacementRoot = placement;
            collisionFlagPresenter
                .SetIncidentPresentationRoot(placement);
        }

        private void CreateCollisionDebris()
        {
            int count = Mathf.Clamp(
                collisionShowcase.debrisCount,
                0,
                16);
            collisionDebris = new Transform[count];
            collisionDebrisVelocities =
                new Vector3[count];
            collisionDebrisSpins =
                new Vector3[count];
            if (count == 0)
                return;

            collisionDebrisMesh =
                CreateCollisionDebrisMesh();
            collisionDebrisMaterial =
                ReplayCarVisualUtil.CreateUnlitMaterial(
                    collisionShowcase.debrisColor);
            collisionDebrisMaterial.name =
                "Runtime_CollisionCarbonDebris";

            for (int i = 0; i < count; i++)
            {
                GameObject shard =
                    new($"CarbonShard_{i:00}");
                shard.transform.SetParent(
                    collisionVfxRoot,
                    false);
                shard.AddComponent<MeshFilter>()
                    .sharedMesh = collisionDebrisMesh;
                MeshRenderer renderer =
                    shard.AddComponent<MeshRenderer>();
                renderer.sharedMaterial =
                    collisionDebrisMaterial;
                renderer.shadowCastingMode =
                    ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.motionVectorGenerationMode =
                    MotionVectorGenerationMode.ForceNoMotion;
                renderer.lightProbeUsage =
                    LightProbeUsage.Off;
                renderer.reflectionProbeUsage =
                    ReflectionProbeUsage.Off;

                float sizeVariation =
                    0.72f + (i % 5) * 0.11f;
                shard.transform.localScale =
                    Vector3.one *
                    roadWidth *
                    collisionShowcase
                        .debrisSizeInRoadWidths *
                    sizeVariation;
                collisionDebris[i] = shard.transform;

                float angle =
                    (i * 137.508f + 19f) *
                    Mathf.Deg2Rad;
                float speedVariation =
                    0.72f + (i % 4) * 0.13f;
                float horizontalSpeed =
                    roadWidth *
                    collisionShowcase
                        .debrisHorizontalSpeedInRoadWidths *
                    speedVariation;
                collisionDebrisVelocities[i] =
                    new Vector3(
                        Mathf.Cos(angle) * horizontalSpeed,
                        roadWidth *
                        collisionShowcase
                            .debrisVerticalSpeedInRoadWidths *
                        (0.78f + (i % 3) * 0.16f),
                        Mathf.Sin(angle) * horizontalSpeed);
                collisionDebrisSpins[i] =
                    new Vector3(
                        210f + i * 17f,
                        -260f + i * 29f,
                        145f + i * 23f);
                shard.SetActive(false);
            }
        }

        private void CreateCollisionAudio()
        {
            collisionAudio =
                collisionVfxRoot.gameObject
                    .AddComponent<AudioSource>();
            collisionAudio.playOnAwake = false;
            collisionAudio.loop = false;
            collisionAudio.spatialBlend =
                collisionShowcase.impactSpatialBlend;
            collisionAudio.volume =
                collisionShowcase.impactVolume;
            collisionAudio.dopplerLevel = 0f;
            collisionAudio.rolloffMode =
                AudioRolloffMode.Linear;
            collisionAudio.minDistance =
                collisionShowcase.impactMinDistance;
            collisionAudio.maxDistance = Mathf.Max(
                collisionAudio.minDistance + 0.01f,
                collisionShowcase.impactMaxDistance);
            collisionImpactClip =
                CreateCollisionImpactClip();
            collisionAudio.clip = collisionImpactClip;
        }

        private void UpdateCollisionShowcase(float replayTime)
        {
            if (collisionVfxRoot == null ||
                !IsCollisionEvent(currentEvent))
            {
                return;
            }

            bool hasPrevious =
                !float.IsNaN(lastCollisionVfxReplayTime);
            float resetThreshold = Mathf.Max(
                0.05f,
                collisionShowcase
                    .seekResetThresholdSeconds);
            bool discontinuity = hasPrevious &&
                (replayTime < lastCollisionVfxReplayTime ||
                 replayTime - lastCollisionVfxReplayTime >
                 resetThreshold);
            if (!hasPrevious || discontinuity)
            {
                ResetCollisionShowcasePlayback(replayTime);
            }
            else if (
                lastCollisionVfxReplayTime <
                    currentEvent.anchorTime &&
                replayTime >= currentEvent.anchorTime)
            {
                TriggerCollisionImpact();
            }

            UpdateCollisionReconstruction(replayTime);
            UpdateCollisionDebris(replayTime);
            lastCollisionVfxReplayTime = replayTime;
        }

        private void TriggerCollisionImpact()
        {
            if (collisionShowcase.playImpactAudio &&
                collisionAudio != null &&
                collisionImpactClip != null)
            {
                collisionAudio.Stop();
                collisionAudio.Play();
            }

            collisionHitStopRemaining = Mathf.Max(
                0f,
                collisionShowcase.impactHoldSeconds);
            SetCollisionYellowFlag(true);
        }

        private void UpdateCollisionReconstruction(
            float replayTime)
        {
            if (!ResolveCollisionReconstruction())
                return;

            float age = replayTime -
                currentEvent.anchorTime;
            bool hasVictim = TryGetCollisionCar(
                collisionVictimDriver,
                out ReplayCarView victim);
            bool hasOther = TryGetCollisionCar(
                collisionOtherDriver,
                out ReplayCarView other);
            if (age < 0f)
            {
                victim?.ResetVisualMotion();
                other?.ResetVisualMotion();
                SetCollisionSkidsVisible(false);
                StopCollisionSmoke(true);
                SetCollisionYellowFlag(false);
                return;
            }

            float duration = Mathf.Max(
                0.1f,
                collisionShowcase
                    .victimSlideDuration);
            float progress = Mathf.Clamp01(
                age / duration);
            float eased = Mathf.SmoothStep(
                0f,
                1f,
                progress);
            float stopEase =
                1f - Mathf.Pow(1f - progress, 3f);
            Vector3 victimLocal =
                EvaluateCollisionVictimLocal(
                    progress,
                    eased,
                    stopEase);
            float yawSign = Mathf.Sign(
                Vector3.Dot(
                    collisionOutwardLocal,
                    Vector3.Cross(
                        Vector3.up,
                        collisionForwardLocal)));
            if (Mathf.Approximately(yawSign, 0f))
                yawSign = 1f;
            float victimYaw = yawSign *
                collisionShowcase
                    .victimYawDegrees *
                eased;
            if (hasVictim)
            {
                Vector3 targetWorld =
                    PresentationRoot.TransformPoint(
                        victimLocal);
                victim.ApplyVisualMotion(
                    targetWorld -
                    victim.LogicalRoot.position,
                    victimYaw);
            }

            float joltDuration = Mathf.Max(
                0.05f,
                collisionShowcase
                    .otherJoltDuration);
            if (hasOther && age <= joltDuration)
            {
                float joltProgress = Mathf.Clamp01(
                    age / joltDuration);
                float jolt = Mathf.Sin(
                    joltProgress * Mathf.PI) *
                    (1f - joltProgress * 0.35f);
                Vector3 joltWorld =
                    PresentationRoot.TransformVector(
                        -collisionOutwardLocal *
                        roadWidth *
                        collisionShowcase
                            .otherJoltInRoadWidths *
                        jolt);
                other.ApplyVisualMotion(
                    joltWorld,
                    -yawSign *
                    collisionShowcase
                        .otherYawDegrees *
                    jolt);
            }
            else
            {
                other?.ResetVisualMotion();
            }

            UpdateCollisionSkids(
                progress,
                yawSign);
            UpdateCollisionSmoke(
                age,
                victimLocal);
            SetCollisionYellowFlag(
                age <= Mathf.Max(
                    0f,
                    collisionShowcase
                        .incidentYellowSeconds));
        }

        private Vector3 EvaluateCollisionVictimLocal(
            float progress,
            float eased,
            float stopEase)
        {
            return collisionVictimAnchorLocal +
                collisionForwardLocal *
                roadWidth *
                collisionShowcase
                    .victimForwardSlideInRoadWidths *
                stopEase +
                collisionOutwardLocal *
                roadWidth *
                collisionShowcase
                    .victimOutwardSlideInRoadWidths *
                eased +
                Vector3.up * roadWidth * 0.015f;
        }

        private Vector3 EvaluateCollisionVictimLocal(
            float progress)
        {
            float eased = Mathf.SmoothStep(
                0f,
                1f,
                progress);
            float stopEase =
                1f - Mathf.Pow(1f - progress, 3f);
            return EvaluateCollisionVictimLocal(
                progress,
                eased,
                stopEase);
        }

        private void UpdateCollisionSkids(
            float progress,
            float yawSign)
        {
            if (collisionSkidLines == null ||
                progress <= 0.015f)
            {
                SetCollisionSkidsVisible(false);
                return;
            }

            int pointCount = Mathf.Clamp(
                Mathf.CeilToInt(
                    progress *
                    (CollisionSkidPointCount - 1)) + 1,
                2,
                CollisionSkidPointCount);
            for (int lineIndex = 0;
                 lineIndex < collisionSkidLines.Length;
                 lineIndex++)
            {
                LineRenderer line =
                    collisionSkidLines[lineIndex];
                if (line == null)
                    continue;

                line.enabled = true;
                line.positionCount = pointCount;
                float tireSide = lineIndex == 0
                    ? -1f
                    : 1f;
                for (int point = 0;
                     point < pointCount;
                     point++)
                {
                    float pointProgress = progress *
                        point /
                        (pointCount - 1f);
                    Vector3 center =
                        EvaluateCollisionVictimLocal(
                            pointProgress);
                    float pointYaw = yawSign *
                        collisionShowcase
                            .victimYawDegrees *
                        Mathf.SmoothStep(
                            0f,
                            1f,
                            pointProgress);
                    Vector3 tireRight =
                        Quaternion.AngleAxis(
                            pointYaw,
                            Vector3.up) *
                        Vector3.Cross(
                            Vector3.up,
                            collisionForwardLocal)
                            .normalized;
                    line.SetPosition(
                        point,
                        center +
                        tireRight *
                        collisionVictimHalfWidth *
                        tireSide);
                }
            }
        }

        private void SetCollisionSkidsVisible(
            bool visible)
        {
            if (collisionSkidLines == null)
                return;

            foreach (LineRenderer line in collisionSkidLines)
            {
                if (line != null)
                    line.enabled = visible;
            }
        }

        private void UpdateCollisionSmoke(
            float age,
            Vector3 victimLocal)
        {
            if (collisionSmoke == null ||
                collisionVfxRoot == null)
            {
                return;
            }

            collisionSmoke.transform.localPosition =
                victimLocal -
                collisionVfxRoot.localPosition;
            float duration = Mathf.Max(
                0.1f,
                collisionShowcase.smokeDuration);
            if (age <= duration)
            {
                if (!collisionSmoke.isPlaying)
                    collisionSmoke.Play();
            }
            else if (collisionSmoke.isEmitting)
            {
                collisionSmoke.Stop(
                    true,
                    ParticleSystemStopBehavior
                        .StopEmitting);
            }
        }

        private void StopCollisionSmoke(bool clear)
        {
            if (collisionSmoke == null)
                return;

            collisionSmoke.Stop(
                true,
                clear
                    ? ParticleSystemStopBehavior
                        .StopEmittingAndClear
                    : ParticleSystemStopBehavior
                        .StopEmitting);
        }

        private void SetCollisionYellowFlag(bool active)
        {
            if (collisionYellowFlagActive == active)
                return;

            collisionYellowFlagActive = active;
            if (collisionFlagPresenter == null)
            {
                collisionFlagPresenter =
                    FindAnyObjectByType<
                        RaceControlFlagPresenter>();
            }

            collisionFlagPresenter?
                .SetIncidentYellowOverride(active);
        }

        private bool UpdateCollisionHitStop(
            float unscaledDeltaTime)
        {
            if (collisionHitStopRemaining <= 0f)
                return false;

            collisionHitStopRemaining = Mathf.Max(
                0f,
                collisionHitStopRemaining -
                Mathf.Max(0f, unscaledDeltaTime));
            return true;
        }

        private void UpdateCollisionDebris(float replayTime)
        {
            if (collisionDebris == null)
                return;

            float age = replayTime -
                currentEvent.anchorTime;
            float duration = Mathf.Max(
                0.05f,
                collisionShowcase.debrisLifetime);
            bool visible = age >= 0f && age <= duration;
            float gravity =
                roadWidth *
                collisionShowcase
                    .debrisGravityInRoadWidths;
            for (int i = 0;
                 i < collisionDebris.Length;
                 i++)
            {
                Transform shard = collisionDebris[i];
                if (shard == null)
                    continue;

                shard.gameObject.SetActive(visible);
                if (!visible)
                    continue;

                shard.localPosition =
                    collisionDebrisVelocities[i] * age +
                    Vector3.down *
                    (0.5f * gravity * age * age);
                shard.localRotation =
                    Quaternion.Euler(
                        collisionDebrisSpins[i] * age);
            }
        }

        private void ResetCollisionShowcasePlayback(
            float replayTime)
        {
            lastCollisionVfxReplayTime = replayTime;
            if (collisionAudio != null)
                collisionAudio.Stop();

            int[] drivers = currentEvent != null
                ? currentEvent.driverNumbers
                : null;
            if (eventCars != null &&
                drivers != null &&
                drivers.Length >= 2)
            {
                eventCars.ResetCollisionContactVfx(
                    drivers[0],
                    drivers[1]);
            }

            collisionHitStopRemaining = 0f;
            UpdateCollisionReconstruction(replayTime);
            UpdateCollisionDebris(replayTime);
        }

        private float ResolveCollisionPlaybackSpeedMultiplier(
            float replayTime)
        {
            if (!IsCollisionEvent(currentEvent) ||
                collisionShowcase == null ||
                !collisionShowcase.enabled)
            {
                return 1f;
            }

            float blend = Mathf.Max(
                0.01f,
                collisionShowcase
                    .slowMotionBlendSeconds);
            float start = currentEvent.anchorTime -
                Mathf.Max(
                    0f,
                    collisionShowcase
                        .slowMotionLeadSeconds);
            float end = currentEvent.anchorTime +
                Mathf.Max(
                    0f,
                    collisionShowcase
                        .slowMotionTailSeconds);
            float enter = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    start - blend,
                    start,
                    replayTime));
            float exit = 1f - Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    end,
                    end + blend,
                    replayTime));
            float weight = Mathf.Min(enter, exit);
            return Mathf.Lerp(
                1f,
                Mathf.Clamp(
                    collisionShowcase
                        .slowMotionSpeed,
                    0.1f,
                    1f),
                weight);
        }

        private void DestroyCollisionShowcase()
        {
            if (collisionAudio != null)
                collisionAudio.Stop();

            SetCollisionYellowFlag(false);
            collisionFlagPresenter?
                .SetIncidentPresentationRoot(null);
            StopCollisionSmoke(true);
            if (TryGetCollisionCar(
                    collisionVictimDriver,
                    out ReplayCarView victim))
            {
                victim.ResetVisualMotion();
            }
            if (TryGetCollisionCar(
                    collisionOtherDriver,
                    out ReplayCarView other))
            {
                other.ResetVisualMotion();
            }

            int[] drivers = currentEvent != null
                ? currentEvent.driverNumbers
                : null;
            if (eventCars != null &&
                drivers != null &&
                drivers.Length >= 2)
            {
                eventCars.ResetCollisionContactVfx(
                    drivers[0],
                    drivers[1]);
            }

            if (collisionVfxRoot != null)
                Destroy(collisionVfxRoot.gameObject);
            if (collisionFlagPlacementRoot != null)
                Destroy(collisionFlagPlacementRoot.gameObject);
            if (collisionDebrisMesh != null)
                Destroy(collisionDebrisMesh);
            if (collisionDebrisMaterial != null)
                Destroy(collisionDebrisMaterial);
            if (collisionSkidMaterial != null)
                Destroy(collisionSkidMaterial);
            if (collisionSmokeMaterial != null)
                Destroy(collisionSmokeMaterial);
            if (collisionImpactClip != null)
                Destroy(collisionImpactClip);

            collisionVfxRoot = null;
            collisionDebrisMesh = null;
            collisionDebrisMaterial = null;
            collisionDebris = null;
            collisionDebrisVelocities = null;
            collisionDebrisSpins = null;
            collisionAudio = null;
            collisionImpactClip = null;
            collisionSkidLines = null;
            collisionSkidMaterial = null;
            collisionSmoke = null;
            collisionSmokeMaterial = null;
            collisionSparkSettings = null;
            lastCollisionVfxReplayTime = float.NaN;
            collisionResolvedStageScale = 1f;
            collisionPresentationFitted = false;
            collisionReconstructionResolved = false;
            collisionVictimDriver = 0;
            collisionOtherDriver = 0;
            collisionVictimAnchorLocal = Vector3.zero;
            collisionOtherAnchorLocal = Vector3.zero;
            collisionForwardLocal = Vector3.forward;
            collisionOutwardLocal = Vector3.right;
            collisionVictimHalfWidth = 0f;
            collisionHitStopRemaining = 0f;
            collisionFlagPresenter = null;
            collisionFlagPlacementRoot = null;
            collisionYellowFlagActive = false;
        }

        private OvertakeSideBySideVfxSettings
            CreateCollisionSparkSettings()
        {
            return new OvertakeSideBySideVfxSettings
            {
                enabled = true,
                sparkBurstCount = Mathf.Clamp(
                    collisionShowcase
                        .sparkBurstPerCar,
                    1,
                    24),
                sparkLifetime =
                    collisionShowcase.sparkLifetime,
                sparkSizeInCarWidths =
                    collisionShowcase
                        .sparkSizeInCarWidths,
                sparkSpeedInCarLengthsPerSecond =
                    collisionShowcase
                        .sparkSpeedInCarLengthsPerSecond,
                sparkEmissionColor =
                    collisionShowcase.sparkColor,
                sparkRearOffsetInCarLengths = 0.08f,
                sparkFloorOffsetInCarHeights = 0.12f
            };
        }

        private static Mesh CreateCollisionDebrisMesh()
        {
            Mesh mesh = new()
            {
                name = "Runtime_CollisionCarbonShard",
                vertices = new[]
                {
                    new Vector3(-0.55f, -0.12f, -0.3f),
                    new Vector3(0.62f, -0.08f, -0.2f),
                    new Vector3(-0.12f, 0.18f, 0.72f),
                    new Vector3(0.08f, 0.26f, -0.05f)
                },
                triangles = new[]
                {
                    0, 1, 2,
                    0, 3, 1,
                    1, 3, 2,
                    2, 3, 0
                }
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static AudioClip CreateCollisionImpactClip()
        {
            const int sampleRate = 24000;
            const float duration = 0.52f;
            int sampleCount =
                Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[sampleCount];
            var random = new System.Random(19780407);
            for (int i = 0; i < sampleCount; i++)
            {
                float time = i / (float)sampleRate;
                float noiseEnvelope =
                    Mathf.Exp(-time * 17f);
                float metalEnvelope =
                    Mathf.Exp(-time * 8.5f);
                float thumpEnvelope =
                    Mathf.Exp(-time * 12f);
                float noise =
                    (float)(random.NextDouble() * 2.0 - 1.0) *
                    noiseEnvelope * 0.62f;
                float thump = Mathf.Sin(
                    Mathf.PI * 2f *
                    (92f - time * 48f) * time) *
                    thumpEnvelope * 0.72f;
                float metal =
                    (Mathf.Sin(
                         Mathf.PI * 2f * 760f * time) *
                     0.24f +
                     Mathf.Sin(
                         Mathf.PI * 2f * 1280f * time) *
                     0.13f) *
                    metalEnvelope;
                samples[i] = Mathf.Clamp(
                    noise + thump + metal,
                    -1f,
                    1f);
            }

            AudioClip clip = AudioClip.Create(
                "Runtime_CollisionImpact",
                sampleCount,
                1,
                sampleRate,
                false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static bool IsCollisionEvent(
            ReplayEventDto replayEvent)
        {
            return replayEvent != null &&
                (string.Equals(
                     replayEvent.eventType,
                     "Collision",
                     StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(
                     replayEvent.eventType,
                     "Contact",
                     StringComparison.OrdinalIgnoreCase));
        }

        private static ReplayEventDto FindClosestCollision(
            ReplayEventDto[] events,
            float time,
            float minimumAnchorTime =
                float.NegativeInfinity,
            float maximumAnchorTime =
                float.PositiveInfinity)
        {
            if (events == null)
                return null;

            ReplayEventDto closest = null;
            float closestDistance =
                float.PositiveInfinity;
            for (int i = 0; i < events.Length; i++)
            {
                ReplayEventDto candidate = events[i];
                if (!IsCollisionEvent(candidate) ||
                    candidate.anchorTime < minimumAnchorTime ||
                    candidate.anchorTime > maximumAnchorTime)
                    continue;

                float distance = Mathf.Abs(
                    candidate.anchorTime - time);
                if (distance < closestDistance ||
                    Mathf.Approximately(
                        distance,
                        closestDistance) &&
                    string.CompareOrdinal(
                        candidate.eventId,
                        closest?.eventId) < 0)
                {
                    closest = candidate;
                    closestDistance = distance;
                }
            }

            return closest;
        }

        private bool TryFindNextCollision(
            out ReplayEventDto next)
        {
            next = null;
            ReplayEventDto[] events =
                player != null ? player.Events : null;
            if (events == null || events.Length == 0)
                return false;

            float currentAnchor = currentEvent != null
                ? currentEvent.anchorTime
                : player.CurrentTime;
            string currentId = currentEvent != null
                ? currentEvent.eventId
                : string.Empty;
            for (int i = 0; i < events.Length; i++)
            {
                ReplayEventDto candidate = events[i];
                if (!IsCollisionEvent(candidate) ||
                    candidate.anchorTime <
                        player.TimelineStartTime ||
                    candidate.anchorTime >
                        player.ReadyUntilTime)
                    continue;

                bool followsCurrent =
                    candidate.anchorTime >
                        currentAnchor + 0.0001f ||
                    Mathf.Approximately(
                        candidate.anchorTime,
                        currentAnchor) &&
                    string.CompareOrdinal(
                        candidate.eventId,
                        currentId) > 0;
                if (!followsCurrent)
                    continue;

                if (next == null ||
                    candidate.anchorTime < next.anchorTime ||
                    Mathf.Approximately(
                        candidate.anchorTime,
                        next.anchorTime) &&
                    string.CompareOrdinal(
                        candidate.eventId,
                        next.eventId) < 0)
                {
                    next = candidate;
                }
            }

            return next != null;
        }
    }
}

namespace F1XR.RestAPI.Replay
{
    // Kept as a partial so the prepared incident cache stays isolated from the regular replay path.
    public sealed partial class EventPopoutReplay
    {
        private CollisionIncidentPresentation
            collisionIncidentPresentation;
        private ReplayEventDto collisionPreparedDefinition;
        private Transform collisionIslandRoot;
        private Coroutine collisionFirstFrameRoutine;

        public bool IsCollisionPrepared
        {
            get
            {
                string currentKey = collisionPreparedDefinition != null
                    ? CreateCollisionPreloadKey(
                        collisionPreparedDefinition)
                    : string.Empty;
                return collisionPreloadReady &&
                    collisionPreparedDefinition != null &&
                    collisionIncidentPresentation != null &&
                    stageRoot != null &&
                    !string.IsNullOrWhiteSpace(currentKey) &&
                    string.Equals(
                        collisionPreloadKey,
                        currentKey,
                        StringComparison.Ordinal);
            }
        }

        public bool IsCollisionRevealComplete =>
            isActive &&
            IsCurrentCollision &&
            collisionIncidentPresentation != null &&
            collisionIncidentPresentation.RevealComplete;

        public CollisionPresentationPhase CollisionPhase =>
            collisionIncidentPresentation != null &&
            isActive &&
            IsCurrentCollision
                ? collisionIncidentPresentation.Phase
                : CollisionPresentationPhase.Preparing;

        public bool IsCollisionImpactReplaying =>
            isActive &&
            IsCurrentCollision &&
            collisionIncidentPresentation != null &&
            collisionIncidentPresentation.ImpactReplaying;

        public bool IsCollisionTimeLensAvailable =>
            isActive &&
            IsCurrentCollision &&
            collisionIncidentPresentation != null &&
            collisionIncidentPresentation.IsTimeLensAvailable;

        public bool IsCollisionTimeLensGrabbed =>
            IsCollisionTimeLensAvailable &&
            collisionIncidentPresentation.IsTimeLensGrabbed;

        public float CollisionTimeLensNormalized =>
            collisionIncidentPresentation != null
                ? collisionIncidentPresentation.TimeLensNormalized
                : 1f;

        public float CollisionTimeLensTimeSeconds =>
            collisionIncidentPresentation != null
                ? collisionIncidentPresentation.TimeLensTimeSeconds
                : CollisionPresentationContactTime;

        public string CollisionTimeLensStatus =>
            collisionIncidentPresentation != null
                ? collisionIncidentPresentation.TimeLensStatus
                : string.Empty;

        public void SetCollisionTimeLensNormalized(float value)
        {
            if (!IsCollisionTimeLensAvailable)
                return;

            collisionIncidentPresentation.SetTimeLensNormalized(value);
        }

        public void NotifyDatasetChanged()
        {
            CancelCollisionPreparation();
            RestoreTableTrackRendering();
            hasSnapshot = false;
            collisionPreloadReady = false;
            collisionPreloadKey = null;
            collisionPreparedDefinition = null;
            collisionPreparationFailure = null;
            if (collisionIncidentPresentation != null ||
                IsCollisionEvent(currentEvent))
            {
                DestroyStage(false);
            }

            PrepareTestCollision();
        }

        public void PrepareTestCollision()
        {
            if (player == null ||
                !player.HasDataset ||
                isActive ||
                isLoading ||
                collisionPreloadRoutine != null)
            {
                return;
            }

            ReplayEventDto source = FindTestCollisionDefinition();
            if (source == null)
                return;

            ReplayEventDto definition =
                CreatePresentationEvent(source);
            string key = CreateCollisionPreloadKey(definition);
            if (IsCollisionPrepared &&
                string.Equals(
                    collisionPreloadKey,
                    key,
                    StringComparison.Ordinal))
            {
                return;
            }

            collisionPreloadReady = false;
            collisionPreloadKey = key;
            collisionPreparedDefinition = null;
            collisionPreparationFailure = null;
            collisionPreloadRoutine = StartCoroutine(
                PrepareCollisionIncidentRoutine(
                    definition,
                    key));
        }

        public void ReplayCollisionImpact()
        {
            if (!IsCollisionRevealComplete ||
                IsCollisionTimeLensGrabbed ||
                collisionIncidentPresentation == null)
            {
                return;
            }

            collisionIncidentPresentation.ReplayImpact();
        }

        public void RestartCollisionReveal()
        {
            if (!isActive ||
                !IsCurrentCollision ||
                IsCollisionTimeLensGrabbed ||
                collisionIncidentPresentation == null)
            {
                return;
            }

            timeline.SetTime(CollisionPresentationContactTime);
            timeline.Pause();
            ResetIndices();
            collisionIncidentPresentation.RestartReveal();
        }

        private IEnumerator PrepareCollisionIncidentRoutine(
            ReplayEventDto definition,
            string key)
        {
            float startedAt = Time.realtimeSinceStartup;
            DestroyStage(false);
            collisionTrajectoryAnalysis = null;
            collisionPresentationContactTime = 0f;
            collisionRoomPlacement = default;
            collisionRoomPlacementResolver.Invalidate();
            currentEvent = definition;

            float loadStart = Mathf.Max(
                player.TimelineStartTime,
                definition.startTime - 0.6f);
            float loadEnd = Mathf.Min(
                player.ReadyUntilTime,
                definition.endTime + 0.6f);
            bool loaded = false;
            yield return player.LoadEventRange(
                loadStart,
                loadEnd,
                value => loaded = value);

            if (!IsCollisionPreparationCurrent(key) || !loaded)
            {
                FailCollisionPreparation(
                    key,
                    "data range was unavailable");
                yield break;
            }

            if (!BuildCollisionSourceSnapshot(
                    definition,
                    loadStart,
                    loadEnd))
            {
                FailCollisionPreparation(
                    key,
                    "vehicle samples were unavailable");
                yield break;
            }

            if (!TryBuildCollisionTrajectoryAnalysis(definition))
            {
                FailCollisionPreparation(
                    key,
                    "the incident trajectory could not be resolved");
                yield break;
            }

            // Keep data, geometry and visual allocation on separate frames.
            yield return null;
            if (!IsCollisionPreparationCurrent(key) ||
                !BuildStage(definition, loadStart, loadEnd))
            {
                FailCollisionPreparation(
                    key,
                    "the incident island could not be built");
                yield break;
            }

            yield return null;
            timeline.Reset(
                showcasePlaybackWindow.StartTime,
                showcasePlaybackWindow.EndTime);
            timeline.SetTime(CollisionPresentationContactTime);
            timeline.Pause();
            isActive = true;
            ResetIndices();
            ShowCollisionCars(CollisionPresentationContactTime);
            if (!ResolveCollisionReconstruction() ||
                !TryGetCollisionCar(
                    collisionVictimDriver,
                    out ReplayCarView victimCar) ||
                !TryGetCollisionCar(
                    collisionOtherDriver,
                    out ReplayCarView otherCar))
            {
                isActive = false;
                FailCollisionPreparation(
                    key,
                    "the collision vehicle pair could not be resolved");
                yield break;
            }
            if (!TryBuildMappedCollisionForensicPath(
                    collisionVictimDriver,
                    out collisionVictimMappedForensicPath) ||
                !TryBuildMappedCollisionForensicPath(
                    collisionOtherDriver,
                    out collisionOtherMappedForensicPath))
            {
                isActive = false;
                FailCollisionPreparation(
                    key,
                    "the calibrated incident trajectory could not be mapped");
                yield break;
            }
            FitCollisionPresentationStage();
            if (!collisionPresentationFitted ||
                !collisionRoomPlacement.IsValid)
            {
                isActive = false;
                FailCollisionPreparation(
                    key,
                    "no safe collision presentation placement was available");
                yield break;
            }

            float vehicleLength = Mathf.Max(
                0.001f,
                victimCar.GetVisualLength());
            float vehicleWidth = Mathf.Max(
                0.001f,
                victimCar.GetVisualWidth());
            Vector3 contact = ResolveCollisionContactPosition();
            Vector3[] victimIncoming =
                BuildCollisionIncomingPath(
                    collisionVictimDriver,
                    CollisionPresentationContactTime);
            Vector3[] otherIncoming =
                BuildCollisionIncomingPath(
                    collisionOtherDriver,
                    CollisionPresentationContactTime);
            string label =
                $"{eventCars.GetDriverLabel(collisionVictimDriver)} / " +
                eventCars.GetDriverLabel(collisionOtherDriver);
            string victimLabel = eventCars.GetDriverLabel(
                collisionVictimDriver);
            string otherLabel = eventCars.GetDriverLabel(
                collisionOtherDriver);
            string reportedTime = FormatCollisionIncidentTime(
                definition.anchorTime);
            string observedTime = FormatCollisionIncidentTime(
                CollisionPresentationContactTime);
            string incidentMetadata = definition.lapNumber > 0
                ? $"L{definition.lapNumber}  REPORTED {reportedTime}  |  " +
                  $"OBSERVED {observedTime}"
                : $"REPORTED {reportedTime}  |  OBSERVED {observedTime}";

            collisionIncidentPresentation =
                new CollisionIncidentPresentation();
            collisionIncidentPresentation.Build(
                stageRoot.transform,
                collisionIslandRoot,
                leftRoadEdge,
                victimCar,
                otherCar,
                CollisionPresentationContactTime,
                contact,
                collisionForwardLocal,
                collisionOutwardLocal,
                vehicleLength,
                vehicleWidth,
                victimIncoming,
                otherIncoming,
                label,
                incidentMetadata,
                victimLabel,
                otherLabel,
                player.GetDriverColor(collisionVictimDriver),
                player.GetDriverColor(collisionOtherDriver),
                collisionShowcase);

            yield return null;

            collisionIncidentPresentation
                .ConfigureTrajectoryForensics(
                    collisionTrajectoryAnalysis,
                    collisionVictimDriver,
                    collisionOtherDriver,
                    collisionVictimMappedForensicPath,
                    collisionOtherMappedForensicPath,
                    ResolveCollisionCorridorLengthLocal(),
                    reportedTime,
                    observedTime,
                    collisionShowcase);
            yield return null;

            ShowCollisionCars(CollisionPresentationContactTime);
            collisionIncidentPresentation.HidePrepared();
            eventAudio?.SetPlaying(false);
            timeline.Pause();
            isActive = false;
            stageRoot.SetActive(false);

            if (!IsCollisionPreparationCurrent(key))
            {
                FailCollisionPreparation(
                    key,
                    "the dataset changed during preparation");
                yield break;
            }

            collisionPreparedDefinition = definition;
            collisionPreloadReady = true;
            collisionPreparationFailure = null;
            collisionPreloadRoutine = null;
            Debug.Log(
                $"[CollisionIncident] Prepared '{definition.eventId}' " +
                $"in {Time.realtimeSinceStartup - startedAt:0.00}s. " +
                "Open path now performs placement and activation only.",
                this);
        }

        private bool BuildCollisionSourceSnapshot(
            ReplayEventDto definition,
            float loadStart,
            float loadEnd)
        {
            eventSamples.Clear();
            eventIndices.Clear();
            eventDrivers.Clear();
            referenceDriverNumber = 0;

            int[] requestedDrivers = definition?.driverNumbers;
            if (player == null ||
                requestedDrivers == null ||
                requestedDrivers.Length < 2)
            {
                return false;
            }

            for (int index = 0;
                 index < requestedDrivers.Length &&
                 eventDrivers.Count < MaxEventDrivers;
                 index++)
            {
                int driver = requestedDrivers[index];
                if (driver <= 0 || eventDrivers.Contains(driver))
                    continue;

                List<LocationSample> snapshot = new();
                if (!player.CopyLocationSourceRange(
                        driver,
                        loadStart,
                        loadEnd,
                        snapshot) ||
                    snapshot.Count < 2)
                {
                    continue;
                }

                LocationMotionStabilizer.Apply(snapshot);
                eventDrivers.Add(driver);
                eventSamples.Add(driver, snapshot);
                eventIndices.Add(driver, 0);
                if (referenceDriverNumber == 0)
                    referenceDriverNumber = driver;
            }

            return eventDrivers.Count >= 2;
        }

        private bool TryBuildCollisionTrajectoryAnalysis(
            ReplayEventDto definition)
        {
            collisionTrajectoryAnalysis = null;
            collisionPresentationContactTime = 0f;
            if (definition?.driverNumbers == null ||
                definition.driverNumbers.Length < 2)
            {
                return false;
            }

            int firstDriver = definition.driverNumbers[0];
            int secondDriver = definition.driverNumbers[1];
            if (!eventSamples.TryGetValue(
                    firstDriver,
                    out List<LocationSample> firstSamples) ||
                !eventSamples.TryGetValue(
                    secondDriver,
                    out List<LocationSample> secondSamples))
            {
                return false;
            }

            collisionShowcase ??= new CollisionShowcaseVfxSettings();
            CollisionTrajectoryForensicsOptions options = new()
            {
                visibleLeadSeconds = Mathf.Max(
                    0.25f,
                    collisionShowcase.observedLeadSeconds),
                vehicleRevealLeadSeconds = Mathf.Max(
                    0.25f,
                    collisionShowcase.observedLeadSeconds),
                visibleTailSeconds = Mathf.Max(
                    0.1f,
                    collisionShowcase.observedTailSeconds),
                visibleSampleStepSeconds = 1f / Mathf.Clamp(
                    collisionShowcase.trajectorySamplesPerSecond,
                    10,
                    60)
            };
            if (!CollisionTrajectoryForensics.TryAnalyze(
                    firstSamples,
                    firstDriver,
                    secondSamples,
                    secondDriver,
                    definition.anchorTime,
                    definition.startTime,
                    definition.endTime,
                    options,
                    out collisionTrajectoryAnalysis) ||
                collisionTrajectoryAnalysis == null)
            {
                return false;
            }

            collisionPresentationContactTime =
                collisionTrajectoryAnalysis.PresentationTime;
            Debug.Log(
                $"[CollisionForensics] tier=" +
                $"{collisionTrajectoryAnalysis.Tier}, " +
                $"reported={definition.anchorTime:0.000}, " +
                $"observed={collisionPresentationContactTime:0.000}, " +
                $"separation=" +
                $"{collisionTrajectoryAnalysis.Contact.SeparationMeters:0.000}m.",
                this);
            return true;
        }

        private float ResolveCollisionCorridorLengthLocal()
        {
            float physicalLength = collisionRoomPlacement.IsValid
                ? collisionRoomPlacement.PhysicalLengthMeters
                : Mathf.Min(
                    2.8f,
                    Mathf.Max(
                        1.2f,
                        collisionShowcase != null
                            ? collisionShowcase
                                .maximumIslandSpanMeters
                            : 2.8f));
            Vector3 worldScale = PresentationRoot != null
                ? PresentationRoot.lossyScale
                : Vector3.one;
            float uniformWorldScale = Mathf.Max(
                0.0001f,
                Mathf.Max(
                    Mathf.Abs(worldScale.x),
                    Mathf.Max(
                        Mathf.Abs(worldScale.y),
                        Mathf.Abs(worldScale.z))));
            float physicalVehicleLength = collisionRoomPlacement.IsValid
                ? collisionRoomPlacement.TargetVehicleLengthMeters
                : 0f;
            float physicalRailLength = Mathf.Max(
                physicalLength * 0.5f,
                physicalLength -
                physicalVehicleLength *
                CollisionFootprintLongitudinalPadding * 2f);
            return physicalRailLength / uniformWorldScale;
        }

        private bool TryBuildMappedCollisionForensicPath(
            int driver,
            out Vector3[] mappedLocalPath)
        {
            mappedLocalPath = null;
            if (collisionTrajectoryAnalysis == null ||
                eventCars == null)
            {
                return false;
            }

            CollisionObservedTrajectory trajectory =
                collisionTrajectoryAnalysis.First.DriverNumber == driver
                    ? collisionTrajectoryAnalysis.First
                    : collisionTrajectoryAnalysis.Second.DriverNumber == driver
                        ? collisionTrajectoryAnalysis.Second
                        : null;
            if (trajectory == null || trajectory.VisibleSamples.Count < 2)
                return false;

            int sampleCount = trajectory.VisibleSamples.Count;
            LocationSample[] mappingSamples = new LocationSample[sampleCount];
            float inverseScale = 1f / Mathf.Max(
                0.000001f,
                ReplayCoordinate.scale);
            for (int index = 0;
                 index < sampleCount;
                 index++)
            {
                CollisionTrajectorySample source =
                    trajectory.VisibleSamples[index];
                mappingSamples[index] = new LocationSample
                {
                    t = source.Time,
                    driverNumber = driver,
                    x = source.SourcePosition.x * inverseScale,
                    y = source.SourcePosition.z * inverseScale,
                    z = source.SourcePosition.y * inverseScale,
                    speed = source.Telemetry.SpeedKph,
                    throttle = source.Telemetry.ThrottlePercent,
                    brake = source.Telemetry.Brake,
                    rpm = source.Telemetry.Rpm,
                    nGear = source.Telemetry.Gear,
                    n_gear = source.Telemetry.Gear,
                    drs = source.Telemetry.Drs
                };
            }

            Vector3[] mappedPath = new Vector3[sampleCount];
            if (!eventCars.TryGetMappedPositionsContinuously(
                    mappingSamples,
                    mappedPath))
            {
                return false;
            }

            mappedLocalPath = new Vector3[sampleCount];
            for (int index = 0; index < sampleCount; index++)
            {
                Vector3 mapped = mappedPath[index];
                mappedLocalPath[index] = sourceToEventRotation *
                    (mapped - eventSpaceCenter);
            }

            return true;
        }

        private void OpenPreparedCollision()
        {
            if (!IsCollisionPrepared ||
                collisionPreparedDefinition == null ||
                collisionIncidentPresentation == null)
            {
                Debug.LogWarning(
                    "[CollisionIncident] Collision is still preparing.",
                    this);
                PrepareTestCollision();
                return;
            }

            if (!TryRefreshCollisionPlacementForActivation())
            {
                Debug.LogWarning(
                    "[CollisionForensics] Cached room placement changed; " +
                    "preparing the incident again before opening.",
                    this);
                collisionPreloadReady = false;
                collisionPreparationFailure = null;
                PrepareTestCollision();
                return;
            }

            float clickedAt = Time.realtimeSinceStartup;
            if (!hasSnapshot)
            {
                snapshot = new ReplaySnapshot(player);
                hasSnapshot = true;
            }

            player.Pause();
            player.SetEventPresentationSuppressed(true);
            SuspendTableTrackRendering();
            currentEvent = collisionPreparedDefinition;
            timeline.SetTime(CollisionPresentationContactTime);
            timeline.Pause();
            ResetIndices();
            isLoading = false;
            isActive = true;
            ActivateCollisionPresentationStage();
            ShowCollisionCars(CollisionPresentationContactTime);
            collisionIncidentPresentation.BeginReveal();
            eventAudio?.SetPlaying(false);

            if (collisionFirstFrameRoutine != null)
                StopCoroutine(collisionFirstFrameRoutine);
            collisionFirstFrameRoutine = StartCoroutine(
                LogCollisionFirstFrame(clickedAt));
        }

        private IEnumerator LogCollisionFirstFrame(float clickedAt)
        {
            yield return new WaitForEndOfFrame();
            Debug.Log(
                $"[CollisionIncident] Click-to-first-frame " +
                $"{Time.realtimeSinceStartup - clickedAt:0.000}s; " +
                "network=False, geometryBuild=False, vehicleCreate=False.",
                this);
            collisionFirstFrameRoutine = null;
        }

        private void UpdateCollisionIncidentPresentation()
        {
            if (collisionIncidentPresentation == null ||
                eventCars == null ||
                currentEvent == null)
            {
                return;
            }

            float replayTime = collisionIncidentPresentation.Tick(
                Time.unscaledDeltaTime);
            timeline.SetTime(replayTime);
            ShowCollisionCars(replayTime);
            collisionIncidentPresentation.ApplyVehicleMotion();
            eventAudio?.Update(
                player != null ? player.engineSound : null,
                true,
                collisionIncidentPresentation.ShouldPlayEngineAudio,
                null);
        }

        private void ShowCollisionCars(float replayTime)
        {
            eventCars?.Show(
                eventSamples,
                eventIndices,
                replayTime,
                null,
                eventDrivers);
        }

        private Vector3[] BuildCollisionIncomingPath(
            int driver,
            float anchor)
        {
            float[] times =
            {
                anchor - 1f,
                anchor - 0.72f,
                anchor - 0.45f,
                anchor - 0.2f,
                anchor
            };
            List<Vector3> points = new(times.Length);
            for (int i = 0; i < times.Length; i++)
            {
                if (TryGetEventLocalVehiclePosition(
                        driver,
                        times[i],
                        out Vector3 point))
                {
                    points.Add(point);
                }
            }
            if (points.Count == 0)
                points.Add(ResolveCollisionContactPosition());
            return points.ToArray();
        }

        private static string FormatCollisionIncidentTime(float seconds)
        {
            seconds = Mathf.Max(0f, seconds);
            int minutes = Mathf.FloorToInt(seconds / 60f);
            float remaining = seconds - minutes * 60f;
            return $"{minutes:00}:{remaining:00.00}";
        }

        private bool TryClosePreparedCollision()
        {
            if (!isActive ||
                !IsCurrentCollision ||
                collisionIncidentPresentation == null)
            {
                return false;
            }

            if (collisionFirstFrameRoutine != null)
            {
                StopCoroutine(collisionFirstFrameRoutine);
                collisionFirstFrameRoutine = null;
            }
            collisionIncidentPresentation.HidePrepared();
            eventAudio?.SetPlaying(false);
            timeline.Pause();
            if (stageRoot != null)
                stageRoot.SetActive(false);
            isActive = false;
            RestoreTableTrackRendering();
            player?.SetEventPresentationSuppressed(false);
            RestoreReplay();
            return true;
        }

        private void CancelCollisionPreparation()
        {
            if (collisionPreloadRoutine != null)
            {
                StopCoroutine(collisionPreloadRoutine);
                collisionPreloadRoutine = null;
            }
        }

        private bool IsCollisionPreparationCurrent(string key)
        {
            return !string.IsNullOrWhiteSpace(key) &&
                string.Equals(
                    collisionPreloadKey,
                    key,
                    StringComparison.Ordinal);
        }

        private void FailCollisionPreparation(
            string key,
            string reason)
        {
            if (!IsCollisionPreparationCurrent(key))
                return;

            DestroyStage(false);
            collisionPreloadReady = false;
            collisionPreparedDefinition = null;
            collisionPreloadRoutine = null;
            collisionPreparationFailure = reason;
            Debug.LogWarning(
                $"[CollisionIncident] Preparation failed: {reason}.",
                this);
        }

        private void ClearCollisionIncidentPresentation()
        {
            if (collisionFirstFrameRoutine != null)
            {
                StopCoroutine(collisionFirstFrameRoutine);
                collisionFirstFrameRoutine = null;
            }
            collisionIncidentPresentation?.Clear();
            collisionIncidentPresentation = null;
            collisionIslandRoot = null;
            collisionTrajectoryAnalysis = null;
            collisionPresentationContactTime = 0f;
            collisionRoomPlacement = default;
            collisionVictimMappedForensicPath = null;
            collisionOtherMappedForensicPath = null;
            collisionRoomPlacementResolver.Invalidate();
            collisionPreloadReady = false;
            collisionPreparedDefinition = null;
            collisionPreparationFailure = null;
            player?.SetEventPresentationSuppressed(false);
        }

        private void CreateCollisionIncidentIsland(
            Vector3 center,
            Quaternion sourceToLocalRotation,
            float referenceVehicleLength,
            out Bounds stageBounds)
        {
            float safeLength = Mathf.Max(
                0.001f,
                referenceVehicleLength);
            Vector3 contact = ResolveRawCollisionContactLocal(
                center,
                sourceToLocalRotation);
            Vector3 forward = ResolveRawCollisionForwardLocal(
                center,
                sourceToLocalRotation);
            Quaternion islandRotation =
                Quaternion.LookRotation(forward, Vector3.up);
            float halfLength = safeLength * 0.34f;
            float halfWidth = safeLength * 0.36f;
            float bevel = Mathf.Min(
                halfWidth * 0.32f,
                safeLength * 0.2f);

            collisionIslandRoot = new GameObject(
                "CollisionIncidentIsland").transform;
            collisionIslandRoot.SetParent(
                stageRoot.transform,
                false);
            collisionIslandRoot.localPosition = contact;
            collisionIslandRoot.localRotation = islandRotation;

            Vector3[] perimeter =
            {
                new(-halfWidth + bevel, 0f, -halfLength),
                new(halfWidth - bevel, 0f, -halfLength),
                new(halfWidth, 0f, -halfLength + bevel),
                new(halfWidth, 0f, halfLength - bevel),
                new(halfWidth - bevel, 0f, halfLength),
                new(-halfWidth + bevel, 0f, halfLength),
                new(-halfWidth, 0f, halfLength - bevel),
                new(-halfWidth, 0f, -halfLength + bevel)
            };
            Vector3[] vertices = new Vector3[perimeter.Length + 1];
            vertices[0] = Vector3.zero;
            Array.Copy(
                perimeter,
                0,
                vertices,
                1,
                perimeter.Length);
            int[] triangles = new int[perimeter.Length * 3];
            for (int i = 0; i < perimeter.Length; i++)
            {
                int offset = i * 3;
                triangles[offset] = 0;
                triangles[offset + 1] =
                    (i + 1) % perimeter.Length + 1;
                triangles[offset + 2] = i + 1;
            }

            roadMesh = new Mesh
            {
                name = "CollisionIncidentIslandMesh",
                vertices = vertices,
                triangles = triangles
            };
            roadMesh.RecalculateNormals();
            roadMesh.RecalculateBounds();
            GameObject surface = new(
                "CharcoalContactCore",
                typeof(MeshFilter),
                typeof(MeshRenderer));
            surface.transform.SetParent(
                collisionIslandRoot,
                false);
            surface.transform.localPosition =
                Vector3.up * safeLength * 0.008f;
            surface.GetComponent<MeshFilter>().sharedMesh = roadMesh;
            roadMaterial = CreateMaterial(
                new Color(0.018f, 0.016f, 0.015f, 1f));
            MeshRenderer surfaceRenderer =
                surface.GetComponent<MeshRenderer>();
            surfaceRenderer.sharedMaterial = roadMaterial;
            surfaceRenderer.shadowCastingMode = ShadowCastingMode.Off;
            surfaceRenderer.receiveShadows = false;

            edgeMaterial = CreateMaterial(
                new Color(1.35f, 0.48f, 0.015f, 1f));
            GameObject borderObject = new(
                "OrangeContactBoundary",
                typeof(LineRenderer));
            borderObject.transform.SetParent(
                collisionIslandRoot,
                false);
            LineRenderer border =
                borderObject.GetComponent<LineRenderer>();
            border.useWorldSpace = false;
            border.loop = true;
            border.positionCount = perimeter.Length;
            border.widthMultiplier = safeLength * 0.045f;
            border.numCapVertices = 2;
            border.numCornerVertices = 2;
            border.sharedMaterial = edgeMaterial;
            border.shadowCastingMode = ShadowCastingMode.Off;
            border.receiveShadows = false;
            for (int i = 0; i < perimeter.Length; i++)
            {
                border.SetPosition(
                    i,
                    perimeter[i] +
                    Vector3.up * safeLength * 0.012f);
            }
            leftRoadEdge = border;
            rightRoadEdge = null;

            Vector3 firstStagePoint =
                collisionIslandRoot.localPosition +
                collisionIslandRoot.localRotation * perimeter[0];
            stageBounds = new Bounds(
                firstStagePoint,
                Vector3.zero);
            for (int i = 1; i < perimeter.Length; i++)
            {
                stageBounds.Encapsulate(
                    collisionIslandRoot.localPosition +
                    collisionIslandRoot.localRotation * perimeter[i]);
            }
            Vector3 stageSize = stageBounds.size;
            stageSize.y = Mathf.Max(
                safeLength * 0.12f,
                0.001f);
            stageBounds.size = stageSize;
        }

        private Bounds ResolveCollisionIslandActionBounds(
            Vector3 center,
            Quaternion sourceToLocalRotation,
            Vector3 contact,
            Quaternion islandRotation,
            float vehicleLength)
        {
            Quaternion toIsland = Quaternion.Inverse(islandRotation);
            Bounds bounds = new(Vector3.zero, Vector3.zero);
            bool hasPoint = false;
            int[] drivers = currentEvent?.driverNumbers;
            if (drivers != null)
            {
                float[] times =
                {
                    CollisionPresentationContactTime - 0.9f,
                    CollisionPresentationContactTime - 0.45f,
                    CollisionPresentationContactTime
                };
                for (int driverIndex = 0;
                     driverIndex < drivers.Length;
                     driverIndex++)
                {
                    for (int timeIndex = 0;
                         timeIndex < times.Length;
                         timeIndex++)
                    {
                        if (!TryGetRawEventLocalVehiclePosition(
                                drivers[driverIndex],
                                times[timeIndex],
                                center,
                                sourceToLocalRotation,
                                out Vector3 position))
                        {
                            continue;
                        }

                        Vector3 local = toIsland *
                            (position - contact);
                        if (!hasPoint)
                        {
                            bounds = new Bounds(local, Vector3.zero);
                            hasPoint = true;
                        }
                        else
                        {
                            bounds.Encapsulate(local);
                        }
                    }
                }
            }

            if (!hasPoint)
                bounds = new Bounds(Vector3.zero, Vector3.zero);
            bounds.Encapsulate(new Vector3(
                -vehicleLength * 0.9f,
                0f,
                vehicleLength * 1.25f));
            bounds.Encapsulate(new Vector3(
                vehicleLength * 0.9f,
                0f,
                vehicleLength * 1.25f));
            bounds.Encapsulate(new Vector3(
                0f,
                0f,
                -vehicleLength * 1.75f));
            return bounds;
        }

        private Vector3 ResolveRawCollisionContactLocal(
            Vector3 center,
            Quaternion sourceToLocalRotation)
        {
            int[] drivers = currentEvent?.driverNumbers;
            float contactTime = CollisionPresentationContactTime;
            if (drivers != null && drivers.Length >= 2 &&
                TryGetRawEventLocalVehiclePosition(
                    drivers[0],
                    contactTime,
                    center,
                    sourceToLocalRotation,
                    out Vector3 first) &&
                TryGetRawEventLocalVehiclePosition(
                    drivers[1],
                    contactTime,
                    center,
                    sourceToLocalRotation,
                    out Vector3 second))
            {
                return (first + second) * 0.5f;
            }

            return Vector3.zero;
        }

        private Vector3 ResolveRawCollisionForwardLocal(
            Vector3 center,
            Quaternion sourceToLocalRotation)
        {
            List<LocationSample> samples = FindReferenceSamples();
            float contactTime = CollisionPresentationContactTime;
            if (samples != null &&
                TryGetMappedPosition(
                    samples,
                    contactTime - 0.35f,
                    out Vector3 before) &&
                TryGetMappedPosition(
                    samples,
                    contactTime + 0.35f,
                    out Vector3 after))
            {
                Vector3 forward = sourceToLocalRotation *
                    (after - before);
                forward.y = 0f;
                if (forward.sqrMagnitude > 0.000001f)
                    return forward.normalized;
            }

            return Vector3.forward;
        }

        private bool TryGetRawEventLocalVehiclePosition(
            int driver,
            float replayTime,
            Vector3 center,
            Quaternion sourceToLocalRotation,
            out Vector3 position)
        {
            position = Vector3.zero;
            if (!eventSamples.TryGetValue(
                    driver,
                    out List<LocationSample> samples) ||
                !TryGetMappedPosition(
                    samples,
                    replayTime,
                    out Vector3 mapped))
            {
                return false;
            }

            position = sourceToLocalRotation * (mapped - center);
            return true;
        }
    }
}
