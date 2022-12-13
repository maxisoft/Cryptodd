using System.Text.Json.Nodes;
using Cryptodd.Binance.Models;
using Cryptodd.Binance.RateLimiter;
using Maxisoft.Utils.Collections.Lists;

namespace Cryptodd.Binance;

public interface IBinancePublicHttpApi
{
    public const int DefaultOrderbookLimit = 100;
    public const int MaxOrderbookLimit = 5000;
    
    public IBinanceRateLimiter RateLimiter { get; }
    Task<JsonObject> GetExchangeInfoAsync(BinancePublicHttpApiCallOptionsExchangeInfo? options = null,
        CancellationToken cancellationToken = default);

    Task<BinanceHttpOrderbook> GetOrderbook(string symbol, int limit = DefaultOrderbookLimit,
        BinancePublicHttpApiCallOptionsOrderBook? options = null,
        CancellationToken cancellationToken = default);

    Task<List<string>> ListSymbols(bool useCache = false, bool checkStatus = false,
        CancellationToken cancellationToken = default);
}