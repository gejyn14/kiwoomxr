"""News panel source: OpenDART filings plus securities RSS headlines."""

import asyncio
import html
import inspect
import logging
import re
from datetime import datetime, timedelta
from email.utils import parsedate_to_datetime
from urllib.parse import urlparse

import httpx
from defusedxml import ElementTree

from spatial_gateway.kiwoom.values import KST
from spatial_gateway.market.observe import observe
from spatial_gateway.models import NEWS, NewsFeed, NewsItem, Observation, Origin

log = logging.getLogger(__name__)

# Verified against DART corpCode.xml (stock_code -> corp_code).
CORP_CODES: dict[str, str] = {"039490": "00296290", "005930": "00126380", "000660": "00164779"}
# Insertion order is the order mentions are reported in.
MENTIONS: dict[str, tuple[str, ...]] = {
    "039490": ("키움증권", "키움"),
    "005930": ("삼성전자", "삼성"),
    "000660": ("SK하이닉스", "하이닉스"),
    "001": ("코스피", "KOSPI"),
}
# Free, Korean, unauthenticated; both returned 200 and valid RSS (maekyung blocks scripted clients).
DEFAULT_FEEDS: tuple[str, ...] = (
    "https://www.hankyung.com/feed/finance",
    "https://www.yna.co.kr/rss/economy.xml",
)
FEED_LABELS = {"www.hankyung.com": "한국경제", "www.yna.co.kr": "연합뉴스"}

DART_LIST_URL = "https://opendart.fss.or.kr/api/list.json"
RSS_PERIOD = timedelta(minutes=5)
DART_PERIOD = timedelta(minutes=10)
MAX_ITEMS = 40
USER_AGENT = "Mozilla/5.0 (compatible; kiwoomxr-gateway)"

_TAG = re.compile(r"<[^>]*>")
_SPACE = re.compile(r"\s+")
_BRACKET = re.compile(r"\[([^\]]+)\]")


def clean_title(raw: str) -> str:
    text = _TAG.sub(" ", html.unescape(raw or ""))
    return _SPACE.sub(" ", text).strip()[:200]


def mentions_of(title: str) -> tuple[str, ...]:
    return tuple(code for code, names in MENTIONS.items() if any(name in title for name in names))


def parse_rss(xml: bytes, source: str) -> list[NewsItem]:
    root = ElementTree.fromstring(xml)
    items = []
    for node in root.iter("item"):
        try:
            title = clean_title(node.findtext("title") or "")
            published = parsedate_to_datetime((node.findtext("pubDate") or "").strip())
            if published.tzinfo is None:
                published = published.replace(tzinfo=KST)
            items.append(NewsItem(kind="NEWS", source=source[:40], title=title,
                                  published_at=published.astimezone(KST), mentions=mentions_of(title)))
        except (ValueError, TypeError):
            continue
    return items


def parse_dart(data: dict, code: str) -> list[NewsItem]:
    items = []
    for row in data.get("list") or []:
        try:
            name = row["report_nm"]
            published = datetime.strptime(row["rcept_dt"], "%Y%m%d").replace(tzinfo=KST)
            tag = _BRACKET.search(name)
            items.append(NewsItem(kind="FILING", source="DART", title=clean_title(name), published_at=published,
                                  mentions=(code,), filing_type=tag.group(1) if tag else None))
        except (KeyError, ValueError, TypeError):
            continue
    return items


def merge_items(groups) -> list[NewsItem]:
    """Newest first, one item per title, at most MAX_ITEMS."""
    everything = sorted((i for group in groups for i in group), key=lambda i: i.published_at, reverse=True)
    seen, merged = set(), []
    for entry in everything:
        if entry.title in seen:
            continue
        seen.add(entry.title)
        merged.append(entry)
    return merged[:MAX_ITEMS]


class NewsService:
    def __init__(self, client: httpx.AsyncClient, settings, clock, notify, sleep=asyncio.sleep):
        self.client, self.settings, self.clock, self.notify, self.sleep = client, settings, clock, notify, sleep
        self.feeds = tuple(settings.news_feeds) or DEFAULT_FEEDS
        self.items: dict[str, list[NewsItem]] = {}
        self.ok: dict[str, bool] = {}
        self.last_success: datetime | None = None

    def _record(self, key: str, items: list[NewsItem] | None) -> None:
        self.ok[key] = items is not None
        if items is not None:
            self.items[key] = items
            self.last_success = self.clock()

    async def _fetch_rss(self, url: str) -> None:
        try:
            response = await self.client.get(url, headers={"User-Agent": USER_AGENT}, timeout=15)
            response.raise_for_status()
            label = FEED_LABELS.get(urlparse(url).hostname or "", (urlparse(url).hostname or "RSS"))
            self._record(url, parse_rss(response.content, label))
        except Exception as exc:  # never log the exception message or URL
            log.warning("news rss fetch failed: %s", type(exc).__name__)
            self._record(url, None)

    async def _fetch_dart(self, code: str, corp: str) -> None:
        key = f"dart:{code}"
        params = {"crtfc_key": self.settings.dart_api_key, "corp_code": corp,
                  "bgn_de": (self.clock().astimezone(KST) - timedelta(days=7)).strftime("%Y%m%d"),
                  "page_count": 10}
        try:
            response = await self.client.get(DART_LIST_URL, params=params, timeout=15)
            response.raise_for_status()
            data = response.json()
            if data.get("status") not in ("000", "013"):  # 013: no filings in the window
                raise ValueError("DART status")
            self._record(key, parse_dart(data, code))
        except Exception as exc:  # the URL carries the key: type name only
            log.warning("news dart fetch failed: %s", type(exc).__name__)
            self._record(key, None)

    async def poll(self, *, rss: bool = True, dart: bool = True) -> None:
        jobs = []
        if rss:
            jobs += [self._fetch_rss(url) for url in self.feeds]
        if dart and self.settings.dart_api_key:
            jobs += [self._fetch_dart(code, corp) for code, corp in CORP_CODES.items()]
        if not jobs:
            return
        await asyncio.gather(*jobs)
        result = self.notify()
        if inspect.isawaitable(result):
            await result

    async def run(self) -> None:
        next_rss = next_dart = self.clock()
        while True:
            now = self.clock()
            do_rss, do_dart = now >= next_rss, now >= next_dart
            await self.poll(rss=do_rss, dart=do_dart)
            if do_rss:
                next_rss = now + RSS_PERIOD
            if do_dart:
                next_dart = now + DART_PERIOD
            await self.sleep(max(1.0, (min(next_rss, next_dart) - self.clock()).total_seconds()))

    def observation(self, cid: str) -> Observation[NewsFeed]:
        merged = merge_items(self.items.values())
        value = NewsFeed(items=tuple(merged)) if self.last_success else None
        connected = value is None or any(self.ok.values())
        return observe(NewsFeed, NEWS, cid, value=value, received=self.last_success, source=self.last_success,
                       connected=connected, reason=None if connected else "NEWS_FETCH_FAILED",
                       closed=False, watched=True, origin=Origin.NEWS)
