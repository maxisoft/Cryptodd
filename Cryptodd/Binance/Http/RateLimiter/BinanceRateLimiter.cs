﻿using System.Collections.Concurrent;
using System.Diagnostics;
using Cryptodd.IoC;
using Lamar;
using Maxisoft.Utils.Objects;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.Binance.Http.RateLimiter;

[Singleton]
public class BinanceRateLimiter : IService, IInternalBinanceRateLimiter, IDisposable
{
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private readonly ConcurrentBag<TaskCompletionSource> _taskCompletionSources = new();
    private readonly BinanceHttpUsedWeightCalculator _weightCalculator;

    public BinanceRateLimiter(BinanceHttpUsedWeightCalculator weightCalculator, ILogger logger,
        IConfiguration configuration, Boxed<CancellationToken> cancellationToken)
    {
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _weightCalculator = weightCalculator;
        _logger = logger.ForContext(GetType());
        configuration.GetSection("Binance:Http:RateLimiter").Bind(Options);
        MaxUsableWeight = Options.DefaultMaxUsableWeight;
        AvailableWeightMultiplier = Options.AvailableWeightMultiplier;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public float AvailableWeightMultiplier { get; set; } = 1f;
    public BinanceRateLimiterOptions Options { get; } = new();

    public int AvailableWeight =>
        Math.Clamp(
            checked((int)(MaxUsableWeight * AvailableWeightMultiplier - _weightCalculator.GuessTotalWeightInForce())),
            0, int.MaxValue >> 1);

    public long MaxUsableWeight { get; set; }

    public async ValueTask<IApiCallRegistration> WaitForSlot(Uri uri, int weight, CancellationToken cancellationToken)
    {
        if (weight < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(weight));
        }

        if (weight > MaxUsableWeight)
        {
            throw new ArgumentOutOfRangeException(nameof(weight));
        }

        bool Check(int? additional = null)
        {
            return _weightCalculator.GuessTotalWeightInForce() + additional.GetValueOrDefault(weight) < MaxUsableWeight;
        }

        using var cts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellationTokenSource.Token);
        if (Options.WaitForSlotTimeout > TimeSpan.Zero)
        {
            cts.CancelAfter(Options.WaitForSlotTimeout);
        }


        while (!cts.IsCancellationRequested)
        {
            if (Check())
            {
                await _semaphore.WaitAsync(cts.Token);
                try
                {
                    if (!Check())
                    {
                        continue;
                    }

                    var registration = _weightCalculator.Register(uri, weight);
                    try
                    {
                        Debug.Assert(Check(0));
                        return registration;
                    }
                    catch
                    {
                        registration.Dispose();
                        throw;
                    }
                }
                finally
                {
                    try
                    {
                        CheckAndNotify();
                    }
                    finally
                    {
                        _semaphore.Release();
                    }
                }
            }

            var tcs = new TaskCompletionSource(this);
            _taskCompletionSources.Add(tcs);
            try
            {
                await Task.WhenAny(Task.Delay(500, cts.Token), tcs.Task).ConfigureAwait(false);
            }
            catch (Exception e) when (!cts.IsCancellationRequested)
            {
                tcs.TrySetException(e);
            }
            finally
            {
                if (cts.IsCancellationRequested)
                {
                    tcs.TrySetCanceled(cts.Token);
                }
                else
                {
                    tcs.TrySetResult();
                }
            }


            FastCleanTaskCompletionSources();
        }

        cts.Token.ThrowIfCancellationRequested();
        return _weightCalculator.Register(uri, 0, false);
    }

    public void UpdateUsedWeightFromBinance(int weight, DateTimeOffset dateTimeOffset)
    {
        _logger.Verbose("Updated used weight to {Weight}", weight);
        _weightCalculator.UpdateUsedWeight(weight, dateTimeOffset);
        CheckAndNotify();
    }

    private void CheckAndNotify()
    {
        if (_weightCalculator.GuessTotalWeightInForce() < MaxUsableWeight * Options.UsableMaxWeightMultiplier)
        {
            while (_taskCompletionSources.TryTake(out var taskCompletionSource))
            {
                taskCompletionSource.TrySetResult();
            }
        }
    }

    private void FastCleanTaskCompletionSources()
    {
        while (_taskCompletionSources.TryPeek(out var tmp) && tmp is { Task.IsCompleted: true })
        {
            if (!_taskCompletionSources.TryTake(out var tmp2) || tmp2 is null)
            {
                break;
            }

            if (!ReferenceEquals(tmp, tmp2)) // concurrent modification edge case
            {
                _taskCompletionSources.Add(tmp2);
                break;
            }
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cancellationTokenSource.Dispose();
            _semaphore.Dispose();
        }
    }
}