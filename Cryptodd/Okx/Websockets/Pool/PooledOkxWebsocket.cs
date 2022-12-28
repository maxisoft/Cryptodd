using Cryptodd.Http;
using Cryptodd.IoC;
using Cryptodd.Okx.Limiters;
using Cryptodd.Okx.Models;
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
}