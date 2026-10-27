import json
import logging
from datetime import datetime, timedelta
from pathlib import Path
from types import SimpleNamespace

import httpx
import pytest

from spatial_gateway import news
from spatial_gateway.kiwoom.values import KST
from spatial_gateway.models import DataState, NewsFeed, NewsItem, Origin

FIXTURES = Path(__file__).parent / "fixtures"
KEY = "SECRET-DART-KEY-123"
FEED_A, FEED_B = "https://a.example/rss.xml", "https://b.example/rss.xml"
NOW = datetime(2026, 10, 28, 10, 0, tzinfo=KST)


def settings(**kw):
    base = dict(dart_api_key=KEY, news_feeds=(FEED_A, FEED_B))
    base.update(kw)
    return SimpleNamespace(**base)  # the service reads only dart_api_key and news_feeds


def rss_xml(titles, start=NOW):
    items = "".join(
        f"<item><title>{t}</title><pubDate>{(start - timedelta(minutes=i)).strftime('%a, %d %b %Y %H:%M:%S +0900')}"
        f"</pubDate></item>" for i, t in enumerate(titles))
    return f"<rss><channel><title>x</title>{items}</channel></rss>".encode()


DART_OK = json.loads((FIXTURES / "dart_list_005930.json").read_text(encoding="utf-8"))


class Net:
    """Routes MockTransport requests; records calls."""

    def __init__(self, a=None, b=None, dart=None):
        self.a, self.b, self.dart = a, b, dart
        self.dart_calls = 0

    def handler(self, request):
        host = request.url.host
        if host == "opendart.fss.or.kr":
            self.dart_calls += 1
            if isinstance(self.dart, Exception):
                raise self.dart
            return httpx.Response(200, json=self.dart)
        body = self.a if host == "a.example" else self.b
        if body is None:
            return httpx.Response(500)
        return httpx.Response(200, content=body)


def service(net, **kw):
    client = httpx.AsyncClient(transport=httpx.MockTransport(net.handler))
    notified = []
    svc = news.NewsService(client, settings(**kw), clock=lambda: NOW, notify=lambda: notified.append(1))
    svc.notified = notified
    return svc


@pytest.mark.parametrize("name", ["rss_hankyung.xml", "rss_yonhap_economy.xml"])
def test_rss_fixture_parses_kst_times_and_clean_titles(name):
    items = news.parse_rss((FIXTURES / name).read_bytes(), "src")
    assert items
    for item in items:
        assert item.kind == "NEWS"
        assert item.published_at.utcoffset() == timedelta(hours=9)
        assert not any(bad in item.title for bad in ("<", ">", "&amp;"))


def test_dart_fixture_parses_filings_with_amber_kind():
    items = news.parse_dart(DART_OK, "005930")
    assert items
    assert all(i.kind == "FILING" and i.source == "DART" and i.mentions == ("005930",) for i in items)
    assert items[0].published_at.hour == 0 and items[0].published_at.utcoffset() == timedelta(hours=9)


def test_dart_filing_type_is_first_bracket_tag():
    data = {"status": "000", "list": [
        {"report_nm": "[기재정정]사업보고서", "rcept_dt": "20261026"},
        {"report_nm": "분기보고서", "rcept_dt": "20261026"}]}
    first, second = news.parse_dart(data, "005930")
    assert first.filing_type == "기재정정" and second.filing_type is None


def test_clean_title_strips_tags_entities_and_caps():
    assert news.clean_title("  <b>A &amp; B</b>\n  c ") == "A & B c"
    assert len(news.clean_title("x" * 500)) == 200


def test_mentions_detect_stock_names():
    assert news.mentions_of("SK하이닉스 HBM 증설… 삼성전자도") == ("005930", "000660")
    assert news.mentions_of("무관한 제목") == ()


def test_corp_codes_verified():
    assert news.CORP_CODES == {"039490": "00296290", "005930": "00126380", "000660": "00164779"}


async def test_one_failing_feed_does_not_drop_others():
    svc = service(Net(a=None, b=rss_xml(["B1", "B2"])), dart_api_key=None)
    await svc.poll()
    obs = svc.observation("c1")
    assert obs.state == DataState.LIVE and obs.origin == Origin.NEWS
    assert [i.title for i in obs.value.items] == ["B1", "B2"]
    assert svc.notified


async def test_missing_dart_key_skips_dart_and_keeps_rss_live():
    net = Net(a=rss_xml(["A1"]), b=rss_xml(["B1"]))
    svc = service(net, dart_api_key=None)
    await svc.poll()
    assert net.dart_calls == 0
    assert svc.observation("c").state == DataState.LIVE


async def test_dart_is_requested_per_corp_with_key_and_window():
    seen = []

    def handler(request):
        if request.url.host == "opendart.fss.or.kr":
            seen.append(dict(request.url.params))
            return httpx.Response(200, json=DART_OK)
        return httpx.Response(200, content=rss_xml(["A1"]))

    client = httpx.AsyncClient(transport=httpx.MockTransport(handler))
    svc = news.NewsService(client, settings(), clock=lambda: NOW, notify=lambda: None)
    await svc.poll()
    assert {p["corp_code"] for p in seen} == set(news.CORP_CODES.values())
    assert all(p["crtfc_key"] == KEY and p["bgn_de"] == "20261021" and p["page_count"] == "10" for p in seen)
    kinds = {i.kind for i in svc.observation("c").value.items}
    assert kinds == {"NEWS", "FILING"}


