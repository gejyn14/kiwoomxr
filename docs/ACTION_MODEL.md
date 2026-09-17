# Action model

Status: Milestone 0 contract. No dispatcher or transport is implemented.

## Typed envelope

Use a closed, versioned discriminated union in C# and Pydantic. Match `type` to one payload schema, reject extra fields, validate strictly, and avoid coercing strings/floats/booleans into quantities. Generate/check shared wire schemas in Milestone 4; use the same fixtures in both languages.

| Field | Type and rule |
| --- | --- |
| `schemaVersion` | Supported positive integer; reject unknown versions |
| `actionId` | UUID; deduplication identity, not submission authority |
| `type` | Closed ActionType enum |
| `payload` | Typed payload matched to `type` |
| `sourceInterface` | `HAND`, `CONTROLLER`, `UI`, `VOICE`, `GEMINI`; assigned by the ingress adapter |
| `occurredAt` | UTC timestamp from originating client; diagnostic only |
| `correlationId` | UUID carried through gateway and internal broker request record |
| `contextRevision` | Nonnegative revision of relevant selection/session context |

The gateway adds trusted principal/session identity, `receivedAt`, and immutable `Origin` metadata (`inputChannel`, origin adapter, provider turn/tool-call ID if any, parent action ID). Credentials, permission flags and broker account numbers are not payload fields. Arbitrary user-supplied source metadata cannot authorize an operation. Time-to-live and challenge deadlines use server time.

For voice through Gemini, `sourceInterface=GEMINI` and `Origin.inputChannel=VOICE`. A manually typed Gemini request has channel `TEXT`; it cannot spoof a hand source. Voice origin survives edits, copying and physical confirmation. A disabled voice-trading policy cannot be bypassed by re-labeling a voice-created draft as UI-created.

## Initial actions

Types below are application-domain types from [FINANCIAL_MODELS.md](FINANCIAL_MODELS.md). `InstrumentRef` is resolved identity, not freeform name text. A name search returns candidates first; ambiguity never becomes an arbitrary first match.

| Action | Typed payload | Validation | Effect / owner |
| --- | --- | --- | --- |
| `SELECT_INSTRUMENT` | `{instrument: InstrumentRef}` | Resolved supported instrument; expected context revision | Session reducer updates selection, increments revision; bound components refresh independently |
| `OPEN_CHART` | `{instrument?: InstrumentRef, period?: ChartPeriod}` | Explicit instrument or existing selection; supported period | Component registry opens/focuses Chart; optional input pins this component without silently changing global selection |
| `OPEN_ORDERBOOK` | `{instrument?: InstrumentRef}` | Resolved instrument or existing selection | Opens/focuses OrderBook |
| `OPEN_POSITION` | `{instrument?: InstrumentRef}` | Selected authorized account exists; resolved instrument or selection | Opens/focuses Position for that account |
| `COMPARE_INSTRUMENT` | `{instrument: InstrumentRef}` | Current primary exists; second is resolved and different | Sets comparison reference and opens Compare; primary is unchanged |
| `CREATE_ORDER_DRAFT` | `{intent: OrderIntent}` | Strict intent schema; origin/voice gate; selected authorized account resolved server-side; ambiguity fails closed | Order controller invokes gateway OrderDraftValidator and returns structured draft; Trade Mode opens |
| `CANCEL_ORDER_DRAFT` | `{draftId: UUID, expectedRevision: int}` | Principal owns draft; pre-confirmation state and matching revision | Cancels the local draft only; leaves Trade Mode after acknowledgment |
| `CONFIRM_ORDER` | `{draftId: UUID, expectedRevision: int, reviewDigest: string, challengeId: UUID, confirmationEventId: UUID}` | Authenticated physical ingress, single-use current challenge, exact review binding and state; then OrderValidator | Order controller invokes deterministic confirmation service; never accepts new financial fields in this payload |

`OrderIntent` contains explicit instrument, side, integer quantity, order type and limit price when applicable. Gateway binds the account from the session's selected authorized alias and records it in the draft. Manual UI may select an account using the separate authorized account action. Gemini cannot change it. A missing selection yields an incomplete draft or explicit error, never a default account. Material changes require a new draft revision and a new review.

For manual actions, business results are the same regardless of hand, controller or UI entry. Only capability authorization and physical-confirmation/voice policy are intentional interface-sensitive exceptions. An ordinary UI button click is not proof of a physical interaction. A button activated through the restricted hand/controller confirmation adapter retains that physical source; `UI` denotes a generic activation without that physical evidence.

Additional presentation actions use the same envelope: `SELECT_ACCOUNT {account: AccountRef}`, `SET_MODE {mode: FOCUS|WORKSPACE}`, `ADD_COMPONENT {kind, binding}`, `CLOSE_COMPONENT {componentId}`, `SET_COMPONENT_BINDING {componentId, binding}`, `CHANGE_PERIOD {componentId, period}`, `MOVE_COMPONENT {componentId, pose}`, `RESIZE_COMPONENT {componentId, size}`. Validate component identity, finite transforms and comfort bounds. Trade Mode is entered by draft state and cannot be bypassed with `SET_MODE`. Reject close/move/resize of the active confirmation surface. Draft cancellation remains explicit.

