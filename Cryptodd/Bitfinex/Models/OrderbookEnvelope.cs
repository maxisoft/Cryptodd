using Maxisoft.Utils.Collections.Lists.Specialized;

namespace Cryptodd.Bitfinex.Models;

public record OrderbookEnvelope(long Channel, long MessageId, long Time) : IDisposable
{
    public PooledList<PriceCountSizeTuple> Orderbook { get; set; } = new PooledList<PriceCountSizeTuple>();
    public string Symbol { get; set; } = "";

    public void Dispose()
    {
        Orderbook.Dispose();
        GC.SuppressFinalize(this);
    }
}