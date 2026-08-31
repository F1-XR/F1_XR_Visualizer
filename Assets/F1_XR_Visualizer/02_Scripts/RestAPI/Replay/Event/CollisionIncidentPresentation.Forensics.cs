using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace F1XR.RestAPI.Replay
{
    internal sealed partial class CollisionIncidentPresentation
    {
        private const float ForensicsRailRevealEnd = 0.45f;
        private const float ForensicsApproachEnd = 1.80f;
        private const float ForensicsHitStopEnd = 1.89f;
        private const float ForensicsObservedPostEnd = 2.44f;
        private const float ForensicsAnnotationEnd = 3.15f;
        private const float ForensicsRevealEnd = 5.15f;
        private const float ForensicsReplayApproachEnd = 1.35f;
        private const float ForensicsReplayHitStopEnd = 1.44f;
        private const float ForensicsReplayEnd = 1.99f;
        private const float ForensicsVehicleHoldPostSeconds = 0.35f;
        private const float ForensicsManualContactResetMeters = 0.085f;
        private const float ForensicsImpactVisibleSeconds = 1.20f;
        private const float ForensicsTrackRevealEnd = 0.25f;
        private const int ForensicsTrackSubmeshCount = 6;
        private static readonly float[] ForensicsPreStationOffsets =
        {
            -1.30f,
            -0.80f,
            -0.30f
        };

        private sealed class ForensicPath
        {
            public int DriverNumber;
            public CollisionObservedTrajectory Source;
            public float[] Times;
            public Vector3[] Points;
            public Vector3[] Tangents;
            public LineRenderer Rail;
            public LineRenderer Tail;
            public MeshRenderer StableRail;
        }

        private readonly struct ForensicMarker
        {
            public ForensicMarker(Transform root, float time)
            {
                Root = root;
                Time = time;
            }

            public Transform Root { get; }
            public float Time { get; }
        }

        private readonly struct ForensicOutline
        {
            public ForensicOutline(
                Transform root,
                float time,
                int driverNumber,
                Vector3 basePosition,
                Vector3 baseScale)
            {
                Root = root;
                Time = time;
                DriverNumber = driverNumber;
                BasePosition = basePosition;
                BaseScale = baseScale;
            }

            public Transform Root { get; }
            public float Time { get; }
            public int DriverNumber { get; }
            public Vector3 BasePosition { get; }
            public Vector3 BaseScale { get; }
        }

        private sealed class ForensicStation
        {
            public Transform Root;
            public float Time;
            public bool Contact;
            public TextMeshPro Header;
            public TextMeshPro Gap;
            public TextMeshPro VictimTelemetry;
            public TextMeshPro OtherTelemetry;
            public Vector3 HeaderBaseScale;
            public Vector3 GapBaseScale;
            public Vector3 TelemetryBaseScale;
            public Transform PulseRoot;
            public Vector3 PulseBaseScale;
        }

        private CollisionTrajectoryAnalysis forensicsAnalysis;
        private ForensicPath forensicsVictimPath;
        private ForensicPath forensicsOtherPath;
        private Transform forensicsRoot;
        private Transform forensicsTrackRoot;
        private Transform forensicsStableRailRoot;
        private Transform forensicsTrackWarningRoot;
        private Transform forensicsRailRoot;
        private Transform forensicsTailRoot;
        private Transform forensicsMarkerRoot;
        private Transform forensicsOutlineRoot;
        private Transform forensicsStationRoot;
        private CollisionTimeLensGate forensicsTimeLensGate;
        private Material forensicsVictimMaterial;
        private Material forensicsOtherMaterial;
        private Material forensicsVictimStableMaterial;
        private Material forensicsOtherStableMaterial;
        private Material forensicsVictimOutlineMaterial;
        private Material forensicsOtherOutlineMaterial;
        private Material forensicsVictimTailMaterial;
        private Material forensicsOtherTailMaterial;
        private Material forensicsConnectorMaterial;
        private Material forensicsBrakeMaterial;
        private Material forensicsReconstructedMaterial;
        private Material forensicsLensMaterial;
        private Material forensicsTrackAsphaltMaterial;
        private Material forensicsTrackKerbRedMaterial;
        private Material forensicsTrackKerbWhiteMaterial;
        private Material forensicsTrackEdgeMaterial;
        private Material forensicsTrackRunoffMaterial;
        private Material forensicsTrackGrassMaterial;
        private Material forensicsTrackBarrierMaterial;
        private Material forensicsTrackWarningMaterial;
        private Material forensicsStationMaterial;
        private TextMeshPro forensicsLegend;
        private TextMeshPro forensicsLensHud;
        private readonly List<Material> forensicsMaterials = new();
        private readonly List<Mesh> forensicsMeshes = new();
        private readonly List<LineRenderer> forensicsTrackWarningLines = new();
        private readonly List<Vector3[]> forensicsTrackWarningPoints = new();
        private readonly List<ForensicMarker> forensicsMarkers = new();
        private readonly List<ForensicOutline> forensicsOutlines = new();
        private readonly List<Renderer> forensicsGhostRenderers = new();
        private readonly List<ForensicStation> forensicsStations = new();
        private readonly List<Vector3> forensicsVictimTailPoints = new(32);
        private readonly List<Vector3> forensicsOtherTailPoints = new(32);
        private EventTrackSegment forensicsActualTrackSegment;
        private Mesh forensicsTrackMesh;
        private int[][] forensicsTrackTriangles;
        private float forensicsTrackOuterHalfWidth;
        private float forensicsLastTrackProgress = -1f;
        private Color forensicsTrackWarningColor;
        private bool forensicsTrackEnabled;
        private bool forensicsTrackUserVisible = true;
        private bool forensicsUsesActualTrack;
        private Vector3 forensicsCorridorForward = Vector3.forward;
        private Vector3 forensicsCorridorRight = Vector3.right;
        private float forensicsCorridorScale = 1f;
        private Vector3 suzukaAccidentSourceCenter;
        private Quaternion suzukaAccidentSourceRotation =
            Quaternion.identity;
        private float forensicsVisibleStartTime;
        private float forensicsVisibleEndTime;
        private float forensicsVehicleHoldTime;
        private float forensicsCurrentTime;
        private float forensicsWallTime;
        private float forensicsTailSeconds = 0.15f;
        private float forensicsTickSeconds = 0.25f;
        private float forensicsEchoDelaySeconds = 0.12f;
        private float forensicsDefaultLensNormalized = 1f;
        private float forensicsSavedLensNormalized = 1f;
        private int forensicsVictimDriver;
        private int forensicsOtherDriver;
        private string forensicsReportedTime;
        private string forensicsObservedTime;
        private string forensicsStatus = string.Empty;
        private bool forensicsConfigured;
        private bool forensicsRevealRunning;
        private bool forensicsImpactReplaying;
        private bool forensicsVehicleVisible;
        private bool forensicsFinalApplied;
        private bool forensicsTimeLensEnabled;
        private bool forensicsLensActive;
        private bool forensicsManualContactLatched;
        private float forensicsPreviousLensNormalized = float.NaN;
        private float forensicsManualContactPulseRemaining;

        public bool UsesTrajectoryForensics => forensicsConfigured;

        public bool CanToggleForensicsTrack =>
            forensicsConfigured &&
            forensicsTrackEnabled &&
            forensicsTrackRoot != null;

        public bool IsForensicsTrackVisible =>
            CanToggleForensicsTrack && forensicsTrackUserVisible;

        public void ToggleForensicsTrackVisibility()
        {
            if (!CanToggleForensicsTrack)
                return;

            forensicsTrackUserVisible = !forensicsTrackUserVisible;
            SetForensicsTrackProgress(
                forensicsTrackUserVisible ? 1f : 0f);
        }

        public bool ShouldPlayTrajectoryForensicsEngineAudio =>
            forensicsConfigured &&
            forensicsAnalysis.Tier != CollisionEvidenceTier.ContactUnresolved &&
            !forensicsLensActive &&
            (accidentCinematicRunning
                ? ShouldPlayAccidentCinematicEngineAudio
                : forensicsImpactReplaying
                    ? forensicsWallTime < ForensicsReplayApproachEnd
                    : forensicsRevealRunning &&
                      forensicsWallTime >= ForensicsRailRevealEnd &&
                      forensicsWallTime < ForensicsApproachEnd);

        public bool IsTimeLensAvailable =>
            forensicsConfigured &&
            revealComplete &&
            !forensicsImpactReplaying &&
            forensicsTimeLensGate != null &&
            forensicsTimeLensGate.IsAvailable;

        public bool IsTimeLensGrabbed =>
            IsTimeLensAvailable && forensicsTimeLensGate.IsGrabbed;

        public float TimeLensNormalized => forensicsTimeLensGate != null
            ? forensicsTimeLensGate.NormalizedValue
            : 1f;

        public float TimeLensTimeSeconds => forensicsConfigured
            ? forensicsCurrentTime - forensicsAnalysis.PresentationTime
            : 0f;

        public string TimeLensStatus => forensicsStatus;

        public void ConfigureTrajectoryForensics(
            CollisionTrajectoryAnalysis analysis,
            int victimDriver,
            int otherDriver,
            IReadOnlyList<Vector3> victimMappedLocalPath,
            IReadOnlyList<Vector3> otherMappedLocalPath,
            float corridorLengthLocal,
            string reportedTime,
            string observedTime,
            CollisionShowcaseVfxSettings settings,
            Transform trackSourceRoot,
            Vector3 sourceCenter,
            Quaternion sourceToLocalRotation)
        {
            ClearTrajectoryForensics();
            if (analysis == null || stage == null || presentationRoot == null)
                return;

            forensicsAnalysis = analysis;
            forensicsTrackUserVisible = true;
            suzukaAccidentSourceCenter = sourceCenter;
            suzukaAccidentSourceRotation = sourceToLocalRotation;
            forensicsVictimDriver = victimDriver;
            forensicsOtherDriver = otherDriver;
            forensicsReportedTime = reportedTime;
            forensicsObservedTime = observedTime;
            forensicsTailSeconds = Mathf.Max(
                0.05f,
                settings != null ? settings.temporalTailSeconds : 0.15f);
            forensicsTickSeconds = Mathf.Max(
                0.05f,
                settings != null ? settings.evidenceTickSeconds : 0.25f);
            forensicsEchoDelaySeconds = Mathf.Max(
                0f,
                settings != null
                    ? settings.evidenceEchoDelaySeconds
                    : 0.12f);
            forensicsTimeLensEnabled = settings == null || settings.enableTimeLens;

            ResolveForensicsCorridor(
                Mathf.Max(carLength * 2f, corridorLengthLocal),
                victimMappedLocalPath,
                otherMappedLocalPath);
            forensicsVictimPath = BuildForensicPath(
                analysis,
                victimDriver,
                "Victim",
                victimMappedLocalPath);
            forensicsOtherPath = BuildForensicPath(
                analysis,
                otherDriver,
                "Other",
                otherMappedLocalPath);
            if (forensicsVictimPath == null || forensicsOtherPath == null)
            {
                ClearTrajectoryForensics();
                return;
            }

            forensicsVisibleStartTime = Mathf.Max(
                forensicsVictimPath.Source.VisibleStartTime,
                forensicsOtherPath.Source.VisibleStartTime);
            float observedEnd = Mathf.Min(
                forensicsVictimPath.Source.VisibleEndTime,
                forensicsOtherPath.Source.VisibleEndTime);
            forensicsVisibleEndTime = analysis.Tier switch
            {
                CollisionEvidenceTier.ObservedContactAndPost => observedEnd,
                CollisionEvidenceTier.ObservedContactRequiresReconstruction =>
                    analysis.PresentationTime +
                    ForensicsVehicleHoldPostSeconds,
                _ => analysis.PresentationTime
            };
            forensicsVehicleHoldTime = analysis.Tier switch
            {
                CollisionEvidenceTier.ObservedContactAndPost => Mathf.Min(
                    observedEnd,
                    analysis.PresentationTime +
                    ForensicsVehicleHoldPostSeconds),
                CollisionEvidenceTier.ObservedContactRequiresReconstruction =>
                    analysis.PresentationTime +
                    ForensicsVehicleHoldPostSeconds,
                _ => analysis.PresentationTime
            };
            forensicsCurrentTime = forensicsVisibleStartTime;
            ResolveAccidentVisualContact();

            CreateForensicsVisuals(
                settings,
                victimMappedLocalPath,
                otherMappedLocalPath,
                trackSourceRoot,
                sourceCenter,
                sourceToLocalRotation);
            LogAccidentTrackHeadingAudit();
            forensicsConfigured = true;
            OrientForensicsReadableText();
            ResetTrajectoryForensicsPrepared();
        }

        public void ResetTrajectoryForensicsPrepared()
        {
            if (!forensicsConfigured)
                return;

            ResetAccidentCinematic();
            forensicsRevealRunning = false;
            forensicsImpactReplaying = false;
            forensicsVehicleVisible = false;
            forensicsFinalApplied = false;
            forensicsLensActive = false;
            forensicsManualContactLatched = false;
            forensicsPreviousLensNormalized = float.NaN;
            forensicsManualContactPulseRemaining = 0f;
            forensicsWallTime = 0f;
            forensicsCurrentTime = forensicsVisibleStartTime;
            revealRunning = false;
            revealComplete = false;
            impactReplaying = false;
            impactTriggered = false;
            finalTableauApplied = false;
            revealTime = 0f;
            impactReplayTime = 0f;
            secondaryHapticTriggered = false;
            secondaryHapticCountdown = -1f;
            Phase = CollisionPresentationPhase.Preparing;
            forensicsStatus = ResolveInitialForensicsStatus();

            if (impactAudio != null)
                impactAudio.Stop();
            ResetVehicleMotion();
            SetCarsVisible(false);
            SetRootVisible(forensicsRoot, false);
            SetRootVisible(earlyGhostRoot, false);
            SetRootVisible(lateGhostRoot, false);
            SetRootVisible(postRoot, false);
            SetRootVisible(impactRoot, false);
            SetRootVisible(warningRoot, false);
            SetRootVisible(impactPulseRoot, false);
            SetImpactWarningWave(-1f);
            SetImpactBurst(-1f);
            ClearImpactSmoke();
            SetIncomingTrajectoryProgress(0f, 0f);
            SetPostImpactProgress(0f);
            SetForensicsRailProgress(0f);
            SetForensicsTrackProgress(0f);
            SetForensicsTrackWarningPulse(-1f);
            SetForensicsTail(float.NaN);
            SetForensicsAnnotations(0f);
            UpdateForensicsOutlines(
                float.NegativeInfinity,
                false,
                false);
            forensicsTimeLensGate?.SetAvailable(false);
            forensicsTimeLensGate?.ResetValue(
                forensicsDefaultLensNormalized,
                false);
            if (island != null)
            {
                island.gameObject.SetActive(false);
                island.localScale = islandBaseScale;
            }
        }

        public void BeginTrajectoryForensicsReveal()
        {
            if (!forensicsConfigured)
                return;

            ResetTrajectoryForensicsPrepared();
            BeginAccidentCinematic();
        }

        public void ReplayTrajectoryForensicsImpact()
        {
            if (!forensicsConfigured ||
                !revealComplete ||
                forensicsImpactReplaying ||
                forensicsAnalysis.Tier == CollisionEvidenceTier.ContactUnresolved)
            {
                return;
            }

            ResetTrajectoryForensicsPrepared();
            BeginAccidentCinematic();
        }

        public float TickTrajectoryForensics(float delta)
        {
            if (!forensicsConfigured)
                return anchorTime;

            float safeDelta = Mathf.Max(0f, delta);
            if (accidentCinematicRunning)
                return TickAccidentCinematic(safeDelta);

            TickSecondaryImpactHaptic(safeDelta);
            TickForensicsManualContactPulse(safeDelta);
            OrientForensicsReadableText();
            UpdateForensicsContactStationPulse();
            if (IsTimeLensGrabbed)
                FaceReadableTextToViewer(forensicsLensHud);
            if (forensicsImpactReplaying)
                return TickForensicsImpactReplay(safeDelta);
            if (!forensicsRevealRunning)
                return forensicsCurrentTime;

            forensicsWallTime = Mathf.Min(
                ForensicsRevealEnd,
                forensicsWallTime + safeDelta);
            revealTime = forensicsWallTime;
            ApplyForensicsReveal(forensicsWallTime);
            if (forensicsWallTime >= ForensicsRevealEnd)
            {
                forensicsRevealRunning = false;
                revealRunning = false;
                revealComplete = true;
                Phase = CollisionPresentationPhase.ForensicHold;
                forensicsTimeLensGate?.SetAvailable(
                    forensicsTimeLensEnabled);
                forensicsLensActive = forensicsTimeLensGate != null;
                if (forensicsLensActive)
                {
                    ApplyForensicsTimeLensValue(
                        forensicsTimeLensGate.NormalizedValue);
                }
                else
                {
                    UpdateForensicsStatus();
                }
            }

            return forensicsCurrentTime;
        }

        public void ApplyTrajectoryForensicsVehicleMotion()
        {
            if (!forensicsConfigured)
                return;

            bool showCars =
                forensicsAnalysis.Tier != CollisionEvidenceTier.ContactUnresolved &&
                (forensicsVehicleVisible || forensicsFinalApplied || forensicsLensActive);
            if (!showCars)
            {
                ResetVehicleMotion();
                SetCarsVisible(false);
                return;
            }

            SetCarsVisible(true);
            if (accidentContactReached)
            {
                ApplyAccidentCollisionResponse();
                UpdateDriverAnnotations();
                return;
            }

            if (!TryResolveForensicsPose(
                forensicsVictimDriver,
                forensicsCurrentTime,
                out Vector3 victimPoint,
                out Vector3 victimTangent) ||
                !TryResolveForensicsPose(
                forensicsOtherDriver,
                forensicsCurrentTime,
                out Vector3 otherPoint,
                out Vector3 otherTangent))
            {
                ResetVehicleMotion();
                SetCarsVisible(false);
                return;
            }

            ApplyForensicCarPose(
                victim,
                victimPoint,
                victimTangent);
            ApplyForensicCarPose(
                other,
                otherPoint,
                otherTangent);
            UpdateDriverAnnotations();
        }

        public void ApplyTrajectoryForensicsFinalTableau()
        {
            if (!forensicsConfigured)
                return;

            forensicsFinalApplied = true;
            finalTableauApplied = true;
            forensicsVehicleVisible =
                forensicsAnalysis.Tier != CollisionEvidenceTier.ContactUnresolved;
            forensicsCurrentTime = forensicsVehicleHoldTime;
            SetRootVisible(forensicsRoot, true);
            SetRootVisible(earlyGhostRoot, false);
            SetRootVisible(lateGhostRoot, false);
            SetForensicsRailProgress(1f);
            SetForensicsTrackProgress(1f);
            SetForensicsTrackWarningPulse(-1f);
            SetForensicsTail(float.NaN);
            SetForensicsAnnotations(1f);
            SetRootVisible(
                postRoot,
                forensicsAnalysis.Tier ==
                    CollisionEvidenceTier.ObservedContactRequiresReconstruction);
            SetRootVisible(
                impactRoot,
                forensicsAnalysis.Tier != CollisionEvidenceTier.ContactUnresolved);
            SetRootVisible(warningRoot, true);
            SetRootVisible(
                annotationRoot,
                forensicsAnalysis.Tier != CollisionEvidenceTier.ContactUnresolved);
            SetImpactFlash(false, 0f);
            SetImpactPulse(-1f);
            SetImpactTransient(-1f);
            SetImpactWarningWave(-1f);
            SetImpactBurst(-1f);
            ClearImpactSmoke();
            UpdateForensicsOutlines(
                forensicsCurrentTime,
                true,
                false);
            Phase = CollisionPresentationPhase.ForensicHold;
            UpdateForensicsStatus();
        }

        public void ClearTrajectoryForensics()
        {
            ClearAccidentCinematic();
            forensicsActualTrackSegment?.Clear();
            forensicsActualTrackSegment = null;
            if (forensicsTimeLensGate != null)
            {
                forensicsTimeLensGate.ValueChanged -=
                    OnForensicsTimeLensChanged;
                forensicsTimeLensGate.GrabStateChanged -=
                    OnForensicsTimeLensGrabStateChanged;
                UnityEngine.Object.Destroy(
                    forensicsTimeLensGate.gameObject);
            }
            if (forensicsRoot != null)
                UnityEngine.Object.Destroy(forensicsRoot.gameObject);

            ResetVehicleMotion();
            for (int i = 0; i < forensicsMeshes.Count; i++)
            {
                Mesh mesh = forensicsMeshes[i];
                if (mesh == null)
                    continue;
                meshes.Remove(mesh);
                UnityEngine.Object.Destroy(mesh);
            }
            for (int i = 0; i < forensicsMaterials.Count; i++)
            {
                Material material = forensicsMaterials[i];
                if (material == null)
                    continue;
                materials.Remove(material);
                UnityEngine.Object.Destroy(material);
            }

            forensicsMaterials.Clear();
            forensicsMeshes.Clear();
            forensicsTrackWarningLines.Clear();
            forensicsTrackWarningPoints.Clear();
            forensicsMarkers.Clear();
            forensicsOutlines.Clear();
            forensicsGhostRenderers.Clear();
            forensicsStations.Clear();
            forensicsVictimTailPoints.Clear();
            forensicsOtherTailPoints.Clear();
            forensicsAnalysis = null;
            forensicsVictimPath = null;
            forensicsOtherPath = null;
            forensicsRoot = null;
            forensicsTrackRoot = null;
            forensicsStableRailRoot = null;
            forensicsTrackWarningRoot = null;
            forensicsRailRoot = null;
            forensicsTailRoot = null;
            forensicsMarkerRoot = null;
            forensicsOutlineRoot = null;
            forensicsStationRoot = null;
            forensicsTimeLensGate = null;
            forensicsVictimMaterial = null;
            forensicsOtherMaterial = null;
            forensicsVictimStableMaterial = null;
            forensicsOtherStableMaterial = null;
            forensicsVictimOutlineMaterial = null;
            forensicsOtherOutlineMaterial = null;
            forensicsVictimTailMaterial = null;
            forensicsOtherTailMaterial = null;
            forensicsConnectorMaterial = null;
            forensicsBrakeMaterial = null;
            forensicsReconstructedMaterial = null;
            forensicsLensMaterial = null;
            forensicsTrackAsphaltMaterial = null;
            forensicsTrackKerbRedMaterial = null;
            forensicsTrackKerbWhiteMaterial = null;
            forensicsTrackEdgeMaterial = null;
            forensicsTrackRunoffMaterial = null;
            forensicsTrackGrassMaterial = null;
            forensicsTrackBarrierMaterial = null;
            forensicsTrackWarningMaterial = null;
            forensicsStationMaterial = null;
            forensicsTrackMesh = null;
            forensicsTrackTriangles = null;
            forensicsTrackOuterHalfWidth = 0f;
            forensicsLastTrackProgress = -1f;
            forensicsTrackEnabled = false;
            forensicsTrackUserVisible = true;
            forensicsUsesActualTrack = false;
            forensicsLegend = null;
            forensicsLensHud = null;
            forensicsDefaultLensNormalized = 1f;
            forensicsSavedLensNormalized = 1f;
            forensicsConfigured = false;
            forensicsRevealRunning = false;
            forensicsImpactReplaying = false;
            forensicsVehicleVisible = false;
            forensicsFinalApplied = false;
            forensicsLensActive = false;
            forensicsManualContactLatched = false;
            forensicsPreviousLensNormalized = float.NaN;
            forensicsManualContactPulseRemaining = 0f;
            forensicsStatus = string.Empty;
            revealRunning = false;
            revealComplete = false;
            impactReplaying = false;
        }

        public bool SetTimeLensNormalized(float value)
        {
            if (!IsTimeLensAvailable)
                return false;

            bool result = forensicsTimeLensGate.SetNormalized(value, true);
            if (result)
            {
                ApplyForensicsTimeLensValue(
                    forensicsTimeLensGate.NormalizedValue);
            }
            return result;
        }

        private void ResolveForensicsCorridor(
            float corridorLengthLocal,
            IReadOnlyList<Vector3> victimMappedLocalPath,
            IReadOnlyList<Vector3> otherMappedLocalPath)
        {
            forensicsCorridorForward = FlattenNormalized(
                forwardLocal,
                Vector3.forward);
            forensicsCorridorRight = FlattenNormalized(
                Vector3.Cross(Vector3.up, forensicsCorridorForward),
                outwardLocal);

            float minimum = float.PositiveInfinity;
            float maximum = float.NegativeInfinity;
            AccumulateForensicsLongitudinalRange(
                victimMappedLocalPath,
                ref minimum,
                ref maximum);
            AccumulateForensicsLongitudinalRange(
                otherMappedLocalPath,
                ref minimum,
                ref maximum);
            float span = maximum - minimum;
            forensicsCorridorScale = float.IsFinite(span) && span > 0.0001f
                ? corridorLengthLocal / span
                : 1f;
        }

        private void AccumulateForensicsLongitudinalRange(
            IReadOnlyList<Vector3> samples,
            ref float minimum,
            ref float maximum)
        {
            if (samples == null)
                return;
            for (int i = 0; i < samples.Count; i++)
            {
                Vector3 relative = samples[i] - contactLocal;
                relative.y = 0f;
                float longitudinal = Vector3.Dot(
                    relative,
                    forensicsCorridorForward);
                minimum = Mathf.Min(minimum, longitudinal);
                maximum = Mathf.Max(maximum, longitudinal);
            }
        }

        private ForensicPath BuildForensicPath(
            CollisionTrajectoryAnalysis analysis,
            int driverNumber,
            string label,
            IReadOnlyList<Vector3> mappedLocalPath)
        {
            CollisionObservedTrajectory source =
                analysis.First.DriverNumber == driverNumber
                    ? analysis.First
                    : analysis.Second.DriverNumber == driverNumber
                        ? analysis.Second
                        : null;
            if (source == null ||
                source.VisibleSamples.Count < 2 ||
                mappedLocalPath == null ||
                mappedLocalPath.Count != source.VisibleSamples.Count)
                return null;

            int count = source.VisibleSamples.Count;
            ForensicPath path = new()
            {
                DriverNumber = driverNumber,
                Source = source,
                Times = new float[count],
                Points = new Vector3[count],
                Tangents = new Vector3[count]
            };
            for (int i = 0; i < count; i++)
            {
                CollisionTrajectorySample sample = source.VisibleSamples[i];
                path.Times[i] = sample.Time;
                Vector3 relative = mappedLocalPath[i] - contactLocal;
                relative.y = 0f;
                float longitudinal = Vector3.Dot(
                    relative,
                    forensicsCorridorForward);
                float lateral = Vector3.Dot(
                    relative,
                    forensicsCorridorRight);
                path.Points[i] = TransformSuzukaAccidentPoint(
                    contactLocal +
                    forensicsCorridorForward * longitudinal +
                    forensicsCorridorRight * lateral);
            }
            for (int i = 0; i < count; i++)
            {
                int before = Mathf.Max(0, i - 1);
                int after = Mathf.Min(count - 1, i + 1);
                path.Tangents[i] = FlattenNormalized(
                    path.Points[after] - path.Points[before],
                    forensicsCorridorForward);
            }
            return path;
        }

        private Vector3 TransformSuzukaAccidentPoint(
            Vector3 mappedLocalPoint)
        {
            return contactLocal +
                (mappedLocalPoint - contactLocal) *
                forensicsCorridorScale;
        }

        private void CreateForensicsVisuals(
            CollisionShowcaseVfxSettings settings,
            IReadOnlyList<Vector3> victimMappedLocalPath,
            IReadOnlyList<Vector3> otherMappedLocalPath,
            Transform trackSourceRoot,
            Vector3 sourceCenter,
            Quaternion sourceToLocalRotation)
        {
            forensicsRoot = CreateRoot(
                "CollisionTrajectoryForensics",
                presentationRoot);
            forensicsTrackRoot = CreateRoot(
                "SuzukaInspiredAccidentProxyTrackRoot",
                forensicsRoot);
            forensicsStableRailRoot = CreateRoot(
                "StableObservedRibbons",
                forensicsRoot);
            forensicsTrackWarningRoot = CreateRoot(
                "TrackContactWarning",
                forensicsRoot);
            forensicsRailRoot = CreateRoot("ObservedRails", forensicsRoot);
            forensicsTailRoot = CreateRoot("TemporalTails", forensicsRoot);
            forensicsMarkerRoot = CreateRoot("EvidenceTicks", forensicsRoot);
            forensicsOutlineRoot = CreateRoot("EvidenceOutlines", forensicsRoot);
            forensicsStationRoot = CreateRoot(
                "EvidenceStations",
                forensicsRoot);

            forensicsVictimMaterial = CreateForensicsMaterial(
                "Runtime_ForensicsDriverA",
                new Color(0.93f, 0.97f, 1f, 0.9f));
            forensicsOtherMaterial = CreateForensicsMaterial(
                "Runtime_ForensicsDriverB",
                new Color(0.12f, 0.82f, 1f, 0.92f));
            forensicsVictimStableMaterial = CreateForensicsOpaqueMaterial(
                "Runtime_ForensicsDriverAStable",
                new Color(0.92f, 0.97f, 1f, 1f));
            forensicsOtherStableMaterial = CreateForensicsOpaqueMaterial(
                "Runtime_ForensicsDriverBStable",
                new Color(0.04f, 0.78f, 0.94f, 1f));
            forensicsVictimOutlineMaterial = CreateForensicsMaterial(
                "Runtime_ForensicsDriverAOutline",
                new Color(0.94f, 0.98f, 1f, 0.58f));
            forensicsOtherOutlineMaterial = CreateForensicsMaterial(
                "Runtime_ForensicsDriverBOutline",
                new Color(0.05f, 0.86f, 1f, 0.62f));
            forensicsVictimTailMaterial = CreateForensicsMaterial(
                "Runtime_ForensicsDriverATail",
                new Color(1f, 1f, 1f, 0.94f),
                true);
            forensicsOtherTailMaterial = CreateForensicsMaterial(
                "Runtime_ForensicsDriverBTail",
                new Color(0.1f, 0.88f, 1f, 0.96f),
                true);
            forensicsConnectorMaterial = CreateForensicsMaterial(
                "Runtime_ForensicsConnector",
                new Color(0.65f, 0.72f, 0.78f, 0.25f));
            forensicsBrakeMaterial = CreateForensicsMaterial(
                "Runtime_ForensicsBrake",
                new Color(1.35f, 0.78f, 0.02f, 0.94f),
                true);
            forensicsReconstructedMaterial = CreateForensicsMaterial(
                "Runtime_ForensicsReconstructed",
                new Color(1f, 0.10f, 0.06f, 0.88f),
                true);
            forensicsLensMaterial = CreateForensicsMaterial(
                "Runtime_ForensicsTimeLens",
                new Color(1.25f, 0.82f, 0.08f, 0.88f),
                true);
            forensicsStationMaterial = CreateForensicsMaterial(
                "Runtime_ForensicsStation",
                new Color(0.78f, 0.9f, 1f, 0.88f));

            forensicsVictimPath.Rail = CreateLine(
                "DriverAObservedRail",
                forensicsRailRoot,
                forensicsVictimMaterial,
                Mathf.Max(0.0001f, carWidth * 0.1f),
                true);
            forensicsOtherPath.Rail = CreateLine(
                "DriverBObservedRail",
                forensicsRailRoot,
                forensicsOtherMaterial,
                Mathf.Max(0.0001f, carWidth * 0.065f),
                true);
            forensicsVictimPath.Tail = CreateLine(
                "DriverARecentTail",
                forensicsTailRoot,
                forensicsVictimTailMaterial,
                carWidth * 0.06f,
                true);
            forensicsOtherPath.Tail = CreateLine(
                "DriverBRecentTail",
                forensicsTailRoot,
                forensicsOtherTailMaterial,
                carWidth * 0.06f,
                true);

            bool missingSerializedTrackDefaults =
                settings != null &&
                settings.forensicRoadWidthInCarWidths <= 0f &&
                settings.forensicKerbWidthInCarWidths <= 0f &&
                settings.forensicRunoffWidthInCarWidths <= 0f;
            forensicsTrackEnabled = settings == null ||
                settings.enableForensicTrack ||
                missingSerializedTrackDefaults;
            if (forensicsTrackEnabled)
            {
                forensicsUsesActualTrack = false;
                BuildForensicsTrack(settings);
                Debug.Log(
                    "RIC_ALB_TRACK_SOURCE = " +
                    "SUZUKA_INSPIRED_CINEMATIC_PROXY");
            }
            BuildStableForensicsRails();
            BuildForensicsOutlines();
            BuildForensicsStations();
            CreateForensicsLegend();
            UpdateBaseForensicsIncidentPanel();
            if (forensicsTimeLensEnabled)
                BuildForensicsTimeLens(settings);
        }

        private void CreateForensicsLegend()
        {
            GameObject legendObject = new(
                "ForensicsLegend",
                typeof(TextMeshPro));
            legendObject.transform.SetParent(forensicsRoot, false);
            legendObject.transform.localPosition = contactLocal -
                forensicsCorridorRight * Mathf.Max(
                    carWidth * 1.35f,
                    forensicsTrackOuterHalfWidth + carWidth * 0.38f) +
                Vector3.up * carWidth * 1.32f;
            legendObject.transform.localRotation =
                Quaternion.LookRotation(Vector3.up, forensicsCorridorForward);
            legendObject.transform.localScale =
                Vector3.one * carLength * 0.055f;
            forensicsLegend = legendObject.GetComponent<TextMeshPro>();
            string tier = forensicsAnalysis.Tier switch
            {
                CollisionEvidenceTier.ObservedContactAndPost =>
                    "<color=#34D8FF>OBSERVED AFTERMATH</color>",
                CollisionEvidenceTier.ObservedContactRequiresReconstruction =>
                    "<color=#FF3A32>RECONSTRUCTED</color>",
                _ => "CONTACT UNRESOLVED"
            };
            string closest = forensicsAnalysis.Tier ==
                             CollisionEvidenceTier.ContactUnresolved
                ? "CLOSEST UNRESOLVED"
                : $"CLOSEST {forensicsObservedTime}";
            forensicsLegend.text =
                (forensicsUsesActualTrack
                    ? "SUZUKA TRACK SLICE | TIME-COMPRESSED\n"
                    : "SUZUKA-INSPIRED T2-T3 PROXY | TIME-COMPRESSED\n") +
                "<color=#F1FAFF>OBSERVED</color> | " +
                "<color=#FF922E>CONTACT</color> | " + tier + "\n" +
                $"REPORTED {forensicsReportedTime} | {closest}";
            forensicsLegend.alignment = TextAlignmentOptions.Left;
            forensicsLegend.richText = true;
            forensicsLegend.fontSize = 4.8f;
            forensicsLegend.fontStyle = FontStyles.Bold;
            forensicsLegend.enableAutoSizing = false;
            forensicsLegend.color = new Color(0.82f, 0.9f, 0.98f, 0.94f);
            forensicsLegend.rectTransform.sizeDelta = new Vector2(22f, 5.6f);
            forensicsLegend.renderer.shadowCastingMode = ShadowCastingMode.Off;
            forensicsLegend.renderer.receiveShadows = false;
        }

        private void UpdateBaseForensicsIncidentPanel()
        {
            if (warningRoot == null)
                return;

            Transform panel = warningRoot.Find("IncidentPanel");
            TextMeshPro text = panel != null
                ? panel.GetComponent<TextMeshPro>()
                : null;
            if (text == null)
                return;

            string evidence = forensicsAnalysis.Tier switch
            {
                CollisionEvidenceTier.ObservedContactAndPost =>
                    "<color=#DCEFFF>OBSERVED</color>  " +
                    "<color=#FF8A24>CONTACT</color>",
                CollisionEvidenceTier.ObservedContactRequiresReconstruction =>
                    "<color=#DCEFFF>OBSERVED</color>  " +
                    "<color=#FF8A24>CONTACT</color>  " +
                    "<color=#FF3028>RECONSTRUCTED</color>",
                _ => "INCIDENT REPORTED  /  CONTACT UNRESOLVED"
            };
            text.text = $"INCIDENT\n{evidence}";
        }

        private Material CreateForensicsMaterial(
            string name,
            Color color,
            bool additive = false)
        {
            Material material = CreateTransparentMaterial(
                name,
                color,
                additive);
            forensicsMaterials.Add(material);
            return material;
        }

        private Material CreateForensicsOpaqueMaterial(
            string name,
            Color color)
        {
            Material material = CreateOpaqueMaterial(name, color);
            if (material.HasProperty("_Cull"))
                material.SetFloat("_Cull", (float)CullMode.Off);
            material.doubleSidedGI = true;
            forensicsMaterials.Add(material);
            return material;
        }

        private Material CreateForensicsTrackMaterial(
            string name,
            Color color,
            float smoothness,
            Texture baseMap = null,
            Vector2? textureTiling = null)
        {
            Shader shader = Shader.Find(
                    "Universal Render Pipeline/Simple Lit") ??
                Shader.Find("Universal Render Pipeline/Lit") ??
                Shader.Find("Universal Render Pipeline/Unlit") ??
                Shader.Find("Unlit/Color");
            Material material = new(shader)
            {
                name = name
            };
            ReplayCarVisualUtil.SetMaterialColor(material, color);
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat(
                    "_Smoothness",
                    Mathf.Clamp01(smoothness));
            }
            if (material.HasProperty("_SpecColor"))
            {
                material.SetColor(
                    "_SpecColor",
                    new Color(0.08f, 0.085f, 0.09f, 1f));
            }
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 0f);
            if (material.HasProperty("_Cull"))
                material.SetFloat("_Cull", (float)CullMode.Off);
            if (baseMap != null)
            {
                if (material.HasProperty("_BaseMap"))
                {
                    material.SetTexture("_BaseMap", baseMap);
                    material.SetTextureScale(
                        "_BaseMap",
                        textureTiling ?? Vector2.one);
                }
                if (material.HasProperty("_MainTex"))
                {
                    material.SetTexture("_MainTex", baseMap);
                    material.SetTextureScale(
                        "_MainTex",
                        textureTiling ?? Vector2.one);
                }
            }
            material.doubleSidedGI = true;
            materials.Add(material);
            forensicsMaterials.Add(material);
            return material;
        }

        private static Texture2D LoadAccidentProxyTexture(string name)
        {
            Texture2D texture = Resources.Load<Texture2D>(
                $"AccidentProxy/{name}");
            if (texture != null)
                texture.wrapMode = TextureWrapMode.Repeat;
            return texture;
        }

        private bool TryBuildActualForensicsTrack(
            CollisionShowcaseVfxSettings settings,
            IReadOnlyList<Vector3> victimMappedLocalPath,
            IReadOnlyList<Vector3> otherMappedLocalPath,
            Transform trackSourceRoot,
            Vector3 sourceCenter,
            Quaternion sourceToLocalRotation)
        {
            int count = Mathf.Min(
                victimMappedLocalPath?.Count ?? 0,
                otherMappedLocalPath?.Count ?? 0);
            count = Mathf.Min(
                count,
                Mathf.Min(
                    forensicsVictimPath?.Points?.Length ?? 0,
                    forensicsOtherPath?.Points?.Length ?? 0));
            if (count < 2 ||
                trackSourceRoot == null ||
                forensicsTrackRoot == null)
            {
                return false;
            }

            Vector3[] sourceLocalPath = new Vector3[count];
            Vector3[] targetLocalPath = new Vector3[count];
            Vector3[] sourceMappedPath = new Vector3[count];
            Quaternion localToSource =
                Quaternion.Inverse(sourceToLocalRotation);
            for (int index = 0; index < count; index++)
            {
                sourceLocalPath[index] =
                    (victimMappedLocalPath[index] +
                     otherMappedLocalPath[index]) * 0.5f;
                targetLocalPath[index] =
                    (forensicsVictimPath.Points[index] +
                     forensicsOtherPath.Points[index]) * 0.5f;
                sourceMappedPath[index] = sourceCenter +
                    localToSource * sourceLocalPath[index];
            }

            float roadHalfWidth = ResolveForensicsRoadHalfWidth(settings);
            float kerbWidth = ResolveForensicsKerbWidth(settings);
            float runoffWidth = ResolveForensicsRunoffWidth(settings);
            float outerHalfWidth =
                roadHalfWidth + kerbWidth + runoffWidth;
            float safeUniformScale = Mathf.Max(
                0.0001f,
                forensicsCorridorScale);
            float sourceLateralPadding =
                outerHalfWidth / safeUniformScale;
            float sourceLongitudinalPadding =
                ResolveStageLocalMeters(
                    forensicsCorridorForward,
                    0.08f).magnitude /
                safeUniformScale;
            float sourcePathLength = 0f;
            for (int index = 1;
                 index < sourceMappedPath.Length;
                 index++)
            {
                sourcePathLength += Vector3.Distance(
                    sourceMappedPath[index - 1],
                    sourceMappedPath[index]);
            }
            EventTrackSegment segment = new();
            if (!segment.Build(
                    forensicsTrackRoot,
                    trackSourceRoot,
                    sourceMappedPath,
                    sourceCenter,
                    sourceToLocalRotation,
                    sourceLateralPadding,
                    sourceLongitudinalPadding,
                    EventTrackSegmentSurfaceMode.TrackContextOnly,
                    out _) ||
                !segment.ApplyUniformScale(
                    forensicsCorridorScale,
                    contactLocal,
                    out Bounds scaledBounds))
            {
                segment.Clear();
                Debug.LogWarning(
                    "[CollisionForensics] Actual Suzuka track context was " +
                    "unavailable; generic track fallback remains disabled.");
                return false;
            }

            forensicsActualTrackSegment = segment;
            forensicsTrackOuterHalfWidth = outerHalfWidth;
            LogActualSuzukaRenderAudit();
            Debug.Log(
                "RIC_ALB_TRACK_SOURCE = ACTUAL_SUZUKA");
            Debug.Log(
                "[CollisionForensics] Actual Suzuka track slice cached " +
                "with intact ROAD01 GPU clipping. " +
                $"localBounds={scaledBounds.size:F4}, " +
                $"worldBounds={ResolveAccidentTrackWorldSize(scaledBounds):F3}, " +
                $"uniformScale={safeUniformScale:0.000000}, " +
                $"sourceRenderer={string.Join(",", segment.SourceRendererNames)}, " +
                $"sourceSubmesh={segment.SourceRoadSubmesh}, " +
                $"sourceVertices={segment.SourceRoadVertexCount}, " +
                $"sourceIndices={segment.SourceRoadIndexCount}, " +
                "selection=FULL_ROAD01_INDEX_BUFFER, " +
                "cpuTriangleCropping=False, " +
                $"sourceProgressWindow=" +
                $"[-{sourceLongitudinalPadding:0.000000}," +
                $"{sourcePathLength + sourceLongitudinalPadding:0.000000}], " +
                $"clipBoxes={DescribeSuzukaClipBoxes(segment, safeUniformScale)}, " +
                "grooveEnabled=False, " +
                $"convertedMaterials={segment.ConvertedMaterialCount}, " +
                $"unsupportedSourceMaterials=" +
                $"{segment.UnsupportedSourceMaterialCount}, " +
                $"maxTriangleEdgeWorld=" +
                $"{MeasurementAxisWorldMeters(Vector3.right, segment.MaximumTrackTriangleEdge * safeUniformScale):0.000}m, " +
                $"SuzukaAccidentFrame=" +
                $"scale:{safeUniformScale:0.000000}/" +
                $"rotation:{suzukaAccidentSourceRotation.eulerAngles:F2}/" +
                $"sourceCenter:{suzukaAccidentSourceCenter:F5}/" +
                $"contactAnchor:{contactLocal:F5}.");
            return true;
        }

        private string DescribeSuzukaClipBoxes(
            EventTrackSegment segment,
            float uniformScale)
        {
            List<string> descriptions = new();
            int count = Mathf.Min(
                segment.ClipBoxSizes.Count,
                segment.ClipBoxYawDegrees.Count);
            for (int index = 0; index < count; index++)
            {
                Vector3 size = segment.ClipBoxSizes[index] * uniformScale;
                Vector3 worldSize = stage != null
                    ? new Vector3(
                        stage.TransformVector(
                            Vector3.right * size.x).magnitude,
                        stage.TransformVector(
                            Vector3.up * size.y).magnitude,
                        stage.TransformVector(
                            Vector3.forward * size.z).magnitude)
                    : size;
                descriptions.Add(
                    $"B{index}:{worldSize:F2}m@" +
                    $"{segment.ClipBoxYawDegrees[index]:+0.0;-0.0;0.0}deg");
            }
            return string.Join("|", descriptions);
        }

        private void LogActualSuzukaRenderAudit()
        {
            if (forensicsTrackRoot == null)
                return;

            Renderer[] renderers = forensicsTrackRoot
                .GetComponentsInChildren<Renderer>(true);
            int materialSlots = 0;
            int magentaRiskMaterials = 0;
            bool hasBounds = false;
            Bounds worldBounds = default;
            for (int rendererIndex = 0;
                 rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                if (renderer == null)
                    continue;
                if (!hasBounds)
                {
                    worldBounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    worldBounds.Encapsulate(renderer.bounds);
                }

                Material[] materials = renderer.sharedMaterials;
                materialSlots += materials.Length;
                List<string> rendererMaterialNames = new();
                for (int materialIndex = 0;
                     materialIndex < materials.Length;
                     materialIndex++)
                {
                    Material material = materials[materialIndex];
                    Shader shader = material != null
                        ? material.shader
                        : null;
                    if (material == null ||
                        shader == null ||
                        !shader.isSupported ||
                        shader.name.Contains("InternalErrorShader"))
                    {
                        magentaRiskMaterials++;
                    }

                    string materialName = material != null
                        ? material.name
                        : "MissingMaterial";
                    string shaderName = shader != null
                        ? shader.name
                        : "MissingShader";
                    rendererMaterialNames.Add(
                        $"{materialName}/{shaderName}");
                }

                string rendererMaterials = string.Join(
                    ",",
                    rendererMaterialNames);
                Debug.Log(
                    $"[CollisionTrackRendererAudit] name={renderer.name}, " +
                    $"worldBounds={renderer.bounds.size:F3}, " +
                    $"materials={rendererMaterials}.");
            }

            Debug.Log(
                $"[CollisionTrackAudit] renderers={renderers.Length}, " +
                $"materialSlots={materialSlots}, " +
                $"magentaRiskMaterials={magentaRiskMaterials}, " +
                $"worldBounds={(hasBounds ? worldBounds.size : Vector3.zero):F3}.");
        }

        private Vector3 ResolveAccidentTrackWorldSize(Bounds localBounds)
        {
            if (stage == null)
                return localBounds.size;
            return new Vector3(
                stage.TransformVector(
                    Vector3.right * localBounds.size.x).magnitude,
                stage.TransformVector(
                    Vector3.up * localBounds.size.y).magnitude,
                stage.TransformVector(
                    Vector3.forward * localBounds.size.z).magnitude);
        }

        private void BuildActualForensicsTrackWarning(
            IReadOnlyList<Vector3> centers,
            float roadHalfWidth,
            float kerbOuter)
        {
            int count = centers?.Count ?? 0;
            if (count < 2)
                return;

            Vector3[] leftRoadEdge = new Vector3[count];
            Vector3[] rightRoadEdge = new Vector3[count];
            Vector3[] leftKerbEdge = new Vector3[count];
            Vector3[] rightKerbEdge = new Vector3[count];
            int closestContactIndex = 0;
            float closestContactDistance = float.PositiveInfinity;
            Vector3 previousRight = forensicsCorridorRight;
            for (int index = 0; index < count; index++)
            {
                int before = Mathf.Max(0, index - 1);
                int after = Mathf.Min(count - 1, index + 1);
                Vector3 tangent = FlattenNormalized(
                    centers[after] - centers[before],
                    forensicsCorridorForward);
                Vector3 right = FlattenNormalized(
                    Vector3.Cross(Vector3.up, tangent),
                    previousRight);
                if (index > 0 && Vector3.Dot(right, previousRight) < 0f)
                    right = -right;
                previousRight = right;
                Vector3 lift = Vector3.up * carWidth * 0.03f;
                leftRoadEdge[index] =
                    centers[index] - right * roadHalfWidth + lift;
                rightRoadEdge[index] =
                    centers[index] + right * roadHalfWidth + lift;
                leftKerbEdge[index] =
                    centers[index] - right * kerbOuter + lift;
                rightKerbEdge[index] =
                    centers[index] + right * kerbOuter + lift;
                float distance =
                    (centers[index] - contactLocal).sqrMagnitude;
                if (distance < closestContactDistance)
                {
                    closestContactDistance = distance;
                    closestContactIndex = index;
                }
            }

            BuildForensicsTrackWarning(
                leftRoadEdge,
                rightRoadEdge,
                leftKerbEdge,
                rightKerbEdge,
                closestContactIndex);
        }

        private float ResolveForensicsRoadHalfWidth(
            CollisionShowcaseVfxSettings settings)
        {
            float configured = settings != null
                ? settings.forensicRoadWidthInCarWidths
                : 5.4f;
            return (configured > 0f ? configured : 5.4f) *
                carWidth * 0.5f;
        }

        private float ResolveForensicsKerbWidth(
            CollisionShowcaseVfxSettings settings)
        {
            float configured = settings != null
                ? settings.forensicKerbWidthInCarWidths
                : 0.42f;
            return (configured > 0f ? configured : 0.42f) * carWidth;
        }

        private float ResolveForensicsRunoffWidth(
            CollisionShowcaseVfxSettings settings)
        {
            float configured = settings != null
                ? settings.forensicRunoffWidthInCarWidths
                : 0.75f;
            return Mathf.Max(
                configured > 0f ? configured : 0.75f,
                2.0f) * carWidth;
        }

        private float ResolveForensicsGrassWidth()
        {
            return carWidth * 3.0f;
        }

        private void BuildForensicsTrack(
            CollisionShowcaseVfxSettings settings)
        {
            if (forensicsTrackRoot == null)
            {
                forensicsTrackEnabled = false;
                return;
            }

            List<Vector3> centerList =
                BuildSuzukaInspiredProxyCenterline();
            int count = centerList.Count;
            if (count < 2)
            {
                forensicsTrackEnabled = false;
                return;
            }

            float configuredRoadWidth = settings != null
                ? settings.forensicRoadWidthInCarWidths
                : 5.4f;
            float configuredKerbWidth = settings != null
                ? settings.forensicKerbWidthInCarWidths
                : 0.42f;
            float configuredRunoffWidth = settings != null
                ? settings.forensicRunoffWidthInCarWidths
                : 0.75f;
            float roadWidth = (configuredRoadWidth > 0f
                ? configuredRoadWidth
                : 5.4f) * carWidth;
            float kerbWidth = (configuredKerbWidth > 0f
                ? configuredKerbWidth
                : 0.42f) * carWidth;
            float runoffWidth = (configuredRunoffWidth > 0f
                ? configuredRunoffWidth
                : 0.75f) * carWidth;
            runoffWidth = Mathf.Max(runoffWidth, carWidth * 2.0f);
            float grassWidth = ResolveForensicsGrassWidth();
            float roadHalfWidth = roadWidth * 0.5f;
            float kerbOuter = roadHalfWidth + kerbWidth;
            float runoffOuter = kerbOuter + runoffWidth;
            float blueGroundExtent =
                accidentVictimResponseOffset.magnitude +
                carWidth * 0.8f;
            forensicsTrackOuterHalfWidth = Mathf.Max(
                runoffOuter + grassWidth,
                ResolveStageLocalMeters(
                    forensicsCorridorRight,
                    1.25f).magnitude,
                blueGroundExtent);
            float edgeWidth = Mathf.Min(
                roadHalfWidth * 0.12f,
                Mathf.Max(carWidth * 0.055f, roadWidth * 0.014f));
            float[] lateralOffsets =
            {
                -forensicsTrackOuterHalfWidth,
                -runoffOuter,
                -kerbOuter,
                -roadHalfWidth,
                -roadHalfWidth + edgeWidth,
                roadHalfWidth - edgeWidth,
                roadHalfWidth,
                kerbOuter,
                runoffOuter,
                forensicsTrackOuterHalfWidth
            };

            Vector3[] centers = centerList.ToArray();
            count = centers.Length;
            Vector3[] rights = new Vector3[count];
            float[] cumulative = new float[count];
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    cumulative[i] = cumulative[i - 1] +
                        Vector3.Distance(centers[i - 1], centers[i]);
                }
            }
            for (int i = 0; i < count; i++)
            {
                int before = Mathf.Max(0, i - 1);
                int after = Mathf.Min(count - 1, i + 1);
                Vector3 tangent = FlattenNormalized(
                    centers[after] - centers[before],
                    forensicsCorridorForward);
                Vector3 right = FlattenNormalized(
                    Vector3.Cross(Vector3.up, tangent),
                    i > 0 ? rights[i - 1] : forensicsCorridorRight);
                if (i > 0 && Vector3.Dot(right, rights[i - 1]) < 0f)
                    right = -right;
                rights[i] = right;
            }

            const int verticesPerRow = 10;
            Vector3[] vertices = new Vector3[count * verticesPerRow];
            Vector3[] normals = new Vector3[vertices.Length];
            Vector2[] uv = new Vector2[vertices.Length];
            float lift = carWidth * 0.012f;
            float localToWorldMeters = stage != null
                ? stage.TransformVector(forensicsCorridorForward).magnitude
                : 1f;
            localToWorldMeters = Mathf.Max(0.0001f, localToWorldMeters);
            for (int i = 0; i < count; i++)
            {
                for (int band = 0; band < verticesPerRow; band++)
                {
                    int index = i * verticesPerRow + band;
                    float kerbLift = band == 3 || band == 6
                        ? carWidth * 0.026f
                        : band == 2 || band == 7
                            ? carWidth * 0.012f
                            : 0f;
                    vertices[index] = centers[i] +
                        rights[i] * lateralOffsets[band] +
                        Vector3.up * (lift + kerbLift);
                    normals[index] = Vector3.up;
                    uv[index] = new Vector2(
                        (lateralOffsets[band] + roadHalfWidth) /
                        roadWidth,
                        cumulative[i] * localToWorldMeters);
                }
            }

            List<int>[] triangles = new List<int>[ForensicsTrackSubmeshCount];
            for (int i = 0; i < triangles.Length; i++)
                triangles[i] = new List<int>((count - 1) * 12);
            float stripeLength = Mathf.Max(
                carWidth * 0.6f,
                carLength * 0.42f);
            for (int segment = 0; segment < count - 1; segment++)
            {
                AddForensicsTrackQuad(
                    triangles[5], segment, 0, 1, verticesPerRow);
                AddForensicsTrackQuad(
                    triangles[4], segment, 1, 2, verticesPerRow);
                AddForensicsTrackQuad(
                    triangles[4], segment, 7, 8, verticesPerRow);
                AddForensicsTrackQuad(
                    triangles[5], segment, 8, 9, verticesPerRow);
                float trackProgress = cumulative[count - 1] > 0.0001f
                    ? (cumulative[segment] + cumulative[segment + 1]) *
                      0.5f / cumulative[count - 1]
                    : 0f;
                bool showKerb = trackProgress <= 0.30f ||
                    trackProgress >= 0.58f;
                int kerbSubmesh = showKerb
                    ? Mathf.FloorToInt(
                        (cumulative[segment] + cumulative[segment + 1]) *
                        0.5f / stripeLength) % 2 == 0
                            ? 1
                            : 2
                    : 4;
                AddForensicsTrackQuad(
                    triangles[kerbSubmesh], segment, 2, 3, verticesPerRow);
                AddForensicsTrackQuad(
                    triangles[kerbSubmesh], segment, 6, 7, verticesPerRow);
                AddForensicsTrackQuad(
                    triangles[3], segment, 3, 4, verticesPerRow);
                AddForensicsTrackQuad(
                    triangles[3], segment, 5, 6, verticesPerRow);
                AddForensicsTrackQuad(
                    triangles[0], segment, 4, 5, verticesPerRow);
            }

            forensicsTrackMesh = new Mesh
            {
                name = "Runtime_CollisionForensicTrackSlice"
            };
            forensicsTrackMesh.vertices = vertices;
            forensicsTrackMesh.normals = normals;
            forensicsTrackMesh.uv = uv;
            forensicsTrackMesh.subMeshCount = ForensicsTrackSubmeshCount;
            forensicsTrackTriangles = new int[ForensicsTrackSubmeshCount][];
            for (int i = 0; i < ForensicsTrackSubmeshCount; i++)
            {
                forensicsTrackTriangles[i] = triangles[i].ToArray();
                forensicsTrackMesh.SetTriangles(
                    forensicsTrackTriangles[i],
                    i,
                    false);
            }
            forensicsTrackMesh.RecalculateBounds();
            meshes.Add(forensicsTrackMesh);
            forensicsMeshes.Add(forensicsTrackMesh);

            Texture2D roadTexture = LoadAccidentProxyTexture(
                "SuzukaRoad01");
            Texture2D kerbTexture = LoadAccidentProxyTexture(
                "SuzukaKerb01");
            Texture2D gravelTexture = LoadAccidentProxyTexture(
                "SuzukaGravel01");
            Texture2D grassTexture = LoadAccidentProxyTexture(
                "SuzukaGrassSeamless01");
            forensicsTrackAsphaltMaterial = CreateForensicsTrackMaterial(
                "ProxySuzukaAsphalt_URP",
                new Color(0.24f, 0.255f, 0.27f, 1f),
                0.12f,
                roadTexture,
                new Vector2(1f, 0.85f));
            forensicsTrackKerbRedMaterial = CreateForensicsTrackMaterial(
                "ProxySuzukaKerbRed_URP",
                new Color(0.58f, 0.095f, 0.075f, 1f),
                0.16f,
                kerbTexture,
                new Vector2(6f, 2.4f));
            forensicsTrackKerbWhiteMaterial = CreateForensicsTrackMaterial(
                "ProxySuzukaKerbWhite_URP",
                new Color(0.78f, 0.775f, 0.73f, 1f),
                0.14f,
                kerbTexture,
                new Vector2(6f, 2.4f));
            forensicsTrackEdgeMaterial = CreateForensicsTrackMaterial(
                "Runtime_ForensicsTrackEdge",
                new Color(0.014f, 0.018f, 0.021f, 1f),
                0.04f);
            forensicsTrackRunoffMaterial = CreateForensicsTrackMaterial(
                "ProxySuzukaRunoffGravel_URP",
                new Color(0.31f, 0.30f, 0.265f, 1f),
                0.04f,
                gravelTexture,
                new Vector2(2.6f, 1.8f));
            forensicsTrackGrassMaterial = CreateForensicsTrackMaterial(
                "ProxySuzukaGrass_URP",
                new Color(0.19f, 0.31f, 0.17f, 1f),
                0.03f,
                grassTexture,
                new Vector2(2.2f, 1.35f));
            GameObject trackObject = new(
                "SuzukaT2ToT3InspiredAccidentPresentationTrack",
                typeof(MeshFilter),
                typeof(MeshRenderer));
            trackObject.transform.SetParent(forensicsTrackRoot, false);
            trackObject.GetComponent<MeshFilter>().sharedMesh =
                forensicsTrackMesh;
            MeshRenderer renderer = trackObject.GetComponent<MeshRenderer>();
            renderer.sharedMaterials = new[]
            {
                forensicsTrackAsphaltMaterial,
                forensicsTrackKerbRedMaterial,
                forensicsTrackKerbWhiteMaterial,
                forensicsTrackEdgeMaterial,
                forensicsTrackRunoffMaterial,
                forensicsTrackGrassMaterial
            };
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            renderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            Debug.Log(
                "[CollisionVisualPolish] Suzuka source textures " +
                $"ROAD01={(roadTexture != null)}, " +
                $"RDCP01={(kerbTexture != null)}, " +
                $"GRVL01={(gravelTexture != null)}, " +
                $"GRASS11={(grassTexture != null)}; " +
                "shader=URP/Simple Lit fallback chain, " +
                "UV=road-lateral/centerline-world-arc, " +
                "tiling asphalt=(1.00,0.85), kerb=(6.00,2.40), " +
                "runoff=(2.60,1.80), grass=(2.20,1.35).");

            Material wearMaterial = CreateForensicsTrackMaterial(
                "Runtime_ForensicsTrackRacingWear",
                new Color(0.018f, 0.021f, 0.025f, 1f),
                0.08f);
            float wearWidth = carWidth * 0.34f;
            float wearOffset = roadWidth * 0.13f;
            BuildProxySurfaceRibbon(
                "RacingWearLeft",
                centers,
                rights,
                -wearOffset,
                wearWidth,
                lift + carWidth * 0.004f,
                wearMaterial);
            BuildProxySurfaceRibbon(
                "RacingWearRight",
                centers,
                rights,
                wearOffset,
                wearWidth,
                lift + carWidth * 0.004f,
                wearMaterial);

            Vector3[] leftRoadEdge = new Vector3[count];
            Vector3[] rightRoadEdge = new Vector3[count];
            Vector3[] leftKerbEdge = new Vector3[count];
            Vector3[] rightKerbEdge = new Vector3[count];
            int closestContactIndex = 0;
            float closestContactDistance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                leftRoadEdge[i] = vertices[i * verticesPerRow + 3] +
                    Vector3.up * carWidth * 0.03f;
                rightRoadEdge[i] = vertices[i * verticesPerRow + 6] +
                    Vector3.up * carWidth * 0.03f;
                leftKerbEdge[i] = vertices[i * verticesPerRow + 2] +
                    Vector3.up * carWidth * 0.03f;
                rightKerbEdge[i] = vertices[i * verticesPerRow + 7] +
                    Vector3.up * carWidth * 0.03f;
                float distance = (centers[i] - contactLocal).sqrMagnitude;
                if (distance < closestContactDistance)
                {
                    closestContactDistance = distance;
                    closestContactIndex = i;
                }
            }
            BuildForensicsTrackWarning(
                leftRoadEdge,
                rightRoadEdge,
                leftKerbEdge,
                rightKerbEdge,
                closestContactIndex);
            BuildSuzukaOutsideBarrier(
                centers,
                forensicsTrackOuterHalfWidth);
            BuildBlueAftermathContext();
            SetForensicsTrackProgress(0f);
            LogSuzukaInspiredProxyTrackAudit(
                centers,
                cumulative,
                roadHalfWidth,
                roadWidth,
                renderer.bounds);
        }

        private List<Vector3> BuildSuzukaInspiredProxyCenterline()
        {
            const int approachSamples = 32;
            List<Vector3> centers = new(approachSamples + 16);
            float start = accidentApproachStagingConfigured
                ? accidentApproachStagingStartTime
                : forensicsVisibleStartTime;
            float end = accidentVisualContactResolved
                ? accidentVisualContactTime
                : accidentOriginalContactTime;
            for (int index = 0; index < approachSamples; index++)
            {
                float progress = index / (approachSamples - 1f);
                float time = Mathf.Lerp(start, end, progress);
                if (!TryGetFinalPresentedVehiclePose(
                        forensicsVictimPath,
                        true,
                        time,
                        out Vector3 victimPoint,
                        out _) ||
                    !TryGetFinalPresentedVehiclePose(
                        forensicsOtherPath,
                        false,
                        time,
                        out Vector3 otherPoint,
                        out _))
                {
                    continue;
                }

                Vector3 center = (victimPoint + otherPoint) * 0.5f;
                center.y = contactLocal.y;
                centers.Add(center);
            }

            if (centers.Count < 2)
                return centers;

            SmoothProxyCenterline(centers, 2);
            ExtendSuzukaProxyBeforeApproach(centers);
            ExtendSuzukaProxyAfterContact(centers);
            return centers;
        }

        private static void SmoothProxyCenterline(
            List<Vector3> centers,
            int passes)
        {
            if (centers == null || centers.Count < 3)
                return;

            Vector3[] source = centers.ToArray();
            for (int pass = 0; pass < passes; pass++)
            {
                for (int index = 1; index < source.Length - 1; index++)
                {
                    centers[index] = source[index - 1] * 0.25f +
                        source[index] * 0.5f +
                        source[index + 1] * 0.25f;
                }
                centers.CopyTo(source);
            }
        }

        private void ExtendSuzukaProxyBeforeApproach(
            List<Vector3> centers)
        {
            if (centers == null || centers.Count < 3)
                return;

            Vector3 tangent = FlattenNormalized(
                centers[2] - centers[0],
                forensicsCorridorForward);
            Vector3 right = FlattenNormalized(
                Vector3.Cross(Vector3.up, tangent),
                forensicsCorridorRight);
            float extension = ResolveStageLocalMeters(
                tangent,
                0.24f).magnitude;
            float sweep = ResolveStageLocalMeters(
                right,
                0.09f).magnitude;
            Vector3 join = centers[0];
            const int segments = 5;
            for (int index = segments; index >= 1; index--)
            {
                float progress = index / (float)segments;
                centers.Insert(
                    0,
                    join - tangent * extension * progress +
                    right * sweep * progress * progress);
            }
        }

        private void ExtendSuzukaProxyAfterContact(
            List<Vector3> centers)
        {
            if (centers == null || centers.Count < 4)
                return;

            Vector3 tangent = FlattenNormalized(
                centers[centers.Count - 1] -
                centers[centers.Count - 4],
                forensicsCorridorForward);
            Vector3 entryTangent = FlattenNormalized(
                centers[Mathf.Min(centers.Count - 1, 10)] -
                centers[0],
                tangent);
            float entryTurn = Vector3.SignedAngle(
                entryTangent,
                tangent,
                Vector3.up);
            float turnSign = entryTurn >= 0f ? -1f : 1f;
            Vector3 right = FlattenNormalized(
                Vector3.Cross(Vector3.up, tangent),
                forensicsCorridorRight);
            float extension = ResolveStageLocalMeters(
                tangent,
                0.72f).magnitude;
            float sweep = ResolveStageLocalMeters(
                right,
                0.23f).magnitude;

            const int extensionSegments = 10;
            Vector3 start = centers[centers.Count - 1];
            for (int i = 1; i <= extensionSegments; i++)
            {
                float progress = i / (float)extensionSegments;
                float lateral = Mathf.SmoothStep(
                    0f,
                    sweep,
                    progress);
                centers.Add(
                    start + tangent * extension * progress +
                    right * turnSign * lateral);
            }
        }

        private void LogSuzukaInspiredProxyTrackAudit(
            IReadOnlyList<Vector3> centers,
            IReadOnlyList<float> cumulative,
            float roadHalfWidth,
            float roadWidth,
            Bounds asphaltBounds)
        {
            if (centers == null || centers.Count < 2)
                return;

            float worldLength = 0f;
            float maximumSegmentWorld = 0f;
            for (int index = 1; index < centers.Count; index++)
            {
                Vector3 localDelta = centers[index] - centers[index - 1];
                float worldSegment = stage != null
                    ? stage.TransformVector(localDelta).magnitude
                    : localDelta.magnitude;
                worldLength += worldSegment;
                maximumSegmentWorld = Mathf.Max(
                    maximumSegmentWorld,
                    worldSegment);
            }

            float worldRoadWidth = stage != null
                ? stage.TransformVector(
                    forensicsCorridorRight * roadWidth).magnitude
                : roadWidth;
            Bounds visibleBounds = asphaltBounds;
            Renderer[] renderers = forensicsTrackRoot != null
                ? forensicsTrackRoot.GetComponentsInChildren<Renderer>(true)
                : System.Array.Empty<Renderer>();
            for (int index = 0; index < renderers.Length; index++)
            {
                if (renderers[index] != null)
                    visibleBounds.Encapsulate(renderers[index].bounds);
            }

            Debug.Log(
                "[CollisionProxyTrack] " +
                "identity=Suzuka T2-to-T3 inspired accident " +
                "presentation track, geometryExactSuzuka=False, " +
                "source=TryGetFinalPresentedVehiclePose, " +
                $"centerlineSamples={centers.Count}, " +
                $"length={worldLength:0.000}m, " +
                $"asphaltWidth={worldRoadWidth:0.000}m, " +
                $"maxSegment={maximumSegmentWorld:0.000}m, " +
                $"localProgressLength=" +
                $"{(cumulative.Count > 0 ? cumulative[cumulative.Count - 1] : 0f):0.0000}, " +
                $"contactLocal={contactLocal:F4}, " +
                $"asphaltBounds={asphaltBounds.size:F3}, " +
                $"visibleBounds={visibleBounds.size:F3}, " +
                "actualSuzuka=False, oldRectangularTrack=False, " +
                "barrier=ENABLED.");

            float[] checkpoints =
            {
                0f,
                0.25f,
                0.50f,
                0.75f,
                0.90f,
                1f
            };
            float start = accidentApproachStagingConfigured
                ? accidentApproachStagingStartTime
                : forensicsVisibleStartTime;
            float contact = accidentVisualContactResolved
                ? accidentVisualContactTime
                : accidentOriginalContactTime;
            for (int index = 0; index < checkpoints.Length; index++)
            {
                float progress = checkpoints[index];
                float time = progress >= 1f
                    ? Mathf.Max(
                        start,
                        contact - CinematicContactSearchStepSeconds)
                    : Mathf.Lerp(start, contact, progress);
                if (!TryGetFinalPresentedVehiclePose(
                        forensicsVictimPath,
                        true,
                        time,
                        out Vector3 victimPoint,
                        out Vector3 victimTangent) ||
                    !TryGetFinalPresentedVehiclePose(
                        forensicsOtherPath,
                        false,
                        time,
                        out Vector3 otherPoint,
                        out Vector3 otherTangent))
                {
                    continue;
                }

                float victimDistance = DistanceToProxyCenterline(
                    victimPoint,
                    centers,
                    out Vector3 victimTrackTangent);
                float otherDistance = DistanceToProxyCenterline(
                    otherPoint,
                    centers,
                    out Vector3 otherTrackTangent);
                float victimHeading = Vector3.Angle(
                    victimTangent,
                    victimTrackTangent);
                float otherHeading = Vector3.Angle(
                    otherTangent,
                    otherTrackTangent);
                bool victimInside = victimDistance <= roadHalfWidth;
                bool otherInside = otherDistance <= roadHalfWidth;
                Debug.Log(
                    "[CollisionProxyTrackAlignment] " +
                    $"progress={progress:0.00}, " +
                    $"time={time:0.000000}, " +
                    $"BLUEOffset={ResolveLocalDistanceWorldMeters(victimDistance):0.000}m, " +
                    $"REDOffset={ResolveLocalDistanceWorldMeters(otherDistance):0.000}m, " +
                    $"halfWidth={ResolveLocalDistanceWorldMeters(roadHalfWidth):0.000}m, " +
                    $"BLUEInside={victimInside}, REDInside={otherInside}, " +
                    $"headingDelta={victimHeading:0.0}/{otherHeading:0.0}deg.");
            }
        }

        private float ResolveLocalDistanceWorldMeters(float localDistance)
        {
            return stage != null
                ? stage.TransformVector(
                    forensicsCorridorRight * localDistance).magnitude
                : localDistance;
        }

        private static float DistanceToProxyCenterline(
            Vector3 point,
            IReadOnlyList<Vector3> centers,
            out Vector3 tangent)
        {
            tangent = Vector3.forward;
            float closestSquared = float.PositiveInfinity;
            Vector3 flatPoint = point;
            flatPoint.y = 0f;
            for (int index = 0; index < centers.Count - 1; index++)
            {
                Vector3 start = centers[index];
                Vector3 end = centers[index + 1];
                start.y = 0f;
                end.y = 0f;
                Vector3 segment = end - start;
                float lengthSquared = segment.sqrMagnitude;
                if (lengthSquared <= 0.0000001f)
                    continue;
                float progress = Mathf.Clamp01(
                    Vector3.Dot(flatPoint - start, segment) /
                    lengthSquared);
                Vector3 closest = start + segment * progress;
                float squared = (flatPoint - closest).sqrMagnitude;
                if (squared >= closestSquared)
                    continue;
                closestSquared = squared;
                tangent = segment.normalized;
            }
            return Mathf.Sqrt(closestSquared);
        }

        private void BuildProxySurfaceRibbon(
            string name,
            IReadOnlyList<Vector3> centers,
            IReadOnlyList<Vector3> rights,
            float centerOffset,
            float width,
            float lift,
            Material material)
        {
            int count = Mathf.Min(
                centers?.Count ?? 0,
                rights?.Count ?? 0);
            if (count < 2 || material == null || forensicsTrackRoot == null)
                return;

            Vector3[] vertices = new Vector3[count * 2];
            Vector3[] normals = new Vector3[vertices.Length];
            Vector2[] uv = new Vector2[vertices.Length];
            int[] triangles = new int[(count - 1) * 6];
            float halfWidth = Mathf.Max(0.00001f, width * 0.5f);
            float cumulative = 0f;
            for (int index = 0; index < count; index++)
            {
                if (index > 0)
                {
                    Vector3 segment =
                        centers[index] - centers[index - 1];
                    cumulative += stage != null
                        ? stage.TransformVector(segment).magnitude
                        : segment.magnitude;
                }
                Vector3 center = centers[index] +
                    rights[index] * centerOffset +
                    Vector3.up * lift;
                vertices[index * 2] = center - rights[index] * halfWidth;
                vertices[index * 2 + 1] = center + rights[index] * halfWidth;
                normals[index * 2] = Vector3.up;
                normals[index * 2 + 1] = Vector3.up;
                uv[index * 2] = new Vector2(0f, cumulative);
                uv[index * 2 + 1] = new Vector2(1f, cumulative);
                if (index >= count - 1)
                    continue;
                int triangle = index * 6;
                int vertex = index * 2;
                triangles[triangle] = vertex;
                triangles[triangle + 1] = vertex + 2;
                triangles[triangle + 2] = vertex + 3;
                triangles[triangle + 3] = vertex;
                triangles[triangle + 4] = vertex + 3;
                triangles[triangle + 5] = vertex + 1;
            }

            Mesh mesh = new()
            {
                name = $"Runtime_{name}Mesh",
                vertices = vertices,
                normals = normals,
                uv = uv,
                triangles = triangles
            };
            mesh.RecalculateBounds();
            meshes.Add(mesh);
            forensicsMeshes.Add(mesh);
            GameObject ribbon = new(
                name,
                typeof(MeshFilter),
                typeof(MeshRenderer));
            ribbon.transform.SetParent(forensicsTrackRoot, false);
            ribbon.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = ribbon.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            renderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
        }

        private void BuildBlueAftermathContext()
        {
            if (forensicsTrackRoot == null ||
                accidentVictimResponseOffset.sqrMagnitude <= 0.000001f)
            {
                return;
            }

            const int skidSamples = 10;
            Vector3[] skidCenters = new Vector3[skidSamples];
            Vector3[] skidRights = new Vector3[skidSamples];
            Vector3 start = accidentVictimContactPoint;
            for (int index = 0; index < skidSamples; index++)
            {
                float progress = index / (skidSamples - 1f);
                float responseProgress = Mathf.Lerp(0f, 0.45f, progress);
                float eased = Mathf.SmoothStep(
                    0f,
                    1f,
                    responseProgress);
                skidCenters[index] = start +
                    accidentVictimResponseOffset * eased;
                skidCenters[index].y = contactLocal.y;
            }
            for (int index = 0; index < skidSamples; index++)
            {
                int before = Mathf.Max(0, index - 1);
                int after = Mathf.Min(skidSamples - 1, index + 1);
                Vector3 tangent = FlattenNormalized(
                    skidCenters[after] - skidCenters[before],
                    accidentVictimContactTangent);
                skidRights[index] = FlattenNormalized(
                    Vector3.Cross(Vector3.up, tangent),
                    forensicsCorridorRight);
            }

            Material skid = CreateForensicsTrackMaterial(
                "Runtime_BlueAftermathSkid",
                new Color(0.009f, 0.011f, 0.012f, 1f),
                0.01f);
            float skidWidth = ResolveStageLocalMeters(
                skidRights[0],
                0.018f).magnitude;
            float skidOffset = carWidth * 0.24f;
            BuildProxySurfaceRibbon(
                "BlueAftermathSkidLeft",
                skidCenters,
                skidRights,
                -skidOffset,
                skidWidth,
                carWidth * 0.024f,
                skid);
            BuildProxySurfaceRibbon(
                "BlueAftermathSkidRight",
                skidCenters,
                skidRights,
                skidOffset,
                skidWidth,
                carWidth * 0.024f,
                skid);

            Vector3 responseForward = FlattenNormalized(
                accidentVictimResponseOffset,
                accidentVictimContactTangent);
            Vector3 responseRight = FlattenNormalized(
                Vector3.Cross(Vector3.up, responseForward),
                forensicsCorridorRight);
            BuildBlueDestinationBarrier(
                start + accidentVictimResponseOffset,
                responseRight);
            Vector3 worldOffset = stage != null
                ? stage.TransformVector(accidentVictimResponseOffset)
                : accidentVictimResponseOffset;
            Debug.Log(
                "[CollisionEnvironmentCleanup] BLUE offset=" +
                $"{worldOffset:F3}m, specialGravelLane=False, " +
                "landingPad=False, shortExitSkids=True, " +
                $"grassOuterExtent=" +
                $"{ResolveLocalDistanceWorldMeters(forensicsTrackOuterHalfWidth):0.000}m.");
        }

        private void BuildBlueDestinationBarrier(
            Vector3 destination,
            Vector3 right)
        {
            if (forensicsTrackRoot == null)
                return;

            if (forensicsTrackBarrierMaterial == null)
            {
                forensicsTrackBarrierMaterial =
                    CreateForensicsOpaqueMaterial(
                        "Runtime_SuzukaTyreBarrier",
                        new Color(0.055f, 0.062f, 0.07f, 1f));
            }

            Vector3 forward = FlattenNormalized(
                Vector3.Cross(right, Vector3.up),
                accidentVictimContactTangent);
            Transform barrierRoot = CreateRoot(
                "BlueDestinationBarrierContext",
                forensicsTrackRoot);
            const int packCount = 5;
            float packWidth = ResolveStageLocalMeters(right, 0.10f).magnitude;
            float height = carWidth * 0.42f;
            float depth = carWidth * 0.52f;
            Vector3 barrierCenter = destination +
                forward * ResolveStageLocalMeters(forward, 0.20f).magnitude;
            for (int index = 0; index < packCount; index++)
            {
                float centered = index - (packCount - 1) * 0.5f;
                GameObject pack = GameObject.CreatePrimitive(
                    PrimitiveType.Cube);
                pack.name = $"BlueBarrierPack_{index:00}";
                pack.transform.SetParent(barrierRoot, false);
                pack.transform.localPosition = barrierCenter +
                    right * centered * packWidth +
                    Vector3.up * height * 0.5f;
                pack.transform.localRotation =
                    Quaternion.LookRotation(right, Vector3.up);
                pack.transform.localScale = new Vector3(
                    depth,
                    height,
                    packWidth * 0.92f);
                Collider collider = pack.GetComponent<Collider>();
                if (collider != null)
                    UnityEngine.Object.Destroy(collider);
                MeshRenderer renderer = pack.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = forensicsTrackBarrierMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.motionVectorGenerationMode =
                    MotionVectorGenerationMode.ForceNoMotion;
            }
        }

        private void BuildSuzukaOutsideBarrier(
            IReadOnlyList<Vector3> centers,
            float outerHalfWidth)
        {
            int count = centers?.Count ?? 0;
            if (count < 4 || forensicsTrackRoot == null)
                return;

            int contactIndex = 0;
            float closestDistance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                float distance =
                    (centers[i] - VisualContactLocalPoint).sqrMagnitude;
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    contactIndex = i;
                }
            }

            int turnStart = Mathf.Max(0, contactIndex - 8);
            int turnEnd = Mathf.Min(count - 1, contactIndex + 3);
            Vector3 entry = FlattenNormalized(
                centers[contactIndex] - centers[turnStart],
                forensicsCorridorForward);
            Vector3 exit = FlattenNormalized(
                centers[turnEnd] - centers[contactIndex],
                entry);
            float turnAngle = Vector3.SignedAngle(
                entry,
                exit,
                Vector3.up);
            float outsideSign = Mathf.Abs(turnAngle) > 2f
                ? (turnAngle < 0f ? 1f : -1f)
                : 1f;

            if (forensicsTrackBarrierMaterial == null)
            {
                forensicsTrackBarrierMaterial =
                    CreateForensicsOpaqueMaterial(
                        "Runtime_SuzukaTyreBarrier",
                        new Color(0.055f, 0.062f, 0.07f, 1f));
            }

            Transform barrierRoot = CreateRoot(
                "SuzukaTurn3OutsideTyreBarrier",
                forensicsTrackRoot);
            int startIndex = Mathf.Max(0, contactIndex - 4);
            const int barrierCount = 12;
            float barrierHeight = carWidth * 0.42f;
            float barrierDepth = carWidth * 0.52f;
            float barrierLength = Mathf.Max(
                carLength * 0.32f,
                Vector3.Distance(
                    centers[startIndex],
                    centers[count - 1]) / barrierCount * 0.82f);
            for (int i = 0; i < barrierCount; i++)
            {
                float progress = i / (barrierCount - 1f);
                int index = Mathf.Clamp(
                    Mathf.RoundToInt(Mathf.Lerp(
                        startIndex,
                        count - 1,
                        progress)),
                    0,
                    count - 1);
                int before = Mathf.Max(0, index - 1);
                int after = Mathf.Min(count - 1, index + 1);
                Vector3 tangent = FlattenNormalized(
                    centers[after] - centers[before],
                    forensicsCorridorForward);
                Vector3 right = FlattenNormalized(
                    Vector3.Cross(Vector3.up, tangent),
                    forensicsCorridorRight);

                GameObject tyrePack = GameObject.CreatePrimitive(
                    PrimitiveType.Cube);
                tyrePack.name = $"TyreBarrierPack_{i:00}";
                tyrePack.transform.SetParent(barrierRoot, false);
                tyrePack.transform.localPosition = centers[index] +
                    right * outsideSign *
                    (outerHalfWidth - barrierDepth * 0.5f) +
                    Vector3.up * barrierHeight * 0.5f;
                tyrePack.transform.localRotation =
                    Quaternion.LookRotation(tangent, Vector3.up);
                tyrePack.transform.localScale = new Vector3(
                    barrierDepth,
                    barrierHeight,
                    barrierLength);
                Collider collider = tyrePack.GetComponent<Collider>();
                if (collider != null)
                    UnityEngine.Object.Destroy(collider);
                MeshRenderer renderer =
                    tyrePack.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = forensicsTrackBarrierMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.motionVectorGenerationMode =
                    MotionVectorGenerationMode.ForceNoMotion;
            }
        }

        private static void AddForensicsTrackQuad(
            List<int> destination,
            int segment,
            int leftBand,
            int rightBand,
            int rowWidth)
        {
            int a = segment * rowWidth + leftBand;
            int b = segment * rowWidth + rightBand;
            int c = (segment + 1) * rowWidth + leftBand;
            int d = (segment + 1) * rowWidth + rightBand;
            destination.Add(a);
            destination.Add(c);
            destination.Add(d);
            destination.Add(a);
            destination.Add(d);
            destination.Add(b);
        }

        private void BuildStableForensicsRails()
        {
            BuildStableForensicsRail(
                forensicsVictimPath,
                "DriverAObservedRibbon",
                forensicsVictimStableMaterial,
                0.055f,
                0.068f);
            BuildStableForensicsRail(
                forensicsOtherPath,
                "DriverBObservedRibbon",
                forensicsOtherStableMaterial,
                0.035f,
                0.084f);
            SetRootVisible(forensicsStableRailRoot, false);
        }

        private void BuildStableForensicsRail(
            ForensicPath path,
            string name,
            Material material,
            float halfWidthInCarWidths,
            float liftInCarWidths)
        {
            if (path?.Points == null ||
                path.Points.Length < 2 ||
                forensicsStableRailRoot == null)
            {
                return;
            }

            int count = path.Points.Length;
            Vector3[] vertices = new Vector3[count * 2];
            Vector3[] normals = new Vector3[vertices.Length];
            Vector2[] uv = new Vector2[vertices.Length];
            int[] triangles = new int[(count - 1) * 6];
            float halfWidth = Mathf.Max(
                carWidth * halfWidthInCarWidths,
                0.0001f);
            Vector3 lift = Vector3.up * carWidth * liftInCarWidths;
            Vector3 previousRight = forensicsCorridorRight;
            float distance = 0f;
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    distance += Vector3.Distance(
                        path.Points[i - 1],
                        path.Points[i]);
                }
                Vector3 right = FlattenNormalized(
                    Vector3.Cross(Vector3.up, path.Tangents[i]),
                    previousRight);
                if (i > 0 && Vector3.Dot(right, previousRight) < 0f)
                    right = -right;
                previousRight = right;
                vertices[i * 2] = path.Points[i] - right * halfWidth + lift;
                vertices[i * 2 + 1] = path.Points[i] + right * halfWidth + lift;
                normals[i * 2] = Vector3.up;
                normals[i * 2 + 1] = Vector3.up;
                uv[i * 2] = new Vector2(0f, distance);
                uv[i * 2 + 1] = new Vector2(1f, distance);
            }
            for (int i = 0; i < count - 1; i++)
            {
                int triangle = i * 6;
                int vertex = i * 2;
                triangles[triangle] = vertex;
                triangles[triangle + 1] = vertex + 2;
                triangles[triangle + 2] = vertex + 3;
                triangles[triangle + 3] = vertex;
                triangles[triangle + 4] = vertex + 3;
                triangles[triangle + 5] = vertex + 1;
            }

            Mesh mesh = new()
            {
                name = $"Runtime_{name}"
            };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            meshes.Add(mesh);
            forensicsMeshes.Add(mesh);
            GameObject ribbon = new(
                name,
                typeof(MeshFilter),
                typeof(MeshRenderer));
            ribbon.transform.SetParent(forensicsStableRailRoot, false);
            ribbon.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = ribbon.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            path.StableRail = renderer;
        }

        private void BuildForensicsTrackWarning(
            Vector3[] leftRoad,
            Vector3[] rightRoad,
            Vector3[] leftKerb,
            Vector3[] rightKerb,
            int contactIndex)
        {
            if (forensicsTrackWarningRoot == null ||
                leftRoad == null || rightRoad == null ||
                leftKerb == null || rightKerb == null ||
                leftRoad.Length < 2 ||
                rightRoad.Length != leftRoad.Length ||
                leftKerb.Length != leftRoad.Length ||
                rightKerb.Length != leftRoad.Length)
            {
                return;
            }

            forensicsTrackWarningColor =
                new Color(1.45f, 0.72f, 0.035f, 0.96f);
            forensicsTrackWarningMaterial = CreateForensicsMaterial(
                "Runtime_ForensicsTrackContactWarning",
                forensicsTrackWarningColor,
                true);
            AddForensicsTrackWarningLine(
                leftRoad,
                contactIndex,
                0,
                "LeftWarningEntry");
            AddForensicsTrackWarningLine(
                leftRoad,
                contactIndex,
                leftRoad.Length - 1,
                "LeftWarningExit");
            AddForensicsTrackWarningLine(
                rightRoad,
                contactIndex,
                0,
                "RightWarningEntry");
            AddForensicsTrackWarningLine(
                rightRoad,
                contactIndex,
                rightRoad.Length - 1,
                "RightWarningExit");
            AddForensicsTrackWarningLine(
                leftKerb,
                contactIndex,
                0,
                "LeftKerbWarningEntry");
            AddForensicsTrackWarningLine(
                leftKerb,
                contactIndex,
                leftKerb.Length - 1,
                "LeftKerbWarningExit");
            AddForensicsTrackWarningLine(
                rightKerb,
                contactIndex,
                0,
                "RightKerbWarningEntry");
            AddForensicsTrackWarningLine(
                rightKerb,
                contactIndex,
                rightKerb.Length - 1,
                "RightKerbWarningExit");
            SetRootVisible(forensicsTrackWarningRoot, false);
        }

        private void AddForensicsTrackWarningLine(
            Vector3[] source,
            int start,
            int end,
            string name)
        {
            int step = end >= start ? 1 : -1;
            int count = Mathf.Abs(end - start) + 1;
            if (count < 2)
                return;
            Vector3[] points = new Vector3[count];
            for (int i = 0; i < count; i++)
                points[i] = source[start + step * i];
            LineRenderer line = CreateLine(
                name,
                forensicsTrackWarningRoot,
                forensicsTrackWarningMaterial,
                carWidth * 0.065f,
                true);
            line.positionCount = 0;
            forensicsTrackWarningLines.Add(line);
            forensicsTrackWarningPoints.Add(points);
        }

        private void SetForensicsTrackProgress(float progress)
        {
            if (!forensicsTrackEnabled ||
                !forensicsTrackUserVisible ||
                forensicsTrackRoot == null)
            {
                SetRootVisible(forensicsTrackRoot, false);
                return;
            }

            float clamped = Mathf.Clamp01(progress);
            SetRootVisible(forensicsTrackRoot, clamped > 0.0001f);
            if (forensicsUsesActualTrack)
                return;
            if (forensicsTrackMesh == null ||
                forensicsTrackTriangles == null)
            {
                SetRootVisible(forensicsTrackRoot, false);
                return;
            }
            if (Mathf.Abs(clamped - forensicsLastTrackProgress) < 0.001f)
                return;
            forensicsLastTrackProgress = clamped;
            for (int i = 0; i < forensicsTrackTriangles.Length; i++)
            {
                int[] full = forensicsTrackTriangles[i];
                int quadCount = full.Length / 6;
                int visibleIndices = clamped >= 0.9999f
                    ? full.Length
                    : Mathf.Clamp(
                        Mathf.FloorToInt(quadCount * clamped) * 6,
                        0,
                        full.Length);
                forensicsTrackMesh.SetTriangles(
                    full,
                    0,
                    visibleIndices,
                    i,
                    false);
            }
        }

        private void SetForensicsTrackWarningPulse(float age)
        {
            if (forensicsTrackWarningRoot == null ||
                forensicsTrackWarningMaterial == null ||
                age < 0f || age > 0.55f)
            {
                SetRootVisible(forensicsTrackWarningRoot, false);
                return;
            }

            float progress = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0f, 0.42f, age));
            float fade = 1f - Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0.18f, 0.55f, age));
            Color color = forensicsTrackWarningColor;
            color.a *= fade;
            ReplayCarVisualUtil.SetMaterialColor(
                forensicsTrackWarningMaterial,
                color);
            SetRootVisible(forensicsTrackWarningRoot, fade > 0.001f);
            for (int i = 0; i < forensicsTrackWarningLines.Count; i++)
            {
                SetLineProgress(
                    forensicsTrackWarningLines[i],
                    forensicsTrackWarningPoints[i],
                    progress,
                    Vector3.zero);
            }
        }

        private void BuildForensicsMarkers()
        {
            float contact = forensicsAnalysis.PresentationTime;
            float firstOffset = Mathf.Ceil(
                (forensicsVisibleStartTime - contact) /
                forensicsTickSeconds) * forensicsTickSeconds;
            float lastOffset = forensicsVisibleEndTime - contact;
            for (float offset = firstOffset;
                 offset <= lastOffset + 0.001f;
                 offset += forensicsTickSeconds)
            {
                float time = contact + offset;
                if (!TryResolveForensicsPose(
                        forensicsVictimDriver,
                        time,
                        out Vector3 victimPoint,
                        out Vector3 victimTangent) ||
                    !TryResolveForensicsPose(
                        forensicsOtherDriver,
                        time,
                        out Vector3 otherPoint,
                        out Vector3 otherTangent))
                {
                    continue;
                }

                Transform marker = CreateRoot(
                    $"EvidenceTick_{offset:+0.00;-0.00;0.00}",
                    forensicsMarkerRoot);
                CreateForensicsTick(
                    marker,
                    forensicsVictimDriver,
                    time,
                    victimPoint,
                    victimTangent,
                    forensicsVictimMaterial);
                CreateForensicsTick(
                    marker,
                    forensicsOtherDriver,
                    time,
                    otherPoint,
                    otherTangent,
                    forensicsOtherMaterial);
                LineRenderer connector = CreateLine(
                    "SameTimeConnector",
                    marker,
                    forensicsConnectorMaterial,
                    carWidth * 0.015f,
                    false);
                connector.positionCount = 2;
                connector.SetPosition(0, victimPoint + ForensicsLineLift());
                connector.SetPosition(1, otherPoint + ForensicsLineLift());
                marker.gameObject.SetActive(false);
                forensicsMarkers.Add(new ForensicMarker(marker, time));
            }
        }

        private void CreateForensicsTick(
            Transform parent,
            int driverNumber,
            float time,
            Vector3 point,
            Vector3 tangent,
            Material defaultMaterial)
        {
            bool braking = forensicsAnalysis.TryGetTelemetry(
                    driverNumber,
                    time,
                    out CollisionForensicTelemetry telemetry) &&
                telemetry.Available &&
                telemetry.Brake > 0;
            Material material = braking
                ? forensicsBrakeMaterial
                : defaultMaterial;
            Vector3 side = FlattenNormalized(
                Vector3.Cross(Vector3.up, tangent),
                forensicsCorridorRight);
            LineRenderer tick = CreateLine(
                braking ? "BrakeTick" : "TimeTick",
                parent,
                material,
                carWidth * (braking ? 0.04f : 0.025f),
                true);
            tick.positionCount = 2;
            Vector3 lift = ForensicsLineLift();
            tick.SetPosition(0, point - side * carWidth * 0.16f + lift);
            tick.SetPosition(1, point + side * carWidth * 0.16f + lift);
        }

        private void BuildForensicsStations()
        {
            if (forensicsStationRoot == null)
                return;

            float contact = forensicsAnalysis.PresentationTime;
            int stationIndex = 0;
            for (int i = 0; i < ForensicsPreStationOffsets.Length; i++)
            {
                CreateForensicsStation(
                    contact + ForensicsPreStationOffsets[i],
                    stationIndex++,
                    false,
                    false);
            }
            CreateForensicsStation(
                contact,
                stationIndex++,
                true,
                false);
            if (forensicsVehicleHoldTime > contact + 0.001f)
            {
                CreateForensicsStation(
                    forensicsVehicleHoldTime,
                    stationIndex,
                    false,
                    true);
            }
        }

        private void CreateForensicsStation(
            float time,
            int stationIndex,
            bool contactStation,
            bool outcomeStation)
        {
            if (!TryResolveForensicsPose(
                    forensicsVictimDriver,
                    time,
                    out Vector3 victimPoint,
                    out _) ||
                !TryResolveForensicsPose(
                    forensicsOtherDriver,
                    time,
                    out Vector3 otherPoint,
                    out _))
            {
                return;
            }

            Vector3 midpoint = (victimPoint + otherPoint) * 0.5f;
            Transform root = CreateRoot(
                $"EvidenceStation_{stationIndex + 1:00}",
                forensicsStationRoot);
            root.localPosition = midpoint;
            Vector3 victimLocal = victimPoint - midpoint;
            Vector3 otherLocal = otherPoint - midpoint;
            Vector3 gapDirection = FlattenNormalized(
                otherLocal - victimLocal,
                forensicsCorridorRight);
            Material accent = contactStation
                ? impactMaterial
                : outcomeStation
                    ? forensicsOtherMaterial
                    : forensicsStationMaterial;

            LineRenderer gap = CreateLine(
                "ObservedGap",
                root,
                accent,
                carWidth * (contactStation ? 0.035f : 0.022f),
                true);
            gap.positionCount = 2;
            Vector3 gapLift = Vector3.up * carWidth * 0.13f;
            gap.SetPosition(0, victimLocal + gapLift);
            gap.SetPosition(1, otherLocal + gapLift);

            CreateForensicsStationRing(
                "DriverANode",
                root,
                victimLocal,
                carWidth * 0.19f,
                forensicsVictimMaterial,
                false);
            CreateForensicsStationRing(
                "DriverBNode",
                root,
                otherLocal,
                carWidth * 0.19f,
                forensicsOtherMaterial,
                false);
            if (IsForensicsBraking(forensicsVictimDriver, time))
            {
                CreateForensicsStationRing(
                    "DriverABrakeHalo",
                    root,
                    victimLocal,
                    carWidth * 0.29f,
                    forensicsBrakeMaterial,
                    false);
            }
            if (IsForensicsBraking(forensicsOtherDriver, time))
            {
                CreateForensicsStationRing(
                    "DriverBBrakeHalo",
                    root,
                    otherLocal,
                    carWidth * 0.29f,
                    forensicsBrakeMaterial,
                    false);
            }

            LineRenderer beacon = CreateLine(
                "StationBeacon",
                root,
                accent,
                carWidth * (contactStation ? 0.04f : 0.025f),
                true);
            beacon.positionCount = 2;
            beacon.SetPosition(
                0,
                Vector3.up * carWidth * 0.05f);
            beacon.SetPosition(
                1,
                Vector3.up * carWidth * (contactStation ? 1.05f : 0.78f));

            Transform pulseRoot = null;
            if (contactStation)
            {
                pulseRoot = CreateRoot("ContactSpatialPulse", root);
                pulseRoot.localPosition = Vector3.up * carWidth * 0.12f;
                CreateForensicsStationRing(
                    "ContactGroundRing",
                    pulseRoot,
                    Vector3.zero,
                    carWidth * 0.62f,
                    impactMaterial,
                    false);
                CreateForensicsStationRing(
                    "ContactVerticalRing",
                    pulseRoot,
                    Vector3.up * carWidth * 0.42f,
                    carWidth * 0.52f,
                    impactMaterial,
                    true);
            }

            TextMeshPro header = CreateForensicsSpatialLabel(
                "TimeLabel",
                root,
                Vector3.up * carWidth * (contactStation ? 1.18f : 0.9f),
                ResolveForensicsStationHeading(
                    time,
                    contactStation,
                    outcomeStation),
                contactStation
                    ? new Color(1f, 0.57f, 0.2f, 1f)
                    : outcomeStation
                        ? new Color(0.2f, 0.85f, 1f, 1f)
                        : new Color(0.9f, 0.96f, 1f, 1f),
                0.048f,
                5.6f,
                TextAlignmentOptions.Center);
            TextMeshPro gapLabel = CreateForensicsSpatialLabel(
                "GapLabel",
                root,
                gapLift + Vector3.up * carWidth * 0.2f,
                ResolveForensicsStationGapText(time, outcomeStation),
                contactStation
                    ? new Color(1f, 0.66f, 0.26f, 1f)
                    : new Color(0.76f, 0.86f, 0.94f, 1f),
                0.033f,
                4.7f,
                TextAlignmentOptions.Center);

            Vector3 victimLabelPosition = victimLocal -
                gapDirection * carWidth * 0.45f +
                Vector3.up * carWidth * 0.52f;
            Vector3 otherLabelPosition = otherLocal +
                gapDirection * carWidth * 0.45f +
                Vector3.up * carWidth * 0.52f;
            TextMeshPro victimTelemetry = CreateForensicsSpatialLabel(
                "DriverATelemetry",
                root,
                victimLabelPosition,
                FormatForensicsStationTelemetry(
                    forensicsVictimDriver,
                    time),
                new Color(0.94f, 0.98f, 1f, 1f),
                0.031f,
                4.5f,
                TextAlignmentOptions.Center);
            TextMeshPro otherTelemetry = CreateForensicsSpatialLabel(
                "DriverBTelemetry",
                root,
                otherLabelPosition,
                FormatForensicsStationTelemetry(
                    forensicsOtherDriver,
                    time),
                new Color(0.08f, 0.85f, 1f, 1f),
                0.031f,
                4.5f,
                TextAlignmentOptions.Center);
            CreateForensicsLabelTether(
                "DriverATelemetryTether",
                root,
                victimLocal + Vector3.up * carWidth * 0.16f,
                victimLabelPosition,
                forensicsVictimMaterial);
            CreateForensicsLabelTether(
                "DriverBTelemetryTether",
                root,
                otherLocal + Vector3.up * carWidth * 0.16f,
                otherLabelPosition,
                forensicsOtherMaterial);

            root.gameObject.SetActive(false);
            forensicsStations.Add(new ForensicStation
            {
                Root = root,
                Time = time,
                Contact = contactStation,
                Header = header,
                Gap = gapLabel,
                VictimTelemetry = victimTelemetry,
                OtherTelemetry = otherTelemetry,
                HeaderBaseScale = header.transform.localScale,
                GapBaseScale = gapLabel.transform.localScale,
                TelemetryBaseScale = victimTelemetry.transform.localScale,
                PulseRoot = pulseRoot,
                PulseBaseScale = pulseRoot != null
                    ? pulseRoot.localScale
                    : Vector3.one
            });
        }

        private string ResolveForensicsStationHeading(
            float time,
            bool contactStation,
            bool outcomeStation)
        {
            if (contactStation)
                return "CONTACT  |  OBSERVED CLOSEST";
            if (outcomeStation)
            {
                return forensicsAnalysis.Tier ==
                       CollisionEvidenceTier.ObservedContactAndPost
                    ? "T+0.35s  |  OBSERVED AFTERMATH"
                    : "RECONSTRUCTED OUTCOME";
            }

            float relative = time - forensicsAnalysis.PresentationTime;
            return $"T{relative:+0.00;-0.00;0.00}s  |  OBSERVED";
        }

        private string ResolveForensicsStationGapText(
            float time,
            bool outcomeStation)
        {
            if (TryResolveForensicsGapMeters(
                    time,
                    outcomeStation,
                    out float gapMeters))
            {
                string suffix = Mathf.Abs(
                        time - forensicsAnalysis.PresentationTime) <= 0.001f
                    ? "  CLOSEST"
                    : "  OBSERVED GAP";
                return $"{FormatForensicsGap(gapMeters)}{suffix}";
            }
            return outcomeStation && forensicsAnalysis.RequiresReconstructedPost
                ? "GAP NOT OBSERVED"
                : "OBSERVED GAP UNAVAILABLE";
        }

        private TextMeshPro CreateForensicsSpatialLabel(
            string name,
            Transform parent,
            Vector3 localPosition,
            string text,
            Color color,
            float scaleInCarLengths,
            float fontSize,
            TextAlignmentOptions alignment)
        {
            GameObject labelObject = new(name, typeof(TextMeshPro));
            labelObject.transform.SetParent(parent, false);
            labelObject.transform.localPosition = localPosition;
            labelObject.transform.localRotation =
                Quaternion.LookRotation(Vector3.up, forensicsCorridorForward);
            labelObject.transform.localScale =
                Vector3.one * carLength * scaleInCarLengths;
            TextMeshPro label = labelObject.GetComponent<TextMeshPro>();
            label.text = text;
            label.alignment = alignment;
            label.richText = true;
            label.fontSize = fontSize;
            label.fontStyle = FontStyles.Bold;
            label.enableAutoSizing = false;
            label.color = color;
            label.rectTransform.sizeDelta = new Vector2(15f, 2.6f);
            label.renderer.shadowCastingMode = ShadowCastingMode.Off;
            label.renderer.receiveShadows = false;
            label.renderer.sortingOrder = 1;
            return label;
        }

        private void CreateForensicsLabelTether(
            string name,
            Transform parent,
            Vector3 start,
            Vector3 end,
            Material material)
        {
            LineRenderer tether = CreateLine(
                name,
                parent,
                material,
                carWidth * 0.012f,
                false);
            tether.positionCount = 2;
            tether.SetPosition(0, start);
            tether.SetPosition(1, end);
        }

        private void CreateForensicsStationRing(
            string name,
            Transform parent,
            Vector3 center,
            float radius,
            Material material,
            bool vertical)
        {
            LineRenderer ring = CreateLine(
                name,
                parent,
                material,
                carWidth * 0.02f,
                true);
            ring.loop = true;
            const int pointCount = 20;
            ring.positionCount = pointCount;
            for (int i = 0; i < pointCount; i++)
            {
                float angle = i * Mathf.PI * 2f / pointCount;
                Vector3 radial = vertical
                    ? forensicsCorridorRight * Mathf.Cos(angle) * radius +
                      Vector3.up * Mathf.Sin(angle) * radius
                    : forensicsCorridorRight * Mathf.Cos(angle) * radius +
                      forensicsCorridorForward * Mathf.Sin(angle) * radius;
                ring.SetPosition(i, center + radial);
            }
        }

        private bool IsForensicsBraking(int driverNumber, float time)
        {
            return forensicsAnalysis.TryGetTelemetry(
                    driverNumber,
                    time,
                    out CollisionForensicTelemetry telemetry) &&
                telemetry.Available &&
                telemetry.Brake > 0;
        }

        private bool TryResolveForensicsGapMeters(
            float time,
            bool outcomeStation,
            out float gapMeters)
        {
            gapMeters = 0f;
            if (forensicsAnalysis.Tier ==
                CollisionEvidenceTier.ContactUnresolved)
            {
                return false;
            }
            if (outcomeStation && forensicsAnalysis.RequiresReconstructedPost)
                return false;
            if (Mathf.Abs(
                    time - forensicsAnalysis.PresentationTime) <= 0.001f)
            {
                gapMeters = forensicsAnalysis.Contact.SeparationMeters;
                return float.IsFinite(gapMeters);
            }
            if (!forensicsAnalysis.TryEvaluate(
                    forensicsVictimDriver,
                    time,
                    out CollisionTrajectorySample victimSample) ||
                !forensicsAnalysis.TryEvaluate(
                    forensicsOtherDriver,
                    time,
                    out CollisionTrajectorySample otherSample))
            {
                return false;
            }

            float contactSourceDistance = Vector3.Distance(
                forensicsAnalysis.Contact.FirstSourcePosition,
                forensicsAnalysis.Contact.SecondSourcePosition);
            if (contactSourceDistance <= 0.0000001f ||
                forensicsAnalysis.Contact.SeparationMeters < 0f)
            {
                return false;
            }
            float metersPerSourceUnit =
                forensicsAnalysis.Contact.SeparationMeters /
                contactSourceDistance;
            gapMeters = Vector3.Distance(
                    victimSample.SourcePosition,
                    otherSample.SourcePosition) *
                metersPerSourceUnit;
            return float.IsFinite(gapMeters);
        }

        private string FormatForensicsStationTelemetry(
            int driverNumber,
            float time)
        {
            string label = driverNumber == forensicsVictimDriver
                ? victimLabel != null ? victimLabel.text : $"CAR {driverNumber}"
                : otherLabel != null ? otherLabel.text : $"CAR {driverNumber}";
            string coloredLabel = driverNumber == forensicsVictimDriver
                ? $"<color=#F1FAFF>{label}</color>"
                : $"<color=#21D9FF>{label}</color>";
            if (!forensicsAnalysis.TryGetTelemetry(
                    driverNumber,
                    time,
                    out CollisionForensicTelemetry telemetry) ||
                !telemetry.Available)
            {
                return $"{coloredLabel}  TELEMETRY N/A";
            }
            string brake = telemetry.Brake > 0
                ? "<color=#FFD23F>BRAKE</color>"
                : "NO BRAKE";
            return $"{coloredLabel}  {telemetry.SpeedKph:0} km/h  |  {brake}";
        }

        private static string FormatForensicsGap(float meters)
        {
            return meters < 1f
                ? $"{meters:0.00} m"
                : $"{meters:0.0} m";
        }

        private void BuildForensicsOutlines()
        {
            for (int i = 0; i < ForensicsPreStationOffsets.Length; i++)
            {
                float offset = ForensicsPreStationOffsets[i];
                float time = forensicsAnalysis.PresentationTime + offset;
                ForensicOutline victimOutline = CreateForensicsOutline(
                    forensicsVictimDriver,
                    time,
                    $"DriverAOutline_{offset:0.00}",
                    forensicsVictimOutlineMaterial);
                if (victimOutline.Root != null)
                    forensicsOutlines.Add(victimOutline);
                ForensicOutline otherOutline = CreateForensicsOutline(
                    forensicsOtherDriver,
                    time,
                    $"DriverBOutline_{offset:0.00}",
                    forensicsOtherOutlineMaterial);
                if (otherOutline.Root != null)
                    forensicsOutlines.Add(otherOutline);
            }
        }

        private ForensicOutline CreateForensicsOutline(
            int driverNumber,
            float time,
            string name,
            Material material)
        {
            if (!TryResolveForensicsPose(
                    driverNumber,
                    time,
                    out Vector3 point,
                    out Vector3 tangent))
            {
                return default;
            }

            ReplayCarView source = driverNumber == forensicsVictimDriver
                ? victim
                : driverNumber == forensicsOtherDriver
                    ? other
                    : null;
            GameObject clone = CaptureGhost(
                source,
                name,
                forensicsOutlineRoot,
                material,
                forensicsGhostRenderers);
            if (clone == null)
                return default;

            Transform root = clone.transform;
            Vector3 forward = FlattenNormalized(tangent, forensicsCorridorForward);
            root.localPosition = point + ForensicsLineLift() * 1.3f;
            root.localRotation = Quaternion.LookRotation(forward, Vector3.up);

            root.gameObject.SetActive(false);
            return new ForensicOutline(
                root,
                time,
                driverNumber,
                root.localPosition,
                root.localScale);
        }

        private void BuildForensicsTimeLens(
            CollisionShowcaseVfxSettings settings)
        {
            if (!TryResolveForensicsPose(
                    forensicsVictimDriver,
                    forensicsVisibleStartTime,
                    out Vector3 victimStart,
                    out _) ||
                !TryResolveForensicsPose(
                    forensicsOtherDriver,
                    forensicsVisibleStartTime,
                    out Vector3 otherStart,
                    out _) ||
                !TryResolveForensicsPose(
                    forensicsVictimDriver,
                    forensicsAnalysis.PresentationTime,
                    out Vector3 victimContact,
                    out _) ||
                !TryResolveForensicsPose(
                    forensicsOtherDriver,
                    forensicsAnalysis.PresentationTime,
                    out Vector3 otherContact,
                    out _) ||
                !TryResolveForensicsPose(
                    forensicsVictimDriver,
                    forensicsVisibleEndTime,
                    out Vector3 victimEnd,
                    out _) ||
                !TryResolveForensicsPose(
                    forensicsOtherDriver,
                    forensicsVisibleEndTime,
                    out Vector3 otherEnd,
                    out _))
            {
                return;
            }

            GameObject gateObject = new("CollisionTimeLensGate");
            gateObject.transform.SetParent(stage, false);
            Transform body = CreateRoot(
                "CollisionTimeLensGateBody",
                gateObject.transform);
            Transform handle = CreateRoot(
                "CollisionTimeLensHandle",
                gateObject.transform);
            float handleHeightMeters = settings != null
                ? settings.timeLensHandleHeightMeters
                : 0.75f;
            float stageVerticalScale = Mathf.Max(
                0.0001f,
                Mathf.Abs(stage.lossyScale.y));
            float localHeight = handleHeightMeters / stageVerticalScale;
            LineRenderer bodyLine = CreateLine(
                "TimeLensGateLine",
                body,
                forensicsLensMaterial,
                carWidth * 0.018f,
                true);
            float gateHalfWidth = carWidth * 1.45f;
            float gateBottom = carWidth * 0.05f;
            float gateTop = Mathf.Max(
                carWidth * 0.8f,
                localHeight * 0.72f);
            bodyLine.positionCount = 5;
            bodyLine.SetPosition(
                0,
                new Vector3(-gateHalfWidth, gateBottom, 0f));
            bodyLine.SetPosition(
                1,
                new Vector3(-gateHalfWidth, gateTop, 0f));
            bodyLine.SetPosition(
                2,
                new Vector3(gateHalfWidth, gateTop, 0f));
            bodyLine.SetPosition(
                3,
                new Vector3(gateHalfWidth, gateBottom, 0f));
            bodyLine.SetPosition(
                4,
                new Vector3(-gateHalfWidth, gateBottom, 0f));
            LineRenderer handleRing = CreateLine(
                "TimeLensHandleRing",
                handle,
                forensicsLensMaterial,
                carWidth * 0.022f,
                true);
            handleRing.loop = true;
            const int ringPoints = 16;
            handleRing.positionCount = ringPoints;
            float radius = carWidth * 0.18f;
            for (int i = 0; i < ringPoints; i++)
            {
                float angle = i * Mathf.PI * 2f / ringPoints;
                handleRing.SetPosition(
                    i,
                    new Vector3(
                        Mathf.Cos(angle) * radius,
                        Mathf.Sin(angle) * radius,
                        0f));
            }

            Vector3[] rail =
            {
                (victimStart + otherStart) * 0.5f,
                (victimContact + otherContact) * 0.5f,
                (victimEnd + otherEnd) * 0.5f
            };
            forensicsTimeLensGate =
                gateObject.AddComponent<CollisionTimeLensGate>();
            forensicsTimeLensGate.SetInteractionTuning(
                settings != null
                    ? settings.timeLensHandleRadiusMeters
                    : 0.055f,
                settings != null
                    ? settings.timeLensDeadzoneMeters
                    : 0.007f,
                settings != null
                    ? settings.timeLensVisualSmoothSeconds
                    : 0.035f,
                settings != null
                    ? settings.timeLensContactDetentMeters
                    : 0.04f,
                settings != null
                    ? settings.timeLensEndpointDetentMeters
                    : 0.03f);
            if (!forensicsTimeLensGate.Configure(
                    stage,
                    rail,
                    1,
                    body,
                    handle,
                    1f))
            {
                UnityEngine.Object.Destroy(gateObject);
                forensicsTimeLensGate = null;
                return;
            }

            float postProgress = Mathf.InverseLerp(
                forensicsAnalysis.PresentationTime,
                forensicsVisibleEndTime,
                forensicsVehicleHoldTime);
            forensicsDefaultLensNormalized = Mathf.Lerp(
                forensicsTimeLensGate.ContactNormalized,
                1f,
                postProgress);
            forensicsTimeLensGate.ResetValue(
                forensicsDefaultLensNormalized,
                false);

            forensicsTimeLensGate.SetHandleHeightMeters(
                handleHeightMeters);
            CreateForensicsLensHud(
                body,
                localHeight);
            forensicsTimeLensGate.ValueChanged +=
                OnForensicsTimeLensChanged;
            forensicsTimeLensGate.GrabStateChanged +=
                OnForensicsTimeLensGrabStateChanged;
            forensicsTimeLensGate.SetAvailable(false);
        }

        private void CreateForensicsLensHud(
            Transform gateRoot,
            float localHeight)
        {
            GameObject hudObject = new(
                "CollisionTimeLensHud",
                typeof(TextMeshPro));
            hudObject.transform.SetParent(gateRoot, false);
            hudObject.transform.localPosition =
                Vector3.up * Mathf.Max(
                    carWidth * 0.4f,
                    localHeight * 0.58f) +
                Vector3.right * carWidth * 0.72f;
            hudObject.transform.localRotation =
                Quaternion.LookRotation(Vector3.up, Vector3.forward);
            hudObject.transform.localScale =
                Vector3.one * carLength * 0.06f;
            forensicsLensHud = hudObject.GetComponent<TextMeshPro>();
            forensicsLensHud.alignment = TextAlignmentOptions.Left;
            forensicsLensHud.fontSize = 4.6f;
            forensicsLensHud.fontStyle = FontStyles.Bold;
            forensicsLensHud.enableAutoSizing = false;
            forensicsLensHud.richText = true;
            forensicsLensHud.color = new Color(1f, 0.86f, 0.18f, 0.98f);
            forensicsLensHud.rectTransform.sizeDelta = new Vector2(16f, 7f);
            forensicsLensHud.renderer.shadowCastingMode =
                ShadowCastingMode.Off;
            forensicsLensHud.renderer.receiveShadows = false;
            UpdateForensicsLensHud();
            forensicsLensHud.gameObject.SetActive(false);
        }

        private void UpdateForensicsLensHud()
        {
            if (forensicsLensHud == null || forensicsAnalysis == null)
                return;

            float relative = forensicsCurrentTime -
                forensicsAnalysis.PresentationTime;
            string time = Mathf.Abs(relative) <= 0.025f
                ? "CONTACT"
                : relative < 0f
                    ? $"OBSERVED {relative:0.00}s"
                    : forensicsAnalysis.Tier ==
                      CollisionEvidenceTier.ObservedContactAndPost
                        ? $"OBSERVED +{relative:0.00}s"
                        : $"RECONSTRUCTED " +
                          $"{Mathf.RoundToInt(Mathf.InverseLerp(forensicsAnalysis.PresentationTime, forensicsVisibleEndTime, forensicsCurrentTime) * 100f)}%";
            string victimName = victimLabel != null &&
                                !string.IsNullOrWhiteSpace(victimLabel.text)
                ? victimLabel.text
                : forensicsVictimDriver.ToString();
            string otherName = otherLabel != null &&
                               !string.IsNullOrWhiteSpace(otherLabel.text)
                ? otherLabel.text
                : forensicsOtherDriver.ToString();
            forensicsLensHud.text =
                $"{time}\n" +
                $"{FormatForensicsTelemetry(victimName, forensicsVictimDriver)}\n" +
                $"{FormatForensicsTelemetry(otherName, forensicsOtherDriver)}\n" +
                "NEAREST TELEMETRY";
            FaceReadableTextToViewer(forensicsLensHud);
        }

        private string FormatForensicsTelemetry(
            string driverName,
            int driverNumber)
        {
            if (!forensicsAnalysis.TryGetTelemetry(
                    driverNumber,
                    forensicsCurrentTime,
                    out CollisionForensicTelemetry telemetry) ||
                !telemetry.Available)
            {
                return $"{driverName}  -- km/h";
            }

            string brake = telemetry.Brake > 0
                ? "  BRAKE"
                : string.Empty;
            return $"{driverName}  {telemetry.SpeedKph:0} km/h{brake}";
        }

        private void ApplyForensicsReveal(float time)
        {
            float contact = forensicsAnalysis.PresentationTime;
            float trackProgress = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0f, ForensicsTrackRevealEnd, time));
            SetForensicsTrackProgress(trackProgress);
            float railProgress = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0.15f, ForensicsRailRevealEnd, time));
            SetForensicsRailProgress(railProgress);
            SetForensicsAnnotations(0f);

            float islandProgress = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0f, 0.22f, time));
            if (island != null)
            {
                island.localScale = Vector3.Scale(
                    islandBaseScale,
                    new Vector3(
                        Mathf.Lerp(0.88f, 1f, islandProgress),
                        Mathf.Lerp(0.025f, 1f, islandProgress),
                        Mathf.Lerp(0.88f, 1f, islandProgress)));
            }

            if (time < ForensicsRailRevealEnd)
            {
                Phase = time < 0.22f
                    ? CollisionPresentationPhase.IslandReveal
                    : CollisionPresentationPhase.PreImpact;
                forensicsVehicleVisible = false;
                forensicsCurrentTime = forensicsVisibleStartTime;
                SetForensicsTail(float.NaN);
                UpdateForensicsOutlines(
                    forensicsCurrentTime,
                    false,
                    false);
                ResetForensicsImpactVisuals();
                return;
            }

            if (time < ForensicsApproachEnd)
            {
                Phase = CollisionPresentationPhase.PreImpact;
                forensicsVehicleVisible =
                    forensicsAnalysis.Tier != CollisionEvidenceTier.ContactUnresolved;
                float progress = Mathf.InverseLerp(
                    ForensicsRailRevealEnd,
                    ForensicsApproachEnd,
                    time);
                forensicsCurrentTime = Mathf.Lerp(
                    forensicsAnalysis.VehicleRevealTime,
                    contact,
                    progress);
                SetForensicsTail(forensicsCurrentTime);
                UpdateForensicsOutlines(
                    forensicsCurrentTime,
                    false,
                    false);
                ResetForensicsImpactVisuals();
                return;
            }

            if (time < ForensicsHitStopEnd)
            {
                Phase = forensicsAnalysis.Tier ==
                        CollisionEvidenceTier.ContactUnresolved
                    ? CollisionPresentationPhase.PreImpact
                    : CollisionPresentationPhase.Impact;
                forensicsVehicleVisible =
                    forensicsAnalysis.Tier != CollisionEvidenceTier.ContactUnresolved;
                forensicsCurrentTime = contact;
                SetForensicsTail(forensicsCurrentTime);
                UpdateForensicsOutlines(
                    forensicsCurrentTime,
                    false,
                    false);
                TriggerForensicsImpact();
                UpdateForensicsImpactVisuals(
                    time - ForensicsApproachEnd);
                return;
            }

            if (time < ForensicsObservedPostEnd)
            {
                Phase = forensicsAnalysis.Tier ==
                        CollisionEvidenceTier.ContactUnresolved
                    ? CollisionPresentationPhase.PreImpact
                    : CollisionPresentationPhase.PostImpact;
                forensicsVehicleVisible =
                    forensicsAnalysis.Tier != CollisionEvidenceTier.ContactUnresolved;
                float progress = Mathf.InverseLerp(
                    ForensicsHitStopEnd,
                    ForensicsObservedPostEnd,
                    time);
                forensicsCurrentTime = Mathf.Lerp(
                    contact,
                    forensicsVehicleHoldTime,
                    progress);
                SetForensicsTail(forensicsCurrentTime);
                UpdateForensicsOutlines(
                    forensicsCurrentTime,
                    false,
                    false);
                TriggerForensicsImpact();
                UpdateForensicsImpactVisuals(
                    time - ForensicsApproachEnd);
                return;
            }

            Phase = CollisionPresentationPhase.ForensicHold;
            forensicsVehicleVisible =
                forensicsAnalysis.Tier != CollisionEvidenceTier.ContactUnresolved;
            forensicsCurrentTime = forensicsVehicleHoldTime;
            SetForensicsTail(float.NaN);
            UpdateForensicsOutlines(
                forensicsCurrentTime,
                true,
                false);
            float annotation = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    ForensicsObservedPostEnd,
                    ForensicsAnnotationEnd,
                    time));
            SetForensicsAnnotations(annotation);
            UpdateForensicsImpactVisuals(
                time - ForensicsApproachEnd);
            if (time >= ForensicsAnnotationEnd && !forensicsFinalApplied)
                ApplyTrajectoryForensicsFinalTableau();
        }

        private float TickForensicsImpactReplay(float delta)
        {
            forensicsWallTime = Mathf.Min(
                ForensicsReplayEnd,
                forensicsWallTime + delta);
            impactReplayTime = forensicsWallTime;
            float contact = forensicsAnalysis.PresentationTime;
            if (forensicsWallTime < ForensicsReplayApproachEnd)
            {
                float progress = Mathf.InverseLerp(
                    0f,
                    ForensicsReplayApproachEnd,
                    forensicsWallTime);
                forensicsCurrentTime = Mathf.Lerp(
                    forensicsAnalysis.VehicleRevealTime,
                    contact,
                    progress);
                SetForensicsTail(forensicsCurrentTime);
                UpdateForensicsOutlines(
                    forensicsCurrentTime,
                    false,
                    false);
                ResetForensicsImpactVisuals();
            }
            else if (forensicsWallTime < ForensicsReplayHitStopEnd)
            {
                forensicsCurrentTime = contact;
                SetForensicsTail(forensicsCurrentTime);
                UpdateForensicsOutlines(
                    forensicsCurrentTime,
                    false,
                    false);
                TriggerForensicsImpact();
                UpdateForensicsImpactVisuals(
                    forensicsWallTime - ForensicsReplayApproachEnd);
            }
            else
            {
                float progress = Mathf.InverseLerp(
                    ForensicsReplayHitStopEnd,
                    ForensicsReplayEnd,
                    forensicsWallTime);
                forensicsCurrentTime = Mathf.Lerp(
                    contact,
                    forensicsVehicleHoldTime,
                    progress);
                SetForensicsTail(forensicsCurrentTime);
                UpdateForensicsOutlines(
                    forensicsCurrentTime,
                    false,
                    false);
                TriggerForensicsImpact();
                UpdateForensicsImpactVisuals(
                    forensicsWallTime - ForensicsReplayApproachEnd);
            }

            if (forensicsWallTime < ForensicsReplayEnd)
                return forensicsCurrentTime;

            forensicsImpactReplaying = false;
            impactReplaying = false;
            revealComplete = true;
            ApplyTrajectoryForensicsFinalTableau();
            if (forensicsTimeLensGate != null)
            {
                forensicsTimeLensGate.ResetValue(
                    forensicsSavedLensNormalized,
                    false);
                forensicsTimeLensGate.SetAvailable(
                    forensicsTimeLensEnabled);
                ApplyForensicsTimeLensValue(
                    forensicsTimeLensGate.NormalizedValue);
            }
            return forensicsCurrentTime;
        }

        private void TriggerForensicsImpact()
        {
            if (impactTriggered ||
                forensicsAnalysis.Tier == CollisionEvidenceTier.ContactUnresolved)
            {
                return;
            }

            impactTriggered = true;
            secondaryHapticTriggered = false;
            SetRootVisible(impactRoot, true);
            SetImpactTransient(0f);
            TriggerImpactFeedback();
        }

        private void ResetForensicsImpactVisuals()
        {
            if (!impactTriggered)
                SetRootVisible(impactRoot, false);
            SetImpactFlash(false, 0f);
            SetImpactPulse(-1f);
            SetImpactTransient(-1f);
            SetImpactWarningWave(-1f);
            SetImpactBurst(-1f);
            SetForensicsTrackWarningPulse(-1f);
        }

        private void UpdateForensicsImpactVisuals(float age)
        {
            if (forensicsAnalysis.Tier == CollisionEvidenceTier.ContactUnresolved)
            {
                ResetForensicsImpactVisuals();
                return;
            }

            SetRootVisible(impactRoot, true);
            SetImpactFlash(age >= 0f && age <= 0.13f, age);
            SetImpactPulse(age);
            SetImpactWarningWave(age);
            SetForensicsTrackWarningPulse(age);
            SetImpactTransient(age);
            if (age <= ForensicsImpactVisibleSeconds)
                SetImpactBurst(age);
            else
                SetImpactBurst(-1f);
        }

        private void SetForensicsRailProgress(float progress)
        {
            float clamped = Mathf.Clamp01(progress);
            bool useStableMesh = clamped >= 0.999f;
            SetRootVisible(forensicsStableRailRoot, useStableMesh);
            if (forensicsVictimPath?.Rail != null)
                forensicsVictimPath.Rail.enabled = !useStableMesh;
            if (forensicsOtherPath?.Rail != null)
                forensicsOtherPath.Rail.enabled = !useStableMesh;
            if (useStableMesh)
                return;

            SetLineProgress(
                forensicsVictimPath?.Rail,
                forensicsVictimPath?.Points,
                clamped,
                Vector3.up * carWidth * 0.068f);
            SetLineProgress(
                forensicsOtherPath?.Rail,
                forensicsOtherPath?.Points,
                clamped,
                Vector3.up * carWidth * 0.084f);
        }

        private void SetForensicsTail(
            float time,
            bool fullHistory = false)
        {
            SetForensicsTail(
                forensicsVictimPath,
                forensicsVictimTailPoints,
                time,
                fullHistory);
            SetForensicsTail(
                forensicsOtherPath,
                forensicsOtherTailPoints,
                time,
                fullHistory);
        }

        private void SetForensicsTail(
            ForensicPath path,
            List<Vector3> buffer,
            float time,
            bool fullHistory)
        {
            if (path?.Tail == null)
                return;
            buffer.Clear();
            if (!float.IsFinite(time) ||
                forensicsAnalysis.Tier == CollisionEvidenceTier.ContactUnresolved)
            {
                path.Tail.positionCount = 0;
                return;
            }

            float drawTime = fullHistory &&
                             forensicsAnalysis.Tier ==
                             CollisionEvidenceTier
                                 .ObservedContactRequiresReconstruction
                ? Mathf.Min(
                    time,
                    forensicsAnalysis.PresentationTime)
                : time;
            float start = fullHistory
                ? forensicsVisibleStartTime
                : Mathf.Max(
                    forensicsVisibleStartTime,
                    drawTime - forensicsTailSeconds);
            bool reconstructedTail =
                !fullHistory &&
                forensicsAnalysis.Tier ==
                    CollisionEvidenceTier
                        .ObservedContactRequiresReconstruction &&
                drawTime > forensicsAnalysis.PresentationTime + 0.001f;
            if (reconstructedTail)
            {
                start = Mathf.Max(
                    start,
                    forensicsAnalysis.PresentationTime);
            }
            const float step = 0.05f;
            for (float sampleTime = start;
                 sampleTime < drawTime - 0.001f;
                 sampleTime += step)
            {
                if (TryResolveForensicsPose(
                        path.DriverNumber,
                        sampleTime,
                        out Vector3 point,
                        out _))
                {
                    buffer.Add(point + ForensicsLineLift() * 1.4f);
                }
            }
            if (TryResolveForensicsPose(
                    path.DriverNumber,
                    drawTime,
                    out Vector3 end,
                    out _))
            {
                buffer.Add(end + ForensicsLineLift() * 1.4f);
            }

            path.Tail.sharedMaterial =
                reconstructedTail
                    ? forensicsReconstructedMaterial
                    : path.DriverNumber == forensicsVictimDriver
                        ? forensicsVictimTailMaterial
                        : forensicsOtherTailMaterial;
            path.Tail.positionCount = buffer.Count;
            for (int i = 0; i < buffer.Count; i++)
                path.Tail.SetPosition(i, buffer[i]);
        }

        private void SetForensicsAnnotations(float progress)
        {
            float clamped = Mathf.Clamp01(progress);
            int markerVisible = Mathf.CeilToInt(
                clamped * forensicsMarkers.Count);
            for (int i = 0; i < forensicsMarkers.Count; i++)
            {
                Transform root = forensicsMarkers[i].Root;
                if (root != null)
                    root.gameObject.SetActive(i < markerVisible);
            }

            bool showWarning = clamped > 0.08f;
            if (forensicsLegend != null)
            {
                forensicsLegend.gameObject.SetActive(
                    clamped > 0.15f);
            }
            SetRootVisible(warningRoot, showWarning);
            SetRootVisible(
                annotationRoot,
                showWarning &&
                forensicsAnalysis.Tier != CollisionEvidenceTier.ContactUnresolved);
            SetRootVisible(
                postRoot,
                clamped > 0.55f &&
                forensicsAnalysis.Tier ==
                    CollisionEvidenceTier.ObservedContactRequiresReconstruction);
        }

        private void UpdateForensicsOutlines(
            float currentTime,
            bool keepAll,
            bool highlightNearest)
        {
            float nearestDelta = float.PositiveInfinity;
            float nearestTime = float.NaN;
            if (highlightNearest)
            {
                for (int i = 0; i < forensicsOutlines.Count; i++)
                {
                    float delta = Mathf.Abs(
                        currentTime - forensicsOutlines[i].Time);
                    if (delta < nearestDelta)
                    {
                        nearestDelta = delta;
                        nearestTime = forensicsOutlines[i].Time;
                    }
                }
            }

            for (int i = 0; i < forensicsOutlines.Count; i++)
            {
                ForensicOutline outline = forensicsOutlines[i];
                if (outline.Root == null)
                    continue;

                bool visible = keepAll ||
                    currentTime >= outline.Time +
                    forensicsEchoDelaySeconds;
                outline.Root.gameObject.SetActive(visible);
                bool highlighted = visible &&
                    highlightNearest &&
                    Mathf.Abs(outline.Time - nearestTime) <= 0.001f;
                outline.Root.localScale = outline.BaseScale *
                    (highlighted ? 1.08f : 1f);
            }

            UpdateForensicsStations(
                currentTime,
                keepAll,
                highlightNearest);
        }

        private void UpdateForensicsStations(
            float currentTime,
            bool keepAll,
            bool highlightNearest)
        {
            float nearestDelta = float.PositiveInfinity;
            float nearestTime = float.NaN;
            if (highlightNearest)
            {
                for (int i = 0; i < forensicsStations.Count; i++)
                {
                    float delta = Mathf.Abs(
                        currentTime - forensicsStations[i].Time);
                    if (delta < nearestDelta)
                    {
                        nearestDelta = delta;
                        nearestTime = forensicsStations[i].Time;
                    }
                }
            }

            for (int i = 0; i < forensicsStations.Count; i++)
            {
                ForensicStation station = forensicsStations[i];
                if (station?.Root == null)
                    continue;
                bool visible = keepAll ||
                    currentTime >= station.Time +
                    forensicsEchoDelaySeconds;
                station.Root.gameObject.SetActive(visible);
                bool highlighted = visible &&
                    highlightNearest &&
                    Mathf.Abs(station.Time - nearestTime) <= 0.001f;
                SetForensicsStationLabelScale(
                    station.Header,
                    station.HeaderBaseScale,
                    highlighted ? 1.15f : 1f);
                SetForensicsStationLabelScale(
                    station.Gap,
                    station.GapBaseScale,
                    highlighted ? 1.1f : 1f);
                SetForensicsStationLabelScale(
                    station.VictimTelemetry,
                    station.TelemetryBaseScale,
                    highlighted ? 1.06f : 1f);
                SetForensicsStationLabelScale(
                    station.OtherTelemetry,
                    station.TelemetryBaseScale,
                    highlighted ? 1.06f : 1f);
            }
        }

        private void UpdateForensicsContactStationPulse()
        {
            for (int i = 0; i < forensicsStations.Count; i++)
            {
                ForensicStation station = forensicsStations[i];
                if (station?.PulseRoot == null)
                    continue;
                bool visible = station.Contact &&
                    station.Root != null &&
                    station.Root.gameObject.activeInHierarchy;
                float pulse = visible
                    ? 1f + Mathf.Sin(Time.unscaledTime * 3.6f) * 0.08f
                    : 1f;
                station.PulseRoot.localScale =
                    station.PulseBaseScale * pulse;
            }
        }

        private static void SetForensicsStationLabelScale(
            TextMeshPro label,
            Vector3 baseScale,
            float multiplier)
        {
            if (label != null)
                label.transform.localScale = baseScale * multiplier;
        }

        private void ApplyForensicCarPose(
            ReplayCarView car,
            Vector3 desiredLocal,
            Vector3 desiredTangent)
        {
            if (car == null || stage == null || car.VisualMotionRoot == null)
                return;

            car.ResetVisualMotion();
            Vector3 logicalLocal = stage.InverseTransformPoint(
                car.VisualMotionRoot.position);
            Vector3 worldOffset = stage.TransformVector(
                desiredLocal - logicalLocal);
            Vector3 desiredForward = Vector3.ProjectOnPlane(
                stage.TransformDirection(desiredTangent),
                stage.up);
            car.ApplyVisualMotionFacing(worldOffset, desiredForward);
            UpdateAccidentContactShadow(
                car,
                desiredLocal,
                desiredTangent);
        }

        private bool TryResolveForensicsPose(
            int driverNumber,
            float time,
            out Vector3 point,
            out Vector3 tangent)
        {
            point = contactLocal;
            tangent = forensicsCorridorForward;
            float contact = forensicsAnalysis.PresentationTime;
            if (time <= contact + 0.0001f ||
                forensicsAnalysis.Tier ==
                    CollisionEvidenceTier.ObservedContactAndPost)
            {
                ForensicPath observedPath = driverNumber ==
                    forensicsVictimDriver
                        ? forensicsVictimPath
                        : forensicsOtherPath;
                return TryGetFinalPresentedVehiclePose(
                    observedPath,
                    driverNumber == forensicsVictimDriver,
                    time,
                    out point,
                    out tangent);
            }

            if (forensicsAnalysis.Tier == CollisionEvidenceTier.ContactUnresolved)
                return false;
            ForensicPath contactPath = driverNumber ==
                forensicsVictimDriver
                    ? forensicsVictimPath
                    : forensicsOtherPath;
            if (!TryEvaluateForensicPath(
                    contactPath,
                    contact,
                    out point,
                    out tangent))
            {
                return false;
            }
            float progress = EaseOutCubic(Mathf.InverseLerp(
                contact,
                contact + ForensicsVehicleHoldPostSeconds,
                time));
            if (driverNumber == forensicsVictimDriver)
            {
                point += ResolveVictimOffset(progress);
                tangent = Quaternion.AngleAxis(
                    victimYawSign * VictimYawDegrees * progress,
                    Vector3.up) * tangent;
            }
            else
            {
                point += ResolveOtherOffset(progress);
                tangent = Quaternion.AngleAxis(
                    -victimYawSign * OtherYawDegrees * progress,
                    Vector3.up) * tangent;
            }
            tangent = FlattenNormalized(tangent, forensicsCorridorForward);
            return true;
        }

        private bool TryEvaluateForensicPath(
            ForensicPath path,
            float time,
            out Vector3 point,
            out Vector3 tangent)
        {
            point = contactLocal;
            tangent = forensicsCorridorForward;
            if (path?.Times == null ||
                path.Points == null ||
                path.Tangents == null ||
                path.Times.Length < 2)
            {
                return false;
            }

            float clamped = Mathf.Clamp(
                time,
                path.Times[0],
                path.Times[path.Times.Length - 1]);
            int segment = 0;
            while (segment < path.Times.Length - 2 &&
                   path.Times[segment + 1] < clamped)
            {
                segment++;
            }

            float duration = Mathf.Max(
                0.0001f,
                path.Times[segment + 1] - path.Times[segment]);
            float u = Mathf.Clamp01(
                (clamped - path.Times[segment]) / duration);
            Vector3 a = path.Points[segment];
            Vector3 b = path.Points[segment + 1];
            float chord = Vector3.Distance(a, b);
            Vector3 m0 = path.Tangents[segment] * chord;
            Vector3 m1 = path.Tangents[segment + 1] * chord;
            float u2 = u * u;
            float u3 = u2 * u;
            point =
                (2f * u3 - 3f * u2 + 1f) * a +
                (u3 - 2f * u2 + u) * m0 +
                (-2f * u3 + 3f * u2) * b +
                (u3 - u2) * m1;

            Vector3 segmentVector = b - a;
            float segmentLengthSq = segmentVector.sqrMagnitude;
            if (segmentLengthSq > 0.000001f)
            {
                float projection = Mathf.Clamp01(
                    Vector3.Dot(point - a, segmentVector) /
                    segmentLengthSq);
                Vector3 closest = a + segmentVector * projection;
                float maximumDeviation = carWidth * 0.15f;
                Vector3 deviation = point - closest;
                if (deviation.magnitude > maximumDeviation)
                {
                    point = closest + deviation.normalized *
                        maximumDeviation;
                }
            }

            tangent =
                (6f * u2 - 6f * u) * a +
                (3f * u2 - 4f * u + 1f) * m0 +
                (-6f * u2 + 6f * u) * b +
                (3f * u2 - 2f * u) * m1;
            tangent = FlattenNormalized(
                tangent,
                segmentVector.sqrMagnitude > 0.000001f
                    ? segmentVector
                    : forensicsCorridorForward);
            return true;
        }

        private Vector3 ForensicsLineLift()
        {
            return Vector3.up * carWidth * 0.065f;
        }

        private void OrientForensicsReadableText()
        {
            FaceReadableTextToViewer(forensicsLegend);
            FaceReadableTextToViewer(forensicsLensHud);
            FaceReadableTextToViewer(victimLabel);
            FaceReadableTextToViewer(otherLabel);
            for (int i = 0; i < forensicsStations.Count; i++)
            {
                ForensicStation station = forensicsStations[i];
                if (station == null)
                    continue;
                FaceReadableTextToViewer(station.Header);
                FaceReadableTextToViewer(station.Gap);
                FaceReadableTextToViewer(station.VictimTelemetry);
                FaceReadableTextToViewer(station.OtherTelemetry);
            }

            Transform incidentPanel = warningRoot != null
                ? warningRoot.Find("IncidentPanel")
                : null;
            FaceReadableTextToViewer(
                incidentPanel != null
                    ? incidentPanel.GetComponent<TextMeshPro>()
                    : null);
            Transform contactLabel = impactRoot != null
                ? impactRoot.Find("ContactLabel")
                : null;
            FaceReadableTextToViewer(
                contactLabel != null
                    ? contactLabel.GetComponent<TextMeshPro>()
                    : null);
        }

        private void OnForensicsTimeLensChanged(
            float distance,
            float normalized)
        {
            ApplyForensicsTimeLensValue(normalized);
        }

        private void OnForensicsTimeLensGrabStateChanged(bool grabbed)
        {
            if (forensicsLensHud == null)
                return;

            forensicsLensHud.gameObject.SetActive(grabbed);
            if (grabbed)
                UpdateForensicsLensHud();
        }

        private void ApplyForensicsTimeLensValue(float normalized)
        {
            if (!forensicsConfigured || forensicsImpactReplaying)
                return;

            float contactNormalized = forensicsTimeLensGate != null
                ? forensicsTimeLensGate.ContactNormalized
                : 0.6666667f;
            float contact = forensicsAnalysis.PresentationTime;
            float clamped = Mathf.Clamp01(normalized);
            if (clamped <= contactNormalized)
            {
                float progress = contactNormalized > 0.0001f
                    ? clamped / contactNormalized
                    : 0f;
                forensicsCurrentTime = Mathf.Lerp(
                    forensicsVisibleStartTime,
                    contact,
                    progress);
            }
            else
            {
                float denominator = 1f - contactNormalized;
                float progress = denominator > 0.0001f
                    ? (clamped - contactNormalized) / denominator
                    : 1f;
                forensicsCurrentTime = Mathf.Lerp(
                    contact,
                    forensicsVisibleEndTime,
                    progress);
            }

            forensicsLensActive = true;
            forensicsVehicleVisible =
                forensicsAnalysis.Tier != CollisionEvidenceTier.ContactUnresolved;
            SetForensicsRailProgress(1f);
            SetForensicsTail(forensicsCurrentTime, true);
            UpdateForensicsOutlines(
                forensicsCurrentTime,
                true,
                true);

            if (forensicsAnalysis.Tier ==
                CollisionEvidenceTier.ObservedContactRequiresReconstruction)
            {
                float reconstructedProgress = Mathf.InverseLerp(
                    contact,
                    forensicsVisibleEndTime,
                    forensicsCurrentTime);
                SetRootVisible(postRoot, reconstructedProgress > 0.001f);
                SetPostImpactProgress(reconstructedProgress);
            }
            else
            {
                SetRootVisible(postRoot, false);
            }

            bool atContact = forensicsAnalysis.Tier !=
                             CollisionEvidenceTier.ContactUnresolved &&
                             (Mathf.Abs(
                                  clamped - contactNormalized) <= 0.0005f ||
                              Mathf.Abs(
                                  forensicsCurrentTime - contact) <= 0.025f);
            bool crossedContact =
                forensicsAnalysis.Tier !=
                    CollisionEvidenceTier.ContactUnresolved &&
                float.IsFinite(forensicsPreviousLensNormalized) &&
                (forensicsPreviousLensNormalized - contactNormalized) *
                (clamped - contactNormalized) < 0f;
            float contactDistanceMeters = forensicsTimeLensGate != null
                ? Mathf.Abs(clamped - contactNormalized) *
                  forensicsTimeLensGate.TotalDistanceMeters
                : float.PositiveInfinity;
            if (atContact || crossedContact)
            {
                SetRootVisible(impactRoot, true);
                SetImpactTransient(0.04f);
                SetImpactFlash(false, 0f);
                SetImpactWarningWave(-1f);
                SetImpactBurst(-1f);
                ClearImpactSmoke();
                SetImpactPulse(0.04f);
                if (crossedContact && !atContact)
                    forensicsManualContactPulseRemaining = 0.12f;
                if (!forensicsManualContactLatched)
                {
                    PlayImpactHaptic(0.12f, 0.035f);
                    forensicsManualContactLatched = true;
                }
            }
            else if (forensicsManualContactPulseRemaining <= 0f)
            {
                SetImpactPulse(-1f);
                SetImpactTransient(-1f);
            }
            if (!atContact &&
                !crossedContact &&
                contactDistanceMeters >=
                    ForensicsManualContactResetMeters)
            {
                forensicsManualContactLatched = false;
            }
            forensicsPreviousLensNormalized = clamped;
            UpdateForensicsStatus();
        }

        private void TickForensicsManualContactPulse(float delta)
        {
            if (forensicsManualContactPulseRemaining <= 0f)
                return;

            forensicsManualContactPulseRemaining = Mathf.Max(
                0f,
                forensicsManualContactPulseRemaining -
                Mathf.Max(0f, delta));
            if (forensicsManualContactPulseRemaining <= 0f &&
                !forensicsManualContactLatched)
            {
                SetImpactPulse(-1f);
                SetImpactTransient(-1f);
            }
        }

        private void UpdateForensicsStatus()
        {
            if (!forensicsConfigured)
            {
                forensicsStatus = string.Empty;
                return;
            }

            if (forensicsAnalysis.Tier == CollisionEvidenceTier.ContactUnresolved)
            {
                forensicsStatus = string.IsNullOrWhiteSpace(forensicsReportedTime)
                    ? "CONTACT UNRESOLVED / REPORTED ANCHOR"
                    : $"CONTACT UNRESOLVED / REPORTED {forensicsReportedTime}";
                UpdateForensicsLensHud();
                return;
            }

            float relative = forensicsCurrentTime -
                forensicsAnalysis.PresentationTime;
            if (Mathf.Abs(relative) <= 0.025f)
            {
                forensicsStatus = string.IsNullOrWhiteSpace(forensicsObservedTime)
                    ? "CONTACT / OBSERVED"
                    : $"CONTACT / OBSERVED {forensicsObservedTime}";
            }
            else if (relative < 0f)
            {
                forensicsStatus = $"OBSERVED {relative:0.00}s";
            }
            else if (forensicsAnalysis.Tier ==
                     CollisionEvidenceTier.ObservedContactAndPost)
            {
                forensicsStatus = $"OBSERVED +{relative:0.00}s";
            }
            else
            {
                int percentage = Mathf.RoundToInt(
                    Mathf.InverseLerp(
                        forensicsAnalysis.PresentationTime,
                        forensicsVisibleEndTime,
                        forensicsCurrentTime) * 100f);
                forensicsStatus = $"RECONSTRUCTED {percentage}%";
            }
            UpdateForensicsLensHud();
        }

        private string ResolveInitialForensicsStatus()
        {
            if (forensicsAnalysis == null)
                return string.Empty;
            return forensicsAnalysis.Tier == CollisionEvidenceTier.ContactUnresolved
                ? "PATH EVIDENCE / CONTACT UNRESOLVED"
                : "TRAJECTORY EVIDENCE READY";
        }
    }
}

