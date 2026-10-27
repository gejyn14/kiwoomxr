import asyncio
import json
import logging
from datetime import date, datetime, timedelta
from decimal import Decimal

import pytest
from websockets.exceptions import ConnectionClosedError

from spatial_gateway.kiwoom import realtime as realtime_module
from spatial_gateway.kiwoom.realtime import (
    AccountEvent, CLOSED_IDLE_TIMEOUT, IDLE_TIMEOUT, Book, IndexTick, KiwoomRealtime, Trade, parse_real_frame,
)
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
        "data": [{"item": ["005930", "005930_NX"], "type": ["0B", "0D"]},
                 {"item": [""], "type": ["04"]}],
    }


def test_register_message_omits_an_empty_index_group():
    data = KiwoomRealtime.register_message(["005930"], index_codes=())["data"]
    assert all("0J" not in group["type"] for group in data)


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
        if callable(item):  # side effect (e.g. advancing the clock), then the frame it returns
            item = item()
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


def make(scripts, stop_after=None, clock=lambda: datetime(2026, 10, 27, 15, 40, tzinfo=KST), **kw):
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
        connect=connect, sleep=sleep, clock=clock, **kw,
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


async def test_backoff_caps_at_15_and_a_brief_registration_does_not_reset_it():
    drop = []
    ok = [{"trnm": "LOGIN", "return_code": 0}, {"trnm": "REG", "return_code": 0}]
    scripts = [drop] * 6 + [ok] + [drop]
    rt, connect, tokens, events, states, sleeps = make(scripts, stop_after=8)
    await run_until_cancel(rt)
    assert sleeps == [1, 2, 4, 8, 15, 15, 15, 15]


class SteppedClock:
    def __init__(self):
        self.now = datetime(2026, 10, 27, 15, 40, tzinfo=KST)

    def __call__(self):
        return self.now

    def after(self, seconds, frame):
        def step():
            self.now += timedelta(seconds=seconds)
            return frame
        return step


async def test_backoff_resets_only_after_30_seconds_registered():
    clock = SteppedClock()
    ok = [{"trnm": "LOGIN", "return_code": 0}, {"trnm": "REG", "return_code": 0}]
    short = ok + [clock.after(5, {"trnm": "PING"})]
    long = ok + [clock.after(30, {"trnm": "PING"})]
    rt, connect, tokens, events, states, sleeps = make([[], [], short, long, []], stop_after=5, clock=clock)
    await run_until_cancel(rt)
    assert sleeps == [1, 2, 4, 1, 2]


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


class HangWS(FakeWS):
    async def recv(self):
        if self.script:
            return await super().recv()
        await asyncio.Event().wait()


def hang_connect(connect):
    original = connect.__call__

    def patched(url, **kwargs):
        cm = original(url, **kwargs)
        connect.sockets[-1].__class__ = HangWS
        return cm

    return patched


async def test_silent_server_times_out_at_login_reg_and_idle():
    ok_login = {"trnm": "LOGIN", "return_code": 0}
    ok_reg = {"trnm": "REG", "return_code": 0}
    rt, connect, tokens, events, states, sleeps = make(
        [[], [ok_login], [ok_login, ok_reg]], stop_after=3, ack_timeout=0.01, idle_timeout=0.01
    )
    rt._connect = hang_connect(connect)
    await run_until_cancel(rt)
    assert states == [(False, "KIWOOM_WS_CLOSED"), (False, "KIWOOM_WS_CLOSED"), (True, None), (False, "KIWOOM_WS_CLOSED")]
    assert sleeps == [1, 2, 4]


async def test_callback_exceptions_do_not_kill_the_loop():
    script = [{"trnm": "LOGIN", "return_code": 0}, {"trnm": "REG", "return_code": 0}, REAL, REAL]
    rt, connect, tokens, events, states, sleeps = make([script, script], stop_after=2)
    calls = []

    def bad_event(e):
        calls.append(e)
        raise RuntimeError("boom " + TOKEN)

    def bad_state(ok, reason):
        states.append((ok, reason))
        raise RuntimeError("boom")

    rt._on_event, rt._on_state = bad_event, bad_state
    await run_until_cancel(rt)
    assert len(calls) == 4
    assert states == [(True, None), (False, "KIWOOM_WS_CLOSED")] * 2


