"""Paired client WebSocket: pushes coalesced observations and a heartbeat.

Wire shapes are decoded by the Unity client; do not add or rename fields.
"""

import asyncio
import hmac
import json
import logging
import re
from collections.abc import Callable
from dataclasses import dataclass
from datetime import datetime
from uuid import UUID

from fastapi import WebSocket, WebSocketDisconnect

from .kiwoom.values import KST
from .market.hub import HubSnapshot, MarketHub
from .market.session import Market
from .models import ACCOUNT, NEWS, Observation

log = logging.getLogger(__name__)

CLOSE_UNAUTHORIZED = 4401
_CODE = re.compile(r"^([0-9]{6}|[0-9]{3})$")  # stock or index
_STOCK_CAPABILITIES = ("quote", "candles", "orderbook", "candles_1d", "candles_4h")
_INDEX_CAPABILITIES = ("quote", "candles_1d", "candles_4h")  # an index has no order book
_CHART_INTERVALS = {"candles_1d": "1D", "candles_4h": "4H"}
_SUBJECT_CAPABILITY = {ACCOUNT: "balance", NEWS: "news"}  # one per connection, not tied to codes


@dataclass(frozen=True)
class StreamTiming:
    quote_interval: float = 0.1     # per (code, capability) coalescing window
    candles_interval: float = 0.5   # candles (1m, 1D, 4H) at most 2 per second
    subject_interval: float = 1.0   # balance and news at most once per second
    heartbeat_interval: float = 5.0
    handshake_timeout: float = 10.0


def token_matches(expected: str, supplied: object) -> bool:
    if not isinstance(supplied, str):
        return False
    return hmac.compare_digest(expected.encode(), supplied.encode())


def _parse_subscribe(raw: str, pairing_token: str) -> tuple[list[str], str] | None:
    try:
        msg = json.loads(raw)
        if not isinstance(msg, dict) or msg.get("type") != "subscribe":
            return None
        if not token_matches(pairing_token, msg.get("token")):
            return None
        codes, cid = msg.get("codes"), msg.get("correlation_id")
        if not isinstance(codes, list) or not 1 <= len(codes) <= 10:
            return None
        if not all(isinstance(c, str) and _CODE.fullmatch(c) for c in codes):
            return None
        if not isinstance(cid, str):
            return None
        UUID(cid)
        return list(dict.fromkeys(codes)), cid
    except (ValueError, TypeError):
        return None


async def serve_stream(websocket: WebSocket, hub: MarketHub, pairing_token: str,
                       timing: StreamTiming, clock: Callable[[], datetime] = lambda: datetime.now(KST)) -> None:
    await websocket.accept()
    try:
        raw = await asyncio.wait_for(websocket.receive_text(), timing.handshake_timeout)
    except (asyncio.TimeoutError, WebSocketDisconnect, KeyError, RuntimeError):
        await _close(websocket)
        return
    parsed = _parse_subscribe(raw, pairing_token)
    if parsed is None:
        await _close(websocket)
        return
    codes, cid = parsed
    queue = hub.subscribe()
    reader = asyncio.ensure_future(_drain_client(websocket))
    pusher = asyncio.ensure_future(_push(websocket, hub, queue, codes, cid, timing, clock))
    try:
        await asyncio.wait({reader, pusher}, return_when=asyncio.FIRST_COMPLETED)
        if pusher.done() and not pusher.cancelled() and pusher.exception() is not None:
            log.warning("stream pusher failed: %s", type(pusher.exception()).__name__)
    finally:
        hub.unsubscribe(queue)
        for task in (reader, pusher):
            task.cancel()
        await asyncio.gather(reader, pusher, return_exceptions=True)
    try:
        await websocket.close()
    except Exception:
        pass


async def _close(websocket: WebSocket) -> None:
    try:
        await websocket.close(code=CLOSE_UNAUTHORIZED)
    except Exception:
        pass


async def _drain_client(websocket: WebSocket) -> None:
    while True:  # client messages after subscribe are ignored; this detects disconnects
        await websocket.receive_text()


