"""Daily and four-hour candle series per (code, market), with live last-bar updates.

KRX and NXT series are never merged. Index codes (3 digits) are KRX-only. Updates that would
form an invalid CandleSeries are dropped whole, so existing state is never corrupted.
"""

import logging
from collections.abc import Callable, Sequence
from dataclasses import dataclass, replace
from datetime import datetime
from decimal import Decimal
from typing import Literal

from spatial_gateway.kiwoom.realtime import Trade
from spatial_gateway.kiwoom.rest import Bar
from spatial_gateway.kiwoom.values import KST
from spatial_gateway.models import Candle, CandleSeries, DataState, Observation, Origin, Quote

from .aggregate import bucket_start, four_hour
from .observe import observe
from .session import Market, active_market

log = logging.getLogger(__name__)

Interval = Literal["1D", "4H"]
MAX_BARS = 120
_ADJUSTMENT = {"1D": "adjusted", "4H": "unadjusted"}


@dataclass
class _Series:
    bars: list[Bar]
    received: datetime
    source: datetime | None = None  # latest live tick folded in; None while only REST-seeded


def _is_index(code: str) -> bool:
    return len(code) == 3


def _key(start: datetime, interval: str) -> str:
    return start.astimezone(KST).strftime("%Y%m%d" if interval == "1D" else "%Y%m%d%H%M")


def _series(bars: Sequence[Bar], interval: str) -> CandleSeries:
    last = len(bars) - 1
    return CandleSeries(
        interval=interval, adjustment=_ADJUSTMENT[interval],
        candles=tuple(Candle(period_key=_key(b.start, interval), open=b.open, high=b.high, low=b.low,
                             close=b.close, volume=b.volume, complete=i != last)
                      for i, b in enumerate(bars)))


def _aware(at: datetime) -> None:
    if at.tzinfo is None or at.utcoffset() is None:
        raise ValueError("naive datetime")


def _day_start(at: datetime) -> datetime:
    return at.astimezone(KST).replace(hour=9, minute=0, second=0, microsecond=0)


class ChartStore:
    def __init__(self, clock: Callable[[], datetime], *, watch_codes: Sequence[str] = (),
                 index_codes: Sequence[str] = ()):
        self._clock = clock
        self._known = frozenset(watch_codes) | frozenset(index_codes)
        self._series: dict[tuple[str, Market, str], _Series] = {}
        self._last_4h: dict[str, tuple[datetime, Market]] = {}
        self._quotes: dict[str, tuple[Quote, datetime, datetime]] = {}

    # -- writes ------------------------------------------------------------
    def seed(self, code: str, market: Market, interval: Interval, bars: Sequence[Bar]) -> None:
        try:
            if _is_index(code) and market is not Market.KRX:
                raise ValueError("index is KRX only")
            now = self._clock()
            ordered = sorted(bars, key=lambda b: b.start) if interval == "1D" else four_hour(bars, market)
            ordered = ordered[-MAX_BARS:]
            _series(ordered, interval)
        except Exception as error:
            log.warning("chart seed dropped: %s", type(error).__name__)
            return
        old = self._series.get((code, market, interval))
        if old is not None and old.source is not None and ordered:  # keep live bars newer than the seed
            newer = [b for b in old.bars if b.start > ordered[-1].start]
            ordered = (ordered + newer)[-MAX_BARS:]
        self._series[(code, market, interval)] = _Series(ordered, now, old.source if old else None)
        if interval == "4H":
            self._touch_4h(code, market, now)

    def apply_trade(self, trade: Trade) -> bool:
        try:
            _aware(trade.at)
            now = self._clock()
            changed = False
            if trade.market is Market.KRX and not _is_index(trade.code):
                changed |= self._tick(trade.code, Market.KRX, "1D", trade.at, trade.price,
                                      volume=trade.acc_volume, add=False, now=now)
            changed |= self._tick(trade.code, trade.market, "4H", trade.at, trade.price,
                                  volume=trade.volume, add=True, now=now)
            return changed
        except Exception as error:
            log.warning("chart trade dropped: %s", type(error).__name__)
            return False

    def apply_index(self, code: str, price: Decimal, change: Decimal | None,
                    change_percent: Decimal | None, at: datetime) -> bool:
        try:
            _aware(at)
            now = self._clock()
            quote = Quote(last_price=price, change=change, change_percent=change_percent)
            prior = self._quotes.get(code)
            fresher = prior is None or at >= prior[1]
            changed = self._tick(code, Market.KRX, "1D", at, price, volume=None, add=False, now=now)
            changed |= self._tick(code, Market.KRX, "4H", at, price, volume=None, add=False, now=now)
            if fresher:
                self._quotes[code] = (quote, at, now)
                changed = True
            return changed
        except Exception as error:
            log.warning("index tick dropped: %s", type(error).__name__)
            return False

    def _tick(self, code: str, market: Market, interval: str, at: datetime, price: Decimal,
              *, volume: int | None, add: bool, now: datetime) -> bool:
        start = _day_start(at) if interval == "1D" else bucket_start(at, market)
        if start is None:
            return False
        state = self._series.get((code, market, interval))
        if state is None or not state.bars:
            return False  # a lone tick must not masquerade as history; wait for the REST seed
        bars = list(state.bars)
        if start == bars[-1].start:
            b = bars[-1]
            vol = b.volume
            if volume is not None:
                vol = (b.volume or 0) + volume if add else max(b.volume or 0, volume)
            bars[-1] = replace(b, high=max(b.high, price), low=min(b.low, price), close=price, volume=vol)
        elif start > bars[-1].start:
            bars.append(Bar(start, price, price, price, price, volume or 0))
            bars = bars[-MAX_BARS:]
        else:
            return False  # stale tick for an already-closed period
        _series(bars, interval)  # validate before commit
        source = at if state.source is None else max(state.source, at)
        self._series[(code, market, interval)] = _Series(bars, now, source)
        if interval == "4H":
            self._touch_4h(code, market, now)
        return True

    def _touch_4h(self, code: str, market: Market, now: datetime) -> None:
        self._last_4h[code] = (now, market)

    # -- reads -------------------------------------------------------------
    def _watched(self, code: str) -> bool:
        return code in self._known or any(k[0] == code for k in self._series)

    def observation(self, code: str, interval: str, cid: str, connected: bool,
                    reason: str | None) -> tuple[Market | None, Observation[CandleSeries]]:
        active = active_market(self._clock())
        if interval == "1D" or _is_index(code):
            market, closed = Market.KRX, active is not Market.KRX
        else:
            market, closed = active, active is None
            if market is None and code in self._last_4h:
                market = self._last_4h[code][1]
        state = self._series.get((code, market, interval)) if market else None
        value = _series(state.bars, interval) if state and state.bars else None
        obs = observe(CandleSeries, code, cid, value=value, received=state.received if state else None,
                      source=(state.source or (state.bars[-1].start if state.bars else None)) if state else None,
                      connected=connected, reason=reason, closed=closed, watched=self._watched(code))
        return market, obs

    def index_quote(self, code: str, cid: str, connected: bool, reason: str | None) -> Observation[Quote]:
        entry = self._quotes.get(code)
        closed = active_market(self._clock()) is not Market.KRX
        return observe(Quote, code, cid, value=entry[0] if entry else None,
                       received=entry[2] if entry else None, source=entry[1] if entry else None,
                       connected=connected, reason=reason, closed=closed, watched=self._watched(code))
