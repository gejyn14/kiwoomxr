using System;
using NUnit.Framework;
using SpatialTrading.Domain;

namespace SpatialTrading.Tests
{
    public sealed class OrderTicketTests
    {
        static readonly TimeSpan Kst = TimeSpan.FromHours(9);
        static DateTimeOffset At(int day, int h, int m) => new DateTimeOffset(2026, 10, day, h, m, 0, Kst);

        [Test]
        public void SamsungInRegularSessionDefaultsToMarketOrder()
        {
            var t = new OrderTicket();
            t.Select(TraderCatalog.Samsung, TradingSession.KrxRegular);
            Assert.That(t.State, Is.EqualTo(TicketState.Idle));
            Assert.That(t.Type, Is.EqualTo(OrderType.Market));
        }

        [Test]
        public void NxtDefaultsToLimitAtBestAsk()
        {
            var t = new OrderTicket();
            t.Select(TraderCatalog.Samsung, TradingSession.Nxt);
            Assert.That(t.TryPrepare(225000m, 224500m, out var p), Is.True);
            Assert.That(t.State, Is.EqualTo(TicketState.Preflight));
            Assert.That(p.Type, Is.EqualTo(OrderType.Limit));
            Assert.That(p.Price, Is.EqualTo(225000m));
            Assert.That(p.KiwoomTradeType, Is.EqualTo("0"));
            Assert.That(p.Market, Is.EqualTo("NXT"));
            Assert.That(p.Side, Is.EqualTo("매수"));
            Assert.That(p.Quantity, Is.EqualTo(1));
            Assert.That(p.Badge, Is.EqualTo("PREFLIGHT"));
            Assert.That(p.Code, Is.EqualTo("005930"));
        }

        [Test]
        public void MarketOrderPreviewHasNoPriceAndKrxMarket()
        {
            var t = new OrderTicket();
            t.Select(TraderCatalog.Samsung, TradingSession.KrxRegular);
            Assert.That(t.TryPrepare(225000m, 224500m, out var p), Is.True);
            Assert.That(p.Price, Is.Null);
            Assert.That(p.KiwoomTradeType, Is.EqualTo("3"));
            Assert.That(p.Market, Is.EqualTo("KRX"));
        }

        [Test]
        public void LimitWithoutBestAskCannotPrepare()
        {
            var t = new OrderTicket();
            t.Select(TraderCatalog.Samsung, TradingSession.Nxt);
            Assert.That(t.TryPrepare(null, 224500m, out _), Is.False);
            Assert.That(t.State, Is.EqualTo(TicketState.Idle));
        }

        [Test]
        public void OtherStockLocksWithReasonWhateverTypeIsChosen()
        {
            var t = new OrderTicket();
            t.Select(TraderCatalog.SkHynix, TradingSession.KrxRegular);
            Assert.That(t.State, Is.EqualTo(TicketState.Locked));
            Assert.That(t.LockReason, Is.EqualTo("시연 안전장치: 삼성전자만"));
            Assert.That(t.TryChooseType(OrderType.Limit), Is.False);
            Assert.That(t.State, Is.EqualTo(TicketState.Locked));
            Assert.That(t.TryPrepare(1000m, 1000m, out _), Is.False);
        }

        [Test]
        public void KospiLocksAsIndex()
        {
            var t = new OrderTicket();
            t.Select(TraderCatalog.Kospi, TradingSession.KrxRegular);
            Assert.That(t.State, Is.EqualTo(TicketState.Locked));
            Assert.That(t.LockReason, Is.EqualTo("지수 · 주문 불가"));
        }

        [Test]
        public void ConditionalIsAlwaysDisabled()
        {
            foreach (TradingSession s in Enum.GetValues(typeof(TradingSession)))
            {
                Assert.That(OrderTypeTable.Enabled(OrderType.Conditional, s), Is.False);
                Assert.That(OrderTypeTable.DisabledReason(OrderType.Conditional, s), Is.EqualTo("시연 범위 밖"));
            }
        }

        [Test]
        public void NxtDisablesMarketWithReason()
        {
            Assert.That(OrderTypeTable.Enabled(OrderType.Market, TradingSession.Nxt), Is.False);
            Assert.That(OrderTypeTable.DisabledReason(OrderType.Market, TradingSession.Nxt), Is.EqualTo("NXT 미지원"));
            Assert.That(OrderTypeTable.Enabled(OrderType.Limit, TradingSession.Nxt), Is.True);
            Assert.That(OrderTypeTable.Default(TradingSession.Nxt), Is.EqualTo(OrderType.Limit));
        }

