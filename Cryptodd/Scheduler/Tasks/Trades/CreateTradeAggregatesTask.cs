using System.Diagnostics;
using Cryptodd.Features;
using Cryptodd.TradeAggregates;
using Lamar;
using Maxisoft.Utils.Empties;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.Scheduler.Tasks.Trades;

public class CreateTradeAggregates : BasePeriodicScheduledTask, IDisposable
{
    private readonly IFeatureList _featureList;
    private readonly IContainer _container;
    private CancellationTokenSource _cancellationTokenSource = new();
    private IDisposable _disposable = new EmptyDisposable();
    private Task? _runningTasks;

    public CreateTradeAggregates(ILogger logger, IConfiguration configuration, IContainer container,
        ITradeAggregateService tradeAggregateService, IFeatureList featureList) : base(logger,
        configuration, container)
    {
        Period = TimeSpan.FromSeconds(10);
        _container = container;
        _featureList = featureList;
        AdaptativeReschedule = false;
    }

    public override async Task Execute(CancellationToken cancellationToken)
    {
        if (!_featureList.HasPostgres())
        {
            return;
        }

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

            await _runningTasks.WaitAsync(cancellationToken).ConfigureAwait(true);
            _runningTasks.Dispose();
            _runningTasks = null;
        }

        _disposable.Dispose();
        _cancellationTokenSource.Cancel();
        _disposable = _cancellationTokenSource;
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellationTokenSource.CancelAfter(30_000);
        Debug.Assert(_runningTasks?.IsCompleted ?? true);
        _runningTasks?.Dispose();

        var container = _container.GetNestedContainer();
        _runningTasks = container.GetInstance<ITradeAggregateService>()
            .Update(cancellationToken: _cancellationTokenSource.Token).ContinueWith(
                task => { container.Dispose(); }, CancellationToken.None);
        try
        {
            await _runningTasks.WaitAsync(Period, _cancellationTokenSource.Token).ConfigureAwait(false);
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
    }
}