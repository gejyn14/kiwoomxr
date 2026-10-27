import asyncio
import time
from dataclasses import dataclass
from datetime import date, datetime
from decimal import Decimal

import httpx

from spatial_gateway.kiwoom.auth import TokenProvider
from spatial_gateway.kiwoom.values import index_value, kst_datetime, price, quantity, signed
from spatial_gateway.models import Holding
from spatial_gateway.settings import Settings

_INVALID_TOKEN = 8005


class KiwoomApiError(Exception):
    """Kiwoom rejected a request. Messages never carry keys or tokens."""

    def __init__(self, code: int, message: str = "") -> None:
        self.code = code
        super().__init__(f"kiwoom return_code {code}" + (f": {message}" if message else ""))


@dataclass(frozen=True)
class Bar:
    start: datetime
    open: Decimal
    high: Decimal
    low: Decimal
    close: Decimal
    volume: int


MinuteBar = Bar


@dataclass(frozen=True)
class RawBalance:
    total_evaluation: Decimal | None
    total_profit: Decimal | None
    total_profit_rate: Decimal | None
    holdings: tuple[Holding, ...]


def mask_account(number: str) -> str:
    """First three digits, `*` for the middle, last two digits. Works for any length of 5 or more."""
    if len(number) < 5:
        raise ValueError("account number too short to mask")
    return number[:3] + "*" * (len(number) - 5) + number[-2:]


class KiwoomRest:
    def __init__(self, client: httpx.AsyncClient, tokens: TokenProvider, settings: Settings,
                 *, min_gap: float = 0.25) -> None:
        self._client = client
        self._tokens = tokens
        self._settings = settings
        self._min_gap = min_gap
        self._pace = asyncio.Lock()
        self._last_call: float | None = None

    async def _wait_turn(self) -> None:
        """Every caller (chart seeding, account) shares one spacing: at least `min_gap` between calls."""
        async with self._pace:
            if self._last_call is not None:
                wait = self._last_call + self._min_gap - time.monotonic()
                if wait > 0:
                    await asyncio.sleep(wait)
            self._last_call = time.monotonic()

    async def request(self, api_id: str, path: str, body: dict) -> dict:
        retried = False
        while True:
            await self._wait_turn()
            token = await self._tokens.token()
            try:
                response = await self._client.post(
                    f"{self._settings.rest_origin}{path}",
                    headers={
                        "content-type": "application/json;charset=UTF-8",
                        "api-id": api_id,
                        "authorization": f"Bearer {token}",
                    },
                    json=body,
                )
            except httpx.HTTPError as error:
                raise KiwoomApiError(-1, f"{api_id} request failed: {type(error).__name__}") from None
            try:
                data = response.json()
            except ValueError:
                raise KiwoomApiError(-1, f"{api_id} response not JSON (http {response.status_code})") from None
            if not isinstance(data, dict):
                raise KiwoomApiError(-1, f"{api_id} response malformed (http {response.status_code})")
            code = data.get("return_code")
            if code == 0:
                return data
            if not isinstance(code, int) or isinstance(code, bool):
                raise KiwoomApiError(-1, f"{api_id} response has no return_code (http {response.status_code})")
            if code == _INVALID_TOKEN and not retried:
                retried = True
                if await self._tokens.invalidate():
                    continue
            msg = data.get("return_msg")
            raise KiwoomApiError(code, msg if isinstance(msg, str) else "")

    async def _chart(
        self, api_id: str, body: dict, list_key: str, time_key: str, daily: bool, index: bool, limit: int
    ) -> list[Bar]:
        data = await self.request(api_id, "/api/dostk/chart", body)
        rows = data.get(list_key)
        if not isinstance(rows, list) or limit <= 0:
            return []
        bars: list[Bar] = []
        for row in rows:
            bar = _parse_bar(row, time_key, daily, index) if isinstance(row, dict) else None
            if bar is not None:
                bars.append(bar)
        bars.sort(key=lambda b: b.start)
        return bars[-limit:]

    async def minute_candles(self, code: str, limit: int = 240) -> list[Bar]:
        return await self._chart(
            "ka10080",
            {"stk_cd": code, "tic_scope": "1", "upd_stkpc_tp": "0"},
            "stk_min_pole_chart_qry", "cntr_tm", False, False, limit,
        )

    async def hour_candles(self, code: str, limit: int = 200) -> list[Bar]:
        return await self._chart(
            "ka10080",
            {"stk_cd": code, "tic_scope": "60", "upd_stkpc_tp": "0"},
            "stk_min_pole_chart_qry", "cntr_tm", False, False, limit,
        )

    async def daily_candles(self, code: str, base_date: date, limit: int = 120) -> list[Bar]:
        return await self._chart(
            "ka10081",
            {"stk_cd": code, "base_dt": base_date.strftime("%Y%m%d"), "upd_stkpc_tp": "1"},
            "stk_dt_pole_chart_qry", "dt", True, False, limit,
        )

    async def index_daily(self, index_code: str, base_date: date, limit: int = 120) -> list[Bar]:
        """ka20006. Live fixture: index prices arrive as 100x integers ("563834" = 5638.34); see values.index_value."""
        return await self._chart(
            "ka20006",
            {"inds_cd": index_code, "base_dt": base_date.strftime("%Y%m%d")},
            "inds_dt_pole_qry", "dt", True, True, limit,
        )

    async def index_hour(self, index_code: str, limit: int = 200) -> list[Bar]:
        """ka20005. Live fixture: index prices arrive as 100x integers ("+563834" = 5638.34); see values.index_value."""
        return await self._chart(
            "ka20005",
            {"inds_cd": index_code, "tic_scope": "60"},
            "inds_min_pole_qry", "cntr_tm", False, True, limit,
        )

    async def account_number(self) -> str:
        """ka00001 `acctNo`. Callers keep it in memory only; never log or persist it."""
        data = await self.request("ka00001", "/api/dostk/acnt", {})
        number = data.get("acctNo")
        if not isinstance(number, str) or not number.strip():
            raise KiwoomApiError(-1, "ka00001 response has no acctNo")
        return number.strip()

    async def balance(self) -> RawBalance:
        """kt00018. Live fixture (empty account) confirmed tot_evlt_amt, tot_evlt_pl, tot_prft_rt and the
        list acnt_evlt_remn_indv_tot; row fields follow the brief (stk_cd with `A` prefix, stk_nm, rmnd_qty,
        pur_pric, cur_prc, evltv_prft, prft_rt) since the live account has no rows."""
        data = await self.request("kt00018", "/api/dostk/acnt", {"qry_tp": "1", "dmst_stex_tp": "KRX"})
        rows = data.get("acnt_evlt_remn_indv_tot")
        holdings: list[Holding] = []
        for row in rows if isinstance(rows, list) else []:
            holding = _parse_holding(row) if isinstance(row, dict) else None
            if holding is not None:
                holdings.append(holding)
        return RawBalance(
            total_evaluation=signed(_text(data, "tot_evlt_amt")),
            total_profit=signed(_text(data, "tot_evlt_pl")),
            total_profit_rate=signed(_text(data, "tot_prft_rt")),
            holdings=tuple(holdings),
        )

    async def orderable_cash(self, code: str, price: Decimal) -> Decimal | None:
        """kt00010 `ord_alowa` (live fixture: zero-padded integer string). Read-only simulation query."""
        data = await self.request(
            "kt00010", "/api/dostk/acnt", {"stk_cd": code, "trde_tp": "2", "uv": format(price, "f")}
        )
        return signed(_text(data, "ord_alowa"))


