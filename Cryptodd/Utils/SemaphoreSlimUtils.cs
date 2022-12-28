namespace Cryptodd.Utils;

public static class SemaphoreSlimUtils
{
    public sealed class SemaphoreSlimReleaseOnDispose : IDisposable
    {
        public const int DisposedValue = -1;

        public int ReleaseCount
        {
            get => _releaseCount;
            init => _releaseCount = value;
        }

        public void CancelRelease()
        {
            _releaseCount = DisposedValue;
        }

        private readonly SemaphoreSlim _semaphore;
        private int _releaseCount = 1;

        public SemaphoreSlimReleaseOnDispose(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public bool Disposed => ReleaseCount == DisposedValue;

        public bool ReferenceEquals(SemaphoreSlim semaphore) => ReferenceEquals(semaphore, _semaphore);

        public void Dispose()
        {
            var count = ReleaseCount;
            if (count > 0)
            {
                _semaphore.Release(count);
            }

            Interlocked.CompareExchange(ref _releaseCount, DisposedValue, count);
        }
    }

    public static async Task<SemaphoreSlimReleaseOnDispose> WaitAndGetDisposableAsync(this SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new SemaphoreSlimReleaseOnDispose(semaphore);
    }

    public static async Task<(bool Success, SemaphoreSlimReleaseOnDispose Disposable)> WaitAndGetDisposableAsync(
        this SemaphoreSlim semaphore, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var res = await semaphore.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        return (res,
            new SemaphoreSlimReleaseOnDispose(semaphore)
                { ReleaseCount = res ? 1 : SemaphoreSlimReleaseOnDispose.DisposedValue });
    }


    public static async Task<(bool Success, SemaphoreSlimReleaseOnDispose Disposable)> WaitAndGetDisposableAsync(
        this SemaphoreSlim semaphore, int millisecondsTimeout, CancellationToken cancellationToken)
    {
        var res = await semaphore.WaitAsync(millisecondsTimeout, cancellationToken).ConfigureAwait(false);
        return (res,
            new SemaphoreSlimReleaseOnDispose(semaphore)
                { ReleaseCount = res ? 1 : SemaphoreSlimReleaseOnDispose.DisposedValue });
    }
}