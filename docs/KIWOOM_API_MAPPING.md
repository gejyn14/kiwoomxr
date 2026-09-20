# Kiwoom API mapping and specification gates

Status: Milestone 0 source review, 2026-09-17. **No broker API has been called or implemented.**

## Authoritative source boundary

`K1` is the repository-supplied `docs/미국 REST API 문서.xlsx`, inspected read-only. It contains 339 sheets, including domestic stock REST and realtime sheets despite its filename. The workbook labels itself `키움 REST API`. Its release/version/current operational validity is not established by the supplied file. It is the sole authorized Kiwoom specification for this project; no external Kiwoom website, prior knowledge, third-party client or model-generated example was used to fill gaps.

**Source decision, 2026-09-20:** the user reaffirmed use of the current repository specification only. External Kiwoom documentation must not be consulted or imported to resolve gaps. K1's local SHA-256 was rechecked on this date and is unchanged; no additional specification was found. K-01–K-10 remain blocked. This is a source-policy/hash verification, not a new broker integration test.

The original workbook remains local and is excluded from Git because its examples contain credential-shaped key/token values. A fresh clone must receive the authorized source file separately and verify its hash before extending any integration. This mapping preserves sheet/cell provenance; it does not substitute for missing specification semantics.

SHA-256 of K1:

```text
63216a5a0be7a11e7844a07ac3b320a93004caa0eb70d3133b64930444a6e2b1
```

Source notation consists of `K1:`, the sheet name, `!`, and a cell/range such as `B22:G42`. Tables below transcribe only the fields needed for the planned capability. Unlisted fields are not silently supported. Sheet `C5` is the API name, `C6` its ID, `C8:C13` transport metadata. API names/IDs below are the names in this workbook, not independent certification of its publication provenance.

Status vocabulary:

- `MAPPED_FROM_REPOSITORY`: listed names/fields are present; this is documentation evidence, not a tested working integration.
- `BLOCKED_BY_MISSING_SPEC`: a necessary rule is missing, ambiguous or contradictory. Stop the affected integration. Keep any verified portions in the mapping, but do not ship guessed transport/parsing/order semantics.
- `OUT_OF_SCOPE`: intentionally excluded products/operations. Their presence in the workbook does not enable them.

All listed mappings currently have untested runtime behavior. Authentication gap K-01 transitively blocks credential-dependent live capability calls. The other gates specify additional capability-local blockers. Source fixtures and Quest shell work may proceed without live integration.

## Transport and common errors

| Item | Literal specification | Source / restriction |
| --- | --- | --- |
| REST production origin | `https://api.kiwoom.com` | K1:ka10001!C8:C13; POST, JSON, `application/json;charset=UTF-8` |
| REST simulation origin | `https://mockapi.kiwoom.com` | K1:ka10001!C10; no assumption that every API is supported in simulation |
| Realtime production origin and path | `wss://api.kiwoom.com:10000` + `/api/dostk/websocket` | K1:주식체결(0B)!C8:C13; sheet also labels Method POST, unresolved for WS lifecycle |
| Realtime simulation origin | `wss://mockapi.kiwoom.com:10000` | K1:주식체결(0B)!C10 |
| REST headers | `api-id`, `authorization` required; authorization describes Bearer token; `cont-yn`, `next-key` optional | K1:ka10001!B18:G21. Do not log header values. Token issuance conflict is K-01. |
| Continuation | Response `cont-yn=Y` instructs next request to use response `cont-yn` and `next-key` | K1:ka10099!B20:G21 and B26:G27; complete all pages for completeness claims |
| Response result fields | `return_code`, `return_msg` appear in examples; order/token examples show code 0 | K1:au10001!A36 and kt10000!A38. Response tables omit these in those sheets. Error body/HTTP relationship and complete success semantics need K-04 clarification. |

Never add an account, idempotency or correlation header absent from the source. Keep application correlation IDs in gateway records. The generic header description says a seven-character TR while realtime IDs are two characters; do not turn this boilerplate into a new WS request format.

Error policy `E-REST` applies to every REST row below. HTTP/transport failure and broker result codes are separate. Parse only documented fields, preserve a sanitized observation, and fail closed on unknown/missing/contradictory results. A missing payload is unavailable, never zero. Read retries are bounded and must honor verified quotas; orders never automatically retry. Mapped error families:

