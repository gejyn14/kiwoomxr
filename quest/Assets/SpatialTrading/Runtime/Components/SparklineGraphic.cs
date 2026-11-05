using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SpatialTrading.Components
{
    /// <summary>
    /// A watchlist mini chart: the given closes as one 2 mm cyan line across the rect, the latest close a bright dot.
    /// Fewer than two values draw nothing (the row's price then reads "—").
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SparklineGraphic : MaskableGraphic
    {
        public const float LineWidth = 2f;
        private const float Dot = 5f;
        private readonly List<decimal> _values = new List<decimal>();

        public int Count => _values.Count;

        public void SetValues(IReadOnlyList<decimal> values)
        {
            if (Same(values)) return;
            _values.Clear();
            if (values != null) _values.AddRange(values);
            SetVerticesDirty();
        }

        private bool Same(IReadOnlyList<decimal> values)
        {
            var n = values?.Count ?? 0;
            if (n != _values.Count) return false;
            for (var i = 0; i < n; i++) if (values[i] != _values[i]) return false;
            return true;
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var r = rectTransform.rect;
            if (_values.Count < 2 || r.width <= Dot || r.height <= Dot) return;
            decimal low = decimal.MaxValue, high = decimal.MinValue;
            foreach (var v in _values) { low = Math.Min(low, v); high = Math.Max(high, v); }
            var span = high - low;
            var inset = Dot * .5f;
            var plot = new Rect(r.xMin + inset, r.yMin + inset, r.width - 2 * inset, r.height - 2 * inset);
            Vector2 At(int i)
            {
                var x = plot.xMin + plot.width * i / (_values.Count - 1);
                var y = span <= 0m ? plot.center.y : plot.yMin + (float)((_values[i] - low) / span) * plot.height;
                return new Vector2(x, y);
            }
            var cyan = HoloStyle.Cyan;
            for (var i = 1; i < _values.Count; i++) Segment(mesh, At(i - 1), At(i), LineWidth, cyan);
            var last = At(_values.Count - 1);
            Quad(mesh, last.x - Dot * .5f, last.y - Dot * .5f, Dot, Dot, HoloStyle.BrightCyan);
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
            var s = mesh.currentVertCount;
            mesh.AddVert(new Vector3(x, y), color, Vector2.zero);
            mesh.AddVert(new Vector3(x, y + height), color, Vector2.zero);
            mesh.AddVert(new Vector3(x + width, y + height), color, Vector2.zero);
            mesh.AddVert(new Vector3(x + width, y), color, Vector2.zero);
            mesh.AddTriangle(s, s + 1, s + 2); mesh.AddTriangle(s, s + 2, s + 3);
        }
    }
}
