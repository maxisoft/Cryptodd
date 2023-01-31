using System.Collections.Concurrent;
using Cryptodd.Okx.Models;

namespace Cryptodd.Okx.Collectors.Swap;

public interface ISwapDataRepository
{
    ConcurrentDictionary<OkxInstrumentIdentifier, OkxHttpFundingRate> FundingRates { get; }
    ConcurrentDictionary<OkxInstrumentIdentifier, OkxHttpOpenInterest> OpenInterests { get; }
}