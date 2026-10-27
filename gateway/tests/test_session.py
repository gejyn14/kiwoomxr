from datetime import datetime, timedelta, timezone

import pytest

from spatial_gateway.kiwoom.values import KST
from spatial_gateway.market.session import Market, active_market, realtime_item


@pytest.mark.parametrize("hm,expected", [("08:00", Market.NXT), ("08:50", None), ("09:00", Market.KRX),
                                         ("15:29", Market.KRX), ("15:30", Market.NXT), ("19:59", Market.NXT),
                                         ("20:00", None), ("07:59", None)])
def test_active_market_by_kst_clock(hm, expected):
    hour, minute = map(int, hm.split(":"))
    assert active_market(datetime(2026, 10, 27, hour, minute, tzinfo=KST)) == expected  # Tuesday

def test_weekend_has_no_active_market():
    assert active_market(datetime(2026, 10, 31, 10, 0, tzinfo=KST)) is None  # Saturday

def test_utc_input_is_converted_to_kst():
    assert active_market(datetime(2026, 10, 27, 6, 31, tzinfo=timezone.utc)) == Market.NXT  # 15:31 KST

def test_naive_datetime_is_rejected():
    with pytest.raises(ValueError):
        active_market(datetime(2026, 10, 27, 10, 0))

def test_realtime_item_codes():
    assert realtime_item("005930", Market.NXT) == "005930_NX" and realtime_item("005930", Market.KRX) == "005930"
