import asyncio
import json
from datetime import date, datetime
from decimal import Decimal

import pytest
from websockets.exceptions import ConnectionClosedError

from spatial_gateway.kiwoom.realtime import Book, KiwoomRealtime, Trade, parse_real_frame
from spatial_gateway.kiwoom.values import KST
from spatial_gateway.market.session import Market
from spatial_gateway.settings import Settings

TODAY = date(2026, 10, 27)
TOKEN = "SECRET-TOKEN-xyz"


def trade_frame(values, item="005930_NX", type_="0B"):
    return {"trnm": "REAL", "data": [{"type": type_, "item": item, "values": values}]}


def test_trade_frame_parses_absolute_price_and_signed_change():
    frame = trade_frame({"10": "-78800", "11": "-600", "12": "-0.76", "13": "14947571", "15": "+120", "20": "153512"})
    [t] = parse_real_frame(frame, TODAY)
    assert isinstance(t, Trade)
    assert (t.code, t.market, t.price, t.change, t.volume) == ("005930", Market.NXT, Decimal("78800"), Decimal("-600"), 120)
    assert t.change_percent == Decimal("-0.76")
    assert t.acc_volume == 14947571
    assert t.at == datetime(2026, 10, 27, 15, 35, 12, tzinfo=KST)


def test_krx_item_without_suffix_and_list_values():
    frame = trade_frame([{"10": "+78800", "20": "090000"}], item="005930")
    [t] = parse_real_frame(frame, TODAY)
    assert t.market == Market.KRX and t.change is None and t.volume is None


def test_book_frame_maps_ask_and_bid_blocks():
    values = {"21": "153512"}
    for i in range(10):
        values[str(41 + i)] = f"+{1000 + i}"
        values[str(51 + i)] = f"-{900 - i}"
        values[str(61 + i)] = str(10 + i)
        values[str(71 + i)] = str(20 + i)
    values["43"] = "bad"
    [b] = parse_real_frame(trade_frame(values, type_="0D"), TODAY)
    assert isinstance(b, Book) and b.market == Market.NXT
    assert len(b.asks) == 10 and len(b.bids) == 10
    assert b.asks[0] == (Decimal("1000"), 10)
    assert b.bids[0] == (Decimal("900"), 20)
    assert b.asks[2] == (None, 12)
    assert b.bids[9] == (Decimal("891"), 29)
    assert b.at == datetime(2026, 10, 27, 15, 35, 12, tzinfo=KST)


def test_book_without_time_dropped_and_missing_level_is_none():
    assert parse_real_frame(trade_frame({"41": "1000"}, type_="0D"), TODAY) == []
    [b] = parse_real_frame(trade_frame({"21": "090000"}, type_="0D"), TODAY)
    assert b.asks[0] == (None, None)


@pytest.mark.parametrize("values", [{}, {"10": ""}, {"10": "-", "20": "153512"}, {"10": "78800", "20": "bad"}, {"10": "78800"}])
def test_malformed_trade_frames_are_dropped(values):
    assert parse_real_frame(trade_frame(values), TODAY) == []


def test_unknown_type_and_non_real_frames_are_ignored():
    assert parse_real_frame(trade_frame({"10": "1", "20": "090000"}, type_="0Z"), TODAY) == []
    assert parse_real_frame({"trnm": "PING"}, TODAY) == []
    assert parse_real_frame({"trnm": "REAL", "data": "x"}, TODAY) == []
    assert parse_real_frame({"trnm": "REAL", "data": [{"type": "0B", "item": "005930", "values": {"10": 5, "20": None}}]}, TODAY) == []


def test_register_message_subscribes_krx_and_nxt_items():
    assert KiwoomRealtime.register_message(["005930"]) == {
        "trnm": "REG", "grp_no": "1", "refresh": "1",
        "data": [{"item": ["005930", "005930_NX"], "type": ["0B", "0D"]}],
    }


class FakeWS:
    def __init__(self, script):
        self.script = list(script)
        self.sent = []

    async def send(self, text):
        self.sent.append(json.loads(text))

    async def recv(self):
        if not self.script:
            raise ConnectionClosedError(None, None)
        item = self.script.pop(0)
        if isinstance(item, Exception):
            raise item
        return item if isinstance(item, str) else json.dumps(item)

    async def close(self):
        pass


