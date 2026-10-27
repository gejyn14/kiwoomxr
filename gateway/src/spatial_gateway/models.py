from datetime import datetime
from decimal import Decimal
from enum import StrEnum
from typing import Generic, Literal, TypeVar

from pydantic import BaseModel, ConfigDict, Field, field_serializer, field_validator, model_validator


ACCOUNT = "ACCOUNT"
NEWS = "NEWS"
SUBJECT_PATTERN = r"^([0-9]{6}|[0-9]{3}|ACCOUNT|NEWS)$"


class Contract(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)


class DataState(StrEnum):
    LOADING = "LOADING"
    LIVE = "LIVE"
    DELAYED = "DELAYED"
    STALE = "STALE"
    DISCONNECTED = "DISCONNECTED"
    UNAVAILABLE = "UNAVAILABLE"
    ERROR = "ERROR"


class Origin(StrEnum):
    KIWOOM = "KIWOOM"
    SYNTHETIC = "SYNTHETIC"
    NEWS = "NEWS"


T = TypeVar("T")


class Observation(Contract, Generic[T]):
    instrument_code: str = Field(pattern=SUBJECT_PATTERN)
    state: DataState
    origin: Origin
    value: T | None = None
    received_at: datetime | None = None
    source_at: datetime | None = None
    reason: str | None = None
    correlation_id: str

    @model_validator(mode="after")
    def validate_evidence(self):
        if self.state in (DataState.LIVE, DataState.DELAYED) and (self.value is None or self.received_at is None):
            raise ValueError("Available observations require data and receipt time")
        if self.state == DataState.LIVE and (self.origin == Origin.SYNTHETIC or self.source_at is None):
            raise ValueError("Live requires authoritative data with verified source time")
        for value in (self.received_at, self.source_at):
            if value is not None and (value.tzinfo is None or value.utcoffset() is None):
                raise ValueError("Observation times must have explicit timezone")
        return self


class Quote(Contract):
    last_price: Decimal | None = Field(default=None, gt=0, max_digits=28, decimal_places=8)
    change: Decimal | None = Field(default=None, max_digits=28, decimal_places=8)
    change_percent: Decimal | None = Field(default=None, max_digits=28, decimal_places=8)
    volume: int | None = Field(default=None, ge=0, le=9223372036854775807, strict=True)

    @field_serializer("last_price", "change", "change_percent")
    def decimal_wire(self, value):
        return None if value is None else format(value, "f")

    @field_serializer("volume")
    def quantity_wire(self, value):
        return None if value is None else str(value)

    @field_validator("last_price", "change", "change_percent", mode="before")
    @classmethod
    def no_binary_floats(cls, value):
        if isinstance(value, (float, bool)):
            raise ValueError("Financial decimals must not pass through binary floats")
        return value


class Candle(Contract):
    period_key: str = Field(min_length=1)
    open: Decimal = Field(gt=0, max_digits=28, decimal_places=8)
    high: Decimal = Field(gt=0, max_digits=28, decimal_places=8)
    low: Decimal = Field(gt=0, max_digits=28, decimal_places=8)
    close: Decimal = Field(gt=0, max_digits=28, decimal_places=8)
    volume: int | None = Field(default=None, ge=0, le=9223372036854775807, strict=True)
    complete: bool | None = None

    @field_serializer("open", "high", "low", "close")
    def decimal_wire(self, value):
        return format(value, "f")

    @field_serializer("volume")
    def quantity_wire(self, value):
        return None if value is None else str(value)

    @field_validator("open", "high", "low", "close", mode="before")
    @classmethod
    def no_binary_floats(cls, value):
        return Quote.no_binary_floats(value)

    @model_validator(mode="after")
    def check_bounds(self):
        if not self.low <= self.open <= self.high or not self.low <= self.close <= self.high:
            raise ValueError("Invalid OHLC bounds")
        return self


