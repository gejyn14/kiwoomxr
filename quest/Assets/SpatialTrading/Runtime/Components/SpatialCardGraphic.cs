using UnityEngine;
using UnityEngine.UI;

namespace SpatialTrading.Components
{
    /// <summary>Resolution-independent rounded panel with a restrained edge and vertical gradient.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SpatialCardGraphic : MaskableGraphic
    {
        public float Radius = 20;
        public float EdgeWidth = 1;
        public Color BottomColor = new Color(0.025f, 0.036f, 0.064f, 0.98f);
        public Color EdgeColor = new Color(0.36f, 0.47f, 0.65f, 0.35f);

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = rectTransform.rect;
            if (rect.width <= 0 || rect.height <= 0) return;
            const int segments = 8;
            const int points = 4 * (segments + 1);
            var radius = Mathf.Min(Radius, Mathf.Min(rect.width, rect.height) * .5f);
            var edge = Mathf.Clamp(EdgeWidth, 0, radius);
            mesh.AddVert(rect.center, Color.Lerp(BottomColor, color, .5f), Vector2.zero);
            for (var ring = 0; ring < 2; ring++)
                for (var corner = 0; corner < 4; corner++)
                {
                    var center = new Vector2(corner == 0 || corner == 3 ? rect.xMax - radius : rect.xMin + radius,
                        corner < 2 ? rect.yMax - radius : rect.yMin + radius);
                    for (var step = 0; step <= segments; step++)
                    {
                        var angle = (corner * 90f + step * 90f / segments) * Mathf.Deg2Rad;
                        var position = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (radius - ring * edge);
                        var fill = Color.Lerp(BottomColor, color, Mathf.InverseLerp(rect.yMin, rect.yMax, position.y));
                        mesh.AddVert(position, ring == 0 ? EdgeColor : fill, Vector2.zero);
                    }
                }
            for (var index = 0; index < points; index++)
            {
                var next = (index + 1) % points;
                mesh.AddTriangle(0, points + 1 + index, points + 1 + next);
                mesh.AddTriangle(1 + index, 1 + next, points + 1 + next);
                mesh.AddTriangle(1 + index, points + 1 + next, points + 1 + index);
            }
        }
    }
}
