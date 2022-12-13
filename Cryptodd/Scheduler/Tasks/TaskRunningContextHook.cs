namespace Cryptodd.Scheduler.Tasks;

public class TaskRunningContextHook
{
    /// <summary>
    /// Change measured TimeElapsed
    /// </summary>
    public Lazy<TimeSpan?>? TimeElapsed { get; set; }
}