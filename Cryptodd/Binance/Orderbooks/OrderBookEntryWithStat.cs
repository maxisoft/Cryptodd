using System.Diagnostics;
using MathNet.Numerics.Statistics;

namespace Cryptodd.Binance.Orderbooks;

public record OrderBookEntryWithStat(double Price) : IOrderBookEntry
{
    public OrderBookEntryWithStat() : this(0) { }

    public double Quantity { get; set; }

    public DateTimeOffset Time { get; set; }

    public long UpdateId { get; set; } = long.MinValue;

    public int ChangeCounter { get; set; }

    public RunningStatistics? Statistics { get; protected set; }

    public void ResetStatistics()
    {
        var that = this;
        IOrderBookEntry.DoResetStatistics(ref that);
        Debug.Assert(ReferenceEquals(this, that));
        Statistics = null;
    }

    public void Update(double qty, DateTimeOffset time, long updateId)
    {
        var that = this;
        IOrderBookEntry.DoUpdate(ref that, qty, time, updateId);
        Debug.Assert(ReferenceEquals(this, that));
        Statistics ??= new RunningStatistics();
        Statistics.Push(qty);
    }
}