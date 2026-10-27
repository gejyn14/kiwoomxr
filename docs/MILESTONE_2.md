# Milestone 2 — financial data and spatial UI

Status: **IN_PROGRESS / MARKET_PIPELINE_IMPLEMENTED_LIVE_VERIFICATION_PENDING**, updated 2026-10-27.

On 2026-10-27 the user decided to use the Kiwoom production domain and to accept kiwoom-cli documentation and implementation as evidence, retiring the former `BLOCKED_BY_MISSING_SPEC` gate for market data. Design: [investment-jarvis design](superpowers/specs/2026-10-27-investment-jarvis-design.md); plan: [live market pipeline](superpowers/plans/2026-10-27-live-market-pipeline.md). Sections dated 2026-09-18 below are the earlier shell-era record and are kept as history; where they conflict with the stream architecture section, the latter governs.

## Live market pipeline (2026-10-27)

- Gateway: Settings and TokenProvider (au10001, expiry in KST), `KiwoomRest` 1-minute candles (ka10080), `KiwoomRealtime` WebSocket (LOGIN, REG, 0B trade, 0D book; KRX `005930` and NXT `005930_NX`), market session rules (KRX 09:00-15:30; NXT 08:00-08:50 and 15:30-20:00 KST), `MarketHub`, and a `/v1/stream` WebSocket with pairing token and heartbeat. No order endpoint; `/v1/orders` still returns 404.
- Unity: `GatewayStreamContract`, `GatewayStream`, heartbeat-based aging in `MarketDataSession`; `HubMarket` replaced `SpecificationGatedMarket`. HTTP polling and the 30-second cache-age display policy described below are superseded. Client rules are in [QUEST_SETUP.md](QUEST_SETUP.md): heartbeat every 5 s; no heartbeat for 15 s turns LIVE/SNAPSHOT values STALE (`GATEWAY_HEARTBEAT_LOST`); socket loss shows DISCONNECTED; reconnect backoff 1/2/4/8 s.

| Evidence | Result | Limit |
|---|---|---|
| Gateway pytest | 108 passed | Offline; mocked transports |
| .NET domain tests | 93 passed | Does not exercise Unity |
| `gateway/tests/fixtures/ka10080_005930.json` | Used by `test_rest.py` | From the API documentation example, not a recorded live response |
| Live Kiwoom (au10001, ka10080, 0B/0D) | NOT VERIFIED | Live verification pending: valid app key required; au10001 currently returns 8001 (app key/secret validation failed) |
| Unity EditMode, editor visual, on-device | NOT RUN | Unity license pending; headset arrives 2026-10-29 |

The user requested live data, a substantially improved spatial UI, continued work through M9, and use of Meta's official design guidance. The user confirmed that no physical Quest 3 is available. Continue software/simulator work; do not treat simulator success as device acceptance.

## Implemented scope

- A rounded spatial Focus surface with clearer price hierarchy, Korean text, source/freshness labels, structured candlesticks and volume, nearby controls and explicit selected/hover borders.
- Typed, immutable quote/candle/book observations with separate status, origin, source/receipt time and nullable values.
- An explicit preview capability. Components render the same observation contracts in preview and gateway modes; gateway failure does not activate preview.
- (Superseded 2026-10-27 by the stream client above.) A gateway client boundary with a correlation ID propagated from the triggering Action, selection generation checks, bounded request timeout, no redirects, no automatic retries and a visible 30-second cache-age policy. This is a client display limit, not a claim about market freshness or exchange quotas.
- A FastAPI modular monolith and strict Pydantic contracts. (As of 2026-09-18 the Kiwoom capabilities returned null/unavailable with the specification blocker and broker keys were not read; superseded by the pipeline above.)
- Gateway process readiness separated from broker connectivity, no order endpoints, redacted request-validation errors and localhost host restrictions.
- Decimal strings and integer-quantity strings preserve values across Python/C# without binary floating-point conversions. Supported wire precision is an application constraint, not an exchange rule.
- Explicit token decoding through Unity’s official Newtonsoft package. Null, missing, duplicate and extra fields are distinguished; responses must match the requested instrument/correlation and provider. Candle adjustment/completion metadata is retained. Quote and chart status are presented independently.

## Verified so far

| Evidence | Result | Limit |
|---|---|---|
| Shared C# tests under .NET | 59 passed | Financial invariants, exact gateway decoding, nullable values, precision, identity/correlation, candle ordering and original Action tests |
| Same shared tests inside Unity | 59 passed | Does not establish on-device rendering or networking |
| Gateway tests | 16 passed | Contract correctness, specification gate, no reflected request input, localhost host check and absent order routes |
| Redesigned simulator rendering | Price/focus repair verified | First capture had clipped price and stale paused copy; repaired compositor capture shows full price and normal input cue. |
| Controller Action path | Observed revisions 1–4 | Select Hynix → book → compare → select Samsung. Selecting the comparison instrument correctly closes the now-invalid comparison. |
| Unity → local gateway | PASS after decoder repair | Exact null/unavailable reason preserved; separate manual selection/book Actions also work while blocked. Initial decoder failure retained. |
| Stopped gateway | DISCONNECTED verified | Null values, no synthetic fallback; owned local gateway restored after the probe. |
| Broker REST/WebSocket calls | NOT RUN | K-01 and capability-local source gates remain open |
| Hardware acceptance | NOT RUN | No Quest 3 available |

The final ARM64/IL2CPP Android build and APK inspection passed. Package permissions, required passthrough, optional hands/controller fallback and absence of the known local SDK token were checked. This is a bounded artifact inspection, not device acceptance or a complete secret audit. See [machine-readable evidence](evidence/milestone2-2026-09-18.json), [Focus screenshot](images/milestone2-focus.png) and [gateway-state screenshot](images/milestone2-gateway.png). Dependency tests currently emit upstream Starlette/httpx and AnyIO deprecation warnings; these are not test failures.

## Live integration gate

History: until 2026-10-26 the repository workbook was the only authorized source and the integration was `BLOCKED_BY_MISSING_SPEC` (token bearer-header conflict, undefined WS lifecycle, incomplete numeric/time semantics). The 2026-10-27 decision retired that gate for market data. See the per-item status in [K-01–K-10](KIWOOM_API_MAPPING.md#blocking-specification-register).

What remains gated: nothing has been run against the live Kiwoom API, so every K-item addressed for market data is verified offline only. Do not label a local or fixture response as live market data, and do not normalize ambiguous signed prices with `abs()`. Order paths are out of scope here and their K-items move to a follow-up plan.

The [roadmap](ROADMAP.md) retains M3–M9 exit gates, including deterministic order safeguards and deliberate physical confirmation. The present gateway has no order submission capability.

## Verification distinctions

The first controller harness incorrectly expected a comparison to remain open after its comparison instrument became the primary selection. The captured revision and existing domain regression establish the intended self-comparison removal; the original harness failure is retained locally. An initial new-package compile also failed because the Unity test assembly lacked an explicit Newtonsoft reference; that reference was added and all 59 tests then passed. Neither setup failure is counted as a passing run.

Evidence under `artifacts/milestone1/` retains the existing runner directory name; new M2 records identify their scope explicitly. Generated Unity YAML has serializer-produced trailing spaces; source/document whitespace checks are scoped separately.

The final APK was rebuilt after a final request-cleanup ordering adjustment (abort requests before stopping their enumerators). Simulator captures and shared tests predate that small adjustment. In-flight cancellation stress remains unverified under M9; the APK compilation/inspection is current.
