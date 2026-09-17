# Delivery roadmap

Updated 2026-09-18. Continue incrementally through the user's M0–M9 roadmap. Finish supported work and retain explicit external/specification gates; never turn missing evidence into a completed milestone.

| Milestone | Current state | Exit evidence |
|---|---|---|
| M0 Architecture | Documented | Seven design documents and source-mapped Kiwoom capability register |
| M1 Quest shell | Software verified; hardware pending | Original Unity 23-test run, inspected APK, controller simulator scenarios; every headset acceptance row still open |
| M2 Market data | In progress; live Kiwoom blocked by missing specification | Typed observations, strict transport, local gateway response, redesigned simulator UI and updated APK verified; real auth/REST/WS require reviewed source material |
| M3 Account | Pending | Verified token-account association, safe aliases, complete positions/balance; no inferred buying power |
| M4 Actions and components | Initial shared shell actions implemented; full workspace pending | Hand/controller/UI parity, independent component lifecycle and workspace persistence |
| M5 Gemini voice | Pending | Approved typed tools, ambiguity/timeout rejection, manual path independent of Gemini |
| M6 Multimodal | Pending | Explicit visual context selection; explanations cannot modify financial truth |
| M7 Order drafts | Pending | Deterministic validation, immutable review, voice toggle default false, allowlists and maximum order value |
| M8 Real execution | Blocked until prior gates and physical confirmation | Verified Kiwoom order semantics, durable attempt journal, no replay on uncertainty; explicit human review of the smallest practical live order |
| M9 Hardening | Scenario design documented; implementation tests incremental | Failure matrix including reconnect, timeout, duplicate confirmation, restart and component/provider isolation |

The user currently has no Quest 3. Continue simulator-based development. Do not request a USB connection until the user has a physical device.

The user requested Meta's design guide and the documents in this repository as design inputs. Implementation decisions and review criteria are tracked in [DESIGN_SYSTEM.md](DESIGN_SYSTEM.md).

A pending source-scope question asks whether official Kiwoom documentation may supplement the repository workbook. Until answered, the original repository-only source boundary applies. The supplied workbook's K-01–K-10 gates remain open. No live brokerage request or order is authorized by a simulated successful interaction.
