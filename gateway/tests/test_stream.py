import json
import logging
from datetime import datetime
from decimal import Decimal

import pytest
from fastapi.testclient import TestClient
from starlette.websockets import WebSocketDisconnect

from spatial_gateway.app import create_app
from spatial_gateway.kiwoom.realtime import Trade
from spatial_gateway.kiwoom.rest import Bar
from spatial_gateway.kiwoom.values import KST
from spatial_gateway.market.hub import MarketHub
from spatial_gateway.market.observe import observe
from spatial_gateway.market.session import Market
from spatial_gateway.models import ACCOUNT, NEWS, Balance, NewsFeed, NewsItem, Origin
from spatial_gateway.settings import Settings
from spatial_gateway.stream import StreamTiming

TOKEN = "p" * 40
APPKEY, SECRET = "appkey-AAAA-1111", "secret-BBBB-2222"
FAKE_KIWOOM_TOKEN = "kiwoom-token-ZZZZ-9999"
CID = "11111111-1111-4111-8111-111111111111"
FAST = StreamTiming(quote_interval=0.02, candles_interval=0.05, heartbeat_interval=0.15, handshake_timeout=1.0)
SETTINGS = Settings(APPKEY, SECRET, TOKEN, ("005930",), ("127.0.0.1", "testserver"))  # TestClient's Host
NOW = datetime(2026, 10, 27, 10, 0, 6, tzinfo=KST)


def make(timing=FAST):
    hub = MarketHub(lambda: NOW, watch_codes=("005930",))
    hub.set_connection(True, None)
    return hub, TestClient(create_app(SETTINGS, hub=hub, start_broker=False, timing=timing))


def sub(token=TOKEN, codes=("005930",), cid=CID):
    return json.dumps({"type": "subscribe", "token": token, "codes": list(codes), "correlation_id": cid})


def trade(price, sec=5):
    return Trade(code="005930", market=Market.KRX, price=Decimal(price), change=Decimal("-600"),
                 change_percent=Decimal("-0.76"), volume=10, acc_volume=1000,
                 at=datetime(2026, 10, 27, 10, 0, sec, tzinfo=KST))


def collect(ws, until, limit=60):
    seen = []
    for _ in range(limit):
        seen.append(ws.receive_json())
        if until(seen):
            return seen
    raise AssertionError("condition not met")


def test_subscribe_receives_snapshot_then_updates():
    hub, client = make()
    with client.websocket_connect("/v1/stream") as ws:
        ws.send_text(sub())
        first = collect(ws, lambda m: len([x for x in m if x["type"] == "observation"]) >= 4)
        obs = [m for m in first if m["type"] == "observation"]
        # The legacy 1-minute "candles" series is not streamed: no panel shows it.
        assert {m["capability"] for m in obs} == {"quote", "orderbook", "candles_1d", "candles_4h"}
        assert all(m["observation"]["correlation_id"] == CID for m in obs)
        assert all(m["market"] == "KRX" for m in obs)
        hub.apply(trade("78800"))
        more = collect(ws, lambda m: any(x["type"] == "observation" and x["capability"] == "quote"
                                         and x["observation"]["value"]["last_price"] == "78800" for x in m))
        assert more


@pytest.mark.parametrize("raw", [
    sub(token="x" * 40), sub(codes=("12345",)), sub(codes=()), sub(codes=[f"00593{i}" for i in range(10)] + ["000001"]),
    sub(cid="not-a-uuid"), "not json", json.dumps({"type": "other"}),
])
def test_bad_subscribe_closes_4401_without_data(raw):
    _, client = make()
    with client.websocket_connect("/v1/stream") as ws:
        ws.send_text(raw)
        with pytest.raises(WebSocketDisconnect) as info:
            ws.receive_json()
    assert info.value.code == 4401


