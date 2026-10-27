import asyncio
import logging
import os
from contextlib import asynccontextmanager
from datetime import datetime
from typing import Annotated
from uuid import UUID, uuid4

import httpx
from fastapi import Depends, FastAPI, Header, Path, WebSocket
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from starlette.middleware.trustedhost import TrustedHostMiddleware

from .capabilities import HubMarket
from .kiwoom.auth import TokenProvider
from .kiwoom.realtime import KiwoomRealtime
from .kiwoom.rest import KiwoomRest
from .kiwoom.values import KST
from .market.hub import MarketHub
from .market.session import Market, realtime_item
from .models import CandleSeries, Observation, OrderBook, Quote
from .settings import Settings
from .stream import StreamTiming, serve_stream, token_matches

log = logging.getLogger(__name__)

InstrumentCode = Annotated[str, Path(pattern=r"^[0-9]{6}$")]
CorrelationHeader = Annotated[UUID | None, Header(alias="X-Correlation-ID")]


class Unauthorized(Exception):
    pass


def _now() -> datetime:
    return datetime.now(KST)


async def _seed(rest: KiwoomRest, hub: MarketHub, codes: tuple[str, ...]) -> None:
    for code in codes:
        for market in (Market.KRX, Market.NXT):
            try:
                bars = await rest.minute_candles(realtime_item(code, market))
                hub.seed_candles(code, market, bars)
            except asyncio.CancelledError:
                raise
            except Exception as error:  # series stays LOADING; never stop startup
                log.warning("minute candle seed failed: %s", type(error).__name__)


def create_app(settings: Settings | None = None, *, hub: MarketHub | None = None,
               start_broker: bool = True, timing: StreamTiming | None = None) -> FastAPI:
    settings = settings or Settings.from_env(os.environ)
    hub = hub or MarketHub(_now, watch_codes=settings.watch_codes)
    timing = timing or StreamTiming()
    provider = HubMarket(hub)

    def _broker_done(done: asyncio.Task) -> None:
        if done.cancelled():
            return
        error = done.exception()
        if error is not None:  # never restarted; surface it so clients see the broker as down
            log.error("broker task died: %s", type(error).__name__)
            hub.set_connection(False, "KIWOOM_WS_CLOSED")

    @asynccontextmanager
    async def lifespan(app: FastAPI):
        task: asyncio.Task | None = None
        client: httpx.AsyncClient | None = None
        if start_broker:
            client = httpx.AsyncClient(timeout=10.0)
            tokens = TokenProvider(client, settings, _now)
            rest = KiwoomRest(client, tokens, settings)
            realtime = KiwoomRealtime(settings, tokens, hub.apply, hub.set_connection)

            async def broker() -> None:
                await _seed(rest, hub, settings.watch_codes)
                await realtime.run()

            task = asyncio.create_task(broker())
            task.add_done_callback(_broker_done)
        try:
            yield
        finally:
            if task is not None:
                task.cancel()
                await asyncio.gather(task, return_exceptions=True)
            if client is not None:
                await client.aclose()

    app = FastAPI(title="Spatial Trading Gateway", version="0.3.0", lifespan=lifespan)
    app.add_middleware(TrustedHostMiddleware, allowed_hosts=[*settings.allowed_hosts, "testserver"])

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
