# Spread Comfort Pass Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 트레이더 스프레드를 Meta·Apple XR 가이드에 맞춘다. 패널 6개(관심종목·큰 차트·잔고·호가·뉴스 위 1.10 m, 주문 0.45 m 0.7배)를 ±30° 안에 놓고, 손바닥 트레이 대신 늘 보이는 독, 55 mm 누름 대상, 호버·눌림·틱 소리, 패널 닫기(×)를 넣는다. 차트 머리와 관심종목의 전일대비는 키움 `pred_pre`에서 온 기준가로 계산한다.

**Architecture:**
- 게이트웨이는 일봉 `Bar`/`Candle`에 선택 필드 `reference`(기준가 = 종가 − 전일대비)를 싣는다. 실시간 틱이 종가를 바꿔도 기준가는 유지된다.
- 클라이언트는 순수 C# 뷰 모델(`ChartHeaderView`, 새 `WatchlistView`)과 배치 격자(`SpreadLayout`)를 .NET 하네스로 테스트한다.
- 런타임 패널(`WatchlistPanel`, `SpreadDock`, `ButtonFeedback`)은 그 모델을 그리기만 한다. 장면은 `QuestShellProject`가 생성하고 `Validate()`가 크기와 배치 규칙을 검사한다.

**Tech Stack:**
- 게이트웨이: Python 3.11, FastAPI, pydantic, pytest, uv
- 클라이언트: Unity 6000.0.67f1, Meta XR SDK 205 (Interaction SDK), TextMeshPro, Newtonsoft.Json, .NET 8 NUnit 하네스(`tests/Domain`)

**Spec:** `docs/superpowers/specs/2026-11-05-spread-comfort-design.md` (상위: `docs/superpowers/specs/2026-10-28-trader-spread-design.md`). 실행 방식: subagent-driven.

**이미 되어 있는 것 (작업 없음):** spec §2.8 재정렬. `SpreadWorkspace`는 `XRInputSubsystem.trackingOriginUpdated`를 받으면 `_placed = false`로 두고, `LateUpdate`가 새 머리 자세에 다시 고정한다. 시스템 재정렬도 이 이벤트를 낸다. 헤드셋 확인 항목으로만 남긴다.

## Global Constraints

