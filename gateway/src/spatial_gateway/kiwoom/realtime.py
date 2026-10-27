"""Kiwoom realtime WebSocket: pure frame parsing plus a reconnecting session loop.

The token is only ever sent in the LOGIN frame; it never reaches logs, state reasons or exceptions.
"""

import asyncio
import json
import logging
from collections.abc import Awaitable, Callable, Sequence
from dataclasses import dataclass
from datetime import date, datetime, timedelta
from decimal import Decimal
from typing import Any

import websockets
from websockets.exceptions import WebSocketException

from spatial_gateway.kiwoom import values as v
from spatial_gateway.market.session import Market, active_market, realtime_item
from spatial_gateway.settings import Settings

_BACKOFF = (1, 2, 4, 8, 15)
_NX = "_NX"
_AUTH_CODE = 8005
ACK_TIMEOUT = 10.0
IDLE_TIMEOUT = 15.0         # during a KRX/NXT session trades and PINGs keep the socket busy
CLOSED_IDLE_TIMEOUT = 60.0  # outside sessions only PINGs arrive
_STABLE_SESSION = timedelta(seconds=30)  # a registration must last this long to reset the backoff

_log = logging.getLogger(__name__)


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


@dataclass(frozen=True)
class IndexTick:
    code: str
    price: Decimal
    change: Decimal | None
    change_percent: Decimal | None
    at: datetime


@dataclass(frozen=True)
class AccountEvent:
    """Balance changed. Carries no account data; the account number field is never read."""
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


def _parse_entry(entry: Any, today: date, now: datetime | None = None) -> Trade | Book | IndexTick | AccountEvent | None:
    if not isinstance(entry, dict):
        return None
    kind, item, values = entry.get("type"), entry.get("item"), entry.get("values")
    if isinstance(values, list) and len(values) == 1:
        values = values[0]
    if kind == "04":
        at = _time(values, "908", today) if isinstance(values, dict) else None
        return AccountEvent(at or now or datetime(today.year, today.month, today.day, tzinfo=v.KST))
    if not isinstance(item, str) or not isinstance(values, dict) or kind not in ("0B", "0D", "0J"):
        return None
    if kind == "0J":
        at = _time(values, "20", today)
        price = v.index_value(_field(values, "10"), absolute=True)
        if at is None or price is None:
            return None
        return IndexTick(item, price, v.index_value(_field(values, "11"), absolute=False),
                         v.signed(_field(values, "12")), at)
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


def parse_real_frame(frame: dict, today: date, now: datetime | None = None) -> list[Trade | Book | IndexTick | AccountEvent]:
    if not isinstance(frame, dict) or frame.get("trnm") != "REAL":
        return []
    data = frame.get("data")
    if not isinstance(data, list):
        return []
    parsed = (_parse_entry(entry, today, now) for entry in data)
    return [event for event in parsed if event is not None]


class _Dropped(Exception):
    """Connection ended or misbehaved (not an auth problem)."""


class _AuthFailed(Exception):
    pass


