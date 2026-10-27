from datetime import datetime
from decimal import Decimal

from spatial_gateway.kiwoom.rest import Bar
from spatial_gateway.kiwoom.values import KST
from spatial_gateway.market.aggregate import bucket_start, four_hour
from spatial_gateway.market.session import Market


def at(day: int, hour: int, minute: int = 0) -> datetime:
    return datetime(2026, 10, day, hour, minute, tzinfo=KST)


def bar(start: datetime, o: int, h: int, low: int, c: int, v: int) -> Bar:
    return Bar(start, Decimal(o), Decimal(h), Decimal(low), Decimal(c), v)


def test_krx_hours_fold_into_two_buckets_per_day():
    bars = [bar(at(28, 9 + i, 0), 100 + i, 110 + i, 90 + i, 101 + i, 10) for i in range(7)]
    out = four_hour(bars, Market.KRX)
    assert [b.start for b in out] == [at(28, 9), at(28, 13)]
    first = out[0]
    assert first.open == Decimal(100)
    assert first.close == Decimal(104)  # 12:00 bar close
    assert first.high == Decimal(113)
    assert first.low == Decimal(90)
    assert first.volume == 40
    assert out[1].volume == 30


def test_nxt_post_session_buckets_start_at_1530():
    bars = [bar(at(28, 15, 30), 1, 2, 1, 2, 5)]
    bars += [bar(at(28, h, 30), 1, 2, 1, 2, 5) for h in (16, 17, 18, 19)]
    out = four_hour(bars, Market.NXT)
    assert [b.start for b in out] == [at(28, 15, 30), at(28, 19, 30)]


def test_bars_outside_session_are_dropped_and_markets_never_mix():
    bars = [bar(at(28, 8), 1, 2, 1, 2, 5), bar(at(28, 9), 1, 2, 1, 2, 5)]
    out = four_hour(bars, Market.KRX)
    assert [b.start for b in out] == [at(28, 9)]
    assert bucket_start(at(28, 8, 10), Market.KRX) is None
    assert bucket_start(at(28, 8, 10), Market.NXT) == at(28, 8)
    assert bucket_start(at(28, 9), Market.NXT) is None
    assert bucket_start(at(28, 15, 31), Market.KRX) is None  # 15:30:00 itself is the closing auction


def test_buckets_reset_each_day():
    bars = [bar(at(28, 9), 1, 2, 1, 2, 5), bar(at(27, 13), 1, 2, 1, 2, 5)]
    out = four_hour(bars, Market.KRX)
    assert [b.start for b in out] == [at(27, 13), at(28, 9)]


def test_krx_closing_auction_print_at_1530_lands_in_the_afternoon_bucket():
    assert bucket_start(at(28, 15, 30), Market.KRX) == at(28, 13)
    assert bucket_start(datetime(2026, 10, 28, 15, 30, 1, tzinfo=KST), Market.KRX) is None
    bars = [bar(at(28, 15, 0), 100, 101, 99, 100, 5), bar(at(28, 15, 30), 100, 102, 100, 102, 7)]
    (afternoon,) = four_hour(bars, Market.KRX)
    assert afternoon.start == at(28, 13) and afternoon.close == Decimal(102) and afternoon.volume == 12
    # NXT keeps its own 15:30 bucket start.
    assert bucket_start(at(28, 15, 30), Market.NXT) == at(28, 15, 30)