def test_after_close_stream_labels_the_fallback_market():
    clock = {"now": datetime(2026, 10, 27, 19, 59, 30, tzinfo=KST)}
    hub = MarketHub(lambda: clock["now"], watch_codes=("005930",))
    hub.set_connection(True, None)
    hub.apply(Trade(code="005930", market=Market.NXT, price=Decimal("200"), change=None, change_percent=None,
                    volume=1, acc_volume=10, at=datetime(2026, 10, 27, 19, 59, 29, tzinfo=KST)))
    clock["now"] = datetime(2026, 10, 27, 20, 1, tzinfo=KST)
    client = TestClient(create_app(SETTINGS, hub=hub, start_broker=False, timing=FAST))
    with client.websocket_connect("/v1/stream") as ws:
        ws.send_text(sub())
        seen = collect(ws, lambda m: any(x["type"] == "heartbeat" for x in m)
                       and any(x["type"] == "observation" and x["capability"] == "quote" for x in m))
    quote = [m for m in seen if m["type"] == "observation" and m["capability"] == "quote"][0]
    assert quote["market"] == "NXT"
    assert quote["observation"]["state"] == "DELAYED" and quote["observation"]["reason"] == "MARKET_CLOSED"
    assert quote["observation"]["value"]["last_price"] == "200"
    beat = [m for m in seen if m["type"] == "heartbeat"][0]
    assert beat["broker_connected"] is True and beat["reason"] is None


def test_heartbeat_reports_broker_state():
    hub, client = make()
    with client.websocket_connect("/v1/stream") as ws:
        ws.send_text(sub())
        beats = collect(ws, lambda m: any(x["type"] == "heartbeat" for x in m))
        beat = [m for m in beats if m["type"] == "heartbeat"][0]
        assert beat["broker_connected"] is True and beat["reason"] is None
        assert beat["server_time"].endswith("+09:00")
        hub.set_connection(False, "KIWOOM_AUTH")
        seen = collect(ws, lambda m: any(x["type"] == "heartbeat" and not x["broker_connected"] for x in m))
        down = [m for m in seen if m["type"] == "heartbeat" and not m["broker_connected"]][0]
        assert down["reason"] == "KIWOOM_AUTH"
        later = collect(ws, lambda m: any(x["type"] == "observation" and x["capability"] == "quote"
                                          and x["observation"]["state"] == "DISCONNECTED" for x in m)) \
            if not any(x["type"] == "observation" and x["observation"]["state"] == "DISCONNECTED" for x in seen) else seen
        assert later


def test_burst_of_trades_is_coalesced_per_capability():
    slow = StreamTiming(quote_interval=0.3, candles_interval=0.6, heartbeat_interval=30, handshake_timeout=1.0)
    hub, client = make(slow)
    with client.websocket_connect("/v1/stream") as ws:
        ws.send_text(sub())
        collect(ws, lambda m: len(m) >= 3)
        for i in range(20):
            hub.apply(trade(str(78000 + i), sec=5))
        msgs = collect(ws, lambda m: any(x["observation"]["value"] and x["capability"] == "quote"
                                         and x["observation"]["value"]["last_price"] == "78019" for x in m
                                         if x["type"] == "observation"))
        quotes = [m for m in msgs if m["type"] == "observation" and m["capability"] == "quote"]
        assert len(quotes) <= 2


def test_rest_requires_pairing_token():
    _, client = make()
    r = client.get("/v1/market/005930/quote")
    assert r.status_code == 401 and r.json() == {"error": "UNAUTHORIZED"}
    r = client.get("/v1/market/005930/quote", headers={"X-Pairing-Token": "wrong"})
    assert r.status_code == 401 and r.json() == {"error": "UNAUTHORIZED"}
    assert client.get("/v1/market/005930/quote", headers={"X-Pairing-Token": TOKEN}).status_code == 200
    assert client.get("/health").status_code == 200


