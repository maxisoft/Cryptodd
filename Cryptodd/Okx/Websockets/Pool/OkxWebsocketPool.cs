using Cryptodd.IoC;
using Cryptodd.Okx.Limiters;
using Lamar;
using Maxisoft.Utils.Objects;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.Okx.Websockets.Pool;

[Singleton]
public sealed class OkxWebsocketPool : BaseOkxWebsocketPool<OkxWebsocketPoolOptions>, IService
{
    public OkxWebsocketPool(ILogger logger, IContainer container, IConfiguration configuration,
        IOkxLimiterRegistry limiterRegistry, Boxed<CancellationToken> cancellationToken) : base(logger, container,
        configuration.GetSection("Okx:Websocket:Pool"), limiterRegistry, cancellationToken) { }
    
    
}