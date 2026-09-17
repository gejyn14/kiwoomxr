# Milestone 1 — Quest shell

**Status: SOFTWARE_VERIFIED_HARDWARE_ACCEPTANCE_PENDING — Milestone 1 remains incomplete until Quest 3 acceptance passes.**

Observed on 2026-09-17 (Asia/Seoul). The user has no Quest 3 yet. Editor/simulator evidence cannot close the hardware acceptance gate. No real financial API or order path is implemented. Milestone 2 must not bypass [Kiwoom specification blockers](KIWOOM_API_MAPPING.md).

## Delivered scope

- Focus Mode with one selected instrument, a Korean chart surface and reachable control dock.
- On-demand synthetic orderbook/comparison and explicitly unavailable position state.
- Meta comprehensive rig, OpenXR, passthrough underlay, hand/controller poke and ray interaction bindings.
- Standard SDK grab handles, two-point chart/secondary scaling, bounded placement and recentering.
- Shared typed shell actions, immutable selection context, canonical instrument validation, revision checks and presentation-only duplicate detection.
- Reproducible configure/generate/validate/test/build commands, generated scene/settings/font, actual UPM package lock and asset metadata.
- Editor-only simulator launcher and read-only evidence snapshots; no behavior injection through the snapshot writer.
- Build guard that removes SDK-generated development connection credentials from APK resources.

Minimal shell actions are introduced now so M1 interfaces share behavior. Milestone 4 still owns full action/component integration and Workspace Mode. Voice/Gemini source enum values and parity tests do not mean those interfaces exist yet.

The secondary surface is a presentation-only fixture renderer. Capability-backed components replace it at later milestones. Candle shapes, reference prices and orderbook increments are arbitrary synthetic fixtures, not market data or exchange tick rules. Position data is unavailable, never zero-filled. There is no gateway, account access, voice/Gemini integration, order draft or submission.

## Current executed evidence

| Check | Result | Scope |
|---|---|---|
| Pinned Unity package resolution and semantic compilation | **PASS** | Actual Unity 6000.0.67f1 and Meta SDK 205.0.0 |
| Scene generation | **PASS** | Standard vendor rig/interactions and imported TMP resources |
| Saved-scene/build configuration validation | **PASS** | Missing scripts, component bindings, hands/controllers, OpenXR, passthrough and font checks |
| Unity EditMode tests | **23 passed, 0 failed, 0 skipped** | Shared domain tests inside Unity |
| Independent .NET domain tests | **23 passed, 0 failed, 0 skipped** | Exact same source/tests, .NET 8.0.425 |
| Source preflight | **PASS** | Pins, domain boundary, no authored financial networking/orders |
| Android ARM64 IL2CPP APK | **PASS** | Final build inspected: passthrough required, optional hands, Quest 3 metadata, no Operator/capture/phone/storage artifacts |
| Simulator runtime | **PASS — bounded controller scenarios** | Selection, secondary components, connected instrument updates, move, resize, recenter and close; screenshots and snapshots inspected |
| Quest 3 hardware acceptance | **NOT RUN** | No headset available |

The sanitized [verification record](evidence/milestone1-2026-09-17-verified.json) links local evidence by path/hash. Final APK: **97,400,636 bytes**, SHA-256 `e567faa931ef99e2924d30743e01cd4fc5b807e5fa3faabec1745d54c0a97ffc`. Unity tests were rerun after the runtime fixes: **23/23 passed**. The only manifest permissions are Internet, hand tracking and the app-scoped non-exported dynamic receiver permission. The known local SDK token was absent from all decompressed APK members; this bounded check is not a complete secret audit.

Run evidence stays in ignored `artifacts/milestone1/`. Shared records contain sanitized results/hashes, not credentials or device identifiers. Unity compilation, domain tests, simulator observations and hardware acceptance are distinct evidence levels.

## Simulator observations

Inputs were supplied as controller poses and trigger values through the SDK Operator; no application action was injected directly. The normal SDK UI/interaction callbacks reached the shared dispatcher. State snapshots were read-only observations.

- Samsung → SK Hynix selection, then orderbook, unavailable position and comparison each produced one controller-sourced Action (revisions 1–4).
- Chart top-handle grab moved the surface and remained stable after release. A two-controller grab reached the configured 1.35 scale bound; recenter restored initial positions and scale 1.
- Reopening orderbook and selecting Samsung updated chart and orderbook together (revisions 5–6). The structured fixture values matched; a focused secondary screenshot was inspected.
- Closing the secondary produced revision 7. Korean labels, candlesticks and synthetic-data status remained visible in the final [Focus screenshot](images/milestone1-focus.png).

This verifies simulator controller ray/trigger interaction only. Direct poke, physical hand tracking, real passthrough, seated ergonomics and on-device performance still require the headset. The first resize automation attempt moved an aliased pose object incorrectly; the corrected controller-motion attempt is recorded separately.

