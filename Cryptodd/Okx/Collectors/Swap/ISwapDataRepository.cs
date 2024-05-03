using System.Collections.Concurrent;
using Cryptodd.Okx.Models;

namespace Cryptodd.Okx.Collectors.Swap;

public interface ISwapDataRepository
{
    ConcurrentDictionary<OkxInstrumentIdentifier, OkxHttpFundingRate> FundingRates { get; }
    ConcurrentDictionary<OkxInstrumentIdentifier, OkxHttpOpenInterest> OpenInterests { get; }
    ConcurrentDictionary<OkxInstrumentIdentifier, OkxHttpTickerInfo> Tickers { get; }

    ConcurrentDictionary<OkxInstrumentIdentifier, OkxHttpMarkPrice> MarkPrices { get; }
}