| Documented codes | Source meaning (condensed) | Application behavior | Source |
| --- | --- | --- | --- |
| `1501`, `1504`, `1505` | Missing/unsupported API ID or URI/API mismatch | Configuration/spec error; do not try alternate TRs | K1:오류코드!B4:C6 |
| `1511`–`1517` as listed | Missing fields/header/token or invalid format/grant | Invalid request/auth; no guessing or silent coercion | K1:오류코드!B7:C13 |
| `1687` | Recursive calls restricted | Stop recursion; investigate caller | K1:오류코드!B14:C14 |
| `1700`, `1701`, `1702` | API/global/group request limit exceeded | Rate-limited reads; no invented quota or order replay | K1:오류코드!B15:C17 |
| `1901`, `1902`, `1903` | Market/instrument information absent | Lookup unavailable; no default instrument | K1:오류코드!B18:C20 |
| `1999` | Unexpected error | Unknown failure; after possible order send, UNKNOWN | K1:오류코드!B21:C21 |
| `8001`, `8002`, `8003`, `8005`, `8006`, `8009` | Key/secret/token verification, lookup or creation failures | Authentication unavailable; no secrets in error text | K1:오류코드!B22:C27 |
| `8010` | Token issuance IP and request IP differ | Configuration/auth failure; do not bypass network binding | K1:오류코드!B28:C28 |
| `8011`, `8012`, `8015`, `8016`, `8020` | Grant, revocation or key/secret input errors | Fail request, inspect secure config | K1:오류코드!B29:C33 |
| `8030`, `8031` | Live/simulation credential/token mismatch | Reject; never switch environment automatically | K1:오류코드!B34:C35 |
| `8040`, `8050`, `8103` | Terminal/token authentication failure | Block affected access | K1:오류코드!B36:C38 |
| `8104`, `8200` | Unsupported in simulation / corporate-client restriction | Unavailable; do not claim feature tested | K1:오류코드!B39:C40 |

`E-WS`: the registration/unregistration response describes `return_code` 0 normal / 1 error, `return_msg`, and echoed `trnm`. Realtime data uses `trnm=REAL` and does not carry that registration result code. Never interpret subscription success as an order result. Source: K1:주식체결(0B)!B33:G40. Malformed/unknown events are quarantined, affected data becomes unavailable/stale, and no invented parsing fallback is used.

## REST capability mapping

All inputs below are body fields in addition to the verified common headers. Requiredness follows the workbook; application validation may be stricter, but is identified separately. All endpoints are paths under the REST origin above and use POST.

