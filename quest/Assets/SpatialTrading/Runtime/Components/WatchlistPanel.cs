using System;
using System.Collections.Generic;
using SpatialTrading.Domain;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpatialTrading.Components
{
    /// <summary>
    /// 관심종목: one row per instrument with name, price, signed change, its own honesty label and a 20-day cyan mini
    /// chart. The whole row is one button (poke or ray); the selected row carries the 3 mm bright edge. Rows share the
    /// panel height evenly (about 11.5 cm each at the default 6 × 11), from under the close button to just above the resize
    /// handle's grab square. Draws the <see cref="WatchlistRowView"/>s as given.
    /// </summary>
    public sealed class WatchlistPanel : HoloPanel
    {
        public const string TitleText = "관심종목";
        private const float RowGap = 6f;
        private const float RowInset = 10f;
        private const float SparkMinHeight = 14f;

        [SerializeField] private TextMeshProUGUI _title;
        [SerializeField] private TextMeshProUGUI _empty;
        [SerializeField] private RectTransform _rowsRoot;
        private readonly List<Row> _rows = new List<Row>();
        private readonly List<InstrumentRef> _shown = new List<InstrumentRef>();
        private float _pitch;

        private sealed class Row
        {
            public InstrumentRef Instrument;
            public Button Button;
            public SpatialCardGraphic Card;
            public TextMeshProUGUI Name;
            public TextMeshProUGUI Price;
            public TextMeshProUGUI Change;
            public TextMeshProUGUI Status;
            public SparklineGraphic Spark;
        }

        /// <summary>A row was pressed (poke, ray or the editor mouse via <see cref="TryRowAt"/>).</summary>
        public event Action<InstrumentRef> Selected;

        protected override void Build()
        {
            _title = Holo.Text(transform, "Title", Font, TitleSize, HoloStyle.Ink);
            _title.text = TitleText;
            _empty = Holo.Text(transform, "Empty", Font, 26f, HoloStyle.Muted, TextAlignmentOptions.Center);
            _rowsRoot = Holo.Node(transform, "Rows");
        }

        public void Render(IReadOnlyList<WatchlistRowView> rows, PanelRect rect)
        {
            EnsureBuilt();
            var size = Size(rect);
            float w = size.x, h = size.y, inner = w - 2 * Pad;
            Holo.Place(_title, Pad, Top, w - CloseReserveMm - Pad, HeaderH);
            Rule(w, Top + HeaderH + 3f);

            var count = rows?.Count ?? 0;
            // The last row (less its bottom gap) ends 2 mm above the resize handle's grab square.
            var body = h - HoloStyle.HandleClearMm - 2f + RowGap - BodyTop;
            _pitch = count > 0 ? body / count : 0f;
            Holo.Show(_empty, count == 0);
            if (count == 0)
            {
                Holo.Place(_empty, Pad, BodyTop, inner, h - BodyTop - Top);
                _empty.text = ViewFormat.None;
            }
            Holo.Place(_rowsRoot, 0f, 0f, w, h);
            EnsureRows(count);
            _shown.Clear();
            for (var i = 0; i < count; i++)
            {
                _shown.Add(rows[i].Instrument);
                LayoutRow(_rows[i], rows[i], BodyTop + i * _pitch, inner);
            }
            for (var k = count; k < _rows.Count; k++) { _rows[k].Instrument = null; Holo.Show(_rows[k].Button, false); }
        }

        /// <summary>The instrument whose row contains <paramref name="world"/> (a point on or just behind the panel).</summary>
        public bool TryRowAt(Vector3 world, out InstrumentRef instrument)
        {
            instrument = null;
            if (_pitch <= 0f || _shown.Count == 0) return false;
            var r = Root.rect;
            var local = Root.InverseTransformPoint(world);
            float x = local.x - r.xMin, fromTop = r.yMax - local.y;
            if (x < Pad || x > r.width - Pad) return false;
            var offset = fromTop - BodyTop;
            if (offset < 0f) return false;
            var i = Mathf.FloorToInt(offset / _pitch);
            if (i < 0 || i >= _shown.Count || offset - i * _pitch > _pitch - RowGap) return false;
            instrument = _shown[i];
            return instrument != null;
        }

        private void LayoutRow(Row row, WatchlistRowView view, float y, float inner)
        {
            row.Instrument = view.Instrument;
            Holo.Show(row.Button, true);
            var height = _pitch - RowGap;
            Holo.Place(row.Button, Pad, y, inner, height);
            Holo.Paint(row.Card,
                HoloStyle.Alpha(HoloStyle.Cyan, view.Selected ? .14f : .03f),
                view.Selected ? HoloStyle.BrightCyan : HoloStyle.Alpha(HoloStyle.Cyan, .3f),
                view.Selected ? HoloStyle.SelectedLineWidth : 1.5f);

            float cw = inner - 2 * RowInset;
            var numberH = Holo.LineHeight(HoloStyle.MinNumber);
            // Line 1: name …… price (the price keeps its full width; the name gives way).
            row.Price.text = view.PriceText;
            var priceW = Mathf.Min(Holo.Width(row.Price, view.PriceText) + 4f, cw);
            Holo.Place(row.Price, RowInset + cw - priceW, 4f, priceW, numberH);
            row.Name.text = view.Name;
            Holo.Place(row.Name, RowInset, 4f, Mathf.Max(0f, cw - priceW - 8f), numberH);
            // Line 2: signed change (ViewFormat signs; "—" when it cannot be computed).
            row.Change.text = view.ChangeText;
            Holo.Place(row.Change, RowInset, 32f, cw, numberH);
            // Line 3: this row's own honesty label.
            row.Status.text = view.Status;
            var statusH = Holo.LineHeight(HoloStyle.MinText);
            Holo.Place(row.Status, RowInset, 60f, cw, statusH);
            // Mini chart under the text when the row is tall enough.
            var sparkTop = 60f + statusH + 2f;
            var sparkH = height - sparkTop - 6f;
            var showSpark = sparkH >= SparkMinHeight && view.Spark != null && view.Spark.Count >= 2;
            Holo.Show(row.Spark, showSpark);
            Holo.Place(row.Spark, RowInset, sparkTop, cw, Mathf.Max(0f, sparkH));
            row.Spark.SetValues(showSpark ? view.Spark : null);
        }

        private void EnsureRows(int count)
        {
            if (_rows.Count == 0)
                for (var k = _rowsRoot.childCount - 1; k >= 0; k--) Holo.Destroy(_rowsRoot.GetChild(k).gameObject);
            while (_rows.Count < count)
            {
                var button = Holo.Button(_rowsRoot, "Watchlist row");
                var row = new Row
                {
                    Button = button,
                    Card = (SpatialCardGraphic)button.targetGraphic,
                    Name = Holo.Text(button.transform, "Name", Font, TitleSize, HoloStyle.Ink),
                    Price = Holo.Text(button.transform, "Price", Font, HoloStyle.MinNumber, HoloStyle.Ink, TextAlignmentOptions.MidlineRight),
                    Change = Holo.Text(button.transform, "PriceChange", Font, HoloStyle.MinNumber, HoloStyle.Cyan),
                    Status = Holo.Text(button.transform, "Status", Font, HoloStyle.MinText, HoloStyle.Muted),
                };
                var spark = Holo.Node(button.transform, "Spark").gameObject.AddComponent<SparklineGraphic>();
                spark.raycastTarget = false;
                row.Spark = spark;
                Holo.OnClick(button, () => { if (row.Instrument != null) Selected?.Invoke(row.Instrument); });
                _rows.Add(row);
            }
        }
    }
}
