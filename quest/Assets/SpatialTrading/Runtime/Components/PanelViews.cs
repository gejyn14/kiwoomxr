using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SpatialTrading.Domain;

namespace SpatialTrading.Components
{
    /// <summary>Shared number and status formatting. Pure: decimal math only, InvariantCulture, no Unity API.</summary>
    static class ViewFormat
    {
        public const string None = "—";
        const string Minus = "−";

        public static string Number(decimal v) => v.ToString("#,##0.########", CultureInfo.InvariantCulture);

        public static string Number(decimal? v) => v.HasValue ? Number(v.Value) : None;

        public static string Signed(decimal v) => v > 0m ? "+" + Number(v) : v < 0m ? Minus + Number(-v) : "0";

        public static string SignedPercent(decimal p)
        {
            var r = Math.Round(p, 2, MidpointRounding.AwayFromZero);
            var s = Math.Abs(r).ToString("0.00", CultureInfo.InvariantCulture) + "%";
            return r > 0m ? "+" + s : r < 0m ? Minus + s : s;
        }

        /// <summary>"−1,200 (−0.53%)"; the percent part is left out when it is not known.</summary>
        public static string Change(decimal? change, decimal? percent)
        {
            if (change == null) return None;
            return percent == null ? Signed(change.Value) : Signed(change.Value) + " (" + SignedPercent(percent.Value) + ")";
        }

        /// <summary>LIVE, "장 마감" after the close, otherwise the upper-case state (e.g. DISCONNECTED).</summary>
        public static string Badge(DataState state, string reason)
        {
            if (reason == StatusText.MarketClosed) return StatusText.State(state, reason);
            return state.ToString().ToUpperInvariant();
        }

        public static string Join(params string[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrEmpty(p)));
    }

    public sealed class BookRowView
    {
        public string PriceText { get; }
        public string QuantityText { get; }
        public double BarRatio { get; }
        public BookRowView(string priceText, string quantityText, double barRatio)
        { PriceText = priceText; QuantityText = quantityText; BarRatio = barRatio; }
    }

    public sealed class OrderBookView
    {
        public string Title { get; }
        public string Status { get; }
        public string Hint { get; }
        public bool IsIndex { get; }
        public string Placeholder { get; }
        public IReadOnlyList<BookRowView> Asks { get; }
        public IReadOnlyList<BookRowView> Bids { get; }
        public string LastPriceText { get; }
        public string BidShareText { get; }

        OrderBookView(string title, string status, string hint, bool isIndex, string placeholder,
            IReadOnlyList<BookRowView> asks, IReadOnlyList<BookRowView> bids, string last, string share)
        {
            Title = title; Status = status; Hint = hint; IsIndex = isIndex; Placeholder = placeholder;
            Asks = asks; Bids = bids; LastPriceText = last; BidShareText = share;
        }

        public static OrderBookView From(Observation<OrderBookSnapshot> book, Observation<QuoteSnapshot> quote,
            int depth, InstrumentRef instrument, string market)
        {
            if (instrument == null) throw new ArgumentNullException(nameof(instrument));
            if (depth < 1) depth = 1;
            var title = "호가 · " + instrument.DisplayName + (string.IsNullOrEmpty(market) ? "" : " " + market);
            var empty = new List<BookRowView>().AsReadOnly();
            if (instrument.Kind == InstrumentKind.Index)
                return new OrderBookView(title, ViewFormat.Join(market, "지수"), string.Empty, true, "지수 · 호가 없음",
                    empty, empty, ViewFormat.None, ViewFormat.None);

            var last = ViewFormat.Number(quote?.Value?.LastPrice);
            var snap = book?.Value;
            var state = book?.State ?? DataState.Unavailable;
            var reason = book?.Reason;
            var status = ViewFormat.Join(ViewFormat.Badge(state, reason), market, "상위 " + depth + "호가");
            var qState = quote?.Value != null ? quote.State : DataState.Live;
            if (qState != DataState.Live) status += " · 시세 " + ViewFormat.Badge(qState, quote.Reason);
            var hint = StatusText.Hint(reason);

            // Levels by rank; missing ranks stay as placeholder rows so the grid keeps its shape.
            var askQ = new long?[depth]; var askP = new decimal?[depth];
            var bidQ = new long?[depth]; var bidP = new decimal?[depth];
            if (snap != null)
            {
                foreach (var l in snap.Asks.Where(l => l.Rank >= 1 && l.Rank <= depth)) { askQ[l.Rank - 1] = l.Quantity; askP[l.Rank - 1] = l.Price; }
                foreach (var l in snap.Bids.Where(l => l.Rank >= 1 && l.Rank <= depth)) { bidQ[l.Rank - 1] = l.Quantity; bidP[l.Rank - 1] = l.Price; }
            }
            long max = 0, askSum = 0, bidSum = 0;
            for (var i = 0; i < depth; i++)
            {
                askSum += askQ[i] ?? 0; bidSum += bidQ[i] ?? 0;
                max = Math.Max(max, Math.Max(askQ[i] ?? 0, bidQ[i] ?? 0));
            }
            BookRowView Row(decimal? p, long? q) => new BookRowView(ViewFormat.Number(p),
                q.HasValue ? q.Value.ToString("#,##0", CultureInfo.InvariantCulture) : ViewFormat.None,
                max > 0 && q.HasValue ? (double)q.Value / max : 0d);
            var asks = new List<BookRowView>();
            for (var i = depth - 1; i >= 0; i--) asks.Add(Row(askP[i], askQ[i])); // highest price on top
            var bids = new List<BookRowView>();
            for (var i = 0; i < depth; i++) bids.Add(Row(bidP[i], bidQ[i]));

            var total = askSum + bidSum;
            var share = total == 0 ? ViewFormat.None
                : "매수 " + Math.Round((decimal)bidSum * 100m / total, 0, MidpointRounding.AwayFromZero)
                    .ToString("0", CultureInfo.InvariantCulture) + "%";
            return new OrderBookView(title, status, hint, false, null, asks.AsReadOnly(), bids.AsReadOnly(), last, share);
        }
    }