async def test_handshake_failure_backs_off_as_ws_closed():
    from websockets.exceptions import InvalidStatus

    rt, connect, tokens, events, states, sleeps = make([], stop_after=2)

    def refuse(url, **kwargs):
        raise InvalidStatus(None)

    rt._connect = refuse
    await run_until_cancel(rt)
    assert states == [(False, "KIWOOM_WS_CLOSED")] * 2
    assert sleeps == [1, 2]


async def test_any_token_error_is_auth_and_never_escapes():
    rt, connect, tokens, events, states, sleeps = make([], stop_after=2)

    async def boom():
        raise RuntimeError("transport " + TOKEN)

    tokens.token = boom
    await run_until_cancel(rt)
    assert states == [(False, "KIWOOM_AUTH")] * 2


OK_LOGIN = {"trnm": "LOGIN", "return_code": 0}
OK_REG = {"trnm": "REG", "return_code": 0}


async def test_oversized_digit_field_does_not_kill_the_session():
    huge = trade_frame({"10": "+78800", "15": "1" * 5000, "20": "153512"})
    rt, connect, tokens, events, states, sleeps = make([[OK_LOGIN, OK_REG, huge, REAL]], stop_after=1)
    await run_until_cancel(rt)
    assert states == [(True, None), (False, "KIWOOM_WS_CLOSED")]
    assert events[0].volume is None  # the oversized field is rejected, not parsed
    assert len(events) == 2 and events[1].price == Decimal("78800")


async def test_frame_that_fails_to_parse_is_dropped_and_the_next_is_emitted(monkeypatch, caplog):
    real_parse = realtime_module.parse_real_frame
    calls = []

    def flaky(frame, today, now=None):
        calls.append(frame)
        if len(calls) == 1:
            raise ValueError("unparseable " + TOKEN)
        return real_parse(frame, today, now)

    monkeypatch.setattr(realtime_module, "parse_real_frame", flaky)
    rt, connect, tokens, events, states, sleeps = make([[OK_LOGIN, OK_REG, REAL, REAL]], stop_after=1)
    with caplog.at_level(logging.WARNING):
        await run_until_cancel(rt)
    assert len(events) == 1 and states == [(True, None), (False, "KIWOOM_WS_CLOSED")]
    assert "ValueError" in caplog.text and TOKEN not in caplog.text


async def test_unexpected_session_error_is_reported_and_retried(caplog):
    rt, connect, tokens, events, states, sleeps = make([], stop_after=2)

    def explode(url, **kwargs):
        raise RuntimeError("boom " + TOKEN)

    rt._connect = explode
    with caplog.at_level(logging.WARNING):
        await run_until_cancel(rt)
    assert states == [(False, "KIWOOM_WS_CLOSED")] * 2
    assert sleeps == [1, 2]
    assert "RuntimeError" in caplog.text and TOKEN not in caplog.text


def test_idle_timeout_defaults():
    assert (IDLE_TIMEOUT, CLOSED_IDLE_TIMEOUT) == (15.0, 60.0)


@pytest.mark.parametrize("hour,open_timeout,closed_timeout", [(15, 0.01, 30.0), (21, 30.0, 0.01)])
async def test_idle_timeout_depends_on_the_market_session(hour, open_timeout, closed_timeout):
    rt, connect, tokens, events, states, sleeps = make(
        [[OK_LOGIN, OK_REG]], stop_after=1, clock=lambda: datetime(2026, 10, 27, hour, 40, tzinfo=KST),
        idle_timeout=open_timeout, closed_idle_timeout=closed_timeout,
    )
    rt._connect = hang_connect(connect)
    await asyncio.wait_for(run_until_cancel(rt), 2)  # the wrong timeout would hang for 30 s
    assert states == [(True, None), (False, "KIWOOM_WS_CLOSED")]


