using Cryptodd.Okx.Models;

namespace Cryptodd.Okx.Websockets.Pool;

public static class OkxWebsocketPoolExtensions
{
    public static async ValueTask<bool> TryInjectWebsocket<T>(this T pool,
        OkxWebsocketForOrderbook other, CancellationToken cancellationToken) where T : IOkxWebsocketPool =>
        await pool
            .TryInjectWebsocket<OkxWebsocketForOrderbook, PreParsedOkxWebSocketMessage,
                OkxWebsocketForOrderbookOptions>(other, cancellationToken);
}