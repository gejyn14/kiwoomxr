from datetime import datetime, timezone
from decimal import Decimal
from uuid import uuid4

import pytest
from fastapi.testclient import TestClient
from pydantic import ValidationError

from spatial_gateway.app import create_app
from spatial_gateway.kiwoom.values import KST
from spatial_gateway.market.hub import MarketHub
from spatial_gateway.settings import Settings
from spatial_gateway.models import Candle, CandleSeries, DataState, Observation, Origin, Quote


TOKEN = "t" * 40
SETTINGS = Settings("ak", "sk", TOKEN, ("005930",), ("127.0.0.1", "localhost", "testserver"))
AUTH = {"X-Pairing-Token": TOKEN}


def make_client(hub=None):
    hub = hub or MarketHub(lambda: datetime(2026, 10, 27, 10, 0, tzinfo=KST), watch_codes=())
    return TestClient(create_app(SETTINGS, hub=hub, start_broker=False))


def test_missing_values_are_null_and_decimal_wire_is_exact():
    quote = Quote(last_price=Decimal("123456789.12345678"), change=Decimal("-12.50"))
    payload = quote.model_dump(mode="json")
    assert payload["last_price"] == "123456789.12345678"
    assert payload["change"] == "-12.50"
    assert payload["volume"] is None
    assert Quote().last_price is None


@pytest.mark.parametrize("value", [1.1, True, "NaN", "Infinity", "-1", "0"])
def test_invalid_price_never_silently_coerces(value):
    with pytest.raises(ValidationError):
        Quote(last_price=value)


def test_invalid_candles_and_duplicate_periods_fail_closed():
    with pytest.raises(ValidationError):
        Candle(period_key="20260917", open="100", high="90", low="80", close="85")
    candle = Candle(period_key="20260917", open="85", high="90", low="80", close="85")
    with pytest.raises(ValidationError):
        CandleSeries(interval="1D", adjustment="unadjusted", candles=(candle, candle))


@pytest.mark.parametrize("origin,source_at", [(Origin.SYNTHETIC, datetime.now(timezone.utc)), (Origin.KIWOOM, None)])
def test_live_requires_authoritative_source_time(origin, source_at):
    with pytest.raises(ValidationError):
        Observation[Quote](instrument_code="005930", state=DataState.LIVE, origin=origin,
                           value=Quote(last_price="70000"), received_at=datetime.now(timezone.utc),
                           source_at=source_at, correlation_id=str(uuid4()))


@pytest.mark.parametrize("capability", ["quote", "candles", "orderbook"])
def test_untracked_code_cannot_produce_financial_values_or_read_credentials(capability, monkeypatch):
    monkeypatch.setenv("KIWOOM_APPKEY", "must-not-be-read-or-reflected")
    correlation_id = str(uuid4())
    with make_client() as client:
        response = client.get(f"/v1/market/005930/{capability}", headers={**AUTH, "X-Correlation-ID": correlation_id})
    assert response.status_code == 200
    data = response.json()
    assert data["state"] == "UNAVAILABLE"
    assert data["reason"] == "NOT_SUBSCRIBED"
    assert data["value"] is None
    assert data["received_at"] is None
    assert data["correlation_id"] == correlation_id
    assert "must-not-be-read" not in response.text


def test_unknown_instrument_format_and_untrusted_host_rejected():
    with make_client() as client:
        assert client.get("/v1/market/not-a-code/quote", headers=AUTH).status_code == 422
        assert client.get("/health", headers={"Host": "untrusted.example"}).status_code == 400
        assert client.post("/v1/orders", json={"execute": True}).status_code == 404
        assert client.post("/v1/confirm_order").status_code == 404


def test_validation_error_does_not_echo_untrusted_input():
    with make_client() as client:
        response = client.get("/v1/market/005930/quote", headers={**AUTH, "X-Correlation-ID": "sensitive-input"})
    assert response.status_code == 422
    assert response.json() == {"error": "INVALID_REQUEST"}


def test_decimal_transport_matches_client_precision_and_has_no_exponents():
    assert Quote(last_price=Decimal("1E+5")).model_dump(mode="json")["last_price"] == "100000"
    assert Quote(volume=123456789012345678).model_dump(mode="json")["volume"] == "123456789012345678"
    for value in ("0.123456789", "12345678901234567890123456789"):
        with pytest.raises(ValidationError):
            Quote(last_price=value)
