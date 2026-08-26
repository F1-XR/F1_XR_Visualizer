using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine;
using Object = UnityEngine.Object;

namespace F1XR.RestAPI.Replay
{
    internal sealed class PitStopFirstMilestoneChoreography
    {
        private const string BaseClipName = "AS_Wheel_Gunner_Idle";
        private static readonly int BaseStateHash =
            Animator.StringToHash("Base Layer.AS_Wheel_Gunner_Idle");

        internal const float ReleaseReadyTime = 3.2f;
        private const float FullScaleVehicleLengthMeters = 5.6f;
        private const float FullScaleCrewHeightMeters = 1.78f;
        private const float FallbackTyreDiameterMeters = 0.72f;
        private const float FallbackTyreThicknessMeters = 0.32f;
        private const float CarriedTyreBodyGapMeters = 0.055f;
        private const float JackDuration = 2.8f;
        private const float GunnerLoosenEndTime = 0.8f;
        private const float GunnerTightenStartTime = 2.35f;
        private const float GunnerLoosenEndNormalized = 0.5f;
        private const float WheelOffStartTime = 0.8f;
        private const float WheelOffOwnershipTime = 1.3f;
        private const float WheelOffClearTime = 1.7f;
        private const float WheelOffEndTime = 2.25f;
        private const float WheelOffClearNormalized = 0.7f;
        private const float WheelOnStartTime = 1.7f;
        private const float WheelOnHandoffTime = 2.35f;
        private const float WheelOnEndTime = 3f;
        private const float SignalLeadTime = 0.3f;
        private const float SignalDuration = 3.1f;
        private const float GunnerContactNormalized = 0.25f;
        private const float WheelOffOwnershipNormalized = 0.42f;
        private const float WheelOnHandoffNormalized = 0.62f;
        private const float StaticWheelOffPoseNormalized = 0.34f;
        private const float StaticWheelOnPoseNormalized = 0.18f;
        private const float StaticJackPoseNormalized = 0.42f;
        private const float StaticSignalPoseNormalized = 0.38f;
        private const float VehicleClearanceMarginMeters = 0.22f;
        private const float CrewClearancePaddingMeters = 0.1f;
        private const float RacerVisualScaleMultiplier = 1.03f;
        private const float RacerRunPlaybackSpeed = 1f;
        private const float RacerBackwardRunPlaybackSpeed = 1f;
        private const float RacerLocomotionCrossfadeDuration = 0.1f;
        private const float RacerIdleCrossfadeDuration = 0.06f;
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int MetallicId =
            Shader.PropertyToID("_Metallic");
        private static readonly int SmoothnessId =
            Shader.PropertyToID("_Smoothness");
        private static readonly Color FerrariWheelGunnerRed =
            new(0.34f, 0.035f, 0.028f, 1f);
        private static readonly Color FerrariWheelOffRed =
            new(0.46f, 0.055f, 0.04f, 1f);
        private static readonly Color FerrariWheelOnRed =
            new(0.405f, 0.044f, 0.034f, 1f);
        private static readonly Vector3 FallbackFlHub =
            new(0.92f, 0.39f, 2f);
        private static readonly Vector3 FallbackRearHub =
            new(0.92f, 0.39f, -1.75f);

        private sealed class SampledActor
        {
            private readonly Animator animator;
            private readonly AnimatorOverrideController controller;
            private readonly Transform motionRoot;
            private readonly Vector3 motionRootLocalPosition;
            private readonly Quaternion motionRootLocalRotation;
            private readonly bool lockMotionRoot;
            private AnimationClip activeClip;
            private float sampledNormalizedTime = -1f;
            private PlayableGraph blendGraph;
            private AnimationMixerPlayable blendMixer;
            private AnimationClipPlayable firstBlendPlayable;
            private AnimationClipPlayable secondBlendPlayable;
            private AnimationClip firstBlendClip;
            private AnimationClip secondBlendClip;

            public SampledActor(
                Transform root,
                Animator animator,
                RuntimeAnimatorController baseController,
                bool lockMotionRoot)
            {
                Root = root;
                this.animator = animator;
                this.lockMotionRoot = lockMotionRoot;
                motionRoot = animator.transform;
                motionRootLocalPosition = motionRoot.localPosition;
                motionRootLocalRotation = motionRoot.localRotation;
                controller = new AnimatorOverrideController(baseController);
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = !lockMotionRoot;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.updateMode = AnimatorUpdateMode.Normal;
                animator.speed = 0f;
                animator.Update(0f);
            }

            public Transform Root { get; }

            public Transform GetBoneTransform(HumanBodyBones bone)
            {
                return animator != null &&
                       animator.avatar != null &&
                       animator.avatar.isValid &&
                       animator.avatar.isHuman
                    ? animator.GetBoneTransform(bone)
                    : null;
            }

            public void Dispose()
            {
                DestroyBlendGraph(restoreController: false);
                if (animator != null)
                    animator.runtimeAnimatorController = null;
                Object.Destroy(controller);
            }

            public void SampleNormalized(
                AnimationClip clip,
                float normalizedTime)
            {
                if (clip == null ||
                    animator == null ||
                    !animator.isActiveAndEnabled)
                {
                    return;
                }

                DestroyBlendGraph(restoreController: true);

                if (activeClip != clip)
                {
                    controller[BaseClipName] = clip;
                    activeClip = clip;
                    sampledNormalizedTime = -1f;
                }

                normalizedTime = Mathf.Clamp(
                    normalizedTime,
                    0f,
                    0.999f);
                if (sampledNormalizedTime < 0f ||
                    normalizedTime + 0.00001f < sampledNormalizedTime)
                {
                    ResetToClipStart();
                }

                float deltaNormalized = Mathf.Max(
                    0f,
                    normalizedTime - sampledNormalizedTime);
                if (deltaNormalized > 0f)
                {
                    animator.speed = 1f;
                    animator.Update(deltaNormalized * clip.length);
                    animator.speed = 0f;
                }

                RestoreMotionRoot();
                sampledNormalizedTime = normalizedTime;
            }

            public void SampleBlended(
                AnimationClip firstClip,
                float firstTime,
                AnimationClip secondClip,
                float secondNormalizedTime,
                float secondWeight)
            {
                if (firstClip == null ||
                    secondClip == null ||
                    animator == null ||
                    !animator.isActiveAndEnabled)
                {
                    return;
                }

                EnsureBlendGraph(firstClip, secondClip);
                firstBlendPlayable.SetTime(Mathf.Max(0f, firstTime));
                secondBlendPlayable.SetTime(
                    Mathf.Clamp(secondNormalizedTime, 0f, 0.999f) *
                    secondClip.length);
                secondWeight = Mathf.Clamp01(secondWeight);
                blendMixer.SetInputWeight(0, 1f - secondWeight);
                blendMixer.SetInputWeight(1, secondWeight);
                blendGraph.Evaluate(0f);
                RestoreMotionRoot();
            }

            private void EnsureBlendGraph(
                AnimationClip firstClip,
                AnimationClip secondClip)
            {
                if (blendGraph.IsValid() &&
                    firstBlendClip == firstClip &&
                    secondBlendClip == secondClip)
                {
                    return;
                }

                DestroyBlendGraph(restoreController: false);
                animator.runtimeAnimatorController = null;
                blendGraph = PlayableGraph.Create(
                    $"{Root.name}_Racer_Blend");
                blendGraph.SetTimeUpdateMode(
                    DirectorUpdateMode.Manual);
                blendMixer = AnimationMixerPlayable.Create(
                    blendGraph,
                    2);
                firstBlendPlayable = AnimationClipPlayable.Create(
                    blendGraph,
                    firstClip);
                secondBlendPlayable = AnimationClipPlayable.Create(
                    blendGraph,
                    secondClip);
                firstBlendPlayable.SetApplyFootIK(true);
                secondBlendPlayable.SetApplyFootIK(true);
                firstBlendPlayable.SetSpeed(0f);
                secondBlendPlayable.SetSpeed(0f);
                blendGraph.Connect(
                    firstBlendPlayable,
                    0,
                    blendMixer,
                    0);
                blendGraph.Connect(
                    secondBlendPlayable,
                    0,
                    blendMixer,
                    1);
                AnimationPlayableOutput output =
                    AnimationPlayableOutput.Create(
                        blendGraph,
                        "Racer Wheel Gunner Animation",
                        animator);
                output.SetSourcePlayable(blendMixer);
                blendGraph.Play();
                firstBlendClip = firstClip;
                secondBlendClip = secondClip;
                activeClip = null;
                sampledNormalizedTime = -1f;
            }

            private void DestroyBlendGraph(bool restoreController)
            {
                if (!blendGraph.IsValid())
                    return;

                blendGraph.Destroy();
                blendGraph = default;
                firstBlendClip = null;
                secondBlendClip = null;
                if (restoreController && animator != null)
                {
                    animator.runtimeAnimatorController = controller;
                    animator.Rebind();
                }
            }

            private void ResetToClipStart()
            {
                motionRoot.localPosition = motionRootLocalPosition;
                motionRoot.localRotation = motionRootLocalRotation;
                animator.Rebind();
                animator.Play(BaseStateHash, 0, 0f);
                animator.Update(0f);
                RestoreMotionRoot();
                sampledNormalizedTime = 0f;
            }

            private void RestoreMotionRoot()
            {
                if (!lockMotionRoot || motionRoot == null)
                    return;

                motionRoot.localPosition = motionRootLocalPosition;
                motionRoot.localRotation = motionRootLocalRotation;
            }
        }

        private sealed class CrewTransition
        {
            public SampledActor Actor;
            public Vector3 ServicePosition;
            public Vector3 StandbyPosition;
            public float IngressStart;
            public float IngressEnd;
            public float EgressStart;
            public float EgressEnd;
        }

