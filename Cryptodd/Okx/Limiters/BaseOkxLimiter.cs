using System.Collections.Concurrent;
using System.Diagnostics;
using Maxisoft.Utils.Collections.Lists;

namespace Cryptodd.Okx.Limiters;

public abstract class BaseOkxLimiter : IOkxLimiter, IDisposable
{
    private readonly ConcurrentBag<TaskCompletionSource> _waiters = new();

    protected abstract SemaphoreSlim? Semaphore { get; }

    protected internal int TickPollingTimer { get; set; } = 200;

    public abstract int MaxLimit { get; }
    public abstract int CurrentCount { get; set; }

    public virtual int AvailableCount => IOkxLimiter.ComputeAvailableCount(this);

    private readonly SemaphoreSlim _internalSemaphore = new(1, 1);

    private async ValueTask<int> SemaphoreMultiWaitViaPollingAsync(SemaphoreSlim semaphore, int count,
        int pollingInterval = 20,
        CancellationToken cancellationToken = default)
    {
        Debug.Assert(TickPollingTimer > 2 * pollingInterval, "TickPollingTimer > pollingInterval");
        while (!cancellationToken.IsCancellationRequested)
        {
            await _internalSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                for (var i = 0; i < count; i++)
                {
                    try
                    {
                        var entered = await semaphore.WaitAsync(pollingInterval, cancellationToken)
                            .ConfigureAwait(false);
                        if (entered)
                        {
                            continue;
                        }

                        if (i > 0)
                        {
                            semaphore.Release(i);
                        }

                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        if (i > 0)
                        {
                            semaphore.Release(i);
                        }

                        if (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }

                        break;
                    }
                }

                break;
            }
            finally
            {
                _internalSemaphore.Release();
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return count;
    }

    public async Task<T> WaitForLimit<T>(Func<OkxLimiterOnSuccessParameters, Task<T>> onSuccess, int count = 1,
        CancellationToken cancellationToken = default)
    {
        bool Check()
        {
            return AvailableCount >= count;
        }

        async ValueTask<T> Perform()
        {
            if (!Check())
            {
                return await ValueTask.FromException<T>(new RetryException());
            }

            var semaphore = Semaphore;

            if (semaphore is null || MaxLimit == 1)
            {
                semaphore = _internalSemaphore;
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await SemaphoreMultiWaitViaPollingAsync(semaphore, count, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }


            try
            {
                if (Check() && !cancellationToken.IsCancellationRequested)
                {
                    var parameters = new OkxLimiterOnSuccessParametersInternalImpl
                        { CancellationToken = cancellationToken, Count = count, RegistrationCount = count };
                    try
                    {
                        return await onSuccess(parameters)
                            .ConfigureAwait(false);
                    }
                    finally
                    {
                        if (parameters.AutoRegister)
                        {
                            CurrentCount += parameters.RegistrationCount;
                        }
                    }
                }
            }
            finally
            {
                // check not disposed
                if (ReferenceEquals(semaphore, Semaphore))
                {
                    semaphore.Release(count);
                }
                else if (ReferenceEquals(semaphore, _internalSemaphore))
                {
                    semaphore.Release();
                }
            }


            throw new RetryException();
        }

        var retry = true;
        while (retry || !Check())
        {
            retry = false;
            cancellationToken.ThrowIfCancellationRequested();
            await OnTick().ConfigureAwait(false);
            if (AvailableCount < count)
            {
                var tcs = new TaskCompletionSource();
                _waiters.Add(tcs);
                try
                {
                    await using var registration =
                        cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
                    await Task.WhenAny(Task.Delay(TickPollingTimer, cancellationToken), tcs.Task).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    tcs.TrySetException(e);
                    TryRemove(tcs);
                    throw;
                }

                TryRemoveFast(tcs);
            }

            try
            {
                return await Perform().ConfigureAwait(false);
            }
            catch (RetryException)
            {
                retry = true;
            }
        }

        return await Perform();
    }

    protected bool TryRemove(TaskCompletionSource tcs)
    {
        var queue = new ArrayList<TaskCompletionSource>();
        try
        {
            while (_waiters.TryTake(out var other))
            {
                if (ReferenceEquals(other, tcs))
                {
                    return true;
                }

                queue.Add(other);
            }
        }
        finally
        {
            if (queue.Count > 0)
            {
                var span = queue.AsSpan();
                for (var i = span.Length - 1; i >= 0; i--)
                {
                    _waiters.Add(span[i]);
                }
            }
        }


        return false;
    }

    protected bool TryRemoveFast(TaskCompletionSource tcs)
    {
        if (!_waiters.TryPeek(out var otherTcs) || !ReferenceEquals(otherTcs, tcs))
        {
            return false;
        }

        if (_waiters.TryTake(out otherTcs) && ReferenceEquals(otherTcs, tcs))
        {
            return true;
        }

        if (otherTcs is not null)
        {
            _waiters.Add(otherTcs);
        }

        return false;
    }

    protected internal int Notify()
    {
        var res = 0;
        while (_waiters.TryTake(out var tcs))
        {
            tcs.TrySetResult();
            res++;
        }

        return res;
    }

    protected abstract ValueTask OnTick();

    private class RetryException : Exception { }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _internalSemaphore.Dispose();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}