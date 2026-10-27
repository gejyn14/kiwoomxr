using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpatialTrading.Domain;

namespace SpatialTrading.Market
{
    public enum StreamCapability { Quote, Candles, OrderBook }

    /// <summary>One live observation; only the member matching <see cref="Capability"/> is non-null.</summary>
    public sealed class StreamUpdate
    {
        public StreamCapability Capability { get; }
        public string Code { get; }
        public string Market { get; }
        public Observation<QuoteSnapshot> Quote { get; }
        public Observation<CandleSeries> Candles { get; }
        public Observation<OrderBookSnapshot> Book { get; }
        public StreamUpdate(StreamCapability capability, string code, string market,
            Observation<QuoteSnapshot> quote, Observation<CandleSeries> candles, Observation<OrderBookSnapshot> book)
        { Capability = capability; Code = code; Market = market; Quote = quote; Candles = candles; Book = book; }
    }

    public sealed class StreamHeartbeat
    {
        public bool BrokerConnected { get; }
        public string Reason { get; }
        public DateTimeOffset ServerTime { get; }
        public StreamHeartbeat(bool brokerConnected, string reason, DateTimeOffset serverTime)
        { BrokerConnected = brokerConnected; Reason = reason; ServerTime = serverTime; }
    }

    /// <summary>Strict decoding of gateway stream messages; every failure is a FormatException.</summary>
    public static class GatewayStreamContract
    {
        public static object Parse(string json, Guid subscription)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 2 * 1024 * 1024 || subscription == Guid.Empty)
                throw GatewayContract.Invalid();
            try
            {
                JToken token;
                using (var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None, MaxDepth = 32 })
                {
                    token = JToken.ReadFrom(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                    if (reader.Read()) throw GatewayContract.Invalid();
                }
                if (!(token is JObject root)) throw GatewayContract.Invalid();
                switch (Type(root))
                {
                    case "observation": return Observation(GatewayContract.Object(root, "type", "capability", "market", "observation"), subscription);
                    case "heartbeat": return Heartbeat(GatewayContract.Object(root, "type", "broker_connected", "reason", "server_time"));
                    default: throw GatewayContract.Invalid();
                }
            }
            catch (JsonException) { throw GatewayContract.Invalid(); }
            catch (ArgumentException) { throw GatewayContract.Invalid(); }
            catch (OverflowException) { throw GatewayContract.Invalid(); }
        }

        public static string Subscribe(string token, IEnumerable<string> codes, Guid subscription)
        {
            if (token == null || codes == null || subscription == Guid.Empty) throw new ArgumentException("Invalid subscription.");
            return new JObject
            {
                ["type"] = "subscribe", ["token"] = token, ["codes"] = new JArray(codes),
                ["correlation_id"] = subscription.ToString("D")
            }.ToString(Formatting.None);
        }

        private static string Type(JObject root) =>
            root.TryGetValue("type", StringComparison.Ordinal, out var t) && t.Type == JTokenType.String ? t.Value<string>() : throw GatewayContract.Invalid();

        private static StreamUpdate Observation(JObject root, Guid subscription)
        {
            var capability = GatewayContract.Text(root, "capability");
            var market = GatewayContract.Text(root, "market", true);
            if (market != null && market != "KRX" && market != "NXT") throw GatewayContract.Invalid();
            if (!(GatewayContract.Required(root, "observation") is JObject inner)) throw GatewayContract.Invalid();
            var code = GatewayContract.Text(inner, "instrument_code");
            var text = inner.ToString(Formatting.None);
            switch (capability)
            {
                case "quote": return new StreamUpdate(StreamCapability.Quote, code, market, GatewayContract.Quote(text, code, subscription), null, null);
                case "candles": return new StreamUpdate(StreamCapability.Candles, code, market, null, GatewayContract.Candles(text, code, subscription), null);
                case "orderbook": return new StreamUpdate(StreamCapability.OrderBook, code, market, null, null, GatewayContract.Book(text, code, subscription));
                default: throw GatewayContract.Invalid();
            }
        }

        private static StreamHeartbeat Heartbeat(JObject root)
        {
            var connected = GatewayContract.Required(root, "broker_connected");
            if (connected.Type != JTokenType.Boolean) throw GatewayContract.Invalid();
            var time = GatewayContract.Text(root, "server_time");
            if (!Regex.IsMatch(time, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$") ||
                !DateTimeOffset.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) throw GatewayContract.Invalid();
            return new StreamHeartbeat(connected.Value<bool>(), GatewayContract.Text(root, "reason", true), parsed);
        }
    }
}
