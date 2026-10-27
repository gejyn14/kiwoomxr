using System;
using System.Collections.Generic;
using NUnit.Framework;
using SpatialTrading.Components;
using SpatialTrading.Domain;

namespace SpatialTrading.Tests
{
    public sealed class PanelViewsTests
    {
        static readonly TimeSpan Kst = TimeSpan.FromHours(9);
        static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 28, 10, 45, 0, Kst);

        static Observation<T> Live<T>(T v) where T : class =>
            new Observation<T>(v, DataState.Live, DataOrigin.Kiwoom, Now, Now);

        static OrderBookSnapshot Book(long[] askQ, long[] bidQ)
        {
            var asks = new List<BookLevel>(); var bids = new List<BookLevel>();
            for (var i = 0; i < askQ.Length; i++) asks.Add(new BookLevel(i + 1, 225000m + i * 500m, askQ[i]));
            for (var i = 0; i < bidQ.Length; i++) bids.Add(new BookLevel(i + 1, 224500m - i * 500m, bidQ[i]));
            return new OrderBookSnapshot("005930", asks, bids);
        }

        [Test]
        public void BookViewOrdersAsksHighToLowAndComputesBidShare()
        {
            var b = Book(new long[] { 10, 10, 10, 8, 8 }, new long[] { 20, 10, 10, 8, 6 });
            var v = OrderBookView.From(Live(b), null, 5, TraderCatalog.Samsung, "NXT");
            Assert.That(v.Title, Is.EqualTo("호가 · 삼성전자 NXT"));
            Assert.That(v.Asks[0].PriceText, Is.EqualTo("227,000"));
            Assert.That(v.Asks[4].PriceText, Is.EqualTo("225,000"));
            Assert.That(v.Bids[0].PriceText, Is.EqualTo("224,500"));
            Assert.That(v.BidShareText, Is.EqualTo("매수 54%"));
            Assert.That(v.Bids[0].BarRatio, Is.EqualTo(1.0));
            Assert.That(v.Asks[0].BarRatio, Is.EqualTo(0.4).Within(1e-9));
        }

        [Test]
        public void BookViewUsesDepthFromLayout()
        {
            var b = Book(new long[] { 1, 1, 1, 1, 1, 1 }, new long[] { 1, 1, 1, 1, 1, 1 });
            var v = OrderBookView.From(Live(b), null, 5, TraderCatalog.Samsung, "KRX");
            Assert.That(v.Asks.Count, Is.EqualTo(5));
            Assert.That(v.Bids.Count, Is.EqualTo(5));
            var v3 = OrderBookView.From(Live(b), null, 3, TraderCatalog.Samsung, "KRX");
            Assert.That(v3.Asks.Count, Is.EqualTo(3));
        }

        [Test]
        public void IndexBookShowsPlaceholder()
        {
            var v = OrderBookView.From(null, null, 5, TraderCatalog.Kospi, "KRX");
            Assert.That(v.IsIndex, Is.True);
            Assert.That(v.Placeholder, Is.EqualTo("지수 · 호가 없음"));
        }

        [Test]
        public void DisconnectedBookKeepsValuesWithLabel()
        {
            var b = Book(new long[] { 5, 5, 5 }, new long[] { 5, 5, 5 });
            var obs = new Observation<OrderBookSnapshot>(b, DataState.Disconnected, DataOrigin.Kiwoom, Now, Now);
            var v = OrderBookView.From(obs, null, 3, TraderCatalog.Samsung, "KRX");
            Assert.That(v.Status, Does.Contain("DISCONNECTED"));
            Assert.That(v.Asks.Count, Is.EqualTo(3));
            Assert.That(v.Asks[2].PriceText, Is.EqualTo("225,000"));
        }

        static NewsItem Item(string title, int h, int m, string[] mentions, bool filing = false, string source = "연합뉴스", int day = 28) =>
            new NewsItem(filing, source, title, new DateTimeOffset(2026, 10, day, h, m, 0, Kst), mentions, filing ? "주요사항보고" : null);

        [Test]
        public void NewsRankBoostsSelectedStockThenRecency()
        {
            var feed = new NewsFeed(new[]
            {
                Item("시장 전체 기사", 10, 42, new string[0]),
                Item("삼성전자 신제품", 9, 10, new[] { "005930" }),
                Item("삼성전자 전날 기사", 15, 0, new[] { "005930" }, day: 27),
                Item("하이닉스", 10, 40, new[] { "000660" }),
            });
            var rows = NewsView.Rank(Live(feed), "005930");
            Assert.That(rows[0].Title, Is.EqualTo("삼성전자 신제품"));
            Assert.That(rows[1].Title, Is.EqualTo("삼성전자 전날 기사"));
            Assert.That(rows[2].Title, Is.EqualTo("시장 전체 기사"));
            Assert.That(rows[0].TimeText, Is.EqualTo("09:10"));
            Assert.That(rows[1].TimeText, Is.EqualTo("10/27"));
            Assert.That(NewsView.Rank(Live(feed), "005930", 2).Count, Is.EqualTo(2));
        }

        [Test]
        public void FilingsAreTaggedForAmber()
        {
            var feed = new NewsFeed(new[]
            {
                Item("자기주식 취득", 10, 0, new[] { "005930" }, filing: true, source: "DART"),
                Item("일반 기사", 9, 0, new[] { "005930" }, source: "연합뉴스"),
            });
            var rows = NewsView.Rank(Live(feed), "005930");
            Assert.That(rows[0].IsFiling, Is.True);
            Assert.That(rows[0].Tag, Is.EqualTo("공시"));
            Assert.That(rows[1].IsFiling, Is.False);
            Assert.That(rows[1].Tag, Is.EqualTo("연합뉴스"));
        }

