using Cryptodd.Okx.Models;

namespace Cryptodd.Okx.Orderbooks.Handlers;

public interface IOkxOrderbookHandler
{
    Task Handle<TCollection>(TCollection orderbookResponses, CancellationToken cancellationToken) where TCollection: ICollection<OkxWebsocketOrderbookResponse>;
}