async def test_empty_news_feeds_uses_defaults():
    hosts = []

    def handler(request):
        hosts.append(str(request.url))
        return httpx.Response(500)

    client = httpx.AsyncClient(transport=httpx.MockTransport(handler))
    svc = news.NewsService(client, settings(news_feeds=(), dart_api_key=None), clock=lambda: NOW,
                           notify=lambda: None)
    await svc.poll()
    assert sorted(hosts) == sorted(news.DEFAULT_FEEDS)


async def test_all_sources_failing_keep_last_feed_as_disconnected():
    net = Net(a=rss_xml(["A1"]), b=rss_xml(["B1"]))
    svc = service(net, dart_api_key=None)
    await svc.poll()
    net.a = net.b = None
    await svc.poll()
    obs = svc.observation("c")
    assert obs.state == DataState.DISCONNECTED and obs.reason == "NEWS_FETCH_FAILED"
    assert {i.title for i in obs.value.items} == {"A1", "B1"}


def test_before_any_success_is_loading():
    assert service(Net()).observation("c").state == DataState.LOADING


async def test_dart_key_never_logged(caplog):
    net = Net(a=rss_xml(["A1"]), dart=httpx.ConnectError("boom https://opendart.fss.or.kr?crtfc_key=" + KEY))
    svc = service(net)
    with caplog.at_level(logging.DEBUG):
        await svc.poll()
    assert KEY not in caplog.text
    assert svc.observation("c").state == DataState.LIVE


async def test_run_polls_on_cadence_with_injected_sleep():
    net = Net(a=rss_xml(["A1"]), b=rss_xml(["B1"]), dart=DART_OK)
    svc = service(net)
    sleeps = []

    async def sleep(seconds):
        sleeps.append(seconds)
        if len(sleeps) >= 2:
            raise RuntimeError("stop")

    svc.sleep = sleep
    with pytest.raises(RuntimeError):
        await svc.run()
    assert net.dart_calls == 3  # first round only; the clock never advances
    assert sleeps[0] == 300


def item(title, minutes):
    return NewsItem(kind="NEWS", source="s", title=title, published_at=NOW - timedelta(minutes=minutes),
                    mentions=())


def test_feed_is_deduplicated_newest_first_and_capped():
    merged = news.merge_items([[item("same", 5), item("same", 1)], [item(f"t{i}", i + 10) for i in range(60)]])
    assert len(merged) == 40
    assert [i.title for i in merged].count("same") == 1
    times = [i.published_at for i in merged]
    assert times == sorted(times, reverse=True)
    NewsFeed(items=tuple(merged))


@pytest.mark.parametrize("status", [200, 500])
async def test_dart_key_absent_from_httpx_logs(caplog, status):
    def handler(request):
        if request.url.host == "opendart.fss.or.kr":
            return httpx.Response(status, json=DART_OK)
        return httpx.Response(200, content=rss_xml(["A1"]))

    client = httpx.AsyncClient(transport=httpx.MockTransport(handler))
    svc = news.NewsService(client, settings(), clock=lambda: NOW, notify=lambda: None)
    with caplog.at_level(logging.DEBUG):
        await svc.poll()
    assert KEY not in caplog.text


def test_dart_filings_with_repeated_titles_all_survive():
    filings = news.parse_dart(DART_OK, "005930")
    assert len(news.merge_items([filings])) == len(filings)


def test_same_title_filings_of_different_companies_survive():
    a = news.parse_dart({"list": [{"report_nm": "사업보고서", "rcept_dt": "20261026"}]}, "005930")
    b = news.parse_dart({"list": [{"report_nm": "사업보고서", "rcept_dt": "20261026"}]}, "039490")
    assert len(news.merge_items([a, b])) == 2


def test_same_title_news_still_collapses():
    assert len(news.merge_items([[item("dup", 1)], [item("dup", 2)]])) == 1


async def test_every_feed_failing_at_first_boot_reads_disconnected_not_loading():
    svc = service(Net(), dart_api_key=None)  # both RSS feeds return 500
    await svc.poll()
    obs = svc.observation("c")
    assert obs.state == DataState.DISCONNECTED and obs.reason == "NEWS_FETCH_FAILED"
    assert obs.value is None


async def test_feed_reads_disconnected_once_the_last_success_is_older_than_three_rss_periods():
    clock = {"now": NOW}
    net = Net(a=rss_xml(["A1"]), b=rss_xml(["B1"]))
    client = httpx.AsyncClient(transport=httpx.MockTransport(net.handler))
    svc = news.NewsService(client, settings(dart_api_key=None), clock=lambda: clock["now"], notify=lambda: None)
    await svc.poll()
    clock["now"] = NOW + timedelta(minutes=15)
    assert svc.observation("c").state == DataState.LIVE
    clock["now"] = NOW + timedelta(minutes=15, seconds=1)
    obs = svc.observation("c")
    assert obs.state == DataState.DISCONNECTED and obs.reason == "NEWS_REFRESH_OVERDUE"
    assert {i.title for i in obs.value.items} == {"A1", "B1"}
