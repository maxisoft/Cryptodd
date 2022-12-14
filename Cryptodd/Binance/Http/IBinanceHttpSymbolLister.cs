namespace Cryptodd.Binance.Http;

public interface IBinanceHttpSymbolLister
{
    Task<List<string>> ListSymbols(bool useCache = false, bool checkStatus = false,
        CancellationToken cancellationToken = default);
}