using Cryptodd.Okx.Models;
using Cryptodd.Okx.Models.HttpResponse;

namespace Cryptodd.Okx.Orderbooks.Handlers;

public interface IOkxOrderbookHandler
{
    Task Handle<TCollection>(TCollection orderbookResponses, CancellationToken cancellationToken) where TCollection: ICollection<OkxWebsocketOrderbookResponse>;
}