def _log_system(frame: dict) -> None:
    code = frame.get("code")  # the message text is not logged
    _log.warning("kiwoom SYSTEM frame: code=%r", str(code)[:32] if isinstance(code, (str, int)) else None)


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
        on_event: Callable[[Trade | Book | IndexTick | AccountEvent], None],
        on_state: Callable[[bool, str | None], None],
        *,
        index_codes: Sequence[str] = (),
        connect: Callable[..., Any] = websockets.connect,
        sleep: Callable[[float], Awaitable[None]] = asyncio.sleep,
        clock: Callable[[], datetime] = lambda: datetime.now(v.KST),
        ack_timeout: float = ACK_TIMEOUT,
        idle_timeout: float = IDLE_TIMEOUT,
        closed_idle_timeout: float = CLOSED_IDLE_TIMEOUT,
    ) -> None:
        self._ack_timeout = ack_timeout
        self._idle_timeout = idle_timeout
        self._closed_idle_timeout = closed_idle_timeout
        self._settings = settings
        self._index_codes = tuple(index_codes)
        self._tokens = tokens
        self._on_event = on_event
        self._on_state = on_state
        self._connect = connect
        self._sleep = sleep
        self._clock = clock
        self._attempt = 0
        self._registered_at: datetime | None = None

    @staticmethod
    def register_message(codes: Sequence[str], index_codes: Sequence[str] = ()) -> dict:
        items: list[str] = []
        for code in codes:
            items += [realtime_item(code, Market.KRX), realtime_item(code, Market.NXT)]
        data = [{"item": items, "type": ["0B", "0D"]}]
        if index_codes:  # an empty item group may be rejected, and a failed REG drops the session
            data.append({"item": list(index_codes), "type": ["0J"]})
        data.append({"item": [""], "type": ["04"]})
        return {"trnm": "REG", "grp_no": "1", "refresh": "1", "data": data}

    def _state(self, ok: bool, reason: str | None) -> None:
        try:
            self._on_state(ok, reason)
        except Exception as error:
            _log.warning("on_state callback failed: %s", type(error).__name__)

    async def run(self) -> None:
        while True:
            self._registered_at = None
            try:
                token = await self._tokens.token()
            except Exception as error:
                _log.warning("token fetch failed: %s", type(error).__name__)
                self._state(False, "KIWOOM_AUTH")
            else:
                try:
                    await self._session(token)
                except _AuthFailed:
                    self._state(False, "KIWOOM_AUTH")
                except (_Dropped, WebSocketException, OSError, TimeoutError) as error:
                    _log.warning("realtime connection ended: %s", type(error).__name__)
                    self._state(False, "KIWOOM_WS_CLOSED")
                except Exception as error:  # never let the loop die; CancelledError still propagates
                    _log.error("realtime session failed: %s", type(error).__name__)
                    self._state(False, "KIWOOM_WS_CLOSED")
            if self._registered_at is not None and self._clock() - self._registered_at >= _STABLE_SESSION:
                self._attempt = 0
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
            elif kind == "REAL":
                buffer.append(frame)
            else:
                if kind == "SYSTEM":
                    _log_system(frame)
                # The REG ack's trnm is not pinned down; any non-LOGIN, non-SYSTEM frame with a return_code answers it.
                if "return_code" in frame and (kind == trnm or (trnm == "REG" and kind not in ("LOGIN", "SYSTEM"))):
                    return frame

    async def _session(self, token: str) -> None:
        async with self._connect(self._settings.ws_url, ping_interval=None, ping_timeout=None) as ws:
            buffer: list[dict] = []
            await ws.send(json.dumps({"trnm": "LOGIN", "token": token}))
            async with asyncio.timeout(self._ack_timeout):
                ack = await self._recv_ack(ws, "LOGIN", buffer)
            if ack.get("return_code") != 0:
                code = ack.get("return_code")
                if code == _AUTH_CODE or str(_AUTH_CODE) in str(ack.get("return_msg", "")):
                    await self._tokens.invalidate()
                raise _AuthFailed
            await ws.send(json.dumps(self.register_message(self._settings.watch_codes, self._index_codes)))
            async with asyncio.timeout(self._ack_timeout):
                ack = await self._recv_ack(ws, "REG", buffer)
            if ack.get("return_code") != 0:
                raise _Dropped
            self._registered_at = self._clock()
            self._state(True, None)
            for frame in buffer:
                self._emit(frame)
            while True:
                idle = self._idle_timeout if active_market(self._clock()) is not None else self._closed_idle_timeout
                async with asyncio.timeout(idle):
                    raw = await ws.recv()
                frame = _loads(raw)
                if frame is None:
                    continue
                kind = frame.get("trnm")
                if kind == "PING":
                    await ws.send(json.dumps(frame))
                elif kind == "SYSTEM":
                    _log_system(frame)
                else:
                    self._emit(frame)

    def _emit(self, frame: dict) -> None:
        try:
            now = self._clock().astimezone(v.KST)
            events = parse_real_frame(frame, now.date(), now)
        except Exception as error:  # drop this frame only; the session keeps reading
            _log.warning("realtime frame dropped: %s", type(error).__name__)
            return
        for event in events:
            try:
                self._on_event(event)
            except Exception as error:
                _log.warning("on_event callback failed: %s", type(error).__name__)
