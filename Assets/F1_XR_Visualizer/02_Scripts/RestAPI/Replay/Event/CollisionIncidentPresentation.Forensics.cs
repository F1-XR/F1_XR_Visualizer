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
        private const float ForensicsApproachEnd = 1.35f;
        private const float ForensicsHitStopEnd = 1.44f;
        private const float ForensicsObservedPostEnd = 1.99f;
        private const float ForensicsAnnotationEnd = 2.70f;
        private const float ForensicsRevealEnd = 4.70f;
        private const float ForensicsReplayApproachEnd = 0.90f;
        private const float ForensicsReplayHitStopEnd = 0.99f;
        private const float ForensicsReplayEnd = 1.54f;
        private const float ForensicsVehicleHoldPostSeconds = 0.35f;
        private const float ForensicsManualContactResetMeters = 0.085f;
        private const float ForensicsImpactVisibleSeconds = 1.20f;
        private const float ForensicsTrackRevealEnd = 0.25f;
        private const int ForensicsTrackSubmeshCount = 5;

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
                LineRenderer body,
                LineRenderer wheels,
                LineRenderer arrow,
                LineRenderer slice,
                LineRenderer cage)
            {
                Root = root;
                Time = time;
                DriverNumber = driverNumber;
                Body = body;
                Wheels = wheels;
                Arrow = arrow;
                Slice = slice;
                Cage = cage;
                BodyWidth = body != null ? body.widthMultiplier : 0f;
                WheelsWidth = wheels != null ? wheels.widthMultiplier : 0f;
                ArrowWidth = arrow != null ? arrow.widthMultiplier : 0f;
                SliceWidth = slice != null ? slice.widthMultiplier : 0f;
                CageWidth = cage != null ? cage.widthMultiplier : 0f;
            }

            public Transform Root { get; }
            public float Time { get; }
            public int DriverNumber { get; }
            public LineRenderer Body { get; }
            public LineRenderer Wheels { get; }
            public LineRenderer Arrow { get; }
            public LineRenderer Slice { get; }
            public LineRenderer Cage { get; }
            public float BodyWidth { get; }
            public float WheelsWidth { get; }
            public float ArrowWidth { get; }
            public float SliceWidth { get; }
            public float CageWidth { get; }
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
        private Material forensicsTrackWarningMaterial;
        private TextMeshPro forensicsLegend;
        private TextMeshPro forensicsLensHud;
        private readonly List<Material> forensicsMaterials = new();
        private readonly List<Mesh> forensicsMeshes = new();
        private readonly List<LineRenderer> forensicsTrackWarningLines = new();
        private readonly List<Vector3[]> forensicsTrackWarningPoints = new();
        private readonly List<ForensicMarker> forensicsMarkers = new();
        private readonly List<ForensicOutline> forensicsOutlines = new();
        private ForensicOutline forensicsVictimContactOutline;
        private ForensicOutline forensicsOtherContactOutline;
        private readonly List<Vector3> forensicsVictimTailPoints = new(32);
        private readonly List<Vector3> forensicsOtherTailPoints = new(32);
        private Mesh forensicsTrackMesh;
        private int[][] forensicsTrackTriangles;
        private float forensicsTrackOuterHalfWidth;
        private float forensicsLastTrackProgress = -1f;
        private Color forensicsTrackWarningColor;
        private bool forensicsTrackEnabled;
        private Vector3 forensicsCorridorForward = Vector3.forward;
        private Vector3 forensicsCorridorRight = Vector3.right;
        private float forensicsCorridorScale = 1f;
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

        public bool ShouldPlayTrajectoryForensicsEngineAudio =>
            forensicsConfigured &&
            forensicsAnalysis.Tier != CollisionEvidenceTier.ContactUnresolved &&
            !forensicsLensActive &&
            (forensicsImpactReplaying
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
            CollisionShowcaseVfxSettings settings)
        {
            ClearTrajectoryForensics();
            if (analysis == null || stage == null || presentationRoot == null)
                return;

            forensicsAnalysis = analysis;
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

            CreateForensicsVisuals(settings);
            forensicsConfigured = true;
            OrientForensicsReadableText();
            ResetTrajectoryForensicsPrepared();
        }

        public void ResetTrajectoryForensicsPrepared()
        {
            if (!forensicsConfigured)
                return;

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

            impactAudio?.Stop();
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
            SetForensicsContactOutlines(
                false,
                Vector3.zero,
                Vector3.zero);
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
            SetRootVisible(forensicsRoot, true);
            OrientForensicsReadableText();
            forensicsRevealRunning = true;
            revealRunning = true;
            Phase = CollisionPresentationPhase.IslandReveal;
            if (island != null)
            {
                island.gameObject.SetActive(true);
                island.localScale = Vector3.Scale(
                    islandBaseScale,
                    new Vector3(0.88f, 0.025f, 0.88f));
            }
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

            forensicsSavedLensNormalized = TimeLensNormalized;
            forensicsTimeLensGate?.SetAvailable(false);
            forensicsLensActive = false;
            forensicsImpactReplaying = true;
            forensicsRevealRunning = false;
            forensicsVehicleVisible = true;
            forensicsFinalApplied = false;
            forensicsWallTime = 0f;
            forensicsCurrentTime = forensicsAnalysis.VehicleRevealTime;
            impactReplaying = true;
            revealRunning = false;
            revealComplete = false;
            impactTriggered = false;
            finalTableauApplied = false;
            impactReplayTime = 0f;
            secondaryHapticTriggered = false;
            secondaryHapticCountdown = -1f;
            Phase = CollisionPresentationPhase.ImpactReplay;
            forensicsStatus = "REPLAYING OBSERVED IMPACT";

            impactAudio?.Stop();
            ResetVehicleMotion();
            ClearImpactSmoke();
            SetCarsVisible(true);
            SetRootVisible(impactRoot, false);
            SetRootVisible(warningRoot, false);
            SetImpactWarningWave(-1f);
            SetImpactBurst(-1f);
            SetForensicsTail(forensicsCurrentTime);
            UpdateForensicsOutlines(
                forensicsCurrentTime,
                false,
                false);
        }

        public float TickTrajectoryForensics(float delta)
        {
            if (!forensicsConfigured)
                return anchorTime;

            float safeDelta = Mathf.Max(0f, delta);
            TickSecondaryImpactHaptic(safeDelta);
            TickForensicsManualContactPulse(safeDelta);
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
                SetForensicsContactOutlines(
                    false,
                    Vector3.zero,
                    Vector3.zero);
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
                SetForensicsContactOutlines(
                    false,
                    Vector3.zero,
                    Vector3.zero);
                return;
            }

            Vector3 separation = otherPoint - victimPoint;
            separation.y = 0f;
            float distance = separation.magnitude;
            Vector3 separationDirection = distance > 0.0001f
                ? separation / distance
                : forensicsCorridorRight;
            bool contactFrame = Mathf.Abs(
                forensicsCurrentTime -
                forensicsAnalysis.PresentationTime) <= 0.03f;
            float requiredCorrection = contactFrame
                ? Mathf.Max(
                    0f,
                    carWidth * 0.85f - distance)
                : 0f;
            float maximumCorrection = carWidth * 0.6f;
            float appliedCorrection = Mathf.Min(
                requiredCorrection,
                maximumCorrection);
            Vector3 victimCorrection =
                -separationDirection * appliedCorrection * 0.5f;
            Vector3 otherCorrection =
                separationDirection * appliedCorrection * 0.5f;
            bool useContactOutlines = contactFrame &&
                requiredCorrection > maximumCorrection + 0.0001f;
            if (useContactOutlines)
            {
                Vector3 victimOutlineCorrection =
                    -separationDirection * requiredCorrection * 0.5f;
                Vector3 otherOutlineCorrection =
                    separationDirection * requiredCorrection * 0.5f;
                ResetVehicleMotion();
                SetCarsVisible(false);
                SetForensicsContactOutlines(
                    true,
                    victimOutlineCorrection,
                    otherOutlineCorrection);
                UpdateForensicsContactAnnotations(
                    victimPoint + victimOutlineCorrection,
                    otherPoint + otherOutlineCorrection);
                return;
            }

            SetForensicsContactOutlines(
                false,
                Vector3.zero,
                Vector3.zero);
            SetCarsVisible(true);
            ApplyForensicCarPose(
                victim,
                victimPoint + victimCorrection,
                victimTangent);
            ApplyForensicCarPose(
                other,
                otherPoint + otherCorrection,
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
            forensicsVictimContactOutline = default;
            forensicsOtherContactOutline = default;
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
            forensicsTrackWarningMaterial = null;
            forensicsTrackMesh = null;
            forensicsTrackTriangles = null;
            forensicsTrackOuterHalfWidth = 0f;
            forensicsLastTrackProgress = -1f;
            forensicsTrackEnabled = false;
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
                path.Points[i] = contactLocal +
                    forensicsCorridorForward * longitudinal *
                    forensicsCorridorScale +
                    forensicsCorridorRight * lateral *
                    forensicsCorridorScale;
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

        private void CreateForensicsVisuals(
            CollisionShowcaseVfxSettings settings)
        {
            forensicsRoot = CreateRoot(
                "CollisionTrajectoryForensics",
                presentationRoot);
            forensicsTrackRoot = CreateRoot("ForensicTrackSlice", forensicsRoot);
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
            forensicsVictimOutlineMaterial = CreateForensicsOpaqueMaterial(
                "Runtime_ForensicsDriverAOutline",
                new Color(0.94f, 0.98f, 1f, 1f));
            forensicsOtherOutlineMaterial = CreateForensicsOpaqueMaterial(
                "Runtime_ForensicsDriverBOutline",
                new Color(0.05f, 0.86f, 1f, 1f));
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

            float railWidth = Mathf.Max(0.0001f, carWidth * 0.035f);
            forensicsVictimPath.Rail = CreateLine(
                "DriverAObservedRail",
                forensicsRailRoot,
                forensicsVictimMaterial,
                railWidth,
                true);
            forensicsOtherPath.Rail = CreateLine(
                "DriverBObservedRail",
                forensicsRailRoot,
                forensicsOtherMaterial,
                railWidth,
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
                BuildForensicsTrack(settings);
            BuildStableForensicsRails();
            BuildForensicsMarkers();
            BuildForensicsOutlines();
            BuildForensicsContactOutlines();
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
                    carWidth * 1.45f,
                    forensicsTrackOuterHalfWidth + carWidth * 0.34f) +
                Vector3.up * carWidth * 0.48f;
            legendObject.transform.localRotation =
                Quaternion.LookRotation(Vector3.up, forensicsCorridorForward);
            legendObject.transform.localScale =
                Vector3.one * carLength * 0.06f;
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
                "TRACK CONTEXT | TIME-COMPRESSED\n" +
                "<color=#F1FAFF>OBSERVED</color> | " +
                "<color=#FF922E>CONTACT</color> | " + tier + "\n" +
                $"REPORTED {forensicsReportedTime} | {closest}";
            forensicsLegend.alignment = TextAlignmentOptions.Left;
            forensicsLegend.richText = true;
            forensicsLegend.fontSize = 5.2f;
            forensicsLegend.fontStyle = FontStyles.Bold;
            forensicsLegend.enableAutoSizing = false;
            forensicsLegend.color = new Color(0.82f, 0.9f, 0.98f, 0.94f);
            forensicsLegend.rectTransform.sizeDelta = new Vector2(23f, 5.5f);
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

        private void BuildForensicsTrack(
            CollisionShowcaseVfxSettings settings)
        {
            int count = Mathf.Min(
                forensicsVictimPath?.Points?.Length ?? 0,
                forensicsOtherPath?.Points?.Length ?? 0);
            if (count < 2 || forensicsTrackRoot == null)
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
            float roadHalfWidth = roadWidth * 0.5f;
            float kerbOuter = roadHalfWidth + kerbWidth;
            forensicsTrackOuterHalfWidth = kerbOuter + runoffWidth;
            float edgeWidth = Mathf.Min(
                roadHalfWidth * 0.12f,
                Mathf.Max(carWidth * 0.055f, roadWidth * 0.014f));
            float[] lateralOffsets =
            {
                -forensicsTrackOuterHalfWidth,
                -kerbOuter,
                -roadHalfWidth,
                -roadHalfWidth + edgeWidth,
                roadHalfWidth - edgeWidth,
                roadHalfWidth,
                kerbOuter,
                forensicsTrackOuterHalfWidth
            };

            Vector3[] centers = new Vector3[count];
            Vector3[] rights = new Vector3[count];
            float[] cumulative = new float[count];
            for (int i = 0; i < count; i++)
            {
                centers[i] =
                    (forensicsVictimPath.Points[i] +
                     forensicsOtherPath.Points[i]) * 0.5f;
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

            const int verticesPerRow = 8;
            Vector3[] vertices = new Vector3[count * verticesPerRow];
            Vector3[] normals = new Vector3[vertices.Length];
            Vector2[] uv = new Vector2[vertices.Length];
            float lift = carWidth * 0.012f;
            float uvLength = Mathf.Max(carLength, cumulative[count - 1]);
            for (int i = 0; i < count; i++)
            {
                for (int band = 0; band < verticesPerRow; band++)
                {
                    int index = i * verticesPerRow + band;
                    vertices[index] = centers[i] +
                        rights[i] * lateralOffsets[band] +
                        Vector3.up * lift;
                    normals[index] = Vector3.up;
                    uv[index] = new Vector2(
                        band / (verticesPerRow - 1f),
                        cumulative[i] / uvLength);
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
                    triangles[4], segment, 0, 1, verticesPerRow);
                AddForensicsTrackQuad(
                    triangles[4], segment, 6, 7, verticesPerRow);
                int kerbSubmesh = Mathf.FloorToInt(
                    (cumulative[segment] + cumulative[segment + 1]) *
                    0.5f / stripeLength) % 2 == 0
                        ? 1
                        : 2;
                AddForensicsTrackQuad(
                    triangles[kerbSubmesh], segment, 1, 2, verticesPerRow);
                AddForensicsTrackQuad(
                    triangles[kerbSubmesh], segment, 5, 6, verticesPerRow);
                AddForensicsTrackQuad(
                    triangles[3], segment, 2, 3, verticesPerRow);
                AddForensicsTrackQuad(
                    triangles[3], segment, 4, 5, verticesPerRow);
                AddForensicsTrackQuad(
                    triangles[0], segment, 3, 4, verticesPerRow);
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

            forensicsTrackAsphaltMaterial = CreateForensicsOpaqueMaterial(
                "Runtime_ForensicsTrackAsphalt",
                new Color(0.075f, 0.085f, 0.095f, 1f));
            forensicsTrackKerbRedMaterial = CreateForensicsOpaqueMaterial(
                "Runtime_ForensicsTrackKerbRed",
                new Color(0.72f, 0.035f, 0.045f, 1f));
            forensicsTrackKerbWhiteMaterial = CreateForensicsOpaqueMaterial(
                "Runtime_ForensicsTrackKerbWhite",
                new Color(0.88f, 0.91f, 0.92f, 1f));
            forensicsTrackEdgeMaterial = CreateForensicsOpaqueMaterial(
                "Runtime_ForensicsTrackEdge",
                new Color(0.96f, 0.97f, 0.94f, 1f));
            forensicsTrackRunoffMaterial = CreateForensicsOpaqueMaterial(
                "Runtime_ForensicsTrackRunoff",
                new Color(0.035f, 0.22f, 0.095f, 1f));
            GameObject trackObject = new(
                "SuzukaForensicTrackContext",
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
                forensicsTrackRunoffMaterial
            };
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;

            Vector3[] leftRoadEdge = new Vector3[count];
            Vector3[] rightRoadEdge = new Vector3[count];
            Vector3[] leftKerbEdge = new Vector3[count];
            Vector3[] rightKerbEdge = new Vector3[count];
            int closestContactIndex = 0;
            float closestContactDistance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                leftRoadEdge[i] = vertices[i * verticesPerRow + 2] +
                    Vector3.up * carWidth * 0.03f;
                rightRoadEdge[i] = vertices[i * verticesPerRow + 5] +
                    Vector3.up * carWidth * 0.03f;
                leftKerbEdge[i] = vertices[i * verticesPerRow + 1] +
                    Vector3.up * carWidth * 0.03f;
                rightKerbEdge[i] = vertices[i * verticesPerRow + 6] +
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
            SetForensicsTrackProgress(0f);
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
                forensicsVictimStableMaterial);
            BuildStableForensicsRail(
                forensicsOtherPath,
                "DriverBObservedRibbon",
                forensicsOtherStableMaterial);
            SetRootVisible(forensicsStableRailRoot, false);
        }

        private void BuildStableForensicsRail(
            ForensicPath path,
            string name,
            Material material)
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
                carWidth * 0.035f,
                0.0001f);
            Vector3 lift = Vector3.up * carWidth * 0.072f;
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
                forensicsTrackRoot == null ||
                forensicsTrackMesh == null ||
                forensicsTrackTriangles == null)
            {
                SetRootVisible(forensicsTrackRoot, false);
                return;
            }

            float clamped = Mathf.Clamp01(progress);
            SetRootVisible(forensicsTrackRoot, clamped > 0.0001f);
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

        private void BuildForensicsOutlines()
        {
            float[] offsets = { -0.90f, -0.55f, -0.25f };
            for (int i = 0; i < offsets.Length; i++)
            {
                float time = forensicsAnalysis.PresentationTime + offsets[i];
                ForensicOutline victimOutline = CreateForensicsOutline(
                    forensicsVictimDriver,
                    time,
                    $"DriverAOutline_{offsets[i]:0.00}",
                    forensicsVictimOutlineMaterial);
                if (victimOutline.Root != null)
                    forensicsOutlines.Add(victimOutline);
                ForensicOutline otherOutline = CreateForensicsOutline(
                    forensicsOtherDriver,
                    time,
                    $"DriverBOutline_{offsets[i]:0.00}",
                    forensicsOtherOutlineMaterial);
                if (otherOutline.Root != null)
                    forensicsOutlines.Add(otherOutline);
            }
        }

        private void BuildForensicsContactOutlines()
        {
            float contact = forensicsAnalysis.PresentationTime;
            forensicsVictimContactOutline = CreateForensicsOutline(
                forensicsVictimDriver,
                contact,
                "DriverAContactOutline",
                forensicsVictimOutlineMaterial);
            forensicsOtherContactOutline = CreateForensicsOutline(
                forensicsOtherDriver,
                contact,
                "DriverBContactOutline",
                forensicsOtherOutlineMaterial);
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

            Transform root = CreateRoot(name, forensicsOutlineRoot);
            Vector3 forward = FlattenNormalized(tangent, forensicsCorridorForward);
            Vector3 right = FlattenNormalized(
                Vector3.Cross(Vector3.up, forward),
                forensicsCorridorRight);
            float halfLength = carLength * 0.42f;
            float halfWidth = carWidth * 0.38f;
            Vector3 lift = ForensicsLineLift() * 1.25f;

            LineRenderer body = CreateLine(
                "BodyOutline",
                root,
                material,
                carWidth * 0.055f,
                true);
            body.loop = true;
            body.positionCount = 4;
            body.SetPosition(0, point + forward * halfLength - right * halfWidth + lift);
            body.SetPosition(1, point + forward * halfLength + right * halfWidth + lift);
            body.SetPosition(2, point - forward * halfLength + right * halfWidth + lift);
            body.SetPosition(3, point - forward * halfLength - right * halfWidth + lift);

            LineRenderer wheels = CreateLine(
                "WheelOutline",
                root,
                material,
                carWidth * 0.065f,
                true);
            wheels.positionCount = 4;
            wheels.SetPosition(0, point + forward * halfLength * 0.62f - right * halfWidth * 1.15f + lift);
            wheels.SetPosition(1, point - forward * halfLength * 0.62f - right * halfWidth * 1.15f + lift);
            wheels.SetPosition(2, point - forward * halfLength * 0.62f + right * halfWidth * 1.15f + lift);
            wheels.SetPosition(3, point + forward * halfLength * 0.62f + right * halfWidth * 1.15f + lift);

            LineRenderer arrow = CreateLine(
                "ForwardOutline",
                root,
                material,
                carWidth * 0.05f,
                true);
            Vector3 tip = point + forward * carLength * 0.72f + lift;
            arrow.positionCount = 4;
            arrow.SetPosition(0, point + forward * halfLength * 0.2f + lift);
            arrow.SetPosition(1, tip);
            arrow.SetPosition(2, tip - forward * carLength * 0.13f - right * carWidth * 0.12f);
            arrow.SetPosition(3, tip);

            // A floor footprint becomes edge-on when the corridor points away
            // from the viewer. The upright scan section keeps each timestamp
            // readable without cloning another high-detail car.
            LineRenderer slice = CreateLine(
                "VehicleScanSection",
                root,
                material,
                carWidth * 0.055f,
                true);
            slice.positionCount = 7;
            Vector3 sliceBase = point + lift;
            slice.SetPosition(0, sliceBase - right * halfWidth * 1.08f);
            slice.SetPosition(
                1,
                sliceBase - right * halfWidth * 0.82f +
                Vector3.up * carWidth * 0.2f);
            slice.SetPosition(
                2,
                sliceBase - right * halfWidth * 0.38f +
                Vector3.up * carWidth * 0.38f);
            slice.SetPosition(
                3,
                sliceBase + Vector3.up * carWidth * 0.44f);
            slice.SetPosition(
                4,
                sliceBase + right * halfWidth * 0.38f +
                Vector3.up * carWidth * 0.38f);
            slice.SetPosition(
                5,
                sliceBase + right * halfWidth * 0.82f +
                Vector3.up * carWidth * 0.2f);
            slice.SetPosition(6, sliceBase + right * halfWidth * 1.08f);

            LineRenderer cage = CreateLine(
                "VehicleWireCage",
                root,
                material,
                carWidth * 0.05f,
                true);
            cage.loop = true;
            cage.positionCount = 10;
            Vector3 cageRear = point - forward * carLength * 0.4f + lift;
            Vector3 cageCockpitRear =
                point - forward * carLength * 0.16f +
                Vector3.up * carWidth * 0.28f + lift;
            Vector3 cageCockpitTop =
                point + forward * carLength * 0.04f +
                Vector3.up * carWidth * 0.43f + lift;
            Vector3 cageNose =
                point + forward * carLength * 0.38f +
                Vector3.up * carWidth * 0.14f + lift;
            Vector3 cageTip =
                point + forward * carLength * 0.64f +
                Vector3.up * carWidth * 0.05f + lift;
            cage.SetPosition(0, cageRear - right * carWidth * 0.3f);
            cage.SetPosition(1, cageCockpitRear - right * carWidth * 0.3f);
            cage.SetPosition(2, cageCockpitTop - right * carWidth * 0.2f);
            cage.SetPosition(3, cageNose - right * carWidth * 0.16f);
            cage.SetPosition(4, cageTip - right * carWidth * 0.08f);
            cage.SetPosition(5, cageTip + right * carWidth * 0.08f);
            cage.SetPosition(6, cageNose + right * carWidth * 0.16f);
            cage.SetPosition(7, cageCockpitTop + right * carWidth * 0.2f);
            cage.SetPosition(8, cageCockpitRear + right * carWidth * 0.3f);
            cage.SetPosition(9, cageRear + right * carWidth * 0.3f);

            root.gameObject.SetActive(false);
            return new ForensicOutline(
                root,
                time,
                driverNumber,
                body,
                wheels,
                arrow,
                slice,
                cage);
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

            Vector3 lift = ForensicsLineLift();
            SetLineProgress(
                forensicsVictimPath?.Rail,
                forensicsVictimPath?.Points,
                clamped,
                lift);
            SetLineProgress(
                forensicsOtherPath?.Rail,
                forensicsOtherPath?.Points,
                clamped,
                lift);
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
                float widthMultiplier = highlighted ? 1.55f : 1f;
                SetForensicsOutlineWidth(
                    outline.Body,
                    outline.BodyWidth,
                    widthMultiplier);
                SetForensicsOutlineWidth(
                    outline.Wheels,
                    outline.WheelsWidth,
                    widthMultiplier);
                SetForensicsOutlineWidth(
                    outline.Arrow,
                    outline.ArrowWidth,
                    widthMultiplier);
                SetForensicsOutlineWidth(
                    outline.Slice,
                    outline.SliceWidth,
                    widthMultiplier);
                SetForensicsOutlineWidth(
                    outline.Cage,
                    outline.CageWidth,
                    widthMultiplier);
            }
        }

        private static void SetForensicsOutlineWidth(
            LineRenderer line,
            float baseWidth,
            float multiplier)
        {
            if (line != null)
            {
                line.widthMultiplier = Mathf.Max(
                    0.00001f,
                    baseWidth * Mathf.Max(0.1f, multiplier));
            }
        }

        private void SetForensicsContactOutlines(
            bool visible,
            Vector3 victimOffset,
            Vector3 otherOffset)
        {
            SetForensicsContactOutline(
                forensicsVictimContactOutline,
                visible,
                victimOffset);
            SetForensicsContactOutline(
                forensicsOtherContactOutline,
                visible,
                otherOffset);
        }

        private static void SetForensicsContactOutline(
            ForensicOutline outline,
            bool visible,
            Vector3 offset)
        {
            if (outline.Root == null)
                return;

            outline.Root.localPosition = visible
                ? offset
                : Vector3.zero;
            outline.Root.gameObject.SetActive(visible);
            float widthMultiplier = visible ? 1.35f : 1f;
            SetForensicsOutlineWidth(
                outline.Body,
                outline.BodyWidth,
                widthMultiplier);
            SetForensicsOutlineWidth(
                outline.Wheels,
                outline.WheelsWidth,
                widthMultiplier);
            SetForensicsOutlineWidth(
                outline.Arrow,
                outline.ArrowWidth,
                widthMultiplier);
            SetForensicsOutlineWidth(
                outline.Slice,
                outline.SliceWidth,
                widthMultiplier);
            SetForensicsOutlineWidth(
                outline.Cage,
                outline.CageWidth,
                widthMultiplier);
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
            Vector3 stageUp = stage.up;
            Vector3 currentForward = Vector3.ProjectOnPlane(
                car.VisualMotionRoot.forward,
                stageUp);
            Vector3 desiredForward = Vector3.ProjectOnPlane(
                stage.TransformDirection(desiredTangent),
                stageUp);
            float yaw = currentForward.sqrMagnitude > 0.000001f &&
                desiredForward.sqrMagnitude > 0.000001f
                    ? Vector3.SignedAngle(
                        currentForward,
                        desiredForward,
                        stageUp)
                    : 0f;
            car.ApplyVisualMotion(worldOffset, yaw);
        }

        private void UpdateForensicsContactAnnotations(
            Vector3 victimPoint,
            Vector3 otherPoint)
        {
            if (annotationRoot == null)
                return;

            Vector3 right = FlattenNormalized(
                Vector3.Cross(Vector3.up, forwardLocal),
                forensicsCorridorRight);
            UpdateForensicsContactAnnotation(
                victimLabel,
                victimLabelTether,
                victimPoint,
                right);
            UpdateForensicsContactAnnotation(
                otherLabel,
                otherLabelTether,
                otherPoint,
                -right);
        }

        private void UpdateForensicsContactAnnotation(
            TextMeshPro label,
            LineRenderer tether,
            Vector3 vehiclePoint,
            Vector3 side)
        {
            if (label == null || tether == null)
                return;

            Vector3 anchor = vehiclePoint +
                Vector3.up * carWidth * 0.12f;
            Vector3 labelPosition = anchor +
                side * carWidth * 0.95f +
                forwardLocal * carLength * 0.08f;
            label.transform.localPosition = labelPosition;
            tether.SetPosition(0, anchor);
            tether.SetPosition(1, labelPosition);
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
                return TryEvaluateForensicPath(
                    observedPath,
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