| Capability | Official API/TR | Endpoint | Required inputs / documented optional inputs | Relevant outputs | Realtime equivalent | Error behavior | Authoritative source and gates |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Authentication | 접근토큰 발급 `au10001` | `/oauth2/token` | Required `grant_type=client_credentials`, `appkey`, `secretkey` | `expires_dt`, `token_type`, `token` | None specified | E-REST; no bootstrapping with a fabricated token | K1:au10001!C5:C13, B18:G24, B30:G32, A34:A36. Mapped fields; K-01/K-04 block auth implementation. |
| Token revocation | 접근토큰폐기 `au10002` | `/oauth2/revoke` | Required `appkey`, `secretkey`, `token` | No response body fields tabulated | None specified | E-REST; do not report revocation merely on HTTP success | K1:au10002!C5:C13, B22:G24. K-04 blocks complete success mapping. |
| Instrument catalog/search | 종목정보 리스트 `ka10099` | `/api/dostk/stkinfo` | Required `mrkt_tp`; documented `0` KOSPI, `10` KOSDAQ for initial search scope | `list`: `code`, `name`, `marketCode`, `marketName`, `state`, `lastPrice`, `orderWarning`, `nxtEnable` | No name-search realtime equivalent established | E-REST; complete pagination; ambiguous matches remain candidates | K1:ka10099!C5:C13, B22:G42. Search is application filtering of the catalog; do not invent a broker name-search field. K-05 for product eligibility. |
| Instrument details | 종목정보 조회 `ka10100` | `/api/dostk/stkinfo` | Required `stk_cd` (six-digit code in this sheet) | `code`, `name`, `state`, `marketCode`, `marketName`, `lastPrice`, `orderWarning`, `nxtEnable` | None selected | E-REST; missing identity rejected | K1:ka10100!C5:C13, B22:G41. K-05; prior close is not current quote. |
| Quote / instrument price facts | 주식기본정보요청 `ka10001` | `/api/dostk/stkinfo` | Required `stk_cd`; documented exchange-specific code forms | `stk_cd`, `stk_nm`, `cur_prc`, `pre_sig`, `pred_pre`, `flu_rt`, `trde_qty`; `upl_pric`, `lst_pric`, `base_pric` are candidate source facts for later validated rules | 주식체결 `0B` for trades/current price | E-REST; unknown sign/time semantics makes affected normalized values unavailable | K1:ka10001!C5:C13, B22:G29, B55:G57, B64:G68. K-03/K-08; an upper-price field alone does not prove an order bound. |
| Order-book snapshot | 주식호가요청 `ka10004` | `/api/dostk/mrkcond` | Required `stk_cd` | `bid_req_base_tm`; `sel_fpr_bid`, `sel_fpr_req`, `buy_fpr_bid`, `buy_fpr_req`; explicit higher levels; `tot_sel_req`, `tot_buy_req` | 주식호가잔량 `0D` | E-REST; absent level/time must not become zero/current | K1:ka10004!C5:C13, B22:G22, B28:G96. K-03/K-08 and K-02 for stream. |
| Daily candles | 주식일봉차트조회요청 `ka10081` | `/api/dostk/chart` | Required `stk_cd`, `base_dt` YYYYMMDD, `upd_stkpc_tp` 0/1 | `stk_cd`, `stk_dt_pole_chart_qry`: `cur_prc`, `trde_qty`, `dt`, `open_pric`, `high_pric`, `low_pric` | No authoritative candle stream selected; `0B` trades do not automatically finalize candles | E-REST; honor adjustment/date and continuation; invalid bars unavailable | K1:ka10081!C5:C13, B22:G24, B30:G41. Adjustment note G24 matters. K-03/K-08 for interpretation/session completeness. |
| Minute candles | 주식분봉차트조회요청 `ka10080` | `/api/dostk/chart` | Required `stk_cd`, `tic_scope`, `upd_stkpc_tp`; optional `base_dt`. Listed minutes: 1,3,5,10,15,30,45,60 | `stk_cd`, `stk_min_pole_chart_qry`: `cur_prc` labeled current/close, `trde_qty`, `cntr_tm`, `open_pric`, `high_pric`, `low_pric` | No candle stream selected | E-REST; no unsupported interval/aggregation assumption | K1:ka10080!C5:C13, B22:G25, B31:G40. K-03/K-08. |
| Token-account identity / safe selection | 계좌번호조회 `ka00001` | `/api/dostk/acnt` | No request body fields tabulated; authenticated token context | `acctNo`, described as current token's account, ten digits; keep server-side | `00`/`04` are account events, not account discovery | E-REST; absent or mismatched account blocks alias binding | K1:ka00001!C5:C15, B18:G27. Account selector lists provisioned verified aliases; this API does not document enumerating arbitrary customer accounts. |
| Positions / account valuation | 계좌평가잔고내역요청 `kt00018` | `/api/dostk/acnt` | Required `qry_tp` 1 aggregate / 2 individual; `dmst_stex_tp` KRX/NXT | `tot_pur_amt`, `tot_evlt_amt`, `tot_evlt_pl`, `tot_prft_rt`, `prsm_dpst_aset_amt`; `acnt_evlt_remn_indv_tot` with `stk_cd`, `stk_nm`, `rmnd_qty`, `trde_able_qty`, `pur_pric`, `cur_prc`, `evlt_amt`, `crd_tp`, `crd_tp_nm` | 잔고 `04`; partial events do not replace complete account snapshot | E-REST; pagination/credit-classification gaps block trading, missing position is not zero | K1:kt00018!C5:C13, B22:G23, B29:G60. K-03/K-05/K-06. |
| Cash balance | 예수금상세현황요청 `kt00001` | `/api/dostk/acnt` | Required `qry_tp`: 3 estimated / 2 normal | `entr`, `pymn_alow_amt`, `ord_alow_amt`, `100stk_ord_alow_amt` remain distinct source measures | No complete cash-balance realtime equivalent established | E-REST; do not equate deposit or estimate with cash buying power | K1:kt00001!C5:C13, B22:G22, B28:G28, B57:G64. K-06 for executable funding. |
| Buying power / cash capacity | 주문인출가능금액요청 `kt00010` | `/api/dostk/acnt` | Required `stk_cd`, `trde_tp` 1 sell / 2 buy, `uv`; optional `io_amt`, `trde_qty`, `exp_buy_unp` | `profa_100ord_alow_amt`, `profa_100ord_alowq`, `ord_alowa` (orderable cash), `cmsn`, `pur_exct_amt`; other margin-rate buckets excluded | None sufficient specified | E-REST; no inference from credit-enabled buckets; unknown price/fee/cash scope blocks execution | K1:kt00010!C5:C13, B22:G27, B45:G46, B53:G59. K-06/K-07. |
| Cash buy submission | 주식 매수주문 `kt10000` | `/api/dostk/ordr` | Required `dmst_stex_tp`, `stk_cd`, `ord_qty`, `trde_tp`; optional in table `ord_uv`, `cond_uv` | `ord_no`, `dmst_stex_tp`; example also has result fields | 주문체결 `00` plus authoritative readback | E-REST; after possible send, ambiguous result is UNKNOWN, never retry | K1:kt10000!C5:C13, B22:G34, A38. K-04/K-05/K-06/K-07/K-09; live submission blocked. |
| Cash sell submission | 주식 매도주문 `kt10001` | `/api/dostk/ordr` | Required `dmst_stex_tp`, `stk_cd`, `ord_qty`, `trde_tp`; optional in table `ord_uv`, `cond_uv` | `ord_no`, `dmst_stex_tp` | 주문체결 `00` plus readback | E-REST; verify cash tradable shares; no automatic retry | K1:kt10001!C5:C13, B22:G34. Same submission gates; live submission blocked. |
| Unfilled order observations | 미체결요청 `ka10075` | `/api/dostk/acnt` | Required `all_stk_tp` 0 all / 1 instrument, `trde_tp` 0 all / 1 sell / 2 buy, `stex_tp` 0 integrated / 1 KRX / 2 NXT; optional `stk_cd` | `oso`: `acnt_no`, `ord_no`, `stk_cd`, `ord_stt`, `ord_qty`, `ord_pric`, `oso_qty`, `orig_ord_no`, `cntr_no`, `cntr_pric`, `cntr_qty` | 주문체결 `00` | E-REST; absent row is not a final order outcome | K1:ka10075!C5:C13, B22:G25, B31:G61. K-09 for exact state/recovery. |
| Fill observations | 체결요청 `ka10076` | `/api/dostk/acnt` | Required `qry_tp` 0 all / 1 instrument, `sell_tp` 0 all / 1 sell / 2 buy, `stex_tp`; optional `stk_cd`, `ord_no` | `cntr`: `ord_no`, `ord_qty`, `cntr_pric`, `cntr_qty`, `oso_qty`, `ord_stt`, `orig_ord_no`, `ord_tm`, `stk_cd` | 주문체결 `00` | E-REST; supplied `ord_no` is a historical search boundary, not an exact-ID lookup | K1:ka10076!C5:C13, B22:G26, B32:G51, especially G25. K-09. |
| Order history for reconciliation | 계좌별주문체결내역상세요청 `kt00007` | `/api/dostk/acnt` | Required `qry_tp`, `stk_bond_tp`, `sell_tp`, `dmst_stex_tp`; optional `ord_dt`, `stk_cd`, `fr_ord_no`. `fr_ord_no` excludes earlier orders; empty means all | `acnt_ord_cntr_prps_dtl`: `ord_no`, `stk_cd`, `trde_tp`, `crd_tp`, `ord_qty`, `ord_uv`, `cnfm_qty`, `acpt_tp`, `ord_tm`, `ori_ord`, `cntr_qty`, `cntr_uv`, `ord_remnq`, `mdfy_cncl` | 주문체결 `00` | E-REST; query bounds and pagination must be complete; enum/quantity meaning cannot be guessed | K1:kt00007!C5:C13, B22:G28, B34:G56. K-09. |

