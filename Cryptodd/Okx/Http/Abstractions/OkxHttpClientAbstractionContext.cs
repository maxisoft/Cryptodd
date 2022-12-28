using Cryptodd.Http;
using Cryptodd.Http.Abstractions;
using Cryptodd.Okx.Limiters;

namespace Cryptodd.Okx.Http.Abstractions;

public record OkxHttpClientAbstractionContext
    (Uri? Uri = null, Uri? OriginalUri = null) : HttpClientAbstractionContext(Uri, OriginalUri), IDisposable
{
    private OkxLimiter? _limiter;

    public OkxLimiter Limiter
    {
        get => _limiter ?? new EmptyHttpOkxLimiter();
        set => _limiter = value;
    }

    public bool HasLimiter => _limiter is not null;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public void RemoveLimiter()
    {
        _limiter = null;
    }

    private void Dispose(bool disposing)
    {
        if (disposing)
        {
            RemoveLimiter();
        }
    }
}