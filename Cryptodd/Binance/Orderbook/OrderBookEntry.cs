namespace Cryptodd.Binance.Orderbook;

public record OrderBookEntry(double Price) : IOrderBookEntry
{
    public double Quantity { get; set; }

    public DateTimeOffset Time { get; set; }

    public long UpdateId { get; set; } = long.MinValue;
    
    public int ChangeCounter { get; set; }

    public void ResetStatistics()
    {
        var that = this;
        IOrderBookEntry.DoResetStatistics(ref that);
    }

    public void Update(double qty, DateTimeOffset time, long updateId)
    {
        var that = this;
        IOrderBookEntry.DoUpdate(ref that, qty, time, updateId);
    }
}