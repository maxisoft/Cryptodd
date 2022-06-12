﻿using System.Diagnostics;
using Cryptodd.TradeAggregates;
using Lamar;
using Maxisoft.Utils.Disposables;
using Maxisoft.Utils.Empties;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.Scheduler.Tasks.Trades;

public class CollectTradeTask : BasePeriodicScheduledTask, IDisposable
{
    private readonly ITradeCollector _tradeCollector;
    private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
    private IDisposable _disposable = new EmptyDisposable();
    private Task? _runningTasks;

    public CollectTradeTask(ILogger logger, IConfiguration configuration, IContainer container,
        ITradeCollector tradeCollector) : base(logger,
        configuration, container)
    {
        Period = TimeSpan.FromSeconds(3);
        _tradeCollector = tradeCollector;
        AdaptativeReschedule = false;
    }

    public override async Task Execute(CancellationToken cancellationToken)
    {
        if (!(_runningTasks?.IsCompleted ?? true))
        {
            return;
        }

        if (_runningTasks?.IsFaulted ?? false)
        {
            await _runningTasks.ConfigureAwait(true);
        }

        _disposable.Dispose();
        _cancellationTokenSource.Cancel();
        _disposable = _cancellationTokenSource;
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellationTokenSource.CancelAfter(30_000);
        Debug.Assert(_runningTasks?.IsCompleted ?? true);
        _runningTasks = _tradeCollector.Collect(_cancellationTokenSource.Token);
        try
        {
            await (_runningTasks?.WaitAsync(Period, cancellationToken) ?? Task.CompletedTask).ConfigureAwait(false);
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