using Maxisoft.Utils.Empties;

namespace Cryptodd.Okx.Limiters;

public static class OkxLimiterExtensions
{
    public static async Task WaitForLimit<T>(this T limiter, bool registerUsage = true, int count = 1,
        int? effectiveCount = null,
        CancellationToken cancellationToken = default) where T : IOkxLimiter
    {
        await limiter.WaitForLimit(parameters =>
        {
            parameters.AutoRegister = registerUsage;
            parameters.RegistrationCount = effectiveCount ?? count;
            return Task.FromResult<EmptyStruct>(default);
        }, count, cancellationToken).ConfigureAwait(false);
    }
}