`kt10000`/`kt10001` document `trde_tp=0` as 보통 and `3` as 시장가 (K1:kt10000!G26 and K1:kt10001!G26), plus many excluded variants. Application LIMIT→보통 is a proposed mapping pending K-07 confirmation of price requirements and execution semantics; MARKET→시장가 is the documented name match but is not enabled without K-07/K-06 safety support. Do not reuse `trde_tp` values from account queries: their codes have different meanings. The smallest target subset contains no `cond_uv`; omission/empty/zero representation must be documented, not copied from an unrelated example.

No account field is present in the order body table. Resolve the server account alias to its verified token binding; do not add an invented account-number field to orders. K1:ka00001!A15 explicitly associates an account with the current token, and K1:주문체결(00)!A15 describes events for the account that issued the token. Reverify the alias mapping whenever a credential/token context changes.

## Explicit order-book field mapping

Do not generate field names from an English ordinal convention: the workbook uses `3th` and `fpr` spellings. The table covers the initial ten levels, sourced from K1:ka10004!B30:G87. WS keys come from K1:주식호가잔량(0D)!B42:G100.

| Level | REST ask price / quantity | REST bid price / quantity | WS ask price / quantity | WS bid price / quantity |
| --- | --- | --- | --- | --- |
| 1 | `sel_fpr_bid` / `sel_fpr_req` | `buy_fpr_bid` / `buy_fpr_req` | `41` / `61` | `51` / `71` |
| 2 | `sel_2th_pre_bid` / `sel_2th_pre_req` | `buy_2th_pre_bid` / `buy_2th_pre_req` | `42` / `62` | `52` / `72` |
| 3 | `sel_3th_pre_bid` / `sel_3th_pre_req` | `buy_3th_pre_bid` / `buy_3th_pre_req` | `43` / `63` | `53` / `73` |
| 4 | `sel_4th_pre_bid` / `sel_4th_pre_req` | `buy_4th_pre_bid` / `buy_4th_pre_req` | `44` / `64` | `54` / `74` |
| 5 | `sel_5th_pre_bid` / `sel_5th_pre_req` | `buy_5th_pre_bid` / `buy_5th_pre_req` | `45` / `65` | `55` / `75` |
| 6 | `sel_6th_pre_bid` / `sel_6th_pre_req` | `buy_6th_pre_bid` / `buy_6th_pre_req` | `46` / `66` | `56` / `76` |
| 7 | `sel_7th_pre_bid` / `sel_7th_pre_req` | `buy_7th_pre_bid` / `buy_7th_pre_req` | `47` / `67` | `57` / `77` |
| 8 | `sel_8th_pre_bid` / `sel_8th_pre_req` | `buy_8th_pre_bid` / `buy_8th_pre_req` | `48` / `68` | `58` / `78` |
| 9 | `sel_9th_pre_bid` / `sel_9th_pre_req` | `buy_9th_pre_bid` / `buy_9th_pre_req` | `49` / `69` | `59` / `79` |
| 10 | `sel_10th_pre_bid` / `sel_10th_pre_req` | `buy_10th_pre_bid` / `buy_10th_pre_req` | `50` / `70` | `60` / `80` |

