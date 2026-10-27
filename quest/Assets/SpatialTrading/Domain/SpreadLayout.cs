using System;
using System.Collections.Generic;

namespace SpatialTrading.Domain
{
    public enum PanelId { ChartKiwoom, ChartSamsung, ChartSkHynix, ChartKospi, OrderBook, News, Balance, Order }

    public enum Plane { Eye, Hand }

    public enum ChartDetail { Line, Candles, CandlesWithAxes }

    /// <summary>Grid rectangle in cells. (X, Y) is the lower-left cell; Y grows upward.</summary>
    public readonly struct PanelRect : IEquatable<PanelRect>
    {
        public Plane Plane { get; }
        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }

        public PanelRect(Plane plane, int x, int y, int width, int height)
        {
            Plane = plane; X = x; Y = y; Width = width; Height = height;
        }

        public bool Overlaps(PanelRect o) =>
            Plane == o.Plane && X < o.X + o.Width && o.X < X + Width && Y < o.Y + o.Height && o.Y < Y + Height;

        public bool Equals(PanelRect o) =>
            Plane == o.Plane && X == o.X && Y == o.Y && Width == o.Width && Height == o.Height;
        public override bool Equals(object obj) => obj is PanelRect r && Equals(r);
        public override int GetHashCode() => HashCode.Combine((int)Plane, X, Y, Width, Height);
        public static bool operator ==(PanelRect a, PanelRect b) => a.Equals(b);
        public static bool operator !=(PanelRect a, PanelRect b) => !a.Equals(b);
        public override string ToString() => $"{Plane}({X},{Y},{Width}x{Height})";
    }

    /// <summary>Panel placement on the two grid planes. Pure data, no engine types.</summary>
    public sealed class SpreadLayout
    {
        public const float CellMeters = 0.05f;

        const int EyeMinX = -20, EyeMaxX = 19, EyeMinY = -6, EyeMaxY = 5;
        const int HandMinX = -10, HandMaxX = 9, HandMinY = -3, HandMaxY = 2;

        static readonly PanelId[] Ids = (PanelId[])Enum.GetValues(typeof(PanelId));

        readonly Dictionary<PanelId, PanelRect> rects = new Dictionary<PanelId, PanelRect>();
        readonly HashSet<PanelId> dismissed = new HashSet<PanelId>();

        SpreadLayout() { }

        static PanelRect DefaultRect(PanelId id)
        {
            switch (id)
            {
                case PanelId.ChartKiwoom: return new PanelRect(Plane.Eye, -19, 1, 7, 5);
                case PanelId.ChartSamsung: return new PanelRect(Plane.Eye, -12, 1, 7, 5);
                case PanelId.ChartSkHynix: return new PanelRect(Plane.Eye, -19, -5, 7, 5);
                case PanelId.ChartKospi: return new PanelRect(Plane.Eye, -12, -5, 7, 5);
                case PanelId.OrderBook: return new PanelRect(Plane.Eye, -4, -5, 9, 11);
                case PanelId.News: return new PanelRect(Plane.Eye, 5, -5, 7, 11);
                case PanelId.Balance: return new PanelRect(Plane.Hand, -9, -2, 9, 5);
                case PanelId.Order: return new PanelRect(Plane.Hand, 1, -3, 9, 6);
                default: throw new ArgumentOutOfRangeException(nameof(id));
            }
        }

        public static SpreadLayout TraderDefault()
        {
            var l = new SpreadLayout();
            l.Reset();
            return l;
        }

        public PanelRect this[PanelId id] => rects[id];

        public bool IsDismissed(PanelId id) => dismissed.Contains(id);

        public IReadOnlyList<PanelId> Dismissed
        {
            get
            {
                var list = new List<PanelId>();
                foreach (var id in Ids) if (dismissed.Contains(id)) list.Add(id);
                return list;
            }
        }

        public PanelRect Drop(PanelId id, Plane plane, float xMeters, float yMeters)
        {
            var cur = rects[id];
            int x = (int)Math.Round(xMeters / CellMeters - cur.Width / 2.0, MidpointRounding.AwayFromZero);
            int y = (int)Math.Round(yMeters / CellMeters - cur.Height / 2.0, MidpointRounding.AwayFromZero);
            return Place(id, plane, x, y, cur.Width, cur.Height, out var placed) ? Store(id, placed) : cur;
        }

        public PanelRect Resize(PanelId id, int width, int height)
        {
            Limits(id, out int minW, out int minH, out int maxW, out int maxH);
            var cur = rects[id];
            int w = Math.Max(minW, Math.Min(maxW, width));
            int h = Math.Max(minH, Math.Min(maxH, height));
            return Place(id, cur.Plane, cur.X, cur.Y, w, h, out var placed) ? Store(id, placed) : cur;
        }

        public void Dismiss(PanelId id) => dismissed.Add(id);

        public PanelRect Restore(PanelId id)
        {
            if (!dismissed.Contains(id)) return rects[id];
            var d = DefaultRect(id);
            Place(id, d.Plane, d.X, d.Y, d.Width, d.Height, out var r);
            dismissed.Remove(id);
            rects[id] = r;
            return r;
        }

        public void Reset()
        {
            dismissed.Clear();
            foreach (var id in Ids) rects[id] = DefaultRect(id);
        }

        public static ChartDetail ChartDetailFor(PanelRect r)
        {
            int area = r.Width * r.Height;
            return area <= 20 ? ChartDetail.Line : area <= 63 ? ChartDetail.Candles : ChartDetail.CandlesWithAxes;
        }

        public static int BookDepthFor(PanelRect r) => r.Height >= 11 ? 10 : 5;

        PanelRect Store(PanelId id, PanelRect r)
        {
            rects[id] = r;
            return r;
        }

        static void Limits(PanelId id, out int minW, out int minH, out int maxW, out int maxH)
        {
            switch (id)
            {
                case PanelId.OrderBook: minW = 6; minH = 8; maxW = 12; maxH = 12; break;
                case PanelId.News: minW = 5; minH = 6; maxW = 10; maxH = 12; break;
                case PanelId.Balance: minW = 6; minH = 3; maxW = 12; maxH = 6; break;
                case PanelId.Order: minW = 6; minH = 4; maxW = 12; maxH = 7; break;
                default: minW = 4; minH = 3; maxW = 16; maxH = 10; break;
            }
        }

        static void Bounds(Plane p, out int minX, out int maxX, out int minY, out int maxY)
        {
            if (p == Plane.Eye) { minX = EyeMinX; maxX = EyeMaxX; minY = EyeMinY; maxY = EyeMaxY; }
            else { minX = HandMinX; maxX = HandMaxX; minY = HandMinY; maxY = HandMaxY; }
        }

        bool Blocked(PanelId self, PanelRect r)
        {
            foreach (var id in Ids)
            {
                if (id == self || dismissed.Contains(id)) continue;
                if (rects[id].Overlaps(r)) return true;
            }
            return false;
        }

        // Clamp into the plane, then search outward in Chebyshev rings for the nearest free cell.
        // Returns false when nothing fits; placed is then the clamped rect (overlapping).
        bool Place(PanelId self, Plane plane, int x, int y, int w, int h, out PanelRect placed)
        {
            Bounds(plane, out int minX, out int maxX, out int minY, out int maxY);
            w = Math.Min(w, maxX - minX + 1);
            h = Math.Min(h, maxY - minY + 1);
            int loX = minX, hiX = maxX - w + 1, loY = minY, hiY = maxY - h + 1;
            int cx = Math.Max(loX, Math.Min(hiX, x));
            int cy = Math.Max(loY, Math.Min(hiY, y));
            var clamped = new PanelRect(plane, cx, cy, w, h);
            placed = clamped;
            if (!Blocked(self, clamped)) return true;

            int maxRing = Math.Max(hiX - loX, hiY - loY);
            for (int ring = 1; ring <= maxRing; ring++)
            {
                bool found = false;
                int bestDx = 0, bestDy = 0, bestCost = int.MaxValue;
                for (int dx = -ring; dx <= ring; dx++)
                {
                    for (int dy = -ring; dy <= ring; dy++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring) continue;
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < loX || nx > hiX || ny < loY || ny > hiY) continue;
                        if (Blocked(self, new PanelRect(plane, nx, ny, w, h))) continue;
                        int cost = Math.Abs(dx) + Math.Abs(dy);
                        // Iteration is x ascending then y ascending, so strict < keeps lower x then lower y.
                        if (!found || cost < bestCost || (cost == bestCost && (nx < cx + bestDx || (nx == cx + bestDx && ny < cy + bestDy))))
                        {
                            found = true; bestCost = cost; bestDx = dx; bestDy = dy;
                        }
                    }
                }
                if (found) { placed = new PanelRect(plane, cx + bestDx, cy + bestDy, w, h); return true; }
            }
            return false;
        }
    }
}
