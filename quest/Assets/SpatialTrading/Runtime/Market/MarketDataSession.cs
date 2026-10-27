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
        public const string DeviceSettingsFile = "gateway.json";
        public const string HeartbeatLost = "GATEWAY_HEARTBEAT_LOST";
        private const string ConfigurationRequired = "GATEWAY_CONFIGURATION_REQUIRED";
        private const float HeartbeatLimitSeconds = 15;

        [SerializeField] private bool _preview;
        public event Action Changed;
        private readonly Dictionary<string, Observation<QuoteSnapshot>> _quotes = new Dictionary<string, Observation<QuoteSnapshot>>();
        private readonly Dictionary<string, Observation<CandleSeries>> _candles = new Dictionary<string, Observation<CandleSeries>>();
        private readonly Dictionary<string, Observation<OrderBookSnapshot>> _books = new Dictionary<string, Observation<OrderBookSnapshot>>();
        private readonly Dictionary<string, string> _markets = new Dictionary<string, string>();
        private readonly List<string> _codes = new List<string>(); // streamed codes; empty when not streaming
        private GatewayStream _stream;
        private string _selected;
        private string _compared;
        private string _linkReason;   // non-null while the gateway socket is down
        private string _brokerReason; // non-null while the last heartbeat reported the broker disconnected
        private bool _heartbeatLost;
        private float? _heartbeatAt;
        private bool _dirty;
        public bool IsPreview => _preview;
        public void Configure(bool preview) { _preview = preview; _selected = null; _compared = null; StopStream(); }

        public Observation<QuoteSnapshot> Quote(string code) => View(_quotes, code);
        public Observation<CandleSeries> Candles(string code) => View(_candles, code);
        public Observation<OrderBookSnapshot> Book(string code) => View(_books, code);
        /// <summary>Exchange of the latest streamed observation for a code: "KRX", "NXT" or null.</summary>
        public string MarketLabel(string code) =>
            !_preview && _codes.Contains(code) && _markets.TryGetValue(code, out var market) ? market : null;

        private Observation<T> Empty<T>(DataState state, string reason = null) where T : class =>
            new Observation<T>(null, state, _preview ? DataOrigin.Synthetic : DataOrigin.Kiwoom, null, reason: reason);

        private Observation<T> View<T>(Dictionary<string, Observation<T>> cache, string code) where T : class
        {
            cache.TryGetValue(code, out var raw);
            if (_preview || !_codes.Contains(code)) return raw ?? Empty<T>(DataState.Loading);
            if (_linkReason != null) return Overlay(raw, DataState.Disconnected, _linkReason);
            if (raw == null) return Empty<T>(DataState.Loading);
            if (_brokerReason != null && (raw.State == DataState.Live || raw.State == DataState.Delayed || raw.State == DataState.Stale))
                return Overlay(raw, DataState.Disconnected, _brokerReason);
            // Aging follows gateway heartbeats only; tick/source time never marks a value stale.
            if (_heartbeatLost && (raw.State == DataState.Live || raw.State == DataState.Delayed))
                return Overlay(raw, DataState.Stale, HeartbeatLost);
            return raw;
        }

        private Observation<T> Overlay<T>(Observation<T> raw, DataState state, string reason) where T : class =>
            raw == null ? Empty<T>(state, reason) : new Observation<T>(raw.Value, state, raw.Origin, raw.ReceivedAt, raw.SourceAt, reason);

        public void Select(ShellState state, Guid correlationId)
        {
            var comparison = state.ComparisonInstrument?.Code;
            if (_selected == state.SelectedInstrument.Code && _compared == comparison) return;
            _selected = state.SelectedInstrument.Code; _compared = comparison;
            if (_preview)
            {
                StopStream(); ClearCache();
                _quotes[_selected] = PreviewMarketData.Quote(state.SelectedInstrument);
                _candles[_selected] = PreviewMarketData.Candles(state.SelectedInstrument);
                _books[_selected] = PreviewMarketData.Book(state.SelectedInstrument);
                if (state.ComparisonInstrument != null) _quotes[comparison] = PreviewMarketData.Quote(state.ComparisonInstrument);
                Changed?.Invoke(); return;
            }
            if (!LoadSettings(out var url, out var token))
            {
                StopStream(); ClearCache();
                _quotes[_selected] = Empty<QuoteSnapshot>(DataState.Unavailable, ConfigurationRequired);
                _candles[_selected] = Empty<CandleSeries>(DataState.Unavailable, ConfigurationRequired);
                _books[_selected] = Empty<OrderBookSnapshot>(DataState.Unavailable, ConfigurationRequired);
                if (comparison != null) _quotes[comparison] = Empty<QuoteSnapshot>(DataState.Unavailable, ConfigurationRequired);
                Changed?.Invoke(); return;
            }
            var codes = new List<string> { _selected };
            if (comparison != null && comparison != _selected) codes.Add(comparison);
            // Stream mode serves the current cache; values of codes leaving the subscription are dropped.
            Retain(_quotes, codes); Retain(_candles, codes); Retain(_books, codes); Retain(_markets, codes);
            if (_codes.Count != codes.Count || !codes.TrueForAll(_codes.Contains))
            {
                _codes.Clear(); _codes.AddRange(codes);
                if (_stream == null) BindStream();
                _stream.Connect(url, token, codes);
                if (!_heartbeatAt.HasValue) _heartbeatAt = Time.unscaledTime;
            }
            Changed?.Invoke();
        }

        public static bool AllowedGateway(string address) => GatewayStreamContract.AllowedStreamAddress(address);

        /// <summary>Editor: EditorPrefs set from the Spatial Trading menu. Device: persistentDataPath/gateway.json.
        /// Neither is part of the build.</summary>
        private static bool LoadSettings(out Uri url, out string token)
        {
            url = null; token = null;
#if UNITY_EDITOR
            var address = UnityEditor.EditorPrefs.GetString(EditorUrlPref, string.Empty);
            var secret = UnityEditor.EditorPrefs.GetString(EditorTokenPref, string.Empty);
            if (!AllowedGateway(address) || secret.Length == 0) return false;
            url = new Uri(address); token = secret;
            return true;
#else
            try
            {
                var file = new FileInfo(Path.Combine(Application.persistentDataPath, DeviceSettingsFile));
                if (!file.Exists || file.Length > 64 * 1024) return false;
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

        private void ClearCache() { _quotes.Clear(); _candles.Clear(); _books.Clear(); _markets.Clear(); }

        private static void Retain<T>(Dictionary<string, T> cache, List<string> codes)
        {
            foreach (var key in new List<string>(cache.Keys))
                if (!codes.Contains(key)) cache.Remove(key);
        }

        private void OnStreamUpdate(StreamUpdate update)
        {
            if (_preview || !_codes.Contains(update.Code)) return;
            _markets[update.Code] = update.Market;
            switch (update.Capability)
            {
                case StreamCapability.Quote: _quotes[update.Code] = update.Quote; break;
                case StreamCapability.Candles: _candles[update.Code] = update.Candles; break;
                case StreamCapability.OrderBook: _books[update.Code] = update.Book; break;
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
            if (changed) Changed?.Invoke();
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
