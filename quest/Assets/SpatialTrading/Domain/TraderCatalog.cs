using System.Collections.Generic;

namespace SpatialTrading.Domain
{
    /// <summary>The fixed instruments of the trader spread. Ids are the broker codes.</summary>
    public static class TraderCatalog
    {
        public const string OrderableCode = "005930";

        public static readonly InstrumentRef Kiwoom = new InstrumentRef("039490", "039490", "키움증권");
        public static readonly InstrumentRef Samsung = new InstrumentRef("005930", "005930", "삼성전자");
        public static readonly InstrumentRef SkHynix = new InstrumentRef("000660", "000660", "SK하이닉스");
        public static readonly InstrumentRef Kospi = new InstrumentRef("001", "001", "코스피", InstrumentKind.Index);

        public static readonly IReadOnlyList<InstrumentRef> Charts = new[] { Kiwoom, Samsung, SkHynix, Kospi };
    }
}
