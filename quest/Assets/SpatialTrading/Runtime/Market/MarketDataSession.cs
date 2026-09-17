using System;
using System.Collections;
using System.Collections.Generic;
using SpatialTrading.Domain;
using UnityEngine;
using UnityEngine.Networking;

namespace SpatialTrading.Market
{
    /// <summary>Capability cache, separate from UI context. No brokerage details or secrets.</summary>
    public sealed class MarketDataSession : MonoBehaviour
    {
        [SerializeField] private bool _preview;
        [SerializeField] private string _gateway = "http://127.0.0.1:8471";
        public event Action Changed;
        private readonly Dictionary<string, Observation<QuoteSnapshot>> _quotes = new Dictionary<string, Observation<QuoteSnapshot>>();
        private readonly Dictionary<string, Observation<CandleSeries>> _candles = new Dictionary<string, Observation<CandleSeries>>();
        private readonly Dictionary<string, Observation<OrderBookSnapshot>> _books = new Dictionary<string, Observation<OrderBookSnapshot>>();
        private readonly HashSet<UnityWebRequest> _requests = new HashSet<UnityWebRequest>();
        private string _selected;
        private string _compared;
        private int _generation;
        private float _nextAgeCheck;
        public bool IsPreview => _preview;
        public void Configure(bool preview) { _preview = preview; _selected = null; _compared = null; }

        public Observation<QuoteSnapshot> Quote(string code) => _quotes.TryGetValue(code, out var data) ? data : Empty<QuoteSnapshot>(DataState.Loading);
        public Observation<CandleSeries> Candles(string code) => _candles.TryGetValue(code, out var data) ? data : Empty<CandleSeries>(DataState.Loading);
        public Observation<OrderBookSnapshot> Book(string code) => _books.TryGetValue(code, out var data) ? data : Empty<OrderBookSnapshot>(DataState.Loading);
        private Observation<T> Empty<T>(DataState state, string reason = null) where T : class =>
            new Observation<T>(null, state, _preview ? DataOrigin.Synthetic : DataOrigin.Kiwoom, null, reason: reason);

        public void Select(ShellState state, Guid correlationId)
        {
            var comparison = state.ComparisonInstrument?.Code;
            if (_selected == state.SelectedInstrument.Code && _compared == comparison) return;
            _selected = state.SelectedInstrument.Code; _compared = comparison; _generation++;
            CancelPendingRequests();
            // Cancelled epochs cannot overwrite a newer selection, even when switching back.
            _quotes.Clear(); _candles.Clear(); _books.Clear();
            if (_preview)
            {
                _quotes[_selected] = PreviewMarketData.Quote(state.SelectedInstrument);
                _candles[_selected] = PreviewMarketData.Candles(state.SelectedInstrument);
                _books[_selected] = PreviewMarketData.Book(state.SelectedInstrument);
                if (state.ComparisonInstrument != null) _quotes[comparison] = PreviewMarketData.Quote(state.ComparisonInstrument);
                Changed?.Invoke(); return;
            }
            if (!AllowedGateway(_gateway))
            {
                _quotes[_selected] = Empty<QuoteSnapshot>(DataState.Unavailable, "GATEWAY_CONFIGURATION_REQUIRED");
                _candles[_selected] = Empty<CandleSeries>(DataState.Unavailable, "GATEWAY_CONFIGURATION_REQUIRED");
                _books[_selected] = Empty<OrderBookSnapshot>(DataState.Unavailable, "GATEWAY_CONFIGURATION_REQUIRED");
                if (comparison != null) _quotes[comparison] = Empty<QuoteSnapshot>(DataState.Unavailable, "GATEWAY_CONFIGURATION_REQUIRED");
                Changed?.Invoke(); return;
            }
            StartCoroutine(Fetch(_selected, "quote", _generation, correlationId));
            StartCoroutine(Fetch(_selected, "candles", _generation, correlationId));
            StartCoroutine(Fetch(_selected, "orderbook", _generation, correlationId));
            if (comparison != null) StartCoroutine(Fetch(comparison, "quote", _generation, correlationId));
        }

        public static bool AllowedGateway(string address)
        {
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0) return false;
            if (uri.Scheme == Uri.UriSchemeHttps) return true;
#if UNITY_EDITOR
            return uri.Scheme == Uri.UriSchemeHttp && (uri.Host == "127.0.0.1" || uri.Host == "localhost" || uri.Host == "[::1]");
#else
            return false;
#endif
        }

        private IEnumerator Fetch(string code, string capability, int generation, Guid correlationId)
        {
            using (var request = UnityWebRequest.Get(_gateway.TrimEnd('/') + "/v1/market/" + Uri.EscapeDataString(code) + "/" + capability))
            {
                _requests.Add(request);
                request.timeout = 8; request.redirectLimit = 0;
                request.SetRequestHeader("X-Correlation-ID", correlationId.ToString());
                yield return request.SendWebRequest();
                if (generation != _generation) { _requests.Remove(request); yield break; }
                if (request.result != UnityWebRequest.Result.Success)
                    Fail(code, capability, DataState.Disconnected, "GATEWAY_UNREACHABLE");
                else
                {
                    try
                    {
                        if (request.downloadedBytes > 2 * 1024 * 1024) throw new FormatException();
                        var text = request.downloadHandler.text;
                        if (capability == "quote") _quotes[code] = GatewayContract.Quote(text, code, correlationId);
                        else if (capability == "candles") _candles[code] = GatewayContract.Candles(text, code, correlationId);
                        else _books[code] = GatewayContract.Book(text, code, correlationId);
                    }
                    catch (Exception error) when (error is ArgumentException || error is FormatException || error is OverflowException)
                    { Fail(code, capability, DataState.Error, "INVALID_FINANCIAL_CONTRACT"); }
                }
                _requests.Remove(request);
                Changed?.Invoke();
            }
        }

        private void CancelPendingRequests()
        {
            // Abort before stopping enumerators, whose using blocks may dispose requests.
            foreach (var request in _requests) request.Abort();
            StopAllCoroutines();
            foreach (var request in _requests) request.Dispose();
            _requests.Clear();
        }
        private void OnDestroy() { _generation++; CancelPendingRequests(); }

        private void Fail(string code, string capability, DataState state, string reason)
        {
            if (capability == "quote") _quotes[code] = Empty<QuoteSnapshot>(state, reason);
            else if (capability == "candles") _candles[code] = Empty<CandleSeries>(state, reason);
            else _books[code] = Empty<OrderBookSnapshot>(state, reason);
        }

        private void Update()
        {
            if (_preview || Time.unscaledTime < _nextAgeCheck) return;
            _nextAgeCheck = Time.unscaledTime + 1;
            var changed = Age(_quotes) | Age(_candles) | Age(_books);
            if (changed) Changed?.Invoke();
        }

        private static bool Age<T>(Dictionary<string, Observation<T>> cache) where T : class
        {
            var changed = false;
            foreach (var key in new List<string>(cache.Keys))
            {
                var old = cache[key];
                // UI cache TTL, not a claim about an exchange SLA or source freshness.
                if (old.Value == null || !old.ReceivedAt.HasValue || old.State != DataState.Live && old.State != DataState.Delayed ||
                    DateTimeOffset.UtcNow - old.ReceivedAt.Value <= TimeSpan.FromSeconds(30)) continue;
                cache[key] = new Observation<T>(old.Value, DataState.Stale, old.Origin, old.ReceivedAt, old.SourceAt, "CLIENT_CACHE_AGE_LIMIT");
                changed = true;
            }
            return changed;
        }

    }
}
