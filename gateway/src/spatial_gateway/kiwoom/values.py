"""Pure parsers for Kiwoom REST/WebSocket value strings.

Financial values never pass through binary float: every number goes through Decimal(str).
"""

import re
from datetime import date, datetime, timedelta, timezone
from decimal import Decimal, InvalidOperation

KST = timezone(timedelta(hours=9))

_INTEGER = re.compile(r"[+-]?[0-9]+")
_HHMMSS = re.compile(r"[0-9]{6}")
_YYYYMMDDHHMMSS = re.compile(r"[0-9]{14}")
_MAX_DIGITS = 19  # longer digit strings are never valid quotes; also keeps int() off huge inputs


def _too_long(text: str) -> bool:
    return sum(ch.isdigit() for ch in text) > _MAX_DIGITS


def _decimal(text: str) -> Decimal | None:
    try:
        value = Decimal(text)
    except InvalidOperation:
        return None
    return value if value.is_finite() else None


def price(raw: str | None) -> Decimal | None:
    """Absolute price. A leading '+'/'-' is a direction marker, not part of the value."""
    if raw is None:
        return None
    text = raw.strip().lstrip("+-")
    if not text or _too_long(text):
        return None
    value = _decimal(text)
    if value is None or value == 0:
        return None
    return value


def signed(raw: str | None) -> Decimal | None:
    """Signed value; the sign is meaningful and is preserved."""
    if raw is None:
        return None
    text = raw.strip()
    if not text:
        return None
    return _decimal(text)


def quantity(raw: str | None) -> int | None:
    """Absolute integer quantity. Non-integer strings yield None."""
    if raw is None:
        return None
    text = raw.strip()
    if not _INTEGER.fullmatch(text) or _too_long(text):
        return None
    return abs(int(text))


def kst_time_today(hhmmss: str, today: date) -> datetime | None:
    """Combine an HHMMSS string with a date, as an explicit KST datetime."""
    if not _HHMMSS.fullmatch(hhmmss):
        return None
    try:
        parsed = datetime.strptime(hhmmss, "%H%M%S")
    except ValueError:
        return None
    return datetime(today.year, today.month, today.day, parsed.hour, parsed.minute, parsed.second, tzinfo=KST)


def kst_datetime(yyyymmddhhmmss: str) -> datetime | None:
    """Parse a YYYYMMDDHHMMSS string as an explicit KST datetime."""
    if not _YYYYMMDDHHMMSS.fullmatch(yyyymmddhhmmss):
        return None
    try:
        return datetime.strptime(yyyymmddhhmmss, "%Y%m%d%H%M%S").replace(tzinfo=KST)
    except ValueError:
        return None


_INDEX_SCALE = Decimal(100)


def index_value(raw: str | None, *, absolute: bool) -> Decimal | None:
    """Kiwoom index level or change. Integer strings are 100x ("563834" = 5638.34); strings with a
    decimal point are already scaled. Shared by REST and realtime so they agree."""
    if raw is None:
        return None
    value = price(raw) if absolute else signed(raw)
    if value is None:
        return None
    return value if "." in raw else value / _INDEX_SCALE
