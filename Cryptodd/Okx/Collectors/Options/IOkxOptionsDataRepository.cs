using System.Collections.Concurrent;
using Cryptodd.Okx.Models;

namespace Cryptodd.Okx.Collectors.Options;

public interface IOkxOptionsDataRepository
{
    ConcurrentDictionary<OkxInstrumentIdentifier, OkxHttpOpenInterest> OpenInterests { get; }
    ConcurrentDictionary<OkxInstrumentIdentifier, OkxHttpTickerInfo> Tickers { get; }
}