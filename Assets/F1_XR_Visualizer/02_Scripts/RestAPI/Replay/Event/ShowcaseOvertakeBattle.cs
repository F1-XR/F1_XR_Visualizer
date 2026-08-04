using System;
using System.Collections.Generic;
using F1XR.RestAPI.Api;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    internal delegate bool OvertakeGapSampler(
        int firstDriver,
        int secondDriver,
        float time,
        out float gap);

    internal enum OvertakeBattleExchangeKind
    {
        Pass,
        Counter,
        Repass
    }

    internal readonly struct OvertakeBattleExchange
    {
        public readonly float anchorTime;
        public readonly float confirmedTime;
        public readonly int overtaker;
        public readonly int defender;
        public readonly OvertakeBattleExchangeKind kind;

        public OvertakeBattleExchange(
            float anchorTime,
            float confirmedTime,
            int overtaker,
            int defender,
            OvertakeBattleExchangeKind kind)
        {
            this.anchorTime = anchorTime;
            this.confirmedTime = confirmedTime;
            this.overtaker = overtaker;
            this.defender = defender;
            this.kind = kind;
        }
    }

    internal sealed class OvertakeBattleSequence
    {
        private readonly List<OvertakeBattleExchange> exchanges;

        public OvertakeBattleSequence(
            ReplayEventDto sourceEvent,
            int firstDriver,
            int secondDriver,
            float startTime,
            float endTime,
            float focusTime,
            float motionStartTime,
            bool reconstructed,
            List<OvertakeBattleExchange> exchanges)
        {
            SourceEvent = sourceEvent;
            FirstDriver = firstDriver;
            SecondDriver = secondDriver;
            StartTime = startTime;
            EndTime = endTime;
            FocusTime = focusTime;
            MotionStartTime = Mathf.Clamp(
                motionStartTime,
                startTime,
                endTime);
            Reconstructed = reconstructed;
            this.exchanges = exchanges ?? new List<OvertakeBattleExchange>();
        }

        public ReplayEventDto SourceEvent { get; }
        public int FirstDriver { get; }
        public int SecondDriver { get; }
        public float StartTime { get; }
        public float EndTime { get; }
        public float FocusTime { get; }
        public float MotionStartTime { get; }
        public bool Reconstructed { get; }
        public IReadOnlyList<OvertakeBattleExchange> Exchanges => exchanges;
        public bool IsValid =>
            FirstDriver > 0 &&
            SecondDriver > 0 &&
            FirstDriver != SecondDriver &&
            EndTime > StartTime &&
            exchanges.Count > 0;
        public int FinalLeader => exchanges.Count > 0
            ? exchanges[exchanges.Count - 1].overtaker
            : 0;
        public float LastExchangeTime => exchanges.Count > 0
            ? exchanges[exchanges.Count - 1].anchorTime
            : FocusTime;

        public int FindExchangeIndex(float time)
        {
            if (exchanges.Count == 0)
                return -1;

            int result = 0;
            for (int i = 1; i < exchanges.Count; i++)
            {
                float boundary =
                    (exchanges[i - 1].anchorTime +
                     exchanges[i].anchorTime) * 0.5f;
                if (time < boundary)
                    break;

                result = i;
            }

            return result;
        }
    }

    internal sealed class ShowcaseOvertakeBattleBuilder
    {
        private readonly List<DetectedExchange> detected = new();
        private readonly List<OvertakeBattleExchange> selected = new();

        public OvertakeBattleSequence Build(
            ReplayEventDto sourceEvent,
            float scanStart,
            float scanEnd,
            float timelineStart,
            float timelineEnd,
            float leadSeconds,
            float tailSeconds,
            float motionLeadSeconds,
            float continuationSeconds,
            float maximumDuration,
            float confirmationDistance,
            float confirmationSeconds,
            float sampleSeconds,
            OvertakeGapSampler sampleGap)
        {
            if (!TryGetDrivers(
                    sourceEvent,
                    out int firstDriver,
                    out int secondDriver) ||
                sampleGap == null)
            {
                return null;
            }

            scanStart = Mathf.Max(timelineStart, scanStart);
            scanEnd = Mathf.Min(timelineEnd, scanEnd);
            if (scanEnd <= scanStart)
                return null;

            DetectExchanges(
                firstDriver,
                secondDriver,
                scanStart,
                scanEnd,
                Mathf.Max(0.00001f, confirmationDistance),
                Mathf.Max(0f, confirmationSeconds),
                Mathf.Clamp(sampleSeconds, 0.02f, 0.25f),
                sampleGap);

            if (detected.Count == 0)
            {
                return CreateFallback(
                    sourceEvent,
                    firstDriver,
                    secondDriver,
                    timelineStart,
                    timelineEnd,
                    leadSeconds,
                    tailSeconds,
                    motionLeadSeconds);
            }

            int seedIndex = FindSeedExchange(sourceEvent);
            SelectBattle(
                seedIndex,
                Mathf.Max(0f, continuationSeconds),
                Mathf.Max(1f, maximumDuration),
                Mathf.Max(0f, leadSeconds),
                Mathf.Max(0f, tailSeconds));
            if (selected.Count == 0)
                return null;

            float start = Mathf.Max(
                timelineStart,
                selected[0].anchorTime -
                Mathf.Max(0f, leadSeconds));
            float end = Mathf.Min(
                timelineEnd,
                selected[selected.Count - 1].anchorTime +
                Mathf.Max(0f, tailSeconds));
            float focus = detected[seedIndex].anchorTime;
            float motionStart = Mathf.Max(
                start,
                selected[0].anchorTime -
                Mathf.Max(0f, motionLeadSeconds));

            return new OvertakeBattleSequence(
                sourceEvent,
                firstDriver,
                secondDriver,
                start,
                end,
                focus,
                motionStart,
                true,
                new List<OvertakeBattleExchange>(selected));
        }

        private void DetectExchanges(
            int firstDriver,
            int secondDriver,
            float startTime,
            float endTime,
            float confirmationDistance,
            float confirmationSeconds,
            float sampleSeconds,
            OvertakeGapSampler sampleGap)
        {
            detected.Clear();
            int stableOrder = 0;
            int candidateOrder = 0;
            float candidateStart = float.NaN;
            float candidateCrossing = float.NaN;
            float initialGap = 0f;
            bool hasInitial = sampleGap(
                firstDriver,
                secondDriver,
                startTime,
                out initialGap);
            float stableSideTime = startTime;
            float stableSideGap = initialGap;

            if (hasInitial &&
                Mathf.Abs(initialGap) >= confirmationDistance)
            {
                stableOrder = Sign(initialGap);
            }

            int steps = Mathf.Max(
                1,
                Mathf.CeilToInt(
                    (endTime - startTime) / sampleSeconds));
            for (int i = 1; i <= steps; i++)
            {
                float time = i == steps
                    ? endTime
                    : startTime + i * sampleSeconds;
                if (!sampleGap(
                        firstDriver,
                        secondDriver,
                        time,
                        out float gap))
                {
                    continue;
                }

                int order = Sign(gap);
                bool confirmedDistance =
                    Mathf.Abs(gap) >= confirmationDistance;
                if (stableOrder == 0)
                {
                    if (confirmedDistance)
                    {
                        stableOrder = order;
                        stableSideTime = time;
                        stableSideGap = gap;
                    }

                    continue;
                }

                if (candidateOrder == 0)
                {
                    if (order == stableOrder)
                    {
                        stableSideTime = time;
                        stableSideGap = gap;
                    }

                    if (order != 0 &&
                        order != stableOrder &&
                        confirmedDistance)
                    {
                        candidateOrder = order;
                        candidateStart = time;
                        candidateCrossing = EstimateCrossing(
                            stableSideTime,
                            stableSideGap,
                            time,
                            gap);
                    }
                }
                else if (order == stableOrder && confirmedDistance)
                {
                    candidateOrder = 0;
                    candidateStart = float.NaN;
                    candidateCrossing = float.NaN;
                    stableSideTime = time;
                    stableSideGap = gap;
                }
                else if (!confirmedDistance)
                {
                    candidateStart = float.NaN;
                }
                else if (order == candidateOrder && confirmedDistance)
                {
                    if (float.IsNaN(candidateStart))
                    {
                        candidateStart = time;
                    }
                    else if (
                        time - candidateStart >=
                        confirmationSeconds)
                    {
                        int overtaker = candidateOrder > 0
                            ? firstDriver
                            : secondDriver;
                        int defender = candidateOrder > 0
                            ? secondDriver
                            : firstDriver;
                        detected.Add(new DetectedExchange(
                            candidateCrossing,
                            time,
                            overtaker,
                            defender));
                        stableOrder = candidateOrder;
                        stableSideTime = time;
                        stableSideGap = gap;
                        candidateOrder = 0;
                        candidateStart = float.NaN;
                        candidateCrossing = float.NaN;
                    }
                }

            }
        }

        private int FindSeedExchange(ReplayEventDto sourceEvent)
        {
            int preferredOvertaker =
                sourceEvent.driverNumbers[0];
            int best = -1;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < detected.Count; i++)
            {
                if (detected[i].overtaker != preferredOvertaker)
                    continue;

                float distance = Mathf.Abs(
                    detected[i].anchorTime -
                    sourceEvent.anchorTime);
                if (distance >= bestDistance)
                    continue;

                best = i;
                bestDistance = distance;
            }

            if (best >= 0)
                return best;

            for (int i = 0; i < detected.Count; i++)
            {
                float distance = Mathf.Abs(
                    detected[i].anchorTime -
                    sourceEvent.anchorTime);
                if (distance >= bestDistance)
                    continue;

                best = i;
                bestDistance = distance;
            }

            return Mathf.Max(0, best);
        }

        private void SelectBattle(
            int seedIndex,
            float continuationSeconds,
            float maximumDuration,
            float leadSeconds,
            float tailSeconds)
        {
            selected.Clear();
            int first = seedIndex;
            int last = seedIndex;
            while (first > 0 &&
                   detected[first].anchorTime -
                   detected[first - 1].anchorTime <=
                   continuationSeconds)
            {
                float duration =
                    detected[last].anchorTime -
                    detected[first - 1].anchorTime +
                    leadSeconds +
                    tailSeconds;
                if (duration > maximumDuration)
                    break;

                first--;
            }

            while (last + 1 < detected.Count &&
                   detected[last + 1].anchorTime -
                   detected[last].anchorTime <=
                   continuationSeconds)
            {
                float duration =
                    detected[last + 1].anchorTime -
                    detected[first].anchorTime +
                    leadSeconds +
                    tailSeconds;
                if (duration > maximumDuration)
                    break;

                last++;
            }

            for (int i = first; i <= last; i++)
            {
                DetectedExchange exchange = detected[i];
                int sequenceIndex = i - first;
                OvertakeBattleExchangeKind kind = sequenceIndex == 0
                    ? OvertakeBattleExchangeKind.Pass
                    : sequenceIndex == 1
                        ? OvertakeBattleExchangeKind.Counter
                        : OvertakeBattleExchangeKind.Repass;
                selected.Add(new OvertakeBattleExchange(
                    exchange.anchorTime,
                    exchange.confirmedTime,
                    exchange.overtaker,
                    exchange.defender,
                    kind));
            }
        }

        private static OvertakeBattleSequence CreateFallback(
            ReplayEventDto sourceEvent,
            int firstDriver,
            int secondDriver,
            float timelineStart,
            float timelineEnd,
            float leadSeconds,
            float tailSeconds,
            float motionLeadSeconds)
        {
            float anchor = Mathf.Clamp(
                sourceEvent.anchorTime,
                timelineStart,
                timelineEnd);
            float start = Mathf.Max(
                timelineStart,
                anchor - Mathf.Max(0f, leadSeconds));
            float end = Mathf.Min(
                timelineEnd,
                anchor + Mathf.Max(0f, tailSeconds));
            List<OvertakeBattleExchange> exchanges = new()
            {
                new OvertakeBattleExchange(
                    anchor,
                    anchor,
                    firstDriver,
                    secondDriver,
                    OvertakeBattleExchangeKind.Pass)
            };
            return new OvertakeBattleSequence(
                sourceEvent,
                firstDriver,
                secondDriver,
                start,
                end,
                anchor,
                Mathf.Max(
                    start,
                    anchor - Mathf.Max(0f, motionLeadSeconds)),
                false,
                exchanges);
        }

        private static float EstimateCrossing(
            float previousTime,
            float previousGap,
            float time,
            float gap)
        {
            if (Sign(previousGap) == Sign(gap) ||
                Mathf.Approximately(previousGap, gap))
            {
                return time;
            }

            float blend =
                Mathf.Abs(previousGap) /
                Mathf.Max(
                    0.00001f,
                    Mathf.Abs(previousGap) +
                    Mathf.Abs(gap));
            return Mathf.Lerp(previousTime, time, blend);
        }

        private static int Sign(float value)
        {
            if (value > 0.000001f)
                return 1;
            if (value < -0.000001f)
                return -1;
            return 0;
        }

        private static bool TryGetDrivers(
            ReplayEventDto sourceEvent,
            out int firstDriver,
            out int secondDriver)
        {
            firstDriver = 0;
            secondDriver = 0;
            if (sourceEvent == null ||
                sourceEvent.driverNumbers == null ||
                sourceEvent.driverNumbers.Length < 2)
            {
                return false;
            }

            firstDriver = sourceEvent.driverNumbers[0];
            secondDriver = sourceEvent.driverNumbers[1];
            return firstDriver > 0 &&
                secondDriver > 0 &&
                firstDriver != secondDriver;
        }

        private readonly struct DetectedExchange
        {
            public readonly float anchorTime;
            public readonly float confirmedTime;
            public readonly int overtaker;
            public readonly int defender;

            public DetectedExchange(
                float anchorTime,
                float confirmedTime,
                int overtaker,
                int defender)
            {
                this.anchorTime = anchorTime;
                this.confirmedTime = confirmedTime;
                this.overtaker = overtaker;
                this.defender = defender;
            }
        }
    }
}
