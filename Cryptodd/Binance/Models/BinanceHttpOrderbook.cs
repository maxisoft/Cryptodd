using Maxisoft.Utils.Collections.Lists.Specialized;

namespace Cryptodd.Binance.Models;
/***
A BinanceHttpOrderbook represents the state of a Binance exchange order book at a given point in time.
The order book has 
BinancePriceQuantityEntry<double>
s for each price level and each price level has one or more bids and asks.
The order book is updated with new bids and asks when trades are executed on the exchange.
*/
public readonly record struct BinanceHttpOrderbook(long LastUpdateId, PooledList<BinancePriceQuantityEntry<double>> Bids, PooledList<BinancePriceQuantityEntry<double>> Asks, DateTimeOffset? DateTime = null) : IDisposable
{
    public void Dispose()
    {
        Bids.Dispose();
        Asks.Dispose();
    }
}