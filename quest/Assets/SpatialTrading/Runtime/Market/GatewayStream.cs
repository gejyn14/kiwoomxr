using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SpatialTrading.Market
{
    /// <summary>
    /// Gateway stream client. A background task owns the socket and only enqueues raw text;
    /// decoding and every event run on the Unity main thread in <see cref="Update"/>.
    /// The pairing token is held in memory only and never logged.
    /// </summary>
    public sealed class GatewayStream : MonoBehaviour
    {
        public const int MaxMessageBytes = 2 * 1024 * 1024;
        public const string Unreachable = "GATEWAY_UNREACHABLE";
        public const string Unauthorized = "GATEWAY_UNAUTHORIZED";
        public const string MessageTooLarge = "GATEWAY_MESSAGE_TOO_LARGE";
        private const int CloseUnauthorized = 4401;
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(8);
        // Heartbeats arrive every 5 s; a silent socket this long is treated as half-open.
        private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(20);
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public event Action<StreamUpdate> Updated;
        public event Action<StreamHeartbeat> Heartbeat;
        public event Action<string> Disconnected;

        private readonly ConcurrentQueue<Inbound> _inbox = new ConcurrentQueue<Inbound>();
        private CancellationTokenSource _cancel;
        private int _generation;
        private Guid _subscription;
        private Uri _url;
        private string _token;
        private string[] _codes;
        private string _lastFailure;

        private struct Inbound
        {
            public int Generation;
            public string Text;     // message text, or null for a disconnect
            public string Reason;   // disconnect reason
            public string Error;     // exception type name only, for diagnostics
        }

        public void Connect(Uri url, string token, IReadOnlyList<string> codes)
        {
            if (url == null || !GatewayStreamContract.AllowedStreamAddress(url.OriginalString) ||
                string.IsNullOrEmpty(token) || codes == null || codes.Count == 0)
                throw new ArgumentException("Invalid gateway stream configuration.");
            Stop();
            _url = url; _token = token; _codes = new List<string>(codes).ToArray();
            if (isActiveAndEnabled) Begin();
        }

        /// <summary>Stops streaming and forgets the connection settings.</summary>
        public void Close() { Stop(); _url = null; _token = null; _codes = null; }

        private void Begin()
        {
            if (_url == null || _cancel != null) return;
            _subscription = Guid.NewGuid();
            var subscribe = GatewayStreamContract.Subscribe(_token, _codes, _subscription);
            var generation = ++_generation;
            var cancel = _cancel = new CancellationTokenSource();
            var url = _url;
            Task.Run(() => Run(url, subscribe, generation, cancel.Token))
                .ContinueWith(_ => cancel.Dispose(), TaskScheduler.Default);
        }

        private void Stop()
        {
            _generation++; // late messages from the cancelled socket are discarded in Update
            if (_cancel == null) return;
            try { _cancel.Cancel(); }
            catch (ObjectDisposedException) { } // the receive task already ended and released it
            _cancel = null;
        }

        private void OnEnable() => Begin();
        private void OnDisable() => Stop();
        private void OnDestroy() => Close();

        private void Update()
        {
            // Bounded per frame so a burst cannot stall rendering.
            for (var i = 0; i < 256 && _inbox.TryDequeue(out var item); i++)
            {
                if (item.Generation != _generation) continue;
                if (item.Text == null)
                {
                    var failure = item.Reason + "/" + item.Error;
                    if (failure != _lastFailure) Debug.LogWarning("[GatewayStream] Disconnected: " + item.Reason + (item.Error == null ? "" : " (" + item.Error + ")"));
                    _lastFailure = failure;
                    Disconnected?.Invoke(item.Reason);
                    continue;
                }
                object message;
                try { message = GatewayStreamContract.Parse(item.Text, _subscription); }
                catch (FormatException)
                { Debug.LogWarning("[GatewayStream] Dropped a malformed stream message."); continue; }
                _lastFailure = null;
                if (message is StreamUpdate update) Updated?.Invoke(update);
                else if (message is StreamHeartbeat beat) Heartbeat?.Invoke(beat);
            }
        }

        // ---- background thread: no Unity API below this line ----

        private async Task Run(Uri url, string subscribe, int generation, CancellationToken cancel)
        {
            var attempt = 0;
            while (!cancel.IsCancellationRequested)
            {
                var reason = Unreachable;
                string error = null;
                var delivered = false;
                try
                {
                    using (var socket = new ClientWebSocket())
                    {
                        using (var connect = CancellationTokenSource.CreateLinkedTokenSource(cancel))
                        {
                            connect.CancelAfter(ConnectTimeout);
                            await socket.ConnectAsync(url, connect.Token).ConfigureAwait(false);
                            var bytes = Encoding.UTF8.GetBytes(subscribe);
                            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, connect.Token).ConfigureAwait(false);
                        }
                        reason = await Receive(socket, generation, cancel, () => delivered = true).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested) { return; }
                catch (Exception failure) { error = failure.GetType().Name; }
                if (cancel.IsCancellationRequested) return;
                _inbox.Enqueue(new Inbound { Generation = generation, Reason = reason, Error = error });
                // Only a session that delivered data and then dropped resets the backoff; refusals
                // (including 4401) and protocol violations such as oversized messages keep backing off.
                if (delivered && reason == Unreachable && error == null) attempt = 0;
                var delay = TimeSpan.FromSeconds(Math.Min(8, 1 << Math.Min(attempt, 3)));
                attempt++;
                try { await Task.Delay(delay, cancel).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }

        private async Task<string> Receive(ClientWebSocket socket, int generation, CancellationToken cancel, Action delivered)
        {
            var buffer = new byte[16 * 1024];
            using (var message = new MemoryStream())
            {
                while (true)
                {
                    WebSocketReceiveResult result;
                    using (var idle = CancellationTokenSource.CreateLinkedTokenSource(cancel))
                    {
                        idle.CancelAfter(IdleTimeout);
                        try { result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), idle.Token).ConfigureAwait(false); }
                        catch (OperationCanceledException) when (!cancel.IsCancellationRequested) { return Unreachable; }
                    }
                    if (result.MessageType == WebSocketMessageType.Close)
                        return (int?)result.CloseStatus == CloseUnauthorized ? Unauthorized : Unreachable;
                    if (message.Length + result.Count > MaxMessageBytes)
                    {
                        await CloseQuietly(socket, WebSocketCloseStatus.MessageTooBig).ConfigureAwait(false);
                        return MessageTooLarge;
                    }
                    message.Write(buffer, 0, result.Count);
                    if (!result.EndOfMessage) continue;
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string text = null;
                        try { text = StrictUtf8.GetString(message.GetBuffer(), 0, (int)message.Length); }
                        catch (DecoderFallbackException) { text = string.Empty; } // dropped as malformed on the main thread
                        _inbox.Enqueue(new Inbound { Generation = generation, Text = text });
                        delivered();
                    }
                    message.SetLength(0);
                }
            }
        }

        private static async Task CloseQuietly(ClientWebSocket socket, WebSocketCloseStatus status)
        {
            try
            {
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                    await socket.CloseOutputAsync(status, null, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception) { /* the socket is disposed next either way */ }
        }
    }
}
