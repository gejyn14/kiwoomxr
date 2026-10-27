# Trader Spread Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 헤드셋을 쓰면 차트 4개(키움증권·삼성전자·SK하이닉스·KOSPI, 기본 일봉), 정중앙 호가창, 오른쪽 뉴스, 왼손 잔고, 오른손 주문이 실제 키움·DART·RSS 데이터로 정면에 펼쳐지고, 모든 패널을 옮기고·늘리고·던져 치우고·트레이에서 되살릴 수 있다.

**Architecture:** 게이트웨이가 일봉·4시간봉(60분봉 묶음)·지수(0J)·잔고(kt00018/kt00010/04)·뉴스(OpenDART+RSS)를 모아 기존 `Observation` 봉투로 `/v1/stream`에 새 capability로 푸시한다. Unity는 순수 C# 도메인 모델(배치 격자, 선택, 주문 티켓, 패널 뷰 모델)을 .NET 하네스로 테스트하고, 런타임은 그 모델을 그리는 얇은 패널 컴포넌트와 이동·크기·치우기·트레이 상호작용만 맡는다. 장면은 `QuestShellProject`가 절차적으로 생성한다.

**Tech Stack:** Python 3.11, FastAPI, httpx, websockets 16, defusedxml, pytest/pytest-asyncio, uv · Unity 6000.0.67f1, Meta XR SDK 205 (Interaction: Grabbable, GrabFreeTransformer, PointableCanvas), TextMeshPro, Newtonsoft.Json · .NET 8 NUnit 하네스(`tests/Domain`).

**Spec:** `docs/superpowers/specs/2026-10-28-trader-spread-design.md` (상위: `docs/superpowers/specs/2026-10-27-investment-jarvis-design.md` §6, §6.1, §7). 실행 방식: subagent-driven.

## Global Constraints

