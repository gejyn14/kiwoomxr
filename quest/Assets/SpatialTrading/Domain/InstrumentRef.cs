using System;

namespace SpatialTrading.Domain
{
    /// <summary>Selection identity only. No price, holdings or broker credentials.</summary>
    public sealed class InstrumentRef : IEquatable<InstrumentRef>
    {
        public string Id { get; }
        public string Code { get; }
        public string DisplayName { get; }

        public InstrumentRef(string id, string code, string displayName)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(code) ||
                string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("Instrument identity must be explicit.");
            Id = id;
            Code = code;
            DisplayName = displayName;
        }

        public bool Equals(InstrumentRef other) => other != null && Id == other.Id;
        public override bool Equals(object obj) => Equals(obj as InstrumentRef);
        public override int GetHashCode() => Id.GetHashCode();
    }
}
