"""KRX/NXT session clock. Pure functions: no I/O, no holiday calendar.

Windows are half-open: start inclusive, end exclusive.
"""

from datetime import datetime, time
from enum import StrEnum

from spatial_gateway.kiwoom.values import KST


class Market(StrEnum):
    KRX = "KRX"
    NXT = "NXT"


_NXT_PRE = (time(8, 0), time(8, 50))
_KRX = (time(9, 0), time(15, 30))
_NXT_POST = (time(15, 30), time(20, 0))


def _in(window: tuple[time, time], clock: time) -> bool:
    return window[0] <= clock < window[1]


def active_market(now: datetime) -> Market | None:
    """Market whose session is open at `now` (KST), or None outside sessions and on weekends."""
    if now.tzinfo is None:
        raise ValueError("active_market requires a timezone-aware datetime")
    local = now.astimezone(KST)
    if local.weekday() >= 5:
        return None
    clock = local.timetz().replace(tzinfo=None)
    if _in(_KRX, clock):
        return Market.KRX
    if _in(_NXT_PRE, clock) or _in(_NXT_POST, clock):
        return Market.NXT
    return None


def realtime_item(code: str, market: Market) -> str:
    """Kiwoom realtime item code: NXT quotes carry the `_NX` suffix, KRX is the bare code."""
    return f"{code}_NX" if market is Market.NXT else code
