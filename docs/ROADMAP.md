# Delivery roadmap

Updated 2026-10-27 for the live market pipeline plan and the new source policy. Earlier software/build evidence remains dated 2026-09-18. Continue incrementally through the user's M0–M9 roadmap. Finish supported work and retain explicit external/specification gates; never turn missing evidence into a completed milestone.

| Milestone | Current state | Exit evidence |
|---|---|---|
| M0 Architecture | Documented | Seven design documents and source-mapped Kiwoom capability register |
| M1 Quest shell | Software verified; hardware pending | Original Unity 23-test run, inspected APK, controller simulator scenarios; every headset acceptance row still open |
| M2 Market data | In progress; market-data pipeline implemented, live verification pending | Offline: gateway pytest 108 passed, .NET domain tests 93 passed, spec-example fixture `gateway/tests/fixtures/ka10080_005930.json`. Live verification pending: valid app key required (au10001 returns 8001). Unity EditMode, editor visual and on-device checks pending (license; headset due 2026-10-29) |
| M3 Account | Pending | Verified token-account association, safe aliases, complete positions/balance; no inferred buying power |
| M4 Actions and components | Initial shared shell actions implemented; full workspace pending | Hand/controller/UI parity, independent component lifecycle and workspace persistence |
| M5 Gemini voice | Pending | Approved typed tools, ambiguity/timeout rejection, manual path independent of Gemini |
| M6 Multimodal | Pending | Explicit visual context selection; explanations cannot modify financial truth |
| M7 Order drafts | Pending | Deterministic validation, immutable review, voice toggle default false, allowlists and maximum order value |
| M8 Real execution | Blocked until prior gates and physical confirmation | Verified Kiwoom order semantics, durable attempt journal, no replay on uncertainty; explicit human review of the smallest practical live order |
| M9 Hardening | Scenario design documented; implementation tests incremental | Failure matrix including reconnect, timeout, duplicate confirmation, restart and component/provider isolation |

The user currently has no Quest 3. Continue simulator-based development. Do not request a USB connection until the user has a physical device.

The user requested Meta's design guide and the documents in this repository as design inputs. Implementation decisions and review criteria are tracked in [DESIGN_SYSTEM.md](DESIGN_SYSTEM.md).

Source policy, updated 2026-10-27: the user decided to use the Kiwoom production domain and to accept kiwoom-cli documentation and implementation as evidence, in addition to workbook K1 (SHA-256 unchanged, see [API mapping](KIWOOM_API_MAPPING.md)). This replaces the 2026-09-20 "repository specification only" policy and retires `BLOCKED_BY_MISSING_SPEC` for market data. K-01–K-10 are re-recorded with their evidence in the API mapping; order-related items (K-05–K-07, K-09, and the order parts of K-03/K-04) move to a follow-up plan. Order paths remain out of scope: there is no order endpoint and no live order is authorized by a simulated or offline-tested interaction. Design: [investment-jarvis design](superpowers/specs/2026-10-27-investment-jarvis-design.md); plan: [live market pipeline](superpowers/plans/2026-10-27-live-market-pipeline.md).