namespace F1XR.RestAPI.Replay
{
    internal sealed partial class CollisionIncidentPresentation
    {
        private enum AccidentCinematicPhase
        {
            Inactive,
            Approach,
            Focus,
            Isolation,
            Impact,
            Aftermath,
            Recovery,
            Complete
        }

        private readonly struct AccidentFootprintPose
        {
            public AccidentFootprintPose(
                Vector3 pathPoint,
                Vector3 forward,
                Vector2 centerOffset,
                float halfLength,
                float halfWidth)
            {
                PathPoint = pathPoint;
                Forward = FlattenNormalized(forward, Vector3.forward);
                Right = FlattenNormalized(
                    Vector3.Cross(Vector3.up, Forward),
                    Vector3.right);
                Center = pathPoint +
                    Right * centerOffset.x +
                    Forward * centerOffset.y;
                HalfLength = halfLength;
                HalfWidth = halfWidth;
            }

            public Vector3 PathPoint { get; }
            public Vector3 Center { get; }
            public Vector3 Forward { get; }
            public Vector3 Right { get; }
            public float HalfLength { get; }
            public float HalfWidth { get; }
        }

        private readonly struct AccidentVisualContact
        {
            public AccidentVisualContact(
                float time,
                Vector3 point,
                Vector3 normal,
                AccidentFootprintPose victimPose,
                AccidentFootprintPose otherPose)
            {
                Time = time;
                Point = point;
                Normal = normal;
                VictimPose = victimPose;
                OtherPose = otherPose;
            }

            public float Time { get; }
            public Vector3 Point { get; }
            public Vector3 Normal { get; }
            public AccidentFootprintPose VictimPose { get; }
            public AccidentFootprintPose OtherPose { get; }
        }

