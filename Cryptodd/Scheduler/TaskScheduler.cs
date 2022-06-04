using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Cryptodd.IoC;
using Maxisoft.Utils.Collections.Lists;
using Maxisoft.Utils.Collections.Lists.Specialized;
using Maxisoft.Utils.Disposables;
using Serilog;

namespace Cryptodd.Scheduler;

public class TaskScheduler : IService
{
    private readonly DisposableManager _disposableManager = new DisposableManager();

    public TaskScheduler(ILogger logger)
    {
        _logger = logger;
        _disposableManager.LinkDisposableAsWeak(_semaphoreSlim);
    }

    // TODO use a PriorityQueue ?
    public PriorityQueue<ScheduledTask, ScheduledTask> Tasks =
        new PriorityQueue<ScheduledTask, ScheduledTask>(new TaskTimePriorityComparer<ScheduledTask>());

    private readonly ILogger _logger;
    private readonly SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1, 1);


    internal void RegisterTask<TTask>(TTask task) where TTask : ScheduledTask
    {
        lock (Tasks)
        {
            Tasks.Enqueue(task, task);
        }

        var disposable = task.RescheduleEvent.Subscribe(
            args => OnTaskRescheduleEvent(task, args),
            exception => OnTaskRescheduleError(task, exception),
            () => OnTaskRescheduleCompleted(task)
        );
        _disposableManager.LinkDisposable(disposable);
        if (task is IDisposable d)
        {
            _disposableManager.LinkDisposableAsWeak(d);
        }
    }

    private void OnTaskRescheduleEvent<TTask>(TTask task, RescheduleEventArgs obj) where TTask : ScheduledTask
    {
        _logger.Verbose("Task {Task} reschedule", task);

        lock (Tasks)
        {
            Tasks.Enqueue(task, task);
        }
    }

    private void OnTaskRescheduleError<TTask>(TTask task, Exception e) where TTask : ScheduledTask
    {
        _logger.Error(e, "Task {Task} reschedule error", task);
    }

    private void OnTaskRescheduleCompleted<TTask>(TTask task) where TTask : ScheduledTask
    {
        _logger.Debug("Task {Task} reschedule completed", task);

        Debug.Assert(!Tasks.TryPeek(out var head, out _) || !ReferenceEquals(head, task));
    }


    public async ValueTask<int> Tick(CancellationToken cancellationToken)
    {
        _logger.Verbose("Tick()");
        await _semaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await DoTick(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }

    public async ValueTask<int> DoTick(CancellationToken cancellationToken)
    {
        // ReSharper disable once InconsistentlySynchronizedField
        var hasElement = Tasks.Count > 0;
        var now = DateTimeOffset.Now; // use of non monotonic datapoint => need system clock to be synced
        var processed = 0;
        using var runningTasks = new PooledList<Task>();
        var postExecutes = new ConcurrentBag<Task>();
        using var toReschedule = new PooledList<ScheduledTask>();
        
        void Cleanup()
        {
            lock (Tasks)
            {
                foreach (var scheduledTask in toReschedule)
                {
                    Tasks.Enqueue(scheduledTask, scheduledTask);
                }
            }
        }
        
        try
        {
            while (hasElement && !cancellationToken.IsCancellationRequested)
            {
                ScheduledTask task;
                lock (Tasks)
                {
                    hasElement = Tasks.TryPeek(out task!, out _);
                }

                if (!hasElement) break;
                if (task.NextSchedule > now) break;

                TaskExecutionStatistics stats;
                lock (Tasks)
                {
                    hasElement = Tasks.TryDequeue(out var sameTask, out _);
                    if (!ReferenceEquals(task, sameTask) && sameTask is not null)
                    {
                        Tasks.Enqueue(sameTask, sameTask);
                        continue;
                    }

                    stats = task.ExecutionStatistics;
                }

                var sw = Stopwatch.StartNew();
                try
                {
                    await task.PreExecute(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    _logger.Warning(e, "Task {Task}.PreExecute() failed", task);
                    stats._exceptions.Add(e);
                    Interlocked.Increment(ref stats._errorCounter);
                    toReschedule.Add(task);
                    continue;
                }

                if (sw.ElapsedMilliseconds > 3_000)
                {
                    _logger.Warning("{Task} Task.PreExecute() should be faster", task);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.Debug("Cancellation Requested before reaching {Task}.Execute()", task);
                }

                var execTask = task.Execute(cancellationToken);
                runningTasks.Add(execTask.ContinueWith((execTask, ctx) =>
                {
                    if (ctx is not (ScheduledTask task, Stopwatch sw))
                    {
                        throw new ArgumentException("", nameof(stats));
                    }

                    var s = task.ExecutionStatistics;

                    if (execTask.IsFaulted)
                    {
                        _logger.Verbose(execTask.Exception!, "{Task} faulted", execTask);
                        s._exceptions.Add(execTask.Exception!);
                        Interlocked.Increment(ref s._errorCounter);
                    }
                    else
                    {
                        Debug.Assert(execTask.IsCompletedSuccessfully);
                        _logger.Verbose(execTask.Exception!, "{Task} done", execTask);
                        s._executionTimes.Add(sw.Elapsed);
                        Interlocked.Increment(ref s._successCounter);
                        Interlocked.Increment(ref processed);
                    }

                    postExecutes.Add(task.PostExecute(execTask.Exception, cancellationToken));
                }, new Tuple<ScheduledTask, Stopwatch>(task, sw), cancellationToken));
            }
        }
        finally
        {
            Cleanup();
        }

        try
        {
            await Task.WhenAll(runningTasks).ConfigureAwait(false);
        }
        catch (Exception e) when (e is (TaskCanceledException or OperationCanceledException))
        {
            _logger.Debug(e, "Running tasks {Count} cancelled", runningTasks.Count);
        }

        try
        {
            await Task.WhenAll(postExecutes).ConfigureAwait(false);
        }
        catch (Exception e) when (e is (TaskCanceledException or OperationCanceledException))
        {
            _logger.Debug(e, "Post execute {Count} cancelled", postExecutes.Count);
        }


        foreach (var postExecute in postExecutes)
        {
            if (postExecute.IsFaulted)
            {
                _logger.Error(postExecute.Exception!, "PostExecute must not throw !");
            }
        }

        return processed;
    }
}