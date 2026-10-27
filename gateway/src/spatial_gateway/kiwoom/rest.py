from dataclasses import dataclass
from datetime import datetime
from decimal import Decimal

import httpx

from spatial_gateway.kiwoom.auth import TokenProvider
from spatial_gateway.kiwoom.values import kst_datetime, price, quantity
from spatial_gateway.settings import Settings

_INVALID_TOKEN = 8005


class KiwoomApiError(Exception):
    """Kiwoom rejected a request. Messages never carry keys or tokens."""

    def __init__(self, code: int, message: str = "") -> None:
        self.code = code
        super().__init__(f"kiwoom return_code {code}" + (f": {message}" if message else ""))


@dataclass(frozen=True)
class MinuteBar:
    start: datetime
    open: Decimal
    high: Decimal
    low: Decimal
    close: Decimal
    volume: int


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

    async def minute_candles(self, code: str, limit: int = 240) -> list[MinuteBar]:
        data = await self.request(
            "ka10080",
            "/api/dostk/chart",
            {"stk_cd": code, "tic_scope": "1", "upd_stkpc_tp": "0"},
        )
        rows = data.get("stk_min_pole_chart_qry")
        if not isinstance(rows, list):
            return []
        bars: list[MinuteBar] = []
        for row in rows:
            bar = _parse_bar(row) if isinstance(row, dict) else None
            if bar is not None:
                bars.append(bar)
        bars.sort(key=lambda b: b.start)
        return bars[-limit:] if limit > 0 else []


def _text(row: dict, key: str) -> str | None:
    value = row.get(key)
    return value if isinstance(value, str) else None


def _parse_bar(row: dict) -> MinuteBar | None:
    stamp = _text(row, "cntr_tm")
    start = kst_datetime(stamp) if stamp is not None else None
    open_ = price(_text(row, "open_pric"))
    high = price(_text(row, "high_pric"))
    low = price(_text(row, "low_pric"))
    close = price(_text(row, "cur_prc"))
    volume = quantity(_text(row, "trde_qty"))
    if start is None or open_ is None or high is None or low is None or close is None or volume is None:
        return None
    if not (low <= open_ <= high and low <= close <= high):
        return None
    return MinuteBar(start=start, open=open_, high=high, low=low, close=close, volume=volume)
