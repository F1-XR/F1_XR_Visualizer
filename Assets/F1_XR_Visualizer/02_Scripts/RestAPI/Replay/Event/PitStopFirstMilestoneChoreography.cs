using UnityEngine;
using Object = UnityEngine.Object;

namespace F1XR.RestAPI.Replay
{
    internal sealed class PitStopFirstMilestoneChoreography
    {
        private const string BaseClipName = "AS_Wheel_Gunner_Idle";
        private static readonly int BaseStateHash =
            Animator.StringToHash("Base Layer.AS_Wheel_Gunner_Idle");

        internal const float ReleaseReadyTime = 2.8f;
        private const float FullScaleVehicleLengthMeters = 5.6f;
        private const float FullScaleCrewHeightMeters = 1.78f;
        private const float FallbackTyreDiameterMeters = 0.72f;
        private const float WheelOffStartTime = 0.15f;
        private const float WheelOffDuration = 2.25f;
        private const float WheelOnStartTime = 0.1f;
        private const float WheelOnDuration = 2.55f;
        private const float SignalLeadTime = 0.3f;
        private const float SignalDuration = 3.1f;
        private const float GunnerContactNormalized = 0.25f;
        private const float WheelOffOwnershipNormalized = 0.42f;
        private const float WheelOnHandoffNormalized = 0.62f;
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
            private AnimationClip activeClip;
            private float sampledNormalizedTime = -1f;

            public SampledActor(
                Transform root,
                Animator animator,
                RuntimeAnimatorController baseController)
            {
                Root = root;
                this.animator = animator;
                motionRoot = animator.transform;
                motionRootLocalPosition = motionRoot.localPosition;
                motionRootLocalRotation = motionRoot.localRotation;
                controller = new AnimatorOverrideController(baseController);
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = true;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.updateMode = AnimatorUpdateMode.Normal;
                animator.speed = 0f;
                animator.Update(0f);
            }

            public Transform Root { get; }

            public void Dispose()
            {
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

                sampledNormalizedTime = normalizedTime;
            }

            private void ResetToClipStart()
            {
                motionRoot.localPosition = motionRootLocalPosition;
                motionRoot.localRotation = motionRootLocalRotation;
                animator.Rebind();
                animator.Play(BaseStateHash, 0, 0f);
                animator.Update(0f);
                sampledNormalizedTime = 0f;
            }
        }
        private Vector3 flHub = FallbackFlHub;
        private Quaternion flHubRotation = Quaternion.identity;
        private float tyreDiameterMeters = FallbackTyreDiameterMeters;

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
        private Transform gunnerRightHand;
        private Transform wheelOffLeftHand;
        private Transform wheelOffRightHand;
        private Transform wheelOnLeftHand;
        private Transform wheelOnRightHand;
        private Vector3 wheelOffTyreGripOffset;
        private Vector3 wheelOnTyreGripOffset;
        private GameObject oldLooseTyre;
        private GameObject newLooseTyre;
        private Vector3 oldLooseTyreVisualCenter;
        private Vector3 newLooseTyreVisualCenter;
        private Renderer[] originalFlWheelRenderers;
        private bool[] originalFlWheelRendererStates;

