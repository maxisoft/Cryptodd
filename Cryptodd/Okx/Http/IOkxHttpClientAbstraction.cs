using Cryptodd.Http;
using Cryptodd.Http.Abstractions;
using Cryptodd.Okx.Limiters;

namespace Cryptodd.Okx.Http;

public interface IOkxHttpClientAbstraction : IHttpClientAbstraction
{
    public RemoveLimiterOnDispose<OkxLimiter, OkxHttpClientAbstractionContext> UseLimiter<TLimiter>(string name, string configName)
        where TLimiter : OkxLimiter, new();
}