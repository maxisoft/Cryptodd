namespace Cryptodd.Scheduler;

public struct TaskTimePriorityComparer<TTask> : IComparer<TTask> where TTask : ScheduledTask
{
    public int Compare(TTask? x, TTask? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (ReferenceEquals(null, y))
        {
            return 1;
        }

        if (ReferenceEquals(null, x))
        {
            return -1;
        }

        var nextScheduleComparison = x.NextSchedule.CompareTo(y.NextSchedule);
        if (nextScheduleComparison != 0)
        {
            return nextScheduleComparison;
        }

        var priorityComparison = x.Priority.CompareTo(y.Priority);
        if (priorityComparison != 0)
        {
            return priorityComparison;
        }

        int errorComparision;
        unchecked
        {
            errorComparision =
                ((ulong)x.ExecutionStatistics.ErrorCounter / ((ulong)x.ExecutionStatistics.SuccessCounter + 1))
                .CompareTo(
                    (ulong)y.ExecutionStatistics.ErrorCounter / ((ulong)y.ExecutionStatistics.SuccessCounter + 1));
        }

        if (errorComparision != 0)
        {
            return errorComparision;
        }

        var periodComparison = x.Period.CompareTo(y.Period);
        if (periodComparison != 0)
        {
            return periodComparison;
        }

        var nameComparison = string.Compare(x.Name, y.Name, StringComparison.Ordinal);
        if (nameComparison != 0)
        {
            return nameComparison;
        }

        var timeoutComparison = x.Timeout.CompareTo(y.Timeout);
        if (timeoutComparison != 0)
        {
            return timeoutComparison;
        }

        var maxParallelismComparison = x.MaxParallelism.CompareTo(y.MaxParallelism);
        return maxParallelismComparison;
    }
}