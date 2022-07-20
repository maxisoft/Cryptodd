using System.Diagnostics;
using Cryptodd.Features;
using Cryptodd.TradeAggregates;
using Lamar;
using Maxisoft.Utils.Disposables;
using Maxisoft.Utils.Empties;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Cryptodd.Scheduler.Tasks.Trades;

public class CollectTradeTask : BasePeriodicScheduledTask, IDisposable
{
    private CancellationTokenSource _cancellationTokenSource = new();
    private IDisposable _disposable = new EmptyDisposable();
    private Task? _runningTasks;
    private readonly IContainer _container;

    public CollectTradeTask(ILogger logger, IConfiguration configuration, IContainer container) : base(logger,
        configuration, container)
    {
        _container = container;
        Period = TimeSpan.FromSeconds(3);
        AdaptativeReschedule = false;
    }

    public override async Task Execute(CancellationToken cancellationToken)
    {

        if (_runningTasks is not null)
        {
            if (!_runningTasks.IsCompleted)
            {
                try
                {
                    await _runningTasks.WaitAsync(Period, cancellationToken).ConfigureAwait(false);
                }
                catch (TimeoutException e)
                {
                    Logger.Verbose(e, "expected timeout while waiting for task to complete");
                }
                
                return;
            }
            
            if (_runningTasks.IsFaulted)
            {
                try
                {
                    await _runningTasks;
                }
                catch (Exception)
                {
                    _runningTasks.Dispose();
                    _runningTasks = null;
                    throw;
                }
            }
        }

        var container = _container.GetNestedContainer();
        var featureList = container.GetRequiredService<IFeatureList>();
        if (!featureList.HasPostgres())
        {
            await container.DisposeAsync();
            return;
        }

        _disposable.Dispose();
        _cancellationTokenSource.Cancel();
        var dm = new DisposableManager();
        dm.LinkDisposable(_cancellationTokenSource);
        _disposable = dm;
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellationTokenSource.CancelAfter(30_000);
        Debug.Assert(_runningTasks?.IsCompleted ?? true);
        var tradeCollector = container.GetRequiredService<ITradeCollector>();
        dm.LinkDisposable(container);
        _runningTasks?.Dispose();
        _runningTasks = tradeCollector.Collect(_cancellationTokenSource.Token);
        try
        {
            await _runningTasks.WaitAsync(Period, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException e)
        {
            Logger.Verbose(e, "expected timeout while waiting for task to complete");
        }
    }

    public override Task PostExecute(Exception? e, CancellationToken cancellationToken)
    {
        if (e is not (TaskCanceledException or OperationCanceledException))
        {
            return base.PostExecute(e, cancellationToken);
        }

        var nextSchedule =
            DateTimeOffset.FromUnixTimeSeconds((DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1)).ToUnixTimeSeconds());
        NextSchedule = nextSchedule;
        Reschedule();
        return Task.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        _disposable.Dispose();
        base.Dispose(disposing);
        _cancellationTokenSource.Dispose();
    }
}