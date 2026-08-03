using System;
using System.Collections.Generic;
using F1XR.RestAPI.Replay.Track.Placement;
using F1XR.RestAPI.Api;
using UnityEngine;
using Unity.Profiling;
using F1XR.RestAPI.Replay.Track.Build;

namespace F1XR.RestAPI.Replay
{
    public class ReplayCarSet
    {
        private const int MaxDetailedCarCount = 3;
        private const float RenderLodBudgetInterval = 0.2f;
        private const float ApproachRibbonFadeOutSeconds = 0.3f;

        private static readonly ProfilerMarker BuildFramesMarker =
            new("F1XR.Cars.BuildFrames");
        private static readonly ProfilerMarker ApplyLogicalPosesMarker =
            new("F1XR.Cars.ApplyLogicalPoses");
        private static readonly ProfilerMarker ApplyVisualsMarker =
            new("F1XR.Cars.ApplyVisuals");
        private static readonly ProfilerMarker AudioAudibilityMarker =
            new("F1XR.Cars.AudioAudibility");
        private static readonly ProfilerMarker FindCarMarker =
            new("F1XR.Cars.FindCar");
        private static readonly ProfilerMarker ResolvePoseMarker =
            new("F1XR.Cars.ResolvePose");

        private readonly CarInstances carInstances;
        private readonly ReplayCarMotion carMotion;
        private readonly ReplayGridStartAudio gridStartAudio = new();
        private readonly DriverRoster driverRoster = new();
        private readonly CarPresentation carPresentation;
        private readonly CarAudio carAudio;
        private readonly OvertakeMotion overtakeMotion = new();
        private readonly List<CarFrame> frames = new();
        private readonly Dictionary<int, ReplayCarPose> poses = new();
        private readonly Dictionary<int, float> visualWidths = new();
        private readonly Dictionary<int, float> visualLengths = new();
        private readonly Dictionary<int, int> ranks = new();
        private readonly Dictionary<int, string> debugEventByDriver = new();
        private readonly List<RenderLodCandidate>
            renderLodCandidates = new();
        private readonly HashSet<ReplayCarView>
            renderLodDetailedCars = new();
        private readonly Action<int> removeCarState;
        private readonly Action<int, ReplayCarView> setupCar;
        private readonly ReplayPlayer player;
        private readonly bool allowInteraction;
        private OvertakeMotionSettings overtakeSettings = new();
        private ReplayEventDto approachRibbonEvent;
        private OvertakeApproachRibbonSettings approachRibbonSettings;
        private ReplayEventDto sideBySideVfxEvent;
        private OvertakeSideBySideVfxSettings sideBySideVfxSettings;
        private OvertakeCompletionVfxSettings completionVfxSettings;
        private OvertakeBattleSequence showcaseBattle;
        private float sideBySideLastReplayTime = float.NaN;
        private bool sideBySideWasActive;
        private bool sideBySideSweepTriggered;
        private bool sideBySideSparksTriggered;
        private int sideBySideExchangeIndex = -1;
        private int completionVfxDriver;
        private int selectedDriverNumber;
        private bool renderLodEnabled = true;
        private Camera renderLodCamera;
        private float nextRenderLodBudgetTime;
        private float overtakeVehicleSizeScale = 1f;

        public ReplayCarSet(
            GameObject carPrefab,
            ReplayPlayer player,
            bool allowInteraction = true)
        {
            this.player = player;
            this.allowInteraction = allowInteraction;
            removeCarState = RemoveCarState;
            setupCar = SetupCar;
            TeamCarPrefabs teamCarPrefabs = new TeamCarPrefabs(carPrefab);
            carInstances = new CarInstances(
                teamCarPrefabs,
                driverRoster);
            carMotion = new ReplayCarMotion(carInstances);
            carPresentation = new CarPresentation(carInstances.Cars, driverRoster);
            carAudio = new CarAudio(carInstances.Cars, driverRoster.Teams);
        }

        public bool HasCars => carInstances.HasCars;

        public void SetMapScaleRatio(float ratio)
        {
            carInstances.SetMapScaleRatio(ratio);
        }

        public void SetTeamPrefabs(TeamCarPrefab[] prefabs)
        {
            carInstances.SetTeamPrefabs(
                prefabs,
                removeCarState,
                setupCar);
        }

        public bool TryGetCarTransform(int driverNumber, out Transform carTransform)
        {
            return carInstances.TryGetTransform(driverNumber, out carTransform);
        }

        public bool TryGetVisualTransform(int driverNumber, out Transform carTransform)
        {
            return carInstances.TryGetVisualTransform(driverNumber, out carTransform);
        }

        public bool TryGetVisualLength(
            int driverNumber,
            out float visualLength)
        {
            return visualLengths.TryGetValue(
                    driverNumber,
                    out visualLength) &&
                visualLength > 0f;
        }

