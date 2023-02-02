using System.Collections.Concurrent;
using Cryptodd.IoC;
using Cryptodd.Okx.Models;
using Lamar;

namespace Cryptodd.Okx.Collectors.Swap;

public interface IOptionsDataRepository
{
    ConcurrentDictionary<OkxInstrumentIdentifier, OkxHttpOpenInterest> OpenInterests { get; }
    ConcurrentDictionary<OkxInstrumentIdentifier, OkxHttpTickerInfo> Tickers { get; }
}

[Singleton]
public class OptionsDataRepository : IService, IOptionsDataRepository
{
    public ConcurrentDictionary<OkxInstrumentIdentifier, OkxHttpOpenInterest> OpenInterests { get; }
    public ConcurrentDictionary<OkxInstrumentIdentifier, OkxHttpTickerInfo> Tickers { get; }
}