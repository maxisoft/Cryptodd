using Cryptodd.Http;
using Cryptodd.Http.Abstractions;
using Cryptodd.Okx.Limiters;
using Serilog;

namespace Cryptodd.Okx.Http.Abstractions;

public class OkxHttpClientAbstraction :
    HttpClientAbstractionWithUriRewrite<IUriRewriteService, OkxHttpClientAbstractionContext>, IOkxHttpClientAbstraction
{
    private readonly IOkxLimiterRegistry _limiterRegistry;

    public OkxHttpClientAbstraction(HttpClient client, ILogger logger,
        IUriRewriteService uriRewriteService, IOkxLimiterRegistry limiterRegistry) : base(client, uriRewriteService)
    {
        Logger = logger.ForContext(GetType());
        _limiterRegistry = limiterRegistry;
    }

    protected ILogger Logger { get; }

    public RemoveLimiterOnDispose<OkxLimiter, OkxHttpClientAbstractionContext> UseLimiter<TLimiter>(string name,
        string configName) where TLimiter : OkxLimiter, new()
    {
        if (!TryGetContext(out var context))
        {
            context = Context = DefaultContext();
        }

        var limiter = context.Limiter = _limiterRegistry.GetHttpSubscriptionLimiter<TLimiter>(name, configName);

        return new RemoveLimiterOnDispose<OkxLimiter, OkxHttpClientAbstractionContext>(limiter, context);
    }

    public override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        HttpCompletionOption completionOption, CancellationToken cancellationToken)
    {
        if (HasContext && Context.HasLimiter)
        {
            return await Context.Limiter.WaitForLimit(_ => base.SendAsync(request, completionOption, cancellationToken),
                cancellationToken: cancellationToken);
        }

        if (Const.IsDebug)
        {
            throw new ArgumentException($"There's no limiter for {request.RequestUri}");
        }

        Logger.Warning("There's no limiter for {Request}", request.RequestUri);

        return await base.SendAsync(request, completionOption, cancellationToken);
    }

    protected override OkxHttpClientAbstractionContext DefaultContext() => new();
}