def _is_index(code: str) -> bool:
    return len(code) == 3


def _subjects(hub: MarketHub) -> list[str]:
    return [subject for subject, service in ((ACCOUNT, hub.account), (NEWS, hub.news)) if service is not None]


def _keys(hub: MarketHub, codes: list[str]) -> set[tuple[str, str]]:
    keys = {(c, cap) for c in codes for cap in (_INDEX_CAPABILITIES if _is_index(c) else _STOCK_CAPABILITIES)}
    keys.update((subject, _SUBJECT_CAPABILITY[subject]) for subject in _subjects(hub))
    return keys


def _changed_keys(hub: MarketHub, codes: list[str], changed: str) -> set[tuple[str, str]]:
    if changed in _SUBJECT_CAPABILITY:
        return {(changed, _SUBJECT_CAPABILITY[changed])} if changed in _subjects(hub) else set()
    return _keys(hub, [changed]) if changed in codes else set()


def _interval(cap: str, timing: StreamTiming) -> float:
    if cap == "candles" or cap in _CHART_INTERVALS:
        return timing.candles_interval
    if cap in ("balance", "news"):
        return timing.subject_interval
    return timing.quote_interval


def _observe(hub: MarketHub, code: str, cap: str, cid: str,
             snapshots: dict[str, HubSnapshot]) -> tuple[Market | None, Observation]:
    connected, reason = hub.connection
    if cap == "balance":
        return None, hub.account.observation(cid)
    if cap == "news":
        return None, hub.news.observation(cid)
    if cap in _CHART_INTERVALS:
        return hub.charts.observation(code, _CHART_INTERVALS[cap], cid, connected, reason)
    if _is_index(code):  # quote; indices are KRX only
        return Market.KRX, hub.charts.index_quote(code, cid, connected, reason)
    if code not in snapshots:
        snapshots[code] = hub.observations(code, cid)
    snapshot = snapshots[code]
    return snapshot.market, getattr(snapshot, cap)


async def _push(websocket: WebSocket, hub: MarketHub, queue: asyncio.Queue[str], codes: list[str],
                cid: str, timing: StreamTiming, clock: Callable[[], datetime]) -> None:
    loop = asyncio.get_running_loop()
    last_sent: dict[tuple[str, str], str] = {}
    last_at: dict[tuple[str, str], float] = {}
    pending: set[tuple[str, str]] = _keys(hub, codes)
    connection = hub.connection
    next_beat = loop.time()
    tick = min(timing.quote_interval, timing.heartbeat_interval)

    async def heartbeat() -> None:
        connected, reason = hub.connection
        await websocket.send_text(json.dumps({
            "type": "heartbeat", "broker_connected": connected,
            "reason": None if connected else reason, "server_time": clock().isoformat()}))

    while True:
        now = loop.time()
        if now >= next_beat:
            await heartbeat()
            next_beat = now + timing.heartbeat_interval
            pending.update(_keys(hub, codes))  # re-check session/state drift
        if hub.connection != connection:
            connection = hub.connection
            await heartbeat()
            pending.update(_keys(hub, codes))
        snapshots: dict[str, HubSnapshot] = {}
        for key in sorted(pending):
            code, cap = key
            if now - last_at.get(key, float("-inf")) < _interval(cap, timing):
                continue
            pending.discard(key)
            market, observation = _observe(hub, code, cap, cid, snapshots)
            body = observation.model_dump_json()
            if last_sent.get(key) == body:
                continue
            last_sent[key] = body
            last_at[key] = now
            await websocket.send_text(json.dumps({
                "type": "observation", "capability": cap,
                "market": market.value if market is not None else None,
                "observation": json.loads(body)}))
        try:
            changed = await asyncio.wait_for(queue.get(), tick)
            pending.update(_changed_keys(hub, codes, changed))
            while not queue.empty():
                pending.update(_changed_keys(hub, codes, queue.get_nowait()))
        except asyncio.TimeoutError:
            pass