        [Test]
        public void EmptyBalanceIsDesignedState()
        {
            var bal = new BalanceSnapshot("123*****90", 0m, 0m, 0m, 1032000m, new Holding[0]);
            var v = BalanceView.From(Live(bal));
            Assert.That(v.IsEmpty, Is.True);
            Assert.That(v.EmptyText, Is.EqualTo("보유 종목 없음"));
            Assert.That(v.OrderableText, Is.EqualTo("주문가능 1,032,000원"));
            Assert.That(v.AccountText, Is.EqualTo("계좌 123*****90"));
            var full = new BalanceSnapshot("123*****90", 224000m, -500m, -0.22m, 1032000m,
                new[] { new Holding("005930", "삼성전자", 1, 224500m, 224000m, -500m, -0.22m) });
            var r = BalanceView.From(Live(full)).Rows[0];
            Assert.That(r.Text, Is.EqualTo("삼성전자 1주 · 평단 224,500"));
            Assert.That(r.ProfitText, Is.EqualTo("−500 (−0.22%)"));
        }

        [Test]
        public void ChangeTextUsesSigns()
        {
            var up = new QuoteSnapshot("005930", 225000m, 500m, null, 100);
            var dn = new QuoteSnapshot("005930", 224000m, -1200m, -0.53m, 100);
            var empty = Live(new CandleSeries("005930", "D", new Candle[0]));
            Assert.That(ChartHeaderView.From(TraderCatalog.Samsung, empty, Live(up), "일봉", "KRX").ChangeText, Is.EqualTo("+500"));
            var h = ChartHeaderView.From(TraderCatalog.Samsung, empty, Live(dn), "일봉", "KRX");
            Assert.That(h.ChangeText, Is.EqualTo("−1,200 (−0.53%)"));
            Assert.That(h.PriceText, Is.EqualTo("224,000"));
            Assert.That(h.Status, Is.EqualTo("LIVE · KRX · 일봉"));
        }

        [Test]
        public void ChartHeaderFallsBackToLastCloseWhenQuoteUnavailable()
        {
            var s = new CandleSeries("005930", "D", new[]
            {
                new Candle("20261026", 220000m, 226000m, 219000m, 224000m, 1, true),
                new Candle("20261027", 224000m, 226000m, 223000m, 225000m, 1, true),
            });
            var series = new Observation<CandleSeries>(s, DataState.Stale, DataOrigin.Kiwoom, Now, Now, StatusText.MarketClosed);
            var q = new Observation<QuoteSnapshot>(null, DataState.Unavailable, DataOrigin.Kiwoom, null);
            var h = ChartHeaderView.From(TraderCatalog.Samsung, series, q, "일봉", "KRX");
            Assert.That(h.PriceText, Is.EqualTo("225,000"));
            Assert.That(h.Status, Is.EqualTo("장 마감 · KRX · 일봉"));
            Assert.That(h.ChangeText, Is.EqualTo("+1,000 (+0.45%)"));
        }

        [Test]
        public void OrderViewShowsLockedReasonForOtherStocks()
        {
            var t = new OrderTicket();
            t.Select(TraderCatalog.SkHynix, TradingSession.KrxRegular);
            var v = OrderView.From(t, null, TradingSession.KrxRegular);
            Assert.That(v.LockReason, Is.EqualTo("시연 안전장치: 삼성전자만"));
            Assert.That(v.PrepareEnabled, Is.False);
            Assert.That(v.Types.Count, Is.EqualTo(5));
            foreach (var b in v.Types) { Assert.That(b.Enabled, Is.False); Assert.That(b.Reason, Is.EqualTo(v.LockReason)); }
            Assert.That(v.QuantityText, Is.EqualTo("1주 🔒"));
            Assert.That(v.SellTabText, Is.EqualTo("매도 🔒"));
        }

        [Test]
        public void OrderViewShowsSessionAvailabilityAndPrice()
        {
            var t = new OrderTicket();
            t.Select(TraderCatalog.Samsung, TradingSession.Nxt);
            var book = Live(Book(new long[] { 1 }, new long[] { 1 }));
            var v = OrderView.From(t, book, TradingSession.Nxt);
            Assert.That(v.Types[0].Label, Is.EqualTo("지정가"));
            Assert.That(v.Types[0].Selected, Is.True);
            Assert.That(v.Types[1].Enabled, Is.False);
            Assert.That(v.Types[1].Reason, Is.EqualTo("NXT 미지원"));
            Assert.That(v.Types[4].Reason, Is.EqualTo("시연 범위 밖"));
            Assert.That(v.PriceText, Is.EqualTo("225,000"));
            Assert.That(v.PrepareEnabled, Is.True);
            Assert.That(v.LockReason, Is.Null);
            t.Select(TraderCatalog.Samsung, TradingSession.KrxRegular);
            var m = OrderView.From(t, book, TradingSession.KrxRegular, Live(new QuoteSnapshot("005930", 224500m, null, null, null)));
            Assert.That(m.PriceText, Is.EqualTo("시장가"));
            Assert.That(m.PrepareEnabled, Is.True);
        }
    }
}
