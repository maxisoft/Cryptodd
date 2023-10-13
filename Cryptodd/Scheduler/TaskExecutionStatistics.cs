using Maxisoft.Utils.Collections.Queues;
using Maxisoft.Utils.Collections.Queues.Specialized;

namespace Cryptodd.Scheduler;

/// <summary>
/// The TaskExecutionStatistics class stores various running statistics for a given task.
/// These statistics include the number of errors that have occurred,
/// the number of times the task has been executed,
/// and the total amount of time that the task has taken to execute.
/// The class also stores a list of exceptions that have been thrown during the execution of the task.
/// </summary>
public class TaskExecutionStatistics
{
    // ReSharper disable once InconsistentNaming
    internal readonly CircularDeque<Exception> _exceptions = new(16, DequeInitialUsage.Fifo);

    // ReSharper disable once InconsistentNaming
    internal readonly CircularDeque<TimeSpan>
        _executionTimes = new(256, DequeInitialUsage.Fifo);

    // ReSharper disable once InconsistentNaming
    internal long _errorCounter;

    // ReSharper disable once InconsistentNaming
    internal long _successCounter;

    public long ErrorCounter => _errorCounter;

    public long SuccessCounter => _successCounter;

    public IReadOnlyList<Exception> Exceptions => _exceptions;
    public IReadOnlyList<TimeSpan> ExecutionTimes => _executionTimes;
}