# Interaction and component model

Status: Milestone 0 design. Comfort, readability, tracking and performance require actual Quest 3 testing.

## Spatial experience

Passthrough MR is primary. Build for a seated user with relaxed shoulders and supported elbows. Place frequent controls near chest/lower visual-field height, within comfortable reach, and offer a single recenter control. Avoid head-locked surfaces that follow every movement. Use world-stable placement with explicit recentering; prevent clipping into the user, desk and neighboring controls.

Use a prominent chart surface, a shallow order-book depth view, a compact position summary and a two-instrument comparison view. Each has a purpose and independent lifecycle. Do not reproduce a desktop title-bar/window grid or a dense HTS table. Names, numbers and freshness labels must remain readable over passthrough. Use text plus shape for BUY/SELL and connection status; color alone is insufficient.

Starting usability hypotheses, not Meta standards or verified outcomes: near controls around 0.45–0.70 m from the seated user, chart around 0.8–1.2 m, primary button targets at least about 4 cm wide in near space, adjustable scale and left/right-handed placement. Validate per-user reach, occlusion and angular readability on-device before freezing dimensions. Resize changes information density/layout within bounds; never shrink text into illegibility.

## Input behavior

| Intent | Hand interaction | Controller fallback | Action boundary |
| --- | --- | --- | --- |
| Select/open/compare | Near direct poke/touch; far hand ray plus pinch | Ray plus deliberate trigger activation | Same typed Action |
| Move component | Standard Meta grab on a separate handle | Standard grab/select handle | Presentation transform only |
| Resize | Standard supported grab/transform handles, bounded | Same handles through controller | Presentation size only |
| Search / quantity entry | Voice or large constrained selectors | Same selectors; keyboard only when needed | Resolve/validate before Action |
| Cancel draft | Large explicit CANCEL control; voice action may cancel if supported later | Trigger on CANCEL | `CANCEL_ORDER_DRAFT` |
| Confirm real order | Deliberate near confirmation interaction after structured review | Deliberate controller press-and-hold after review | Restricted `CONFIRM_ORDER` |

Use Meta Interaction SDK primitives; do not add bespoke hand gestures. Hover, gaze/head direction, ray intersection, pinch used to grab, and incidental touches never count as order confirmation. Route a single completed activation to the dispatcher; prevent simultaneous hand/controller callbacks from creating duplicate actions. Switching input devices cancels any hold/armed confirmation and requires release before restarting.

Quest 3 eye tracking is not an input capability. “Selected visual context” means explicit hand/controller component selection, not inferred eye fixation. Hand-tracking loss shows a clear fallback cue and exposes controller operation. Do not combine UI activation hit regions with grab/resize handles.

## Focus Mode

Show one selected instrument, its quote, data age and the main chart. Start without book, position or compare. A reachable action strip opens secondary components on demand. Component additions are paced and spatially consistent; opening Position prompts account selection if none exists, without inventing balance data. Selection changes update `FollowSelectedInstrument` bindings; each component transitions through its own loading/live states.

## Workspace Mode

Users can add, remove, move and resize independent components where appropriate. A component binding is either `FollowSelectedInstrument` or `Pinned(InstrumentRef)` and is visibly labeled. Position also binds to the selected authorized account. Comparison binds to two explicit references. Pinned components do not silently change when the shared selection changes.

Persist only component kind, binding, period, transform and scale. Do not persist positions, balance, last-known price as truth, confirmation challenges or executable commands in layout. Closing a component releases its subscription reference; it never cancels an order. Failure to save/load Workspace cannot disable the Order controller.

## Trade Mode

Creating a real-order draft opens Trade Mode even if incomplete or policy-blocked. Dim unrelated surfaces and stop them intercepting input. Present the order at comfortable near reach, with all material fields visible together. Disable moving/resizing the confirmation surface while reviewing; restoring hand tracking or changing input modality resets the confirmation interaction. Manual cancel and order-status access remain available if a chart or workspace fails.

Render only structured server-returned draft data, with localized fixed labels:

```text
Account: safe account alias        Environment: REAL
Instrument: resolved Korean name and code
Side: BUY / SELL
Quantity: integer shares
Order type: MARKET / LIMIT
Limit price: explicit KRW value, or not applicable for MARKET
Venue: reviewed venue
Estimated value: deterministic calculation + basis/time, or unavailable
Safety bound: validated maximum basis, or a blocking reason
Draft revision / validation issues / source and data age

[ CANCEL ]                    [ CONFIRM ORDER ]
```

Do not ask Gemini to write this surface. Missing/ambiguous material fields display an incomplete state with disabled confirmation. An unavailable optional estimate is never zero; if its missing inputs also prevent maximum-value or cash validation, confirmation stays disabled. Do not replace requested MARKET with LIMIT to make validation pass.

Physical confirmation protocol:

1. Render the immutable current draft revision from the gateway. Request a review challenge only on the paired physical-device channel after the full review surface is shown.
2. Require all activators to be released before arming. Use a standard hold on the dedicated confirmation control; initial design target is 1.5 seconds with visible progress, subject to device usability validation. Hand confirmation is near/poke; far pinch alone cannot submit. Controller confirmation requires deliberate targeted press-and-hold, preserving fallback accessibility.
3. Leaving the target, tracking/focus loss, surface occlusion, app pause, input switch, draft/policy change or expiry resets the hold. No click-through from the action that created the draft.
4. On completion, emit one `CONFIRM_ORDER` with the reviewed revision, digest and challenge. Disable the control while the result is pending; never optimistically show an accepted/filled order.
5. Render authoritative order observations and unknown outcomes separately. Once submitting, CANCEL cannot pretend to undo the order. Initial scope has no broker cancel action; show that draft cancellation is no longer available.

