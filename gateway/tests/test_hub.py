import asyncio
from datetime import datetime, timedelta
from decimal import Decimal

import pytest

from spatial_gateway.capabilities import HubMarket
from spatial_gateway.kiwoom.realtime import AccountEvent, Book, IndexTick, Trade
from spatial_gateway.kiwoom.rest import MinuteBar
from spatial_gateway.kiwoom.values import KST
from spatial_gateway.market.hub import MarketHub
from spatial_gateway.market.session import Market
from spatial_gateway.models import DataState

def live_hub(clock, **kw):
    hub = MarketHub(clock, **kw)
    hub.set_connection(True, None)
    return hub


CID = "11111111-1111-4111-8111-111111111111"


def kst(h, m, s=0, day=27):
    return datetime(2026, 10, day, h, m, s, tzinfo=KST)


class Clock:
    def __init__(self, now):
        self.now = now

    def __call__(self):
        return self.now


def trade(at, price, market=Market.KRX, volume=10, acc=1000, code="005930", change="-600", pct="-0.76"):
    return Trade(code=code, market=market, price=Decimal(str(price)),
                 change=Decimal(change), change_percent=Decimal(pct), volume=volume, acc_volume=acc, at=at)


def bar(start, price="100", volume=5):
    p = Decimal(price)
    return MinuteBar(start=start, open=p, high=p, low=p, close=p, volume=volume)


def test_trade_updates_live_quote_with_source_time():
    hub = live_hub(Clock(kst(10, 1, 6)))
    t = trade(kst(10, 1, 5), "78800")
    hub.apply(t)
    snap = hub.observations("005930", CID)
    assert snap.market == Market.KRX and snap.quote.state == DataState.LIVE
    assert snap.quote.value.last_price == Decimal("78800") and snap.quote.source_at == t.at
    assert snap.quote.value.change == Decimal("-600")
    assert snap.quote.value.change_percent == Decimal("-0.76")
    assert snap.quote.value.volume == 1000
    assert snap.quote.received_at == kst(10, 1, 6)


def test_trades_extend_current_minute_bar_and_open_next():
    hub = live_hub(Clock(kst(10, 1, 10)))
    hub.seed_candles("005930", Market.KRX, [bar(kst(10, 0))])
    hub.apply(trade(kst(10, 0, 30), 105, volume=7))
    hub.apply(trade(kst(10, 1, 2), 99, volume=3))
    snap = hub.observations("005930", CID)
    first, last = snap.candles.value.candles
    assert (first.period_key, first.high, first.close, first.volume, first.complete) == ("202610271000", Decimal("105"), Decimal("105"), 7, True)
    assert last.period_key == "202610271001"
    assert (last.open, last.high, last.low, last.close, last.complete) == (Decimal("99"),) * 4 + (False,)
    assert snap.candles.state == DataState.LIVE and snap.candles.source_at == kst(10, 1, 2)
    assert snap.candles.value.interval == "1m" and snap.candles.value.adjustment == "unadjusted"


def test_krx_and_nxt_bars_never_mix():
    clock = Clock(kst(15, 30, 5))
    hub = live_hub(clock)
    hub.apply(trade(kst(15, 29, 59), 100, Market.KRX))
    hub.apply(trade(kst(15, 30, 1), 200, Market.NXT))
    snap = hub.observations("005930", CID)
    assert snap.market == Market.NXT
    closes = [c.close for c in snap.candles.value.candles]
    assert closes == [Decimal("200")]
    assert snap.quote.value.last_price == Decimal("200")


def test_disconnect_marks_values_disconnected_but_keeps_them():
    hub = live_hub(Clock(kst(10, 1, 6)))
    hub.apply(trade(kst(10, 1, 5), 78800))
    hub.set_connection(False, "KIWOOM_WS_CLOSED")
    snap = hub.observations("005930", CID)
    for obs in (snap.quote, snap.candles):
        assert obs.state == DataState.DISCONNECTED and obs.reason == "KIWOOM_WS_CLOSED"
    assert snap.quote.value.last_price == Decimal("78800")
    hub.set_connection(True, None)
    assert hub.observations("005930", CID).quote.state == DataState.LIVE