        internal bool TryEnsureVisualSize(
            int driverNumber,
            out float visualWidth,
            out float visualLength)
        {
            visualWidth = 0f;
            visualLength = 0f;
            if (driverNumber <= 0)
                return false;

            ReplayCarView car = carInstances.GetOrCreate(
                driverNumber,
                removeCarState,
                setupCar);
            if (car == null)
                return false;

            visualWidth =
                car.GetVisualWidth() *
                overtakeVehicleSizeScale;
            visualLength =
                car.GetVisualLength() *
                overtakeVehicleSizeScale;
            return visualWidth > 0f && visualLength > 0f;
        }

        public void SetReplayEvents(ReplayEventDto[] events)
        {
            overtakeMotion.SetEvents(events);
        }

        public void SetOvertakeApproachRibbon(
            ReplayEventDto replayEvent,
            OvertakeApproachRibbonSettings settings)
        {
            approachRibbonEvent = replayEvent;
            approachRibbonSettings = settings;
        }

        public void SetOvertakeSideBySideVfx(
            ReplayEventDto replayEvent,
            OvertakeSideBySideVfxSettings settings)
        {
            sideBySideVfxEvent = replayEvent;
            sideBySideVfxSettings = settings;
            sideBySideLastReplayTime = float.NaN;
            sideBySideWasActive = false;
            sideBySideSweepTriggered = false;
            sideBySideSparksTriggered = false;
        }

        public void SetOvertakeCompletionVfx(
            OvertakeCompletionVfxSettings settings)
        {
            ResetOvertakeCompletionVfx();
            completionVfxSettings = settings;
        }

        public void TriggerOvertakeCompletionVfx(
            int driver,
            float replayTime)
        {
            TriggerOvertakeCompletionVfx(
                driver,
                replayTime,
                null,
                1f);
        }

        public void TriggerOvertakeCompletionVfx(
            int driver,
            float replayTime,
            string hudText,
            float intensityScale)
        {
            if (completionVfxSettings == null ||
                !completionVfxSettings.enabled ||
                !carInstances.Cars.TryGetValue(
                    driver,
                    out ReplayCarView car) ||
                car == null)
            {
                return;
            }

            ResetOvertakeCompletionVfx();
            completionVfxDriver = driver;
            car.TriggerOvertakeCompletionVfx(
                completionVfxSettings,
                replayTime,
                hudText,
                intensityScale);
        }

        public void UpdateOvertakeCompletionVfx(
            float replayTime)
        {
            if (completionVfxDriver <= 0 ||
                !carInstances.Cars.TryGetValue(
                    completionVfxDriver,
                    out ReplayCarView car) ||
                car == null)
            {
                return;
            }

            car.UpdateOvertakeCompletionVfx(replayTime);
        }

        public void ResetOvertakeCompletionVfx()
        {
            foreach (ReplayCarView car
                in carInstances.Cars.Values)
            {
                car?.ResetOvertakeCompletionVfx();
            }

            completionVfxDriver = 0;
        }

        public void SetOvertakeSettings(OvertakeMotionSettings settings)
        {
            overtakeSettings = settings ?? new OvertakeMotionSettings();
            overtakeMotion.SetSettings(overtakeSettings);
        }

        internal void SetOvertakePresentationMode(
            OvertakePresentationMode mode)
        {
            overtakeMotion.SetPresentationMode(mode);
        }

        internal void SetShowcaseBattle(
            OvertakeBattleSequence sequence)
        {
            showcaseBattle = sequence != null && sequence.IsValid
                ? sequence
                : null;
            sideBySideExchangeIndex = -1;
            overtakeMotion.SetShowcaseBattle(sequence);
        }

        internal void SetOvertakeVehicleSizeScale(float scale)
        {
            overtakeVehicleSizeScale = Mathf.Max(0.01f, scale);
        }

        internal void ResetResolvedOvertakeSides()
        {
            overtakeMotion.ResetResolvedPassingSides();
        }

        public void SetFallbackOvertakeCorridor(
            IReadOnlyList<Vector3> centerline,
            float roadWidth,
            bool loop)
        {
            overtakeMotion.SetFallbackCorridor(
                centerline,
                roadWidth,
                loop);
        }

        public void SetActualOvertakeCorridor(
            IReadOnlyList<Vector3> centerline,
            IReadOnlyList<Vector3> leftBoundary,
            IReadOnlyList<Vector3> rightBoundary,
            bool loop)
        {
            overtakeMotion.SetTrackCorridor(
                centerline,
                leftBoundary,
                rightBoundary,
                loop);
        }

        public bool TryGetResolvedOvertakeSide(
            ReplayEventDto replayEvent,
            out int side)
        {
            return overtakeMotion.TryGetResolvedPassingSide(
                replayEvent,
                out side);
        }

