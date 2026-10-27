"""Kiwoom realtime WebSocket: pure frame parsing plus a reconnecting session loop.

The token is only ever sent in the LOGIN frame; it never reaches logs, state reasons or exceptions.
"""

import asyncio
import json
from collections.abc import Awaitable, Callable, Sequence
from dataclasses import dataclass
from datetime import date, datetime
from decimal import Decimal
from typing import Any

import websockets
from websockets.exceptions import ConnectionClosed

from spatial_gateway.kiwoom import values as v
from spatial_gateway.kiwoom.auth import KiwoomAuthError
from spatial_gateway.market.session import Market, realtime_item
from spatial_gateway.settings import Settings

_BACKOFF = (1, 2, 4, 8, 15)
_NX = "_NX"
_AUTH_CODE = 8005


@dataclass(frozen=True)
class Trade:
    code: str
    market: Market
    price: Decimal
    change: Decimal | None
    change_percent: Decimal | None
    volume: int | None
    acc_volume: int | None
    at: datetime


@dataclass(frozen=True)
class Book:
    code: str
    market: Market
    asks: tuple[tuple[Decimal | None, int | None], ...]
    bids: tuple[tuple[Decimal | None, int | None], ...]
    at: datetime


def _field(values: dict, key: str) -> str | None:
    raw = values.get(key)
    return raw if isinstance(raw, str) else None


def _time(values: dict, key: str, today: date) -> datetime | None:
    raw = _field(values, key)
    return None if raw is None else v.kst_time_today(raw, today)


def _levels(values: dict, price_start: int, qty_start: int):
    return tuple(
        (v.price(_field(values, str(price_start + i))), v.quantity(_field(values, str(qty_start + i))))
        for i in range(10)
    )


def _parse_entry(entry: Any, today: date) -> Trade | Book | None:
    if not isinstance(entry, dict):
        return None
    kind, item, values = entry.get("type"), entry.get("item"), entry.get("values")
    if isinstance(values, list) and len(values) == 1:
        values = values[0]
    if not isinstance(item, str) or not isinstance(values, dict) or kind not in ("0B", "0D"):
        return None
    if item.endswith(_NX):
        code, market = item[: -len(_NX)], Market.NXT
    else:
        code, market = item, Market.KRX
    at = _time(values, "20" if kind == "0B" else "21", today)
    if at is None:
        return None
    if kind == "0B":
        price = v.price(_field(values, "10"))
        if price is None:
            return None
        return Trade(
            code, market, price,
            v.signed(_field(values, "11")), v.signed(_field(values, "12")),
            v.quantity(_field(values, "15")), v.quantity(_field(values, "13")), at,
        )
    return Book(code, market, _levels(values, 41, 61), _levels(values, 51, 71), at)


def parse_real_frame(frame: dict, today: date) -> list[Trade | Book]:
    if not isinstance(frame, dict) or frame.get("trnm") != "REAL":
        return []
    data = frame.get("data")
    if not isinstance(data, list):
        return []
    parsed = (_parse_entry(entry, today) for entry in data)
    return [event for event in parsed if event is not None]


class _Dropped(Exception):
    """Connection ended or misbehaved (not an auth problem)."""


class _AuthFailed(Exception):
    pass


def _loads(raw: Any) -> dict | None:
    try:
        frame = json.loads(raw)
    except (ValueError, TypeError):
        return None
    return frame if isinstance(frame, dict) else None


class KiwoomRealtime:
    def __init__(
        self,
        settings: Settings,
        tokens: Any,
        on_event: Callable[[Trade | Book], None],
        on_state: Callable[[bool, str | None], None],
        *,
        connect: Callable[..., Any] = websockets.connect,
        sleep: Callable[[float], Awaitable[None]] = asyncio.sleep,
        clock: Callable[[], datetime] = lambda: datetime.now(v.KST),
    ) -> None:
        self._settings = settings
        self._tokens = tokens
        self._on_event = on_event
        self._on_state = on_state
        self._connect = connect
        self._sleep = sleep
        self._clock = clock
        self._attempt = 0

    @staticmethod
    def register_message(codes: Sequence[str]) -> dict:
        items: list[str] = []
        for code in codes:
            items += [realtime_item(code, Market.KRX), realtime_item(code, Market.NXT)]
        return {"trnm": "REG", "grp_no": "1", "refresh": "1", "data": [{"item": items, "type": ["0B", "0D"]}]}

    async def run(self) -> None:
        while True:
            try:
                await self._session()
            except _AuthFailed:
                self._on_state(False, "KIWOOM_AUTH")
            except (_Dropped, ConnectionClosed, OSError, KiwoomAuthError) as error:
                reason = "KIWOOM_AUTH" if isinstance(error, KiwoomAuthError) else "KIWOOM_WS_CLOSED"
                self._on_state(False, reason)
            else:
                self._on_state(False, "KIWOOM_WS_CLOSED")
            delay = _BACKOFF[min(self._attempt, len(_BACKOFF) - 1)]
            self._attempt += 1
            await self._sleep(delay)

    async def _recv_ack(self, ws: Any, trnm: str, buffer: list[dict]) -> dict:
        while True:
            frame = _loads(await ws.recv())
            if frame is None:
                continue
            kind = frame.get("trnm")
            if kind == "PING":
                await ws.send(json.dumps(frame))
            elif "return_code" in frame and kind == trnm:
                return frame
            elif kind == "REAL":
                buffer.append(frame)

    async def _session(self) -> None:
        token = await self._tokens.token()
        async with self._connect(self._settings.ws_url, ping_interval=None, ping_timeout=None) as ws:
            buffer: list[dict] = []
            await ws.send(json.dumps({"trnm": "LOGIN", "token": token}))
            ack = await self._recv_ack(ws, "LOGIN", buffer)
            if ack.get("return_code") != 0:
                code = ack.get("return_code")
                if code == _AUTH_CODE or str(_AUTH_CODE) in str(ack.get("return_msg", "")):
                    await self._tokens.invalidate()
                raise _AuthFailed
            await ws.send(json.dumps(self.register_message(self._settings.watch_codes)))
            ack = await self._recv_ack(ws, "REG", buffer)
            if ack.get("return_code") != 0:
                raise _Dropped
            self._attempt = 0
            self._on_state(True, None)
            for frame in buffer:
                self._emit(frame)
            while True:
                frame = _loads(await ws.recv())
                if frame is None:
                    continue
                if frame.get("trnm") == "PING":
                    await ws.send(json.dumps(frame))
                else:
                    self._emit(frame)

    def _emit(self, frame: dict) -> None:
        for event in parse_real_frame(frame, self._clock().astimezone(v.KST).date()):
            self._on_event(event)
