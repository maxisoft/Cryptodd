namespace Cryptodd.Binance.Orderbooks.Handlers;

public interface IBinanceOrderbookHandler
{
    public Task Handle(BinanceOrderbookHandlerArguments arguments, CancellationToken cancellationToken);
}

public interface IBinanceOrderbookHandler<T>
{
    public ValueTask<T> Handle(BinanceOrderbookHandlerArguments arguments, CancellationToken cancellationToken);
}