import asyncio
import json
import logging
from datetime import datetime, timedelta
from decimal import Decimal
from pathlib import Path

import httpx
import pytest

from spatial_gateway.account import AccountService, mask_account
from spatial_gateway.kiwoom.auth import TokenProvider
from spatial_gateway.kiwoom.rest import KiwoomRest
from spatial_gateway.kiwoom.values import KST
from spatial_gateway.models import DataState
from test_auth import Clock, make_settings

FX = Path(__file__).parent / "fixtures"
EMPTY = json.loads((FX / "kt00018_empty.json").read_text(encoding="utf-8"))
ONE = json.loads((FX / "kt00018_one.json").read_text(encoding="utf-8"))
CASH = json.loads((FX / "kt00010_005930.json").read_text(encoding="utf-8"))
NUMBER = "1234567890"
OPEN = datetime(2026, 10, 27, 10, 0, tzinfo=KST)
CLOSED = datetime(2026, 10, 27, 22, 0, tzinfo=KST)


class FakeClock(Clock):
    async def sleep(self, seconds: float) -> None:
        self.now += timedelta(seconds=seconds)
        await asyncio.sleep(0)  # yield so concurrent tasks interleave


def build(balances, now=OPEN, price=Decimal("225000"), on_balance=None, account_fail=False):
    clock = FakeClock(now)
    calls: list[tuple[str, datetime, dict]] = []
    queue = list(balances)

    def handler(request: httpx.Request) -> httpx.Response:
        if request.url.path == "/oauth2/token":
            return httpx.Response(200, json={"token": "T", "expires_dt": "20271027235959", "return_code": 0})
        api = request.headers["api-id"]
        calls.append((api, clock.now, json.loads(request.content)))
        if api == "ka00001" and account_fail:
            return httpx.Response(200, json={"return_code": 8001, "return_msg": "x"})
        if api == "ka00001":
            return httpx.Response(200, json={"acctNo": NUMBER, "return_code": 0, "return_msg": ""})
        if api == "kt00010":
            return httpx.Response(200, json=CASH)
        if on_balance is not None:
            on_balance(sum(1 for c in calls if c[0] == "kt00018"))
        return httpx.Response(200, json=queue.pop(0))

    client = httpx.AsyncClient(transport=httpx.MockTransport(handler))
    settings = make_settings()
    rest = KiwoomRest(client, TokenProvider(client, settings, clock), settings, min_gap=0)
    notified = []
    service = AccountService(
        rest, clock, last_price=lambda code: price, notify=lambda: notified.append(1), sleep=clock.sleep
    )
    return service, clock, calls, notified


def test_mask_account_keeps_three_and_two_digits():
    assert mask_account("1234567890") == "123*****90"
    assert mask_account("123456") == "123*56"
    with pytest.raises(ValueError):
        mask_account("1234")


async def test_empty_balance_is_live_with_no_holdings_and_orderable_cash():
    service, _, calls, notified = build([EMPTY])
    await service.refresh()
    obs = service.observation("c1")
    assert obs.state is DataState.LIVE and OPEN <= obs.source_at <= OPEN + timedelta(seconds=1)
    assert obs.value.holdings == ()
    assert obs.value.account == "123*****90"
    assert obs.value.orderable_cash == Decimal(CASH["ord_alowa"]) and obs.value.orderable_cash >= 0
    assert obs.value.total_evaluation == Decimal(0)
    bodies = {api: body for api, _, body in calls}
    assert bodies["kt00018"] == {"qry_tp": "1", "dmst_stex_tp": "KRX"}
    assert bodies["kt00010"] == {"stk_cd": "005930", "trde_tp": "2", "uv": "225000"}  # plain decimal text
    assert notified


async def test_loading_before_first_success_and_delayed_when_market_closed():
    service, _, _, _ = build([EMPTY], now=CLOSED)
    assert service.observation("c").state is DataState.UNAVAILABLE
    await service.refresh()
    obs = service.observation("c")
    assert obs.state is DataState.DELAYED and obs.reason == "MARKET_CLOSED"
    service2, _, _, _ = build([EMPTY])
    assert service2.observation("c").state is DataState.LOADING


async def test_balance_signal_requeries_within_a_second_and_coalesces_bursts():
    service, clock, calls, _ = build([EMPTY, EMPTY, EMPTY])
    await service.refresh()

    def times():
        return [t for api, t, _ in calls if api == "kt00018"]

    for _ in range(10):
        service.balance_changed()
    await service.wait_idle()
    assert len(times()) == 2
    assert times()[1] - times()[0] == timedelta(seconds=2)  # 2s spacing from the first query
    clock.now = times()[1] + timedelta(seconds=0.5)
    service.balance_changed()
    await service.wait_idle()
    assert len(times()) == 3
    assert times()[2] - times()[1] == timedelta(seconds=2)


