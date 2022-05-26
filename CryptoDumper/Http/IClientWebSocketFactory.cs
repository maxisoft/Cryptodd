using System.Net.WebSockets;
using CryptoDumper.IoC;

namespace CryptoDumper.Http;

public interface IClientWebSocketFactory : IService
{
    ValueTask<ClientWebSocket> GetWebSocket(Uri uri, CancellationToken cancellationToken = default, bool connect = true);
}