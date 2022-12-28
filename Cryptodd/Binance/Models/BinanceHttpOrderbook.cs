using Maxisoft.Utils.Collections.Lists.Specialized;

namespace Cryptodd.Binance.Models;

public readonly record struct BinanceHttpOrderbook(long LastUpdateId, PooledList<BinancePriceQuantityEntry<double>> Bids, PooledList<BinancePriceQuantityEntry<double>> Asks, DateTimeOffset? DateTime = null) : IDisposable
{
    public void Dispose()
    {
        Bids.Dispose();
        Asks.Dispose();
    }
}