        public void Show(
            Dictionary<int, List<LocationSample>> samples,
            Dictionary<int, int> indices,
            float time,
            List<PositionSampleDto> positions = null,
            HashSet<int> driverFilter = null)
        {
            carMotion.PrepareMappedPositions(samples);

            Dictionary<int, int> ranks = positions != null
                ? GetRanksByDriver(positions)
                : null;
            BuildFramesMarker.Begin();
            frames.Clear();
            poses.Clear();
            visualWidths.Clear();
            visualLengths.Clear();

            foreach (KeyValuePair<int, List<LocationSample>> pair in samples)
            {
                int driver = pair.Key;
                if (driverFilter != null && !driverFilter.Contains(driver))
                    continue;

                List<LocationSample> list = pair.Value;

                if (list.Count < 2)
                    continue;

                FindCarMarker.Begin();
                ReplayCarView car = carInstances.GetOrCreate(
                    driver,
                    removeCarState,
                    setupCar);
                FindCarMarker.End();
                car.ClearRoomPresentation();

                carAudio.EnsureCar(driver, car);
                if (ranks != null && ranks.TryGetValue(driver, out int rank))
                    carPresentation.SetRank(car, rank);

                int index = indices[driver];
                index = Mathf.Clamp(index, 0, list.Count - 2);

                while (index > 0 && list[index].t > time)
                    index--;

                while (index < list.Count - 2 && list[index + 1].t < time)
                    index++;

                indices[driver] = index;

                ResolvePoseMarker.Begin();
                carMotion.ResolvePose(
                    car,
                    list[Mathf.Max(0, index - 1)],
                    list[index],
                    list[index + 1],
                    list[Mathf.Min(list.Count - 1, index + 2)],
                    time,
                    out ReplayCarPose pose,
                    out float interpolation,
                    out float duration);
                ResolvePoseMarker.End();
                frames.Add(new CarFrame(
                    driver,
                    car,
                    list[index],
                    list[index + 1],
                    pose,
                    interpolation,
                    duration));
                poses[driver] = pose;
                visualWidths[driver] =
                    car.GetVisualWidth() *
                    overtakeVehicleSizeScale;
                visualLengths[driver] =
                    car.GetVisualLength() *
                    overtakeVehicleSizeScale;
            }
            BuildFramesMarker.End();

            UpdateRenderLodBudget();

            ApplyLogicalPosesMarker.Begin();
            foreach (CarFrame frame in frames)
            {
                if (frame.car.ShouldApplyMotionThisFrame())
                {
                    carMotion.ApplyLogicalPose(
                        frame.car,
                        frame.pose);
                }
            }
            ApplyLogicalPosesMarker.End();

            overtakeMotion.PrepareFrame(
                time,
                poses,
                visualWidths,
                visualLengths);

            ApplyVisualsMarker.Begin();
            foreach (CarFrame frame in frames)
            {
                if (!frame.car.ShouldApplyMotionThisFrame())
                    continue;

                VisualMotionPose visualPose = overtakeMotion.Resolve(
                    frame.driver,
                    time,
                    poses,
                    visualWidths,
                    visualLengths);
                carMotion.ApplyVisualPose(frame.car, frame.pose, visualPose);

                DrawOvertakeDebug(frame, visualPose);

                UpdateEngineSound(
                    frame.car,
                    frame.a,
                    frame.b,
                    frame.interpolation,
                    frame.duration,
                    time);
            }
            ApplyVisualsMarker.End();

            UpdateOvertakeApproachRibbon(time);
            UpdateOvertakeSideBySideVfx(time);

            AudioAudibilityMarker.Begin();
            carAudio.UpdateAudibility();
            AudioAudibilityMarker.End();
        }

        private Dictionary<int, int> GetRanksByDriver(List<PositionSampleDto> positions)
        {
            ranks.Clear();

            if (positions == null)
                return ranks;

            foreach (PositionSampleDto position in positions)
            {
                if (position == null)
                    continue;

                ranks[position.driverNumber] = position.position;
            }

            return ranks;
        }

        public void Clear()
        {
            ClearOvertakeApproachRibbon();
            ClearOvertakeSideBySideVfx();
            ResetOvertakeCompletionVfx();
            showcaseBattle = null;
            overtakeMotion.SetShowcaseBattle(null);
            completionVfxSettings = null;
            selectedDriverNumber = 0;
            debugEventByDriver.Clear();
            carPresentation.SetSelectedDriver(0);
            ResetPlacement();
        }

        public void ResetPlacement()
        {
            carInstances.Clear();
            carMotion.Clear();
            gridStartAudio.Clear(selectedDriverNumber);
            carAudio.Clear();
        }

        public void SetSelectedDriver(int driverNumber)
        {
            selectedDriverNumber = driverNumber;

            carPresentation.SetSelectedDriver(driverNumber);
            carAudio.SetSelectedDriver(driverNumber);
        }

        public void SetAudioFocusDriver(int driverNumber)
        {
            carAudio.SetMixFocusDriver(driverNumber);
        }

        public void SetShowcaseDrivingPresentation(
            int firstDriver,
            int secondDriver,
            bool enabled)
        {
            foreach (KeyValuePair<int, ReplayCarView> pair
                in carInstances.Cars)
            {
                if (pair.Value == null)
                    continue;

                bool emphasize =
                    enabled &&
                    (pair.Key == firstDriver ||
                     pair.Key == secondDriver);
                pair.Value.SetDrivingPresentationEmphasis(
                    emphasize);
            }
        }