    public sealed class ChartHeaderView
    {
        public string Title { get; }
        public string PriceText { get; }
        public string ChangeText { get; }
        public string Status { get; }

        ChartHeaderView(string title, string price, string change, string status)
        { Title = title; PriceText = price; ChangeText = change; Status = status; }

        /// <summary>
        /// Prefers the live quote. Without one (the gateway keeps quotes UNAVAILABLE after the close) the price is the last
        /// candle close and the change is last close minus the previous candle close, both honestly from the series; the
        /// status then carries the series' own state label. No change is shown when it cannot be computed.
        /// </summary>
        public static ChartHeaderView From(InstrumentRef instrument, Observation<CandleSeries> series,
            Observation<QuoteSnapshot> quote, string interval, string market)
        {
            if (instrument == null) throw new ArgumentNullException(nameof(instrument));
            var q = quote?.Value;
            var candles = series?.Value?.Candles;
            string price, change;
            DataState state; string reason;
            if (q != null && q.LastPrice.HasValue)
            {
                price = ViewFormat.Number(q.LastPrice);
                change = ViewFormat.Change(q.Change, q.ChangePercent);
                state = quote.State; reason = quote.Reason;
            }
            else
            {
                state = series?.State ?? DataState.Unavailable; reason = series?.Reason;
                if (candles != null && candles.Count > 0)
                {
                    var c = candles[candles.Count - 1].Close;
                    price = ViewFormat.Number(c);
                    if (candles.Count > 1)
                    {
                        var prev = candles[candles.Count - 2].Close;
                        change = ViewFormat.Change(c - prev, prev > 0m ? (c - prev) * 100m / prev : (decimal?)null);
                    }
                    else change = ViewFormat.None;
                }
                else { price = ViewFormat.None; change = ViewFormat.None; }
            }
            var status = ViewFormat.Join(ViewFormat.Badge(state, reason), market, interval);
            return new ChartHeaderView(instrument.DisplayName, price, change, status);
        }
    }

    public sealed class NewsRowView
    {
        public string Tag { get; }
        public bool IsFiling { get; }
        public string Title { get; }
        public string TimeText { get; }
        public NewsRowView(string tag, bool isFiling, string title, string timeText)
        { Tag = tag; IsFiling = isFiling; Title = title; TimeText = timeText; }
    }

    public static class NewsView
    {
        static readonly TimeSpan Kst = TimeSpan.FromHours(9);

        /// <summary>
        /// Items mentioning the selected code first, newest first inside each group. "Today" for the time text is the
        /// caller's <paramref name="now"/> (compared in KST), never the receipt time, so stale items read as dates.
        /// </summary>
        public static IReadOnlyList<NewsRowView> Rank(Observation<NewsFeed> feed, string selectedCode, DateTimeOffset now, int max = 8)
        {
            var items = feed?.Value?.Items;
            if (items == null || items.Count == 0 || max <= 0) return new List<NewsRowView>().AsReadOnly();
            var today = now.ToOffset(Kst).Date;
            bool Hit(NewsItem i) => selectedCode != null && i.Mentions.Contains(selectedCode);
            return items.OrderByDescending(Hit).ThenByDescending(i => i.PublishedAt).Take(max)
                .Select(i =>
                {
                    var t = i.PublishedAt.ToOffset(Kst);
                    var time = t.Date == today ? t.ToString("HH:mm", CultureInfo.InvariantCulture)
                                               : t.ToString("MM/dd", CultureInfo.InvariantCulture);
                    return new NewsRowView(i.IsFiling ? "공시" : i.Source, i.IsFiling, i.Title, time);
                }).ToList().AsReadOnly();
        }
    }

    public sealed class BalanceRowView
    {
        public string Text { get; }
        public string ProfitText { get; }
        public BalanceRowView(string text, string profitText) { Text = text; ProfitText = profitText; }
    }

    public sealed class BalanceView
    {
        public string AccountText { get; }
        public string Status { get; }
        public bool IsEmpty { get; }
        public string EmptyText { get; }
        public string OrderableText { get; }
        public string TotalText { get; }
        public IReadOnlyList<BalanceRowView> Rows { get; }