def test_no_secret_or_kiwoom_token_in_any_payload(monkeypatch):
    import asyncio
    import time

    import httpx

    from spatial_gateway import app as app_module

    seen = {"login": None, "rest": []}
    real_client = httpx.AsyncClient

    class FakeTokens:  # the provider the real REST and realtime clients use holds the fake Kiwoom token
        def __init__(self, *a, **k): pass
        async def token(self): return FAKE_KIWOOM_TOKEN
        async def invalidate(self): return True

    def rest_handler(request):
        seen["rest"].append(request.headers["authorization"])
        return httpx.Response(200, json={"return_code": 0, "return_msg": FAKE_KIWOOM_TOKEN, "stk_min_pole_chart_qry": []})

    class KiwoomWS:
        def __init__(self):
            self.frames = [{"trnm": "LOGIN", "return_code": 0, "return_msg": FAKE_KIWOOM_TOKEN},
                           {"trnm": "REG", "return_code": 0},
                           {"trnm": "REAL", "data": [{"type": "0B", "item": "005930",
                                                      "values": {"10": "+78800", "15": "+1", "20": "100005"}}]}]

        async def send(self, text):
            msg = json.loads(text)
            if msg["trnm"] == "LOGIN":
                seen["login"] = msg["token"]

        async def recv(self):
            if self.frames:
                return json.dumps(self.frames.pop(0))
            await asyncio.Event().wait()

    class Connect:
        def __init__(self, url, **kwargs): self.ws = KiwoomWS()
        async def __aenter__(self): return self.ws
        async def __aexit__(self, *exc): return False

    real_realtime = app_module.KiwoomRealtime
    monkeypatch.setattr(app_module, "TokenProvider", FakeTokens)
    monkeypatch.setattr(app_module.httpx, "AsyncClient",
                        lambda **k: real_client(transport=httpx.MockTransport(rest_handler)))
    monkeypatch.setattr(app_module, "KiwoomRealtime",
                        lambda *a, **k: real_realtime(*a, connect=Connect, clock=lambda: NOW, **k))

    hub = MarketHub(lambda: NOW, watch_codes=("005930",))
    texts = []
    with TestClient(create_app(SETTINGS, hub=hub, start_broker=True, timing=FAST)) as client:
        deadline = time.time() + 3
        while time.time() < deadline and hub.observations("005930", CID).quote.value is None:
            time.sleep(0.02)
        assert seen["login"] == FAKE_KIWOOM_TOKEN and f"Bearer {FAKE_KIWOOM_TOKEN}" in seen["rest"]
        with client.websocket_connect("/v1/stream") as ws:
            ws.send_text(sub())
            texts += [json.dumps(m) for m in collect(
                ws, lambda m: any(x["type"] == "heartbeat" for x in m) and len(m) >= 4)]
        for cap in ("quote", "candles", "orderbook"):
            texts.append(client.get(f"/v1/market/005930/{cap}", headers={"X-Pairing-Token": TOKEN}).text)
        health = client.get("/health")
        texts.append(health.text)
    assert health.json()["orders_enabled"] is False and health.json()["broker_connected"] is True
    assert any('"78800"' in text for text in texts)
    for text in texts:
        for secret in (APPKEY, SECRET, FAKE_KIWOOM_TOKEN, TOKEN):
            assert secret not in text


def test_order_routes_still_absent():
    _, client = make()
    assert client.post("/v1/orders", json={}).status_code == 404
    assert client.post("/v1/confirm_order").status_code == 404


def test_module_import_does_not_require_credentials(monkeypatch):
    for name in ("KIWOOM_APPKEY", "KIWOOM_SECRETKEY", "GATEWAY_PAIRING_TOKEN"):
        monkeypatch.delenv(name, raising=False)
    import spatial_gateway.app as module
    assert callable(module.create_app)


