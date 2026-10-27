import asyncio
from datetime import datetime, timedelta
from decimal import Decimal

import pytest

from spatial_gateway.capabilities import HubMarket
from spatial_gateway.kiwoom.realtime import Book, Trade
from spatial_gateway.kiwoom.rest import MinuteBar
from spatial_gateway.kiwoom.values import KST
from spatial_gateway.market.hub import MarketHub
from spatial_gateway.market.session import Market
from spatial_gateway.models import DataState

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
    hub = MarketHub(Clock(kst(10, 1, 6)))
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
    hub = MarketHub(Clock(kst(10, 1, 10)))
    hub.seed_candles("005930", Market.KRX, [bar(kst(10, 0))])
    hub.apply(trade(kst(10, 0, 30), 105, volume=7))
    hub.apply(trade(kst(10, 1, 2), 99, volume=3))
    snap = hub.observations("005930", CID)
    first, last = snap.candles.value.candles
    assert (first.period_key, first.high, first.close, first.volume, first.complete) == ("202610271000", Decimal("105"), Decimal("105"), 12, True)
    assert last.period_key == "202610271001"
    assert (last.open, last.high, last.low, last.close, last.complete) == (Decimal("99"),) * 4 + (False,)
    assert snap.candles.state == DataState.LIVE and snap.candles.source_at == kst(10, 1, 2)
    assert snap.candles.value.interval == "1m" and snap.candles.value.adjustment == "unadjusted"


def test_krx_and_nxt_bars_never_mix():
    clock = Clock(kst(15, 30, 5))
    hub = MarketHub(clock)
    hub.apply(trade(kst(15, 29, 59), 100, Market.KRX))
    hub.apply(trade(kst(15, 30, 1), 200, Market.NXT))
    snap = hub.observations("005930", CID)
    assert snap.market == Market.NXT
    closes = [c.close for c in snap.candles.value.candles]
    assert closes == [Decimal("200")]
    assert snap.quote.value.last_price == Decimal("200")


def test_disconnect_marks_values_disconnected_but_keeps_them():
    hub = MarketHub(Clock(kst(10, 1, 6)))
    hub.apply(trade(kst(10, 1, 5), 78800))
    hub.set_connection(False, "KIWOOM_WS_CLOSED")
    snap = hub.observations("005930", CID)
    for obs in (snap.quote, snap.candles):
        assert obs.state == DataState.DISCONNECTED and obs.reason == "KIWOOM_WS_CLOSED"
    assert snap.quote.value.last_price == Decimal("78800")
    hub.set_connection(True, None)
    assert hub.observations("005930", CID).quote.state == DataState.LIVE


def test_no_active_market_reports_unavailable_market_closed():
    hub = MarketHub(Clock(kst(10, 0, day=31)))
    hub.apply(trade(kst(10, 0, day=31), 100))
    snap = hub.observations("005930", CID)
    assert snap.market is None
    for obs in (snap.quote, snap.candles, snap.orderbook):
        assert obs.state == DataState.UNAVAILABLE and obs.reason == "MARKET_CLOSED" and obs.value is None


def test_untracked_code_is_unavailable_not_zero():
    hub = MarketHub(Clock(kst(10, 0)))
    snap = hub.observations("000000", CID)
    for obs in (snap.quote, snap.candles, snap.orderbook):
        assert obs.state == DataState.UNAVAILABLE and obs.reason == "NOT_SUBSCRIBED" and obs.value is None


def test_tracked_without_data_for_active_market_is_loading():
    hub = MarketHub(Clock(kst(10, 0)))
    hub.apply(trade(kst(10, 0), 100))
    snap = hub.observations("005930", CID)
    assert snap.orderbook.state == DataState.LOADING and snap.orderbook.value is None


def test_book_maps_all_ten_levels_with_none_allowed():
    hub = MarketHub(Clock(kst(10, 0, 5)))
    asks = tuple((Decimal(100 + i), 10 + i) for i in range(9)) + ((None, None),)
    bids = tuple((Decimal(99 - i), 5) for i in range(10))
    hub.apply(Book(code="005930", market=Market.KRX, asks=asks, bids=bids, at=kst(10, 0, 4)))
    ob = hub.observations("005930", CID).orderbook
    assert ob.state == DataState.LIVE and ob.source_at == kst(10, 0, 4)
    assert [l.rank for l in ob.value.asks] == list(range(1, 11))
    assert ob.value.asks[9].price is None and ob.value.asks[0].price == Decimal("100")


def test_seeded_only_series_live_when_recent_else_delayed():
    hub = MarketHub(Clock(kst(10, 2, 30)))
    hub.seed_candles("005930", Market.KRX, [bar(kst(10, 0))])
    s = hub.observations("005930", CID).candles
    assert s.state == DataState.LIVE and s.source_at == kst(10, 0)
    hub2 = MarketHub(Clock(kst(10, 3, 0)))
    hub2.seed_candles("005930", Market.KRX, [bar(kst(10, 0))])
    s = hub2.observations("005930", CID).candles
    assert s.state == DataState.DELAYED and s.source_at == kst(10, 0) and s.received_at == kst(10, 3, 0)
    assert s.value.candles[-1].complete is False


def test_invalid_events_are_dropped_without_corrupting_state(caplog):
    hub = MarketHub(Clock(kst(10, 1, 6)))
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
    hub = MarketHub(Clock(kst(10, 5)), max_bars=3)
    hub.seed_candles("005930", Market.KRX, [bar(kst(10, i)) for i in range(5)])
    keys = [c.period_key for c in hub.observations("005930", CID).candles.value.candles]
    assert keys == ["202610271002", "202610271003", "202610271004"]


async def test_subscribers_receive_changed_codes():
    hub = MarketHub(Clock(kst(10, 1, 6)))
    q = hub.subscribe()
    hub.apply(trade(kst(10, 1, 5), 100))
    assert await asyncio.wait_for(q.get(), 1) == "005930"
    hub.unsubscribe(q)
    hub.apply(trade(kst(10, 1, 5), 100))
    assert q.empty()


def test_full_subscriber_queue_drops_oldest():
    hub = MarketHub(Clock(kst(10, 1, 6)))
    q = hub.subscribe()
    for i in range(70):
        hub.apply(trade(kst(10, 1, 5), 100, code=f"{i:06d}"))
    assert q.qsize() == 64
    assert q.get_nowait() == "000006" and q._queue[-1] == "000069"


async def test_hub_market_serves_observations():
    hub = MarketHub(Clock(kst(10, 1, 6)))
    hub.apply(trade(kst(10, 1, 5), 100))
    market = HubMarket(hub)
    assert (await market.quote("005930", CID)).state == DataState.LIVE
    assert (await market.candles("005930", CID)).state == DataState.LIVE
    assert (await market.orderbook("000000", CID)).reason == "NOT_SUBSCRIBED"
