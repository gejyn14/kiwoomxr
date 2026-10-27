using SpatialTrading.Domain;

namespace SpatialTrading.Components
{
    /// <summary>Korean status wording shared by the panels. Pure: no Unity API.</summary>
    public static class StatusText
    {
        public const string MarketClosed = "MARKET_CLOSED";
        /// <summary>Label for any synthetic (preview) observation, whatever its state: it is never market data.</summary>
        public const string Synthetic = "SYNTHETIC · 합성 예시";

        public static string Name(DataState state)
        {
            switch (state)
            {
                case DataState.Loading: return "불러오는 중";
                case DataState.Live: return "실시간";
                case DataState.Delayed: return "스냅샷 · 실시간 보장 없음";
                case DataState.Stale: return "오래된 데이터";
                case DataState.Disconnected: return "연결 끊김";
                case DataState.Error: return "데이터 오류";
                default: return "데이터 이용 불가";
            }
        }

        /// <summary>State label; after the close the last session's values read as "장 마감".</summary>
        public static string State(DataState state, string reason) => reason == MarketClosed ? "장 마감" : Name(state);

        /// <summary><see cref="State(DataState, string)"/>, except that synthetic data always reads <see cref="Synthetic"/>.</summary>
        public static string State<T>(Observation<T> o) where T : class =>
            o?.Origin == DataOrigin.Synthetic ? Synthetic : State(o?.State ?? DataState.Unavailable, o?.Reason);

        /// <summary>Connection advice only for link and broker reasons; never for a closed market.</summary>
        public static string Hint(string reason)
        {
            if (reason == null) return string.Empty;
            if (reason.StartsWith("GATEWAY_") || reason.StartsWith("KIWOOM_") || reason == "BROKER_DISCONNECTED")
                return "연결 상태를 확인해 주세요.";
            return reason == MarketClosed ? "다음 장이 열리면\n시세가 갱신됩니다." : string.Empty;
        }

        /// <summary>Order-book caption with the same venue label (KRX/NXT) the chart shows.</summary>
        public static string BookCaption(string market, DataState state, string reason) =>
            (market == null ? "" : market + " · ") + State(state, reason) + " · 상위 3호가";
    }
}
