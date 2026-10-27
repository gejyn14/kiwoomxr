import asyncio
import logging
import os
from collections.abc import Awaitable, Callable
from contextlib import asynccontextmanager
from datetime import date, datetime
from typing import Annotated
from uuid import UUID, uuid4

import httpx
from fastapi import Depends, FastAPI, Header, Path, WebSocket
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from starlette.datastructures import Headers
from starlette.middleware.trustedhost import TrustedHostMiddleware

from .account import AccountService
from .capabilities import HubMarket
from .kiwoom.auth import TokenProvider
from .kiwoom.realtime import KiwoomRealtime
from .kiwoom.rest import KiwoomRest
from .kiwoom.values import KST
from .market.hub import MarketHub
from .market.session import Market, realtime_item
from .models import ACCOUNT, NEWS, CandleSeries, Observation, OrderBook, Quote
from .news import NewsService
from .settings import Settings
from .stream import StreamTiming, serve_stream, token_matches

log = logging.getLogger(__name__)

SEED_GAP = 0.25            # seconds between seeding REST calls
RESEED_SECONDS = 30 * 60   # daily/four-hour charts are re-seeded this often

InstrumentCode = Annotated[str, Path(pattern=r"^[0-9]{6}$")]
CorrelationHeader = Annotated[UUID | None, Header(alias="X-Correlation-ID")]


class Unauthorized(Exception):
    pass


def _now() -> datetime:
    return datetime.now(KST)


class _LoggedTrustedHost(TrustedHostMiddleware):
    """TrustedHostMiddleware that logs the rejected Host value (only that header) for LAN setup debugging."""

    async def __call__(self, scope, receive, send) -> None:
        if not self.allow_any and scope["type"] in ("http", "websocket"):
            host = Headers(scope=scope).get("host", "").split(":")[0]
            if not any(host == p or (p.startswith("*") and host.endswith(p[1:])) for p in self.allowed_hosts):
                log.warning("rejected Host: %r (add it to GATEWAY_ALLOWED_HOSTS)", host[:255])
        await super().__call__(scope, receive, send)


_SeedCall = tuple[str, Callable[[], Awaitable[list]], Callable[[list], None]]


async def _run_spaced(calls: list[_SeedCall]) -> None:
    for i, (label, fetch, store) in enumerate(calls):
        if i:
            await asyncio.sleep(SEED_GAP)
        try:
            store(await fetch())
        except asyncio.CancelledError:
            raise
        except Exception as error:  # series stays LOADING; one failure never stops the others
            log.warning("%s seed failed: %s", label, type(error).__name__)


def _minute_calls(rest: KiwoomRest, hub: MarketHub, codes: tuple[str, ...]) -> list[_SeedCall]:
    return [("minute candle", lambda item=realtime_item(code, m): rest.minute_candles(item),
             lambda bars, code=code, m=m: hub.seed_candles(code, m, bars))
            for code in codes for m in (Market.KRX, Market.NXT)]


def _chart_calls(rest: KiwoomRest, hub: MarketHub, settings: Settings, today: date) -> list[_SeedCall]:
    calls: list[_SeedCall] = []
    for code in settings.watch_codes:  # daily is KRX only; four-hour per market from 60-minute bars
        calls.append(("daily chart", lambda code=code: rest.daily_candles(code, today),
                      lambda bars, code=code: hub.seed_chart(code, Market.KRX, "1D", bars)))
        for m in (Market.KRX, Market.NXT):
            calls.append(("four-hour chart", lambda item=realtime_item(code, m): rest.hour_candles(item),
                          lambda bars, code=code, m=m: hub.seed_chart(code, m, "4H", bars)))
    for index in settings.index_codes:  # indices are KRX only
        calls.append(("index daily chart", lambda index=index: rest.index_daily(index, today),
                      lambda bars, index=index: hub.seed_chart(index, Market.KRX, "1D", bars)))
        calls.append(("index four-hour chart", lambda index=index: rest.index_hour(index),
                      lambda bars, index=index: hub.seed_chart(index, Market.KRX, "4H", bars)))
    return calls


async def _seed(rest: KiwoomRest, hub: MarketHub, settings: Settings) -> None:
    await _run_spaced(_minute_calls(rest, hub, settings.watch_codes))
    while True:
        await asyncio.sleep(SEED_GAP)
        await _run_spaced(_chart_calls(rest, hub, settings, _now().date()))
        await asyncio.sleep(RESEED_SECONDS)


