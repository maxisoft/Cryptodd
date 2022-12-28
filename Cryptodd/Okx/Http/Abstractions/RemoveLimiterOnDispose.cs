namespace Cryptodd.Okx.Http.Abstractions;

public sealed class RemoveLimiterOnDispose<T, TContext> : IDisposable
    where TContext : OkxHttpClientAbstractionContext
    where T : class
{
    private readonly WeakReference<TContext> _context;
    private T? _limiter;

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