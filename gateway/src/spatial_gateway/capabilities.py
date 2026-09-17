from typing import Protocol

from .models import CandleSeries, DataState, Observation, OrderBook, Origin, Quote


class MarketCapabilities(Protocol):
    async def quote(self, code: str, correlation_id: str) -> Observation[Quote]: ...
    async def candles(self, code: str, correlation_id: str) -> Observation[CandleSeries]: ...
    async def orderbook(self, code: str, correlation_id: str) -> Observation[OrderBook]: ...


class SpecificationGatedMarket:
    """Executable missing-spec behavior, not a simulated successful integration.

    K-01 blocks authentication in the repository-supplied workbook. No broker
    transport is constructed and no keys are read while that evidence is absent.
    """

    @staticmethod
    def unavailable(code: str, correlation_id: str, model):
        return Observation[model](
            instrument_code=code, state=DataState.UNAVAILABLE, origin=Origin.KIWOOM,
            reason="BLOCKED_BY_MISSING_SPEC:K-01", correlation_id=correlation_id,
        )

    async def quote(self, code: str, correlation_id: str) -> Observation[Quote]:
        return self.unavailable(code, correlation_id, Quote)

    async def candles(self, code: str, correlation_id: str) -> Observation[CandleSeries]:
        return self.unavailable(code, correlation_id, CandleSeries)

    async def orderbook(self, code: str, correlation_id: str) -> Observation[OrderBook]:
        return self.unavailable(code, correlation_id, OrderBook)