The server validates all guards independently. The UI hold is deliberate intent evidence from the trusted client, not hardware attestation. Detailed limitations and replay prevention are in [ORDER_SAFETY.md](ORDER_SAFETY.md).

## Component contracts

Each component consists of an input binding, application controller, capability subscription and renderer. Common quality semantics are defined in [FINANCIAL_MODELS.md](FINANCIAL_MODELS.md). `DELAYED` is mandatory in addition to the loading/live/stale/disconnected/unavailable/error states. Every visible financial value carries its own source and age when components have mixed freshness.

| Component | Inputs | Data dependencies | Supported actions |
| --- | --- | --- | --- |
| `InstrumentSelectorComponent` | Search expression, optional market filter | Instrument lookup/search candidates | `SELECT_INSTRUMENT`, `CLOSE_COMPONENT` in Workspace |
| `ChartComponent` | Instrument binding, ChartPeriod, adjustment choice | CandleSeries and separately labeled quote | `CHANGE_PERIOD`, `COMPARE_INSTRUMENT`, `CLOSE_COMPONENT` |
| `OrderBookComponent` | Instrument binding, venue, depth view | OrderBookSnapshot/updates | `CREATE_ORDER_DRAFT` with explicit intended fields, `CLOSE_COMPONENT` |
| `PositionComponent` | Authorized account alias, instrument binding | Account positions, cash balance; buying power only through its capability | `OPEN_CHART`, `CREATE_ORDER_DRAFT`, `CLOSE_COMPONENT` |
| `CompareComponent` | Primary and comparison references, aligned period | Independent quotes/candles for both, aligned by explicit period and currency | `COMPARE_INSTRUMENT`, `CHANGE_PERIOD`, `CLOSE_COMPONENT` |
| `OrderComponent` | Draft/order reference, reviewed revision | DraftValidator results, buying power/position validation, authoritative order observations | `CREATE_ORDER_DRAFT`, `CANCEL_ORDER_DRAFT`, restricted `CONFIRM_ORDER` |

The OrderBook may propose a selected price but never silently assign quantity/side/account. Position shortcuts cannot infer “sell all” from cached shares. Compare does not use missing history as zero or imply unmatched dates are equivalent.

## Component state behavior

| Component | LOADING | LIVE | DELAYED | STALE | DISCONNECTED | UNAVAILABLE | ERROR |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Selector | Searching; cancel old search | Current complete candidates | Label catalog lag | Label old catalog; refresh before trade | Keep labeled prior candidates | No match/unsupported source explicitly | Search issue and safe retry |
| Chart | Placeholder, no fabricated candles | Valid candle series with period/as-of | Show known lag | Freeze and label old bars | Keep history with offline banner | Explain absent history/period | Local chart issue; other components work |
| OrderBook | Empty ladder placeholder | Valid per-side levels and age | Lag badge on levels | Freeze; disable price shortcut | Disconnected banner; no implied liquidity | Missing depth is not empty market | Parsing/subscription issue; resync |
| Position | Account-bound loading state | Verified holdings/balance snapshot | Label snapshot delay | Keep old data with warning; disable trade shortcuts | Reauth/reconnect cue | Missing account/data, never zero shares | Redacted account error |
| Compare | Independent progress for both | Both valid with individual times | Show which side lags | Mark affected side, no fresh comparison claim | Per-side connection status | Missing side/gap explicitly | A failed side does not overwrite the other |
| Order | Validating/preparing review | Draft eligible or current authoritative observation | Delayed evidence blocks new confirmation | Expire validation/challenge | Keep draft/order identity, disable new confirm | Explain missing field/policy/spec | Known pre-send failure or UNKNOWN outcome; never generic retry submission |

For Order, the financial quality indicator does not replace its lifecycle state. A `FILLED` observation may be old yet still describe a historical fill. A `READY_FOR_CONFIRMATION` draft can become invalid when its validation expires.

## Demo and device acceptance

| Demo | Acceptance evidence and milestone |
| --- | --- |
| A — Hand | Select Samsung Electronics; chart/book/position show the same selected identity with independent freshness. M2 market portion, M3 full flow. |
| B — Components | Move Chart, remove book, add Position, change selection; follow/pinned behavior and subscriptions are correct. M4. |
| C — Voice | “SK하이닉스 차트 보여줘” uses SELECT_INSTRUMENT and OPEN_CHART with the same result as manual interaction. M5. |
| D — Gemini | Samsung primary stays selected; comparison tool adds SK Hynix through COMPARE_INSTRUMENT. M5. |
| E — Multimodal | Explicitly select a chart; “이거 설명해줘” explains the selected snapshot without changing structured values. M6. |
| F — Real voice order | Voice creates a draft only when enabled; all field/policy checks pass; physical confirmation submits once; status comes from broker observations. M8, blocked until required specs/tests are complete. |

M1 device testing must cover seated reach, Korean text/numerals over light/dark passthrough, hands and controllers separately, rapid switching, tracking loss, occlusion, recenter, bounds on movement/resize, app pause and repeated activation. Record actual frame pacing, thermal behavior and interaction misses; do not treat Editor playback as device performance evidence. Synthetic content and simulated order states must carry visible labels and must never be presented as a real API integration.
