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
    /// <summary>Strict gateway wire decoding. No broker fields, reflection deserialization or numeric coercion.</summary>
    public static class GatewayContract
    {
        public static Observation<QuoteSnapshot> Quote(string json, string code, Guid correlation)
        {
            var root = Read(json, code, correlation); var value = Required(root, "value");
            QuoteSnapshot quote = null;
            if (value.Type != JTokenType.Null)
            {
                var item = Object(value, "last_price", "change", "change_percent", "volume");
                quote = new QuoteSnapshot(code, Money(item, "last_price"), Money(item, "change"),
                    Money(item, "change_percent"), Quantity(item, "volume"));
            }
            return Wrap(root, quote);
        }

        public static Observation<CandleSeries> Candles(string json, string code, Guid correlation)
        {
            var root = Read(json, code, correlation); var value = Required(root, "value");
            CandleSeries series = null;
            if (value.Type != JTokenType.Null)
            {
                var item = Object(value, "interval", "adjustment", "complete_history", "candles");
                var bars = new List<Candle>();
                foreach (var token in Array(item, "candles"))
                {
                    var bar = Object(token, "period_key", "open", "high", "low", "close", "volume", "complete");
                    bars.Add(new Candle(Text(bar, "period_key"), Price(bar, "open"), Price(bar, "high"),
                        Price(bar, "low"), Price(bar, "close"), Quantity(bar, "volume"), Boolean(bar, "complete", true)));
                }
                series = new CandleSeries(code, Text(item, "interval"), bars, Text(item, "adjustment"),
                    Boolean(item, "complete_history", false).Value);
            }
            return Wrap(root, series);
        }

        public static Observation<OrderBookSnapshot> Book(string json, string code, Guid correlation)
        {
            var root = Read(json, code, correlation); var value = Required(root, "value");
            OrderBookSnapshot book = null;
            if (value.Type != JTokenType.Null)
            {
                var item = Object(value, "asks", "bids");
                book = new OrderBookSnapshot(code, Levels(item, "asks"), Levels(item, "bids"));
            }
            return Wrap(root, book);
        }

        private static JObject Read(string json, string code, Guid correlation)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 2 * 1024 * 1024 || correlation == Guid.Empty)
                throw Invalid();
            try
            {
                using (var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None, MaxDepth = 32 })
                {
                    var root = Object(JToken.ReadFrom(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error }),
                        "instrument_code", "state", "origin", "value", "received_at", "source_at", "reason", "correlation_id");
                    if (reader.Read() || Text(root, "instrument_code") != code || Text(root, "origin") != "KIWOOM" ||
                        !Guid.TryParseExact(Text(root, "correlation_id"), "D", out var actual) || actual != correlation) throw Invalid();
                    return root;
                }
            }
            catch (JsonException) { throw Invalid(); }
        }

        private static Observation<T> Wrap<T>(JObject root, T value) where T : class
        {
            var text = Text(root, "state");
            if (!Enum.TryParse(text, true, out DataState state) || !Enum.IsDefined(typeof(DataState), state) ||
                state.ToString().ToUpperInvariant() != text) throw Invalid();
            return new Observation<T>(value, state, DataOrigin.Kiwoom, Time(root, "received_at"),
                Time(root, "source_at"), Text(root, "reason", true));
        }

        private static IEnumerable<BookLevel> Levels(JObject item, string name)
        {
            var result = new List<BookLevel>();
            foreach (var token in Array(item, name))
            {
                var level = Object(token, "rank", "price", "quantity"); var rank = Required(level, "rank");
                if (rank.Type != JTokenType.Integer || !int.TryParse(rank.ToString(), out var number)) throw Invalid();
                result.Add(new BookLevel(number, Money(level, "price"), Quantity(level, "quantity")));
            }
            return result;
        }

        private static JObject Object(JToken token, params string[] names)
        {
            if (!(token is JObject obj) || obj.Count != names.Length) throw Invalid();
            foreach (var name in names) Required(obj, name);
            return obj;
        }
        private static JToken Required(JObject obj, string name) => obj.TryGetValue(name, StringComparison.Ordinal, out var token) ? token : throw Invalid();
        private static JArray Array(JObject obj, string name) => Required(obj, name) is JArray array ? array : throw Invalid();
        private static string Text(JObject obj, string name, bool nullable = false)
        {
            var token = Required(obj, name);
            if (nullable && token.Type == JTokenType.Null) return null;
            if (token.Type != JTokenType.String) throw Invalid();
            return token.Value<string>();
        }
        private static bool? Boolean(JObject obj, string name, bool nullable)
        {
            var token = Required(obj, name);
            if (nullable && token.Type == JTokenType.Null) return null;
            if (token.Type != JTokenType.Boolean) throw Invalid();
            return token.Value<bool>();
        }
        private static decimal? Money(JObject obj, string name) => FinancialDecimal.ParseOptional(Text(obj, name, true));
        private static decimal Price(JObject obj, string name) => Money(obj, name) ?? throw Invalid();
        private static long? Quantity(JObject obj, string name)
        {
            var text = Text(obj, name, true);
            if (text == null) return null;
            if (!Regex.IsMatch(text, @"^[0-9]+$") || !long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)) throw Invalid();
            return value;
        }
        private static DateTimeOffset? Time(JObject obj, string name)
        {
            var text = Text(obj, name, true);
            if (text == null) return null;
            if (!Regex.IsMatch(text, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$") ||
                !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) throw Invalid();
            return time;
        }
        private static FormatException Invalid() => new FormatException("INVALID_FINANCIAL_CONTRACT");
    }
}