        [Test]
        public void ClosedDisablesEverythingWithReason()
        {
            foreach (OrderType o in Enum.GetValues(typeof(OrderType)))
                Assert.That(OrderTypeTable.Enabled(o, TradingSession.Closed), Is.False);
            Assert.That(OrderTypeTable.DisabledReason(OrderType.Limit, TradingSession.Closed), Is.EqualTo("장 마감"));
        }

        [Test]
        public void KrxEnablesFourTypes()
        {
            var s = TradingSession.KrxRegular;
            Assert.That(OrderTypeTable.Enabled(OrderType.Limit, s));
            Assert.That(OrderTypeTable.Enabled(OrderType.Market, s));
            Assert.That(OrderTypeTable.Enabled(OrderType.BestLimit, s));
            Assert.That(OrderTypeTable.Enabled(OrderType.PriorityLimit, s));
            Assert.That(OrderTypeTable.DisabledReason(OrderType.Market, s), Is.Null);
        }

        [Test]
        public void ChangingInstrumentOrDismissCancelsPreflight()
        {
            var t = new OrderTicket();
            t.Select(TraderCatalog.Samsung, TradingSession.KrxRegular);
            Assert.That(t.TryPrepare(225000m, 224500m, out _), Is.True);
            t.Select(TraderCatalog.SkHynix, TradingSession.KrxRegular);
            Assert.That(t.State, Is.Not.EqualTo(TicketState.Preflight));
            t.Select(TraderCatalog.Samsung, TradingSession.KrxRegular);
            Assert.That(t.TryPrepare(225000m, 224500m, out _), Is.True);
            t.Cancel();
            Assert.That(t.State, Is.EqualTo(TicketState.Idle));
        }

        [Test]
        public void ClosedSessionCannotPrepare()
        {
            var t = new OrderTicket();
            t.Select(TraderCatalog.Samsung, TradingSession.Closed);
            Assert.That(t.TryPrepare(225000m, 224500m, out _), Is.False);
        }

        [Test]
        public void QuantityIsAlwaysOneAndSellLocked()
        {
            var t = new OrderTicket();
            Assert.That(t.Quantity, Is.EqualTo(1));
            Assert.That(t.SellLocked, Is.True);
        }

        [Test]
        public void SessionWindowsMatchGateway()
        {
            Assert.That(TradingSessions.SessionAt(At(28, 7, 59)), Is.EqualTo(TradingSession.Closed));
            Assert.That(TradingSessions.SessionAt(At(28, 8, 0)), Is.EqualTo(TradingSession.Nxt));
            Assert.That(TradingSessions.SessionAt(At(28, 8, 49)), Is.EqualTo(TradingSession.Nxt));
            Assert.That(TradingSessions.SessionAt(At(28, 8, 55)), Is.EqualTo(TradingSession.Closed));
            Assert.That(TradingSessions.SessionAt(At(28, 9, 0)), Is.EqualTo(TradingSession.KrxRegular));
            Assert.That(TradingSessions.SessionAt(At(28, 15, 29)), Is.EqualTo(TradingSession.KrxRegular));
            Assert.That(TradingSessions.SessionAt(At(28, 15, 30)), Is.EqualTo(TradingSession.Nxt));
            Assert.That(TradingSessions.SessionAt(At(28, 19, 59)), Is.EqualTo(TradingSession.Nxt));
            Assert.That(TradingSessions.SessionAt(At(28, 20, 0)), Is.EqualTo(TradingSession.Closed));
            Assert.That(TradingSessions.SessionAt(At(31, 10, 0)), Is.EqualTo(TradingSession.Closed)); // Saturday
            Assert.That(TradingSessions.SessionAt(At(25, 10, 0)), Is.EqualTo(TradingSession.Closed)); // Sunday
        }

        [Test]
        public void SessionConvertsAnyOffsetToKst()
        {
            // 00:30 UTC Wednesday = 09:30 KST
            var utc = new DateTimeOffset(2026, 10, 28, 0, 30, 0, TimeSpan.Zero);
            Assert.That(TradingSessions.SessionAt(utc), Is.EqualTo(TradingSession.KrxRegular));
        }
    }
}
