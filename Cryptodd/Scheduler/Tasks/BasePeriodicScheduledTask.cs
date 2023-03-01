using Lamar;
using MathNet.Numerics.Statistics;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.Scheduler.Tasks;

public abstract class BasePeriodicScheduledTask : BaseScheduledTask
{
    protected IContainer Container { get; private set; }
    private IDisposable? _configurationChangeDisposable;

    public BasePeriodicScheduledTask(ILogger logger, IConfiguration configuration, IContainer container, IConfigurationSection? section = null) : base(
        logger, configuration)
    {
        Section = section ?? configuration.GetSection(Name.Replace('.', ':'));
        Container = container;
        Period = TimeSpan.FromMinutes(1);
        NextSchedule = DateTimeOffset.Now;
        _configurationChangeDisposable =
            configuration.GetReloadToken().RegisterChangeCallback(OnConfigurationChange, this);
        OnConfigurationChange();
    }

    public TimeSpan PeriodOffset { get; protected internal set; } = TimeSpan.Zero;

    public IConfigurationSection Section { get; protected init; }

    protected bool DefaultEnabledState { get; set; } = true;

    protected void OnConfigurationChange()
    {
        var section = Section;
        Period = TimeSpan.FromMilliseconds(section.GetValue("Period", Period.TotalMilliseconds));
        PeriodOffset = TimeSpan.FromMilliseconds(section.GetValue("PeriodOffset", PeriodOffset.TotalMilliseconds));
        if (!section.GetValue<bool>("Enabled", !section.GetValue<bool>("Disabled", !DefaultEnabledState)))
        {
            NextSchedule = DateTimeOffset.MaxValue;
        }
    }

    protected virtual void OnConfigurationChange(object? obj)
    {
        if (!ReferenceEquals(obj, this))
        {
            return;
        }

        OnConfigurationChange();
    }

    internal protected double PrevExecutionStd;
    internal protected double RollingExecutionTimeMean = 10;

    protected bool AdaptativeReschedule { get; set; } = true;


    protected virtual void Reschedule()
    {
        if (AdaptativeReschedule)
        {
            var mean = ExecutionStatistics.ExecutionTimes.Select(span => span.TotalMilliseconds)
                .LastOrDefault(RollingExecutionTimeMean);
            if (ExecutionStatistics.ExecutionTimes.Count > 1 && ExecutionStatistics.ExecutionTimes.Count % 8 == 0)
            {
                (mean, PrevExecutionStd) = ExecutionStatistics.ExecutionTimes.Select(span => span.TotalMilliseconds)
                    .MeanStandardDeviation();
            }

            mean = RollingExecutionTimeMean = 0.9 * mean + 0.1 * RollingExecutionTimeMean;

            for (var i = 0; i < 10; i++)
            {
                var nextSchedule = Math.Ceiling((DateTimeOffset.UtcNow).ToUnixTimeMilliseconds() /
                                                Period.TotalMilliseconds) +
                                   i;
                nextSchedule *= (long)Period.TotalMilliseconds;
                nextSchedule -= Math.Min(mean + 0.5 * PrevExecutionStd, mean * 2);
                nextSchedule += PeriodOffset.TotalMilliseconds;
                var next = DateTimeOffset.FromUnixTimeMilliseconds((long)nextSchedule);
                if (next > NextSchedule && (next - NextSchedule).Duration() > Period / 2)
                {
                    NextSchedule = next;
                    break;
                }
            }
        }
        else
        {
            var ms = Math.Ceiling(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / Period.TotalMilliseconds);
            ms += 1;
            ms *= Period.TotalMilliseconds;
            ms += PeriodOffset.TotalMilliseconds;
            NextSchedule = DateTimeOffset.FromUnixTimeMilliseconds((long)ms);
        }
        

        RaiseRescheduleEvent();
    }

    public override Task PostExecute(Exception? e, CancellationToken cancellationToken)
    {
        Reschedule();

        return Task.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        _configurationChangeDisposable?.Dispose();
        _configurationChangeDisposable = null;
        base.Dispose(disposing);
    }
}