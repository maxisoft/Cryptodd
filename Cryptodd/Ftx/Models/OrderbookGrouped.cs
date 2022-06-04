using Maxisoft.Utils.Collections.Lists.Specialized;

namespace Cryptodd.Ftx.Models;

public readonly record struct GroupedOrderbook(PooledList<PriceSizePair> Bids, PooledList<PriceSizePair> Asks)
{
    public static readonly GroupedOrderbook Empty = new(new PooledList<PriceSizePair>(), new PooledList<PriceSizePair>());
}