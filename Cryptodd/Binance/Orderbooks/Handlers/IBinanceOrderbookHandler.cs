namespace Cryptodd.Binance.Orderbooks.Handlers;

public interface IBinanceOrderbookHandler : IBaseBinanceOrderbookHandler
{
}

public interface IBinanceOrderbookHandler<T>
{
    public ValueTask<T> Handle(BinanceOrderbookHandlerArguments arguments, CancellationToken cancellationToken);
}