        public void SetRenderLodEnabled(bool enabled)
        {
            if (renderLodEnabled == enabled)
                return;

            renderLodEnabled = enabled;
            nextRenderLodBudgetTime = 0f;
            foreach (ReplayCarView car
                in carInstances.Cars.Values)
            {
                if (car != null)
                    car.SetRenderLodEnabled(enabled);
            }
        }

        private void SetupCar(int driver, ReplayCarView car)
        {
            carPresentation.SetupCar(driver, car);
            car.ConfigureDrivingPresentation();
            car.ConfigureRenderLod();
            car.SetRenderLodEnabled(renderLodEnabled);
            carAudio.ConfigureCar(driver, car);

            ReplayCarInteractable interaction = car.GetComponent<ReplayCarInteractable>();
            if (interaction == null && allowInteraction)
                interaction = car.gameObject.AddComponent<ReplayCarInteractable>();

            if (interaction != null)
            {
                interaction.enabled = allowInteraction;
                if (allowInteraction)
                {
                    interaction.Configure(car, player);
                    interaction.enabled = player == null || !player.IsTrackEditMode;
                }
            }
        }

        private void UpdateOvertakeApproachRibbon(float time)
        {
            if (showcaseBattle != null && showcaseBattle.IsValid)
            {
                UpdateBattleApproachRibbon(time);
                return;
            }

            if (approachRibbonEvent == null ||
                approachRibbonSettings == null ||
                approachRibbonEvent.driverNumbers == null ||
                approachRibbonEvent.driverNumbers.Length < 2)
            {
                return;
            }

            if (!approachRibbonSettings.enabled)
            {
                foreach (ReplayCarView car in carInstances.Cars.Values)
                    car?.ClearOvertakeApproachRibbon();
                return;
            }

            float totalPortion = Mathf.Max(
                0.0001f,
                overtakeSettings.approachPortion +
                overtakeSettings.parallelPortion +
                overtakeSettings.returnPortion);
            float duration =
                approachRibbonEvent.endTime -
                approachRibbonEvent.startTime;
            if (duration <= 0f)
                return;

            float anchorProgress = Mathf.Clamp01(
                (approachRibbonEvent.anchorTime -
                 approachRibbonEvent.startTime) /
                duration);
            float approachProgress = Mathf.Clamp(
                Mathf.Min(
                    overtakeSettings.approachPortion /
                    totalPortion,
                    anchorProgress),
                0.0001f,
                0.9998f);
            float approachEnd =
                approachRibbonEvent.startTime +
                duration * approachProgress;
            float approachStart =
                approachRibbonEvent.startTime -
                approachRibbonSettings.preRollSeconds;
            bool isApproaching =
                time >= approachStart &&
                time < approachEnd;
            float fadeEnd =
                approachEnd +
                ApproachRibbonFadeOutSeconds;
            bool isFading =
                time >= approachEnd &&
                time < fadeEnd;

            if (!isApproaching && !isFading)
            {
                ClearApproachRibbonForDriver(
                    approachRibbonEvent.driverNumbers[0]);
                ClearApproachRibbonForDriver(
                    approachRibbonEvent.driverNumbers[1]);
                return;
            }

            float progress = isApproaching
                ? Mathf.InverseLerp(
                    approachStart,
                    approachEnd,
                    time)
                : 0f;
            float intensity = isApproaching
                ? Mathf.Lerp(
                    approachRibbonSettings.startIntensity,
                    1f,
                    Mathf.Clamp01(
                        approachRibbonSettings.growth != null
                        ? approachRibbonSettings.growth.Evaluate(progress)
                        : progress))
                : 1f -
                  Mathf.SmoothStep(
                      0f,
                      1f,
                      Mathf.InverseLerp(
                          approachEnd,
                          fadeEnd,
                          time));

            SetApproachRibbonForDriver(
                approachRibbonEvent.driverNumbers[0],
                true,
                intensity,
                time,
                isApproaching);
            SetApproachRibbonForDriver(
                approachRibbonEvent.driverNumbers[1],
                false,
                intensity,
                time,
                isApproaching);
        }

        private void SetApproachRibbonForDriver(
            int driver,
            bool overtaker,
            float intensity,
            float time,
            bool allowEmission)
        {
            if (!carInstances.Cars.TryGetValue(
                    driver,
                    out ReplayCarView car) ||
                car == null)
            {
                return;
            }

            car.SetOvertakeApproachRibbon(
                approachRibbonSettings,
                overtaker,
                intensity,
                time,
                allowEmission: allowEmission);
        }

        private void ClearApproachRibbonForDriver(int driver)
        {
            if (carInstances.Cars.TryGetValue(
                    driver,
                    out ReplayCarView car))
            {
                car?.ClearOvertakeApproachRibbon();
            }
        }

        private void ClearOvertakeApproachRibbon()
        {
            foreach (ReplayCarView car in carInstances.Cars.Values)
                car?.ClearOvertakeApproachRibbon();

            approachRibbonEvent = null;
            approachRibbonSettings = null;
        }