        private sealed class WheelServiceCorner
        {
            public string Name;
            public bool IsFront;
            public Vector3 Hub;
            public Vector3 Outward;
            public Quaternion HubRotation;
            public float TyreDiameter;
            public float TyreThickness;
            public Transform OriginalWheel;
            public Renderer[] OriginalWheelRenderers;
            public bool[] OriginalWheelRendererStates;
            public SampledActor Gunner;
            public SampledActor WheelOff;
            public SampledActor WheelOn;
            public Transform GunnerRightHand;
            public Transform WheelOffLeftHand;
            public Transform WheelOffRightHand;
            public Transform WheelOnLeftHand;
            public Transform WheelOnRightHand;
            public GameObject OldLooseTyre;
            public GameObject NewLooseTyre;
            public Vector3 OldLooseTyreVisualCenter;
            public Vector3 NewLooseTyreVisualCenter;
            public GameObject WheelGun;
            public Transform WheelGunGrip;
        }

        private Vector3 flHub = FallbackFlHub;
        private Vector3 flOutward = Vector3.right;
        private Quaternion flHubRotation = Quaternion.identity;
        private float tyreDiameterMeters = FallbackTyreDiameterMeters;
        private float tyreThicknessMeters = FallbackTyreThicknessMeters;

        private PitShowcaseAssetProfile assets;
        private Transform origin;
        private Transform anchorRoot;
        private Transform crewRoot;
        private Transform propRoot;
        private SampledActor gunner;
        private SampledActor wheelOff;
        private SampledActor wheelOn;
        private SampledActor frontJack;
        private SampledActor rearJack;
        private SampledActor pitSignal;
        private GameObject wheelGun;
        private Transform wheelGunGrip;
        private Transform originalFlWheel;
        private Transform gunnerRightHand;
        private Transform wheelOffLeftHand;
        private Transform wheelOffRightHand;
        private Transform wheelOnLeftHand;
        private Transform wheelOnRightHand;
        private GameObject oldLooseTyre;
        private GameObject newLooseTyre;
        private Vector3 oldLooseTyreVisualCenter;
        private Vector3 newLooseTyreVisualCenter;
        private Renderer[] originalFlWheelRenderers;
        private bool[] originalFlWheelRendererStates;
        private WheelServiceCorner[] additionalCorners;
        private CrewTransition[] crewTransitions;
        private float vehicleCorridorMinX;
        private float vehicleCorridorMaxX;
        private bool useRacerTyreServiceCrew;

        public bool ReleaseReady { get; private set; }

        private AnimationClip WheelGunnerClip =>
            useRacerTyreServiceCrew
                ? assets.FlWheelGunnerHumanoidFull
                : assets.WheelGunnerFull;

        public bool TryBuild(
            Transform parent,
            ReplayCarView vehicle,
            float localVehicleLength,
            PitShowcaseAssetProfile profile)
        {
            Clear();
            if (parent == null ||
                vehicle == null ||
                localVehicleLength <= 0.0001f ||
                profile == null ||
                !profile.HasFirstMilestoneChoreographyAssets)
            {
                return false;
            }

            Transform flWheel = FindDescendant(
                vehicle.VisualMotionRoot,
                "FL_Tire");
            Transform frWheel = FindDescendant(
                vehicle.VisualMotionRoot,
                "FR_Tire");
            Transform rlWheel = FindDescendant(
                vehicle.VisualMotionRoot,
                "RL_Tire");
            Transform rrWheel = FindDescendant(
                vehicle.VisualMotionRoot,
                "RR_Tire");
            if (flWheel == null ||
                frWheel == null ||
                rlWheel == null ||
                rrWheel == null)
            {
                return false;
            }

            originalFlWheel = flWheel;

            Renderer[] flRenderers =
                flWheel.GetComponentsInChildren<Renderer>(true);
            if (flRenderers.Length == 0)
                return false;

            float measuredLocalVehicleLength = localVehicleLength;
            Renderer[] vehicleRenderers =
                vehicle.VisualMotionRoot.GetComponentsInChildren<Renderer>(
                    true);
            bool hasVehicleBounds = TryMeasureRenderersInSpace(
                    vehicleRenderers,
                    vehicle.LogicalRoot,
                    out Bounds vehicleBounds);
            if (hasVehicleBounds)
            {
                float horizontalLength = Mathf.Max(
                    vehicleBounds.size.x,
                    vehicleBounds.size.z);
                if (horizontalLength > 0.0001f)
                    measuredLocalVehicleLength = horizontalLength;
            }

            float localToPhysical = FullScaleVehicleLengthMeters /
                measuredLocalVehicleLength;
            if (TryMeasureRenderersInSpace(
                    flRenderers,
                    vehicle.LogicalRoot,
                    out Bounds flBounds))
            {
                flHub = flBounds.center * localToPhysical;
                tyreDiameterMeters = Mathf.Max(
                    flBounds.size.y,
                    flBounds.size.z) * localToPhysical;
                tyreThicknessMeters =
                    flBounds.size.x * localToPhysical;
            }
            else
            {
                flHub = FallbackFlHub;
                tyreDiameterMeters = FallbackTyreDiameterMeters;
                tyreThicknessMeters = FallbackTyreThicknessMeters;
            }
            flOutward = Mathf.Abs(flHub.x) > 0.0001f
                ? Vector3.right * Mathf.Sign(flHub.x)
                : Vector3.right;

            float vehicleFront = hasVehicleBounds
                ? vehicleBounds.max.z * localToPhysical
                : flHub.z + 1.15f;
            float vehicleRear = hasVehicleBounds
                ? vehicleBounds.min.z * localToPhysical
                : FallbackRearHub.z - 1.15f;
            float vehicleMinX = hasVehicleBounds
                ? vehicleBounds.min.x * localToPhysical
                : -Mathf.Abs(flHub.x);
            float vehicleMaxX = hasVehicleBounds
                ? vehicleBounds.max.x * localToPhysical
                : Mathf.Abs(flHub.x);
            vehicleCorridorMinX = vehicleMinX -
                VehicleClearanceMarginMeters;
            vehicleCorridorMaxX = vehicleMaxX +
                VehicleClearanceMarginMeters;

            Animator prefabAnimator =
                profile.PitCrewPrefab.GetComponentInChildren<Animator>(true);
            Transform prefabRightHand = FindDescendant(
                profile.PitCrewPrefab.transform,
                "hand_r");
            if (prefabAnimator == null || prefabRightHand == null)
                return false;

            if (profile.FlWheelGunnerPrefab != null &&
                profile.FlWheelGunnerVisualPrefab != null)
            {
                Animator racerAnimator = profile.FlWheelGunnerVisualPrefab
                    .GetComponentInChildren<Animator>(true);
                useRacerTyreServiceCrew =
                    racerAnimator != null &&
                    racerAnimator.avatar != null &&
                    racerAnimator.avatar.isValid &&
                    racerAnimator.avatar.isHuman &&
                    profile.FlWheelGunnerHumanoidFull != null &&
                    profile.RacerWheelOffHumanoidFullL != null &&
                    profile.RacerWheelOffHumanoidFullR != null &&
                    profile.RacerWheelOnHumanoidFullL != null &&
                    profile.RacerWheelOnHumanoidFullR != null;
                if (!useRacerTyreServiceCrew)
                {
                    Debug.LogWarning(
                        "[PitRacerRetarget] Racer tyre-service crew require a valid Humanoid Avatar and Humanoid Gunner/Off/On clips.");
                }
            }

            assets = profile;
            originalFlWheelRenderers = flRenderers;
            originalFlWheelRendererStates =
                new bool[originalFlWheelRenderers.Length];
            for (int i = 0; i < originalFlWheelRenderers.Length; i++)
            {
                originalFlWheelRendererStates[i] =
                    originalFlWheelRenderers[i] != null &&
                    originalFlWheelRenderers[i].enabled;
            }

            origin = CreateRoot("PitChoreographyOrigin", parent);
            origin.localScale = Vector3.one *
                (measuredLocalVehicleLength /
                 FullScaleVehicleLengthMeters);
            flHubRotation = Quaternion.Inverse(origin.rotation) *
                flWheel.rotation;
            anchorRoot = CreateRoot("AnchorRoot", origin);
            crewRoot = CreateRoot("CrewRoot", origin);
            propRoot = CreateRoot("PropRoot", origin);
            CreateAnchor("VehicleStopAnchor", Vector3.zero);
            CreateAnchor("FL_Hub", flHub);

            additionalCorners = new[]
            {
                CreateWheelServiceCorner(
                    "FR",
                    frWheel,
                    vehicle.LogicalRoot,
                    localToPhysical,
                    isFront: true),
                CreateWheelServiceCorner(
                    "RL",
                    rlWheel,
                    vehicle.LogicalRoot,
                    localToPhysical,
                    isFront: false),
                CreateWheelServiceCorner(
                    "RR",
                    rrWheel,
                    vehicle.LogicalRoot,
                    localToPhysical,
                    isFront: false)
            };
            for (int i = 0; i < additionalCorners.Length; i++)
            {
                if (additionalCorners[i] == null)
                {
                    Clear();
                    return false;
                }
            }

            float gunnerOutward = tyreDiameterMeters * 1.05f;
            float wheelOffOutward = tyreDiameterMeters * 1.18f;
            float wheelOffForward = tyreDiameterMeters * 1.26f;
            float wheelOnOutward = tyreDiameterMeters * 1.62f;
            float wheelOnRearward = tyreDiameterMeters * 1.08f;
            float frontJackClearance = tyreDiameterMeters * 0.18f;
            float rearJackClearance = tyreDiameterMeters * 0.62f;
            float signalClearance = Mathf.Max(
                tyreDiameterMeters * 3.4f,
                2.35f);
            Vector3 frontJackPosition =
                new(0f, 0f, vehicleFront + frontJackClearance);
            Vector3 rearJackPosition =
                new(0f, 0f, vehicleRear - rearJackClearance);
            Vector3 signalPosition =
                new(0f, 0f, vehicleFront + signalClearance);

            Vector3 flGround =
                new(flHub.x, 0f, flHub.z);
            Vector3 gunnerPosition = flGround +
                flOutward * gunnerOutward;
            Vector3 wheelOffPosition = flGround +
                flOutward * wheelOffOutward +
                Vector3.forward * wheelOffForward;
            Vector3 wheelOnPosition = flGround +
                flOutward * wheelOnOutward +
                Vector3.back * wheelOnRearward;
            gunner = CreateActor(
                "FL_WheelGunner",
                gunnerPosition,
                ResolveGroundFacing(gunnerPosition, flGround),
                lockMotionRoot: true);
            wheelOff = CreateActor(
                "FL_WheelOff_L",
                wheelOffPosition,
                ResolveGroundFacing(wheelOffPosition, flGround),
                lockMotionRoot: true);
            wheelOn = CreateActor(
                "FL_WheelOn_L",
                wheelOnPosition,
                ResolveGroundFacing(wheelOnPosition, flGround),
                lockMotionRoot: true);
            frontJack = CreateActor(
                "FrontJack",
                frontJackPosition,
                Vector3.back,
                lockMotionRoot: true);
            rearJack = CreateActor(
                "RearJack_R",
                rearJackPosition,
                Vector3.forward,
                lockMotionRoot: true);
            pitSignal = CreateActor(
                "PitSignal_R",
                signalPosition,
                Vector3.back,
                lockMotionRoot: true);
            if (gunner == null ||
                wheelOff == null ||
                wheelOn == null ||
                frontJack == null ||
                rearJack == null ||
                pitSignal == null)
            {
                Clear();
                return false;
            }

            SetActorPresentationVisible(frontJack, false);
            SetActorPresentationVisible(rearJack, false);
            SetActorPresentationVisible(pitSignal, false);

            gunnerRightHand = gunner.GetBoneTransform(
                    HumanBodyBones.RightHand) ??
                FindDescendant(gunner.Root, "hand_r");
            wheelOffLeftHand = wheelOff.GetBoneTransform(
                    HumanBodyBones.LeftHand) ??
                FindDescendant(wheelOff.Root, "hand_l");
            wheelOffRightHand = wheelOff.GetBoneTransform(
                    HumanBodyBones.RightHand) ??
                FindDescendant(wheelOff.Root, "hand_r");
            wheelOnLeftHand = wheelOn.GetBoneTransform(
                    HumanBodyBones.LeftHand) ??
                FindDescendant(wheelOn.Root, "hand_l");
            wheelOnRightHand = wheelOn.GetBoneTransform(
                    HumanBodyBones.RightHand) ??
                FindDescendant(wheelOn.Root, "hand_r");
            if (gunnerRightHand == null ||
                wheelOffLeftHand == null ||
                wheelOffRightHand == null ||
                wheelOnLeftHand == null ||
                wheelOnRightHand == null)
            {
                Clear();
                return false;
            }

            oldLooseTyre = CreateProp(
                flWheel.gameObject,
                "FL_OldLooseTyre");
            newLooseTyre = CreateProp(
                flWheel.gameObject,
                "FL_NewLooseTyre");
            wheelGun = CreateProp(
                assets.WheelGunPrefab,
                "FL_WheelGun");
            wheelGunGrip = wheelGun != null
                ? FindDescendant(wheelGun.transform, "GripAnchor")
                : null;
            if (oldLooseTyre == null ||
                newLooseTyre == null ||
                wheelGun == null ||
                wheelGunGrip == null)
            {
                Clear();
                return false;
            }

            NormalizePropDiameter(oldLooseTyre, tyreDiameterMeters);
            NormalizePropDiameter(newLooseTyre, tyreDiameterMeters);
            oldLooseTyreVisualCenter =
                ResolveRendererCenterInObjectSpace(oldLooseTyre);
            newLooseTyreVisualCenter =
                ResolveRendererCenterInObjectSpace(newLooseTyre);
            CalibrateGunner();
            CalibrateWheelOffContact();
            CalibrateWheelOnContact();
            ApplyWheelServiceLaneSpacing(
                "FL",
                flOutward,
                Vector3.forward,
                tyreDiameterMeters,
                wheelOff,
                wheelOn);
            ApplyReadyPose();

            CrewTransition[] flAndAuxiliaryTransitions = new[]
            {
                CreateCrewTransition(
                    gunner,
                    0f,
                    0.36f,
                    2.72f,
                    3.18f,
                    tyreDiameterMeters * 0.3f),
                CreateCrewTransition(
                    wheelOff,
                    0.15f,
                    0.72f,
                    2.25f,
                    2.75f,
                    tyreDiameterMeters * 0.55f),
                CreateCrewTransition(
                    wheelOn,
                    0.8f,
                    1.55f,
                    2.82f,
                    3.18f,
                    tyreDiameterMeters * 0.55f),
                CreateCrewTransition(frontJack, 0f, 0.45f, 2.8f, 3.18f),
                CreateCrewTransition(rearJack, 0f, 0.45f, 2.8f, 3.18f),
                CreateCrewTransition(pitSignal, 0f, 0.45f, 2.8f, 3.18f)
            };

            crewTransitions = new CrewTransition[
                flAndAuxiliaryTransitions.Length +
                additionalCorners.Length * 3];
            for (int i = 0; i < flAndAuxiliaryTransitions.Length; i++)
                crewTransitions[i] = flAndAuxiliaryTransitions[i];
            for (int i = 0; i < additionalCorners.Length; i++)
            {
                WheelServiceCorner corner = additionalCorners[i];
                int transitionIndex =
                    flAndAuxiliaryTransitions.Length + i * 3;
                crewTransitions[transitionIndex] = CreateCrewTransition(
                    corner.Gunner,
                    0f,
                    0.36f,
                    2.72f,
                    3.18f,
                    corner.Outward,
                    corner.TyreDiameter,
                    corner.TyreDiameter * 0.3f);
                crewTransitions[transitionIndex + 1] = CreateCrewTransition(
                    corner.WheelOff,
                    0.15f,
                    0.72f,
                    2.25f,
                    2.75f,
                    corner.Outward,
                    corner.TyreDiameter,
                    corner.TyreDiameter * 0.55f);
                crewTransitions[transitionIndex + 2] = CreateCrewTransition(
                    corner.WheelOn,
                    0.8f,
                    1.55f,
                    2.82f,
                    3.18f,
                    corner.Outward,
                    corner.TyreDiameter,
                    corner.TyreDiameter * 0.55f);
            }

            CreateAnchor(
                "FL_WheelGunner_Service",
                crewTransitions[0].ServicePosition);
            CreateAnchor(
                "FL_WheelOff_Service",
                crewTransitions[1].ServicePosition);
            CreateAnchor(
                "FL_WheelOn_Service",
                crewTransitions[2].ServicePosition);
            CreateAnchor(
                "FrontJack_Service",
                crewTransitions[3].ServicePosition);
            CreateAnchor(
                "RearJack_Service",
                crewTransitions[4].ServicePosition);
            CreateAnchor(
                "PitSignal_Service",
                crewTransitions[5].ServicePosition);
            for (int i = 0; i < crewTransitions.Length; i++)
            {
                CreateAnchor(
                    $"{crewTransitions[i].Actor.Root.name}_Standby",
                    crewTransitions[i].StandbyPosition);
            }
            ApplyCrewTransitions(float.NegativeInfinity);
            ApplyReadyPose();
            return true;
        }

