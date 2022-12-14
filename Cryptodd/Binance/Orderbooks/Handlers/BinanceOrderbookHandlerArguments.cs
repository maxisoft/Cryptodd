namespace Cryptodd.Binance.Orderbooks.Handlers;

public record BinanceOrderbookHandlerArguments(
    string Symbol,
    InMemoryOrderbook<OrderBookEntryWithStat>.SortedView Asks,
    InMemoryOrderbook<OrderBookEntryWithStat>.SortedView Bids
)
{
    public InMemoryOrderbook<OrderBookEntryWithStat> Orderbook => Asks.Orderbook;
}