        private void UpdateBattleApproachRibbon(float time)
        {
            if (approachRibbonSettings == null ||
                !approachRibbonSettings.enabled)
            {
                ClearBattleRibbons();
                return;
            }

            int exchangeIndex = FindUpcomingBattleExchange(time);
            if (exchangeIndex < 0)
            {
                ClearBattleRibbons();
                return;
            }

            OvertakeBattleExchange exchange =
                showcaseBattle.Exchanges[exchangeIndex];
            float approachStart = Mathf.Max(
                showcaseBattle.StartTime,
                exchange.anchorTime -
                approachRibbonSettings.preRollSeconds);
            float fadeEnd =
                exchange.anchorTime +
                ApproachRibbonFadeOutSeconds;
            if (time < approachStart || time >= fadeEnd)
            {
                ClearBattleRibbons();
                return;
            }

            bool approaching = time < exchange.anchorTime;
            float progress = approaching
                ? Mathf.InverseLerp(
                    approachStart,
                    exchange.anchorTime,
                    time)
                : 1f;
            float intensity = approaching
                ? Mathf.Lerp(
                    approachRibbonSettings.startIntensity,
                    1f,
                    Mathf.Clamp01(
                        approachRibbonSettings.growth != null
                            ? approachRibbonSettings.growth
                                .Evaluate(progress)
                            : progress))
                : 1f - Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(
                        exchange.anchorTime,
                        fadeEnd,
                        time));
            SetApproachRibbonForDriver(
                exchange.overtaker,
                true,
                intensity,
                time,
                approaching);
            SetApproachRibbonForDriver(
                exchange.defender,
                false,
                intensity,
                time,
                approaching);
        }

        private int FindUpcomingBattleExchange(float time)
        {
            for (int i = 0;
                 i < showcaseBattle.Exchanges.Count;
                 i++)
            {
                if (time <
                    showcaseBattle.Exchanges[i].anchorTime +
                    ApproachRibbonFadeOutSeconds)
                {
                    return i;
                }
            }

            return -1;
        }

        private void ClearBattleRibbons()
        {
            ClearApproachRibbonForDriver(
                showcaseBattle.FirstDriver);
            ClearApproachRibbonForDriver(
                showcaseBattle.SecondDriver);
        }

        private void UpdateOvertakeSideBySideVfx(float time)
        {
            if (showcaseBattle != null && showcaseBattle.IsValid)
            {
                UpdateBattleSideBySideVfx(time);
                return;
            }

            if (sideBySideVfxEvent == null ||
                sideBySideVfxSettings == null ||
                approachRibbonSettings == null ||
                sideBySideVfxEvent.driverNumbers == null ||
                sideBySideVfxEvent.driverNumbers.Length < 2)
            {
                return;
            }

            if (!sideBySideVfxSettings.enabled)
            {
                ResetSideBySideRuntimeEffects();
                return;
            }

            int overtakerDriver =
                sideBySideVfxEvent.driverNumbers[0];
            int defenderDriver =
                sideBySideVfxEvent.driverNumbers[1];
            PrepareSideBySideVfx(
                overtakerDriver,
                true);
            PrepareSideBySideVfx(
                defenderDriver,
                false);

            bool timeDiscontinuity =
                !float.IsNaN(sideBySideLastReplayTime) &&
                (time < sideBySideLastReplayTime ||
                 time - sideBySideLastReplayTime >
                 sideBySideVfxSettings
                     .seekResetThresholdSeconds);
            if (timeDiscontinuity)
                ResetSideBySideOneShotState();

            sideBySideLastReplayTime = time;
            if (!TryGetSideBySideInterval(
                    out float stageStart,
                    out float stageEnd))
            {
                return;
            }

            bool stageActive =
                time >= stageStart &&
                time < stageEnd;
            if (stageActive && !sideBySideWasActive)
            {
                TriggerSideBySideLightSweep(
                    overtakerDriver,
                    time);
            }

            float sparkTime = Mathf.Lerp(
                stageStart,
                stageEnd,
                sideBySideVfxSettings
                    .sparkTriggerNormalized);
            if (stageActive &&
                !sideBySideSparksTriggered &&
                time >= sparkTime)
            {
                TriggerSideBySideSparks(
                    overtakerDriver);
            }

            sideBySideWasActive = stageActive;
            UpdateSideBySideLightSweep(
                overtakerDriver,
                time);
        }

        private bool TryGetSideBySideInterval(
            out float stageStart,
            out float stageEnd)
        {
            stageStart = 0f;
            stageEnd = 0f;
            float duration =
                sideBySideVfxEvent.endTime -
                sideBySideVfxEvent.startTime;
            if (duration <= 0f)
                return false;

            float totalPortion = Mathf.Max(
                0.0001f,
                overtakeSettings.approachPortion +
                overtakeSettings.parallelPortion +
                overtakeSettings.returnPortion);
            float startProgress =
                overtakeSettings.approachPortion /
                totalPortion;
            float endProgress =
                (overtakeSettings.approachPortion +
                 overtakeSettings.parallelPortion) /
                totalPortion;
            float anchorProgress = Mathf.Clamp01(
                (sideBySideVfxEvent.anchorTime -
                 sideBySideVfxEvent.startTime) /
                duration);
            startProgress = Mathf.Clamp(
                Mathf.Min(
                    startProgress,
                    anchorProgress) +
                sideBySideVfxSettings
                    .startOffsetNormalized,
                0.0001f,
                0.9998f);
            endProgress = Mathf.Clamp(
                Mathf.Max(
                    endProgress,
                    anchorProgress) +
                sideBySideVfxSettings
                    .endOffsetNormalized,
                startProgress + 0.0001f,
                0.9999f);
            stageStart =
                sideBySideVfxEvent.startTime +
                duration *
                startProgress;
            stageEnd =
                sideBySideVfxEvent.startTime +
                duration *
                endProgress;
            return stageEnd > stageStart;
        }

