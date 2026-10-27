# Prototype gateway

FastAPI modular monolith for reusable financial capabilities. The gateway holds the Kiwoom credentials (from `gateway/.env`), seeds 1-minute bars over Kiwoom REST, streams KRX/NXT trades and order books from the Kiwoom WebSocket into a per-market hub, and pushes coalesced observations to paired clients. It exposes no order routes and never forwards Kiwoom tokens, keys or broker error text to clients.

```bash
uv sync --project gateway
uv run --project gateway pytest -q gateway/tests
uv run --project gateway --env-file gateway/.env uvicorn spatial_gateway.app:app \
  --app-dir gateway/src --host 0.0.0.0 --port 8471 --no-access-log
```

Required environment: `KIWOOM_APPKEY`, `KIWOOM_SECRETKEY`, `GATEWAY_PAIRING_TOKEN` (32+ chars), `GATEWAY_WATCH_CODES`, `GATEWAY_ALLOWED_HOSTS`. For a LAN demo add the Mac's LAN IP to `GATEWAY_ALLOWED_HOSTS`; a request with any other `Host` is rejected (400) and only that `Host` value is logged as a warning. `uv.lock` pins the resolved dependencies. Plain HTTP/WS on a LAN is a demo setup only; a deployed gateway needs HTTPS/WSS and account authorization before account data or orders are introduced.

Routes:

- `GET /health`: open; service status, `broker_connected`, `orders_enabled: false` only.
- `GET /v1/market/{six_digit_code}/quote|candles|orderbook`: require header `X-Pairing-Token`; mismatch returns 401 `{"error":"UNAUTHORIZED"}`.
- `WS /v1/stream`: first client message `{"type":"subscribe","token":"<pairing>","codes":["005930"],"correlation_id":"<uuid>"}` (1-10 six-digit codes). Bad token or format closes with code 4401 before any data. The gateway then sends `{"type":"observation","capability":"quote|candles|orderbook","market":"KRX|NXT|null","observation":{...}}` (same envelope as REST, with the subscribe correlation id; per code and capability at most 10 per second, candles 2 per second, latest wins) and `{"type":"heartbeat","broker_connected":bool,"reason":null|"<code>","server_time":"<ISO8601 +09:00>"}` every 5 seconds and on broker state change.

The optional `X-Correlation-ID` is an application UUID, not a Kiwoom request header. Financial decimals serialize as non-exponent decimal strings, up to 28 digits and eight fractional places. Quantities serialize as integer strings bounded to signed 64-bit range. Missing values remain JSON null. Those are application wire constraints, not exchange tick/quantity rules.

Origin `KIWOOM` identifies the capability provider. Without a live broker connection, observations are `DISCONNECTED`/`LOADING`/`UNAVAILABLE` with explicit reasons; there is no synthetic fallback in this gateway. Outside KRX/NXT sessions the gateway keeps serving the most recently received market's values as `DELAYED` with reason `MARKET_CLOSED` (and that market in the stream's `market` field); a code with no values yet is `UNAVAILABLE`/`MARKET_CLOSED`.

Unity's **Spatial Trading → 7. Connect to local gateway** selects this provider. **8. Preview with synthetic data** is a separate explicit design mode. These modes can switch during Play. The Editor and the headset share one address policy ([QUEST_SETUP.md](../docs/QUEST_SETUP.md#live-market-stream-gateway)): `wss://` for any host, plain `ws://` only for loopback or a private LAN address. Plain `ws://` on a LAN is for the demo only.

The client validates the response instrument and correlation UUID against the request. Envelopes require explicit null fields, exact uppercase status names and the intended `KIWOOM` provider. Duplicate keys, extra/missing fields, numeric JSON in string-valued financial fields, ambiguous timestamps and synthetic data received through this route are rejected. Candle adjustment and completion metadata are preserved. A renderer may retain stale values only with their explicit stale/disconnected status.

See [Kiwoom API mapping](../docs/KIWOOM_API_MAPPING.md), [financial contracts](../docs/FINANCIAL_MODELS.md) and [roadmap](../docs/ROADMAP.md). Do not remove a specification gate merely because a key exists or an undocumented trial request happens to succeed.
