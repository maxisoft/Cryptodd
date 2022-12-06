namespace Cryptodd.Binance.Orderbook;

public interface IOrderBookEntry
{
    double Price { get; init; }

    DateTimeOffset Time { get; set; }
    
    /// <summary>
    /// Internal <a href="https://binance-docs.github.io/apidocs/spot/en/#how-to-manage-a-local-order-book-correctly">Binance Update Identifier</a>.
    /// </summary>
    long UpdateId { get; set; }
    
    double Quantity { get; set; }
    int ChangeCounter { get; }

    void ResetStatistics();
    void Update(double qty, DateTimeOffset time, long updateId);
}