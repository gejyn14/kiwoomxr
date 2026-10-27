"""Per-market live quote, book and minute bars, exposed as Observation contracts.

State is keyed by (code, market); KRX and NXT data are never merged. Events that
cannot form valid models are dropped whole, so existing state is never corrupted.
"""

import asyncio
import logging
from collections.abc import Callable, Sequence
from dataclasses import dataclass, field
from datetime import datetime, timedelta
from decimal import Decimal

from spatial_gateway.kiwoom.realtime import Book, Trade
from spatial_gateway.kiwoom.rest import MinuteBar
from spatial_gateway.kiwoom.values import KST
from spatial_gateway.models import (
    BookLevel, Candle, CandleSeries, DataState, Observation, OrderBook, Origin, Quote,
)

from .session import Market, active_market

log = logging.getLogger(__name__)

_SEED_LIVE_WINDOW = timedelta(minutes=2)
_QUEUE_SIZE = 64


@dataclass(frozen=True)
class HubSnapshot:
    market: Market | None
    quote: Observation[Quote]
    candles: Observation[CandleSeries]
    orderbook: Observation[OrderBook]


@dataclass
class _State:
    quote: Quote | None = None
    quote_at: datetime | None = None
    quote_received: datetime | None = None
    book: OrderBook | None = None
    book_at: datetime | None = None
    book_received: datetime | None = None
    bars: list[Candle] = field(default_factory=list)
    bar_starts: dict[str, datetime] = field(default_factory=dict)
    seeded_at: datetime | None = None
    trade_at: datetime | None = None
    series_received: datetime | None = None


def _minute(at: datetime) -> datetime:
    return at.astimezone(KST).replace(second=0, microsecond=0)


def _key(start: datetime) -> str:
    return start.astimezone(KST).strftime("%Y%m%d%H%M")


def _aware(at: datetime) -> None:
    if at.tzinfo is None or at.utcoffset() is None:
        raise ValueError("naive datetime")


