#if UNITY_EDITOR
using F1XR.RestAPI.Replay.Room;
using NUnit.Framework;

namespace F1XR.RestAPI.Replay.Tests
{
    public sealed class PitWallOverlayLayoutTests
    {
        [TestCase(2.5f, 1.6f, PitWallOverlayLayout.Full)]
        [TestCase(4f, 2f, PitWallOverlayLayout.Full)]
        [TestCase(1.8f, 1.3f, PitWallOverlayLayout.Compact)]
        [TestCase(2.49f, 1.6f, PitWallOverlayLayout.Compact)]
        [TestCase(2.5f, 1.59f, PitWallOverlayLayout.Compact)]
        [TestCase(1.79f, 2f, PitWallOverlayLayout.None)]
        [TestCase(3f, 1.29f, PitWallOverlayLayout.None)]
        public void ResolvesFullCompactAndRejectedWalls(
            float width,
            float height,
            PitWallOverlayLayout expected)
        {
            Assert.That(
                PitWallLayoutPolicy.Resolve(width, height),
                Is.EqualTo(expected));
        }
    }
}
#endif
