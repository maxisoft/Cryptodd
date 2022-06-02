using System.Net.WebSockets;
using Cryptodd.IoC;

namespace Cryptodd.Http;

public interface IClientWebSocketFactory : IService
{
    ValueTask<ClientWebSocket> GetWebSocket(Uri uri, CancellationToken cancellationToken = default, bool connect = true);
}