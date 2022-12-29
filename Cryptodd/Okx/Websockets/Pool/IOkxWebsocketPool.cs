using Cryptodd.Okx.Models;

namespace Cryptodd.Okx.Websockets.Pool;

public interface IOkxWebsocketPool
{
    public int Count { get; }
    Task BackgroundLoop(CancellationToken cancellationToken);

    ValueTask<bool> TryInjectWebsocket<T, TData2, TOptions2>(T other, CancellationToken cancellationToken)
        where T : BaseOkxWebsocket<TData2, TOptions2>
        where TData2 : PreParsedOkxWebSocketMessage, new()
        where TOptions2 : BaseOkxWebsocketOptions, new();

    Task Tick(CancellationToken cancellationToken);
}