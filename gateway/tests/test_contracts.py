from datetime import datetime, timezone
from decimal import Decimal
from uuid import uuid4

import pytest
from fastapi.testclient import TestClient
from pydantic import ValidationError

from spatial_gateway.app import create_app
from spatial_gateway.models import Candle, CandleSeries, DataState, Observation, Origin, Quote


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
def test_missing_spec_cannot_produce_financial_values_or_read_credentials(capability, monkeypatch):
    monkeypatch.setenv("KIWOOM_APPKEY", "must-not-be-read-or-reflected")
    correlation_id = str(uuid4())
    with TestClient(create_app()) as client:
        response = client.get(f"/v1/market/005930/{capability}", headers={"X-Correlation-ID": correlation_id})
    assert response.status_code == 200
    data = response.json()
    assert data["state"] == "UNAVAILABLE"
    assert data["reason"] == "BLOCKED_BY_MISSING_SPEC:K-01"
    assert data["value"] is None
    assert data["received_at"] is None
    assert data["correlation_id"] == correlation_id
    assert "must-not-be-read" not in response.text


def test_unknown_instrument_format_and_untrusted_host_rejected():
    with TestClient(create_app()) as client:
        assert client.get("/v1/market/not-a-code/quote").status_code == 422
        assert client.get("/health", headers={"Host": "untrusted.example"}).status_code == 400
        assert client.post("/v1/orders", json={"execute": True}).status_code == 404
        assert client.post("/v1/confirm_order").status_code == 404


def test_validation_error_does_not_echo_untrusted_input():
    with TestClient(create_app()) as client:
        response = client.get("/v1/market/005930/quote", headers={"X-Correlation-ID": "sensitive-input"})
    assert response.status_code == 422
    assert response.json() == {"error": "INVALID_REQUEST"}


def test_decimal_transport_matches_client_precision_and_has_no_exponents():
    assert Quote(last_price=Decimal("1E+5")).model_dump(mode="json")["last_price"] == "100000"
    assert Quote(volume=123456789012345678).model_dump(mode="json")["volume"] == "123456789012345678"
    for value in ("0.123456789", "12345678901234567890123456789"):
        with pytest.raises(ValidationError):
            Quote(last_price=value)
