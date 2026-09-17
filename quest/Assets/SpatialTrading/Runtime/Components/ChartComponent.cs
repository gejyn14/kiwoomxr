using System;
using System.Globalization;
using SpatialTrading.Domain;
using TMPro;
using UnityEngine;

namespace SpatialTrading.Components
{
    public sealed class ChartComponent : MonoBehaviour
    {
        [SerializeField] private TMP_Text _instrument;
        [SerializeField] private TMP_Text _price;
        [SerializeField] private TMP_Text _change;
        [SerializeField] private TMP_Text _source;
        [SerializeField] private TMP_Text _freshness;
        [SerializeField] private TMP_Text _period;
        [SerializeField] private TMP_Text _high;
        [SerializeField] private TMP_Text _low;
        [SerializeField] private CandleChartGraphic _candles;
        public void Configure(TMP_Text instrument, TMP_Text price, CandleChartGraphic candles,
            TMP_Text change, TMP_Text source, TMP_Text freshness, TMP_Text period, TMP_Text high, TMP_Text low)
        { _instrument = instrument; _price = price; _candles = candles; _change = change;
          _source = source; _freshness = freshness; _period = period; _high = high; _low = low; }

        public void RenderData(InstrumentRef instrument, Observation<QuoteSnapshot> quote, Observation<CandleSeries> candles)
        {
            if (quote.Value != null && quote.Value.InstrumentCode != instrument.Code ||
                candles.Value != null && candles.Value.InstrumentCode != instrument.Code) return;
            _instrument.text = instrument.DisplayName + "  <size=45%><color=#8C9BB6>" + instrument.Code + "</color></size>";
            _price.text = Format(quote.Value?.LastPrice) + " <size=28%><color=#AAB9CF>KRW</color></size>";
            var percent = quote.Value?.ChangePercent;
            _change.text = percent.HasValue ? percent.Value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + "%  ·  전일 대비" : "전일 대비 —";
            _change.color = percent > 0 ? new Color(1,.43f,.52f) : percent < 0 ? new Color(.30f,.63f,1) : new Color(.55f,.63f,.74f);
            _source.text = quote.Origin == DataOrigin.Synthetic ? "PREVIEW  /  합성 시세" : "KIWOOM  /  " + (quote.State == DataState.Delayed ? "SNAPSHOT" : quote.State.ToString().ToUpperInvariant());
            _freshness.text = quote.Reason != null && quote.Reason.StartsWith("BLOCKED_BY_MISSING_SPEC") ? "시세 연결 준비 중 · API 명세 확인 필요" : quote.Origin == DataOrigin.Synthetic ? "디자인 미리보기 · 실제 시세 아님" :
                StateName(quote.State) + (quote.ReceivedAt.HasValue ? "  ·  수신 " + quote.ReceivedAt.Value.ToOffset(TimeSpan.FromHours(9)).ToString("HH:mm:ss") : "  ·  수신 데이터 없음");
            _candles.SetSeries(candles.Value);
            var bars = candles.Value?.Candles;
            _period.text = bars == null || bars.Count == 0 ? StateName(candles.State) :
                candles.Origin == DataOrigin.Synthetic ? "40 BARS  ·  합성 차트" :
                StateName(candles.State) + "  ·  " + candles.Value.Interval + "  ·  최근 " + Math.Min(bars.Count, CandleChartGraphic.MaxVisible) + "개";
            decimal? high = null, low = null;
            if (bars != null) for (var i = Math.Max(0, bars.Count - CandleChartGraphic.MaxVisible); i < bars.Count; i++)
            { var bar = bars[i]; high = high.HasValue ? Math.Max(high.Value, bar.High) : bar.High; low = low.HasValue ? Math.Min(low.Value, bar.Low) : bar.Low; }
            _high.text = "최고\n" + Format(high); _low.text = "최저\n" + Format(low);
        }

        public static string Format(decimal? value) => value.HasValue ? value.Value.ToString("#,0.########", CultureInfo.InvariantCulture) : "—";
        public static string StateName(DataState state)
        {
            switch (state)
            {
                case DataState.Loading: return "불러오는 중";
                case DataState.Live: return "실시간";
                case DataState.Delayed: return "스냅샷 · 실시간 보장 없음";
                case DataState.Stale: return "오래된 데이터";
                case DataState.Disconnected: return "연결 끊김";
                case DataState.Error: return "데이터 오류";
                default: return "데이터 이용 불가";
            }
        }
    }
}
