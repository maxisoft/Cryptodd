namespace Cryptodd.Binance.Orderbook;

public interface IOrderBookEntry
{
    double Price { get; init; }

    DateTimeOffset Time { get; set; }
    double Quantity { get; set; }
    int ChangeCounter { get; }

    void ResetStatistics();
    void Update(double qty, DateTimeOffset time);
}