class MarketHub:
    def __init__(self, clock: Callable[[], datetime], max_bars: int = 240):
        self._clock = clock
        self._max_bars = max_bars
        self._states: dict[tuple[str, Market], _State] = {}
        self._subscribers: list[asyncio.Queue[str]] = []
        self._connected = True
        self._reason: str | None = None

    # -- subscribers -------------------------------------------------------
    def subscribe(self) -> asyncio.Queue[str]:
        queue: asyncio.Queue[str] = asyncio.Queue(maxsize=_QUEUE_SIZE)
        self._subscribers.append(queue)
        return queue

    def unsubscribe(self, queue: asyncio.Queue[str]) -> None:
        if queue in self._subscribers:
            self._subscribers.remove(queue)

    def _notify(self, code: str) -> None:
        for queue in self._subscribers:
            if queue.full():
                queue.get_nowait()
            queue.put_nowait(code)

    # -- writes ------------------------------------------------------------
    def set_connection(self, connected: bool, reason: str | None) -> None:
        self._connected = connected
        self._reason = None if connected else (reason or "DISCONNECTED")

    def seed_candles(self, code: str, market: Market, bars: Sequence[MinuteBar]) -> None:
        try:
            now = self._clock()
            seeded: dict[str, tuple[datetime, Candle]] = {}
            for item in sorted(bars, key=lambda b: b.start):
                _aware(item.start)
                start = _minute(item.start)
                seeded[_key(start)] = (start, Candle(
                    period_key=_key(start), open=item.open, high=item.high, low=item.low,
                    close=item.close, volume=item.volume))
            state = self._states.get((code, market)) or _State()
            merged = {k: (state.bar_starts[k], c) for k, c in zip(state.bar_starts, state.bars)}
            for k, v in seeded.items():
                merged.setdefault(k, v)  # live-built bars win over REST
            ordered = sorted(merged.items())[-self._max_bars:]
            CandleSeries(interval="1m", adjustment="unadjusted", candles=tuple(c for _, (_, c) in ordered))
        except Exception as error:
            log.warning("minute bar seed dropped: %s", type(error).__name__)
            return
        state.bars = [c for _, (_, c) in ordered]
        state.bar_starts = {k: s for k, (s, _) in ordered}
        state.seeded_at = now
        state.series_received = state.series_received or now
        self._states[(code, market)] = state
        self._notify(code)

    def apply(self, event: Trade | Book) -> None:
        try:
            if isinstance(event, Trade):
                self._apply_trade(event)
            elif isinstance(event, Book):
                self._apply_book(event)
            else:
                return
        except Exception as error:
            log.warning("realtime event dropped: %s", type(error).__name__)
            return
        self._notify(event.code)

    def _apply_trade(self, t: Trade) -> None:
        _aware(t.at)
        now = self._clock()
        quote = Quote(last_price=t.price, change=t.change, change_percent=t.change_percent, volume=t.acc_volume)
        state = self._states.get((t.code, t.market)) or _State()
        start = _minute(t.at)
        key = _key(start)
        bars, starts = list(state.bars), dict(state.bar_starts)
        if bars and key == bars[-1].period_key:
            last = bars[-1]
            bars[-1] = Candle(period_key=key, open=last.open, high=max(last.high, t.price),
                              low=min(last.low, t.price), close=t.price,
                              volume=(last.volume or 0) + (t.volume or 0))
        elif not bars or key > bars[-1].period_key:
            bars.append(Candle(period_key=key, open=t.price, high=t.price, low=t.price,
                               close=t.price, volume=t.volume or 0))
            starts[key] = start
            for old in bars[:-self._max_bars]:
                starts.pop(old.period_key, None)
            bars = bars[-self._max_bars:]
        else:
            bars = None  # late trade for a closed minute: quote only
        if bars is not None:
            CandleSeries(interval="1m", adjustment="unadjusted", candles=tuple(bars))
        state.quote, state.quote_at, state.quote_received = quote, t.at, now
        if bars is not None:
            state.bars, state.bar_starts = bars, starts
            state.trade_at, state.series_received = t.at, now
        self._states[(t.code, t.market)] = state

    def _apply_book(self, b: Book) -> None:
        _aware(b.at)
        now = self._clock()
        if len(b.asks) != 10 or len(b.bids) != 10:
            raise ValueError("book depth")
        book = OrderBook(
            asks=tuple(BookLevel(rank=i + 1, price=p, quantity=q) for i, (p, q) in enumerate(b.asks)),
            bids=tuple(BookLevel(rank=i + 1, price=p, quantity=q) for i, (p, q) in enumerate(b.bids)))
        state = self._states.get((b.code, b.market)) or _State()
        state.book, state.book_at, state.book_received = book, b.at, now
        self._states[(b.code, b.market)] = state

    # -- reads -------------------------------------------------------------
    def observations(self, code: str, correlation_id: str) -> HubSnapshot:
        now = self._clock()
        market = active_market(now)

        def empty(model, state: DataState, reason: str | None, value=None, received=None, source=None):
            return Observation[model](
                instrument_code=code, state=state, origin=Origin.KIWOOM, value=value,
                received_at=received, source_at=source, reason=reason, correlation_id=correlation_id)

        def one(model, value, received, source):
            if not any(k[0] == code for k in self._states):
                return empty(model, DataState.UNAVAILABLE, "NOT_SUBSCRIBED")
            if not self._connected:
                return empty(model, DataState.DISCONNECTED, self._reason, value, received, source)
            if value is None:
                return empty(model, DataState.LOADING, None)
            return Observation[model](
                instrument_code=code, state=DataState.LIVE, origin=Origin.KIWOOM, value=value,
                received_at=received, source_at=source, correlation_id=correlation_id)

        if market is None:
            return HubSnapshot(None, *(empty(m, DataState.UNAVAILABLE, "MARKET_CLOSED")
                                       for m in (Quote, CandleSeries, OrderBook)))
        state = self._states.get((code, market)) or _State()
        quote = one(Quote, state.quote, state.quote_received, state.quote_at)
        book = one(OrderBook, state.book, state.book_received, state.book_at)
        series, received, source, delayed = None, None, None, False
        if state.bars:
            last = len(state.bars) - 1
            series = CandleSeries(
                interval="1m", adjustment="unadjusted",
                candles=tuple(c.model_copy(update={"complete": i != last}) for i, c in enumerate(state.bars)))
            received = state.series_received
            if state.trade_at is not None:
                source = state.trade_at
            else:
                source = state.bar_starts[state.bars[-1].period_key]
                received = state.seeded_at
                delayed = abs(_minute(now) - source) > _SEED_LIVE_WINDOW
        candles = one(CandleSeries, series, received, source)
        if delayed and candles.state == DataState.LIVE:
            candles = Observation[CandleSeries](
                instrument_code=code, state=DataState.DELAYED, origin=Origin.KIWOOM, value=series,
                received_at=received, source_at=source, correlation_id=correlation_id)
        return HubSnapshot(market, quote, candles, book)
