using MathNet.Numerics.Statistics;

namespace Cryptodd.Binance.Orderbook;

public record OrderBookEntryWithStat(double Price) : OrderBookEntry(Price), IOrderBookEntry
{
    public RunningStatistics? Statistics { get; protected set; } = null;

    public new void ResetStatistics()
    {
        base.ResetStatistics();
        Statistics = null;
    }

    void IOrderBookEntry.ResetStatistics()
    {
        ResetStatistics();
    }

    public new void Update(double qty, DateTimeOffset time, long updateId)
    {
        base.Update(qty, time, updateId);
        Statistics ??= new RunningStatistics();
        Statistics.Push(qty);
    }

    void IOrderBookEntry.Update(double qty, DateTimeOffset time, long updateId)
    {
        Update(qty, time, updateId);
    }
}