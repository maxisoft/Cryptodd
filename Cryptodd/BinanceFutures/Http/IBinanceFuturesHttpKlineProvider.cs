using Cryptodd.Binance.Http;
using Cryptodd.Binance.Models;
using Cryptodd.BinanceFutures.Http.Options;
using Maxisoft.Utils.Collections.Lists.Specialized;

namespace Cryptodd.BinanceFutures.Http;

public interface IBinanceFuturesHttpKlineProvider
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="symbol">The symbol for the kline data.</param>
    /// <param name="interval">The interval for the kline data.</param>
    /// <param name="startTime">The start time for the kline data.</param>
    /// <param name="endTime">The end time for the kline data.</param>
    /// <param name="limit">The number of klines to return. The default is the maximum number of klines that can be returned by the API.</param>
    /// <param name="options">The options for the API call.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the request.</param>
    /// <returns>A PooledList containing the kline data.</returns>
    Task<PooledList<BinanceHttpKline>> GetKlines(string symbol,
        string interval = "1m",
        long? startTime = null,
        long? endTime = null,
        int limit = IBinancePublicHttpApi.MaxKlineLimit,
        BinanceFuturesPublicHttpApiCallKlinesOptions? options = null,
        CancellationToken cancellationToken = default);

    async Task<PooledList<BinanceHttpKline>> GetCandles(string symbol,
        string interval = "1m",
        long? startTime = null,
        long? endTime = null,
        int limit = IBinancePublicHttpApi.DefaultKlineLimit,
        BinanceFuturesPublicHttpApiCallKlinesOptions? options = null,
        CancellationToken cancellationToken = default) =>
        await GetKlines(symbol, interval, startTime, endTime, limit, options, cancellationToken).ConfigureAwait(false);
}