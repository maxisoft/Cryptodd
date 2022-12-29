using System.Diagnostics;
using Cryptodd.Okx.Orderbooks;
using Cryptodd.Okx.Websockets.Pool;
using Lamar;
using Maxisoft.Utils.Collections.Queues.Specialized;
using Microsoft.Extensions.Configuration;
using Polly;
using Serilog;

namespace Cryptodd.Scheduler.Tasks.Okx;

// ReSharper disable once UnusedType.Global
public class OkxOrderbookTask : BasePeriodicScheduledTask
{
    private readonly IOkxWebsocketPool _websocketPool;
    private Task _websocketBackgroundTask = Task.CompletedTask;
    private AsyncPolicy _retryPolicy;
    private readonly BoundedDeque<CancellationTokenSource> _cancellationTokenSources = new(8);

    public OkxOrderbookTask(IContainer container, ILogger logger, IConfiguration configuration,
        IOkxWebsocketPool websocketPool) : base(
        logger,
        configuration, container)
    {
        _websocketPool = websocketPool;
        _retryPolicy = Policy.NoOpAsync();
        Period = TimeSpan.FromSeconds(15);
        Section = Configuration.GetSection("Okx:Orderbook:Task");
        OnConfigurationChange();
    }

    private async ValueTask RestartBackgroundWebsocketPoolTask(CancellationToken cancellationToken)
    {
        Logger.Verbose("There's {Count} websocket in pool", _websocketPool.Count);
        if (_websocketBackgroundTask.IsCompleted)
        {
            if (_websocketBackgroundTask.IsFaulted)
            {
                Logger.Warning(_websocketBackgroundTask.Exception, "websocket pool background task faulted");
            }

            _websocketBackgroundTask.Dispose();
            _websocketBackgroundTask = _websocketPool.BackgroundLoop(cancellationToken);
        }

        try
        {
            await Task.WhenAny(
                    _websocketBackgroundTask.WaitAsync(TimeSpan.FromMilliseconds(50), cancellationToken),
                    _websocketPool.Tick(cancellationToken)
                )
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Logger.Verbose(e, "waiting for background task");
            if (e is not (OperationCanceledException or TimeoutException))
            {
                throw;
            }
        }
    }

    public override async ValueTask<bool> PreExecute(CancellationToken cancellationToken)
    {
        await RestartBackgroundWebsocketPoolTask(cancellationToken);
        return await base.PreExecute(cancellationToken);
    }

    public override async Task Execute(CancellationToken cancellationToken)
    {
        var cts = CreateCancellationTokenSource(cancellationToken);
        await using var container = Container.GetNestedContainer();
        var orderBookService = container.GetInstance<IOkxOrderbookCollector>();

        var downloadingTimeout = Period * 0.9;
        {
            var other = Period - TimeSpan.FromSeconds(1);
            if (downloadingTimeout > other)
            {
                downloadingTimeout = other;
            }
        }
        var mainTask = _retryPolicy.ExecuteAsync(_ =>
            {
                void OrderbookContinuation()
                {
                    var ctx = TaskRunningContext;
                    if (Const.IsDebug && Debugger.IsAttached)
                    {
                        return;
                    }

                    if (ctx is null)
                    {
                        return;
                    }


                    ctx.Hooks.TimeElapsed = new Lazy<TimeSpan?>(ctx.TimeElapsed);
                }


                return orderBookService.CollectOrderbooks(OrderbookContinuation, downloadingTimeout, cts.Token);
            },
            cancellationToken);

        try
        {
            await Task.WhenAny(mainTask, _websocketBackgroundTask).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Logger.Debug(e, "");
            var wasCancelled = cts.IsCancellationRequested;
            if (!wasCancelled)
            {
                cts.Cancel();
            }

            if (mainTask.IsFaulted || !mainTask.IsCompleted)
            {
                try
                {
                    await mainTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException e2)
                {
                    if (wasCancelled ||
                        TaskRunningContext?.TimeElapsed <= downloadingTimeout)
                    {
                        Logger.Warning(e2, "unable to download orderbook");
                        throw;
                    }

                    Logger.Debug(e2, "unable to download all symbols in time ?");
                    return;
                }
            }
        }

        if (!mainTask.IsCanceled || cts.IsCancellationRequested)
        {
            await mainTask.ConfigureAwait(false);
        }

        if (!mainTask.IsCompleted)
        {
            await mainTask.ConfigureAwait(false);
        }

        mainTask.Dispose();


        cts.Cancel();
    }

    protected virtual CancellationTokenSource CreateCancellationTokenSource(CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            cts.CancelAfter(Period);
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

    private void ConfigureRetryPolicy()
    {
        var maxRetry = Section.GetValue("MaxRetry", 3);
        _retryPolicy = Policy.Handle<Exception>(static e => e is not OperationCanceledException)
            .WaitAndRetryAsync(maxRetry, i => TimeSpan.FromSeconds(1 + i));
    }

    protected override void OnConfigurationChange(object? obj)
    {
        base.OnConfigurationChange(obj);
        ConfigureRetryPolicy();
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
            _websocketBackgroundTask.Dispose();
        }

        base.Dispose(disposing);
    }
}