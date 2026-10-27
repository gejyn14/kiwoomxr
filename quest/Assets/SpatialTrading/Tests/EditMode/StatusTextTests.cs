using NUnit.Framework;
using SpatialTrading.Components;
using SpatialTrading.Domain;

namespace SpatialTrading.Tests
{
    public sealed class StatusTextTests
    {
        [TestCase(DataState.Delayed)][TestCase(DataState.Unavailable)]
        public void MarketClosedReadsAsMarketClose(DataState state) =>
            Assert.That(StatusText.State(state, "MARKET_CLOSED"), Is.EqualTo("장 마감"));

        [Test] public void OtherReasonsUseTheStateName()
        {
            Assert.That(StatusText.State(DataState.Live, null), Is.EqualTo("실시간"));
            Assert.That(StatusText.State(DataState.Stale, "GATEWAY_HEARTBEAT_LOST"), Is.EqualTo("오래된 데이터"));
            Assert.That(StatusText.State(DataState.Disconnected, "KIWOOM_AUTH"), Is.EqualTo("연결 끊김"));
        }

        [TestCase("GATEWAY_UNREACHABLE")][TestCase("GATEWAY_HEARTBEAT_LOST")][TestCase("KIWOOM_AUTH")]
        [TestCase("KIWOOM_WS_CLOSED")][TestCase("BROKER_DISCONNECTED")]
        public void ConnectionAdviceOnlyForLinkAndBrokerReasons(string reason) =>
            Assert.That(StatusText.Hint(reason), Is.EqualTo("연결 상태를 확인해 주세요."));

        [TestCase(null)][TestCase("MARKET_CLOSED")][TestCase("NOT_SUBSCRIBED")]
        public void NoConnectionAdviceForOtherReasons(string reason) =>
            Assert.That(StatusText.Hint(reason), Does.Not.Contain("연결"));

        [Test] public void BookCaptionCarriesTheChartVenue()
        {
            Assert.That(StatusText.BookCaption("NXT", DataState.Live, null), Is.EqualTo("NXT · 실시간 · 상위 3호가"));
            Assert.That(StatusText.BookCaption("KRX", DataState.Delayed, "MARKET_CLOSED"), Is.EqualTo("KRX · 장 마감 · 상위 3호가"));
            Assert.That(StatusText.BookCaption(null, DataState.Live, null), Is.EqualTo("실시간 · 상위 3호가"));
        }
    }
}