        private void PrepareSideBySideVfx(
            int driver,
            bool overtaker)
        {
            if (!carInstances.Cars.TryGetValue(
                    driver,
                    out ReplayCarView car) ||
                car == null)
            {
                return;
            }

            car.PrepareOvertakeSideBySideVfx(
                approachRibbonSettings,
                sideBySideVfxSettings,
                overtaker);
        }

        private void TriggerSideBySideLightSweep(
            int overtakerDriver,
            float time)
        {
            if (sideBySideSweepTriggered)
                return;

            sideBySideSweepTriggered = true;
            if (carInstances.Cars.TryGetValue(
                    overtakerDriver,
                    out ReplayCarView car) &&
                car != null)
            {
                car.TriggerOvertakeLightSweep(
                    sideBySideVfxSettings,
                    time);
            }
        }

        private void UpdateSideBySideLightSweep(
            int overtakerDriver,
            float time)
        {
            if (carInstances.Cars.TryGetValue(
                    overtakerDriver,
                    out ReplayCarView car) &&
                car != null)
            {
                car.UpdateOvertakeLightSweep(
                    sideBySideVfxSettings,
                    time);
            }
        }

        private void TriggerSideBySideSparks(
            int overtakerDriver)
        {
            sideBySideSparksTriggered = true;
            if (carInstances.Cars.TryGetValue(
                    overtakerDriver,
                    out ReplayCarView car) &&
                car != null)
            {
                car.TriggerOvertakeUnderfloorSparks(
                    sideBySideVfxSettings);
            }
        }

        private void ResetSideBySideOneShotState()
        {
            sideBySideWasActive = false;
            sideBySideSweepTriggered = false;
            sideBySideSparksTriggered = false;
            ResetSideBySideRuntimeEffects();
        }

        private void ResetSideBySideRuntimeEffects()
        {
            foreach (ReplayCarView car in carInstances.Cars.Values)
                car?.ResetOvertakeSideBySideVfx();
        }

        private void ClearOvertakeSideBySideVfx()
        {
            ResetSideBySideRuntimeEffects();
            sideBySideVfxEvent = null;
            sideBySideVfxSettings = null;
            sideBySideLastReplayTime = float.NaN;
            sideBySideWasActive = false;
            sideBySideSweepTriggered = false;
            sideBySideSparksTriggered = false;
            sideBySideExchangeIndex = -1;
        }

        private void UpdateBattleSideBySideVfx(float time)
        {
            if (sideBySideVfxSettings == null ||
                approachRibbonSettings == null ||
                !sideBySideVfxSettings.enabled)
            {
                ResetSideBySideRuntimeEffects();
                return;
            }

            bool timeDiscontinuity =
                !float.IsNaN(sideBySideLastReplayTime) &&
                (time < sideBySideLastReplayTime ||
                 time - sideBySideLastReplayTime >
                 sideBySideVfxSettings
                     .seekResetThresholdSeconds);
            if (timeDiscontinuity)
            {
                sideBySideExchangeIndex = -1;
                ResetSideBySideOneShotState();
            }

            sideBySideLastReplayTime = time;
            int exchangeIndex = FindActiveBattleExchange(time);
            if (exchangeIndex < 0)
            {
                sideBySideWasActive = false;
                return;
            }

            OvertakeBattleExchange exchange =
                showcaseBattle.Exchanges[exchangeIndex];
            if (sideBySideExchangeIndex != exchangeIndex)
            {
                ResetSideBySideOneShotState();
                sideBySideExchangeIndex = exchangeIndex;
            }

            PrepareSideBySideVfx(exchange.overtaker, true);
            PrepareSideBySideVfx(exchange.defender, false);
            float stageStart =
                exchange.anchorTime -
                sideBySideVfxSettings.transitionBlendSeconds;
            float stageEnd = Mathf.Max(
                    exchange.anchorTime,
                    exchange.confirmedTime) +
                sideBySideVfxSettings.transitionBlendSeconds;
            if (!sideBySideWasActive)
            {
                TriggerSideBySideLightSweep(
                    exchange.overtaker,
                    time);
            }

            if (!sideBySideSparksTriggered &&
                time >= Mathf.Lerp(
                    stageStart,
                    stageEnd,
                    sideBySideVfxSettings
                        .sparkTriggerNormalized))
            {
                TriggerSideBySideSparks(
                    exchange.overtaker);
            }

            sideBySideWasActive = true;
            UpdateSideBySideLightSweep(
                exchange.overtaker,
                time);
        }

