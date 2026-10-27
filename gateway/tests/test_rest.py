import json
from datetime import datetime
from decimal import Decimal
from pathlib import Path

import httpx
import pytest

from spatial_gateway.kiwoom.auth import TokenProvider
from spatial_gateway.kiwoom.rest import KiwoomApiError, KiwoomRest
from spatial_gateway.kiwoom.values import KST
from test_auth import Clock, make_settings

FIXTURE = json.loads(
    (Path(__file__).parent / "fixtures" / "ka10080_005930.json").read_text(encoding="utf-8")
)


def build(api_responses):
    """api_responses: list of dict bodies returned in order for chart calls."""
    api_requests: list[httpx.Request] = []
    token_calls = []
    queue = list(api_responses)

    def handler(request: httpx.Request) -> httpx.Response:
        if request.url.path == "/oauth2/token":
            token_calls.append(request)
            return httpx.Response(
                200,
                json={"token": f"T{len(token_calls)}", "expires_dt": "20261027235959", "return_code": 0},
            )
        api_requests.append(request)
        return httpx.Response(200, json=queue.pop(0))

    client = httpx.AsyncClient(transport=httpx.MockTransport(handler))
    settings = make_settings()
    clock = Clock(datetime(2026, 10, 27, 12, 0, tzinfo=KST))
    tokens = TokenProvider(client, settings, clock)
    return KiwoomRest(client, tokens, settings), api_requests, token_calls


async def test_minute_candles_parse_ascending_absolute_prices():
    rest, requests, _ = build([FIXTURE])
    bars = await rest.minute_candles("005930")
    assert [b.start for b in bars] == sorted(b.start for b in bars)
    assert bars[-1].close == Decimal("78800") and bars[-1].high == Decimal("78900")
    assert bars[-1].open == Decimal("78850") and bars[-1].low == Decimal("78800")
    assert bars[-1].volume == 7913
    assert bars[-1].start == datetime(2025, 9, 17, 13, 20, tzinfo=KST)
    assert len(bars) == 2
    (req,) = requests
    assert req.method == "POST" and req.url == "https://api.kiwoom.com/api/dostk/chart"
    assert req.headers["api-id"] == "ka10080"
    assert req.headers["authorization"] == "Bearer T1"
    assert req.headers["content-type"] == "application/json;charset=UTF-8"
    assert json.loads(req.content) == {"stk_cd": "005930", "tic_scope": "1", "upd_stkpc_tp": "0"}


async def test_8005_reissues_once_then_raises():
    bad = {"return_code": 8005, "return_msg": "invalid token"}
    rest, requests, token_calls = build([bad, bad])
    with pytest.raises(KiwoomApiError) as info:
        await rest.minute_candles("005930")
    assert info.value.code == 8005
    assert len(requests) == 2 and len(token_calls) == 2


async def test_8005_then_success_after_reissue():
    rest, requests, token_calls = build([{"return_code": 8005, "return_msg": "x"}, FIXTURE])
    bars = await rest.minute_candles("005930")
    assert len(bars) == 2
    assert requests[1].headers["authorization"] == "Bearer T2"


async def test_rate_limit_is_not_retried():
    rest, requests, _ = build([{"return_code": 1700, "return_msg": "limit"}])
    with pytest.raises(KiwoomApiError) as info:
        await rest.minute_candles("005930")
    assert info.value.code == 1700
    assert len(requests) == 1


async def test_bar_with_unparseable_price_is_dropped_not_zeroed():
    body = json.loads(json.dumps(FIXTURE))
    body["stk_min_pole_chart_qry"][0]["open_pric"] = ""
    rest, _, _ = build([body])
    bars = await rest.minute_candles("005930")
    assert len(bars) == 1
    assert bars[0].start == datetime(2025, 9, 17, 13, 19, tzinfo=KST)


async def test_ohlc_violation_dropped_and_limit_keeps_latest():
    body = json.loads(json.dumps(FIXTURE))
    bad = dict(body["stk_min_pole_chart_qry"][0], cntr_tm="20250917132100", high_pric="-78000")
    body["stk_min_pole_chart_qry"].insert(0, bad)  # close 78800 > high 78000
    rest, _, _ = build([body])
    bars = await rest.minute_candles("005930")
    assert [b.start.minute for b in bars] == [19, 20]
    rest, _, _ = build([body])
    bars = await rest.minute_candles("005930", limit=1)
    assert [b.start.minute for b in bars] == [20]


async def test_error_message_has_no_secrets():
    rest, _, _ = build([{"return_code": 1701, "return_msg": "too many"}])
    with pytest.raises(KiwoomApiError) as info:
        await rest.minute_candles("005930")
    assert "T1" not in str(info.value)
