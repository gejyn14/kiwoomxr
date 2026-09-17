# Failure, recovery and observability

Status: Milestone 0 failure design and future verification plan. No runtime failure tests have been executed.

## Failure matrix

| Failure | Detection and visible behavior | Recovery / retry policy | Required evidence |
| --- | --- | --- | --- |
| Gemini unavailable | Adapter health/connection error; voice/explanation unavailable, manual controls active | Reconnect provider independently; no pending tool replay | Manual actions and eligible confirmation succeed with provider stopped |
| Gemini timeout | Bounded turn/tool deadline; discard late result for expired turn/context | User may repeat intent; dedup draft tool IDs; never confirm | Late response produces no mutation |
| Malformed/unknown Gemini tool | Strict schema and allowlist failure; explain missing/invalid input | Return typed tool error; no guessed repair | Executor untouched; only approved Actions reachable |
| Ambiguous voice order | Multiple instruments, unclear quantity/side/type/account or conflicting tool calls | Clarify explicitly; no ready draft | Incomplete/error reason identifies ambiguity |
| Kiwoom auth failure | Redacted typed auth error; affected capabilities unavailable | Before a read/send, renew token only under verified auth rules; no order replay; reverify token-account binding | Changed/invalid binding blocks account access/submission |
| Read-only REST timeout | Component retains aged result, then stale/error; no missing-as-zero | Bounded read retry with backoff/jitter once retry/rate specs permit, honoring cancellation | Old response cannot overwrite new selection |
| REST rate limit | Typed rate-limit status; shared admission control protects gateway | No invented requests-per-second; configure verified quotas, bounded read retry only | Saturation does not queue stale order confirmations |
| Kiwoom WS disconnect | Mark subscriptions disconnected immediately; age last data | Verify login/heartbeat/re-register protocol first, reconnect with backoff, resnapshot and discard old epoch | No LIVE label merely on socket-open; no order replay |
| Stale/delayed market data | Per-capability age/gap/lag policy; persistent visible labels | Refresh authoritative snapshot; invalidate any dependent draft validation | Missing freshness policy and expired evidence block new confirm |
| Missing account data | Incomplete pages/missing fields/unauthorized alias shown unavailable | Fetch complete authorized scope; no inferred zero or invented buying power | No sell from absent cached position; no buy from estimated assets |
| Duplicate confirmation | Single-use challenge, unique attempt and atomic state transition | Return existing state for identical replay, reject mismatched payload | Exactly one adapter call despite concurrent requests/reconnect |
| Order response timeout | Durable send intent exists; outcome shown UNKNOWN | Never retry submission. Query verified authoritative order state | Request count stays one; reservations retained |
| API response suggests receipt but execution unknown | Persist response/order reference separately from lifecycle evidence | Reconcile read-only; do not infer acceptance/fill | HTTP success is never asserted as FILLED |
| Unknown/unmapped broker state | Preserve sanitized raw state and known observations | UNKNOWN until verified mapping/evidence exists | Unknown enum cannot fall through to success |
| Out-of-order/duplicate WS events | Match account/order/date/event identity; detect conflicts/gaps | Do not sum duplicated fills; resnapshot/reconcile using sourced ordering | Prior confirmed fill retained; no double-count or rollback |
| Component failure | Isolate renderer/controller failure, show recoverable surface error | Recreate affected view and subscriptions only | Chart crash leaves Order controller/status service active |
| Order surface failure before confirm | Physical review cannot be completed | Disable confirm until full surface restored and new challenge acquired | No background execution from missing/occluded UI |
| Order surface failure after send | Gateway retains admitted attempt; UI reports pending recovery | Restore order-status view by order reference; never resend | Known/unknown order survives UI destruction |
| Gateway restart | New connection epoch, journal recovery and stale session/challenge detection | Fence old execution owner; reconcile unresolved attempts; refresh drafts and accounts | Crash-at-each-boundary tests show no auto-send/retry |
| Journal write failure/full disk | Required transaction fails before send | Disable new submission; preserve safe read-only operation | No adapter call if intent not durably recorded |
| Journal failure after broker send | Outcome cannot be fully persisted | Freeze affected submission/account, recover from durable intent and broker evidence | UI UNKNOWN; no retry triggered by storage error |
| Quest reconnect / HTTP response lost | Invalidate old epoch and display connection loss | Fetch server draft/order status first; never replay confirmation queue | Same attempt identity survives and no second send |
| Tracking loss / app pause | SDK focus/tracking callbacks; clear armed hold | Recenter/resume, fetch validity, re-render then new physical action | Partial hold never resumes automatically |
| Account/selection changed during voice or review | Context/draft revision mismatch | Reject late tool call or invalidate review, never retarget | Order fields remain exactly those reviewed |
| Analytics/export failure | Bounded queue, drop/failure counter; execution remains operational | Export/redrive redacted telemetry asynchronously; correctness journal unaffected | Disabled remote log endpoint does not delay send |
| Corrupt/incompatible workspace | Layout schema/version/transform validation | Load safe Focus layout; fetch financial data anew | Workspace failure cannot prevent valid manual trading |

