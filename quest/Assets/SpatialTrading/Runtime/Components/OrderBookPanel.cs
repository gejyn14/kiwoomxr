using System;
using System.Collections.Generic;
using SpatialTrading.Domain;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpatialTrading.Components
{
    /// <summary>
    /// Order book: asks on top, bids below, the current price on the divider between them, quantity bars from
    /// <see cref="BookRowView.BarRatio"/>, the bid share and the market label. An index shows only its placeholder.
    /// Depth, ordering and wording all come from <see cref="OrderBookView"/>.
    /// </summary>
    public sealed class OrderBookPanel : HoloPanel
    {
        private const float MidH = 30f;
        private const float BookTop = 8f;
        private const float TitleRowH = 30f;   // 20 mm title, line box 29 mm
        private const float StatusRowH = 24f;  // 16 mm status, line box 23.2 mm
        private const float LastSize = 24f;

        [SerializeField] private TextMeshProUGUI _title;
        [SerializeField] private TextMeshProUGUI _status;
        [SerializeField] private TextMeshProUGUI _hint;
        [SerializeField] private TextMeshProUGUI _placeholder;
        [SerializeField] private RectTransform _rowsRoot;
        [SerializeField] private Image _midLine;
        [SerializeField] private SpatialCardGraphic _lastChip;
        [SerializeField] private TextMeshProUGUI _last;
        [SerializeField] private TextMeshProUGUI _shareKey;
        [SerializeField] private TextMeshProUGUI _share;
        private readonly List<Row> _rows = new List<Row>();

        private sealed class Row
        {
            public RectTransform Root;
            public Image Bar;
            public TextMeshProUGUI Price;
            public TextMeshProUGUI Qty;
        }

        protected override void Build()
        {
            _title = Holo.Text(transform, "Title", Font, TitleSize, HoloStyle.Ink);
            _status = Holo.Text(transform, "Status", Font, HoloStyle.MinText, HoloStyle.Muted);
            _hint = Holo.Text(transform, "Hint", Font, HoloStyle.MinText, HoloStyle.Muted, TextAlignmentOptions.MidlineRight);
            _placeholder = Holo.Text(transform, "Placeholder", Font, 26f, HoloStyle.Muted, TextAlignmentOptions.Center);
            _rowsRoot = Holo.Node(transform, "Rows");
            _midLine = Holo.Bar(transform, "Divider", HoloStyle.Alpha(HoloStyle.Cyan, .5f));
            _lastChip = Holo.Card(transform, "Last Chip", HoloStyle.Alpha(HoloStyle.PanelFill, 1f), HoloStyle.Cyan, 1.5f, 6f);
            _last = Holo.Text(_lastChip.transform, "LastPrice", Font, LastSize, HoloStyle.Ink, TextAlignmentOptions.Midline, true);
            Holo.Stretch(_last);
            _shareKey = Holo.Text(transform, "Share Key", Font, HoloStyle.MinText, HoloStyle.Muted, TextAlignmentOptions.MidlineLeft, true);
            _shareKey.text = "잔량비";
            _share = Holo.Text(transform, "BidShare", Font, HoloStyle.MinNumber, HoloStyle.Ink, TextAlignmentOptions.MidlineRight, true);
        }

        public void Render(OrderBookView view, PanelRect rect)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            EnsureBuilt();
            var size = Size(rect);
            float w = size.x, h = size.y, inner = w - 2 * Pad;

            // Compact header so ten levels a side keep a ≥ 22 mm pitch at 9×11: title, then status with the hint on
            // the same line, then the rule.
            var y = BookTop;
            Holo.Place(_title, Pad, y, inner, TitleRowH);
            _title.text = view.Title;
            y += TitleRowH;
            _status.text = view.Status;
            var hasHint = !string.IsNullOrEmpty(view.Hint);
            var statusW = hasHint ? Mathf.Min(Holo.Width(_status, view.Status) + 4f, inner * .6f) : inner;
            Holo.Place(_status, Pad, y, statusW, StatusRowH);
            Holo.Show(_hint, hasHint);
            if (hasHint)
            {
                Holo.Place(_hint, Pad + statusW + 12f, y, inner - statusW - 12f, StatusRowH);
                _hint.text = view.Hint.Replace('\n', ' ');
            }
            y += StatusRowH + 1f;
            Rule(w, y);
            y += 4f;

            Holo.Show(_placeholder, view.IsIndex);
            Holo.Show(_rowsRoot, !view.IsIndex);
            Holo.Show(_midLine, !view.IsIndex);
            Holo.Show(_lastChip, !view.IsIndex);
            Holo.Show(_shareKey, !view.IsIndex);
            Holo.Show(_share, !view.IsIndex);
            if (view.IsIndex)
            {
                Holo.Place(_placeholder, Pad, y, inner, h - y - BookTop);
                _placeholder.text = view.Placeholder;
                return;
            }

            Holo.Place(_rowsRoot, 0f, 0f, w, h);
            var count = view.Asks.Count + view.Bids.Count;
            EnsureRows(count);
            var pitch = count == 0 ? 0f : Mathf.Max(0f, h - BookTop - y - MidH) / count;
            var i = 0;
            foreach (var r in view.Asks) { LayoutRow(_rows[i], r, y + i * pitch, pitch, inner, true); i++; }

            // Divider row: [last price chip] ──── 잔량비 매수 54%
            var midY = y + view.Asks.Count * pitch;
            _last.text = view.LastPriceText;
            var chipW = Mathf.Min(inner * .45f, Holo.Width(_last, view.LastPriceText) + 28f);
            Holo.Place(_lastChip, Pad, midY + 2f, chipW, MidH - 4f);
            _share.text = view.BidShareText;
            var shareW = Mathf.Min(Holo.Width(_share, view.BidShareText) + 4f, inner * .35f);
            Holo.Place(_share, w - Pad - shareW, midY, shareW, MidH);
            var keyW = Holo.Width(_shareKey, _shareKey.text) + 4f;
            var keyX = w - Pad - shareW - 8f - keyW;
            Holo.Place(_shareKey, keyX, midY, keyW, MidH);
            var lineX = Pad + chipW + 8f;
            Holo.Place(_midLine, lineX, midY + MidH / 2f - .75f, Mathf.Max(0f, keyX - 8f - lineX), 1.5f);

            var bidTop = midY + MidH;
            for (var j = 0; j < view.Bids.Count; j++) LayoutRow(_rows[i + j], view.Bids[j], bidTop + j * pitch, pitch, inner, false);
            for (var k = count; k < _rows.Count; k++) Holo.Show(_rows[k].Root, false);
        }

        private void LayoutRow(Row row, BookRowView view, float y, float pitch, float inner, bool ask)
        {
            Holo.Show(row.Root, true);
            Holo.Place(row.Root, Pad, y, inner, pitch);
            var ink = ask ? HoloStyle.AskInk : HoloStyle.Cyan;
            var barMax = inner * .58f;
            var barW = (float)Math.Max(0d, Math.Min(1d, view.BarRatio)) * barMax;
            Holo.Show(row.Bar, barW > 0f);
            Holo.Place(row.Bar, inner - barW, pitch * .18f, barW, pitch * .64f);
            row.Bar.color = HoloStyle.Alpha(ink, ask ? .18f : .26f);
            Holo.Place(row.Price, 8f, 0f, inner * .45f, pitch);
            row.Price.text = view.PriceText;
            row.Price.color = ink;
            Holo.Place(row.Qty, inner * .45f, 0f, inner * .55f - 8f, pitch);
            row.Qty.text = view.QuantityText;
            row.Qty.color = ink;
        }

        private void EnsureRows(int count)
        {
            if (_rows.Count == 0)
                for (var k = _rowsRoot.childCount - 1; k >= 0; k--) Holo.Destroy(_rowsRoot.GetChild(k).gameObject);
            while (_rows.Count < count)
            {
                var root = Holo.Node(_rowsRoot, "Level");
                var row = new Row
                {
                    Root = root,
                    Bar = Holo.Bar(root, "Bar", HoloStyle.Alpha(HoloStyle.Cyan, .2f)),
                    Price = Holo.Text(root, "Price", Font, HoloStyle.MinNumber, HoloStyle.Cyan, TextAlignmentOptions.MidlineLeft, true),
                    Qty = Holo.Text(root, "Qty", Font, HoloStyle.MinNumber, HoloStyle.Cyan, TextAlignmentOptions.MidlineRight, true),
                };
                _rows.Add(row);
            }
        }
    }
}
