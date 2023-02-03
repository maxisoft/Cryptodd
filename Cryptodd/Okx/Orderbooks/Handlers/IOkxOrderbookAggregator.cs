using Cryptodd.Okx.Models;
using Cryptodd.Okx.Models.HttpResponse;

namespace Cryptodd.Okx.Orderbooks.Handlers;

public interface IOkxOrderbookAggregator
{
    public ValueTask<OkxAggregatedOrderbookHandlerArguments> Handle(OkxWebsocketOrderbookResponse response,
        CancellationToken cancellationToken);
}