These are field correspondences only. They do not resolve signed-price normalization, missing-level semantics or snapshot/event sequencing.

## Realtime mapping

All four rows below share the documented WSS origin/path above. Registration fields in each sheet's B22:G27 are `trnm` (`REG`/`REMOVE`), `grp_no`, `refresh` (REG: 0 clears prior registrations / 1 preserves; unnecessary for REMOVE), `data`, child `item`, child `type`. The exact request nesting and lifecycle remain blocked by K-02; **do not construct a guessed registration frame** from this table. The response example establishes a `data` list containing `type`, `name`, `item`, `values` for its shown event, not a complete lifecycle specification.

| Capability / official item | Required registration context | Relevant documented response fields | REST equivalent | Error behavior | Authoritative source / status |
| --- | --- | --- | --- | --- | --- |
| Live quote — 주식체결 `0B` | `type=0B`, instrument item with documented venue code; common fields above | `20` trade time HHmmss; `10` current price; `11` change; `12` percentage; `27` ask; `28` bid; `15` trade quantity (+ buy / − sell); `13` cumulative volume; `16/17/18` open/high/low | `ka10001` snapshot | E-WS; no live claim until valid current data | K1:주식체결(0B)!C5:C13, B22:G53, A85:A87. K-02/K-03/K-08. |
| Live book — 주식호가잔량 `0D` | `type=0D`, instrument item and common fields | `21` quote time HHmmss; explicit level keys above; `121` total ask quantity; `125` total bid quantity | `ka10004` | E-WS; missing fields do not zero existing levels or prove a delta | K1:주식호가잔량(0D)!C5:C13, B22:G27, B41:G104. K-02/K-03/K-08. |
| Order events — 주문체결 `00` | `type=00`; A15 states token-account events arrive irrespective of registered instrument item | `9201` account, `9203` order, `9001` instrument, `913` state, `900` order qty, `901` order price, `902` unfilled qty, `903` cumulative execution amount, `904` original order, `905/906/907` order/type/side, `908` time, `909` fill ID, `910/911` fill price/qty, `919` rejection reason | `ka10075`, `ka10076`, `kt00007` | E-WS; account authentication/filtering mandatory; event scope matters | K1:주문체결(00)!C5:C15, B22:G27, B41:G75. K-02/K-09. |
| Position events — 잔고 `04` | `type=04`; A15 associates events with token account irrespective of item | `9201` account, `9001` instrument, `917` credit class, `916` loan date, `930` held qty, `931` purchase price, `933` orderable qty | `kt00018`; no complete cash balance equivalent established | E-WS; use as verified observation/invalidation, not complete-account truth | K1:잔고(04)!C5:C15, B22:G27, B41:G50. K-02/K-05/K-06. |

