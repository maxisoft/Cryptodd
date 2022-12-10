using Cryptodd.OrderBooks;

namespace Cryptodd.Binance.Orderbook.Handlers;

public record BinanceAggregatedOrderbookHandlerArguments(
    string Symbol,
    DateTimeOffset DateTime,
    DetailedOrderbookEntryFloatTuple[] Asks,
    DetailedOrderbookEntryFloatTuple[] Bids,
    InMemoryOrderbook<OrderBookEntryWithStat>.SortedView RawAsks,
    InMemoryOrderbook<OrderBookEntryWithStat>.SortedView RawBids
)
{
    public InMemoryOrderbook<OrderBookEntryWithStat> Orderbook => RawAsks.Orderbook;
}