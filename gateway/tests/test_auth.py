import json
from datetime import datetime, timedelta, timezone

import httpx
import pytest

from spatial_gateway.kiwoom.auth import KiwoomAuthError, TokenProvider
from spatial_gateway.settings import Settings

KST = timezone(timedelta(hours=9))
APPKEY = "APPKEY-VALUE-xyz"
SECRET = "SECRET-VALUE-abc"


def make_settings(**overrides) -> Settings:
    values = dict(
        appkey=APPKEY,
        secretkey=SECRET,
        pairing_token="p" * 32,
        watch_codes=("005930",),
        allowed_hosts=("127.0.0.1",),
    )
    values.update(overrides)
    return Settings(**values)


class Clock:
    def __init__(self, now: datetime):
        self.now = now

    def __call__(self) -> datetime:
        return self.now


def make_provider(handler, clock):
    client = httpx.AsyncClient(transport=httpx.MockTransport(handler))
    return TokenProvider(client, make_settings(), clock)


async def test_token_issued_once_and_reused_until_five_minutes_before_expiry():
    requests: list[httpx.Request] = []

    def handler(request: httpx.Request) -> httpx.Response:
        requests.append(request)
        return httpx.Response(
            200, json={"token": "T1", "expires_dt": "20261027235959", "return_code": 0}
        )

    clock = Clock(datetime(2026, 10, 27, 12, 0, tzinfo=KST))
    provider = make_provider(handler, clock)
    assert await provider.token() == "T1"
    assert await provider.token() == "T1"
    assert len(requests) == 1

    request = requests[0]
    assert request.url == "https://api.kiwoom.com/oauth2/token"
    assert request.headers["api-id"] == "au10001"
    assert "authorization" not in request.headers
    body = json.loads(request.content)
    assert body["grant_type"] == "client_credentials"

    clock.now = datetime(2026, 10, 27, 23, 56, tzinfo=KST)
    await provider.token()
    assert len(requests) == 2


async def test_invalidate_allows_one_reissue_per_minute():
    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(
            200, json={"token": "T1", "expires_dt": "20261027235959", "return_code": 0}
        )

    clock = Clock(datetime(2026, 10, 27, 12, 0, tzinfo=KST))
    provider = make_provider(handler, clock)
    await provider.token()
    assert await provider.invalidate() is True
    assert await provider.invalidate() is False
    clock.now += timedelta(seconds=61)
    assert await provider.invalidate() is True


async def test_broker_error_never_leaks_secrets():
    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(200, json={"return_code": 8001, "return_msg": "bad credentials"})

    provider = make_provider(handler, Clock(datetime(2026, 10, 27, 12, 0, tzinfo=KST)))
    with pytest.raises(KiwoomAuthError) as info:
        await provider.token()
    text = str(info.value)
    assert APPKEY not in text and SECRET not in text
    assert "8001" in text


def test_settings_require_keys_and_strong_pairing_token():
    base = {
        "KIWOOM_APPKEY": "a",
        "KIWOOM_SECRETKEY": "b",
        "GATEWAY_PAIRING_TOKEN": "x" * 32,
        "GATEWAY_WATCH_CODES": "005930, 000660",
        "GATEWAY_ALLOWED_HOSTS": "127.0.0.1,localhost",
    }
    settings = Settings.from_env(base)
    assert settings.watch_codes == ("005930", "000660")
    assert settings.allowed_hosts == ("127.0.0.1", "localhost")

    missing = {k: v for k, v in base.items() if k != "KIWOOM_APPKEY"}
    with pytest.raises(ValueError):
        Settings.from_env(missing)
    with pytest.raises(ValueError):
        Settings.from_env({**base, "GATEWAY_PAIRING_TOKEN": "short"})
    with pytest.raises(ValueError):
        Settings.from_env({**base, "GATEWAY_WATCH_CODES": "005930,ABC123"})
