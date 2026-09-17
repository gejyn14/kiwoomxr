using System;
using NUnit.Framework;
using SpatialTrading.Domain;

namespace SpatialTrading.Tests
{
    public sealed class MarketDataTests
    {
        [Test] public void MissingValuesRemainMissing()
        {
            var quote = new QuoteSnapshot("005930", null, null, null, null);
            Assert.That(quote.LastPrice, Is.Null);
            Assert.That(quote.Volume, Is.Null);
            Assert.That(new BookLevel(1, null, null).Quantity, Is.Null);
            Assert.That(FinancialDecimal.ParseOptional(null), Is.Null);
            Assert.That(FinancialDecimal.ParseOptional("0"), Is.Zero);
        }

        [Test] public void NegativeChangeIsPreserved()
        {
            Assert.That(FinancialDecimal.ParseOptional("-1234.50"), Is.EqualTo(-1234.50m));
            var quote = new QuoteSnapshot("005930", 70000, -100, -.14m, 0);
            Assert.That(quote.Change, Is.EqualTo(-100));
            Assert.That(quote.Volume, Is.Zero);
        }

        [TestCase("")][TestCase(" ")][TestCase("1,000")][TestCase("1e3")][TestCase("NaN")][TestCase(" 12")][TestCase("0.123456789")][TestCase("12345678901234567890123456789")]
        public void InvalidNormalizedDecimalsAreRejected(string input) =>
            Assert.Throws<FormatException>(() => FinancialDecimal.ParseOptional(input));

        [Test] public void InvalidCandleIsNotRepaired()
        {
            Assert.Throws<ArgumentException>(() => new Candle("20260917", 100, 95, 80, 90, 10, null));
            Assert.Throws<ArgumentException>(() => new Candle("20260917", 90, 95, 80, 99, 10, null));
            Assert.Throws<ArgumentException>(() => new Candle("20260917", 90, 95, 0, 91, 10, null));
        }

        [Test] public void DuplicateAndReversedPeriodsAreRejected()
        {
            var a = new Candle("20260916", 90, 95, 80, 91, null, null);
            var b = new Candle("20260917", 90, 95, 80, 91, null, null);
            Assert.Throws<ArgumentException>(() => new CandleSeries("005930", "1D", new[] { a, a }));
            Assert.Throws<ArgumentException>(() => new CandleSeries("005930", "1D", new[] { b, a }));
            Assert.That(new CandleSeries("005930", "1D", new[] { a, b }).Candles.Count, Is.EqualTo(2));
        }

        [Test] public void LiveRequiresAuthoritativeDataAndSourceTime()
        {
            var quote = new QuoteSnapshot("005930", 70000, null, null, null);
            var now = DateTimeOffset.UtcNow;
            Assert.Throws<ArgumentException>(() => new Observation<QuoteSnapshot>(quote, DataState.Live, DataOrigin.Synthetic, now, now));
            Assert.Throws<ArgumentException>(() => new Observation<QuoteSnapshot>(quote, DataState.Live, DataOrigin.Kiwoom, now));
            Assert.Throws<ArgumentException>(() => new Observation<QuoteSnapshot>(null, DataState.Live, DataOrigin.Kiwoom, now, now));
            Assert.That(new Observation<QuoteSnapshot>(quote, DataState.Delayed, DataOrigin.Kiwoom, now).SourceAt, Is.Null);
        }

        [Test] public void DisconnectedCanRetainAgedDataWithoutClaimingLive()
        {
            var quote = new QuoteSnapshot("005930", 70000, null, null, null);
            var observation = new Observation<QuoteSnapshot>(quote, DataState.Disconnected, DataOrigin.Kiwoom, DateTimeOffset.UtcNow.AddMinutes(-5));
            Assert.That(observation.Value.LastPrice, Is.EqualTo(70000));
            Assert.That(observation.State, Is.EqualTo(DataState.Disconnected));
        }

        [Test] public void BookRejectsUnprovenLevelOrder()
        {
            Assert.Throws<ArgumentException>(() => new OrderBookSnapshot("005930", new[] { new BookLevel(2, 70000, 10) }, Array.Empty<BookLevel>()));
            Assert.Throws<ArgumentException>(() => new BookLevel(1, -70000, 10));
            Assert.Throws<ArgumentException>(() => new BookLevel(1, 70000, -10));
        }
    }
}
