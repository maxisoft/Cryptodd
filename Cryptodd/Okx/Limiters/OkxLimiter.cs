using System.Diagnostics;

namespace Cryptodd.Okx.Limiters;

// use both reset periodically and on clock behavior to be sure about never reaching limits whatever rate limiter the remote uses.
public class OkxLimiter : IPeriodBasedOkxLimiter, IDisposable
{
    private readonly ResetOnClockOkxLimiter _onClockOkxLimiter;
    private readonly ResetPeriodicallyOkxLimiter _periodicallyOkxLimiter;

    public OkxLimiter(TimeSpan period, int maxLimit)
    {
        _periodicallyOkxLimiter = new ResetPeriodicallyOkxLimiter { Period = period };
        _onClockOkxLimiter = new ResetOnClockOkxLimiter { Period = period };
        MaxLimit = maxLimit;
    }

    public int TickPollingTimer
    {
        get => Math.Min(_periodicallyOkxLimiter.TickPollingTimer, _onClockOkxLimiter.TickPollingTimer);
        set
        {
            _periodicallyOkxLimiter.TickPollingTimer = value;
            _onClockOkxLimiter.TickPollingTimer = value;
            Notify();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public TimeSpan Period
    {
        get => _periodicallyOkxLimiter.Period;
        set
        {
            if ((long)value.TotalMilliseconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "negative");
            }

            _onClockOkxLimiter.Period = value;
            _periodicallyOkxLimiter.Period = value;
            Notify();
        }
    }

    public int AvailableCount => IOkxLimiter.ComputeAvailableCount(this);

    public int MaxLimit
    {
        get => Math.Min(_periodicallyOkxLimiter.MaxLimit, _onClockOkxLimiter.MaxLimit);
        set
        {
            _periodicallyOkxLimiter.SetMaxLimit(value);
            _onClockOkxLimiter.SetMaxLimit(value);
            Notify();
        }
    }

    public int CurrentCount => Math.Max(_periodicallyOkxLimiter.CurrentCount, _onClockOkxLimiter.CurrentCount);

    public async Task TriggerOnTick()
    {
        await Task.WhenAll(_periodicallyOkxLimiter.TriggerOnTick(), _onClockOkxLimiter.TriggerOnTick())
            .ConfigureAwait(false);
    }

    public async Task<T> WaitForLimit<T>(Func<OkxLimiterOnSuccessParameters, Task<T>> onSuccess, int count = 1,
        CancellationToken cancellationToken = default)
    {
        async Task<T> Continuation(OkxLimiterOnSuccessParameters p0)
        {
            var originalAutoRegister = p0.AutoRegister;
            p0.AutoRegister = false;
            if (_onClockOkxLimiter.AvailableCount < count)
            {
                await _onClockOkxLimiter.TriggerOnTick();
                if (_onClockOkxLimiter.AvailableCount < count)
                {
                    // early stop to prevent acquiring the semaphore for too long
                    throw new RetryLater();
                }
            }

            return await _onClockOkxLimiter.WaitForLimit(p1 =>
            {
                p0.AutoRegister = originalAutoRegister;

                async Task<T> Hook(OkxLimiterOnSuccessParameters p2)
                {
                    try
                    {
                        return await onSuccess(p2).ConfigureAwait(false);
                    }
                    finally
                    {
                        Debug.Assert(ReferenceEquals(p1, p2), "ReferenceEquals(p1, p2)");
                        p0.AutoRegister = p2.AutoRegister;
                        p0.RegistrationCount = p2.RegistrationCount;
                    }
                }

                return Hook(p1);
            }, count, cancellationToken).ConfigureAwait(false);
        }

        var tryNumber = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            await TriggerOnTick();
            try
            {
                return await _periodicallyOkxLimiter.WaitForLimit(Continuation, count, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (RetryLater)
            {
                tryNumber++;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new Exception($"Unreachable code reached after {tryNumber} tries");
    }

    private void Notify()
    {
        _periodicallyOkxLimiter.Notify();
        _onClockOkxLimiter.Notify();
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _onClockOkxLimiter.Dispose();
            _periodicallyOkxLimiter.Dispose();
        }
    }

    private class RetryLater : Exception { }
}