def test_broker_lifespan_seeds_marks_down_on_crash_and_cleans_up(monkeypatch):
    import asyncio
    import time
    from spatial_gateway import app as app_module
    from spatial_gateway.kiwoom.rest import MinuteBar

    seeds, state = [], {"closed": False, "cancelled": False}

    class FakeClient:
        def __init__(self, *a, **k): pass
        async def aclose(self): state["closed"] = True

    class FakeTokens:
        def __init__(self, *a, **k): pass

    class FakeRest:
        def __init__(self, *a, **k): pass
        async def minute_candles(self, code, limit=240):
            seeds.append(code)
            p = Decimal("100")
            return [MinuteBar(start=datetime(2026, 10, 27, 9, 59, tzinfo=KST), open=p, high=p, low=p, close=p, volume=5)]

    class FakeRealtime:
        def __init__(self, settings, tokens, on_event, on_state, **k): self.on_state = on_state
        async def run(self):
            self.on_state(True, None)
            await asyncio.sleep(0.05)
            raise RuntimeError("boom")

    for name, fake in (("httpx", None), ("TokenProvider", FakeTokens), ("KiwoomRest", FakeRest),
                       ("KiwoomRealtime", FakeRealtime)):
        if fake:
            monkeypatch.setattr(app_module, name, fake)
    monkeypatch.setattr(app_module.httpx, "AsyncClient", FakeClient)

    hub = MarketHub(lambda: NOW, watch_codes=("005930",))
    app = create_app(SETTINGS, hub=hub, start_broker=True, timing=FAST)
    with TestClient(app) as client:
        deadline = time.time() + 3
        while time.time() < deadline and (hub.connection != (False, "KIWOOM_WS_CLOSED") or len(seeds) < 2):
            time.sleep(0.02)  # REST seeds are spaced, so the second may land after the crash
        assert seeds == ["005930", "005930_NX"]
        assert {("005930", Market.KRX), ("005930", Market.NXT)} <= set(hub._states)
        assert hub.connection == (False, "KIWOOM_WS_CLOSED")
        assert client.get("/health").json()["broker_connected"] is False
        with client.websocket_connect("/v1/stream") as ws:
            ws.send_text(sub())
            beat = collect(ws, lambda m: any(x["type"] == "heartbeat" for x in m))[-1]
            assert beat["broker_connected"] is False and beat["reason"] == "KIWOOM_WS_CLOSED"
    assert state["closed"] is True


def test_realtime_starts_without_waiting_for_a_slow_rest_seed(monkeypatch):
    import asyncio
    import time
    from spatial_gateway import app as app_module

    class FakeClient:
        def __init__(self, *a, **k): pass
        async def aclose(self): pass

    class SlowRest:
        def __init__(self, *a, **k): pass
        async def minute_candles(self, code, limit=240):
            await asyncio.Event().wait()

    class FakeRealtime:
        def __init__(self, settings, tokens, on_event, on_state, **k): self.on_state = on_state
        async def run(self):
            self.on_state(True, None)
            await asyncio.Event().wait()

    monkeypatch.setattr(app_module, "TokenProvider", lambda *a, **k: None)
    monkeypatch.setattr(app_module, "KiwoomRest", SlowRest)
    monkeypatch.setattr(app_module, "KiwoomRealtime", FakeRealtime)
    monkeypatch.setattr(app_module.httpx, "AsyncClient", FakeClient)
    hub = MarketHub(lambda: NOW, watch_codes=("005930",))
    with TestClient(create_app(SETTINGS, hub=hub, start_broker=True, timing=FAST)):
        deadline = time.time() + 3
        while time.time() < deadline and hub.connection != (True, None):
            time.sleep(0.02)
        assert hub.connection == (True, None)


def test_pusher_crash_is_logged_by_type(caplog):
    class BrokenHub(MarketHub):
        def observations(self, code, correlation_id):
            raise RuntimeError("boom " + FAKE_KIWOOM_TOKEN)

    hub = BrokenHub(lambda: NOW, watch_codes=("005930",))
    client = TestClient(create_app(SETTINGS, hub=hub, start_broker=False, timing=FAST))
    with caplog.at_level(logging.WARNING):
        with client.websocket_connect("/v1/stream") as ws:
            ws.send_text(sub())
            with pytest.raises(WebSocketDisconnect):
                for _ in range(10):
                    ws.receive_json()
    assert "RuntimeError" in caplog.text and FAKE_KIWOOM_TOKEN not in caplog.text


def test_rejected_host_is_logged_and_testserver_is_not_a_production_host(caplog):
    production = Settings(APPKEY, SECRET, TOKEN, ("005930",), ("127.0.0.1",))
    client = TestClient(create_app(production, start_broker=False, timing=FAST))
    with caplog.at_level(logging.WARNING):
        assert client.get("/health").status_code == 400
        assert client.get("/health", headers={"host": "evil.example:8000", "X-Pairing-Token": TOKEN}).status_code == 400
        assert client.get("/health", headers={"host": "127.0.0.1:8000"}).status_code == 200
    rejected = [r.getMessage() for r in caplog.records if r.levelno == logging.WARNING]
    assert any("testserver" in m for m in rejected) and any("evil.example" in m for m in rejected)
    assert not any("127.0.0.1" in m for m in rejected)
    assert TOKEN not in caplog.text