class FakeConnect:
    def __init__(self, scripts):
        self.scripts = list(scripts)
        self.sockets = []
        self.kwargs = []

    def __call__(self, url, **kwargs):
        self.kwargs.append((url, kwargs))
        ws = FakeWS(self.scripts.pop(0))
        self.sockets.append(ws)

        class CM:
            async def __aenter__(self_):
                return ws

            async def __aexit__(self_, *exc):
                return False

        return CM()


class FakeTokens:
    def __init__(self):
        self.invalidated = 0

    async def token(self):
        return TOKEN

    async def invalidate(self):
        self.invalidated += 1
        return True


def make(scripts, stop_after=None):
    settings = Settings("a", "s", "p" * 32, ("005930",), ("127.0.0.1",))
    connect = FakeConnect(scripts)
    tokens = FakeTokens()
    events, states, sleeps = [], [], []

    async def sleep(seconds):
        sleeps.append(seconds)
        if stop_after is not None and len(sleeps) >= stop_after:
            raise asyncio.CancelledError

    rt = KiwoomRealtime(
        settings, tokens, events.append, lambda ok, reason: states.append((ok, reason)),
        connect=connect, sleep=sleep, clock=lambda: datetime(2026, 10, 27, 15, 40, tzinfo=KST),
    )
    return rt, connect, tokens, events, states, sleeps


async def run_until_cancel(rt):
    with pytest.raises(asyncio.CancelledError):
        await rt.run()


REAL = trade_frame({"10": "+78800", "20": "153512"})


async def test_session_logs_in_echoes_ping_and_reports_disconnect():
    ping = {"trnm": "PING"}
    script = [
        REAL,  # arrives before the LOGIN ack: buffered
        {"trnm": "LOGIN", "return_code": 0},
        {"trnm": "SYSTEM", "message": "x"},
        {"trnm": "REG", "return_code": 0},
        ping,
        REAL,
    ]
    rt, connect, tokens, events, states, sleeps = make([script], stop_after=1)
    await run_until_cancel(rt)
    ws = connect.sockets[0]
    assert ws.sent[0] == {"trnm": "LOGIN", "token": TOKEN}
    assert ws.sent[1] == KiwoomRealtime.register_message(["005930"])
    assert ping in ws.sent[2:]
    assert states == [(True, None), (False, "KIWOOM_WS_CLOSED")]
    assert len(events) == 2 and all(isinstance(e, Trade) for e in events)
    assert connect.kwargs[0][1]["ping_interval"] is None and connect.kwargs[0][1]["ping_timeout"] is None
    assert sleeps == [1]


async def test_login_8005_invalidates_token_and_reconnects_with_backoff():
    bad = [{"trnm": "LOGIN", "return_code": 8005, "return_msg": "bad"}]
    rt, connect, tokens, events, states, sleeps = make([bad, bad, bad], stop_after=3)
    await run_until_cancel(rt)
    assert tokens.invalidated == 3
    assert states == [(False, "KIWOOM_AUTH")] * 3
    assert sleeps == [1, 2, 4]
    assert TOKEN not in repr(states)


async def test_backoff_caps_at_15_and_resets_after_successful_registration():
    drop = []
    ok = [{"trnm": "LOGIN", "return_code": 0}, {"trnm": "REG", "return_code": 0}]
    scripts = [drop] * 6 + [ok] + [drop]
    rt, connect, tokens, events, states, sleeps = make(scripts, stop_after=8)
    await run_until_cancel(rt)
    assert sleeps == [1, 2, 4, 8, 15, 15, 1, 2]


async def test_token_failure_backs_off_without_connecting():
    from spatial_gateway.kiwoom.auth import KiwoomAuthError

    rt, connect, tokens, events, states, sleeps = make([], stop_after=2)

    async def boom():
        raise KiwoomAuthError("token issuance rejected (http 200, return_code 8001)")

    tokens.token = boom
    await run_until_cancel(rt)
    assert connect.sockets == []
    assert states == [(False, "KIWOOM_AUTH")] * 2
    assert sleeps == [1, 2]