No `LOGIN`, `PING`, login, reconnect or per-second quota definition was found in the inspected workbook cell text. This is an inventory finding, not proof that the operational API has no such requirements. Neither a WebSocket login message nor a heartbeat reply is specified here.

## Order state evidence

| Source observation | Permitted application interpretation now | What remains blocked |
| --- | --- | --- |
| Order HTTP response / example `return_code=0`, `ord_no` | Store returned reference and response evidence; execution outcome remains UNKNOWN absent further authoritative evidence | Complete response acceptance semantics, K-04; never FILLED |
| `00` field `913=접수` | RECEIVED only for a correctly associated original new-order event | Do not treat a cancel/modify receipt as original order acceptance |
| `00` field `913=거부` | REJECTED only when association and event scope prove this is the new order's rejection | Rejected cancellation/modify is not rejection of the original order |
| `00` field `913=확인` | UNKNOWN with raw state retained | No source definition establishes ACCEPTED |
| `00` field `913=체결` with `909/910/911` | Preserve execution observation, but no final cumulative state guess | Distinguish per-event/cumulative quantities, duplicate/order sequence and terminal state; K-09 |
| `00` field `913=취소` | Preserve cancellation observation, UNKNOWN if scope/remaining fills are not established | Partial cancellation/original-order linkage and terminal semantics; K-09 |
| `ka10075.ord_stt`, `ka10076.ord_stt`, `kt00007.acpt_tp/mdfy_cncl` | Preserve sourced raw values; unknown mappings stay UNKNOWN | Tables name fields but do not provide complete enums/transition definitions |
| No matching row after query | No negative proof; retain prior known state/UNKNOWN | Coverage, visibility delay, query bounds, retention and missing-response recovery guarantees |

`913` literals are explicitly listed at K1:주문체결(00)!G46. Quantity/association fields are at B41:G59. REST status fields are K1:ka10075!B37:G37, ka10076!B43:G43, kt00007!B42:G53. Application PARTIALLY_FILLED/FILLED/CANCELLED reducers cannot be enabled solely from plausible field names or one sample response. A zero remaining quantity could have explanations other than a full fill.

## Blocking specification register

Every row is **BLOCKED_BY_MISSING_SPEC** until the stated repository evidence is supplied and reviewed. Do not resolve these through undocumented trial-and-error against live orders.

