using System;
using System.Collections.Generic;
using SpatialTrading.Domain;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpatialTrading.Components
{
    /// <summary>
    /// News list: source tag and time on the left, the headline (up to two lines) on the right. Filing tags are amber.
    /// The number of rows follows the panel height; pass <see cref="RowsFor"/> as the max to <see cref="NewsView.Rank"/>.
    /// </summary>
    public sealed class NewsPanel : HoloPanel
    {
        private const float Pitch = 60f;
        private const float TagW = 70f;
        private const float HeadlineSize = 18f;

        [SerializeField] private TextMeshProUGUI _title;
        [SerializeField] private TextMeshProUGUI _empty;
        [SerializeField] private RectTransform _rowsRoot;
        private readonly List<Row> _rows = new List<Row>();

        private sealed class Row
        {
            public RectTransform Root;
            public SpatialCardGraphic TagChip;
            public TextMeshProUGUI Tag;
            public TextMeshProUGUI Time;
            public TextMeshProUGUI Headline;
            public Image Separator;
        }

        private static float BodyTop => Top + HeaderH + 10f;

        /// <summary>How many rows fit a panel of this size (at least 1).</summary>
        public static int RowsFor(PanelRect rect) =>
            Math.Max(1, Mathf.FloorToInt((HoloStyle.SizeFor(rect).y - BodyTop - Top) / Pitch));

        protected override void Build()
        {
            _title = Holo.Text(transform, "Title", Font, TitleSize, HoloStyle.Ink);
            _title.text = "뉴스";
            _empty = Holo.Text(transform, "Empty", Font, 26f, HoloStyle.Muted, TextAlignmentOptions.Center);
            _rowsRoot = Holo.Node(transform, "Rows");
        }

        public void Render(IReadOnlyList<NewsRowView> rows, PanelRect rect)
        {
            EnsureBuilt();
            var size = Size(rect);
            float w = size.x, h = size.y, inner = w - 2 * Pad;
            Holo.Place(_title, Pad, Top, inner, HeaderH);
            Rule(w, Top + HeaderH + 3f);

            var y = BodyTop;
            var shown = Math.Min(rows?.Count ?? 0, RowsFor(rect));
            Holo.Show(_empty, shown == 0);
            if (shown == 0)
            {
                Holo.Place(_empty, Pad, y, inner, h - y - Top);
                _empty.text = ViewFormat.None;
            }
            Holo.Place(_rowsRoot, 0f, 0f, w, h);
            EnsureRows(shown);
            for (var i = 0; i < shown; i++) LayoutRow(_rows[i], rows[i], y + i * Pitch, inner, i < shown - 1);
            for (var k = shown; k < _rows.Count; k++) Holo.Show(_rows[k].Root, false);
        }

        private static void LayoutRow(Row row, NewsRowView view, float y, float inner, bool separator)
        {
            Holo.Show(row.Root, true);
            Holo.Place(row.Root, Pad, y, inner, Pitch);
            var accent = view.IsFiling ? HoloStyle.Amber : HoloStyle.Cyan;
            Holo.Paint(row.TagChip, HoloStyle.Alpha(accent, view.IsFiling ? .16f : .06f), HoloStyle.Alpha(accent, view.IsFiling ? .9f : .45f), 1.5f);
            Holo.Place(row.TagChip, 0f, 2f, TagW, 26f);
            row.Tag.text = view.Tag;
            row.Tag.color = view.IsFiling ? HoloStyle.Amber : HoloStyle.Cyan;
            Holo.Place(row.Time, 0f, 30f, TagW, Holo.LineHeight(HoloStyle.MinText) - 4f);
            row.Time.text = view.TimeText;
            var headlineH = 2 * Holo.LineHeight(HeadlineSize) - 4f;
            Holo.Place(row.Headline, TagW + 10f, 0f, inner - TagW - 10f, headlineH);
            row.Headline.text = view.Title;
            Holo.Show(row.Separator, separator);
            Holo.Place(row.Separator, 0f, Pitch - 3f, inner, 1f);
        }

        private void EnsureRows(int count)
        {
            if (_rows.Count == 0)
                for (var k = _rowsRoot.childCount - 1; k >= 0; k--) Holo.Destroy(_rowsRoot.GetChild(k).gameObject);
            while (_rows.Count < count)
            {
                var root = Holo.Node(_rowsRoot, "Item");
                var chip = Holo.Card(root, "Tag Chip", Color.clear, HoloStyle.Cyan, 1.5f, 5f);
                var tag = Holo.Text(chip.transform, "Tag", Font, HoloStyle.MinText, HoloStyle.Cyan, TextAlignmentOptions.Midline);
                Holo.Stretch(tag);
                tag.margin = new Vector4(4f, 0f, 4f, 0f);
                var headline = Holo.Text(root, "Headline", Font, HeadlineSize, HoloStyle.Ink, TextAlignmentOptions.TopLeft);
                headline.textWrappingMode = TextWrappingModes.Normal;
                headline.maxVisibleLines = 2;
                headline.lineSpacing = -8f;
                _rows.Add(new Row
                {
                    Root = root,
                    TagChip = chip,
                    Tag = tag,
                    Time = Holo.Text(root, "Time", Font, HoloStyle.MinText, HoloStyle.Muted, TextAlignmentOptions.TopLeft, true),
                    Headline = headline,
                    Separator = Holo.Bar(root, "Separator", HoloStyle.Alpha(HoloStyle.Cyan, .12f)),
                });
            }
        }
    }
}
