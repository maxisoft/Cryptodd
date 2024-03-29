﻿using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using Cryptodd.IoC;
using Cryptodd.Scheduler.Tasks;
using Maxisoft.Utils.Algorithms;
using Maxisoft.Utils.Collections.Lists;
using Maxisoft.Utils.Collections.Lists.Specialized;
using Maxisoft.Utils.Collections.Queues.Specialized;
using Maxisoft.Utils.Disposables;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.Scheduler;

public class TaskSchedulerOptions
{
    public int MaxTickQueue { get; set; }

    public TaskSchedulerOptions()
    {
        MaxTickQueue = Environment.ProcessorCount.Clamp(2, 32);
    }
}

public class TaskScheduler : IService
{
    internal static readonly AsyncLocal<TaskRunningContext> AsyncLocalTaskRunningContext = new AsyncLocal<TaskRunningContext>();
    
    private readonly DisposableManager _disposableManager = new();

    internal readonly ILogger Logger;
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);

    internal PriorityQueue<BaseScheduledTask, BaseScheduledTask> Tasks =
        new(new TaskTimePriorityComparer<BaseScheduledTask>());

    public TaskSchedulerOptions Options = new();
    internal BoundedDeque<Task> TickQueue { get; private set; }
    private readonly object _TickQueueLock = new object();

    public TaskScheduler(ILogger logger, IConfiguration configuration)
    {
        Logger = logger.ForContext(GetType());
        configuration.GetSection("TaskScheduler").Bind(Options);
        TickQueue = new BoundedDeque<Task>(Options.MaxTickQueue);
        _disposableManager.LinkDisposableAsWeak(_semaphoreSlim);
    }

    internal void RegisterTask<TTask>(TTask task) where TTask : BaseScheduledTask
    {
        lock (Tasks)
        {
            Tasks.Enqueue(task, task);
        }
        
        Debug.Assert(task.TaskRunningContextAsyncLocal is null || ReferenceEquals(task.TaskRunningContextAsyncLocal, AsyncLocalTaskRunningContext));
        task.TaskRunningContextAsyncLocal = AsyncLocalTaskRunningContext;

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

    private void OnTaskRescheduleEvent<TTask>(TTask task, RescheduleEventArgs obj) where TTask : BaseScheduledTask
    {
        Logger.Verbose("Task {Task} reschedule", task);

        lock (Tasks)
        {
            Tasks.Enqueue(task, task);
        }
    }

    private void OnTaskRescheduleError<TTask>(TTask task, Exception e) where TTask : BaseScheduledTask
    {
        Logger.Error(e, "Task {Task} reschedule error", task);
    }

    private void OnTaskRescheduleCompleted<TTask>(TTask task) where TTask : BaseScheduledTask
    {
        Logger.Debug("Task {Task} reschedule completed", task);

        Debug.Assert(!Tasks.TryPeek(out var head, out _) || !ReferenceEquals(head, task));
    }

    private ReadOnlyMemory<Task> Maintain()
    {
        if (TickQueue.Count == 0 && Options.MaxTickQueue == TickQueue.CappedSize)
        {
            return Memory<Task>.Empty;
        }

        var res = new ArrayList<Task>();

        lock (_TickQueueLock)
        {
            while (TickQueue.TryPeekFront(out var task) && task.IsCompleted)
            {
                TickQueue.PopFront();
                res.Add(task);
            }

            while (TickQueue.TryPeekBack(out var task) && task.IsCompleted)
            {
                TickQueue.PopBack();
                res.Add(task);
            }
        }

        if (Options.MaxTickQueue != TickQueue.CappedSize && TickQueue.Count < Options.MaxTickQueue)
        {
            lock (_TickQueueLock)
            {
                if (Options.MaxTickQueue != TickQueue.CappedSize && TickQueue.Count < Options.MaxTickQueue)
                {
                    var old = TickQueue;
                    TickQueue = new BoundedDeque<Task>(Options.MaxTickQueue);
                    while (old.TryPopFront(out var task))
                    {
                        TickQueue.PushBack(task);
                    }
                }
            }
        }

        return ((ReadOnlyMemory<Task>)res.Data())[..res.Count];
    }

    public async ValueTask<int> Tick(CancellationToken cancellationToken)
    {
        Logger.Verbose("Tick()");

        await _semaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TickQueue.IsFull)
            {
                static void DoMaintain(in TaskScheduler sched)
                {
                    var memory = sched.Maintain();
                    foreach (var task in memory.Span)
                    {
                        if (!task.IsFaulted)
                        {
                            continue;
                        }

                        sched.Logger.Error(task.Exception, "DoTick() Error. This isn't normal");
#if DEBUG
                        throw task.Exception!;
#endif
                    }
                }

                DoMaintain(this);
            }

            if (TickQueue.IsFull)
            {
                return 0;
            }

            var tickTask = DoTick(cancellationToken);
            lock (_TickQueueLock)
            {
                TickQueue.PushBack(tickTask);
            }

            return 1;
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }

    internal async Task<int> DoTick(CancellationToken cancellationToken)
    {
        // ReSharper disable once InconsistentlySynchronizedField
        var hasElement = Tasks.Count > 0;
        var now = DateTimeOffset.Now; // use of non monotonic datapoint => need system clock to be synced
        var processed = 0;
        var runningTasks = new ArrayList<Task>();
        var postExecutes = new ConcurrentBag<Task>();
        var toReschedule = new ArrayList<BaseScheduledTask>();

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
                BaseScheduledTask task;
                lock (Tasks)
                {
                    hasElement = Tasks.TryPeek(out task!, out _);
                }

                if (!hasElement)
                {
                    break;
                }

                if (task.NextSchedule > now)
                {
                    break;
                }

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
                var hooks = new TaskRunningContextHook();
                AsyncLocalTaskRunningContext.Value = new TaskRunningContext(this){Stopwatch = sw, Hooks = hooks, Task = task};
                try
                {
                    await task.PreExecute(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Logger.Warning(e, "Task {Task}.PreExecute() failed", task);
                    stats._exceptions.Add(e);
                    Interlocked.Increment(ref stats._errorCounter);
                    toReschedule.Add(task);
                    continue;
                }

                if (sw.ElapsedMilliseconds > 1_000)
                {
                    Logger.Warning("{Task} Task.PreExecute() should be faster", task);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    Logger.Debug("Cancellation Requested before reaching {Task}.Execute()", task);
                }

                var execTask = task.Execute(cancellationToken);
                runningTasks.Add(execTask.ContinueWith((execTask, ctx) =>
                {
                    if (ctx is not (BaseScheduledTask task, Stopwatch sw))
                    {
                        throw new ArgumentException("", nameof(stats));
                    }

                    var s = task.ExecutionStatistics;

                    Exception? exception = execTask.Exception;
                    if (execTask.IsFaulted)
                    {
                        if (execTask.Exception!.InnerExceptions.Count == 1)
                        {
                            exception = execTask.Exception.InnerExceptions.First();
                        }

                        Logger.Error(exception, "{Task} faulted", execTask);
#if DEBUG
                        Logger.Error("{DemystifiedException}", exception?.ToStringDemystified());
#endif
                        
                        s._exceptions.Add(exception!);
                        Interlocked.Increment(ref s._errorCounter);
                    }
                    else if (execTask.IsCanceled)
                    {
                        Logger.Warning("{Task} cancelled", execTask);
                        Interlocked.Increment(ref s._errorCounter);
                    }
                    else
                    {
                        Debug.Assert(execTask.IsCompletedSuccessfully);
                        Logger.Verbose("{Task} done", execTask);
                        s._executionTimes.Add(hooks.TimeElapsed?.Value ?? sw.Elapsed);
                        Interlocked.Increment(ref s._successCounter);
                        Interlocked.Increment(ref processed);
                    }

                    postExecutes.Add(task.PostExecute(exception, cancellationToken));
                }, new Tuple<BaseScheduledTask, Stopwatch>(task, sw), cancellationToken));
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
            Logger.Debug(e, "Running tasks {Count} cancelled", runningTasks.Count);
        }

        for (var index = 0; index < runningTasks.Count; index++)
        {
            var runningTask = runningTasks[index];
            if (runningTask.IsFaulted)
            {
                Logger.Error(runningTask.Exception, "A task continuation failed");
            }
        }

        try
        {
            await Task.WhenAll(postExecutes).ConfigureAwait(false);
        }
        catch (Exception e) when (e is (TaskCanceledException or OperationCanceledException))
        {
            Logger.Debug(e, "Post execute {Count} cancelled", postExecutes.Count);
        }


        foreach (var postExecute in postExecutes)
        {
            if (postExecute.IsFaulted)
            {
                Logger.Error(postExecute.Exception!, "PostExecute must not throw !");
            }
        }

        return processed;
    }
}