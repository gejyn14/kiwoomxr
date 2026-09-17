using UnityEngine;
using UnityEngine.UI;

namespace SpatialTrading.Components
{
    /// <summary>Static illustrative candles for M1. Never a financial capability.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SyntheticCandleGraphic : MaskableGraphic
    {
        [SerializeField] private bool _alternate;
        public void SetInstrument(bool alternate) { _alternate = alternate; SetVerticesDirty(); }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var r = rectTransform.rect;
            var grid = new Color(0.32f, 0.45f, 0.58f, 0.22f);
            for (var i = 0; i <= 4; i++)
                Quad(mesh, r.xMin, r.yMin + i * r.height / 4, r.width, 0.7f, grid);
            const int count = 28;
            var step = r.width / count;
            var previous = 0.35f;
            for (var i = 0; i < count; i++)
            {
                // Fixed dimensionless illustration, not generated/estimated market prices.
                var close = 0.33f + i * 0.011f + Mathf.Sin(i * 1.3f + (_alternate ? 1.8f : 0)) * 0.09f;
                var high = Mathf.Max(previous, close) + 0.055f;
                var low = Mathf.Min(previous, close) - 0.045f;
                var tint = close >= previous ? new Color(0.31f, 0.88f, 0.71f) : new Color(1f, 0.47f, 0.43f);
                var x = r.xMin + (i + 0.5f) * step;
                Quad(mesh, x - 0.65f, r.yMin + low * r.height, 1.3f, (high - low) * r.height, tint);
                Quad(mesh, x - step * 0.28f, r.yMin + Mathf.Min(previous, close) * r.height,
                    step * 0.56f, Mathf.Max(2.5f, Mathf.Abs(close - previous) * r.height), tint);
                previous = close;
            }
        }

        private static void Quad(VertexHelper mesh, float x, float y, float width, float height, Color color)
        {
            var start = mesh.currentVertCount;
            mesh.AddVert(new Vector3(x, y), color, Vector2.zero);
            mesh.AddVert(new Vector3(x, y + height), color, Vector2.zero);
            mesh.AddVert(new Vector3(x + width, y + height), color, Vector2.zero);
            mesh.AddVert(new Vector3(x + width, y), color, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2);
            mesh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
