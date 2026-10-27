from dataclasses import dataclass
from datetime import date, datetime
from decimal import Decimal

import httpx

from spatial_gateway.kiwoom.auth import TokenProvider
from spatial_gateway.kiwoom.values import kst_datetime, price, quantity
from spatial_gateway.settings import Settings

_INVALID_TOKEN = 8005
_ONE = Decimal(1)
_INDEX_SCALE = Decimal(100)


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


class KiwoomRest:
    def __init__(self, client: httpx.AsyncClient, tokens: TokenProvider, settings: Settings) -> None:
        self._client = client
        self._tokens = tokens
        self._settings = settings

    async def request(self, api_id: str, path: str, body: dict) -> dict:
        retried = False
        while True:
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
        self, api_id: str, body: dict, list_key: str, time_key: str, daily: bool, scale: Decimal, limit: int
    ) -> list[Bar]:
        data = await self.request(api_id, "/api/dostk/chart", body)
        rows = data.get(list_key)
        if not isinstance(rows, list) or limit <= 0:
            return []
        bars: list[Bar] = []
        for row in rows:
            bar = _parse_bar(row, time_key, daily, scale) if isinstance(row, dict) else None
            if bar is not None:
                bars.append(bar)
        bars.sort(key=lambda b: b.start)
        return bars[-limit:]

    async def minute_candles(self, code: str, limit: int = 240) -> list[Bar]:
        return await self._chart(
            "ka10080",
            {"stk_cd": code, "tic_scope": "1", "upd_stkpc_tp": "0"},
            "stk_min_pole_chart_qry", "cntr_tm", False, _ONE, limit,
        )

    async def hour_candles(self, code: str, limit: int = 200) -> list[Bar]:
        return await self._chart(
            "ka10080",
            {"stk_cd": code, "tic_scope": "60", "upd_stkpc_tp": "0"},
            "stk_min_pole_chart_qry", "cntr_tm", False, _ONE, limit,
        )

    async def daily_candles(self, code: str, base_date: date, limit: int = 120) -> list[Bar]:
        return await self._chart(
            "ka10081",
            {"stk_cd": code, "base_dt": base_date.strftime("%Y%m%d"), "upd_stkpc_tp": "1"},
            "stk_dt_pole_chart_qry", "dt", True, _ONE, limit,
        )

    async def index_daily(self, index_code: str, base_date: date, limit: int = 120) -> list[Bar]:
        """ka20006. Live fixture: index prices arrive as 100x integers ("563834" = 5638.34), so divide by 100."""
        return await self._chart(
            "ka20006",
            {"inds_cd": index_code, "base_dt": base_date.strftime("%Y%m%d")},
            "inds_dt_pole_qry", "dt", True, _INDEX_SCALE, limit,
        )

    async def index_hour(self, index_code: str, limit: int = 200) -> list[Bar]:
        """ka20005. Live fixture: index prices arrive as 100x integers ("+563834" = 5638.34), so divide by 100."""
        return await self._chart(
            "ka20005",
            {"inds_cd": index_code, "tic_scope": "60"},
            "inds_min_pole_qry", "cntr_tm", False, _INDEX_SCALE, limit,
        )


def _text(row: dict, key: str) -> str | None:
    value = row.get(key)
    return value if isinstance(value, str) else None


def _parse_bar(row: dict, time_key: str = "cntr_tm", daily: bool = False, scale: Decimal = _ONE) -> Bar | None:
    stamp = _text(row, time_key)
    if stamp is None:
        return None
    if daily:
        start = kst_datetime(stamp.strip() + "090000") if len(stamp.strip()) == 8 else None
    else:
        start = kst_datetime(stamp)
    open_ = price(_text(row, "open_pric"))
    high = price(_text(row, "high_pric"))
    low = price(_text(row, "low_pric"))
    close = price(_text(row, "cur_prc"))
    volume = quantity(_text(row, "trde_qty"))
    if start is None or open_ is None or high is None or low is None or close is None or volume is None:
        return None
    if not (low <= open_ <= high and low <= close <= high):
        return None
    if scale != _ONE:
        open_, high, low, close = (v / scale for v in (open_, high, low, close))
    return Bar(start=start, open=open_, high=high, low=low, close=close, volume=volume)
