using System;
using NUnit.Framework;
using SpatialTrading.Domain;
using SpatialTrading.Market;

namespace SpatialTrading.Tests
{
    public sealed class StreamOverlayTests
    {
        private static readonly DateTimeOffset At = new DateTimeOffset(2026, 10, 27, 10, 0, 0, TimeSpan.FromHours(9));
        private static readonly QuoteSnapshot Value = new QuoteSnapshot("005930", 78800, -600, -.76m, 1000);
        private static Observation<QuoteSnapshot> Raw(DataState state, string reason = null) =>
            state == DataState.Live || state == DataState.Delayed || state == DataState.Stale || state == DataState.Disconnected
                ? new Observation<QuoteSnapshot>(Value, state, DataOrigin.Kiwoom, At, At, reason)
                : new Observation<QuoteSnapshot>(null, state, DataOrigin.Kiwoom, null, reason: reason);

        [Test] public void LinkReasonWinsOverBrokerAndHeartbeatAndKeepsValues()
        {
            var view = StreamOverlay.Apply(Raw(DataState.Live), "GATEWAY_UNREACHABLE", "KIWOOM_AUTH", true);
            Assert.That(view.State, Is.EqualTo(DataState.Disconnected));
            Assert.That(view.Reason, Is.EqualTo("GATEWAY_UNREACHABLE"));
            Assert.That(view.Value, Is.SameAs(Value));
            Assert.That(view.ReceivedAt, Is.EqualTo(At));
        }

        [Test] public void LinkReasonWithoutCachedValueIsAnEmptyDisconnect()
        {
            var view = StreamOverlay.Apply<QuoteSnapshot>(null, "GATEWAY_UNAUTHORIZED", null, false);
            Assert.That(view.State, Is.EqualTo(DataState.Disconnected));
            Assert.That(view.Reason, Is.EqualTo("GATEWAY_UNAUTHORIZED"));
            Assert.That(view.Value, Is.Null);
            Assert.That(view.Origin, Is.EqualTo(DataOrigin.Kiwoom));
        }

        [Test] public void NoCachedValueOnAHealthyLinkIsLoading()
        {
            var view = StreamOverlay.Apply<QuoteSnapshot>(null, null, "KIWOOM_AUTH", true);
            Assert.That(view.State, Is.EqualTo(DataState.Loading));
            Assert.That(view.Value, Is.Null);
        }

        [TestCase(DataState.Live)][TestCase(DataState.Delayed)][TestCase(DataState.Stale)]
        public void BrokerReasonBeatsHeartbeatLoss(DataState state)
        {
            var view = StreamOverlay.Apply(Raw(state), null, "KIWOOM_WS_CLOSED", true);
            Assert.That(view.State, Is.EqualTo(DataState.Disconnected));
            Assert.That(view.Reason, Is.EqualTo("KIWOOM_WS_CLOSED"));
            Assert.That(view.Value, Is.SameAs(Value));
        }

        [TestCase(DataState.Live)][TestCase(DataState.Delayed)]
        public void HeartbeatLossMarksLiveAndSnapshotStale(DataState state)
        {
            var view = StreamOverlay.Apply(Raw(state, state == DataState.Delayed ? "MARKET_CLOSED" : null), null, null, true);
            Assert.That(view.State, Is.EqualTo(DataState.Stale));
            Assert.That(view.Reason, Is.EqualTo(StreamOverlay.HeartbeatLost));
            Assert.That(view.Value, Is.SameAs(Value));
            Assert.That(view.SourceAt, Is.EqualTo(At));
        }

        [TestCase(DataState.Unavailable)][TestCase(DataState.Loading)]
        public void ServerStatesWithoutValuesPassThroughBrokerAndHeartbeatOverlays(DataState state)
        {
            var raw = Raw(state, "MARKET_CLOSED");
            Assert.That(StreamOverlay.Apply(raw, null, "KIWOOM_AUTH", true), Is.SameAs(raw));
        }

        [Test] public void HealthyLinkReturnsTheServerObservation()
        {
            var raw = Raw(DataState.Delayed, "MARKET_CLOSED");
            Assert.That(StreamOverlay.Apply(raw, null, null, false), Is.SameAs(raw));
        }
    }
}
