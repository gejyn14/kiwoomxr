using System.Linq;
using NUnit.Framework;
using SpatialTrading.Domain;

namespace SpatialTrading.Tests
{
    public sealed class SpreadLayoutTests
    {
        static readonly PanelId[] All =
        {
            PanelId.Watchlist, PanelId.Chart, PanelId.OrderBook, PanelId.News, PanelId.Balance, PanelId.Order,
        };

        static readonly (PanelId Id, PanelRect Rect)[] Spec =
        {
            (PanelId.Watchlist, new PanelRect(Plane.Eye, -13, -7, 6, 11)),
            (PanelId.Chart, new PanelRect(Plane.Eye, -7, -2, 8, 6)),
            (PanelId.Balance, new PanelRect(Plane.Eye, -7, -7, 8, 5)),
            (PanelId.OrderBook, new PanelRect(Plane.Eye, 1, -7, 7, 11)),
            (PanelId.News, new PanelRect(Plane.Eye, 8, -7, 5, 11)),
            (PanelId.Order, new PanelRect(Plane.Hand, -2, -3, 9, 6)),
        };

        static void AssertInBounds(PanelRect r)
        {
            int minX = r.Plane == Plane.Eye ? -13 : -9, maxX = r.Plane == Plane.Eye ? 12 : 8;
            int minY = r.Plane == Plane.Eye ? -7 : -4, maxY = r.Plane == Plane.Eye ? 4 : 2;
            Assert.That(r.X, Is.GreaterThanOrEqualTo(minX));
            Assert.That(r.Y, Is.GreaterThanOrEqualTo(minY));
            Assert.That(r.X + r.Width - 1, Is.LessThanOrEqualTo(maxX));
            Assert.That(r.Y + r.Height - 1, Is.LessThanOrEqualTo(maxY));
        }

        static void AssertNoOverlap(SpreadLayout l)
        {
            var live = All.Where(p => !l.IsDismissed(p)).ToList();
            for (int i = 0; i < live.Count; i++)
                for (int j = i + 1; j < live.Count; j++)
                    Assert.That(l[live[i]].Overlaps(l[live[j]]), Is.False, live[i] + " vs " + live[j]);
        }

        [Test] public void DefaultLayoutMatchesSpec()
        {
            Assert.That(System.Enum.GetValues(typeof(PanelId)).Cast<PanelId>(), Is.EquivalentTo(All));
            var l = SpreadLayout.TraderDefault();
            foreach (var (id, rect) in Spec) Assert.That(l[id], Is.EqualTo(rect), id.ToString());
            foreach (var p in All) AssertInBounds(l[p]);
            AssertNoOverlap(l);
        }

        [Test] public void DefaultEyeRowFitsThirtyDegrees()
        {
            // 1.10 m is SpreadWorkspace.EyeDistance (runtime, not visible from the domain tests).
            const double eyeDistance = 1.10;
            var l = SpreadLayout.TraderDefault();
            var cell = SpreadLayout.CellMetersFor(Plane.Eye);
            foreach (var p in All.Where(p => l[p].Plane == Plane.Eye))
            {
                var r = l[p];
                Assert.That(r.X, Is.GreaterThanOrEqualTo(-13), p.ToString());
                Assert.That(r.X + r.Width, Is.LessThanOrEqualTo(13), p.ToString());
                foreach (var edge in new[] { r.X, r.X + r.Width })
                {
                    var degrees = System.Math.Atan(System.Math.Abs(edge * cell) / eyeDistance) * 180.0 / System.Math.PI;
                    Assert.That(degrees, Is.LessThanOrEqualTo(31.0), p + " edge " + edge);
                }
            }
        }

        [Test] public void DropOnHandPlaneUsesHandCells()
        {
            var l = SpreadLayout.TraderDefault();
            l.Drop(PanelId.Order, Plane.Hand, 0.5f, 0.0f); // somewhere else first
            var r = l.Drop(PanelId.Order, Plane.Hand, 0.0875f, 0.0f);
            Assert.That(r, Is.EqualTo(new PanelRect(Plane.Hand, -2, -3, 9, 6)));
            Assert.That(l[PanelId.Order], Is.EqualTo(r));
        }

