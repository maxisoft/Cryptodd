using System.Diagnostics;
using Cryptodd.Ftx;
using Lamar;
using MathNet.Numerics.Statistics;
using Microsoft.Extensions.Configuration;
using Polly;
using Serilog;

namespace Cryptodd.Scheduler.Tasks;

public class FtxGroupedOrderbookTask : ScheduledTask
{
    private readonly IContainer _container;
    private IDisposable? _configurationChangeDisposable;
    private AsyncPolicy _retryPolicy;
    private double _prevExecutionStd;
    private TimeSpan PeriodOffset { get; set; } = TimeSpan.Zero;
    
    private double _rollingExecutionTimeMean = 10;

    public FtxGroupedOrderbookTask(IContainer container, ILogger logger, IConfiguration configuration) : base(logger,
        configuration)
    {
        _container = container;
        Period = TimeSpan.FromMinutes(1);
        NextSchedule = DateTimeOffset.Now;
        _configurationChangeDisposable =
            configuration.GetReloadToken().RegisterChangeCallback(OnConfigurationChange, this);
        _retryPolicy = Policy.NoOpAsync();
        OnConfigurationChange(this);
    }

    private void OnConfigurationChange(object obj)
    {
        var section = Configuration.GetSection("Ftx").GetSection("GroupedOrderBook").GetSection("Task");
        Period = TimeSpan.FromMilliseconds(section.GetValue("Period", 60 * 1000));
        PeriodOffset = TimeSpan.FromMilliseconds(section.GetValue("PeriodOffset", 0));
        var maxRetry = section.GetValue("MaxRetry", 3);
        _retryPolicy = Policy.TimeoutAsync(TimeSpan.FromMilliseconds(Period.TotalMilliseconds / maxRetry))
            .WrapAsync(
                Policy.Handle<Exception>(_ => true).WaitAndRetryAsync(maxRetry, i => TimeSpan.FromSeconds(1 + i)));
    }

    public override Task Execute(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        return _retryPolicy.ExecuteAsync(token =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, token);
            cts.CancelAfter(Period - sw.Elapsed);
            using var orderBookService = _container.GetInstance<GatherGroupedOrderBookService>();
            return orderBookService.CollectOrderBooks(cts.Token);
        }, cancellationToken);
    }

    public override Task PostExecute(Exception? e, CancellationToken cancellationToken)
    {
        var mean = ExecutionStatistics.ExecutionTimes.Select(span => span.TotalMilliseconds)
            .LastOrDefault(_rollingExecutionTimeMean);
        if (ExecutionStatistics.ExecutionTimes.Count > 1 && ExecutionStatistics.ExecutionTimes.Count % 8 == 0)
        {
            (mean, _prevExecutionStd) = ExecutionStatistics.ExecutionTimes.Select(span => span.TotalMilliseconds)
                .MeanStandardDeviation();
        }

        mean = _rollingExecutionTimeMean = 0.9 * mean + 0.1 * _rollingExecutionTimeMean;

        for (var i = 0; i < 10; i++)
        {
            var nextSchedule = Math.Ceiling((DateTimeOffset.UtcNow + PeriodOffset).ToUnixTimeMilliseconds() / Period.TotalMilliseconds) +
                               i;
            nextSchedule *= (long)Period.TotalMilliseconds;
            nextSchedule -= Math.Min(mean + 0.5 * _prevExecutionStd, mean * 2);
            var next = DateTimeOffset.FromUnixTimeMilliseconds((long)nextSchedule);
            if (next > NextSchedule && (next - NextSchedule).Duration() > Period / 2)
            {
                NextSchedule = next;
                break;
            }
        }

        RaiseRescheduleEvent();

        return Task.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        _configurationChangeDisposable?.Dispose();
        _configurationChangeDisposable = null;
        base.Dispose(disposing);
    }
}