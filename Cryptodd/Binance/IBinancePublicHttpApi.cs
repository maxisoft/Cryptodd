using System.Text.Json.Nodes;
using Cryptodd.Binance.Models;
using Cryptodd.Binance.RateLimiter;
using Maxisoft.Utils.Collections.Lists;

namespace Cryptodd.Binance;

public interface IBinancePublicHttpApi
{
    public IBinanceRateLimiter RateLimiter { get; }
    Task<JsonObject> GetExchangeInfoAsync(BinancePublicHttpApiCallOptionsExchangeInfo? options = null,
        CancellationToken cancellationToken = default);

    Task<BinanceHttpOrderbook> GetOrderbook(string symbol, int limit = BinancePublicHttpApi.DefaultOrderbookLimit,
        BinancePublicHttpApiCallOptionsOrderBook? options = null,
        CancellationToken cancellationToken = default);

    Task<List<string>> ListSymbols(bool useCache = false, bool checkStatus = false,
        CancellationToken cancellationToken = default);
}