        public bool ReleaseReady { get; private set; }

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
            if (flWheel == null)
                return false;

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
            }
            else
            {
                flHub = FallbackFlHub;
                tyreDiameterMeters = FallbackTyreDiameterMeters;
            }

            float vehicleFront = hasVehicleBounds
                ? vehicleBounds.max.z * localToPhysical
                : flHub.z + 1.15f;
            float vehicleRear = hasVehicleBounds
                ? vehicleBounds.min.z * localToPhysical
                : FallbackRearHub.z - 1.15f;

            Animator prefabAnimator =
                profile.PitCrewPrefab.GetComponentInChildren<Animator>(true);
            Transform prefabRightHand = FindDescendant(
                profile.PitCrewPrefab.transform,
                "hand_r");
            if (prefabAnimator == null || prefabRightHand == null)
                return false;

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

            Vector3 wheelOffFacing =
                new Vector3(-0.82f, 0f, 0.57f).normalized;
            Vector3 wheelOnFacing =
                new Vector3(-0.82f, 0f, -0.57f).normalized;
            Vector3 frontJackPosition =
                new(0f, 0f, vehicleFront + 0.28f);
            Vector3 rearJackPosition =
                new(0f, 0f, vehicleRear - 0.28f);
            Vector3 signalPosition =
                new(-1.55f, 0f, vehicleFront + 0.48f);

            Vector3 flGround =
                new(flHub.x, 0f, flHub.z);
            gunner = CreateActor(
                "FL_WheelGunner",
                flGround + Vector3.right * 0.45f,
                Vector3.left);
            wheelOff = CreateActor(
                "FL_WheelOff_L",
                flGround - wheelOffFacing * 0.72f,
                wheelOffFacing);
            wheelOn = CreateActor(
                "FL_WheelOn_L",
                flGround - wheelOnFacing * 0.85f,
                wheelOnFacing);
            frontJack = CreateActor(
                "FrontJack",
                frontJackPosition,
                Vector3.back);
            rearJack = CreateActor(
                "RearJack_R",
                rearJackPosition,
                Vector3.forward);
            pitSignal = CreateActor(
                "PitSignal_R",
                signalPosition,
                -signalPosition);
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

            gunnerRightHand = FindDescendant(
                gunner.Root,
                "hand_r");
            wheelOffLeftHand = FindDescendant(
                wheelOff.Root,
                "hand_l");
            wheelOffRightHand = FindDescendant(
                wheelOff.Root,
                "hand_r");
            wheelOnLeftHand = FindDescendant(
                wheelOn.Root,
                "hand_l");
            wheelOnRightHand = FindDescendant(
                wheelOn.Root,
                "hand_r");
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
            wheelOffTyreGripOffset = ResolveWheelCarrierGripOffset(
                wheelOff,
                assets.WheelOffFullL,
                WheelOffOwnershipNormalized,
                wheelOffLeftHand,
                wheelOffRightHand);
            wheelOnTyreGripOffset = ResolveWheelCarrierGripOffset(
                wheelOn,
                assets.WheelOnFullL,
                WheelOnHandoffNormalized,
                wheelOnLeftHand,
                wheelOnRightHand);
            CalibrateGunner();

            CreateAnchor(
                "FL_WheelGunner_Service",
                gunner.Root.localPosition);
            CreateAnchor(
                "FL_WheelOff_Service",
                wheelOff.Root.localPosition);
            CreateAnchor(
                "FL_WheelOn_Service",
                wheelOn.Root.localPosition);
            CreateAnchor(
                "FrontJack_Service",
                frontJack.Root.localPosition);
            CreateAnchor(
                "RearJack_Service",
                rearJack.Root.localPosition);
            CreateAnchor(
                "PitSignal_Service",
                pitSignal.Root.localPosition);
            ApplyReadyPose();
            return true;
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
        }

        public void ApplyChoreographyTime(
            float time,
            bool releaseEligible = false)
        {
            if (origin == null)
                return;

            origin.gameObject.SetActive(true);
            time = Mathf.Clamp(time, 0f, ReleaseReadyTime);
            float gunnerProgress = ResolveProgress(
                time,
                0f,
                ReleaseReadyTime);
            float wheelOffProgress = ResolveProgress(
                time,
                WheelOffStartTime,
                WheelOffDuration);
            float wheelOnProgress = ResolveProgress(
                time,
                WheelOnStartTime,
                WheelOnDuration);
            float jackProgress = ResolveProgress(
                time,
                0f,
                ReleaseReadyTime);
            float signalProgress = ResolveProgress(
                time,
                -SignalLeadTime,
                SignalDuration);

            gunner.SampleNormalized(
                assets.WheelGunnerFull,
                gunnerProgress);
            wheelOff.SampleNormalized(
                assets.WheelOffFullL,
                wheelOffProgress);
            wheelOn.SampleNormalized(
                assets.WheelOnFullL,
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

        public void Clear()
        {
            SetOriginalFlWheelVisible(true);
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
            gunnerRightHand = null;
            wheelOffLeftHand = null;
            wheelOffRightHand = null;
            wheelOnLeftHand = null;
            wheelOnRightHand = null;
            wheelOffTyreGripOffset = Vector3.zero;
            wheelOnTyreGripOffset = Vector3.zero;
            oldLooseTyre = null;
            newLooseTyre = null;
            oldLooseTyreVisualCenter = Vector3.zero;
            newLooseTyreVisualCenter = Vector3.zero;
            originalFlWheelRenderers = null;
            originalFlWheelRendererStates = null;
            flHub = FallbackFlHub;
            flHubRotation = Quaternion.identity;
            tyreDiameterMeters = FallbackTyreDiameterMeters;
            ReleaseReady = false;
        }

        private SampledActor CreateActor(
            string name,
            Vector3 position,
            Vector3 lookDirection)
        {
            Transform actorRoot = CreateRoot(name, crewRoot);
            actorRoot.localPosition = position;
            actorRoot.localRotation = Quaternion.LookRotation(
                lookDirection.normalized,
                Vector3.up);

            GameObject instance = Object.Instantiate(
                assets.PitCrewPrefab,
                actorRoot);
            instance.name = $"{name}_Visual";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            NormalizeActorHeight(instance, FullScaleCrewHeightMeters);
            DisablePhysics(instance, keepAnimator: true);

            Animator animator =
                instance.GetComponentInChildren<Animator>(true);
            return animator != null
                ? new SampledActor(
                    actorRoot,
                    animator,
                    assets.ChoreographyBaseController)
                : null;
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

        private Vector3 ResolveWheelCarrierGripOffset(
            SampledActor actor,
            AnimationClip clip,
            float contactNormalized,
            Transform leftHand,
            Transform rightHand)
        {
            actor.SampleNormalized(clip, 0f);
            actor.SampleNormalized(clip, contactNormalized);
            Vector3 handMidpoint = ResolveHandMidpoint(
                leftHand,
                rightHand,
                flHub);
            return flHub - handMidpoint;
        }

        private void CalibrateGunner()
        {
            gunner.SampleNormalized(
                assets.WheelGunnerFull,
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

        private float EstimateWheelGunReach()
        {
            Renderer[] renderers =
                wheelGun.GetComponentsInChildren<Renderer>(true);
            if (!TryMeasureRenderersInSpace(
                    renderers,
                    wheelGun.transform,
                    out Bounds bounds))
            {
                return 0.32f;
            }

            Vector3 grip = wheelGun.transform.InverseTransformPoint(
                wheelGunGrip.position);
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
            gunner.SampleNormalized(assets.WheelGunnerFull, 0f);
            wheelOff.SampleNormalized(assets.WheelOffFullL, 0f);
            wheelOn.SampleNormalized(assets.WheelOnFullL, 0f);
            frontJack.SampleNormalized(assets.FrontJackFullL, 0f);
            rearJack.SampleNormalized(assets.RearJackFullR, 0f);
            pitSignal.SampleNormalized(assets.PitSignalFullR, 0f);
            ApplyWheelState(0f, 0f);
            ApplyWheelGun();
        }

        private void ApplyWheelState(
            float wheelOffProgress,
            float wheelOnProgress)
        {
            bool oldTyreOwned =
                wheelOffProgress >= WheelOffOwnershipNormalized;
            bool replacementMounted =
                wheelOnProgress >= WheelOnHandoffNormalized;

            SetOriginalFlWheelVisible(
                !oldTyreOwned || replacementMounted);
            oldLooseTyre.SetActive(oldTyreOwned);
            newLooseTyre.SetActive(!replacementMounted);

            if (oldTyreOwned)
            {
                SetTyreVisualCenter(
                    oldLooseTyre,
                    oldLooseTyreVisualCenter,
                    ResolveTyreGripCenter(
                        wheelOffLeftHand,
                        wheelOffRightHand,
                        wheelOffTyreGripOffset,
                        flHub));
            }

            if (!replacementMounted)
            {
                SetTyreVisualCenter(
                    newLooseTyre,
                    newLooseTyreVisualCenter,
                    ResolveTyreGripCenter(
                        wheelOnLeftHand,
                        wheelOnRightHand,
                        wheelOnTyreGripOffset,
                        flHub));
            }
        }

        private void SetTyreVisualCenter(
            GameObject tyre,
            Vector3 visualCenterInTyre,
            Vector3 desiredCenter)
        {
            Transform tyreTransform = tyre.transform;
            tyreTransform.localPosition = desiredCenter;
            tyreTransform.localRotation = flHubRotation;
            Vector3 currentCenter = origin.InverseTransformPoint(
                tyreTransform.TransformPoint(visualCenterInTyre));
            tyreTransform.localPosition += desiredCenter - currentCenter;
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
            Transform leftHand,
            Transform rightHand,
            Vector3 gripOffset,
            Vector3 fallback)
        {
            return ResolveHandMidpoint(
                    leftHand,
                    rightHand,
                    fallback) +
                gripOffset;
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

        private static void NormalizeActorHeight(
            GameObject instance,
            float targetHeight)
        {
            if (instance == null || targetHeight <= 0.0001f)
                return;

            Renderer[] renderers =
                instance.GetComponentsInChildren<Renderer>(true);
            if (!TryMeasureRenderersInSpace(
                    renderers,
                    instance.transform,
                    out Bounds bounds) ||
                bounds.size.y <= 0.0001f)
            {
                return;
            }

            instance.transform.localScale *= targetHeight / bounds.size.y;
        }
    }
}
