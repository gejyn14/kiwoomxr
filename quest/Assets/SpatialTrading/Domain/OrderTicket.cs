using System;

namespace SpatialTrading.Domain
{
    public enum TradingSession { KrxRegular, Nxt, Closed }

    /// <summary>Session windows mirror gateway market/session.py (half-open, KST, weekends closed).</summary>
    public static class TradingSessions
    {
        public static TradingSession SessionAt(DateTimeOffset kst)
        {
            var local = kst.ToOffset(TimeSpan.FromHours(9));
            if (local.DayOfWeek == DayOfWeek.Saturday || local.DayOfWeek == DayOfWeek.Sunday)
                return TradingSession.Closed;
            int minutes = local.Hour * 60 + local.Minute;
            if (minutes >= 9 * 60 && minutes < 15 * 60 + 30) return TradingSession.KrxRegular;
            if ((minutes >= 8 * 60 && minutes < 8 * 60 + 50) ||
                (minutes >= 15 * 60 + 30 && minutes < 20 * 60)) return TradingSession.Nxt;
            return TradingSession.Closed;
        }
    }

    /// <summary>Display names: 지정가 시장가 최유리 최우선 조건부. Kiwoom kt10000 trde_tp: 0 3 6 7 5.</summary>
    public enum OrderType { Limit, Market, BestLimit, PriorityLimit, Conditional }

    /// <summary>
    /// Which order types a session allows. 근거: kiwoom-cli order.py ORDER_TYPES(limit 0, market 3,
    /// conditional 5, best 6, first 7) and docs/KIWOOM_API_MAPPING.md (kt10000 trde_tp 0/3).
    /// NXT 제한은 문서에 명시 없음 → 보수적으로 지정가만.
    /// </summary>
    public static class OrderTypeTable
    {
        public static string KiwoomTradeType(OrderType t)
        {
            switch (t)
            {
                case OrderType.Limit: return "0";
                case OrderType.Market: return "3";
                case OrderType.BestLimit: return "6";
                case OrderType.PriorityLimit: return "7";
                default: return "5";
            }
        }

        public static string DisplayName(OrderType t)
        {
            switch (t)
            {
                case OrderType.Limit: return "지정가";
                case OrderType.Market: return "시장가";
                case OrderType.BestLimit: return "최유리";
                case OrderType.PriorityLimit: return "최우선";
                default: return "조건부";
            }
        }

        public static bool Enabled(OrderType t, TradingSession s) => DisabledReason(t, s) == null;

        /// <summary>Null when the type is enabled.</summary>
        public static string DisabledReason(OrderType t, TradingSession s)
        {
            if (t == OrderType.Conditional) return "시연 범위 밖";
            if (s == TradingSession.Closed) return "장 마감";
            if (s == TradingSession.Nxt && t != OrderType.Limit) return "NXT 미지원";
            return null;
        }

        public static OrderType Default(TradingSession s) =>
            s == TradingSession.KrxRegular ? OrderType.Market : OrderType.Limit;
    }

    public enum TicketState { Idle, Locked, Preflight }

    /// <summary>Preflight preview only. Nothing here sends an order.</summary>
    public sealed class OrderPreview
    {
        public string Code { get; }
        public string Name { get; }
        public string Side { get; }
        public OrderType Type { get; }
        public string KiwoomTradeType { get; }
        public decimal? Price { get; }
        public int Quantity { get; }
        public string Market { get; }
        public string Badge { get; }

        public OrderPreview(string code, string name, OrderType type, string kiwoomTradeType, decimal? price,
            int quantity, string market, string side = "매수", string badge = "PREFLIGHT")
        {
            Code = code; Name = name; Side = side; Type = type; KiwoomTradeType = kiwoomTradeType;
            Price = price; Quantity = quantity; Market = market; Badge = badge;
        }
    }

    public sealed class OrderTicket
    {
        InstrumentRef _instrument;
        TradingSession _session = TradingSession.Closed;

        public TicketState State { get; private set; } = TicketState.Idle;
        public string LockReason { get; private set; }
        public OrderType Type { get; private set; } = OrderType.Limit;
        public int Quantity => 1;
        public bool SellLocked => true;

        public void Select(InstrumentRef instrument, TradingSession session)
        {
            if (instrument == null) throw new ArgumentNullException(nameof(instrument));
            bool same = _instrument != null && _instrument.Equals(instrument) && _session == session;
            _instrument = instrument;
            _session = session;
            if (instrument.Kind == InstrumentKind.Index)
            {
                State = TicketState.Locked; LockReason = "지수 · 주문 불가";
            }
            else if (instrument.Code != TraderCatalog.OrderableCode)
            {
                State = TicketState.Locked; LockReason = "시연 안전장치: 삼성전자만";
            }
            else
            {
                LockReason = null;
                if (same && State == TicketState.Preflight) return;
                State = TicketState.Idle;
            }
            Type = OrderTypeTable.Default(session);
        }

        public bool TryChooseType(OrderType t)
        {
            if (State == TicketState.Locked || !OrderTypeTable.Enabled(t, _session)) return false;
            if (t != Type && State == TicketState.Preflight) State = TicketState.Idle;
            Type = t;
            return true;
        }

        public bool TryPrepare(decimal? bestAsk, decimal? lastPrice, out OrderPreview preview)
        {
            preview = null;
            if (State == TicketState.Locked || _instrument == null) return false;
            if (!OrderTypeTable.Enabled(Type, _session)) return false;
            decimal? price;
            if (Type == OrderType.Limit)
            {
                if (bestAsk == null || bestAsk <= 0m) return false;
                price = bestAsk; // 최우선 매도호가 지정가
            }
            else
            {
                if (lastPrice == null || lastPrice <= 0m) return false; // reference price required
                price = null;    // exchange prices market / best / priority orders
            }
            preview = new OrderPreview(_instrument.Code, _instrument.DisplayName, Type,
                OrderTypeTable.KiwoomTradeType(Type), price, Quantity,
                _session == TradingSession.Nxt ? "NXT" : "KRX");
            State = TicketState.Preflight;
            return true;
        }

        public void Cancel()
        {
            if (State == TicketState.Preflight) State = TicketState.Idle;
        }
    }
}