        public void ApplyStaticServiceComposition()
        {
            if (origin == null)
                return;

            origin.gameObject.SetActive(true);
            SetCrewServicePositions();
            gunner.SampleNormalized(
                WheelGunnerClip,
                GunnerContactNormalized);
            wheelOff.SampleNormalized(
                ResolveWheelOffClip(wheelOff),
                StaticWheelOffPoseNormalized);
            wheelOn.SampleNormalized(
                ResolveWheelOnClip(wheelOn),
                StaticWheelOnPoseNormalized);
            frontJack.SampleNormalized(
                assets.FrontJackFullL,
                StaticJackPoseNormalized);
            rearJack.SampleNormalized(
                assets.RearJackFullR,
                StaticJackPoseNormalized);
            pitSignal.SampleNormalized(
                assets.PitSignalFullR,
                StaticSignalPoseNormalized);

            SetOriginalFlWheelVisible(true);
            oldLooseTyre.SetActive(false);
            newLooseTyre.SetActive(true);
            SetTyreVisualCenter(
                newLooseTyre,
                newLooseTyreVisualCenter,
                ResolveTyreGripCenter(
                    wheelOn,
                    wheelOnLeftHand,
                    wheelOnRightHand,
                    flHub,
                    flOutward,
                    tyreDiameterMeters,
                    tyreThicknessMeters));
            ApplyWheelGun();
            ApplyAdditionalStaticComposition();
            ReleaseReady = false;
        }

        public void Apply(float replayTime, PitStopSequence sequence)
        {
            if (origin == null || sequence == null)
                return;

            if (sequence.IsDriveThrough)
            {
                origin.gameObject.SetActive(false);
                SetOriginalFlWheelVisible(true);
                ReleaseReady = false;
                return;
            }

            origin.gameObject.SetActive(true);
            float time = ResolveChoreographyTime(replayTime, sequence);
            ApplyChoreographyTime(
                time,
                replayTime >= ResolveReplayEnd(sequence));
            ApplyCrewTransitions(time);
            float clampedTime = Mathf.Clamp(
                time,
                0f,
                ReleaseReadyTime);
            ApplyWheelState(
                ResolveWheelOffProgress(clampedTime),
                ResolveWheelOnProgress(clampedTime));
            ApplyWheelGun();
            ApplyAdditionalCorners(
                clampedTime,
                ResolveGunnerProgress(clampedTime),
                ResolveWheelOffProgress(clampedTime),
                ResolveWheelOnProgress(clampedTime));
        }

