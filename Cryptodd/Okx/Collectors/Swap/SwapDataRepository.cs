using System.Collections.Concurrent;
using Cryptodd.IoC;
using Cryptodd.Okx.Models;
using Lamar;

namespace Cryptodd.Okx.Collectors.Swap;

[Singleton]
// ReSharper disable once UnusedType.Global
public class SwapDataRepository : IService, ISwapDataRepository
{
    public ConcurrentDictionary<OkxInstrumentIdentifier, OkxHttpFundingRate> FundingRates { get; } = new();

    public ConcurrentDictionary<OkxInstrumentIdentifier, OkxHttpOpenInterest> OpenInterests { get; } = new();
    public ConcurrentDictionary<OkxInstrumentIdentifier, OkxHttpTickerInfo> Tickers { get; } = new();
    public ConcurrentDictionary<OkxInstrumentIdentifier, OkxHttpMarkPrice> MarkPrices { get; } = new();
}