| ID | Missing/contradictory evidence | Affected integration | Evidence required to unblock |
| --- | --- | --- | --- |
| K-01 | Token issuance table requires existing `authorization` at au10001 E19; body example only documents credential grant. Initial bootstrap and expiry timezone/renewal are not established. | M2 authentication and all dependent live calls | Authoritative issuance header exception/requirements, expiry format/timezone and token renewal/IP rules, including token-account binding verification. Do not manufacture a bootstrap bearer token. |
| K-02 | Realtime sheets label Method POST; no login/heartbeat/reconnect protocol. `주식체결(0B)!A85` has `data:null` and literal top-level `- item`/`- type`, conflicting with a nested-list interpretation of B25:B27. | All M2/M3/M8 WebSocket integrations | Complete WSS handshake/auth, registration/unregistration JSON shapes and types, heartbeat, response/errors, group/subscription limits, token expiry behavior, replay/gap/order and snapshot recovery semantics. |
| K-03 | Several price fields say signed numeric without fully defining sign normalization. `0B` G47 defines trade-volume sign, which must not be reused for price/P&L. Many type cells are blank. | Normalized quote/book/minute candles and order inputs derived from them | Per-field sign/scale/null/sentinel rules and numeric wire types. Preserve signed P/L; never blanket `abs()`. |
| K-04 | Common result fields appear in examples but are absent from some response tables; full HTTP/result/success/uncertain rejection rules and revocation response are incomplete. Some response examples are not strict JSON. | REST error codecs and especially submission/receipt mapping | Complete success/error envelopes, value types, HTTP relation, order receipt vs processing guarantees and safe failure classification. |
| K-05 | Catalog/position metadata exists, but full product eligibility, stock status and cash/credit code mappings are not supplied for deterministic exclusion. | Product whitelist and cash-equity-only orders | Allowed security classes and verified instrument eligibility; suspension/restriction rules and account/position cash/credit codes. Six-digit codes and market membership alone are insufficient. |
| K-06 | Cash/margin buckets and fee fields are listed; executable cash-only funding/fee/tax/settlement and reservation semantics are not fully defined. `kt00010.uv` is required with no verified MARKET valuation rule. | Buying power, balance-as-capacity, BUY/SELL preflight | Authoritative cash-only orderable amount/quantity interpretation, fee basis, pending-order reservation and sellable-cash-share rules; MARKET query requirements. |
| K-07 | `0:보통` and `3:시장가` are listed but conditional price requirements, null/omission handling, tick/lot/session rules and provable maximum execution-value bounds are incomplete. | Real order encoding, maximum-value enforcement, Demo F | Exact smallest cash LIMIT/MARKET request rules (including unused `ord_uv`/`cond_uv`), tick/lot/session constraints, and authoritative bound valid for side/type/session. `upl_pric` alone is not proof. |
| K-08 | `ka10004!G28` labels a book time as YYYYMMDD while WS `0D!G41` uses HHmmss; quote lacks a verified exchange timestamp; date rollover/session/timezone and chart completion/adjustment semantics need clarification. | Freshness, chart finality, snapshot/stream alignment and order evidence age | Exact time formats/timezone/calendar, current-day candle semantics (including daily `cur_prc`), adjustment continuity and reliable ordering/quality rules. Unknown source time stays unknown. |
| K-09 | State names and quantities exist but full acceptance/fill/cancel semantics, identity uniqueness, event ordering/deduplication, retention/visibility and lost-response reconciliation are incomplete. No broker idempotency contract is supplied. | M8 order-state tracking/recovery and safe live activation | Authoritative order lifecycle definitions, fill identity/cumulative rules, complete order lookup/history pagination coverage, date/account scope and reconciliation procedure when no `ord_no` was received. Retain UNKNOWN when unresolved; no invented idempotency field. |
| K-10 | Rate-limit errors exist without numeric quota, backoff, subscription-cap or supported simulation coverage specification. | Operational polling, reconnect/subscription planning and integration tests | API/global/group quotas, concurrent connection/subscription limits and environment-specific feature support. Do not invent requests-per-second values. |

## Unblocking workflow

The current authorized source set is K1 only. Do not fetch external documentation to populate this register. If the user later supplies additional authoritative specification in the repository or explicitly changes the source policy, review that material without publishing credentials or private account examples. Record its path, version/date and SHA-256. Update the relevant mapping with exact cell/page/section citations and separate remaining ambiguities. Implement only the now-supported smallest capability slice, with source-shaped contract fixtures and deterministic negative tests. Record live/simulation/device evidence separately. A successful guessed request is not a substitute for a specification, and closing a market-data gap does not automatically enable orders.

Initial excluded integrations are news, credit/margin, derivatives, overseas products, advanced routing, complex orders and broker amendment/cancellation. `CANCEL_ORDER_DRAFT` is purely an application action and has no Kiwoom TR mapping. The presence of overseas APIs in K1 does not expand scope.
