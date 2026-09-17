using System;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SpatialTrading.Domain;
using SpatialTrading.Market;

namespace SpatialTrading.Tests
{
    public sealed class GatewayContractTests
    {
        private static readonly Guid Correlation = Guid.Parse("4604f3f4-8e0e-4e8b-9e93-a69c920df0ac");
        private static JObject Envelope(JToken value = null) => new JObject
        {
            ["instrument_code"] = "005930", ["state"] = value == null ? "UNAVAILABLE" : "DELAYED",
            ["origin"] = "KIWOOM", ["value"] = value,
            ["received_at"] = value == null ? null : "2026-09-17T15:00:00+00:00", ["source_at"] = null,
            ["reason"] = value == null ? "BLOCKED_BY_MISSING_SPEC:K-01" : null,
            ["correlation_id"] = Correlation.ToString()
        };
        private static JObject QuoteValue() => JObject.Parse("{\"last_price\":\"123456789.12345678\",\"change\":\"-12.5\",\"change_percent\":null,\"volume\":\"123456789012345678\"}");

        [Test]
        public void ExplicitNullFromGatewayRemainsUnavailableForAllCapabilities()
        {
            var json = Envelope().ToString();
            var quote = GatewayContract.Quote(json, "005930", Correlation);
            Assert.That(quote.State, Is.EqualTo(DataState.Unavailable));
            Assert.That(quote.Value, Is.Null);
            Assert.That(quote.Reason, Is.EqualTo("BLOCKED_BY_MISSING_SPEC:K-01"));
            Assert.That(GatewayContract.Candles(json, "005930", Correlation).Value, Is.Null);
            Assert.That(GatewayContract.Book(json, "005930", Correlation).Value, Is.Null);
        }

        [Test]
        public void QuotePreservesDecimalPrecisionNegativeChangeAndLargeQuantity()
        {
            var quote = GatewayContract.Quote(Envelope(QuoteValue()).ToString(), "005930", Correlation).Value;
            Assert.That(quote.LastPrice, Is.EqualTo(123456789.12345678m));
            Assert.That(quote.Change, Is.EqualTo(-12.5m));
            Assert.That(quote.ChangePercent, Is.Null);
            Assert.That(quote.Volume, Is.EqualTo(123456789012345678L));
        }

        [TestCase("origin", "SYNTHETIC")]
        [TestCase("origin", "1")]
        [TestCase("state", "5")]
        [TestCase("state", "999")]
        [TestCase("state", "unavailable")]
        [TestCase("instrument_code", "000660")]
        [TestCase("correlation_id", "00000000-0000-0000-0000-000000000000")]
        [TestCase("received_at", "2026-09-17T15:00:00")]
        [TestCase("received_at", "")]
        public void AmbiguousOrMismatchedEnvelopesAreRejected(string field, string value)
        {
            var root = Envelope(); root[field] = value;
            Assert.Throws<FormatException>(() => GatewayContract.Quote(root.ToString(), "005930", Correlation));
        }

        [TestCase("last_price", "123.45", true)]
        [TestCase("volume", "123", true)]
        [TestCase("last_price", "1e3", false)]
        [TestCase("last_price", "", false)]
        [TestCase("last_price", "1,000", false)]
        [TestCase("volume", "-1", false)]
        [TestCase("volume", "9223372036854775808", false)]
        public void FinancialValuesCannotBeCoercedFromOtherWireTypes(string field, string value, bool number)
        {
            var quote = QuoteValue(); quote[field] = number ? JToken.Parse(value) : new JValue(value);
            Assert.Throws<FormatException>(() => GatewayContract.Quote(Envelope(quote).ToString(), "005930", Correlation));
        }

        [Test]
        public void MissingNullDuplicateAndExtraFieldsAreNotEquivalent()
        {
            var root = Envelope(); root.Remove("value");
            Assert.Throws<FormatException>(() => GatewayContract.Quote(root.ToString(), "005930", Correlation));
            root = Envelope(); root["execute"] = true;
            Assert.Throws<FormatException>(() => GatewayContract.Quote(root.ToString(), "005930", Correlation));
            var duplicate = Envelope().ToString().Replace("\"state\": \"UNAVAILABLE\"", "\"state\": \"LIVE\", \"state\": \"UNAVAILABLE\"");
            Assert.Throws<FormatException>(() => GatewayContract.Quote(duplicate, "005930", Correlation));
            Assert.Throws<FormatException>(() => GatewayContract.Quote(Envelope() + " {}", "005930", Correlation));
        }

        [Test]
        public void CandleCompletenessAndAdjustmentArePreserved()
        {
            var value = JObject.Parse("{\"interval\":\"1D\",\"adjustment\":\"UNADJUSTED\",\"complete_history\":false,\"candles\":[{\"period_key\":\"20260917\",\"open\":\"100\",\"high\":\"110\",\"low\":\"90\",\"close\":\"105\",\"volume\":null,\"complete\":false}]}");
            var series = GatewayContract.Candles(Envelope(value).ToString(), "005930", Correlation).Value;
            Assert.That(series.Adjustment, Is.EqualTo("UNADJUSTED"));
            Assert.That(series.CompleteHistory, Is.False);
            Assert.That(series.Candles[0].Complete, Is.False);
            Assert.That(series.Candles[0].Volume, Is.Null);
        }

        [Test]
        public void BookMissingPriceAndZeroQuantityRemainDifferent()
        {
            var value = JObject.Parse("{\"asks\":[{\"rank\":1,\"price\":null,\"quantity\":\"0\"}],\"bids\":[]}");
            var book = GatewayContract.Book(Envelope(value).ToString(), "005930", Correlation).Value;
            Assert.That(book.Asks[0].Price, Is.Null);
            Assert.That(book.Asks[0].Quantity, Is.Zero);
        }
    }
}
