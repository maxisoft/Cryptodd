using Cryptodd.Okx.Limiters;

namespace Cryptodd.Http;

public abstract class HttpOkxLimiter : OkxLimiter
{
    protected HttpOkxLimiter(TimeSpan period, int maxLimit) : base(period, maxLimit) { }
}