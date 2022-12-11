using System.Diagnostics;
using Serilog;

namespace Cryptodd.Scheduler.Tasks;

public class TaskRunningContext
{
    public TaskScheduler Scheduler { get; }
    public Guid RunId { get; } = Guid.NewGuid();

    internal Stopwatch Stopwatch { private get; set; } = Stopwatch.StartNew();

    public TimeSpan TimeElapsed => Stopwatch.Elapsed;

    public TaskRunningContext(TaskScheduler scheduler)
    {
        Scheduler = scheduler;
        Logger = scheduler.Logger.ForContext(GetType());
    }

    public TaskRunningContextHook Hooks { get; internal init; } = new();

    public required BaseScheduledTask Task { get; init; }
    
    public ILogger Logger { get; }
}