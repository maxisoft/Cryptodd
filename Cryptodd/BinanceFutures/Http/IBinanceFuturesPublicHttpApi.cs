using System.Text.Json.Nodes;
using Cryptodd.Binance.Http;
using Cryptodd.Binance.Models;
using Cryptodd.BinanceFutures.Http.Options;
using Cryptodd.BinanceFutures.Http.RateLimiter;

namespace Cryptodd.BinanceFutures.Http;

public interface IBinanceFuturesPublicHttpApi : IBinanceHttpSymbolLister, IBinanceHttpOrderbookProvider
{
    public const int DefaultOrderbookLimit = 500;
    public const int MaxOrderbookLimit = 1000;

    public IBinanceFuturesRateLimiter RateLimiter { get; }

    Task<JsonObject> GetExchangeInfoAsync(BinanceFuturesPublicHttpApiCallExchangeInfoOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<BinanceHttpOrderbook> GetOrderbook(string symbol, int limit = DefaultOrderbookLimit,
        BinanceFuturesPublicHttpApiCallOrderBookOptions? options = null,
        CancellationToken cancellationToken = default);
}