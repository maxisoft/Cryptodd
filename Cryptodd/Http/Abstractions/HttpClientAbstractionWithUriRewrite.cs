using System.Diagnostics.CodeAnalysis;

namespace Cryptodd.Http.Abstractions;

public abstract class HttpClientAbstractionWithUriRewrite<TUriRewriteService, TContext>(
    HttpClient client,
    TUriRewriteService uriRewriteService) : HttpClientAbstraction(client)
    where TUriRewriteService : IUriRewriteService
    where TContext : HttpClientAbstractionContext
{
    protected TUriRewriteService UriRewriteService { get; } = uriRewriteService;
    private readonly AsyncLocal<TContext> _localContext = new();

    protected abstract TContext DefaultContext();

    protected TContext Context
    {
        get => _localContext.Value ?? DefaultContext();
        set => _localContext.Value = value;
    }

    protected bool HasContext => _localContext.Value is not null;

    protected bool TryGetContext([NotNullWhen(true)]out TContext? context)
    {
        context = _localContext.Value;
        return context is not null;
    }

    protected Uri? Rewrite(Uri? uri, bool setContext = true)
    {
        Uri? res = null;
        var context = Context;
        if (setContext && context.OriginalUri is not null && context.OriginalUri != uri)
        {
            throw new ArgumentException("trying to reuse a non matching context");
        }

        try
        {
            if (uri is null)
            {
                return null;
            }

            var task = UriRewriteService.Rewrite(uri);

            res = task.IsCompleted ? task.Result : task.AsTask().Result;
        }
        finally
        {
            if (setContext)
            {
                Context = context with { OriginalUri = uri, Uri = res };
            }
        }

        return res;
    }


    public override HttpRequestMessage CreateRequestMessage(HttpMethod method, Uri? uri)
    {
        uri = Rewrite(uri);
        return base.CreateRequestMessage(method, uri);
    }
}