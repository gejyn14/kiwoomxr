from datetime import datetime
from decimal import Decimal

from spatial_gateway.kiwoom.realtime import Trade
from spatial_gateway.kiwoom.rest import Bar
from spatial_gateway.kiwoom.values import KST
from spatial_gateway.market.charts import ChartStore
from spatial_gateway.market.session import Market
from spatial_gateway.models import DataState

CID = "11111111-1111-4111-8111-111111111111"


def kst(h, m, day=28):
    return datetime(2026, 10, day, h, m, tzinfo=KST)


class Clock:
    def __init__(self, now):
        self.now = now

    def __call__(self):
        return self.now


def bar(start, price, volume=10):
    p = Decimal(str(price))
    return Bar(start=start, open=p, high=p, low=p, close=p, volume=volume)


def store(clock):
    return ChartStore(clock, watch_codes=["005930"], index_codes=["001"])


def trade(at, price, market=Market.KRX):
    return Trade(code="005930", market=market, price=Decimal(str(price)), change=Decimal("100"),
                 change_percent=Decimal("0.4"), volume=5, acc_volume=1000, at=at)


def test_daily_series_updates_last_bar_from_krx_trades_only():
    s = store(Clock(kst(10, 1)))
    s.seed("005930", Market.KRX, "1D", [bar(kst(9, 0, 27), 223000), bar(kst(9, 0), 224500)])
    assert s.apply_trade(trade(kst(10, 1), 225000))
    _, obs = s.observation("005930", "1D", CID, True, None)
    last = obs.value.candles[-1]
    assert last.period_key == "20261028" and last.close == Decimal("225000") and last.high >= Decimal("225000")
    assert last.complete is False and obs.value.candles[0].complete is True
    assert obs.value.adjustment == "adjusted" and obs.state == DataState.LIVE
    s.apply_trade(trade(kst(16, 0), 226000, Market.NXT))
    _, obs = s.observation("005930", "1D", CID, True, None)
    assert obs.value.candles[-1].close == Decimal("225000")


def test_four_hour_series_follows_active_market_with_label():
    clock = Clock(kst(16, 10))
    s = store(clock)
    s.seed("005930", Market.KRX, "4H", [bar(kst(9, 0), 100), bar(kst(13, 0), 101)])
    s.seed("005930", Market.NXT, "4H", [bar(kst(15, 30), 102)])
    market, obs = s.observation("005930", "4H", CID, True, None)
    assert market == Market.NXT and obs.state == DataState.LIVE
    assert obs.value.interval == "4H" and obs.value.candles[-1].period_key == "202610281530"
    clock.now = kst(10, 0)
    assert s.observation("005930", "4H", CID, True, None)[0] == Market.KRX


def test_kospi_during_nxt_hours_is_market_closed_daily():
    s = store(Clock(kst(17, 0)))
    s.seed("001", Market.KRX, "1D", [bar(kst(9, 0), "2640.5")])
    market, obs = s.observation("001", "1D", CID, True, None)
    assert market == Market.KRX and obs.state == DataState.DELAYED and obs.reason == "MARKET_CLOSED"


def test_unseeded_series_is_loading_in_session_and_unavailable_outside():
    clock = Clock(kst(10, 0))
    s = store(clock)
    assert s.observation("005930", "1D", CID, True, None)[1].state == DataState.LOADING
    clock.now = kst(21, 0)
    obs = s.observation("005930", "1D", CID, True, None)[1]
    assert obs.state == DataState.UNAVAILABLE and obs.reason == "MARKET_CLOSED"


def test_disconnect_turns_chart_observations_disconnected_with_reason():
    s = store(Clock(kst(10, 0)))
    s.seed("005930", Market.KRX, "1D", [bar(kst(9, 0), 224500)])
    obs = s.observation("005930", "1D", CID, False, "KIWOOM_WS_CLOSED")[1]
    assert obs.state == DataState.DISCONNECTED and obs.reason == "KIWOOM_WS_CLOSED"
    assert obs.value is not None


def test_index_tick_updates_index_quote_and_daily_close():
    s = store(Clock(kst(10, 0)))
    s.seed("001", Market.KRX, "1D", [bar(kst(9, 0), "2640.5")])
    assert s.apply_index("001", Decimal("2645.12"), Decimal("4.62"), Decimal("0.17"), kst(10, 0))
    q = s.index_quote("001", CID, True, None)
    assert q.state == DataState.LIVE and q.value.last_price == Decimal("2645.12")
    daily = s.observation("001", "1D", CID, True, None)[1]
    assert daily.value.candles[-1].close == Decimal("2645.12")


def test_adjustment_label_by_instrument():
    s = store(Clock(kst(10, 0)))
    s.seed("001", Market.KRX, "1D", [bar(kst(9, 0), "2640.5")])
    s.seed("001", Market.KRX, "4H", [bar(kst(9, 0), "2640.5")])
    s.seed("005930", Market.KRX, "1D", [bar(kst(9, 0), 224500)])
    assert s.observation("001", "1D", CID, True, None)[1].value.adjustment == "unadjusted"
    assert s.observation("001", "4H", CID, True, None)[1].value.adjustment == "unadjusted"
    assert s.observation("005930", "1D", CID, True, None)[1].value.adjustment == "adjusted"


def test_out_of_order_stock_trade_keeps_close_but_widens_range():
    s = store(Clock(kst(10, 5)))
    s.seed("005930", Market.KRX, "1D", [bar(kst(9, 0), 224500)])
    s.seed("005930", Market.KRX, "4H", [bar(kst(9, 0), 224500)])
    s.apply_trade(trade(kst(10, 3), 225000))
    s.apply_trade(trade(kst(10, 1), 223000))
    for interval in ("1D", "4H"):
        c = s.observation("005930", interval, CID, True, None)[1].value.candles[-1]
        assert c.close == Decimal("225000") and c.low == Decimal("223000") and c.high == Decimal("225000")


def test_out_of_order_index_tick_keeps_close_but_widens_range():
    s = store(Clock(kst(10, 5)))
    s.seed("001", Market.KRX, "1D", [bar(kst(9, 0), "2640.5")])
    s.seed("001", Market.KRX, "4H", [bar(kst(9, 0), "2640.5")])
    s.apply_index("001", Decimal("2650"), None, None, kst(10, 3))
    s.apply_index("001", Decimal("2630"), None, None, kst(10, 1))
    for interval in ("1D", "4H"):
        c = s.observation("001", interval, CID, True, None)[1].value.candles[-1]
        assert c.close == Decimal("2650") and c.low == Decimal("2630") and c.high == Decimal("2650")
    assert s.index_quote("001", CID, True, None).value.last_price == Decimal("2650")
