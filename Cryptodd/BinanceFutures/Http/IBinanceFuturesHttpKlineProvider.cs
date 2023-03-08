using Cryptodd.Binance.Http;
using Cryptodd.Binance.Models;
using Cryptodd.BinanceFutures.Http.Options;
using Maxisoft.Utils.Collections.Lists.Specialized;

namespace Cryptodd.BinanceFutures.Http;

public interface IBinanceFuturesHttpKlineProvider
{
    Task<PooledList<BinanceHttpKline>> GetKlines(string symbol,
        string interval = "1m",
        long? startTime = null,
        long? endTime = null,
        int limit = IBinancePublicHttpApi.DefaultKlineLimit,
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