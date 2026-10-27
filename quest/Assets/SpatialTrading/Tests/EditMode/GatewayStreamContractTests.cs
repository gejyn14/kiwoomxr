using System;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SpatialTrading.Domain;
using SpatialTrading.Market;

namespace SpatialTrading.Tests
{
    public sealed class GatewayStreamContractTests
    {
        private static readonly Guid Sub = Guid.Parse("4604f3f4-8e0e-4e8b-9e93-a69c920df0ac");

        private static JObject Observation(string correlation = null) => new JObject
        {
            ["instrument_code"] = "005930", ["state"] = "LIVE", ["origin"] = "KIWOOM",
            ["value"] = JObject.Parse("{\"last_price\":\"78800\",\"change\":\"100\",\"change_percent\":\"0.13\",\"volume\":\"1000\"}"),
            ["received_at"] = "2026-09-17T15:00:00+09:00", ["source_at"] = "2026-09-17T15:00:00+09:00",
            ["reason"] = null, ["correlation_id"] = correlation ?? Sub.ToString()
        };
        private static JObject Message(string market = "NXT", string correlation = null) => new JObject
        {
            ["type"] = "observation", ["capability"] = "quote", ["market"] = market, ["observation"] = Observation(correlation)
        };
        private static JObject Heartbeat() => JObject.Parse(
            "{\"type\":\"heartbeat\",\"broker_connected\":false,\"reason\":\"KIWOOM_AUTH\",\"server_time\":\"2026-09-17T15:00:00+09:00\"}");

        [Test]
        public void ObservationMessageReusesGatewayEnvelopeDecoding()
        {
            var update = (StreamUpdate)GatewayStreamContract.Parse(Message().ToString(), Sub);
            Assert.That(update.Capability, Is.EqualTo(StreamCapability.Quote));
            Assert.That(update.Code, Is.EqualTo("005930"));
            Assert.That(update.Market, Is.EqualTo("NXT"));
            Assert.That(update.Quote.State, Is.EqualTo(DataState.Live));
            Assert.That(update.Quote.Value.LastPrice, Is.EqualTo(78800m));
            Assert.That(update.Candles, Is.Null);
            Assert.That(update.Book, Is.Null);
        }

        [Test]
        public void NullMarketIsAccepted()
        {
            var update = (StreamUpdate)GatewayStreamContract.Parse(Message(null).ToString(), Sub);
            Assert.That(update.Market, Is.Null);
        }

        [Test]
        public void HeartbeatParsesBrokerState()
        {
            var beat = (StreamHeartbeat)GatewayStreamContract.Parse(Heartbeat().ToString(), Sub);
            Assert.That(beat.BrokerConnected, Is.False);
            Assert.That(beat.Reason, Is.EqualTo("KIWOOM_AUTH"));
            Assert.That(beat.ServerTime.Offset, Is.EqualTo(TimeSpan.FromHours(9)));
            var root = Heartbeat(); root["reason"] = null; root["broker_connected"] = true;
            var ok = (StreamHeartbeat)GatewayStreamContract.Parse(root.ToString(), Sub);
            Assert.That(ok.BrokerConnected, Is.True);
            Assert.That(ok.Reason, Is.Null);
        }

        [Test]
        public void MalformedHeartbeatIsRejected()
        {
            foreach (var edit in new System.Action<JObject>[]
            {
                r => r["broker_connected"] = "true", r => r["broker_connected"] = 1, r => r["reason"] = 5,
                r => r["server_time"] = "2026-09-17T15:00:00", r => r["server_time"] = 5, r => r["extra"] = 1,
                r => r.Remove("reason")
            })
            {
                var root = Heartbeat(); edit(root);
                Assert.Throws<FormatException>(() => GatewayStreamContract.Parse(root.ToString(), Sub));
            }
        }

        [Test]
        public void UnknownTypeExtraFieldOrMismatchedCorrelationIsRejected()
        {
            var order = Message(); order["type"] = "order";
            Assert.Throws<FormatException>(() => GatewayStreamContract.Parse(order.ToString(), Sub));
            var extra = Message(); extra["extra"] = 1;
            Assert.Throws<FormatException>(() => GatewayStreamContract.Parse(extra.ToString(), Sub));
            var missing = Message(); missing.Remove("market");
            Assert.Throws<FormatException>(() => GatewayStreamContract.Parse(missing.ToString(), Sub));
            var other = Message(correlation: Guid.NewGuid().ToString());
            Assert.Throws<FormatException>(() => GatewayStreamContract.Parse(other.ToString(), Sub));
            var capability = Message(); capability["capability"] = "trades";
            Assert.Throws<FormatException>(() => GatewayStreamContract.Parse(capability.ToString(), Sub));
        }

        [Test]
        public void DuplicateKeysAndGarbageAreRejected()
        {
            Assert.Throws<FormatException>(() => GatewayStreamContract.Parse(
                "{\"type\":\"heartbeat\",\"type\":\"heartbeat\",\"broker_connected\":true,\"reason\":null,\"server_time\":\"2026-09-17T15:00:00+09:00\"}", Sub));
            Assert.Throws<FormatException>(() => GatewayStreamContract.Parse("not json", Sub));
            Assert.Throws<FormatException>(() => GatewayStreamContract.Parse("[]", Sub));
            Assert.Throws<FormatException>(() => GatewayStreamContract.Parse("", Sub));
        }

        [Test]
        public void InnerDecodingErrorsSurfaceAsFormatException()
        {
            var root = Message(); root["observation"]["value"]["last_price"] = 78800;
            Assert.Throws<FormatException>(() => GatewayStreamContract.Parse(root.ToString(), Sub));
            var origin = Message(); origin["observation"]["origin"] = "SYNTHETIC";
            Assert.Throws<FormatException>(() => GatewayStreamContract.Parse(origin.ToString(), Sub));
            var code = Message(); code["observation"]["instrument_code"] = 5930;
            Assert.Throws<FormatException>(() => GatewayStreamContract.Parse(code.ToString(), Sub));
        }

        [Test]
        public void MarketMustBeKnownExchangeOrNull()
        {
            Assert.Throws<FormatException>(() => GatewayStreamContract.Parse(Message("NYSE").ToString(), Sub));
            Assert.DoesNotThrow(() => GatewayStreamContract.Parse(Message("KRX").ToString(), Sub));
        }

        [Test]
        public void SubscribeMessageHasExactShape()
        {
            var obj = JObject.Parse(GatewayStreamContract.Subscribe("tok", new[] { "005930" }, Sub));
            CollectionAssert.AreEquivalent(new[] { "type", "token", "codes", "correlation_id" },
                System.Linq.Enumerable.Select(obj.Properties(), p => p.Name));
            Assert.That((string)obj["type"], Is.EqualTo("subscribe"));
            Assert.That((string)obj["correlation_id"], Is.EqualTo(Sub.ToString()));
        }

        [Test]
        public void SubscribeEscapesTokenAndKeepsCodes()
        {
            var obj = JObject.Parse(GatewayStreamContract.Subscribe("a\"b\\c", new[] { "005930", "000660" }, Sub));
            Assert.That((string)obj["token"], Is.EqualTo("a\"b\\c"));
            Assert.That(obj["codes"].ToObject<string[]>(), Is.EqualTo(new[] { "005930", "000660" }));
        }
    }
}
