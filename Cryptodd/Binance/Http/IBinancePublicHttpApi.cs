using System.Text.Json.Nodes;
using Cryptodd.Binance.Http.Options;
using Cryptodd.Binance.Http.RateLimiter;
using Cryptodd.Binance.Models;

namespace Cryptodd.Binance.Http;

public interface IBinanceHttpOrderbookProvider
{
    Task<BinanceHttpOrderbook> GetOrderbook(string symbol, int limit,
        CancellationToken cancellationToken = default);
}

public interface IBinancePublicHttpApi : IBinanceHttpSymbolLister, IBinanceHttpOrderbookProvider
{
    public const int DefaultOrderbookLimit = 100;
    public const int MaxOrderbookLimit = 5000;
    public const int DefaultKlineLimit = 500;
    public const int MaxKlineLimit = 1000;

    public IBinanceRateLimiter RateLimiter { get; }

    Task<JsonObject> GetExchangeInfoAsync(BinancePublicHttpApiCallExchangeInfoOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<BinanceHttpOrderbook> GetOrderbook(string symbol, int limit = DefaultOrderbookLimit,
        BinancePublicHttpApiCallOrderBookOptions? options = null,
        CancellationToken cancellationToken = default);
}