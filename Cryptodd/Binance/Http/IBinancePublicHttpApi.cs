using System.Text.Json.Nodes;
using Cryptodd.Binance.Http.Options;
using Cryptodd.Binance.Http.RateLimiter;
using Cryptodd.Binance.Models;
using Maxisoft.Utils.Collections.Lists.Specialized;

namespace Cryptodd.Binance.Http;

public interface IBinanceHttpOrderbookProvider
{
    Task<BinanceHttpOrderbook> GetOrderbook(string symbol, int limit,
        CancellationToken cancellationToken = default);
}

public interface IBinanceHttpKlineProvider
{
    Task<PooledList<BinanceHttpKline>> GetKlines(string symbol,
        string interval = "1m",
        long? startTime = null,
        long? endTime = null,
        int limit = IBinancePublicHttpApi.DefaultKlineLimit,
        BinancePublicHttpApiCallKlinesOptions? options = null,
        CancellationToken cancellationToken = default);

    async Task<PooledList<BinanceHttpKline>> GetCandles(string symbol,
        string interval = "1m",
        long? startTime = null,
        long? endTime = null,
        int limit = IBinancePublicHttpApi.DefaultKlineLimit,
        BinancePublicHttpApiCallKlinesOptions? options = null,
        CancellationToken cancellationToken = default) =>
        await GetKlines(symbol, interval, startTime, endTime, limit, options, cancellationToken);
}

public interface IBinancePublicHttpApi : IBinanceHttpSymbolLister, IBinanceHttpOrderbookProvider,
    IBinanceHttpKlineProvider
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

    Task<BinanceHttpServerTime> GetServerTime(
        BinancePublicHttpApiCallServerTimeOptions? options = null,
        CancellationToken cancellationToken = default);
}