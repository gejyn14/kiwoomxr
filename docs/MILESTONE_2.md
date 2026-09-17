# Milestone 2 — financial data and spatial UI

Status: **IN_PROGRESS / LIVE_INTEGRATION_BLOCKED_BY_MISSING_SPEC**, 2026-09-18.

The user requested live data, a substantially improved spatial UI, continued work through M9, and use of Meta's official design guidance. The user confirmed that no physical Quest 3 is available. Continue software/simulator work; do not treat simulator success as device acceptance.

## Implemented scope

- A rounded spatial Focus surface with clearer price hierarchy, Korean text, source/freshness labels, structured candlesticks and volume, nearby controls and explicit selected/hover borders.
- Typed, immutable quote/candle/book observations with separate status, origin, source/receipt time and nullable values.
- An explicit preview capability. Components render the same observation contracts in preview and gateway modes; gateway failure does not activate preview.
- A gateway client boundary with a correlation ID propagated from the triggering Action, selection generation checks, bounded request timeout, no redirects, no automatic retries and a visible 30-second cache-age policy. This is a client display limit, not a claim about market freshness or exchange quotas.
- A FastAPI modular monolith and strict Pydantic contracts. Current Kiwoom capabilities deterministically return null/unavailable with the documented specification blocker. Broker keys are neither read nor sent while blocked.
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

The repository workbook remains the only authorized Kiwoom source. Its required bearer header for initial token issuance conflicts with credential bootstrap, and its WS lifecycle and several numeric/time semantics are incomplete. See [K-01–K-10](KIWOOM_API_MAPPING.md#blocking-specification-register).

A source-scope question is pending: may official Kiwoom documentation be consulted and the relevant evidence added to this repository? Until answered, do not guess auth headers, try undocumented requests, normalize ambiguous signed prices with `abs()`, or label a local response as live market data. An available environment key is not a substitute for a verified wire specification.

The [roadmap](ROADMAP.md) retains M3–M9 exit gates, including deterministic order safeguards and deliberate physical confirmation. The present gateway has no order submission capability.

## Verification distinctions

The first controller harness incorrectly expected a comparison to remain open after its comparison instrument became the primary selection. The captured revision and existing domain regression establish the intended self-comparison removal; the original harness failure is retained locally. An initial new-package compile also failed because the Unity test assembly lacked an explicit Newtonsoft reference; that reference was added and all 59 tests then passed. Neither setup failure is counted as a passing run.

Evidence under `artifacts/milestone1/` retains the existing runner directory name; new M2 records identify their scope explicitly. Generated Unity YAML has serializer-produced trailing spaces; source/document whitespace checks are scoped separately.

The final APK was rebuilt after a final request-cleanup ordering adjustment (abort requests before stopping their enumerators). Simulator captures and shared tests predate that small adjustment. In-flight cancellation stress remains unverified under M9; the APK compilation/inspection is current.
