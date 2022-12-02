using Maxisoft.Utils.Collections.Lists.Specialized;


namespace Cryptodd.Binance.Models;
// ReSharper disable InconsistentNaming
public record DepthUpdateMessage(string e, long E, string s, long U, long u,
        PooledList<BinancePriceQuantityEntry<double>> b, PooledList<BinancePriceQuantityEntry<double>> a) : IDisposable
    // ReSharper restore InconsistentNaming
{
    # region Aliases

    public string EventType => e;

    public long Time => E;

    public DateTimeOffset DateTimeOffset => DateTimeOffset.FromUnixTimeMilliseconds(E);

    public string Symbol => s;

    public PooledList<BinancePriceQuantityEntry<double>> Bids => b;

    public PooledList<BinancePriceQuantityEntry<double>> Asks => a;

    #endregion

    public void Dispose()
    {
        b.Dispose();
        a.Dispose();
        GC.SuppressFinalize(this);
    }
}