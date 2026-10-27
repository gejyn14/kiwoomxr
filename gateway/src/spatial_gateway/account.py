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
REST_GAP = 0.25
ORDERABLE_CODE = "005930"
FAILED = "ACCOUNT_QUERY_FAILED"
# A dead poll task must not leave the balance LIVE: past three polls without a success, the balance is overdue.
OVERDUE_AFTER = timedelta(seconds=3 * POLL_SECONDS)
OVERDUE = "ACCOUNT_REFRESH_OVERDUE"


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
        self._pending = False
        self._lock = asyncio.Lock()

    async def run(self) -> None:
        while True:
            await self.refresh()
            await self._sleep(POLL_SECONDS)

    def balance_changed(self) -> None:
        """Realtime balance signal. Bursts coalesce into one query ~1s later, at least 2s after the last.
        A signal that lands while a query is in flight leaves `_pending` set, so another query follows."""
        self._pending = True
        if self._signal_task is None or self._signal_task.done():
            self._signal_task = asyncio.ensure_future(self._signalled_refresh())

    async def wait_idle(self) -> None:
        task = self._signal_task
        if task is not None:
            await asyncio.shield(task)

    async def _signalled_refresh(self) -> None:
        while True:
            await self._sleep(SIGNAL_DELAY.total_seconds())
            if not await self.refresh() or not self._pending:
                return  # a failed query is retried by the 60s poll, not by the signal loop

    async def refresh(self) -> bool:
        async with self._lock:
            try:
                if self._masked is None:
                    self._masked = mask_account(await self._rest.account_number())
                    await self._sleep(REST_GAP)
                if self._last_balance_call is not None:
                    wait = (self._last_balance_call + MIN_SPACING - self._clock()).total_seconds()
                    if wait > 0:
                        await self._sleep(wait)
                self._pending = False
                self._last_balance_call = self._clock()
                raw = await self._rest.balance()
                await self._sleep(REST_GAP)
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
                self._pending = False
                ok = False
            else:
                self._value = value
                self._received = self._clock()
                self._connected = True
                self._reason = None
                ok = True
        self._notify()
        return ok

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
        connected, reason = self._connected, self._reason
        if connected and self._received is not None and now - self._received > OVERDUE_AFTER:
            connected, reason = False, OVERDUE
        return observe(
            Balance, ACCOUNT, cid,
            value=self._value, received=self._received, source=self._received,
            connected=connected, reason=reason,
            closed=active_market(now) is None, watched=True,
        )
