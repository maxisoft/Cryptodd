using Maxisoft.Utils.Collections.Lists.Specialized;


namespace Cryptodd.Binance.Models;
// ReSharper disable InconsistentNaming
public sealed record DepthUpdateMessage(string e, long E, long T, string s, long U, long u, long pu,
        PooledList<BinancePriceQuantityEntry<double>> b, PooledList<BinancePriceQuantityEntry<double>> a) : IDisposable
    // ReSharper restore InconsistentNaming
{
    # region Aliases

    public string EventType => e;

    public long Time => E;

    public long FirstUpdateId => U;
    public long FinalUpdateId => u;

    public long? TransactionTime => T is 0 or long.MinValue ? null : T;

    public DateTimeOffset DateTimeOffset => DateTimeOffset.FromUnixTimeMilliseconds(E);

    public string Symbol => s;

    public long? PreviousUpdateId => pu is 0 or long.MinValue ? null : pu;

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