using Cryptodd.Okx.Models;
using Cryptodd.Okx.Options;

namespace Cryptodd.Okx.Collectors.Options;

public class OkxOptionDataContext : Tuple<OkxOptionInstrumentId, OkxHttpOpenInterest, OkxHttpTickerInfo,
    OkxHttpOptionSummary,
    OkxHttpInstrumentInfo>
{
    public OkxOptionDataContext(OkxOptionInstrumentId item1, OkxHttpOpenInterest item2, OkxHttpTickerInfo item3, OkxHttpOptionSummary item4, OkxHttpInstrumentInfo item5) : base(item1, item2, item3, item4, item5) { }
}