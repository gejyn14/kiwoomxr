from typing import Protocol

from .market.hub import MarketHub
from .models import CandleSeries, Observation, OrderBook, Quote


class MarketCapabilities(Protocol):
    async def quote(self, code: str, correlation_id: str) -> Observation[Quote]: ...
    async def candles(self, code: str, correlation_id: str) -> Observation[CandleSeries]: ...
    async def orderbook(self, code: str, correlation_id: str) -> Observation[OrderBook]: ...


class HubMarket:
    """Serves the live hub's per-market observations through the capability protocol."""

    def __init__(self, hub: MarketHub):
        self._hub = hub

    async def quote(self, code: str, correlation_id: str) -> Observation[Quote]:
        return self._hub.observations(code, correlation_id).quote

    async def candles(self, code: str, correlation_id: str) -> Observation[CandleSeries]:
        return self._hub.observations(code, correlation_id).candles

    async def orderbook(self, code: str, correlation_id: str) -> Observation[OrderBook]:
        return self._hub.observations(code, correlation_id).orderbook
