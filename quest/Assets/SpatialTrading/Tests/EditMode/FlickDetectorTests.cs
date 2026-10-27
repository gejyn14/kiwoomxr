using NUnit.Framework;
using SpatialTrading.Domain;

namespace SpatialTrading.Tests
{
    public sealed class FlickDetectorTests
    {
        [Test]
        public void FastSidewaysReleaseIsFlick()
        {
            var f = new FlickDetector();
            f.Begin(0f, 0f, 1f, 0f);
            f.Sample(0.1f, 0.2f, 1f, 0f);
            Assert.That(f.ReleaseIsFlick(0.1f), Is.True);
        }

        [Test]
        public void ForwardZMotionCountsAsHorizontal()
        {
            var f = new FlickDetector();
            f.Begin(0f, 0f, 1f, 0f);
            f.Sample(0.1f, 0f, 1f, 0.2f);
            Assert.That(f.ReleaseIsFlick(0.1f), Is.True);
        }

        [Test]
        public void SlowDragIsNotFlick()
        {
            var f = new FlickDetector();
            f.Begin(0f, 0f, 1f, 0f);
            f.Sample(1f, 0.3f, 1f, 0f);
            Assert.That(f.ReleaseIsFlick(1f), Is.False);
        }

        [Test]
        public void ShortJerkIsNotFlick()
        {
            var f = new FlickDetector();
            f.Begin(0f, 0f, 1f, 0f);
            f.Sample(0.03f, 0.05f, 1f, 0f); // 1.67 m/s but only 5 cm
            Assert.That(f.ReleaseIsFlick(0.03f), Is.False);
        }

        [Test]
        public void VerticalOnlyIsNotFlick()
        {
            var f = new FlickDetector();
            f.Begin(0f, 0f, 1f, 0f);
            f.Sample(0.1f, 0f, 1.3f, 0f);
            Assert.That(f.ReleaseIsFlick(0.1f), Is.False);
        }

        [Test]
        public void FewerThanTwoSamplesInWindowIsNotFlick()
        {
            var f = new FlickDetector();
            Assert.That(f.ReleaseIsFlick(0f), Is.False);
            f.Begin(0f, 0f, 0f, 0f);
            Assert.That(f.ReleaseIsFlick(0f), Is.False);
            f.Sample(1f, 5f, 0f, 0f);
            Assert.That(f.ReleaseIsFlick(1f), Is.False); // earlier sample is outside the 0.15 s window
        }

        [Test]
        public void OnlyTheLastWindowCounts()
        {
            var f = new FlickDetector();
            f.Begin(0f, 0f, 0f, 0f);
            f.Sample(0.1f, 1f, 0f, 0f);   // fast early
            f.Sample(1.0f, 1.0f, 0f, 0f);  // then still
            f.Sample(1.1f, 1.0f, 0f, 0f);
            Assert.That(f.ReleaseIsFlick(1.1f), Is.False);
        }

        [Test]
        public void BeginResetsPreviousGrab()
        {
            var f = new FlickDetector();
            f.Begin(0f, 0f, 0f, 0f);
            f.Sample(0.1f, 0.5f, 0f, 0f);
            f.Begin(5f, 0f, 0f, 0f);
            Assert.That(f.ReleaseIsFlick(5f), Is.False);
        }
    }
}
