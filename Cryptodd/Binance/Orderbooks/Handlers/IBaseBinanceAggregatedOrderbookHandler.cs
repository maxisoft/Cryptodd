namespace Cryptodd.Binance.Orderbooks.Handlers;

public interface IBaseBinanceAggregatedOrderbookHandler
{
    public Task Handle(BinanceAggregatedOrderbookHandlerArguments arguments, CancellationToken cancellationToken);
}