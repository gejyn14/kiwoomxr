using System;
using System.Collections.Generic;

namespace SpatialTrading.Domain
{
    public enum PanelId { Watchlist, Chart, OrderBook, News, Balance, Order }

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
        /// <summary>Eye-plane cell edge; <see cref="CellMetersFor"/> gives each plane's own.</summary>
        public const float CellMeters = 0.05f;
        public const float HandCellMeters = 0.035f;
        /// <summary>Hand-plane panels are the same canvas drawn at 0.7× (50 mm canvas cell × 0.7 = 35 mm).</summary>
        public const float HandScale = 0.7f;

        // Cell indices, both ends inclusive. Eye x −13..12 keeps every edge within ±30° at 1.10 m.
        const int EyeMinX = -13, EyeMaxX = 12, EyeMinY = -7, EyeMaxY = 4;
        const int HandMinX = -9, HandMaxX = 8, HandMinY = -4, HandMaxY = 2;

        static readonly PanelId[] Ids = (PanelId[])Enum.GetValues(typeof(PanelId));

        readonly Dictionary<PanelId, PanelRect> rects = new Dictionary<PanelId, PanelRect>();
        readonly HashSet<PanelId> dismissed = new HashSet<PanelId>();

        SpreadLayout() { }

        static PanelRect DefaultRect(PanelId id)
        {
            switch (id)
            {
                case PanelId.Watchlist: return new PanelRect(Plane.Eye, -13, -7, 6, 11);
                case PanelId.Chart: return new PanelRect(Plane.Eye, -7, -2, 8, 6);
                case PanelId.Balance: return new PanelRect(Plane.Eye, -7, -7, 8, 5);
                case PanelId.OrderBook: return new PanelRect(Plane.Eye, 1, -7, 7, 11);
                case PanelId.News: return new PanelRect(Plane.Eye, 8, -7, 5, 11);
                case PanelId.Order: return new PanelRect(Plane.Hand, -2, -3, 9, 6);
                default: throw new ArgumentOutOfRangeException(nameof(id));
            }
        }

        public static float CellMetersFor(Plane p) => p == Plane.Hand ? HandCellMeters : CellMeters;

        /// <summary>Restore dock width in mm: padding, one 64 mm button plus 8 mm gap per dismissed panel, "기본 배치로" (150 mm).</summary>
        public static float DockWidthMm(int dismissedCount) => 24f + dismissedCount * (64f + 8f) + 150f;

        public static float ScaleFor(Plane p) => p == Plane.Hand ? HandScale : 1f;

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
            var cell = CellMetersFor(plane);
            int x = (int)Math.Round(xMeters / cell - cur.Width / 2.0, MidpointRounding.AwayFromZero);
            int y = (int)Math.Round(yMeters / cell - cur.Height / 2.0, MidpointRounding.AwayFromZero);
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
            return area <= 20 ? ChartDetail.Line : area <= 40 ? ChartDetail.Candles : ChartDetail.CandlesWithAxes;
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
                case PanelId.Watchlist: minW = 5; minH = 8; maxW = 8; maxH = 12; break;
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
