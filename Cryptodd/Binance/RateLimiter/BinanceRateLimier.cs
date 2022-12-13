using System.Collections.Concurrent;
using System.Diagnostics;
using Cryptodd.IoC;
using Lamar;
using Maxisoft.Utils.Objects;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.Binance.RateLimiter;

public class BinanceRateLimiterOptions
{
    public int DefaultMaxUsableWeight { get; set; } = 1200;
    public float UsableMaxWeightMultiplier { get; set; } = 1.0f;

    public TimeSpan WaitForSlotTimeout { get; set; } = TimeSpan.FromMinutes(2);

    public float AvailableWeightMultiplier { get; set; } = 0.8f;
}

public interface IBinanceRateLimiter
{
    int AvailableWeight { get; }
    long MaxUsableWeight { get; }
    ValueTask<IApiCallRegistration> WaitForSlot(Uri uri, int weight, CancellationToken cancellationToken);
}

public interface IInternalBinanceRateLimiter : IBinanceRateLimiter
{
    BinanceRateLimiterOptions Options { get; }
    new long MaxUsableWeight { get; set; }
    void UpdateUsedWeightFromBinance(int weight, DateTimeOffset dateTimeOffset);
    void UpdateUsedWeightFromBinance(int weight) => UpdateUsedWeightFromBinance(weight, DateTimeOffset.Now);

    float AvailableWeightMultiplier { get; set; }
}

[Singleton]
public class BinanceRateLimiter : IService, IInternalBinanceRateLimiter
{
    private readonly BinanceHttpUsedWeightCalculator _weightCalculator;
    private readonly ILogger _logger;
    private readonly BinanceRateLimiterOptions _options = new();
    private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
    private readonly CancellationTokenSource _cancellationTokenSource;

    public float AvailableWeightMultiplier { get; set; } = 1f;

    private readonly ConcurrentBag<TaskCompletionSource> _taskCompletionSources = new();
    public BinanceRateLimiterOptions Options => _options;

    public int AvailableWeight =>
        Math.Clamp(
            checked((int)(MaxUsableWeight * AvailableWeightMultiplier - _weightCalculator.GuessTotalWeightInForce())),
            0, int.MaxValue >> 1);

    public long MaxUsableWeight { get; set; }

    public BinanceRateLimiter(BinanceHttpUsedWeightCalculator weightCalculator, ILogger logger,
        IConfiguration configuration, Boxed<CancellationToken> cancellationtoken)
    {
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationtoken);
        _weightCalculator = weightCalculator;
        _logger = logger.ForContext(GetType());
        configuration.GetSection("Binance:RateLimiter").Bind(_options);
        MaxUsableWeight = _options.DefaultMaxUsableWeight;
        AvailableWeightMultiplier = _options.AvailableWeightMultiplier;
    }

    private void CheckAndNotify()
    {
        if (_weightCalculator.GuessTotalWeightInForce() < MaxUsableWeight * _options.UsableMaxWeightMultiplier)
        {
            while (_taskCompletionSources.TryTake(out var taskCompletionSource))
            {
                taskCompletionSource.TrySetResult();
            }
        }
    }

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
        if (_options.WaitForSlotTimeout > TimeSpan.Zero)
        {
            cts.CancelAfter(_options.WaitForSlotTimeout);
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
            await Task.WhenAny(Task.Delay(500, cts.Token), tcs.Task).ConfigureAwait(false);
            tcs.TrySetCanceled(cts.Token);
            FastCleanTaskCompletionSources();
        }

        cts.Token.ThrowIfCancellationRequested();
        return _weightCalculator.Register(uri, 0, false);
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

    public void UpdateUsedWeightFromBinance(int weight, DateTimeOffset dateTimeOffset)
    {
        _logger.Verbose("Updated used weight to {Weight}", weight);
        _weightCalculator.UpdateUsedWeight(weight, dateTimeOffset);
        CheckAndNotify();
    }
}