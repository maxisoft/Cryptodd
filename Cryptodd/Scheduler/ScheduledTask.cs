using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Cryptodd.Ftx;
using Lamar;
using MathNet.Numerics.Statistics;
using Microsoft.Extensions.Configuration;
using Polly;
using Polly.Wrap;
using Serilog;

namespace Cryptodd.Scheduler;

public class RescheduleEventArgs { }

public abstract class ScheduledTask : IDisposable
{
    public ScheduledTask(ILogger logger, IConfiguration configuration)
    {
        Logger = logger;
        Name = GetType().FullName ?? GetType().Name;
        Configuration = configuration;
    }


    public string Name { get; internal protected set; } = string.Empty;

    public IConfiguration Configuration { get; protected set; }
    public TimeSpan Period { get; set; } = TimeSpan.Zero;

    public int Priority { get; protected set; }

    public TimeSpan Timeout { get; protected set; } = TimeSpan.MaxValue;

    public DateTimeOffset NextSchedule { get; protected set; } = DateTimeOffset.MaxValue;

    private readonly Subject<RescheduleEventArgs> rescheduleEventSubject = new Subject<RescheduleEventArgs>();

    public IObservable<RescheduleEventArgs> RescheduleEvent => rescheduleEventSubject.AsObservable();

    protected virtual void RaiseRescheduleEvent()
    {
        rescheduleEventSubject.OnNext(new RescheduleEventArgs());
    }

    public virtual ValueTask<bool> PreExecute(CancellationToken cancellationToken) =>
        ValueTask.FromResult<bool>(!cancellationToken.IsCancellationRequested);

    public abstract Task Execute(CancellationToken cancellationToken);

    public virtual Task PostExecute(Exception? e, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Limit task parallel execution
    /// Not implemented yet (only 1 Scheduled task type allowed to run at the same time)
    /// </summary>
    public int MaxParallelism { get; protected set; } = 1;

    protected readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
    protected readonly ILogger Logger;

    public TaskExecutionStatistics ExecutionStatistics { get; protected set; } = new TaskExecutionStatistics();

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _semaphore.Dispose();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}

public class FtxGroupedOrderbookTask : ScheduledTask
{
    private readonly IContainer _container;
    private IDisposable? _configurationChangeDisposable;
    private AsyncPolicy _retryPolicy;

    public FtxGroupedOrderbookTask(IContainer container, ILogger logger, IConfiguration configuration) : base(logger,
        configuration)
    {
        _container = container;
        Period = TimeSpan.FromMinutes(1);
        NextSchedule = DateTimeOffset.Now;
        _configurationChangeDisposable = configuration.GetReloadToken().RegisterChangeCallback(OnConfigurationChange, this);
        _retryPolicy = Policy.NoOpAsync();
        OnConfigurationChange(this);
    }

    private void OnConfigurationChange(object obj)
    {
        var section = Configuration.GetSection("Ftx").GetSection("GroupedOrderBook").GetSection("Task");
        Period = TimeSpan.FromMilliseconds(section.GetValue<int>("Period", 60 * 1000));
        var maxRetry = section.GetValue<int>("MaxRetry", 3);
        _retryPolicy = Policy.TimeoutAsync(TimeSpan.FromMilliseconds(Period.TotalMilliseconds / maxRetry))
            .WrapAsync(Policy.Handle<Exception>(_ => true).WaitAndRetryAsync(maxRetry, i => TimeSpan.FromSeconds(1 + i)));
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

    private double rollingExecutionTimeMean = 10;
    private double prevExecutionStd = 0;

    public override Task PostExecute(Exception? e, CancellationToken cancellationToken)
    {
        var mean = ExecutionStatistics.ExecutionTimes.Select(span => span.TotalMilliseconds).LastOrDefault(rollingExecutionTimeMean);
        if (ExecutionStatistics.ExecutionTimes.Count > 1 && ExecutionStatistics.ExecutionTimes.Count % 8 == 0)
        {
            (mean, prevExecutionStd) = ExecutionStatistics.ExecutionTimes.Select(span => span.TotalMilliseconds).MeanStandardDeviation();
        }

        mean = rollingExecutionTimeMean = 0.9 * mean + 0.1 * rollingExecutionTimeMean;
        
        for (var i = 0; i < 10; i++)
        {
            var nextSchedule = Math.Ceiling(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / Period.TotalMilliseconds) + i;
            nextSchedule *= (long)Period.TotalMilliseconds;
            nextSchedule -= Math.Min(mean + 0.5 * prevExecutionStd, mean * 2);
            var next = DateTimeOffset.FromUnixTimeMilliseconds((long)nextSchedule);
            if (next > NextSchedule)
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