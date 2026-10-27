using System;
using System.Collections.Generic;
using System.IO;
using SpatialTrading.Domain;
using UnityEngine;

namespace SpatialTrading.Market
{
    /// <summary>
    /// Capability cache, separate from UI context. No brokerage details or secrets.
    /// Live mode streams from the gateway; the cache keeps the latest server observation per
    /// capability and reads overlay the link state (socket, broker heartbeat, heartbeat age).
    /// </summary>
    public sealed class MarketDataSession : MonoBehaviour
    {
        public const string EditorUrlPref = "SpatialTrading.GatewayStreamUrl";
        public const string EditorTokenPref = "SpatialTrading.GatewayPairingToken";
        /// <summary>Editor-only, in-memory override: when the editor process was started with these environment variables
        /// (e.g. the screenshot run), they win over EditorPrefs, so the pairing token is never written to disk.</summary>
        public const string EditorTokenVariable = "SPATIAL_PAIRING_TOKEN";
        public const string EditorUrlVariable = "SPATIAL_GATEWAY_URL";
        public const string DeviceSettingsFile = "gateway.json";
        private const string ConfigurationRequired = "GATEWAY_CONFIGURATION_REQUIRED";
        private const float HeartbeatLimitSeconds = 15;

        [SerializeField] private bool _preview;
        public event Action Changed;
        private readonly Dictionary<string, Observation<QuoteSnapshot>> _quotes = new Dictionary<string, Observation<QuoteSnapshot>>();
        private readonly Dictionary<string, Observation<CandleSeries>> _candles = new Dictionary<string, Observation<CandleSeries>>();
        private readonly Dictionary<string, Observation<CandleSeries>> _daily = new Dictionary<string, Observation<CandleSeries>>();
        private readonly Dictionary<string, Observation<CandleSeries>> _fourHour = new Dictionary<string, Observation<CandleSeries>>();
        private readonly Dictionary<string, Observation<OrderBookSnapshot>> _books = new Dictionary<string, Observation<OrderBookSnapshot>>();
        private readonly Dictionary<string, string> _markets = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _intervalMarkets = new Dictionary<string, string>(); // key: code|interval
        private readonly List<string> _codes = new List<string>(); // streamed codes; empty when not streaming
        private Observation<BalanceSnapshot> _balance;
        private Observation<NewsFeed> _news;
        private GatewayStream _stream;
        private bool _subscribed;
        private string _linkReason;   // non-null while the gateway socket is down
        private string _brokerReason; // non-null while the last heartbeat reported the broker disconnected
        private bool _heartbeatLost;
        private float? _heartbeatAt;
        private bool _dirty;
        public bool IsPreview => _preview;
        public void Configure(bool preview)
        {
            if (preview != _preview) ClearCache(); // synthetic values never survive into gateway mode, nor live ones into preview
            _preview = preview; _subscribed = false; StopStream();
        }

        public Observation<QuoteSnapshot> Quote(string code) => View(_quotes, code);
        /// <summary>Legacy one-minute candles, kept for the old shell.</summary>
        public Observation<CandleSeries> Candles(string code) => View(_candles, code);
        public Observation<CandleSeries> Daily(string code) => View(_daily, code);
        public Observation<CandleSeries> FourHour(string code) => View(_fourHour, code);
        public Observation<OrderBookSnapshot> Book(string code) => View(_books, code);
        public Observation<BalanceSnapshot> Balance() => ViewSingle(_balance);
        public Observation<NewsFeed> News() => ViewSingle(_news);
        /// <summary>Exchange of the latest streamed observation for a code: "KRX", "NXT" or null.</summary>
        public string MarketLabel(string code) =>
            !_preview && _codes.Contains(code) && _markets.TryGetValue(code, out var market) ? market : null;
        /// <summary>Exchange of the latest streamed candles of an interval ("1D" or "4H") for a code.</summary>
        public string MarketLabel(string code, string interval) =>
            !_preview && _codes.Contains(code) && _intervalMarkets.TryGetValue(code + "|" + interval, out var market) ? market : null;

        private Observation<T> Empty<T>(DataState state, string reason = null) where T : class =>
            new Observation<T>(null, state, _preview ? DataOrigin.Synthetic : DataOrigin.Kiwoom, null, reason: reason);

