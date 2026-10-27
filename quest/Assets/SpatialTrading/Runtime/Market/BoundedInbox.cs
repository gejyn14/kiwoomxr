using System;
using System.Collections.Generic;

namespace SpatialTrading.Market
{
    /// <summary>
    /// Thread-safe FIFO that drops its oldest item when full. The gateway sends full state per
    /// capability, so an older message is always superseded by a newer one. Pure: no Unity API.
    /// </summary>
    public sealed class BoundedInbox<T>
    {
        private readonly Queue<T> _items = new Queue<T>();
        private readonly int _capacity;

        public BoundedInbox(int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
        }

        public int Count { get { lock (_items) return _items.Count; } }

        public void Enqueue(T item)
        {
            lock (_items)
            {
                if (_items.Count == _capacity) _items.Dequeue();
                _items.Enqueue(item);
            }
        }

        public bool TryDequeue(out T item)
        {
            lock (_items)
            {
                if (_items.Count == 0) { item = default; return false; }
                item = _items.Dequeue();
                return true;
            }
        }

        public void Clear() { lock (_items) _items.Clear(); }
    }
}
