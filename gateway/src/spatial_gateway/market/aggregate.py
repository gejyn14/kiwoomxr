"""Fold 60-minute bars into session-anchored four-hour candles. Pure functions: no I/O."""

from collections.abc import Sequence
from datetime import datetime, time

from spatial_gateway.kiwoom.rest import Bar
from spatial_gateway.kiwoom.values import KST
from spatial_gateway.market.session import Market

# Bucket (start, end, end_inclusive) windows per market; half-open and cut at session end, except that the KRX
# afternoon bucket keeps the 15:30:00 closing-auction print, so the last four-hour close matches the daily close.
_BUCKETS: dict[Market, tuple[tuple[time, time, bool], ...]] = {
    Market.KRX: ((time(9, 0), time(13, 0), False), (time(13, 0), time(15, 30), True)),
    Market.NXT: (
        (time(8, 0), time(8, 50), False),
        (time(15, 30), time(19, 30), False),
        (time(19, 30), time(20, 0), False),
    ),
}


def bucket_start(at: datetime, market: Market) -> datetime | None:
    """Start of the four-hour bucket containing `at`, or None outside the market's session."""
    local = at.astimezone(KST) if at.tzinfo else at.replace(tzinfo=KST)
    clock = local.timetz().replace(tzinfo=None)
    for start, end, inclusive in _BUCKETS[market]:
        if start <= clock < end or (inclusive and clock == end):
            return local.replace(
                hour=start.hour, minute=start.minute, second=0, microsecond=0
            )
    return None


def four_hour(bars: Sequence[Bar], market: Market) -> list[Bar]:
    """Merge 60-minute bars into session-anchored buckets, ascending; out-of-session bars dropped."""
    groups: dict[datetime, list[Bar]] = {}
    for bar in sorted(bars, key=lambda b: b.start):
        key = bucket_start(bar.start, market)
        if key is not None:
            groups.setdefault(key, []).append(bar)
    return [
        Bar(
            start=key,
            open=members[0].open,
            high=max(b.high for b in members),
            low=min(b.low for b in members),
            close=members[-1].close,
            volume=sum(b.volume for b in members),
        )
        for key, members in sorted(groups.items())
    ]