# -- trader spread: index codes, daily/four-hour charts, balance and news --------------------------------

SPREAD = StreamTiming(quote_interval=0.02, candles_interval=0.05, heartbeat_interval=0.15,
                      handshake_timeout=1.0, subject_interval=0.2)


class FakeAccount:
    def __init__(self):
        self.value = None
        self.signals = 0

    def balance_changed(self):
        self.signals += 1

    def observation(self, cid):
        return observe(Balance, ACCOUNT, cid, value=self.value, received=NOW if self.value else None,
                       source=NOW if self.value else None, connected=True, reason=None, closed=False, watched=True)


class FakeNews:
    def __init__(self):
        self.value = None

    def observation(self, cid):
        return observe(NewsFeed, NEWS, cid, value=self.value, received=NOW if self.value else None,
                       source=NOW if self.value else None, connected=True, reason=None, closed=False,
                       watched=True, origin=Origin.NEWS)


def make_spread(timing=SPREAD):
    hub = MarketHub(lambda: NOW, watch_codes=("005930",), index_codes=("001",))
    hub.set_connection(True, None)
    hub.account, hub.news = FakeAccount(), FakeNews()
    return hub, TestClient(create_app(SETTINGS, hub=hub, start_broker=False, timing=timing))


def observations(msgs):
    return [m for m in msgs if m["type"] == "observation"]


def caps(msgs):
    return {m["capability"] for m in observations(msgs)}


def beats_after(n, cond=lambda m: True):
    """Until `cond` holds and then n more heartbeats arrived."""
    def until(seen):
        hit = next((i for i in range(len(seen)) if cond(seen[:i + 1])), None)
        return hit is not None and sum(1 for x in seen[hit + 1:] if x["type"] == "heartbeat") >= n
    return until


def bar(day, hour, price="100"):
    p = Decimal(price)
    return Bar(datetime(2026, 10, day, hour, 0, tzinfo=KST), p, p, p, p, 5)


def test_stream_timing_defaults_coalesce_subjects_at_one_second():
    assert StreamTiming().subject_interval == 1.0


def test_index_code_subscription_gets_quote_and_charts_but_no_orderbook():
    hub, client = make_spread()
    hub.seed_chart("001", Market.KRX, "1D", [bar(26, 9, "2645.12")])
    with client.websocket_connect("/v1/stream") as ws:
        ws.send_text(sub(codes=("001",)))
        seen = collect(ws, beats_after(3, lambda m: caps(m) >= {"quote", "candles_1d", "candles_4h",
                                                                "balance", "news"}), limit=200)
    assert caps(seen) == {"quote", "candles_1d", "candles_4h", "balance", "news"}
    by_cap = {m["capability"]: m for m in observations(seen)}
    assert by_cap["quote"]["observation"]["instrument_code"] == "001"
    assert by_cap["quote"]["market"] == "KRX"
    daily = by_cap["candles_1d"]
    assert daily["market"] == "KRX" and daily["observation"]["value"]["interval"] == "1D"
    assert daily["observation"]["value"]["candles"][-1]["close"] == "2645.12"


def test_stock_subscription_gets_daily_and_four_hour_charts():
    hub, client = make_spread()
    hub.seed_chart("005930", Market.KRX, "1D", [bar(26, 9)])
    with client.websocket_connect("/v1/stream") as ws:
        ws.send_text(sub(codes=("005930",)))
        seen = collect(ws, lambda m: caps(m) >= {"quote", "orderbook", "candles_1d", "candles_4h",
                                                 "balance", "news"}, limit=200)
        hub.seed_chart("005930", Market.KRX, "4H", [bar(27, 9, "101")])  # a later seed is pushed
        more = collect(ws, lambda m: any(x["capability"] == "candles_4h" and x["observation"]["value"]
                                         for x in observations(m)), limit=200)
    daily = [m for m in observations(seen) if m["capability"] == "candles_1d"][0]
    assert daily["market"] == "KRX" and daily["observation"]["value"]["interval"] == "1D"
    assert daily["observation"]["instrument_code"] == "005930"
    four = [m for m in observations(more) if m["capability"] == "candles_4h" and m["observation"]["value"]][0]
    assert four["market"] == "KRX" and four["observation"]["value"]["interval"] == "4H"


