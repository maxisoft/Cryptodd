namespace Cryptodd.Binance.Orderbook.Handlers;

public interface IBinanceAggregatedOrderbookHandler
{
    public Task Handle(BinanceAggregatedOrderbookHandlerArguments arguments, CancellationToken cancellationToken);
}