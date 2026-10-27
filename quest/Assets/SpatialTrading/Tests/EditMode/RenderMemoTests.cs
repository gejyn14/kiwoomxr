using System;
using NUnit.Framework;
using SpatialTrading.Components;
using SpatialTrading.Domain;

namespace SpatialTrading.Tests
{
    public sealed class RenderMemoTests
    {
        static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 28, 10, 45, 0, TimeSpan.FromHours(9));

        static Observation<QuoteSnapshot> Quote(decimal price, DataState state = DataState.Live, string reason = null) =>
            new Observation<QuoteSnapshot>(new QuoteSnapshot("005930", price, null, null, null), state, DataOrigin.Kiwoom, Now, Now, reason);

        static readonly PanelRect Rect = new PanelRect(Plane.Eye, 0, 0, 4, 3);

        [Test]
        public void SameInputsSkipTheRender()
        {
            var memo = new RenderMemo<PanelId>();
            var q = Quote(225000m);
            Assert.That(memo.Unchanged(PanelId.OrderBook, RenderMemo.Of(q), Rect, PanelId.ChartSamsung), Is.False);
            memo.Remember(PanelId.OrderBook, RenderMemo.Of(q), Rect, PanelId.ChartSamsung);
            Assert.That(memo.Unchanged(PanelId.OrderBook, RenderMemo.Of(q), Rect, PanelId.ChartSamsung), Is.True);
            Assert.That(memo.Unchanged(PanelId.News, RenderMemo.Of(q), Rect, PanelId.ChartSamsung), Is.False); // per panel
        }

        [Test]
        public void ANewObservationRendersEvenWithTheSamePrice()
        {
            var memo = new RenderMemo<PanelId>();
            memo.Remember(PanelId.OrderBook, RenderMemo.Of(Quote(225000m)));
            Assert.That(memo.Unchanged(PanelId.OrderBook, RenderMemo.Of(Quote(225000m))), Is.False);
        }

        [Test]
        public void AnOverlayCopyWithTheSameValueAndStatusSkipsButAStatusChangeRenders()
        {
            var memo = new RenderMemo<PanelId>();
            var raw = Quote(225000m);
            var down = new Observation<QuoteSnapshot>(raw.Value, DataState.Disconnected, raw.Origin, raw.ReceivedAt, raw.SourceAt, "GATEWAY_UNREACHABLE");
            var again = new Observation<QuoteSnapshot>(raw.Value, DataState.Disconnected, raw.Origin, raw.ReceivedAt, raw.SourceAt, "GATEWAY_UNREACHABLE");
            memo.Remember(PanelId.Order, RenderMemo.Of(raw));
            Assert.That(memo.Unchanged(PanelId.Order, RenderMemo.Of(down)), Is.False);
            memo.Remember(PanelId.Order, RenderMemo.Of(down));
            Assert.That(memo.Unchanged(PanelId.Order, RenderMemo.Of(again)), Is.True);
            Assert.That(memo.Unchanged(PanelId.Order, RenderMemo.Of(raw)), Is.False);
        }

        [Test]
        public void RectSelectionSessionIntervalOrTicketChangesRender()
        {
            var memo = new RenderMemo<PanelId>();
            var q = RenderMemo.Of(Quote(1m));
            object[] Inputs(PanelRect r, PanelId sel, TradingSession s, string interval, TicketState t) =>
                new object[] { q, r, sel, s, interval, t };
            memo.Remember(PanelId.ChartSamsung, Inputs(Rect, PanelId.ChartSamsung, TradingSession.KrxRegular, "1D", TicketState.Idle));
            Assert.That(memo.Unchanged(PanelId.ChartSamsung, Inputs(Rect, PanelId.ChartSamsung, TradingSession.KrxRegular, "1D", TicketState.Idle)), Is.True);
            Assert.That(memo.Unchanged(PanelId.ChartSamsung, Inputs(new PanelRect(Plane.Eye, 0, 0, 5, 3), PanelId.ChartSamsung, TradingSession.KrxRegular, "1D", TicketState.Idle)), Is.False);
            Assert.That(memo.Unchanged(PanelId.ChartSamsung, Inputs(Rect, PanelId.ChartKiwoom, TradingSession.KrxRegular, "1D", TicketState.Idle)), Is.False);
            Assert.That(memo.Unchanged(PanelId.ChartSamsung, Inputs(Rect, PanelId.ChartSamsung, TradingSession.Nxt, "1D", TicketState.Idle)), Is.False);
            Assert.That(memo.Unchanged(PanelId.ChartSamsung, Inputs(Rect, PanelId.ChartSamsung, TradingSession.KrxRegular, "4H", TicketState.Idle)), Is.False);
            Assert.That(memo.Unchanged(PanelId.ChartSamsung, Inputs(Rect, PanelId.ChartSamsung, TradingSession.KrxRegular, "1D", TicketState.Preflight)), Is.False);
        }

        [Test]
        public void ForgetForcesTheNextRender()
        {
            var memo = new RenderMemo<PanelId>();
            memo.Remember(PanelId.News, "x");
            memo.Forget(PanelId.News);
            Assert.That(memo.Unchanged(PanelId.News, "x"), Is.False);
            memo.Remember(PanelId.News, "x");
            memo.Clear();
            Assert.That(memo.Unchanged(PanelId.News, "x"), Is.False);
        }
    }
}
