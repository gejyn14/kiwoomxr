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
        [SerializeField] private SyntheticCandleGraphic _candles;
        public void Configure(TMP_Text instrument, TMP_Text price, SyntheticCandleGraphic candles)
        { _instrument = instrument; _price = price; _candles = candles; }

        public void Render(ShellState state)
        {
            var instrument = state.SelectedInstrument;
            _instrument.text = instrument.DisplayName + "  <size=60%>" + instrument.Code + "</size>";
            _price.text = SyntheticCatalog.ReferencePrice(instrument).ToString("N0", CultureInfo.InvariantCulture) +
                " <size=45%>KRW · 합성 예시</size>";
            _candles.SetInstrument(instrument.Equals(SyntheticCatalog.SkHynix));
        }
    }
}
