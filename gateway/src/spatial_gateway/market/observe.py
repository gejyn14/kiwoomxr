"""Shared decision table turning a value and link state into an Observation."""

from datetime import datetime

from spatial_gateway.models import DataState, Observation, Origin


def observe(model, code: str, cid: str, *, value, received: datetime | None, source: datetime | None,
            connected: bool, reason: str | None, closed: bool, watched: bool,
            origin: Origin = Origin.KIWOOM) -> Observation:
    """NOT_SUBSCRIBED -> DISCONNECTED -> LOADING/UNAVAILABLE -> DELAYED MARKET_CLOSED -> LIVE."""

    def build(state: DataState, why: str | None, val=None, rec=None, src=None):
        return Observation[model](
            instrument_code=code, state=state, origin=origin, value=val,
            received_at=rec, source_at=src, reason=why, correlation_id=cid)

    if not watched:
        return build(DataState.UNAVAILABLE, "NOT_SUBSCRIBED")
    if not connected:
        return build(DataState.DISCONNECTED, reason, value, received, source)
    if value is None:
        return build(DataState.UNAVAILABLE, "MARKET_CLOSED") if closed else build(DataState.LOADING, None)
    if closed:
        return build(DataState.DELAYED, "MARKET_CLOSED", value, received, source)
    return build(DataState.LIVE, None, value, received, source)
