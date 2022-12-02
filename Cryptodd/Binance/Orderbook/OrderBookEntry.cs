namespace Cryptodd.Binance.Orderbook;

public record OrderBookEntry(double Price) : IOrderBookEntry
{
    public double Quantity { get; set; }

    public DateTimeOffset Time { get; set; }
    public int ChangeCounter { get; protected set; }

    public void ResetStatistics()
    {
        ChangeCounter = 0;
    }

    public void Update(double qty, DateTimeOffset time)
    {
        Time = time;
        Quantity = qty;
        ChangeCounter += 1;
    }
}