- 모든 패널은 사용자를 정면으로 향하고 평평하다. 패널 회전은 머리 기준 yaw만 따르며 pitch·roll 기울기는 0이다. 대각선·책상형 배치 금지(사용자 결정).
- 시연 종목 고정: 키움증권 `039490`, 삼성전자 `005930`, SK하이닉스 `000660`, KOSPI 지수 `001`(키움 업종코드). 차트 기본 주기는 일봉 `1D`, 전환 주기는 `4H`.
- 색: 자비스 시안 `#00e5ff`, 경고·주문·공시는 앰버 `#ffb300`. 상승은 속 빈 봉, 하락은 채운 봉, 둘 다 시안이고 숫자에는 `+`/`−` 부호를 붙인다.
- 캐스트 가독성: 생성되는 모든 TMP 글자 크기는 캔버스 단위(1 = 1mm) 16 이상, 가격·수량 숫자는 22 이상. `Validate()`가 이를 검사한다.
- 주문 경로 없음: `/v1/orders`는 계속 404. 주문 패널은 UI와 미리보기 카드까지만. 주문 가능 종목 005930, 수량 1주 잠금, 매수만, 매도 탭은 잠금 표시.
- 키움 실전 도메인만. 앱키·시크릿·DART 키는 `gateway/.env`에서만 읽고 로그·에러·클라이언트 메시지에 절대 나가지 않는다. 키는 길이나 해시 지문으로만 비교·확인한다. `.env`는 커밋 금지.
- 계좌번호는 게이트웨이에서 마스킹한 값만 헤드셋으로 보낸다. 원문 계좌번호는 로그에도 남기지 않는다.
- 금융 값은 binary float를 거치지 않는다(Python `Decimal`/`int`, C# `decimal`/`long`). wire 규칙(소수 문자열, 정수 문자열, null 유지) 유지.
- KRX와 NXT 봉은 한 시리즈에 섞지 않는다. 시간은 KST(+09:00) 명시.
- 키움 조회 오류 1700–1702 백오프·무한 재시도 금지, 8005 재발급 분당 1회(기존 `TokenProvider`·`KiwoomRest` 규칙 재사용). REST 호출 간격은 최소 0.25초.
- 정직성 라벨(LIVE, DELAYED, 장 마감, DISCONNECTED, UNAVAILABLE)은 모든 패널에 유지. 합성 데이터를 실데이터처럼 보이게 하지 않는다.
- 게이트웨이 실행은 `scripts/gateway.sh`로 한다(Task 1). `~/.zshrc`가 export하는 `KIWOOM_APPKEY`/`KIWOOM_SECRETKEY`가 `gateway/.env`를 덮어쓰지 않게 한다.
- Unity 배치 모드(`scripts/unity.sh`)는 같은 프로젝트를 연 에디터가 있으면 실패한다. 배치 명령 전에 에디터를 닫거나, 열린 에디터의 메뉴로 같은 작업을 한다.
- 테스트 명령: 게이트웨이 `uv run --project gateway pytest -q gateway/tests`, 도메인 `DOTNET=~/.dotnet/dotnet scripts/test_domain.sh`.

## Review Focus

1. **패널을 화면 밖이나 다른 패널 밑으로 놓침(시연 중 손떨림, 빠른 던지기 오인식)** → 놓는 순간 격자에 맞춰지고 작업 공간 경계 안으로 끌려 들어오며, 던지기 임계값 미만의 빠른 이동은 치우기가 아니다. "기본 배치로" 한 동작이면 모든 패널(치운 것 포함)이 기본 위치로 돌아온다. (Task 9 `Clamp`·`Reset`, Task 10 `FlickDetector` 테스트)
2. **주문 잠금 우회(KOSPI나 다른 종목 선택 뒤 주문 유형을 바꾸거나, 주문 패널을 치웠다 되살림)** → 005930이 아니면 어떤 유형을 골라도 "매수 준비"가 비활성이고 잠금 이유가 보인다. 주문 패널을 치우면 PREFLIGHT가 취소되고, 되살려도 PREFLIGHT가 복원되지 않는다. (Task 10 테스트)
3. **잔고·뉴스 소스 실패(DART 키 없음, RSS 피드 하나 다운, kt00018 오류)** → 해당 패널만 UNAVAILABLE·DISCONNECTED와 이유를 보이고, 나머지 패널과 시세 스트림은 계속 돈다. 실패한 피드 하나 때문에 다른 피드 항목이 사라지지 않는다. (Task 6, Task 7 테스트)
4. **장 마감·NXT 시간대 시연(KOSPI는 NXT 세션 없음)** → KOSPI 차트는 "장 마감" 라벨의 마지막 일봉, 종목 4H는 NXT 시리즈로 바뀌고 라벨이 NXT, 일봉은 KRX 시리즈로 "장 마감" 표시. 0원·빈 차트가 LIVE로 보이지 않는다. (Task 4 테스트)
5. **보유 없음 → 첫 체결로 보유 생김(S4 연결점)** → 실시간 04 이벤트가 오면 1초 안에 잔고를 다시 조회해 "보유 종목 없음"이 첫 행으로 바뀐다. 04 폭주에도 kt00018은 2초에 1회를 넘지 않는다. (Task 6 테스트)

---

## File Structure

Gateway (`gateway/src/spatial_gateway/`)

| 파일 | 책임 |
|---|---|
| `settings.py` (수정) | `dart_api_key`, `index_codes`, `news_feeds` 추가 |
| `models.py` (수정) | 지수·계좌·뉴스 subject 코드, `Origin.NEWS`, `Balance`/`Holding`/`NewsItem`/`NewsFeed` |
| `kiwoom/rest.py` (수정) | `Bar`, `daily_candles`, `hour_candles`, `index_daily`, `index_hour`, `account_number`, `balance`, `orderable_cash` |
| `kiwoom/realtime.py` (수정) | `IndexTick`(0J), `AccountEvent`(04) 파싱과 REG |
| `market/aggregate.py` (신규) | 60분봉 → 4시간봉 (순수) |
| `market/observe.py` (신규) | 연결·세션·값 → `Observation` 상태 결정 (hub에서 추출, 순수) |
| `market/charts.py` (신규) | `ChartStore`: (code, market, interval) 시리즈, 실시간 마지막 봉 갱신 |
| `account.py` (신규) | `AccountService`: 잔고·주문가능 주기 조회, 04 디바운스 |
| `news.py` (신규) | `NewsService`: DART·RSS 수집, 정리, 종목 언급 표시 |
| `stream.py` (수정) | 새 capability 푸시, 지수 코드 구독 |
| `app.py` (수정) | 차트·계좌·뉴스 서비스 기동 |
| `scripts/gateway.sh` (신규, 저장소 루트) | 셸 환경변수 덮어쓰기 없이 게이트웨이 실행 |

Unity (`quest/Assets/SpatialTrading/`)

| 파일 | 책임 |
|---|---|
| `Domain/InstrumentRef.cs` (수정) | `InstrumentKind { Stock, Index }` |
| `Domain/TraderCatalog.cs` (신규) | 시연 종목 4개와 언급 키워드 |
| `Domain/AccountAndNews.cs` (신규) | `BalanceSnapshot`, `Holding`, `NewsItem`, `NewsFeed` |
| `Domain/SpreadLayout.cs` (신규) | 패널 id, 격자 배치, 기본 프리셋, 옮기기·크기·경계·치우기·되살리기·리셋, 크기별 정보량 |
| `Domain/FlickDetector.cs` (신규) | 놓는 순간 속도 기록 → 치우기 판정 (순수) |
| `Domain/OrderTicket.cs` (신규) | 주문 유형 표(시간대별), 선택·잠금, PREFLIGHT 미리보기 |
| `Domain/PanelViews.cs` (신규) | 호가·뉴스·잔고·차트 헤더 표시 모델 (순수) |
| `Runtime/Market/GatewayStreamContract.cs` (수정) | 새 capability 디코딩 |
| `Runtime/Market/MarketDataSession.cs` (수정) | 4종목+지수 상시 구독, 새 캐시 |
| `Runtime/Spread/SpreadPanel.cs` (신규) | 패널 한 장: 잡기 이동, 모서리 크기, 던지기, 격자 스냅 애니메이션 |
| `Runtime/Spread/SpreadWorkspace.cs` (신규) | 레이아웃 모델 소유, 선택 배선, 트레이, 리셋, 에디터 단축키 |
| `Runtime/Spread/PalmTray.cs` (신규) | 손바닥 뒤집기 트레이와 아이콘 |
| `Runtime/Components/CandleChartGraphic.cs` (수정) | 시안 속빈/채운 봉, 거래량, 축, 정보량 단계 |
| `Runtime/Components/ChartPanel.cs`, `OrderBookPanel.cs`, `NewsPanel.cs`, `BalancePanel.cs`, `OrderPanel.cs` (신규) | 뷰 모델을 UGUI로 그리기 |
| `Editor/QuestShellProject.cs` (수정) | 트레이더 스프레드 장면 생성·검증 |
| `Tests/EditMode/*Tests.cs` (신규) | 도메인 테스트 (.NET 하네스가 glob으로 포함) |

새 순수 C# 파일은 모두 `Domain/`에 둔다. `tests/Domain/Domain.Tests.csproj`가 `Domain/*.cs`를 glob으로 컴파일하므로 csproj 수정은 `Runtime/Market/GatewayStreamContract.cs`처럼 이미 등록된 파일 외에는 필요 없다. `Domain` asmdef는 UnityEngine을 참조하지 않으므로 벡터는 `float` 필드 구조체로 다룬다.

---

### Task 1: 설정, 실행 스크립트, 계약 확장

**Files:**
- Create: `scripts/gateway.sh`
- Modify: `gateway/src/spatial_gateway/settings.py`, `gateway/src/spatial_gateway/models.py`, `gateway/.env.example`, `gateway/pyproject.toml` (`"defusedxml>=0.7,<1"` 추가)
- Test: `gateway/tests/test_contracts.py`, `gateway/tests/test_auth.py`(설정 테스트가 있는 곳)

**Interfaces:**
- Produces:
  - `Settings` 새 필드: `dart_api_key: str | None` (env `DART_API_KEY`, 비어 있으면 None), `index_codes: tuple[str, ...]` (env `GATEWAY_INDEX_CODES`, 기본 `("001",)`, 각 `^[0-9]{3}$`), `news_feeds: tuple[str, ...]` (env `GATEWAY_NEWS_FEEDS`, 쉼표 구분 https URL, 기본은 Task 7이 정한 피드 상수). `__repr__`는 계속 `Settings(<redacted>)`.
  - `models.SUBJECT_PATTERN = r"^([0-9]{6}|[0-9]{3}|ACCOUNT|NEWS)$"`; `Observation.instrument_code`가 이 패턴을 쓴다. 상수 `ACCOUNT = "ACCOUNT"`, `NEWS = "NEWS"`.
  - `Origin.NEWS = "NEWS"`. LIVE 규칙: `origin != SYNTHETIC`이고 `source_at`이 있어야 한다.
  - `Holding(code: str 6자리, name: str, quantity: int ≥0, average_price: Decimal|None, current_price: Decimal|None, profit: Decimal|None, profit_rate: Decimal|None)`
  - `Balance(account: str, total_evaluation: Decimal|None, total_profit: Decimal|None, total_profit_rate: Decimal|None, orderable_cash: Decimal|None, holdings: tuple[Holding, ...])` — `account`는 `^[0-9]{3}\*+[0-9]{2}$`만 허용(마스킹 값만 통과).
  - `NewsItem(kind: Literal["NEWS","FILING"], source: str ≤40, title: str 1..200, published_at: datetime aware, mentions: tuple[str, ...], filing_type: str|None)`, `NewsFeed(items: tuple[NewsItem, ...] ≤40, published_at 내림차순)`.
  - 모든 Decimal 필드는 기존 `decimal_wire`/`no_binary_floats` 규칙, 정수는 문자열 wire.

- [ ] **Step 1: 실패하는 테스트 작성**

```python
def test_subject_codes_accept_index_account_news_and_reject_others():
    # "005930","001","ACCOUNT","NEWS" 통과; "05930","0001","account","00593O" → ValidationError

def test_news_origin_can_be_live_but_synthetic_cannot():
    # Observation[NewsFeed](state=LIVE, origin=NEWS, source_at=..., received_at=...) 통과
    # 같은 것을 origin=SYNTHETIC → ValidationError

def test_balance_accepts_only_masked_account():
    # account="123*****90" 통과, "1234567890" → ValidationError
    # Balance(...).model_dump(mode="json")["orderable_cash"] == "1032000"  (문자열 wire)

def test_news_feed_must_be_newest_first_and_bounded():
    # 시간 역순 아님 → ValidationError; 41개 → ValidationError; title 201자 → ValidationError

def test_settings_read_dart_key_index_codes_and_feeds():
    # DART_API_KEY 없음 → dart_api_key is None; GATEWAY_INDEX_CODES 기본 ("001",); "1" → ValueError
    # GATEWAY_NEWS_FEEDS="http://x" → ValueError (https만)
    # repr(settings)에 DART 키 값 없음
```

- [ ] **Step 2: 실패 확인** — Run: `uv run --project gateway pytest -q gateway/tests/test_contracts.py gateway/tests/test_auth.py` / Expected: FAIL (새 이름 없음)
- [ ] **Step 3: 구현** — 위 Interfaces 그대로. 기존 `Quote`/`Candle` 테스트가 계속 통과해야 한다.
- [ ] **Step 4: `scripts/gateway.sh` 작성** — `cd` 저장소 루트, `exec env -u KIWOOM_APPKEY -u KIWOOM_SECRETKEY -u DART_API_KEY uv run --project gateway --env-file gateway/.env uvicorn spatial_gateway.app:app --app-dir gateway/src --host "${GATEWAY_HOST:-127.0.0.1}" --port "${GATEWAY_PORT:-8471}" --no-access-log`. 실행 권한 부여.
- [ ] **Step 5: DART 키 복사 (1회, 값 출력 금지)** — team-03 `members/gejyn14/app/.env`의 `DART_API_KEY` 줄을 `gateway/.env`에 추가하고 `.env.example`에는 `DART_API_KEY=`, `GATEWAY_INDEX_CODES=001`, `GATEWAY_NEWS_FEEDS=`와 `GATEWAY_WATCH_CODES=039490,005930,000660`을 적는다. `gateway/.env`의 `GATEWAY_WATCH_CODES`도 같은 세 종목으로 바꾼다.

Run: `grep -c '^DART_API_KEY=.\+' gateway/.env; git check-ignore gateway/.env`
Expected: `1` 그리고 `gateway/.env`

- [ ] **Step 6: 통과 확인** — Run: `uv run --project gateway pytest -q gateway/tests` / Expected: 전부 PASS
- [ ] **Step 7: 커밋**

```bash
git add scripts/gateway.sh gateway/pyproject.toml gateway/uv.lock gateway/.env.example gateway/src/spatial_gateway/settings.py gateway/src/spatial_gateway/models.py gateway/tests
git commit -m "Extend gateway contracts for index, account and news subjects"
```

---

### Task 2: 키움 REST 차트 (일봉, 60분봉, 업종)

**Files:**
- Modify: `gateway/src/spatial_gateway/kiwoom/rest.py`
- Create: `gateway/tests/fixtures/ka10081_005930.json`, `ka10080_60_005930.json`, `ka20006_001.json`, `ka20005_60_001.json`
- Test: `gateway/tests/test_rest.py`

**Interfaces:**
- Produces (모두 `KiwoomRest` 메서드, 결과는 `start` 오름차순, 잘못된 행은 버림):
  - `@dataclass(frozen=True) class Bar(start: datetime, open: Decimal, high: Decimal, low: Decimal, close: Decimal, volume: int)`; 기존 `MinuteBar`는 `Bar`의 별칭으로 남긴다(`MinuteBar = Bar`).
  - `async def daily_candles(self, code: str, base_date: date, limit: int = 120) -> list[Bar]` — ka10081, 본문 `{"stk_cd": code, "base_dt": "YYYYMMDD", "upd_stkpc_tp": "1"}`, 행 목록 `stk_dt_pole_chart_qry`, 날짜 `dt`(YYYYMMDD → 그날 09:00 KST를 `start`로).
  - `async def hour_candles(self, code: str, limit: int = 200) -> list[Bar]` — ka10080 `tic_scope="60"`, 기존 분봉 파서 재사용.
  - `async def index_daily(self, index_code: str, base_date: date, limit: int = 120) -> list[Bar]` — ka20006, 본문 `{"inds_cd": index_code, "base_dt": "YYYYMMDD"}`.
  - `async def index_hour(self, index_code: str, limit: int = 200) -> list[Bar]` — ka20005, 본문 `{"inds_cd": index_code, "tic_scope": "60"}`.
  - 지수 값 스케일: 업종 차트 가격이 소수점 없이 100배 정수로 오면 `Decimal(value) / 100`으로 바꾼다. 픽스처로 확인한 결과를 함수 docstring에 한 줄로 적는다.

- [ ] **Step 1: 실응답 픽스처 수집 (네트워크, team-03 앱 꺼진 상태)** — 각 API를 1회 호출해 응답을 저장하되, 행은 앞 5개만 남기고 `return_msg` 외 개인 정보가 없는지 확인한다. ka20006/ka20005의 정확한 행 목록 키와 날짜·시각 필드 이름을 응답에서 확인해 파서에 쓴다(예상: `inds_dt_pole_qry`/`dt`, `inds_min_pole_qry`/`cntr_tm`). 호출은 `KiwoomRest.request`를 쓰는 일회성 `python -c`로 하고 토큰을 출력하지 않는다.

Run: `ls gateway/tests/fixtures/ka10081_005930.json gateway/tests/fixtures/ka20006_001.json gateway/tests/fixtures/ka20005_60_001.json gateway/tests/fixtures/ka10080_60_005930.json`
Expected: 네 파일 모두 존재

- [ ] **Step 2: 실패하는 테스트 작성** (`httpx.MockTransport`로 픽스처 반환)

```python
async def test_daily_candles_parse_fixture_and_request_adjusted_prices():
    # 요청 헤더 api-id == "ka10081", 본문 upd_stkpc_tp == "1", base_dt == "20261028"
    # 결과 5개, 오름차순, bars[-1].start == datetime(그 dt, 9, 0, tzinfo=KST), 모든 가격 > 0, 타입 Decimal

async def test_hour_candles_use_tic_scope_60():
    # 본문 tic_scope == "60"; 결과 start 분이 모두 0

async def test_index_daily_scales_to_index_points():
    # 1000 < bars[-1].close < 10000 (KOSPI 포인트 범위), Decimal 소수 2자리 이하

async def test_index_hour_parses_fixture():
    # api-id == "ka20005", inds_cd == "001", 결과 오름차순

async def test_chart_rows_with_missing_or_inverted_prices_are_dropped():
    # high < low 행, cur_prc 빈 행 → 결과에서 빠지고 예외 없음
```

- [ ] **Step 3: 실패 확인** — Run: `uv run --project gateway pytest -q gateway/tests/test_rest.py` / Expected: FAIL (`AttributeError: daily_candles`)
- [ ] **Step 4: 구현** — 위 Interfaces. 행 파서는 기존 `_parse_bar`를 시각 필드 이름과 스케일을 인자로 받도록 일반화한다.
- [ ] **Step 5: 통과 확인** — Run: `uv run --project gateway pytest -q gateway/tests/test_rest.py` / Expected: PASS
- [ ] **Step 6: 커밋**

```bash
git add gateway/src/spatial_gateway/kiwoom/rest.py gateway/tests/test_rest.py gateway/tests/fixtures
git commit -m "Fetch daily, hourly and KOSPI index candles from Kiwoom REST"
```

---

### Task 3: 4시간봉 묶기

**Files:**
- Create: `gateway/src/spatial_gateway/market/aggregate.py`
- Test: `gateway/tests/test_aggregate.py`

**Interfaces:**
- Consumes: `Bar` (Task 2), `Market` (`market/session.py`)
- Produces: `def four_hour(bars: Sequence[Bar], market: Market) -> list[Bar]` — `start`가 버킷 시작인 4시간봉, 오름차순. `def bucket_start(at: datetime, market: Market) -> datetime | None` — 세션 밖이면 None.

버킷 규칙(장 시작 기준, 세션 끝에서 자름): KRX `09:00–13:00`, `13:00–15:30`. NXT `08:00–08:50`, `15:30–19:30`, `19:30–20:00`. 60분봉의 `start`가 속한 버킷으로 묶고, open은 첫 봉, close는 마지막 봉, high/low는 최대·최소, volume은 합. 세션 밖 봉은 버린다.

- [ ] **Step 1: 실패하는 테스트 작성**

```python
def test_krx_hours_fold_into_two_buckets_per_day():
    # 2026-10-28 09:00..15:00 60분봉 7개 → 4H 2개, starts == [09:00, 13:00]
    # 첫 버킷 open == 09:00봉 open, close == 12:00봉 close, volume == 4개 합

def test_nxt_post_session_buckets_start_at_1530():
    # 15:30, 16:30 … 19:30 봉 → starts == [15:30, 19:30]

def test_bars_outside_session_are_dropped_and_markets_never_mix():
    # KRX 묶기에 08:00 봉 → 버려짐; bucket_start(08:10, KRX) is None; bucket_start(08:10, NXT) == 08:00

def test_buckets_reset_each_day():
    # 10-27 13:00봉과 10-28 09:00봉은 다른 버킷
```

- [ ] **Step 2: 실패 확인** — Run: `uv run --project gateway pytest -q gateway/tests/test_aggregate.py` / Expected: FAIL (모듈 없음)
- [ ] **Step 3: 구현** — 순수 함수, I/O 없음.
- [ ] **Step 4: 통과 확인** — 같은 명령 / Expected: PASS
- [ ] **Step 5: 커밋** — `git commit -m "Fold hourly bars into session-anchored four-hour candles"`

---

### Task 4: 차트 저장소와 관측 상태 추출

**Files:**
- Create: `gateway/src/spatial_gateway/market/observe.py`, `gateway/src/spatial_gateway/market/charts.py`
- Modify: `gateway/src/spatial_gateway/market/hub.py` (상태 결정 로직을 `observe.py`로 옮기고 호출)
- Test: `gateway/tests/test_charts.py`, 기존 `gateway/tests/test_hub.py`는 수정 없이 통과

**Interfaces:**
- Consumes: `Bar`, `four_hour`, `bucket_start` (Task 2–3), `Trade` (`kiwoom/realtime.py`), `IndexTick` (Task 5에서 정의, 이 Task에서는 `apply_index`가 `(code, price, change, change_percent, at)`을 받는다)
- Produces:
  - `observe.py`: `def observe(model, code: str, cid: str, *, value, received: datetime|None, source: datetime|None, connected: bool, reason: str|None, closed: bool, watched: bool, origin: Origin = Origin.KIWOOM) -> Observation` — 기존 `MarketHub.observations` 안 `one()`과 같은 결정표(NOT_SUBSCRIBED → DISCONNECTED → LOADING/UNAVAILABLE → DELAYED MARKET_CLOSED → LIVE).
  - `charts.py`: `class ChartStore(clock, *, watch_codes, index_codes)`:
    - `seed(code: str, market: Market, interval: Literal["1D","4H"], bars: Sequence[Bar]) -> None` (4H는 60분봉을 받아 내부에서 `four_hour`)
    - `apply_trade(trade: Trade) -> bool` — 종목: 1D는 KRX 체결로만 마지막 일봉(같은 KST 날짜) 갱신, 새 날짜면 새 봉. 4H는 체결 시장 시리즈의 `bucket_start` 버킷 갱신/추가.
    - `apply_index(code, price, change, change_percent, at) -> bool` — 지수 1D·4H(KRX) 갱신과 지수 `Quote` 저장
    - `observation(code: str, interval: str, cid: str, connected: bool, reason: str|None) -> tuple[Market|None, Observation[CandleSeries]]` — 1D는 항상 KRX 시리즈, 4H는 활성 시장(장외면 마지막으로 받은 시장). 지수는 둘 다 KRX. 장외는 DELAYED/MARKET_CLOSED.
    - `index_quote(code, cid, connected, reason) -> Observation[Quote]`
    - 시리즈 길이 상한: 1D 120개, 4H 120개. `CandleSeries.interval`은 `"1D"`/`"4H"`, `period_key`는 1D `YYYYMMDD`, 4H `YYYYMMDDHHMM`. 마지막 봉만 `complete=False`.
  - `MarketHub`가 `charts: ChartStore` 속성을 갖고 `apply(event)`에서 `Trade`를 `ChartStore.apply_trade`에도 넘긴다. 변경되면 같은 `_notify(code)`를 쓴다.

- [ ] **Step 1: 실패하는 테스트 작성**

```python
def test_daily_series_updates_last_bar_from_krx_trades_only():
    # 1D seed(005930, KRX) 마지막 dt 2026-10-28, close 224500
    # KRX Trade 10:01 price 225000 → 마지막 close == 225000, high ≥ 225000
    # NXT Trade 16:00 price 226000 → 1D 변화 없음

def test_four_hour_series_follows_active_market_with_label():
    # clock 16:10 KST, 4H KRX와 NXT 둘 다 seed → observation(...)[0] == Market.NXT
    # clock 10:00 → Market.KRX

def test_kospi_during_nxt_hours_is_market_closed_daily():
    # clock 17:00 평일, index 1D seed → state == DELAYED, reason == "MARKET_CLOSED", market == KRX

def test_unseeded_series_is_loading_in_session_and_unavailable_outside():
    # 10:00 → LOADING, 21:00 → UNAVAILABLE MARKET_CLOSED

def test_disconnect_turns_chart_observations_disconnected_with_reason():
    # connected=False, reason="KIWOOM_WS_CLOSED" → DISCONNECTED, 값은 유지

def test_index_tick_updates_index_quote_and_daily_close():
    # apply_index("001", Decimal("2645.12"), …, 10:00) → index_quote LIVE last_price "2645.12"
```

- [ ] **Step 2: 실패 확인** — Run: `uv run --project gateway pytest -q gateway/tests/test_charts.py` / Expected: FAIL
- [ ] **Step 3: `observe.py` 추출** — `MarketHub.observations`가 `observe()`를 쓰게 바꾼다. Run: `uv run --project gateway pytest -q gateway/tests/test_hub.py` / Expected: PASS(동작 변화 없음)
- [ ] **Step 4: `ChartStore` 구현과 `MarketHub` 배선**
- [ ] **Step 5: 통과 확인** — Run: `uv run --project gateway pytest -q gateway/tests` / Expected: PASS
- [ ] **Step 6: 커밋** — `git commit -m "Keep daily and four-hour series per market with live last-bar updates"`

---

### Task 5: 지수(0J)·잔고(04) 실시간

**Files:**
- Modify: `gateway/src/spatial_gateway/kiwoom/realtime.py`, `gateway/src/spatial_gateway/market/hub.py`
- Test: `gateway/tests/test_realtime.py`

**Interfaces:**
- Produces:
  - `@dataclass(frozen=True) class IndexTick(code: str, price: Decimal, change: Decimal|None, change_percent: Decimal|None, at: datetime)` — 0J, item은 업종코드(`"001"`), 필드 `10` 현재가(절댓값), `11` 전일대비(부호 유지), `12` 등락률(부호 유지), `20` 시각 HHMMSS.
  - `@dataclass(frozen=True) class AccountEvent(at: datetime)` — 04 프레임은 내용을 쓰지 않고 "잔고가 바뀌었다"는 신호만 낸다. 계좌번호 필드(`9201`)는 파싱하지도 로그하지도 않는다.
  - `parse_real_frame`이 `Trade | Book | IndexTick | AccountEvent`를 반환한다. 알 수 없는 type은 지금처럼 버린다.
  - `KiwoomRealtime(settings, tokens, on_event, on_state, *, index_codes: Sequence[str] = ())`: REG 데이터를 `[{"item": 종목 KRX+_NX 목록, "type": ["0B","0D"]}, {"item": index_codes, "type": ["0J"]}, {"item": [""], "type": ["04"]}]`로 보낸다(같은 grp_no "1", refresh "1"). REG ack 실패 처리 규칙은 그대로.
  - `MarketHub.apply`가 `IndexTick`을 `charts.apply_index`로 넘기고, `AccountEvent`는 생성자 인자 `on_account: Callable[[], None] | None`을 호출한다.
  - 지수 값 스케일이 Task 2와 같은 100배 규칙이면 같은 변환을 쓴다. 장중 0J 프레임을 1회 기록해 픽스처로 남긴다(장중이 아니면 Step 1을 다음 장으로 미루고 ledger에 적는다).

- [ ] **Step 1: 실패하는 테스트 작성**

```python
def test_index_frame_parses_absolute_price_and_signed_change():
    # {"trnm":"REAL","data":[{"type":"0J","item":"001","values":{"10":"+264512","11":"-1234","12":"-0.47","20":"101500"}}]}
    # → IndexTick(code="001", price=Decimal("2645.12"), change=Decimal("-12.34"), change_percent=Decimal("-0.47"), at=10:15:00 KST)
    # (100배 스케일은 Task 2 픽스처로 확인한 규칙. 규칙이 다르면 이 입력·기대값도 그 규칙대로 바꾼다)

def test_balance_frame_emits_signal_without_account_number():
    # type "04", values {"9201":"1234567890","9001":"005930","930":"1"} → [AccountEvent(at)]
    # repr(event)와 caplog 어디에도 "1234567890" 없음

def test_register_message_includes_index_and_balance_groups():
    # register_message(["005930"], index_codes=["001"])["data"] 세 묶음, 두 번째 type == ["0J"], 세 번째 type == ["04"]

def test_hub_routes_index_ticks_and_account_events():
    # hub.apply(IndexTick) → charts.index_quote("001") LIVE; hub.apply(AccountEvent) → on_account 1회 호출
```

- [ ] **Step 2: 실패 확인** — Run: `uv run --project gateway pytest -q gateway/tests/test_realtime.py` / Expected: FAIL
- [ ] **Step 3: 구현**
- [ ] **Step 4: 통과 확인** — Run: `uv run --project gateway pytest -q gateway/tests` / Expected: PASS
- [ ] **Step 5: 커밋** — `git commit -m "Receive KOSPI index ticks and balance-change signals in realtime"`

---

### Task 6: 계좌 서비스 (잔고, 주문가능)

**Files:**
- Modify: `gateway/src/spatial_gateway/kiwoom/rest.py`
- Create: `gateway/src/spatial_gateway/account.py`, `gateway/tests/fixtures/kt00018_empty.json`, `gateway/tests/fixtures/kt00010_005930.json`
- Test: `gateway/tests/test_account.py`

**Interfaces:**
- Consumes: `KiwoomRest.request`, `Balance`/`Holding` (Task 1), `observe` (Task 4)
- Produces:
  - `KiwoomRest.account_number() -> str` — ka00001 `acctNo`. 반환값은 호출자 메모리에만 둔다.
  - `KiwoomRest.balance() -> RawBalance` — kt00018 본문 `{"qry_tp": "1", "dmst_stex_tp": "KRX"}`; 필드 `tot_evlt_amt`, `tot_evlt_pl`, `tot_prft_rt`, 목록 `acnt_evlt_remn_indv_tot`(`stk_cd`, `stk_nm`, `rmnd_qty`, `pur_pric`, `cur_prc`, `evltv_prft`, `prft_rt`). `stk_cd`의 `A` 접두사는 떼어 낸다. 응답에서 필드 이름이 다르면 픽스처 기준으로 맞추고 docstring에 적는다.
  - `KiwoomRest.orderable_cash(code: str, price: Decimal) -> Decimal|None` — kt00010 본문 `{"stk_cd": code, "trde_tp": "2", "uv": str(price)}`, 필드 `ord_alowa`.
  - `def mask_account(number: str) -> str` — 앞 3자리 + `*` × (길이−5) + 뒤 2자리. 10자리가 아니어도 동작하고, 5자리 미만은 `ValueError`.
  - `class AccountService(rest, clock, *, last_price: Callable[[str], Decimal|None], notify: Callable[[], None])`:
    - `async def run(self)` — 시작 시 계좌번호 1회, 이후 잔고·주문가능을 60초마다 조회. `last_price("005930")`가 None이면 주문가능 조회는 건너뛰고 값은 None.
    - `def balance_changed(self)` — 04 신호. 1초 뒤 잔고를 다시 조회하되, kt00018 호출 간격은 최소 2초(그 사이 신호는 한 번으로 합침).
    - `def observation(self, cid: str) -> Observation[Balance]` — subject `ACCOUNT`. 조회 성공 후 LIVE(`source_at`=조회 시각), 장외 DELAYED/MARKET_CLOSED, 조회 실패는 마지막 값을 유지한 DISCONNECTED와 `reason="ACCOUNT_QUERY_FAILED"`, 한 번도 성공 못 했으면 LOADING.
  - 로그에는 오류 코드와 예외 타입 이름만 남긴다.

- [ ] **Step 1: 실응답 픽스처 수집** — kt00018, kt00010(005930, 현재가)을 1회 호출해 저장. 계좌번호·이름 등 개인 값이 응답에 있으면 픽스처에서 `0000000000`/빈 문자열로 바꾼다. 보유 행이 있는 경우를 위한 `kt00018_one.json`은 빈 픽스처에 005930 1주 행을 손으로 추가해 만든다(파일 상단에 `"_note": "hand-made from kt00018_empty"`).
- [ ] **Step 2: 실패하는 테스트 작성**

```python
def test_mask_account_keeps_three_and_two_digits():
    # mask_account("1234567890") == "123*****90"; mask_account("1234") → ValueError

async def test_empty_balance_is_live_with_no_holdings_and_orderable_cash():
    # kt00018_empty + kt00010 → observation.state == LIVE, holdings == (), orderable_cash > 0, account == "123*****90"

async def test_balance_signal_requeries_within_a_second_and_coalesces_bursts():
    # 가짜 시계, balance_changed() 10회 연속 → 1초 경과 후 kt00018 1회
    # 0.5초 뒤 다시 신호 → 마지막 호출로부터 2초가 지나기 전에는 호출 없음, 지나면 1회

async def test_first_holding_appears_after_signal():
    # 첫 조회 empty, 신호 후 응답 kt00018_one → holdings[0].code == "005930", quantity == 1

async def test_query_failure_keeps_last_value_as_disconnected():
    # 두 번째 조회 return_code 1700 → DISCONNECTED, reason "ACCOUNT_QUERY_FAILED", value 유지
    # caplog에 계좌번호 원문 없음

async def test_orderable_cash_skipped_without_price():
    # last_price → None → kt00010 호출 없음, orderable_cash is None
```

- [ ] **Step 3: 실패 확인** — Run: `uv run --project gateway pytest -q gateway/tests/test_account.py` / Expected: FAIL
- [ ] **Step 4: 구현**
- [ ] **Step 5: 통과 확인** — Run: `uv run --project gateway pytest -q gateway/tests` / Expected: PASS
- [ ] **Step 6: 커밋** — `git commit -m "Serve masked account balance and orderable cash with realtime refresh"`

---

### Task 7: 뉴스 서비스 (OpenDART + RSS)

**Files:**
- Create: `gateway/src/spatial_gateway/news.py`, `gateway/tests/fixtures/dart_list_005930.json`, `gateway/tests/fixtures/rss_*.xml`(채택한 피드마다 1개)
- Test: `gateway/tests/test_news.py`

**Interfaces:**
- Consumes: `NewsItem`, `NewsFeed`, `Origin.NEWS`, `NEWS` (Task 1), `Settings.dart_api_key`, `Settings.news_feeds`
- Produces:
  - `CORP_CODES: dict[str, str]` — 종목코드 → DART 고유번호. `005930: "00126380"`, `000660: "00164779"`, `039490`은 DART `corpCode.xml`에서 `stock_code == "039490"`인 항목으로 확인해 적는다(Step 1).
  - `MENTIONS: dict[str, tuple[str, ...]]` — `039490: ("키움증권", "키움")`, `005930: ("삼성전자", "삼성")`, `000660: ("SK하이닉스", "하이닉스")`, `001: ("코스피", "KOSPI")`.
  - `DEFAULT_FEEDS: tuple[str, ...]` — 무료·한국어·인증 없는 증권/경제 RSS 1–3개(후보: 한국경제 증권, 매일경제 증권, 연합뉴스 경제). Step 1에서 실제로 200과 유효한 RSS를 주는 것만 남긴다.
  - `def clean_title(raw: str) -> str` — HTML 태그 제거, 엔티티 복원, 공백 정리, 200자 자르기.
  - `def parse_rss(xml: bytes, source: str) -> list[NewsItem]` — `defusedxml.ElementTree`로 파싱, `pubDate`(RFC 822) → KST aware, 실패 항목만 버림.
  - `def parse_dart(data: dict, code: str) -> list[NewsItem]` — `list[].report_nm`, `rcept_dt`(YYYYMMDD, 시각 없음 → 그날 00:00 KST), `kind="FILING"`, `source="DART"`, `filing_type`은 `report_nm`의 첫 대괄호 태그 또는 None, `mentions=(code,)`.
  - `def mentions_of(title: str) -> tuple[str, ...]`
  - `class NewsService(client: httpx.AsyncClient, settings, clock, notify)`: `async def run(self)` — RSS 5분, DART 10분 주기. DART 요청은 `https://opendart.fss.or.kr/api/list.json`에 `crtfc_key`, `corp_code`, `bgn_de`(7일 전), `page_count=10`. `def observation(self, cid) -> Observation[NewsFeed]` — 소스별 마지막 성공 결과를 합쳐 최신순 40개, 같은 제목은 하나만. 하나라도 성공했으면 LIVE(`source_at`=가장 최근 성공 시각), 키 없음은 DART만 빠지고 RSS만 LIVE, 모두 실패면 마지막 값 유지 DISCONNECTED `reason="NEWS_FETCH_FAILED"`.
  - DART 키는 URL 쿼리에 들어가므로 httpx 예외 메시지·로그에 URL을 남기지 않는다(예외 타입 이름만).

- [ ] **Step 1: 소스 확인과 픽스처 (네트워크)** — 후보 RSS를 받아 보고 채택한 것을 `DEFAULT_FEEDS`로, 응답을 픽스처로(항목 5개만) 저장. DART `corpCode.xml`(zip)을 받아 039490의 `corp_code`를 찾아 `CORP_CODES`에 적고 005930·000660 값도 같은 파일로 대조한다. DART list 응답 하나를 픽스처로 저장. 키는 출력하지 않는다.
- [ ] **Step 2: 실패하는 테스트 작성**

```python
def test_rss_fixture_parses_kst_times_and_clean_titles():
    # 각 피드 픽스처 → 항목 ≥1, published_at.utcoffset() == 9h, 제목에 "<" ">" "&amp;" 없음

def test_dart_fixture_parses_filings_with_amber_kind():
    # kind == "FILING", source == "DART", mentions == ("005930",)

def test_mentions_detect_stock_names():
    # mentions_of("SK하이닉스 HBM 증설… 삼성전자도") == ("005930", "000660") (코드 순 정렬)

async def test_one_failing_feed_does_not_drop_others():
    # 피드 A 500, 피드 B 정상 → LIVE, B 항목만

async def test_missing_dart_key_skips_dart_and_keeps_rss_live():
    # dart_api_key None → DART 호출 0회, state LIVE

async def test_all_sources_failing_keep_last_feed_as_disconnected():
    # 첫 라운드 성공, 둘째 라운드 전부 실패 → DISCONNECTED, reason "NEWS_FETCH_FAILED", 항목 유지

async def test_dart_key_never_logged():
    # DART 요청이 ConnectError → caplog 텍스트에 키 값 없음

def test_feed_is_deduplicated_newest_first_and_capped():
    # 같은 제목 2개 → 1개; 60개 입력 → 40개; 정렬 내림차순
```

- [ ] **Step 3: 실패 확인** — Run: `uv run --project gateway pytest -q gateway/tests/test_news.py` / Expected: FAIL
- [ ] **Step 4: 구현**
- [ ] **Step 5: 통과 확인** — Run: `uv run --project gateway pytest -q gateway/tests` / Expected: PASS
- [ ] **Step 6: 커밋** — `git commit -m "Collect OpenDART filings and securities RSS headlines for the news panel"`

---

### Task 8: 스트림 확장과 앱 배선

**Files:**
- Modify: `gateway/src/spatial_gateway/stream.py`, `gateway/src/spatial_gateway/app.py`
- Test: `gateway/tests/test_stream.py`

**Interfaces:**
- Consumes: `ChartStore` (Task 4), `AccountService` (Task 6), `NewsService` (Task 7), `KiwoomRealtime(index_codes=…)` (Task 5)
- Produces (wire, Unity Task 11이 디코딩):
  - 구독 코드: 6자리 종목 또는 3자리 지수, 1–10개(기존 `_CODE`를 `^([0-9]{6}|[0-9]{3})$`로).
  - 종목 코드의 capability: `quote`, `orderbook`, `candles`(1m, 기존), `candles_1d`, `candles_4h`. 지수 코드: `quote`, `candles_1d`, `candles_4h`(호가 없음 — 지수에는 `orderbook` 메시지를 보내지 않는다).
  - 연결 전체에 한 번씩: `balance`(subject `ACCOUNT`), `news`(subject `NEWS`). 메시지 모양은 기존과 같다: `{"type":"observation","capability":…, "market": "KRX"|"NXT"|null, "observation": {...}}`. `balance`/`news`의 `market`은 null.
  - 합치기 간격: `candles_1d`/`candles_4h`는 `candles_interval`, `balance`/`news`는 1초.
  - `MarketHub`에 `account: AccountService | None`, `news: NewsService | None`을 붙이고, 둘의 `notify`는 `_notify("ACCOUNT")`/`_notify("NEWS")`로 연결한다. 스트림은 이 두 키를 모든 연결의 pending에 넣는다.
  - `app.py` lifespan: realtime(index_codes 포함) → 차트 seed(종목 1D KRX, 4H KRX/NXT, 지수 1D/4H)를 REST 0.25초 간격으로, 30분마다 다시 seed → `AccountService.run`, `NewsService.run`. 어느 태스크가 죽어도 다른 태스크와 스트림은 계속 돈다(기존 `_broker_done` 패턴).
  - `/health`에 `orders_enabled: False` 유지. `/v1/orders`는 라우트 없음.

- [ ] **Step 1: 실패하는 테스트 작성**

```python
async def test_index_code_subscription_gets_quote_and_charts_but_no_orderbook():
    # subscribe codes ["001"] → 받은 capability 집합 == {"quote","candles_1d","candles_4h","balance","news"}

async def test_stock_subscription_gets_daily_and_four_hour_charts():
    # codes ["005930"] → "candles_1d","candles_4h" 포함, observation.value.interval == "1D"/"4H"

async def test_balance_and_news_sent_once_per_change_to_every_client():
    # 두 클라이언트, account notify 1회 → 각 클라이언트 balance 메시지 1개, market is None

async def test_subscribe_rejects_bad_codes():
    # ["0001"], ["ACCOUNT"], 11개 → close 4401

def test_orders_route_still_404():
    # GET/POST /v1/orders (페어링 토큰 포함) → 404
```

- [ ] **Step 2: 실패 확인** — Run: `uv run --project gateway pytest -q gateway/tests/test_stream.py` / Expected: FAIL
- [ ] **Step 3: 구현**
- [ ] **Step 4: 통과 확인** — Run: `uv run --project gateway pytest -q gateway/tests` / Expected: PASS
- [ ] **Step 5: 실연결 확인 (네트워크, team-03 앱 꺼진 상태)** — `scripts/gateway.sh`를 백그라운드로 띄우고, 페어링 토큰을 출력하지 않는 일회성 websockets 클라이언트로 `["039490","005930","000660","001"]`을 구독해 10초 동안 받은 `(capability, code, state)` 집합만 출력한다.

Expected: 네 코드 모두 `candles_1d`가 LIVE 또는 DELAYED, `balance`와 `news`가 각각 1개 이상, 오류 로그에 키·계좌번호 없음. 결과(상태 집합)를 `artifacts/trader-spread/gateway-live.txt`에 저장.

- [ ] **Step 6: 커밋** — `git commit -m "Stream daily, four-hour, index, balance and news observations"`

---

### Task 9: 배치 모델 (격자, 프리셋, 정보량)

**Files:**
- Create: `quest/Assets/SpatialTrading/Domain/TraderCatalog.cs`, `quest/Assets/SpatialTrading/Domain/SpreadLayout.cs`
- Modify: `quest/Assets/SpatialTrading/Domain/InstrumentRef.cs`
- Test: `quest/Assets/SpatialTrading/Tests/EditMode/SpreadLayoutTests.cs`

**Interfaces:**
- Produces:
  - `enum InstrumentKind { Stock, Index }`; `InstrumentRef`에 `Kind` 속성과 선택 인자 `InstrumentKind kind = InstrumentKind.Stock` 추가(기존 호출 그대로 컴파일).
  - `static class TraderCatalog`: `Kiwoom`(`039490`, "키움증권"), `Samsung`(`005930`, "삼성전자"), `SkHynix`(`000660`, "SK하이닉스"), `Kospi`(`001`, "코스피", Index); `IReadOnlyList<InstrumentRef> Charts`(이 순서); `const string OrderableCode = "005930"`.
  - `enum PanelId { ChartKiwoom, ChartSamsung, ChartSkHynix, ChartKospi, OrderBook, News, Balance, Order }`
  - `enum Plane { Eye, Hand }`
  - `readonly struct PanelRect(Plane plane, int x, int y, int width, int height)` — 격자 칸 단위. `(x, y)`는 왼쪽 아래 칸, 패널은 칸 `x..x+width−1`, `y..y+height−1`을 차지한다. 원점 칸 0은 평면 중앙 바로 오른쪽·위, y는 위가 +.
  - `sealed class SpreadLayout`:
    - `const float CellMeters = 0.05f`; 평면 경계(칸, 양끝 포함): Eye는 머리 앞 1.10m·머리 높이, x −20..19, y −6..5. Hand는 머리 앞 0.60m·머리 높이 −0.45m, x −10..9, y −3..2.
    - `static SpreadLayout TraderDefault()` — 기본 프리셋 `(plane, x, y, w, h)`: ChartKiwoom `(Eye, −19, 1, 7, 5)`, ChartSamsung `(Eye, −12, 1, 7, 5)`, ChartSkHynix `(Eye, −19, −5, 7, 5)`, ChartKospi `(Eye, −12, −5, 7, 5)`, OrderBook `(Eye, −4, −5, 9, 11)`, News `(Eye, 5, −5, 7, 11)`, Balance `(Hand, −9, −2, 9, 5)`, Order `(Hand, 1, −3, 9, 6)`. 겹침 없음.
    - `PanelRect this[PanelId id]`, `bool IsDismissed(PanelId id)`, `IReadOnlyList<PanelId> Dismissed`
    - `PanelRect Drop(PanelId id, Plane plane, float xMeters, float yMeters)` — `(xMeters, yMeters)`는 패널 중심의 평면 좌표. 스냅은 `x = (int)Math.Round(xMeters / CellMeters − width / 2.0, MidpointRounding.AwayFromZero)`(y도 같은 식), 평면 경계 안으로 끌어오기, 다른 패널과 겹치면 겹치지 않는 가장 가까운 칸으로 밀기. 결과를 저장하고 반환.
    - `PanelRect Resize(PanelId id, int width, int height)` — 패널 종류별 최소·최대(차트 4×3..16×10, 호가 6×8..12×12, 뉴스 5×6..10×12, 잔고 6×3..12×6, 주문 6×4..12×7)로 자르고 경계·겹침 규칙 적용.
    - `void Dismiss(PanelId id)`, `PanelRect Restore(PanelId id)` — 되살리면 기본 프리셋 자리, 그 자리가 막혀 있으면 가장 가까운 빈 칸.
    - `void Reset()` — 모든 패널(치운 것 포함)을 기본 프리셋으로.
    - `static ChartDetail ChartDetailFor(PanelRect r)` — `enum ChartDetail { Line, Candles, CandlesWithAxes }`: 넓이 ≤ 20칸 Line, ≤ 63칸 Candles, 그보다 크면 CandlesWithAxes. 기본 7×5=35 → Candles.
    - `static int BookDepthFor(PanelRect r)` — 높이 ≥ 11칸이면 10, 아니면 5. 기본 호가 11 → 10.

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
[Test] public void TraderDefaultPlacesEveryPanelWithoutOverlap() { /* 8개 모두 있음, 같은 평면끼리 교차 없음, 차트 4개와 호가·뉴스는 Eye, 잔고·주문은 Hand */ }
[Test] public void DropSnapsToNearestCell() { /* Drop(News, Eye, 0.62f, 0.02f) → x == 9, y == −5 (12.4−3.5=8.9→9, 0.4−5.5=−5.1→−5) */ }
[Test] public void DropOutsideBoundsIsPulledBackInside() { /* Drop(News, Eye, 5f, 3f) → 차지하는 칸 전체가 x −20..19, y −6..5 안 */ }
[Test] public void DropOntoAnotherPanelMovesToNearestFreeCell() { /* 뉴스를 호가 중앙에 Drop → 호가와 겹치지 않음 */ }
[Test] public void ResizeIsClampedPerPanelKind() { /* Resize(ChartSamsung, 1, 1) → 4×3; Resize(ChartSamsung, 40, 40) → 16×10 이하이고 경계 안 */ }
[Test] public void ResetRestoresDismissedAndMovedPanels() { /* Dismiss(Order), Drop(OrderBook…) → Reset() → 둘 다 TraderDefault와 같고 Dismissed 비어 있음 */ }
[Test] public void RestoreReturnsToDefaultSlotOrNearestFree() { /* Dismiss(News), 다른 패널을 뉴스 기본 자리로 Drop, Restore(News) → 겹치지 않음 */ }
[Test] public void DetailFollowsSize() { /* 4×3 → Line, 7×5 → Candles, 12×8 → CandlesWithAxes; 호가 9×11 → 10, 9×8 → 5 */ }
[Test] public void KospiIsAnIndex() { /* TraderCatalog.Kospi.Kind == Index, Charts 순서 039490,005930,000660,001 */ }
```

- [ ] **Step 2: 실패 확인** — Run: `DOTNET=~/.dotnet/dotnet scripts/test_domain.sh` / Expected: 컴파일 오류(`SpreadLayout` 없음)
- [ ] **Step 3: 구현**
- [ ] **Step 4: 통과 확인** — 같은 명령 / Expected: 전체 PASS(기존 119개 포함)
- [ ] **Step 5: 커밋** — `git commit -m "Model the trader spread grid, default preset and size-driven detail"`

---

### Task 10: 선택, 주문 티켓, 던지기 판정

**Files:**
- Create: `quest/Assets/SpatialTrading/Domain/OrderTicket.cs`, `quest/Assets/SpatialTrading/Domain/FlickDetector.cs`
- Test: `quest/Assets/SpatialTrading/Tests/EditMode/OrderTicketTests.cs`, `quest/Assets/SpatialTrading/Tests/EditMode/FlickDetectorTests.cs`

**Interfaces:**
- Consumes: `TraderCatalog`, `InstrumentKind`, `PanelId` (Task 9)
- Produces:
  - `enum TradingSession { KrxRegular, Nxt, Closed }`; `static TradingSession SessionAt(DateTimeOffset kst)` — 게이트웨이 `active_market`과 같은 창(NXT 08:00–08:50, KRX 09:00–15:30, NXT 15:30–20:00, 주말 Closed).
  - `enum OrderType { Limit, Market, BestLimit, PriorityLimit, Conditional }` — 표시명 지정가·시장가·최유리·최우선·조건부, 키움 kt10000 `trde_tp` "0","3","6","7","5".
  - `static class OrderTypeTable`: `bool Enabled(OrderType t, TradingSession s)`, `string DisabledReason(OrderType t, TradingSession s)`, `OrderType Default(TradingSession s)`. 표: KrxRegular → Limit·Market·BestLimit·PriorityLimit 활성, 기본 Market. Nxt → Limit만 활성(나머지 이유 "NXT 미지원"), 기본 Limit이고 가격은 매도 1호가("최우선 매도호가 지정가"). Closed → 모두 비활성("장 마감"). Conditional은 항상 비활성("시연 범위 밖"). 구현 전에 키움 kt10000 문서(로컬 `docs/KIWOOM_API_MAPPING.md`, kiwoom-cli `order buy` 설명)로 NXT 가능 유형을 확인하고, 문서가 다르면 표와 테스트를 문서대로 고친 뒤 근거를 클래스 주석에 한 줄로 적는다.
  - `enum TicketState { Idle, Locked, Preflight }`
  - `sealed class OrderTicket`:
    - `void Select(InstrumentRef instrument, TradingSession session)` — 005930이 아니면 `Locked`, `LockReason = "시연 안전장치: 삼성전자만"`; 지수면 `LockReason = "지수 · 주문 불가"`. 종목이 바뀌면 PREFLIGHT는 취소.
    - `bool TryChooseType(OrderType t)` — 비활성이면 false, 상태 그대로.
    - `OrderType Type`, `int Quantity => 1`, `bool SellLocked => true`
    - `bool TryPrepare(decimal? bestAsk, decimal? lastPrice, out OrderPreview preview)` — Locked·Closed·가격 없음(지정가류에 bestAsk 없음)이면 false. 성공하면 `Preflight`.
    - `void Cancel()` — Preflight → Idle. 패널 치우기와 종목 변경이 호출한다.
    - `sealed class OrderPreview(string code, string name, string side = "매수", OrderType type, string kiwoomTradeType, decimal? price, int quantity, string market, string badge = "PREFLIGHT")` — 시장가는 price null.
  - `sealed class FlickDetector`: `void Begin(float t, float x, float y, float z)`, `void Sample(float t, float x, float y, float z)`, `bool ReleaseIsFlick(float t)` — 놓기 직전 0.15초 동안의 수평 속도가 1.2 m/s 이상이고 그 구간 수평 이동이 0.15 m 이상이면 true. 표본이 2개 미만이면 false. 수직(위·아래)만 빠른 이동은 flick 아님.

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
[Test] public void SamsungInRegularSessionDefaultsToMarketOrder() { /* Select(Samsung, KrxRegular) → Idle, Type == Market */ }
[Test] public void NxtDefaultsToLimitAtBestAsk() { /* Select(Samsung, Nxt); TryPrepare(225000m, 224500m, out p) → p.Type == Limit, p.Price == 225000m, p.KiwoomTradeType == "0", p.Market == "NXT" */ }
[Test] public void OtherStockLocksWithReasonWhateverTypeIsChosen() { /* Select(SkHynix, KrxRegular) → Locked, LockReason "시연 안전장치: 삼성전자만"; TryChooseType(Limit) → 상태 Locked 유지; TryPrepare → false */ }
[Test] public void KospiLocksAsIndex() { /* Select(Kospi, …) → Locked, "지수 · 주문 불가" */ }
[Test] public void ConditionalIsAlwaysDisabled() { /* 모든 세션에서 Enabled(Conditional) == false */ }
[Test] public void NxtDisablesMarketWithReason() { /* Enabled(Market, Nxt) == false, DisabledReason == "NXT 미지원" */ }
[Test] public void ChangingInstrumentOrDismissCancelsPreflight() { /* Prepare 성공 → Select(SkHynix) → Preflight 아님; 다시 Samsung Prepare → Cancel() → Idle */ }
[Test] public void ClosedSessionCannotPrepare() { /* Select(Samsung, Closed) → TryPrepare false */ }
[Test] public void QuantityIsAlwaysOneAndSellLocked() { }
[Test] public void FastSidewaysReleaseIsFlick() { /* 0.1초에 0.2m 수평 → true */ }
[Test] public void SlowDragOrShortJerkIsNotFlick() { /* 1초에 0.3m → false; 0.15초에 0.05m(속도 높아도 거리 짧음) → false; 수직 0.3m/0.1초 → false */ }
[Test] public void SessionWindowsMatchGateway() { /* 08:49 Nxt, 08:55 Closed, 09:00 KrxRegular, 15:30 Nxt, 20:00 Closed, 토요일 Closed */ }
```

- [ ] **Step 2: 실패 확인** — Run: `DOTNET=~/.dotnet/dotnet scripts/test_domain.sh` / Expected: 컴파일 오류
- [ ] **Step 3: 키움 문서로 NXT 유형 확인** — 결과를 ledger와 클래스 주석에 남긴다.
- [ ] **Step 4: 구현**
- [ ] **Step 5: 통과 확인** — 같은 명령 / Expected: PASS
- [ ] **Step 6: 커밋** — `git commit -m "Lock the order ticket to Samsung with session-aware order types and flick detection"`

---

### Task 11: Unity 스트림 계약과 세션 구독

**Files:**
- Create: `quest/Assets/SpatialTrading/Domain/AccountAndNews.cs`
- Modify: `quest/Assets/SpatialTrading/Domain/MarketData.cs` (`DataOrigin.News`), `quest/Assets/SpatialTrading/Runtime/Market/GatewayContract.cs`(origin 검사), `quest/Assets/SpatialTrading/Runtime/Market/GatewayStreamContract.cs`, `quest/Assets/SpatialTrading/Runtime/Market/MarketDataSession.cs`
- Test: `quest/Assets/SpatialTrading/Tests/EditMode/GatewayStreamContractTests.cs`

**Interfaces:**
- Consumes: Task 8 wire, `TraderCatalog` (Task 9)
- Produces:
  - `Holding(string code, string name, long quantity, decimal? averagePrice, decimal? currentPrice, decimal? profit, decimal? profitRate)`, `BalanceSnapshot(string account, decimal? totalEvaluation, decimal? totalProfit, decimal? totalProfitRate, decimal? orderableCash, IReadOnlyList<Holding> holdings)`, `NewsItem(bool isFiling, string source, string title, DateTimeOffset publishedAt, IReadOnlyList<string> mentions, string filingType)`, `NewsFeed(IReadOnlyList<NewsItem> items)`.
  - `StreamCapability`에 `CandlesDaily`, `CandlesFourHour`, `Balance`, `News` 추가(wire `candles_1d`, `candles_4h`, `balance`, `news`). `StreamUpdate`에 `Observation<BalanceSnapshot> Balance`, `Observation<NewsFeed> News` 추가.
  - 디코더 규칙: 코드는 `^([0-9]{6}|[0-9]{3})$` 또는 balance는 `ACCOUNT`, news는 `NEWS`만. origin은 news만 `NEWS`, 나머지는 `KIWOOM`. 계좌 문자열은 `^[0-9]{3}\*+[0-9]{2}$`가 아니면 거부(마스킹 안 된 값이 오면 버림). 금액은 기존 `FinancialDecimal`. 알 수 없는 capability는 기존처럼 예외.
  - `MarketDataSession`: `Select(ShellState …)` 대신 `void SubscribeAll(Guid correlationId)`로 `TraderCatalog.Charts` 4개 코드를 상시 구독. 조회: `Quote(code)`, `Book(code)`, `Daily(code)`, `FourHour(code)`, `Balance()`, `News()`, `MarketLabel(code)`, `MarketLabel(code, interval)`. 미리보기 모드와 설정 없음 모드의 기존 규칙(UNAVAILABLE + 이유)은 새 조회에도 같게 적용. `Changed` 이벤트 유지. 하트비트 노화 규칙 유지.

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
[Test] public void ParsesDailyCandlesForIndexCode() { /* capability "candles_1d", instrument_code "001", interval "1D" → Capability == CandlesDaily, Code == "001" */ }
[Test] public void ParsesBalanceWithMaskedAccountAndEmptyHoldings() { /* "123*****90", holdings [] → Balance.Value.Holdings.Count == 0, OrderableCash == 1032000m */ }
[Test] public void RejectsUnmaskedAccount() { /* "1234567890" → GatewayContract.Invalid 예외 */ }
[Test] public void ParsesNewsWithNewsOrigin() { /* origin "NEWS" → Origin == DataOrigin.News, items[0].IsFiling, Mentions 포함 */ }
[Test] public void RejectsNewsOriginOnMarketCapabilities() { /* quote에 origin "NEWS" → 예외 */ }
[Test] public void RejectsAccountSubjectOnQuote() { /* capability "quote", instrument_code "ACCOUNT" → 예외 */ }
```

- [ ] **Step 2: 실패 확인** — Run: `DOTNET=~/.dotnet/dotnet scripts/test_domain.sh` / Expected: FAIL
- [ ] **Step 3: 구현** — 계약 디코더 먼저, 그다음 `MarketDataSession`.
- [ ] **Step 4: 통과 확인** — 같은 명령 / Expected: PASS
- [ ] **Step 5: Unity 컴파일 확인** — 열린 에디터가 있으면 에디터 콘솔 오류 0을 확인, 없으면 Run: `UNITY_EDITOR=~/Applications/UnityEditors/6000.0.67f1/Unity.app/Contents/MacOS/Unity scripts/unity.sh import` / Expected: exit 0. 이 단계에서 `FocusShell`의 옛 `Select` 호출이 깨지면 `FocusShell`이 `SubscribeAll`을 부르도록 최소 수정한다(Task 14에서 대체됨).
- [ ] **Step 6: 커밋** — `git commit -m "Decode daily, four-hour, balance and news stream updates in Unity"`

---

### Task 12: 패널 뷰 모델

**Files:**
- Create: `quest/Assets/SpatialTrading/Domain/PanelViews.cs`
- Test: `quest/Assets/SpatialTrading/Tests/EditMode/PanelViewsTests.cs`

**Interfaces:**
- Consumes: `OrderBookSnapshot`, `QuoteSnapshot`, `CandleSeries`, `BalanceSnapshot`, `NewsFeed`, `Observation<T>`, `StatusText`(기존 정직성 문구), `InstrumentRef`
- Produces (모두 UI가 그대로 그릴 문자열·비율을 가진 불변 객체):
  - `OrderBookView.From(Observation<OrderBookSnapshot> book, Observation<QuoteSnapshot> quote, int depth, InstrumentRef instrument, string market)` → `Title`("호가 · 삼성전자 NXT"), `Status`, `Asks`/`Bids`(각 `depth`개, 매도는 높은 가격이 위, `PriceText` 천 단위 콤마, `QuantityText`, `BarRatio` 0..1 = 수량 / 양쪽 최대 수량), `LastPriceText`, `BidShareText`("매수 54%", 양쪽 합이 0이면 "—"), 지수면 `IsIndex == true`, `Placeholder == "지수 · 호가 없음"`.
  - `ChartHeaderView.From(InstrumentRef, Observation<CandleSeries>, Observation<QuoteSnapshot>, string interval, string market)` → `Title`, `PriceText`, `ChangeText`(부호 `+`/`−`, U+2212), `Status`(예: "LIVE · KRX · 일봉", "장 마감 · KRX · 일봉").
  - `NewsView.Rank(Observation<NewsFeed>, string selectedCode, int max = 8)` → 선택 종목을 언급한 항목을 먼저(각 묶음 안은 최신순), 행마다 `Tag`("공시" 또는 출처), `IsFiling`(앰버), `Title`, `TimeText`("10:42" 당일, 아니면 "10/27").
  - `BalanceView.From(Observation<BalanceSnapshot>)` → 보유가 없으면 `IsEmpty == true`, `EmptyText == "보유 종목 없음"`, `OrderableText` 그대로 표시. 행: `"삼성전자 1주 · 평단 224,500"`, 손익 부호. `AccountText == "계좌 123*****90"`.
  - `OrderView.From(OrderTicket, Observation<OrderBookSnapshot>, TradingSession)` → 유형 버튼 5개(`Label`, `Enabled`, `Selected`, `Reason`), `PriceText`(시장가면 "시장가"), `QuantityText == "1주 🔒"`, `SellTabText == "매도 🔒"`, `PrepareEnabled`, `LockReason`, `Badge`.

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
[Test] public void BookViewOrdersAsksHighToLowAndComputesBidShare() { /* 매도 1..5호가 → Asks[0]이 5호가; 매수 합 54, 매도 합 46 → "매수 54%" */ }
[Test] public void BookViewUsesDepthFromLayout() { /* depth 5 → Asks.Count == 5 */ }
[Test] public void IndexBookShowsPlaceholder() { /* Kospi → IsIndex, Placeholder "지수 · 호가 없음" */ }
[Test] public void DisconnectedBookKeepsValuesWithLabel() { /* DISCONNECTED → Status에 "DISCONNECTED" 포함, 행 유지 */ }
[Test] public void NewsRankBoostsSelectedStockThenRecency() { /* 005930 선택 → 삼성 언급 항목이 더 최근인 시장 기사보다 위 */ }
[Test] public void FilingsAreTaggedForAmber() { }
[Test] public void EmptyBalanceIsDesignedState() { /* holdings [] → IsEmpty, "보유 종목 없음", OrderableText "주문가능 1,032,000원" */ }
[Test] public void ChangeTextUsesSigns() { /* +500 → "+500", −1,200 → "−1,200 (−0.53%)" */ }
[Test] public void OrderViewShowsLockedReasonForOtherStocks() { }
```

- [ ] **Step 2: 실패 확인** — Run: `DOTNET=~/.dotnet/dotnet scripts/test_domain.sh` / Expected: FAIL
- [ ] **Step 3: 구현** — 숫자 포맷은 `CultureInfo.InvariantCulture` + 천 단위 콤마, float 금지.
- [ ] **Step 4: 통과 확인** — 같은 명령 / Expected: PASS
- [ ] **Step 5: 커밋** — `git commit -m "Shape order book, chart, news, balance and order views for the spread"`

---

### Task 13: 패널 컴포넌트 (차트, 호가, 뉴스, 잔고, 주문)

**Files:**
- Modify: `quest/Assets/SpatialTrading/Runtime/Components/CandleChartGraphic.cs`
- Create: `quest/Assets/SpatialTrading/Runtime/Components/HoloStyle.cs`, `ChartPanel.cs`, `OrderBookPanel.cs`, `NewsPanel.cs`, `BalancePanel.cs`, `OrderPanel.cs` (모두 `Runtime/Components/`)

**Interfaces:**
- Consumes: Task 12 뷰 모델, `SpreadLayout.ChartDetailFor`/`BookDepthFor` (Task 9), `OrderTicket` (Task 10)
- Produces:
  - `static class HoloStyle`: `Cyan = #00e5ff`, `Amber = #ffb300`, `Ink`, `Muted`, `PanelFill`(어두운 반투명), `MinText = 16f`, `MinNumber = 22f`.
  - `CandleChartGraphic.SetSeries(CandleSeries series, ChartDetail detail)` — 상승(close ≥ open) 속 빈 시안 봉(외곽선 두께 2), 하락 채운 시안 봉. Line은 종가 선 하나. Candles는 봉 + 아래 20% 높이 거래량 막대. CandlesWithAxes는 추가로 오른쪽 가격 눈금 4개와 아래 시간 라벨 3개(TMP 자식, `MinText` 이상). 보이는 봉 수 = 패널 폭(mm) / 9, 최소 20, 최대 120.
  - 각 패널은 `MonoBehaviour`이고 `Render(<View>, PanelRect rect)` 하나로 그린다. 크기(`PanelRect`)가 바뀌면 `RectTransform.sizeDelta = (w·50mm, h·50mm)`에 맞춰 다시 배치한다(트랜스폼 scale은 1 고정).
  - `ChartPanel`: 헤더(종목·가격·부호 변화·정직성 라벨), `4H`/`1D` 토글 버튼(이벤트 `IntervalChanged(string)`), 선택 테두리(`SetSelected(bool)` — 선택 시 두께 3·밝은 시안), 패널 클릭 이벤트 `Selected`.
  - `OrderBookPanel`: 매도 위·매수 아래, 가운데 현재가 구분선, 잔량 막대(`BarRatio`), 잔량비, 시장 라벨. 지수면 플레이스홀더만.
  - `NewsPanel`: 행 최대 개수는 패널 높이에 맞춤, 공시 태그 앰버.
  - `BalancePanel`: 빈 상태는 가운데 정렬 큰 글자 "보유 종목 없음"과 주문가능 금액, 얇은 시안 원형 장식 하나(의도된 상태로 보이게).
  - `OrderPanel`: 매수/매도 탭(매도 잠금), 유형 버튼 5개(비활성은 흐리게, 누르면 이유 토스트), 가격·수량, "매수 준비 → 확인" 버튼(앰버), 미리보기 카드(PREFLIGHT 배지, 보낼 값: 종목·매수·유형·키움 매매구분 코드·가격·1주·시장). "확인"은 이 계획에서 아무 데도 보내지 않고 "S4에서 연결" 배지만 보인다.

- [ ] **Step 1: 컴포넌트 구현** — 뷰 모델 외의 판단을 넣지 않는다(문구·잠금·정렬은 모두 Task 12 뷰에서 온다).
- [ ] **Step 2: Unity 컴파일 확인** — 에디터 콘솔 오류 0 또는 `scripts/unity.sh import` exit 0.
- [ ] **Step 3: 커밋** (컴포넌트의 표시 판단은 Task 12 뷰 테스트가 덮고, 화면 확인은 Task 14 Step 4 스크린샷이 맡는다) — `git commit -m "Draw holographic chart, order book, news, balance and order panels"`

---

### Task 14: 작업공간 상호작용과 장면 생성

**Files:**
- Create: `quest/Assets/SpatialTrading/Runtime/Spread/SpreadPanel.cs`, `SpreadWorkspace.cs`, `PalmTray.cs` (모두 `Runtime/Spread/`)
- Modify: `quest/Assets/SpatialTrading/Editor/QuestShellProject.cs`, `quest/Assets/SpatialTrading/Runtime/Spatial/SurfaceBounds.cs`(스프레드 패널에서는 쓰지 않음 — 경계는 `SpreadLayout`이 맡는다)
- Replace: `quest/Assets/SpatialTrading/Scenes/FocusShell.unity` → `quest/Assets/SpatialTrading/Scenes/TraderSpread.unity` (빌드 설정의 유일한 장면)

**Interfaces:**
- Consumes: `SpreadLayout`, `FlickDetector`, `OrderTicket`, `TradingSession` (Task 9–10), 패널 컴포넌트 (Task 13), `MarketDataSession` (Task 11)
- Produces:
  - `SpreadPanel` (패널 루트마다 1개): `PanelId Id`; 잡는 동안 `Grabbable` 이동을 따르고 `FlickDetector`에 표본을 넣는다. 놓을 때: flick이면 `Dismissed` 이벤트, 아니면 머리 기준 평면 좌표로 바꿔 `SpreadWorkspace.Drop` → 결과 위치로 0.12초 ease-out 스냅. 오른쪽 아래 모서리 손잡이(시안 삼각형, 잡을 수 있는 별도 `Grabbable`)를 끌면 `SpreadLayout.Resize`로 칸 단위 크기 변경, 놓으면 스냅. 패널 회전은 항상 작업공간 기준 정면(yaw만), pitch·roll 0.
  - 치우기 연출: 던진 방향으로 0.35초 동안 0.4 m 날아가며 알파 0, 크기 0.6배, 그동안 절차적으로 만든 짧은 "whoosh" 오디오(0.25초, 노이즈 + 하강 대역 필터, 에셋 파일 없음). 끝나면 비활성화.
  - `SpreadWorkspace`: `SpreadLayout` 소유, 시작 시 머리 위치·yaw로 작업공간 원점 고정(`Recenter()` 유지), 차트 선택 → `OrderTicket.Select` + 호가/주문 패널 갱신, 주문 패널이 치워지면 `OrderTicket.Cancel()`, `Reset()`, `Restore(PanelId)`. `MarketDataSession.Changed`에 뷰 갱신. 시간대는 `TradingSession.SessionAt(DateTimeOffset.Now.ToOffset(+9h))`를 1초마다 갱신.
  - 에디터·Mac 백업 입력: 마우스 드래그로 잡기(Unity 시뮬레이터의 포인터 그대로), 빠른 드래그 → flick 같은 임계값, 키 `T` 트레이 토글, `R` 기본 배치, `1`–`4` 차트 선택.
  - `PalmTray`: 왼손 손바닥이 위를 향함(손바닥 법선 · 월드 up > 0.7)이 0.4초 이어지면 손 위 0.12 m에 트레이 표시, 0.6초 이상 벗어나면 숨김. 치운 패널마다 아이콘(패널 이름 약자, 시안 테두리), 아이콘을 잡아 0.1 m 이상 끌어내 놓으면 `Restore`. 트레이 맨 끝에 "기본 배치로" 버튼. 손 추적이 없으면(시뮬레이터 컨트롤러) `T` 키나 컨트롤러 Y 버튼으로 같은 트레이를 머리 앞 아래쪽에 띄운다.
  - `QuestShellProject.GenerateSceneCore`: OVRCameraRig(패스스루 유지) + app 오브젝트(`SpreadWorkspace`, `MarketDataSession`, `PalmTray`) + 패널 8개를 `TraderDefault` 위치에 생성. 각 패널: 월드 캔버스(scale 0.001), `PointableCanvas`(poke+ray), 배경 카드, 상단 잡기 막대, `BoxCollider`(패널 전체 크기, 두께 0.02), kinematic `Rigidbody`, `Grabbable`(MaxGrabPoints 1, 크기 조절은 모서리 손잡이만), `GrabFreeTransformer`(회전 제한: X·Z 고정), `SpreadPanel`. 메뉴 "2. Generate Focus Shell scene"은 "2. Generate Trader Spread scene"으로 이름을 바꾼다.
  - `Validate()`: 장면에 `SpreadPanel` 8개(각 `PanelId` 1개씩), 각각 `Grabbable`·`PointableCanvas`·poke/ray 상호작용, `PointableCanvasModule` 정확히 1개, 모든 TMP 글자 크기 ≥ `HoloStyle.MinText`, 가격·수량 라벨(이름에 "Price"/"Qty" 포함) ≥ `HoloStyle.MinNumber`, 모든 패널 회전의 pitch·roll == 0. 실패하면 `BuildFailedException`.

- [ ] **Step 1: 기존 장면 변경 되돌리기** — Unity Play로 생긴 변경을 버린다. Run: `git checkout -- quest/Assets/SpatialTrading/Scenes/FocusShell.unity quest/Assets/Generated/NotoSansKR.asset` / Expected: `git status --short quest/`에 두 파일 없음
- [ ] **Step 2: `SpreadPanel`, `SpreadWorkspace`, `PalmTray` 구현**
- [ ] **Step 3: 장면 생성·검증 구현 후 실행** — 에디터를 닫고 Run: `UNITY_EDITOR=… scripts/unity.sh scene && UNITY_EDITOR=… scripts/unity.sh validate` / Expected: 둘 다 exit 0, `TraderSpread.unity` 생성, `FocusShell.unity` 삭제
- [ ] **Step 4: 시뮬레이터 확인 (게이트웨이 실행 중, 미리보기 끔)** — Meta XR Simulator로 Play. 다음을 각각 스크린샷으로 `artifacts/trader-spread/`에 저장: (a) 기본 배치 전체, (b) SK하이닉스 차트 선택 → 호가 바뀜·주문 잠금 이유, (c) KOSPI 선택 → "지수 · 호가 없음", (d) 차트 하나 크게 키움(축 표시), (e) 뉴스 패널 던져 치움 직후, (f) 트레이에서 되살림, (g) `R` 리셋 후 기본 배치. 각 장면에서 정직성 라벨이 실제 상태(장중 LIVE / 장외 장 마감)와 맞는지 확인.

Expected: 7장 저장, 패널 기울기 없음, 잘린 글자 없음

- [ ] **Step 5: 도메인·게이트웨이 회귀** — Run: `DOTNET=~/.dotnet/dotnet scripts/test_domain.sh && uv run --project gateway pytest -q gateway/tests` / Expected: 둘 다 PASS
- [ ] **Step 6: 커밋** — 스크린샷은 `artifacts/`(git 무시)에 두고, 대표 1장만 `docs/images/trader-spread-live.png`로 복사해 커밋.

```bash
git add quest/Assets/SpatialTrading docs/images/trader-spread-live.png quest/ProjectSettings/EditorBuildSettings.asset
git commit -m "Generate the trader spread scene with movable, resizable and dismissable panels"
```

---

### Task 15: 옛 셸 정리와 문서

**Files:**
- Delete: `quest/Assets/SpatialTrading/Runtime/FocusShell.cs`, `Runtime/Components/ChartComponent.cs`, `Runtime/Components/SecondaryComponent.cs`, `Runtime/Interfaces/ShellButton.cs`, `Runtime/Spatial/SeatedLayout.cs`, `Runtime/Spatial/SurfaceBounds.cs`와 각 `.meta` — 단, Task 14 이후 어떤 장면·코드에서도 참조하지 않을 때만. 도메인 `ShellState`/`ShellActions`/`SyntheticCatalog`와 그 테스트는 다른 곳에서 쓰지 않으면 같이 지운다(`grep`으로 확인).
- Modify: `README.md`(실행: `scripts/gateway.sh`, 셸 환경변수 주의, 새 env 키), `docs/ARCHITECTURE.md`(새 capability와 서비스), `docs/KIWOOM_API_MAPPING.md`(ka10081/ka20005/ka20006/kt00018/kt00010/0J/04 실제 사용 필드와 지수 스케일), `docs/INTERACTION_MODEL.md`(잡기·크기·던지기·트레이·리셋, 에디터 단축키)

- [ ] **Step 1: 참조 확인** — Run: `grep -rn "FocusShell\|ChartComponent\|SecondaryComponent\|ShellButton\|SeatedLayout\|SurfaceBounds" quest/Assets/SpatialTrading --include=*.cs --include=*.unity | grep -v "^quest/Assets/SpatialTrading/Runtime/\(FocusShell\|Components/ChartComponent\|Components/SecondaryComponent\|Interfaces/ShellButton\|Spatial/SeatedLayout\|Spatial/SurfaceBounds\).cs"` / Expected: 출력 없음(있으면 그 파일은 남긴다)
- [ ] **Step 2: 삭제와 문서 수정**
- [ ] **Step 3: 전체 검증** — Run: `DOTNET=~/.dotnet/dotnet scripts/test_domain.sh && uv run --project gateway pytest -q gateway/tests && UNITY_EDITOR=… scripts/unity.sh validate` / Expected: 모두 PASS / exit 0
- [ ] **Step 4: 비밀 값 점검 (값 출력 없음)** — Run: `(set -a; . gateway/.env; set +a; git grep -nIF -e "$DART_API_KEY" -e "$KIWOOM_APPKEY" -e "$KIWOOM_SECRETKEY" -e "$GATEWAY_PAIRING_TOKEN" -- . ':!gateway/.env' | cut -d: -f1,2)` / Expected: 출력 없음(있으면 파일:줄만 보이고 값은 안 보인다)
- [ ] **Step 5: 커밋** — `git commit -m "Retire the picker shell and document the trader spread"`
