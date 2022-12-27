using Cryptodd.Http;
using Cryptodd.Http.Abstractions;
using Cryptodd.Okx.Limiters;
using Cryptodd.Utils;
using Polly;
using Serilog;

namespace Cryptodd.Okx.Http;

public record OkxHttpClientAbstractionContext
    (Uri? Uri = null, Uri? OriginalUri = null) : HttpClientAbstractionContext(Uri, OriginalUri), IDisposable
{
    private OkxLimiter? _limiter;

    public OkxLimiter Limiter
    {
        get => _limiter ?? new EmptyHttpOkxLimiter();
        set => _limiter = value;
    }

    public void RemoveLimiter()
    {
        _limiter = null;
    }

    public bool HasLimiter => _limiter is not null;

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            RemoveLimiter();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}

public sealed class RemoveLimiterOnDispose<T, TContext> : IDisposable
    where TContext : OkxHttpClientAbstractionContext
    where T : class
{
    private T? _limiter;
    private readonly WeakReference<TContext> _context;

    public RemoveLimiterOnDispose(T? limiter, TContext context)
    {
        _limiter = limiter;
        _context = new WeakReference<TContext>(context);
    }

    public void Dispose()
    {
        var limiter = _limiter;
        if (limiter is not null && _context.TryGetTarget(out var context) && context.HasLimiter &&
            ReferenceEquals(context.Limiter, limiter))
        {
            context.RemoveLimiter();
        }

        _limiter = null;
    }
}

public class OkxHttpClientAbstraction :
    HttpClientAbstractionWithUriRewrite<IUriRewriteService, OkxHttpClientAbstractionContext>, IOkxHttpClientAbstraction
{
    private readonly IOkxLimiterRegistry _limiterRegistry;
    protected ILogger Logger { get; }

    public OkxHttpClientAbstraction(HttpClient client, ILogger logger,
        IUriRewriteService uriRewriteService, IOkxLimiterRegistry limiterRegistry) : base(client, uriRewriteService)
    {
        Logger = logger.ForContext(GetType());
        _limiterRegistry = limiterRegistry;
    }

    public RemoveLimiterOnDispose<OkxLimiter, OkxHttpClientAbstractionContext> UseLimiter<TLimiter>(string name, string configName) where TLimiter : OkxLimiter, new()
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
        else
        {
            Logger.Warning("There's no limiter for {Request}", request.RequestUri);
        }

        return await base.SendAsync(request, completionOption, cancellationToken);
    }

    protected override OkxHttpClientAbstractionContext DefaultContext() => new();
}