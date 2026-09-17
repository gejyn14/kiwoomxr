using System;
using System.Collections.Generic;

namespace SpatialTrading.Domain
{
    /// <summary>Explicit UI preview source. Never a fallback for broker failure.</summary>
    public static class PreviewMarketData
    {
        public static Observation<QuoteSnapshot> Quote(InstrumentRef instrument) => new Observation<QuoteSnapshot>(
            new QuoteSnapshot(instrument.Code, SyntheticCatalog.ReferencePrice(instrument), null, null, null),
            DataState.Delayed, DataOrigin.Synthetic, DateTimeOffset.UtcNow, reason: "SYNTHETIC_PREVIEW");

        public static Observation<CandleSeries> Candles(InstrumentRef instrument)
        {
            var candles = new List<Candle>();
            var reference = SyntheticCatalog.ReferencePrice(instrument);
            var previous = reference * .92m;
            for (var i = 0; i < 40; i++)
            {
                var close = decimal.Round(reference * (.92m + i * .002m + (decimal)Math.Sin(i * 1.3) * .016m));
                candles.Add(new Candle(i.ToString("D4"), previous, Math.Max(previous, close) + 400,
                    Math.Min(previous, close) - 350, close, 3000 + i * 123, null));
                previous = close;
            }
            return new Observation<CandleSeries>(new CandleSeries(instrument.Code, "PREVIEW", candles),
                DataState.Delayed, DataOrigin.Synthetic, DateTimeOffset.UtcNow, reason: "SYNTHETIC_PREVIEW");
        }

        public static Observation<OrderBookSnapshot> Book(InstrumentRef instrument)
        {
            var asks = new List<BookLevel>(); var bids = new List<BookLevel>();
            var reference = SyntheticCatalog.ReferencePrice(instrument);
            for (var i = 1; i <= 5; i++)
            {
                // Arbitrary fixture spacing, never an exchange tick rule.
                asks.Add(new BookLevel(i, reference + i * 100, 140 + i * 73));
                bids.Add(new BookLevel(i, reference - i * 100, 90 + i * 97));
            }
            return new Observation<OrderBookSnapshot>(new OrderBookSnapshot(instrument.Code, asks, bids),
                DataState.Delayed, DataOrigin.Synthetic, DateTimeOffset.UtcNow, reason: "SYNTHETIC_PREVIEW");
        }
    }
}
