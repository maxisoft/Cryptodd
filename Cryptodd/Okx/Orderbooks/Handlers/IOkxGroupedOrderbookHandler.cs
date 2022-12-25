namespace Cryptodd.Okx.Orderbooks.Handlers;

public interface IOkxGroupedOrderbookHandler
{
    Task Handle(OkxAggregatedOrderbookHandlerArguments arguments, CancellationToken cancellationToken);
}