        private int FindActiveBattleExchange(float time)
        {
            float blend = sideBySideVfxSettings != null
                ? Mathf.Max(
                    0f,
                    sideBySideVfxSettings
                        .transitionBlendSeconds)
                : 0f;
            for (int i = 0;
                 i < showcaseBattle.Exchanges.Count;
                 i++)
            {
                OvertakeBattleExchange exchange =
                    showcaseBattle.Exchanges[i];
                float start = exchange.anchorTime - blend;
                float end = Mathf.Max(
                        exchange.anchorTime,
                        exchange.confirmedTime) +
                    blend;
                if (time >= start && time < end)
                    return i;
            }

            return -1;
        }

        private void UpdateRenderLodBudget()
        {
            if (!renderLodEnabled)
                return;

            float now = Time.unscaledTime;
            if (now < nextRenderLodBudgetTime)
                return;

            nextRenderLodBudgetTime =
                now + RenderLodBudgetInterval;
            if (renderLodCamera == null ||
                !renderLodCamera.isActiveAndEnabled)
            {
                renderLodCamera = Camera.main;
            }

            if (renderLodCamera == null)
                return;

            renderLodCandidates.Clear();
            renderLodDetailedCars.Clear();
            int requiredDetailedCount = 0;
            Vector3 cameraPosition =
                renderLodCamera.transform.position;

            foreach (CarFrame frame in frames)
            {
                if (frame.car == null)
                    continue;

                if (frame.car.RequiresDetailedRenderLod)
                {
                    requiredDetailedCount++;
                    continue;
                }

                if (!frame.car.QualifiesForDetailedRenderLod(
                        renderLodCamera))
                {
                    continue;
                }

                renderLodCandidates.Add(
                    new RenderLodCandidate(
                        frame.car,
                        (frame.car.transform.position -
                         cameraPosition).sqrMagnitude));
            }

            renderLodCandidates.Sort();
            int availableSlots = Mathf.Max(
                0,
                MaxDetailedCarCount -
                requiredDetailedCount);
            int detailedCount = Mathf.Min(
                availableSlots,
                renderLodCandidates.Count);
            for (int i = 0; i < detailedCount; i++)
            {
                renderLodDetailedCars.Add(
                    renderLodCandidates[i].car);
            }

            foreach (ReplayCarView car
                in carInstances.Cars.Values)
            {
                if (car != null)
                {
                    car.SetRenderLodBudgetDetailed(
                        renderLodDetailedCars.Contains(car));
                }
            }
        }

        private void RemoveCarState(int driver)
        {
            if (completionVfxDriver == driver)
                completionVfxDriver = 0;

            carMotion.RemoveCar(driver);
            carAudio.RemoveCar(driver);
            gridStartAudio.RemoveCar(driver);
        }

        private readonly struct RenderLodCandidate :
            IComparable<RenderLodCandidate>
        {
            public readonly ReplayCarView car;
            private readonly float distanceSquared;

            public RenderLodCandidate(
                ReplayCarView car,
                float distanceSquared)
            {
                this.car = car;
                this.distanceSquared = distanceSquared;
            }

            public int CompareTo(
                RenderLodCandidate other)
            {
                return distanceSquared.CompareTo(
                    other.distanceSquared);
            }
        }

        public void SetPlacement(ARPlanePlacementController source)
        {
            carMotion.SetPlacement(source);
        }

        public void SetBuildPlacer(TrackRevealPlacer source)
        {
            carMotion.SetBuildPlacer(source);
        }

        public void SetLabelsVisible(bool visible)
        {
            carPresentation.SetLabelsVisible(visible);
        }

        public void SetLeaderHighlightVisible(bool visible)
        {
            carPresentation.SetLeaderHighlightVisible(visible);
        }

        public void SetEngineSound(CarEngineSoundSettings settings)
        {
            carAudio.SetSettings(settings);
            gridStartAudio.SetDrivers(
                driverRoster.Teams,
                carAudio.Settings.useTeamBasedEngineAudio);
            StopGridStartAudio();
            carAudio.ConfigureCars();
        }

        public void SetSoundPlaying(bool playing)
        {
            carAudio.SetPlaying(playing);

            if (!carAudio.IsActive)
                gridStartAudio.Pause();
        }

        public void SetSoundPlacementReady(bool ready)
        {
            carAudio.SetPlacementReady(ready);

            if (!carAudio.IsActive)
                gridStartAudio.Pause();
        }

        public void SetCalibration(
            TrackCalibration source,
            bool resetRuntimeState = true)
        {
            carMotion.SetCalibration(source, resetRuntimeState);
        }

        public void SetCustomSpace(
            Transform parent,
            Vector3 sourceOrigin,
            Quaternion sourceToLocalRotation)
        {
            carMotion.SetCustomSpace(
                parent,
                sourceOrigin,
                sourceToLocalRotation);
        }

