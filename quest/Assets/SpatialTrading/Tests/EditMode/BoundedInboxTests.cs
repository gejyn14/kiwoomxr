using System;
using NUnit.Framework;
using SpatialTrading.Market;

namespace SpatialTrading.Tests
{
    public sealed class BoundedInboxTests
    {
        [Test] public void FullInboxDropsTheOldestItems()
        {
            var inbox = new BoundedInbox<int>(3);
            for (var i = 0; i < 5; i++) inbox.Enqueue(i);
            Assert.That(inbox.Count, Is.EqualTo(3));
            for (var expected = 2; expected < 5; expected++)
            {
                Assert.That(inbox.TryDequeue(out var item), Is.True);
                Assert.That(item, Is.EqualTo(expected));
            }
            Assert.That(inbox.TryDequeue(out _), Is.False);
        }

        [Test] public void ClearEmptiesTheInbox()
        {
            var inbox = new BoundedInbox<string>(2);
            inbox.Enqueue("a");
            inbox.Clear();
            Assert.That(inbox.Count, Is.Zero);
            Assert.That(inbox.TryDequeue(out _), Is.False);
        }

        [Test] public void CapacityMustBePositive() =>
            Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedInbox<int>(0));
    }
}