        public void ApplyChoreographyTime(
            float time,
            bool releaseEligible = false)
        {
            if (origin == null)
                return;

            origin.gameObject.SetActive(true);
            SetCrewServicePositions();
            time = Mathf.Clamp(time, 0f, ReleaseReadyTime);
            float gunnerProgress = ResolveGunnerProgress(time);
            float wheelOffProgress = ResolveWheelOffProgress(time);
            float wheelOnProgress = ResolveWheelOnProgress(time);
            float jackProgress = ResolveProgress(
                time,
                0f,
                JackDuration);
            float signalProgress = ResolveProgress(
                time,
                -SignalLeadTime,
                SignalDuration);

            SampleWheelGunner(
                gunner,
                FindCrewTransition(gunner),
                time,
                gunnerProgress);
            wheelOff.SampleNormalized(
                ResolveWheelOffClip(wheelOff),
                wheelOffProgress);
            wheelOn.SampleNormalized(
                ResolveWheelOnClip(wheelOn),
                wheelOnProgress);
            SampleAdditionalCorners(
                time,
                gunnerProgress,
                wheelOffProgress,
                wheelOnProgress);
            frontJack.SampleNormalized(
                assets.FrontJackFullL,
                jackProgress);
            rearJack.SampleNormalized(
                assets.RearJackFullR,
                jackProgress);
            pitSignal.SampleNormalized(
                assets.PitSignalFullR,
                signalProgress);

            ApplyWheelState(
                wheelOffProgress,
                wheelOnProgress);
            ApplyWheelGun();
            ApplyAdditionalCornerStates(
                wheelOffProgress,
                wheelOnProgress);
            ApplyAdditionalWheelGuns();
            ReleaseReady =
                releaseEligible &&
                time >= ReleaseReadyTime &&
                gunnerProgress >= 0.999f &&
                wheelOffProgress >= 0.999f &&
                wheelOnProgress >= 0.999f &&
                jackProgress >= 0.999f &&
                signalProgress >= 0.999f;
        }

        internal static float ResolveReplayStart(PitStopSequence sequence)
        {
            if (sequence == null)
                return 0f;

            return Mathf.Max(
                sequence.ServiceStartTime,
                sequence.FocusTime - ReleaseReadyTime * 0.5f);
        }

        internal static float ResolveReplayEnd(PitStopSequence sequence)
        {
            if (sequence == null)
                return ReleaseReadyTime;

            return Mathf.Min(
                sequence.ServiceEndTime,
                sequence.FocusTime + ReleaseReadyTime * 0.5f);
        }

        private static float ResolveChoreographyTime(
            float replayTime,
            PitStopSequence sequence)
        {
            float serviceStart = ResolveReplayStart(sequence);
            float serviceEnd = ResolveReplayEnd(sequence);
            float serviceDuration = Mathf.Max(
                0.05f,
                serviceEnd - serviceStart);
            return (replayTime - serviceStart) *
                (ReleaseReadyTime / serviceDuration);
        }

        private static float ResolveProgress(
            float time,
            float startTime,
            float duration)
        {
            return Mathf.Clamp01(
                (time - startTime) /
                Mathf.Max(0.001f, duration));
        }

        private static float ResolveGunnerProgress(float time)
        {
            if (time <= GunnerLoosenEndTime)
            {
                return RemapProgress(
                    time,
                    0f,
                    GunnerLoosenEndTime,
                    0f,
                    GunnerLoosenEndNormalized);
            }

            if (time < GunnerTightenStartTime)
                return GunnerLoosenEndNormalized;

            return RemapProgress(
                time,
                GunnerTightenStartTime,
                ReleaseReadyTime,
                GunnerLoosenEndNormalized,
                1f);
        }

        private static float ResolveWheelOffProgress(float time)
        {
            if (time <= WheelOffStartTime)
                return 0f;

            if (time <= WheelOffOwnershipTime)
            {
                return RemapProgress(
                    time,
                    WheelOffStartTime,
                    WheelOffOwnershipTime,
                    0f,
                    WheelOffOwnershipNormalized);
            }

            if (time <= WheelOffClearTime)
            {
                return RemapProgress(
                    time,
                    WheelOffOwnershipTime,
                    WheelOffClearTime,
                    WheelOffOwnershipNormalized,
                    WheelOffClearNormalized);
            }

            return RemapProgress(
                time,
                WheelOffClearTime,
                WheelOffEndTime,
                WheelOffClearNormalized,
                1f);
        }

        private static float ResolveWheelOnProgress(float time)
        {
            if (time <= WheelOnStartTime)
                return 0f;

            if (time <= WheelOnHandoffTime)
            {
                return RemapProgress(
                    time,
                    WheelOnStartTime,
                    WheelOnHandoffTime,
                    0f,
                    WheelOnHandoffNormalized);
            }

            return RemapProgress(
                time,
                WheelOnHandoffTime,
                WheelOnEndTime,
                WheelOnHandoffNormalized,
                1f);
        }

        private static float RemapProgress(
            float time,
            float startTime,
            float endTime,
            float startProgress,
            float endProgress)
        {
            float progress = Mathf.InverseLerp(
                startTime,
                endTime,
                time);
            return Mathf.Lerp(
                startProgress,
                endProgress,
                progress);
        }

        public void Clear()
        {
            SetOriginalFlWheelVisible(true);
            if (additionalCorners != null)
            {
                for (int i = 0; i < additionalCorners.Length; i++)
                {
                    WheelServiceCorner corner = additionalCorners[i];
                    if (corner == null)
                        continue;

                    SetCornerOriginalWheelVisible(corner, true);
                    corner.Gunner?.Dispose();
                    corner.WheelOff?.Dispose();
                    corner.WheelOn?.Dispose();
                }
            }
            gunner?.Dispose();
            wheelOff?.Dispose();
            wheelOn?.Dispose();
            frontJack?.Dispose();
            rearJack?.Dispose();
            pitSignal?.Dispose();
            if (origin != null)
                Object.Destroy(origin.gameObject);

            assets = null;
            origin = null;
            anchorRoot = null;
            crewRoot = null;
            propRoot = null;
            gunner = null;
            wheelOff = null;
            wheelOn = null;
            frontJack = null;
            rearJack = null;
            pitSignal = null;
            wheelGun = null;
            wheelGunGrip = null;
            originalFlWheel = null;
            gunnerRightHand = null;
            wheelOffLeftHand = null;
            wheelOffRightHand = null;
            wheelOnLeftHand = null;
            wheelOnRightHand = null;
            oldLooseTyre = null;
            newLooseTyre = null;
            oldLooseTyreVisualCenter = Vector3.zero;
            newLooseTyreVisualCenter = Vector3.zero;
            originalFlWheelRenderers = null;
            originalFlWheelRendererStates = null;
            additionalCorners = null;
            crewTransitions = null;
            vehicleCorridorMinX = 0f;
            vehicleCorridorMaxX = 0f;
            useRacerTyreServiceCrew = false;
            flHub = FallbackFlHub;
            flOutward = Vector3.right;
            flHubRotation = Quaternion.identity;
            tyreDiameterMeters = FallbackTyreDiameterMeters;
            tyreThicknessMeters = FallbackTyreThicknessMeters;
            ReleaseReady = false;
        }

        private SampledActor CreateActor(
            string name,
            Vector3 position,
            Vector3 lookDirection,
            bool lockMotionRoot = false)
        {
            Transform actorRoot = CreateRoot(name, crewRoot);
            actorRoot.localPosition = position;
            actorRoot.localRotation = Quaternion.LookRotation(
                lookDirection.normalized,
                Vector3.up);

            bool useCandidateVisual =
                IsTyreServiceActor(name) &&
                useRacerTyreServiceCrew;
            GameObject actorPrefab = useCandidateVisual
                ? assets.FlWheelGunnerPrefab
                : assets.PitCrewPrefab;
            GameObject instance = Object.Instantiate(
                actorPrefab,
                actorRoot);
            instance.name = $"{name}_Visual";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            GameObject characterVisual = instance;
            if (useCandidateVisual)
            {
                Renderer[] serviceRenderers =
                    instance.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < serviceRenderers.Length; i++)
                    serviceRenderers[i].enabled = false;

                Animator[] serviceAnimators =
                    instance.GetComponentsInChildren<Animator>(true);
                for (int i = 0; i < serviceAnimators.Length; i++)
                    serviceAnimators[i].enabled = false;

                characterVisual = Object.Instantiate(
                    assets.FlWheelGunnerVisualPrefab,
                    instance.transform);
                characterVisual.name = $"{name}_RacerVisual";
                characterVisual.transform.localPosition = Vector3.zero;
                characterVisual.transform.localRotation = Quaternion.identity;
                characterVisual.transform.localScale = Vector3.one;
            }

            float targetHeight = FullScaleCrewHeightMeters *
                (useCandidateVisual ? RacerVisualScaleMultiplier : 1f);
            float sourceHeight = NormalizeActorHeight(
                characterVisual,
                targetHeight);
            DisablePhysics(instance, keepAnimator: true);
            if (useCandidateVisual)
            {
                ApplyMaterial(
                    characterVisual,
                    assets.FlWheelGunnerMaterial);
                LogRacerTyreServiceActor(
                    name,
                    characterVisual,
                    sourceHeight,
                    targetHeight);
            }
            else
            {
                ApplyFerrariCrewAppearance(name, instance);
            }

            Animator animator =
                characterVisual.GetComponentInChildren<Animator>(true);
            return animator != null
                ? new SampledActor(
                    actorRoot,
                    animator,
                    assets.ChoreographyBaseController,
                    lockMotionRoot)
                : null;
        }

