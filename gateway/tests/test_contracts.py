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
from spatial_gateway.models import (
    Balance,
    Candle,
    CandleSeries,
    DataState,
    Holding,
    NewsFeed,
    NewsItem,
    Observation,
    Origin,
    Quote,
)


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


def _obs(code, **kw):
    return Observation[Quote](instrument_code=code, state=DataState.LOADING, origin=Origin.KIWOOM, correlation_id="c", **kw)


def test_subject_codes_accept_index_account_news_and_reject_others():
    for code in ("005930", "001", "ACCOUNT", "NEWS"):
        _obs(code)
    for code in ("05930", "0001", "account", "00593O"):
        with pytest.raises(ValidationError):
            _obs(code)


def _news_item(minute=0, title="t"):
    return NewsItem(
        kind="NEWS", source="src", title=title,
        published_at=datetime(2026, 10, 27, 10, 30 - minute, tzinfo=timezone.utc),
        mentions=(), filing_type=None,
    )


def test_news_origin_can_be_live_but_synthetic_cannot():
    now = datetime(2026, 10, 27, 10, 0, tzinfo=timezone.utc)
    feed = NewsFeed(items=(_news_item(),))
    live = dict(instrument_code="NEWS", state=DataState.LIVE, value=feed, received_at=now, source_at=now, correlation_id="c")
    Observation[NewsFeed](origin=Origin.NEWS, **live)
    with pytest.raises(ValidationError):
        Observation[NewsFeed](origin=Origin.SYNTHETIC, **live)


def test_balance_accepts_only_masked_account():
    kwargs = dict(
        total_evaluation=Decimal("5000000"), total_profit=Decimal("-1000.5"), total_profit_rate=Decimal("-0.02"),
        orderable_cash=Decimal("1032000"),
        holdings=(Holding(code="005930", name="Samsung", quantity=3, average_price=Decimal("70000"),
                          current_price=None, profit=None, profit_rate=None),),
    )
    balance = Balance(account="123*****90", **kwargs)
    dumped = balance.model_dump(mode="json")
    assert dumped["orderable_cash"] == "1032000"
    assert dumped["holdings"][0]["quantity"] == "3"
    with pytest.raises(ValidationError):
        Balance(account="1234567890", **kwargs)
    with pytest.raises(ValidationError):
        Balance(account="123*****90", **{**kwargs, "orderable_cash": 1.5})


def test_news_feed_must_be_newest_first_and_bounded():
    NewsFeed(items=(_news_item(0), _news_item(1)))
    with pytest.raises(ValidationError):
        NewsFeed(items=(_news_item(1), _news_item(0)))
    with pytest.raises(ValidationError):
        NewsFeed(items=tuple(_news_item(0) for _ in range(41)))
    with pytest.raises(ValidationError):
        _news_item(title="x" * 201)
