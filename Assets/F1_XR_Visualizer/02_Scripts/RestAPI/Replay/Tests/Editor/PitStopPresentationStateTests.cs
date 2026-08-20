#if UNITY_EDITOR
using NUnit.Framework;

namespace F1XR.RestAPI.Replay.Tests
{
    public sealed class PitStopPresentationStateTests
    {
        [Test]
        public void PhaseProgressUsesActivePhaseWindow()
        {
            PitStopSequence sequence = Stop();

            AssertPhase(sequence, 1f, PitStopPhase.Approach, 0.5f);
            AssertPhase(sequence, 2f, PitStopPhase.Brake, 0f);
            AssertPhase(sequence, 2.5f, PitStopPhase.Brake, 0.5f);
            AssertPhase(sequence, 4f, PitStopPhase.Service, 0.5f);
            AssertPhase(sequence, 5f, PitStopPhase.Release, 0f);
            AssertPhase(sequence, 5.5f, PitStopPhase.Release, 0.5f);
            AssertPhase(sequence, 7f, PitStopPhase.Exit, 0.5f);
        }

        [Test]
        public void ServiceTimingClampsBeforeDuringAndAfterService()
        {
            PitStopSequence sequence = Stop();

            PitStopPresentationState before =
                sequence.GetPresentationState(2f);
            PitStopPresentationState during =
                sequence.GetPresentationState(4f);
            PitStopPresentationState after =
                sequence.GetPresentationState(7f);

            Assert.That(before.ServiceTotalSeconds, Is.EqualTo(2f));
            Assert.That(before.ServiceElapsedSeconds, Is.EqualTo(0f));
            Assert.That(before.ServiceProgress, Is.EqualTo(0f));
            Assert.That(during.ServiceTotalSeconds, Is.EqualTo(2f));
            Assert.That(during.ServiceElapsedSeconds, Is.EqualTo(1f));
            Assert.That(during.ServiceProgress, Is.EqualTo(0.5f));
            Assert.That(after.ServiceTotalSeconds, Is.EqualTo(2f));
            Assert.That(after.ServiceElapsedSeconds, Is.EqualTo(2f));
            Assert.That(after.ServiceProgress, Is.EqualTo(1f));
        }

        [Test]
        public void ResultCompletesExactlyAtServiceEnd()
        {
            PitStopSequence sequence = Stop();

            Assert.That(
                sequence.GetPresentationState(4.999f).ResultState,
                Is.EqualTo(PitStopResultState.Pending));
            Assert.That(
                sequence.GetPresentationState(5f).ResultState,
                Is.EqualTo(PitStopResultState.Completed));
            Assert.That(
                sequence.GetPresentationState(7f).ResultState,
                Is.EqualTo(PitStopResultState.Completed));
        }

        [Test]
        public void ReconstructedFlagMirrorsSequenceWithoutChangingTiming()
        {
            PitStopPresentationState authoritative =
                Stop().GetPresentationState(4f);
            PitStopPresentationState reconstructed =
                Stop(true).GetPresentationState(4f);

            Assert.That(authoritative.IsReconstructed, Is.False);
            Assert.That(reconstructed.IsReconstructed, Is.True);
            Assert.That(authoritative.IsDriveThrough, Is.False);
            Assert.That(reconstructed.IsDriveThrough, Is.False);
            Assert.That(reconstructed.Phase, Is.EqualTo(authoritative.Phase));
            Assert.That(
                reconstructed.PhaseProgress,
                Is.EqualTo(authoritative.PhaseProgress));
            Assert.That(
                reconstructed.ServiceElapsedSeconds,
                Is.EqualTo(authoritative.ServiceElapsedSeconds));
            Assert.That(
                reconstructed.ServiceTotalSeconds,
                Is.EqualTo(authoritative.ServiceTotalSeconds));
            Assert.That(
                reconstructed.ResultState,
                Is.EqualTo(authoritative.ResultState));
        }

        [Test]
        public void DriveThroughDoesNotExposeServiceTiming()
        {
            PitStopSequence sequence = DriveThrough();

            AssertPhase(sequence, 2f, PitStopPhase.Approach, 0.5f);
            AssertPhase(sequence, 4f, PitStopPhase.Exit, 0f);
            AssertPhase(sequence, 6f, PitStopPhase.Exit, 0.5f);

            PitStopPresentationState state =
                sequence.GetPresentationState(6f);
            Assert.That(state.ServiceElapsedSeconds, Is.EqualTo(0f));
            Assert.That(state.ServiceTotalSeconds, Is.EqualTo(0f));
            Assert.That(state.ServiceProgress, Is.EqualTo(0f));
            Assert.That(
                state.ResultState,
                Is.EqualTo(PitStopResultState.DriveThrough));
            Assert.That(state.IsDriveThrough, Is.True);
        }

        [Test]
        public void StateRecomputesDeterministicallyAfterSeek()
        {
            PitStopSequence sequence = Stop();

            PitStopPresentationState first =
                sequence.GetPresentationState(5.5f);
            PitStopPresentationState rewound =
                sequence.GetPresentationState(3.5f);
            PitStopPresentationState replayed =
                sequence.GetPresentationState(5.5f);

            Assert.That(first.Phase, Is.EqualTo(PitStopPhase.Release));
            Assert.That(first.PhaseProgress, Is.EqualTo(0.5f));
            Assert.That(
                first.ResultState,
                Is.EqualTo(PitStopResultState.Completed));
            Assert.That(rewound.Phase, Is.EqualTo(PitStopPhase.Service));
            Assert.That(rewound.PhaseProgress, Is.EqualTo(0.25f));
            Assert.That(
                rewound.ResultState,
                Is.EqualTo(PitStopResultState.Pending));
            Assert.That(replayed.Phase, Is.EqualTo(first.Phase));
            Assert.That(
                replayed.PhaseProgress,
                Is.EqualTo(first.PhaseProgress));
            Assert.That(
                replayed.ServiceElapsedSeconds,
                Is.EqualTo(first.ServiceElapsedSeconds));
            Assert.That(
                replayed.ServiceTotalSeconds,
                Is.EqualTo(first.ServiceTotalSeconds));
            Assert.That(replayed.ResultState, Is.EqualTo(first.ResultState));
        }

        private static PitStopSequence Stop(bool reconstructed = false)
        {
            return new PitStopSequence(
                0f,
                2f,
                3f,
                5f,
                6f,
                8f,
                0.9f,
                reconstructed,
                false);
        }

        private static PitStopSequence DriveThrough()
        {
            return new PitStopSequence(
                0f,
                4f,
                4f,
                4f,
                4f,
                8f,
                0.9f,
                false,
                true);
        }

        private static void AssertPhase(
            PitStopSequence sequence,
            float replayTime,
            PitStopPhase expectedPhase,
            float expectedProgress)
        {
            PitStopPresentationState state =
                sequence.GetPresentationState(replayTime);

            Assert.That(state.Phase, Is.EqualTo(expectedPhase));
            Assert.That(
                state.PhaseProgress,
                Is.EqualTo(expectedProgress).Within(0.001f));
        }
    }
}
#endif