def create_app(settings: Settings | None = None, *, hub: MarketHub | None = None,
               start_broker: bool = True, timing: StreamTiming | None = None) -> FastAPI:
    settings = settings or Settings.from_env(os.environ)
    hub = hub or MarketHub(_now, watch_codes=settings.watch_codes, index_codes=settings.index_codes)
    timing = timing or StreamTiming()
    provider = HubMarket(hub)

    def _broker_done(done: asyncio.Task) -> None:
        if done.cancelled():
            return
        error = done.exception()
        if error is not None:  # never restarted; surface it so clients see the broker as down
            log.error("broker task died: %s", type(error).__name__)
            hub.set_connection(False, "KIWOOM_WS_CLOSED")

    def _task_done(name: str) -> Callable[[asyncio.Task], None]:
        def done(task: asyncio.Task) -> None:
            if not task.cancelled() and task.exception() is not None:  # never restarted; others keep running
                log.error("%s task died: %s", name, type(task.exception()).__name__)
        return done

    @asynccontextmanager
    async def lifespan(app: FastAPI):
        tasks: list[asyncio.Task] = []
        client: httpx.AsyncClient | None = None
        if start_broker:
            client = httpx.AsyncClient(timeout=10.0)
            tokens = TokenProvider(client, settings, _now)
            rest = KiwoomRest(client, tokens, settings)
            realtime = KiwoomRealtime(settings, tokens, hub.apply, hub.set_connection,
                                      index_codes=settings.index_codes)
            hub.account = AccountService(rest, _now, last_price=hub.last_price,
                                         notify=lambda: hub._notify(ACCOUNT))
            hub.news = NewsService(client, settings, _now, lambda: hub._notify(NEWS))
            # Realtime starts first and never waits for REST seeding; a slow REST only delays history.
            broker = asyncio.create_task(realtime.run())
            broker.add_done_callback(_broker_done)
            tasks = [broker]
            for name, job in (("seeding", _seed(rest, hub, settings)), ("account", hub.account.run()),
                              ("news", hub.news.run())):
                task = asyncio.create_task(job)
                task.add_done_callback(_task_done(name))
                tasks.append(task)
        try:
            yield
        finally:
            for task in tasks:
                task.cancel()
            await asyncio.gather(*tasks, return_exceptions=True)
            if client is not None:
                await client.aclose()

    app = FastAPI(title="Spatial Trading Gateway", version="0.3.0", lifespan=lifespan)
    app.add_middleware(_LoggedTrustedHost, allowed_hosts=list(settings.allowed_hosts))

    @app.exception_handler(RequestValidationError)
    async def validation_error(request, error):
        # Do not reflect arbitrary request content into errors or telemetry.
        return JSONResponse(status_code=422, content={"error": "INVALID_REQUEST"})

    @app.exception_handler(Unauthorized)
    async def unauthorized(request, error):
        return JSONResponse(status_code=401, content={"error": "UNAUTHORIZED"})

    def require_pairing(x_pairing_token: Annotated[str | None, Header(alias="X-Pairing-Token")] = None) -> None:
        if not token_matches(settings.pairing_token, x_pairing_token):
            raise Unauthorized()

    paired = [Depends(require_pairing)]

    @app.get("/health")
    async def health():
        connected, _ = hub.connection
        return {"service": "spatial-trading", "status": "ready",
                "broker_connected": connected, "orders_enabled": False}

    @app.get("/v1/market/{code}/quote", response_model=Observation[Quote], dependencies=paired)
    async def quote(code: InstrumentCode, x_correlation_id: CorrelationHeader = None):
        return await provider.quote(code, str(x_correlation_id or uuid4()))

    @app.get("/v1/market/{code}/candles", response_model=Observation[CandleSeries], dependencies=paired)
    async def candles(code: InstrumentCode, x_correlation_id: CorrelationHeader = None):
        return await provider.candles(code, str(x_correlation_id or uuid4()))

    @app.get("/v1/market/{code}/orderbook", response_model=Observation[OrderBook], dependencies=paired)
    async def orderbook(code: InstrumentCode, x_correlation_id: CorrelationHeader = None):
        return await provider.orderbook(code, str(x_correlation_id or uuid4()))

    @app.websocket("/v1/stream")
    async def stream(websocket: WebSocket):
        await serve_stream(websocket, hub, settings.pairing_token, timing)

    return app


_app: FastAPI | None = None


def __getattr__(name: str):
    # `uvicorn spatial_gateway.app:app` resolves lazily so importing this module never needs credentials.
    global _app
    if name == "app":
        if _app is None:
            _app = create_app()
        return _app
    raise AttributeError(name)
