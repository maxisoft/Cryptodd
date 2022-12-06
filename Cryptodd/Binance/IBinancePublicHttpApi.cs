using System.Text.Json.Nodes;
using Cryptodd.Binance.Models;

namespace Cryptodd.Binance;

public interface IBinancePublicHttpApi
{
    Task<JsonObject> GetExchangeInfoAsync(BinancePublicHttpApiCallOptionsExchangeInfo? options = null,
        CancellationToken cancellationToken = default);

    Task<BinanceHttpOrderbook> GetOrderbook(string symbol, int limit = BinancePublicHttpApi.DefaultOrderbookLimit,
        BinancePublicHttpApiCallOptionsOrderBook? options = null,
        CancellationToken cancellationToken = default);
}