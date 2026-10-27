import json
import logging
from datetime import datetime
from decimal import Decimal

import pytest
from fastapi.testclient import TestClient
from starlette.websockets import WebSocketDisconnect

from spatial_gateway.app import create_app
from spatial_gateway.kiwoom.realtime import Trade
from spatial_gateway.kiwoom.values import KST
from spatial_gateway.market.hub import MarketHub
from spatial_gateway.market.session import Market
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
        first = collect(ws, lambda m: len([x for x in m if x["type"] == "observation"]) >= 3)
        obs = [m for m in first if m["type"] == "observation"]
        assert {m["capability"] for m in obs} == {"quote", "candles", "orderbook"}
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
        while time.time() < deadline and hub.connection != (False, "KIWOOM_WS_CLOSED"):
            time.sleep(0.02)
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