        BalanceView(string account, string status, bool isEmpty, string emptyText, string orderable, string total,
            IReadOnlyList<BalanceRowView> rows)
        { AccountText = account; Status = status; IsEmpty = isEmpty; EmptyText = emptyText; OrderableText = orderable; TotalText = total; Rows = rows; }

        public static BalanceView From(Observation<BalanceSnapshot> balance)
        {
            var b = balance?.Value;
            var state = balance?.State ?? DataState.Unavailable;
            var status = ViewFormat.Badge(state, balance?.Reason);
            if (b == null)
                return new BalanceView("", status, true, StatusText.State(state, balance?.Reason), "주문가능 " + ViewFormat.None,
                    ViewFormat.None, new List<BalanceRowView>().AsReadOnly());
            var rows = b.Holdings.Select(h => new BalanceRowView(
                h.Name + " " + h.Quantity.ToString("#,##0", CultureInfo.InvariantCulture) + "주 · 평단 " + ViewFormat.Number(h.AveragePrice),
                ViewFormat.Change(h.Profit, h.ProfitRate))).ToList().AsReadOnly();
            var orderable = "주문가능 " + (b.OrderableCash.HasValue ? ViewFormat.Number(b.OrderableCash) + "원" : ViewFormat.None);
            return new BalanceView("계좌 " + b.Account, status, rows.Count == 0, rows.Count == 0 ? "보유 종목 없음" : null, orderable,
                ViewFormat.Number(b.TotalEvaluation) + " " + ViewFormat.Change(b.TotalProfit, b.TotalProfitRate), rows);
        }
    }

    public sealed class OrderTypeButtonView
    {
        public string Label { get; }
        public bool Enabled { get; }
        public bool Selected { get; }
        public string Reason { get; }
        public OrderTypeButtonView(string label, bool enabled, bool selected, string reason)
        { Label = label; Enabled = enabled; Selected = selected; Reason = reason; }
    }

    public sealed class OrderView
    {
        static readonly OrderType[] Order =
            { OrderType.Limit, OrderType.Market, OrderType.BestLimit, OrderType.PriorityLimit, OrderType.Conditional };

        public IReadOnlyList<OrderTypeButtonView> Types { get; }
        public string PriceText { get; }
        public string QuantityText => "1주 🔒";
        public string SellTabText => "매도 🔒";
        public bool PrepareEnabled { get; }
        public string LockReason { get; }
        public string Badge { get; }

        OrderView(IReadOnlyList<OrderTypeButtonView> types, string price, bool prepare, string lockReason, string badge)
        { Types = types; PriceText = price; PrepareEnabled = prepare; LockReason = lockReason; Badge = badge; }

        static Tuple<string, string> Stale<T>(Observation<T> o, string what) where T : class
        {
            var st = o?.State ?? DataState.Unavailable;
            if (st == DataState.Live || st == DataState.Delayed) return null;
            return Tuple.Create(ViewFormat.Badge(st, o?.Reason), what + " " + StatusText.State(st, o?.Reason));
        }

        /// <summary>
        /// A limit order prices at the best ask of the book; every other type needs a last price (the optional quote)
        /// as its reference, exactly as <see cref="OrderTicket.TryPrepare"/> does.
        /// </summary>
        public static OrderView From(OrderTicket ticket, Observation<OrderBookSnapshot> book, TradingSession session,
            Observation<QuoteSnapshot> quote = null)
        {
            if (ticket == null) throw new ArgumentNullException(nameof(ticket));
            var locked = ticket.State == TicketState.Locked;
            var types = Order.Select(t => new OrderTypeButtonView(OrderTypeTable.DisplayName(t),
                !locked && OrderTypeTable.Enabled(t, session), ticket.Type == t,
                locked ? ticket.LockReason : OrderTypeTable.DisabledReason(t, session))).ToList().AsReadOnly();
            var ask1 = book?.Value?.Asks.FirstOrDefault(l => l.Rank == 1);
            decimal? bestAsk = ask1 != null && ask1.Price > 0m ? ask1.Price : null;
            var lastPrice = quote?.Value?.LastPrice;
            var price = ticket.Type == OrderType.Limit ? ViewFormat.Number(bestAsk) : OrderTypeTable.DisplayName(ticket.Type);
            var refOk = ticket.Type == OrderType.Limit ? bestAsk > 0m : lastPrice > 0m;
            // The book (and the quote for non-limit types) must be Live or Delayed; otherwise the state is the reason.
            var bad = Stale(book, "호가") ?? (ticket.Type == OrderType.Limit ? null : Stale(quote, "시세"));
            var prepare = !locked && ticket.HasSelection && OrderTypeTable.Enabled(ticket.Type, session) && refOk && bad == null;
            var badge = ticket.State == TicketState.Preflight ? "PREFLIGHT" : locked ? "LOCKED" : "READY";
            var lockReason = ticket.LockReason;
            if (!locked && bad != null) { badge = bad.Item1; lockReason = bad.Item2; }
            return new OrderView(types, price, prepare, lockReason, badge);
        }
    }
}