def test_no_active_market_without_data_reports_unavailable_market_closed():
    hub = live_hub(Clock(kst(10, 0, day=31)), watch_codes=["005930"])
    snap = hub.observations("005930", CID)
    assert snap.market is None
    for obs in (snap.quote, snap.candles, snap.orderbook):
        assert obs.state == DataState.UNAVAILABLE and obs.reason == "MARKET_CLOSED" and obs.value is None


def test_no_active_market_without_data_while_disconnected_reports_disconnected():
    hub = MarketHub(Clock(kst(21, 0)), watch_codes=["005930"])
    hub.set_connection(False, "KIWOOM_AUTH")
    snap = hub.observations("005930", CID)
    for obs in (snap.quote, snap.candles, snap.orderbook):
        assert obs.state == DataState.DISCONNECTED and obs.reason == "KIWOOM_AUTH" and obs.value is None


def test_after_close_serves_most_recent_market_as_delayed_market_closed():
    clock = Clock(kst(15, 29, 50))
    hub = live_hub(clock)
    hub.apply(trade(kst(15, 29, 49), 100, Market.KRX))
    clock.now = kst(19, 59, 30)
    hub.apply(trade(kst(19, 59, 29), 200, Market.NXT))
    asks = tuple((Decimal(201 + i), 1) for i in range(10))
    bids = tuple((Decimal(199 - i), 1) for i in range(10))
    hub.apply(Book(code="005930", market=Market.NXT, asks=asks, bids=bids, at=kst(19, 59, 29)))
    clock.now = kst(20, 1)
    snap = hub.observations("005930", CID)
    assert snap.market == Market.NXT
    for obs in (snap.quote, snap.candles, snap.orderbook):
        assert obs.state == DataState.DELAYED and obs.reason == "MARKET_CLOSED" and obs.value is not None
    assert snap.quote.value.last_price == Decimal("200") and snap.quote.source_at == kst(19, 59, 29)
    assert [c.close for c in snap.candles.value.candles] == [Decimal("200")]
    assert snap.orderbook.value.asks[0].price == Decimal("201")
    clock.now = kst(7, 0, day=28)  # before the next NXT pre-market: still the last NXT values
    assert hub.observations("005930", CID).market == Market.NXT


def test_after_close_capability_without_values_stays_unavailable_and_disconnect_wins():
    clock = Clock(kst(15, 29, 50))
    hub = live_hub(clock)
    hub.apply(trade(kst(15, 29, 49), 100, Market.KRX))
    clock.now = kst(16, 0, day=31)  # Saturday
    snap = hub.observations("005930", CID)
    assert snap.market == Market.KRX and snap.quote.state == DataState.DELAYED
    assert snap.orderbook.state == DataState.UNAVAILABLE and snap.orderbook.reason == "MARKET_CLOSED"
    hub.set_connection(False, "KIWOOM_WS_CLOSED")
    quote = hub.observations("005930", CID).quote
    assert quote.state == DataState.DISCONNECTED and quote.reason == "KIWOOM_WS_CLOSED"
    assert quote.value.last_price == Decimal("100")


def test_untracked_code_is_unavailable_not_zero():
    hub = live_hub(Clock(kst(10, 0)))
    snap = hub.observations("000000", CID)
    for obs in (snap.quote, snap.candles, snap.orderbook):
        assert obs.state == DataState.UNAVAILABLE and obs.reason == "NOT_SUBSCRIBED" and obs.value is None


def test_tracked_without_data_for_active_market_is_loading():
    hub = live_hub(Clock(kst(10, 0)))
    hub.apply(trade(kst(10, 0), 100))
    snap = hub.observations("005930", CID)
    assert snap.orderbook.state == DataState.LOADING and snap.orderbook.value is None