        private readonly struct AccidentFootprintMeasurement
        {
            public AccidentFootprintMeasurement(
                bool overlapping,
                float separation,
                float overlapDepth,
                float centerDistance,
                Vector3 normal,
                Vector3 victimPoint,
                Vector3 otherPoint)
            {
                Overlapping = overlapping;
                Separation = separation;
                OverlapDepth = overlapDepth;
                CenterDistance = centerDistance;
                Normal = normal;
                VictimPoint = victimPoint;
                OtherPoint = otherPoint;
            }

            public bool Overlapping { get; }
            public float Separation { get; }
            public float OverlapDepth { get; }
            public float CenterDistance { get; }
            public Vector3 Normal { get; }
            public Vector3 VictimPoint { get; }
            public Vector3 OtherPoint { get; }
        }

        private sealed class AccidentEnvironmentMaterialSlot
        {
            public Renderer Renderer;
            public int MaterialIndex;
            public int ColorPropertyId;
            public Color BaseColor;
            public MaterialPropertyBlock OriginalProperties;
            public MaterialPropertyBlock WorkingProperties;
        }

        internal const float CinematicApproachEndProgress = 0.54f;
        internal const float CinematicFocusEndProgress = 0.81f;
        internal const float CinematicIsolationDarkness = 0.97f;
        internal const float CinematicImpactBeatSeconds = 0.25f;
        internal const float CinematicImpactVfxSeconds = 0.35f;
        internal const float CinematicRecoverySeconds = 1.00f;

