# Prototype gateway

FastAPI modular monolith for reusable financial capabilities. Current implementation provides strict typed contracts and explicit unavailable responses while Kiwoom source gaps remain unresolved. It does **not** call the broker, read brokerage keys, authenticate an account, or execute orders.

```bash
uv sync --project gateway
uv run --project gateway pytest -q gateway/tests
uv run --project gateway uvicorn spatial_gateway.app:app \
  --app-dir gateway/src --host 127.0.0.1 --port 8471 --no-access-log
```

`uv.lock` pins the resolved dependencies. This development server is loopback-only. It has no remote session authentication and must not be exposed on a LAN/public interface. A deployed gateway needs HTTPS/WSS, authenticated device sessions and account authorization before account data or orders are introduced.

Routes:

- `GET /health`: process readiness; not brokerage connectivity.
- `GET /v1/market/{six_digit_code}/quote`
- `GET /v1/market/{six_digit_code}/candles`
- `GET /v1/market/{six_digit_code}/orderbook`

The optional `X-Correlation-ID` is an application UUID, not a Kiwoom request header. Financial decimals serialize as non-exponent decimal strings, up to 28 digits and eight fractional places. Quantities serialize as integer strings bounded to signed 64-bit range. Missing values remain JSON null. Those are application wire constraints, not exchange tick/quantity rules.

With the current specification gate, capabilities return `UNAVAILABLE`, origin `KIWOOM`, reason `BLOCKED_BY_MISSING_SPEC:K-01`, null value and null source/receipt times. Origin identifies the intended capability provider; it does not claim an observed broker response. There is no synthetic fallback in this gateway.

Unity's **Spatial Trading → 7. Connect to local gateway** selects this provider. **8. Preview with synthetic data** is a separate explicit design mode. These modes can switch during Play. Android builds refuse the cleartext loopback development URL; a secure gateway configuration is required on a headset.

The client validates the response instrument and correlation UUID against the request. Envelopes require explicit null fields, exact uppercase status names and the intended `KIWOOM` provider. Duplicate keys, extra/missing fields, numeric JSON in string-valued financial fields, ambiguous timestamps and synthetic data received through this route are rejected. Candle adjustment and completion metadata are preserved. A renderer may retain stale values only with their explicit stale/disconnected status.

See [Kiwoom API mapping](../docs/KIWOOM_API_MAPPING.md), [financial contracts](../docs/FINANCIAL_MODELS.md) and [roadmap](../docs/ROADMAP.md). Do not remove a specification gate merely because a key exists or an undocumented trial request happens to succeed.
