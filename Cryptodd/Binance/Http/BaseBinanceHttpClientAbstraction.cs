using Cryptodd.Http;
using Cryptodd.Http.Abstractions;
using Serilog;

namespace Cryptodd.Binance.Http;

public abstract class BaseBinanceHttpClientAbstraction : HttpClientAbstractionWithUriRewrite<IUriRewriteService, HttpClientAbstractionContext>
{
    protected ILogger Logger { get; }

    protected BaseBinanceHttpClientAbstraction(HttpClient client, ILogger logger,
        IUriRewriteService uriRewriteService) : base(client, uriRewriteService)
    {
        Logger = logger;
    }

    protected override HttpClientAbstractionContext DefaultContext() => new();
}