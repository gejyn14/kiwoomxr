using System;
using System.Collections.Generic;
using SpatialTrading.Domain;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpatialTrading.Components
{
    /// <summary>
    /// Balance: header with the (gateway-masked) account and the honesty label, then total, holdings and the orderable
    /// cash. The empty state is a deliberate composition: one large centred line, the orderable cash and a thin cyan ring.
    /// Account text is displayed only, never logged.
    /// </summary>
    public sealed class BalancePanel : HoloPanel
    {
        private const float EmptySize = 30f;
        private const float TotalSize = 26f;

        [SerializeField] private TextMeshProUGUI _title;
        [SerializeField] private TextMeshProUGUI _account;
        [SerializeField] private TextMeshProUGUI _status;
        [SerializeField] private SpatialCardGraphic _ring;
        [SerializeField] private TextMeshProUGUI _empty;
        [SerializeField] private TextMeshProUGUI _total;
        [SerializeField] private TextMeshProUGUI _orderable;
        [SerializeField] private RectTransform _rowsRoot;
        private readonly List<Row> _rows = new List<Row>();

        private sealed class Row
        {
            public RectTransform Root;
            public TextMeshProUGUI Text;
            public TextMeshProUGUI Profit;
        }

        protected override void Build()
        {
            _title = Holo.Text(transform, "Title", Font, TitleSize, HoloStyle.Ink);
            _title.text = "잔고";
            _account = Holo.Text(transform, "Account", Font, HoloStyle.MinText, HoloStyle.Muted);
            _status = Holo.Text(transform, "Status", Font, HoloStyle.MinText, HoloStyle.Muted, TextAlignmentOptions.MidlineRight);
            _ring = Holo.Card(transform, "Ring", Color.clear, HoloStyle.Alpha(HoloStyle.Cyan, .4f), 1.5f, 100f);
            _empty = Holo.Text(transform, "Empty", Font, EmptySize, HoloStyle.Ink, TextAlignmentOptions.Midline);
            _total = Holo.Text(transform, "TotalPrice", Font, TotalSize, HoloStyle.Ink);
            _orderable = Holo.Text(transform, "OrderablePrice", Font, HoloStyle.MinNumber, HoloStyle.Ink);
            _rowsRoot = Holo.Node(transform, "Rows");
        }

        public void Render(BalanceView view, PanelRect rect)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            EnsureBuilt();
            var size = Size(rect);
            float w = size.x, h = size.y, inner = w - 2 * Pad;

            // Header: 잔고 · account …… status
            _title.text = "잔고";
            var titleW = Holo.Width(_title, _title.text) + 4f;
            Holo.Place(_title, Pad, Top, titleW, HeaderH);
            _status.text = view.Status;
            var statusW = Mathf.Min(Holo.Width(_status, view.Status) + 4f, inner * .45f);
            Holo.Place(_status, w - Pad - statusW, Top, statusW, HeaderH);
            Holo.Place(_account, Pad + titleW + 12f, Top, inner - titleW - statusW - 24f, HeaderH);
            _account.text = view.AccountText;
            Rule(w, Top + HeaderH + 3f);
            var y = Top + HeaderH + 10f;
            var bottom = h - Top;

            Holo.Show(_ring, view.IsEmpty);
            Holo.Show(_empty, view.IsEmpty);
            Holo.Show(_total, !view.IsEmpty);
            Holo.Show(_rowsRoot, !view.IsEmpty);
            _orderable.text = view.OrderableText;

            if (view.IsEmpty)
            {
                var body = bottom - y;
                var cy = y + body / 2f;
                var d = Mathf.Max(0f, Mathf.Min(body - 4f, inner * .6f));
                _ring.Radius = d / 2f;
                Holo.Place(_ring, (w - d) / 2f, cy - d / 2f, d, d);
                var emptyH = Holo.LineHeight(EmptySize);
                var orderH = Holo.LineHeight(HoloStyle.MinNumber);
                var top = cy - (emptyH + orderH) / 2f;
                Holo.Place(_empty, Pad, top, inner, emptyH);
                _empty.text = view.EmptyText;
                _orderable.alignment = TextAlignmentOptions.Midline;
                _orderable.color = HoloStyle.Muted;
                Holo.Place(_orderable, Pad, top + emptyH, inner, orderH);
                return;
            }

            var totalH = Holo.LineHeight(TotalSize);
            Holo.Place(_total, Pad, y, inner, totalH);
            _total.text = view.TotalText;
            y += totalH + 2f;

            var orderableH = Holo.LineHeight(HoloStyle.MinNumber);
            _orderable.alignment = TextAlignmentOptions.MidlineLeft;
            _orderable.color = HoloStyle.Ink;
            Holo.Place(_orderable, Pad, bottom - orderableH, inner, orderableH);

            Holo.Place(_rowsRoot, 0f, 0f, w, h);
            var pitch = Holo.LineHeight(HoloStyle.MinNumber);
            var fit = Math.Max(0, Mathf.FloorToInt((bottom - orderableH - 2f - y) / pitch));
            var shown = Math.Min(view.Rows.Count, fit);
            EnsureRows(shown);
            for (var i = 0; i < shown; i++)
            {
                var row = _rows[i];
                var v = view.Rows[i];
                Holo.Show(row.Root, true);
                Holo.Place(row.Root, Pad, y + i * pitch, inner, pitch);
                row.Profit.text = v.ProfitText;
                var profitW = Mathf.Min(Holo.Width(row.Profit, v.ProfitText) + 4f, inner * .45f);
                Holo.Place(row.Profit, inner - profitW, 0f, profitW, pitch);
                Holo.Place(row.Text, 0f, 0f, inner - profitW - 12f, pitch);
                row.Text.text = v.Text;
            }
            for (var k = shown; k < _rows.Count; k++) Holo.Show(_rows[k].Root, false);
        }

        private void EnsureRows(int count)
        {
            if (_rows.Count == 0)
                for (var k = _rowsRoot.childCount - 1; k >= 0; k--) Holo.Destroy(_rowsRoot.GetChild(k).gameObject);
            while (_rows.Count < count)
            {
                var root = Holo.Node(_rowsRoot, "Holding");
                _rows.Add(new Row
                {
                    Root = root,
                    Text = Holo.Text(root, "HoldingQty", Font, HoloStyle.MinNumber, HoloStyle.Ink),
                    Profit = Holo.Text(root, "ProfitPrice", Font, HoloStyle.MinNumber, HoloStyle.Cyan, TextAlignmentOptions.MidlineRight),
                });
            }
        }
    }
}