@pytest.mark.parametrize("trnm", ["REG", "REG_ACK"])
async def test_reg_ack_is_any_non_login_frame_with_a_return_code(trnm):
    script = [OK_LOGIN, {"trnm": "LOGIN", "return_code": 0}, {"trnm": trnm, "return_code": 0}, REAL]
    rt, connect, tokens, events, states, sleeps = make([script], stop_after=1)
    await run_until_cancel(rt)
    assert states == [(True, None), (False, "KIWOOM_WS_CLOSED")]
    assert len(events) == 1


async def test_system_frame_with_return_code_is_not_taken_as_reg_ack():
    script = [OK_LOGIN, {"trnm": "SYSTEM", "code": "R1", "return_code": 1}, OK_REG, REAL]
    rt, connect, tokens, events, states, sleeps = make([script], stop_after=1)
    await run_until_cancel(rt)
    assert states == [(True, None), (False, "KIWOOM_WS_CLOSED")]
    assert len(events) == 1


async def test_system_code_is_logged_escaped(caplog):
    script = [OK_LOGIN, OK_REG, {"trnm": "SYSTEM", "code": "R1\nforged line"}]
    rt, connect, tokens, events, states, sleeps = make([script], stop_after=1)
    with caplog.at_level(logging.WARNING):
        await run_until_cancel(rt)
    assert "code='R1\\nforged line'" in caplog.text and "\nforged line" not in caplog.text


async def test_system_frames_are_logged_by_code_only(caplog):
    script = [
        OK_LOGIN, {"trnm": "SYSTEM", "code": "R10001", "message": "handshake-text"},
        OK_REG, {"trnm": "SYSTEM", "code": "R10002", "message": "session-text"},
    ]
    rt, connect, tokens, events, states, sleeps = make([script], stop_after=1)
    with caplog.at_level(logging.WARNING):
        await run_until_cancel(rt)
    assert "R10001" in caplog.text and "R10002" in caplog.text
    assert "handshake-text" not in caplog.text and "session-text" not in caplog.text


async def test_measured_login_8005_frame_invalidates_token():
    bad = [{"trnm": "LOGIN", "return_code": 805004, "return_msg": "인증에 실패했습니다 [CODE=8005 토큰이 유효하지 않습니다]"}]
    rt, connect, tokens, events, states, sleeps = make([bad], stop_after=1)
    await run_until_cancel(rt)
    assert tokens.invalidated == 1
    assert states == [(False, "KIWOOM_AUTH")]


def test_index_frame_parses_absolute_price_and_signed_change():
    frame = trade_frame({"10": "+264512", "11": "-1234", "12": "-0.47", "20": "101500"}, item="001", type_="0J")
    [t] = parse_real_frame(frame, TODAY)
    assert isinstance(t, IndexTick)
    assert (t.code, t.price, t.change, t.change_percent) == ("001", Decimal("2645.12"), Decimal("-12.34"), Decimal("-0.47"))
    assert t.at == datetime(2026, 10, 27, 10, 15, 0, tzinfo=KST)


def test_index_frame_with_decimal_point_is_not_rescaled():
    frame = trade_frame({"10": "2645.12", "11": "-12.34", "12": "-0.47", "20": "101500"}, item="001", type_="0J")
    [t] = parse_real_frame(frame, TODAY)
    assert (t.price, t.change) == (Decimal("2645.12"), Decimal("-12.34"))


def test_balance_frame_emits_signal_without_account_number(caplog):
    frame = trade_frame({"9201": "1234567890", "9001": "005930", "930": "1"}, item="", type_="04")
    now = datetime(2026, 10, 27, 11, 0, tzinfo=KST)
    events = parse_real_frame(frame, TODAY, now)
    assert events == [AccountEvent(now)]
    assert "1234567890" not in repr(events[0]) and "1234567890" not in caplog.text


def test_register_message_includes_index_and_balance_groups():
    data = KiwoomRealtime.register_message(["005930"], index_codes=["001"])["data"]
    assert len(data) == 3
    assert data[0]["type"] == ["0B", "0D"]
    assert data[1] == {"item": ["001"], "type": ["0J"]}
    assert data[2]["type"] == ["04"]
