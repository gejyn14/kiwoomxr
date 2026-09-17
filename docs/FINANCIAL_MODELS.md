# Financial models and capabilities

Status: Milestone 0 application contracts. All Kiwoom translations require the corresponding evidence in [KIWOOM_API_MAPPING.md](KIWOOM_API_MAPPING.md).

## Identity, quantities and time

| Type | Contract |
| --- | --- |
| `InstrumentRef` | Opaque stable instrument ID, market, currency, validated product class; canonical code/name are display metadata resolved from the catalog. Initial executable class: Korean cash equity only. |
| `AccountRef` | Opaque gateway alias ID and safe operator-defined display label. Principal authorization is separate. Contains no broker account number, token or credential slot. |
| `Venue` | Application enum initially restricted to verified KRX cash-equity routing. Wire encoding remains adapter-owned. Venue changes require a new draft. |
| `Money` | `{amount: exact decimal string, currency: KRW}`; Python Decimal / C# decimal. No binary float, NaN, Infinity, locale commas or implicit currency conversion. |
| `ShareQuantity` | Strict bounded integer, positive for intent, nonnegative for observed holdings/fills. Boolean/fractional/exponent inputs are rejected. Reject overflow before multiplication. |
| `Price` | Positive Money for order prices; tick/lot constraints are validated only against sourced rules. A price may be absent, never represented by a fabricated zero. |
| `Percentage` | Exact decimal percentage with explicit unit, not an ambiguous fraction. |
| `ObservationTime` | Source timestamp when documented, server UTC receipt time, monotonic receipt age, and timestamp-confidence reason when source time is incomplete. |

Do not strip code prefixes/suffixes globally. Apply only a verified per-field mapping, preserve the original identifier inside the adapter, and reject unknown forms. Keep venue identity through lookup, quote, chart, order book, account and order operations. A stock's six-digit shape is not proof it is an allowed product. Account/venue must not be silently replaced.

Wire strings with a sign need a field-specific rule: signed price presentation, price change, trade direction and negative P/L have different meanings. Do not globally call `abs()` or strip signs/leading characters. Preserve raw interpretation until a documented rule exists; an unresolved field is unavailable to numerical display/calculation/order validation. Documented zero-padding can be removed for decimal parsing without losing the sign. Missing, empty, malformed and out-of-range fields have distinct errors.

Dates and times in the workbook sometimes omit timezone or trading date. Keep their raw meaning and label server receipt time separately; do not assign a fabricated exchange timestamp. Receive time alone cannot prove a resumed market feed is current. Calendar, rollover and session assumptions must be explicitly sourced before use in live-order validation.

## Capability result envelope

Every financial capability returns a typed `DataResult<T>`:

| Field | Meaning |
| --- | --- |
| `data` | Optional typed payload; null when there is no validated value |
| `quality` | `LOADING`, `LIVE`, `DELAYED`, `STALE`, `DISCONNECTED`, `UNAVAILABLE`, `ERROR` |
| `source` | `KIWOOM_REST`, `KIWOOM_WS`, or `SYNTHETIC` in isolated shell/tests; source evidence revision |
| `sourceAsOf` / `receivedAt` | Source time if known and gateway receipt time; do not conflate them |
| `ageMs` / `knownDelayMs` | Server-calculated age and known upstream lag; nullable when not determinable |
| `subscriptionId`, `bindingRevision`, `connectionEpoch` | Prevent applying an old response to a new instrument/account/session |
| `completeness` | `COMPLETE`, `PARTIAL`, `UNKNOWN`, with continuation state retained internally |
| `reasonCode` / `fieldIssues` | Typed application errors; no raw secrets/provider messages |

Quality is derived from independent connection, freshness and validity facts. Display connection failure prominently while retaining last-known age/reason. Priority for the primary badge is ERROR for invalid data, DISCONNECTED for a broken transport, UNAVAILABLE for no usable source, LOADING with no result, STALE for expired data, DELAYED for known lag, then LIVE. Secondary reasons remain visible. LIVE means valid and within the configured policy for that capability, not “guaranteed exchange real time.”

Freshness policies are capability-specific and gateway-owned: `maxQuoteAge`, `maxBookAge`, `maxAccountAge`, `maxValidationAge`, plus chart period/session policy. Set and test explicit positive durations before integration; missing policy makes trading ineligible. Do not invent brokerage latency or heartbeat guarantees. Client derives display age from server age plus monotonic elapsed time, so a lost stream cannot freeze the freshness label. Closed-market history is labeled as history; silence is not automatically fabricated zero activity.

After a gap, keep last-known values visibly stale/disconnected, fetch a validated snapshot, then resume the verified stream protocol. Do not mix a later stream update with an older snapshot unless ordering is established. The current repository lacks stream sequencing/recovery semantics; full realtime integration remains blocked. Completed REST reads may support clearly labeled snapshots independently once their own blockers are resolved.

## Capability interface

These are application method names, not Kiwoom endpoints or TRs. Each accepts a correlation context, authorization context where needed, cancellation and deadline. All return typed results. UI adapters cannot access adapter internals.

