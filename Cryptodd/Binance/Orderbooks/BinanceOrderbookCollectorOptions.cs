namespace Cryptodd.Binance.Orderbooks;

public class BinanceOrderbookCollectorOptions
{
    public TimeSpan SymbolsExpiry { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan EntryExpiry { get; set; } = TimeSpan.FromHours(10);

    public bool? FullCleanupOrderbookOnReconnect { get; set; } = true;
}