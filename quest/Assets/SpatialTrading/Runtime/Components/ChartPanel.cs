using System;
using SpatialTrading.Domain;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpatialTrading.Components
{
    /// <summary>
    /// The spread's one chart, always the selected instrument (chosen in 관심종목): header (name, price, signed change,
    /// honesty label), 4H/1D toggle and the candle plot whose detail follows the panel size. Draws the
    /// <see cref="ChartHeaderView"/> as given.
    /// </summary>
    public sealed class ChartPanel : HoloPanel
    {
        public const string FourHour = "4H";
        public const string Daily = "1D";
        private const float PriceSize = 28f;
        /// <summary>1D/4H toggles: 60 × 55 mm, about 2.9° at 1.10 m.</summary>
        public const float ToggleW = 60f;
        public const float ToggleH = 55f;
        private const float ToggleGap = 6f;
        /// <summary>Extra room between the toggles and the close button's reserve.</summary>
        private const float CloseGap = 4f;
        /// <summary>Narrower than this beside the toggles and the title moves under them.</summary>
        private const float MinTitleW = 80f;

        [SerializeField] private TextMeshProUGUI _title;
        [SerializeField] private TextMeshProUGUI _price;
        [SerializeField] private TextMeshProUGUI _change;
        [SerializeField] private TextMeshProUGUI _status;
        [SerializeField] private Button _fourHour;
        [SerializeField] private TextMeshProUGUI _fourHourLabel;
        [SerializeField] private Button _daily;
        [SerializeField] private TextMeshProUGUI _dailyLabel;
        [SerializeField] private CandleChartGraphic _plot;

        /// <summary>"4H" or "1D", raised when a toggle is pressed.</summary>
        public event Action<string> IntervalChanged;

        public CandleChartGraphic Plot { get { EnsureBuilt(); return _plot; } }

        protected override void Build()
        {
            _title = Holo.Text(transform, "Title", Font, TitleSize, HoloStyle.Ink);
            _price = Holo.Text(transform, "Price", Font, PriceSize, HoloStyle.Ink, TextAlignmentOptions.BaselineLeft);
            _change = Holo.Text(transform, "PriceChange", Font, HoloStyle.MinNumber, HoloStyle.Cyan, TextAlignmentOptions.BaselineLeft);
            _status = Holo.Text(transform, "Status", Font, HoloStyle.MinText, HoloStyle.Muted);
            _fourHour = Toggle(FourHour, out _fourHourLabel);
            _daily = Toggle(Daily, out _dailyLabel);
            _plot = Holo.Node(transform, "Plot").gameObject.AddComponent<CandleChartGraphic>();
            _plot.raycastTarget = false;
            _plot.Font = Font;
        }

        private Button Toggle(string interval, out TextMeshProUGUI label)
        {
            var b = Holo.Button(transform, "Interval " + interval);
            label = Holo.Text(b.transform, "IntervalLabel", Font, TitleSize, HoloStyle.Cyan, TextAlignmentOptions.Midline, true);
            Holo.Stretch(label);
            label.text = interval;
            return b;
        }

        protected override void Wire()
        {
            Holo.OnClick(_fourHour, () => IntervalChanged?.Invoke(FourHour));
            Holo.OnClick(_daily, () => IntervalChanged?.Invoke(Daily));
        }

        protected override void OnFont(TMP_FontAsset font) => _plot.Font = font;

        /// <param name="interval">The interval currently shown ("1D" or "4H"); lights its toggle.</param>
        public void Render(ChartHeaderView view, CandleSeries series, string interval, PanelRect rect)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            EnsureBuilt();
            var size = Size(rect);
            float w = size.x, h = size.y;

            // Header band: name [· price · change when compact and it fits] …… [4H][1D] [×]. The toggles line up with the
            // close button, or drop just under the centred ray-grab strip when they would reach into it (a ray must never
            // grab where it means to press); whatever does not fit beside them moves under the band.
            var togglesX = Mathf.Max(8f, w - CloseReserveMm - CloseGap - 2 * ToggleW - ToggleGap);
            var stripRight = (w + HoloStyle.GrabStripWidthMm(w)) / 2f;
            var togglesY = togglesX < stripRight ? HoloStyle.GrabStripDepthMm + 2f : CloseInset;
            Holo.Place(_fourHour, togglesX, togglesY, ToggleW, ToggleH);
            Holo.Place(_daily, togglesX + ToggleW + ToggleGap, togglesY, ToggleW, ToggleH);
            StyleToggle(_fourHour, _fourHourLabel, interval == FourHour);
            StyleToggle(_daily, _dailyLabel, interval == Daily);
            var bandW = togglesX - 8f - Pad; // free header width left of the toggles
            _title.text = view.Title;
            _price.text = view.PriceText;
            _change.text = view.ChangeText;
            var y = Mathf.Max(CloseBottom, togglesY + ToggleH) + 2f;
            if (rect.Height <= 4)
            {
                // Compact: one row of name, price and change (the name gives way first), in the band when the price and
                // change fit there whole, otherwise under it at full width; then the honesty label.
                _price.fontSize = HoloStyle.MinNumber;
                var rowH = Holo.LineHeight(HoloStyle.MinNumber); // fits the 22 mm price line box
                var priceFull = Holo.Width(_price, view.PriceText) + 4f;
                var changeFull = Holo.Width(_change, view.ChangeText) + 4f;
                var inBand = priceFull + 8f + changeFull <= bandW;
                var rowW = inBand ? bandW : w - 2 * Pad;
                var rowY = inBand ? Top : y;
                var priceW = Mathf.Min(priceFull, rowW);
                var changeW = Mathf.Min(changeFull, Mathf.Max(0f, rowW - priceW - 8f));
                var titleW = Mathf.Min(Holo.Width(_title, view.Title) + 4f, Mathf.Max(0f, rowW - priceW - changeW - 16f));
                var showTitle = titleW > 24f;
                var x = Pad;
                Holo.Show(_title, showTitle);
                if (showTitle) { Holo.Place(_title, x, rowY, titleW, rowH); x += titleW + 8f; }
                Holo.Place(_price, x, rowY, priceW, rowH);
                Holo.Place(_change, x + priceW + 8f, rowY, changeW, rowH);
                if (!inBand) y += rowH;
            }
            else
            {
                // Name in the band (or under it when the panel is narrow), then price and signed change on one baseline.
                Holo.Show(_title, true);
                if (bandW >= MinTitleW) Holo.Place(_title, Pad, Top, bandW, HeaderH);
                else { Holo.Place(_title, Pad, y, w - 2 * Pad, HeaderH); y += HeaderH; }
                _price.fontSize = PriceSize;
                var priceH = Holo.LineHeight(PriceSize);
                var priceW = Mathf.Min(Holo.Width(_price, view.PriceText) + 4f, (w - 2 * Pad) * .6f);
                Holo.Place(_price, Pad, y, priceW, priceH);
                Holo.Place(_change, Pad + priceW + 10f, y, w - 2 * Pad - priceW - 10f, priceH);
                y += priceH;
            }

            // Honesty label.
            var statusH = Holo.LineHeight(HoloStyle.MinText);
            Holo.Place(_status, Pad, y, w - 2 * Pad, statusH);
            _status.text = view.Status;
            y += statusH + 2f;
            Rule(w, y);
            y += 6f;

            Holo.Place(_plot, Pad, y, w - 2 * Pad, h - y - Top);
            _plot.CountWidth = w;
            if (Font != null) _plot.Font = Font;
            _plot.SetSeries(series, SpreadLayout.ChartDetailFor(rect));
        }

        private static void StyleToggle(Button b, TMP_Text label, bool on)
        {
            Holo.Paint((SpatialCardGraphic)b.targetGraphic, on ? HoloStyle.Alpha(HoloStyle.Cyan, .22f) : HoloStyle.Alpha(HoloStyle.Cyan, .03f),
                HoloStyle.Alpha(HoloStyle.Cyan, on ? 1f : .5f), on ? HoloStyle.LineWidth : 1.5f);
            label.color = on ? HoloStyle.Ink : HoloStyle.Alpha(HoloStyle.Cyan, .6f);
        }
    }
}
