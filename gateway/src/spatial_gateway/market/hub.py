"""Per-market live quote, book and minute bars, exposed as Observation contracts.

State is keyed by (code, market); KRX and NXT data are never merged. Events that
cannot form valid models are dropped whole, so existing state is never corrupted.
"""

import asyncio
import logging
from collections.abc import Callable, Sequence
from dataclasses import dataclass, field, replace
from datetime import datetime, timedelta
from decimal import Decimal

from spatial_gateway.kiwoom.realtime import AccountEvent, Book, IndexTick, Trade
from spatial_gateway.kiwoom.rest import MinuteBar
from spatial_gateway.kiwoom.values import KST
from spatial_gateway.models import (
    BookLevel, Candle, CandleSeries, DataState, Observation, OrderBook, Origin, Quote,
)

from .charts import ChartStore
from .observe import observe
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
class _Bar:
    start: datetime
    open: Decimal
    high: Decimal
    low: Decimal
    close: Decimal
    rest_volume: int | None = None
    live_volume: int | None = None
    trade_at: datetime | None = None  # latest live trade folded into this bar

    @property
    def key(self) -> str:
        return _key(self.start)

    def candle(self) -> Candle:
        volumes = [v for v in (self.rest_volume, self.live_volume) if v is not None]
        return Candle(period_key=self.key, open=self.open, high=self.high, low=self.low,
                      close=self.close, volume=max(volumes) if volumes else None)


@dataclass
class _State:
    quote: Quote | None = None
    quote_at: datetime | None = None
    quote_received: datetime | None = None
    book: OrderBook | None = None
    book_at: datetime | None = None
    book_received: datetime | None = None
    bars: list[_Bar] = field(default_factory=list)
    seeded_at: datetime | None = None
    trade_at: datetime | None = None
    series_received: datetime | None = None

    @property
    def last_received(self) -> datetime | None:
        times = [t for t in (self.quote_received, self.book_received, self.series_received) if t is not None]
        return max(times) if times else None


def _minute(at: datetime) -> datetime:
    return at.astimezone(KST).replace(second=0, microsecond=0)


def _key(start: datetime) -> str:
    return start.astimezone(KST).strftime("%Y%m%d%H%M")


def _aware(at: datetime) -> None:
    if at.tzinfo is None or at.utcoffset() is None:
        raise ValueError("naive datetime")


def _check(bars: list[_Bar]) -> None:
    CandleSeries(interval="1m", adjustment="unadjusted", candles=tuple(b.candle() for b in bars))


