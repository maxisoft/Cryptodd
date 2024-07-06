using Cryptodd.Http;
using Cryptodd.Http.Abstractions;
using Serilog;

namespace Cryptodd.Binance.Http;

public abstract class BaseBinanceHttpClientAbstraction(
    HttpClient client,
    ILogger logger,
    IUriRewriteService uriRewriteService)
    : HttpClientAbstractionWithUriRewrite<IUriRewriteService, HttpClientAbstractionContext>(client, uriRewriteService)
{
    protected ILogger Logger { get; } = logger;

    protected override HttpClientAbstractionContext DefaultContext() => new();
}