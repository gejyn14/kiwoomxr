using System.Globalization;
using SpatialTrading.Domain;
using TMPro;
using UnityEngine;

namespace SpatialTrading.Components
{
    /// <summary>Independent secondary surface. It consumes selection, never Chart's values.</summary>
    public sealed class SecondaryComponent : MonoBehaviour
    {
        [SerializeField] private TMP_Text _title;
        [SerializeField] private TMP_Text _body;
        public void Configure(TMP_Text title, TMP_Text body) { _title = title; _body = body; }

        public void Render(ShellState state)
        {
            gameObject.SetActive(state.SecondaryComponent.HasValue);
            if (!state.SecondaryComponent.HasValue) return;
            var instrument = state.SelectedInstrument;
            switch (state.SecondaryComponent.Value)
            {
                case ComponentKind.OrderBook:
                    _title.text = "호가 · 합성 예시";
                    var price = SyntheticCatalog.ReferencePrice(instrument);
                    _body.text = instrument.DisplayName + "\n\n" +
                        "<color=#FF8C80>매도</color>  3단계 예시\n" +
                        "<size=80%>" + Level(price + 300, 120) + Level(price + 200, 250) + Level(price + 100, 180) + "</size>\n" +
                        "<color=#6CE2BF>매수</color>  3단계 예시\n" +
                        "<size=80%>" + Level(price - 100, 140) + Level(price - 200, 220) + Level(price - 300, 190) + "</size>\n" +
                        "<size=65%>정적 UI 예시 · 시장 데이터 아님</size>";
                    break;
                case ComponentKind.Position:
                    _title.text = "보유 종목";
                    _body.text = instrument.DisplayName + "\n\n<size=85%>계좌 데이터 없음</size>\n\n" +
                        "<size=70%>UNAVAILABLE\n계좌가 연결되지 않았습니다.\n\n" +
                        "보유수량 · 잔고 · 주문가능금액\n아직 확인되지 않았습니다.</size>";
                    break;
                case ComponentKind.Compare:
                    _title.text = "종목 비교 · 합성 예시";
                    var other = state.ComparisonInstrument;
                    _body.text = instrument.DisplayName + "\n" + FormatPrice(instrument) + "\n\n" +
                        other.DisplayName + "\n" + FormatPrice(other) + "\n\n<size=65%>실시간 시세 아님\n독립 컴포넌트가 선택 상태를 공유합니다.</size>";
                    break;
            }
        }

        private static string FormatPrice(InstrumentRef instrument) =>
            SyntheticCatalog.ReferencePrice(instrument).ToString("N0", CultureInfo.InvariantCulture) + " KRW";

        // Arbitrary fixture spacing; this is not an exchange tick-size rule.
        private static string Level(decimal price, int quantity) =>
            price.ToString("N0", CultureInfo.InvariantCulture) + "   ·   " + quantity + "주\n";
    }
}
