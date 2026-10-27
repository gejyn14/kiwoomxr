using SpatialTrading.Domain;

namespace SpatialTrading.Market
{
    /// <summary>
    /// Overlays the stream link state on the latest server observation. Priority: link reason
    /// (socket down) &gt; broker reason (heartbeat reports the broker down) &gt; heartbeat lost &gt; raw.
    /// Values are kept under every overlay; only the status and reason change. Pure: no Unity API.
    /// </summary>
    public static class StreamOverlay
    {
        public const string HeartbeatLost = "GATEWAY_HEARTBEAT_LOST";

        public static Observation<T> Apply<T>(Observation<T> raw, string linkReason, string brokerReason, bool heartbeatLost) where T : class
        {
            if (linkReason != null) return Overlay(raw, DataState.Disconnected, linkReason);
            if (raw == null) return new Observation<T>(null, DataState.Loading, DataOrigin.Kiwoom, null);
            if (brokerReason != null && (raw.State == DataState.Live || raw.State == DataState.Delayed || raw.State == DataState.Stale))
                return Overlay(raw, DataState.Disconnected, brokerReason);
            // Aging follows gateway heartbeats only; tick/source time never marks a value stale.
            if (heartbeatLost && (raw.State == DataState.Live || raw.State == DataState.Delayed))
                return Overlay(raw, DataState.Stale, HeartbeatLost);
            return raw;
        }

        private static Observation<T> Overlay<T>(Observation<T> raw, DataState state, string reason) where T : class =>
            raw == null ? new Observation<T>(null, state, DataOrigin.Kiwoom, null, reason: reason)
                : new Observation<T>(raw.Value, state, raw.Origin, raw.ReceivedAt, raw.SourceAt, reason);
    }
}
