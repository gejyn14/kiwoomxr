import asyncio
from collections.abc import Callable
from datetime import datetime, timedelta, timezone

import httpx

from spatial_gateway.settings import Settings

KST = timezone(timedelta(hours=9))
_REFRESH_MARGIN = timedelta(minutes=5)
_DEFAULT_LIFETIME = timedelta(hours=1)
_REISSUE_INTERVAL = timedelta(minutes=1)


class KiwoomAuthError(Exception):
    """Token issuance failed. Messages never carry keys or tokens."""


def _parse_expiry(raw: object, issued_at: datetime) -> datetime:
    if isinstance(raw, str):
        try:
            return datetime.strptime(raw, "%Y%m%d%H%M%S").replace(tzinfo=KST)
        except ValueError:
            pass
    return issued_at + _DEFAULT_LIFETIME


class TokenProvider:
    def __init__(
        self, client: httpx.AsyncClient, settings: Settings, clock: Callable[[], datetime]
    ) -> None:
        self._client = client
        self._settings = settings
        self._clock = clock
        self._lock = asyncio.Lock()
        self._token: str | None = None
        self._expires_at: datetime | None = None
        self._last_invalidated: datetime | None = None

    async def token(self) -> str:
        async with self._lock:
            now = self._clock()
            if (
                self._token is not None
                and self._expires_at is not None
                and now < self._expires_at - _REFRESH_MARGIN
            ):
                return self._token
            return await self._issue(now)

    async def invalidate(self) -> bool:
        async with self._lock:
            now = self._clock()
            if (
                self._last_invalidated is not None
                and now - self._last_invalidated < _REISSUE_INTERVAL
            ):
                return False
            self._last_invalidated = now
            self._token = None
            self._expires_at = None
            return True

    async def _issue(self, now: datetime) -> str:
        try:
            response = await self._client.post(
                f"{self._settings.rest_origin}/oauth2/token",
                headers={
                    "content-type": "application/json;charset=UTF-8",
                    "api-id": "au10001",
                },
                json={
                    "grant_type": "client_credentials",
                    "appkey": self._settings.appkey,
                    "secretkey": self._settings.secretkey,
                },
            )
        except httpx.HTTPError as error:
            raise KiwoomAuthError(f"token request failed: {type(error).__name__}") from None
        try:
            body = response.json()
        except ValueError:
            raise KiwoomAuthError(f"token response not JSON (http {response.status_code})") from None
        if not isinstance(body, dict):
            raise KiwoomAuthError(f"token response malformed (http {response.status_code})")
        code = body.get("return_code")
        token = body.get("token")
        if response.status_code != 200 or code != 0 or not isinstance(token, str) or not token:
            raise KiwoomAuthError(
                f"token issuance rejected (http {response.status_code}, return_code {code})"
            )
        self._token = token
        self._expires_at = _parse_expiry(body.get("expires_dt"), now)
        return token
