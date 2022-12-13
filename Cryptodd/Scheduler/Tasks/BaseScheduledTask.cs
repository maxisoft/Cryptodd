using System.Reactive.Linq;
using System.Reactive.Subjects;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.Scheduler.Tasks;

public abstract class BaseScheduledTask : IDisposable
{
    protected internal AsyncLocal<TaskRunningContext>? TaskRunningContextAsyncLocal { get; internal set; }
    protected TaskRunningContext? TaskRunningContext => TaskRunningContextAsyncLocal?.Value;
    protected readonly SemaphoreSlim _semaphore = new(1, 1);
    protected readonly ILogger Logger;

    private readonly Subject<RescheduleEventArgs> rescheduleEventSubject = new();

    public BaseScheduledTask(ILogger logger, IConfiguration configuration)
    {
        Logger = logger;
        Name = GetType().FullName ?? GetType().Name;
        Configuration = configuration;
    }


    public string Name { get; protected internal set; } = string.Empty;

    public IConfiguration Configuration { get; protected set; }
    public TimeSpan Period { get; set; } = TimeSpan.Zero;

    public int Priority { get; protected set; }

    public TimeSpan Timeout { get; protected set; } = TimeSpan.MaxValue;

    public DateTimeOffset NextSchedule { get; protected set; } = DateTimeOffset.MaxValue;

    public IObservable<RescheduleEventArgs> RescheduleEvent => rescheduleEventSubject.AsObservable();

    /// <summary>
    ///     Limit task parallel execution
    ///     Not implemented yet (only 1 Scheduled task type allowed to run at the same time)
    /// </summary>
    public int MaxParallelism { get; protected set; } = 1;

    public TaskExecutionStatistics ExecutionStatistics { get; protected set; } = new();

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void RaiseRescheduleEvent()
    {
        rescheduleEventSubject.OnNext(new RescheduleEventArgs());
    }

    public virtual ValueTask<bool> PreExecute(CancellationToken cancellationToken) =>
        ValueTask.FromResult(!cancellationToken.IsCancellationRequested);

    public abstract Task Execute(CancellationToken cancellationToken);

    public virtual Task PostExecute(Exception? e, CancellationToken cancellationToken) => Task.CompletedTask;

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _semaphore.Dispose();
        }
    }
}