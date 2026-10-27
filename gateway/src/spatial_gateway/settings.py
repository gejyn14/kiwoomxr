import re
from collections.abc import Mapping
from dataclasses import dataclass

_CODE = re.compile(r"^[0-9]{6}$")
_MIN_PAIRING_TOKEN = 32


def _split(raw: str) -> tuple[str, ...]:
    return tuple(part.strip() for part in raw.split(",") if part.strip())


@dataclass(frozen=True, repr=False)
class Settings:
    appkey: str
    secretkey: str
    pairing_token: str
    watch_codes: tuple[str, ...]
    allowed_hosts: tuple[str, ...]
    rest_origin: str = "https://api.kiwoom.com"
    ws_url: str = "wss://api.kiwoom.com:10000/api/dostk/websocket"

    def __repr__(self) -> str:
        return "Settings(<redacted>)"

    @classmethod
    def from_env(cls, env: Mapping[str, str]) -> "Settings":
        def required(name: str) -> str:
            value = env.get(name, "").strip()
            if not value:
                raise ValueError(f"{name} is required")
            return value

        appkey = required("KIWOOM_APPKEY")
        secretkey = required("KIWOOM_SECRETKEY")
        pairing_token = required("GATEWAY_PAIRING_TOKEN")
        if len(pairing_token) < _MIN_PAIRING_TOKEN:
            raise ValueError(f"GATEWAY_PAIRING_TOKEN must be at least {_MIN_PAIRING_TOKEN} characters")
        watch_codes = _split(required("GATEWAY_WATCH_CODES"))
        if not watch_codes or any(not _CODE.fullmatch(code) for code in watch_codes):
            raise ValueError("GATEWAY_WATCH_CODES must be comma-separated 6-digit codes")
        allowed_hosts = _split(required("GATEWAY_ALLOWED_HOSTS"))
        return cls(appkey, secretkey, pairing_token, watch_codes, allowed_hosts)
