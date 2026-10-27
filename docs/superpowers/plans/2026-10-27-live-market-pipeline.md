# Live Market Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 실제 키움 시세(삼성전자 호가 10단계·체결·1분봉)가 Python 게이트웨이를 거쳐 Unity로 실시간 푸시되고, 에디터와 Quest 3S에서 기존 Focus 화면에 LIVE로 표시된다.

**Architecture:** 게이트웨이가 키움 토큰을 직접 발급하고, 키움 WebSocket 연결 하나로 KRX·NXT의 0B(체결)·0D(호가잔량)를 구독한다. 초기 분봉은 REST ka10080으로 받는다. `MarketHub`가 종목·시장별 최신 상태를 유지하고, 페어링 토큰으로 인증된 Unity 클라이언트에게 기존 Observation 봉투를 그대로 감싼 스트림 메시지로 푸시한다. Unity는 `ClientWebSocket` 수신 스레드 → 메인 스레드 큐 → 기존 `GatewayContract` 디코더 → `MarketDataSession` 순으로 반영한다.

**Tech Stack:** Python 3.11, FastAPI, httpx, websockets 16, pytest/pytest-asyncio, uv · Unity 6000.0.67f1, Newtonsoft.Json, System.Net.WebSockets · .NET 8 NUnit 하네스(`tests/Domain`).

**Spec:** `docs/superpowers/specs/2026-10-27-investment-jarvis-design.md` (§3 제약, §4 구성, §5 S2, §7 가드레일). 이 계획은 발표 범위 중 **실시세 파이프라인**만 다룬다. 주문(S4), 차트 컴포넌트·확대(S2-b), 자비스(S3), 아이즈프리(S5), 비주얼(S1)은 후속 계획이다.

## Global Constraints