def test_balance_and_news_sent_once_per_change_to_every_client():
    hub, client = make_spread()
    first = lambda m: caps(m) >= {"balance", "news"}
    with client.websocket_connect("/v1/stream") as a, client.websocket_connect("/v1/stream") as b:
        a.send_text(sub())
        b.send_text(sub(cid="22222222-2222-4222-8222-222222222222"))
        for ws in (a, b):
            collect(ws, first, limit=200)
        hub.account.value = Balance(account="123****89", total_evaluation=Decimal("1000"), total_profit=None,
                                    total_profit_rate=None, orderable_cash=None, holdings=())
        hub.news.value = NewsFeed(items=(NewsItem(kind="NEWS", source="연합뉴스", title="코스피 마감",
                                                  published_at=NOW, mentions=("001",)),))
        hub._notify(ACCOUNT)
        hub._notify(NEWS)
        for ws in (a, b):
            got = lambda m: any(x["capability"] == c and x["observation"]["value"]
                                for x in observations(m) for c in ("balance",)) and \
                any(x["capability"] == "news" and x["observation"]["value"] for x in observations(m))
            seen = collect(ws, beats_after(4, got), limit=300)
            for cap, subject in (("balance", "ACCOUNT"), ("news", "NEWS")):
                sent = [m for m in observations(seen) if m["capability"] == cap]
                assert len(sent) == 1
                assert sent[0]["market"] is None and sent[0]["observation"]["instrument_code"] == subject


def test_stream_never_sends_the_legacy_minute_candles():
    hub, client = make_spread()
    with client.websocket_connect("/v1/stream") as ws:
        ws.send_text(sub())
        seen = collect(ws, beats_after(2, lambda m: caps(m) >= {"quote", "orderbook", "candles_1d"}), limit=200)
    assert "candles" not in caps(seen)


def test_code_ticks_mark_only_code_keys_pending():
    from spatial_gateway.stream import _changed_keys
    hub, _ = make_spread()
    keys = _changed_keys(hub, ["005930", "001"], "005930")
    assert keys == {("005930", c) for c in ("quote", "orderbook", "candles_1d", "candles_4h")}
    assert _changed_keys(hub, ["005930"], ACCOUNT) == {(ACCOUNT, "balance")}
    assert _changed_keys(hub, ["005930"], NEWS) == {(NEWS, "news")}
    assert _changed_keys(hub, ["005930"], "000660") == set()


def test_stream_without_account_or_news_services_sends_neither():
    hub, client = make()
    with client.websocket_connect("/v1/stream") as ws:
        ws.send_text(sub())
        seen = collect(ws, beats_after(2, lambda m: caps(m) >= {"quote", "candles_1d"}), limit=200)
    assert not caps(seen) & {"balance", "news"}


@pytest.mark.parametrize("codes", [("0001",), ("ACCOUNT",), ("NEWS",), tuple(f"00593{i}" for i in range(10)) + ("001",)])
def test_subscribe_rejects_bad_codes(codes):
    _, client = make_spread()
    with client.websocket_connect("/v1/stream") as ws:
        ws.send_text(sub(codes=codes))
        with pytest.raises(WebSocketDisconnect) as info:
            ws.receive_json()
    assert info.value.code == 4401


def test_orders_route_still_404():
    _, client = make()
    headers = {"X-Pairing-Token": TOKEN}
    assert client.get("/v1/orders", headers=headers).status_code == 404
    assert client.post("/v1/orders", headers=headers, json={}).status_code == 404
    assert client.get("/health").json()["orders_enabled"] is False