        [Test] public void DropBeyondHandBoundsClampsInside()
        {
            var l = SpreadLayout.TraderDefault();
            var r = l.Drop(PanelId.Order, Plane.Hand, 0.6f, 0f);
            Assert.That(r, Is.EqualTo(new PanelRect(Plane.Hand, 0, -3, 9, 6)));
            AssertInBounds(r);
        }

        [Test] public void ScaleAndCellSizePerPlane()
        {
            Assert.That(SpreadLayout.CellMetersFor(Plane.Eye), Is.EqualTo(0.05f));
            Assert.That(SpreadLayout.CellMetersFor(Plane.Hand), Is.EqualTo(0.035f));
            Assert.That(SpreadLayout.ScaleFor(Plane.Eye), Is.EqualTo(1f));
            Assert.That(SpreadLayout.ScaleFor(Plane.Hand), Is.EqualTo(0.7f));
            Assert.That(SpreadLayout.CellMeters, Is.EqualTo(SpreadLayout.CellMetersFor(Plane.Eye)));
        }

        [Test] public void ChartDetailThresholds()
        {
            Assert.That(SpreadLayout.ChartDetailFor(new PanelRect(Plane.Eye, 0, 0, 4, 5)), Is.EqualTo(ChartDetail.Line));
            Assert.That(SpreadLayout.ChartDetailFor(new PanelRect(Plane.Eye, 0, 0, 5, 8)), Is.EqualTo(ChartDetail.Candles));
            Assert.That(SpreadLayout.ChartDetailFor(new PanelRect(Plane.Eye, 0, 0, 8, 6)), Is.EqualTo(ChartDetail.CandlesWithAxes));
            Assert.That(SpreadLayout.ChartDetailFor(SpreadLayout.TraderDefault()[PanelId.Chart]), Is.EqualTo(ChartDetail.CandlesWithAxes));
            Assert.That(SpreadLayout.BookDepthFor(new PanelRect(Plane.Eye, 0, 0, 9, 11)), Is.EqualTo(10));
            Assert.That(SpreadLayout.BookDepthFor(new PanelRect(Plane.Eye, 0, 0, 9, 8)), Is.EqualTo(5));
        }

        [Test] public void WatchlistLimits()
        {
            var l = SpreadLayout.TraderDefault();
            var small = l.Resize(PanelId.Watchlist, 3, 3);
            Assert.That((small.Width, small.Height), Is.EqualTo((5, 8)));
            // 8×12 is the full eye-plane height: clear the neighbours so the clamp, not the overlap rule, decides.
            l.Dismiss(PanelId.Chart);
            l.Dismiss(PanelId.Balance);
            var big = l.Resize(PanelId.Watchlist, 20, 20);
            Assert.That((big.Width, big.Height), Is.EqualTo((8, 12)));
            AssertInBounds(big);
            AssertNoOverlap(l);
        }

        [Test] public void ResetRestoresAllSix()
        {
            var l = SpreadLayout.TraderDefault();
            l.Dismiss(PanelId.Order);
            l.Dismiss(PanelId.Watchlist);
            l.Drop(PanelId.OrderBook, Plane.Eye, 0.3f, 0.1f);
            l.Resize(PanelId.Chart, 4, 3);
            l.Reset();
            foreach (var (id, rect) in Spec) Assert.That(l[id], Is.EqualTo(rect), id.ToString());
            Assert.That(l.Dismissed, Is.Empty);
        }

        [Test] public void DropSnapsToNearestCell()
        {
            var l = SpreadLayout.TraderDefault();
            var r = l.Drop(PanelId.News, Plane.Eye, 0.52f, -0.04f);
            Assert.That(r, Is.EqualTo(new PanelRect(Plane.Eye, 8, -6, 5, 11)));
            Assert.That(l[PanelId.News], Is.EqualTo(r));
        }

