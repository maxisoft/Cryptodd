namespace Cryptodd.Binance.Orderbooks.Handlers;

public interface IBaseBinanceOrderbookHandler
{
    public Task Handle(BinanceOrderbookHandlerArguments arguments, CancellationToken cancellationToken);
}