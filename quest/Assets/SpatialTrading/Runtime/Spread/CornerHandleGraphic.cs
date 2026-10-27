using UnityEngine;
using UnityEngine.UI;

namespace SpatialTrading.Spread
{
    /// <summary>The resize handle mark: a cyan right triangle whose right angle sits in the bottom-right corner.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CornerHandleGraphic : MaskableGraphic
    {
        /// <summary>Leg length as a fraction of the rect's smaller side.</summary>
        public float Leg = .72f;

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var r = rectTransform.rect;
            if (r.width <= 0f || r.height <= 0f) return;
            var leg = Mathf.Min(r.width, r.height) * Mathf.Clamp01(Leg);
            mesh.AddVert(new Vector2(r.xMax, r.yMin), color, Vector2.zero);
            mesh.AddVert(new Vector2(r.xMax, r.yMin + leg), color, Vector2.zero);
            mesh.AddVert(new Vector2(r.xMax - leg, r.yMin), color, Vector2.zero);
            mesh.AddTriangle(0, 1, 2);
        }
    }
}
