using System.Globalization;
using System.Text;
using SpatialTrading.Domain;
using TMPro;
using UnityEngine;

namespace SpatialTrading.Components
{
    /// <summary>Independent capability presentation; does not consume Chart's values.</summary>
    public sealed class SecondaryComponent : MonoBehaviour
    {
        [SerializeField] private TMP_Text _title;
        [SerializeField] private TMP_Text _body;
        public void Configure(TMP_Text title, TMP_Text body)
        { _title = title; _body = body; }

        public void RenderData(ShellState state, Observation<OrderBookSnapshot> book,
            Observation<QuoteSnapshot> quote, Observation<QuoteSnapshot> other, string market = null)
        {
            gameObject.SetActive(state.SecondaryComponent.HasValue);
            if (!state.SecondaryComponent.HasValue) return;
            var instrument = state.SelectedInstrument;
            switch (state.SecondaryComponent.Value)
            {
                case ComponentKind.OrderBook:
                    _title.text = "호가";
                    if (book.Value == null)
                    { _body.text = Empty(instrument.DisplayName, book.State, book.Reason); return; }
                    if (book.Value.InstrumentCode != instrument.Code) return;
                    var text = new StringBuilder("<size=90%>" + instrument.DisplayName + "</size>\n");
                    text.Append("<size=65%><color=#9AAFC9>").Append(book.Origin == DataOrigin.Synthetic ? "합성 예시 · 상위 3호가" : StatusText.BookCaption(market, book.State, book.Reason)).Append("</color></size>\n\n");
                    text.Append("<size=72%><color=#FF8291>매도  /  가격 · 잔량</color></size>\n");
                    for (var i = System.Math.Min(3, book.Value.Asks.Count) - 1; i >= 0; i--) text.Append(Level(book.Value.Asks[i]));
                    text.Append("\n<size=72%><color=#74B5FF>매수  /  가격 · 잔량</color></size>\n");
                    for (var i = 0; i < System.Math.Min(3, book.Value.Bids.Count); i++) text.Append(Level(book.Value.Bids[i]));
                    _body.text = text.ToString();
                    break;
                case ComponentKind.Position:
                    _title.text = "내 포지션";
                    _body.text = instrument.DisplayName + "\n\n<size=90%>계좌 연결 전</size>\n\n" +
                        "<size=75%><color=#AAB9CF>보유 수량과 평가금액을\n확인할 수 없습니다.\n\n계좌 정보는 연결 후 표시됩니다.</color></size>";
                    break;
                case ComponentKind.Compare:
                    _title.text = "함께 보기";
                    var comparison = state.ComparisonInstrument;
                    _body.text = InstrumentQuote(instrument, quote) + "\n\n" +
                        (comparison == null || other == null ? "비교 종목을 선택하세요" : InstrumentQuote(comparison, other));
                    break;
            }
        }

        private static string InstrumentQuote(InstrumentRef instrument, Observation<QuoteSnapshot> quote) =>
            "<size=90%>" + instrument.DisplayName + "</size>\n<size=140%>" + ChartComponent.Format(quote.Value?.LastPrice) +
            "</size> <size=60%>KRW</size>\n<size=65%><color=#AAB9CF>" +
            (quote.Origin == DataOrigin.Synthetic ? "합성 예시" : StatusText.State(quote.State, quote.Reason)) + "</color></size>";

        private static string Empty(string name, DataState state, string reason)
        {
            var hint = StatusText.Hint(reason);
            return name + "\n\n<size=95%>" + StatusText.State(state, reason) + "</size>" +
                (hint.Length == 0 ? "" : "\n\n<size=75%><color=#AAB9CF>" + hint + "</color></size>");
        }

        private static string Level(BookLevel level) =>
            "<size=90%>" + ChartComponent.Format(level.Price) + "</size>   <size=70%><color=#AAB9CF>" +
            (level.Quantity.HasValue ? level.Quantity.Value.ToString("N0", CultureInfo.InvariantCulture) : "—") + "주</color></size>\n";
    }
}
