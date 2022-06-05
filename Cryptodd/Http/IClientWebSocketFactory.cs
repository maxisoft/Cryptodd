using System.Net.WebSockets;
using Cryptodd.IoC;

namespace Cryptodd.Http;

public interface IClientWebSocketFactory : IService
{
    ValueTask<ClientWebSocket> GetWebSocket(Uri uri,
        bool connect = true, CancellationToken cancellationToken = default);
}