- **주문 안전장치:** `/v1/orders`는 계속 404다. 주문 가능 종목은 005930, 수량 1주 잠금, 매수만, 매도는 잠금 표시. "확인"은 아무것도 보내지 않는다.
- **정직한 라벨:** LIVE, DELAYED, 장 마감, SYNTHETIC · 합성 예시, DISCONNECTED, UNAVAILABLE을 모든 패널(새 관심종목 포함)에 유지한다. 계산할 수 없는 전일대비는 0이 아니라 "—"로 보인다.
- **패널 자세:** 평평하고 yaw만 사용자를 향한다. pitch·roll은 0이다.
- **값 타입:** 금융 값은 binary float를 거치지 않는다(Python `Decimal`, C# `decimal`). wire 규칙(소수 문자열, null 유지)도 그대로다.
- **`verify_m1_sources.py` PASS 유지:** `quest/Assets` 아래 어디에도(주석 포함) `ka`/`kt`/`au`+숫자 5자리, 브로커 호스트, `DllImport`, 주문 실행기를 쓰지 않는다. 클라이언트 코드와 주석에서는 "키움 전일대비"처럼 쓰고 TR 코드는 쓰지 않는다.
- **비밀:** 키·토큰·계좌번호를 출력하거나 커밋하지 않는다. `gateway/.env`, `docs/*_appkey.txt`, `docs/*_secretkey.txt`는 열지 않는다. 파일은 경로를 지정해 하나씩 stage한다.
- **글자 크기:** 캔버스 단위(1 = 1 mm) 16 이상, 가격·수량 숫자는 22 이상. 기존 `Validate()` 검사를 유지한다.
- **누름 대상:** 눈 평면 패널과 독의 모든 `Button`은 캔버스 단위로 폭·높이 55 이상이다. 손 평면 패널의 `Button`은 32 이상이다(0.7배로 약 22 mm, Meta 손가락 누름 최소값). `Validate()`가 검사한다(Task 5).
- **Unity 배치 모드:** `scripts/unity.sh`는 같은 프로젝트를 연 에디터가 있으면 실패한다. 배치 명령 전에 에디터가 닫혀 있는지 확인한다. 열려 있으면 컨트롤러에게 알린다.
- **테스트 명령:**
  - 게이트웨이: `uv run --project gateway pytest -q gateway/tests`
  - 도메인: `DOTNET=~/.dotnet/dotnet scripts/test_domain.sh`
  - Unity: `UNITY_EDITOR=/Users/yujin-an/Applications/UnityEditors/6000.0.67f1/Unity.app/Contents/MacOS/Unity scripts/unity.sh scene`, 이어서 같은 방식으로 `validate`, `test`
  - 소스 가드: `python3 scripts/verify_m1_sources.py`
- **커밋:** 메시지 끝에 `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`를 붙인다.

## Review Focus

1. **오늘 일봉이 틱으로 새로 생겼는데 그 틱에 전일대비가 없음:** 예를 들어 장 시작 첫 틱이 `change=None`으로 온 경우. 기준가는 `None`, 차트 머리는 종가끼리 뺀 값으로 돌아가고, 오류도 0도 없어야 한다. (Task 1 `test_new_day_bar_without_change_has_no_reference`)
2. **`pred_pre`가 비었거나 이상함:** `""`, `"+0"`, 기준가가 0 이하가 되는 값. 기준가는 `None`(0 이하일 때) 또는 종가(`+0`일 때)이고, 봉 자체는 버리지 않는다. (Task 1 `test_daily_reference_edge_values`)
3. **관심종목에서 코스피 선택:** 큰 차트는 코스피, 호가는 지수 안내, 주문은 잠김과 이유를 보인다. 다시 삼성전자를 누르면 모두 돌아온다. (Task 3 `SelectingKospiKeepsOrderLocked`)
4. **패널을 눈 평면과 손 평면 사이로 끌어 놓음:** 배율이 0.7과 1 사이에서 바뀌고, 격자 칸 크기도 평면에 맞게 계산되며, 경계 밖에 놓으면 경계 안으로 끌려 들어온다(기존 `Clamp` 규칙). (Task 3 `DropOnHandPlaneUsesHandCells`, `DropBeyondHandBoundsClampsInside`)
5. **관심종목 줄에 데이터가 없음:** 로딩, UNAVAILABLE, 시리즈 없음. 그 줄은 "—"와 상태 라벨을 보이고 그래도 선택할 수 있다. 미니 차트는 빈 채로 그린다. (Task 2 `WatchlistRowWithoutDataShowsDashAndStatus`)

---

## File Structure

| 파일 | 책임 | Task |
| --- | --- | --- |
| `gateway/src/spatial_gateway/kiwoom/rest.py` | `Bar.reference`, 일봉 `pred_pre` 파싱 | 1 |
| `gateway/src/spatial_gateway/models.py` | `Candle.reference` wire 필드 | 1 |
| `gateway/src/spatial_gateway/market/charts.py` | 틱 갱신에서 기준가 유지, 새 날 봉의 기준가 | 1 |
| `quest/Assets/SpatialTrading/Domain/MarketData.cs` | `Candle.Reference` | 2 |
| `quest/Assets/SpatialTrading/Runtime/Market/GatewayContract.cs` | `reference` 디코딩 | 2 |
| `quest/Assets/SpatialTrading/Runtime/Components/PanelViews.cs` | `ChartHeaderView` 기준가 사용과 `Direction`, 새 `WatchlistView` | 2 |
| `quest/Assets/SpatialTrading/Domain/SpreadLayout.cs` | 6개 `PanelId`, 새 기본 배치·경계·한계, 평면별 칸 크기·배율 | 3 |
| `quest/Assets/SpatialTrading/Runtime/Components/WatchlistPanel.cs` (새 파일) | 관심종목 패널 그리기와 줄 선택 | 3 |
| `quest/Assets/SpatialTrading/Runtime/Spread/SpreadWorkspace.cs`, `SpreadPanel.cs` | 종목 기준 선택, 평면 거리·배율, 독 연결, 닫기 | 3, 4, 5 |
| `quest/Assets/SpatialTrading/Runtime/Spread/SpreadDock.cs` (새 파일) | 늘 보이는 독: 치운 패널 버튼, 기본 배치로 | 4 |
| `quest/Assets/SpatialTrading/Runtime/Spread/PalmTray.cs` | 손바닥 감지 기본 꺼짐 | 4 |
| `quest/Assets/SpatialTrading/Runtime/Components/ButtonFeedback.cs` (새 파일) | 호버·눌림 시각 반응 | 5 |
| `quest/Assets/SpatialTrading/Runtime/Components/HoloStyle.cs` | `Muted` 색, 틱 소리, 버튼 생성 | 5 |
| `quest/Assets/SpatialTrading/Editor/QuestShellProject.cs` | 장면 생성·검증 | 3, 4, 5 |
| `quest/Assets/SpatialTrading/Editor/SpreadCapture.cs`, `SimulatorSession.cs` | 캡처 시나리오, 시뮬레이터 상태 | 3, 6 |
| 문서 (`docs/INTERACTION_MODEL.md`, `docs/DESIGN_SYSTEM.md`, `README.md`, `docs/QUEST_SETUP.md`, `docs/images/trader-spread-live.png`) | 새 배치와 가이드 대응 | 6 |

---

### Task 1: 게이트웨이 일봉 기준가

**Files:**
- Modify: `gateway/src/spatial_gateway/kiwoom/rest.py` (`Bar`, `_parse_bar`)
- Modify: `gateway/src/spatial_gateway/models.py` (`Candle`)
- Modify: `gateway/src/spatial_gateway/market/charts.py` (`_series`, `_tick`, `apply_trade`, `apply_index`)
- Test: `gateway/tests/test_rest.py`, `gateway/tests/test_charts.py`, `gateway/tests/test_contracts.py`

**Interfaces:**
- Produces: `Bar.reference: Decimal | None = None`. 이 필드는 dataclass의 마지막 필드로 두어 기존 위치 인자 생성자를 깨지 않는다.
- Produces: `Candle.reference: Decimal | None`. 범위는 `gt=0`, `max_digits=28`, `decimal_places=8`이고 기본값은 `None`이다. wire에서는 소수 문자열이거나 `null`이며, 기존 `decimal_wire` 직렬화 규칙을 따른다.
- 일봉 행(`daily=True`, `index=False`): `reference = close − signed(pred_pre)`. `pred_pre`가 없거나 파싱되지 않거나 결과가 0 이하면 `None`이다. 지수 행(ka20006)에는 `pred_pre`가 없으므로 `None`이다.
- `_tick`이 같은 기간의 봉을 갱신할 때 `reference`는 그대로 둔다.
- `_tick`이 새 기간의 봉을 추가할 때: 그 틱이 전일대비 `change`를 주면 `reference = price − change`(0 이하면 `None`), 아니면 `None`이다. 이 규칙은 1D에만 적용한다. 4H 봉의 `reference`는 늘 `None`이다.

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`gateway/tests/test_rest.py`:
```python
async def test_daily_bars_carry_reference_from_pred_pre(...):
    # ka10081_005930.json 실데이터 파일: 20261027 cur_prc 225000, pred_pre +12000
    bars = await rest.daily_candles("005930", date(2026, 10, 27))
    by_day = {b.start.strftime("%Y%m%d"): b for b in bars}
    assert by_day["20261027"].reference == Decimal("213000")
    assert by_day["20261026"].reference == Decimal("215500")   # 213500 − (−2000)

async def test_index_daily_bars_have_no_reference(...):
    bars = await rest.index_daily("001", date(2026, 10, 27))
    assert all(b.reference is None for b in bars)

def test_daily_reference_edge_values():
    row = {"dt": "20261105", "open_pric": "100", "high_pric": "110", "low_pric": "90", "cur_prc": "100", "trde_qty": "1"}
    assert _parse_bar({**row, "pred_pre": ""}, "dt", daily=True).reference is None
    assert _parse_bar({**row, "pred_pre": "+0"}, "dt", daily=True).reference == Decimal("100")
    assert _parse_bar({**row, "pred_pre": "+100"}, "dt", daily=True).reference is None   # 0 → None
    assert _parse_bar({**row, "pred_pre": "+150"}, "dt", daily=True).reference is None   # 음수 → None
    assert _parse_bar({**row}, "dt", daily=True).reference is None
```

`gateway/tests/test_charts.py`:
```python
def test_live_tick_keeps_daily_reference():
    # 1D 시드 마지막 봉 close 201500, reference 218000; KRX 틱 price 202000
    obs = store.observation("005930", "1D", cid, True, None)
    assert obs.value.candles[-1].close == Decimal("202000")
    assert obs.value.candles[-1].reference == Decimal("218000")

def test_new_day_bar_takes_reference_from_trade_change():
    # 시드 마지막 봉은 어제; 오늘 첫 KRX 틱 price 205000, change −13000
    assert obs.value.candles[-1].reference == Decimal("218000")

def test_new_day_bar_without_change_has_no_reference():
    # 같은 상황, change None
    assert obs.value.candles[-1].reference is None

def test_index_new_day_bar_takes_reference_from_index_change():
    # apply_index(code="001", price=5383.8, change=−300, ...)가 새 날 1D 봉을 만든다
    assert obs.value.candles[-1].reference == Decimal("5683.8")

def test_four_hour_bars_never_carry_reference(): ...
```

`gateway/tests/test_contracts.py`:
```python
def test_candle_reference_wire():
    c = Candle(period_key="20261105", open=..., high=..., low=..., close=Decimal("201500"), volume=1,
               complete=False, reference=Decimal("218000"))
    assert c.model_dump(mode="json")["reference"] == "218000"
    assert Candle(..., reference=None).model_dump(mode="json")["reference"] is None
    with pytest.raises(ValidationError):
        Candle(..., reference=Decimal("0"))
```

- [ ] **Step 2: 테스트가 실패하는지 확인한다**

Run: `uv run --project gateway pytest -q gateway/tests/test_rest.py gateway/tests/test_charts.py gateway/tests/test_contracts.py`
Expected: FAIL (`reference` 속성 또는 필드 없음)

- [ ] **Step 3: `Bar.reference`, `_parse_bar`, `Candle.reference`, `_series`, `_tick(..., change: Decimal | None = None)`를 구현한다**

- `apply_trade`는 1D `_tick`에 `trade.change`를 넘긴다.
- `apply_index`는 1D `_tick`에 `change`를 넘긴다.
- 4H 호출에는 넘기지 않는다.
- `_series`는 `Candle(..., reference=b.reference)`를 만든다.

- [ ] **Step 4: 전체 게이트웨이 테스트를 돌린다**

Run: `uv run --project gateway pytest -q gateway/tests`
Expected: 기존 216개 + 새 테스트 모두 PASS

- [ ] **Step 5: 커밋한다**

```bash
git add gateway/src/spatial_gateway/kiwoom/rest.py gateway/src/spatial_gateway/models.py gateway/src/spatial_gateway/market/charts.py gateway/tests/test_rest.py gateway/tests/test_charts.py gateway/tests/test_contracts.py
git commit -m "Carry the broker's prior-day reference price on daily candles"
```

---

### Task 2: 클라이언트 기준가, 차트 머리, 관심종목 뷰 모델

**Files:**
- Modify: `quest/Assets/SpatialTrading/Domain/MarketData.cs` (`Candle`)
- Modify: `quest/Assets/SpatialTrading/Runtime/Market/GatewayContract.cs` (캔들 디코딩)
- Modify: `quest/Assets/SpatialTrading/Runtime/Components/PanelViews.cs` (`ChartHeaderView`, 새 `WatchlistRowView`/`WatchlistView`)
- Test: `quest/Assets/SpatialTrading/Tests/EditMode/GatewayContractTests.cs`, `GatewayStreamContractTests.cs`, `PanelViewsTests.cs`

**Interfaces:**
- Consumes: Task 1 wire 필드 `reference`(소수 문자열 또는 `null`, 없을 수도 있음)
- Produces:
  - `Candle.Reference : decimal?`. 생성자 마지막에 선택 인자 `decimal? reference = null`을 둔다. `reference <= 0`이면 `ArgumentException`.
  - `ChartHeaderView.Direction : int`. 전일대비 부호로 `-1`, `0`, `1`이며, 계산 못 하면 `0`.
  - `ChartHeaderView.From`의 시세 없는 경로(fallback): 마지막 봉에 `Reference`가 있으면 `change = c − ref`, `pct = (c − ref) × 100 / ref`. 없으면 지금처럼 이전 봉 종가와의 차이를 쓴다. 시세 경로는 바뀌지 않는다.
  - `public sealed class WatchlistRowView`: `InstrumentRef Instrument`, `string Name`, `string PriceText`, `string ChangeText`, `int Direction`, `string Status`, `IReadOnlyList<decimal> Spark`, `bool Selected`
  - `public static class WatchlistView`:
    ```
    IReadOnlyList<WatchlistRowView> From(IReadOnlyList<InstrumentRef> instruments,
        Func<string, Observation<CandleSeries>> daily, Func<string, Observation<QuoteSnapshot>> quote,
        Func<string, string> dailyMarket, Func<string, string> quoteMarket, InstrumentRef selected, int sparkCount = 20)
    ```
    각 줄의 이름·가격·전일대비·방향·상태는 `ChartHeaderView.From(instrument, daily(code), quote(code), "1D", dailyMarket(code), quoteMarket(code))`에서 가져온다. 중복 계산 없이 그 결과를 쓴다. `Spark`는 일봉 마지막 `sparkCount`개의 종가이고, 시리즈가 없으면 빈 목록이다.

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`GatewayContractTests.cs`와 `GatewayStreamContractTests.cs`:
```csharp
[Test] public void CandleReferenceDecodes()        // "reference":"218000" → 218000m
[Test] public void CandleReferenceNullOrMissingIsNull()
[Test] public void CandleReferenceZeroIsRejected()  // 기존 Invalid 규칙대로 관측 전체 거부
```

`PanelViewsTests.cs`:
```csharp
[Test] public void HeaderUsesReferenceWhenQuoteIsOtherMarket()
// KRX 일봉 [221500 → 201500(reference 218000)], NXT 시세 201500/−16500
// ChangeText == ViewFormat.Change(-16500m, -7.57m)와 같은 문자열, Direction == -1
// 즉 "−16,500 (−7.57%)": 반올림은 ViewFormat 규칙을 따른다
[Test] public void HeaderFallsBackToPreviousCloseWithoutReference()   // 기존 동작 유지
[Test] public void HeaderDirectionIsZeroWhenChangeUnknown()
[Test] public void WatchlistRowsFollowCatalogOrderAndMarkSelection()  // 4줄, 선택한 줄만 Selected
[Test] public void WatchlistSparkIsLastTwentyCloses()                 // 30개 봉 → 마지막 20개 종가
[Test] public void WatchlistRowWithoutDataShowsDashAndStatus()        // 시리즈·시세 null → PriceText == ViewFormat.None, Status에 UNAVAILABLE 라벨, Spark 비어 있음
[Test] public void WatchlistSyntheticRowSaysSynthetic()               // StatusText.Synthetic 포함
```

- [ ] **Step 2: 실패를 확인한다**

Run: `DOTNET=~/.dotnet/dotnet scripts/test_domain.sh`
Expected: 컴파일 오류 또는 새 테스트 FAIL

- [ ] **Step 3: 구현한다**

- 디코더는 `Object(token, ...)` 허용 키 목록에 `"reference"`를 넣고, 기존 선택 금액 헬퍼(`Money`)로 읽는다.
- `RenderMemo`가 `Candle`의 값 동등성을 쓴다면 `Reference`도 비교에 넣는다.

- [ ] **Step 4: 통과를 확인한다**

Run: `DOTNET=~/.dotnet/dotnet scripts/test_domain.sh && python3 scripts/verify_m1_sources.py`
Expected: 모두 PASS

- [ ] **Step 5: 커밋한다**

```bash
git add quest/Assets/SpatialTrading/Domain/MarketData.cs quest/Assets/SpatialTrading/Runtime/Market/GatewayContract.cs quest/Assets/SpatialTrading/Runtime/Components/PanelViews.cs quest/Assets/SpatialTrading/Tests/EditMode/GatewayContractTests.cs quest/Assets/SpatialTrading/Tests/EditMode/GatewayStreamContractTests.cs quest/Assets/SpatialTrading/Tests/EditMode/PanelViewsTests.cs
git commit -m "Compute chart and watchlist changes from the broker reference price"
```

---

### Task 3: 패널 6개 배치, 관심종목 패널, 종목 기준 선택

**Files:**
- Modify: `quest/Assets/SpatialTrading/Domain/SpreadLayout.cs`
- Create: `quest/Assets/SpatialTrading/Runtime/Components/WatchlistPanel.cs`
- Modify: `quest/Assets/SpatialTrading/Runtime/Components/ChartPanel.cs` (선택 이벤트 제거, 늘 선택 종목을 그림)
- Modify: `quest/Assets/SpatialTrading/Runtime/Spread/SpreadWorkspace.cs`, `SpreadPanel.cs`, `PalmTray.cs` (`Abbreviation`)
- Modify: `quest/Assets/SpatialTrading/Editor/QuestShellProject.cs`, `SpreadCapture.cs`, `SimulatorSession.cs`
- Regenerate: `quest/Assets/SpatialTrading/Scenes/TraderSpread.unity`
- Test: `quest/Assets/SpatialTrading/Tests/EditMode/SpreadLayoutTests.cs`, `OrderTicketTests.cs` (필요할 때만)

**Interfaces:**
- Consumes: Task 2 `WatchlistView.From`, `WatchlistRowView`, `ChartHeaderView.Direction`
- Produces (`SpreadLayout`):
  - `enum PanelId { Watchlist, Chart, OrderBook, News, Balance, Order }`
  - `static float CellMetersFor(Plane p)`: Eye `0.05f`, Hand `0.035f`. 기존 `CellMeters` 상수는 Eye 값으로 남긴다.
  - `static float ScaleFor(Plane p)`: Eye `1f`, Hand `0.7f`
  - 기본 배치:
    - Watchlist `Eye(-13,-7,6,11)`
    - Chart `Eye(-7,-2,8,6)`
    - Balance `Eye(-7,-7,8,5)`
    - OrderBook `Eye(1,-7,7,11)`
    - News `Eye(8,-7,5,11)`
    - Order `Hand(-2,-3,9,6)`
  - 경계(칸 인덱스, 양끝 포함): Eye x −13..12, y −7..4. Hand x −9..8, y −4..2.
  - 한계: Watchlist 최소 5×8, 최대 8×12. Chart는 기존 기본값(4×3..16×10). 나머지는 지금 값 그대로.
  - `Drop`은 `CellMetersFor(plane)`로 미터를 칸으로 바꾼다.
  - `ChartDetailFor`: 면적 ≤ 20은 Line, ≤ 40은 Candles, 그보다 크면 CandlesWithAxes. **(규칙 변경: 기본 큰 차트 8×6 = 48이 축 있는 캔들이 되게 한다.)**
- Produces (`SpreadWorkspace`):
  - `EyeDistance = 1.10f`, `HandDistance = 0.45f`, `HandHeight = -0.24f`
  - `LocalCenter`는 평면별 칸 크기를 쓴다.
  - 평면 판정 경계는 `(EyeDistance + HandDistance) / 2`이다.
  - `InstrumentRef SelectedInstrument { get; }`, `void SelectInstrument(InstrumentRef instrument)` (TraderCatalog.Charts에 없는 종목은 무시)
  - `string Interval { get; }`, `void SetInterval(string interval)` (차트 패널 하나)
  - `ChartIds`, `IsChart`, `InstrumentFor`, `SelectChart(PanelId)`는 제거한다.
  - 호가·주문·뉴스는 지금처럼 `SelectedInstrument`를 따른다.
- Produces (`SpreadPanel`): `Place`는 `transform.localScale = Vector3.one * SpreadLayout.ScaleFor(rect.Plane)`를 적용한다. 스냅 애니메이션 중에는 배율도 함께 보간한다.
- Produces (`WatchlistPanel : HoloPanel`):
  - `event Action<InstrumentRef> Selected`
  - `void Render(IReadOnlyList<WatchlistRowView> rows, PanelRect rect)`
  - 줄마다 누름 영역(poke·ray)은 줄 전체다.
  - 선택한 줄은 `HoloStyle.SelectedLineWidth` 테두리로 표시한다.
  - 전일대비 글자는 숫자에 부호를 붙인다(기존 규칙).
  - 미니 차트는 시안 선이다.
  - 글자 크기 규칙(16/22 이상)을 지킨다.
- 에디터 키 `1`–`4`는 `TraderCatalog.Charts[i]`를 선택한다. 마우스 클릭이 관심종목 줄을 고를 수 있어야 한다. 줄마다 콜라이더를 두거나, 패널 로컬 좌표로 줄 인덱스를 계산하는 방식 중 구현자가 고른다.

- [ ] **Step 1: 실패하는 배치 테스트를 쓴다** (`SpreadLayoutTests.cs`, 기존 차트 4개 테스트를 새 ID로 바꾼다)

```csharp
[Test] public void DefaultLayoutMatchesSpec()       // 위 기본 배치 6개를 정확히, 겹침 없음
[Test] public void DefaultEyeRowFitsThirtyDegrees() // 눈 평면 패널 x 범위 ⊆ [−13, 13), 1.10 m에서 |각| ≤ 31°
[Test] public void DropOnHandPlaneUsesHandCells()   // Drop(Order, Hand, x=0.0875, y=0.0) → Hand(-2,-3,9,6)
[Test] public void DropBeyondHandBoundsClampsInside() // Drop(Order, Hand, x=0.6, y=0) → Hand(0,-3,9,6) (x 최대 8칸에 맞춰 끌려 들어옴)
[Test] public void ScaleAndCellSizePerPlane()       // Eye 0.05/1, Hand 0.035/0.7
[Test] public void ChartDetailThresholds()          // 4×5=20 Line, 5×8=40 Candles, 8×6=48 Axes
[Test] public void WatchlistLimits()                // Resize(Watchlist, 3, 3) → 5×8로 고정, Resize(.., 20, 20) → 8×12
[Test] public void ResetRestoresAllSix()
```

`OrderTicketTests`(또는 새 테스트)에 Review Focus 3을 넣는다:
```csharp
[Test] public void SelectingKospiKeepsOrderLocked()   // 지수 선택 → HasSelection 유지, 잠금 이유 표시, 매수 준비 불가; 삼성전자 재선택 → 준비 가능
```

- [ ] **Step 2: 실패를 확인한다**

Run: `DOTNET=~/.dotnet/dotnet scripts/test_domain.sh`
Expected: FAIL

- [ ] **Step 3: `SpreadLayout`을 구현하고 도메인 테스트를 통과시킨다**

- [ ] **Step 4: 런타임과 에디터를 새 모델로 바꾼다**

- `WatchlistPanel`, 차트 하나, 종목 기준 선택, 평면 배율을 넣는다.
- `QuestShellProject.GenerateScene`은 6개 패널을 만든다.
- `Validate`가 다음을 확인한다.
  - 패널 6개
  - 각 기본 rect
  - 손 평면 패널 `localScale` 0.7
  - 눈 평면 패널이 1.10 m, 손 평면 패널이 0.45 m
- `SpreadCapture` 시나리오를 새 API로 바꾼다.
  - b: SK하이닉스 선택
  - c: 코스피 선택
  - d: 차트를 크게
  - 나머지는 이름만 맞춘다.
- `SimulatorSession` 스냅숏에서 선택 종목 코드를 쓴다.

- [ ] **Step 5: 장면을 생성하고 검증·테스트한다**

Run: `scripts/unity.sh scene`, `scripts/unity.sh validate`, `scripts/unity.sh test`, `DOTNET=~/.dotnet/dotnet scripts/test_domain.sh`, `python3 scripts/verify_m1_sources.py`
Expected:
- `scene` 로그에 "Trader Spread scene generated: 6 grabbable panels"
- validate exit 0
- EditMode 전체 PASS
- 도메인 PASS
- 가드 PASS

- [ ] **Step 6: 커밋한다** (바뀐 파일과 새 `.cs`·`.meta`, 장면, `EditorBuildSettings.asset`이 바뀌었으면 그것도 경로로 하나씩 stage)

```bash
git commit -m "Spread six panels within thirty degrees: watchlist, one chart, a near order panel"
```

---

### Task 4: 늘 보이는 독, 손바닥 트레이 끄기

**Files:**
- Create: `quest/Assets/SpatialTrading/Runtime/Spread/SpreadDock.cs`
- Modify: `quest/Assets/SpatialTrading/Runtime/Spread/PalmTray.cs`, `SpreadWorkspace.cs`
- Modify: `quest/Assets/SpatialTrading/Editor/QuestShellProject.cs`
- Regenerate: 장면

**Interfaces:**
- Consumes: `SpreadWorkspace.Layout.Dismissed`, `Restore(PanelId)`, `Reset()`, `LayoutChanged`, `PalmTray.Abbreviation(PanelId)`
- Produces (`SpreadDock : MonoBehaviour`):
  - `void Configure(SpreadWorkspace workspace, RectTransform canvas)`
  - `void Refresh()`: `LayoutChanged`에서 호출한다.
  - `bool TryPress(Collider c)`: 마우스 대체 입력용. 맞으면 동작을 실행하고 `true`.
  - 치운 패널마다 버튼이 하나씩 있다. 크기 64 × 55 mm, 간격 8 mm, 라벨은 `Abbreviation`. 마지막에 "기본 배치로" 버튼(150 × 55 mm)이 있고, 안쪽 여백은 12 mm다.
  - 독 높이는 60 mm이고, 왼쪽 끝은 눈 평면 x = −13칸(−0.65 m)에 고정한다.
  - 독의 윗변은 눈 평면 y = −7칸 아랫변(−0.35 m)에서 5 mm 아래, 거리는 `EyeDistance`다.
  - 내용에 따라 오른쪽으로 늘어난다.
  - 작업공간 원점·회전을 따라 다시 놓인다(`PlaceAll` 시점).
  - 버튼은 `Holo.Button`과 `Holo.OnClick`을 쓰고, poke와 ray 캔버스 상호작용을 가진다. 마우스 대체용으로 버튼마다 `BoxCollider`가 있다.
- `PalmTray`:
  - `[SerializeField] bool _palmGesture = false`. `false`면 손바닥 자세 감지가 트레이를 열지 않는다.
  - `Toggle`/`Pin`(에디터 `T`, 컨트롤러 Y)은 지금처럼 동작한다. **(규칙: 개발용으로 남김)**
- `SpreadWorkspace`의 마우스 입력은 독 콜라이더를 먼저 검사한다.

- [ ] **Step 1: 독 크기 계산을 순수 함수로 만들고 테스트한다**

도메인 하네스는 `Runtime/Spread`를 컴파일하지 않으므로, 계산은 `Domain/SpreadLayout.cs`의 `public static float DockWidthMm(int dismissedCount)`에 둔다. `SpreadDock`은 이 값을 쓴다. 값은 `24 + dismissedCount × (64 + 8) + 150`이다(여백 12 × 2, 버튼과 간격, 기본 배치로 버튼). `SpreadLayoutTests`에 넣는다:
```csharp
[Test] public void DockWidthGrowsWithDismissedPanels()   // 0 → 174, 1 → 246, 5 → 534
```

- [ ] **Step 2: 실패 → 구현 → 통과** (`DOTNET=~/.dotnet/dotnet scripts/test_domain.sh`)

- [ ] **Step 3: `SpreadDock`, `PalmTray._palmGesture`, 장면 생성, `Validate`를 구현한다**

`Validate`가 확인할 것:
- 독이 있다.
- 손바닥 감지가 꺼져 있다.
- 독 버튼이 55 mm 이상이다.
- 독이 눈 평면 패널과 겹치지 않는다.

- [ ] **Step 4: `scripts/unity.sh scene`, `validate`, `test`, 도메인 테스트, 가드** — 모두 PASS

- [ ] **Step 5: 커밋한다**

```bash
git commit -m "Restore panels from an always-visible dock; palm tray off by default"
```

---

### Task 5: 누름 대상, 호버·눌림·틱, 닫기(×), 흐린 글자 대비

**Files:**
- Create: `quest/Assets/SpatialTrading/Runtime/Components/ButtonFeedback.cs`
- Modify: `quest/Assets/SpatialTrading/Runtime/Components/HoloStyle.cs`, `ChartPanel.cs`, 머리 오른쪽에 글자가 있는 패널(`OrderBookPanel.cs`, `NewsPanel.cs`, `BalancePanel.cs`, `OrderPanel.cs`, `WatchlistPanel.cs`)
- Modify: `quest/Assets/SpatialTrading/Runtime/Spread/SpreadPanel.cs`, `SpreadWorkspace.cs`
- Modify: `quest/Assets/SpatialTrading/Editor/QuestShellProject.cs`
- Regenerate: 장면

**Interfaces:**
- `HoloStyle.Muted` = `#86BAC6`
- `HoloStyle.Tick : AudioClip`: 코드로 합성한다. 길이 약 30 ms, 2–3 kHz, 빠르게 줄어드는 소리. `Whoosh`처럼 한 번만 만든다.
- `Holo.OnClick(Button b, Action action)`: 성공한 누름마다 `AudioSource.PlayClipAtPoint(HoloStyle.Tick, b.transform.position, 0.5f)`. 같은 프레임 중복 막기는 유지한다. 모든 버튼(차트 1D/4H, 주문 유형·매수·매도 탭·준비·확인·취소, 독, 트레이 기본 배치로, 닫기)이 이 경로를 쓴다.
- `ButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler`:
  - 버튼 안 "Glow" 이미지의 투명도: 평소 0, 호버 0.18, 눌림 0.32
  - 테두리: 호버 때 밝아짐
  - 배율: 눌림 때 0.97
  - 비활성 버튼은 반응하지 않는다.
  - `Holo.Button`이 붙인다.
- `ChartPanel`의 1D/4H 버튼은 60 × 55 mm다. 머리 배치를 그에 맞춘다.
- `SpreadPanel`:
  - 보이는 손잡이 삼각형은 34 mm(`HandleMarkSize = 0.034f`)
  - 잡히는 콜라이더는 55 mm(`HandleHitSize = 0.055f`). 중심은 패널 모서리다.
  - 패널 안쪽으로 들어오는 27.5 × 27.5 mm 영역에는 어떤 `Button`도 겹치지 않아야 한다. 패널 오른쪽 아래 버튼은 여백을 두어 피한다.
- 닫기(×):
  - 55 × 55 mm `Holo.Button`, 라벨 "×"(U+00D7)
  - 위치는 패널 위쪽 오른쪽 모서리 안쪽. 모든 `HoloPanel`은 머리 오른쪽 끝 62 mm(`CloseReserveMm = 62f`)를 비워 둔다. 기존 오른쪽 배지·상태 글자는 그 왼쪽에서 끝난다.
  - 누르면 `SpreadWorkspace.Dismiss(id, Rotation * Vector3.right)`. 던지기와 같은 경로라서 주문 패널이면 준비한 미리보기가 취소된다.
  - 위쪽 가운데의 레이 잡기 띠와 겹치지 않는다.
- `QuestShellProject.Validate` 추가 검사:
  - 눈 평면 패널과 독의 모든 `Button` `RectTransform`이 폭·높이 55 이상
  - 손 평면 패널은 32 이상
  - 손잡이 안쪽 영역과 겹치는 `Button` 없음
  - 모든 패널에 닫기 버튼이 있음
  - `Muted` 색 값

- [ ] **Step 1: 대비 테스트를 쓴다** (`quest/Assets/SpatialTrading/Tests/EditMode/StatusTextTests.cs` 또는 새 `HoloContrastTests.cs`. `HoloStyle`은 Unity 타입이라 도메인 하네스가 컴파일하지 않으므로 Unity EditMode 테스트로 둔다)

```csharp
[Test] public void MutedTextMeetsFourPointFiveOverBrightPassthrough()
// PanelFill(알파 0.82)을 sRGB (0.8, 0.8, 0.8) 벽 위에 sRGB 값으로 알파 합성한 배경 대비 Muted 명암비 ≥ 5.0
// (#86BAC6은 약 6.0, 지금 #6FA3B0은 약 4.6이라 먼저 실패한다)
// (WCAG 상대 휘도 공식)
```

- [ ] **Step 2: 구현한다**

- [ ] **Step 3: `scripts/unity.sh scene`, `validate`, `test`, 도메인 테스트, 가드** — 모두 PASS

- [ ] **Step 4: 커밋한다**

```bash
git commit -m "Give every control a 55 mm target, hover/press glow, a tick, and a close button"
```

---

### Task 6: 캡처와 문서

**Files:**
- Modify: `quest/Assets/SpatialTrading/Editor/SpreadCapture.cs` (계좌 가리기)
- Modify: `docs/INTERACTION_MODEL.md` (트레이더 스프레드 절), `docs/DESIGN_SYSTEM.md` (가이드 대응 표), `README.md`, `docs/QUEST_SETUP.md`
- Replace: `docs/images/trader-spread-live.png`

**Interfaces:**
- `SpreadCapture`: 환경 변수 `SPATIAL_CAPTURE_REDACT=1`이면 캡처 전에 잔고 패널의 계좌 글자를 "계좌 ••••"로 바꾼다. 커밋할 이미지는 반드시 이 모드로 찍는다.

- [ ] **Step 1: 계좌 가리기를 구현하고 캡처한다**

- 게이트웨이를 `scripts/gateway.sh`로 띄운다. 토큰은 `SPATIAL_PAIRING_TOKEN` 환경 변수로만 넘긴다(출력 금지).
- `SpreadCapture`를 돌려 `artifacts/trader-spread-comfort/`에 시나리오 a–g를 저장한다.
- a 장면을 `SPATIAL_CAPTURE_REDACT=1`로 다시 찍어 `docs/images/trader-spread-live.png`를 교체한다.
- 커밋 전에 이미지를 열어 계좌번호 숫자가 보이지 않는지 눈으로 확인한다.

- [ ] **Step 2: 문서를 고친다**

- `INTERACTION_MODEL.md`:
  - 패널 6개, 두 평면(1.10 m, 0.45 m·0.7배)
  - 독, 닫기(×)
  - 손바닥 트레이 기본 꺼짐
  - 단축키 `1`–`4` = 관심종목
  - 헤드셋 확인 항목(spec §5)
- `DESIGN_SYSTEM.md`: 가이드 대응 표를 트레이더 스프레드 기준으로 다시 쓴다(spec §2의 수치와 출처 URL).
- `README.md`: 이미지 설명. 무엇으로 찍었는지를 정직하게 쓴다.
- `QUEST_SETUP.md`: 발표 체크리스트에서 손바닥 트레이 항목을 독으로 바꾼다. 장중 확인 "plain-code 일봉이 KRX인지 통합인지"를 추가한다.

- [ ] **Step 3: 전체 검증** — 게이트웨이, 도메인, Unity `validate`·`test`, 가드 모두 PASS

- [ ] **Step 4: 커밋한다** (이미지와 문서, `SpreadCapture.cs`를 경로로 하나씩 stage)

```bash
git commit -m "Document the comfort pass and refresh the live capture"
```

---

## 계획 밖 (장중 수동 확인, 컨트롤러가 사용자와 함께)

- **일봉이 어느 시장 것인지:**
  - 장중에 키움 일봉을 종목코드만 넣었을 때와 `_AL`(통합)로 받았을 때를 KRX 공식 종가와 비교한다.
  - 종목코드만 넣은 일봉이 통합이면, 라벨 "KRX"를 "통합"으로 바꾸는 작은 후속 작업을 연다.
  - kiwoom-cli와 게이트웨이가 같은 앱키 토큰을 쓰므로, 확인할 때 토큰 재발급이 서로를 끊지 않게 순서를 정한다.
- **헤드셋 확인:** spec §5 전부와 재정렬.
