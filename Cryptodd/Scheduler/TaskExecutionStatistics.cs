using Maxisoft.Utils.Collections.Queues;
using Maxisoft.Utils.Collections.Queues.Specialized;

namespace Cryptodd.Scheduler;

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