using SpatialTrading.Domain;
using UnityEngine;
using UnityEngine.UI;

namespace SpatialTrading.Components
{
    /// <summary>Renders validated structured candles. Origin labeling belongs to its component.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CandleChartGraphic : MaskableGraphic
    {
        public const int MaxVisible = 60;
        private CandleSeries _series;
        public void SetSeries(CandleSeries series) { _series = series; SetVerticesDirty(); }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = rectTransform.rect;
            var grid = new Color(.32f, .45f, .65f, .13f);
            for (var i = 0; i <= 4; i++) Quad(mesh, rect.xMin, rect.yMin + rect.height * (.2f + i * .2f), rect.width, .7f, grid);
            if (_series == null || _series.Candles.Count == 0) return;
            var values = _series.Candles;
            var start = Mathf.Max(0, values.Count - MaxVisible);
            var low = values[start].Low; var high = values[start].High; long maxVolume = 1;
            for (var i = start; i < values.Count; i++)
            { low = System.Math.Min(low, values[i].Low); high = System.Math.Max(high, values[i].High); maxVolume = System.Math.Max(maxVolume, values[i].Volume ?? 0); }
            var padding = System.Math.Max((high - low) * .12m, high * .001m);
            low -= padding; high += padding;
            var range = high - low;
            var step = rect.width / (values.Count - start);
            var plotBottom = rect.yMin + rect.height * .23f;
            var plotHeight = rect.height * .72f;
            for (var i = start; i < values.Count; i++)
            {
                var bar = values[i]; var x = rect.xMin + (i - start + .5f) * step;
                var tint = bar.Close >= bar.Open ? new Color(1f, .43f, .52f) : new Color(.30f, .63f, 1f);
                var yLow = plotBottom + (float)((bar.Low - low) / range) * plotHeight;
                var yHigh = plotBottom + (float)((bar.High - low) / range) * plotHeight;
                var yOpen = plotBottom + (float)((bar.Open - low) / range) * plotHeight;
                var yClose = plotBottom + (float)((bar.Close - low) / range) * plotHeight;
                Quad(mesh, x - .65f, yLow, 1.3f, Mathf.Max(1, yHigh - yLow), tint);
                Quad(mesh, x - step * .29f, Mathf.Min(yOpen, yClose), step * .58f, Mathf.Max(2, Mathf.Abs(yClose - yOpen)), tint);
                if (bar.Volume.HasValue)
                    Quad(mesh, x - step * .29f, rect.yMin, step * .58f,
                        (float)((decimal)bar.Volume.Value / maxVolume) * rect.height * .15f,
                        new Color(tint.r, tint.g, tint.b, .3f));
            }
        }

        private static void Quad(VertexHelper mesh, float x, float y, float width, float height, Color color)
        {
            var start = mesh.currentVertCount;
            mesh.AddVert(new Vector3(x, y), color, Vector2.zero);
            mesh.AddVert(new Vector3(x, y + height), color, Vector2.zero);
            mesh.AddVert(new Vector3(x + width, y + height), color, Vector2.zero);
            mesh.AddVert(new Vector3(x + width, y), color, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