        public bool TryGetMappedPosition(
            LocationSample sample,
            out Vector3 position)
        {
            return carMotion.TryGetMappedPosition(sample, out position);
        }

        private void UpdateEngineSound(
            ReplayCarView car,
            LocationSample a,
            LocationSample b,
            float u,
            float duration,
            float replayTime)
        {
            float rpm = Mathf.Lerp(a.rpm, b.rpm, u);
            float throttle = Mathf.Lerp(a.throttle, b.throttle, u);
            float speed = Mathf.Lerp(a.speed, b.speed, u);
            int gear = u < 0.5f ? Gear(a) : Gear(b);
            int brake = u < 0.5f ? a.brake : b.brake;
            int drs = u < 0.5f ? a.drs : b.drs;

            if (speed <= 0.01f)
                speed = EstimateSpeed(a, b, duration);

            car.ApplyDrivingPresentation(
                replayTime,
                speed,
                brake);
            carAudio.UpdateTelemetry(
                car.driverNumber,
                rpm,
                throttle,
                speed,
                gear,
                brake,
                drs);
        }

        public void ApplyGridStartTimeline(float currentReplayTime, float raceStartTime, bool isPlaying, float playbackSpeed)
        {
            gridStartAudio.Apply(
                carAudio.Settings,
                carInstances.Cars,
                selectedDriverNumber,
                carAudio.PlacementReady,
                currentReplayTime,
                raceStartTime,
                isPlaying,
                playbackSpeed);
        }

        public void StopGridStartAudio()
        {
            gridStartAudio.Stop(selectedDriverNumber);
        }

        private static int Gear(LocationSample sample)
        {
            return sample.nGear > 0 ? sample.nGear : sample.n_gear;
        }

        private static float EstimateSpeed(LocationSample a, LocationSample b, float duration)
        {
            Vector3 positionA = new Vector3(a.x, a.y, a.z);
            Vector3 positionB = new Vector3(b.x, b.y, b.z);
            float metersPerSecond = Vector3.Distance(positionA, positionB) / Mathf.Max(0.001f, duration);
            return Mathf.Clamp(metersPerSecond * 3.6f, 0f, 340f);
        }

        public void SetDrivers(DriverInfoDto[] drivers)
        {
            if (drivers == null || drivers.Length == 0)
                return;

            driverRoster.SetDrivers(drivers);

            carAudio.RefreshDriverData();
            gridStartAudio.SetDrivers(
                driverRoster.Teams,
                carAudio.Settings != null && carAudio.Settings.useTeamBasedEngineAudio);
            carInstances.RefreshPrefabs(removeCarState, setupCar);
            carPresentation.RefreshDriverAppearance();

            foreach (KeyValuePair<int, ReplayCarView> pair in carInstances.Cars)
                carAudio.ConfigureCar(pair.Key, pair.Value);
        }

        public string GetDriverLabel(int driverNumber)
        {
            return driverRoster.GetLabel(driverNumber);
        }

        public DriverInfoDto GetDriverInfo(int driverNumber)
        {
            return driverRoster.GetInfo(driverNumber);
        }

        public Color GetDriverColor(int driverNumber)
        {
            return driverRoster.GetColor(driverNumber);
        }

        private void DrawOvertakeDebug(
            CarFrame frame,
            VisualMotionPose visualPose)
        {
            if (!overtakeSettings.debugOvertakeVisuals)
                return;

            if (!visualPose.active)
            {
                debugEventByDriver.Remove(frame.driver);
                return;
            }

            Debug.DrawLine(
                frame.pose.worldPosition,
                frame.car.VisualMotionRoot.position,
                visualPose.lateralOffset >= 0f ? Color.cyan : Color.magenta);

            if (debugEventByDriver.TryGetValue(frame.driver, out string previous) &&
                previous == visualPose.sourceEventId)
                return;

            debugEventByDriver[frame.driver] = visualPose.sourceEventId;
            Debug.Log(
                $"[OvertakeMotion] driver={frame.driver}, role={visualPose.role}, " +
                $"event={visualPose.sourceEventId}, side={visualPose.passingSide}, " +
                $"source={visualPose.sideSource}, confidence={visualPose.confidence:0.00}, " +
                $"offset={visualPose.lateralOffset:0.000}, yaw={visualPose.localYaw:0.0}, " +
                $"logical={frame.pose.worldPosition}, visual={frame.car.VisualMotionRoot.position}");
        }

        private readonly struct CarFrame
        {
            public readonly int driver;
            public readonly ReplayCarView car;
            public readonly LocationSample a;
            public readonly LocationSample b;
            public readonly ReplayCarPose pose;
            public readonly float interpolation;
            public readonly float duration;

            public CarFrame(
                int driver,
                ReplayCarView car,
                LocationSample a,
                LocationSample b,
                ReplayCarPose pose,
                float interpolation,
                float duration)
            {
                this.driver = driver;
                this.car = car;
                this.a = a;
                this.b = b;
                this.pose = pose;
                this.interpolation = interpolation;
                this.duration = duration;
            }
        }
    }
}