async def test_first_holding_appears_after_signal():
    service, clock, _, _ = build([EMPTY, ONE])
    await service.refresh()
    assert service.observation("c").value.holdings == ()
    clock.now += timedelta(seconds=10)
    service.balance_changed()
    await service.wait_idle()
    (holding,) = service.observation("c").value.holdings
    assert holding.code == "005930" and holding.quantity == 1
    assert holding.current_price == Decimal("225000") and holding.profit == Decimal("154000")


async def test_query_failure_keeps_last_value_as_disconnected(caplog):
    caplog.set_level(logging.DEBUG)
    service, _, _, _ = build([EMPTY, {"return_code": 1700, "return_msg": f"too many {NUMBER}"}])
    await service.refresh()
    await service.refresh()
    obs = service.observation("c")
    assert obs.state is DataState.DISCONNECTED and obs.reason == "ACCOUNT_QUERY_FAILED"
    assert obs.value is not None and obs.value.account == "123*****90"
    assert NUMBER not in caplog.text and NUMBER not in str(obs.model_dump_json())
    assert "1700" in caplog.text


async def test_orderable_cash_skipped_without_price():
    service, _, calls, _ = build([EMPTY], price=None)
    await service.refresh()
    assert not [c for c in calls if c[0] == "kt00010"]
    assert service.observation("c").value.orderable_cash is None


async def test_signal_during_inflight_query_triggers_another_query():
    holder = {}

    def hook(n):
        if n == 1:
            holder["s"].balance_changed()

    service, clock, calls, _ = build([EMPTY, ONE], on_balance=hook)
    holder["s"] = service
    service.balance_changed()
    await service.wait_idle()
    times = [t for api, t, _ in calls if api == "kt00018"]
    assert len(times) == 2
    assert times[1] - times[0] >= timedelta(seconds=2)
    assert len(service.observation("c").value.holdings) == 1


async def test_spacing_holds_when_poll_holds_the_lock_and_a_signal_arrives():
    holder = {}

    def hook(n):
        if n == 1:
            holder["s"].balance_changed()

    service, clock, calls, _ = build([EMPTY, EMPTY], on_balance=hook)
    holder["s"] = service
    await service.refresh()  # poll refresh; the signal fires while it is in flight
    await service.wait_idle()
    times = [t for api, t, _ in calls if api == "kt00018"]
    assert len(times) == 2 and times[1] - times[0] >= timedelta(seconds=2)


async def test_account_rest_calls_are_at_least_a_quarter_second_apart():
    service, _, calls, _ = build([EMPTY])
    await service.refresh()
    assert [c[0] for c in calls] == ["ka00001", "kt00018", "kt00010"]
    gaps = [b[1] - a[1] for a, b in zip(calls, calls[1:])]
    assert all(g >= timedelta(seconds=0.25) for g in gaps)


async def test_failed_account_lookup_is_not_retried_by_the_signal_loop():
    service, _, calls, _ = build([], account_fail=True)
    service.balance_changed()
    await asyncio.wait_for(service.wait_idle(), 5)
    assert [c[0] for c in calls] == ["ka00001"]
    assert service.observation("c").reason == "ACCOUNT_QUERY_FAILED"


async def test_blank_holding_name_falls_back_to_the_stock_code():
    blank = json.loads(json.dumps(ONE))
    blank["acnt_evlt_remn_indv_tot"][0]["stk_nm"] = "   "
    service, _, _, _ = build([blank])
    await service.refresh()
    obs = service.observation("c")
    assert obs.state is DataState.LIVE
    (holding,) = obs.value.holdings
    assert holding.code == "005930" and holding.name == "005930"


def test_holding_name_must_not_be_empty():
    from pydantic import ValidationError

    from spatial_gateway.models import Holding
    with pytest.raises(ValidationError):
        Holding(code="005930", name="", quantity=1, average_price=None, current_price=None, profit=None,
                profit_rate=None)


async def test_balance_reads_disconnected_once_the_last_refresh_is_older_than_three_polls():
    service, clock, _, _ = build([EMPTY])
    await service.refresh()
    clock.now += timedelta(seconds=179)
    assert service.observation("c").state is DataState.LIVE
    clock.now += timedelta(seconds=2)
    obs = service.observation("c")
    assert obs.state is DataState.DISCONNECTED and obs.reason == "ACCOUNT_REFRESH_OVERDUE"
    assert obs.value is not None and obs.value.account == "123*****90"
