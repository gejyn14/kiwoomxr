using System.Linq;
using NUnit.Framework;
using SpatialTrading.Domain;

namespace SpatialTrading.Tests
{
    public sealed class SpreadLayoutTests
    {
        static readonly PanelId[] All =
        {
            PanelId.ChartKiwoom, PanelId.ChartSamsung, PanelId.ChartSkHynix, PanelId.ChartKospi,
            PanelId.OrderBook, PanelId.News, PanelId.Balance, PanelId.Order,
        };

        static void AssertInBounds(PanelRect r)
        {
            int minX = r.Plane == Plane.Eye ? -20 : -10, maxX = r.Plane == Plane.Eye ? 19 : 9;
            int minY = r.Plane == Plane.Eye ? -6 : -3, maxY = r.Plane == Plane.Eye ? 5 : 2;
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

        [Test] public void TraderDefaultPlacesEveryPanelWithoutOverlap()
        {
            var l = SpreadLayout.TraderDefault();
            AssertNoOverlap(l);
            foreach (var p in All) AssertInBounds(l[p]);
            foreach (var p in new[] { PanelId.ChartKiwoom, PanelId.ChartSamsung, PanelId.ChartSkHynix, PanelId.ChartKospi, PanelId.OrderBook, PanelId.News })
                Assert.That(l[p].Plane, Is.EqualTo(Plane.Eye));
            Assert.That(l[PanelId.Balance].Plane, Is.EqualTo(Plane.Hand));
            Assert.That(l[PanelId.Order].Plane, Is.EqualTo(Plane.Hand));
            Assert.That(l[PanelId.OrderBook], Is.EqualTo(new PanelRect(Plane.Eye, -4, -5, 9, 11)));
        }

        [Test] public void DropSnapsToNearestCell()
        {
            var l = SpreadLayout.TraderDefault();
            var r = l.Drop(PanelId.News, Plane.Eye, 0.62f, 0.02f);
            Assert.That(r.X, Is.EqualTo(9));
            Assert.That(r.Y, Is.EqualTo(-5));
            Assert.That(l[PanelId.News], Is.EqualTo(r));
        }

        [Test] public void DropOutsideBoundsIsPulledBackInside()
        {
            var l = SpreadLayout.TraderDefault();
            AssertInBounds(l.Drop(PanelId.News, Plane.Eye, 5f, 3f));
            AssertInBounds(l.Drop(PanelId.News, Plane.Eye, -5f, -3f));
            AssertInBounds(l.Drop(PanelId.Balance, Plane.Hand, 5f, 3f));
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
            var small = l.Resize(PanelId.ChartSamsung, 1, 1);
            Assert.That(small.Width, Is.EqualTo(4));
            Assert.That(small.Height, Is.EqualTo(3));
            var big = l.Resize(PanelId.ChartSamsung, 40, 40);
            Assert.That(big.Width, Is.LessThanOrEqualTo(16));
            Assert.That(big.Height, Is.LessThanOrEqualTo(10));
            AssertInBounds(big);
            AssertNoOverlap(l);
        }

        [Test] public void ResizeNeverLeavesAnOverlap()
        {
            var l = SpreadLayout.TraderDefault();
            l.Resize(PanelId.ChartKiwoom, 8, 6);
            AssertNoOverlap(l);
            var before = l[PanelId.Order];
            var after = l.Resize(PanelId.Order, 12, 7);
            Assert.That(after, Is.EqualTo(before));
            Assert.That(l[PanelId.Order], Is.EqualTo(before));
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

        [Test] public void ResetRestoresDismissedAndMovedPanels()
        {
            var l = SpreadLayout.TraderDefault();
            var d = SpreadLayout.TraderDefault();
            l.Dismiss(PanelId.Order);
            l.Drop(PanelId.OrderBook, Plane.Eye, 0.3f, 0.1f);
            l.Reset();
            Assert.That(l[PanelId.Order], Is.EqualTo(d[PanelId.Order]));
            Assert.That(l[PanelId.OrderBook], Is.EqualTo(d[PanelId.OrderBook]));
            Assert.That(l.Dismissed, Is.Empty);
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

        [Test] public void DetailFollowsSize()
        {
            Assert.That(SpreadLayout.ChartDetailFor(new PanelRect(Plane.Eye, 0, 0, 4, 3)), Is.EqualTo(ChartDetail.Line));
            Assert.That(SpreadLayout.ChartDetailFor(new PanelRect(Plane.Eye, 0, 0, 7, 5)), Is.EqualTo(ChartDetail.Candles));
            Assert.That(SpreadLayout.ChartDetailFor(new PanelRect(Plane.Eye, 0, 0, 12, 8)), Is.EqualTo(ChartDetail.CandlesWithAxes));
            Assert.That(SpreadLayout.BookDepthFor(new PanelRect(Plane.Eye, 0, 0, 9, 11)), Is.EqualTo(10));
            Assert.That(SpreadLayout.BookDepthFor(new PanelRect(Plane.Eye, 0, 0, 9, 8)), Is.EqualTo(5));
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
