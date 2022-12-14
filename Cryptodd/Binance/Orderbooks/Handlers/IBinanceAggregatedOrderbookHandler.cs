namespace Cryptodd.Binance.Orderbooks.Handlers;

public interface IBinanceAggregatedOrderbookHandler
{
    public Task Handle(BinanceAggregatedOrderbookHandlerArguments arguments, CancellationToken cancellationToken);
}