def _parse_holding(row: dict) -> Holding | None:
    code = (_text(row, "stk_cd") or "").strip()
    if code.startswith("A"):
        code = code[1:]
    qty = quantity(_text(row, "rmnd_qty"))
    if len(code) != 6 or not code.isdigit() or qty is None:
        return None
    return Holding(
        code=code,
        name=(_text(row, "stk_nm") or "").strip(),
        quantity=qty,
        average_price=price(_text(row, "pur_pric")),
        current_price=price(_text(row, "cur_prc")),
        profit=signed(_text(row, "evltv_prft")),
        profit_rate=signed(_text(row, "prft_rt")),
    )


def _text(row: dict, key: str) -> str | None:
    value = row.get(key)
    return value if isinstance(value, str) else None


def _parse_bar(row: dict, time_key: str = "cntr_tm", daily: bool = False, index: bool = False) -> Bar | None:
    stamp = _text(row, time_key)
    if stamp is None:
        return None
    if daily:
        start = kst_datetime(stamp.strip() + "090000") if len(stamp.strip()) == 8 else None
    else:
        start = kst_datetime(stamp)
    level = (lambda raw: index_value(raw, absolute=True)) if index else price
    open_ = level(_text(row, "open_pric"))
    high = level(_text(row, "high_pric"))
    low = level(_text(row, "low_pric"))
    close = level(_text(row, "cur_prc"))
    volume = quantity(_text(row, "trde_qty"))
    if start is None or open_ is None or high is None or low is None or close is None or volume is None:
        return None
    if not (low <= open_ <= high and low <= close <= high):
        return None
    return Bar(start=start, open=open_, high=high, low=low, close=close, volume=volume)