| Capability | Inputs | Output | Consumer |
| --- | --- | --- | --- |
| `search_instruments` | Bounded search text, supported market filter | `InstrumentCandidates` from complete/paginated catalog with catalog age | Selector and all intent resolvers |
| `get_instrument` | InstrumentRef | `InstrumentDefinition` and sourced eligibility facts | Selector, order validation |
| `get_quote` / `subscribe_quote` | InstrumentRef, venue | `QuoteSnapshot` | Focus, Chart, Compare, order review estimates |
| `get_order_book` / `subscribe_order_book` | InstrumentRef, venue | `OrderBookSnapshot` | OrderBook |
| `get_candles` | InstrumentRef, venue, ChartPeriod, date window, adjustment policy | `CandleSeries` | Chart, Compare |
| `list_authorized_accounts` | Authenticated principal | Safe `AccountRef[]` backed by verified token-account bindings | Account selector |
| `get_positions` | AccountRef, optional instrument filter, venue | Complete `PositionSet` | Position, sell validation |
| `get_balance` | AccountRef | `CashBalance` | Position/account summary |
| `get_buying_power` | AccountRef, explicit instrument/side/quantity/price context | `BuyingPower` with calculation basis | Order validator only; optional read-only UI |
| `submit_order` | Validated immutable execution intent, internal single-use execution permit | `SubmissionObservation` | Only OrderExecutor |
| `get_order_status` / `subscribe_order_status` | Authorized order reference and verified broker correlation | `OrderObservation` | Order controller and reconciliation worker |

The order executor is the only caller of `submit_order`. It is not a public arbitrary order RPC. Compare combines independent structured capability results deterministically; it does not ask Gemini to supply prices. Authentication/token refresh is an internal adapter capability, never a Quest action.

## Market and account payloads

| Model | Required structure and invariants |
| --- | --- |
| `InstrumentDefinition` | Resolved identity/code/name, product class, currency, venue eligibility and source references. Unknown eligibility cannot authorize trading. |
| `QuoteSnapshot` | Instrument/venue, optional last price, price change, percentage, volume and optional session facts; each available field has validated units. No assumption that last price equals executable price. |
| `BookLevel` | Side, explicit rank, optional validated price and quantity. Missing level differs from zero quantity. |
| `OrderBookSnapshot` | Instrument/venue, ordered bid/ask arrays, source time, snapshot/completeness evidence. Parse each documented level mapping explicitly. Do not interpolate depth. |
| `Candle` | Source period key/time, open/high/low/close, volume when known, adjustment policy, completion status if known. Validate positive prices and OHLC bounds; quarantine invalid bars. |
| `CandleSeries` | Instrument/venue, supported interval (initial daily; minute after mapping validation), ordered bars, gaps, completeness, date/adjustment provenance. Do not interpolate gaps or append trade ticks as finalized broker candles. |
| `Position` | Account alias/instrument, cash/credit classification when sourced, held quantity, tradable quantity, average cost, current valuation/P&L when supplied. Each missing field remains missing. |
| `PositionSet` | Account alias, positions, coverage/pagination scope and observation time. Only a successful complete account snapshot can establish an instrument is absent. An empty/missing partial page cannot establish zero holdings. |
| `CashBalance` | Distinct cash deposit, withdrawable amount and source-defined orderable cash. Estimated assets and buying power must not be relabeled balance. |
| `BuyingPower` | Account/instrument/side/price basis, orderable cash/quantity if sourced, fee treatment, cash-only eligibility and freshness. Unknown fee/credit semantics blocks executable validation. |

Persist only evidence needed for order recovery, not an unversioned copy of financial truth in UI state. Calculated display values include formula and source observation IDs. For example, quantity × observed last price is an estimate with an as-of time; it is neither a fill price nor an enforceable maximum. Account totals come from the authoritative capability, not recomputed from a partial page of visible positions.

## Order models

| Model | Contract |
| --- | --- |
| `OrderIntent` | Instrument, `BUY` or `SELL`, quantity, `MARKET` or `LIMIT`, limitPrice only for LIMIT. A partial intent has explicitly nullable unresolved fields and cannot become an ExecutionIntent. Present fields are still strictly typed; invalid types are rejected rather than treated as missing. |
| `OrderDraft` | UUID, revision, owner/session, bound AccountRef, resolved instrument, side, quantity, type, price applicability, venue, currency, environment, immutable origin chain, creation/expiry, lifecycle state. |
| `DraftValidation` | Field errors, policy/spec eligibility, account/market evidence refs and ages, optional estimate, required maximum-value bound, server policy revision, valid-until time. |
| `ReviewSnapshot` | All material draft fields plus safe account label, environment, validation basis and expiry; deterministic canonical serialization/digest. Render directly. |
| `ConfirmationChallenge` | Opaque unpredictable one-use ID, principal/device/session/epoch, draft ID/revision, review digest, policy revision, expiry. Delivered only to physical review ingress. |
| `ExecutionIntent` | Server-side frozen draft + validation and confirmation identity. Never accepts replacement fields from a confirm request. |
| `SubmissionAttempt` | Unique internal attempt ID, draft revision, immutable intent hash, durable attempt phase, correlation ID, known broker reference if returned. |
| `OrderObservation` | Known state, raw broker state scoped to event kind, quantities/prices when validated, broker/order/date/account association, observed/received times and evidence ID. UNKNOWN carries reason and retained prior evidence. |

Define draft lifecycle and order outcome separately even when a combined UI timeline uses the same labels. See [ORDER_SAFETY.md](ORDER_SAFETY.md). Do not serialize an internal execution permit, raw account ID or brokerage token to Quest or Gemini.

## Transformation test requirements

Use synthetic fixtures explicitly shaped from documented fields, with source sheet/cell references. Include decimal round trips across Python/C#, unit conversions only where documented, zero vs blank/missing, signed prices vs signed P/L, zero-padded numbers, prefixed instrument IDs, invalid OHLC, duplicated pages, partial-account snapshots, mixed venue/time, date rollover and late response races. No real account identifiers or credentials in fixtures. Unknown field semantics require a failing/blocked capability result, not a speculative conversion test that blesses a guess.
