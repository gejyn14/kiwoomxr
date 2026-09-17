# Third-party notices

## Noto Sans CJK KR

Included file: `quest/Assets/SpatialTrading/Fonts/NotoSansKR-Regular.otf`.

Copyright and licensing terms are reproduced in [OFL.txt](../quest/Assets/SpatialTrading/Fonts/OFL.txt). This font is distributed under the SIL Open Font License 1.1. The upstream font is unmodified; the Unity TMP atlas will be a generated representation.

- Upstream: [notofonts/noto-cjk](https://github.com/notofonts/noto-cjk)
- Exact source: [NotoSansKR-Regular.otf at 165c01b46ea533872e002e0785ff17e44f6d97d8](https://github.com/notofonts/noto-cjk/blob/165c01b46ea533872e002e0785ff17e44f6d97d8/Sans/SubsetOTF/KR/NotoSansKR-Regular.otf)
- Font SHA-256: `69975a0ac8472717870aefeab0a4d52739308d90856b9955313b2ad5e0148d68`
- Included license SHA-256: `6a73f9541c2de74158c0e7cf6b0a58ef774f5a780bf191f2d7ec9cc53efe2bf2`

## Unity and Meta dependencies

Unity Editor and package dependencies retain their respective licenses. This repository references packages rather than redistributing SDK implementation source. Inspect each resolved package's license/notice files and Unity-generated third-party notices before distributing an APK. No claim of redistribution clearance is made by a successful technical build.

- [Unity legal terms](https://unity.com/legal)
- [Meta SDK license](https://developers.meta.com/horizon/licenses/oculussdk/)
- [Pinned dependency manifest](../quest/Packages/manifest.json)

TMP Essential Resources were imported by Unity from the resolved `com.unity.ugui` package. Their supplied notices remain with the assets:

- [Liberation Sans OFL](<../quest/Assets/TextMesh Pro/Fonts/LiberationSans - OFL.txt>)
- [EmojiOne attribution](<../quest/Assets/TextMesh Pro/Sprites/EmojiOne Attribution.txt>)

These imported resources accompany TMP settings and fallback assets. Keep their notices when redistributing those assets or the built prototype.

## Newtonsoft JSON

Gateway responses use Unity's official `com.unity.nuget.newtonsoft-json` 3.2.2 package. The [.NET package documentation](https://docs.unity3d.com/Packages/com.unity.nuget.newtonsoft-json@3.2/manual/index.html) identifies the corresponding Newtonsoft.Json version as 13.0.2, also pinned in the shared test harness. Explicit token parsing preserves JSON null and decimal strings without reflection-based object construction.

The resolved package's complete third-party notices are included as a Unity resource in [NewtonsoftNotices.txt](../quest/Assets/SpatialTrading/Resources/NewtonsoftNotices.txt) so they accompany the build. The Unity package wrapper retains its own license as distributed through Package Manager.