def test_book_maps_all_ten_levels_with_none_allowed():
    hub = live_hub(Clock(kst(10, 0, 5)))
    asks = tuple((Decimal(100 + i), 10 + i) for i in range(9)) + ((None, None),)
    bids = tuple((Decimal(99 - i), 5) for i in range(10))
    hub.apply(Book(code="005930", market=Market.KRX, asks=asks, bids=bids, at=kst(10, 0, 4)))
    ob = hub.observations("005930", CID).orderbook
    assert ob.state == DataState.LIVE and ob.source_at == kst(10, 0, 4)
    assert [l.rank for l in ob.value.asks] == list(range(1, 11))
    assert ob.value.asks[9].price is None and ob.value.asks[0].price == Decimal("100")


def test_seeded_only_series_live_when_recent_else_delayed():
    hub = live_hub(Clock(kst(10, 2, 30)))
    hub.seed_candles("005930", Market.KRX, [bar(kst(10, 0))])
    s = hub.observations("005930", CID).candles
    assert s.state == DataState.LIVE and s.source_at == kst(10, 0)
    hub2 = live_hub(Clock(kst(10, 3, 0)))
    hub2.seed_candles("005930", Market.KRX, [bar(kst(10, 0))])
    s = hub2.observations("005930", CID).candles
    assert s.state == DataState.DELAYED and s.source_at == kst(10, 0) and s.received_at == kst(10, 3, 0)
    assert s.value.candles[-1].complete is False


def test_invalid_events_are_dropped_without_corrupting_state(caplog):
    hub = live_hub(Clock(kst(10, 1, 6)))
    good = trade(kst(10, 1, 5), 100)
    hub.apply(good)
    q = hub.subscribe()
    bad = trade(kst(10, 1, 6), Decimal("1E+999999"))
    hub.apply(bad)
    hub.apply(Trade(code="005930", market=Market.KRX, price=Decimal("0"), change=None,
                    change_percent=None, volume=1, acc_volume=1, at=kst(10, 1, 6)))
    hub.apply(trade(datetime(2026, 10, 27, 10, 1, 6), 100))  # naive
    hub.apply(Book(code="005930", market=Market.KRX, asks=((Decimal("-1"), 1),) * 10,
                   bids=((None, None),) * 10, at=kst(10, 1, 6)))
    snap = hub.observations("005930", CID)
    assert snap.quote.value.last_price == Decimal("100") and snap.quote.source_at == good.at
    assert snap.orderbook.state == DataState.LOADING
    assert q.empty()
    assert "1E+999999" not in caplog.text


def test_max_bars_keeps_newest():
    hub = live_hub(Clock(kst(10, 5)), max_bars=3)
    hub.seed_candles("005930", Market.KRX, [bar(kst(10, i)) for i in range(5)])
    keys = [c.period_key for c in hub.observations("005930", CID).candles.value.candles]
    assert keys == ["202610271002", "202610271003", "202610271004"]


async def test_subscribers_receive_changed_codes():
    hub = live_hub(Clock(kst(10, 1, 6)))
    q = hub.subscribe()
    hub.apply(trade(kst(10, 1, 5), 100))
    assert await asyncio.wait_for(q.get(), 1) == "005930"
    hub.unsubscribe(q)
    hub.apply(trade(kst(10, 1, 5), 100))
    assert q.empty()


def test_full_subscriber_queue_drops_oldest():
    hub = live_hub(Clock(kst(10, 1, 6)))
    q = hub.subscribe()
    for i in range(70):
        hub.apply(trade(kst(10, 1, 5), 100, code=f"{i:06d}"))
    assert q.qsize() == 64
    assert q.get_nowait() == "000006" and q._queue[-1] == "000069"


async def test_hub_market_serves_observations():
    hub = live_hub(Clock(kst(10, 1, 6)))
    hub.apply(trade(kst(10, 1, 5), 100))
    market = HubMarket(hub)
    assert (await market.quote("005930", CID)).state == DataState.LIVE
    assert (await market.candles("005930", CID)).state == DataState.LIVE
    assert (await market.orderbook("000000", CID)).reason == "NOT_SUBSCRIBED"


def test_new_hub_is_connecting_not_live():
    hub = MarketHub(Clock(kst(10, 1, 6)))
    hub.apply(trade(kst(10, 1, 5), 100))
    snap = hub.observations("005930", CID)
    assert snap.quote.state == DataState.DISCONNECTED and snap.quote.reason == "KIWOOM_CONNECTING"
    hub.set_connection(True, None)
    assert hub.observations("005930", CID).quote.state == DataState.LIVE


