using System;
using System.Collections.Generic;
using SpatialTrading.Domain;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpatialTrading.Components
{
    /// <summary>
    /// Renders validated structured candles in hologram cyan. Rising bars (close ≥ open) are hollow with a 2 mm
    /// outline, falling bars are filled. <see cref="ChartDetail.Line"/> draws one close line; Candles adds volume in the
    /// lower 20 %; CandlesWithAxes adds four price ticks on the right and three time labels below. Origin labeling
    /// belongs to its component.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CandleChartGraphic : MaskableGraphic
    {
        /// <summary>Visible bars for the legacy single-argument <see cref="SetSeries(CandleSeries)"/>.</summary>
        public const int MaxVisible = 60;
        public const float MmPerBar = 9f;
        public const int MinBars = 20;
        public const int MaxBars = 120;
        public const float Outline = 2f;
        private const float AxisRight = 88f;
        private const float AxisBottom = 30f;
        private const float AxisText = HoloStyle.MinText + 2f;
        private static readonly float[] TickAt = { .125f, .375f, .625f, .875f };

        /// <summary>Font for the axis labels; the owning panel injects it.</summary>
        public TMP_FontAsset Font;
        /// <summary>Panel width in millimetres that sets the visible bar count; 0 uses this graphic's own width.</summary>
        public float CountWidth;

        private CandleSeries _series;
        private ChartDetail _detail = ChartDetail.Candles;
        private bool _legacy;
        private readonly List<TextMeshProUGUI> _labels = new List<TextMeshProUGUI>();

        public ChartDetail Detail => _detail;

        /// <summary>Visible bars = panel width (mm) / 9, clamped to 20…120.</summary>
        public static int VisibleCountFor(float widthMm) => Mathf.Clamp(Mathf.FloorToInt(widthMm / MmPerBar), MinBars, MaxBars);

        public void SetSeries(CandleSeries series) { _legacy = true; Apply(series, ChartDetail.Candles); }

        public void SetSeries(CandleSeries series, ChartDetail detail) { _legacy = false; Apply(series, detail); }

        private void Apply(CandleSeries series, ChartDetail detail)
        {
            _series = series;
            _detail = detail;
            UpdateLabels();
            SetVerticesDirty();
        }

        private int Visible => _legacy ? MaxVisible : VisibleCountFor(CountWidth > 0f ? CountWidth : rectTransform.rect.width);

        private struct Window
        {
            public int Start, Slots;
            public decimal Low, High;
            public long MaxVolume;
            public Rect Plot, Price, Volume;
            public float Step => Price.width / Slots;
        }

        private bool TryWindow(out Window w)
        {
            w = default;
            var plot = rectTransform.rect;
            if (_detail == ChartDetail.CandlesWithAxes) { plot.xMax -= AxisRight; plot.yMin += AxisBottom; }
            w.Plot = plot;
            if (_detail == ChartDetail.Line)
            {
                w.Price = plot;
                w.Volume = new Rect(plot.xMin, plot.yMin, plot.width, 0f);
            }
            else
            {
                var vh = plot.height * .2f;
                w.Volume = new Rect(plot.xMin, plot.yMin, plot.width, vh);
                w.Price = new Rect(plot.xMin, plot.yMin + vh + 3f, plot.width, plot.height - vh - 3f);
            }
            var values = _series?.Candles;
            if (values == null || values.Count == 0 || w.Price.width <= 0f || w.Price.height <= 0f) return false;
            w.Slots = Visible;
            w.Start = Math.Max(0, values.Count - w.Slots);
            decimal low = decimal.MaxValue, high = decimal.MinValue;
            long maxVolume = 1;
            for (var i = w.Start; i < values.Count; i++)
            {
                var c = values[i];
                low = Math.Min(low, _detail == ChartDetail.Line ? c.Close : c.Low);
                high = Math.Max(high, _detail == ChartDetail.Line ? c.Close : c.High);
                maxVolume = Math.Max(maxVolume, c.Volume ?? 0);
            }
            var padding = Math.Max((high - low) * .08m, high * .001m);
            w.Low = low - padding;
            w.High = high + padding;
            w.MaxVolume = maxVolume;
            return true;
        }

        private static float Y(in Window w, decimal v) => w.Price.yMin + (float)((v - w.Low) / (w.High - w.Low)) * w.Price.height;

        /// <summary>Latest bar at the right edge; slots are fixed so a short series does not stretch.</summary>
        private float X(in Window w, int index) => w.Price.xMax - (_series.Candles.Count - 1 - index + .5f) * w.Step;

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var ok = TryWindow(out var w);
            var grid = HoloStyle.Alpha(HoloStyle.Cyan, .10f);
            foreach (var t in TickAt) Quad(mesh, w.Price.xMin, w.Price.yMin + w.Price.height * t, w.Price.width, 1f, grid);
            if (_detail == ChartDetail.CandlesWithAxes)
            {
                var axis = HoloStyle.Alpha(HoloStyle.Cyan, .35f);
                Quad(mesh, w.Plot.xMax, w.Plot.yMin, 1.5f, w.Plot.height, axis);
                Quad(mesh, w.Plot.xMin, w.Plot.yMin, w.Plot.width, 1.5f, axis);
            }
            if (!ok) return;
            if (_detail == ChartDetail.Line) DrawLine(mesh, w); else DrawCandles(mesh, w);
        }

        private void DrawLine(VertexHelper mesh, in Window w)
        {
            var values = _series.Candles;
            var cyan = HoloStyle.Cyan;
            var top = HoloStyle.Alpha(cyan, .16f);
            var bottom = HoloStyle.Alpha(cyan, 0f);
            for (var i = w.Start + 1; i < values.Count; i++)
            {
                var a = new Vector2(X(w, i - 1), Y(w, values[i - 1].Close));
                var b = new Vector2(X(w, i), Y(w, values[i].Close));
                var s = mesh.currentVertCount;
                mesh.AddVert(new Vector3(a.x, w.Price.yMin), bottom, Vector2.zero);
                mesh.AddVert(new Vector3(a.x, a.y), top, Vector2.zero);
                mesh.AddVert(new Vector3(b.x, b.y), top, Vector2.zero);
                mesh.AddVert(new Vector3(b.x, w.Price.yMin), bottom, Vector2.zero);
                mesh.AddTriangle(s, s + 1, s + 2); mesh.AddTriangle(s, s + 2, s + 3);
            }
            for (var i = w.Start + 1; i < values.Count; i++)
                Segment(mesh, new Vector2(X(w, i - 1), Y(w, values[i - 1].Close)), new Vector2(X(w, i), Y(w, values[i].Close)), Outline, cyan);
            var last = values.Count - 1;
            Quad(mesh, X(w, last) - 3f, Y(w, values[last].Close) - 3f, 6f, 6f, HoloStyle.BrightCyan);
        }

        private void DrawCandles(VertexHelper mesh, in Window w)
        {
            var values = _series.Candles;
            var cyan = HoloStyle.Cyan;
            var step = w.Step;
            var body = Mathf.Max(2f, step * .7f);
            var wick = Mathf.Clamp(step * .18f, 1.2f, 2f);
            for (var i = w.Start; i < values.Count; i++)
            {
                var c = values[i];
                var x = X(w, i);
                var rising = c.Close >= c.Open;
                if (c.Volume.HasValue)
                    Quad(mesh, x - body / 2f, w.Volume.yMin, body, (float)((decimal)c.Volume.Value / w.MaxVolume) * w.Volume.height,
                        HoloStyle.Alpha(cyan, rising ? .22f : .38f));
                var yLow = Y(w, c.Low);
                var yHigh = Y(w, c.High);
                var top = Mathf.Max(Y(w, c.Open), Y(w, c.Close));
                var bottom = Mathf.Min(Y(w, c.Open), Y(w, c.Close));
                var left = x - body / 2f;
                if (rising && top - bottom >= 2f * Outline + 1f)
                {
                    // Hollow: wick stops at the body, four outline edges.
                    Quad(mesh, x - wick / 2f, yLow, wick, bottom - yLow, cyan);
                    Quad(mesh, x - wick / 2f, top, wick, yHigh - top, cyan);
                    Quad(mesh, left, bottom, body, Outline, cyan);
                    Quad(mesh, left, top - Outline, body, Outline, cyan);
                    Quad(mesh, left, bottom, Outline, top - bottom, cyan);
                    Quad(mesh, left + body - Outline, bottom, Outline, top - bottom, cyan);
                }
                else
                {
                    // Filled (falling, or a rising bar too short to show a hollow interior).
                    Quad(mesh, x - wick / 2f, yLow, wick, Mathf.Max(1f, yHigh - yLow), cyan);
                    var h = Mathf.Max(Outline, top - bottom);
                    Quad(mesh, left, (top + bottom) / 2f - h / 2f, body, h, cyan);
                }
            }
        }

        private void UpdateLabels()
        {
            var w = default(Window);
            var show = _detail == ChartDetail.CandlesWithAxes && TryWindow(out w);
            if (!show)
            {
                foreach (var l in _labels) Holo.Show(l, false);
                return;
            }
            while (_labels.Count < TickAt.Length + 3)
            {
                var label = Holo.Text(transform, "AxisTick", Font, AxisText, HoloStyle.Muted, TextAlignmentOptions.MidlineLeft, true);
                label.rectTransform.anchorMin = label.rectTransform.anchorMax = rectTransform.pivot; // anchoredPosition == local rect space
                _labels.Add(label);
            }
            foreach (var l in _labels) if (Font != null && l.font != Font) l.font = Font;

            var decimals = w.High - w.Low >= 100m ? 0 : 2;
            for (var k = 0; k < TickAt.Length; k++)
            {
                var v = w.Low + (w.High - w.Low) * (decimal)TickAt[k];
                var l = _labels[k];
                l.text = ViewFormat.Number(Math.Round(v, decimals, MidpointRounding.AwayFromZero));
                l.alignment = TextAlignmentOptions.MidlineLeft;
                Anchor(l, new Vector2(0f, .5f), new Vector2(w.Plot.xMax + 8f, w.Price.yMin + w.Price.height * TickAt[k]),
                    new Vector2(AxisRight - 10f, Holo.LineHeight(AxisText)));
                Holo.Show(l, true);
            }

            var values = _series.Candles;
            var first = w.Start;
            var lastIndex = values.Count - 1;
            int[] at = { first, first + (lastIndex - first) / 2, lastIndex };
            for (var k = 0; k < 3; k++)
            {
                var l = _labels[TickAt.Length + k];
                var distinct = k == 0 || at[k] != at[k - 1];
                Holo.Show(l, distinct);
                if (!distinct) continue;
                l.text = TimeLabel(values[at[k]].PeriodKey);
                var x = X(w, at[k]);
                var size = new Vector2(150f, Holo.LineHeight(AxisText));
                var y = w.Plot.yMin - 4f;
                if (k == 0) { l.alignment = TextAlignmentOptions.TopLeft; Anchor(l, new Vector2(0f, 1f), new Vector2(Mathf.Max(w.Plot.xMin, x - 6f), y), size); }
                else if (k == 1) { l.alignment = TextAlignmentOptions.Top; Anchor(l, new Vector2(.5f, 1f), new Vector2(x, y), size); }
                else { l.alignment = TextAlignmentOptions.TopRight; Anchor(l, new Vector2(1f, 1f), new Vector2(Mathf.Min(w.Plot.xMax, x + 6f), y), size); }
            }
        }

        private static void Anchor(TMP_Text l, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var r = l.rectTransform;
            r.pivot = pivot;
            r.anchoredPosition = position;
            r.sizeDelta = size;
        }

        /// <summary>"YYYYMMDD" → "MM/dd"; "YYYYMMDDHHmm" → "MM/dd HH:mm"; anything else verbatim.</summary>
        private static string TimeLabel(string key)
        {
            if (key == null || key.Length < 8) return key ?? string.Empty;
            for (var i = 0; i < key.Length; i++) if (key[i] < '0' || key[i] > '9') return key;
            var date = key.Substring(4, 2) + "/" + key.Substring(6, 2);
            return key.Length >= 12 ? date + " " + key.Substring(8, 2) + ":" + key.Substring(10, 2) : date;
        }

        private static void Segment(VertexHelper mesh, Vector2 a, Vector2 b, float width, Color color)
        {
            var d = b - a;
            if (d.sqrMagnitude < 1e-6f) return;
            var n = new Vector2(-d.y, d.x).normalized * (width / 2f);
            var s = mesh.currentVertCount;
            mesh.AddVert(a + n, color, Vector2.zero);
            mesh.AddVert(b + n, color, Vector2.zero);
            mesh.AddVert(b - n, color, Vector2.zero);
            mesh.AddVert(a - n, color, Vector2.zero);
            mesh.AddTriangle(s, s + 1, s + 2); mesh.AddTriangle(s, s + 2, s + 3);
        }

        private static void Quad(VertexHelper mesh, float x, float y, float width, float height, Color color)
        {
            if (width <= 0f || height <= 0f) return;
            var start = mesh.currentVertCount;
            mesh.AddVert(new Vector3(x, y), color, Vector2.zero);
            mesh.AddVert(new Vector3(x, y + height), color, Vector2.zero);
            mesh.AddVert(new Vector3(x + width, y + height), color, Vector2.zero);
            mesh.AddVert(new Vector3(x + width, y), color, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