- 키움 REST origin `https://api.kiwoom.com`, WS `wss://api.kiwoom.com:10000/api/dostk/websocket` (실전 도메인만; 모의 도메인 코드 경로 없음).
- 앱키·시크릿은 `gateway/.env`의 `KIWOOM_APPKEY`, `KIWOOM_SECRETKEY`에서만 읽는다. 값은 team-03 `members/gejyn14/app/.env`와 같다. `.env`는 커밋 금지(이미 `.gitignore`의 `.env` 규칙에 해당).
- 키움 토큰·앱키·시크릿은 로그, 에러 응답, 클라이언트 메시지에 절대 나가지 않는다.
- 금융 값은 binary float를 거치지 않는다(Python `Decimal`/`int`, C# `decimal`/`long`). 기존 wire 규칙(소수 문자열, 정수 문자열, null 유지) 유지.
- 가격류 필드의 `+`/`-` 접두사는 전일 대비 방향 표시이며 값은 절댓값이다: REST `cur_prc`, `open_pric`, `high_pric`, `low_pric`, WS `10`, `16`, `17`, `18`, `41`–`60`. 부호를 유지하는 필드: WS `11`(전일대비), `12`(등락율), REST `pred_pre`. 근거: kiwoom-cli `formatters._ABS_FIELDS`/`_SIGNED_FIELDS`와 `normalize.parse_signed`.
- 거래소별 종목코드: KRX `005930`, NXT `005930_NX` (키움 REST 문서 "거래소별 종목코드 (KRX:039490,NXT:039490_NX,SOR:039490_AL)").
- 시간은 모두 KST(+09:00) 명시. 체결시간 WS `20`, 호가시간 WS `21`은 `HHMMSS`, REST `cntr_tm`은 `YYYYMMDDHHMMSS`.
- 이 계획에는 주문 경로가 없다. `/v1/orders` 계열 라우트는 계속 404.
- 키움 조회 오류코드 1700–1702는 백오프, 무한 재시도 금지. 8005(토큰 무효)는 분당 1회까지만 재발급.
- 게이트웨이는 LAN에 바인딩할 수 있지만 모든 클라이언트 접근(REST·WS)은 `GATEWAY_PAIRING_TOKEN` 일치 시에만 허용, 비교는 `hmac.compare_digest`.

## Review Focus

1. **키움 연결이 시연 중 끊기거나 토큰이 무효화됨(team-03 재발급 포함)** → 게이트웨이는 해당 시장 데이터를 DISCONNECTED로 바꿔 푸시하고 백오프 재연결, Unity는 마지막 값을 DISCONNECTED 라벨과 함께 보인다. LIVE로 얼어 있으면 안 된다. (Task 5, Task 6 테스트)
2. **조용한 시장(점심, 애프터마켓 한산)** → 체결이 몇 분 없어도 연결이 살아 있으면 STALE이 아니다. Unity의 30초 노화는 틱이 아니라 게이트웨이 하트비트 기준. (Task 6, Task 9 테스트)
3. **15:30 KRX→NXT 전환** → KRX와 NXT 체결이 하나의 봉에 섞이지 않는다. 시장별 별도 시리즈, 표시 시장은 KST 시계로 결정, 라벨이 바뀐다. (Task 4, Task 6 테스트)
4. **이상한 REAL 프레임(빈 values, 가격 누락, 부호만 있는 값, 알 수 없는 type)** → 해당 프레임만 버리고 루프는 계속, 0원이나 음수 가격을 만들지 않는다. (Task 2, Task 5 테스트)
5. **잘못된 페어링 토큰·낯선 LAN 클라이언트** → 데이터 0바이트, WS close 4401, REST 401. 키움 토큰은 어떤 응답에도 없다. (Task 7 테스트)

---

## File Structure

Gateway (`gateway/src/spatial_gateway/`)

| 파일 | 책임 |
|---|---|
| `settings.py` (신규) | 환경변수 → `Settings` (키, 페어링 토큰, 감시 종목, 허용 호스트) |
| `kiwoom/values.py` (신규) | 키움 문자열 값 파싱: 절댓값 가격, 부호 값, KST 시각 |
| `kiwoom/auth.py` (신규) | `TokenProvider`: au10001 발급, 만료 관리, 8005 재발급 제한 |
| `kiwoom/rest.py` (신규) | `KiwoomRest.request`, `minute_candles` (ka10080) |
| `kiwoom/realtime.py` (신규) | `parse_real_frame` (순수), `KiwoomRealtime` (LOGIN/REG/PING/재연결) |
| `market/session.py` (신규) | `active_market(now)`: KRX/NXT/None |
| `market/hub.py` (신규) | `MarketHub`: 시장별 시세 상태, 1분봉 갱신, Observation 생성, 구독자 팬아웃 |
| `stream.py` (신규) | `/v1/stream` WebSocket: 페어링 인증, 스냅샷+갱신 푸시, 하트비트 |
| `capabilities.py` (수정) | `SpecificationGatedMarket` 삭제, `HubMarket`(REST 조회가 허브를 읽음) |
| `app.py` (수정) | lifespan에서 키움 구성요소 기동, 페어링 의존성, 허용 호스트 설정 |

Unity (`quest/Assets/SpatialTrading/`)

| 파일 | 책임 |
|---|---|
| `Runtime/Market/GatewayStreamContract.cs` (신규) | 스트림 메시지 → `StreamUpdate` 디코딩 (순수, .NET 테스트 대상) |
| `Runtime/Market/GatewayStream.cs` (신규) | `ClientWebSocket` 수신 루프, 재연결, 메인 스레드 큐 |
| `Runtime/Market/MarketDataSession.cs` (수정) | 스트림 모드 반영, 하트비트 기반 노화, 허용 주소 규칙 |
| `Tests/EditMode/GatewayStreamContractTests.cs` (신규) | 스트림 계약 테스트 |
| `tests/Domain/Domain.Tests.csproj` (수정) | 새 계약 파일 컴파일 포함 |

---

### Task 1: 설정과 키움 토큰

**Files:**
- Create: `gateway/src/spatial_gateway/settings.py`, `gateway/src/spatial_gateway/kiwoom/__init__.py`, `gateway/src/spatial_gateway/kiwoom/auth.py`, `gateway/.env.example`
- Modify: `gateway/pyproject.toml` (dependencies에 `"websockets>=16,<17"` 추가)
- Test: `gateway/tests/test_auth.py`

**Interfaces:**
- Produces:
  - `Settings` (frozen dataclass): `appkey: str`, `secretkey: str`, `pairing_token: str`, `watch_codes: tuple[str, ...]`, `allowed_hosts: tuple[str, ...]`, `rest_origin: str = "https://api.kiwoom.com"`, `ws_url: str = "wss://api.kiwoom.com:10000/api/dostk/websocket"`; `Settings.from_env(env: Mapping[str, str]) -> Settings`
  - `class KiwoomAuthError(Exception)` — 메시지에 키·토큰 값 없음
  - `class TokenProvider(client: httpx.AsyncClient, settings: Settings, clock: Callable[[], datetime])`: `async def token(self) -> str`, `async def invalidate(self) -> bool` (재발급 허용되면 True)

- [ ] **Step 1: `.env` 준비 (사람 또는 에이전트가 1회)**

`gateway/.env` 생성: team-03 `members/gejyn14/app/.env`에서 `KIWOOM_APPKEY`, `KIWOOM_SECRETKEY` 두 줄을 복사하고, `GATEWAY_PAIRING_TOKEN`(32바이트 이상 무작위: `python3 -c "import secrets;print(secrets.token_urlsafe(32))"`), `GATEWAY_WATCH_CODES=005930,000660`, `GATEWAY_ALLOWED_HOSTS=127.0.0.1,localhost`를 추가. 값을 출력하지 않는다. `.env.example`에는 같은 키 이름과 빈 값/예시만.

Run: `git check-ignore gateway/.env`
Expected: `gateway/.env` 출력(무시됨)

- [ ] **Step 2: 실패하는 테스트 작성** (`gateway/tests/test_auth.py`, `httpx.MockTransport` 사용)

```python
async def test_token_issued_once_and_reused_until_five_minutes_before_expiry(): ...
    # au10001 응답 {"token":"T1","expires_dt":"20261027235959","return_code":0}
    # clock = 2026-10-27T12:00+09:00 → token() 두 번 호출, 요청 1회
    # clock = 2026-10-27T23:56+09:00 → token() 재발급(요청 2회)
    # 요청 헤더 api-id == "au10001", 본문 grant_type == "client_credentials", authorization 헤더 없음

async def test_invalidate_allows_one_reissue_per_minute():
    # invalidate() → True, 즉시 다시 invalidate() → False, clock +61s → True

async def test_broker_error_never_leaks_secrets():
    # 응답 {"return_code":8001,"return_msg":"..."} → KiwoomAuthError
    # str(error)에 appkey/secretkey/토큰 값이 없고 "8001"은 포함

def test_settings_require_keys_and_strong_pairing_token():
    # KIWOOM_APPKEY 누락 → ValueError, GATEWAY_PAIRING_TOKEN 길이 < 32 → ValueError
    # watch_codes 각 항목은 ^[0-9]{6}$ 이 아니면 ValueError
```

- [ ] **Step 3: 실패 확인**

Run: `uv run --project gateway pytest -q gateway/tests/test_auth.py`
Expected: FAIL (`ModuleNotFoundError: spatial_gateway.settings`)

- [ ] **Step 4: 구현** — `Settings.from_env`, `TokenProvider`. `expires_dt`가 없거나 파싱 불가면 발급 후 1시간 만료로 간주. 토큰 발급 요청은 `POST {rest_origin}/oauth2/token`, 헤더 `content-type: application/json;charset=UTF-8`, `api-id: au10001`. 동시 호출은 `asyncio.Lock`으로 1회만 발급.

- [ ] **Step 5: 통과 확인**

Run: `uv run --project gateway pytest -q gateway/tests/test_auth.py`
Expected: PASS

- [ ] **Step 6: 실토큰 확인 (네트워크, 1회)** — team-03 앱이 꺼져 있는 시간에 실행. 토큰 값은 출력하지 않는다.

Run: `uv run --project gateway --env-file gateway/.env python -c "import asyncio,httpx;from datetime import datetime,timezone,timedelta;from spatial_gateway.settings import Settings;from spatial_gateway.kiwoom.auth import TokenProvider;import os;s=Settings.from_env(os.environ);print(len(asyncio.run(TokenProvider(httpx.AsyncClient(),s,lambda:datetime.now(timezone(timedelta(hours=9)))).token()))>0)"`
Expected: `True`

- [ ] **Step 7: 커밋**

```bash
git add gateway/pyproject.toml gateway/uv.lock gateway/.env.example gateway/src/spatial_gateway/settings.py gateway/src/spatial_gateway/kiwoom gateway/tests/test_auth.py
git commit -m "Issue Kiwoom tokens from gateway settings with bounded reissue"
```

---

### Task 2: 키움 값 파싱

**Files:**
- Create: `gateway/src/spatial_gateway/kiwoom/values.py`
- Test: `gateway/tests/test_values.py`

**Interfaces:**
- Produces:
  - `KST = timezone(timedelta(hours=9))`
  - `def price(raw: str | None) -> Decimal | None` — 접두 부호 제거한 절댓값, 빈 값/`"0"`/숫자 아님 → `None`
  - `def signed(raw: str | None) -> Decimal | None` — 부호 유지
  - `def quantity(raw: str | None) -> int | None` — 부호 제거한 정수, 숫자 아님 → `None`
  - `def kst_time_today(hhmmss: str, today: date) -> datetime | None`
  - `def kst_datetime(yyyymmddhhmmss: str) -> datetime | None`

- [ ] **Step 1: 실패하는 테스트 작성**

```python
@pytest.mark.parametrize("raw,expected", [("-78800", Decimal("78800")), ("+156600", Decimal("156600")),
                                          ("78900", Decimal("78900")), ("", None), ("0", None), ("-", None),
                                          (None, None), ("12a", None)])
def test_price_strips_direction_sign(raw, expected): assert price(raw) == expected

@pytest.mark.parametrize("raw,expected", [("-600", Decimal("-600")), ("+1.25", Decimal("1.25")), ("0", Decimal("0")), ("", None)])
def test_signed_keeps_sign(raw, expected): assert signed(raw) == expected

def test_quantity_parses_integer_strings_only():
    assert quantity("7913") == 7913 and quantity("-120") == 120 and quantity("1.5") is None

def test_times_are_explicit_kst():
    assert kst_datetime("20250917132000") == datetime(2025, 9, 17, 13, 20, tzinfo=KST)
    assert kst_time_today("153001", date(2026, 10, 27)) == datetime(2026, 10, 27, 15, 30, 1, tzinfo=KST)
    assert kst_time_today("99", date(2026, 10, 27)) is None
```

- [ ] **Step 2: 실패 확인** — Run: `uv run --project gateway pytest -q gateway/tests/test_values.py` → FAIL (import error)
- [ ] **Step 3: 구현** — `Decimal(str)`만 사용, `float()` 금지.
- [ ] **Step 4: 통과 확인** — 같은 명령 → PASS
- [ ] **Step 5: 커밋** — `git commit -m "Parse Kiwoom signed price strings and KST times exactly"`

---

### Task 3: REST 분봉

**Files:**
- Create: `gateway/src/spatial_gateway/kiwoom/rest.py`
- Create: `gateway/tests/fixtures/ka10080_005930.json` (Step 6에서 실응답 녹화로 교체 전, 키움 문서 예시로 시작)
- Test: `gateway/tests/test_rest.py`

**Interfaces:**
- Consumes: `TokenProvider.token/invalidate`, `values.price/quantity/kst_datetime` (Task 1, 2)
- Produces:
  - `class KiwoomApiError(Exception)`: 속성 `code: int`
  - `class KiwoomRest(client: httpx.AsyncClient, tokens: TokenProvider, settings: Settings)`:
    - `async def request(self, api_id: str, path: str, body: dict) -> dict`
    - `async def minute_candles(self, code: str, limit: int = 240) -> list[MinuteBar]`
  - `MinuteBar` (frozen dataclass): `start: datetime`(KST, 분 시작), `open/high/low/close: Decimal`, `volume: int`

- [ ] **Step 1: 실패하는 테스트 작성**

```python
async def test_minute_candles_parse_ascending_absolute_prices():
    # MockTransport가 fixtures/ka10080_005930.json 반환
    bars = await rest.minute_candles("005930")
    assert [b.start for b in bars] == sorted(b.start for b in bars)          # 응답은 최신순, 결과는 오름차순
    assert bars[-1].close == Decimal("78800") and bars[-1].high == Decimal("78900")
    assert bars[-1].start == datetime(2025, 9, 17, 13, 20, tzinfo=KST)
    # 요청: POST /api/dostk/chart, 헤더 api-id=ka10080, authorization="Bearer <token>",
    #       본문 {"stk_cd":"005930","tic_scope":"1","upd_stkpc_tp":"0"}

async def test_8005_reissues_once_then_raises():
    # 첫 응답 return_code 8005 → invalidate() → 재시도 1회 → 다시 8005면 KiwoomApiError(code=8005)

async def test_rate_limit_is_not_retried():
    # return_code 1700 → KiwoomApiError(code=1700), 요청 1회

async def test_bar_with_unparseable_price_is_dropped_not_zeroed():
    # 한 항목 open_pric="" → 해당 봉 제외, 나머지 유지
```

- [ ] **Step 2: 실패 확인** — `uv run --project gateway pytest -q gateway/tests/test_rest.py` → FAIL
- [ ] **Step 3: 구현** — 요청 `POST {rest_origin}{path}`, 헤더 `content-type`, `api-id`, `authorization: Bearer`. 응답 `return_code`가 0이 아니면 `KiwoomApiError`. 분봉 리스트 키 `stk_min_pole_chart_qry`, 필드 `cntr_tm`, `open_pric`, `high_pric`, `low_pric`, `cur_prc`, `trde_qty`. OHLC 경계(low ≤ open,close ≤ high) 위반 봉은 버린다. `limit`개만 남긴다(최신 쪽).
- [ ] **Step 4: 통과 확인** — PASS
- [ ] **Step 5: 실응답 녹화 (네트워크, 1회)** — `KiwoomRest.request("ka10080", "/api/dostk/chart", {...})`의 원본 응답을 `gateway/tests/fixtures/ka10080_005930.json`에 저장하는 일회성 명령을 실행하고, 저장 전 응답에 토큰·계좌 정보가 없음을 확인. 테스트를 다시 돌려 PASS(필드명이 문서와 다르면 여기서 드러난다; 다르면 실응답 기준으로 고치고 테스트 기대값 갱신).
- [ ] **Step 6: 커밋** — `git commit -m "Load live one-minute candles from Kiwoom ka10080"`

---

### Task 4: 시장 세션 판정

**Files:**
- Create: `gateway/src/spatial_gateway/market/__init__.py`, `gateway/src/spatial_gateway/market/session.py`
- Test: `gateway/tests/test_session.py`

**Interfaces:**
- Produces:
  - `class Market(StrEnum)`: `KRX = "KRX"`, `NXT = "NXT"`
  - `def realtime_item(code: str, market: Market) -> str` — KRX `"005930"`, NXT `"005930_NX"`
  - `def active_market(now: datetime) -> Market | None` — KST 평일 09:00≤t<15:30 → KRX; 08:00≤t<08:50 또는 15:30≤t<20:00 → NXT; 그 외·주말 → None. 공휴일 달력은 이 계획 범위 밖(None 대신 시간 기준 판정, 데이터가 없으면 Task 5의 상태가 정직하게 드러남).

- [ ] **Step 1: 실패하는 테스트 작성**

```python
@pytest.mark.parametrize("hm,expected", [("08:00", Market.NXT), ("08:50", None), ("09:00", Market.KRX),
                                         ("15:29", Market.KRX), ("15:30", Market.NXT), ("19:59", Market.NXT),
                                         ("20:00", None), ("07:59", None)])
def test_active_market_by_kst_clock(hm, expected): ...   # 2026-10-27(화) KST

def test_weekend_has_no_active_market(): ...              # 2026-10-31(토) 10:00 → None
def test_utc_input_is_converted_to_kst(): ...             # 2026-10-27T06:31Z == 15:31 KST → NXT
def test_realtime_item_codes():
    assert realtime_item("005930", Market.NXT) == "005930_NX" and realtime_item("005930", Market.KRX) == "005930"
```

- [ ] **Step 2: 실패 확인** → FAIL · **Step 3: 구현** · **Step 4: 통과 확인** → PASS
- [ ] **Step 5: 커밋** — `git commit -m "Decide the displayed market from the KST session clock"`

---

### Task 5: 키움 실시간 연결

**Files:**
- Create: `gateway/src/spatial_gateway/kiwoom/realtime.py`
- Test: `gateway/tests/test_realtime.py`

**Interfaces:**
- Consumes: `TokenProvider` (Task 1), `values.*` (Task 2), `Market`, `realtime_item` (Task 4)
- Produces:
  - `Trade` (frozen dataclass): `code: str`, `market: Market`, `price: Decimal`, `change: Decimal | None`, `change_percent: Decimal | None`, `volume: int | None`(체결량, WS `15`), `acc_volume: int | None`(WS `13`), `at: datetime`
  - `Book` (frozen dataclass): `code`, `market`, `asks: tuple[tuple[Decimal | None, int | None], ...]`(10개, 1호가부터), `bids`(동일), `at: datetime`
  - `def parse_real_frame(frame: dict, today: date) -> list[Trade | Book]` — 순수 함수. `item`의 `_NX` 접미사로 시장 판정. 가격(`10`) 없는 0B, 시각 파싱 실패, 알 수 없는 type은 버린다.
  - `class KiwoomRealtime(settings, tokens, on_event: Callable[[Trade | Book], None], on_state: Callable[[bool, str | None], None])`: `async def run(self) -> None`(취소될 때까지 연결 유지), `def register_message(codes: Sequence[str]) -> dict`(staticmethod)

- [ ] **Step 1: 실패하는 테스트 작성**

```python
def test_trade_frame_parses_absolute_price_and_signed_change():
    frame = {"trnm": "REAL", "data": [{"type": "0B", "item": "005930_NX",
             "values": {"10": "-78800", "11": "-600", "12": "-0.76", "13": "14947571", "15": "+120", "20": "153512"}}]}
    [t] = parse_real_frame(frame, date(2026, 10, 27))
    assert (t.code, t.market, t.price, t.change, t.volume) == ("005930", Market.NXT, Decimal("78800"), Decimal("-600"), 120)
    assert t.at == datetime(2026, 10, 27, 15, 35, 12, tzinfo=KST)

def test_book_frame_maps_ask_and_bid_blocks():
    # values: "41".."50" 매도호가1~10, "51".."60" 매수호가, "61".."70" 매도잔량, "71".."80" 매수잔량, "21" 호가시간
    # assert book.asks[0] == (Decimal(<41>), int(<61>)) and book.bids[0] == (Decimal(<51>), int(<71>))

@pytest.mark.parametrize("values", [{}, {"10": ""}, {"10": "-", "20": "153512"}, {"10": "78800", "20": "bad"}])
def test_malformed_trade_frames_are_dropped(values): ...       # 결과 []

def test_unknown_type_and_non_real_frames_are_ignored(): ...   # type "0Z" → [], trnm "PING" → []

def test_register_message_subscribes_krx_and_nxt_items():
    assert KiwoomRealtime.register_message(["005930"]) == {"trnm": "REG", "grp_no": "1", "refresh": "1",
        "data": [{"item": ["005930", "005930_NX"], "type": ["0B", "0D"]}]}

async def test_session_logs_in_echoes_ping_and_reports_disconnect(fake_ws):
    # fake_ws 스크립트: 클라이언트 첫 송신은 {"trnm":"LOGIN","token":<token>}
    # 서버 → {"trnm":"LOGIN","return_code":0}, {"trnm":"SYSTEM",...}, {"trnm":"REG","return_code":0},
    #        {"trnm":"PING"} (클라이언트가 같은 JSON을 되돌려 보내야 함), REAL 1건, 연결 종료
    # 기대: on_state(True, None) 후 on_event 1회, 종료 시 on_state(False, "KIWOOM_WS_CLOSED")

async def test_login_8005_invalidates_token_and_reconnects_with_backoff(fake_ws): ...
    # LOGIN return_code 8005 → tokens.invalidate() 호출, on_state(False, "KIWOOM_AUTH"), 다음 시도까지 sleep ≥ 1s
```

`fake_ws`는 `websockets` 연결과 같은 `send/recv/close` 비동기 인터페이스를 가진 테스트 더블. `KiwoomRealtime`은 연결 팩토리(`connect: Callable[..., AsyncContextManager]`)를 생성자 키워드 인자로 받아 주입 가능해야 한다.

- [ ] **Step 2: 실패 확인** → FAIL
- [ ] **Step 3: 구현** — 프로토콜은 kiwoom-cli `streaming.py`를 따른다: 연결 시 `ping_interval=None`, 첫 송신 LOGIN, ack는 위치가 아니라 `return_code`가 있는 프레임으로 판정(PING은 에코, SYSTEM은 건너뜀, ack 전 REAL은 버퍼), REG 송신. 재연결 백오프 1, 2, 4, 8, 15초(최대 15초), 성공 시 초기화.
- [ ] **Step 4: 통과 확인** → PASS
- [ ] **Step 5: 실연결 확인 (네트워크, 장중 또는 NXT 시간)** — `KiwoomRealtime`을 30초 돌려 이벤트 수를 출력하는 일회성 명령. Expected: Trade ≥ 1, Book ≥ 1 (장외면 0이어도 LOGIN/REG 성공 로그로 대체하고 기록).
- [ ] **Step 6: 커밋** — `git commit -m "Stream KRX and NXT trades and order book from Kiwoom WebSocket"`

---

### Task 6: 시세 허브

**Files:**
- Create: `gateway/src/spatial_gateway/market/hub.py`
- Modify: `gateway/src/spatial_gateway/capabilities.py` (`SpecificationGatedMarket` 삭제, `HubMarket` 추가)
- Test: `gateway/tests/test_hub.py`

**Interfaces:**
- Consumes: `Trade`, `Book` (Task 5), `MinuteBar` (Task 3), `Market`, `active_market` (Task 4), 기존 `models.Observation/Quote/CandleSeries/Candle/OrderBook/BookLevel/DataState/Origin`
- Produces:
  - `class MarketHub(clock: Callable[[], datetime], max_bars: int = 240)`:
    - `def seed_candles(self, code: str, market: Market, bars: Sequence[MinuteBar]) -> None`
    - `def apply(self, event: Trade | Book) -> None`
    - `def set_connection(self, connected: bool, reason: str | None) -> None`
    - `def observations(self, code: str, correlation_id: str) -> HubSnapshot`
    - `def subscribe(self) -> asyncio.Queue[str]` / `def unsubscribe(self, queue) -> None` — 갱신된 종목 코드를 넣는다(큐 maxsize 64, 가득 차면 가장 오래된 항목 버림)
  - `HubSnapshot` (frozen dataclass): `market: Market | None`, `quote: Observation[Quote]`, `candles: Observation[CandleSeries]`, `orderbook: Observation[OrderBook]`
  - `class HubMarket(hub: MarketHub)` — 기존 `MarketCapabilities` 프로토콜 구현

- [ ] **Step 1: 실패하는 테스트 작성**

```python
def test_trade_updates_live_quote_with_source_time():
    # active_market == KRX인 clock, Trade(KRX, 78800, change -600, at 10:01:05)
    snap = hub.observations("005930", cid)
    assert snap.market == Market.KRX and snap.quote.state == DataState.LIVE
    assert snap.quote.value.last_price == Decimal("78800") and snap.quote.source_at == trade.at

def test_trades_extend_current_minute_bar_and_open_next():
    # seed 10:00 봉(OHLC 100), Trade 10:00:30 가격 105 → 10:00 봉 high 105 close 105
    # Trade 10:01:02 가격 99 → 새 10:01 봉 OHLC 99, period_key "202610271001"
    # 직전 봉 complete True, 마지막 봉 complete False

def test_krx_and_nxt_bars_never_mix():
    # KRX Trade 15:29:59 100, NXT Trade 15:30:01 200 → clock 15:30:05에서 표시 시장 NXT,
    # 캔들 시리즈의 마지막 봉 close 200이며 KRX 봉 값 100이 NXT 시리즈에 없음

def test_disconnect_marks_values_disconnected_but_keeps_them():
    # Trade 후 set_connection(False, "KIWOOM_WS_CLOSED") → quote.state DISCONNECTED, reason 동일, value 유지

def test_no_active_market_reports_unavailable_market_closed():
    # clock 토요일 → 세 Observation 모두 UNAVAILABLE, reason "MARKET_CLOSED", value None

def test_untracked_code_is_unavailable_not_zero(): ...   # "000000" → UNAVAILABLE, reason "NOT_SUBSCRIBED"

async def test_subscribers_receive_changed_codes(): ...  # apply 후 큐에서 "005930"
```

- [ ] **Step 2: 실패 확인** → FAIL
- [ ] **Step 3: 구현** — 상태 키는 `(code, market)`. 표시 시장은 `active_market(clock())`; 그 시장 데이터가 아직 없으면 해당 Observation은 `LOADING`. `CandleSeries.interval="1m"`, `adjustment="unadjusted"`, `period_key` 형식 `YYYYMMDDHHMM`. `Quote.volume`은 누적거래량(`acc_volume`). `Origin.KIWOOM`, `received_at`은 `apply` 시점의 clock. 기존 `test_contracts.py`의 `BLOCKED_BY_MISSING_SPEC` 테스트는 `HubMarket` 기준(구독 안 한 종목 → `UNAVAILABLE/NOT_SUBSCRIBED`, 키 값 미노출)으로 바꾼다.
- [ ] **Step 4: 통과 확인** — Run: `uv run --project gateway pytest -q gateway/tests` → 전체 PASS
- [ ] **Step 5: 커밋** — `git commit -m "Keep live per-market quote, book and minute bars in a market hub"`

---

### Task 7: 클라이언트 스트림과 앱 배선

**Files:**
- Create: `gateway/src/spatial_gateway/stream.py`
- Modify: `gateway/src/spatial_gateway/app.py`, `gateway/README.md`
- Test: `gateway/tests/test_stream.py`

**Interfaces:**
- Consumes: `Settings` (Task 1), `MarketHub`, `HubMarket` (Task 6), `KiwoomRest` (Task 3), `KiwoomRealtime` (Task 5)
- Produces (wire, Unity Task 8이 디코딩):
  - 클라이언트 → 게이트웨이 첫 메시지: `{"type":"subscribe","token":"<pairing>","codes":["005930"],"correlation_id":"<uuid>"}`
  - 게이트웨이 → 클라이언트:
    - `{"type":"observation","capability":"quote"|"candles"|"orderbook","market":"KRX"|"NXT"|null,"observation":{<기존 REST 봉투와 동일>}}` — `observation.correlation_id`는 구독 메시지의 값
    - `{"type":"heartbeat","broker_connected":true|false,"reason":null|"<code>","server_time":"<ISO8601 +09:00>"}` 5초마다
  - 잘못된 토큰·형식 → close code `4401`, 그 전 데이터 0건
  - `def create_app(settings: Settings | None = None, *, hub: MarketHub | None = None, start_broker: bool = True) -> FastAPI`
  - REST 라우트는 헤더 `X-Pairing-Token` 필수, 불일치 시 401 `{"error":"UNAUTHORIZED"}`

- [ ] **Step 1: 실패하는 테스트 작성** (`TestClient.websocket_connect`, `start_broker=False`, 허브에 이벤트 직접 주입)

```python
def test_subscribe_receives_snapshot_then_updates():
    # 구독 직후 quote/candles/orderbook 3개 observation 메시지, 각 observation.correlation_id == 구독 값
    # hub.apply(Trade) 후 quote 메시지 1개 이상 추가 수신, last_price "78800"

def test_wrong_pairing_token_closes_4401_without_data(): ...
def test_heartbeat_reports_broker_state(): ...      # hub.set_connection(False,"KIWOOM_AUTH") → heartbeat broker_connected false, reason "KIWOOM_AUTH"
def test_rest_requires_pairing_token(): ...         # 헤더 없음 → 401, 올바른 헤더 → 200
def test_no_secret_or_kiwoom_token_in_any_payload():
    # settings.appkey/secretkey 및 가짜 키움 토큰 문자열이 수신 메시지·REST 응답·/health 어디에도 없음
def test_order_routes_still_absent(): ...           # POST /v1/orders, /v1/confirm_order → 404
```

- [ ] **Step 2: 실패 확인** → FAIL
- [ ] **Step 3: 구현** — 같은 종목의 연속 갱신은 종목·capability별로 100ms 단위로 합쳐 최신 것만 보낸다(초당 최대 10회). `candles`는 변경 시 최대 초당 2회. lifespan: `start_broker`면 `httpx.AsyncClient` 생성 → 감시 종목·시장(KRX, NXT)별 `minute_candles`로 `seed_candles`(실패해도 기동은 계속, 해당 시리즈 LOADING 유지) → `KiwoomRealtime.run()` 태스크 시작, 종료 시 취소. `TrustedHostMiddleware`의 허용 목록은 `settings.allowed_hosts + ["testserver"]`. `/health`는 페어링 없이 열어 두되 `broker_connected`, `orders_enabled: False`만 포함하고 비밀값은 없다. `test_contracts.py`의 REST 호출은 `X-Pairing-Token` 헤더를 보내도록 고친다. README의 "does not call the broker" 문구와 실행 명령을 갱신: `uv run --project gateway --env-file gateway/.env uvicorn spatial_gateway.app:app --app-dir gateway/src --host 0.0.0.0 --port 8471 --no-access-log` (LAN 시연 시 `GATEWAY_ALLOWED_HOSTS`에 Mac의 LAN IP 추가).
- [ ] **Step 4: 통과 확인** — `uv run --project gateway pytest -q gateway/tests` → 전체 PASS
- [ ] **Step 5: 실행 확인 (네트워크)** — 게이트웨이를 띄우고 `uv run --project gateway python -c` 로 `websockets.connect("ws://127.0.0.1:8471/v1/stream")`에 구독해 10초간 받은 메시지 type별 개수를 출력. Expected: observation ≥ 3, heartbeat ≥ 1, 장중이면 quote 갱신 다수.
- [ ] **Step 6: 커밋** — `git commit -m "Push live observations to paired clients over a gateway stream"`

---

### Task 8: Unity 스트림 계약

**Files:**
- Create: `quest/Assets/SpatialTrading/Runtime/Market/GatewayStreamContract.cs`
- Create: `quest/Assets/SpatialTrading/Tests/EditMode/GatewayStreamContractTests.cs`
- Modify: `tests/Domain/Domain.Tests.csproj` (`GatewayStreamContract.cs` Compile 항목 추가, 기존 GatewayContract 항목과 같은 형식)

**Interfaces:**
- Consumes: 기존 `GatewayContract.Quote/Candles/Book(string json, string code, Guid correlation)`
- Produces:
  - `public enum StreamCapability { Quote, Candles, OrderBook }`
  - `public sealed class StreamUpdate` : `StreamCapability Capability`, `string Code`, `string Market`(null 가능), `Observation<QuoteSnapshot> Quote`, `Observation<CandleSeries> Candles`, `Observation<OrderBookSnapshot> Book` (해당 capability만 non-null)
  - `public sealed class StreamHeartbeat` : `bool BrokerConnected`, `string Reason`, `DateTimeOffset ServerTime`
  - `public static class GatewayStreamContract`:
    - `public static object Parse(string json, Guid subscription)` — `StreamUpdate` 또는 `StreamHeartbeat` 반환, 형식 오류는 `FormatException`
    - `public static string Subscribe(string token, IEnumerable<string> codes, Guid subscription)`

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
[Test] public void ObservationMessageReusesGatewayEnvelopeDecoding()
// {"type":"observation","capability":"quote","market":"NXT","observation":{...LIVE 봉투, last_price "78800"...}}
// → StreamUpdate.Capability == Quote, Market == "NXT", Quote.State == Live, Quote.Value.LastPrice == 78800m

[Test] public void HeartbeatParsesBrokerState()
// broker_connected false, reason "KIWOOM_AUTH" → BrokerConnected false, Reason "KIWOOM_AUTH"

[Test] public void UnknownTypeExtraFieldOrMismatchedCorrelationIsRejected()
// type "order" → FormatException; 최상위 추가 필드 → FormatException;
// observation.correlation_id가 구독 Guid와 다르면 FormatException

[Test] public void MarketMustBeKnownExchangeOrNull()   // "NYSE" → FormatException

[Test] public void SubscribeMessageHasExactShape()
// JObject.Parse(Subscribe("tok", new[]{"005930"}, g)) 키 집합 == {type, token, codes, correlation_id}
```

- [ ] **Step 2: 실패 확인** — Run: `scripts/test_domain.sh` → FAIL (컴파일 오류: `GatewayStreamContract` 없음)
- [ ] **Step 3: 구현** — 최상위는 기존 `GatewayContract`와 같은 엄격성(중복 키 거부, 정확한 필드 집합). 내부 `observation`은 `ToString(Formatting.None)` 후 기존 디코더로 전달, 종목 코드는 `observation.instrument_code`.
- [ ] **Step 4: 통과 확인** — `scripts/test_domain.sh` → 기존 59 + 신규 전부 PASS
- [ ] **Step 5: 커밋** — `git commit -m "Decode gateway stream messages with the existing strict envelope"`

---

### Task 9: Unity 스트림 연결과 표시

**Files:**
- Create: `quest/Assets/SpatialTrading/Runtime/Market/GatewayStream.cs`
- Modify: `quest/Assets/SpatialTrading/Runtime/Market/MarketDataSession.cs`, `quest/Assets/SpatialTrading/Runtime/Market/GatewayStreamContract.cs`(`AllowedStreamAddress` 추가), `quest/Assets/SpatialTrading/Tests/EditMode/GatewayStreamContractTests.cs`, `quest/Assets/SpatialTrading/Editor/QuestShellProject.cs`(게이트웨이 주소·페어링 토큰 설정 메뉴), `docs/QUEST_SETUP.md`

**Interfaces:**
- Consumes: `GatewayStreamContract` (Task 8), 게이트웨이 wire (Task 7)
- Produces:
  - `GatewayStream : MonoBehaviour` — `public event Action<StreamUpdate> Updated; public event Action<StreamHeartbeat> Heartbeat; public event Action<string> Disconnected; public void Connect(Uri url, string token, IReadOnlyList<string> codes)`
  - `MarketDataSession`: `public string MarketLabel(string code)`(KRX/NXT/null), 스트림 모드에서 `Select`는 HTTP 요청 대신 현재 캐시를 쓰고 `GatewayStream`에 구독 종목을 맞춘다.
  - `public static bool AllowedGateway(string address)` 규칙 변경: `wss`는 허용, `ws`는 루프백 또는 사설 IPv4(10/8, 172.16/12, 192.168/16)만 허용, 쿼리·사용자정보·프래그먼트 거부. 에디터·기기 동일 규칙.

- [ ] **Step 1: 실패하는 테스트 작성** (`Tests/EditMode/GatewayStreamContractTests.cs`에 추가)

```csharp
[TestCase("ws://192.168.0.12:8471/v1/stream", true)]
[TestCase("ws://10.0.0.5:8471/v1/stream", true)]
[TestCase("ws://172.20.1.1:8471/v1/stream", true)]
[TestCase("ws://8.8.8.8:8471/v1/stream", false)]
[TestCase("ws://gateway.example.com/v1/stream", false)]
[TestCase("wss://gateway.example.com/v1/stream", true)]
[TestCase("ws://127.0.0.1:8471/v1/stream?x=1", false)]
public void GatewayAddressPolicy(string address, bool allowed) { ... }
```

`MarketDataSession`이 Unity 런타임 어셈블리라 .NET 하네스에 없으므로, 판정 로직을 `Runtime/Market/GatewayStreamContract.cs`의 `public static bool AllowedStreamAddress(string)`로 두고 `MarketDataSession.AllowedGateway`가 그것을 호출하게 한다(그러면 `scripts/test_domain.sh`로도 실행됨).

- [ ] **Step 2: 실패 확인** — `scripts/test_domain.sh` → FAIL
- [ ] **Step 3: 구현**
  - `GatewayStream`: 백그라운드 `Task`에서 `ClientWebSocket` 수신(메시지 1개 최대 2 MiB, 초과 시 연결 종료), 문자열을 `ConcurrentQueue<string>`에 넣고 `Update()`에서 꺼내 `GatewayStreamContract.Parse`. 파싱 실패는 그 메시지만 버리고 경고 로그(내용 미출력). 끊기면 `Disconnected` 후 1, 2, 4, 8초(최대 8초) 재연결. `OnDestroy`에서 취소·정리.
  - `MarketDataSession`: 스트림 업데이트를 캐시에 반영하고 `Changed` 발생. 노화 규칙 변경 — 마지막 하트비트가 15초 넘게 없으면 LIVE 값을 STALE(`GATEWAY_HEARTBEAT_LOST`)로, 하트비트 `broker_connected=false`면 DISCONNECTED(하트비트 reason)로. 틱 시각으로는 STALE 처리하지 않는다. Preview 모드 동작은 그대로.
  - 기존 화면(`FocusShell`/컴포넌트)에서 시장 라벨을 출처 라벨 옆에 표시(`KRX`/`NXT`).
  - 메뉴: 게이트웨이 URL과 페어링 토큰을 `EditorPrefs`/로컬 설정 에셋에 저장하되 토큰은 빌드에 포함하지 않는다 — 기기에서는 첫 실행 시 입력(이 계획에서는 개발용으로 `Application.persistentDataPath`의 `gateway.json`을 adb로 푸시하는 방법을 `docs/QUEST_SETUP.md`에 기록). 기존 `BuildCredentialGuard`가 토큰 문자열을 APK에서 검출하도록 패턴 추가.
- [ ] **Step 4: 통과 확인** — `scripts/test_domain.sh` → PASS, Unity EditMode 전체 테스트(기존 `scripts/unity.sh` 테스트 명령) → PASS
- [ ] **Step 5: 에디터 실시세 확인** — 게이트웨이 실행 후 Unity 플레이 모드(스트림 모드)에서 삼성전자 Focus 화면에 LIVE, 시장 라벨, 움직이는 호가가 보이는지 확인하고 시뮬레이터 캡처를 `docs/images/live-market-<date>.png`로 저장. 게이트웨이를 끄면 8초 안에 DISCONNECTED, 다시 켜면 자동 복구되는지 확인.
- [ ] **Step 6: 실기기 확인 (헤드셋 도착 후)** — 같은 핫스팟/LAN에서 `GATEWAY_ALLOWED_HOSTS`에 Mac IP 추가, APK 사이드로딩, `gateway.json` 푸시, Quest에서 LIVE 표시 확인. 결과(성공/실패와 로그)를 `docs/MILESTONE_2.md`에 기록.
- [ ] **Step 7: 커밋** — `git commit -m "Show live Kiwoom market data in the headset over the gateway stream"`

---

### Task 10: 문서 정리

**Files:**
- Modify: `README.md`, `docs/ROADMAP.md`, `docs/KIWOOM_API_MAPPING.md`, `docs/MILESTONE_2.md`

- [ ] **Step 1:** 각 문서 상단의 "저장소 스펙만 / BLOCKED_BY_MISSING_SPEC / no broker call" 서술을, 2026-10-27 사용자 결정(실전 도메인, kiwoom-cli 문서·구현을 근거로 허용)과 이 계획으로 검증된 범위(토큰, ka10080, 0B/0D, KRX/NXT)로 교체한다. K-01–K-10 중 이번에 해소된 항목에 근거(테스트 파일·녹화 픽스처·실연결 확인 날짜)를 적고, 남은 항목(주문 관련)은 후속 계획으로 넘긴다고 적는다. 설계 문서 링크 추가.
- [ ] **Step 2:** `git grep -n "BLOCKED_BY_MISSING_SPEC" -- README.md docs gateway quest/Assets/SpatialTrading` 결과가 이력 서술(과거형) 외에 남지 않았는지 확인.
- [ ] **Step 3: 커밋** — `git commit -m "Record live Kiwoom market integration and retire the spec gate"`