def test_watched_code_without_data_is_loading_unwatched_not_subscribed():
    hub = live_hub(Clock(kst(10, 0)), watch_codes=["005930"])
    assert hub.observations("005930", CID).quote.state == DataState.LOADING
    assert hub.observations("000660", CID).quote.reason == "NOT_SUBSCRIBED"
    hub_closed = live_hub(Clock(kst(10, 0, day=31)), watch_codes=["005930"])
    assert hub_closed.observations("005930", CID).quote.reason == "MARKET_CLOSED"


def test_out_of_order_trade_does_not_regress_quote():
    hub = live_hub(Clock(kst(10, 1, 30)))
    hub.apply(trade(kst(10, 1, 20), 110))
    hub.apply(trade(kst(10, 1, 10), 90))
    snap = hub.observations("005930", CID)
    assert snap.quote.value.last_price == Decimal("110") and snap.quote.source_at == kst(10, 1, 20)
    bar = snap.candles.value.candles[-1]
    assert (bar.close, bar.low, bar.high, bar.open) == (Decimal("110"), Decimal("90"), Decimal("110"), Decimal("110"))
    assert snap.candles.source_at == kst(10, 1, 20)


def test_stale_trade_for_closed_minute_is_dropped():
    hub = live_hub(Clock(kst(10, 2, 30)))
    hub.apply(trade(kst(10, 2, 20), 110))
    q = hub.subscribe()
    hub.apply(trade(kst(10, 1, 10), 90))
    snap = hub.observations("005930", CID)
    assert [c.period_key for c in snap.candles.value.candles] == ["202610271002"]
    assert snap.quote.value.last_price == Decimal("110") and q.empty()


def test_stale_book_is_ignored():
    hub = live_hub(Clock(kst(10, 0, 5)))
    def book(at, price):
        return Book(code="005930", market=Market.KRX, asks=((Decimal(price), 1),) * 10,
                    bids=((Decimal("50"), 1),) * 10, at=at)
    hub.apply(book(kst(10, 0, 4), "100"))
    hub.apply(book(kst(10, 0, 2), "999"))
    ob = hub.observations("005930", CID).orderbook
    assert ob.value.asks[0].price == Decimal("100") and ob.source_at == kst(10, 0, 4)


def _rest(start, o, h, l, c, v):
    return MinuteBar(start=start, open=Decimal(o), high=Decimal(h), low=Decimal(l), close=Decimal(c), volume=v)


@pytest.mark.parametrize("seed_first", [True, False])
def test_rest_seed_and_live_partial_bar_merge(seed_first):
    hub = live_hub(Clock(kst(10, 0, 40)))
    seed = lambda: hub.seed_candles("005930", Market.KRX, [_rest(kst(10, 0), "100", "108", "98", "104", 50)])
    live = lambda: (hub.apply(trade(kst(10, 0, 20), 106, volume=7)), hub.apply(trade(kst(10, 0, 30), 105, volume=8)))
    if seed_first:
        seed(); live()
    else:
        live(); seed()
    (c,) = hub.observations("005930", CID).candles.value.candles
    assert (c.open, c.high, c.low, c.close, c.volume) == (Decimal("100"), Decimal("108"), Decimal("98"), Decimal("105"), 50)


def test_hub_routes_index_ticks_and_account_events():
    calls = []
    hub = live_hub(Clock(kst(10, 0)), index_codes=["001"], on_account=lambda: calls.append(1))
    hub.apply(IndexTick("001", Decimal("2645.12"), Decimal("-12.34"), Decimal("-0.47"), kst(10, 0)))
    assert hub.charts.index_quote("001", CID, True, None).state == DataState.LIVE
    hub.apply(AccountEvent(kst(10, 0)))
    assert calls == [1]
    boom = live_hub(Clock(kst(10, 0)), on_account=lambda: 1 / 0)
    boom.apply(AccountEvent(kst(10, 0)))  # swallowed