class MarketHub:
    def __init__(self, clock: Callable[[], datetime], max_bars: int = 240, *, watch_codes: Sequence[str] = (),
                 index_codes: Sequence[str] = (), on_account: Callable[[], None] | None = None):
        self._clock = clock
        self._on_account = on_account
        self._max_bars = max_bars
        self._states: dict[tuple[str, Market], _State] = {}
        self._subscribers: list[asyncio.Queue[str]] = []
        self._watched = frozenset(watch_codes)
        self.charts = ChartStore(clock, watch_codes=watch_codes, index_codes=index_codes)
        self._connected = False
        self._reason: str | None = "KIWOOM_CONNECTING"

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

    @property
    def connection(self) -> tuple[bool, str | None]:
        return self._connected, self._reason

    # -- writes ------------------------------------------------------------
    def set_connection(self, connected: bool, reason: str | None) -> None:
        self._connected = connected
        self._reason = None if connected else (reason or "DISCONNECTED")

    def seed_candles(self, code: str, market: Market, bars: Sequence[MinuteBar]) -> None:
        try:
            now = self._clock()
            state = self._states.get((code, market)) or _State()
            merged = {b.key: replace(b) for b in state.bars}
            for item in sorted(bars, key=lambda b: b.start):
                _aware(item.start)
                start = _minute(item.start)
                key = _key(start)
                live = merged.get(key)
                if live is None:
                    merged[key] = _Bar(start, item.open, item.high, item.low, item.close, rest_volume=item.volume)
                else:  # same minute from REST and live: open from REST, close from live if any trade
                    live.open = item.open
                    live.high, live.low = max(live.high, item.high), min(live.low, item.low)
                    if live.trade_at is None:
                        live.close = item.close
                    live.rest_volume = item.volume
            ordered = [merged[k] for k in sorted(merged)][-self._max_bars:]
            _check(ordered)
        except Exception as error:
            log.warning("minute bar seed dropped: %s", type(error).__name__)
            return
        state.bars = ordered
        state.seeded_at = now
        state.series_received = state.series_received or now
        self._states[(code, market)] = state
        self._notify(code)

    def apply(self, event: Trade | Book | IndexTick | AccountEvent) -> None:
        try:
            if isinstance(event, AccountEvent):
                if self._on_account is not None:
                    self._on_account()
                return
            if isinstance(event, IndexTick):
                if self.charts.apply_index(event.code, event.price, event.change, event.change_percent, event.at):
                    self._notify(event.code)
                return
            if isinstance(event, Trade):
                changed = self._apply_trade(event)
                changed = self.charts.apply_trade(event) or changed
            elif isinstance(event, Book):
                changed = self._apply_book(event)
            else:
                return
        except Exception as error:
            log.warning("realtime event dropped: %s", type(error).__name__)
            return
        if changed:
            self._notify(event.code)

    def _apply_trade(self, t: Trade) -> bool:
        _aware(t.at)
        now = self._clock()
        quote = Quote(last_price=t.price, change=t.change, change_percent=t.change_percent, volume=t.acc_volume)
        state = self._states.get((t.code, t.market)) or _State()
        fresher = state.quote_at is None or t.at >= state.quote_at
        start = _minute(t.at)
        key = _key(start)
        bars = [replace(b) for b in state.bars]
        touched_bars = False
        if bars and key == bars[-1].key:
            bar = bars[-1]
            bar.high, bar.low = max(bar.high, t.price), min(bar.low, t.price)
            bar.live_volume = (bar.live_volume or 0) + (t.volume or 0)
            if bar.trade_at is None or t.at >= bar.trade_at:
                bar.close, bar.trade_at = t.price, t.at
            touched_bars = True
        elif not bars or key > bars[-1].key:
            bars.append(_Bar(start, t.price, t.price, t.price, t.price,
                             live_volume=t.volume or 0, trade_at=t.at))
            bars = bars[-self._max_bars:]
            touched_bars = True
        elif not fresher:
            return False  # stale trade for an already-closed minute
        if touched_bars:
            _check(bars)
        if not touched_bars and not fresher:
            return False
        if fresher:
            state.quote, state.quote_at, state.quote_received = quote, t.at, now
        if touched_bars:
            state.bars = bars
            state.trade_at = t.at if state.trade_at is None else max(state.trade_at, t.at)
            state.series_received = now
        self._states[(t.code, t.market)] = state
        return True

    def _apply_book(self, b: Book) -> bool:
        _aware(b.at)
        now = self._clock()
        if len(b.asks) != 10 or len(b.bids) != 10:
            raise ValueError("book depth")
        book = OrderBook(
            asks=tuple(BookLevel(rank=i + 1, price=p, quantity=q) for i, (p, q) in enumerate(b.asks)),
            bids=tuple(BookLevel(rank=i + 1, price=p, quantity=q) for i, (p, q) in enumerate(b.bids)))
        state = self._states.get((b.code, b.market)) or _State()
        if state.book_at is not None and b.at < state.book_at:
            return False
        state.book, state.book_at, state.book_received = book, b.at, now
        self._states[(b.code, b.market)] = state
        return True

    # -- reads -------------------------------------------------------------
    def _latest_market(self, code: str) -> Market | None:
        """Market whose state for `code` was received most recently, i.e. the last session that traded."""
        received = [(s.last_received, m) for (c, m), s in self._states.items() if c == code and s.last_received]
        return max(received)[1] if received else None

    def observations(self, code: str, correlation_id: str) -> HubSnapshot:
        now = self._clock()
        market = active_market(now)
        closed = market is None
        if closed:  # outside sessions the last session's values stay visible as DELAYED / MARKET_CLOSED
            market = self._latest_market(code)

        def empty(model, state: DataState, reason: str | None, value=None, received=None, source=None):
            return Observation[model](
                instrument_code=code, state=state, origin=Origin.KIWOOM, value=value,
                received_at=received, source_at=source, reason=reason, correlation_id=correlation_id)

        def one(model, value, received, source):
            watched = code in self._watched or any(k[0] == code for k in self._states)
            return observe(model, code, correlation_id, value=value, received=received, source=source,
                           connected=self._connected, reason=self._reason, closed=closed, watched=watched)

        if market is None:  # a broken link must not read as an ordinary close
            state, reason = (DataState.UNAVAILABLE, "MARKET_CLOSED") if self._connected else (DataState.DISCONNECTED, self._reason)
            return HubSnapshot(None, *(empty(m, state, reason)
                                       for m in (Quote, CandleSeries, OrderBook)))
        state = self._states.get((code, market)) or _State()
        quote = one(Quote, state.quote, state.quote_received, state.quote_at)
        book = one(OrderBook, state.book, state.book_received, state.book_at)
        series, received, source, delayed = None, None, None, False
        if state.bars:
            last = len(state.bars) - 1
            series = CandleSeries(
                interval="1m", adjustment="unadjusted",
                candles=tuple(b.candle().model_copy(update={"complete": i != last}) for i, b in enumerate(state.bars)))
            received = state.series_received
            if state.trade_at is not None:
                source = state.trade_at
            else:
                source = state.bars[-1].start
                received = state.seeded_at
                delayed = abs(_minute(now) - source) > _SEED_LIVE_WINDOW
        candles = one(CandleSeries, series, received, source)
        if delayed and candles.state == DataState.LIVE:
            candles = Observation[CandleSeries](
                instrument_code=code, state=DataState.DELAYED, origin=Origin.KIWOOM, value=series,
                received_at=received, source_at=source, correlation_id=correlation_id)
        return HubSnapshot(market, quote, candles, book)
