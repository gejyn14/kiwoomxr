using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SpatialTrading.Domain
{
    public enum DataState { Loading, Live, Delayed, Stale, Disconnected, Unavailable, Error }
    public enum DataOrigin { Synthetic, Kiwoom }

    public sealed class Observation<T> where T : class
    {
        public T Value { get; }
        public DataState State { get; }
        public DataOrigin Origin { get; }
        public DateTimeOffset? ReceivedAt { get; }
        public DateTimeOffset? SourceAt { get; }
        public string Reason { get; }
        public Observation(T value, DataState state, DataOrigin origin, DateTimeOffset? receivedAt,
            DateTimeOffset? sourceAt = null, string reason = null)
        {
            if (!Enum.IsDefined(typeof(DataState), state) || !Enum.IsDefined(typeof(DataOrigin), origin))
                throw new ArgumentException("Unknown observation status or origin.");
            if ((state == DataState.Live || state == DataState.Delayed) && (value == null || receivedAt == null))
                throw new ArgumentException("Available observations require data and receipt time.");
            if (state == DataState.Live && (origin == DataOrigin.Synthetic || sourceAt == null))
                throw new ArgumentException("Live requires authoritative data and verified source time.");
            Value = value; State = state; Origin = origin; ReceivedAt = receivedAt; SourceAt = sourceAt; Reason = reason;
        }
    }

    public sealed class QuoteSnapshot
    {
        public string InstrumentCode { get; }
        public decimal? LastPrice { get; }
        public decimal? Change { get; }
        public decimal? ChangePercent { get; }
        public long? Volume { get; }
        public QuoteSnapshot(string instrumentCode, decimal? lastPrice, decimal? change, decimal? changePercent, long? volume)
        {
            if (string.IsNullOrWhiteSpace(instrumentCode) || lastPrice <= 0 || volume < 0)
                throw new ArgumentException("Invalid quote identity, price or quantity.");
            InstrumentCode = instrumentCode; LastPrice = lastPrice; Change = change; ChangePercent = changePercent; Volume = volume;
        }
    }

    public sealed class Candle
    {
        public string PeriodKey { get; }
        public decimal Open { get; }
        public decimal High { get; }
        public decimal Low { get; }
        public decimal Close { get; }
        public long? Volume { get; }
        public bool? Complete { get; }
        public Candle(string periodKey, decimal open, decimal high, decimal low, decimal close, long? volume, bool? complete)
        {
            if (string.IsNullOrWhiteSpace(periodKey) || low <= 0 || high < low ||
                open < low || open > high || close < low || close > high || volume < 0)
                throw new ArgumentException("Invalid candle bounds or identity.");
            PeriodKey = periodKey; Open = open; High = high; Low = low; Close = close; Volume = volume; Complete = complete;
        }
    }

    public sealed class CandleSeries
    {
        public string InstrumentCode { get; }
        public string Interval { get; }
        public string Adjustment { get; }
        public bool CompleteHistory { get; }
        public IReadOnlyList<Candle> Candles { get; }
        public CandleSeries(string code, string interval, IEnumerable<Candle> candles,
            string adjustment = "UNKNOWN", bool completeHistory = false)
        {
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(interval) || candles == null)
                throw new ArgumentException("Candle series requires identity and an explicit sequence.");
            var copy = new List<Candle>(candles);
            for (var i = 0; i < copy.Count; i++)
                if (copy[i] == null || i > 0 && string.CompareOrdinal(copy[i - 1].PeriodKey, copy[i].PeriodKey) >= 0)
                    throw new ArgumentException("Candles must be unique and ordered by period.");
            if (string.IsNullOrWhiteSpace(adjustment)) throw new ArgumentException("Adjustment must be explicit.");
            InstrumentCode = code; Interval = interval; Adjustment = adjustment;
            CompleteHistory = completeHistory; Candles = copy.AsReadOnly();
        }
    }

    public sealed class BookLevel
    {
        public int Rank { get; }
        public decimal? Price { get; }
        public long? Quantity { get; }
        public BookLevel(int rank, decimal? price, long? quantity)
        {
            if (rank < 1 || rank > 10 || price <= 0 || quantity < 0) throw new ArgumentException("Invalid book level.");
            Rank = rank; Price = price; Quantity = quantity;
        }
    }

    public sealed class OrderBookSnapshot
    {
        public string InstrumentCode { get; }
        public IReadOnlyList<BookLevel> Asks { get; }
        public IReadOnlyList<BookLevel> Bids { get; }
        public OrderBookSnapshot(string code, IEnumerable<BookLevel> asks, IEnumerable<BookLevel> bids)
        {
            if (string.IsNullOrWhiteSpace(code) || asks == null || bids == null) throw new ArgumentException("Book identity is required.");
            InstrumentCode = code; Asks = CopyLevels(asks); Bids = CopyLevels(bids);
        }
        private static IReadOnlyList<BookLevel> CopyLevels(IEnumerable<BookLevel> levels)
        {
            var copy = new List<BookLevel>(levels);
            for (var i = 0; i < copy.Count; i++)
                if (copy[i] == null || copy[i].Rank != i + 1) throw new ArgumentException("Explicit consecutive book ranks required.");
            return copy.AsReadOnly();
        }
    }

    /// <summary>Normalized gateway decimal wire format. Broker sign interpretation belongs in its adapter.</summary>
    public static class FinancialDecimal
    {
        public static decimal? ParseOptional(string value)
        {
            if (value == null) return null;
            if (!Regex.IsMatch(value, @"^[+-]?[0-9]+(?:\.[0-9]{1,8})?$") ||
                value.Replace(".", "").TrimStart('+', '-', '0').Length > 28 ||
                !decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var result)) throw new FormatException("Invalid normalized decimal.");
            return result;
        }
    }
}
