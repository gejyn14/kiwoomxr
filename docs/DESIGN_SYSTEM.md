# Spatial trading design system

Status: initial design iteration implemented and simulator-reviewed, 2026-09-18; the guideline table was rewritten for the trader spread comfort pass on 2026-11-05. This adapts Meta's and Apple's official guidance to an immersive Unity application. It is not a claim of complete platform accessibility or hardware compliance, and nothing here has been verified on a Quest 3 headset.

## Source and decisions

The numbers come from the [comfort design](superpowers/specs/2026-11-05-spread-comfort-design.md) §2. Where a guideline figure is not a measured value of this app, the last column says what actually checks it. Apple links point at the HIG index (the section is named in the link text); Meta links are the section pages under developers.meta.com/horizon/design. Sources were read by the design's author in 2026-11 except where the design itself says otherwise (the palm-gesture / system menu overlap is from general knowledge, not a Meta page).

| Guideline (source) | Application decision | Verification |
|---|---|---|
| Hand-pressed panels at 42-46 cm, ray-pointed panels at 0.8-3 m, avoid 0.5-0.8 m ([Meta hands UI best practices](https://developers.meta.com/horizon/design/hands/)) | 주문 panel on a hand plane 0.45 m ahead at 0.7x scale (about 315 x 210 mm); the other five panels on an eye plane 1.10 m ahead. | Layout unit tests and Validate bounds; seated arm comfort is untested. |
| Long reading at 1 m or more, slightly below eye level ([Meta display](https://developers.meta.com/horizon/design/display/), [Apple HIG Eyes](https://developer.apple.com/design/human-interface-guidelines)) | Reading panels at 1.10 m, centred about -18 to +10 degrees vertically (a little below the eyes). | Layout tests; headset reading is untested. |
| Keep important content in front, avoid head turns, fewer windows ([Apple HIG Spatial layout and Windows](https://developer.apple.com/design/human-interface-guidelines), [Meta Comfort](https://developers.meta.com/horizon/design/)) | Six panels instead of eight (관심종목, one big chart, 잔고, 호가, 뉴스, 주문) within about +-30 degrees; 호가 just right of straight ahead. | Layout tests; wearer view untested. |
| Touch targets of 48 dp (about 22 mm, 2.5-3 degrees), 12 mm between finger targets ([Meta hands UI best practices](https://developers.meta.com/horizon/design/hands/)); Apple 60 pt ([HIG Eyes](https://developer.apple.com/design/human-interface-guidelines)) | Every button and the corner grab area is at least 55 mm (1D/4H toggles 60 x 55 mm), including the close x and the dock buttons; the 1D/4H toggles sit clear of the top-bar grab strip. | Validate checks every button against 55 mm and against the ray-grab strip; dp-to-mm is not assumed. |
| Hover, pressed and a short sound on success ([Meta hands UI best practices](https://developers.meta.com/horizon/design/hands/), [Apple Playing audio](https://developer.apple.com/design/human-interface-guidelines)) | Hover fills the button with a pale cyan glow and brightens the border; press sinks and brightens; a synthesized tick plays on every successful press. | Unit tests for state and sound path; sound not heard on device. |
| Do not use the palm / wrist for app content; offer non-gesture alternatives ([Apple HIG Gestures](https://developer.apple.com/design/human-interface-guidelines), [Meta hands gesture design](https://developers.meta.com/horizon/design/hands/), both Accessibility pages) | An always-visible dock (dismissed-panel icons and 기본 배치로) replaces the palm tray, which is off by default; every panel has a x close button on the same path as the flick. | Unit tests; false-trigger behavior untested on device. |
| Sustained text contrast of at least 4.5:1 ([Meta Accessibility](https://developers.meta.com/horizon/design/accessibility/)) | Muted text is #86BAC6 (it was #6FA3B0, about 4.6:1 on a bright wall); numeric signs and labels carry meaning besides color. | Contrast calculation; passthrough lighting untested. |
| Let the user recenter at any time ([Meta Head](https://developers.meta.com/horizon/design/)) | The existing tracking-origin listener re-places the spread in front of the headset after a Quest system recenter. | Code path only; headset check pending. |

## Earlier iteration

The first iteration (2026-09-18) cited the following pages, which still apply as general principles: [design overview](https://developers.meta.com/horizon/design/), [Buttons](https://developers.meta.com/horizon/design/buttons/), [Fonts and icons](https://developers.meta.com/horizon/design/fonts-icons/) and [Panels](https://developers.meta.com/horizon/design/panels/). Its dock numbers (0.57 m forward, 0.38 m below) described the retired Focus shell. Custom world-space components are not Horizon system panels; system-panel dp dimensions are not copied as world-space requirements. OS text scaling and screen-reader integration are not implemented for the Unity world-space canvas.

## Visual language

- Dark navy, mostly opaque surfaces keep text readable over varying passthrough backgrounds. Restrained rounded edges separate components from the environment without large decorative chrome.
- Mint identifies selection and neutral emphasis. Price direction follows the app's Korean-market convention: coral for rising candles and blue for falling candles. Numeric signs and labels carry meaning independently of color.
- Instrument and price occupy the top of the main component. Candles and observed volume remain distinct. Highest/lowest labels refer to displayed bars, not the trading day's complete session.
- Source and freshness remain visible. Preview is explicitly synthetic; unavailable values display an em dash. A failed live connection never selects preview automatically.
- Loading, unavailable, stale, disconnected and error states preserve the component's identity and explain the missing information. Receipt time is labeled as receipt time, never substituted for exchange time.
- The developer console is hidden at startup. Action names and correlation IDs remain in diagnostic logs rather than occupying user controls.

## Financial and interaction boundaries

[INTERACTION_MODEL](INTERACTION_MODEL.md), [ACTION_MODEL](ACTION_MODEL.md), [FINANCIAL_MODELS](FINANCIAL_MODELS.md) and [ORDER_SAFETY](ORDER_SAFETY.md) remain authoritative project requirements. Components render typed observations from financial capabilities. Shared selection state contains identity, not prices or holdings.

Preview data exists only in the explicit preview source. Gateway mode queries reusable quote/candle/orderbook capabilities and shows their actual status. The Kiwoom source gaps remain documented in [API mapping](KIWOOM_API_MAPPING.md); visual completeness must never hide an integration blocker.

Trade Mode must eventually use structured immutable order fields and a dedicated physical confirmation. A highlighted BUY control, voice command, AI response or simulator input is not authorization to submit a real order.

## Review gates

1. Compile with the pinned Unity/Meta versions and pass transformation/contract tests.
2. Inspect actual compositor output for Korean text, visual hierarchy, selected state, candle bounds and unavailable/stale states.
3. Exercise standard controller interactions; separately simulate hand interactions where supported.
4. With a physical Quest 3, assess direct poke, pinch, tracking loss, passthrough, seated reach and sustained reading. Simulator evidence cannot close this gate.
5. Recheck minimum-size/maximum-distance readability and state labels after resize. Add accessibility settings before any claim of complete accessibility.

The initial price-clipping and input-focus status issues were found through actual compositor output and repaired. [M2 evidence](MILESTONE_2.md) separates these first failures from the final captures.
