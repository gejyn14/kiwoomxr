using System;
using SpatialTrading.Domain;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpatialTrading.Components
{
    /// <summary>
    /// One chart on the spread: header (name, price, signed change, honesty label), 4H/1D toggle and the candle plot
    /// whose detail follows the panel size. Draws the <see cref="ChartHeaderView"/> as given.
    /// </summary>
    public sealed class ChartPanel : HoloPanel, IPointerClickHandler
    {
        public const string FourHour = "4H";
        public const string Daily = "1D";
        private const float PriceSize = 28f;
        private const float ToggleW = 48f;
        private const float ToggleH = 30f;

        [SerializeField] private TextMeshProUGUI _title;
        [SerializeField] private TextMeshProUGUI _price;
        [SerializeField] private TextMeshProUGUI _change;
        [SerializeField] private TextMeshProUGUI _status;
        [SerializeField] private Button _fourHour;
        [SerializeField] private TextMeshProUGUI _fourHourLabel;
        [SerializeField] private Button _daily;
        [SerializeField] private TextMeshProUGUI _dailyLabel;
        [SerializeField] private CandleChartGraphic _plot;
        private bool _selected;
        private int _lastClick = -1;

        /// <summary>"4H" or "1D", raised when a toggle is pressed.</summary>
        public event Action<string> IntervalChanged;
        /// <summary>Raised when the panel body is clicked (poke or ray).</summary>
        public event Action Selected;

        public bool IsSelected => _selected;
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
            label = Holo.Text(b.transform, "IntervalLabel", Font, HoloStyle.MinText, HoloStyle.Cyan, TextAlignmentOptions.Midline, true);
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

            // Row 1: name · toggles.
            Holo.Place(_daily, w - Pad - ToggleW, Top, ToggleW, ToggleH);
            Holo.Place(_fourHour, w - Pad - 2 * ToggleW - 6f, Top, ToggleW, ToggleH);
            Holo.Place(_title, Pad, Top, w - 2 * Pad - 2 * ToggleW - 14f, HeaderH);
            _title.text = view.Title;
            StyleToggle(_fourHour, _fourHourLabel, interval == FourHour);
            StyleToggle(_daily, _dailyLabel, interval == Daily);

            // Row 2: price, signed change on the same baseline.
            var y = Top + HeaderH + 2f;
            var priceH = Holo.LineHeight(PriceSize);
            _price.text = view.PriceText;
            var priceW = Mathf.Min(Holo.Width(_price, view.PriceText) + 4f, (w - 2 * Pad) * .6f);
            Holo.Place(_price, Pad, y, priceW, priceH);
            Holo.Place(_change, Pad + priceW + 10f, y, w - 2 * Pad - priceW - 10f, priceH);
            _change.text = view.ChangeText;
            y += priceH;

            // Row 3: honesty label.
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

        /// <summary>Selected: 3 mm bright-cyan edge; otherwise the 2 mm cyan edge.</summary>
        public void SetSelected(bool selected)
        {
            EnsureBuilt();
            _selected = selected;
            SetFrame(selected);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || _lastClick == Time.frameCount) return;
            _lastClick = Time.frameCount;
            Selected?.Invoke();
        }

        private static void StyleToggle(Button b, TMP_Text label, bool on)
        {
            Holo.Paint((SpatialCardGraphic)b.targetGraphic, on ? HoloStyle.Alpha(HoloStyle.Cyan, .22f) : HoloStyle.Alpha(HoloStyle.Cyan, .03f),
                HoloStyle.Alpha(HoloStyle.Cyan, on ? 1f : .5f), on ? HoloStyle.LineWidth : 1.5f);
            label.color = on ? HoloStyle.Ink : HoloStyle.Alpha(HoloStyle.Cyan, .6f);
        }
    }
}
