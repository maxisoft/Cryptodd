using System.Diagnostics;

namespace Cryptodd.Okx.Limiters;

public abstract class BaseResetOnScheduleOkxLimiter : BaseOkxLimiter, IPeriodBasedOkxLimiter
{
    protected static readonly TimeSpan DefaultPeriod = TimeSpan.FromHours(1);

    private int _maxLimit;

    private SemaphoreSlim? _semaphore = null;

    protected override SemaphoreSlim? Semaphore => _semaphore;

    public DateTimeOffset DateTime { get; private set; } = DateTimeOffset.UnixEpoch;

    // ReSharper disable once ConvertToAutoPropertyWithPrivateSetter
    public override int MaxLimit => _maxLimit;

    public abstract TimeSpan Period { get; set; }

    public override int CurrentCount { get; set; }

    internal void SetMaxLimit(int value)
    {
        Debug.Assert(value > 0);
        _maxLimit = value;
        _semaphore?.Dispose();
        _semaphore = new SemaphoreSlim(value, value);
    }

    protected abstract bool ShouldReset(DateTimeOffset now);

    protected override ValueTask OnTick()
    {
        var now = DateTimeOffset.Now;
        if (!ShouldReset(now))
        {
            return ValueTask.CompletedTask;
        }

        Notify();
        DateTime = now;

        return ValueTask.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            var semaphore = _semaphore;
            while (semaphore is not null)
            {
                semaphore = Interlocked.CompareExchange(ref _semaphore, null, semaphore);
                semaphore?.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}