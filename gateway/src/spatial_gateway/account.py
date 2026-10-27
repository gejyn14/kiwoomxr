"""Read-only account service: masked balance and orderable cash. No order path exists here."""

import asyncio
import logging
from collections.abc import Awaitable, Callable
from datetime import datetime, timedelta
from decimal import Decimal

from spatial_gateway.kiwoom.rest import KiwoomApiError, KiwoomRest, mask_account
from spatial_gateway.market.observe import observe
from spatial_gateway.market.session import active_market
from spatial_gateway.models import ACCOUNT, Balance, Observation

__all__ = ["AccountService", "mask_account"]

log = logging.getLogger(__name__)

POLL_SECONDS = 60
SIGNAL_DELAY = timedelta(seconds=1)
MIN_SPACING = timedelta(seconds=2)
ORDERABLE_CODE = "005930"
FAILED = "ACCOUNT_QUERY_FAILED"


class AccountService:
    def __init__(
        self,
        rest: KiwoomRest,
        clock: Callable[[], datetime],
        *,
        last_price: Callable[[str], Decimal | None],
        notify: Callable[[], None],
        sleep: Callable[[float], Awaitable[None]] = asyncio.sleep,
    ) -> None:
        self._rest = rest
        self._clock = clock
        self._last_price = last_price
        self._notify = notify
        self._sleep = sleep
        self._masked: str | None = None
        self._value: Balance | None = None
        self._received: datetime | None = None
        self._connected = True
        self._reason: str | None = None
        self._last_balance_call: datetime | None = None
        self._signal_task: asyncio.Task | None = None
        self._lock = asyncio.Lock()

    async def run(self) -> None:
        while True:
            await self.refresh()
            await self._sleep(POLL_SECONDS)

    def balance_changed(self) -> None:
        """Realtime balance signal. Bursts coalesce into one query ~1s later, at least 2s after the last."""
        if self._signal_task is None or self._signal_task.done():
            self._signal_task = asyncio.ensure_future(self._signalled_refresh())

    async def wait_idle(self) -> None:
        task = self._signal_task
        if task is not None:
            await asyncio.shield(task)

    async def _signalled_refresh(self) -> None:
        await self._sleep(SIGNAL_DELAY.total_seconds())
        last = self._last_balance_call
        if last is not None:
            wait = (last + MIN_SPACING - self._clock()).total_seconds()
            if wait > 0:
                await self._sleep(wait)
        await self.refresh()

    async def refresh(self) -> None:
        async with self._lock:
            try:
                if self._masked is None:
                    self._masked = mask_account(await self._rest.account_number())
                self._last_balance_call = self._clock()
                raw = await self._rest.balance()
                cash = await self._orderable_cash()
                value = Balance(
                    account=self._masked,
                    total_evaluation=raw.total_evaluation,
                    total_profit=raw.total_profit,
                    total_profit_rate=raw.total_profit_rate,
                    orderable_cash=cash,
                    holdings=raw.holdings,
                )
            except Exception as error:  # report code and type only; messages may carry account data
                code = error.code if isinstance(error, KiwoomApiError) else None
                log.warning("account query failed code=%s type=%s", code, type(error).__name__)
                self._connected = False
                self._reason = FAILED
            else:
                self._value = value
                self._received = self._clock()
                self._connected = True
                self._reason = None
        self._notify()

    async def _orderable_cash(self) -> Decimal | None:
        price = self._last_price(ORDERABLE_CODE)
        if price is None:
            return None
        try:
            return await self._rest.orderable_cash(ORDERABLE_CODE, price)
        except KiwoomApiError as error:
            log.warning("orderable cash query failed code=%s type=%s", error.code, type(error).__name__)
            return None

    def observation(self, cid: str) -> Observation[Balance]:
        now = self._clock()
        return observe(
            Balance, ACCOUNT, cid,
            value=self._value, received=self._received, source=self._received,
            connected=self._connected, reason=self._reason,
            closed=active_market(now) is None, watched=True,
        )
