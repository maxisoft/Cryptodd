using System.Text.Json.Nodes;
using Cryptodd.Binance.Http;
using Cryptodd.Binance.Models;
using Cryptodd.BinanceFutures.Http.Options;
using Cryptodd.BinanceFutures.Http.RateLimiter;

namespace Cryptodd.BinanceFutures.Http;

public interface IBinanceFuturesPublicHttpApi : IBinanceHttpSymbolLister, IBinanceHttpOrderbookProvider,
    IBinanceFuturesHttpKlineProvider
{
    public const int DefaultOrderbookLimit = 500;
    public const int MaxOrderbookLimit = 1000;
    public const int DefaultKlineLimit = 500;
    public const int OptimalKlineLimit = 499;
    public const int MaxKlineLimit = 1500;

    public IBinanceFuturesRateLimiter RateLimiter { get; }

    Task<JsonObject> GetExchangeInfoAsync(BinanceFuturesPublicHttpApiCallExchangeInfoOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<BinanceHttpOrderbook> GetOrderbook(string symbol, int limit = DefaultOrderbookLimit,
        BinanceFuturesPublicHttpApiCallOrderBookOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<BinanceHttpServerTime> GetServerTime(
        BinanceFuturesPublicHttpApiCallServerTimeOptions? options = null,
        CancellationToken cancellationToken = default);
}