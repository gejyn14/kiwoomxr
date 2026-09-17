using System;
using System.Collections.Generic;

namespace SpatialTrading.Domain
{
    /// <summary>M1's explicit synthetic catalog. Not brokerage lookup or financial authority.</summary>
    public static class SyntheticCatalog
    {
        public static readonly InstrumentRef Samsung = new InstrumentRef("shell:005930", "005930", "삼성전자");
        public static readonly InstrumentRef SkHynix = new InstrumentRef("shell:000660", "000660", "SK하이닉스");
        public static IReadOnlyList<InstrumentRef> Instruments { get; } =
            Array.AsReadOnly(new[] { Samsung, SkHynix });

        // Arbitrary fixture values, deliberately static. Never called LIVE or used for orders.
        public static decimal ReferencePrice(InstrumentRef instrument)
        {
            if (Samsung.Equals(instrument)) return 71200m;
            if (SkHynix.Equals(instrument)) return 182000m;
            throw new ArgumentException("No synthetic fixture for this instrument.", nameof(instrument));
        }
    }
}