        private const float CinematicImpactFlashSeconds = 0.11f;
        private const float CinematicImpactBurstSeconds = 0.32f;
        private const float CinematicPostMotionSeconds = 1.20f;
        private const float CinematicContactLoadingSeconds = 0.035f;
        private const float CinematicAftermathSeconds = 0.80f;
        private const float CinematicDarkShellDiameterMeters = 8f;
        private const float CinematicFocusApertureStartDegrees = 84f;
        private const float CinematicFocusApertureMidDegrees = 52f;
        private const float CinematicFocusApertureIsolationDegrees = 34f;
        private const float CinematicFocusApertureLateDegrees = 24f;
        private const float CinematicFocusApertureImpactDegrees = 20f;
        private const float CinematicFocusApertureFeatherDegrees = 8f;
        private const int CinematicFocusApertureSegments = 40;
        private const int CinematicFocusApertureRings = 10;
        private const float CinematicFocusGlowMaximumAlpha = 0.24f;
        private const float CinematicFocusGlowImpactAlpha = 0.52f;
        private const float CinematicFootprintScale = 0.98f;
        private const float CinematicInitialBodyGapMeters = 0.18f;
        private const float CinematicMaximumTrailingOffsetMeters = 0.35f;
        private const float CinematicTrailingReleaseStartProgress = 0.86f;
        private const float CinematicMaximumPhaseOffsetSeconds = 0.65f;
        private const float CinematicHeadingSampleSeconds = 1f / 240f;
        private const float CinematicContactSearchStepSeconds = 1f / 240f;
        private const int CinematicContactRefinementIterations = 14;
        private const float CinematicStrongLateralKickMeters = 0.86f;
        private const float CinematicStrongForwardCarryMeters = 0.74f;
        private const float CinematicStrongYawDegrees = 38f;
        private const float CinematicSecondaryLateralKickMeters = 0.38f;
        private const float CinematicSecondaryForwardCarryMeters = 0.78f;
        private const float CinematicSecondaryYawDegrees = 16f;
        private static readonly Color CinematicWarmVehicleColor =
            new(1f, 0.105f, 0.035f, 1f);
        private static readonly int AccidentBaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int AccidentColorId =
            Shader.PropertyToID("_Color");