        [Test] public void DropOutsideBoundsIsPulledBackInside()
        {
            var l = SpreadLayout.TraderDefault();
            AssertInBounds(l.Drop(PanelId.News, Plane.Eye, 5f, 3f));
            AssertInBounds(l.Drop(PanelId.News, Plane.Eye, -5f, -3f));
            AssertInBounds(l.Drop(PanelId.Order, Plane.Hand, 5f, 3f));
            AssertInBounds(l.Drop(PanelId.Order, Plane.Hand, -5f, -3f));
        }

        [Test] public void DropOntoAnotherPanelMovesToNearestFreeCell()
        {
            var l = SpreadLayout.TraderDefault();
            var r = l.Drop(PanelId.News, Plane.Eye, 0.05f * 0.5f, 0.05f * 0.5f);
            Assert.That(r.Overlaps(l[PanelId.OrderBook]), Is.False);
            AssertInBounds(r);
            AssertNoOverlap(l);
        }

        [Test] public void ResizeIsClampedPerPanelKind()
        {
            var l = SpreadLayout.TraderDefault();
            var small = l.Resize(PanelId.Chart, 1, 1);
            Assert.That(small.Width, Is.EqualTo(4));
            Assert.That(small.Height, Is.EqualTo(3));
            var big = l.Resize(PanelId.Chart, 40, 40);
            Assert.That(big.Width, Is.LessThanOrEqualTo(16));
            Assert.That(big.Height, Is.LessThanOrEqualTo(10));
            AssertInBounds(big);
            AssertNoOverlap(l);
        }

        [Test] public void ResizeNeverLeavesAnOverlap()
        {
            var l = SpreadLayout.TraderDefault();
            l.Resize(PanelId.Chart, 8, 6);
            AssertNoOverlap(l);
            var before = l[PanelId.OrderBook];
            var after = l.Resize(PanelId.OrderBook, 12, 12); // no 12×12 hole on the eye plane
            Assert.That(after, Is.EqualTo(before));
            Assert.That(l[PanelId.OrderBook], Is.EqualTo(before));
            AssertNoOverlap(l);
        }

        [Test] public void DropWithNoFreeSpotKeepsPreviousRect()
        {
            var l = SpreadLayout.TraderDefault();
            var before = l[PanelId.Order];
            var r = l.Drop(PanelId.Order, Plane.Eye, 0.1f, 0.1f);
            Assert.That(r, Is.EqualTo(before));
            Assert.That(l[PanelId.Order], Is.EqualTo(before));
            AssertNoOverlap(l);
        }

        [Test] public void RestoreReturnsToDefaultSlotOrNearestFree()
        {
            var l = SpreadLayout.TraderDefault();
            var slot = l[PanelId.News];
            l.Dismiss(PanelId.News);
            Assert.That(l.IsDismissed(PanelId.News), Is.True);
            Assert.That(l.Dismissed, Is.EqualTo(new[] { PanelId.News }));
            l.Drop(PanelId.OrderBook, Plane.Eye, (slot.X + 4.3f) * 0.05f, (slot.Y + 5.3f) * 0.05f);
            var r = l.Restore(PanelId.News);
            Assert.That(l.IsDismissed(PanelId.News), Is.False);
            AssertNoOverlap(l);
            AssertInBounds(r);

            var l2 = SpreadLayout.TraderDefault();
            l2.Dismiss(PanelId.News);
            Assert.That(l2.Restore(PanelId.News), Is.EqualTo(slot));
        }

        [Test] public void DockWidthGrowsWithDismissedPanels()
        {
            Assert.That(SpreadLayout.DockWidthMm(0), Is.EqualTo(174f));
            Assert.That(SpreadLayout.DockWidthMm(1), Is.EqualTo(246f));
            Assert.That(SpreadLayout.DockWidthMm(5), Is.EqualTo(534f));
        }

        [Test] public void KospiIsAnIndex()
        {
            Assert.That(TraderCatalog.Kospi.Kind, Is.EqualTo(InstrumentKind.Index));
            Assert.That(TraderCatalog.Kiwoom.Kind, Is.EqualTo(InstrumentKind.Stock));
            Assert.That(TraderCatalog.Charts.Select(c => c.Code), Is.EqualTo(new[] { "039490", "005930", "000660", "001" }));
            Assert.That(TraderCatalog.OrderableCode, Is.EqualTo("005930"));
        }
    }
}
