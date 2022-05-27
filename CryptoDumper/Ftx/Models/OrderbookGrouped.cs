using Maxisoft.Utils.Collections.Lists;

namespace CryptoDumper.Ftx.Models;

public readonly record struct GroupedOrderbook(PriceSizePair[] Bids, PriceSizePair[] Asks)
{
    public static readonly GroupedOrderbook Empty =
        new GroupedOrderbook(Array.Empty<PriceSizePair>(), Array.Empty<PriceSizePair>());
}