        private AccidentCinematicPhase accidentCinematicPhase;
        private GameObject accidentDarknessShell;
        private Renderer accidentDarknessRenderer;
        private Material accidentDarknessMaterial;
        private Material accidentDarknessFeatherMaterial;
        private Mesh accidentDarknessMesh;
        private Vector3[] accidentDarknessVertices;
        private GameObject accidentFocusGlow;
        private Renderer accidentFocusGlowRenderer;
        private Material accidentFocusGlowMaterial;
        private Texture2D accidentFocusGlowTexture;
        private Transform accidentVictimContactShadow;
        private Transform accidentOtherContactShadow;
        private Renderer accidentVictimContactShadowRenderer;
        private Renderer accidentOtherContactShadowRenderer;
        private Material accidentContactShadowMaterial;
        private Texture2D accidentContactShadowTexture;
        private bool accidentContactShadowPolicyLogged;
        private Material accidentImpactFlashMaterial;
        private Material accidentImpactPulseMaterial;
        private float accidentCinematicStartTime;
        private float accidentImpactAge = -1f;
        private float accidentPlaybackSpeed = 1f;
        private float accidentFocusAmount;
        private float accidentApertureDegrees =
            CinematicFocusApertureStartDegrees;
        private float accidentOriginalContactTime;
        private float accidentVisualContactTime;
        private Vector3 accidentVisualContactPoint;
        private Vector3 accidentVisualContactNormal = Vector3.right;
        private Vector3 accidentVictimContactPoint;
        private Vector3 accidentVictimContactTangent = Vector3.forward;
        private Vector3 accidentOtherContactPoint;
        private Vector3 accidentOtherContactTangent = Vector3.forward;
        private Vector3 accidentVictimResponseOriginPoint;
        private Vector3 accidentVictimResponseOriginTangent = Vector3.forward;
        private Vector3 accidentOtherResponseOriginPoint;
        private Vector3 accidentOtherResponseOriginTangent = Vector3.forward;
        private Vector3 accidentVictimResponseOffset;
        private Vector3 accidentOtherResponseOffset;
        private float accidentVictimResponseYaw;
        private float accidentOtherResponseYaw;
        private float accidentVictimFootprintLength;
        private float accidentVictimFootprintWidth;
        private float accidentOtherFootprintLength;
        private float accidentOtherFootprintWidth;
        private float accidentApproachStagingStartTime;
        private float accidentTrailingBackwardOffsetMeters;
        private bool accidentVictimIsTrailing;
        private float accidentVictimInitialPhaseSeconds;
        private float accidentOtherInitialPhaseSeconds;
        private float accidentRacingLineSqueezeMeters;
        private float accidentVictimSeparationSign = -1f;
        private int accidentWarmTintSlotCount;
        private bool accidentCinematicRunning;
        private bool accidentContactReached;
        private bool accidentVisualContactResolved;
        private bool accidentApproachStagingConfigured;
        private bool accidentRecoveryVisibilityLogged;
        private readonly List<AccidentEnvironmentMaterialSlot>
            accidentEnvironmentMaterialSlots = new();

        internal float VisualContactPresentationTime =>
            accidentVisualContactResolved
                ? accidentVisualContactTime
                : forensicsAnalysis?.PresentationTime ?? anchorTime;
        internal Vector3 VisualContactLocalPoint =>
            accidentVisualContactResolved
                ? accidentVisualContactPoint
                : contactLocal;

        private bool ShouldPlayAccidentCinematicEngineAudio =>
            accidentCinematicRunning &&
            !accidentContactReached &&
            (accidentCinematicPhase == AccidentCinematicPhase.Approach ||
             accidentCinematicPhase == AccidentCinematicPhase.Focus ||
             accidentCinematicPhase == AccidentCinematicPhase.Isolation) &&
            forensicsAnalysis != null &&
            forensicsAnalysis.Tier != CollisionEvidenceTier.ContactUnresolved;