        private Observation<T> View<T>(Dictionary<string, Observation<T>> cache, string code) where T : class
        {
            cache.TryGetValue(code, out var raw);
            if (_preview || !_codes.Contains(code)) return raw ?? Empty<T>(DataState.Loading);
            return StreamOverlay.Apply(raw, _linkReason, _brokerReason, _heartbeatLost);
        }

        private Observation<T> ViewSingle<T>(Observation<T> raw) where T : class
        {
            if (_preview || _codes.Count == 0) return raw ?? Empty<T>(DataState.Loading);
            return StreamOverlay.Apply(raw, _linkReason, _brokerReason, _heartbeatLost);
        }

        /// <summary>Always-on subscription to every trader spread chart instrument, plus the account and news feeds.</summary>
        public void SubscribeAll(Guid correlationId)
        {
            if (_subscribed) return;
            _subscribed = true;
            if (_preview)
            {
                StopStream(); ClearCache();
                foreach (var instrument in TraderCatalog.Charts)
                {
                    var code = instrument.Code;
                    _quotes[code] = PreviewMarketData.Quote(instrument);
                    var candles = PreviewMarketData.Candles(instrument);
                    _candles[code] = candles;
                    _daily[code] = Relabel(candles, "1D"); _fourHour[code] = Relabel(candles, "4H");
                    if (instrument.Kind != InstrumentKind.Index) _books[code] = PreviewMarketData.Book(instrument);
                }
                // No synthetic account or news: those would be invented financial facts.
                _balance = Empty<BalanceSnapshot>(DataState.Unavailable, PreviewNoAccount);
                _news = Empty<NewsFeed>(DataState.Unavailable, PreviewNoNews);
                Changed?.Invoke(); return;
            }
            if (!LoadSettings(out var url, out var token))
            {
                StopStream(); ClearCache();
                foreach (var instrument in TraderCatalog.Charts)
                {
                    var code = instrument.Code;
                    _quotes[code] = Empty<QuoteSnapshot>(DataState.Unavailable, ConfigurationRequired);
                    _candles[code] = Empty<CandleSeries>(DataState.Unavailable, ConfigurationRequired);
                    _daily[code] = Empty<CandleSeries>(DataState.Unavailable, ConfigurationRequired);
                    _fourHour[code] = Empty<CandleSeries>(DataState.Unavailable, ConfigurationRequired);
                    if (instrument.Kind != InstrumentKind.Index) _books[code] = Empty<OrderBookSnapshot>(DataState.Unavailable, ConfigurationRequired);
                }
                _balance = Empty<BalanceSnapshot>(DataState.Unavailable, ConfigurationRequired);
                _news = Empty<NewsFeed>(DataState.Unavailable, ConfigurationRequired);
                Changed?.Invoke(); return;
            }
            var codes = new List<string>();
            foreach (var instrument in TraderCatalog.Charts) codes.Add(instrument.Code);
            _codes.Clear(); _codes.AddRange(codes);
            if (_stream == null) BindStream();
            _stream.Connect(url, token, codes);
            _heartbeatAt = Time.unscaledTime; // the subscription gets a fresh heartbeat window
            Changed?.Invoke();
        }

        public const string PreviewNoAccount = "PREVIEW_NO_ACCOUNT_DATA";
        public const string PreviewNoNews = "PREVIEW_NO_NEWS_DATA";

        private static Observation<CandleSeries> Relabel(Observation<CandleSeries> source, string interval) =>
            new Observation<CandleSeries>(new CandleSeries(source.Value.InstrumentCode, interval, source.Value.Candles),
                source.State, source.Origin, source.ReceivedAt, source.SourceAt, source.Reason);

        public static bool AllowedGateway(string address) => GatewayStreamContract.AllowedStreamAddress(address);

