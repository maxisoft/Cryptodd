using Cryptodd.Okx.Models;

namespace Cryptodd.Okx.Orderbooks.Handlers;

public interface IOkxOrderbookAggregator
{
    public ValueTask<OkxAggregatedOrderbookHandlerArguments> Handle(OkxWebsocketOrderbookResponse response,
        CancellationToken cancellationToken);
}