Missing-spec responses are explicit unavailable capability results, not simulated successful Kiwoom responses. A blocked implementation must not issue exploratory real orders to discover undocumented behavior.

## Recovery boundaries

Read-only retry and mutation retry are separate policies. GET-like financial capabilities may use POST on the broker wire; classification is by financial effect, not HTTP verb. No generic “retry all POSTs” middleware. Broker order submission never automatically retries, including after authentication errors, transport disconnects, proxy failures or 5xx responses. Preserve first failure and subsequent observations separately.

Each gateway session/subscription has a connection epoch. Reconnection invalidates old callbacks, challenges and stream registrations. A fresh transport is not evidence of fresh data. Account and order subscriptions are authorization-scoped; account switch/log-out cancels old reads and removes private cached values from the device. A delayed response cannot repopulate them.

A complete verified snapshot establishes its own coverage only. Absence from the unfilled-order list does not establish a fill, cancellation or rejection. Reconciliation preserves authoritative observations, records conflict, and avoids “latest timestamp wins” when broker time/order semantics are missing. New account submissions remain blocked when exposure is unresolved.

## Observability without a remote logging dependency

Carry `correlationId` from Quest Action to gateway to the internal Kiwoom request record. Store API ID and internal request/attempt ID at the adapter boundary. Do not add an undocumented correlation header/field to Kiwoom requests. Store a broker-provided reference only when its field is documented.

Minimum structured events:

| Event | Safe fields |
| --- | --- |
| `order_draft_created` | Correlation/action/draft ID, revision, server-assigned interface/origin, safe account alias, environment |
| `order_validation_completed` | Draft revision, policy version, pass/block reason codes, evidence ages and references |
| `order_user_confirmed` | Confirmation event ID, draft/review digest, authenticated device-session reference, physical interaction class; no raw challenge secret |
| `order_submission_attempted` | Attempt ID, intent digest, adapter/API ID, start time; written in the correctness journal before possible send |
| `order_broker_observation` | Attempt/order association, redacted allowlisted broker fields, response classification, observation time and source evidence |
| `order_state_changed` | Previous known state, new known/unknown state, reason and observation ID |
| `order_reconciliation_needed` | Uncertainty reason, affected attempt/account alias, last known evidence |

The local journal durably records mandatory lifecycle/attempt evidence and minimal review data. It contains private trading information and requires restricted file permissions, protected storage/backups and a defined retention policy before live operation. It is not committed to Git or sent to Gemini. Emit redacted telemetry through a bounded async exporter after local correctness transitions. Optional export drops must be counted; do not lose correctness records to the market-data queue or telemetry backpressure.

Never log credentials, tokens, full authorization headers, raw account IDs, raw provider bodies, microphone audio, screen images or unredacted exception/request dumps. Use a field allowlist at serialization, not just string replacement after logging. Broker `return_msg` may embed sensitive parameters; map to a safe reason before display/logging. Retain only sanitized source observations needed to establish known states. Fixture/example values must be synthetic and labeled.

Measure confirmation-validation latency, journal transaction time, broker round-trip, total submit-response time, unknown-state duration, reconnect count, stale duration, duplicate-confirm rejection, queue saturation and dropped optional events. These are measurements to collect, not promised performance numbers. Keep headset frame/interaction performance separate from gateway/broker latency. Do not add a network metrics call to the fast path.

## Verification plan

| Stage | Scope | Pass condition |
| --- | --- | --- |
| M0 document verification | Required docs, internal links, source sheet/cell consistency, source hash, no production implementation | Document relationships and evidence pointers are internally consistent; blockers remain explicit |
| M1 device shell | Actual headset hands/controller/passthrough/lifecycle/comfort | Device evidence distinct from Editor behavior; no broker credentials/network |
| M2–3 transformation and contract tests | Captured source-shaped synthetic fixtures, pagination, identity, decimals, freshness and account isolation | No missing-as-zero, no stale overwrite, unknown semantics fail closed |
| M4–6 adapter tests | Identical Action outcomes across interfaces, bounded Gemini input, malformed/late tools, multimodal source separation | All interfaces use shared handlers; AI cannot reach confirmation/executor |
| M7–8 safety tests | All cases in ORDER_SAFETY, recording fake adapter, crash injection, durable concurrency and reconciliation | At most one local send, no execution from nonphysical confirmation or invalid policy/data |
| M8 integration then controlled live | Verified auth/account/REST/WS semantics and tiny explicitly reviewed real order | Separate broker receipt and execution observations; no inference from API success |
| M9 fault campaign | Full matrix under disconnect/restart/slow dependency/component failures | Manual path remains available when its required financial/security prerequisites are valid; otherwise clearly blocks |

Report setup failures, simulated successes, live responses, device UX findings and unresolved broker semantics separately. No test or live result is claimed merely because the architecture specifies it.