        internal static float ResolveAccidentPlaybackSpeed(
            float approachProgress)
        {
            float progress = Mathf.Clamp01(approachProgress);
            if (progress <= CinematicApproachEndProgress)
                return 1.15f;
            if (progress <= CinematicFocusEndProgress)
            {
                return Mathf.Lerp(
                    1.15f,
                    0.85f,
                    Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.InverseLerp(
                            CinematicApproachEndProgress,
                            CinematicFocusEndProgress,
                            progress)));
            }
            if (progress <= 0.90f)
            {
                return Mathf.Lerp(
                    0.85f,
                    0.55f,
                    Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.InverseLerp(
                            CinematicFocusEndProgress,
                            0.90f,
                            progress)));
            }
            if (progress <= 0.97f)
            {
                return Mathf.Lerp(
                    0.55f,
                    0.30f,
                    Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.InverseLerp(0.90f, 0.97f, progress)));
            }

            return Mathf.Lerp(
                0.30f,
                0.15f,
                Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(0.97f, 1f, progress)));
        }

        internal static float ResolveAccidentDarkness(
            float approachProgress)
        {
            float progress = Mathf.Clamp01(approachProgress);
            if (progress <= 0.68f)
                return 0f;
            if (progress <= CinematicFocusEndProgress)
            {
                return Mathf.Lerp(
                    0f,
                    0.12f,
                    Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.InverseLerp(
                            0.68f,
                            CinematicFocusEndProgress,
                            progress)));
            }
            if (progress <= 0.90f)
            {
                return Mathf.Lerp(
                    0.12f,
                    0.55f,
                    Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.InverseLerp(
                            CinematicFocusEndProgress,
                            0.90f,
                            progress)));
            }

            return Mathf.Lerp(
                0.55f,
                CinematicIsolationDarkness,
                Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(
                        0.90f,
                        1f,
                        progress)));
        }

        internal static float ResolveAccidentApertureDegrees(
            float approachProgress)
        {
            float progress = Mathf.Clamp01(approachProgress);
            if (progress <= 0.68f)
                return CinematicFocusApertureStartDegrees;
            if (progress <= CinematicFocusEndProgress)
            {
                return Mathf.Lerp(
                    CinematicFocusApertureStartDegrees,
                    CinematicFocusApertureMidDegrees,
                    Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.InverseLerp(
                            0.68f,
                            CinematicFocusEndProgress,
                            progress)));
            }
            if (progress <= 0.94f)
            {
                return Mathf.Lerp(
                    CinematicFocusApertureMidDegrees,
                    CinematicFocusApertureIsolationDegrees,
                    Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.InverseLerp(
                            CinematicFocusEndProgress,
                            0.94f,
                            progress)));
            }
            if (progress <= 0.98f)
            {
                return Mathf.Lerp(
                    CinematicFocusApertureIsolationDegrees,
                    CinematicFocusApertureLateDegrees,
                    Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.InverseLerp(
                            0.94f,
                            0.98f,
                            progress)));
            }

            return Mathf.Lerp(
                CinematicFocusApertureLateDegrees,
                CinematicFocusApertureImpactDegrees,
                Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(0.98f, 1f, progress)));
        }

        private void BeginAccidentCinematic()
        {
            if (!forensicsConfigured || forensicsAnalysis == null)
                return;

            EnsureAccidentCinematicVisuals();
            accidentCinematicStartTime = accidentApproachStagingConfigured
                ? accidentApproachStagingStartTime
                : Mathf.Min(
                    forensicsAnalysis.VehicleRevealTime,
                    VisualContactPresentationTime - 0.02f);
            forensicsCurrentTime = accidentCinematicStartTime;
            forensicsWallTime = 0f;
            revealTime = 0f;
            impactReplayTime = 0f;
            accidentImpactAge = -1f;
            accidentPlaybackSpeed = 1f;
            accidentCinematicRunning = true;
            accidentContactReached = false;
            accidentRecoveryVisibilityLogged = false;
            accidentCinematicPhase = AccidentCinematicPhase.Approach;
            forensicsRevealRunning = true;
            forensicsImpactReplaying = false;
            forensicsVehicleVisible = true;
            forensicsFinalApplied = false;
            forensicsLensActive = false;
            revealRunning = true;
            revealComplete = false;
            impactReplaying = false;
            impactTriggered = false;
            finalTableauApplied = false;
            secondaryHapticTriggered = false;
            secondaryHapticCountdown = -1f;
            Phase = CollisionPresentationPhase.PreImpact;
            forensicsStatus = "ACCIDENT APPROACH";

            if (impactAudio != null)
                impactAudio.Stop();
            ApplyAccidentVehicleContrast();
            ResetVehicleMotion();
            ClearImpactSmoke();
            SetCarsVisible(true);
            SetRootVisible(forensicsRoot, true);
            SetRootVisible(
                forensicsTrackRoot,
                forensicsTrackUserVisible);
            SetForensicsTrackProgress(1f);
            HideAccidentForensicClutter();
            ResetAccidentImpactVisuals();
            SetAccidentDarkness(0f);
            SetAccidentAperture(CinematicFocusApertureStartDegrees);
            SetAccidentFocus(0f);
            if (island != null)
            {
                island.gameObject.SetActive(false);
                island.localScale = islandBaseScale;
            }
        }

        private float TickAccidentCinematic(float delta)
        {
            if (!accidentCinematicRunning || forensicsAnalysis == null)
                return forensicsCurrentTime;

            float safeDelta = Mathf.Max(0f, delta);
            TickSecondaryImpactHaptic(safeDelta);
            forensicsWallTime += safeDelta;
            revealTime = forensicsWallTime;

            float contactTime = VisualContactPresentationTime;
            if (!accidentContactReached)
            {
                float progress = Mathf.InverseLerp(
                    accidentCinematicStartTime,
                    contactTime,
                    forensicsCurrentTime);
                accidentPlaybackSpeed = ResolveAccidentPlaybackSpeed(
                    progress);
                forensicsCurrentTime = Mathf.Min(
                    contactTime,
                    forensicsCurrentTime +
                    safeDelta * accidentPlaybackSpeed);
                progress = Mathf.InverseLerp(
                    accidentCinematicStartTime,
                    contactTime,
                    forensicsCurrentTime);
                ApplyAccidentApproach(progress);

                if (forensicsCurrentTime >= contactTime - 0.0001f)
                    BeginAccidentImpact();

                return forensicsCurrentTime;
            }

            accidentImpactAge += safeDelta;
            forensicsCurrentTime = contactTime;
            UpdateAccidentImpactVisuals(accidentImpactAge);

            if (accidentImpactAge < CinematicImpactBeatSeconds)
            {
                accidentCinematicPhase = AccidentCinematicPhase.Impact;
                accidentPlaybackSpeed = 0.12f;
                Phase = CollisionPresentationPhase.Impact;
                forensicsStatus = "ACCIDENT IMPACT";
                SetAccidentDarkness(CinematicIsolationDarkness);
                SetAccidentAperture(
                    CinematicFocusApertureImpactDegrees);
                SetAccidentFocus(1f);
                return forensicsCurrentTime;
            }

            float responseEnd =
                CinematicContactLoadingSeconds +
                CinematicPostMotionSeconds;
            float recoveryStart =
                responseEnd + CinematicAftermathSeconds;
            if (accidentImpactAge < recoveryStart)
            {
                accidentCinematicPhase = AccidentCinematicPhase.Aftermath;
                accidentPlaybackSpeed = accidentImpactAge < responseEnd
                    ? 0.15f
                    : 0.35f;
                Phase = CollisionPresentationPhase.PostImpact;
                forensicsStatus = accidentImpactAge < responseEnd
                    ? "ACCIDENT RESPONSE"
                    : "ACCIDENT AFTERMATH";
                SetAccidentDarkness(CinematicIsolationDarkness);
                SetAccidentAperture(
                    CinematicFocusApertureImpactDegrees);
                SetAccidentFocus(1f);
                return forensicsCurrentTime;
            }

            accidentCinematicPhase = AccidentCinematicPhase.Recovery;
            Phase = CollisionPresentationPhase.PostImpact;
            forensicsStatus = "ACCIDENT RECOVERY";
            float recoveryProgress = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    recoveryStart,
                    recoveryStart +
                    CinematicRecoverySeconds,
                    accidentImpactAge));
            accidentPlaybackSpeed = Mathf.Lerp(
                0.12f,
                1f,
                recoveryProgress);
            SetAccidentDarkness(Mathf.Lerp(
                CinematicIsolationDarkness,
                0f,
                recoveryProgress));
            SetAccidentAperture(Mathf.Lerp(
                CinematicFocusApertureImpactDegrees,
                CinematicFocusApertureStartDegrees,
                recoveryProgress));
            SetAccidentFocus(1f - recoveryProgress);
            if (!accidentRecoveryVisibilityLogged)
            {
                accidentRecoveryVisibilityLogged = true;
                LogAccidentCarVisibility("RECOVERY");
            }

            if (recoveryProgress >= 0.9999f)
                CompleteAccidentCinematic();

            return forensicsCurrentTime;
        }

        private void ApplyAccidentApproach(float progress)
        {
            float darkness = ResolveAccidentDarkness(progress);
            SetAccidentDarkness(darkness);
            SetAccidentAperture(
                ResolveAccidentApertureDegrees(progress));
            float focus = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    CinematicApproachEndProgress,
                    1f,
                    progress));
            SetAccidentFocus(focus);
            HideAccidentForensicClutter();

            if (progress < CinematicApproachEndProgress)
            {
                accidentCinematicPhase = AccidentCinematicPhase.Approach;
                forensicsStatus = "ACCIDENT APPROACH";
            }
            else if (progress < CinematicFocusEndProgress)
            {
                accidentCinematicPhase = AccidentCinematicPhase.Focus;
                forensicsStatus = "ACCIDENT FOCUS";
            }
            else
            {
                accidentCinematicPhase = AccidentCinematicPhase.Isolation;
                forensicsStatus = "ACCIDENT ISOLATION";
            }

            Phase = CollisionPresentationPhase.PreImpact;
        }

        private void BeginAccidentImpact()
        {
            CaptureAccidentResponseOrigins();
            accidentContactReached = true;
            accidentImpactAge = 0f;
            accidentCinematicPhase = AccidentCinematicPhase.Impact;
            accidentPlaybackSpeed = 0.12f;
            forensicsCurrentTime = VisualContactPresentationTime;
            Phase = CollisionPresentationPhase.Impact;
            forensicsStatus = "ACCIDENT IMPACT";
            SetAccidentDarkness(CinematicIsolationDarkness);
            SetAccidentAperture(
                CinematicFocusApertureImpactDegrees);
            SetAccidentFocus(1f);
            TriggerForensicsImpact();
            UpdateAccidentImpactVisuals(0f);
            LogAccidentCarVisibility("IMPACT");
            Debug.Log(
                $"[CollisionIncident] impactAtFirstContact " +
                $"presentationTime={VisualContactPresentationTime:0.000000}, " +
                $"wallFromStart={forensicsWallTime:0.000}s.");
        }

        private void CaptureAccidentResponseOrigins()
        {
            Vector3 victimPoint = accidentVictimContactPoint;
            Vector3 victimTangent = accidentVictimContactTangent;
            Vector3 otherPoint = accidentOtherContactPoint;
            Vector3 otherTangent = accidentOtherContactTangent;
            if (TryGetFinalPresentedVehiclePose(
                    forensicsVictimPath,
                    true,
                    VisualContactPresentationTime,
                    out Vector3 resolvedVictimPoint,
                    out Vector3 resolvedVictimTangent))
            {
                victimPoint = resolvedVictimPoint;
                victimTangent = resolvedVictimTangent;
            }
            if (TryGetFinalPresentedVehiclePose(
                    forensicsOtherPath,
                    false,
                    VisualContactPresentationTime,
                    out Vector3 resolvedOtherPoint,
                    out Vector3 resolvedOtherTangent))
            {
                otherPoint = resolvedOtherPoint;
                otherTangent = resolvedOtherTangent;
            }

            accidentVictimResponseOriginPoint = victimPoint;
            accidentVictimResponseOriginTangent = victimTangent;
            accidentOtherResponseOriginPoint = otherPoint;
            accidentOtherResponseOriginTangent = otherTangent;

            float victimPositionDelta = stage != null
                ? stage.TransformVector(
                    victimPoint - accidentVictimContactPoint).magnitude
                : Vector3.Distance(
                    victimPoint,
                    accidentVictimContactPoint);
            float otherPositionDelta = stage != null
                ? stage.TransformVector(
                    otherPoint - accidentOtherContactPoint).magnitude
                : Vector3.Distance(
                    otherPoint,
                    accidentOtherContactPoint);
            float victimYawDelta = Vector3.SignedAngle(
                accidentVictimContactTangent,
                victimTangent,
                Vector3.up);
            float otherYawDelta = Vector3.SignedAngle(
                accidentOtherContactTangent,
                otherTangent,
                Vector3.up);
            Debug.Log(
                $"[CollisionContinuity] responseOrigin " +
                $"space=AccidentPresentationRootLocal, " +
                $"A.positionDelta={victimPositionDelta:0.000000}m, " +
                $"A.yawDelta={victimYawDelta:0.000000}deg, " +
                $"B.positionDelta={otherPositionDelta:0.000000}m, " +
                $"B.yawDelta={otherYawDelta:0.000000}deg, " +
                $"source=TryGetFinalPresentedVehiclePose.");
        }

        private void CompleteAccidentCinematic()
        {
            accidentCinematicRunning = false;
            accidentCinematicPhase = AccidentCinematicPhase.Complete;
            accidentPlaybackSpeed = 1f;
            forensicsCurrentTime = VisualContactPresentationTime;
            forensicsRevealRunning = false;
            forensicsImpactReplaying = false;
            forensicsVehicleVisible = true;
            revealRunning = false;
            revealComplete = true;
            impactReplaying = false;
            finalTableauApplied = false;
            Phase = CollisionPresentationPhase.PostImpact;
            forensicsStatus = "ACCIDENT COMPLETE";
            SetAccidentDarkness(0f);
            SetAccidentAperture(CinematicFocusApertureStartDegrees);
            SetAccidentFocus(0f);
            ResetAccidentImpactVisuals();
            HideAccidentForensicClutter();
            LogAccidentCarVisibility("COMPLETE");
        }

        private void LogAccidentCarVisibility(string phase)
        {
            LogAccidentCarVisibility(phase, "A", victim);
            LogAccidentCarVisibility(phase, "B", other);
        }

        private static void LogAccidentCarVisibility(
            string phase,
            string label,
            ReplayCarView car)
        {
            if (car == null)
            {
                Debug.LogWarning(
                    $"[CollisionIncident] visibility {phase} {label}=MISSING.");
                return;
            }

            Renderer[] renderers = car.LogicalRoot
                .GetComponentsInChildren<Renderer>(true);
            int enabledRenderers = 0;
            for (int index = 0; index < renderers.Length; index++)
            {
                if (renderers[index] != null && renderers[index].enabled)
                    enabledRenderers++;
            }
            Debug.Log(
                $"[CollisionIncident] visibility {phase} {label} " +
                $"active={car.LogicalRoot.gameObject.activeInHierarchy}, " +
                $"renderers={enabledRenderers}/{renderers.Length}.");
        }

        private void ResetAccidentCinematic()
        {
            accidentCinematicRunning = false;
            accidentContactReached = false;
            accidentCinematicPhase = AccidentCinematicPhase.Inactive;
            accidentImpactAge = -1f;
            accidentPlaybackSpeed = 1f;
            ClearAccidentVehicleContrast();
            SetAccidentDarkness(0f);
            SetAccidentAperture(CinematicFocusApertureStartDegrees);
            SetAccidentFocus(0f);
            ResetAccidentImpactVisuals();
        }

        private void ClearAccidentCinematic()
        {
            ResetAccidentCinematic();
            ReleaseAccidentEnvironmentRenderers();
            if (accidentDarknessShell != null)
                UnityEngine.Object.Destroy(accidentDarknessShell);
            if (accidentFocusGlow != null)
                UnityEngine.Object.Destroy(accidentFocusGlow);
            if (accidentVictimContactShadow != null)
            {
                UnityEngine.Object.Destroy(
                    accidentVictimContactShadow.gameObject);
            }
            if (accidentOtherContactShadow != null)
            {
                UnityEngine.Object.Destroy(
                    accidentOtherContactShadow.gameObject);
            }
            DestroyAccidentMaterial(accidentDarknessMaterial);
            DestroyAccidentMaterial(accidentDarknessFeatherMaterial);
            DestroyAccidentMaterial(accidentFocusGlowMaterial);
            DestroyAccidentMaterial(accidentContactShadowMaterial);
            DestroyAccidentMaterial(accidentImpactFlashMaterial);
            DestroyAccidentMaterial(accidentImpactPulseMaterial);
            if (accidentDarknessMesh != null)
            {
                meshes.Remove(accidentDarknessMesh);
                UnityEngine.Object.Destroy(accidentDarknessMesh);
            }
            if (accidentFocusGlowTexture != null)
                UnityEngine.Object.Destroy(accidentFocusGlowTexture);
            if (accidentContactShadowTexture != null)
                UnityEngine.Object.Destroy(accidentContactShadowTexture);

            accidentDarknessShell = null;
            accidentDarknessRenderer = null;
            accidentDarknessMaterial = null;
            accidentDarknessFeatherMaterial = null;
            accidentDarknessMesh = null;
            accidentDarknessVertices = null;
            accidentFocusGlow = null;
            accidentFocusGlowRenderer = null;
            accidentFocusGlowMaterial = null;
            accidentFocusGlowTexture = null;
            accidentVictimContactShadow = null;
            accidentOtherContactShadow = null;
            accidentVictimContactShadowRenderer = null;
            accidentOtherContactShadowRenderer = null;
            accidentContactShadowMaterial = null;
            accidentContactShadowTexture = null;
            accidentContactShadowPolicyLogged = false;
            accidentImpactFlashMaterial = null;
            accidentImpactPulseMaterial = null;
            accidentVisualContactResolved = false;
            accidentOriginalContactTime = 0f;
            accidentVisualContactTime = 0f;
            accidentVisualContactPoint = Vector3.zero;
            accidentVisualContactNormal = Vector3.right;
            accidentVictimContactPoint = Vector3.zero;
            accidentOtherContactPoint = Vector3.zero;
            accidentVictimResponseOriginPoint = Vector3.zero;
            accidentVictimResponseOriginTangent = Vector3.forward;
            accidentOtherResponseOriginPoint = Vector3.zero;
            accidentOtherResponseOriginTangent = Vector3.forward;
            accidentVictimResponseOffset = Vector3.zero;
            accidentOtherResponseOffset = Vector3.zero;
            accidentVictimResponseYaw = 0f;
            accidentOtherResponseYaw = 0f;
            accidentApproachStagingStartTime = 0f;
            accidentTrailingBackwardOffsetMeters = 0f;
            accidentVictimIsTrailing = false;
            accidentApproachStagingConfigured = false;
        }

        private void DestroyAccidentMaterial(Material material)
        {
            if (material == null)
                return;
            materials.Remove(material);
            UnityEngine.Object.Destroy(material);
        }

        private void HideAccidentForensicClutter()
        {
            SetRootVisible(earlyGhostRoot, false);
            SetRootVisible(lateGhostRoot, false);
            SetRootVisible(postRoot, false);
            SetRootVisible(warningRoot, false);
            SetRootVisible(annotationRoot, false);
            SetRootVisible(forensicsStableRailRoot, false);
            SetRootVisible(forensicsRailRoot, false);
            SetRootVisible(forensicsTailRoot, false);
            SetRootVisible(forensicsMarkerRoot, false);
            SetRootVisible(forensicsOutlineRoot, false);
            SetRootVisible(forensicsStationRoot, false);
            SetRootVisible(forensicsLegend != null
                ? forensicsLegend.transform
                : null, false);
            SetRootVisible(forensicsLensHud != null
                ? forensicsLensHud.transform
                : null, false);
            SetRootVisible(forensicsTimeLensGate != null
                ? forensicsTimeLensGate.transform
                : null, false);
            forensicsTimeLensGate?.SetAvailable(false);
            if (victimIncomingLine != null)
                victimIncomingLine.enabled = false;
            if (otherIncomingLine != null)
                otherIncomingLine.enabled = false;
            SetForensicsTail(float.NaN);
            UpdateForensicsOutlines(
                float.NegativeInfinity,
                false,
                false);
        }

        private void EnsureAccidentCinematicVisuals()
        {
            Camera viewer = Camera.main;
            if (accidentDarknessShell == null && viewer != null)
            {
                accidentDarknessShell = new GameObject(
                    "CollisionRoomFocusAperture",
                    typeof(MeshFilter),
                    typeof(MeshRenderer));
                accidentDarknessShell.name =
                    "CollisionRoomFocusAperture";
                accidentDarknessMesh = CreateAccidentApertureMesh();
                meshes.Add(accidentDarknessMesh);
                accidentDarknessShell.GetComponent<MeshFilter>()
                    .sharedMesh = accidentDarknessMesh;

                accidentDarknessRenderer =
                    accidentDarknessShell.GetComponent<Renderer>();
                accidentDarknessMaterial = CreateTransparentMaterial(
                    "Runtime_CollisionRoomDarkness",
                    new Color(0.002f, 0.005f, 0.01f, 0f));
                accidentDarknessFeatherMaterial =
                    CreateTransparentMaterial(
                        "Runtime_CollisionRoomDarknessFeather",
                        new Color(0.002f, 0.005f, 0.01f, 0f));
                if (accidentDarknessMaterial.HasProperty("_Cull"))
                    accidentDarknessMaterial.SetFloat("_Cull", 0f);
                if (accidentDarknessFeatherMaterial.HasProperty("_Cull"))
                    accidentDarknessFeatherMaterial.SetFloat("_Cull", 0f);
                if (accidentDarknessMaterial.HasProperty("_ZWrite"))
                    accidentDarknessMaterial.SetFloat("_ZWrite", 0f);
                if (accidentDarknessFeatherMaterial.HasProperty("_ZWrite"))
                    accidentDarknessFeatherMaterial.SetFloat("_ZWrite", 0f);
                accidentDarknessMaterial.renderQueue = 2990;
                accidentDarknessFeatherMaterial.renderQueue = 2989;
                accidentDarknessRenderer.sharedMaterials = new[]
                {
                    accidentDarknessFeatherMaterial,
                    accidentDarknessMaterial
                };
                accidentDarknessRenderer.shadowCastingMode =
                    ShadowCastingMode.Off;
                accidentDarknessRenderer.receiveShadows = false;
                accidentDarknessRenderer.motionVectorGenerationMode =
                    MotionVectorGenerationMode.ForceNoMotion;
                accidentDarknessRenderer.enabled = false;
            }

            if (accidentFocusGlow == null && presentationRoot != null)
            {
                accidentFocusGlow = GameObject.CreatePrimitive(
                    PrimitiveType.Quad);
                accidentFocusGlow.name = "CollisionIncidentFocusGlow";
                accidentFocusGlow.transform.SetParent(
                    presentationRoot,
                    false);
                accidentFocusGlow.transform.localPosition = contactLocal +
                    Vector3.up * carWidth * 0.025f;
                accidentFocusGlow.transform.localRotation =
                    Quaternion.Euler(90f, 0f, 0f);
                Collider glowCollider =
                    accidentFocusGlow.GetComponent<Collider>();
                if (glowCollider != null)
                    UnityEngine.Object.Destroy(glowCollider);

                accidentFocusGlowTexture = CreateRadialAlphaTexture();
                accidentFocusGlowTexture.name =
                    "Runtime_CollisionFocusGlowAlpha";
                accidentFocusGlowMaterial =
                    ReplayCarVisualUtil.CreateSelectionMaterial(
                        new Color(0.34f, 0.7f, 1.2f, 0f));
                accidentFocusGlowMaterial.name =
                    "Runtime_CollisionFocusGlow";
                if (accidentFocusGlowMaterial.HasProperty("_BaseMap"))
                {
                    accidentFocusGlowMaterial.SetTexture(
                        "_BaseMap",
                        accidentFocusGlowTexture);
                }
                if (accidentFocusGlowMaterial.HasProperty("_MainTex"))
                {
                    accidentFocusGlowMaterial.SetTexture(
                        "_MainTex",
                        accidentFocusGlowTexture);
                }
                accidentFocusGlowMaterial.renderQueue = 2995;
                materials.Add(accidentFocusGlowMaterial);
                accidentFocusGlowRenderer =
                    accidentFocusGlow.GetComponent<Renderer>();
                accidentFocusGlowRenderer.sharedMaterial =
                    accidentFocusGlowMaterial;
                accidentFocusGlowRenderer.shadowCastingMode =
                    ShadowCastingMode.Off;
                accidentFocusGlowRenderer.receiveShadows = false;
                accidentFocusGlowRenderer.motionVectorGenerationMode =
                    MotionVectorGenerationMode.ForceNoMotion;
                accidentFocusGlowRenderer.enabled = false;
                Debug.Log(
                    "[CollisionVisualPolish] rectangular ground focus " +
                    "quad=DISABLED; impact emphasis uses flash, radial " +
                    "rings, sparks and debris only.");
            }

            if (!accidentContactShadowPolicyLogged)
            {
                accidentContactShadowPolicyLogged = true;
                Debug.Log(
                    "[CollisionVisualPolish] Quest-safe vehicle contact " +
                    "shadow quads=DISABLED; procedural transparent " +
                    "quads are not created.");
            }

            if (accidentImpactFlashMaterial == null)
            {
                accidentImpactFlashMaterial = CreateTransparentMaterial(
                    "Runtime_CollisionImpactCoolFlash",
                    new Color(1.7f, 1.9f, 2.2f, 0f),
                    true);
                Renderer[] flashRenderers = impactFlashRoot != null
                    ? impactFlashRoot.GetComponentsInChildren<Renderer>(true)
                    : Array.Empty<Renderer>();
                for (int i = 0; i < flashRenderers.Length; i++)
                    flashRenderers[i].sharedMaterial =
                        accidentImpactFlashMaterial;
            }

            if (accidentImpactPulseMaterial == null)
            {
                accidentImpactPulseMaterial = CreateTransparentMaterial(
                    "Runtime_CollisionImpactCoolPulse",
                    new Color(0.42f, 1.05f, 1.65f, 0f),
                    true);
                for (int i = 0; i < pulseRenderers.Count; i++)
                {
                    if (pulseRenderers[i] != null)
                    {
                        pulseRenderers[i].sharedMaterial =
                            accidentImpactPulseMaterial;
                    }
                }
            }

            UpdateAccidentAperturePoseAndSize();
        }

        private void SetAccidentDarkness(float amount)
        {
            float clamped = Mathf.Clamp01(amount);
            SetAccidentEnvironmentDarkness(clamped);

            if (accidentDarknessRenderer == null ||
                accidentDarknessMaterial == null)
            {
                return;
            }

            accidentDarknessRenderer.enabled = clamped > 0.001f;
            ReplayCarVisualUtil.SetMaterialColor(
                accidentDarknessMaterial,
                new Color(0.002f, 0.005f, 0.01f, clamped));
            ReplayCarVisualUtil.SetMaterialColor(
                accidentDarknessFeatherMaterial,
                new Color(
                    0.002f,
                    0.005f,
                    0.01f,
                    clamped * 0.42f));
        }

        private void SetAccidentEnvironmentDarkness(float darkness)
        {
            if (accidentEnvironmentMaterialSlots.Count == 0)
                CaptureAccidentEnvironmentRenderers();

            bool restore = darkness <= 0.001f;
            float visibility = 1f - Mathf.Clamp01(darkness);
            for (int index = 0;
                 index < accidentEnvironmentMaterialSlots.Count;
                 index++)
            {
                AccidentEnvironmentMaterialSlot slot =
                    accidentEnvironmentMaterialSlots[index];
                if (slot.Renderer == null)
                    continue;

                if (restore)
                {
                    slot.Renderer.SetPropertyBlock(
                        slot.OriginalProperties,
                        slot.MaterialIndex);
                    continue;
                }

                slot.WorkingProperties.Clear();
                slot.Renderer.GetPropertyBlock(
                    slot.WorkingProperties,
                    slot.MaterialIndex);
                Color baseColor = slot.BaseColor;
                slot.WorkingProperties.SetColor(
                    slot.ColorPropertyId,
                    new Color(
                        baseColor.r * visibility,
                        baseColor.g * visibility,
                        baseColor.b * visibility,
                        baseColor.a));
                slot.Renderer.SetPropertyBlock(
                    slot.WorkingProperties,
                    slot.MaterialIndex);
            }
        }

        private void CaptureAccidentEnvironmentRenderers()
        {
            if (forensicsTrackRoot == null)
                return;

            Renderer[] renderers = forensicsTrackRoot
                .GetComponentsInChildren<Renderer>(true);
            for (int rendererIndex = 0;
                 rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                if (renderer == null)
                    continue;

                Material[] sharedMaterials = renderer.sharedMaterials;
                for (int materialIndex = 0;
                     materialIndex < sharedMaterials.Length;
                     materialIndex++)
                {
                    Material material = sharedMaterials[materialIndex];
                    if (material == null)
                        continue;

                    int colorPropertyId;
                    if (material.HasProperty(AccidentBaseColorId))
                        colorPropertyId = AccidentBaseColorId;
                    else if (material.HasProperty(AccidentColorId))
                        colorPropertyId = AccidentColorId;
                    else
                        continue;

                    MaterialPropertyBlock originalProperties = new();
                    renderer.GetPropertyBlock(
                        originalProperties,
                        materialIndex);
                    Color baseColor = originalProperties.HasColor(
                        colorPropertyId)
                        ? originalProperties.GetColor(colorPropertyId)
                        : material.GetColor(colorPropertyId);
                    accidentEnvironmentMaterialSlots.Add(
                        new AccidentEnvironmentMaterialSlot
                        {
                            Renderer = renderer,
                            MaterialIndex = materialIndex,
                            ColorPropertyId = colorPropertyId,
                            BaseColor = baseColor,
                            OriginalProperties = originalProperties,
                            WorkingProperties = new MaterialPropertyBlock()
                        });
                }
            }

            if (accidentEnvironmentMaterialSlots.Count > 0)
            {
                Debug.Log(
                    "[CollisionCinematicDarkness] proxy environment " +
                    $"renderers={renderers.Length}, " +
                    $"materialSlots={accidentEnvironmentMaterialSlots.Count}, " +
                    "scope=SuzukaInspiredAccidentProxyTrackRoot, " +
                    "mode=MaterialPropertyBlock.");
            }
        }

        private void ReleaseAccidentEnvironmentRenderers()
        {
            for (int index = 0;
                 index < accidentEnvironmentMaterialSlots.Count;
                 index++)
            {
                AccidentEnvironmentMaterialSlot slot =
                    accidentEnvironmentMaterialSlots[index];
                if (slot.Renderer != null)
                {
                    slot.Renderer.SetPropertyBlock(
                        slot.OriginalProperties,
                        slot.MaterialIndex);
                }
            }
            accidentEnvironmentMaterialSlots.Clear();
        }

        private void SetAccidentAperture(float clearRegionDegrees)
        {
            accidentApertureDegrees = Mathf.Clamp(
                clearRegionDegrees,
                CinematicFocusApertureImpactDegrees,
                CinematicFocusApertureStartDegrees);
            UpdateAccidentAperturePoseAndSize();
        }

        private void SetAccidentFocus(float amount)
        {
            accidentFocusAmount = Mathf.Clamp01(amount);
            if (accidentFocusGlowRenderer != null)
                accidentFocusGlowRenderer.enabled = false;
        }

        private Renderer CreateAccidentContactShadow(
            string name,
            out Transform shadow)
        {
            GameObject shadowObject = GameObject.CreatePrimitive(
                PrimitiveType.Quad);
            shadowObject.name = name;
            shadowObject.transform.SetParent(presentationRoot, false);
            shadowObject.transform.localScale = new Vector3(
                carWidth * 1.18f,
                carLength * 0.82f,
                1f);
            Collider collider = shadowObject.GetComponent<Collider>();
            if (collider != null)
                UnityEngine.Object.Destroy(collider);
            Renderer renderer = shadowObject.GetComponent<Renderer>();
            renderer.sharedMaterial = accidentContactShadowMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            renderer.enabled = false;
            shadow = shadowObject.transform;
            return renderer;
        }

        private void UpdateAccidentContactShadow(
            ReplayCarView car,
            Vector3 desiredLocal,
            Vector3 desiredTangent)
        {
            Transform shadow = car == victim
                ? accidentVictimContactShadow
                : car == other
                    ? accidentOtherContactShadow
                    : null;
            if (shadow == null || stage == null || presentationRoot == null)
                return;

            Vector3 worldPosition = stage.TransformPoint(desiredLocal);
            Vector3 localPosition =
                presentationRoot.InverseTransformPoint(worldPosition);
            localPosition.y = presentationRoot.InverseTransformPoint(
                stage.TransformPoint(contactLocal)).y +
                carWidth * 0.021f;
            Vector3 worldForward = stage.TransformDirection(
                FlattenNormalized(desiredTangent, forensicsCorridorForward));
            Vector3 localForward = FlattenNormalized(
                presentationRoot.InverseTransformDirection(worldForward),
                forensicsCorridorForward);
            shadow.localPosition = localPosition;
            shadow.localRotation = Quaternion.LookRotation(
                localForward,
                Vector3.up) * Quaternion.Euler(90f, 0f, 0f);
        }

        private void SetAccidentContactShadowsVisible(bool visible)
        {
            if (accidentVictimContactShadowRenderer != null)
                accidentVictimContactShadowRenderer.enabled = visible;
            if (accidentOtherContactShadowRenderer != null)
                accidentOtherContactShadowRenderer.enabled = visible;
        }

        private void ResetAccidentImpactVisuals()
        {
            SetRootVisible(impactRoot, false);
            SetImpactFlash(false, 0f);
            SetImpactPulse(-1f);
            SetImpactTransient(-1f);
            SetImpactWarningWave(-1f);
            SetImpactBurst(-1f);
            SetForensicsTrackWarningPulse(-1f);
            ClearImpactSmoke();
            SetAccidentImpactFlashAlpha(0f);
            SetAccidentFocus(accidentFocusAmount);
        }

        private void UpdateAccidentImpactVisuals(float age)
        {
            if (age < 0f || age > CinematicImpactVfxSeconds)
            {
                ResetAccidentImpactVisuals();
                return;
            }

            SetRootVisible(impactRoot, true);
            HideAccidentImpactAnnotations();
            SetImpactFlash(age <= CinematicImpactFlashSeconds, age);
            SetImpactPulse(age);
            SetImpactTransient(age);
            SetImpactWarningWave(-1f);
            SetForensicsTrackWarningPulse(-1f);
            SetImpactBurst(age <= CinematicImpactBurstSeconds
                ? age
                : -1f);
            SetAccidentImpactFlashAlpha(
                ResolveAccidentImpactFlash(age));
            SetAccidentFocus(accidentFocusAmount);
        }

        private Mesh CreateAccidentApertureMesh()
        {
            int rowLength = CinematicFocusApertureSegments + 1;
            int vertexCount =
                (CinematicFocusApertureRings + 1) * rowLength;
            accidentDarknessVertices = new Vector3[vertexCount];
            Vector2[] uv = new Vector2[vertexCount];
            for (int ring = 0;
                 ring <= CinematicFocusApertureRings;
                 ring++)
            {
                for (int segment = 0;
                     segment <= CinematicFocusApertureSegments;
                     segment++)
                {
                    int index = ring * rowLength + segment;
                    uv[index] = new Vector2(
                        segment / (float)CinematicFocusApertureSegments,
                        ring / (float)CinematicFocusApertureRings);
                }
            }

            int[] featherTriangles = new int[
                CinematicFocusApertureSegments * 6];
            int[] darknessTriangles = new int[
                (CinematicFocusApertureRings - 1) *
                CinematicFocusApertureSegments * 6];
            FillAccidentApertureTriangles(
                featherTriangles,
                0,
                0,
                rowLength);
            int triangleIndex = 0;
            for (int ring = 1;
                 ring < CinematicFocusApertureRings;
                 ring++)
            {
                triangleIndex = FillAccidentApertureTriangles(
                    darknessTriangles,
                    triangleIndex,
                    ring,
                    rowLength);
            }

            Mesh mesh = new()
            {
                name = "Runtime_CollisionFocusAperture"
            };
            mesh.MarkDynamic();
            mesh.vertices = accidentDarknessVertices;
            mesh.uv = uv;
            mesh.subMeshCount = 2;
            mesh.SetTriangles(featherTriangles, 0, false);
            mesh.SetTriangles(darknessTriangles, 1, false);
            mesh.bounds = new Bounds(
                Vector3.zero,
                Vector3.one * CinematicDarkShellDiameterMeters);
            accidentDarknessMesh = mesh;
            UpdateAccidentApertureMesh(
                CinematicFocusApertureStartDegrees);
            return mesh;
        }

        private static int FillAccidentApertureTriangles(
            int[] triangles,
            int triangleIndex,
            int ring,
            int rowLength)
        {
            for (int segment = 0;
                 segment < CinematicFocusApertureSegments;
                 segment++)
            {
                int current = ring * rowLength + segment;
                int next = current + rowLength;
                triangles[triangleIndex++] = current;
                triangles[triangleIndex++] = next + 1;
                triangles[triangleIndex++] = next;
                triangles[triangleIndex++] = current;
                triangles[triangleIndex++] = current + 1;
                triangles[triangleIndex++] = next + 1;
            }
            return triangleIndex;
        }

        private void UpdateAccidentAperturePoseAndSize()
        {
            if (accidentDarknessShell == null ||
                accidentDarknessMesh == null ||
                stage == null)
            {
                return;
            }

            Camera viewer = Camera.main;
            if (viewer == null)
                return;

            Vector3 target = stage.TransformPoint(
                contactLocal + Vector3.up * carWidth * 0.18f);
            Vector3 focusDirection = target - viewer.transform.position;
            if (focusDirection.sqrMagnitude < 0.0001f)
                focusDirection = viewer.transform.forward;
            float comfortableDegrees = ResolveComfortableApertureDegrees(
                viewer.transform.position,
                focusDirection.normalized);
            float appliedDegrees = Mathf.Max(
                accidentApertureDegrees,
                comfortableDegrees);
            Vector3 apertureUp = viewer.transform.up;
            if (Mathf.Abs(Vector3.Dot(
                focusDirection.normalized,
                apertureUp)) > 0.98f)
            {
                apertureUp = viewer.transform.right;
            }
            accidentDarknessShell.transform.SetPositionAndRotation(
                viewer.transform.position,
                Quaternion.LookRotation(
                    focusDirection.normalized,
                    apertureUp));
            UpdateAccidentApertureMesh(appliedDegrees);
        }

        private float ResolveComfortableApertureDegrees(
            Vector3 viewerPosition,
            Vector3 focusDirection)
        {
            float maximumCenterAngle = 0f;
            float nearestDistance = float.PositiveInfinity;
            AccumulateAccidentVehicleAperture(
                victim,
                viewerPosition,
                focusDirection,
                ref maximumCenterAngle,
                ref nearestDistance);
            AccumulateAccidentVehicleAperture(
                other,
                viewerPosition,
                focusDirection,
                ref maximumCenterAngle,
                ref nearestDistance);
            if (float.IsInfinity(nearestDistance) ||
                float.IsNaN(nearestDistance))
                return CinematicFocusApertureImpactDegrees;

            float worldVehicleRadius = stage.TransformVector(
                forwardLocal * carLength * 0.42f).magnitude;
            float vehicleRadiusDegrees = Mathf.Atan2(
                worldVehicleRadius,
                Mathf.Max(0.05f, nearestDistance)) * Mathf.Rad2Deg;
            return Mathf.Clamp(
                2f * (maximumCenterAngle + vehicleRadiusDegrees + 4f),
                CinematicFocusApertureImpactDegrees,
                48f);
        }

        private static void AccumulateAccidentVehicleAperture(
            ReplayCarView car,
            Vector3 viewerPosition,
            Vector3 focusDirection,
            ref float maximumCenterAngle,
            ref float nearestDistance)
        {
            if (car == null)
                return;
            Vector3 toCar = car.VisualMotionRoot.position - viewerPosition;
            if (toCar.sqrMagnitude < 0.0001f)
                return;
            maximumCenterAngle = Mathf.Max(
                maximumCenterAngle,
                Vector3.Angle(focusDirection, toCar));
            nearestDistance = Mathf.Min(nearestDistance, toCar.magnitude);
        }

        private void UpdateAccidentApertureMesh(float fullDegrees)
        {
            if (accidentDarknessMesh == null ||
                accidentDarknessVertices == null)
            {
                return;
            }

            int rowLength = CinematicFocusApertureSegments + 1;
            float halfAngle = Mathf.Clamp(
                fullDegrees * 0.5f,
                1f,
                89f) * Mathf.Deg2Rad;
            float featherEnd = Mathf.Min(
                Mathf.PI - 0.01f,
                halfAngle +
                CinematicFocusApertureFeatherDegrees * Mathf.Deg2Rad);
            float radius = CinematicDarkShellDiameterMeters * 0.5f;
            for (int ring = 0;
                 ring <= CinematicFocusApertureRings;
                 ring++)
            {
                float polarAngle = ring switch
                {
                    0 => halfAngle,
                    1 => featherEnd,
                    _ => Mathf.Lerp(
                        featherEnd,
                        Mathf.PI - 0.01f,
                        (ring - 1f) /
                        (CinematicFocusApertureRings - 1f))
                };
                float sin = Mathf.Sin(polarAngle);
                float cos = Mathf.Cos(polarAngle);
                for (int segment = 0;
                     segment <= CinematicFocusApertureSegments;
                     segment++)
                {
                    float azimuth = segment /
                        (float)CinematicFocusApertureSegments *
                        Mathf.PI * 2f;
                    accidentDarknessVertices[ring * rowLength + segment] =
                        new Vector3(
                            sin * Mathf.Cos(azimuth),
                            sin * Mathf.Sin(azimuth),
                            cos) * radius;
                }
            }

            accidentDarknessMesh.vertices = accidentDarknessVertices;
            accidentDarknessMesh.bounds = new Bounds(
                Vector3.zero,
                Vector3.one * CinematicDarkShellDiameterMeters);
        }

        private void HideAccidentImpactAnnotations()
        {
            if (impactRoot == null)
                return;
            for (int i = 0; i < impactRoot.childCount; i++)
            {
                Transform child = impactRoot.GetChild(i);
                if (child != impactTransientRoot)
                    child.gameObject.SetActive(false);
            }
        }

        private static float ResolveAccidentImpactFlash(float age)
        {
            if (age < 0f || age > CinematicImpactFlashSeconds)
                return 0f;
            if (age <= 0.008f)
                return Mathf.InverseLerp(0f, 0.008f, age);
            if (age <= 0.045f)
                return 1f;
            return 1f - Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    0.045f,
                    CinematicImpactFlashSeconds,
                    age));
        }

        private static float ResolveAccidentImpactLightPulse(float age)
        {
            return ResolveAccidentImpactFlash(age);
        }

        private void SetAccidentImpactFlashAlpha(float alpha)
        {
            if (accidentImpactFlashMaterial == null)
                return;
            float clamped = Mathf.Clamp01(alpha);
            ReplayCarVisualUtil.SetMaterialColor(
                accidentImpactFlashMaterial,
                new Color(1.7f, 1.9f, 2.2f, clamped));
        }

        private void ResolveAccidentVisualContact()
        {
            accidentVisualContactResolved = false;
            accidentApproachStagingConfigured = false;
            accidentOriginalContactTime =
                forensicsAnalysis?.PresentationTime ?? anchorTime;
            accidentVisualContactTime = accidentOriginalContactTime;
            accidentVisualContactPoint = contactLocal;
            accidentVisualContactNormal = outwardLocal;
            if (forensicsAnalysis == null ||
                forensicsAnalysis.Tier == CollisionEvidenceTier.ContactUnresolved)
            {
                return;
            }

            Vector2 victimCenterOffset = Vector2.zero;
            Vector2 otherCenterOffset = Vector2.zero;
            if (victim == null ||
                !victim.TryGetCollisionFootprint(
                    out victimCenterOffset,
                    out accidentVictimFootprintLength,
                    out accidentVictimFootprintWidth))
            {
                accidentVictimFootprintLength = carLength;
                accidentVictimFootprintWidth = carWidth;
            }
            if (other == null ||
                !other.TryGetCollisionFootprint(
                    out otherCenterOffset,
                    out accidentOtherFootprintLength,
                    out accidentOtherFootprintWidth))
            {
                accidentOtherFootprintLength = carLength;
                accidentOtherFootprintWidth = carWidth;
            }

            accidentVictimFootprintLength *= CinematicFootprintScale;
            accidentVictimFootprintWidth *= CinematicFootprintScale;
            accidentOtherFootprintLength *= CinematicFootprintScale;
            accidentOtherFootprintWidth *= CinematicFootprintScale;
            if (!ConfigureAccidentApproachStaging(
                    victimCenterOffset,
                    otherCenterOffset))
            {
                Debug.LogWarning(
                    "[CollisionIncident] Accident approach staging was not " +
                    "resolved; keeping the observed presentation time.");
                return;
            }
            if (!TryFindFirstAccidentFootprintContact(
                    victimCenterOffset,
                    otherCenterOffset,
                    out AccidentVisualContact contact))
            {
                Debug.LogWarning(
                    "[CollisionIncident] Visual OBB contact was not " +
                    "resolved; keeping the observed presentation time.");
                return;
            }

            Vector3 previousContactLocal = contactLocal;
            accidentVisualContactResolved = true;
            accidentVisualContactTime = contact.Time;
            accidentVisualContactPoint = contact.Point;
            accidentVisualContactNormal = contact.Normal;
            accidentVictimContactPoint = contact.VictimPose.PathPoint;
            accidentVictimContactTangent = contact.VictimPose.Forward;
            accidentOtherContactPoint = contact.OtherPose.PathPoint;
            accidentOtherContactTangent = contact.OtherPose.Forward;
            contactLocal = contact.Point;
            ShiftAccidentImpactVisuals(
                contactLocal - previousContactLocal);
            ConfigureAccidentCollisionResponse(contact);
            ReorientAccidentImpactBurst(contact);

            float victimWorldLength = stage != null
                ? stage.TransformVector(
                    contact.VictimPose.Forward *
                    accidentVictimFootprintLength).magnitude
                : accidentVictimFootprintLength;
            float victimWorldWidth = stage != null
                ? stage.TransformVector(
                    contact.VictimPose.Right *
                    accidentVictimFootprintWidth).magnitude
                : accidentVictimFootprintWidth;
            float otherWorldLength = stage != null
                ? stage.TransformVector(
                    contact.OtherPose.Forward *
                    accidentOtherFootprintLength).magnitude
                : accidentOtherFootprintLength;
            float otherWorldWidth = stage != null
                ? stage.TransformVector(
                    contact.OtherPose.Right *
                    accidentOtherFootprintWidth).magnitude
                : accidentOtherFootprintWidth;
            AccidentFootprintMeasurement contactMeasurement =
                MeasureAccidentFootprints(
                    contact.VictimPose,
                    contact.OtherPose);
            float preContactTime = Mathf.Max(
                accidentApproachStagingStartTime,
                contact.Time - CinematicContactSearchStepSeconds);
            AccidentFootprintMeasurement preContactMeasurement = default;
            bool hasPreContactMeasurement =
                TryBuildAccidentFootprintPoses(
                    preContactTime,
                    victimCenterOffset,
                    otherCenterOffset,
                    true,
                    out AccidentFootprintPose preContactVictim,
                    out AccidentFootprintPose preContactOther);
            if (hasPreContactMeasurement)
            {
                preContactMeasurement = MeasureAccidentFootprints(
                    preContactVictim,
                    preContactOther);
            }
            Debug.Log(
                $"[CollisionIncident] visualContact=" +
                $"{accidentVisualContactTime:0.000000}, " +
                $"observedContact={accidentOriginalContactTime:0.000000}, " +
                $"delta=" +
                $"{accidentVisualContactTime - accidentOriginalContactTime:+0.000000;-0.000000;0.000000}s, " +
                $"point={accidentVisualContactPoint:F4}, " +
                $"normal={accidentVisualContactNormal:F4}, " +
                $"startToContactSource=" +
                $"{accidentVisualContactTime - accidentApproachStagingStartTime:0.000000}s, " +
                $"contactSeparation=" +
                $"{MeasurementSeparationWorldMeters(contactMeasurement):0.000000}m, " +
                $"contactOverlap=" +
                $"{MeasurementOverlapWorldMeters(contactMeasurement):0.000000}m, " +
                $"preContactSeparation=" +
                $"{(hasPreContactMeasurement ? MeasurementSeparationWorldMeters(preContactMeasurement) : -1f):0.000000}m, " +
                $"preContactOverlap=" +
                $"{(hasPreContactMeasurement ? MeasurementOverlapWorldMeters(preContactMeasurement) : -1f):0.000000}m, " +
                $"footprints=" +
                $"{victimWorldLength:0.000}x{victimWorldWidth:0.000}m/" +
                $"{otherWorldLength:0.000}x{otherWorldWidth:0.000}m.");
            LogAccidentFinalPoseAudit(
                victimCenterOffset,
                otherCenterOffset);
            LogAccidentMotionDiagnostics();
        }

        private void LogAccidentFinalPoseAudit(
            Vector2 victimCenterOffset,
            Vector2 otherCenterOffset)
        {
            float start = accidentApproachStagingStartTime;
            float contact = accidentVisualContactTime;
            LogAccidentFinalPoseSample(
                "START",
                start,
                victimCenterOffset,
                otherCenterOffset,
                "PRE_IMPACT");
            LogAccidentFinalPoseSample(
                "APPROACH_50",
                Mathf.Lerp(start, contact, 0.50f),
                victimCenterOffset,
                otherCenterOffset,
                "PRE_IMPACT");
            LogAccidentFinalPoseSample(
                "APPROACH_90",
                Mathf.Lerp(start, contact, 0.90f),
                victimCenterOffset,
                otherCenterOffset,
                "PRE_IMPACT");
            LogAccidentFinalPoseSample(
                "PRECONTACT_240HZ",
                Mathf.Max(start, contact - CinematicContactSearchStepSeconds),
                victimCenterOffset,
                otherCenterOffset,
                "PRE_IMPACT");
            LogAccidentFinalPoseSample(
                "FIRST_CONTACT",
                contact,
                victimCenterOffset,
                otherCenterOffset,
                "CONTACT_CLAMP");
            LogAccidentFinalPoseSample(
                "THEORETICAL_SOURCE_AFTER",
                contact + CinematicContactSearchStepSeconds,
                victimCenterOffset,
                otherCenterOffset,
                "CLAMPED_POST_IMPACT_CONTROL");

        }

        private void LogAccidentMotionDiagnostics()
        {
            LogAccidentMotionDiagnostics(
                forensicsVictimPath,
                true,
                "BLUE");
            LogAccidentMotionDiagnostics(
                forensicsOtherPath,
                false,
                "RED");
        }

        private void LogAccidentMotionDiagnostics(
            ForensicPath path,
            bool isVictim,
            string color)
        {
            if (path == null)
                return;

            float start = accidentApproachStagingStartTime;
            float contact = accidentVisualContactTime;
            float step = CinematicContactSearchStepSeconds;
            float end = contact - step;
            if (end <= start)
                return;

            float minimumSourceDerivative = float.PositiveInfinity;
            float minimumWorldSpeed = float.PositiveInfinity;
            float minimumForwardVelocity = float.PositiveInfinity;
            float minimumForwardAlignment = float.PositiveInfinity;
            float detailedStart = Mathf.Max(start, contact - 2f);
            int lastDetailBucket = -1;
            for (float time = start; time <= end; time += step)
            {
                float nextTime = Mathf.Min(end, time + step);
                float deltaTime = nextTime - time;
                if (deltaTime <= 0.000001f ||
                    !TryGetFinalPresentedVehiclePose(
                        path,
                        isVictim,
                        time,
                        out Vector3 point,
                        out Vector3 tangent) ||
                    !TryGetFinalPresentedVehiclePose(
                        path,
                        isVictim,
                        nextTime,
                        out Vector3 nextPoint,
                        out _))
                {
                    continue;
                }

                float effectiveSourceTime = Mathf.Clamp(
                    time,
                    path.Source.VisibleStartTime,
                    path.Source.VisibleEndTime);
                float nextEffectiveSourceTime = Mathf.Clamp(
                    nextTime,
                    path.Source.VisibleStartTime,
                    path.Source.VisibleEndTime);
                float sourceDerivative =
                    (nextEffectiveSourceTime - effectiveSourceTime) /
                    deltaTime;
                Vector3 localVelocity =
                    (nextPoint - point) / deltaTime;
                Vector3 worldVelocity = stage != null
                    ? stage.TransformVector(localVelocity)
                    : localVelocity;
                Vector3 worldTangent = stage != null
                    ? stage.TransformDirection(tangent)
                    : tangent;
                worldTangent = FlattenNormalized(
                    worldTangent,
                    Vector3.forward);
                float worldSpeed = worldVelocity.magnitude;
                float forwardVelocity = Vector3.Dot(
                    worldVelocity,
                    worldTangent);
                float forwardAlignment = worldSpeed > 0.000001f
                    ? Vector3.Dot(
                        worldTangent,
                        worldVelocity / worldSpeed)
                    : 0f;

                minimumSourceDerivative = Mathf.Min(
                    minimumSourceDerivative,
                    sourceDerivative);
                minimumWorldSpeed = Mathf.Min(
                    minimumWorldSpeed,
                    worldSpeed);
                minimumForwardVelocity = Mathf.Min(
                    minimumForwardVelocity,
                    forwardVelocity);
                minimumForwardAlignment = Mathf.Min(
                    minimumForwardAlignment,
                    forwardAlignment);

                if (time < detailedStart)
                    continue;
                int detailBucket = Mathf.FloorToInt(
                    (time - detailedStart) / 0.25f);
                if (detailBucket == lastDetailBucket)
                    continue;
                lastDetailBucket = detailBucket;
                Debug.Log(
                    $"[CollisionMotion] sample color={color}, " +
                    $"driver={path.DriverNumber}, " +
                    $"presentation={time:0.000000}, " +
                    $"rawSource={time:0.000000}, " +
                    "phaseOffset=0.000000, " +
                    $"effectiveSource={effectiveSourceTime:0.000000}, " +
                    $"deltaEffective={nextEffectiveSourceTime - effectiveSourceTime:0.000000}, " +
                    $"position={point:F4}, tangent={tangent:F4}, " +
                    $"worldVelocity={worldVelocity:F4}.");
            }

            Debug.Log(
                $"[CollisionMotion] summary color={color}, " +
                $"driver={path.DriverNumber}, " +
                "sourceTimeMapping=SYNCHRONIZED_1_TO_1, " +
                $"minSourceDerivative={minimumSourceDerivative:0.000000}, " +
                $"minWorldSpeed={minimumWorldSpeed:0.000000}m/s, " +
                $"minVelocityTangent={minimumForwardVelocity:0.000000}m/s, " +
                $"minForwardVelocityAlignment={minimumForwardAlignment:0.000000}.");
        }

        private void LogAccidentTrackHeadingAudit()
        {
            float start = accidentApproachStagingStartTime;
            float contact = accidentVisualContactTime;
            float[] checkpoints = { 0.10f, 0.35f, 0.60f, 0.80f, 0.95f };
            for (int index = 0; index < checkpoints.Length; index++)
            {
                float progress = checkpoints[index];
                float time = Mathf.Lerp(start, contact, progress);
                if (!TryGetFinalPresentedVehiclePose(
                        forensicsVictimPath,
                        true,
                        time,
                        out Vector3 victimPoint,
                        out Vector3 victimTangent) ||
                    !TryGetFinalPresentedVehiclePose(
                        forensicsOtherPath,
                        false,
                        time,
                        out Vector3 otherPoint,
                        out Vector3 otherTangent))
                {
                    continue;
                }
                Debug.Log(
                    $"[CollisionIncident] trackHeading " +
                    $"progress={progress:0.00}, " +
                    $"trackSource=" +
                    $"{(forensicsUsesActualTrack ? "ACTUAL_SUZUKA" : "PROXY_FALLBACK")}, " +
                    $"A={victimPoint:F4}/{victimTangent:F4}, " +
                    $"B={otherPoint:F4}/{otherTangent:F4}, " +
                    "renderAndObbPose=TryGetFinalPresentedVehiclePose.");
            }
        }

        private void LogAccidentFinalPoseSample(
            string label,
            float time,
            Vector2 victimCenterOffset,
            Vector2 otherCenterOffset,
            string control)
        {
            if (!TryBuildAccidentFootprintPoses(
                    time,
                    victimCenterOffset,
                    otherCenterOffset,
                    true,
                    out AccidentFootprintPose victimPose,
                    out AccidentFootprintPose otherPose))
            {
                Debug.LogWarning(
                    $"[CollisionIncident] poseAudit {label} unresolved.");
                return;
            }

            AccidentFootprintMeasurement measurement =
                MeasureAccidentFootprints(victimPose, otherPose);
            Debug.Log(
                $"[CollisionIncident] poseAudit {label} " +
                $"time={time:0.000000}, " +
                $"separation={MeasurementSeparationWorldMeters(measurement):0.000000}m, " +
                $"overlap={MeasurementOverlapWorldMeters(measurement):0.000000}m, " +
                $"overlapping={measurement.Overlapping}, " +
                $"A={victimPose.PathPoint:F4}/{victimPose.Forward:F4}, " +
                $"B={otherPose.PathPoint:F4}/{otherPose.Forward:F4}, " +
                $"control={control}, " +
                "renderPoseSource=TryGetFinalPresentedVehiclePose, " +
                "obbPoseSource=TryGetFinalPresentedVehiclePose.");
        }

        private bool ConfigureAccidentApproachStaging(
            Vector2 victimCenterOffset,
            Vector2 otherCenterOffset)
        {
            accidentApproachStagingStartTime = Mathf.Clamp(
                forensicsAnalysis.VehicleRevealTime,
                forensicsVisibleStartTime,
                accidentOriginalContactTime - 0.02f);
            if (!TryBuildAccidentFootprintPoses(
                    accidentApproachStagingStartTime,
                    victimCenterOffset,
                    otherCenterOffset,
                    false,
                    out AccidentFootprintPose rawVictim,
                    out AccidentFootprintPose rawOther))
            {
                return false;
            }

            AccidentFootprintMeasurement rawStart =
                MeasureAccidentFootprints(rawVictim, rawOther);
            Vector3 sharedForward = FlattenNormalized(
                rawVictim.Forward + rawOther.Forward,
                forensicsCorridorForward);
            accidentVictimIsTrailing =
                Vector3.Dot(rawVictim.PathPoint, sharedForward) <
                Vector3.Dot(rawOther.PathPoint, sharedForward);
            accidentTrailingBackwardOffsetMeters = 0f;
            accidentVictimInitialPhaseSeconds = 0f;
            accidentOtherInitialPhaseSeconds = 0f;
            accidentRacingLineSqueezeMeters = 0f;
            accidentApproachStagingConfigured = true;

            bool targetReached = !rawStart.Overlapping &&
                MeasurementSeparationWorldMeters(rawStart) >=
                CinematicInitialBodyGapMeters;
            if (!targetReached)
            {
                accidentTrailingBackwardOffsetMeters =
                    CinematicMaximumTrailingOffsetMeters;
                if (!TryMeasureStagedAccidentStart(
                        victimCenterOffset,
                        otherCenterOffset,
                        out AccidentFootprintMeasurement maximumProbe))
                {
                    return false;
                }

                targetReached = !maximumProbe.Overlapping &&
                    MeasurementSeparationWorldMeters(maximumProbe) >=
                    CinematicInitialBodyGapMeters;
                if (targetReached)
                {
                    float low = 0f;
                    float high = CinematicMaximumTrailingOffsetMeters;
                    for (int iteration = 0; iteration < 18; iteration++)
                    {
                        accidentTrailingBackwardOffsetMeters =
                            (low + high) * 0.5f;
                        if (!TryMeasureStagedAccidentStart(
                                victimCenterOffset,
                                otherCenterOffset,
                                out AccidentFootprintMeasurement probe))
                        {
                            return false;
                        }

                        if (!probe.Overlapping &&
                            MeasurementSeparationWorldMeters(probe) >=
                            CinematicInitialBodyGapMeters)
                        {
                            high = accidentTrailingBackwardOffsetMeters;
                        }
                        else
                        {
                            low = accidentTrailingBackwardOffsetMeters;
                        }
                    }
                    accidentTrailingBackwardOffsetMeters = high;
                }
            }

            if (!TryMeasureStagedAccidentStart(
                    victimCenterOffset,
                    otherCenterOffset,
                    out AccidentFootprintMeasurement stagedStart))
            {
                return false;
            }
            float midpointTime = Mathf.Lerp(
                accidentApproachStagingStartTime,
                accidentOriginalContactTime,
                0.5f);
            if (!TryBuildAccidentFootprintPoses(
                    midpointTime,
                    victimCenterOffset,
                    otherCenterOffset,
                    true,
                    out AccidentFootprintPose midpointVictim,
                    out AccidentFootprintPose midpointOther))
            {
                return false;
            }
            AccidentFootprintMeasurement stagedMidpoint =
                MeasureAccidentFootprints(midpointVictim, midpointOther);

            float victimWorldLength = MeasurementAxisWorldMeters(
                rawVictim.Forward,
                accidentVictimFootprintLength);
            float victimWorldWidth = MeasurementAxisWorldMeters(
                rawVictim.Right,
                accidentVictimFootprintWidth);
            float otherWorldLength = MeasurementAxisWorldMeters(
                rawOther.Forward,
                accidentOtherFootprintLength);
            float otherWorldWidth = MeasurementAxisWorldMeters(
                rawOther.Right,
                accidentOtherFootprintWidth);
            Debug.Log(
                $"[CollisionIncident] initialFootprints old " +
                $"time={accidentApproachStagingStartTime:0.000000}, " +
                $"A={victimWorldLength:0.000}x{victimWorldWidth:0.000}m, " +
                $"B={otherWorldLength:0.000}x{otherWorldWidth:0.000}m, " +
                $"centerDistance={MeasurementCenterWorldMeters(rawStart):0.000}m, " +
                $"separation={MeasurementSeparationWorldMeters(rawStart):0.000}m, " +
                $"overlap={MeasurementOverlapWorldMeters(rawStart):0.000}m, " +
                $"overlapping={rawStart.Overlapping}.");
            Debug.Log(
                $"[CollisionIncident] monotonicStaging " +
                "sourceTimeMapping=SYNCHRONIZED_1_TO_1, " +
                $"trailing={(accidentVictimIsTrailing ? "BLUE" : "RED")}, " +
                $"backwardOffset={accidentTrailingBackwardOffsetMeters:0.000}m, " +
                $"targetGap={CinematicInitialBodyGapMeters:0.000}m, " +
                $"targetReached={targetReached}.");
            Debug.Log(
                $"[CollisionIncident] initialFootprints staged " +
                $"centerDistance={MeasurementCenterWorldMeters(stagedStart):0.000}m, " +
                $"separation={MeasurementSeparationWorldMeters(stagedStart):0.000}m, " +
                $"overlap={MeasurementOverlapWorldMeters(stagedStart):0.000}m, " +
                $"overlapping={stagedStart.Overlapping}.");
            Debug.Log(
                $"[CollisionIncident] midpointFootprints staged " +
                $"time={midpointTime:0.000000}, " +
                $"separation={MeasurementSeparationWorldMeters(stagedMidpoint):0.000}m, " +
                $"overlap={MeasurementOverlapWorldMeters(stagedMidpoint):0.000}m, " +
                $"overlapping={stagedMidpoint.Overlapping}.");
            return !stagedStart.Overlapping;
        }

        private bool ConfigureAccidentApproachStagingLegacy(
            Vector2 victimCenterOffset,
            Vector2 otherCenterOffset)
        {
            accidentApproachStagingStartTime = Mathf.Clamp(
                forensicsAnalysis.VehicleRevealTime,
                forensicsVisibleStartTime,
                accidentOriginalContactTime - 0.02f);
            if (!TryBuildAccidentFootprintPoses(
                    accidentApproachStagingStartTime,
                    victimCenterOffset,
                    otherCenterOffset,
                    false,
                    out AccidentFootprintPose oldStartVictim,
                    out AccidentFootprintPose oldStartOther))
            {
                return false;
            }

            AccidentFootprintMeasurement oldStart =
                MeasureAccidentFootprints(
                    oldStartVictim,
                    oldStartOther);
            accidentVictimInitialPhaseSeconds = 0f;
            accidentOtherInitialPhaseSeconds = 0f;
            accidentRacingLineSqueezeMeters = 0f;
            accidentVictimSeparationSign =
                Vector3.Dot(
                    oldStartOther.Center - oldStartVictim.Center,
                    forensicsCorridorRight) >= 0f
                    ? -1f
                    : 1f;
            accidentApproachStagingConfigured = true;

            const int phaseSearchSteps = 40;
            float selectedTotalPhase = 0f;
            bool selectedVictimEarlier = true;
            float selectedScore = float.NegativeInfinity;
            float selectedLow = 0f;
            bool reachedTarget = false;
            for (int step = 1; step <= phaseSearchSteps; step++)
            {
                float totalPhase = CinematicMaximumPhaseOffsetSeconds *
                    step / phaseSearchSteps;
                for (int direction = 0; direction < 2; direction++)
                {
                    bool victimEarlier = direction == 0;
                    SetAccidentPhaseOffsets(totalPhase, victimEarlier);
                    if (!TryMeasureStagedAccidentStart(
                            victimCenterOffset,
                            otherCenterOffset,
                            out AccidentFootprintMeasurement probe))
                    {
                        return false;
                    }
                    float score = probe.Overlapping
                        ? -MeasurementOverlapWorldMeters(probe)
                        : MeasurementSeparationWorldMeters(probe);
                    if (score > selectedScore)
                    {
                        selectedScore = score;
                        selectedTotalPhase = totalPhase;
                        selectedVictimEarlier = victimEarlier;
                        selectedLow = Mathf.Max(
                            0f,
                            totalPhase -
                            CinematicMaximumPhaseOffsetSeconds /
                            phaseSearchSteps);
                    }
                    if (!probe.Overlapping &&
                        MeasurementSeparationWorldMeters(probe) >=
                        CinematicInitialBodyGapMeters)
                    {
                        selectedTotalPhase = totalPhase;
                        selectedVictimEarlier = victimEarlier;
                        selectedLow = Mathf.Max(
                            0f,
                            totalPhase -
                            CinematicMaximumPhaseOffsetSeconds /
                            phaseSearchSteps);
                        reachedTarget = true;
                        break;
                    }
                }
                if (reachedTarget)
                    break;
            }
            if (reachedTarget)
            {
                float low = selectedLow;
                float high = selectedTotalPhase;
                for (int iteration = 0; iteration < 18; iteration++)
                {
                    float midpoint = (low + high) * 0.5f;
                    SetAccidentPhaseOffsets(
                        midpoint,
                        selectedVictimEarlier);
                    if (!TryMeasureStagedAccidentStart(
                            victimCenterOffset,
                            otherCenterOffset,
                            out AccidentFootprintMeasurement probe))
                    {
                        return false;
                    }
                    if (!probe.Overlapping &&
                        MeasurementSeparationWorldMeters(probe) >=
                        CinematicInitialBodyGapMeters)
                    {
                        high = midpoint;
                    }
                    else
                    {
                        low = midpoint;
                    }
                }
                selectedTotalPhase = high;
            }
            SetAccidentPhaseOffsets(
                selectedTotalPhase,
                selectedVictimEarlier);
            if (!TryMeasureStagedAccidentStart(
                    victimCenterOffset,
                    otherCenterOffset,
                    out AccidentFootprintMeasurement phaseStagedStart))
            {
                return false;
            }
            if (phaseStagedStart.Overlapping ||
                MeasurementSeparationWorldMeters(phaseStagedStart) <
                CinematicInitialBodyGapMeters)
            {
                const float maximumSeparationMeters = 1.2f;
                const int separationSearchSteps = 48;
                float low = 0f;
                float high = maximumSeparationMeters;
                bool separationResolved = false;
                for (int step = 1;
                     step <= separationSearchSteps;
                     step++)
                {
                    accidentRacingLineSqueezeMeters =
                        maximumSeparationMeters * step /
                        separationSearchSteps;
                    if (!TryMeasureStagedAccidentStart(
                            victimCenterOffset,
                            otherCenterOffset,
                            out AccidentFootprintMeasurement probe))
                    {
                        return false;
                    }
                    if (!probe.Overlapping &&
                        MeasurementSeparationWorldMeters(probe) >=
                        CinematicInitialBodyGapMeters)
                    {
                        low = maximumSeparationMeters * (step - 1) /
                            separationSearchSteps;
                        high = accidentRacingLineSqueezeMeters;
                        separationResolved = true;
                        break;
                    }
                }
                if (!separationResolved)
                    return false;
                for (int iteration = 0; iteration < 16; iteration++)
                {
                    float midpoint = (low + high) * 0.5f;
                    accidentRacingLineSqueezeMeters = midpoint;
                    if (!TryMeasureStagedAccidentStart(
                            victimCenterOffset,
                            otherCenterOffset,
                            out AccidentFootprintMeasurement probe))
                    {
                        return false;
                    }
                    if (!probe.Overlapping &&
                        MeasurementSeparationWorldMeters(probe) >=
                        CinematicInitialBodyGapMeters)
                    {
                        high = midpoint;
                    }
                    else
                    {
                        low = midpoint;
                    }
                }
                accidentRacingLineSqueezeMeters = high;
                reachedTarget = true;
            }
            if (!TryMeasureStagedAccidentStart(
                    victimCenterOffset,
                    otherCenterOffset,
                    out AccidentFootprintMeasurement stagedStart))
            {
                return false;
            }
            float midpointTime = Mathf.Lerp(
                accidentApproachStagingStartTime,
                accidentOriginalContactTime,
                0.5f);
            if (!TryBuildAccidentFootprintPoses(
                    midpointTime,
                    victimCenterOffset,
                    otherCenterOffset,
                    true,
                    out AccidentFootprintPose midpointVictim,
                    out AccidentFootprintPose midpointOther))
            {
                return false;
            }
            AccidentFootprintMeasurement stagedMidpoint =
                MeasureAccidentFootprints(midpointVictim, midpointOther);

            float victimWorldLength = MeasurementAxisWorldMeters(
                oldStartVictim.Forward,
                accidentVictimFootprintLength);
            float victimWorldWidth = MeasurementAxisWorldMeters(
                oldStartVictim.Right,
                accidentVictimFootprintWidth);
            float otherWorldLength = MeasurementAxisWorldMeters(
                oldStartOther.Forward,
                accidentOtherFootprintLength);
            float otherWorldWidth = MeasurementAxisWorldMeters(
                oldStartOther.Right,
                accidentOtherFootprintWidth);
            Debug.Log(
                $"[CollisionIncident] initialFootprints old " +
                $"time={accidentApproachStagingStartTime:0.000000}, " +
                $"A={victimWorldLength:0.000}x{victimWorldWidth:0.000}m, " +
                $"B={otherWorldLength:0.000}x{otherWorldWidth:0.000}m, " +
                $"centerDistance={MeasurementCenterWorldMeters(oldStart):0.000}m, " +
                $"separation={MeasurementSeparationWorldMeters(oldStart):0.000}m, " +
                $"overlap={MeasurementOverlapWorldMeters(oldStart):0.000}m, " +
                $"overlapping={oldStart.Overlapping}.");
            Debug.Log(
                $"[CollisionIncident] approachStaging " +
                $"victimPhase={accidentVictimInitialPhaseSeconds:+0.000;-0.000;0.000}s, " +
                $"otherPhase={accidentOtherInitialPhaseSeconds:+0.000;-0.000;0.000}s, " +
                $"lineSqueeze={accidentRacingLineSqueezeMeters:0.000}m, " +
                $"targetGap={CinematicInitialBodyGapMeters:0.000}m, " +
                $"targetReached={reachedTarget}.");
            Debug.Log(
                $"[CollisionIncident] initialFootprints staged " +
                $"centerDistance={MeasurementCenterWorldMeters(stagedStart):0.000}m, " +
                $"separation={MeasurementSeparationWorldMeters(stagedStart):0.000}m, " +
                $"overlap={MeasurementOverlapWorldMeters(stagedStart):0.000}m, " +
                $"overlapping={stagedStart.Overlapping}.");
            Debug.Log(
                $"[CollisionIncident] midpointFootprints staged " +
                $"time={midpointTime:0.000000}, " +
                $"separation={MeasurementSeparationWorldMeters(stagedMidpoint):0.000}m, " +
                $"overlap={MeasurementOverlapWorldMeters(stagedMidpoint):0.000}m, " +
                $"overlapping={stagedMidpoint.Overlapping}.");
            return !stagedStart.Overlapping;
        }

        private void SetAccidentPhaseOffsets(
            float totalSeconds,
            bool victimEarlier)
        {
            float half = Mathf.Max(0f, totalSeconds) * 0.5f;
            accidentVictimInitialPhaseSeconds = victimEarlier
                ? -half
                : half;
            accidentOtherInitialPhaseSeconds = victimEarlier
                ? half
                : -half;
        }

        private bool TryMeasureStagedAccidentStart(
            Vector2 victimCenterOffset,
            Vector2 otherCenterOffset,
            out AccidentFootprintMeasurement measurement)
        {
            measurement = default;
            if (!TryBuildAccidentFootprintPoses(
                    accidentApproachStagingStartTime,
                    victimCenterOffset,
                    otherCenterOffset,
                    true,
                    out AccidentFootprintPose victimPose,
                    out AccidentFootprintPose otherPose))
            {
                return false;
            }
            measurement = MeasureAccidentFootprints(victimPose, otherPose);
            return true;
        }

        private bool TryGetFinalPresentedVehiclePose(
            ForensicPath path,
            bool isVictim,
            float time,
            out Vector3 point,
            out Vector3 tangent)
        {
            point = contactLocal;
            tangent = forensicsCorridorForward;
            if (path == null)
                return false;

            if (accidentVisualContactResolved &&
                time >= accidentVisualContactTime - 0.000001f)
            {
                point = isVictim
                    ? accidentVictimContactPoint
                    : accidentOtherContactPoint;
                tangent = isVictim
                    ? accidentVictimContactTangent
                    : accidentOtherContactTangent;
                return true;
            }

            if (!TryEvaluateAccidentStagedPosition(
                    path,
                    isVictim,
                    time,
                    out point,
                    out Vector3 fallbackTangent))
            {
                return false;
            }

            float sampleMinimum = accidentApproachStagingConfigured
                ? accidentApproachStagingStartTime
                : path.Source.VisibleStartTime;
            float sampleMaximum = Mathf.Min(
                accidentOriginalContactTime,
                path.Source.VisibleEndTime);
            float afterTime = Mathf.Min(
                sampleMaximum,
                time + CinematicHeadingSampleSeconds);
            if (afterTime > time + 0.000001f &&
                TryEvaluateAccidentStagedPosition(
                    path,
                    isVictim,
                    afterTime,
                    out Vector3 afterPoint,
                    out _))
            {
                tangent = FlattenNormalized(
                    afterPoint - point,
                    fallbackTangent);
            }
            else
            {
                float beforeTime = Mathf.Max(
                    sampleMinimum,
                    time - CinematicHeadingSampleSeconds);
                if (time > beforeTime + 0.000001f &&
                    TryEvaluateAccidentStagedPosition(
                        path,
                        isVictim,
                        beforeTime,
                        out Vector3 beforePoint,
                        out _))
                {
                    tangent = FlattenNormalized(
                        point - beforePoint,
                        fallbackTangent);
                }
                else
                {
                    tangent = fallbackTangent;
                }
            }
            return true;
        }

        private bool TryEvaluateAccidentStagedPosition(
            ForensicPath path,
            bool isVictim,
            float time,
            out Vector3 point,
            out Vector3 tangent)
        {
            point = contactLocal;
            tangent = forensicsCorridorForward;
            if (!accidentApproachStagingConfigured)
            {
                return TryEvaluateForensicPath(
                    path,
                    time,
                    out point,
                    out tangent);
            }
            float progress = Mathf.InverseLerp(
                accidentApproachStagingStartTime,
                accidentOriginalContactTime,
                time);
            if (!TryEvaluateForensicPath(
                path,
                time,
                out point,
                out tangent))
            {
                return false;
            }
            bool isTrailing = isVictim == accidentVictimIsTrailing;
            if (isTrailing &&
                accidentTrailingBackwardOffsetMeters > 0.0001f)
            {
                float remainingOffset = 1f - Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(
                        CinematicTrailingReleaseStartProgress,
                        1f,
                        progress));
                point -= ResolveStageLocalMeters(
                    tangent,
                    accidentTrailingBackwardOffsetMeters) *
                    remainingOffset;
            }
            return true;
        }

        private bool TryFindFirstAccidentFootprintContact(
            Vector2 victimCenterOffset,
            Vector2 otherCenterOffset,
            out AccidentVisualContact contact)
        {
            contact = default;
            float searchStart = accidentApproachStagingConfigured
                ? accidentApproachStagingStartTime
                : forensicsVisibleStartTime;
            float searchEnd = accidentOriginalContactTime;
            if (searchEnd <= searchStart + 0.0001f)
                return false;

            if (!TryBuildAccidentFootprintPoses(
                    searchStart,
                    victimCenterOffset,
                    otherCenterOffset,
                    true,
                    out AccidentFootprintPose startVictim,
                    out AccidentFootprintPose startOther))
            {
                return false;
            }
            if (TryEvaluateAccidentFootprintOverlap(
                    startVictim,
                    startOther,
                    out Vector3 startNormal,
                    out Vector3 startPoint))
            {
                contact = new AccidentVisualContact(
                    searchStart,
                    startPoint,
                    startNormal,
                    startVictim,
                    startOther);
                return true;
            }

            float separatedTime = searchStart;
            while (separatedTime < searchEnd - 0.00001f)
            {
                float probeTime = Mathf.Min(
                    searchEnd,
                    separatedTime + CinematicContactSearchStepSeconds);
                if (!TryBuildAccidentFootprintPoses(
                        probeTime,
                        victimCenterOffset,
                        otherCenterOffset,
                        true,
                        out AccidentFootprintPose victimPose,
                        out AccidentFootprintPose otherPose))
                {
                    return false;
                }

                if (TryEvaluateAccidentFootprintOverlap(
                        victimPose,
                        otherPose,
                        out _,
                        out _))
                {
                    float low = separatedTime;
                    float high = probeTime;
                    for (int iteration = 0;
                         iteration < CinematicContactRefinementIterations;
                         iteration++)
                    {
                        float midpoint = (low + high) * 0.5f;
                        if (!TryBuildAccidentFootprintPoses(
                                midpoint,
                                victimCenterOffset,
                                otherCenterOffset,
                                true,
                                out AccidentFootprintPose midVictim,
                                out AccidentFootprintPose midOther))
                        {
                            return false;
                        }
                        if (TryEvaluateAccidentFootprintOverlap(
                                midVictim,
                                midOther,
                                out _,
                                out _))
                        {
                            high = midpoint;
                        }
                        else
                        {
                            low = midpoint;
                        }
                    }

                    if (!TryBuildAccidentFootprintPoses(
                            high,
                            victimCenterOffset,
                            otherCenterOffset,
                            true,
                            out victimPose,
                            out otherPose) ||
                        !TryEvaluateAccidentFootprintOverlap(
                            victimPose,
                            otherPose,
                            out Vector3 contactNormal,
                            out Vector3 contactPoint))
                    {
                        return false;
                    }
                    contact = new AccidentVisualContact(
                        high,
                        contactPoint,
                        contactNormal,
                        victimPose,
                        otherPose);
                    return true;
                }

                if (probeTime >= searchEnd - 0.00001f)
                    break;
                separatedTime = probeTime;
            }
            return false;
        }

        private bool TryBuildAccidentFootprintPoses(
            float time,
            Vector2 victimCenterOffset,
            Vector2 otherCenterOffset,
            bool applyApproachStaging,
            out AccidentFootprintPose victimPose,
            out AccidentFootprintPose otherPose)
        {
            victimPose = default;
            otherPose = default;
            bool victimResolved = applyApproachStaging
                ? TryGetFinalPresentedVehiclePose(
                    forensicsVictimPath,
                    true,
                    time,
                    out Vector3 victimPoint,
                    out Vector3 victimTangent)
                : TryEvaluateForensicPath(
                    forensicsVictimPath,
                    time,
                    out victimPoint,
                    out victimTangent);
            bool otherResolved = applyApproachStaging
                ? TryGetFinalPresentedVehiclePose(
                    forensicsOtherPath,
                    false,
                    time,
                    out Vector3 otherPoint,
                    out Vector3 otherTangent)
                : TryEvaluateForensicPath(
                    forensicsOtherPath,
                    time,
                    out otherPoint,
                    out otherTangent);
            if (!victimResolved || !otherResolved)
            {
                return false;
            }

            victimPose = new AccidentFootprintPose(
                victimPoint,
                victimTangent,
                victimCenterOffset,
                accidentVictimFootprintLength * 0.5f,
                accidentVictimFootprintWidth * 0.5f);
            otherPose = new AccidentFootprintPose(
                otherPoint,
                otherTangent,
                otherCenterOffset,
                accidentOtherFootprintLength * 0.5f,
                accidentOtherFootprintWidth * 0.5f);
            return true;
        }

        private static bool TryEvaluateAccidentFootprintOverlap(
            AccidentFootprintPose victimPose,
            AccidentFootprintPose otherPose,
            out Vector3 contactNormal,
            out Vector3 contactPoint)
        {
            contactNormal = victimPose.Right;
            contactPoint = (victimPose.Center + otherPose.Center) * 0.5f;
            Vector3 centerDelta = otherPose.Center - victimPose.Center;
            float minimumOverlap = float.PositiveInfinity;
            if (!AccumulateAccidentFootprintAxis(
                    victimPose,
                    otherPose,
                    victimPose.Forward,
                    centerDelta,
                    ref minimumOverlap,
                    ref contactNormal) ||
                !AccumulateAccidentFootprintAxis(
                    victimPose,
                    otherPose,
                    victimPose.Right,
                    centerDelta,
                    ref minimumOverlap,
                    ref contactNormal) ||
                !AccumulateAccidentFootprintAxis(
                    victimPose,
                    otherPose,
                    otherPose.Forward,
                    centerDelta,
                    ref minimumOverlap,
                    ref contactNormal) ||
                !AccumulateAccidentFootprintAxis(
                    victimPose,
                    otherPose,
                    otherPose.Right,
                    centerDelta,
                    ref minimumOverlap,
                    ref contactNormal))
            {
                return false;
            }

            if (Vector3.Dot(contactNormal, centerDelta) < 0f)
                contactNormal = -contactNormal;
            float victimRadius = ProjectAccidentFootprintRadius(
                victimPose,
                contactNormal);
            float otherRadius = ProjectAccidentFootprintRadius(
                otherPose,
                contactNormal);
            Vector3 victimSurface =
                victimPose.Center + contactNormal * victimRadius;
            Vector3 otherSurface =
                otherPose.Center - contactNormal * otherRadius;
            contactPoint = (victimSurface + otherSurface) * 0.5f;
            contactPoint.y =
                (victimPose.PathPoint.y + otherPose.PathPoint.y) * 0.5f;
            return true;
        }

        private static bool AccumulateAccidentFootprintAxis(
            AccidentFootprintPose victimPose,
            AccidentFootprintPose otherPose,
            Vector3 axis,
            Vector3 centerDelta,
            ref float minimumOverlap,
            ref Vector3 minimumAxis)
        {
            Vector3 normalized = FlattenNormalized(axis, Vector3.right);
            float separation = Mathf.Abs(
                Vector3.Dot(centerDelta, normalized));
            float radius = ProjectAccidentFootprintRadius(
                victimPose,
                normalized) +
                ProjectAccidentFootprintRadius(otherPose, normalized);
            float overlap = radius - separation;
            if (overlap < -0.000001f)
                return false;
            if (overlap < minimumOverlap)
            {
                minimumOverlap = overlap;
                minimumAxis = normalized;
            }
            return true;
        }

        private static float ProjectAccidentFootprintRadius(
            AccidentFootprintPose pose,
            Vector3 axis)
        {
            return Mathf.Abs(Vector3.Dot(axis, pose.Forward)) *
                   pose.HalfLength +
                   Mathf.Abs(Vector3.Dot(axis, pose.Right)) *
                   pose.HalfWidth;
        }

        private static AccidentFootprintMeasurement
            MeasureAccidentFootprints(
                AccidentFootprintPose victimPose,
                AccidentFootprintPose otherPose)
        {
            Vector3 centerDelta = otherPose.Center - victimPose.Center;
            float minimumOverlap = float.PositiveInfinity;
            float maximumGap = float.NegativeInfinity;
            Vector3 minimumAxis = victimPose.Right;
            MeasureAccidentFootprintAxis(
                victimPose,
                otherPose,
                victimPose.Forward,
                ref minimumOverlap,
                ref maximumGap,
                ref minimumAxis);
            MeasureAccidentFootprintAxis(
                victimPose,
                otherPose,
                victimPose.Right,
                ref minimumOverlap,
                ref maximumGap,
                ref minimumAxis);
            MeasureAccidentFootprintAxis(
                victimPose,
                otherPose,
                otherPose.Forward,
                ref minimumOverlap,
                ref maximumGap,
                ref minimumAxis);
            MeasureAccidentFootprintAxis(
                victimPose,
                otherPose,
                otherPose.Right,
                ref minimumOverlap,
                ref maximumGap,
                ref minimumAxis);

            bool overlapping = maximumGap <= 0.000001f;
            if (overlapping)
            {
                Vector3 normal = minimumAxis;
                if (Vector3.Dot(normal, centerDelta) < 0f)
                    normal = -normal;
                float victimRadius = ProjectAccidentFootprintRadius(
                    victimPose,
                    normal);
                float otherRadius = ProjectAccidentFootprintRadius(
                    otherPose,
                    normal);
                Vector3 victimPoint =
                    victimPose.Center + normal * victimRadius;
                Vector3 otherPoint =
                    otherPose.Center - normal * otherRadius;
                return new AccidentFootprintMeasurement(
                    true,
                    0f,
                    Mathf.Max(0f, minimumOverlap),
                    centerDelta.magnitude,
                    normal,
                    victimPoint,
                    otherPoint);
            }

            ResolveClosestAccidentFootprintPoints(
                victimPose,
                otherPose,
                out Vector3 closestVictim,
                out Vector3 closestOther);
            Vector3 separationVector = closestOther - closestVictim;
            Vector3 separationNormal = FlattenNormalized(
                separationVector,
                centerDelta);
            return new AccidentFootprintMeasurement(
                false,
                separationVector.magnitude,
                0f,
                centerDelta.magnitude,
                separationNormal,
                closestVictim,
                closestOther);
        }

        private static void MeasureAccidentFootprintAxis(
            AccidentFootprintPose victimPose,
            AccidentFootprintPose otherPose,
            Vector3 axis,
            ref float minimumOverlap,
            ref float maximumGap,
            ref Vector3 minimumAxis)
        {
            Vector3 normalized = FlattenNormalized(axis, Vector3.right);
            float centerSeparation = Mathf.Abs(Vector3.Dot(
                otherPose.Center - victimPose.Center,
                normalized));
            float radii = ProjectAccidentFootprintRadius(
                    victimPose,
                    normalized) +
                ProjectAccidentFootprintRadius(otherPose, normalized);
            float gap = centerSeparation - radii;
            maximumGap = Mathf.Max(maximumGap, gap);
            float overlap = -gap;
            if (overlap < minimumOverlap)
            {
                minimumOverlap = overlap;
                minimumAxis = normalized;
            }
        }

        private static void ResolveClosestAccidentFootprintPoints(
            AccidentFootprintPose victimPose,
            AccidentFootprintPose otherPose,
            out Vector3 victimPoint,
            out Vector3 otherPoint)
        {
            Vector3[] victimCorners = AccidentFootprintCorners(victimPose);
            Vector3[] otherCorners = AccidentFootprintCorners(otherPose);
            victimPoint = victimCorners[0];
            otherPoint = otherCorners[0];
            float minimumDistanceSq = float.PositiveInfinity;
            for (int i = 0; i < 4; i++)
            {
                int next = (i + 1) & 3;
                for (int j = 0; j < 4; j++)
                {
                    Vector3 onOther = ClosestAccidentSegmentPoint(
                        victimCorners[i],
                        otherCorners[j],
                        otherCorners[(j + 1) & 3]);
                    float victimDistanceSq =
                        (onOther - victimCorners[i]).sqrMagnitude;
                    if (victimDistanceSq < minimumDistanceSq)
                    {
                        minimumDistanceSq = victimDistanceSq;
                        victimPoint = victimCorners[i];
                        otherPoint = onOther;
                    }

                    Vector3 onVictim = ClosestAccidentSegmentPoint(
                        otherCorners[j],
                        victimCorners[i],
                        victimCorners[next]);
                    float otherDistanceSq =
                        (otherCorners[j] - onVictim).sqrMagnitude;
                    if (otherDistanceSq < minimumDistanceSq)
                    {
                        minimumDistanceSq = otherDistanceSq;
                        victimPoint = onVictim;
                        otherPoint = otherCorners[j];
                    }
                }
            }
        }

        private static Vector3[] AccidentFootprintCorners(
            AccidentFootprintPose pose)
        {
            Vector3 forward = pose.Forward * pose.HalfLength;
            Vector3 right = pose.Right * pose.HalfWidth;
            return new[]
            {
                pose.Center + forward + right,
                pose.Center + forward - right,
                pose.Center - forward - right,
                pose.Center - forward + right
            };
        }

        private static Vector3 ClosestAccidentSegmentPoint(
            Vector3 point,
            Vector3 start,
            Vector3 end)
        {
            Vector3 segment = end - start;
            float lengthSq = segment.sqrMagnitude;
            if (lengthSq <= 0.0000001f)
                return start;
            float progress = Mathf.Clamp01(
                Vector3.Dot(point - start, segment) / lengthSq);
            return start + segment * progress;
        }

        private float MeasurementAxisWorldMeters(
            Vector3 localAxis,
            float localDistance)
        {
            Vector3 localVector = FlattenNormalized(
                localAxis,
                Vector3.right) * localDistance;
            return stage != null
                ? stage.TransformVector(localVector).magnitude
                : localVector.magnitude;
        }

        private float MeasurementSeparationWorldMeters(
            AccidentFootprintMeasurement measurement)
        {
            if (measurement.Overlapping)
                return 0f;
            Vector3 localVector =
                measurement.OtherPoint - measurement.VictimPoint;
            return stage != null
                ? stage.TransformVector(localVector).magnitude
                : localVector.magnitude;
        }

        private float MeasurementOverlapWorldMeters(
            AccidentFootprintMeasurement measurement)
        {
            return MeasurementAxisWorldMeters(
                measurement.Normal,
                measurement.OverlapDepth);
        }

        private float MeasurementCenterWorldMeters(
            AccidentFootprintMeasurement measurement)
        {
            return MeasurementAxisWorldMeters(
                measurement.Normal,
                measurement.CenterDistance);
        }

        private void ConfigureAccidentCollisionResponse(
            AccidentVisualContact contact)
        {
            float victimSideSeverity = Mathf.Abs(Vector3.Dot(
                contact.VictimPose.Right,
                contact.Normal));
            float otherSideSeverity = Mathf.Abs(Vector3.Dot(
                contact.OtherPose.Right,
                contact.Normal));
            bool victimReceivesStrongResponse =
                victimSideSeverity >= otherSideSeverity;
            float victimLateral = victimReceivesStrongResponse
                ? CinematicStrongLateralKickMeters
                : CinematicSecondaryLateralKickMeters;
            float victimCarry = victimReceivesStrongResponse
                ? CinematicStrongForwardCarryMeters
                : CinematicSecondaryForwardCarryMeters;
            float victimYaw = victimReceivesStrongResponse
                ? CinematicStrongYawDegrees
                : CinematicSecondaryYawDegrees;
            float otherLateral = victimReceivesStrongResponse
                ? CinematicSecondaryLateralKickMeters
                : CinematicStrongLateralKickMeters;
            float otherCarry = victimReceivesStrongResponse
                ? CinematicSecondaryForwardCarryMeters
                : CinematicStrongForwardCarryMeters;
            float otherYaw = victimReceivesStrongResponse
                ? CinematicSecondaryYawDegrees
                : CinematicStrongYawDegrees;

            Vector3 victimSeparation = -contact.Normal;
            Vector3 otherSeparation = contact.Normal;
            accidentVictimResponseOffset =
                ResolveStageLocalMeters(victimSeparation, victimLateral) +
                ResolveStageLocalMeters(
                    contact.VictimPose.Forward,
                    victimCarry);
            accidentOtherResponseOffset =
                ResolveStageLocalMeters(otherSeparation, otherLateral) +
                ResolveStageLocalMeters(
                    contact.OtherPose.Forward,
                    otherCarry);
            accidentVictimResponseYaw = ResolveAccidentResponseYaw(
                contact.VictimPose.Forward,
                victimSeparation,
                victimYaw,
                victimYawSign);
            accidentOtherResponseYaw = ResolveAccidentResponseYaw(
                contact.OtherPose.Forward,
                otherSeparation,
                otherYaw,
                -victimYawSign);
            Vector3 victimWorldOffset = stage != null
                ? stage.TransformVector(accidentVictimResponseOffset)
                : accidentVictimResponseOffset;
            Vector3 otherWorldOffset = stage != null
                ? stage.TransformVector(accidentOtherResponseOffset)
                : accidentOtherResponseOffset;
            Debug.Log(
                $"[CollisionIncident] response victim=" +
                $"{victimWorldOffset:F3}m/" +
                $"{accidentVictimResponseYaw:+0.0;-0.0;0.0}deg, " +
                $"forward={victimCarry:0.00}m, " +
                $"lateral={victimLateral:0.00}m; " +
                $"other={otherWorldOffset:F3}m/" +
                $"{accidentOtherResponseYaw:+0.0;-0.0;0.0}deg, " +
                $"forward={otherCarry:0.00}m, " +
                $"lateral={otherLateral:0.00}m; " +
                $"strong=" +
                $"{(victimReceivesStrongResponse ? "victim" : "other")}, " +
                $"duration={CinematicPostMotionSeconds:0.00}s.");
        }

        private Vector3 ResolveStageLocalMeters(
            Vector3 localDirection,
            float meters)
        {
            Vector3 direction = FlattenNormalized(
                localDirection,
                Vector3.forward);
            if (stage == null)
                return direction * meters;
            float worldMetersPerLocalUnit = Mathf.Max(
                0.0001f,
                stage.TransformVector(direction).magnitude);
            return direction * (meters / worldMetersPerLocalUnit);
        }

        private static float ResolveAccidentResponseYaw(
            Vector3 forward,
            Vector3 separation,
            float magnitude,
            float fallbackSign)
        {
            float angle = Vector3.SignedAngle(
                forward,
                separation,
                Vector3.up);
            float sign = Mathf.Abs(angle) > 1f
                ? Mathf.Sign(angle)
                : Mathf.Sign(fallbackSign);
            return sign * magnitude;
        }

        private void ApplyAccidentCollisionResponse()
        {
            float progress = ResolveAccidentResponseProgress(
                accidentImpactAge);
            Vector3 victimTangent = Quaternion.AngleAxis(
                accidentVictimResponseYaw * progress,
                Vector3.up) * accidentVictimResponseOriginTangent;
            Vector3 otherTangent = Quaternion.AngleAxis(
                accidentOtherResponseYaw * progress,
                Vector3.up) * accidentOtherResponseOriginTangent;
            ApplyForensicCarPose(
                victim,
                accidentVictimResponseOriginPoint +
                accidentVictimResponseOffset * progress,
                victimTangent);
            ApplyForensicCarPose(
                other,
                accidentOtherResponseOriginPoint +
                accidentOtherResponseOffset * progress,
                otherTangent);
        }

        private static float ResolveAccidentResponseProgress(float age)
        {
            float progress = Mathf.Clamp01(
                Mathf.Max(0f, age - CinematicContactLoadingSeconds) /
                CinematicPostMotionSeconds);
            return Mathf.SmoothStep(0f, 1f, progress);
        }

        private void ShiftAccidentImpactVisuals(Vector3 delta)
        {
            if (delta.sqrMagnitude <= 0.0000001f)
                return;
            if (impactTransientRoot != null)
            {
                for (int i = 0;
                     i < impactTransientRoot.childCount;
                     i++)
                {
                    impactTransientRoot.GetChild(i).localPosition += delta;
                }
            }
            if (impactRoot != null)
            {
                for (int i = 0; i < impactRoot.childCount; i++)
                {
                    Transform child = impactRoot.GetChild(i);
                    if (child != impactTransientRoot)
                        child.localPosition += delta;
                }
            }
        }

        private void ReorientAccidentImpactBurst(
            AccidentVisualContact contact)
        {
            float probeTime = Mathf.Max(
                forensicsVisibleStartTime,
                contact.Time - 0.04f);
            Vector3 relativeDirection =
                contact.VictimPose.Forward - contact.OtherPose.Forward;
            if (TryEvaluateForensicPath(
                    forensicsVictimPath,
                    probeTime,
                    out Vector3 previousVictimPoint,
                    out _) &&
                TryEvaluateForensicPath(
                    forensicsOtherPath,
                    probeTime,
                    out Vector3 previousOtherPoint,
                    out _))
            {
                float duration = Mathf.Max(0.001f, contact.Time - probeTime);
                Vector3 victimVelocity =
                    (contact.VictimPose.PathPoint - previousVictimPoint) /
                    duration;
                Vector3 otherVelocity =
                    (contact.OtherPose.PathPoint - previousOtherPoint) /
                    duration;
                relativeDirection = victimVelocity - otherVelocity;
            }
            relativeDirection = FlattenNormalized(
                relativeDirection,
                contact.VictimPose.Forward);
            impactDirectionLocal = relativeDirection;
            Vector3 contactTangent = FlattenNormalized(
                Vector3.Cross(Vector3.up, contact.Normal),
                relativeDirection);

            int sparkCount = Mathf.Min(
                impactSparkLines.Count,
                impactSparkEnds.Count);
            for (int i = 0; i < sparkCount; i++)
            {
                float side = (i & 1) == 0 ? 1f : -1f;
                float spread = (i - (sparkCount - 1) * 0.5f) /
                    Mathf.Max(1f, sparkCount - 1f);
                Vector3 direction =
                    contact.Normal * side * 0.8f +
                    relativeDirection * 0.48f +
                    contactTangent * spread * 0.55f +
                    Vector3.up * (0.08f + (i % 4) * 0.07f);
                direction.Normalize();
                Vector3 end = direction * carWidth *
                    Mathf.Lerp(0.3f, 0.78f, (i % 6) / 5f);
                impactSparkEnds[i] = end;
                if (impactSparkLines[i] != null)
                    impactSparkLines[i].SetPosition(1, end);
            }

            int debrisCount = Mathf.Min(
                impactDebrisPositions.Count,
                impactDebris.Count);
            for (int i = 0; i < debrisCount; i++)
            {
                float side = (i & 1) == 0 ? 1f : -1f;
                float spread = (i - (debrisCount - 1) * 0.5f) /
                    Mathf.Max(1f, debrisCount - 1f);
                Vector3 direction = FlattenNormalized(
                    contact.Normal * side * 0.72f +
                    relativeDirection * 0.38f +
                    contactTangent * spread * 0.5f,
                    contact.Normal * side);
                float radius = carWidth *
                    Mathf.Lerp(0.22f, 0.9f, (i % 7) / 6f);
                impactDebrisPositions[i] = contactLocal +
                    direction * radius +
                    Vector3.up * carWidth *
                    (0.06f + (i % 4) * 0.08f);
            }
        }

        private void ApplyAccidentVehicleContrast()
        {
            victim?.ClearCollisionBodyTint();
            other?.ClearCollisionBodyTint();
            accidentWarmTintSlotCount = other != null
                ? other.ApplyCollisionBodyTint(CinematicWarmVehicleColor)
                : 0;
            string warmColorHex = ColorUtility.ToHtmlStringRGB(
                CinematicWarmVehicleColor);
            Debug.Log(
                $"[CollisionIncident] warm vehicle tint slots=" +
                $"{accidentWarmTintSlotCount}, " +
                $"color=#{warmColorHex}.");
        }

        private void ClearAccidentVehicleContrast()
        {
            victim?.ClearCollisionBodyTint();
            other?.ClearCollisionBodyTint();
            accidentWarmTintSlotCount = 0;
        }
    }
}
