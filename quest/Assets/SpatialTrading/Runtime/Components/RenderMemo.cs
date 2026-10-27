using System;
using System.Collections.Generic;
using SpatialTrading.Domain;

namespace SpatialTrading.Components
{
    /// <summary>Helpers for <see cref="RenderMemo{TKey}"/> inputs. Pure: no Unity API.</summary>
    public static class RenderMemo
    {
        /// <summary>
        /// Render identity of an observation: its value reference, state, reason and origin. Observations and their values
        /// are immutable, so the same value reference means the same data; an overlay copy (same value, same status) made
        /// afresh on every read compares equal, while any status change does not.
        /// </summary>
        public static object Of<T>(Observation<T> o) where T : class =>
            o == null ? null : (object)Tuple.Create<object, DataState, string, DataOrigin>(o.Value, o.State, o.Reason, o.Origin);
    }

    /// <summary>
    /// Remembers the inputs of each panel's last render, so a stream change redraws only the panels whose inputs moved.
    /// Inputs compare with <see cref="object.Equals(object, object)"/>: value types and strings by value, data by reference
    /// (wrap observations with <see cref="RenderMemo.Of{T}"/>). Pure: no Unity API.
    /// </summary>
    public sealed class RenderMemo<TKey>
    {
        private readonly Dictionary<TKey, object[]> _last = new Dictionary<TKey, object[]>();

        public bool Unchanged(TKey key, params object[] inputs)
        {
            if (!_last.TryGetValue(key, out var previous) || previous.Length != inputs.Length) return false;
            for (var i = 0; i < inputs.Length; i++)
                if (!Equals(previous[i], inputs[i])) return false;
            return true;
        }

        public void Remember(TKey key, params object[] inputs) => _last[key] = (object[])inputs.Clone();

        public void Forget(TKey key) => _last.Remove(key);

        public void Clear() => _last.Clear();
    }
}