## Failure and repair history

1. Initial Unity attempts exited **198** because no valid license was activated. Those were setup failures, not compiler evidence. [Initial record](evidence/milestone1-2026-09-17.json), [follow-up](evidence/milestone1-2026-09-17-followup.json), [three-turn blocker audit](evidence/milestone1-2026-09-17-blocked.json). License activation by the user later resolved that blocker.
2. First licensed import failed because the SDK's NavMesh types require Unity's built-in AI module. Adding that explicit module resolved compilation. The actual editor-bundled Test Framework version is pinned to 1.6.0.
3. Scene generation first failed because a TMP shader existed while `TMP Settings` did not. The next attempt established that Essential Resources import is asynchronous. The generator now waits for the import-completed event before creating fonts/scene; subsequent generation and validation passed.
4. SDK 205's build processor injects local AgentBridge credentials even with its assistant disabled. The final-order build callback clears those credential fields and disables the assistant. Its local settings asset is ignored by Git.

5. The first simulator run exposed inactive-interactor identifier access, placement before tracked head initialization, and Korean text disappearing under TMP vertical ellipsis. These were fixed by resolving only live interactors at event time, placing after tracked rig updates, and fitting Noto KR text to its rectangles. The Editor evidence writer also now respects Unity component null semantics. A subsequent fresh-process simulator retest verified rendering and real controller input events. Floor-origin changes are observed before initial layout placement. Editor interaction follows XR input focus when the simulator is present, so a focused XR session remains usable when the desktop Editor window loses focus.
6. Recompiling and re-entering Play Mode in the same Editor session produced SDK lifecycle exceptions. The successful verification used a fresh Editor process with zero exception entries in its runtime log. Hot reload remains an open Editor limitation; this is not evidence for Quest pause/resume.
7. First APK inspection found capture/phone/storage permissions introduced by the SDK Operator AAR, which is automatically included in Development builds. This project uses Operator only for Editor simulation; its Android AAR is now excluded with Unity's build inclusion callback. The final APK inspection passed: the Operator library and capture/phone/storage permissions are absent. The SDK restores local Editor connection values after building; verification checks the emitted APK rather than assuming the local asset stays blank.

All first-failure logs are retained separately from successful retries. Scene generation refuses overwriting an existing scene. Validation refuses unsaved scene edits rather than discarding them. The original Kiwoom workbook was not changed; it remains local and ignored because its examples contain credential-shaped values.

## Remaining completion gates

1. When a Quest 3 is available, install the verified APK and execute every hardware acceptance check below. A successful simulator session does not replace this step.

## Headset acceptance

Every row is **NOT RUN** as of this report. Record app version/APK hash, headset OS version, date, result and concrete observations. Keep personal/device identifiers out of shared logs. A human wearer must assess comfort, reach, text readability and hand interaction.

| Check | Procedure / pass condition |
|---|---|
| Passthrough | Launch seated; actual room is visible behind opaque readable components; no unexpected opaque background |
| Initial layout | One selected instrument with chart; secondary starts closed; dock is reachable below central view |
| Korean text | Samsung and SK Hynix names, instructions, data-status labels and unavailable state render without missing glyphs, clipping or material text truncation |
| Direct poke | Select both instruments and open each secondary with near touch; exactly one action per deliberate activation |
| Far hand ray/pinch | Perform the same selections and component actions without direct touch |
| Controller fallback | Put hands away, use each controller to perform the same actions; return to hands without duplicate activations |
| Selection binding | Chart and each reopened/current secondary show the selected instrument; compare uses the other instrument; no account values appear |
| Move | Grab top handles and move chart/dock/secondary; buttons remain clickable; release leaves surface stable without throwing |
| Resize | Two-hand chart/secondary resize stays within 0.85–1.35; text remains usable; dock size stays fixed |
| Recenter | Move surfaces then use 앞으로 정렬; all return to a comfortable front layout from current head direction |
| Component lifecycle | Close/reopen secondary repeatedly; no lost input bindings, accumulating event handlers or hidden duplicate surfaces |
| Input interruption | Open system menu, remove/reseat headset and briefly lose hand tracking; no unintended activation; deliberate input resumes |
| App lifecycle | Pause/resume and relaunch; selection initializes deterministically; no bogus live financial values |
| Comfort/performance | At least 10 minutes seated; record reach/arm fatigue, frame timing, text readability and any adjustments; 72 FPS target is a target, not measured evidence |
| Boundaries | No eye-gaze claim, voice/Gemini execution, account request, gateway request, order draft or order submission |

If a row fails, retain the first failure, fix it, then record the retest separately. When all completion gates and headset checks pass, update this status and link the build/device evidence. Do not proceed to real-order work from an unchecked shell.
