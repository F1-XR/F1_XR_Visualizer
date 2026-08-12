using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace F1XR.RestAPI.Replay
{
    internal sealed partial class CollisionIncidentPresentation
    {
        private const float RevealDuration = 2.9f;
        private const float IslandRevealEnd = 0.22f;
        private const float LiveApproachStart = 0.2f;
        private const float LiveApproachLeadSeconds = 1.15f;
        private const float ImpactRevealTime = 1.02f;
        private const float PostImpactRevealEnd = 2.2f;
        private const float ImpactReplayDuration = 2.35f;
        private const float ImpactReplayContactTime = 0.82f;
        private const float TemporalEchoFadeSeconds = 0.22f;
        private const float ImpactTransientDuration = 1.2f;
        private const float VictimForwardInCarLengths = 1f;
        private const float VictimOutwardInCarWidths = 0.7f;
        private const float VictimYawDegrees = 28f;
        private const float OtherOutwardInCarWidths = 0.18f;
        private const float OtherYawDegrees = 5f;

        private static readonly List<InputDevice> HapticDevices = new(4);

        private enum ContactPattern
        {
            Side,
            Rear,
            Crossing
        }

        private readonly List<Material> materials = new();
        private readonly List<Mesh> meshes = new();
        private readonly List<Renderer> earlyGhostRenderers = new();
        private readonly List<Renderer> lateGhostRenderers = new();
        private readonly List<Renderer> postEvidenceRenderers = new();
        private readonly List<Renderer> warningRenderers = new();
        private readonly List<Renderer> pulseRenderers = new();
        private readonly List<Collider> disabledVehicleColliders = new();
        private readonly List<LineRenderer> impactSparkLines = new();
        private readonly List<Vector3> impactSparkEnds = new();
        private readonly List<Transform> impactDebris = new();
        private readonly List<Vector3> impactDebrisPositions = new();
        private readonly List<Vector3> impactDebrisScales = new();
        private readonly List<Quaternion> impactDebrisRotations = new();

        private Transform stage;
        private Transform island;
        private Transform presentationRoot;
        private Transform earlyGhostRoot;
        private Transform lateGhostRoot;
        private Transform postRoot;
        private Transform impactRoot;
        private Transform impactTransientRoot;
        private Transform impactFlashRoot;
        private Transform impactPulseRoot;
        private Transform warningWaveRoot;
        private Transform boundaryPulseRoot;
        private Transform warningRoot;
        private Transform scanRingRoot;
        private Transform annotationRoot;
        private TextMeshPro victimLabel;
        private TextMeshPro otherLabel;
        private LineRenderer victimLabelTether;
        private LineRenderer otherLabelTether;
        private LineRenderer victimIncomingLine;
        private LineRenderer otherIncomingLine;
        private LineRenderer postTrajectoryLine;
        private LineRenderer[] postSkidLines;
        private ReplayCarView victim;
        private ReplayCarView other;
        private AudioSource impactAudio;
        private AudioClip impactClip;
        private bool ownsImpactClip;
        private ParticleSystem smoke;
        private Texture2D smokeTexture;
        private Material earlyGhostMaterial;
        private Material lateGhostMaterial;
        private Material victimTrajectoryMaterial;
        private Material otherTrajectoryMaterial;
        private Material postMaterial;
        private Material warningMaterial;
        private Material impactMaterial;
        private Material pulseMaterial;
        private Material warningWaveMaterial;
        private Material boundaryPulseMaterial;
        private Vector3[] victimIncomingPoints;
        private Vector3[] otherIncomingPoints;
        private Vector3[] postImpactPoints;
        private Vector3[][] postSkidPoints;
        private Vector3 contactLocal;
        private Vector3 victimAnchorLocal;
        private Vector3 forwardLocal;
        private Vector3 outwardLocal;
        private Vector3 impactDirectionLocal;
        private Vector3 islandBaseScale = Vector3.one;
        private float anchorTime;
        private float carLength;
        private float carWidth;
        private float forensicHoldSeconds = 2f;
        private float impactHoldSeconds = 0.09f;
        private float primaryHapticAmplitude = 0.55f;
        private float primaryHapticDuration = 0.08f;
        private float secondaryHapticAmplitude = 0.2f;
        private float secondaryHapticDuration = 0.05f;
        private float secondaryHapticDelaySeconds = 0.09f;
        private float warningWaveIntensity = 1f;
        private float warningWaveDurationSeconds = 0.62f;
        private float secondaryHapticCountdown = -1f;
        private float revealTime;
        private float impactReplayTime;
        private float victimYawSign = 1f;
        private ContactPattern contactPattern;
        private bool revealRunning;
        private bool revealComplete;
        private bool impactReplaying;
        private bool impactTriggered;
        private bool secondaryHapticTriggered;
        private bool impactHapticsEnabled = true;
        private bool finalTableauApplied;

        public bool RevealComplete => revealComplete;
        public bool ImpactReplaying => impactReplaying;
        public bool ShouldPlayEngineAudio =>
            UsesTrajectoryForensics
                ? ShouldPlayTrajectoryForensicsEngineAudio
                : impactReplaying
                ? impactReplayTime < ImpactReplayContactTime
                : revealRunning &&
                  revealTime >= LiveApproachStart &&
                  revealTime < ImpactRevealTime;
        public CollisionPresentationPhase Phase { get; private set; } =
            CollisionPresentationPhase.Preparing;

        public void Build(
            Transform stageRoot,
            Transform islandRoot,
            LineRenderer islandBoundary,
            ReplayCarView victimCar,
            ReplayCarView otherCar,
            float collisionAnchorTime,
            Vector3 collisionContactLocal,
            Vector3 collisionForwardLocal,
            Vector3 collisionOutwardLocal,
            float vehicleLength,
            float vehicleWidth,
            IReadOnlyList<Vector3> victimIncoming,
            IReadOnlyList<Vector3> otherIncoming,
            string incidentLabel,
            string incidentMetadata,
            string victimDriverLabel,
            string otherDriverLabel,
            Color victimDriverColor,
            Color otherDriverColor,
            CollisionShowcaseVfxSettings settings)
        {
            stage = stageRoot;
            island = islandRoot;
            victim = victimCar;
            other = otherCar;
            anchorTime = collisionAnchorTime;
            contactLocal = collisionContactLocal;
            forwardLocal = FlattenNormalized(
                collisionForwardLocal,
                Vector3.forward);
            outwardLocal = FlattenNormalized(
                collisionOutwardLocal,
                Vector3.right);
            carLength = Mathf.Max(0.001f, vehicleLength);
            carWidth = Mathf.Max(0.001f, vehicleWidth);
            victimAnchorLocal = victim != null && stage != null
                ? stage.InverseTransformPoint(
                    victim.VisualMotionRoot.position)
                : contactLocal;
            forensicHoldSeconds = Mathf.Max(
                0f,
                settings != null
                    ? settings.forensicHoldSeconds
                    : 2f);
            impactHoldSeconds = Mathf.Clamp(
                settings != null
                    ? settings.impactHoldSeconds
                    : 0.09f,
                0f,
                0.2f);
            impactHapticsEnabled = settings == null ||
                settings.playImpactHaptics;
            primaryHapticAmplitude = Mathf.Clamp01(
                settings != null
                    ? settings.primaryHapticAmplitude
                    : 0.55f);
            primaryHapticDuration = Mathf.Max(
                0.01f,
                settings != null
                    ? settings.primaryHapticDuration
                    : 0.08f);
            secondaryHapticAmplitude = Mathf.Clamp01(
                settings != null
                    ? settings.secondaryHapticAmplitude
                    : 0.2f);
            secondaryHapticDuration = Mathf.Max(
                0.01f,
                settings != null
                    ? settings.secondaryHapticDuration
                    : 0.05f);
            secondaryHapticDelaySeconds = Mathf.Max(
                0f,
                settings != null
                    ? settings.secondaryHapticDelaySeconds
                    : 0.09f);
            warningWaveIntensity = Mathf.Clamp(
                settings != null
                    ? settings.warningWaveIntensity
                    : 1f,
                0f,
                2f);
            warningWaveDurationSeconds = Mathf.Max(
                0.1f,
                settings != null
                    ? settings.warningWaveDurationSeconds
                    : 0.62f);
            victimYawSign = ResolveYawSign();
            victimIncomingPoints = CopyPoints(victimIncoming);
            otherIncomingPoints = CopyPoints(otherIncoming);
            contactPattern = ResolveContactPattern(
                victimIncomingPoints,
                otherIncomingPoints,
                out impactDirectionLocal);

            presentationRoot = CreateRoot(
                "CollisionIncidentPresentation",
                stage);
            earlyGhostRoot = CreateRoot(
                "PreImpactGhost_Minus1_00",
                presentationRoot);
            lateGhostRoot = CreateRoot(
                "PreImpactGhost_Minus0_45",
                presentationRoot);
            postRoot = CreateRoot(
                "PostImpactForensics",
                presentationRoot);
            impactRoot = CreateRoot(
                "ImpactEvidence",
                presentationRoot);
            impactTransientRoot = CreateRoot(
                "ImpactTransient",
                impactRoot);
            warningRoot = CreateRoot(
                "IncidentWarning",
                presentationRoot);
            annotationRoot = CreateRoot(
                "IncidentAnnotations",
                warningRoot);

            earlyGhostMaterial = CreateTransparentMaterial(
                "Runtime_CollisionGhostEarly",
                new Color(0.82f, 0.92f, 1f, 0.18f));
            lateGhostMaterial = CreateTransparentMaterial(
                "Runtime_CollisionGhostLate",
                new Color(0.92f, 0.97f, 1f, 0.32f));
            victimTrajectoryMaterial = CreateTransparentMaterial(
                "Runtime_CollisionObservedVictim",
                new Color(0.9f, 0.97f, 1f, 0.62f));
            otherTrajectoryMaterial = CreateTransparentMaterial(
                "Runtime_CollisionObservedOther",
                new Color(0.3f, 0.72f, 1f, 0.62f));
            postMaterial = CreateTransparentMaterial(
                "Runtime_CollisionPostImpact",
                new Color(1f, 0.08f, 0.055f, 0.72f));
            warningMaterial = CreateTransparentMaterial(
                "Runtime_CollisionWarning",
                new Color(1.25f, 0.78f, 0.02f, 0.92f),
                true);
            impactMaterial = CreateTransparentMaterial(
                "Runtime_CollisionImpact",
                new Color(1.5f, 0.28f, 0.015f, 0.96f),
                true);
            pulseMaterial = CreateTransparentMaterial(
                "Runtime_CollisionImpactPulse",
                new Color(1.6f, 0.34f, 0.015f, 0.94f),
                true);
            warningWaveMaterial = CreateTransparentMaterial(
                "Runtime_CollisionWarningWave",
                new Color(1.45f, 0.82f, 0.02f, 0.9f),
                true);
            boundaryPulseMaterial = CreateTransparentMaterial(
                "Runtime_CollisionBoundaryPulse",
                new Color(1.6f, 0.9f, 0.02f, 0.9f),
                true);

            victimIncomingLine = CreateTrajectory(
                "VictimIncomingTrajectory",
                victimIncomingPoints,
                carWidth * 0.035f,
                victimTrajectoryMaterial,
                presentationRoot);
            otherIncomingLine = CreateTrajectory(
                "OtherIncomingTrajectory",
                otherIncomingPoints,
                carWidth * 0.025f,
                otherTrajectoryMaterial,
                presentationRoot);
            CreatePostImpactEvidence();
            CreateImpactEvidence(settings);
            CreateImpactWarningWave(islandBoundary);
            CreateWarningBoundary(
                incidentLabel,
                incidentMetadata);
            CreateDriverAnnotations(
                victimDriverLabel,
                otherDriverLabel,
                victimDriverColor,
                otherDriverColor);
            CreateImpactAudio(settings);
            DisableVehicleColliders(victim);
            DisableVehicleColliders(other);

            if (island != null)
                islandBaseScale = island.localScale;

            HidePrepared();
        }

        public void CapturePreImpactSnapshot(
            ReplayCarView victimCar,
            ReplayCarView otherCar,
            bool early)
        {
            if (UsesTrajectoryForensics)
                return;

            Transform target = early
                ? earlyGhostRoot
                : lateGhostRoot;
            Material material = early
                ? earlyGhostMaterial
                : lateGhostMaterial;
            List<Renderer> renderers = early
                ? earlyGhostRenderers
                : lateGhostRenderers;

            CaptureGhost(
                victimCar,
                early ? "Victim_Minus1_00" : "Victim_Minus0_45",
                target,
                material,
                renderers);
            CaptureGhost(
                otherCar,
                early ? "Other_Minus1_00" : "Other_Minus0_45",
                target,
                material,
                renderers);
        }

        public void HidePrepared()
        {
            if (UsesTrajectoryForensics)
            {
                ResetTrajectoryForensicsPrepared();
                return;
            }

            Phase = CollisionPresentationPhase.Preparing;
            revealRunning = false;
            revealComplete = false;
            impactReplaying = false;
            impactTriggered = false;
            secondaryHapticTriggered = false;
            secondaryHapticCountdown = -1f;
            finalTableauApplied = false;
            revealTime = 0f;
            impactReplayTime = 0f;
            impactAudio?.Stop();
            ResetVehicleMotion();
            SetCarsVisible(false);
            SetRootVisible(earlyGhostRoot, false);
            SetRootVisible(lateGhostRoot, false);
            SetRootVisible(postRoot, false);
            SetRootVisible(impactRoot, false);
            SetRootVisible(impactTransientRoot, false);
            SetRootVisible(warningRoot, false);
            SetRootVisible(impactPulseRoot, false);
            SetImpactWarningWave(-1f);
            SetImpactBurst(-1f);
            ClearImpactSmoke();
            SetIncomingTrajectoryProgress(0f, 0f);
            SetPostImpactProgress(0f);
            if (island != null)
            {
                island.gameObject.SetActive(false);
                island.localScale = islandBaseScale;
            }
        }

        public void BeginReveal()
        {
            if (UsesTrajectoryForensics)
            {
                BeginTrajectoryForensicsReveal();
                return;
            }

            Phase = CollisionPresentationPhase.IslandReveal;
            revealRunning = true;
            revealComplete = false;
            impactReplaying = false;
            impactTriggered = false;
            secondaryHapticTriggered = false;
            secondaryHapticCountdown = -1f;
            finalTableauApplied = false;
            revealTime = 0f;
            impactReplayTime = 0f;
            impactAudio?.Stop();
            ResetVehicleMotion();
            SetCarsVisible(false);
            SetRootVisible(earlyGhostRoot, false);
            SetRootVisible(lateGhostRoot, false);
            SetRootVisible(postRoot, false);
            SetRootVisible(impactRoot, false);
            SetRootVisible(impactTransientRoot, false);
            SetRootVisible(warningRoot, false);
            SetRootVisible(impactPulseRoot, false);
            SetImpactWarningWave(-1f);
            SetImpactBurst(-1f);
            ClearImpactSmoke();
            SetIncomingTrajectoryProgress(0f, 0f);
            SetPostImpactProgress(0f);
            if (island != null)
            {
                island.gameObject.SetActive(true);
                island.localScale = Vector3.Scale(
                    islandBaseScale,
                    new Vector3(0.88f, 0.025f, 0.88f));
            }
        }

        public void RestartReveal()
        {
            BeginReveal();
        }

        public void ReplayImpact()
        {
            if (UsesTrajectoryForensics)
            {
                ReplayTrajectoryForensicsImpact();
                return;
            }

            if (!revealComplete || impactReplaying)
                return;

            impactReplaying = true;
            Phase = CollisionPresentationPhase.ImpactReplay;
            revealRunning = false;
            impactReplayTime = 0f;
            impactTriggered = false;
            secondaryHapticTriggered = false;
            secondaryHapticCountdown = -1f;
            impactAudio?.Stop();
            ResetVehicleMotion();
            ClearImpactSmoke();
            SetCarsVisible(true);
            SetRootVisible(earlyGhostRoot, false);
            SetRootVisible(lateGhostRoot, false);
            SetRootVisible(postRoot, false);
            SetRootVisible(warningRoot, false);
            SetRootVisible(impactRoot, false);
            SetRootVisible(impactTransientRoot, false);
            SetRootVisible(impactPulseRoot, false);
            SetImpactWarningWave(-1f);
            SetImpactBurst(-1f);
            SetIncomingTrajectoryProgress(0f, 0f);
            SetObservedTrajectoryAlpha(0f);
            SetPostImpactProgress(0f);
        }

        public float Tick(float unscaledDeltaTime)
        {
            if (UsesTrajectoryForensics)
            {
                return TickTrajectoryForensics(
                    Mathf.Max(0f, unscaledDeltaTime));
            }

            float delta = Mathf.Max(0f, unscaledDeltaTime);
            TickSecondaryImpactHaptic(delta);
            if (impactReplaying)
                return TickImpactReplay(delta);

            if (!revealRunning)
                return anchorTime;

            revealTime = Mathf.Min(
                RevealDuration + forensicHoldSeconds,
                revealTime + delta);
            ApplyReveal(Mathf.Min(RevealDuration, revealTime));
            if (revealTime >= RevealDuration)
            {
                if (!finalTableauApplied)
                {
                    finalTableauApplied = true;
                    ApplyFinalTableau();
                }
                Phase = CollisionPresentationPhase.ForensicHold;
            }
            if (revealTime >= RevealDuration + forensicHoldSeconds)
            {
                revealRunning = false;
                revealComplete = true;
            }

            return ResolveRevealReplayTime();
        }

        public void ApplyVehicleMotion()
        {
            if (UsesTrajectoryForensics)
            {
                ApplyTrajectoryForensicsVehicleMotion();
                return;
            }

            if (revealRunning &&
                revealTime >= RevealImpactHoldEnd &&
                revealTime < RevealDuration)
            {
                float revealMotion = ResolveRevealPostProgress(
                    revealTime);
                ApplyPostImpactVehicleMotion(revealMotion);
                UpdateDriverAnnotations();
                return;
            }

            if (!impactReplaying)
            {
                if (finalTableauApplied ||
                    Phase == CollisionPresentationPhase.ForensicHold)
                {
                    ApplyPostImpactVehicleMotion(1f);
                }
                else
                {
                    ResetVehicleMotion();
                }
                UpdateDriverAnnotations();
                return;
            }

            if (impactReplayTime <= ImpactReplayContactTime)
            {
                ResetVehicleMotion();
                UpdateDriverAnnotations();
                return;
            }

            float progress = ResolveImpactReplayPostProgress(
                impactReplayTime);
            ApplyPostImpactVehicleMotion(progress);
            UpdateDriverAnnotations();
        }

        private void ApplyPostImpactVehicleMotion(float progress)
        {
            Vector3 victimLocalOffset = ResolveVictimOffset(progress);
            Vector3 otherLocalOffset = ResolveOtherOffset(progress);
            victim?.ApplyVisualMotion(
                stage.TransformVector(victimLocalOffset),
                victimYawSign * VictimYawDegrees * progress);
            other?.ApplyVisualMotion(
                stage.TransformVector(otherLocalOffset),
                -victimYawSign * OtherYawDegrees * progress);
        }

        private Vector3 ResolveVictimOffset(float progress)
        {
            float clamped = Mathf.Clamp01(progress);
            return
                forwardLocal * carLength *
                VictimForwardInCarLengths * clamped +
                outwardLocal * carWidth *
                VictimOutwardInCarWidths * clamped;
        }

        private Vector3 ResolveOtherOffset(float progress)
        {
            return -outwardLocal * carWidth *
                OtherOutwardInCarWidths * Mathf.Clamp01(progress);
        }

        private float RevealImpactHoldEnd =>
            ImpactRevealTime + impactHoldSeconds;

        private float ImpactReplayHoldEnd =>
            ImpactReplayContactTime + impactHoldSeconds;

        private float ResolveRevealPostProgress(float time)
        {
            return EaseOutCubic(Mathf.InverseLerp(
                RevealImpactHoldEnd,
                PostImpactRevealEnd,
                time));
        }

        private float ResolveImpactReplayPostProgress(float time)
        {
            return EaseOutCubic(Mathf.InverseLerp(
                ImpactReplayHoldEnd,
                ImpactReplayHoldEnd +
                (PostImpactRevealEnd - RevealImpactHoldEnd),
                time));
        }

        private static float EaseOutCubic(float value)
        {
            float inverse = 1f - Mathf.Clamp01(value);
            return 1f - inverse * inverse * inverse;
        }

        public void Clear()
        {
            ClearTrajectoryForensics();
            if (impactAudio != null)
                impactAudio.Stop();
            ResetVehicleMotion();
            if (presentationRoot != null)
                UnityEngine.Object.Destroy(
                    presentationRoot.gameObject);

            for (int i = 0; i < materials.Count; i++)
            {
                if (materials[i] != null)
                    UnityEngine.Object.Destroy(materials[i]);
            }
            for (int i = 0; i < meshes.Count; i++)
            {
                if (meshes[i] != null)
                    UnityEngine.Object.Destroy(meshes[i]);
            }
            if (ownsImpactClip && impactClip != null)
                UnityEngine.Object.Destroy(impactClip);
            if (smokeTexture != null)
                UnityEngine.Object.Destroy(smokeTexture);

            materials.Clear();
            meshes.Clear();
            earlyGhostRenderers.Clear();
            lateGhostRenderers.Clear();
            postEvidenceRenderers.Clear();
            warningRenderers.Clear();
            pulseRenderers.Clear();
            disabledVehicleColliders.Clear();
            impactSparkLines.Clear();
            impactSparkEnds.Clear();
            impactDebris.Clear();
            impactDebrisPositions.Clear();
            impactDebrisScales.Clear();
            impactDebrisRotations.Clear();
            presentationRoot = null;
            stage = null;
            island = null;
            victim = null;
            other = null;
            impactAudio = null;
            impactClip = null;
            ownsImpactClip = false;
            smoke = null;
            smokeTexture = null;
            victimIncomingLine = null;
            otherIncomingLine = null;
            postTrajectoryLine = null;
            postSkidLines = null;
            impactTransientRoot = null;
            warningWaveRoot = null;
            boundaryPulseRoot = null;
            victimIncomingPoints = null;
            otherIncomingPoints = null;
            postImpactPoints = null;
            postSkidPoints = null;
        }

        private float TickImpactReplay(float delta)
        {
            impactReplayTime = Mathf.Min(
                ImpactReplayDuration,
                impactReplayTime + delta);
            ApplyTemporalEchoes(
                impactReplayTime,
                0f,
                ImpactReplayContactTime,
                0f);
            if (!impactTriggered &&
                impactReplayTime >= ImpactReplayContactTime)
            {
                impactTriggered = true;
                SetRootVisible(impactRoot, true);
                TriggerImpactFeedback();
            }

            float flashAge = impactReplayTime -
                ImpactReplayContactTime;
            SetImpactTransient(flashAge);
            SetImpactFlash(flashAge >= 0f && flashAge <= 0.16f,
                flashAge);
            SetImpactPulse(flashAge);
            SetImpactWarningWave(flashAge);
            SetImpactBurst(flashAge);

            float postProgress = ResolveImpactReplayPostProgress(
                impactReplayTime);
            ApplyPostImpactEvidence(postProgress);

            if (impactReplayTime >= ImpactReplayDuration)
            {
                impactReplaying = false;
                impactReplayTime = 0f;
                Phase = CollisionPresentationPhase.ForensicHold;
                ApplyFinalTableau();
                return anchorTime;
            }

            if (impactReplayTime <= ImpactReplayContactTime)
            {
                return Mathf.Lerp(
                    anchorTime - LiveApproachLeadSeconds,
                    anchorTime,
                    Mathf.InverseLerp(
                        0f,
                        ImpactReplayContactTime,
                        impactReplayTime));
            }

            return anchorTime;
        }

        private float ResolveRevealReplayTime()
        {
            if (!revealRunning ||
                revealTime < LiveApproachStart ||
                revealTime >= ImpactRevealTime)
            {
                return anchorTime;
            }

            return Mathf.Lerp(
                anchorTime - LiveApproachLeadSeconds,
                anchorTime,
                Mathf.InverseLerp(
                    LiveApproachStart,
                    ImpactRevealTime,
                    revealTime));
        }

        private void ApplyReveal(float time)
        {
            Phase = time < IslandRevealEnd
                ? CollisionPresentationPhase.IslandReveal
                : time < ImpactRevealTime
                    ? CollisionPresentationPhase.PreImpact
                    : time < RevealImpactHoldEnd
                        ? CollisionPresentationPhase.Impact
                        : time < RevealDuration
                            ? CollisionPresentationPhase.PostImpact
                            : CollisionPresentationPhase.ForensicHold;
            float islandProgress = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0f, IslandRevealEnd, time));
            if (island != null)
            {
                island.gameObject.SetActive(true);
                island.localScale = Vector3.Scale(
                    islandBaseScale,
                    new Vector3(
                        Mathf.Lerp(0.88f, 1f, islandProgress),
                        Mathf.Lerp(0.025f, 1f, islandProgress),
                        Mathf.Lerp(0.88f, 1f, islandProgress)));
            }

            SetCarsVisible(time >= LiveApproachStart);

            float warningProgress = time >= PostImpactRevealEnd
                ? Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(
                        PostImpactRevealEnd,
                        RevealDuration,
                        time))
                : 0f;
            ApplyTemporalEchoes(
                time,
                LiveApproachStart,
                ImpactRevealTime,
                warningProgress);

            if (time >= ImpactRevealTime)
            {
                SetRootVisible(impactRoot, true);
                if (!impactTriggered)
                {
                    impactTriggered = true;
                    TriggerImpactFeedback();
                }
            }
            else
            {
                SetRootVisible(impactRoot, false);
            }
            SetImpactFlash(
                time >= ImpactRevealTime &&
                time <= ImpactRevealTime + 0.16f,
                time - ImpactRevealTime);
            float impactAge = time - ImpactRevealTime;
            SetImpactTransient(impactAge);
            SetImpactPulse(impactAge);
            SetImpactWarningWave(impactAge);
            SetImpactBurst(impactAge);

            float postProgress = ResolveRevealPostProgress(time);
            ApplyPostImpactEvidence(postProgress);

            bool showWarning = time >= PostImpactRevealEnd;
            SetRootVisible(warningRoot, showWarning);
            if (showWarning)
            {
                SetRendererAlpha(
                    warningRenderers,
                    warningProgress * 0.92f);
                if (scanRingRoot != null)
                {
                    float scale = Mathf.Lerp(
                        0.25f,
                        1f,
                        warningProgress);
                    scanRingRoot.localScale =
                        Vector3.one * scale;
                }
            }
        }

        private void ApplyTemporalEchoes(
            float time,
            float approachStart,
            float contactTime,
            float forensicProgress)
        {
            float earlyPassTime = Mathf.Lerp(
                approachStart,
                contactTime,
                (LiveApproachLeadSeconds - 1f) /
                LiveApproachLeadSeconds);
            float latePassTime = Mathf.Lerp(
                approachStart,
                contactTime,
                (LiveApproachLeadSeconds - 0.45f) /
                LiveApproachLeadSeconds);
            float approachProgress = Mathf.InverseLerp(
                earlyPassTime,
                contactTime,
                time);

            float earlyAlpha = ResolveTemporalEchoAlpha(
                time,
                earlyPassTime,
                0.16f);
            float lateAlpha = ResolveTemporalEchoAlpha(
                time,
                latePassTime,
                0.28f);
            SetRootVisible(earlyGhostRoot, earlyAlpha > 0.001f);
            SetRootVisible(lateGhostRoot, lateAlpha > 0.001f);
            SetRendererAlpha(earlyGhostRenderers, earlyAlpha);
            SetRendererAlpha(lateGhostRenderers, lateAlpha);

            SetIncomingTrajectoryProgress(
                approachProgress,
                Mathf.Clamp01(approachProgress - 0.025f));
            float pathAlpha = time < approachStart
                ? 0f
                : Mathf.Lerp(0.08f, 0.46f, approachProgress);
            if (time > contactTime)
            {
                pathAlpha = Mathf.Lerp(
                    0.46f,
                    0.24f,
                    Mathf.InverseLerp(
                        contactTime,
                        PostImpactRevealEnd,
                        time));
            }
            pathAlpha = Mathf.Max(
                pathAlpha,
                forensicProgress * 0.34f);
            SetObservedTrajectoryAlpha(pathAlpha);
        }

        private static float ResolveTemporalEchoAlpha(
            float time,
            float passTime,
            float peakAlpha)
        {
            if (time < passTime)
                return 0f;

            float age = time - passTime;
            if (age <= 0.035f)
            {
                return Mathf.SmoothStep(
                    0f,
                    peakAlpha,
                    age / 0.035f);
            }

            return peakAlpha *
                (1f - Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(
                        0.035f,
                        TemporalEchoFadeSeconds,
                        age)));
        }

        private void ApplyPostImpactEvidence(float progress)
        {
            float clamped = Mathf.Clamp01(progress);
            SetRootVisible(postRoot, clamped > 0.001f);
            SetRendererAlpha(
                postEvidenceRenderers,
                clamped * 0.78f);
            SetMaterialAlpha(postMaterial, clamped * 0.78f);
            SetPostImpactProgress(clamped);
        }

        private void ApplyFinalTableau()
        {
            ApplyPostImpactVehicleMotion(1f);
            if (island != null)
            {
                island.gameObject.SetActive(true);
                island.localScale = islandBaseScale;
            }
            SetCarsVisible(true);
            SetRootVisible(earlyGhostRoot, false);
            SetRootVisible(lateGhostRoot, false);
            SetRootVisible(postRoot, true);
            SetRootVisible(impactRoot, true);
            SetImpactTransient(float.MaxValue);
            SetRootVisible(impactFlashRoot, false);
            SetRootVisible(warningRoot, true);
            SetRendererAlpha(postEvidenceRenderers, 0.78f);
            SetRendererAlpha(warningRenderers, 0.92f);
            SetObservedTrajectoryAlpha(0.34f);
            SetMaterialAlpha(postMaterial, 0.78f);
            SetIncomingTrajectoryProgress(1f, 1f);
            SetPostImpactProgress(1f);
            SetImpactWarningWave(float.MaxValue);
            SetImpactBurst(-1f);
            SetRootVisible(impactPulseRoot, false);
            if (scanRingRoot != null)
                scanRingRoot.localScale = Vector3.one;
            UpdateDriverAnnotations();
        }

        private void CreatePostImpactEvidence()
        {
            Vector3 end = victimAnchorLocal +
                ResolveVictimOffset(1f);
            const int pointCount = 14;
            Vector3[] path = new Vector3[pointCount];
            for (int i = 0; i < pointCount; i++)
            {
                float progress = i / (pointCount - 1f);
                path[i] = Vector3.Lerp(
                    contactLocal,
                    end,
                    progress) +
                    outwardLocal *
                    Mathf.Sin(progress * Mathf.PI) *
                    carWidth * 0.08f +
                    Vector3.up * carWidth * 0.025f;
            }
            postImpactPoints = path;

            postTrajectoryLine = CreateLine(
                "VictimPostImpactTrajectory",
                postRoot,
                postMaterial,
                carWidth * 0.045f,
                true);
            postTrajectoryLine.positionCount = path.Length;
            postTrajectoryLine.SetPositions(path);
            postEvidenceRenderers.Add(postTrajectoryLine);

            Vector3 tireRight = Vector3.Cross(
                Vector3.up,
                forwardLocal).normalized;
            postSkidLines = new LineRenderer[2];
            postSkidPoints = new Vector3[2][];
            for (int lineIndex = 0; lineIndex < 2; lineIndex++)
            {
                float side = lineIndex == 0 ? -1f : 1f;
                LineRenderer skid = CreateLine(
                    lineIndex == 0
                        ? "ForensicSkidLeft"
                        : "ForensicSkidRight",
                    postRoot,
                    postMaterial,
                    carWidth * 0.028f,
                    false);
                skid.positionCount = path.Length;
                postSkidPoints[lineIndex] = new Vector3[path.Length];
                for (int i = 0; i < path.Length; i++)
                {
                    Vector3 point = path[i] +
                        tireRight * carWidth * 0.28f * side;
                    postSkidPoints[lineIndex][i] = point;
                    skid.SetPosition(i, point);
                }
                postSkidLines[lineIndex] = skid;
                postEvidenceRenderers.Add(skid);
            }
        }

        private void CreateImpactEvidence(
            CollisionShowcaseVfxSettings settings)
        {
            Transform sparksRoot = CreateRoot(
                "FrozenOrangeSparks_12",
                impactTransientRoot);
            sparksRoot.localPosition = contactLocal +
                Vector3.up * carWidth * 0.14f;
            for (int i = 0; i < 12; i++)
            {
                float angle = (i * 137.508f + 17f) *
                    Mathf.Deg2Rad;
                bool groundScrape = i < 5;
                float spread = (i - 5.5f) / 5.5f;
                float rise = groundScrape
                    ? 0.035f + (i % 3) * 0.025f
                    : 0.16f + (i % 4) * 0.08f;
                Vector3 direction =
                    impactDirectionLocal * 0.92f +
                    forwardLocal * spread * 0.62f +
                    outwardLocal * Mathf.Cos(angle) * 0.24f +
                    Vector3.up * rise;
                direction.Normalize();
                LineRenderer spark = CreateLine(
                    $"Spark_{i:00}",
                    sparksRoot,
                    impactMaterial,
                    carWidth * Mathf.Lerp(
                        0.02f,
                        0.04f,
                        (i % 5) / 4f),
                    false);
                spark.positionCount = 2;
                spark.SetPosition(0, Vector3.zero);
                Vector3 sparkEnd = direction * carWidth *
                    Mathf.Lerp(0.24f, 0.72f, (i % 6) / 5f);
                spark.SetPosition(1, sparkEnd);
                impactSparkLines.Add(spark);
                impactSparkEnds.Add(sparkEnd);
            }

            Mesh shardMesh = CreateShardMesh();
            Material shardMaterial = CreateOpaqueMaterial(
                "Runtime_CollisionOrangeDebris",
                new Color(1f, 0.22f, 0.025f, 1f));
            for (int i = 0; i < 12; i++)
            {
                GameObject shard = new(
                    $"FrozenDebris_{i:00}",
                    typeof(MeshFilter),
                    typeof(MeshRenderer));
                shard.transform.SetParent(impactTransientRoot, false);
                float angle = (i * 137.508f + 31f) *
                    Mathf.Deg2Rad;
                float radius = carWidth *
                    Mathf.Lerp(0.2f, 0.85f, (i % 7) / 6f);
                Vector3 finalPosition = contactLocal +
                    outwardLocal * Mathf.Cos(angle) * radius +
                    forwardLocal * Mathf.Sin(angle) * radius +
                    impactDirectionLocal * radius * 0.42f +
                    Vector3.up * carWidth *
                    (0.06f + (i % 4) * 0.08f);
                Quaternion finalRotation = Quaternion.Euler(
                    i * 29f,
                    i * 47f,
                    i * 19f);
                float size = carWidth * Mathf.Lerp(
                    0.03f,
                    0.07f,
                    (i % 6) / 5f);
                Vector3 finalScale = Vector3.one * size;
                shard.transform.localPosition = finalPosition;
                shard.transform.localRotation = finalRotation;
                shard.transform.localScale = finalScale;
                shard.GetComponent<MeshFilter>().sharedMesh = shardMesh;
                MeshRenderer renderer =
                    shard.GetComponent<MeshRenderer>();
                ConfigureRenderer(renderer, shardMaterial);
                impactDebris.Add(shard.transform);
                impactDebrisPositions.Add(finalPosition);
                impactDebrisScales.Add(finalScale);
                impactDebrisRotations.Add(finalRotation);
            }

            CreateFrozenSmoke(settings);
            CreateImpactFlash();
            CreateImpactPulse();
            CreateContactVector();
        }

        private void CreateFrozenSmoke(
            CollisionShowcaseVfxSettings settings)
        {
            GameObject smokeObject = new(
                "FrozenRadialSmoke",
                typeof(ParticleSystem));
            smokeObject.transform.SetParent(
                impactTransientRoot,
                false);
            smokeObject.transform.localPosition = contactLocal +
                forwardLocal * carLength * 0.55f +
                outwardLocal * carWidth * 0.4f +
                Vector3.up * carWidth * 0.1f;
            smoke = smokeObject.GetComponent<ParticleSystem>();
            smoke.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = smoke.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startLifetime = 2f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(
                carWidth * 0.18f,
                carWidth * 0.28f);
            main.startColor = new Color(
                0.52f,
                0.55f,
                0.6f,
                0.46f);
            main.simulationSpace =
                ParticleSystemSimulationSpace.Local;
            main.scalingMode =
                ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 12;

            ParticleSystem.EmissionModule emission = smoke.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = smoke.shape;
            shape.enabled = false;
            ParticleSystem.SizeOverLifetimeModule size =
                smoke.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(
                    new Keyframe(0f, 1f),
                    new Keyframe(1f, 1.96f)));

            smokeTexture = CreateRadialAlphaTexture();
            Material smokeMaterial = CreateParticleMaterial(
                smokeTexture,
                new Color(0.58f, 0.61f, 0.66f, 0.46f));
            ParticleSystemRenderer renderer =
                smokeObject.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode =
                ParticleSystemRenderMode.Billboard;
            renderer.alignment =
                ParticleSystemRenderSpace.View;
            renderer.sharedMaterial = smokeMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            ClearImpactSmoke();
        }

        private void CreateImpactFlash()
        {
            impactFlashRoot = CreateRoot(
                "ImpactFlash",
                impactTransientRoot);
            impactFlashRoot.localPosition = contactLocal +
                Vector3.up * carWidth * 0.18f;
            for (int axis = 0; axis < 3; axis++)
            {
                LineRenderer line = CreateLine(
                    $"ImpactAxis_{axis}",
                    impactFlashRoot,
                    impactMaterial,
                    carWidth * 0.035f,
                    false);
                line.positionCount = 2;
                Vector3 direction = axis switch
                {
                    0 => forwardLocal,
                    1 => outwardLocal,
                    _ => Vector3.up
                };
                line.SetPosition(0, -direction * carWidth * 0.38f);
                line.SetPosition(1, direction * carWidth * 0.38f);
            }
        }

        private void CreateImpactPulse()
        {
            impactPulseRoot = CreateRoot(
                "ImpactScanPulse",
                impactTransientRoot);
            impactPulseRoot.localPosition = contactLocal +
                Vector3.up * carWidth * 0.035f;
            for (int ringIndex = 0; ringIndex < 3; ringIndex++)
            {
                LineRenderer ring = CreateLine(
                    $"ImpactPulseRing_{ringIndex:00}",
                    impactPulseRoot,
                    pulseMaterial,
                    carWidth * Mathf.Lerp(0.018f, 0.032f,
                        ringIndex / 2f),
                    false);
                const int pointCount = 48;
                ring.loop = true;
                ring.positionCount = pointCount;
                float radius = carWidth *
                    (0.34f + ringIndex * 0.2f);
                for (int i = 0; i < pointCount; i++)
                {
                    float angle = i / (float)pointCount *
                        Mathf.PI * 2f;
                    ring.SetPosition(
                        i,
                        outwardLocal * Mathf.Cos(angle) * radius +
                        forwardLocal * Mathf.Sin(angle) * radius);
                }
                pulseRenderers.Add(ring);
            }
            SetRootVisible(impactPulseRoot, false);
        }

        private void CreateImpactWarningWave(
            LineRenderer islandBoundary)
        {
            Vector3[] boundaryPoints = null;
            if (islandBoundary != null &&
                islandBoundary.positionCount >= 3 &&
                stage != null)
            {
                boundaryPoints = new Vector3[
                    islandBoundary.positionCount];
                for (int i = 0; i < boundaryPoints.Length; i++)
                {
                    Vector3 sourcePoint =
                        islandBoundary.GetPosition(i);
                    Vector3 worldPoint = islandBoundary.useWorldSpace
                        ? sourcePoint
                        : islandBoundary.transform.TransformPoint(
                            sourcePoint);
                    Vector3 localPoint =
                        stage.InverseTransformPoint(worldPoint);
                    boundaryPoints[i] = localPoint +
                        Vector3.up * carWidth * 0.012f;
                }
            }

            warningWaveRoot = CreateRoot(
                "IslandWarningWave",
                impactTransientRoot);
            warningWaveRoot.localPosition = contactLocal +
                Vector3.up * carWidth * 0.025f;
            LineRenderer wave = CreateLine(
                "IslandWarningWaveRing",
                warningWaveRoot,
                warningWaveMaterial,
                carWidth * 0.035f,
                false);
            int wavePoints = boundaryPoints != null
                ? boundaryPoints.Length
                : 64;
            wave.loop = true;
            wave.positionCount = wavePoints;
            for (int i = 0; i < wavePoints; i++)
            {
                if (boundaryPoints != null)
                {
                    Vector3 delta = boundaryPoints[i] -
                        contactLocal;
                    delta.y = 0f;
                    wave.SetPosition(i, delta * 0.94f);
                }
                else
                {
                    float angle = i / (float)wavePoints *
                        Mathf.PI * 2f;
                    wave.SetPosition(
                        i,
                        outwardLocal * Mathf.Cos(angle) *
                        carLength * 1.45f +
                        forwardLocal * Mathf.Sin(angle) *
                        carLength * 2.8f);
                }
            }

            boundaryPulseRoot = CreateRoot(
                "IslandBoundaryDoublePulse",
                impactTransientRoot);
            if (boundaryPoints != null)
            {
                LineRenderer boundaryPulse = CreateLine(
                    "IslandBoundaryPulseLine",
                    boundaryPulseRoot,
                    boundaryPulseMaterial,
                    Mathf.Max(
                        carWidth * 0.035f,
                        islandBoundary.widthMultiplier * 1.7f),
                    false);
                boundaryPulse.loop = islandBoundary.loop;
                boundaryPulse.positionCount =
                    boundaryPoints.Length;
                boundaryPulse.SetPositions(boundaryPoints);
            }
            SetImpactWarningWave(-1f);
        }

        private void CreateContactVector()
        {
            Vector3 direction = FlattenNormalized(
                impactDirectionLocal,
                outwardLocal);
            Vector3 start = contactLocal -
                direction * carWidth * 0.22f +
                Vector3.up * carWidth * 0.055f;
            Vector3 end = contactLocal +
                direction * carWidth * 0.78f +
                Vector3.up * carWidth * 0.055f;
            LineRenderer vector = CreateLine(
                $"ContactVector_{contactPattern}",
                impactRoot,
                impactMaterial,
                carWidth * 0.032f,
                false);
            vector.positionCount = 2;
            vector.SetPosition(0, start);
            vector.SetPosition(1, end);

            Vector3 right = Vector3.Cross(Vector3.up, direction)
                .normalized;
            for (int sideIndex = 0; sideIndex < 2; sideIndex++)
            {
                float side = sideIndex == 0 ? -1f : 1f;
                LineRenderer head = CreateLine(
                    $"ContactVectorHead_{sideIndex:00}",
                    impactRoot,
                    impactMaterial,
                    carWidth * 0.032f,
                    false);
                head.positionCount = 2;
                head.SetPosition(0, end);
                head.SetPosition(
                    1,
                    end - direction * carWidth * 0.22f +
                    right * side * carWidth * 0.14f);
            }

            GameObject labelObject = new(
                "ContactLabel",
                typeof(TextMeshPro));
            labelObject.transform.SetParent(impactRoot, false);
            labelObject.transform.localPosition = contactLocal -
                forwardLocal * carLength * 0.18f +
                Vector3.up * carWidth * 0.18f;
            labelObject.transform.localRotation =
                Quaternion.LookRotation(Vector3.up, forwardLocal);
            labelObject.transform.localScale =
                Vector3.one * carLength * 0.052f;
            TextMeshPro label =
                labelObject.GetComponent<TextMeshPro>();
            label.text = "CONTACT";
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 4.4f;
            label.fontStyle = FontStyles.Bold;
            label.enableAutoSizing = false;
            label.color = new Color(1f, 0.48f, 0.08f, 0.98f);
            label.rectTransform.sizeDelta = new Vector2(8f, 2f);
            label.renderer.shadowCastingMode = ShadowCastingMode.Off;
            label.renderer.receiveShadows = false;
        }

        private void CreateWarningBoundary(
            string label,
            string metadata)
        {
            scanRingRoot = CreateRoot(
                "VerticalScanRing",
                warningRoot);
            scanRingRoot.localPosition = contactLocal +
                Vector3.up * carWidth * 0.34f;
            LineRenderer ring = CreateLine(
                "ScanRingLine",
                scanRingRoot,
                warningMaterial,
                carWidth * 0.025f,
                false);
            const int ringPoints = 49;
            ring.positionCount = ringPoints;
            float radius = carWidth * 0.72f;
            Vector3 right = Vector3.Cross(
                Vector3.up,
                impactDirectionLocal).normalized;
            if (right.sqrMagnitude <= 0.000001f)
                right = outwardLocal;
            for (int i = 0; i < ringPoints; i++)
            {
                float angle = i /
                    (ringPoints - 1f) *
                    Mathf.PI * 2f;
                ring.SetPosition(
                    i,
                    right * Mathf.Cos(angle) * radius +
                    Vector3.up * Mathf.Sin(angle) * radius);
            }
            warningRenderers.Add(ring);

            Vector3 rightLocal = Vector3.Cross(
                Vector3.up,
                forwardLocal).normalized;
            for (int i = 0; i < 4; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                float along = i < 2 ? -1f : 1f;
                Vector3 basePosition = contactLocal +
                    rightLocal * side * carLength * 1.12f +
                    forwardLocal * along * carLength * 1.88f;
                LineRenderer beacon = CreateLine(
                    $"YellowBeacon_{i:00}",
                    warningRoot,
                    warningMaterial,
                    carWidth * 0.045f,
                    false);
                beacon.positionCount = 2;
                beacon.SetPosition(0, basePosition);
                beacon.SetPosition(
                    1,
                    basePosition + Vector3.up * carWidth * 0.55f);
                warningRenderers.Add(beacon);
            }

            GameObject panel = new(
                "IncidentPanel",
                typeof(TextMeshPro));
            panel.transform.SetParent(warningRoot, false);
            panel.transform.localPosition = contactLocal -
                forwardLocal * carLength * 1.68f +
                Vector3.up * carWidth * 0.68f;
            panel.transform.localRotation =
                Quaternion.LookRotation(Vector3.up, forwardLocal);
            panel.transform.localScale =
                Vector3.one * carLength * 0.085f;
            TextMeshPro text = panel.GetComponent<TextMeshPro>();
            string participants = string.IsNullOrWhiteSpace(label)
                ? string.Empty
                : $"\n{label}";
            string timing = string.IsNullOrWhiteSpace(metadata)
                ? string.Empty
                : $"  |  {metadata}";
            text.text =
                $"INCIDENT{participants}{timing}\n" +
                "<color=#DCEFFF>OBSERVED</color>  " +
                "<color=#FF8A24>CONTACT</color>  " +
                "<color=#FF3028>RECONSTRUCTED</color>";
            text.alignment = TextAlignmentOptions.Center;
            text.richText = true;
            text.fontSize = 5.2f;
            text.fontStyle = FontStyles.Bold;
            text.enableAutoSizing = false;
            text.color = new Color(1f, 0.78f, 0.04f, 0.95f);
            text.rectTransform.sizeDelta = new Vector2(16f, 4f);
            text.renderer.shadowCastingMode = ShadowCastingMode.Off;
            text.renderer.receiveShadows = false;
        }

        private void CreateDriverAnnotations(
            string victimDriverLabel,
            string otherDriverLabel,
            Color victimDriverColor,
            Color otherDriverColor)
        {
            victimLabel = CreateDriverLabel(
                "VictimDriverLabel",
                victimDriverLabel,
                victimDriverColor);
            otherLabel = CreateDriverLabel(
                "OtherDriverLabel",
                otherDriverLabel,
                otherDriverColor);
            victimLabelTether = CreateLine(
                "VictimDriverTether",
                annotationRoot,
                warningMaterial,
                carWidth * 0.018f,
                false);
            otherLabelTether = CreateLine(
                "OtherDriverTether",
                annotationRoot,
                warningMaterial,
                carWidth * 0.018f,
                false);
            victimLabelTether.positionCount = 2;
            otherLabelTether.positionCount = 2;
            warningRenderers.Add(victimLabelTether);
            warningRenderers.Add(otherLabelTether);
            UpdateDriverAnnotations();
        }

        private TextMeshPro CreateDriverLabel(
            string objectName,
            string driverLabel,
            Color color)
        {
            GameObject labelObject = new(
                objectName,
                typeof(TextMeshPro));
            labelObject.transform.SetParent(annotationRoot, false);
            labelObject.transform.localRotation =
                Quaternion.LookRotation(Vector3.up, forwardLocal);
            labelObject.transform.localScale =
                Vector3.one * carLength * 0.06f;
            TextMeshPro text = labelObject.GetComponent<TextMeshPro>();
            text.text = string.IsNullOrWhiteSpace(driverLabel)
                ? "CAR"
                : driverLabel;
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = 4.4f;
            text.fontStyle = FontStyles.Bold;
            text.enableAutoSizing = false;
            text.color = color.a > 0f
                ? new Color(color.r, color.g, color.b, 0.98f)
                : new Color(0.9f, 0.95f, 1f, 0.98f);
            text.rectTransform.sizeDelta = new Vector2(7f, 2f);
            text.renderer.shadowCastingMode = ShadowCastingMode.Off;
            text.renderer.receiveShadows = false;
            return text;
        }

        private void UpdateDriverAnnotations()
        {
            if (stage == null || annotationRoot == null)
                return;

            Vector3 right = Vector3.Cross(
                Vector3.up,
                forwardLocal).normalized;
            UpdateDriverAnnotation(
                victim,
                victimLabel,
                victimLabelTether,
                right);
            UpdateDriverAnnotation(
                other,
                otherLabel,
                otherLabelTether,
                -right);
        }

        private void UpdateDriverAnnotation(
            ReplayCarView car,
            TextMeshPro label,
            LineRenderer tether,
            Vector3 side)
        {
            if (car == null || label == null || tether == null)
                return;

            Vector3 anchor = stage.InverseTransformPoint(
                car.VisualMotionRoot.position) +
                Vector3.up * carWidth * 0.28f;
            Vector3 labelPosition = anchor +
                side * carWidth * 0.95f +
                forwardLocal * carLength * 0.08f;
            label.transform.localPosition = labelPosition;
            FaceReadableTextToViewer(label);
            tether.SetPosition(0, anchor);
            tether.SetPosition(1, labelPosition);
        }

        private void FaceReadableTextToViewer(TextMeshPro text)
        {
            Camera viewer = Camera.main;
            if (text == null || viewer == null)
                return;

            Vector3 toViewer = viewer.transform.position -
                text.transform.position;
            if (toViewer.sqrMagnitude <= 0.000001f)
                return;

            Vector3 up = stage != null
                ? stage.up
                : Vector3.up;
            text.transform.rotation = Quaternion.LookRotation(
                -toViewer.normalized,
                up);
        }

        private void CreateImpactAudio(
            CollisionShowcaseVfxSettings settings)
        {
            if (settings != null && !settings.playImpactAudio)
                return;

            GameObject emitter = new("ImpactAudioEmitter");
            emitter.transform.SetParent(impactRoot, false);
            emitter.transform.localPosition = contactLocal +
                Vector3.up * carWidth * 0.15f;
            impactAudio = emitter.AddComponent<AudioSource>();
            impactAudio.playOnAwake = false;
            impactAudio.loop = false;
            impactAudio.spatialBlend = Mathf.Clamp01(
                settings != null
                    ? settings.impactSpatialBlend
                    : 0.92f);
            impactAudio.volume = Mathf.Clamp01(
                settings != null
                    ? settings.impactVolume
                    : 0.85f);
            impactAudio.dopplerLevel = 0f;
            impactAudio.priority = 32;
            impactAudio.reverbZoneMix = 0f;
            impactAudio.rolloffMode = AudioRolloffMode.Linear;
            impactAudio.minDistance = Mathf.Max(
                0.05f,
                settings != null
                    ? settings.impactMinDistance
                    : 0.12f);
            impactAudio.maxDistance = Mathf.Max(
                impactAudio.minDistance + 0.01f,
                settings != null
                    ? settings.impactMaxDistance
                    : 6f);
            impactClip = settings != null
                ? settings.authoredImpactClip
                : null;
            ownsImpactClip = impactClip == null;
            if (ownsImpactClip)
                impactClip = CreateImpactClip();
            impactAudio.clip = impactClip;
        }

        private void TriggerImpactFeedback()
        {
            SetRootVisible(impactTransientRoot, true);
            PrepareImpactSmoke();
            PlayImpactAudio();
            PlayImpactHaptic(
                primaryHapticAmplitude,
                primaryHapticDuration);
            secondaryHapticCountdown =
                secondaryHapticDelaySeconds;
            if (smoke != null)
                smoke.Play(true);
        }

        private void TickSecondaryImpactHaptic(float delta)
        {
            if (secondaryHapticTriggered ||
                !impactTriggered ||
                secondaryHapticCountdown < 0f)
            {
                return;
            }

            secondaryHapticCountdown -= Mathf.Max(0f, delta);
            if (secondaryHapticCountdown > 0f)
                return;

            secondaryHapticTriggered = true;
            secondaryHapticCountdown = -1f;
            PlayImpactHaptic(
                secondaryHapticAmplitude,
                secondaryHapticDuration);
        }

        private void PlayImpactHaptic(float amplitude, float duration)
        {
            if (!impactHapticsEnabled ||
                amplitude <= 0f ||
                duration <= 0f)
            {
                return;
            }

            SendHapticImpulse(
                XRNode.LeftHand,
                amplitude,
                duration);
            SendHapticImpulse(
                XRNode.RightHand,
                amplitude,
                duration);
        }

        private static void SendHapticImpulse(
            XRNode node,
            float amplitude,
            float duration)
        {
            HapticDevices.Clear();
            InputDevices.GetDevicesAtXRNode(node, HapticDevices);
            for (int i = 0; i < HapticDevices.Count; i++)
            {
                InputDevice device = HapticDevices[i];
                if (device.isValid &&
                    device.TryGetHapticCapabilities(
                        out HapticCapabilities capabilities) &&
                    capabilities.supportsImpulse)
                {
                    device.SendHapticImpulse(
                        0u,
                        Mathf.Clamp01(amplitude),
                        duration);
                }
            }
        }

        private void PlayImpactAudio()
        {
            if (impactAudio == null || impactClip == null)
                return;

            impactAudio.Stop();
            impactAudio.Play();
        }

        private void SetImpactFlash(bool visible, float age)
        {
            SetRootVisible(impactFlashRoot, visible);
            if (impactFlashRoot == null || !visible)
                return;

            float pulse = 1f + Mathf.Clamp01(age / 0.18f) * 0.75f;
            impactFlashRoot.localScale = Vector3.one * pulse;
        }

        private void SetImpactPulse(float age)
        {
            bool visible = age >= 0f && age <= 0.42f;
            SetRootVisible(impactPulseRoot, visible);
            if (!visible || impactPulseRoot == null)
                return;

            float progress = Mathf.Clamp01(age / 0.42f);
            float scale = Mathf.Lerp(0.38f, 1.55f,
                Mathf.SmoothStep(0f, 1f, progress));
            impactPulseRoot.localScale = Vector3.one * scale;
            SetRendererAlpha(
                pulseRenderers,
                (1f - progress) * 0.94f);
        }

        private void SetImpactTransient(float age)
        {
            bool visible = age >= 0f &&
                age <= ImpactTransientDuration;
            SetRootVisible(impactTransientRoot, visible);
            if (age > ImpactTransientDuration &&
                smoke != null &&
                (smoke.isPlaying || smoke.particleCount > 0))
            {
                smoke.Stop(
                    true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void SetImpactWarningWave(float age)
        {
            bool waveVisible = age >= 0f &&
                age <= warningWaveDurationSeconds &&
                warningWaveIntensity > 0f;
            SetRootVisible(warningWaveRoot, waveVisible);
            if (waveVisible && warningWaveRoot != null)
            {
                float progress = Mathf.Clamp01(
                    age / warningWaveDurationSeconds);
                float scale = Mathf.Lerp(
                    0.15f,
                    1f,
                    Mathf.SmoothStep(0f, 1f, progress));
                warningWaveRoot.localScale = Vector3.one * scale;
                SetMaterialAlpha(
                    warningWaveMaterial,
                    (1f - progress) * 0.9f *
                    warningWaveIntensity);
            }

            float boundaryPulse = Mathf.Max(
                ResolvePulseWindow(age, 0.02f, 0.18f),
                ResolvePulseWindow(age, 0.24f, 0.42f));
            bool boundaryVisible = boundaryPulse > 0.001f &&
                warningWaveIntensity > 0f;
            SetRootVisible(boundaryPulseRoot, boundaryVisible);
            SetMaterialAlpha(
                boundaryPulseMaterial,
                boundaryPulse * 0.92f * warningWaveIntensity);
        }

        private static float ResolvePulseWindow(
            float age,
            float start,
            float end)
        {
            if (age < start || age > end)
                return 0f;
            return Mathf.Sin(
                Mathf.InverseLerp(start, end, age) * Mathf.PI);
        }

        private void ClearImpactSmoke()
        {
            if (smoke == null)
                return;
            smoke.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void PrepareImpactSmoke()
        {
            if (smoke == null)
                return;

            ClearImpactSmoke();
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI * 0.75f;
                ParticleSystem.EmitParams emit = new()
                {
                    position = new Vector3(
                        Mathf.Cos(angle) * carWidth * 0.12f,
                        (i % 4) * carWidth * 0.11f,
                        Mathf.Sin(angle) * carWidth * 0.1f),
                    startSize = carWidth * Mathf.Lerp(
                        0.18f,
                        0.28f,
                        (i % 5) / 4f),
                    startLifetime = 2f,
                    startColor = new Color32(148, 154, 166, 118)
                };
                smoke.Emit(emit, 1);
            }
        }

        private void SetImpactBurst(float age)
        {
            float sparkProgress = age < 0f
                ? 0f
                : Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(0f, 0.11f, age));
            int sparkCount = Mathf.Min(
                impactSparkLines.Count,
                impactSparkEnds.Count);
            for (int i = 0; i < sparkCount; i++)
            {
                LineRenderer spark = impactSparkLines[i];
                if (spark == null)
                    continue;
                spark.positionCount = 2;
                spark.SetPosition(0, Vector3.zero);
                spark.SetPosition(
                    1,
                    impactSparkEnds[i] * sparkProgress);
            }

            float debrisProgress = age < 0f
                ? 0f
                : Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(0.025f, 0.32f, age));
            Vector3 debrisOrigin = contactLocal +
                Vector3.up * carWidth * 0.12f;
            int debrisCount = Mathf.Min(
                impactDebris.Count,
                Mathf.Min(
                    impactDebrisPositions.Count,
                    Mathf.Min(
                        impactDebrisScales.Count,
                        impactDebrisRotations.Count)));
            for (int i = 0; i < debrisCount; i++)
            {
                Transform debris = impactDebris[i];
                if (debris == null)
                    continue;

                debris.localPosition = Vector3.Lerp(
                    debrisOrigin,
                    impactDebrisPositions[i],
                    debrisProgress);
                debris.localScale = age < 0f
                    ? Vector3.zero
                    : impactDebrisScales[i] *
                      Mathf.Lerp(0.12f, 1f, debrisProgress);
                debris.localRotation = Quaternion.Slerp(
                    Quaternion.identity,
                    impactDebrisRotations[i],
                    debrisProgress);
            }
        }

        private GameObject CaptureGhost(
            ReplayCarView source,
            string name,
            Transform parent,
            Material material,
            List<Renderer> renderers)
        {
            if (source == null || parent == null || stage == null)
                return null;

            Transform sourceVisual = source.VisualMotionRoot;
            GameObject clone = UnityEngine.Object.Instantiate(
                sourceVisual.gameObject);
            clone.name = name;
            clone.transform.SetParent(parent, false);
            clone.transform.localPosition =
                stage.InverseTransformPoint(sourceVisual.position);
            clone.transform.localRotation =
                Quaternion.Inverse(stage.rotation) *
                sourceVisual.rotation;
            clone.transform.localScale = DivideScale(
                sourceVisual.lossyScale,
                stage.lossyScale);

            MonoBehaviour[] behaviours =
                clone.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
                behaviours[i].enabled = false;
            Collider[] colliders =
                clone.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
                colliders[i].enabled = false;
            AudioSource[] sources =
                clone.GetComponentsInChildren<AudioSource>(true);
            for (int i = 0; i < sources.Length; i++)
                sources[i].enabled = false;
            ParticleSystemRenderer[] particleRenderers =
                clone.GetComponentsInChildren<ParticleSystemRenderer>(true);
            for (int i = 0; i < particleRenderers.Length; i++)
                particleRenderers[i].enabled = false;
            TrailRenderer[] trails =
                clone.GetComponentsInChildren<TrailRenderer>(true);
            for (int i = 0; i < trails.Length; i++)
                trails[i].enabled = false;
            LineRenderer[] lines =
                clone.GetComponentsInChildren<LineRenderer>(true);
            for (int i = 0; i < lines.Length; i++)
                lines[i].enabled = false;
            LODGroup[] lodGroups =
                clone.GetComponentsInChildren<LODGroup>(true);
            for (int i = 0; i < lodGroups.Length; i++)
                lodGroups[i].ForceLOD(0);

            Renderer[] cloneRenderers =
                clone.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < cloneRenderers.Length; i++)
            {
                Renderer renderer = cloneRenderers[i];
                if (renderer is not MeshRenderer &&
                    renderer is not SkinnedMeshRenderer)
                {
                    renderer.enabled = false;
                    continue;
                }

                Material[] shared = renderer.sharedMaterials;
                for (int materialIndex = 0;
                     materialIndex < shared.Length;
                     materialIndex++)
                {
                    shared[materialIndex] = material;
                }
                renderer.sharedMaterials = shared;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.motionVectorGenerationMode =
                    MotionVectorGenerationMode.ForceNoMotion;
                renderers.Add(renderer);
            }

            return clone;
        }

        private void SetIncomingTrajectoryProgress(
            float victimProgress,
            float otherProgress)
        {
            Vector3 lift = Vector3.up * carWidth * 0.02f;
            SetLineProgress(
                victimIncomingLine,
                victimIncomingPoints,
                victimProgress,
                lift);
            SetLineProgress(
                otherIncomingLine,
                otherIncomingPoints,
                otherProgress,
                lift);
        }

        private void SetObservedTrajectoryAlpha(float alpha)
        {
            float clamped = Mathf.Clamp01(alpha);
            SetMaterialAlpha(
                victimTrajectoryMaterial,
                clamped);
            SetMaterialAlpha(
                otherTrajectoryMaterial,
                clamped * 0.9f);
        }

        private void SetPostImpactProgress(float progress)
        {
            SetLineProgress(
                postTrajectoryLine,
                postImpactPoints,
                progress,
                Vector3.zero);
            if (postSkidLines == null || postSkidPoints == null)
                return;

            int count = Mathf.Min(
                postSkidLines.Length,
                postSkidPoints.Length);
            for (int i = 0; i < count; i++)
            {
                SetLineProgress(
                    postSkidLines[i],
                    postSkidPoints[i],
                    progress,
                    Vector3.zero);
            }
        }

        private static void SetLineProgress(
            LineRenderer line,
            IReadOnlyList<Vector3> points,
            float progress,
            Vector3 offset)
        {
            if (line == null)
                return;
            int available = points != null ? points.Count : 0;
            if (available < 2 || progress <= 0f)
            {
                line.positionCount = 0;
                return;
            }

            float segmentProgress = Mathf.Clamp01(progress) *
                (available - 1);
            int segmentIndex = Mathf.FloorToInt(segmentProgress);
            if (segmentIndex >= available - 1)
            {
                line.positionCount = available;
                for (int i = 0; i < available; i++)
                    line.SetPosition(i, points[i] + offset);
                return;
            }

            int visible = segmentIndex + 2;
            line.positionCount = visible;
            for (int i = 0; i <= segmentIndex; i++)
                line.SetPosition(i, points[i] + offset);
            float interpolation = segmentProgress - segmentIndex;
            line.SetPosition(
                visible - 1,
                Vector3.Lerp(
                    points[segmentIndex],
                    points[segmentIndex + 1],
                    interpolation) + offset);
        }

        private LineRenderer CreateTrajectory(
            string name,
            IReadOnlyList<Vector3> points,
            float width,
            Material material,
            Transform parent)
        {
            LineRenderer line = CreateLine(
                name,
                parent,
                material,
                width,
                true);
            int count = points != null ? points.Count : 0;
            line.positionCount = count;
            for (int i = 0; i < count; i++)
                line.SetPosition(i, points[i] + Vector3.up * carWidth * 0.02f);
            return line;
        }

        private static LineRenderer CreateLine(
            string name,
            Transform parent,
            Material material,
            float width,
            bool rounded)
        {
            GameObject lineObject = new(
                name,
                typeof(LineRenderer));
            lineObject.transform.SetParent(parent, false);
            LineRenderer line =
                lineObject.GetComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.widthMultiplier = Mathf.Max(0.00001f, width);
            line.numCapVertices = rounded ? 3 : 0;
            line.numCornerVertices = rounded ? 3 : 0;
            line.sharedMaterial = material;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            return line;
        }

        private Material CreateTransparentMaterial(
            string name,
            Color color,
            bool additive = false)
        {
            Material material = additive
                ? ReplayCarVisualUtil.CreateSelectionMaterial(color)
                : ReplayCarVisualUtil.CreateUnlitMaterial(color);
            material.name = name;
            materials.Add(material);
            return material;
        }

        private Material CreateOpaqueMaterial(
            string name,
            Color color)
        {
            Shader shader = Shader.Find(
                "Universal Render Pipeline/Unlit") ??
                Shader.Find("Unlit/Color");
            Material material = new(shader)
            {
                name = name,
                color = color
            };
            ReplayCarVisualUtil.SetMaterialColor(material, color);
            materials.Add(material);
            return material;
        }

        private Material CreateParticleMaterial(
            Texture texture,
            Color color)
        {
            Shader shader = Shader.Find(
                    "Universal Render Pipeline/Particles/Unlit") ??
                Shader.Find("Particles/Standard Unlit") ??
                Shader.Find("Sprites/Default");
            Material material = new(shader)
            {
                name = "Runtime_CollisionRadialSmoke",
                renderQueue = 3000
            };
            ReplayCarVisualUtil.SetMaterialColor(material, color);
            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_SrcBlend"))
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend"))
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite"))
                material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", texture);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            materials.Add(material);
            return material;
        }

        private static Texture2D CreateRadialAlphaTexture()
        {
            const int size = 64;
            Texture2D texture = new(
                size,
                size,
                TextureFormat.RGBA32,
                false,
                true)
            {
                name = "Runtime_CollisionRadialSmokeAlpha",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            Color32[] pixels = new Color32[size * size];
            Vector2 center = new((size - 1) * 0.5f, (size - 1) * 0.5f);
            float radius = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(
                        new Vector2(x, y),
                        center) / radius;
                    float alpha = Mathf.Pow(
                        Mathf.Clamp01(1f - distance),
                        1.75f);
                    pixels[y * size + x] = new Color32(
                        214,
                        220,
                        230,
                        (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private Mesh CreateShardMesh()
        {
            Mesh mesh = new()
            {
                name = "Runtime_CollisionEvidenceShard",
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
            meshes.Add(mesh);
            return mesh;
        }

        private static AudioClip CreateImpactClip()
        {
            const int sampleRate = 24000;
            const float duration = 0.72f;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[sampleCount];
            var random = new System.Random(19780407);
            for (int i = 0; i < sampleCount; i++)
            {
                float time = i / (float)sampleRate;
                float randomValue =
                    (float)(random.NextDouble() * 2.0 - 1.0) *
                    Mathf.Exp(-time * 31f) * 0.72f;

                float bodyTime = Mathf.Max(0f, time - 0.008f);
                float body = Mathf.Sin(
                    Mathf.PI * 2f *
                    (105f - bodyTime * 58f) * bodyTime) *
                    Mathf.Exp(-bodyTime * 10.5f) * 0.76f;

                float metalTime = Mathf.Max(0f, time - 0.032f);
                float metalActive = time >= 0.032f ? 1f : 0f;
                float metal = metalActive *
                    (Mathf.Sin(Mathf.PI * 2f * 710f * metalTime) * 0.22f +
                     Mathf.Sin(Mathf.PI * 2f * 1170f * metalTime) * 0.14f +
                     Mathf.Sin(Mathf.PI * 2f * 1730f * metalTime) * 0.08f) *
                    Mathf.Exp(-metalTime * 7.2f);

                float debrisTime = Mathf.Max(0f, time - 0.12f);
                float debrisActive = time >= 0.12f ? 1f : 0f;
                float debrisGate = Mathf.Pow(
                    Mathf.Max(
                        0f,
                        Mathf.Sin(
                            debrisTime * Mathf.PI * 2f * 10.5f)),
                    10f);
                float debris = debrisActive *
                    (float)(random.NextDouble() * 2.0 - 1.0) *
                    debrisGate * Mathf.Exp(-debrisTime * 4.8f) * 0.34f;
                samples[i] = Mathf.Clamp(
                    randomValue + body + metal + debris,
                    -1f,
                    1f);
            }

            AudioClip clip = AudioClip.Create(
                "Runtime_CollisionIncidentImpact_Layered",
                sampleCount,
                1,
                sampleRate,
                false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static void ConfigureRenderer(
            Renderer renderer,
            Material material)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private void SetCarsVisible(bool visible)
        {
            if (victim != null)
                victim.LogicalRoot.gameObject.SetActive(visible);
            if (other != null)
                other.LogicalRoot.gameObject.SetActive(visible);
        }

        private void ResetVehicleMotion()
        {
            victim?.ResetVisualMotion();
            other?.ResetVisualMotion();
        }

        private static void SetRootVisible(
            Transform root,
            bool visible)
        {
            if (root != null && root.gameObject.activeSelf != visible)
                root.gameObject.SetActive(visible);
        }

        private static Transform CreateRoot(
            string name,
            Transform parent)
        {
            GameObject root = new(name);
            root.transform.SetParent(parent, false);
            return root.transform;
        }

        private static void SetRendererAlpha(
            List<Renderer> renderers,
            float alpha)
        {
            for (int i = 0; i < renderers.Count; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                Material[] shared = renderer.sharedMaterials;
                for (int materialIndex = 0;
                     materialIndex < shared.Length;
                     materialIndex++)
                {
                    SetMaterialAlpha(shared[materialIndex], alpha);
                }
            }
        }

        private static void SetMaterialAlpha(
            Material material,
            float alpha)
        {
            if (material == null)
                return;

            Color color = material.color;
            color.a = Mathf.Clamp01(alpha);
            ReplayCarVisualUtil.SetMaterialColor(material, color);
        }

        private static Vector3[] CopyPoints(
            IReadOnlyList<Vector3> source)
        {
            int count = source != null ? source.Count : 0;
            Vector3[] copy = new Vector3[count];
            for (int i = 0; i < count; i++)
                copy[i] = source[i];
            return copy;
        }

        private ContactPattern ResolveContactPattern(
            IReadOnlyList<Vector3> victimPath,
            IReadOnlyList<Vector3> otherPath,
            out Vector3 impactDirection)
        {
            Vector3 victimHeading = ResolvePathHeading(
                victimPath,
                forwardLocal);
            Vector3 otherHeading = ResolvePathHeading(
                otherPath,
                forwardLocal);
            float alignment = Vector3.Dot(
                victimHeading,
                otherHeading);

            if (alignment < 0.55f)
            {
                impactDirection = FlattenNormalized(
                    victimHeading - otherHeading,
                    outwardLocal);
                return ContactPattern.Crossing;
            }

            Vector3 averageHeading = FlattenNormalized(
                victimHeading + otherHeading,
                forwardLocal);
            Vector3 lateral = Vector3.Cross(
                Vector3.up,
                averageHeading).normalized;
            Vector3 separation = ResolveLastPoint(otherPath) -
                ResolveLastPoint(victimPath);
            float longitudinal = Mathf.Abs(Vector3.Dot(
                separation,
                averageHeading));
            float sideways = Mathf.Abs(Vector3.Dot(
                separation,
                lateral));
            if (longitudinal > sideways * 1.15f)
            {
                impactDirection = averageHeading;
                return ContactPattern.Rear;
            }

            float sideSign = Mathf.Sign(Vector3.Dot(
                separation,
                lateral));
            impactDirection = lateral *
                (Mathf.Approximately(sideSign, 0f) ? 1f : sideSign);
            return ContactPattern.Side;
        }

        private static Vector3 ResolvePathHeading(
            IReadOnlyList<Vector3> points,
            Vector3 fallback)
        {
            if (points == null || points.Count < 2)
                return FlattenNormalized(fallback, Vector3.forward);

            return FlattenNormalized(
                points[points.Count - 1] -
                points[points.Count - 2],
                fallback);
        }

        private static Vector3 ResolveLastPoint(
            IReadOnlyList<Vector3> points)
        {
            return points != null && points.Count > 0
                ? points[points.Count - 1]
                : Vector3.zero;
        }

        private void DisableVehicleColliders(ReplayCarView car)
        {
            if (car == null)
                return;

            Collider[] colliders = car.LogicalRoot
                .GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || !collider.enabled)
                    continue;
                collider.enabled = false;
                disabledVehicleColliders.Add(collider);
            }
        }

        private float ResolveYawSign()
        {
            Vector3 right = Vector3.Cross(
                Vector3.up,
                forwardLocal).normalized;
            float sign = Mathf.Sign(Vector3.Dot(outwardLocal, right));
            return Mathf.Approximately(sign, 0f) ? 1f : sign;
        }

        private static Vector3 FlattenNormalized(
            Vector3 value,
            Vector3 fallback)
        {
            value.y = 0f;
            return value.sqrMagnitude > 0.000001f
                ? value.normalized
                : fallback;
        }

        private static Vector3 DivideScale(
            Vector3 value,
            Vector3 divisor)
        {
            return new Vector3(
                value.x / Mathf.Max(0.0001f, Mathf.Abs(divisor.x)),
                value.y / Mathf.Max(0.0001f, Mathf.Abs(divisor.y)),
                value.z / Mathf.Max(0.0001f, Mathf.Abs(divisor.z)));
        }
    }
}
