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
            var rows = NewsView.Rank(Live(feed), "005930", Now);
            Assert.That(rows[0].Title, Is.EqualTo("삼성전자 신제품"));
            Assert.That(rows[1].Title, Is.EqualTo("삼성전자 전날 기사"));
            Assert.That(rows[2].Title, Is.EqualTo("시장 전체 기사"));
            Assert.That(rows[0].TimeText, Is.EqualTo("09:10"));
            Assert.That(rows[1].TimeText, Is.EqualTo("10/27"));
            Assert.That(NewsView.Rank(Live(feed), "005930", Now, 2).Count, Is.EqualTo(2));
        }

        [Test]
        public void FilingsAreTaggedForAmber()
        {
            var feed = new NewsFeed(new[]
            {
                Item("자기주식 취득", 10, 0, new[] { "005930" }, filing: true, source: "DART"),
                Item("일반 기사", 9, 0, new[] { "005930" }, source: "연합뉴스"),
            });
            var rows = NewsView.Rank(Live(feed), "005930", Now);
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
            Assert.That(ChartHeaderView.From(TraderCatalog.Samsung, empty, Live(up), "일봉", "KRX", "KRX").ChangeText, Is.EqualTo("+500"));
            var h = ChartHeaderView.From(TraderCatalog.Samsung, empty, Live(dn), "일봉", "KRX", "KRX");
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
            var h = ChartHeaderView.From(TraderCatalog.Samsung, series, q, "일봉", "KRX", null);
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

        [Test]
        public void OrderTypeButtonsCarryTheirTypeInDisplayOrder()
        {
            var t = new OrderTicket();
            t.Select(TraderCatalog.Samsung, TradingSession.KrxRegular);
            var v = OrderView.From(t, null, TradingSession.KrxRegular);
            var expected = new[] { OrderType.Limit, OrderType.Market, OrderType.BestLimit, OrderType.PriorityLimit, OrderType.Conditional };
            for (var i = 0; i < expected.Length; i++)
            {
                Assert.That(v.Types[i].Type, Is.EqualTo(expected[i]));
                Assert.That(v.Types[i].Label, Is.EqualTo(OrderTypeTable.DisplayName(expected[i])));
            }
        }

        [Test]
        public void OrderViewExposesLocksWithoutTheGlyph()
        {
            var t = new OrderTicket();
            t.Select(TraderCatalog.Samsung, TradingSession.KrxRegular);
            var v = OrderView.From(t, null, TradingSession.KrxRegular);
            Assert.That(v.SellLocked, Is.True);
            Assert.That(v.QuantityLocked, Is.True);
            Assert.That(v.SellTabLabel, Is.EqualTo("매도"));
            Assert.That(v.QuantityLabel, Is.EqualTo("1주"));
            Assert.That(v.SellTabText, Is.EqualTo("매도 🔒"));
            Assert.That(v.QuantityText, Is.EqualTo("1주 🔒"));
        }

        [Test]
        public void NewsStatusSeparatesUnavailableFeedFromEmptyFeed()
        {
            var empty = Live(new NewsFeed(new NewsItem[0]));
            Assert.That(NewsView.Status(empty), Is.EqualTo("실시간"));
            Assert.That(NewsView.Rank(empty, "005930", Now).Count, Is.EqualTo(0));
            var down = new Observation<NewsFeed>(null, DataState.Unavailable, DataOrigin.Kiwoom, null);
            Assert.That(NewsView.Status(down), Is.EqualTo("데이터 이용 불가"));
            Assert.That(NewsView.Status(null), Is.EqualTo("데이터 이용 불가"));
            var closed = new Observation<NewsFeed>(new NewsFeed(new NewsItem[0]), DataState.Stale, DataOrigin.Kiwoom, Now, Now, StatusText.MarketClosed);
            Assert.That(NewsView.Status(closed), Is.EqualTo("장 마감"));
        }

        [Test]
        public void NewsTimeTextUsesGivenNowNotReceiptTime()
        {
            var feed = new NewsFeed(new[] { Item("오늘 기사", 9, 10, new string[0]) });
            var later = new DateTimeOffset(2026, 10, 29, 9, 0, 0, Kst);
            Assert.That(NewsView.Rank(Live(feed), "005930", Now)[0].TimeText, Is.EqualTo("09:10"));
            Assert.That(NewsView.Rank(Live(feed), "005930", later)[0].TimeText, Is.EqualTo("10/28"));
        }

        [Test]
        public void OrderViewWithoutBestAskShowsDashAndCannotPrepare()
        {
            var t = new OrderTicket();
            t.Select(TraderCatalog.Samsung, TradingSession.Nxt);
            var v = OrderView.From(t, Live(Book(new long[0], new long[] { 1 })), TradingSession.Nxt);
            Assert.That(v.PriceText, Is.EqualTo("—"));
            Assert.That(v.PrepareEnabled, Is.False);
        }

        [Test]
        public void OrderViewNeedsFreshBookAndQuote()
        {
            var t = new OrderTicket();
            t.Select(TraderCatalog.Samsung, TradingSession.Nxt);
            var b = Book(new long[] { 1 }, new long[] { 1 });
            foreach (var st in new[] { DataState.Disconnected, DataState.Stale })
            {
                var v = OrderView.From(t, new Observation<OrderBookSnapshot>(b, st, DataOrigin.Kiwoom, Now, Now), TradingSession.Nxt);
                Assert.That(v.PrepareEnabled, Is.False);
                Assert.That(v.Badge, Is.EqualTo(st.ToString().ToUpperInvariant()));
                Assert.That(v.LockReason, Does.Contain(StatusText.Name(st)));
            }
            var delayed = new Observation<OrderBookSnapshot>(b, DataState.Delayed, DataOrigin.Kiwoom, Now);
            Assert.That(OrderView.From(t, delayed, TradingSession.Nxt).PrepareEnabled, Is.True);
            t.Select(TraderCatalog.Samsung, TradingSession.KrxRegular);
            var staleQ = new Observation<QuoteSnapshot>(new QuoteSnapshot("005930", 1m, null, null, null), DataState.Stale, DataOrigin.Kiwoom, Now);
            var m = OrderView.From(t, Live(b), TradingSession.KrxRegular, staleQ);
            Assert.That(m.PrepareEnabled, Is.False);
            Assert.That(m.Badge, Is.EqualTo("STALE"));
            Assert.That(m.LockReason, Does.Contain("시세"));
        }

        [Test]
        public void UnselectedTicketCannotPrepare()
        {
            var v = OrderView.From(new OrderTicket(), Live(Book(new long[] { 1 }, new long[] { 1 })), TradingSession.Nxt);
            Assert.That(v.PrepareEnabled, Is.False);
        }

        [Test]
        public void BookIgnoresLevelsBeyondDepthAndFlagsNonLiveQuote()
        {
            var b = Book(new long[] { 1, 1, 1, 1 }, new long[] { 1, 1, 1, 1 });
            var q = new Observation<QuoteSnapshot>(new QuoteSnapshot("005930", 1m, null, null, null), DataState.Stale, DataOrigin.Kiwoom, Now);
            var v = OrderBookView.From(Live(b), q, 2, TraderCatalog.Samsung, "KRX");
            Assert.That(v.Asks.Count, Is.EqualTo(2));
            Assert.That(v.Asks[0].PriceText, Is.EqualTo("225,500"));
            Assert.That(v.Status, Does.Contain("STALE"));
        }

        // ---------- synthetic preview data ----------

        const string SyntheticLabel = "SYNTHETIC · 합성 예시";

        [Test]
        public void PreviewDataReadsSyntheticOnEveryViewNeverDelayed()
        {
            var samsung = TraderCatalog.Samsung;
            var book = PreviewMarketData.Book(samsung);
            var quote = PreviewMarketData.Quote(samsung);
            var candles = PreviewMarketData.Candles(samsung);
            Assert.That(book.State, Is.EqualTo(DataState.Delayed)); // the preview fixtures themselves are Delayed

            var b = OrderBookView.From(book, quote, 5, samsung, null);
            Assert.That(b.Status, Does.StartWith(SyntheticLabel));
            Assert.That(b.Status, Does.Not.Contain("DELAYED"));

            var h = ChartHeaderView.From(samsung, candles, quote, "1D", null, null);
            Assert.That(h.Status, Does.StartWith(SyntheticLabel));
            Assert.That(h.Status, Does.Not.Contain("DELAYED"));
            var fallback = ChartHeaderView.From(samsung, candles, null, "1D", null, null);
            Assert.That(fallback.Status, Does.StartWith(SyntheticLabel));

            var balance = BalanceView.From(new Observation<BalanceSnapshot>(null, DataState.Unavailable, DataOrigin.Synthetic, null,
                reason: "PREVIEW_NO_ACCOUNT_DATA"));
            Assert.That(balance.Status, Is.EqualTo(SyntheticLabel));
            var news = new Observation<NewsFeed>(null, DataState.Unavailable, DataOrigin.Synthetic, null, reason: "PREVIEW_NO_NEWS_DATA");
            Assert.That(NewsView.Status(news), Is.EqualTo(SyntheticLabel));

            var t = new OrderTicket();
            t.Select(samsung, TradingSession.KrxRegular);
            var o = OrderView.From(t, book, TradingSession.KrxRegular, quote);
            Assert.That(o.Badge, Is.EqualTo(SyntheticLabel));
            Assert.That(o.PrepareEnabled, Is.False);
            Assert.That(o.LockReason, Does.Contain("합성 예시"));
        }

        [Test]
        public void OrderViewRefusesASyntheticBookOrQuoteWithALabelledReason()
        {
            var samsung = TraderCatalog.Samsung;
            var t = new OrderTicket();
            t.Select(samsung, TradingSession.Nxt); // limit order: prices off the book alone
            var liveBook = Live(Book(new long[] { 1 }, new long[] { 1 }));
            var syntheticQuote = PreviewMarketData.Quote(samsung);
            var v = OrderView.From(t, liveBook, TradingSession.Nxt, syntheticQuote);
            Assert.That(v.PrepareEnabled, Is.False);
            Assert.That(v.Badge, Is.EqualTo(SyntheticLabel));
            Assert.That(v.LockReason, Is.EqualTo("시세 합성 예시 · 주문 준비 불가"));

            var w = OrderView.From(t, PreviewMarketData.Book(samsung), TradingSession.Nxt);
            Assert.That(w.PrepareEnabled, Is.False);
            Assert.That(w.LockReason, Is.EqualTo("호가 합성 예시 · 주문 준비 불가"));

            // A locked ticket keeps its own reason; the badge still names the synthetic data.
            var locked = new OrderTicket();
            locked.Select(TraderCatalog.SkHynix, TradingSession.Nxt);
            var l = OrderView.From(locked, PreviewMarketData.Book(TraderCatalog.SkHynix), TradingSession.Nxt);
            Assert.That(l.Badge, Is.EqualTo(SyntheticLabel));
            Assert.That(l.LockReason, Is.EqualTo("시연 안전장치: 삼성전자만"));
        }

        // ---------- chart header market matching ----------

        [Test]
        public void ChartHeaderDuringNxtHoursUsesTheKrxDailySeriesNotTheNxtQuote()
        {
            var s = new CandleSeries("005930", "1D", new[]
            {
                new Candle("20261027", 220000m, 226000m, 219000m, 224000m, 1, true),
                new Candle("20261028", 224000m, 226000m, 223000m, 225000m, 1, false),
            });
            var daily = new Observation<CandleSeries>(s, DataState.Delayed, DataOrigin.Kiwoom, Now, Now, StatusText.MarketClosed);
            var nxtQuote = Live(new QuoteSnapshot("005930", 226500m, 1500m, 0.67m, 100));
            var h = ChartHeaderView.From(TraderCatalog.Samsung, daily, nxtQuote, "1D", "KRX", "NXT");
            Assert.That(h.Status, Is.EqualTo("장 마감 · KRX · 1D"));
            Assert.That(h.PriceText, Is.EqualTo("225,000"));
            Assert.That(h.ChangeText, Is.EqualTo("+1,000 (+0.45%)"));

            // The 4H series follows NXT in NXT hours, so the NXT quote leads it.
            var four = new Observation<CandleSeries>(new CandleSeries("005930", "4H", s.Candles), DataState.Live, DataOrigin.Kiwoom, Now, Now);
            var h4 = ChartHeaderView.From(TraderCatalog.Samsung, four, nxtQuote, "4H", "NXT", "NXT");
            Assert.That(h4.Status, Is.EqualTo("LIVE · NXT · 4H"));
            Assert.That(h4.PriceText, Is.EqualTo("226,500"));
        }
    }
}
