using System.Collections.Concurrent;
using Cryptodd.IoC;
using Cryptodd.Okx.Models;
using Lamar;

namespace Cryptodd.Okx.Collectors.Options;

[Singleton]
public class OkxOptionsDataRepository : IService, IOkxOptionsDataRepository
{
    public ConcurrentDictionary<OkxInstrumentIdentifier, OkxHttpOpenInterest> OpenInterests { get; } = new();
    public ConcurrentDictionary<OkxInstrumentIdentifier, OkxHttpTickerInfo> Tickers { get; } = new();
}