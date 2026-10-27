import json
from datetime import date, datetime
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


def _fx(name):
    return json.loads((Path(__file__).parent / "fixtures" / name).read_text(encoding="utf-8"))


async def test_daily_candles_parse_fixture_and_request_adjusted_prices():
    rest, requests, _ = build([_fx("ka10081_005930.json")])
    bars = await rest.daily_candles("005930", date(2026, 10, 28))
    (req,) = requests
    assert req.headers["api-id"] == "ka10081"
    body = json.loads(req.content)
    assert body["upd_stkpc_tp"] == "1" and body["base_dt"] == "20261028" and body["stk_cd"] == "005930"
    assert len(bars) == 5
    assert [b.start for b in bars] == sorted(b.start for b in bars)
    assert bars[-1].start == datetime(2026, 10, 27, 9, 0, tzinfo=KST)
    assert all(isinstance(b.close, Decimal) and b.close > 0 for b in bars)


async def test_hour_candles_use_tic_scope_60():
    rest, requests, _ = build([_fx("ka10080_60_005930.json")])
    bars = await rest.hour_candles("005930")
    assert json.loads(requests[0].content)["tic_scope"] == "60"
    assert bars and all(b.start.minute == 0 for b in bars)


async def test_index_daily_scales_to_index_points():
    rest, requests, _ = build([_fx("ka20006_001.json")])
    bars = await rest.index_daily("001", date(2026, 10, 28))
    assert requests[0].headers["api-id"] == "ka20006"
    assert json.loads(requests[0].content) == {"inds_cd": "001", "base_dt": "20261028"}
    assert 1000 < bars[-1].close < 10000
    assert bars[-1].close == Decimal("5638.34")
    assert all(b.close.as_tuple().exponent >= -2 for b in bars)


async def test_index_hour_parses_fixture():
    rest, requests, _ = build([_fx("ka20005_60_001.json")])
    bars = await rest.index_hour("001")
    body = json.loads(requests[0].content)
    assert requests[0].headers["api-id"] == "ka20005" and body["inds_cd"] == "001" and body["tic_scope"] == "60"
    assert bars and [b.start for b in bars] == sorted(b.start for b in bars)
    assert 1000 < bars[-1].close < 10000


async def test_chart_rows_with_missing_or_inverted_prices_are_dropped():
    data = _fx("ka10081_005930.json")
    rows = data["stk_dt_pole_chart_qry"]
    rows[0] = {**rows[0], "high_pric": "1", "low_pric": "999999"}
    rows[1] = {**rows[1], "cur_prc": ""}
    rest, _, _ = build([data])
    bars = await rest.daily_candles("005930", date(2026, 10, 28))
    assert len(bars) == 3
