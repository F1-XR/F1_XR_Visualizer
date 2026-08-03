using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    internal enum OvertakeCompletionResult
    {
        None,
        Reset,
        Triggered,
        Suppressed
    }

    internal sealed class OvertakeCompletionDetector
    {
        private OvertakeCompletionVfxSettings settings;
        private float lastReplayTime = float.NaN;
        private float candidateStartTime = float.NaN;
        private bool observedBeforeCompletion;
        private bool triggered;

        public bool HasTriggered => triggered;

        public void Configure(
            OvertakeCompletionVfxSettings completionSettings)
        {
            settings = completionSettings;
            Reset();
        }

        public void Reset()
        {
            lastReplayTime = float.NaN;
            ResetDetectionState();
        }

        public OvertakeCompletionResult Update(
            float replayTime,
            float clearanceDistance,
            float centerLeadDistance,
            float referenceVehicleLength,
            bool orderingConfirmed)
        {
            if (settings == null || !settings.enabled)
                return OvertakeCompletionResult.None;

            float vehicleLength =
                Mathf.Max(0.001f, referenceVehicleLength);
            float threshold =
                Mathf.Max(0f, settings.clearanceInCarLengths) *
                vehicleLength;
            float resetThreshold =
                threshold -
                Mathf.Max(0f, settings.hysteresisInCarLengths) *
                vehicleLength;
            float orderingLeadThreshold =
                Mathf.Max(
                    0f,
                    settings.orderingLeadInCarLengths) *
                vehicleLength;
            float orderingLeadResetThreshold =
                orderingLeadThreshold -
                Mathf.Max(
                    0f,
                    settings.hysteresisInCarLengths) *
                vehicleLength;
            bool physicalCompletion =
                clearanceDistance >= threshold;
            bool orderingLeadCompletion =
                settings.allowOrderingLeadFallback &&
                orderingConfirmed &&
                centerLeadDistance >= orderingLeadThreshold;
            bool completionReached =
                physicalCompletion ||
                orderingLeadCompletion;
            bool physicalCompletionReset =
                clearanceDistance < resetThreshold;
            bool orderingLeadCompletionReset =
                !settings.allowOrderingLeadFallback ||
                !orderingConfirmed ||
                centerLeadDistance <
                orderingLeadResetThreshold;
            bool completionReset =
                physicalCompletionReset &&
                orderingLeadCompletionReset;
            bool hasPreviousTime =
                !float.IsNaN(lastReplayTime);
            bool backwardSeek =
                hasPreviousTime &&
                replayTime < lastReplayTime;
            bool forwardSeek =
                hasPreviousTime &&
                replayTime - lastReplayTime >
                settings.seekResetThresholdSeconds;

            if (backwardSeek || forwardSeek)
            {
                ResetDetectionState();
                lastReplayTime = replayTime;

                if (forwardSeek &&
                    settings.suppressForwardSeekConfirmation &&
                    completionReached)
                {
                    triggered = true;
                    return OvertakeCompletionResult.Suppressed;
                }

                if (!completionReached)
                    observedBeforeCompletion = true;

                return OvertakeCompletionResult.Reset;
            }

            lastReplayTime = replayTime;
            if (triggered)
                return OvertakeCompletionResult.None;

            if (!observedBeforeCompletion &&
                !completionReached)
            {
                observedBeforeCompletion = true;
            }

            if (completionReset)
            {
                candidateStartTime = float.NaN;
                return OvertakeCompletionResult.None;
            }

            if (!observedBeforeCompletion ||
                !completionReached)
            {
                return OvertakeCompletionResult.None;
            }

            if (float.IsNaN(candidateStartTime))
                candidateStartTime = replayTime;

            if (replayTime - candidateStartTime <
                Mathf.Max(
                    0f,
                    settings.stabilityDurationReplaySeconds))
            {
                return OvertakeCompletionResult.None;
            }

            triggered = true;
            return OvertakeCompletionResult.Triggered;
        }

        private void ResetDetectionState()
        {
            candidateStartTime = float.NaN;
            observedBeforeCompletion = false;
            triggered = false;
        }
    }
}
