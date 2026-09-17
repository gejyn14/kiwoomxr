# Spatial trading design system

Status: initial design iteration implemented and simulator-reviewed, 2026-09-18. This adapts Meta's official guidance to an immersive Unity application. It is not a claim of complete platform accessibility or hardware compliance.

## Source and decisions

| Official source, accessed 2026-09-18 | Application decision | Verification |
|---|---|---|
| [Meta design overview](https://developers.meta.com/horizon/design/) | Spatial focus, independent financial capabilities and progressive disclosure remain the organizing principles. | Default scene shows one selected instrument and a reachable control dock. |
| [Hands: Comfort and design principles](https://developers.meta.com/horizon/design/hands/) | Frequent controls are near the body; use standard poke, ray and grab, with controller alternatives. | Dock is 0.57 m forward and 0.38 m below head level; human reach/arm fatigue remains untested. |
| [Display: depth and comfort](https://developers.meta.com/horizon/design/display/) | Keep sustained reading surfaces at least 0.5 m away; main chart starts near 1 m. | Center-distance bounds are 0.5–1.8 m; wearer may still approach a world-stable surface. These bounds do not guarantee comfort. |
| [Buttons](https://developers.meta.com/horizon/design/buttons/) | Short action labels, generous hit targets, visible hover/press/selection. | Dock controls are 55–62 authored mm high; compare separately with Meta's 48 dp direct-touch target guidance, not an assumed dp-to-mm conversion. |
| [Fonts and icons](https://developers.meta.com/horizon/design/fonts-icons/) | Strong hierarchy: instrument, price, comparison, status. Use readable Korean Noto glyphs; no decorative icon-only primary actions. | Inspect Korean line boxes, clipping and contrast in compositor captures. |
| [Accessibility](https://developers.meta.com/horizon/design/accessibility/) | Values and text labels accompany colors; focus/selection also changes border thickness. | Check contrast, alternate input and minimum size; OS text scaling and screen-reader integration are not yet implemented for the Unity world-space canvas. |
| [Panels](https://developers.meta.com/horizon/design/panels/) | Distinct manipulation affordance and on-demand related content; avoid crowding the primary view. | Custom world-space components are not Horizon system panels; system-panel dp dimensions are not copied as world-space requirements. |

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
