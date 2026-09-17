from uuid import UUID, uuid4

from fastapi import FastAPI, Header, Path
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from starlette.middleware.trustedhost import TrustedHostMiddleware
from typing import Annotated

from .capabilities import MarketCapabilities, SpecificationGatedMarket
from .models import CandleSeries, Observation, OrderBook, Quote

InstrumentCode = Annotated[str, Path(pattern=r"^[0-9]{6}$")]
CorrelationHeader = Annotated[UUID | None, Header(alias="X-Correlation-ID")]


def create_app(market: MarketCapabilities | None = None) -> FastAPI:
    app = FastAPI(title="Spatial Trading Gateway", version="0.2.0")
    app.add_middleware(TrustedHostMiddleware, allowed_hosts=["127.0.0.1", "localhost", "testserver"])
    provider = market or SpecificationGatedMarket()

    @app.exception_handler(RequestValidationError)
    async def validation_error(request, error):
        # Do not reflect arbitrary request content into errors or telemetry.
        return JSONResponse(status_code=422, content={"error": "INVALID_REQUEST"})

    @app.get("/health")
    async def health():
        return {"service": "spatial-trading", "status": "ready", "scope": "loopback-development",
                "orders_enabled": False, "broker_connectivity": "NOT_VERIFIED"}

    @app.get("/v1/market/{code}/quote", response_model=Observation[Quote])
    async def quote(code: InstrumentCode, x_correlation_id: CorrelationHeader = None):
        return await provider.quote(code, str(x_correlation_id or uuid4()))

    @app.get("/v1/market/{code}/candles", response_model=Observation[CandleSeries])
    async def candles(code: InstrumentCode, x_correlation_id: CorrelationHeader = None):
        return await provider.candles(code, str(x_correlation_id or uuid4()))

    @app.get("/v1/market/{code}/orderbook", response_model=Observation[OrderBook])
    async def orderbook(code: InstrumentCode, x_correlation_id: CorrelationHeader = None):
        return await provider.orderbook(code, str(x_correlation_id or uuid4()))

    return app


app = create_app()
