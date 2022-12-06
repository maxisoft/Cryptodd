namespace Cryptodd.Binance.Orderbook;

public record OrderBookEntry(double Price) : IOrderBookEntry
{
    public double Quantity { get; set; }

    public DateTimeOffset Time { get; set; }

    public long UpdateId { get; set; } = long.MinValue;
    
    public int ChangeCounter { get; protected set; }

    public void ResetStatistics()
    {
        ChangeCounter = 0;
    }

    public void Update(double qty, DateTimeOffset time, long updateId)
    {
        if (updateId < UpdateId)
        {
            throw new ArgumentOutOfRangeException(nameof(updateId), updateId, "trying to update entry to an older version");
        }
        UpdateId = updateId;
        Time = time;
        Quantity = qty;
        ChangeCounter += 1;
    }
}