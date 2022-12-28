namespace Cryptodd.Okx.Limiters;

public interface IOkxLimiter
{
    int MaxLimit { get; }
    int CurrentCount { get; }

    int AvailableCount => ComputeAvailableCount(this);

    internal static int ComputeAvailableCount<T>(in T that) where T : IOkxLimiter =>
        Math.Max(that.MaxLimit - that.CurrentCount, 0);

    Task<T> WaitForLimit<T>(Func<OkxLimiterOnSuccessParameters, Task<T>> onSuccess, int count = 1,
        CancellationToken cancellationToken = default);
}