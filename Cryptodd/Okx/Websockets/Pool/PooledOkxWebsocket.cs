using System.Net.WebSockets;
using Cryptodd.Http;
using Cryptodd.IoC;
using Cryptodd.Okx.Limiters;
using Cryptodd.Okx.Models;
using Cryptodd.Okx.Websockets.Subscriptions;
using Maxisoft.Utils.Objects;
using Serilog;

namespace Cryptodd.Okx.Websockets.Pool;

// ReSharper disable once ClassNeverInstantiated.Global
public sealed class PooledOkxWebsocket : BaseOkxWebsocket<PreParsedOkxWebSocketMessage, PooledOkxWebsocketOptions>,
    IService
{
    public PooledOkxWebsocket(IOkxLimiterRegistry limiterRegistry, ILogger logger,
        IClientWebSocketFactory webSocketFactory, Boxed<CancellationToken> cancellationToken) : base(limiterRegistry,
        logger, webSocketFactory, cancellationToken) { }

    public override async Task<bool> Ping(CancellationToken cancellationToken)
    {
        var res = await base.Ping(cancellationToken).ConfigureAwait(false);
        if (!res)
        {
            return false;
        }
        // consume pong message
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(Options.ReceiveTimeout);
        if (!res || IsClosed || cts.IsCancellationRequested)
        {
            return false;
        }

        using var memOwned = MemoryPool.Rent(OkxWebsocketMessageHelper.PongMessage.Length);
        var mem = memOwned.Memory;
        ValueWebSocketReceiveResult resp;
        do
        {
            resp = await WebSocket!.ReceiveAsync(mem, cts.Token);
        } while (!resp.EndOfMessage && !cts.IsCancellationRequested);

        return res && mem.Span[..resp.Count].SequenceEqual(OkxWebsocketMessageHelper.PongMessage.Span);
    }
}