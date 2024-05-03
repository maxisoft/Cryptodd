using Lamar;
using Maxisoft.Utils.Collections.Queues.Specialized;
using Microsoft.Extensions.Configuration;
using Polly;
using Serilog;

namespace Cryptodd.Scheduler.Tasks;

public abstract class BasePeriodicScheduledTaskWithRetryPolicyAndCancellationTokenSources: BasePeriodicScheduledTask
{
    protected AsyncPolicy RetryPolicy { get; set; } = Policy.NoOpAsync();
    private readonly BoundedDeque<CancellationTokenSource> _cancellationTokenSources = new(8);
    protected BasePeriodicScheduledTaskWithRetryPolicyAndCancellationTokenSources(ILogger logger, IConfiguration configuration, IContainer container, IConfigurationSection? section = null) : base(logger, configuration, container, section) { }
    
    protected virtual TimeSpan CancellationTokenTimeSpan => Period;
    
    protected virtual CancellationTokenSource CreateCancellationTokenSource(CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            cts.CancelAfter(CancellationTokenTimeSpan);
            while (_cancellationTokenSources.IsFull)
            {
                if (_cancellationTokenSources.TryPopFront(out var old))
                {
                    old.Dispose();
                }
            }

            var stable = false;
            while (!stable)
            {
                stable = true;
                foreach (var source in _cancellationTokenSources)
                {
                    try
                    {
                        source.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                        _cancellationTokenSources.Remove(source);
                        stable = false;
                        break;
                    }
                }
            }


            _cancellationTokenSources.Add(cts);
            return cts;
        }
        catch
        {
            cts.Dispose();
            throw;
        }
    }
    
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var cts in _cancellationTokenSources)
            {
                cts.Dispose();
            }

            _cancellationTokenSources.Clear();
        }

        base.Dispose(disposing);
    }
}