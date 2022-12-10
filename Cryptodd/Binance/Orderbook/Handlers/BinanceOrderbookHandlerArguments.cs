namespace Cryptodd.Binance.Orderbook.Handlers;

public record BinanceOrderbookHandlerArguments(
    string Symbol,
    InMemoryOrderbook<OrderBookEntryWithStat>.SortedView Asks,
    InMemoryOrderbook<OrderBookEntryWithStat>.SortedView Bids
)
{
    public InMemoryOrderbook<OrderBookEntryWithStat> Orderbook => Asks.Orderbook;
}