        private void SampleWheelGunner(
            SampledActor actor,
            CrewTransition transition,
            float time,
            float serviceProgress)
        {
            if (actor == null)
                return;

            AnimationClip idle = assets.FlWheelGunnerIdle;
            AnimationClip fastRun = assets.FlWheelGunnerFastRun;
            AnimationClip service = WheelGunnerClip;
            AnimationClip backwardRun =
                assets.FlWheelGunnerRunningBackward;
            if (!useRacerTyreServiceCrew ||
                idle == null ||
                fastRun == null ||
                backwardRun == null)
            {
                actor.SampleNormalized(
                    service,
                    serviceProgress);
                return;
            }

            float ingressStart = transition?.IngressStart ?? 0f;
            float ingressEnd = transition?.IngressEnd ?? 0.36f;
            float egressStart = transition?.EgressStart ?? 2.72f;
            float egressEnd = transition?.EgressEnd ?? 3.18f;
            float ingressRunBlendEnd = ingressStart +
                RacerIdleCrossfadeDuration;
            float serviceBlendStart = ingressEnd -
                RacerLocomotionCrossfadeDuration;
            float backwardBlendStart = egressStart -
                RacerLocomotionCrossfadeDuration;
            float finalIdleBlendStart = egressEnd -
                RacerIdleCrossfadeDuration;

            if (time <= ingressRunBlendEnd)
            {
                float runWeight = Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(
                        ingressStart,
                        ingressRunBlendEnd,
                        time));
                float ingressTime = Mathf.Max(
                    0f,
                    time - ingressStart);
                actor.SampleBlended(
                    idle,
                    ingressTime,
                    fastRun,
                    ingressTime * RacerRunPlaybackSpeed /
                        Mathf.Max(0.001f, fastRun.length),
                    runWeight);
            }
            else if (time <= ingressEnd)
            {
                float serviceWeight = Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(
                        serviceBlendStart,
                        ingressEnd,
                        time));
                actor.SampleBlended(
                    fastRun,
                    (time - ingressStart) *
                        RacerRunPlaybackSpeed,
                    service,
                    serviceProgress,
                    serviceWeight);
            }
            else if (time < finalIdleBlendStart)
            {
                float backwardWeight = Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(
                        backwardBlendStart,
                        egressStart,
                        time));
                float backwardTime = Mathf.Max(
                        0f,
                        time - backwardBlendStart) *
                    RacerBackwardRunPlaybackSpeed;
                actor.SampleBlended(
                    service,
                    serviceProgress * service.length,
                    backwardRun,
                    backwardTime /
                        Mathf.Max(0.001f, backwardRun.length),
                    backwardWeight);
            }
            else
            {
                float idleWeight = Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(
                        finalIdleBlendStart,
                        egressEnd,
                        time));
                float backwardTime = Mathf.Max(
                        0f,
                        time - backwardBlendStart) *
                    RacerBackwardRunPlaybackSpeed;
                float idleTime = Mathf.Max(
                    0f,
                    time - finalIdleBlendStart);
                actor.SampleBlended(
                    backwardRun,
                    backwardTime,
                    idle,
                    idleTime / Mathf.Max(0.001f, idle.length),
                    idleWeight);
            }
        }

        private static void ApplyMaterial(
            GameObject instance,
            Material material)
        {
            if (instance == null || material == null)
                return;

            Renderer[] renderers =
                instance.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Material[] sharedMaterials = renderers[i].sharedMaterials;
                for (int slot = 0; slot < sharedMaterials.Length; slot++)
                    sharedMaterials[slot] = material;
                renderers[i].sharedMaterials = sharedMaterials;
            }
        }

        private static void LogRacerTyreServiceActor(
            string actorName,
            GameObject instance,
            float sourceHeight,
            float targetHeight)
        {
            Renderer[] renderers =
                instance.GetComponentsInChildren<Renderer>(true);
            SkinnedMeshRenderer[] skinnedRenderers =
                instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            int vertices = 0;
            int triangles = 0;
            int materialSlots = 0;
            string materialName = "None";
            for (int i = 0; i < renderers.Length; i++)
            {
                materialSlots += renderers[i].sharedMaterials.Length;
                if (materialName == "None" &&
                    renderers[i].sharedMaterial != null)
                {
                    materialName = renderers[i].sharedMaterial.name;
                }
            }
            for (int i = 0; i < skinnedRenderers.Length; i++)
            {
                Mesh mesh = skinnedRenderers[i].sharedMesh;
                if (mesh == null)
                    continue;
                vertices += mesh.vertexCount;
                for (int subMesh = 0;
                     subMesh < mesh.subMeshCount;
                     subMesh++)
                {
                    triangles += (int)mesh.GetIndexCount(subMesh) / 3;
                }
            }

            float finalHeight = 0f;
            if (TryMeasureRenderersInSpace(
                    renderers,
                    instance.transform.parent != null
                        ? instance.transform.parent
                        : instance.transform,
                    out Bounds bounds))
            {
                finalHeight = bounds.size.y;
            }

            float scale = sourceHeight > 0.0001f
                ? targetHeight / sourceHeight
                : 1f;
            Debug.Log(
                $"[PitRacerRetarget] {actorName} sourceHeight={sourceHeight:F3}m finalHeight={finalHeight:F3}m uniformScale={scale:F4} renderers={renderers.Length} skinnedMeshes={skinnedRenderers.Length} vertices={vertices} triangles={triangles} materialSlots={materialSlots} sharedMaterial={materialName}");
        }

        private WheelServiceCorner CreateWheelServiceCorner(
            string cornerName,
            Transform wheel,
            Transform vehicleSpace,
            float localToPhysical,
            bool isFront)
        {
            Renderer[] wheelRenderers =
                wheel.GetComponentsInChildren<Renderer>(true);
            if (wheelRenderers.Length == 0 ||
                !TryMeasureRenderersInSpace(
                    wheelRenderers,
                    vehicleSpace,
                    out Bounds wheelBounds))
            {
                return null;
            }

            WheelServiceCorner corner = new()
            {
                Name = cornerName,
                IsFront = isFront,
                Hub = wheelBounds.center * localToPhysical,
                TyreDiameter = Mathf.Max(
                    wheelBounds.size.y,
                    wheelBounds.size.z) * localToPhysical,
                TyreThickness =
                    wheelBounds.size.x * localToPhysical,
                OriginalWheel = wheel,
                OriginalWheelRenderers = wheelRenderers,
                OriginalWheelRendererStates =
                    new bool[wheelRenderers.Length]
            };
            corner.Outward = Mathf.Abs(corner.Hub.x) > 0.0001f
                ? Vector3.right * Mathf.Sign(corner.Hub.x)
                : Vector3.right;
            corner.HubRotation = Quaternion.Inverse(origin.rotation) *
                wheel.rotation;
            for (int i = 0; i < wheelRenderers.Length; i++)
            {
                corner.OriginalWheelRendererStates[i] =
                    wheelRenderers[i] != null && wheelRenderers[i].enabled;
            }

            CreateAnchor($"{cornerName}_Hub", corner.Hub);
            Vector3 ground = new(corner.Hub.x, 0f, corner.Hub.z);
            Vector3 serviceDirection = isFront
                ? Vector3.forward
                : Vector3.back;
            Vector3 gunnerPosition = ground +
                corner.Outward * corner.TyreDiameter * 1.05f;
            Vector3 wheelOffPosition = ground +
                corner.Outward * corner.TyreDiameter * 1.18f +
                serviceDirection * corner.TyreDiameter * 1.26f;
            Vector3 wheelOnPosition = ground +
                corner.Outward * corner.TyreDiameter * 1.62f -
                serviceDirection * corner.TyreDiameter * 1.08f;
            string side = corner.Outward.x >= 0f ? "L" : "R";
            corner.Gunner = CreateActor(
                $"{cornerName}_WheelGunner",
                gunnerPosition,
                ResolveGroundFacing(gunnerPosition, ground),
                lockMotionRoot: true);
            corner.WheelOff = CreateActor(
                $"{cornerName}_WheelOff_{side}",
                wheelOffPosition,
                ResolveGroundFacing(wheelOffPosition, ground),
                lockMotionRoot: true);
            corner.WheelOn = CreateActor(
                $"{cornerName}_WheelOn_{side}",
                wheelOnPosition,
                ResolveGroundFacing(wheelOnPosition, ground),
                lockMotionRoot: true);
            if (corner.Gunner == null ||
                corner.WheelOff == null ||
                corner.WheelOn == null)
            {
                return null;
            }

            corner.GunnerRightHand = corner.Gunner.GetBoneTransform(
                    HumanBodyBones.RightHand) ??
                FindDescendant(corner.Gunner.Root, "hand_r");
            corner.WheelOffLeftHand = corner.WheelOff.GetBoneTransform(
                    HumanBodyBones.LeftHand) ??
                FindDescendant(corner.WheelOff.Root, "hand_l");
            corner.WheelOffRightHand = corner.WheelOff.GetBoneTransform(
                    HumanBodyBones.RightHand) ??
                FindDescendant(corner.WheelOff.Root, "hand_r");
            corner.WheelOnLeftHand = corner.WheelOn.GetBoneTransform(
                    HumanBodyBones.LeftHand) ??
                FindDescendant(corner.WheelOn.Root, "hand_l");
            corner.WheelOnRightHand = corner.WheelOn.GetBoneTransform(
                    HumanBodyBones.RightHand) ??
                FindDescendant(corner.WheelOn.Root, "hand_r");
            if (corner.GunnerRightHand == null ||
                corner.WheelOffLeftHand == null ||
                corner.WheelOffRightHand == null ||
                corner.WheelOnLeftHand == null ||
                corner.WheelOnRightHand == null)
            {
                return null;
            }

            corner.OldLooseTyre = CreateProp(
                wheel.gameObject,
                $"{cornerName}_OldLooseTyre");
            corner.NewLooseTyre = CreateProp(
                wheel.gameObject,
                $"{cornerName}_NewLooseTyre");
            corner.WheelGun = CreateProp(
                assets.WheelGunPrefab,
                $"{cornerName}_WheelGun");
            corner.WheelGunGrip = corner.WheelGun != null
                ? FindDescendant(corner.WheelGun.transform, "GripAnchor")
                : null;
            if (corner.OldLooseTyre == null ||
                corner.NewLooseTyre == null ||
                corner.WheelGun == null ||
                corner.WheelGunGrip == null)
            {
                return null;
            }

            NormalizePropDiameter(
                corner.OldLooseTyre,
                corner.TyreDiameter);
            NormalizePropDiameter(
                corner.NewLooseTyre,
                corner.TyreDiameter);
            corner.OldLooseTyreVisualCenter =
                ResolveRendererCenterInObjectSpace(corner.OldLooseTyre);
            corner.NewLooseTyreVisualCenter =
                ResolveRendererCenterInObjectSpace(corner.NewLooseTyre);
            CalibrateCorner(corner);
            CreateAnchor(
                $"{cornerName}_WheelGunner_Service",
                corner.Gunner.Root.localPosition);
            CreateAnchor(
                $"{cornerName}_WheelOff_Service",
                corner.WheelOff.Root.localPosition);
            CreateAnchor(
                $"{cornerName}_WheelOn_Service",
                corner.WheelOn.Root.localPosition);
            return corner;
        }

        private static void ApplyFerrariCrewAppearance(
            string actorName,
            GameObject instance)
        {
            if (instance == null ||
                !IsTyreServiceActor(actorName))
            {
                return;
            }

            Renderer[] renderers =
                instance.GetComponentsInChildren<Renderer>(true);
            Color roleColor = actorName.Contains("_WheelGunner")
                ? FerrariWheelGunnerRed
                : actorName.Contains("_WheelOn_")
                    ? FerrariWheelOnRed
                    : FerrariWheelOffRed;
            float roleSmoothness = actorName.Contains("_WheelOn_")
                ? 0.24f
                : 0.28f;
            MaterialPropertyBlock properties = new();
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                renderer.GetPropertyBlock(properties);
                properties.SetColor(BaseColorId, roleColor);
                properties.SetColor(ColorId, roleColor);
                properties.SetFloat(MetallicId, 0f);
                properties.SetFloat(SmoothnessId, roleSmoothness);
                renderer.SetPropertyBlock(properties);
                properties.Clear();
            }
        }

        private static bool IsTyreServiceActor(string actorName)
        {
            bool validCorner =
                actorName.StartsWith("FL_") ||
                actorName.StartsWith("FR_") ||
                actorName.StartsWith("RL_") ||
                actorName.StartsWith("RR_");
            return validCorner &&
                (actorName.Contains("_WheelGunner") ||
                 actorName.Contains("_WheelOff_") ||
                 actorName.Contains("_WheelOn_"));
        }

        private AnimationClip ResolveWheelOffClip(SampledActor actor)
        {
            if (!useRacerTyreServiceCrew)
                return assets.WheelOffFullL;

            return IsRightSideActor(actor)
                ? assets.RacerWheelOffHumanoidFullR
                : assets.RacerWheelOffHumanoidFullL;
        }

        private AnimationClip ResolveWheelOnClip(SampledActor actor)
        {
            if (!useRacerTyreServiceCrew)
                return assets.WheelOnFullL;

            return IsRightSideActor(actor)
                ? assets.RacerWheelOnHumanoidFullR
                : assets.RacerWheelOnHumanoidFullL;
        }

        private static bool IsRightSideActor(SampledActor actor)
        {
            return actor?.Root != null &&
                   actor.Root.name.EndsWith("_R");
        }

        private static void SetActorPresentationVisible(
            SampledActor actor,
            bool visible)
        {
            Renderer[] renderers =
                actor.Root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].enabled = visible;
        }

        private CrewTransition CreateCrewTransition(
            SampledActor actor,
            float ingressStart,
            float ingressEnd,
            float egressStart,
            float egressEnd,
            float carriedPropClearance = CrewClearancePaddingMeters)
        {
            return CreateCrewTransition(
                actor,
                ingressStart,
                ingressEnd,
                egressStart,
                egressEnd,
                flOutward,
                tyreDiameterMeters,
                carriedPropClearance);
        }

        private CrewTransition CreateCrewTransition(
            SampledActor actor,
            float ingressStart,
            float ingressEnd,
            float egressStart,
            float egressEnd,
            Vector3 outward,
            float tyreDiameter,
            float carriedPropClearance)
        {
            Vector3 servicePosition = actor.Root.localPosition;
            float outwardSign = Mathf.Sign(outward.x);
            float outwardDistance = tyreDiameter * 0.45f;
            Renderer[] renderers =
                actor.Root.GetComponentsInChildren<Renderer>(true);
            if (TryMeasureRenderersInSpace(
                    renderers,
                    origin,
                    out Bounds bounds))
            {
                float targetInnerEdge = outwardSign < 0f
                    ? vehicleCorridorMinX - carriedPropClearance
                    : vehicleCorridorMaxX + carriedPropClearance;
                float requiredDistance = outwardSign < 0f
                    ? bounds.max.x - targetInnerEdge
                    : targetInnerEdge - bounds.min.x;
                outwardDistance = Mathf.Max(
                    outwardDistance,
                    requiredDistance);
            }

            return new CrewTransition
            {
                Actor = actor,
                ServicePosition = servicePosition,
                StandbyPosition = servicePosition +
                    outward * outwardDistance,
                IngressStart = ingressStart,
                IngressEnd = ingressEnd,
                EgressStart = egressStart,
                EgressEnd = egressEnd
            };
        }

        private void SetCrewServicePositions()
        {
            if (crewTransitions == null)
                return;

            for (int i = 0; i < crewTransitions.Length; i++)
            {
                CrewTransition transition = crewTransitions[i];
                transition.Actor.Root.localPosition =
                    transition.ServicePosition;
            }
        }

        private void ApplyCrewTransitions(float time)
        {
            if (crewTransitions == null)
                return;

            for (int i = 0; i < crewTransitions.Length; i++)
            {
                CrewTransition transition = crewTransitions[i];
                float ingress = Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(
                        transition.IngressStart,
                        transition.IngressEnd,
                        time));
                float egress = Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(
                        transition.EgressStart,
                        transition.EgressEnd,
                        time));
                Vector3 ingressPosition = Vector3.Lerp(
                    transition.StandbyPosition,
                    transition.ServicePosition,
                    ingress);
                transition.Actor.Root.localPosition = Vector3.Lerp(
                    ingressPosition,
                    transition.StandbyPosition,
                    egress);
            }
        }

        private GameObject CreateProp(GameObject prefab, string name)
        {
            if (prefab == null)
                return null;

            GameObject instance = Object.Instantiate(prefab, propRoot);
            instance.name = name;
            DisablePhysics(instance, keepAnimator: false);
            return instance;
        }

        private void CalibrateGunner()
        {
            gunner.SampleNormalized(
                WheelGunnerClip,
                GunnerContactNormalized);
            Vector3 hand = origin.InverseTransformPoint(
                gunnerRightHand.position);
            Vector3 facing =
                gunner.Root.localRotation * Vector3.forward;
            Vector3 target = flHub -
                facing * EstimateWheelGunReach();
            Vector3 correction = target - hand;
            correction.y = 0f;
            gunner.Root.localPosition += correction;
        }

        private void CalibrateCorner(WheelServiceCorner corner)
        {
            corner.Gunner.SampleNormalized(
                WheelGunnerClip,
                GunnerContactNormalized);
            Vector3 hand = origin.InverseTransformPoint(
                corner.GunnerRightHand.position);
            Vector3 facing =
                corner.Gunner.Root.localRotation * Vector3.forward;
            Vector3 target = corner.Hub -
                facing * EstimateWheelGunReach(
                    corner.WheelGun,
                    corner.WheelGunGrip);
            Vector3 correction = target - hand;
            correction.y = 0f;
            corner.Gunner.Root.localPosition += correction;

            corner.WheelOff.SampleNormalized(
                ResolveWheelOffClip(corner.WheelOff),
                WheelOffOwnershipNormalized);
            Vector3 wheelOffHands = ResolveHandMidpoint(
                corner.WheelOffLeftHand,
                corner.WheelOffRightHand,
                corner.Hub);
            correction = corner.Hub - wheelOffHands;
            correction.y = 0f;
            corner.WheelOff.Root.localPosition += correction;

            corner.WheelOn.SampleNormalized(
                ResolveWheelOnClip(corner.WheelOn),
                WheelOnHandoffNormalized);
            Vector3 wheelOnHands = ResolveHandMidpoint(
                corner.WheelOnLeftHand,
                corner.WheelOnRightHand,
                corner.Hub);
            correction = corner.Hub - wheelOnHands;
            correction.y = 0f;
            corner.WheelOn.Root.localPosition += correction;

            ApplyWheelServiceLaneSpacing(
                corner.Name,
                corner.Outward,
                corner.IsFront ? Vector3.forward : Vector3.back,
                corner.TyreDiameter,
                corner.WheelOff,
                corner.WheelOn);
        }

        private static void ApplyWheelServiceLaneSpacing(
            string cornerName,
            Vector3 outward,
            Vector3 serviceDirection,
            float tyreDiameter,
            SampledActor wheelOffActor,
            SampledActor wheelOnActor)
        {
            float wheelOffOutward;
            float wheelOffLongitudinal;
            float wheelOnOutward;
            float wheelOnLongitudinal;
            if (cornerName == "FL")
            {
                wheelOffOutward = 0.12f;
                wheelOffLongitudinal = 0.26f;
                wheelOnOutward = 0.08f;
                wheelOnLongitudinal = 0.12f;
            }
            else if (cornerName == "RL")
            {
                wheelOffOutward = 0.24f;
                wheelOffLongitudinal = 0.30f;
                wheelOnOutward = 0.22f;
                wheelOnLongitudinal = 0.34f;
            }
            else
            {
                return;
            }

            wheelOffActor.Root.localPosition +=
                outward * tyreDiameter * wheelOffOutward +
                serviceDirection * tyreDiameter *
                wheelOffLongitudinal;
            wheelOnActor.Root.localPosition +=
                outward * tyreDiameter * wheelOnOutward -
                serviceDirection * tyreDiameter *
                wheelOnLongitudinal;
        }

        private void CalibrateWheelOffContact()
        {
            wheelOff.SampleNormalized(
                ResolveWheelOffClip(wheelOff),
                WheelOffOwnershipNormalized);
            Vector3 hands = ResolveHandMidpoint(
                wheelOffLeftHand,
                wheelOffRightHand,
                flHub);
            Vector3 correction = flHub - hands;
            correction.y = 0f;
            wheelOff.Root.localPosition += correction;
        }

        private void CalibrateWheelOnContact()
        {
            wheelOn.SampleNormalized(
                ResolveWheelOnClip(wheelOn),
                WheelOnHandoffNormalized);
            Vector3 hands = ResolveHandMidpoint(
                wheelOnLeftHand,
                wheelOnRightHand,
                flHub);
            Vector3 correction = flHub - hands;
            correction.y = 0f;
            wheelOn.Root.localPosition += correction;
        }

        private static Vector3 ResolveGroundFacing(
            Vector3 position,
            Vector3 target)
        {
            Vector3 direction = target - position;
            direction.y = 0f;
            return direction.sqrMagnitude > 0.000001f
                ? direction.normalized
                : Vector3.forward;
        }

        private float EstimateWheelGunReach()
        {
            return EstimateWheelGunReach(wheelGun, wheelGunGrip);
        }

        private static float EstimateWheelGunReach(
            GameObject gun,
            Transform gripAnchor)
        {
            Renderer[] renderers =
                gun.GetComponentsInChildren<Renderer>(true);
            if (!TryMeasureRenderersInSpace(
                    renderers,
                    gun.transform,
                    out Bounds bounds))
            {
                return 0.32f;
            }

            Vector3 grip = gun.transform.InverseTransformPoint(
                gripAnchor.position);
            float forwardReach = bounds.max.z - grip.z;
            if (forwardReach <= 0.08f)
            {
                forwardReach = Mathf.Max(
                    bounds.size.x,
                    Mathf.Max(bounds.size.y, bounds.size.z)) * 0.5f;
            }
            return Mathf.Clamp(forwardReach, 0.18f, 0.48f);
        }

        private void ApplyReadyPose()
        {
            SampleWheelGunner(
                gunner,
                FindCrewTransition(gunner),
                0f,
                0f);
            wheelOff.SampleNormalized(ResolveWheelOffClip(wheelOff), 0f);
            wheelOn.SampleNormalized(ResolveWheelOnClip(wheelOn), 0f);
            SampleAdditionalCorners(0f, 0f, 0f, 0f);
            frontJack.SampleNormalized(assets.FrontJackFullL, 0f);
            rearJack.SampleNormalized(assets.RearJackFullR, 0f);
            pitSignal.SampleNormalized(assets.PitSignalFullR, 0f);
            ApplyWheelState(0f, 0f);
            ApplyWheelGun();
            ApplyAdditionalCornerStates(0f, 0f);
            ApplyAdditionalWheelGuns();
        }

        private CrewTransition FindCrewTransition(SampledActor actor)
        {
            if (actor == null || crewTransitions == null)
                return null;

            for (int i = 0; i < crewTransitions.Length; i++)
            {
                if (crewTransitions[i].Actor == actor)
                    return crewTransitions[i];
            }

            return null;
        }

        private void ApplyAdditionalStaticComposition()
        {
            if (additionalCorners == null)
                return;

            for (int i = 0; i < additionalCorners.Length; i++)
            {
                WheelServiceCorner corner = additionalCorners[i];
                corner.Gunner.SampleNormalized(
                    WheelGunnerClip,
                    GunnerContactNormalized);
                corner.WheelOff.SampleNormalized(
                    ResolveWheelOffClip(corner.WheelOff),
                    StaticWheelOffPoseNormalized);
                corner.WheelOn.SampleNormalized(
                    ResolveWheelOnClip(corner.WheelOn),
                    StaticWheelOnPoseNormalized);
                SetCornerOriginalWheelVisible(corner, true);
                corner.OldLooseTyre.SetActive(false);
                corner.NewLooseTyre.SetActive(true);
                SetTyreVisualPose(
                    corner.NewLooseTyre,
                    corner.NewLooseTyreVisualCenter,
                    ResolveTyreGripCenter(
                        corner.WheelOn,
                        corner.WheelOnLeftHand,
                        corner.WheelOnRightHand,
                        corner.Hub,
                        corner.Outward,
                        corner.TyreDiameter,
                        corner.TyreThickness),
                    corner.HubRotation);
                ApplyCornerWheelGun(corner);
            }
        }

        private void ApplyAdditionalCorners(
            float time,
            float gunnerProgress,
            float wheelOffProgress,
            float wheelOnProgress)
        {
            SampleAdditionalCorners(
                time,
                gunnerProgress,
                wheelOffProgress,
                wheelOnProgress);
            ApplyAdditionalCornerStates(
                wheelOffProgress,
                wheelOnProgress);
            ApplyAdditionalWheelGuns();
        }

        private void SampleAdditionalCorners(
            float time,
            float gunnerProgress,
            float wheelOffProgress,
            float wheelOnProgress)
        {
            if (additionalCorners == null)
                return;

            for (int i = 0; i < additionalCorners.Length; i++)
            {
                WheelServiceCorner corner = additionalCorners[i];
                SampleWheelGunner(
                    corner.Gunner,
                    FindCrewTransition(corner.Gunner),
                    time,
                    gunnerProgress);
                corner.WheelOff.SampleNormalized(
                    ResolveWheelOffClip(corner.WheelOff),
                    wheelOffProgress);
                corner.WheelOn.SampleNormalized(
                    ResolveWheelOnClip(corner.WheelOn),
                    wheelOnProgress);
            }
        }

        private void ApplyAdditionalCornerStates(
            float wheelOffProgress,
            float wheelOnProgress)
        {
            if (additionalCorners == null)
                return;

            for (int i = 0; i < additionalCorners.Length; i++)
            {
                ApplyCornerWheelState(
                    additionalCorners[i],
                    wheelOffProgress,
                    wheelOnProgress);
            }
        }

        private void ApplyCornerWheelState(
            WheelServiceCorner corner,
            float wheelOffProgress,
            float wheelOnProgress)
        {
            bool oldTyreOwned =
                wheelOffProgress >= WheelOffOwnershipNormalized;
            bool replacementMounted =
                wheelOnProgress >= WheelOnHandoffNormalized;
            Quaternion mountedRotation =
                ResolveCornerMountedTyreRotation(corner);
            float removalBlend = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    WheelOffOwnershipNormalized,
                    WheelOffClearNormalized,
                    wheelOffProgress));
            float installationBlend = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    0f,
                    WheelOnHandoffNormalized,
                    wheelOnProgress));

            SetCornerOriginalWheelVisible(
                corner,
                !oldTyreOwned || replacementMounted);
            corner.OldLooseTyre.SetActive(oldTyreOwned);
            corner.NewLooseTyre.SetActive(!replacementMounted);

            if (oldTyreOwned)
            {
                Vector3 carryCenter = ResolveTyreGripCenter(
                    corner.WheelOff,
                    corner.WheelOffLeftHand,
                    corner.WheelOffRightHand,
                    corner.Hub,
                    corner.Outward,
                    corner.TyreDiameter,
                    corner.TyreThickness);
                SetTyreVisualPose(
                    corner.OldLooseTyre,
                    corner.OldLooseTyreVisualCenter,
                    Vector3.Lerp(
                        corner.Hub,
                        carryCenter,
                        removalBlend),
                    Quaternion.Slerp(
                        mountedRotation,
                        corner.HubRotation,
                        removalBlend));
            }

            if (!replacementMounted)
            {
                Vector3 carryCenter = ResolveTyreGripCenter(
                    corner.WheelOn,
                    corner.WheelOnLeftHand,
                    corner.WheelOnRightHand,
                    corner.Hub,
                    corner.Outward,
                    corner.TyreDiameter,
                    corner.TyreThickness);
                SetTyreVisualPose(
                    corner.NewLooseTyre,
                    corner.NewLooseTyreVisualCenter,
                    Vector3.Lerp(
                        carryCenter,
                        corner.Hub,
                        installationBlend),
                    Quaternion.Slerp(
                        corner.HubRotation,
                        mountedRotation,
                        installationBlend));
            }
        }

        private void ApplyAdditionalWheelGuns()
        {
            if (additionalCorners == null)
                return;

            for (int i = 0; i < additionalCorners.Length; i++)
                ApplyCornerWheelGun(additionalCorners[i]);
        }

        private void ApplyCornerWheelGun(WheelServiceCorner corner)
        {
            corner.WheelGun.SetActive(true);
            Vector3 hubWorld = origin.TransformPoint(corner.Hub);
            Vector3 direction =
                hubWorld - corner.GunnerRightHand.position;
            if (direction.sqrMagnitude <= 0.000001f)
                direction = -origin.TransformDirection(corner.Outward);

            corner.WheelGun.transform.SetPositionAndRotation(
                corner.GunnerRightHand.position,
                Quaternion.LookRotation(
                    direction.normalized,
                    origin.up));
            if (corner.WheelGunGrip != corner.WheelGun.transform)
            {
                Vector3 gripOffset =
                    corner.WheelGunGrip.position -
                    corner.WheelGun.transform.position;
                corner.WheelGun.transform.position -= gripOffset;
            }
        }

        private Quaternion ResolveCornerMountedTyreRotation(
            WheelServiceCorner corner)
        {
            return corner.OriginalWheel != null && origin != null
                ? Quaternion.Inverse(origin.rotation) *
                  corner.OriginalWheel.rotation
                : corner.HubRotation;
        }

        private void ApplyWheelState(
            float wheelOffProgress,
            float wheelOnProgress)
        {
            bool oldTyreOwned =
                wheelOffProgress >= WheelOffOwnershipNormalized;
            bool replacementMounted =
                wheelOnProgress >= WheelOnHandoffNormalized;
            Quaternion mountedRotation = ResolveMountedTyreRotation();
            float removalBlend = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    WheelOffOwnershipNormalized,
                    WheelOffClearNormalized,
                    wheelOffProgress));
            float installationBlend = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    0f,
                    WheelOnHandoffNormalized,
                    wheelOnProgress));

            SetOriginalFlWheelVisible(
                !oldTyreOwned || replacementMounted);
            oldLooseTyre.SetActive(oldTyreOwned);
            newLooseTyre.SetActive(!replacementMounted);

            if (oldTyreOwned)
            {
                Vector3 carryCenter = ResolveTyreGripCenter(
                    wheelOff,
                    wheelOffLeftHand,
                    wheelOffRightHand,
                    flHub,
                    flOutward,
                    tyreDiameterMeters,
                    tyreThicknessMeters);
                SetTyreVisualPose(
                    oldLooseTyre,
                    oldLooseTyreVisualCenter,
                    Vector3.Lerp(
                        flHub,
                        carryCenter,
                        removalBlend),
                    Quaternion.Slerp(
                        mountedRotation,
                        flHubRotation,
                        removalBlend));
            }

            if (!replacementMounted)
            {
                Vector3 carryCenter = ResolveTyreGripCenter(
                    wheelOn,
                    wheelOnLeftHand,
                    wheelOnRightHand,
                    flHub,
                    flOutward,
                    tyreDiameterMeters,
                    tyreThicknessMeters);
                SetTyreVisualPose(
                    newLooseTyre,
                    newLooseTyreVisualCenter,
                    Vector3.Lerp(
                        carryCenter,
                        flHub,
                        installationBlend),
                    Quaternion.Slerp(
                        flHubRotation,
                        mountedRotation,
                        installationBlend));
            }
        }

        private void SetTyreVisualCenter(
            GameObject tyre,
            Vector3 visualCenterInTyre,
            Vector3 desiredCenter)
        {
            SetTyreVisualPose(
                tyre,
                visualCenterInTyre,
                desiredCenter,
                flHubRotation);
        }

        private void SetTyreVisualPose(
            GameObject tyre,
            Vector3 visualCenterInTyre,
            Vector3 desiredCenter,
            Quaternion desiredRotation)
        {
            Transform tyreTransform = tyre.transform;
            tyreTransform.localPosition = desiredCenter;
            tyreTransform.localRotation = desiredRotation;
            Vector3 currentCenter = origin.InverseTransformPoint(
                tyreTransform.TransformPoint(visualCenterInTyre));
            tyreTransform.localPosition += desiredCenter - currentCenter;
        }

        private Quaternion ResolveMountedTyreRotation()
        {
            return originalFlWheel != null && origin != null
                ? Quaternion.Inverse(origin.rotation) *
                  originalFlWheel.rotation
                : flHubRotation;
        }

        private static Vector3 ResolveRendererCenterInObjectSpace(
            GameObject instance)
        {
            Renderer[] renderers =
                instance.GetComponentsInChildren<Renderer>(true);
            if (!TryMeasureRenderersInSpace(
                    renderers,
                    instance.transform,
                    out Bounds bounds))
            {
                return Vector3.zero;
            }

            return bounds.center;
        }

        private Vector3 ResolveTyreGripCenter(
            SampledActor actor,
            Transform leftHand,
            Transform rightHand,
            Vector3 fallback,
            Vector3 outward,
            float tyreDiameter,
            float tyreThickness)
        {
            Vector3 handCenter = ResolveHandMidpoint(
                leftHand,
                rightHand,
                fallback);
            if (actor?.Root == null || origin == null)
                return handCenter;

            Vector3 actorCenter = origin.InverseTransformPoint(
                actor.Root.position);
            float renderedHalfDepth = tyreDiameter * 0.28f;
            Renderer[] renderers =
                actor.Root.GetComponentsInChildren<Renderer>(true);
            if (TryMeasureRenderersInSpace(
                    renderers,
                    origin,
                    out Bounds actorBounds))
            {
                Vector3 extents = actorBounds.extents;
                renderedHalfDepth =
                    Mathf.Abs(outward.x) * extents.x +
                    Mathf.Abs(outward.y) * extents.y +
                    Mathf.Abs(outward.z) * extents.z;
            }

            float torsoHalfDepth = Mathf.Clamp(
                renderedHalfDepth * 0.5f,
                0.12f,
                tyreDiameter * 0.34f);
            float tyreHalfThickness = Mathf.Max(
                tyreThickness * 0.5f,
                tyreDiameter * 0.08f);
            float bodyClearProjection =
                Vector3.Dot(actorCenter, outward) -
                torsoHalfDepth -
                tyreHalfThickness -
                CarriedTyreBodyGapMeters;
            float handProjection =
                Vector3.Dot(handCenter, outward);
            float correction = Mathf.Min(
                0f,
                bodyClearProjection - handProjection);
            correction = Mathf.Max(
                correction,
                -tyreDiameter * 0.52f);
            return handCenter + outward * correction;
        }

        private Vector3 ResolveHandMidpoint(
            Transform leftHand,
            Transform rightHand,
            Vector3 fallback)
        {
            if (leftHand == null || rightHand == null || origin == null)
                return fallback;

            return origin.InverseTransformPoint(
                Vector3.Lerp(
                    leftHand.position,
                    rightHand.position,
                    0.5f));
        }

        private void ApplyWheelGun()
        {
            wheelGun.SetActive(true);
            if (gunnerRightHand == null)
                return;

            Vector3 hubWorld = origin.TransformPoint(flHub);
            Vector3 direction = hubWorld - gunnerRightHand.position;
            if (direction.sqrMagnitude <= 0.000001f)
                direction = -origin.right;

            wheelGun.transform.SetPositionAndRotation(
                gunnerRightHand.position,
                Quaternion.LookRotation(
                    direction.normalized,
                    origin.up));
            if (wheelGunGrip != wheelGun.transform)
            {
                Vector3 gripOffset =
                    wheelGunGrip.position - wheelGun.transform.position;
                wheelGun.transform.position -= gripOffset;
            }
        }

        private void SetOriginalFlWheelVisible(bool visible)
        {
            if (originalFlWheelRenderers == null ||
                originalFlWheelRendererStates == null)
            {
                return;
            }

            for (int i = 0; i < originalFlWheelRenderers.Length; i++)
            {
                Renderer renderer = originalFlWheelRenderers[i];
                if (renderer != null)
                {
                    renderer.enabled = visible &&
                        originalFlWheelRendererStates[i];
                }
            }
        }

        private static void SetCornerOriginalWheelVisible(
            WheelServiceCorner corner,
            bool visible)
        {
            if (corner?.OriginalWheelRenderers == null ||
                corner.OriginalWheelRendererStates == null)
            {
                return;
            }

            for (int i = 0;
                 i < corner.OriginalWheelRenderers.Length;
                 i++)
            {
                Renderer renderer = corner.OriginalWheelRenderers[i];
                if (renderer != null)
                {
                    renderer.enabled = visible &&
                        corner.OriginalWheelRendererStates[i];
                }
            }
        }

        private void CreateAnchor(string name, Vector3 localPosition)
        {
            Transform anchor = CreateRoot(name, anchorRoot);
            anchor.localPosition = localPosition;
        }

        private static Transform CreateRoot(string name, Transform parent)
        {
            Transform child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static Transform FindDescendant(
            Transform root,
            string name)
        {
            if (root == null)
                return null;
            if (string.Equals(
                    root.name,
                    name,
                    System.StringComparison.OrdinalIgnoreCase))
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform match = FindDescendant(root.GetChild(i), name);
                if (match != null)
                    return match;
            }
            return null;
        }

        private static bool TryMeasureRenderersInSpace(
            Renderer[] renderers,
            Transform referenceSpace,
            out Bounds bounds)
        {
            bounds = default;
            if (renderers == null || referenceSpace == null)
                return false;

            bool hasBounds = false;
            for (int rendererIndex = 0;
                 rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                if (renderer == null)
                    continue;

                Bounds localBounds = renderer.localBounds;
                Vector3 min = localBounds.min;
                Vector3 max = localBounds.max;
                for (int cornerIndex = 0;
                     cornerIndex < 8;
                     cornerIndex++)
                {
                    Vector3 localCorner = new(
                        (cornerIndex & 1) == 0 ? min.x : max.x,
                        (cornerIndex & 2) == 0 ? min.y : max.y,
                        (cornerIndex & 4) == 0 ? min.z : max.z);
                    Vector3 point = referenceSpace.InverseTransformPoint(
                        renderer.transform.TransformPoint(localCorner));
                    if (!hasBounds)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(point);
                    }
                }
            }

            return hasBounds;
        }

        private static void DisablePhysics(
            GameObject instance,
            bool keepAnimator)
        {
            Collider[] colliders =
                instance.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
                colliders[i].enabled = false;

            Rigidbody[] bodies =
                instance.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < bodies.Length; i++)
            {
                bodies[i].isKinematic = true;
                bodies[i].useGravity = false;
                bodies[i].detectCollisions = false;
            }

            Behaviour[] behaviours =
                instance.GetComponentsInChildren<Behaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (keepAnimator && behaviours[i] is Animator)
                    continue;
                behaviours[i].enabled = false;
            }
        }

        private void NormalizePropDiameter(
            GameObject instance,
            float diameter)
        {
            Renderer[] renderers =
                instance.GetComponentsInChildren<Renderer>(true);
            if (!TryMeasureRenderersInSpace(
                    renderers,
                    origin,
                    out Bounds bounds))
            {
                return;
            }

            float currentDiameter = Mathf.Max(
                bounds.size.x,
                Mathf.Max(bounds.size.y, bounds.size.z));
            if (currentDiameter <= 0.0001f)
                return;

            instance.transform.localScale *= diameter / currentDiameter;
        }

        private static float NormalizeActorHeight(
            GameObject instance,
            float targetHeight)
        {
            if (instance == null || targetHeight <= 0.0001f)
                return 0f;

            Renderer[] renderers =
                instance.GetComponentsInChildren<Renderer>(true);
            if (!TryMeasureRenderersInSpace(
                    renderers,
                    instance.transform,
                    out Bounds bounds) ||
                bounds.size.y <= 0.0001f)
            {
                return 0f;
            }

            instance.transform.localScale *= targetHeight / bounds.size.y;
            return bounds.size.y;
        }
    }
}