class CandleSeries(Contract):
    interval: str
    adjustment: str
    candles: tuple[Candle, ...]
    complete_history: bool = False

    @model_validator(mode="after")
    def check_order(self):
        keys = [bar.period_key for bar in self.candles]
        if keys != sorted(set(keys)):
            raise ValueError("Candles must be unique and ordered")
        return self


class BookLevel(Contract):
    rank: int = Field(ge=1, le=10, strict=True)
    price: Decimal | None = Field(default=None, gt=0, max_digits=28, decimal_places=8)
    quantity: int | None = Field(default=None, ge=0, le=9223372036854775807, strict=True)

    @field_serializer("price")
    def decimal_wire(self, value):
        return None if value is None else format(value, "f")

    @field_serializer("quantity")
    def quantity_wire(self, value):
        return None if value is None else str(value)

    @field_validator("price", mode="before")
    @classmethod
    def no_binary_floats(cls, value):
        return Quote.no_binary_floats(value)


class OrderBook(Contract):
    asks: tuple[BookLevel, ...]
    bids: tuple[BookLevel, ...]

    @model_validator(mode="after")
    def check_ranks(self):
        for side in (self.asks, self.bids):
            if [level.rank for level in side] != list(range(1, len(side) + 1)):
                raise ValueError("Explicit consecutive book ranks required")
        return self


_MONEY = dict(default=None, max_digits=28, decimal_places=8)


class Holding(Contract):
    code: str = Field(pattern=r"^[0-9]{6}$")
    name: str = Field(min_length=1, pattern=r"\S")  # Unity rejects a blank name
    quantity: int = Field(ge=0, le=9223372036854775807, strict=True)
    average_price: Decimal | None = Field(**_MONEY)
    current_price: Decimal | None = Field(**_MONEY)
    profit: Decimal | None = Field(**_MONEY)
    profit_rate: Decimal | None = Field(**_MONEY)

    @field_serializer("average_price", "current_price", "profit", "profit_rate")
    def decimal_wire(self, value):
        return None if value is None else format(value, "f")

    @field_serializer("quantity")
    def quantity_wire(self, value):
        return str(value)

    @field_validator("average_price", "current_price", "profit", "profit_rate", mode="before")
    @classmethod
    def no_binary_floats(cls, value):
        return Quote.no_binary_floats(value)


class Balance(Contract):
    account: str = Field(pattern=r"^[0-9]{3}\*+[0-9]{2}$")
    total_evaluation: Decimal | None = Field(**_MONEY)
    total_profit: Decimal | None = Field(**_MONEY)
    total_profit_rate: Decimal | None = Field(**_MONEY)
    orderable_cash: Decimal | None = Field(**_MONEY)
    holdings: tuple[Holding, ...]

    @field_serializer("total_evaluation", "total_profit", "total_profit_rate", "orderable_cash")
    def decimal_wire(self, value):
        return None if value is None else format(value, "f")

    @field_validator("total_evaluation", "total_profit", "total_profit_rate", "orderable_cash", mode="before")
    @classmethod
    def no_binary_floats(cls, value):
        return Quote.no_binary_floats(value)


class NewsItem(Contract):
    kind: Literal["NEWS", "FILING"]
    source: str = Field(max_length=40)
    title: str = Field(min_length=1, max_length=200)
    published_at: datetime
    mentions: tuple[str, ...]
    filing_type: str | None = None

    @field_validator("published_at")
    @classmethod
    def require_timezone(cls, value):
        if value.tzinfo is None or value.utcoffset() is None:
            raise ValueError("News times must have explicit timezone")
        return value


class NewsFeed(Contract):
    items: tuple[NewsItem, ...] = Field(max_length=40)

    @model_validator(mode="after")
    def check_order(self):
        times = [item.published_at for item in self.items]
        if times != sorted(times, reverse=True):
            raise ValueError("News must be newest first")
        return self
