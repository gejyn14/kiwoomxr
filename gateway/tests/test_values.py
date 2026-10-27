from datetime import date, datetime
from decimal import Decimal

import pytest

from spatial_gateway.kiwoom.values import KST, kst_datetime, kst_time_today, price, quantity, signed


@pytest.mark.parametrize("raw,expected", [("-78800", Decimal("78800")), ("+156600", Decimal("156600")),
                                          ("78900", Decimal("78900")), ("", None), ("0", None), ("-", None),
                                          (None, None), ("12a", None)])
def test_price_strips_direction_sign(raw, expected): assert price(raw) == expected

@pytest.mark.parametrize("raw,expected", [("-600", Decimal("-600")), ("+1.25", Decimal("1.25")), ("0", Decimal("0")), ("", None)])
def test_signed_keeps_sign(raw, expected): assert signed(raw) == expected

def test_quantity_parses_integer_strings_only():
    assert quantity("7913") == 7913 and quantity("-120") == 120 and quantity("1.5") is None

def test_times_are_explicit_kst():
    assert kst_datetime("20250917132000") == datetime(2025, 9, 17, 13, 20, tzinfo=KST)
    assert kst_time_today("153001", date(2026, 10, 27)) == datetime(2026, 10, 27, 15, 30, 1, tzinfo=KST)
    assert kst_time_today("99", date(2026, 10, 27)) is None

def test_digit_strings_longer_than_19_digits_are_rejected():
    assert quantity("9" * 19) == int("9" * 19) and price("9" * 19) == Decimal("9" * 19)
    assert quantity("1" * 20) is None and quantity("-" + "1" * 5000) is None
    assert price("+" + "1" * 20) is None and price("1" * 5000) is None
