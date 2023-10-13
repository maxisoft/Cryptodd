using Cryptodd.Binance.Collector.Klines;
using Cryptodd.Okx.Collectors.Swap;
using Lamar;
using Maxisoft.Utils.Collections.Queues.Specialized;
using Microsoft.Extensions.Configuration;
using Polly;
using Polly.Retry;
using Serilog;

namespace Cryptodd.Scheduler.Tasks.Binance;

// ReSharper disable once UnusedType.Global
public class BinanceFuturesKlineCollectorTask : BasePeriodicScheduledTaskWithRetryPolicyAndCancellationTokenSources
{
    private IBinanceFuturesKlineCollector? _klineDataCollector;

    public BinanceFuturesKlineCollectorTask(IContainer container, ILogger logger, IConfiguration configuration) : base(
        logger,
        configuration, container)
    {
        Period = TimeSpan.FromSeconds(1);
        Section = Configuration.GetSection("BinanceFutures:Collector:Kline:Task");
        AdaptativeReschedule = false;
        DefaultEnabledState = false;
        OnConfigurationChange();
    }

    public override async Task Execute(CancellationToken cancellationToken)
    {
        var cts = CreateCancellationTokenSource(cancellationToken);

        IBinanceFuturesKlineCollector KlineDataCollector()
        {
            if (_klineDataCollector is null)
            {
                _klineDataCollector = Container.GetInstance<IBinanceFuturesKlineCollector>();
            }

            return _klineDataCollector;
        }


        var mainTask = RetryPolicy.ExecuteAsync(
            _ => KlineDataCollector().Collect(cts.Token),
            cancellationToken);

        try
        {
            await mainTask.ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Logger.Debug(e, "");

            var wasCancelled = cts.IsCancellationRequested;
            if (!wasCancelled)
            {
                cts.Cancel();
                if (_klineDataCollector is not null)
                {
                    await _klineDataCollector.DisposeAsync().ConfigureAwait(false);
                }

                _klineDataCollector = null;
            }
        }

        if (!mainTask.IsCanceled || cts.IsCancellationRequested)
        {
            await mainTask.ConfigureAwait(false);
        }

        if (!mainTask.IsCompleted)
        {
            await mainTask.ConfigureAwait(false);
        }

        mainTask.Dispose();

        cts.Cancel();
    }

    protected override TimeSpan CancellationTokenTimeSpan => TimeSpan.FromSeconds(10);

    private void ConfigureRetryPolicy()
    {
        var maxRetry = Section.GetValue("MaxRetry", 3);
        RetryPolicy = Policy.Handle<Exception>(static e => e is not OperationCanceledException)
            .WaitAndRetryAsync(maxRetry, i => TimeSpan.FromSeconds(1 + i));
    }

    protected override void OnConfigurationChange(object? obj)
    {
        base.OnConfigurationChange(obj);
        ConfigureRetryPolicy();
    }
}