        /// <summary>Editor: the process environment (<see cref="EditorTokenVariable"/>/<see cref="EditorUrlVariable"/>) when the
        /// token variable is set, otherwise EditorPrefs set from the Spatial Trading menu. Device: persistentDataPath/gateway.json.
        /// None of these is part of the build.</summary>
        private static bool LoadSettings(out Uri url, out string token)
        {
            url = null; token = null;
#if UNITY_EDITOR
            var secret = Environment.GetEnvironmentVariable(EditorTokenVariable);
            var address = string.IsNullOrEmpty(secret)
                ? UnityEditor.EditorPrefs.GetString(EditorUrlPref, string.Empty)
                : Environment.GetEnvironmentVariable(EditorUrlVariable) ?? string.Empty;
            if (string.IsNullOrEmpty(secret)) secret = UnityEditor.EditorPrefs.GetString(EditorTokenPref, string.Empty);
            if (!AllowedGateway(address) || secret.Length == 0) return false;
            url = new Uri(address); token = secret;
            return true;
#else
            try
            {
                var file = new FileInfo(Path.Combine(Application.persistentDataPath, DeviceSettingsFile));
                if (!file.Exists || file.Length > GatewayStreamContract.MaxSettingsChars) return false;
                return GatewayStreamContract.TryReadSettings(File.ReadAllText(file.FullName), out url, out token);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Debug.LogWarning("[MarketDataSession] Gateway settings unreadable: " + error.GetType().Name);
                return false;
            }
#endif
        }

        private void BindStream()
        {
            _stream = GetComponent<GatewayStream>();
            if (_stream == null) _stream = gameObject.AddComponent<GatewayStream>();
            _stream.Updated += OnStreamUpdate;
            _stream.Heartbeat += OnHeartbeat;
            _stream.Disconnected += OnDisconnected;
        }

        private void StopStream()
        {
            if (_stream != null) _stream.Close();
            _codes.Clear();
            _linkReason = null; _brokerReason = null; _heartbeatLost = false; _heartbeatAt = null;
        }

        private void ClearCache()
        {
            _quotes.Clear(); _candles.Clear(); _daily.Clear(); _fourHour.Clear(); _books.Clear();
            _markets.Clear(); _intervalMarkets.Clear(); _balance = null; _news = null;
        }

        private void OnStreamUpdate(StreamUpdate update)
        {
            if (_preview || _codes.Count == 0) return;
            switch (update.Capability)
            {
                case StreamCapability.Balance: _balance = update.Balance; break;
                case StreamCapability.News: _news = update.News; break;
                default:
                    if (!_codes.Contains(update.Code)) return;
                    switch (update.Capability)
                    {
                        case StreamCapability.Quote: _quotes[update.Code] = update.Quote; _markets[update.Code] = update.Market; break;
                        case StreamCapability.Candles: _candles[update.Code] = update.Candles; _markets[update.Code] = update.Market; break;
                        case StreamCapability.OrderBook: _books[update.Code] = update.Book; _markets[update.Code] = update.Market; break;
                        case StreamCapability.CandlesDaily: _daily[update.Code] = update.Candles; _intervalMarkets[update.Code + "|1D"] = update.Market; break;
                        case StreamCapability.CandlesFourHour: _fourHour[update.Code] = update.Candles; _intervalMarkets[update.Code + "|4H"] = update.Market; break;
                    }
                    break;
            }
            _dirty = true; // coalesced to one render per frame in LateUpdate
        }

        private void OnHeartbeat(StreamHeartbeat beat)
        {
            if (_preview || _codes.Count == 0) return;
            _heartbeatAt = Time.unscaledTime;
            var broker = beat.BrokerConnected ? null : beat.Reason ?? "BROKER_DISCONNECTED";
            var changed = _linkReason != null || _heartbeatLost || _brokerReason != broker;
            _linkReason = null; _heartbeatLost = false; _brokerReason = broker;
            if (changed) _dirty = true; // coalesced in LateUpdate so a render never mixes pre-outage values with a cleared link status
        }

        private void OnDisconnected(string reason)
        {
            reason = reason ?? GatewayStream.Unreachable;
            if (_preview || _codes.Count == 0 || _linkReason == reason) return;
            _linkReason = reason; // last values are kept and shown as DISCONNECTED until the next heartbeat
            Changed?.Invoke();
        }

        private void Update()
        {
            if (_preview || _codes.Count == 0 || !_heartbeatAt.HasValue) return;
            var lost = Time.unscaledTime - _heartbeatAt.Value > HeartbeatLimitSeconds;
            if (lost == _heartbeatLost) return;
            _heartbeatLost = lost;
            Changed?.Invoke();
        }

        private void LateUpdate()
        {
            if (!_dirty) return;
            _dirty = false;
            Changed?.Invoke();
        }

        private void OnDestroy()
        {
            if (_stream == null) return;
            _stream.Updated -= OnStreamUpdate;
            _stream.Heartbeat -= OnHeartbeat;
            _stream.Disconnected -= OnDisconnected;
            _stream.Close();
        }
    }
}