## Dispatch and ownership

1. An interface adapter produces one action after its interaction completes. The source never performs financial work itself.
2. The shared dispatcher validates schema and local context. It resolves the appropriate application/component controller, not another interface.
3. Presentation actions update the Quest session reducer and component registry. Financial actions go to the gateway; client validation is only an early usability check.
4. The gateway checks principal/account ownership, schema, state and domain rules. It assigns immutable origin metadata and records the result.
5. Components render capability results or the structured failure. A component never sends business events directly to another component.

For a gateway-originated Gemini presentation action, capture `contextRevision` at voice-turn start and deliver the action to Quest's same dispatcher. Quest serializes it with local actions and acknowledges the resulting revision. A stale revision produces `CONTEXT_CHANGED`; the adapter must clarify/re-evaluate, not overwrite a newer manual choice. Gateway order actions also require the session revision so they cannot bind to a recently changed account or instrument.

Result contract: `ActionResult {actionId, correlationId, status: APPLIED|REJECTED|PENDING, reasonCode?, fieldErrors?, resultingRevision?, draftRef?, orderRef?}`. These are application outcomes. `APPLIED` for confirmation means the action was processed; use the separate order state to describe broker receipt/fills. A timeout never means the financial mutation failed to happen.

Read requests/subscriptions carry a binding revision. Late instrument/account/period results are discarded. Repeated open actions focus an existing matching component in Focus Mode. Workspace duplicates require explicit `ADD_COMPONENT`; they share capabilities through subscriptions, not each other's data stores.

Deduplicate Gemini calls by session/turn/tool-call identity and actions by principal/actionId with payload digest. An ID reused with different content is rejected. Draft creation deduplication returns the existing draft. Confirmation safety additionally uses a permanent order-attempt record; it never relies on an expiring generic action cache. Reconnecting cannot replay a confirmation queue.

## Approved Gemini tools

Provider arguments are strictly validated and resolved by the same application lookup service as manual search. Below, `instrument` accepts an exact resolved reference or a bounded search expression. A search expression cannot go directly into an order draft: resolve uniquely or return candidates for clarification.

| Tool | Arguments | Action mapping |
| --- | --- | --- |
| `select_instrument` | `instrument` | `SELECT_INSTRUMENT` |
| `open_chart` | optional `instrument`, optional supported `period` | `OPEN_CHART` |
| `open_orderbook` | optional `instrument` | `OPEN_ORDERBOOK` |
| `open_position` | optional `instrument` | `OPEN_POSITION` with current authorized account |
| `compare_instrument` | `instrument` | `COMPARE_INSTRUMENT` |
| `create_order_draft` | `instrument`, `side`, `quantity`, `orderType`, optional `limitPrice` | `CREATE_ORDER_DRAFT`; no executable order token is returned |

There is no `execute_order`, `confirm_order`, generic function dispatcher, account switch, or broker-request tool. Gemini never receives confirmation challenges. Unknown tool names, unknown parameters, multiple conflicting order intents, malformed numbers, missing required fields and unsupported products/types produce structured errors. The adapter does not repair intent by guessing. A model response saying “confirmed” is explanatory text with no side effect.

The tool registry invokes only allowlisted action constructors. Provider automatic function execution is disabled. Limit argument size, turn duration and tool-call count; quarantine incomplete/cancelled voice turns. Validate every tool in a response before dispatch. Reject conflicting/batched financial mutations; permit at most one draft creation per explicit order intent. Cancellation/barge-in before draft creation invalidates pending tool work; after draft creation it does not silently cancel the draft. Manual CANCEL remains available.

Example Korean flows:

- “삼성전자 보여줘.” → unique lookup → `SELECT_INSTRUMENT`.
- “차트 열어.” → `OPEN_CHART` using the captured current selection.
- “SK하이닉스 차트 보여줘.” → unique lookup → `SELECT_INSTRUMENT`, await revision acknowledgment → `OPEN_CHART`. Both actions are available manually.
- “SK하이닉스랑 같이 보고 싶어.” → `COMPARE_INSTRUMENT`; do not replace the primary instrument.
- “삼성전자 1주 시장가 매수.” → explicit fields → `CREATE_ORDER_DRAFT` only if voice policy allows; incomplete/blocked conditions remain visible. Physical review and confirmation are separate.

“이거 설명해줘” is an explanation request referencing an explicitly selected component and snapshot revision. It may invoke Gemini multimodal processing, but creates no financial Action by default. A screenshot cannot supply price, quantity, balance or status to an executable intent.

## Contract verification to implement

The same typed intent from hand/controller/UI/voice/Gemini must produce the same non-order state transition and capability request, apart from origin metadata. Test unknown enums/extra fields, negative/overflow/fractional quantities, duplicate IDs with mismatched payloads, stale context, cross-account access, delayed tool calls, and server-assigned voice provenance. Test every path attempting `CONFIRM_ORDER` from VOICE/GEMINI/UI or forged source metadata: no executor call is permitted. See [ORDER_SAFETY.md](ORDER_SAFETY.md) for the stronger confirmation contract.