def test_lifespan_seeds_charts_runs_account_and_news_and_survives_a_dead_task(monkeypatch, caplog):
    import asyncio
    import time
    from spatial_gateway import app as app_module

    calls, built = [], {}

    class FakeClient:
        def __init__(self, *a, **k): pass
        async def aclose(self): pass

    class FakeRest:
        def __init__(self, *a, **k): pass
        async def minute_candles(self, code, limit=240):
            calls.append(("1m", code))
            return []
        async def daily_candles(self, code, base_date, limit=120):
            calls.append(("1D", code))
            if code == "000660":
                raise RuntimeError("seed boom " + FAKE_KIWOOM_TOKEN)
            return [bar(26, 9)]
        async def hour_candles(self, code, limit=200):
            calls.append(("60", code))
            return [bar(27, 9)]
        async def index_daily(self, code, base_date, limit=120):
            calls.append(("I1D", code))
            return [bar(26, 9, "2645.12")]
        async def index_hour(self, code, limit=200):
            calls.append(("I60", code))
            return [bar(27, 9, "2645.12")]

    class FakeRealtime:
        def __init__(self, settings, tokens, on_event, on_state, *, index_codes=(), **k):
            built["index_codes"] = tuple(index_codes)
            self.on_state = on_state
        async def run(self):
            self.on_state(True, None)
            await asyncio.Event().wait()

    class DyingAccount:
        def __init__(self, rest, clock, *, last_price, notify, **k):
            built["account"] = self
            self.last_price, self.notify = last_price, notify
        async def run(self):
            raise RuntimeError("account boom " + FAKE_KIWOOM_TOKEN)
        def observation(self, cid):
            return FakeAccount().observation(cid)

    class RunningNews:
        def __init__(self, client, settings, clock, notify, **k):
            built["news"] = self
            self.notify, self.runs = notify, 0
        async def run(self):
            self.runs += 1
            self.notify()
            await asyncio.Event().wait()
        def observation(self, cid):
            return FakeNews().observation(cid)

    monkeypatch.setattr(app_module, "TokenProvider", lambda *a, **k: None)
    monkeypatch.setattr(app_module, "KiwoomRest", FakeRest)
    monkeypatch.setattr(app_module, "KiwoomRealtime", FakeRealtime)
    monkeypatch.setattr(app_module, "AccountService", DyingAccount)
    monkeypatch.setattr(app_module, "NewsService", RunningNews)
    monkeypatch.setattr(app_module.httpx, "AsyncClient", FakeClient)
    monkeypatch.setattr(app_module, "SEED_GAP", 0.001)
    monkeypatch.setattr(app_module, "RESEED_SECONDS", 0.2)
    settings = Settings(APPKEY, SECRET, TOKEN, ("005930", "000660"), ("127.0.0.1", "testserver"))
    hub = MarketHub(lambda: NOW, watch_codes=settings.watch_codes, index_codes=settings.index_codes)
    with caplog.at_level(logging.WARNING):
        with TestClient(create_app(settings, hub=hub, start_broker=True, timing=SPREAD)) as client:
            deadline = time.time() + 3
            while time.time() < deadline and calls.count(("I60", "001")) < 2:
                time.sleep(0.02)
            assert built["index_codes"] == ("001",)
            assert hub.account is built["account"] and hub.news is built["news"]
            assert built["news"].runs == 1 and hub.connection == (True, None)
            assert built["account"].last_price("005930") is None
            hub.apply(trade("78800"))
            assert built["account"].last_price("005930") == Decimal("78800")
            with client.websocket_connect("/v1/stream") as ws:
                ws.send_text(sub(codes=("005930", "001")))
                seen = collect(ws, lambda m: caps(m) >= {"candles_1d", "candles_4h", "balance", "news"}
                               and any(x["capability"] == "candles_1d" and x["observation"]["instrument_code"] == "001"
                                       and x["observation"]["value"] for x in observations(m)), limit=300)
    first = calls[:calls.index(("I60", "001")) + 1]
    assert ("1D", "005930") in first and ("1D", "000660") in first
    assert ("1D", "005930_NX") not in calls  # daily is KRX only
    assert {("60", "005930"), ("60", "005930_NX"), ("60", "000660"), ("60", "000660_NX")} <= set(first)
    assert ("I1D", "001") in first
    assert calls.count(("1D", "005930")) >= 2  # reseeded
    assert any(m["capability"] == "candles_1d" and m["observation"]["value"] for m in observations(seen))
    assert "account task died: RuntimeError" in caplog.text
    assert "RuntimeError" in caplog.text and FAKE_KIWOOM_TOKEN not in caplog.text
